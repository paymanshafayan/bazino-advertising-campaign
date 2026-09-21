import unittest

from avanegar.vocabulary import DEFAULT_VOCABULARY, MAX_VOCABULARY_LENGTH, normalize_vocabulary


class VocabularyTests(unittest.TestCase):
    def test_normalize_separators_and_deduplicate(self):
        self.assertEqual(normalize_vocabulary(" آوانگار, نیم‌فاصله\nآوانگار؛  نام محصول ".replace("؛", ";")), "آوانگار، نیم‌فاصله، نام محصول")

    def test_blank_is_an_explicit_opt_out(self):
        self.assertEqual(normalize_vocabulary(" ,، \n"), "")

    def test_invalid_types_use_default(self):
        for value in (None, 10, [], {}):
            self.assertEqual(normalize_vocabulary(value), DEFAULT_VOCABULARY)

    def test_bounds_and_control_characters(self):
        text = normalize_vocabulary("الف\u202e\x00، " + "ب" * 1000)
        self.assertNotIn("\u202e", text)
        self.assertNotIn("\x00", text)
        self.assertLessEqual(len(text), MAX_VOCABULARY_LENGTH)
