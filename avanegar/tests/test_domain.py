import json
from pathlib import Path
import tempfile
import unittest

from avanegar.domain import Settings, can_insert


class SettingsTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / "settings.json"

    def test_defaults_for_missing_file(self):
        self.assertEqual(Settings.load(self.path), Settings())

    def test_default_is_ctrl_win(self):
        self.assertEqual(Settings().hotkey, "ctrl_win")

    def test_previous_default_migrates_to_ctrl_win(self):
        self.path.write_text('{"hotkey": "ctrl_alt_space", "language": "en"}')
        settings = Settings.load(self.path)
        self.assertEqual(settings.hotkey, "ctrl_win")
        self.assertEqual(settings.language, "en")

    def test_round_trip_unicode(self):
        expected = Settings(mode="cloud", local_model="medium", language="ar", hotkey="ctrl_alt_f9", device_name="میکروفون", auto_paste=False, persian_vocabulary="نام محصول، آوانگار")
        expected.save(self.path)
        self.assertEqual(Settings.load(self.path), expected)
        self.assertFalse(self.path.with_suffix(".tmp").exists())

    def test_local_settings_migrate_without_cloud_consent(self):
        self.path.write_text('{"model": "small", "language": "fa"}')
        settings = Settings.load(self.path)
        self.assertEqual(settings.mode, "local")
        self.assertEqual(settings.local_model, "small")
        self.assertFalse(settings.cloud_consent)
        self.assertEqual(settings.persian_vocabulary, "آوانگار")

    def test_old_medium_selection_survives_upgrade(self):
        self.path.write_text('{"model": "medium", "language": "fa"}')
        settings = Settings.load(self.path)
        self.assertEqual(settings.mode, "local")
        self.assertEqual(settings.local_model, "medium")

    def test_cloud_20_settings_stay_cloud(self):
        self.path.write_text('{"cloud_consent": true, "language": "fa"}')
        settings = Settings.load(self.path)
        self.assertEqual(settings.mode, "cloud")
        self.assertTrue(settings.cloud_consent)

    def test_explicit_local_choice_wins_over_remembered_cloud_consent(self):
        Settings(mode="local", local_model="medium", cloud_consent=True).save(self.path)
        restored = Settings.load(self.path)
        self.assertEqual(restored.mode, "local")
        self.assertEqual(restored.local_model, "medium")

    def test_blank_vocabulary_survives_reload(self):
        Settings(persian_vocabulary="").save(self.path)
        self.assertEqual(Settings.load(self.path).persian_vocabulary, "")

    def test_invalid_json_is_safe(self):
        for value in ('{"model":', 'null', '[]', '42', '"foo"'):
            self.path.write_text(value, encoding="utf-8")
            self.assertEqual(Settings.load(self.path), Settings())

    def test_unknown_and_wrong_types_are_rejected(self):
        self.path.write_text(json.dumps({"model": "large", "language": 42, "hotkey": "nope", "device_name": [], "auto_paste": "false", "extra": True}))
        self.assertEqual(Settings.load(self.path), Settings())

    def test_unhashable_setting_is_safe(self):
        self.path.write_text('{"model": []}')
        self.assertEqual(Settings.load(self.path), Settings())


class InsertionGuardTests(unittest.TestCase):
    def test_only_same_external_window_and_enabled_is_allowed(self):
        self.assertTrue(can_insert(10, 10, False, True))
        for target, current, own, enabled in [(0, 0, False, True), (10, 11, False, True), (10, 10, True, True), (10, 10, False, False)]:
            with self.subTest(target=target, current=current, own=own, enabled=enabled):
                self.assertFalse(can_insert(target, current, own, enabled))
