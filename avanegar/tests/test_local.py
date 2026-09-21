from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import MagicMock, patch
import numpy as np

from support import ensure_audio_import
ensure_audio_import()
from avanegar.audio import AudioClip  # noqa: E402
from avanegar.local import LocalEngine  # noqa: E402


class LocalEngineTests(unittest.TestCase):
    def setUp(self):
        self.engine = LocalEngine(Path("unused/models"))
        self.progress = MagicMock()
        self.model = MagicMock()
        self.model.transcribe.return_value = ([SimpleNamespace(text=" سلام "), SimpleNamespace(text="دنیا")], None)

    def test_existing_path_loaded_with_local_files_only_and_cpu(self):
        with patch("avanegar.local.resolve_model", return_value="existing-cache-path") as resolve, patch("faster_whisper.WhisperModel", return_value=self.model) as factory:
            self.engine.prepare("small", self.progress)
            self.engine.prepare("small", self.progress)
        resolve.assert_called_once_with("small", Path("unused/models"), self.progress)
        factory.assert_called_once()
        self.assertEqual(factory.call_args.args[0], "existing-cache-path")
        self.assertTrue(factory.call_args.kwargs["local_files_only"])
        self.assertEqual(factory.call_args.kwargs["device"], "cpu")

    def test_persian_hint_and_recognition(self):
        self.engine.model, self.engine.name = self.model, "small"
        text = self.engine.transcribe(AudioClip(np.full(16000, 0.1, dtype=np.float32), 16000), "small", "fa", "آوانگار", self.progress)
        self.assertEqual(text, "سلام دنیا")
        self.assertEqual(self.model.transcribe.call_args.kwargs["language"], "fa")
        self.assertEqual(self.model.transcribe.call_args.kwargs["hotwords"], "آوانگار")

    def test_auto_language_and_hint_opt_out(self):
        self.engine.model, self.engine.name = self.model, "small"
        self.engine.transcribe(AudioClip(np.full(16000, 0.1, dtype=np.float32), 16000), "small", "auto", "آوانگار", self.progress)
        self.assertIsNone(self.model.transcribe.call_args.kwargs["language"])
        self.assertIsNone(self.model.transcribe.call_args.kwargs["hotwords"])
