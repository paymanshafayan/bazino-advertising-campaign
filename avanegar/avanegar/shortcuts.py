"""Push-to-talk key state machine. No typed characters are stored."""
CTRL_KEYS = frozenset((0x11, 0xA2, 0xA3))
WIN_KEYS = frozenset((0x5B, 0x5C))
SHIFT_KEYS = frozenset((0x10, 0xA0, 0xA1))
ALT_KEYS = frozenset((0x12, 0xA4, 0xA5))
GROUPS = {
    "ctrl_win": (CTRL_KEYS, WIN_KEYS),
    "ctrl_shift_space": (CTRL_KEYS, SHIFT_KEYS, frozenset((0x20,))),
    "ctrl_alt_f9": (CTRL_KEYS, ALT_KEYS, frozenset((0x78,))),
}


class HoldShortcut:
    def __init__(self, name="ctrl_win", held=()):
        self.groups = GROUPS[name]
        self.allowed = frozenset().union(*self.groups)
        self.held = set(held)
        self.active = False
        self.used = bool(self.held)

    def feed(self, key: int, down: bool) -> tuple[bool, str | None]:
        """Start on the completed chord; stop on first required-key release.

        Additional keys cancel/discard an active recording (e.g. Ctrl+Win+D).
        A gesture cannot rearm until all its keys have been released.
        """
        if down:
            if key in self.held:
                return False, None
            self.held.add(key)
        else:
            if key not in self.held:
                return False, None
            self.held.remove(key)
        complete = all(self.held & group for group in self.groups)
        extra = bool(self.held - self.allowed)
        action = None
        if self.active:
            if extra:
                self.active = False
                action = "cancel"
            elif not complete:
                self.active = False
                action = "stop"
        elif complete and not extra and not self.used:
            self.active = True
            self.used = True
            action = "start"
        if extra:
            self.used = True
        if not (self.held & self.allowed):
            self.used = bool(self.held)
        return action == "start" and bool(self.allowed & WIN_KEYS), action
