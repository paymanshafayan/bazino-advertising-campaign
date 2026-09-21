import ctypes
import sys
import unittest
from unittest.mock import MagicMock, patch

from avanegar.windows import DesktopBridge


@unittest.skipUnless(sys.platform == "win32", "Real Win32 ABI only available on Windows")
class Win32Tests(unittest.TestCase):
    def test_input_struct_has_correct_alignment(self):
        from avanegar.windows import INPUT
        self.assertEqual(ctypes.sizeof(INPUT), 40 if ctypes.sizeof(ctypes.c_void_p) == 8 else 28)

    def test_hotkey_conflict_restores_old_binding(self):
        with patch("avanegar.windows.user32") as api:
            api.RegisterHotKey.side_effect = [True, False, True]
            bridge = DesktopBridge(MagicMock(), lambda: None)
            self.assertTrue(bridge.register("ctrl_shift_space"))
            self.assertFalse(bridge.register("ctrl_alt_f9"))
            self.assertEqual(bridge.hotkey, "ctrl_shift_space")
            self.assertTrue(bridge.registered)

    def test_partial_input_releases_keys(self):
        with patch("avanegar.windows.user32") as api:
            api.SendInput.side_effect = [1, 2]
            self.assertFalse(DesktopBridge.paste())
            self.assertEqual(api.SendInput.call_args_list[-1].args[0], 2)

    def test_modifier_only_chord_installs_and_removes_hook(self):
        with patch("avanegar.windows.user32") as api, patch("avanegar.windows.kernel32"):
            api.GetAsyncKeyState.return_value = 0
            api.SetWindowsHookExW.return_value = 123
            bridge = DesktopBridge(MagicMock(), lambda: None)
            self.assertTrue(bridge.register("ctrl_win"))
            self.assertIsNotNone(bridge._hook_callback)
            api.RegisterHotKey.assert_not_called()
            bridge.close()
            api.UnhookWindowsHookEx.assert_called_once_with(123)
            self.assertIsNone(bridge._hook_callback)

    def test_failed_hook_registration_restores_previous_shortcut(self):
        with patch("avanegar.windows.user32") as api, patch("avanegar.windows.kernel32"):
            api.GetAsyncKeyState.return_value = 0
            api.RegisterHotKey.return_value = True
            api.SetWindowsHookExW.return_value = None
            bridge = DesktopBridge(MagicMock(), lambda: None)
            self.assertTrue(bridge.register("ctrl_shift_space"))
            self.assertFalse(bridge.register("ctrl_win"))
            self.assertEqual(bridge.hotkey, "ctrl_shift_space")
            self.assertTrue(bridge.registered)

    def test_old_queued_gesture_does_not_fire_after_unregister(self):
        callback = MagicMock()
        bridge = DesktopBridge(MagicMock(), callback)
        bridge.registered = True
        bridge.hotkey = "ctrl_win"
        previous_generation = bridge._generation
        with patch("avanegar.windows.user32"):
            bridge.unregister()
        bridge._dispatch_chord(previous_generation)
        callback.assert_not_called()
