"""Cloud requests on a Qt worker, never on the UI thread."""
import logging
import time

from PySide6.QtCore import QObject, Signal, Slot

from .cloud import CloudClient, CloudError
from .domain import CLOUD_MODEL

logger = logging.getLogger("avanegar.transcriber")


class Transcriber(QObject):
    progress = Signal(str)
    completed = Signal(str, bool)  # text, connection-check-only
    failed = Signal(str)

    def __init__(self, key_store, client_factory=CloudClient):
        super().__init__()
        self.key_store = key_store
        self.client_factory = client_factory

    @Slot(object, str, str, bool)
    def run(self, clip, language, vocabulary, consent):
        began = time.monotonic()
        logger.info("Cloud request: model=%s language=%s check_only=%s", CLOUD_MODEL, language, clip is None)
        client = None
        try:
            if not consent:
                raise CloudError("ابتدا در تنظیمات، ارسال صدا به OpenAI را تأیید کنید.")
            key = self.key_store.get()
            if not key:
                raise CloudError("کلید API وارد نشده است. در تنظیمات کلید OpenAI را ذخیره کنید.")
            if clip is not None and (clip.duration < 0.35 or not clip.has_signal):
                self.completed.emit("", False)
                return
            client = self.client_factory()
            if clip is None:
                self.progress.emit("در حال بررسی کلید و اتصال به سرویس؛ صدایی ارسال نمی‌شود…")
                client.check(key, self.progress.emit)
                self.completed.emit("", True)
                return
            text = client.transcribe(clip, key, language, vocabulary, self.progress.emit)
            logger.info("Cloud transcription completed: elapsed=%.2fs has_text=%s", time.monotonic() - began, bool(text))
            self.completed.emit(text, False)
        except CloudError as exc:
            logger.warning("Cloud operation failed: %s", exc)
            self.failed.emit(str(exc))
        except Exception as exc:
            # Never stringify/log third-party HTTP exceptions, headers or response bodies.
            logger.error("Cloud/key operation failed: exception_type=%s", type(exc).__name__)
            self.failed.emit("عملیات ابری یا خواندن کلید انجام نشد. کلید را دوباره در تنظیمات ذخیره و اتصال را بررسی کنید.")
        finally:
            if client is not None:
                client.close()
