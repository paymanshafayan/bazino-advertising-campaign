"""Win32 global hotkey and guarded Ctrl+V injection (no focus stealing)."""
import ctypes
from ctypes import wintypes
import os
import logging
from collections import deque
import sys

from PySide6.QtCore import QAbstractNativeEventFilter, QTimer

from .domain import HOTKEYS
from .shortcuts import HoldShortcut

logger = logging.getLogger("avanegar.hotkey")

IS_WINDOWS = sys.platform == "win32"
HOTKEY_ID = 0xA701
WM_HOTKEY = 0x0312
MOD_NOREPEAT = 0x4000
WH_KEYBOARD_LL = 13
WM_KEYDOWN, WM_KEYUP = 0x0100, 0x0101
WM_SYSKEYDOWN, WM_SYSKEYUP = 0x0104, 0x0105
LLKHF_INJECTED = 0x10
MENU_MASK_KEY = 0xE8  # Unassigned VK: no text; keeps a bare Win chord from opening Start.

if IS_WINDOWS:
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]
    kernel32.GetModuleHandleW.restype = wintypes.HMODULE
    HOOKPROC = ctypes.WINFUNCTYPE(ctypes.c_ssize_t, ctypes.c_int, wintypes.WPARAM, wintypes.LPARAM)
    user32.SetWindowsHookExW.argtypes = [ctypes.c_int, HOOKPROC, wintypes.HINSTANCE, wintypes.DWORD]
    user32.SetWindowsHookExW.restype = wintypes.HANDLE
    user32.UnhookWindowsHookEx.argtypes = [wintypes.HANDLE]
    user32.UnhookWindowsHookEx.restype = wintypes.BOOL
    user32.CallNextHookEx.argtypes = [wintypes.HANDLE, ctypes.c_int, wintypes.WPARAM, wintypes.LPARAM]
    user32.CallNextHookEx.restype = ctypes.c_ssize_t

    class KBDLLHOOKSTRUCT(ctypes.Structure):
        _fields_ = [("vkCode", wintypes.DWORD), ("scanCode", wintypes.DWORD),
                    ("flags", wintypes.DWORD), ("time", wintypes.DWORD),
                    ("dwExtraInfo", ctypes.c_size_t)]

    user32.GetForegroundWindow.restype = wintypes.HWND
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.RegisterHotKey.argtypes = [wintypes.HWND, ctypes.c_int, wintypes.UINT, wintypes.UINT]
    user32.RegisterHotKey.restype = wintypes.BOOL
    user32.UnregisterHotKey.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.UnregisterHotKey.restype = wintypes.BOOL
    user32.GetAsyncKeyState.argtypes = [ctypes.c_int]
    user32.GetAsyncKeyState.restype = ctypes.c_short

    class KEYBDINPUT(ctypes.Structure):
        _fields_ = [("wVk", wintypes.WORD), ("wScan", wintypes.WORD),
                    ("dwFlags", wintypes.DWORD), ("time", wintypes.DWORD),
                    ("dwExtraInfo", ctypes.c_size_t)]

    class MOUSEINPUT(ctypes.Structure):
        _fields_ = [("dx", wintypes.LONG), ("dy", wintypes.LONG),
                    ("mouseData", wintypes.DWORD), ("dwFlags", wintypes.DWORD),
                    ("time", wintypes.DWORD), ("dwExtraInfo", ctypes.c_size_t)]

    class HARDWAREINPUT(ctypes.Structure):
        _fields_ = [("uMsg", wintypes.DWORD), ("wParamL", wintypes.WORD), ("wParamH", wintypes.WORD)]

    class INPUTUNION(ctypes.Union):
        _fields_ = [("ki", KEYBDINPUT), ("mi", MOUSEINPUT), ("hi", HARDWAREINPUT)]

    class INPUT(ctypes.Structure):
        _anonymous_ = ("data",)
        _fields_ = [("type", wintypes.DWORD), ("data", INPUTUNION)]

    user32.SendInput.argtypes = [wintypes.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
    user32.SendInput.restype = wintypes.UINT


class DesktopBridge(QAbstractNativeEventFilter):
    def __init__(self, app, on_press, on_release=None, on_cancel=None):
        super().__init__()
        self.app = app
        self.on_press = on_press
        self.on_release = on_release or (lambda: None)
        self.on_cancel = on_cancel or self.on_release
        self._events = deque()
        self._pump = QTimer()
        self._pump.setInterval(15)
        self._pump.timeout.connect(self._drain_events)
        self.registered = False
        self.hotkey = None
        self._hook = None
        self._hook_callback = None
        self._gesture = HoldShortcut()
        self._generation = 0
        if IS_WINDOWS:
            app.installNativeEventFilter(self)

    def register(self, name: str) -> bool:
        if not IS_WINDOWS or name not in HOTKEYS:
            return False
        old = self.hotkey
        self.unregister()
        if self._register_binding(name):
            return True
        # Preserve the previous working shortcut if the replacement fails.
        if old:
            self._register_binding(old)
        return False

    def _register_binding(self, name: str) -> bool:
        # A hook is needed for keyUP as well as keyDOWN for every hold shortcut.
        held = [key for key in range(8, 256) if user32.GetAsyncKeyState(key) & 0x8000]
        held = [key for key in held if key not in (0x10, 0x11, 0x12)]
        self._gesture = HoldShortcut(name, held)
        self._hook_callback = HOOKPROC(self._keyboard_event)
        self._hook = user32.SetWindowsHookExW(
            WH_KEYBOARD_LL, self._hook_callback, kernel32.GetModuleHandleW(None), 0,
        )
        if not self._hook:
            self._hook_callback = None
            logger.error("Keyboard hook registration failed: winerror=%s", ctypes.get_last_error())
            return False
        self.registered = True
        self.hotkey = name
        self._pump.start()
        logger.info("Push-to-talk shortcut registered: %s", name)
        return True

    def _keyboard_event(self, code, message, pointer):
        # Never invoke Qt, PortAudio, SendInput or recording callbacks inside the
        # native keyboard hook. The periodic Qt pump handles them AFTER it returns.
        try:
            if code >= 0 and message in (WM_KEYDOWN, WM_KEYUP, WM_SYSKEYDOWN, WM_SYSKEYUP):
                event = ctypes.cast(pointer, ctypes.POINTER(KBDLLHOOKSTRUCT)).contents
                if not event.flags & LLKHF_INJECTED:
                    mask, action = self._gesture.feed(
                        event.vkCode, message in (WM_KEYDOWN, WM_SYSKEYDOWN),
                    )
                    if action:
                        self._events.append((self._generation, mask, action))
        except Exception:
            # No exception may escape a ctypes callback.
            self._events.append((self._generation, False, "error"))
        return user32.CallNextHookEx(self._hook, code, message, pointer)

    def _drain_events(self):
        while self._events:
            generation, mask, action = self._events.popleft()
            if not self.registered or generation != self._generation:
                continue
            if mask:
                self._mask_start_menu()
            logger.info("Shortcut event: %s", action)
            if action == "start":
                self.on_press()
            elif action == "stop":
                self.on_release()
            else:
                if action == "error":
                    logger.error("Keyboard hook callback failed; cancelling recording")
                self.on_cancel()

    @staticmethod
    def _mask_start_menu():
        # Windows treats a bare Win release as Start. One unassigned, non-text
        # key pair marks the modifier as used without suppressing real keyups.
        events = (INPUT * 2)()
        for event, flags in zip(events, (0, 2)):
            event.type = 1
            event.ki = KEYBDINPUT(MENU_MASK_KEY, 0, flags, 0, 0)
        sent = user32.SendInput(2, events, ctypes.sizeof(INPUT))
        if sent == 1:
            release = (INPUT * 1)(events[1])
            user32.SendInput(1, release, ctypes.sizeof(INPUT))

    def unregister(self):
        self._generation += 1
        self._pump.stop()
        self._events.clear()
        if IS_WINDOWS and self._hook:
            user32.UnhookWindowsHookEx(self._hook)
            self._hook = None
            self._hook_callback = None
        elif IS_WINDOWS and self.registered:
            user32.UnregisterHotKey(None, HOTKEY_ID)
        self.registered = False
        self.hotkey = None
        self._gesture = HoldShortcut()

    def nativeEventFilter(self, event_type, message):
        return False, 0

    @staticmethod
    def foreground() -> int:
        return int(user32.GetForegroundWindow() or 0) if IS_WINDOWS else 0

    @staticmethod
    def is_own_window(hwnd: int) -> bool:
        if not IS_WINDOWS or not hwnd:
            return True
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        return pid.value == os.getpid()

    @staticmethod
    def modifiers_down() -> bool:
        return IS_WINDOWS and any(user32.GetAsyncKeyState(k) & 0x8000 for k in (0x10, 0x11, 0x12, 0x5B, 0x5C, 0x20, 0x78))

    @staticmethod
    def paste() -> bool:
        if not IS_WINDOWS:
            return False
        events = (INPUT * 4)()
        for event, (key, flags) in zip(events, [(0x11, 0), (0x56, 0), (0x56, 2), (0x11, 2)]):
            event.type = 1  # INPUT_KEYBOARD
            event.ki = KEYBDINPUT(key, 0, flags, 0, 0)
        sent = user32.SendInput(4, events, ctypes.sizeof(INPUT))
        if sent != 4:
            # Don't leave Ctrl or V logically held after a partial SendInput.
            releases = (INPUT * 2)(events[2], events[3])
            user32.SendInput(2, releases, ctypes.sizeof(INPUT))
        return sent == 4

    def close(self):
        self.unregister()
        if IS_WINDOWS:
            self.app.removeNativeEventFilter(self)
