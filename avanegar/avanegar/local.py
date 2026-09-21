"""Local Whisper backend; lazily imported/loaded and never calls the cloud API."""
import gc
import io
import logging
import os
from pathlib import Path

from .cloud import encode_audio
from .models import resolve_model
from .vocabulary import normalize_vocabulary

logger = logging.getLogger("avanegar.local")


class LocalEngine:
    def __init__(self, directory: Path):
        self.directory = directory
        self.model = None
        self.name = None

    def release(self):
        self.model = None
        self.name = None
        gc.collect()

    def prepare(self, name, progress):
        if self.model is not None and self.name == name:
            progress(f"مدل {name} در حافظه آماده است؛ دانلود لازم نیست.")
            return
        path = resolve_model(name, self.directory, progress)
        self.release()
        progress(f"در حال بارگذاری مدل محلی {name} روی CPU…")
        from faster_whisper import WhisperModel
        self.model = WhisperModel(
            path, device="cpu", compute_type="int8", local_files_only=True,
            cpu_threads=max(1, min(8, (os.cpu_count() or 2) - 1)), num_workers=1,
        )
        self.name = name
        logger.info("Local model loaded: %s", name)

    def transcribe(self, clip, name, language, vocabulary, progress):
        self.prepare(name, progress)
        progress("در حال تبدیل محلی روی همین دستگاه؛ صدا به سرویس ابری ارسال نمی‌شود…")
        segments, _ = self.model.transcribe(
            io.BytesIO(encode_audio(clip)), language=None if language == "auto" else language,
            beam_size=5, vad_filter=True, vad_parameters={"min_silence_duration_ms": 500},
            condition_on_previous_text=False,
            hotwords=(normalize_vocabulary(vocabulary) or None) if language == "fa" else None,
        )
        return " ".join(segment.text.strip() for segment in segments).strip()
