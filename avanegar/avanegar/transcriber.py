"""One persistent worker: model loading and decoding never block the UI."""
import gc
import io
import os
from pathlib import Path
import wave

import numpy as np
from PySide6.QtCore import QObject, Signal, Slot

from .audio import AudioClip
from .models import resolve_model


class Transcriber(QObject):
    progress = Signal(str)
    completed = Signal(str, bool)  # text, prepare-only
    failed = Signal(str)

    def __init__(self, model_directory: Path):
        super().__init__()
        self.model_directory = model_directory
        self._model = None
        self._model_name = None

    @Slot(object, str, str)
    def run(self, clip: AudioClip | None, model_name: str, language: str):
        try:
            if clip is not None and clip.duration < 0.35:
                self.completed.emit("", False)
                return
            if self._model_name != model_name:
                self.progress.emit("در حال آماده‌سازی مدل؛ بار اول دریافت از اینترنت ممکن است چند دقیقه طول بکشد…")
                from faster_whisper import WhisperModel
                self._model = None
                self._model_name = None
                gc.collect()
                self._model = WhisperModel(
                    resolve_model(model_name, self.model_directory), device="cpu", compute_type="int8",
                    download_root=str(self.model_directory),
                    cpu_threads=max(1, min(8, (os.cpu_count() or 2) - 1)),
                    num_workers=1,
                )
                self._model_name = model_name
            if clip is None:
                self.completed.emit("", True)
                return
            self.progress.emit("در حال تبدیل صدا به متن روی همین دستگاه…")
            # A WAV in RAM lets PyAV resample microphones that don't support 16 kHz.
            buffer = io.BytesIO()
            with wave.open(buffer, "wb") as wav:
                wav.setnchannels(1)
                wav.setsampwidth(2)
                wav.setframerate(clip.sample_rate)
                wav.writeframes((np.clip(clip.samples, -1, 1) * 32767).astype("<i2").tobytes())
            buffer.seek(0)
            segments, _ = self._model.transcribe(
                buffer, language=None if language == "auto" else language,
                beam_size=5, vad_filter=True,
                vad_parameters={"min_silence_duration_ms": 500},
                condition_on_previous_text=False,
            )
            text = " ".join(segment.text.strip() for segment in segments).strip()
            self.completed.emit(text, False)
        except Exception as exc:
            self.failed.emit(
                "تبدیل انجام نشد. برای اولین دریافت مدل، اینترنت و فضای دیسک را بررسی کنید. "
                "اگر حافظه کافی نیست، مدل Base را انتخاب کنید.\n"
                f"{type(exc).__name__}: {exc}"
            )
