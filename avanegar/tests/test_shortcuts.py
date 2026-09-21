import unittest

from avanegar.shortcuts import ModifierChord


class ModifierChordTests(unittest.TestCase):
    def play(self, events):
        chord = ModifierChord()
        return [chord.feed(key, down) for key, down in events]

    def test_all_sides_and_orders_fire_once_only_after_both_released(self):
        for ctrl in (0xA2, 0xA3):
            for win in (0x5B, 0x5C):
                for first, second in ((ctrl, win), (win, ctrl)):
                    for release1, release2 in ((first, second), (second, first)):
                        with self.subTest(ctrl=ctrl, win=win, first=first, release1=release1):
                            self.assertEqual(self.play([(first, True), (second, True), (release1, False), (release2, False)]),
                                             [(False, False), (True, False), (False, False), (False, True)])

    def test_auto_repeat_does_not_retrigger(self):
        result = self.play([(0xA2, True), (0x5B, True)] + [(0x5B, True)] * 10 + [(0x5B, False), (0xA2, False)])
        self.assertEqual(sum(activate for _, activate in result), 1)
        self.assertEqual(sum(mask for mask, _ in result), 1)

    def test_two_gestures_toggle_twice(self):
        gesture = [(0xA2, True), (0x5B, True), (0x5B, False), (0xA2, False)]
        self.assertEqual(sum(activate for _, activate in self.play(gesture * 2)), 2)

    def test_three_key_windows_shortcut_is_not_dictation(self):
        for other in (0x44, 0x25, 0x27, 0x20, 0xA4, 0xA0):
            result = self.play([(0x5B, True), (0xA2, True), (other, True), (other, False), (0xA2, False), (0x5B, False)])
            self.assertFalse(any(activate for _, activate in result))

    def test_other_modifier_already_held_prevents_chord(self):
        result = self.play([(0xA0, True), (0xA2, True), (0x5B, True), (0xA0, False), (0xA2, False), (0x5B, False)])
        self.assertFalse(any(activate for _, activate in result))

    def test_standalone_modifier_does_not_activate_or_mask(self):
        for key in (0xA2, 0xA3, 0x5B, 0x5C):
            self.assertEqual(self.play([(key, True), (key, False)]), [(False, False), (False, False)])

    def test_unknown_release_is_ignored(self):
        self.assertEqual(ModifierChord().feed(0x5B, False), (False, False))

    def test_ordinary_typing_does_not_block_next_gesture(self):
        self.assertEqual(self.play([(0x41, True), (0x41, False), (0xA2, True), (0x5B, True), (0xA2, False), (0x5B, False)])[-1], (False, True))

    def test_held_keys_at_registration_must_be_released_first(self):
        chord = ModifierChord([0xA2])
        self.assertEqual(chord.feed(0x5B, True), (False, False))
        self.assertEqual(chord.feed(0xA2, False), (False, False))
        self.assertEqual(chord.feed(0x5B, False), (False, False))
        chord.feed(0xA2, True)
        chord.feed(0x5B, True)
        chord.feed(0xA2, False)
        self.assertEqual(chord.feed(0x5B, False), (False, True))

    def test_repress_win_while_holding_ctrl_masks_start_again(self):
        result = self.play([(0xA2, True), (0x5B, True), (0x5B, False), (0x5B, True), (0x5B, False), (0xA2, False)])
        self.assertEqual(sum(mask for mask, _ in result), 2)
        self.assertEqual(sum(activate for _, activate in result), 1)
