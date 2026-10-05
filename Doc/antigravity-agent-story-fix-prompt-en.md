<div dir="rtl" align="right">

# Ready-to-paste prompt for the Antigravity agent — fix Instagram Story publishing

> Paste the block below to the agent as-is. Scope is **this one defect only**; its new UI work and the logo work stay untouched.

---

## Owner instruction (2026-10-04) — Story is being published as a feed post

A real defect hit the Bazino Instagram page tonight. **Stories are being published as ordinary feed posts instead of Stories.** This is a small, bounded fix. Do only this; do not stop or change your new UI work.

### 1) Recorded evidence
- An approved queue item (a Story) attempted publishing at 18:00 North-Cyprus time; the app fired the request and 10 seconds later wrote a **failure** record ("Zernio did not confirm final Instagram success"), yet the image actually landed **on the page feed**.
- Code path: `marketing-app/src/BazinoMarketing.Core/Publishing/PublishQueueModels.cs` → class `ZernioPublishBuilder` → method `BuildInstagramPost`.
- Defect: for `Kind == "story"` the code only omits `locationId` and `content` (caption); it **never sends any Story flag to Zernio**, so Zernio builds a normal feed post.

### 2) Official Zernio contract (source: docs.zernio.com/platforms/instagram)
A Story is published only when this flag is present; omitting it means "feed post":

```json
{ "platform": "instagram", "accountId": "...", "platformSpecificData": { "contentType": "story" } }
```

### 3) Required change (minimal and closed)
1. In `BuildInstagramPost`, for Story items add:
   `platform["platformSpecificData"] = new JsonObject { ["contentType"] = "story" };`
2. Preserve the owner's recorded Story exception: **no `locationId`** and **no caption** (`content`).
3. Behavior of `post` / `carousel` / `reel` must change **exactly zero** — same body, same location, same caption handling.
4. Do not touch the queue model, validation, the `ready/approved/published/failed` folders, media files, or the UI.
5. Do not publish anything, build a release, or cut a version without the owner's explicit approval.

### 4) Mandatory regression test
Add a test in `tests/BazinoMarketing.Core.Tests/PublishQueueTests.cs`:
- **Story** body must contain `platformSpecificData.contentType == "story"`, and must contain neither `locationId` nor `content`.
- **post / carousel / reel** bodies must NOT contain `contentType == "story"` (today's behavior preserved).
- All existing tests must stay green.

### 5) Evidence to report back
- Diff summary (files + changed lines) and the **full commit SHA**.
- Core test run output (pass/fail).
- If the owner authorizes a build: the `marketing-app-dev-NN` tag plus the EXE SHA-256.
- Explicit confirmation that configuration, the publish queue files, and media were left untouched.

### 6) Working rules (mandatory)
- Work only on your own branch; no force-push, no history rewrite, no `git add -A`.
- Never write secrets (Zernio key, portal token, agent fingerprint) into code, queue, reports, or logs.
- One single-purpose commit, e.g.:
  `fix(publish): send platformSpecificData.contentType=story for stories (owner 2026-10-04)`
- **Two-repo sync:** the app source exists in two repositories. Make the change in your copy, and report the exact file list plus commit SHA so the other copy (portal repo, branch `arena/01a10048-bazino-gamenet-portal`) can be kept byte-identical; if you have write access to that repo, mirror it yourself and say so explicitly.
- **Version number:** the repo's `<Version>` is `0.8.4` while the installed app reports `0.8.5`. If you bump the version, explain this mismatch and make the new number consistent with reality. Never re-install old builds.
- **Out of scope:** the official-logo-on-content law (guide section 8.3) and your new UI work.

### 7) Acceptance criteria
Every Story must land in the Instagram Stories area (not the feed) and Zernio's response must report `published`; the regression test must guarantee this, and no other media format may change behavior.
