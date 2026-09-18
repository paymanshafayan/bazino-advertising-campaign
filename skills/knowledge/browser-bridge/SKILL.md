---
name: knowledge-browser-bridge
description: >-
  Knowledge skill for connecting the sandbox agent to the owner's real Chrome.
  Covers why the current PowerShell + arena.site relay fails, the measured
  constraints of this sandbox, and the GitHub-as-transport + Chrome-extension
  architecture that removes both the copy-paste step and the VPN requirement.
  Read before any work on the browser bridge.
license: Original research. Sources cited in section 9.
---

# Browser Bridge — connecting the agent to the owner's Chrome

> **📚 Knowledge skill** — part of `skills/knowledge/` (§32).
> **Mission:** browser bridge setup and operation
> **Last researched:** 2026-09-18
> **Staleness check:** monthly, per the nightly routine in `HANDOFF.md`.

---

# 1. The problem being solved

The agent runs in a Linux sandbox. The owner's Chrome runs on Windows in North
Cyprus, logged into the Bazino Google account. The agent needs to drive that
Chrome to register the business in directories, edit the Google profile, and
fill forms.

## 1.1 What the current system costs

| Pain | Detail |
|---|---|
| **Copy-paste every session** | ~70 lines of PowerShell pasted by hand, every time |
| **VPN required** | `sbx-*.arena.site` is blocked on the owner's network |
| **Base URL changes** | Every sandbox reset produces a new hostname |
| **Fragile** | The window must stay open; a click inside it freezes the loop (QuickEdit) |
| **Reset-prone** | The sandbox reset three times in one day, losing the relay each time |

---

# 2. Measured constraints — verified 2026-09-18

Do not re-derive these. They were measured, not assumed.

## 2.1 Sandbox egress whitelist

```
https://api.github.com               → 200  ✅
https://github.com                   → 200  ✅
https://registry.npmjs.org           → 200  ✅
https://raw.githubusercontent.com    → exit 35  ❌
https://gist.githubusercontent.com   → exit 35  ❌
https://cloudflare.com               → exit 35  ❌
https://ngrok.com                    → exit 35  ❌
```

**`api.github.com` is reachable.** This is the finding everything else rests on.
Note that `raw.githubusercontent.com` is **not** — content must be read through
the API, not the raw CDN.

## 2.2 GitHub as a transport — measured

| Operation | Latency |
|---|---|
| `git push` from sandbox | ~1.2 s |
| `gh api` read of that content | ~0.36 s |
| Rate limit | 5200 requests/hour |

At a 2-second poll interval, 1800 polls/hour — comfortably inside the limit.

## 2.3 Token capability

The sandbox token is `arena-ai-coding-agent[bot]`.
`repos/.../permissions.push` reports **false**, but `git push` over HTTPS
**works**. Writes must go through `git push`, not the contents API.

## 2.4 Why the VPN was really needed

`HANDOFF_PROMPT.md` line 510: *"VPN must be on during the bridge — the `sbx`
domain is blocked on some networks."*

The VPN was never a technical requirement of the architecture. It was a
workaround for one blocked domain. **GitHub is not blocked in North Cyprus** —
the owner's `git push` works without VPN. Changing the transport removes the
VPN requirement entirely.

---

# 3. Architectures evaluated

## 3.1 Dead ends — do not retry

| Approach | Why it fails |
|---|---|
| cloudflared / ngrok tunnel | Both domains blocked by sandbox egress |
| Direct `{port}-{id}.e2b.app` | Needs `e2b-traffic-access-token`, browser-only |
| WebSocket through the arena proxy | Frames stall ~30 s or vanish |
| In-browser page bridge to `ws://127.0.0.1` | Mixed-content + Private Network Access |
| Raw GitHub CDN as transport | `raw.githubusercontent.com` blocked |

## 3.2 Current: PowerShell + arena.site HTTP relay

Works, but carries every cost in §1.1. It is the baseline to beat.

## 3.3 Recommended: GitHub transport + Chrome extension

```
Agent (sandbox)                     Owner's Chrome (Windows)
      │                                        │
      │ git push commands ──▶ GitHub ◀── poll every 2s
      │                       branch           │
      │ gh api read  ◀──────  cdp-bus  ──▶ push results
      │                                        │
      └────────────────────────────────  chrome.debugger API
```

**Two independent improvements. They are separable.**

### Improvement A — swap the transport to GitHub

Removes the VPN requirement and the changing base URL. Works with the existing
PowerShell script, minimally edited.

### Improvement B — replace PowerShell with a Chrome extension

Removes the copy-paste entirely. Install once; it runs on browser start.

Uses `chrome.debugger` — the same CDP surface, via the extension API:

```js
await chrome.debugger.attach({ tabId }, "1.3");
const result = await chrome.debugger.sendCommand({ tabId }, "Runtime.evaluate", {
  expression: "location.href"
});
```

**Capabilities confirmed by Chrome docs:**
- Full CDP access: `Runtime`, `Page`, `Input`, `DOM`, `Network`
- Works in the owner's real profile — cookies and logins intact
- No `--remote-debugging-port` flag
- No separate Chrome instance

