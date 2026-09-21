import unittest

from avanegar.shortcuts import HoldShortcut


class HoldShortcutTests(unittest.TestCase):
    def play(self, events, name="ctrl_win"):
        chord = HoldShortcut(name)
        return [chord.feed(key, down) for key, down in events]

    def test_both_orders_both_sides_start_on_press_stop_on_first_release(self):
        for ctrl in (0xA2, 0xA3):
            for win in (0x5B, 0x5C):
                for first, second in ((ctrl, win), (win, ctrl)):
                    for release1, release2 in ((first, second), (second, first)):
                        with self.subTest(first=first, release1=release1):
                            self.assertEqual(self.play([(first, True), (second, True), (release1, False), (release2, False)]),
                                             [(False, None), (True, "start"), (False, "stop"), (False, None)])

    def test_repeat_does_not_retrigger(self):
        results = self.play([(0xA2, True), (0x5B, True)] + [(0x5B, True)] * 20 + [(0x5B, False), (0xA2, False)])
        self.assertEqual([action for _, action in results if action], ["start", "stop"])

    def test_two_holds_produce_two_sessions(self):
        gesture = [(0xA2, True), (0x5B, True), (0x5B, False), (0xA2, False)]
        self.assertEqual([action for _, action in self.play(gesture * 2) if action], ["start", "stop", "start", "stop"])

    def test_extra_key_cancels_and_does_not_transcribe(self):
        for extra in (0x44, 0x25, 0x20, 0xA4, 0xA0):
            results = self.play([(0x5B, True), (0xA2, True), (extra, True), (extra, False), (0xA2, False), (0x5B, False)])
            self.assertEqual([action for _, action in results if action], ["start", "cancel"])

    def test_extra_modifier_already_held_prevents_start(self):
        result = self.play([(0xA0, True), (0xA2, True), (0x5B, True), (0xA0, False), (0xA2, False), (0x5B, False)])
        self.assertFalse(any(action for _, action in result))

    def test_single_key_does_nothing(self):
        for key in (0xA2, 0xA3, 0x5B, 0x5C):
            self.assertEqual(self.play([(key, True), (key, False)]), [(False, None), (False, None)])

    def test_unknown_release(self):
        self.assertEqual(HoldShortcut().feed(0x5B, False), (False, None))

    def test_typing_before_chord_is_not_retained(self):
        result = self.play([(0x41, True), (0x41, False), (0xA2, True), (0x5B, True), (0xA2, False), (0x5B, False)])
        self.assertEqual([action for _, action in result if action], ["start", "stop"])

    def test_initially_held_keys_must_first_be_released(self):
        chord = HoldShortcut(held=[0xA2])
        self.assertEqual(chord.feed(0x5B, True), (False, None))
        chord.feed(0xA2, False)
        chord.feed(0x5B, False)
        chord.feed(0xA2, True)
        self.assertEqual(chord.feed(0x5B, True), (True, "start"))

    def test_no_rearm_until_entire_gesture_released(self):
        results = self.play([(0xA2, True), (0x5B, True), (0x5B, False), (0x5B, True), (0x5B, False), (0xA2, False)])
        self.assertEqual([action for _, action in results if action], ["start", "stop"])

    def test_alternative_hotkeys_are_also_hold_to_talk(self):
        for name, keys in [("ctrl_shift_space", (0xA2, 0xA0, 0x20)), ("ctrl_alt_f9", (0xA3, 0xA5, 0x78))]:
            events = [(key, True) for key in keys] + [(key, False) for key in reversed(keys)]
            result = self.play(events, name)
            self.assertEqual([action for _, action in result if action], ["start", "stop"])
            self.assertFalse(any(mask for mask, _ in result))
