import logging
from pathlib import Path
import tempfile
import unittest

from avanegar.diagnostics import configure_logging


class DiagnosticsTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.path = Path(self.directory.name)
        self.buffer = configure_logging(self.path)
        self.logger = logging.getLogger("avanegar.test")
        self.addCleanup(self.cleanup)

    def cleanup(self):
        logger = logging.getLogger("avanegar")
        for handler in logger.handlers[:]:
            handler.close()
            logger.removeHandler(handler)
        self.directory.cleanup()

    def test_exception_trace_and_stage_are_captured(self):
        try:
            raise RuntimeError("device unavailable")
        except RuntimeError:
            self.logger.exception("Stopping microphone failed")
        _, text = self.buffer.snapshot()
        self.assertIn("Traceback", text)
        self.assertIn("RuntimeError: device unavailable", text)
        self.assertIn("Stopping microphone failed", text)
        self.assertIn("device unavailable", (self.path / "logs" / "avanegar.log").read_text(encoding="utf-8"))

    def test_home_and_url_secrets_are_redacted(self):
        self.logger.warning("file=%s/model?token=SECRET&sig=SIGNATURE", Path.home())
        _, text = self.buffer.snapshot()
        self.assertNotIn(str(Path.home()), text)
        self.assertNotIn("SECRET", text)
        self.assertNotIn("SIGNATURE", text)
        self.assertIn("<USER_HOME>", text)

    def test_recent_previous_session_is_loaded(self):
        self.logger.info("previous session marker")
        new_buffer = configure_logging(self.path)
        self.assertIn("previous session marker", new_buffer.snapshot()[1])

    def test_ring_buffer_has_line_limit(self):
        for i in range(1100):
            self.logger.info("event %s", i)
        self.assertLessEqual(len(self.buffer.snapshot()[1].splitlines()), 1000)

    def test_disk_failure_keeps_memory_log_working(self):
        blocked = self.path / "blocked"
        blocked.write_text("not a directory")
        buffer = configure_logging(blocked)
        self.logger.info("still usable")
        self.assertIn("still usable", buffer.snapshot()[1])
        self.assertIn("Log file unavailable", buffer.snapshot()[1])
