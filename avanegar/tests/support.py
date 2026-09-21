"""Allow hardware-independent audio tests on runners without PortAudio."""
import sys
import types
from unittest.mock import MagicMock


def ensure_audio_import():
    try:
        import sounddevice  # noqa: F401
    except OSError:
        if sys.platform == "win32":
            raise  # The Windows wheel must include its actual native DLL.
        fake = types.ModuleType("sounddevice")
        fake.PortAudioError = type("PortAudioError", (Exception,), {})
        fake.CallbackStop = type("CallbackStop", (Exception,), {})
        fake.InputStream = MagicMock()
        fake.query_devices = MagicMock(return_value=[])
        fake.check_input_settings = MagicMock()
        sys.modules["sounddevice"] = fake
