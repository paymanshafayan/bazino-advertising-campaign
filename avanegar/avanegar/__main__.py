import os
import sys

# Prevent the model hub's Windows symlink warning; no administrator access needed.
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")
os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")

from PySide6.QtCore import QLockFile
from PySide6.QtGui import QFont
from PySide6.QtWidgets import QApplication, QMessageBox

from .domain import data_directory
from .diagnostics import install_exception_logging
from .ui import MainWindow


def main():
    app = QApplication(sys.argv)
    app.setApplicationName("Avanegar")
    app.setOrganizationName("Avanegar")
    app.setFont(QFont("Segoe UI", 10))
    app.setQuitOnLastWindowClosed(False)
    try:
        directory = data_directory()
    except OSError as exc:
        QMessageBox.critical(None, "آوانگار", f"پوشهٔ داده‌های برنامه قابل ایجاد نیست:\n{exc}")
        return 1
    # Avoid two instances competing for the microphone and global shortcut.
    lock = QLockFile(str(directory / "app.lock"))
    lock.setStaleLockTime(0)
    if not lock.tryLock(100):
        QMessageBox.information(None, "آوانگار", "آوانگار از قبل باز است؛ آیکن کنار ساعت ویندوز را بررسی کنید.")
        return 0
    install_exception_logging()
    window = MainWindow(directory)
    window.show()
    try:
        return app.exec()
    finally:
        lock.unlock()


if __name__ == "__main__":
    sys.exit(main())