**Documented limitations:**
- A yellow "being debugged" banner appears. Unavoidable; it is a safety feature.
- Restricted on `chrome://` pages and the Chrome Web Store.
- Unpacked extensions may need Developer Mode re-enabled after Chrome updates.

---

# 4. The transport protocol

A dedicated orphan branch `cdp-bus` in the existing private repo.

```
cdp-bus/
├── cmd/<seq>.json      ← agent writes, extension reads
├── res/<seq>.json      ← extension writes, agent reads
└── status.json         ← heartbeat
```

Orphan branch so the bus never pollutes project history, and the noise can be
deleted wholesale.

## 4.1 Why polling, not push

GitHub webhooks need a public listener. The sandbox has no inbound route. The
extension polls; 2-second latency is acceptable for form-filling work.

## 4.2 Latency comparison

| Path | Round trip |
|---|---|
| Current relay (when VPN is up) | 1–3 s |
| GitHub transport | 2–4 s |

Slightly slower, but it does not require a VPN and cannot break on a base-URL
change.

---

# 5. Security

⚠️ **Read before implementing.**

| Concern | Control |
|---|---|
| Repo holds browser commands | Repo is **private** (verified 2026-09-18) |
| Extension has broad access | Owner-installed, owner can remove; banner always visible |
| Commands visible in git history | `cdp-bus` is orphan; can be force-deleted |
| Credential exposure | Never write credentials into bus payloads |
| Kill switch | Owner disables the extension, or deletes the branch |

The existing relay had a pairing code. The GitHub transport inherits repo
authentication instead, which is stronger — it is tied to the owner's account.

---

# 6. Chrome extension essentials

## 6.1 Manifest V3 baseline

```json
{
  "manifest_version": 3,
  "name": "Bazino Agent Bridge",
  "version": "1.0",
  "permissions": ["debugger", "storage", "alarms"],
  "host_permissions": ["https://api.github.com/*"],
  "background": { "service_worker": "sw.js" }
}
```

## 6.2 🔴 The service-worker trap

MV3 service workers are **killed after ~30 seconds of inactivity**. A naive
`setInterval` poll dies silently.

**Fix:** `chrome.alarms` with a minimum period of 1 minute, plus a short
`setInterval` inside each wake for sub-minute responsiveness.

This is the single most common failure mode in MV3 bridge projects.

## 6.3 Token handling

The extension needs a GitHub token with write access to the private repo.

- Fine-grained PAT, **contents: read/write on that one repo only**
- Stored in `chrome.storage.local`, entered once via the options page
- Never committed

---

# 7. Comparable projects — GitHub survey, 2026-09-18

Surveyed per §32.1 (GitHub first). None solves our exact problem, because all
assume the agent and the browser share a machine or a LAN.

| Project | Approach | Why it does not fit |
|---|---|---|
| `browserbase/ModCDP` | Extension + CDP routing | Needs a reachable CDP endpoint |
| `whg517/browser-bridge` | Extension + native messaging | Native host is local-only |
| `RobBrautigam/agent-browser-bridge` | Extension + broker | "No network listener" — by design local |
| `TNJ2026/browser-agent-bridge` | Extension + Python host | Local HTTP/WS |
| `askalf/browser-bridge` | Containerised Chromium | Not the owner's real browser |
| `chrome-devtools-mcp` (Google) | MCP over CDP | Assumes local Chrome |

**What is reusable:** the `chrome.debugger` extension pattern is well proven
across all of them. **What is novel here:** the transport. Everyone else assumes
localhost; we need to cross a whitelisted-egress boundary.

⚠️ **Chrome 146+** is reported to add a native settings toggle for agent access
(DevTools → Experiments → MCP). If that ships and reaches the owner's Chrome,
re-evaluate — it may remove the need for a custom extension. Check at the next
monthly staleness review.

---

# 8. Recommendation

**Phase 1 — GitHub transport, keep PowerShell.**
Small edit to the existing script: point it at `api.github.com` instead of
`arena.site`. Removes the VPN and the changing base URL. Still needs paste.

**Phase 2 — Chrome extension.**
Removes the paste. Install once.

Phase 1 is low risk and independently useful. Phase 2 depends on Phase 1's
protocol being proven.

---

# 9. Sources

| Source | Contribution |
|---|---|
| `developer.chrome.com/docs/extensions/reference/api/debugger` | `chrome.debugger` API, restricted domains, session handling |
| `github.com/browserbase/ModCDP` | Extension-as-CDP-transport pattern, routing modes |
| `github.com/whg517/browser-bridge` | MV3 + native messaging architecture, security posture |
| `github.com/RobBrautigam/agent-browser-bridge` | Multi-profile broker model |
| `medium.com/@dzianisv` — CDP from extensions | Confirms no Chromium fork needed |
| `agent-browser.dev/cdp-mode` | Auto-connect and `chrome://inspect` remote debugging |
| Direct measurement in this sandbox | §2 — egress, latency, rate limits, token scope |
| `PORTAL_SERVER_AND_BROWSER_BRIDGE.md` §ج-۲ | The eight original blockers |

---

# 10. Changelog

| Date | Change |
|---|---|
| 2026-09-18 | Created. Measured sandbox egress; found `api.github.com` reachable; identified VPN as an `arena.site` workaround rather than an architectural need; surveyed six comparable GitHub projects. |
