"""Serialized audio worker with bounded, opt-out device recovery.

Never reinitialize PortAudio concurrently with a stream or after a native driver
has hung. Releasing the hotkey closes the capture gate immediately; automatic
recovery cannot start recording again after release. Lost speech isn't recreated.
"""
import logging
import queue
import sys
import threading
import time
import traceback

from PySide6.QtCore import QObject, Signal

from .audio import Recorder, MicrophoneUnavailable, sd

logger = logging.getLogger("avanegar.audio_service")
RECOVERABLE = (MicrophoneUnavailable, sd.PortAudioError)


class AudioService(QObject):
    started = Signal(int)
    stopped = Signal(int, object, bool)
    failed = Signal(int, str)
    recovering = Signal(int, str)
    no_audio = Signal(int, str)
    devices_changed = Signal(object)
    fallback_used = Signal(int)
    scan_finished = Signal()

    def __init__(self, recorder=None):
        super().__init__()
        self.recorder = recorder or Recorder()
        self._commands = queue.Queue()
        self._shutdown = threading.Event()
        self._stop_event = threading.Event()
        self._auto_recover = True
        self._recovery_used = False
        self._capture_began = None
        self._thread = threading.Thread(target=self._run, name="MicrophoneWorker", daemon=True)
        self._thread.start()

    def start(self, session, device, auto_recover=True):
        self._stop_event = threading.Event()
        self._commands.put(("start", session, device, (self._stop_event, auto_recover)))

    def stop(self, session, discard=False):
        self._stop_event.set()
        self._commands.put(("stop", session, None, discard))

    def refresh_devices(self, reset=True):
        self._commands.put(("scan", 0, None, reset))

    def _emit(self, signal, *args):
        if not self._shutdown.is_set():
            signal.emit(*args)

    def _rescan(self, reset=True):
        devices = self.recorder.refresh_devices() if reset else self.recorder.devices()
        logger.info("Microphone scan completed: input_count=%s", len(devices))
        self._emit(self.devices_changed, devices)
        return devices

    def _open(self, session, device, stop_event):
        try:
            self.recorder.start(device, stop_event)
        except RECOVERABLE:
            if not self._auto_recover or stop_event.is_set() or self._recovery_used:
                raise
            self._recovery_used = True
            logger.warning("Microphone open failed; one rediscovery/default-device retry: session=%s", session)
            self._emit(self.recovering, session, "میکروفون باز نشد؛ در حال شناسایی مجدد و یک تلاش با میکروفون پیش‌فرض…")
            self.recorder.close(signal_stop=False)
            devices = self._rescan()
            if stop_event.is_set() or self._shutdown.is_set():
                return
            if not devices:
                raise MicrophoneUnavailable("میکروفون ورودی پیدا نشد. اتصال و مجوز ویندوز را بررسی کنید.")
            # Retry the current system default once, not a random device.
            self.recorder.start("", stop_event)
            if device:
                self._emit(self.fallback_used, session)
        self._capture_began = time.monotonic()
        self._emit(self.started, session)

    def _finish(self, session, discard):
        clip = self.recorder.stop()
        elapsed = time.monotonic() - self._capture_began if self._capture_began is not None else 0
        logger.info("Audio stop complete: session=%s duration=%.3fs overflow=%s has_signal=%s",
                    session, clip.duration, self.recorder.had_overflow, clip.has_signal)
        if not discard and not clip.has_signal and elapsed >= 0.4:
            message = "صدایی دریافت نشد. میکروفون و کلید قطع صدای هدست را بررسی و دوباره ضبط کنید."
            if self._auto_recover and not self._recovery_used:
                self._recovery_used = True
                self._emit(self.recovering, session, "صدایی دریافت نشد؛ در حال شناسایی مجدد میکروفون‌ها…")
                devices = self._rescan()
                message = ("میکروفون‌ها دوباره شناسایی شدند. هیچ صدایی ارسال نشد؛ کلیدها را دوباره نگه دارید و صحبت کنید."
                           if devices else "پس از شناسایی مجدد هم میکروفونی پیدا نشد؛ اتصال و مجوز ویندوز را بررسی کنید.")
            self._emit(self.no_audio, session, message)
            return
        self._emit(self.stopped, session, clip, self.recorder.had_overflow)

    def _run(self):
        while True:
            action, session, device, options = self._commands.get()
            if action == "shutdown":
                try:
                    self.recorder.close()
                except Exception:
                    logger.exception("Audio cleanup failed")
                return
            if self._shutdown.is_set():
                continue
            if action == "scan":
                try:
                    self._rescan(options)
                except Exception:
                    logger.exception("Microphone rediscovery failed")
                    self._emit(self.devices_changed, [])
                finally:
                    self._emit(self.scan_finished)
                continue
            try:
                logger.info("Audio %s begin: session=%s", action, session)
                if action == "start":
                    stop_event, self._auto_recover = options
                    self._recovery_used = False
                    self._capture_began = None
                    self._open(session, device, stop_event)
                else:
                    self._finish(session, options)
            except Exception as exc:
                logger.exception("Audio %s failed: session=%s", action, session)
                self._emit(self.failed, session, f"{type(exc).__name__}: {exc}")

    def log_stack(self):
        frame = sys._current_frames().get(self._thread.ident)
        if frame is not None:
            logger.warning("Audio worker watchdog stack (no local variables):\n%s", "".join(traceback.format_stack(frame)))

    def shutdown(self):
        self._stop_event.set()
        self._shutdown.set()
        self._commands.put(("shutdown", 0, None, None))
