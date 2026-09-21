"""State machine for a bare Ctrl+Win gesture, independent of Win32/Qt.

A gesture fires once when BOTH modifiers have been released. Other keys cancel
it, so normal Windows shortcuts such as Ctrl+Win+D keep their usual meaning.
"""
CTRL_KEYS = frozenset((0x11, 0xA2, 0xA3))
WIN_KEYS = frozenset((0x5B, 0x5C))
CHORD_KEYS = CTRL_KEYS | WIN_KEYS


class ModifierChord:
    def __init__(self, held=()):
        self.held = set(held)
        self.armed = False
        self.blocked = bool(self.held)

    def feed(self, key: int, down: bool) -> tuple[bool, bool]:
        """Return (mask_start_menu, activate). Repeated keydown never repeats."""
        if down:
            if key in self.held:
                return False, False
            self.held.add(key)
            if key not in CHORD_KEYS:
                self.blocked = True
        else:
            if key not in self.held:
                return False, False
            self.held.remove(key)
        ctrl = bool(self.held & CTRL_KEYS)
        win = bool(self.held & WIN_KEYS)
        mask = False
        if ctrl and win and not self.blocked and not (self.held - CHORD_KEYS):
            mask = not self.armed or (down and key in WIN_KEYS)
            self.armed = True
        if not (self.held & CHORD_KEYS):
            activate = self.armed and not self.blocked
            self.armed = False
            self.blocked = bool(self.held)
            return mask, activate
        return mask, False
