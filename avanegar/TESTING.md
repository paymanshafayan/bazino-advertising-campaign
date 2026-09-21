# Validation

## Automated (no model download / microphone required)

From `avanegar/`, after installing requirements:

```sh
python -m unittest discover -s tests -v
```

Tests cover defensive settings parsing, UTF-8 settings persistence, insertion guards, bounded audio buffering, microphone selection/fallback, worker model reuse and errors, UI recording state transitions, clipboard and focus guards, and the Win32 INPUT layout on Windows. Qt tests use the offscreen platform. Model/recorder behavior is mocked where hardware or large model files would otherwise be needed.

## Manual acceptance on Windows 10/11 x64 — required before release

- [ ] Launch from source and from the unpacked PyInstaller directory; no Python on the latter machine.
- [ ] First model download succeeds; repeat with network disabled after download; Persian recognition works.
- [ ] Speak a Persian sentence including a name and a number; review accuracy, do not assume perfect transcription.
- [ ] `Ctrl+Win` starts on release of both keys; a second gesture stops. Holding/repeating does not retrigger. Test both press orders and left/right modifier keys; Start must not open.
- [ ] `Ctrl+Win+D` and other three-key Windows shortcuts do not toggle recording. Standalone Ctrl/Win and typing still work normally.
- [ ] Notepad, Word, a browser textarea and a messenger: caret stays in place and text is pasted once.
- [ ] Changing active window while recording or processing prevents paste into the new window.
- [ ] Holding Ctrl/Alt when conversion completes defers paste, then times out safely.
- [ ] Auto-paste disabled leaves clipboard unchanged; recording from the UI does not paste externally.
- [ ] No speech and a very short recording do not paste an empty/previous result.
- [ ] Cancel recording discards audio, releases the microphone, and permits another recording.
- [ ] Denied microphone permission, disconnected saved microphone, and no devices produce readable errors.
- [ ] Reaching five minutes stops recording and starts one conversion.
- [ ] Download/network errors, a missing model and out-of-memory errors return the UI to idle.
- [ ] Hotkey already reserved: show warning, change shortcut, preserve the old shortcut if the new one fails.
- [ ] Close-to-tray works; single/double click restores; tray exit releases hotkey and microphone.
- [ ] Starting a second instance doesn't start a second worker or recorder.
- [ ] Exit during conversion defers exit and does not paste; shutdown doesn't destroy a running QThread.
- [ ] RTL labels and settings fit at 100%, 125%, 150% scaling (1366×768 and larger desktops).
- [ ] Edited text copies/exports as UTF-8; history holds at most 10 items; clear and full exit remove session text.
- [ ] Normal permission process does not unexpectedly inject into elevated windows. Manual copy is available.

Do not claim these manual checks passed based solely on mocked unit tests or the packaging smoke test.

## This development session (2026-09-21)

- Linux: 37 unit tests passed. The Qt GUI test module was skipped because the runner lacks system `libGL`; 6 Win32-only tests were also skipped. Audio hardware is mocked on this runner because PortAudio is unavailable.
- Static checks: Ruff and Python compileall passed.
- Windows packaging was not run: GitHub rejected the branch push because the connected GitHub App lacks `workflows` permission. No executable has been produced or verified in this session.
- Use `build-windows.bat` locally on Windows, or manually copy `avanegar/workflows/avanegar-windows.yml` to `.github/workflows/avanegar-windows.yml` in GitHub on the branch with the application code. The active workflow was removed from the unpublished commit before pushing application code, so the user can create that one file without changing the Arena integration permissions. The microphone, GUI, recognition quality and real cross-application insertion still need Windows acceptance testing.
