"""User-bound Windows DPAPI storage. Keys never enter settings.json or logs."""
import ctypes
from ctypes import wintypes
from pathlib import Path
import sys
import threading

IS_WINDOWS = sys.platform == "win32"


def validate_key(value: str) -> str:
    value = value.strip()
    if not 8 <= len(value) <= 4096 or any(c.isspace() or ord(c) < 33 or ord(c) > 126 for c in value):
        raise ValueError("کلید API نامعتبر است؛ کلید را بدون فاصله یا خط جدید وارد کنید.")
    return value


def _dpapi(data: bytes, decrypt=False) -> bytes:
    if not IS_WINDOWS:
        raise OSError("ذخیرهٔ امن کلید فقط روی ویندوز پشتیبانی می‌شود.")

    class DATA_BLOB(ctypes.Structure):
        _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_ubyte))]

    crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p
    source = ctypes.create_string_buffer(data)
    incoming = DATA_BLOB(len(data), ctypes.cast(source, ctypes.POINTER(ctypes.c_ubyte)))
    outgoing = DATA_BLOB()
    fn = crypt32.CryptUnprotectData if decrypt else crypt32.CryptProtectData
    fn.argtypes = [ctypes.POINTER(DATA_BLOB), ctypes.c_void_p, ctypes.c_void_p,
                   ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(DATA_BLOB)]
    fn.restype = wintypes.BOOL
    # UI_FORBIDDEN only; absence of LOCAL_MACHINE binds it to this Windows user.
    if not fn(ctypes.byref(incoming), None, None, None, None, 1, ctypes.byref(outgoing)):
        raise OSError("ویندوز نتوانست کلید را رمزگذاری/بازیابی کند. کلید را دوباره در تنظیمات وارد کنید.")
    try:
        return ctypes.string_at(outgoing.pbData, outgoing.cbData)
    finally:
        kernel32.LocalFree(outgoing.pbData)


class KeyStore:
    def __init__(self, directory: Path):
        self.path = directory / "api-key.dpapi"
        self._lock = threading.RLock()
        self._cached = None

    def get(self) -> str:
        with self._lock:
            if self._cached is not None:
                return self._cached
            if not self.path.exists():
                return ""
            self._cached = validate_key(_dpapi(self.path.read_bytes(), decrypt=True).decode("utf-8"))
            return self._cached

    def save(self, value: str):
        value = validate_key(value)
        encrypted = _dpapi(value.encode("utf-8"))
        with self._lock:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            temporary = self.path.with_suffix(".tmp")
            temporary.write_bytes(encrypted)
            temporary.replace(self.path)
            self._cached = value

    def delete(self):
        with self._lock:
            self.path.unlink(missing_ok=True)
            self._cached = None
