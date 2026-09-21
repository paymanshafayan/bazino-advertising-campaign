import unittest
from unittest.mock import MagicMock
import numpy as np

from support import ensure_audio_import
ensure_audio_import()
from avanegar.audio import AudioClip  # noqa: E402
from avanegar.cloud import CloudError  # noqa: E402
from avanegar.transcriber import Transcriber  # noqa: E402


class TranscriberTests(unittest.TestCase):
    def setUp(self):
        self.keys = MagicMock()
        self.keys.get.return_value = "sk-not-a-real-key"
        self.client = MagicMock()
        self.client.transcribe.return_value = "سلام دنیا"
        self.factory = MagicMock(return_value=self.client)
        self.worker = Transcriber(self.keys, self.factory)
        self.results, self.errors = [], []
        self.worker.completed.connect(lambda *args: self.results.append(args))
        self.worker.failed.connect(self.errors.append)
        self.clip = AudioClip(np.full(16000, 0.1, dtype=np.float32), 16000)

    def test_cloud_recognition_preserves_text_and_does_not_log_secrets(self):
        with self.assertLogs("avanegar", level="INFO") as log:
            self.worker.run(self.clip, "fa", "واژه خصوصی", True)
        self.assertEqual(self.results, [("سلام دنیا", False)])
        self.assertEqual(self.client.transcribe.call_args.args[2:4], ("fa", "واژه خصوصی"))
        self.assertNotIn("واژه خصوصی", "\n".join(log.output))
        self.assertNotIn("سلام دنیا", "\n".join(log.output))
        self.assertNotIn("sk-not-a-real-key", "\n".join(log.output))
        self.client.close.assert_called_once()

    def test_missing_consent_prevents_network(self):
        self.worker.run(self.clip, "fa", "", False)
        self.factory.assert_not_called()
        self.assertEqual(len(self.errors), 1)

    def test_missing_key_prevents_network(self):
        self.keys.get.return_value = ""
        self.worker.run(self.clip, "fa", "", True)
        self.factory.assert_not_called()
        self.assertEqual(len(self.errors), 1)

    def test_no_audio_and_short_taps_are_not_uploaded(self):
        for clip in (AudioClip(np.zeros(16000, dtype=np.float32), 16000), AudioClip(np.ones(100, dtype=np.float32), 16000)):
            self.worker.run(clip, "fa", "آوانگار", True)
        self.factory.assert_not_called()
        self.assertEqual(self.results, [("", False), ("", False)])

    def test_connection_test_uploads_no_audio(self):
        self.worker.run(None, "fa", "", True)
        self.client.check.assert_called_once()
        self.client.transcribe.assert_not_called()
        self.assertEqual(self.results, [("", True)])

    def test_safe_cloud_error_is_displayed(self):
        self.client.transcribe.side_effect = CloudError("quota is exhausted")
        self.worker.run(self.clip, "fa", "", True)
        self.assertEqual(self.errors, ["quota is exhausted"])

    def test_unexpected_exception_contents_are_not_logged(self):
        self.client.transcribe.side_effect = RuntimeError("sk-SECRET response private text")
        with self.assertLogs("avanegar", level="INFO") as log:
            self.worker.run(self.clip, "fa", "", True)
        self.assertNotIn("SECRET", "\n".join(log.output))
        self.assertNotIn("private text", "\n".join(log.output))
        self.assertEqual(len(self.errors), 1)
