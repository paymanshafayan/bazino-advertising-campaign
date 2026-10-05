# BAZINO PROJECT — STATUS BRIEF FOR THE AGENT (EN)

Agent copy of `HANDOFF.md` (naming law 2026-10-05: owner doc name minus language suffix + `-prompt`).
**This file = status & changes only. Rules live in `project-guide-prompt.md`. Content-production rules live in `Doc/daily-content-production-guide-prompt.md`.**

## 0. READ ORDER BEFORE ANY WORK
1. This file (status) → 2. `project-guide-prompt.md` (rules) → 3. before content work: `Doc/daily-content-production-guide-prompt.md`.
4. Re-read the Persian `HANDOFF.md` and the relevant plan document before starting (owner law 2026-10-04). Never redo finished work.

## 1. CURRENT STATUS (one look)
- **Installed app:** **0.9.0** (`marketing-app-dev-73`) on the owner's PC, `listening` since `2026-10-04 19:10:20Z`. Full record: `HANDOFF.md` §5.
- **Publishing:** all **five approved items of 2026-10-04 are really published** (verified live in Zernio). Story-2 (21:20 Cyprus) landed correctly in **Stories**. The old `failed/` records were **false failures** and were deliberately left untouched (owner decision).
- **Queue:** `publish-queue/ready/` holds 7 cards = 5 already-fired + 2 unapproved (GTA nostalgia 19:30, couples 21:30). `published/` is empty. **A card in `ready/` ≠ unpublished; a record in `failed/` ≠ real failure. Always verify in Zernio.**
- **Agent connection:** fingerprint `8d49:3d90:95fb:d8a9` approved in the app; command mailbox active on this branch.
- **Awaiting owner's "start":** logo audit + stamping of already-published posts (law L10/§8.3) · review of the seven Jinus UI mockups in `jinus-ui-designs/` · Ootto login + Instagram connect (plan 8) · owner to drop the design reference image into the repo.
- **Last doc change:** 2026-10-05 — content guide rewritten as one playbook; HANDOFF/project-guide split; agent-copy naming law registered.

## 2. WHAT HAS BEEN DONE (newest first, one line each)
**2026-10-05 (docs only, no code):** HANDOFF split from the new project guide; both cleaned of duplicates and re-ordered by priority; agent copies created (`HANDOFF-prompt.md`, `project-guide-prompt.md`); content-guide agent copy renamed to `Doc/daily-content-production-guide-prompt.md`; content guide rewritten (laws L1–L20, QC 20 gates, card template, traps, templates); naming law registered; seven Jinus UI mockups regenerated with the image model (awaiting review).
**2026-10-04:** story-flag fix (plan 9) built, released, installed, and **acceptance green** (story-2 published as a real Story at 21:20 Cyprus) · false-failure fix (plan 10) built, released and installed as **0.9.0/dev-73**; live Zernio check proved all five approved items published · 0.8.9/dev-67 built on the clean 0.8.5 base + story fix · plan 6 phases 1–8 completed (0.8.5/dev-57) · CI screenshot-render crash diagnosed (stale files from the other agent's repo) and fixed · logo law (§8.3), Cyprus-time law (§8.2) and method A (§8.1) registered in the content guide · new agent identity `8d49…` approved · UTF-8 sync damage from the campaign repo repaired and a prevention note written.
**2026-10-03:** product named **Jinus** across 13 docs · plans 4 and 5 completed and verified with real data (0.8.3) · seven content laws + QC recorded · professional Instagram skill distilled from OpenMontage · Insights/Task root cause found (invalid `<AllowStartIfOnBatteries>` node) and fixed on branch — **still unproven until a new build is installed** · read-only mailbox session (11 `read` commands) · 0.8.2 install attempt documented.
**Earlier:** app built in `marketing-app/` (six sections), queue/preview/approval, Instagram + extra destinations, DM automation and one-way Affiliate→Portal registration, Insights and Task, English user guide, content guide, plan register.

## 3. REMAINING WORK (safe order)
1. **Re-read live state before any action:** Zernio connection, destination account, permissions, media — the 2026-10-02/03 lists are dated.
2. **Portal:** ingest token, default account/campaign and media acceptance still unverified. Never POST test data to `POST /api/integrations/instagram/published-media`. Keep the Portal token separate from the Zernio key.
3. **DM / follow gate:** code alone proves nothing; verify behaviour during live work. `whenUnknown=verify` covers one verification pass; automatic re-check until follow is not proven.
4. **Insights & Task:** fix is on branch but **unproven** until a green Windows build is installed and run; evidence = `instagram-insights` log records (`schedule`/`startup-check`/`daily-run`) and `marketing-app-mailbox/insights/daily/YYYY-MM-DD.json`.
5. **Publishing:** re-verify card/media/account/time/location for each item; never auto-repeat an unknown outcome.
6. **2026-10-02 content pack:** historical snapshot, not an approved queue; re-verify every news claim before any use.
7. **Logo audit + stamping (law L10/§8.3):** awaiting "start".
8. **Jinus UI mockups review** in `jinus-ui-designs/`; owner should drop the reference image into the repo for the next round.
9. **Plan 8 (Ootto):** owner login + Instagram connect.

## 4. PLAN REGISTER (full statuses: `plans/README.md`)
1 queue/approval/scheduling/Zernio/location/Insights → built, 0.8.1 installed · 2 DM→Zernio, Affiliate Reel→Portal → built and installed · 3 daily report & UI, name "Jinus" → approved, in progress · 4 Instagram-style review → **done** (0.8.3) · 5 whitebox fun reel → **done** · 6 phased release → phases 1–8 done (0.8.5) · 7 Saved-collection archive → **cancelled by owner** · 8 Ootto connector → in progress (owner login pending) · 9 story publish fix → **done, acceptance green** · 10 false-failure fix → **done, released, installed (0.9.0)** · 11 content playbook → **done** (2026-10-05).

## 5. VERSIONS — NEVER INSTALL A BROKEN BASE
- Installed: **0.9.0 / `marketing-app-dev-73`**, exe 62,344,152 B, sha256 `946096f828a81703d1e9d393fe6531a6a2e957ea127e9cee40871af6db7bc4ab`.
- **Invalid/never install:** 0.8.2 (`dev-47`), and 0.8.6–0.8.8 (wrong base). 0.8.3 (`dev-53`) and 0.8.4 (`dev-56`) are superseded; 0.8.5 (`dev-57`, commit `cc9c01422`) is the correct base; 0.8.9 (`dev-67`) is superseded by 0.9.0.
- The portal repo is the **version source of truth**; no sync with the other agent's repo.

## 6. KEY PATHS
`publish-queue/{ready,approved,attempted,published,failed,feedback}/` · media `publish-queue/media/` · `marketing-app-mailbox/` (mailbox, `state.json`, identities) · `plans/` (plan register) · `استودیوی تبلیغات و بازاریابی/Doc/` (guides) · `jinus-ui-designs/` (UI mockups).

## 7. REPORTING TO THE OWNER
Plain fluent Persian, no jargon, four fixed parts: **1) what happened · 2) next plan (awaiting "start") · 3) what didn't and why · 4) my next step**; final line with the stored record id — only after confirming the commit reached the repo.
