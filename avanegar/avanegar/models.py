"""Cache-first model resolution: prepared models require no network calls."""
from pathlib import Path


def resolve_model(name: str, directory: Path) -> str:
    from faster_whisper.utils import download_model
    from huggingface_hub.errors import LocalEntryNotFoundError

    try:
        cached = Path(download_model(name, cache_dir=str(directory), local_files_only=True))
    except LocalEntryNotFoundError:
        cached = None
    if cached is not None and complete_model(cached):
        return str(cached)
    # An interrupted snapshot can exist with no model.bin: finish the download.
    path = Path(download_model(name, cache_dir=str(directory), local_files_only=False))
    if not complete_model(path):
        raise OSError("فایل‌های مدل کامل نیستند؛ دریافت را دوباره امتحان کنید.")
    return str(path)


def complete_model(path: Path) -> bool:
    required = ("model.bin", "config.json", "tokenizer.json")
    return all((path / filename).is_file() and (path / filename).stat().st_size > 0 for filename in required)
