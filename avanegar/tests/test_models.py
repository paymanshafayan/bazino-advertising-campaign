import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from avanegar.models import complete_model, find_cached_model, resolve_model


def make_model(path):
    path.mkdir(parents=True, exist_ok=True)
    (path / "model.bin").write_bytes(b"mock-weights")
    (path / "config.json").write_text(json.dumps({"model_type": "Whisper"}))
    (path / "tokenizer.json").write_text(json.dumps({"version": "1.0"}))
    (path / "vocabulary.json").write_text('["test"]')
    return path


class ModelCacheTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "Avanegar" / "models"
        self.repo = self.root / "models--Systran--faster-whisper-small"

    def test_legacy_hub_snapshot_reused_with_no_download_or_update_check(self):
        model = make_model(self.repo / "snapshots" / "previous-version-sha")
        (self.repo / "refs").mkdir()
        (self.repo / "refs" / "main").write_text("previous-version-sha")
        with patch("faster_whisper.utils.download_model", side_effect=AssertionError("must stay offline")) as download, patch("requests.sessions.Session.request", side_effect=AssertionError("no HTTP")):
            self.assertEqual(resolve_model("small", self.root), str(model))
        download.assert_not_called()

    def test_missing_refs_still_reuses_complete_snapshot(self):
        model = make_model(self.repo / "snapshots" / "sha")
        self.assertEqual(find_cached_model("small", self.root), model)

    def test_incomplete_new_snapshot_does_not_hide_old_complete_one(self):
        model = make_model(self.repo / "snapshots" / "old")
        incomplete = self.repo / "snapshots" / "new"
        incomplete.mkdir()
        (incomplete / "model.bin.incomplete").write_bytes(b"partial")
        (self.repo / "refs").mkdir()
        (self.repo / "refs" / "main").write_text("new")
        with patch("faster_whisper.utils.download_model") as download:
            self.assertEqual(resolve_model("small", self.root), str(model))
        download.assert_not_called()

    def test_model_selection_isolated(self):
        make_model(self.repo / "snapshots" / "sha")
        self.assertIsNone(find_cached_model("medium", self.root))

    def test_named_manual_export_supported(self):
        model = make_model(self.root / "medium")
        self.assertEqual(find_cached_model("medium", self.root), model)

    def test_empty_missing_broken_json_and_partial_files_not_complete(self):
        model = make_model(self.root / "small")
        for name in ("model.bin", "config.json", "tokenizer.json", "vocabulary.json"):
            with self.subTest(name=name):
                make_model(model)
                (model / name).write_bytes(b"")
                self.assertFalse(complete_model(model))
        make_model(model)
        (model / "tokenizer.json").write_text("{broken")
        self.assertFalse(complete_model(model))
        (model / "tokenizer.json").unlink()
        (model / "tokenizer.json.incomplete").write_text("{}")
        self.assertFalse(complete_model(model))

    def test_only_missing_model_downloads_into_same_root(self):
        output = self.root / "models--Systran--faster-whisper-medium" / "snapshots" / "new"
        def download(name, **kwargs):
            self.assertEqual(name, "medium")
            self.assertEqual(kwargs, {"cache_dir": str(self.root), "local_files_only": False})
            return str(make_model(output))
        with patch("faster_whisper.utils.download_model", side_effect=download) as request:
            self.assertEqual(resolve_model("medium", self.root), str(output))
        request.assert_called_once()
        with patch("faster_whisper.utils.download_model") as no_repeat:
            self.assertEqual(resolve_model("medium", self.root), str(output))
        no_repeat.assert_not_called()

    def test_inspection_does_not_create_or_download_anything(self):
        with patch("faster_whisper.utils.download_model") as download:
            self.assertIsNone(find_cached_model("small", self.root))
        self.assertFalse(self.root.exists())
        download.assert_not_called()

    def test_invalid_model_rejected_before_network(self):
        with patch("faster_whisper.utils.download_model") as download:
            with self.assertRaises(ValueError):
                resolve_model("../other", self.root)
        download.assert_not_called()

    def test_incomplete_download_fails_instead_of_using_partial_weights(self):
        self.root.mkdir(parents=True)
        with patch("faster_whisper.utils.download_model", return_value=str(self.root)):
            with self.assertRaises(OSError):
                resolve_model("small", self.root)
