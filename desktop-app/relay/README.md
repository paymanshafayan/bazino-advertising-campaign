# Encrypted marketing relay (private repository only)

This directory is reserved for the **independent Windows marketing desktop app** and its agent companion. It is not the existing browser/portal bridge, is not a GitHub Action, and does not use Issues.

- `desktop-identity.json` — public X25519 encryption **and** Ed25519 reply-signing keys published by the owner's desktop; fingerprint binds both keys.
- `agent-identity.json` — public Ed25519 signing key published by the agent companion. Pair only after the owner compares its fingerprint with the agent's independently communicated output.
- `requests/<UUID>.json` — short-lived, authenticated encrypted operation. No API credentials or plaintext task body are committed.
- `responses/<UUID>.json` — encrypted result to an ephemeral per-request reply key, **signed by the desktop**. The local desktop can prompt before writes.

A commit contains metadata (file name, time, public keys) even though payloads are encrypted. Do not put sensitive identifiers in commit messages or filenames; do not hand-edit a signed request. Runtime files are created only when the owner connects the desktop app and an agent queues a request. Response ciphertext and public keys stay in Git history until a separately approved retention policy is implemented. For setup and threat model, see [../README.md](../README.md).
