"""Bounded in-memory microphone recording; no audio files are written."""
from dataclasses import dataclass
import threading

import numpy as np
import sounddevice as sd

from .domain import MAX_RECORDING_SECONDS


@dataclass
class AudioClip:
    samples: np.ndarray
    sample_rate: int

    @property
    def duration(self) -> float:
        return len(self.samples) / self.sample_rate


class Recorder:
    def __init__(self):
        self._lock = threading.Lock()
        self._chunks = []
        self._stream = None
        self._frames = 0
        self.sample_rate = 16000
        self.level = 0.0
        self.had_overflow = False
        self.limit_reached = False

    @staticmethod
    def devices():
        # Names (rather than changing indices) are persisted across launches.
        return [(i, d["name"]) for i, d in enumerate(sd.query_devices()) if d["max_input_channels"] > 0]

    def start(self, device_name: str = "") -> None:
        if self._stream is not None:
            raise RuntimeError("ضبط دیگری در حال اجراست.")
        device = None
        if device_name:
            device = next((i for i, name in self.devices() if name == device_name), None)
            if device is None:
                raise RuntimeError("میکروفون انتخاب‌شده متصل نیست. در تنظیمات، میکروفون دیگری انتخاب کنید.")
        try:
            sd.check_input_settings(device=device, channels=1, dtype="float32", samplerate=16000)
            self.sample_rate = 16000
        except sd.PortAudioError:
            self.sample_rate = int(sd.query_devices(device, "input")["default_samplerate"])
        with self._lock:
            self._chunks = []
            self._frames = 0
            self.level = 0.0
            self.had_overflow = False
            self.limit_reached = False
        stream = sd.InputStream(
            device=device, samplerate=self.sample_rate, channels=1,
            dtype="float32", callback=self._callback,
        )
        try:
            stream.start()
        except Exception:
            stream.close()
            raise
        self._stream = stream

    def _callback(self, indata, frames, time_info, status):
        if status:
            self.had_overflow = True
        with self._lock:
            remaining = self.sample_rate * MAX_RECORDING_SECONDS - self._frames
            if remaining <= 0:
                self.limit_reached = True
                raise sd.CallbackStop()
            chunk = indata[:remaining, 0].copy()
            self._chunks.append(chunk)
            self._frames += len(chunk)
            self.level = min(1.0, float(np.sqrt(np.mean(chunk ** 2))) * 6.0)
            if self._frames >= self.sample_rate * MAX_RECORDING_SECONDS:
                self.limit_reached = True
                raise sd.CallbackStop()

    def stop(self) -> AudioClip:
        self.close()
        with self._lock:
            samples = np.concatenate(self._chunks) if self._chunks else np.empty(0, dtype=np.float32)
            self._chunks = []
            self.level = 0.0
        return AudioClip(samples, self.sample_rate)

    def close(self):
        stream, self._stream = self._stream, None
        if stream is not None:
            try:
                stream.stop()
            finally:
                stream.close()
