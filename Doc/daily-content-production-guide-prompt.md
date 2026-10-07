# BAZINO DAILY CONTENT — AGENT OPERATING BRIEF (EN)

**Read this BEFORE any content-production work.** Owner-facing Persian master: `daily-content-production-guide-fa.md`.
Technical queue/publish contract: `../marketing-app/PUBLISHING.md`. Rules below are the owner's; the agent adds none.
If anything is ambiguous → **ask the owner**, never guess. Newer owner instruction always wins.

## 0. NON-NEGOTIABLES
- **Approval:** nothing publishes without per-item owner approval. Preparing/queueing ≠ permission to publish. Scheduler runs **only while the app is open**; item >5 min past due → fails, needs new time + approval.
- **Plan gate:** editing/translating/subtitling/dubbing/cutting/carousel/news-post/story/repurpose = free (no separate plan). **New video generation** = plan + sample + explicit owner permission.
- **Time:** all times **North Cyprus, UTC+03:00**. Card field `timeZoneId = "Turkey Standard Time"`, `publishAt` with `+03:00`. **Nothing after 21:30.** Tehran time forbidden. Card display follows the PC clock (may show −30 min if PC is on Tehran).
- **Secrets:** Zernio key / portal token never in Git, logs, cards, or reports.

## 1. DAILY PIPELINE (10 steps, each with a STOP gate — "no" = go back, never forward)
0. **State:** read `HANDOFF.md`, plan register, repo HEAD. Never redo finished work.
1. **Plan from real data:** read Zernio insights (views, likes, comments, shares); today's topic = yesterday's best-performing pattern.
2. **Source:** headline from **Merlin'in Kazanı**; verify every number/date/claim with a **second source**; **rewrite**, never copy; make own artwork.
3. **Slot:** pick format + language (rotation below) + publish time (≤21:30).
4. **Media:** on-image text is written by the **image model** (method A): exact texts given verbatim + base image as reference.
5. **Brand:** official logo (`public/BazinoLogo.png`) **next to the BAZINO wordmark** (wordmark is never removed); approved fonts only; emoji only in video overlay/caption.
6. **Preview:** show exactly the Instagram frame (4:5 or 9:16) with caption; videos ship with full **transcript**.
7. **Card:** build JSON card per template below; media in `publish-queue/media/`.
8. **Approve & publish:** owner approves each item; app must be open for scheduled send.
9. **Verify & record:** read the real result from Zernio; report with link + media id.

## 2. SLOTS (default; same-day changes need owner approval)
11:00 story 1 (poll/quiz) · 12:00 news post (carousel or video) + 12:30 story 2 reshare · 15:00 didactic reel · 17:30 rotating brand reel + 18:00 story 3 with link · 20:00 brand reel · 21:00 fun reel + 21:30 story 4. Cap: **nothing after 21:30**.

## 3. LANGUAGE ROTATION (5 posts/day: 2 tr · 2 fa · 1 en, no same-language back-to-back)
Day1 fa·tr·en·fa·tr · Day2 tr·fa·tr·en·fa · Day3 fa·tr·fa·tr·en · Day4 en·fa·tr·fa·tr · Day5 tr·en·fa·tr·fa.
Persian content **downloaded from other pages is raw material only** — never reposted as-is for a Persian audience; translate/subtitle for TR/EN or fully re-produce. `language` = `fa|tr|en` and must match reality. For TR slots, prefer converting Persian videos to Turkish.

## 4. CONTENT SOURCES
- **Saved collections** (Daily Reels / Daily Didactic / Daily Game): pick 1 best/day, download, **unsave immediately**. Reels = reverse-engineer (3-sec hook, cut rhythm, core idea) and re-produce in Bazino setting with our characters using **Kling 3.0 Omni Multi-Shot with chained 9:16 keyframes** (`bazino-character-video-recreation-guide-fa.md`); loop ending back to frame 1. Didactic = transcribe, gamer-natural translation, subtitles in safe zone or full character recreation (`bazino-character-video-recreation-guide-fa.md`), save-bait title. Game fun = animation → reproduce with our characters (`bazino-character-video-recreation-guide-fa.md`); text-only → clean translated graphics; dialogue → accurate subtitles.
- **News:** text/image → carousel (slide 1 hook + curiosity loop; slides 2-3 carry the main value; each middle slide = one 2-second point; last = saveable summary + follow CTA). Video news → rhythmic edit + voiceover/subtitles + title bar; end with a binary question.
- **Rotating brand reel (1/day, 3-day cycle):** active tournaments (no unverified prize/terms) · family & safety ("BAZINO SAFE", no "100% safe") · affiliate (**reel only**).
- **Stories:** 1 morning interactive · 2 noon news reshare · 3 evening club vibe + link sticker · 4 night reel boost.

