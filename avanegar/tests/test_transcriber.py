import io
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import MagicMock, patch
import wave

import numpy as np
from support import ensure_audio_import
ensure_audio_import()
from avanegar.audio import AudioClip  # noqa: E402
from avanegar.transcriber import Transcriber  # noqa: E402


class TranscriberTests(unittest.TestCase):
    def setUp(self):
        resolver = patch("avanegar.transcriber.resolve_model", side_effect=lambda name, directory: name)
        resolver.start()
        self.addCleanup(resolver.stop)
        self.worker = Transcriber(Path("unused-test-models"))
        self.results, self.errors = [], []
        self.worker.completed.connect(lambda *args: self.results.append(args))
        self.worker.failed.connect(self.errors.append)
        self.model = MagicMock()
        self.model.transcribe.return_value = ([SimpleNamespace(text=" سلام "), SimpleNamespace(text="دنیا")], None)
        self.patch = patch("faster_whisper.WhisperModel", return_value=self.model)
        self.factory = self.patch.start()
        self.addCleanup(self.patch.stop)

    def test_prepare_and_reuse_model(self):
        self.worker.run(None, "small", "fa")
        self.worker.run(AudioClip(np.ones(16000, dtype=np.float32), 16000), "small", "fa")
        self.factory.assert_called_once()
        self.assertEqual(self.results, [("", True), ("سلام دنیا", False)])
        kwargs = self.model.transcribe.call_args.kwargs
        self.assertEqual(kwargs["language"], "fa")
        self.assertTrue(kwargs["vad_filter"])
        self.assertEqual(self.factory.call_args.kwargs["device"], "cpu")

    def test_model_change_loads_new_model(self):
        self.worker.run(None, "base", "fa")
        self.worker.run(None, "small", "fa")
        self.assertEqual(self.factory.call_count, 2)

    def test_auto_language_and_native_rate_wav_in_memory(self):
        self.worker.run(AudioClip(np.ones(48000, dtype=np.float32), 48000), "base", "auto")
        self.assertIsNone(self.model.transcribe.call_args.kwargs["language"])
        buffer = self.model.transcribe.call_args.args[0]
        self.assertIsInstance(buffer, io.BytesIO)
        buffer.seek(0)
        with wave.open(buffer) as wav:
            self.assertEqual(wav.getframerate(), 48000)
            self.assertEqual(wav.getnframes(), 48000)

    def test_short_recording_does_not_decode(self):
        self.worker.run(AudioClip(np.zeros(100, dtype=np.float32), 16000), "base", "fa")
        self.model.transcribe.assert_not_called()
        self.factory.assert_not_called()
        self.assertEqual(self.results, [("", False)])

    def test_download_failure_is_recoverable(self):
        self.factory.side_effect = OSError("offline")
        self.worker.run(None, "base", "fa")
        self.assertEqual(len(self.errors), 1)
        self.assertIn("offline", self.errors[0])
        self.assertIsNone(self.worker._model_name)
        self.factory.side_effect = None
        self.worker.run(None, "base", "fa")
        self.assertEqual(self.results, [("", True)])

    def test_generator_failure_is_caught(self):
        def broken():
            yield SimpleNamespace(text="partial")
            raise RuntimeError("decoding failed")
        self.model.transcribe.return_value = (broken(), None)
        self.worker.run(AudioClip(np.ones(16000, dtype=np.float32), 16000), "base", "fa")
        self.assertEqual(self.results, [])
        self.assertIn("decoding failed", self.errors[0])
