"""Serialized daemon audio worker. A stuck device must never block the UI.

The UI freezes capture via an Event on key release. Native open/abort/close and
buffer concatenation happen here, with session IDs protecting late callbacks.
"""
import logging
import queue
import sys
import threading
import traceback

from PySide6.QtCore import QObject, Signal

from .audio import Recorder

logger = logging.getLogger("avanegar.audio_service")


class AudioService(QObject):
    started = Signal(int)
    stopped = Signal(int, object, bool)
    failed = Signal(int, str)

    def __init__(self, recorder=None):
        super().__init__()
        self.recorder = recorder or Recorder()
        self._commands = queue.Queue()
        self._shutdown = threading.Event()
        self._stop_event = threading.Event()
        self._thread = threading.Thread(target=self._run, name="MicrophoneWorker", daemon=True)
        self._thread.start()

    def start(self, session, device):
        self._stop_event = threading.Event()
        self._commands.put(("start", session, device, self._stop_event))

    def stop(self, session):
        # Event.set never waits for the driver or the audio callback's lock.
        self._stop_event.set()
        self._commands.put(("stop", session, None, None))

    def _emit(self, signal, *args):
        if not self._shutdown.is_set():
            signal.emit(*args)

    def _run(self):
        while True:
            action, session, device, stop_event = self._commands.get()
            if action == "shutdown":
                try:
                    self.recorder.close()
                except Exception:
                    logger.exception("Audio cleanup failed")
                return
            if self._shutdown.is_set():
                continue
            try:
                logger.info("Audio %s begin: session=%s", action, session)
                if action == "start":
                    self.recorder.start(device, stop_event)
                    self._emit(self.started, session)
                else:
                    clip = self.recorder.stop()
                    logger.info("Audio stop complete: session=%s duration=%.3fs overflow=%s",
                                session, clip.duration, self.recorder.had_overflow)
                    self._emit(self.stopped, session, clip, self.recorder.had_overflow)
            except Exception as exc:
                logger.exception("Audio %s failed: session=%s", action, session)
                self._emit(self.failed, session, f"{type(exc).__name__}: {exc}")

    def log_stack(self):
        frame = sys._current_frames().get(self._thread.ident)
        if frame is not None:
            logger.warning("Audio worker watchdog stack (no local variables):\n%s", "".join(traceback.format_stack(frame)))

    def shutdown(self):
        # No join on the GUI thread: a broken native driver may never return.
        self._stop_event.set()
        self._shutdown.set()
        self._commands.put(("shutdown", 0, None, None))
