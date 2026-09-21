"""One persistent worker: model loading and decoding never block the UI."""
import gc
import io
import logging
import time
import os
from pathlib import Path
import wave

import numpy as np
from PySide6.QtCore import QObject, Signal, Slot

from .audio import AudioClip
from .models import resolve_model
from .vocabulary import DEFAULT_VOCABULARY, normalize_vocabulary

logger = logging.getLogger("avanegar.transcriber")


class Transcriber(QObject):
    progress = Signal(str)
    completed = Signal(str, bool)  # text, prepare-only
    failed = Signal(str)

    def __init__(self, model_directory: Path):
        super().__init__()
        self.model_directory = model_directory
        self._model = None
        self._model_name = None

    @Slot(object, str, str, str)
    def run(self, clip: AudioClip | None, model_name: str, language: str, persian_vocabulary: str = DEFAULT_VOCABULARY):
        began = time.monotonic()
        logger.info("Transcription request: model=%s language=%s prepare_only=%s duration=%.3f", model_name, language, clip is None, clip.duration if clip is not None else 0)
        try:
            if clip is not None and clip.duration < 0.35:
                self.completed.emit("", False)
                return
            if clip is not None:
                # Diagnostics only: don't amplify noise or rewrite what the user said.
                rms = float(np.sqrt(np.mean(np.square(clip.samples), dtype=np.float64)))
                peak = float(np.max(np.abs(clip.samples)))
                clipped = float(np.mean(np.abs(clip.samples) >= 0.999))
                logger.info("Audio levels: rms_dbfs=%.1f peak_dbfs=%.1f clipped_percent=%.3f sample_rate=%s",
                            20 * np.log10(max(rms, 1e-6)), 20 * np.log10(max(peak, 1e-6)), clipped * 100, clip.sample_rate)
                if rms < 0.005:
                    logger.warning("Audio level is low; check input gain/microphone distance (not a speech-quality score)")
                if clipped > 0.01:
                    logger.warning("Possible microphone clipping; check input gain")
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
                logger.info("Model ready: %s elapsed=%.2fs", model_name, time.monotonic() - began)
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
            vocabulary = normalize_vocabulary(persian_vocabulary) if language == "fa" else ""
            logger.info("Decoding options: language=%s persian_vocabulary_enabled=%s", language, bool(vocabulary))
            segments, _ = self._model.transcribe(
                buffer, language=None if language == "auto" else language,
                beam_size=5, vad_filter=True,
                vad_parameters={"min_silence_duration_ms": 500},
                condition_on_previous_text=False,
                hotwords=vocabulary or None,
            )
            text = " ".join(segment.text.strip() for segment in segments).strip()
            logger.info("Decoding complete: elapsed=%.2fs has_text=%s", time.monotonic() - began, bool(text))
            self.completed.emit(text, False)
        except Exception as exc:
            logger.exception("Transcription failed")
            self.failed.emit(
                "تبدیل انجام نشد. برای اولین دریافت مدل، اینترنت و فضای دیسک را بررسی کنید. "
                "اگر حافظه کافی نیست، مدل Base را انتخاب کنید.\n"
                f"{type(exc).__name__}: {exc}"
            )
