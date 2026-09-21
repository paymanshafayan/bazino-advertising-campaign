# Avanegar 2.1 validation

Run `python -m unittest discover -s tests -v` inside `avanegar/` after installing requirements. Cloud requests are mocked; tests never use real API keys or bill an account.

## Automated coverage

- Settings migration preserves old local model selection and 2.0 cloud users; explicit mode selection persists; secrets are not part of the settings schema.
- Consent/key gate prevents network calls; blank/too-short/nonfinite recordings are not uploaded.
- Request format: fixed HTTPS OpenAI endpoint, 16kHz mono WAV, Persian prompt, automatic-language omission, no redirects or disabled TLS verification.
- At most one retry for connection establishment timeout / short 429 rate limiting; quota errors, read timeout, connection loss, 5xx, auth, TLS and malformed response are not automatically replayed.
- Session ownership and thread safety: slow close leaves Qt heartbeat timers running; release during open/recovery is respected; a cancelled gesture never restarts recording or uploads.
- Recovery: one rescan/default-input retry after a known device-open failure; opt-out; exhaustion is terminal; zero signal triggers rediscovery with no reopening/upload; PortAudio refresh only happens after stream closure.
- Key storage: validation, encrypted-file use, deletion, no plaintext fallback; actual user-bound DPAPI round-trip on the Windows runner with a fabricated test key.
- UI: API consent/key required, secret/vocabulary absent from logs, empty result cannot repeat old text, no tray `showMessage`, asynchronous device scans.
- Existing hold-to-talk, foreground paste guards, log rotation/redaction, Unicode settings, export/history tests remain.

Linux may skip native Qt GUI/Win32/DPAPI tests when system libraries are absent. Windows CI exercises those tests and packages/launches the frozen executable, but does not access a microphone or call the paid API.

## Manual Windows acceptance required

- [ ] Exit the old version from the tray; launch 2.0 from the unpacked artifact on a machine without Python.
- [ ] Both modes are selectable and persist. Active-mode badge/privacy messages match the saved selection; unsaved form changes do not reroute audio.
- [ ] Put an actual previously downloaded Small model in the existing `%LOCALAPPDATA%/Avanegar/models` cache. Disconnect the internet, select local Small, prepare/transcribe successfully with no API key or model download.
- [ ] Cloud with an already cached local model still calls only the cloud; local with a saved API key still never uploads audio. Failures do not trigger cross-provider fallback.
- [ ] Missing/incomplete local model: show the cache path and download/complete only that model into the SAME root. Repeating preparation after success must not contact the Hub.
- [ ] Cache inspection is disk-only, runs on the worker, accepts prior Hub snapshots, and does not count a partial download as complete.
- [ ] Save a valid account API key in the masked field; confirm ciphertext file and no key in settings or Log; restart and verify key restoration/deletion.
- [ ] Cloud consent off blocks cloud microphone/upload; local recording works without consent or a key. Connection test uses no audio and does not promise quota availability.
- [ ] With consent/credit, hold Ctrl+Win, speak Persian, release; upload occurs once and text pastes only in the unchanged target window.
- [ ] Test invalid/revoked key, insufficient quota, internet loss and rate limiting; errors stay in-app/Log with no toast/sound.
- [ ] Disconnect selected microphone: one rescan/default-device retry, with an in-app explanation and no infinite loop.
- [ ] Release keys during rescan: no recording starts afterward. Cancelled/empty audio is not sent.
- [ ] Mute/no signal: one rediscovery and prompt to speak again; lost speech is not fabricated/recovered.
- [ ] Disable automatic recovery: no device reinitialization/default-input fallback after failure.
- [ ] Deny desktop microphone permission: bounded failure; the app does not try to grant itself permission or elevate.
- [ ] Simulate a stuck driver: after 10 seconds Log copy/navigation remain available; no concurrent stream reinitialization.
- [ ] Start/stop, ready text, errors and close-to-tray send **zero app notifications**. The Windows microphone privacy indicator may still appear.
- [ ] Confirm account/region service availability, actual cost and data-retention policy before sending sensitive recordings.

API accuracy and live integration cannot be certified by mocked tests. Review Persian names/numbers before sending the transcript.

## Local-cache regression tests

- Prior Hub snapshot with refs/main; absent refs; incomplete latest ref but complete older snapshot; explicitly named exported model directories; different model isolation.
- Empty weights/JSON/vocabulary, malformed JSON and `.incomplete` files aren't considered ready.
- A cache hit causes zero download/update/network calls. Missing models invoke the downloader only with the exact supplied `Avanegar/models` root; a second resolve after successful download is offline.
- Local CPU runtime receives a directory path and `local_files_only=True`; repeated preparation reuses the in-memory model. Tokenizer must be present to avoid the runtime's remote-tokenizer fallback.
- Local paths and synthetic cache files test routing, NOT the integrity/accuracy of a user's actual weight files. Real offline acceptance still needs their Windows cache.
