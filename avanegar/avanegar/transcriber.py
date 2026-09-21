"""Explicit local/cloud routing on one worker. No automatic provider fallback."""
import logging
import time
from pathlib import Path

from PySide6.QtCore import QObject, Signal, Slot

from .cloud import CloudClient, CloudError
from .domain import CLOUD_MODEL, PROCESSING_MODES, LOCAL_MODELS, data_directory
from .local import LocalEngine
from .models import find_cached_model

logger = logging.getLogger("avanegar.transcriber")


class Transcriber(QObject):
    progress = Signal(str)
    completed = Signal(str, bool)  # text, connection-check-only
    failed = Signal(str)
    cache_status = Signal(str, str)  # model, cached path (empty = not complete)

    def __init__(self, key_store, client_factory=CloudClient, model_directory=None):
        super().__init__()
        self.key_store = key_store
        self.client_factory = client_factory
        self.model_directory = Path(model_directory) if model_directory is not None else None
        self.local_engine = None

    def _models_path(self):
        return self.model_directory if self.model_directory is not None else data_directory() / "models"

    @Slot(str)
    def inspect_cache(self, model):
        try:
            cached = find_cached_model(model, self._models_path())
            self.cache_status.emit(model, str(cached) if cached is not None else "")
        except Exception as exc:
            logger.warning("Cache inspection failed: exception_type=%s", type(exc).__name__)
            self.cache_status.emit(model, "")

    @Slot(object, str, str, bool, str, str)
    def run(self, clip, language, vocabulary, consent, mode="cloud", local_model="small"):
        began = time.monotonic()
        logger.info("Transcription request: mode=%s model=%s language=%s prepare_only=%s", mode, local_model if mode == "local" else CLOUD_MODEL, language, clip is None)
        client = None
        try:
            if mode not in PROCESSING_MODES or local_model not in LOCAL_MODELS:
                raise ValueError("Invalid engine selection")
            if mode == "local":
                # No credential access or CloudClient creation in this branch.
                if clip is not None and (clip.duration < 0.35 or not clip.has_signal):
                    self.completed.emit("", False)
                    return
                if self.local_engine is None:
                    self.local_engine = LocalEngine(self._models_path())
                if clip is None:
                    self.local_engine.prepare(local_model, self.progress.emit)
                    self.completed.emit("", True)
                else:
                    text = self.local_engine.transcribe(clip, local_model, language, vocabulary, self.progress.emit)
                    logger.info("Local transcription completed: elapsed=%.2fs has_text=%s", time.monotonic() - began, bool(text))
                    self.completed.emit(text, False)
                self.inspect_cache(local_model)
                return
            if self.local_engine is not None:
                self.local_engine.release()
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
            logger.error("Transcription failed: mode=%s exception_type=%s", mode, type(exc).__name__)
            if mode == "local":
                self.failed.emit("مدل محلی بارگذاری یا اجرا نشد. فایل‌های مدل، حافظه و فضای دیسک را بررسی کنید؛ دریافت اولیه/تکمیل مدل به اینترنت نیاز دارد. هیچ صدایی به سرویس ابری ارسال نشد.")
            else:
                self.failed.emit("عملیات ابری یا خواندن کلید انجام نشد. کلید را دوباره در تنظیمات ذخیره و اتصال را بررسی کنید.")
        finally:
            if client is not None:
                client.close()
