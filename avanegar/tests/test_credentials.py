from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

from avanegar.credentials import KeyStore, validate_key


class KeyStoreTests(unittest.TestCase):
    def test_key_validation(self):
        self.assertEqual(validate_key(" sk-fake-key "), "sk-fake-key")
        for value in ("", "short", "sk-one\nsecond", "الفبای فارسی"):
            with self.assertRaises(ValueError):
                validate_key(value)

    def test_encrypted_file_not_plain_settings_and_delete(self):
        with tempfile.TemporaryDirectory() as directory:
            store = KeyStore(Path(directory))
            with patch("avanegar.credentials._dpapi", return_value=b"ENCRYPTED-ONLY"):
                store.save("sk-fake-for-test")
            self.assertEqual(store.path.read_bytes(), b"ENCRYPTED-ONLY")
            self.assertEqual(store.get(), "sk-fake-for-test")
            self.assertFalse((Path(directory) / "settings.json").exists())
            store.delete()
            self.assertEqual(store.get(), "")
            self.assertFalse(store.path.exists())

    @unittest.skipUnless(sys.platform == "win32", "DPAPI is Windows-only")
    def test_real_dpapi_round_trip_on_windows(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            key = "sk-not-a-real-key-used-only-by-unit-tests"
            store = KeyStore(path)
            store.save(key)
            self.assertNotIn(key.encode(), store.path.read_bytes())
            self.assertEqual(KeyStore(path).get(), key)

    def test_failed_encryption_never_falls_back_to_plaintext(self):
        with tempfile.TemporaryDirectory() as directory:
            store = KeyStore(Path(directory))
            with patch("avanegar.credentials._dpapi", side_effect=OSError("unavailable")):
                with self.assertRaises(OSError):
                    store.save("sk-fake-for-test")
            self.assertFalse(store.path.exists())
            self.assertEqual(store.get(), "")
