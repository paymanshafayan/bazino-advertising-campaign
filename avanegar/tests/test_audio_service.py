import os
import threading
import time
import unittest
from unittest.mock import MagicMock

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
from PySide6.QtCore import QCoreApplication, QTimer
try:
    from PySide6.QtWidgets import QApplication
except ImportError:
    QApplication = QCoreApplication
from support import ensure_audio_import
ensure_audio_import()
from avanegar.audio_service import AudioService  # noqa: E402


class AudioServiceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])

    def setUp(self):
        self.recorder = MagicMock()
        self.recorder.had_overflow = False
        self.recorder.stop.return_value = MagicMock(duration=1.0)
        self.service = AudioService(self.recorder)
        self.gate = threading.Event()
        self.addCleanup(self.cleanup)

    def cleanup(self):
        self.gate.set()
        self.service.shutdown()
        self.service._thread.join(2)
        self.app.processEvents()
        self.assertFalse(self.service._thread.is_alive())

    def wait_for(self, check):
        deadline = time.monotonic() + 2
        while not check() and time.monotonic() < deadline:
            self.app.processEvents()
            threading.Event().wait(0.005)
        self.assertTrue(check())

    def test_slow_close_does_not_block_qt_and_capture_stops_immediately(self):
        started, stopped, beats = [], [], []
        self.service.started.connect(started.append)
        self.service.stopped.connect(lambda *args: stopped.append(args))
        self.service.start(1, "")
        self.wait_for(lambda: started == [1])
        entered = threading.Event()
        def slow_close():
            entered.set()
            self.gate.wait(2)
            return MagicMock(duration=1.0)
        self.recorder.stop.side_effect = slow_close
        clock = QTimer()
        clock.setInterval(5)
        clock.timeout.connect(lambda: beats.append(True))
        clock.start()
        self.addCleanup(clock.stop)
        began = time.monotonic()
        self.service.stop(1)
        self.assertLess(time.monotonic() - began, 0.1)
        self.assertTrue(self.service._stop_event.is_set())
        self.wait_for(lambda: entered.is_set() and len(beats) >= 3)
        self.assertEqual(stopped, [])
        self.gate.set()
        self.wait_for(lambda: len(stopped) == 1)
        self.assertEqual(stopped[0][0], 1)

    def test_release_during_open_is_serialized_and_preserved(self):
        opening, states = threading.Event(), []
        def slow_open(device, stop_event):
            opening.set()
            self.gate.wait(2)
            self.assertTrue(stop_event.is_set())
        self.recorder.start.side_effect = slow_open
        self.service.started.connect(lambda session: states.append(("started", session)))
        self.service.stopped.connect(lambda session, clip, overflow: states.append(("stopped", session)))
        self.service.start(7, "")
        self.wait_for(opening.is_set)
        self.service.stop(7)
        self.gate.set()
        self.wait_for(lambda: len(states) == 2)
        self.assertEqual(states, [("started", 7), ("stopped", 7)])

    def test_native_calls_run_off_main_thread(self):
        threads, done = [], []
        self.recorder.start.side_effect = lambda *args: threads.append(threading.get_ident())
        self.recorder.stop.side_effect = lambda: (threads.append(threading.get_ident()) or MagicMock(duration=0.0))
        self.service.stopped.connect(lambda *args: done.append(True))
        self.service.start(1, "")
        self.service.stop(1)
        self.wait_for(lambda: done)
        self.assertTrue(all(thread != threading.get_ident() for thread in threads))

    def test_error_emits_session_and_exception(self):
        errors = []
        self.service.failed.connect(lambda *args: errors.append(args))
        self.recorder.start.side_effect = RuntimeError("permission denied")
        self.service.start(8, "")
        self.wait_for(lambda: errors)
        self.assertEqual(errors[0], (8, "RuntimeError: permission denied"))
