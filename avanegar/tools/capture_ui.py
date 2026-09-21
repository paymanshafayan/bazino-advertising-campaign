"""Capture the real Qt interface without microphone/model access (CI visual review)."""
import os
from pathlib import Path
import sys
import tempfile
from unittest.mock import patch

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from PySide6.QtWidgets import QApplication  # noqa: E402
from avanegar.ui import MainWindow  # noqa: E402

app = QApplication([])
output = Path("build/screenshots")
output.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory() as directory:
    with patch("avanegar.ui.QSystemTrayIcon.isSystemTrayAvailable", return_value=False), \
         patch("avanegar.ui.DesktopBridge.register", return_value=True), \
         patch("avanegar.ui.AudioService.refresh_devices"):
        window = MainWindow(Path(directory))
    window._scan_finished()
    window.resize(1080, 820)
    window.show()
    for index, name in enumerate(("dictation", "settings", "help", "log")):
        window._navigate(index)
        app.processEvents()
        window.grab().save(str(output / f"{name}.png"))
    window._quit_now()
