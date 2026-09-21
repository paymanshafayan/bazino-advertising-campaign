"""Filesystem-first lookup of the SAME cache used by Avanegar 1.x.

A cache hit does not import the Hub client, contact a server, check for updates,
copy weights, or download anything. Downloads are only for missing/incomplete
models, and still use %LOCALAPPDATA%/Avanegar/models as their cache root.
"""
import json
import logging
from pathlib import Path

from .domain import LOCAL_MODELS

logger = logging.getLogger("avanegar.models")


def complete_model(path: Path) -> bool:
    try:
        for name in ("model.bin", "config.json", "tokenizer.json"):
            if not (path / name).is_file() or (path / name).stat().st_size == 0:
                return False
        # Vocabulary is part of a CTranslate2 export, not merely a Hub directory.
        if not any((path / name).is_file() and (path / name).stat().st_size > 0
                   for name in ("vocabulary.json", "vocabulary.txt")):
            return False
        for name in ("config.json", "tokenizer.json"):
            if (path / name).stat().st_size > 32_000_000:
                return False
            if not isinstance(json.loads((path / name).read_text(encoding="utf-8")), dict):
                return False
        return True
    except (OSError, ValueError):
        return False


def find_cached_model(name: str, directory: Path) -> Path | None:
    if name not in LOCAL_MODELS:
        raise ValueError("مدل محلی انتخاب‌شده معتبر نیست.")
    # Also accept manually copied, explicitly named CTranslate2 model folders.
    candidates = [directory / name, directory / f"faster-whisper-{name}"]
    repository = directory / f"models--Systran--faster-whisper-{name}"
    snapshots = repository / "snapshots"
    try:
        revision = (repository / "refs" / "main").read_text(encoding="utf-8").strip()
        if revision and revision not in (".", "..") and "/" not in revision and "\\" not in revision:
            candidates.append(snapshots / revision)
    except OSError:
        pass
    # refs/main may be missing or point at a partial update; reuse a complete
    # older snapshot instead of redownloading. Names isolate Small from Medium.
    try:
        candidates.extend(sorted((p for p in snapshots.iterdir() if p.is_dir()),
                                 key=lambda p: p.stat().st_mtime, reverse=True))
    except OSError:
        pass
    for candidate in dict.fromkeys(candidates):
        if complete_model(candidate):
            return candidate
    return None


def resolve_model(name: str, directory: Path, progress=lambda message: None) -> str:
    progress(f"در حال بررسی مدل {name} در پوشهٔ مدل‌های قبلی…")
    cached = find_cached_model(name, directory)
    if cached is not None:
        logger.info("Local model cache hit: model=%s path=%s; no download", name, cached)
        progress(f"مدل {name} از قبل موجود است؛ بدون دانلود دوباره بارگذاری می‌شود.")
        return str(cached)
    logger.info("Local model missing/incomplete: model=%s cache=%s", name, directory)
    progress(f"مدل {name} کامل پیدا نشد؛ دریافت/تکمیل در همان پوشه آغاز شد. اینترنت لازم است…")
    directory.mkdir(parents=True, exist_ok=True)
    from faster_whisper.utils import download_model
    downloaded = Path(download_model(name, cache_dir=str(directory), local_files_only=False))
    if not complete_model(downloaded):
        raise OSError("فایل‌های مدل پس از دریافت هنوز کامل نیستند. اتصال و فضای دیسک را بررسی کنید.")
    logger.info("Local model download ready: model=%s", name)
    return str(downloaded)
