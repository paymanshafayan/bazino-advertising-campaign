"""OpenAI transcription over HTTPS. No model download or local inference.

Retry only a connection-establishment timeout or rate limiting, once. Read
errors/timeouts and 5xx are NOT replayed automatically: the server may already
have processed/billed that upload. Never log response bodies or credentials.
"""
import io
import logging
import math
import time
import wave

import numpy as np
import requests
from scipy.signal import resample_poly

from .domain import CLOUD_MODEL, MAX_RECORDING_SECONDS
from .vocabulary import normalize_vocabulary

logger = logging.getLogger("avanegar.cloud")
BASE_URL = "https://api.openai.com/v1"
TIMEOUT = (10, 60)
MAX_UPLOAD_BYTES = 24_000_000


class CloudError(Exception):
    """Safe user-facing message; contains neither server body nor secret key."""


def encode_audio(clip) -> bytes:
    if clip.sample_rate <= 0 or clip.duration > MAX_RECORDING_SECONDS + 1:
        raise CloudError("زمان یا نرخ نمونه‌برداری ضبط معتبر نیست؛ دوباره ضبط کنید.")
    samples = np.asarray(clip.samples, dtype=np.float32)
    if samples.ndim != 1 or not np.isfinite(samples).all():
        raise CloudError("دادهٔ صدا معتبر نیست؛ میکروفون را بازخوانی و دوباره ضبط کنید.")
    if clip.sample_rate != 16000:
        divisor = math.gcd(16000, clip.sample_rate)
        samples = resample_poly(samples, 16000 // divisor, clip.sample_rate // divisor)
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(16000)
        wav.writeframes((np.clip(samples, -1, 1) * 32767).astype("<i2").tobytes())
    payload = buffer.getvalue()
    if len(payload) >= MAX_UPLOAD_BYTES:
        raise CloudError("حجم ضبط زیاد است؛ یک ضبط کوتاه‌تر انجام دهید.")
    return payload


def _safe_error_code(response):
    try:
        body = response.json()
        error = body.get("error", {}) if isinstance(body, dict) else {}
        return error.get("code") if isinstance(error, dict) else None
    except (ValueError, TypeError):
        return None


def _status_error(status, code):
    if status == 401:
        return "کلید API پذیرفته نشد. در تنظیمات، کلید معتبر OpenAI را ذخیره کنید."
    if status == 403:
        return "سرویس اجازهٔ دسترسی نداد؛ دسترسی حساب، پروژه و منطقهٔ پشتیبانی‌شده را بررسی کنید."
    if status in (402, 429) and code in ("insufficient_quota", "billing_hard_limit_reached"):
        return "اعتبار یا سهمیهٔ API کافی نیست. اعتبار API مستقل از اشتراک ChatGPT است."
    if status == 429:
        return "محدودیت تعداد درخواست‌ها برقرار است؛ کمی بعد دوباره ضبط کنید."
    if status == 413:
        return "سرویس حجم ضبط را نپذیرفت؛ کوتاه‌تر ضبط کنید."
    if status >= 500:
        return "سرویس ابری موقتاً خطا دارد. برای جلوگیری از ارسال/هزینهٔ تکراری، این ضبط خودکار دوباره ارسال نشد."
    return f"سرویس درخواست را نپذیرفت (HTTP {status}). تنظیمات و دسترسی API را بررسی کنید."


class CloudClient:
    def __init__(self, session=None, sleep=time.sleep):
        self.session = session or requests.Session()
        self.sleep = sleep

    def close(self):
        self.session.close()

    def _request(self, method, path, key, progress, **kwargs):
        for attempt in range(2):
            try:
                response = self.session.request(
                    method, BASE_URL + path, headers={"Authorization": "Bearer " + key},
                    timeout=TIMEOUT, allow_redirects=False, **kwargs,
                )
            except requests.ConnectTimeout:
                if attempt == 0:
                    logger.warning("Cloud connection establishment timeout; one retry")
                    progress("برقراری اتصال طول کشید؛ یک بار دیگر تلاش می‌کنم…")
                    self.sleep(1)
                    continue
                raise CloudError("اتصال به سرویس برقرار نشد. اینترنت و دسترسی به api.openai.com را بررسی کنید.") from None
            except requests.exceptions.SSLError:
                raise CloudError("ارتباط امن TLS برقرار نشد. ساعت ویندوز و تنظیمات شبکه را بررسی کنید؛ بررسی گواهی غیرفعال نمی‌شود.") from None
            except (requests.ReadTimeout, requests.ConnectionError):
                raise CloudError("ارتباط قطع شد یا پاسخ دیر رسید. ممکن است درخواست پردازش شده باشد؛ برای جلوگیری از هزینهٔ تکراری خودکار ارسال مجدد نشد.") from None
            except requests.RequestException:
                raise CloudError("خطای ارتباط با سرویس. تنظیمات شبکه را بررسی کنید؛ جزئیات محرمانه در Log ثبت نمی‌شود.") from None
            status = response.status_code
            logger.info("Cloud HTTP status=%s attempt=%s", status, attempt + 1)
            if 200 <= status < 300:
                return response
            code = _safe_error_code(response)
            if status == 429 and code not in ("insufficient_quota", "billing_hard_limit_reached") and attempt == 0:
                try:
                    delay = float(response.headers.get("Retry-After", "1"))
                except ValueError:
                    delay = 1.0
                response.close()
                if not math.isfinite(delay) or delay > 5:
                    raise CloudError(_status_error(status, code))
                progress("سرویس شلوغ است؛ یک تلاش مجدد کوتاه انجام می‌دهم…")
                self.sleep(max(1.0, delay))
                continue
            response.close()
            raise CloudError(_status_error(status, code))
        raise CloudError("اتصال به سرویس برقرار نشد.")

    def check(self, key, progress):
        response = self._request("GET", "/models/" + CLOUD_MODEL, key, progress)
        try:
            payload = response.json()
            if not isinstance(payload, dict) or payload.get("id") != CLOUD_MODEL:
                raise CloudError("پاسخ آزمون اتصال معتبر نبود؛ اتصال تأیید نشد.")
        except ValueError:
            raise CloudError("پاسخ آزمون اتصال قابل خواندن نیست.") from None
        finally:
            response.close()

    def transcribe(self, clip, key, language, vocabulary, progress):
        audio = encode_audio(clip)
        data = {"model": CLOUD_MODEL, "response_format": "json"}
        if language != "auto":
            data["language"] = language
        if language == "fa" and vocabulary:
            data["prompt"] = normalize_vocabulary(vocabulary)
        progress("در حال ارسال امن صدا و تبدیل در سرویس OpenAI…")
        logger.info("Cloud upload: bytes=%s language=%s vocabulary_enabled=%s", len(audio), language, "prompt" in data)
        # Immutable bytes are safe to reconstruct into multipart form on one retry.
        response = self._request("POST", "/audio/transcriptions", key, progress,
                                 data=data, files={"file": ("dictation.wav", audio, "audio/wav")})
        try:
            payload = response.json()
            if not isinstance(payload, dict) or not isinstance(payload.get("text"), str):
                raise CloudError("پاسخ سرویس قالب معتبر متن ندارد؛ ارسال خودکار تکرار نشد.")
            return payload["text"].strip()
        except ValueError:
            raise CloudError("پاسخ سرویس قابل خواندن نیست؛ ارسال خودکار تکرار نشد.") from None
        finally:
            response.close()
