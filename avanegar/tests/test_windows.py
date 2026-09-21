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

    def test_partial_input_releases_keys(self):
        with patch("avanegar.windows.user32") as api:
            api.SendInput.side_effect = [1, 2]
            self.assertFalse(DesktopBridge.paste())
            self.assertEqual(api.SendInput.call_args_list[-1].args[0], 2)

    def test_hold_shortcut_installs_and_removes_hook(self):
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
            api.SetWindowsHookExW.side_effect = [123, None, 124]
            bridge = DesktopBridge(MagicMock(), lambda: None)
            self.assertTrue(bridge.register("ctrl_shift_space"))
            self.assertFalse(bridge.register("ctrl_win"))
            self.assertEqual(bridge.hotkey, "ctrl_shift_space")
            self.assertTrue(bridge.registered)
            bridge.close()

    def test_queued_events_are_discarded_after_unregister(self):
        callback = MagicMock()
        bridge = DesktopBridge(MagicMock(), callback)
        bridge.registered = True
        bridge.hotkey = "ctrl_win"
        bridge._events.append((bridge._generation, False, "start"))
        with patch("avanegar.windows.user32"):
            bridge.unregister()
        bridge._drain_events()
        callback.assert_not_called()

    def test_keyboard_hook_never_calls_sendinput_or_audio_inline(self):
        from avanegar.windows import KBDLLHOOKSTRUCT, WM_KEYDOWN, WM_KEYUP
        start, stop = MagicMock(), MagicMock()
        bridge = DesktopBridge(MagicMock(), start, stop)
        bridge.registered = True
        bridge.hotkey = "ctrl_win"
        with patch("avanegar.windows.user32") as api:
            for key in (0xA2, 0x5B):
                event = KBDLLHOOKSTRUCT(key, 0, 0, 0, 0)
                bridge._keyboard_event(0, WM_KEYDOWN, ctypes.addressof(event))
            event = KBDLLHOOKSTRUCT(0x5B, 0, 0, 0, 0)
            bridge._keyboard_event(0, WM_KEYUP, ctypes.addressof(event))
            api.SendInput.assert_not_called()
            start.assert_not_called()
            stop.assert_not_called()
            bridge._drain_events()
            start.assert_called_once()
            stop.assert_called_once()
            api.SendInput.assert_called()
            bridge.close()