## 5. HARD CONTENT RULES
- Fonts: approved families in `Assets/fonts/` only. **Banned:** Tahoma, Arial, Calibri, Segoe UI. ≤2 families per design, title ≥1.5× body, min 40px at 1080.
- **Method A review:** character-by-character check after every generation — Turkish `ı İ ş ğ ü ö ç`, digits, dates, and brand exactly **BAZINO** (writing **BAZİNO** = reject). Any error → regenerate; only the fully correct render is queued.
- Emoji: only video overlay (1–2 per block) and caption (3–6). **Zero on news/official content.** Never 18+/gambling/weapon/blood/money emoji.
- Logo on **all** published content (all carousel slides, image post, story, reel cover; video = cover + first frame), beside the wordmark, legible, undistorted.
- No watermark/logo of other pages; 3–5 sized hashtags; no engagement bait ("double tap", "tag 3 friends").
- Truth & safety: no price, discount, prize, capacity, commission % or result claim without a fresh official source; no minors with 16+/18+ games; ad/AI disclosure.
- Algorithm signals (ranked): DM send & story share > Save > watch-through/loop > real comments + fast replies.
- Affiliate: **reel only**; portal `media_id` registration is one-way, after a confirmed successful reel.
- Location: `locationId = 1091945074011846` for post/reel/carousel. **Story: never send any location.**

## 6. QC (20 gates — a failed gate sends the item back)
1 unsaved from collection · 2 language matches rotation & card code · 3 single clear goal · 4 no-intro hook, first frame not black · 5 first 125 chars of caption hook · 6 characters match character sheet · 7 subtitles in safe zone, no typos, natural tone · 8 no foreign watermark · 9 3–5 sized hashtags · 10 truth/safety/ad disclosure · 11 owner approval for this item · 12 affiliate = reel · 13 approved font + method A + character-by-character text check (BAZINO) · 14 emoji rules · 15 full transcript attached · 16 downloaded Persian content not reposted raw · 17 news: Merlin + second source + rewritten + own artwork · 18 logo beside wordmark everywhere (video: cover + first frame) · 19 Cyprus time, correct zone, ≤21:30 · 20 correct type: story = 1 media + **empty caption**; reel = 9:16 ≤90 s; carousel = 2–10; no auto-crop.

## 7. CARD TEMPLATE (`marketing-app-mailbox/publish-queue/ready/<id>.json`)
`id, title, mediaFormat(carousel|reel|image|story), contentType(carousel|reels|post|story), topic(gaming-news|daily-didactic|daily-reels|daily-game|active-tournaments|bazino-safe|affiliate-reel|story-game-interaction|story-news-reshare|story-club-live|story-night-boost), guideVersion, topicCycle, contentSlot, language(fa|tr|en), targetPlatform=instagram, targetAccountId=6ab391ba8d284ffb21332381, timeZoneId="Turkey Standard Time", publishAt(+03:00), caption(empty for story), cta, productionStatus="final", previewReviewed=true, media[{path: publish-queue/media/…}], republishToConnectedPlatforms=false`.
Any edit after approval voids it → re-approval required.

## 8. PUBLISH & VERIFY (Zernio)
- `POST /v1/posts` returns **201** (all published) or **207** (partial) — both 2xx. Never decide on "did it throw".
- Per-platform status: `pending|processing|uploading|published|failed|cancelled`. **Only** `failed`/`cancelled` = failure; anything else (even unknown) = awaiting confirmation, never assumed failed.
- Real success = **`platformPostId` / `platformPostUrl`**. App polls `GET /v1/posts/{id}` up to 90 s (or finds the post by `metadata.contentId`); request timeout 3 min. Unresolved → **no failure record**; status stays "outcome unknown, manual check".
- Late resolution: later runs re-read the same post by `metadata.contentId` and write the standard `published/` or `failed/` record — **never re-sends**. Idempotency-Key (24 h) on the same deterministic id prevents duplicates.
- **Story:** `platformSpecificData.contentType = "story"` is mandatory; without it Instagram posts it to the **feed**. Story = no location, Instagram only, no cross-post.
- Never hand-edit/clean queue records; read live Zernio state instead.

## 9. KNOWN TRAPS (never repeat)
1. Story landed in feed (missing story flag) → always verify `contentType` before queueing.
2. False "failed" while actually published (5 items on 2026-10-04) → verify in Zernio; judge by `platformPostId/platformPostUrl`.
3. Times recorded in Tehran time → Cyprus only.
4. Image model wrote **BAZİNO** / broken Turkish → character-by-character review, regenerate.
5. A card sitting in `ready/` ≠ unpublished; a record in `failed/` ≠ real failure.
6. Scheduled publish silently skipped because the app was closed.
7. Claiming "I visually reviewed it" when image viewing is unavailable → say so; the owner is the reviewer.

## 10. OUTPUT FOLDER & REPORT
`Daily/<date>/{media,captions,transcripts,cards,review.md}`.
End-of-day report to the owner, plain Persian, 4 fixed parts: **what happened · next plan (awaiting "start") · what didn't and why · my next step** + a final line with the stored record id (only after confirming it reached the repo).

## 11. NEVER
Invent a new rule · publish without per-item approval · re-send an unconfirmed post · mix Zernio and portal credentials · POST test data to the portal ingest endpoint · claim "verified" without evidence · change code/build/version without an approved plan.
