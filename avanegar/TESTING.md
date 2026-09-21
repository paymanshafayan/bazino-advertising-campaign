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
- [ ] `Ctrl+Win` starts when both keys are held; releasing either required key stops and transcribes once. Holding/repeating does not retrigger. Test both press orders and left/right modifier keys; Start must not open.
- [ ] `Ctrl+Win+D` cancels/discards any recording started by that gesture, without transcription/paste. Standalone Ctrl/Win and typing still work normally.
- [ ] Notepad, Word, a browser textarea and a messenger: caret stays in place and text is pasted once.
- [ ] Changing active window while recording or processing prevents paste into the new window.
- [ ] Holding Ctrl/Alt when conversion completes defers paste, then times out safely.
- [ ] Auto-paste disabled leaves clipboard unchanged; recording from the UI does not paste externally.
- [ ] No speech and a very short recording do not paste an empty/previous result.
- [ ] Release during slow microphone opening is remembered; it must not get stuck recording.
- [ ] Simulate a slow/stalled driver close: UI navigation and Log copy remain responsive; watchdog fires after 10s and late audio is discarded.
- [ ] Hold during conversion is ignored; its release cannot stop a button-owned recording.
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
- [ ] Log page: copy includes traceback/stage/version, persists recent entries after restart, omits transcript/audio/clipboard, and rotates at ~1MB × 3.
- [ ] Microphone help has separate Windows 10/11 desktop-access instructions and opens the settings URI.
- [ ] Normal permission process does not unexpectedly inject into elevated windows. Manual copy is available.

Do not claim these manual checks passed based solely on mocked unit tests or the packaging smoke test.

## This development session (2026-09-21)

- Linux: 37 unit tests passed. The Qt GUI test module was skipped because the runner lacks system `libGL`; 6 Win32-only tests were also skipped. Audio hardware is mocked on this runner because PortAudio is unavailable.
- Static checks: Ruff and Python compileall passed.
- Windows packaging was not run: GitHub rejected the branch push because the connected GitHub App lacks `workflows` permission. No executable has been produced or verified in this session.
- Use `build-windows.bat` locally on Windows, or manually copy `avanegar/workflows/avanegar-windows.yml` to `.github/workflows/avanegar-windows.yml` in GitHub on the branch with the application code. The active workflow was removed from the unpublished commit before pushing application code, so the user can create that one file without changing the Arena integration permissions. The microphone, GUI, recognition quality and real cross-application insertion still need Windows acceptance testing.

## Version 1.1.0 regression coverage

- Push-to-talk: all left/right combinations and press/release orders, repeat suppression, alternatives, third-key cancellation, and rearming.
- Asynchronous audio: native operations run off the GUI thread; a gated slow close leaves Qt heartbeat timers active; capture-stop is immediate; release during opening is serialized.
- UI: starting/stopping states, release-before-open race, watchdog/late-result discard, session ownership, and copied logs containing no transcript.
- Diagnostics: traceback capture, user-home/URL-secret redaction, ring bounds, prior-session persistence, and in-memory fallback when file creation fails.
- Native Windows: no SendInput/Qt/audio callbacks inline in the low-level keyboard hook; actions are drained outside it.

The reported hardware-specific freeze has not been reproduced on the user's microphone here. These tests cover the blocking path and races, but the new build still needs a real-device hold/release test.

## Version 1.2.0 — Persian recognition controls

- Persian-only optional `hotwords` hints, bounded/normalized with explicit opt-out and backward-compatible settings.
- The Persian/Medium preset only changes the form until the user saves/prepares it. Existing model selection is not silently migrated; no extra model download is initiated by the preset itself.
- Tests cover hint plumbing through UI/worker, no hints in English/Arabic/auto, no canned correction of the reported sentence, no output invented by application code for an empty decoder result, and no vocabulary/transcript in logs.
- Numeric RMS/peak/clipping diagnostics are not recognition-quality scores; neither automatic gain nor a guessed transcription is applied.
- No real user recording was supplied for this change. Unit tests use mocked model output and do NOT establish a reduction in Persian word-error rate. Compare multiple real spoken sentences on the user's microphone with Small versus Medium and with hints on/off.
