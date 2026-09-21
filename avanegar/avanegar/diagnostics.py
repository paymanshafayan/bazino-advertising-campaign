"""Bounded diagnostics, with rotating files and redacted personal paths.

Never log audio, recognized text, clipboard data or foreground window titles.
"""
from collections import deque
import logging
from logging.handlers import RotatingFileHandler
from pathlib import Path
import platform
import re
import sys
import threading

from . import __version__


class SafeFormatter(logging.Formatter):
    def format(self, record):
        text = super().format(record)
        home = str(Path.home())
        for prefix in (home, home.replace("\\", "/")):
            text = text.replace(prefix, "<USER_HOME>")
        text = re.sub(r"(?i)(token|authorization|signature|sig|api_key)=([^\s&]+)", r"\1=<REDACTED>", text)
        return text[:16000]


class LogBuffer(logging.Handler):
    def __init__(self, previous=""):
        super().__init__()
        self.lines = deque(previous.splitlines()[-1000:], maxlen=1000)
        self.revision = 0

    def emit(self, record):
        try:
            text = self.format(record)
            with self.lock:
                self.lines.extend(text.splitlines())
                self.revision += 1
        except Exception:
            self.handleError(record)

    def snapshot(self):
        with self.lock:
            return self.revision, "\n".join(self.lines)


def configure_logging(directory: Path):
    logger = logging.getLogger("avanegar")
    logger.setLevel(logging.INFO)
    logger.propagate = False
    for handler in logger.handlers[:]:
        logger.removeHandler(handler)
        handler.close()
    path = directory / "logs" / "avanegar.log"
    previous, file_handler, error = "", None, None
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        if path.exists():
            with path.open("rb") as old:
                old.seek(max(0, path.stat().st_size - 256_000))
                previous = old.read().decode("utf-8", errors="replace")
        file_handler = RotatingFileHandler(path, maxBytes=1_000_000, backupCount=2, encoding="utf-8")
    except OSError as exc:
        error = exc
    formatter = SafeFormatter("%(asctime)s %(levelname)s [%(threadName)s] %(name)s: %(message)s")
    buffer = LogBuffer(previous)
    for handler in (buffer, file_handler):
        if handler is not None:
            handler.setFormatter(formatter)
            logger.addHandler(handler)
    logger.info("=== Avanegar %s | %s %s | Python %s | frozen=%s ===",
                __version__, platform.system(), platform.release(), platform.python_version(), bool(getattr(sys, "frozen", False)))
    if error:
        logger.warning("Log file unavailable; in-memory diagnostics still work: %s", error)
    return buffer


def install_exception_logging():
    def exception_hook(kind, value, trace):
        logging.getLogger("avanegar.unhandled").error("Unhandled Python exception", exc_info=(kind, value, trace))
    sys.excepthook = exception_hook
    threading.excepthook = lambda args: exception_hook(args.exc_type, args.exc_value, args.exc_traceback)


def close_logging():
    logger = logging.getLogger("avanegar")
    for handler in logger.handlers[:]:
        logger.removeHandler(handler)
        handler.close()
