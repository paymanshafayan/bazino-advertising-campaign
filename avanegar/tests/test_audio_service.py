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

    def test_open_failure_redetects_and_retries_default_once(self):
        from avanegar.audio import MicrophoneUnavailable
        started, fallback = [], []
        self.recorder.start.side_effect = [MicrophoneUnavailable("missing"), None]
        self.recorder.refresh_devices.return_value = [(1, "default input")]
        self.service.started.connect(started.append)
        self.service.fallback_used.connect(fallback.append)
        self.service.start(3, "missing input", True)
        self.wait_for(lambda: started)
        self.assertEqual(self.recorder.start.call_count, 2)
        self.assertEqual(self.recorder.start.call_args.args[0], "")
        self.assertFalse(self.service._stop_event.is_set())
        self.assertEqual(fallback, [3])
        self.recorder.refresh_devices.assert_called_once()
        self.recorder.close.assert_called_with(signal_stop=False)

    def test_recovery_failure_is_not_an_infinite_loop(self):
        from avanegar.audio import MicrophoneUnavailable
        errors = []
        self.recorder.start.side_effect = MicrophoneUnavailable("not available")
        self.recorder.refresh_devices.return_value = [(1, "default input")]
        self.service.failed.connect(lambda *args: errors.append(args))
        self.service.start(1, "missing", True)
        self.wait_for(lambda: errors)
        self.assertEqual(self.recorder.start.call_count, 2)
        self.recorder.refresh_devices.assert_called_once()

    def test_opt_out_prevents_recovery(self):
        from avanegar.audio import MicrophoneUnavailable
        errors = []
        self.recorder.start.side_effect = MicrophoneUnavailable("not available")
        self.service.failed.connect(lambda *args: errors.append(args))
        self.service.start(1, "missing", False)
        self.wait_for(lambda: errors)
        self.recorder.start.assert_called_once()
        self.recorder.refresh_devices.assert_not_called()

    def test_release_during_recovery_never_restarts_recording(self):
        from avanegar.audio import MicrophoneUnavailable
        scanning, stopped = threading.Event(), []
        self.recorder.start.side_effect = MicrophoneUnavailable("missing")
        def rescan():
            scanning.set()
            self.gate.wait(2)
            return [(1, "default input")]
        self.recorder.refresh_devices.side_effect = rescan
        self.service.stopped.connect(lambda *args: stopped.append(args))
        self.service.start(1, "missing", True)
        self.wait_for(scanning.is_set)
        self.service.stop(1)
        self.gate.set()
        self.wait_for(lambda: stopped)
        self.recorder.start.assert_called_once()

    def test_no_signal_redetects_without_uploading_or_reopening(self):
        started, empty, stopped = [], [], []
        self.recorder.stop.return_value = MagicMock(duration=1.0, has_signal=False)
        self.recorder.refresh_devices.return_value = [(0, "input")]
        self.service.started.connect(started.append)
        self.service.no_audio.connect(lambda *args: empty.append(args))
        self.service.stopped.connect(lambda *args: stopped.append(args))
        self.service.start(1, "", True)
        self.wait_for(lambda: started)
        self.service._capture_began = time.monotonic() - 1
        self.service.stop(1)
        self.wait_for(lambda: empty)
        self.recorder.refresh_devices.assert_called_once()
        self.recorder.start.assert_called_once()
        self.assertEqual(stopped, [])

    def test_cancel_never_triggers_device_recovery(self):
        started, stopped = [], []
        self.recorder.stop.return_value = MagicMock(duration=1.0, has_signal=False)
        self.service.started.connect(started.append)
        self.service.stopped.connect(lambda *args: stopped.append(args))
        self.service.start(1, "", True)
        self.wait_for(lambda: started)
        self.service._capture_began = time.monotonic() - 1
        self.service.stop(1, discard=True)
        self.wait_for(lambda: stopped)
        self.recorder.refresh_devices.assert_not_called()
