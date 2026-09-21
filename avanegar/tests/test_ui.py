import os
from pathlib import Path
import sys
import tempfile
import time
import unittest
from unittest.mock import patch, MagicMock

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
from support import ensure_audio_import
ensure_audio_import()
try:
    from PySide6.QtWidgets import QApplication
    from avanegar.ui import MainWindow
except ImportError as exc:
    if sys.platform == "win32":
        raise
    raise unittest.SkipTest(f"Native Qt GUI libraries unavailable on this runner: {exc}")


class UiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])
        cls.app.setQuitOnLastWindowClosed(False)

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        with patch("avanegar.ui.QSystemTrayIcon.isSystemTrayAvailable", return_value=False), \
             patch("avanegar.ui.DesktopBridge.register", return_value=True), \
             patch("avanegar.ui.Recorder.devices", return_value=[]):
            self.window = MainWindow(Path(self.directory.name))
        self.addCleanup(self.cleanup_window)
        self.window.show()
        self.app.processEvents()

    def cleanup_window(self):
        self.window._quit_now()
        self.window.hide()
        self.window.deleteLater()
        self.app.processEvents()

    def test_initial_state_and_navigation(self):
        self.assertEqual(self.window.state, "idle")
        self.assertFalse(self.window.copy_button.isEnabled())
        self.window._navigate(1)
        self.assertEqual(self.window.pages.currentIndex(), 1)
        self.window._navigate(2)
        self.assertTrue(self.window.nav[2].isChecked())

    def test_record_and_cancel_are_asynchronous(self):
        with patch.object(self.window.audio, "start") as start, patch.object(self.window.audio, "stop") as stop:
            self.window.toggle_recording(False)
            self.assertEqual(self.window.state, "starting")
            self.window._audio_started(self.window.audio_session)
            self.assertEqual(self.window.state, "recording")
            self.assertFalse(self.window.save_button.isEnabled())
            self.window.cancel()
            self.assertEqual(self.window.state, "stopping")
            self.window._audio_stopped(self.window.audio_session, MagicMock(duration=1.0), False)
            self.assertEqual(self.window.state, "idle")
            start.assert_called_once()
            stop.assert_called_once()

    def test_microphone_failure_returns_to_idle(self):
        with patch.object(self.window.audio, "start"):
            self.window.toggle_recording()
            self.window._audio_failed(self.window.audio_session, "denied")
        self.assertEqual(self.window.state, "idle")
        self.assertIn("denied", self.window.notice.text())

    def test_release_during_start_cannot_get_stuck_recording(self):
        self.window.transcribe.disconnect()
        requests = []
        self.window.transcribe.connect(lambda *args: requests.append(args))
        with patch.object(self.window.audio, "start"), patch.object(self.window.audio, "stop") as stop:
            self.window._hotkey_press()
            self.assertEqual(self.window.state, "starting")
            self.window._hotkey_release()
            self.assertEqual(self.window.state, "stopping")
            self.window._audio_started(self.window.audio_session)
            self.assertEqual(self.window.state, "stopping")
            self.window._hotkey_release()
            stop.assert_called_once()
            self.window._audio_stopped(self.window.audio_session, MagicMock(duration=0.1), False)
            self.assertEqual(len(requests), 1)
            self.assertEqual(self.window.state, "processing")
        self.window._set_state("idle")

    def test_hotkey_release_does_not_stop_button_owned_recording(self):
        with patch.object(self.window.audio, "start"), patch.object(self.window.audio, "stop") as stop:
            self.window.toggle_recording(False)
            self.window._audio_started(self.window.audio_session)
            self.window._hotkey_release()
            stop.assert_not_called()
        self.window._set_state("idle")

    def test_driver_timeout_keeps_logs_accessible_and_discards_late_audio(self):
        with patch.object(self.window.audio, "start"), patch.object(self.window.audio, "stop"), patch.object(self.window.audio, "log_stack") as stack:
            self.window._hotkey_press()
            self.window._hotkey_release()
            self.window.phase_started = time.monotonic() - 12
            self.window._tick()
            self.assertEqual(self.window.state, "audio_stalled")
            stack.assert_called_once()
            self.window._navigate(3)
            self.window.copy_log()
            self.assertIn("Microphone driver timeout", self.app.clipboard().text())
            self.window._audio_stopped(self.window.audio_session, MagicMock(duration=1.0), False)
            self.assertEqual(self.window.state, "idle")
            self.assertEqual(self.window.result.toPlainText(), "")

    def test_log_copy_excludes_transcript(self):
        self.window._completed("SENSITIVE_TRANSCRIPT", False)
        self.window.copy_log()
        text = self.app.clipboard().text()
        self.assertIn("Transcription completed", text)
        self.assertNotIn("SENSITIVE_TRANSCRIPT", text)

    def test_log_page_and_microphone_help_exist(self):
        from avanegar.help_text import MICROPHONE_HELP
        self.assertEqual(self.window.pages.count(), 4)
        self.assertIn("Let desktop apps access your microphone", MICROPHONE_HELP)
        self.assertIn("Allow desktop apps to access your microphone", MICROPHONE_HELP)

    def test_completed_text_history_is_bounded_and_editable(self):
        for i in range(12):
            self.window._completed(f"متن {i}", False)
        self.assertEqual(self.window.history.count(), 10)
        self.assertEqual(self.window.result.toPlainText(), "متن 11")
        self.window.result.setPlainText("ویرایش‌شده")
        self.window.copy_text()
        self.assertEqual(self.app.clipboard().text(), "ویرایش‌شده")
        self.window.clear_texts()
        self.assertEqual(self.window.history.count(), 0)
        self.assertEqual(self.window.result.toPlainText(), "")

    def test_empty_result_does_not_repeat_previous_text(self):
        self.window.result.setPlainText("old")
        self.app.clipboard().setText("keep")
        self.window._completed("", False)
        self.assertEqual(self.window.result.toPlainText(), "old")
        self.assertEqual(self.app.clipboard().text(), "keep")
        self.assertFalse(self.window.paste_timer.isActive())

    def test_focus_change_prevents_clipboard_write_and_paste(self):
        self.window.target = 100
        self.app.clipboard().setText("keep")
        with patch.object(self.window.bridge, "foreground", return_value=200), patch.object(self.window.bridge, "paste") as paste:
            self.window._completed("private text", False)
            paste.assert_not_called()
        self.assertEqual(self.app.clipboard().text(), "keep")

    def test_paste_in_same_window_once(self):
        self.window.target = 100
        with patch.object(self.window.bridge, "foreground", return_value=100), \
             patch.object(self.window.bridge, "is_own_window", return_value=False), \
             patch.object(self.window.bridge, "modifiers_down", return_value=False), \
             patch.object(self.window.bridge, "paste", return_value=True) as paste:
            self.window._completed("سلام", False)
            self.assertEqual(self.window.state, "pasting")
            self.window._try_paste()
            paste.assert_called_once()
        self.assertEqual(self.app.clipboard().text(), "سلام")
        self.assertEqual(self.window.state, "idle")
        self.assertFalse(self.window.paste_timer.isActive())

    def test_auto_paste_disabled_preserves_clipboard(self):
        self.window.target = 100
        self.window.settings.auto_paste = False
        self.app.clipboard().setText("keep")
        with patch.object(self.window.bridge, "foreground", return_value=100), patch.object(self.window.bridge, "is_own_window", return_value=False):
            self.window._completed("private", False)
        self.assertEqual(self.window.state, "idle")
        self.assertEqual(self.app.clipboard().text(), "keep")

    def test_held_modifiers_timeout_without_pasting(self):
        self.window.target = 100
        with patch.object(self.window.bridge, "foreground", return_value=100), \
             patch.object(self.window.bridge, "is_own_window", return_value=False), \
             patch.object(self.window.bridge, "modifiers_down", return_value=True), \
             patch.object(self.window.bridge, "paste") as paste:
            self.window._completed("text", False)
            self.window.paste_deadline = time.monotonic() - 1
            self.window._try_paste()
            paste.assert_not_called()
        self.assertEqual(self.window.state, "idle")

    def test_busy_ignores_hotkey(self):
        self.window._set_state("processing")
        with patch.object(self.window.audio, "start") as start:
            self.window.toggle_recording(True)
            start.assert_not_called()
        self.window._set_state("idle")

    def test_persian_preset_is_explicit_and_does_not_start_download(self):
        self.window.settings.model = "small"
        calls = []
        self.window.transcribe.disconnect()
        self.window.transcribe.connect(lambda *args: calls.append(args))
        self.window.select_persian_preset()
        self.assertEqual(self.window.model_combo.currentData(), "medium")
        self.assertEqual(self.window.language_combo.currentData(), "fa")
        self.assertEqual(self.window.settings.model, "small")
        self.assertEqual(self.window.state, "idle")
        self.assertEqual(calls, [])

    def test_vocabulary_is_saved_and_inference_uses_it(self):
        self.window.vocabulary_edit.setText("واژه ویژه, آوانگار")
        with patch.object(self.window.bridge, "register", return_value=True):
            self.assertTrue(self.window.save_settings())
        self.assertEqual(self.window.settings.persian_vocabulary, "واژه ویژه، آوانگار")
        calls = []
        self.window.transcribe.disconnect()
        self.window.transcribe.connect(lambda *args: calls.append(args))
        self.window._set_state("stopping")
        self.window._audio_stopped(self.window.audio_session, MagicMock(duration=1.0), False)
        self.assertEqual(calls[0][3], "واژه ویژه، آوانگار")
        self.window.copy_log()
        self.assertNotIn("واژه ویژه", self.app.clipboard().text())
        self.window._set_state("idle")
