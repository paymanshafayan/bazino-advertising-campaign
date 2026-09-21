"""Platform-independent settings, persistence, and insertion guards."""
from dataclasses import asdict, dataclass
import json
import os
from pathlib import Path

APP_NAME = "Avanegar"
MAX_RECORDING_SECONDS = 300
MODELS = {"base": "سریع · Base", "small": "متعادل · Small", "medium": "دقیق‌تر · Medium"}
LANGUAGES = {"fa": "فارسی", "en": "English", "ar": "العربية", "auto": "تشخیص خودکار"}
HOTKEYS = {
    "ctrl_win": ("Ctrl + Win", 0, 0),
    "ctrl_shift_space": ("Ctrl + Shift + Space", 0x0002 | 0x0004, 0x20),
    "ctrl_alt_f9": ("Ctrl + Alt + F9", 0x0002 | 0x0001, 0x78),
}


def data_directory() -> Path:
    root = Path(os.environ.get("LOCALAPPDATA", Path.home() / ".local" / "share"))
    directory = root / APP_NAME
    directory.mkdir(parents=True, exist_ok=True)
    return directory


@dataclass
class Settings:
    model: str = "small"
    language: str = "fa"
    hotkey: str = "ctrl_win"
    device_name: str = ""
    auto_paste: bool = True

    @classmethod
    def load(cls, path: Path) -> "Settings":
        try:
            raw = json.loads(path.read_text(encoding="utf-8"))
            if not isinstance(raw, dict):
                return cls()
            return cls(
                model=raw.get("model") if raw.get("model") in MODELS else "small",
                language=raw.get("language") if raw.get("language") in LANGUAGES else "fa",
                hotkey=raw.get("hotkey") if raw.get("hotkey") in HOTKEYS else "ctrl_win",
                device_name=raw.get("device_name", "") if isinstance(raw.get("device_name", ""), str) else "",
                auto_paste=raw.get("auto_paste", True) if isinstance(raw.get("auto_paste", True), bool) else True,
            )
        except (OSError, ValueError, TypeError):
            return cls()

    def save(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary = path.with_suffix(".tmp")
        temporary.write_text(json.dumps(asdict(self), ensure_ascii=False, indent=2), encoding="utf-8")
        temporary.replace(path)


def can_insert(target: int, current: int, own_process: bool, enabled: bool) -> bool:
    """Never steal focus or paste into a different window than the recording target."""
    return bool(enabled and target and target == current and not own_process)
