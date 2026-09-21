from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from huggingface_hub.errors import LocalEntryNotFoundError
from avanegar.models import complete_model, resolve_model


class ModelCacheTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name)

    def make_complete(self):
        for name in ("model.bin", "tokenizer.json", "config.json"):
            (self.path / name).write_bytes(b"test")

    def test_prepared_model_does_not_request_network(self):
        self.make_complete()
        with patch("faster_whisper.utils.download_model", return_value=str(self.path)) as download:
            self.assertEqual(resolve_model("small", self.path), str(self.path))
            download.assert_called_once_with("small", cache_dir=str(self.path), local_files_only=True)

    def test_missing_cache_downloads(self):
        self.make_complete()
        with patch("faster_whisper.utils.download_model", side_effect=[LocalEntryNotFoundError("missing"), str(self.path)]) as download:
            resolve_model("base", self.path)
            self.assertFalse(download.call_args.kwargs["local_files_only"])

    def test_incomplete_snapshot_is_not_treated_as_prepared(self):
        (self.path / "config.json").write_text("{}")
        calls = []
        def download(name, **kwargs):
            calls.append(kwargs["local_files_only"])
            if not kwargs["local_files_only"]:
                self.make_complete()
            return str(self.path)
        with patch("faster_whisper.utils.download_model", side_effect=download):
            resolve_model("small", self.path)
        self.assertEqual(calls, [True, False])

    def test_zero_length_model_is_incomplete(self):
        self.make_complete()
        (self.path / "model.bin").write_bytes(b"")
        self.assertFalse(complete_model(self.path))

    def test_invalid_download_raises(self):
        with patch("faster_whisper.utils.download_model", return_value=str(self.path)):
            with self.assertRaises(OSError):
                resolve_model("small", self.path)
