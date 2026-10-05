---
name: knowledge-instagram-video-production
description: >-
  The short-form video editing and finishing skill for BAZINO Pro (@bazinopro).
  Use whenever a Reel, Story video, video carousel slide or any 9:16 video is
  edited, paced, captioned, voiced, mixed, packaged or reviewed — and whenever an
  existing video must be translated, subtitled or text-masked for a Turkish- or
  English-speaking audience (guide §3.5b and §7.6). Distilled from the OpenMontage
  agentic video-production system (calesthio/OpenMontage, AGPL-3.0) into
  Instagram-admin practice: platform specs, hook and retention engineering,
  edit-decision structure, typography and caption typography, audio targets, review
  rubric and platform packaging. Read SKILL-fa.md first when the task is in Persian.
  Does not cover AI video-generation prompts (see knowledge/video-production) or
  still-image generation (see knowledge/image-production).
license: >-
  Synthesis, not a copy. Method, numbers and checklists were re-expressed and
  translated from calesthio/OpenMontage (AGPL-3.0) skills: creative/short-form,
  creative/video-editing, creative/typography, creative/enhancement-strategy,
  creative/storytelling, creative/sound-design, pipelines/clip-factory/*,
  meta/taste-direction, meta/reviewer. No code, tool implementation or file from
  that repository is redistributed here; source and licence are recorded in
  references/openmontage-source.md. The BAZINO layer (truth rules, owner laws §6–§7,
  queue contract, language policy) is original to this project.
---

# BAZINO Instagram Video Production — editing, finishing, packaging

> **📚 Knowledge skill** — part of `skills/knowledge/` (§32).
> **Mission:** turn approved footage into a finished, publish-ready Instagram video, and convert Persian-source video into Turkish/English.
> **Last researched:** 2026-10-03
> **Primary source:** `calesthio/OpenMontage` (GitHub · AGPL-3.0 · 62.6k★) → [`references/openmontage-source.md`](references/openmontage-source.md)
> **Owner ruling that created this skill:** daily-content guide §7.5 (2026-10-03)
> **Staleness check:** monthly, per the nightly routine in `HANDOFF.md`.

You are the editor for BAZINO's short-form output. The platform decides reach;
the first two seconds decide whether there is any reach at all; everything after
that is retention engineering.

## 1. Three layers, and what this skill is *not*

| Layer | Answers | Where |
|---|---|---|
| **This skill** | How do we cut, caption, mix and package it? | `sections/01…07` |
| **Video generation** | How do we prompt a model to shoot it? | `knowledge/video-production/` |
| **Platform growth** | Why does Instagram show it to anyone? | `knowledge/instagram-growth/` |

This skill **does not grant permission**. Publishing, spending, paid providers and
new video *production* remain gated by guide §6–§7 and the owner's per-item
approval. Editing, translation, restyling, subtitling and masking are free
operations (guide §6 item 3), so this skill can be applied without asking.

## 2. Hard rules (BAZINO layer — never overridden by the source project)

1. **No Persian for a Persian-speaking audience** (guide §7.6). A Persian-source
   video is *raw material*: translate, subtitle or dub it into Turkish (first
   choice) or English before it enters the queue. No Persian word, subtitle, hash
   tag or caption survives into the published asset.
2. **Approved fonts only** (guide §7.2). Render text with the families in
   `Assets/fonts/` (Bebas Neue, Anton, Russo One, Archivo Black, Poppins Bold,
   Montserrat, Oswald, Vazirmatn, Lalezar). System defaults (Tahoma, Arial,
   Calibri, Segoe UI) are banned in rendered text.
3. **Emoji with taste** (guide §7.3): 1–2 per on-image text block, 3–6 in a
   caption. No emoji implying weapons, blood, gambling, cash or prizes.
4. **No factual claim without a source** (guide §7.7 + §6 honesty law): no price,
   prize, capacity, discount or commission in a Reel. News numbers need a second
   source next to Merlin'in Kazanı.
5. **Transcript with every video** (guide §7.4): a video without its transcript is
   incomplete and must not be offered for review.
6. **Never re-queue rejected content unchanged**: any item the owner rejected must
   be rebuilt (new edit, new text, new packaging), not resubmitted.
7. **Watermark-free**: no other page's or app's watermark, logo or screen burn-in.

## 3. Quick reference card (9:16, 1080×1920)

```
SAFE ZONE        900×1400 px centred (universal cross-platform)
MIN TEXT         40 px body · 60–90 px title · captions 42 px+
FONT FAMILIES    1 for title + 1 for body, maximum two per video
CONTRAST         4.5:1 minimum · white on 75 % black box = safest
HOOK             first 0.5 s text on screen, no logo, no "hey guys"
PACING           visual change every 1–3 s · 20–40 cuts/minute
DURATION         15 s (completion) · 30 s (engagement) · 60 s (depth) · ≤90 s hard
AUDIO            voice −12…−14 dB · music −22…−26 dB · −14 LUFS · −1 dBTP
CAPTIONS         mandatory (most viewers watch muted)
```

## 4. The finishing pipeline (apply in order)

```
approved footage / source clip
  1. read the transcript            → sections/05 + guide §7.4
  2. decide the cut list            → sections/05-edit-decisions.md
  3. translate/mask the language    → sections/03 + guide §3.5b, §7.6
  4. hook + pacing pass             → sections/02-hook-pacing-retention.md
  5. text, captions, emoji          → sections/03-typography-captions-emoji.md
  6. audio: voice, music, LUFS      → sections/04-audio-mix.md
  7. packaging: caption, CTA, cover → sections/07-packaging-publish.md
  8. self-review before queueing    → sections/06-review-qc.md
  9. queue card + transcript        → guide §5.3–§5.5, PUBLISHING.md
```

## 5. Loading order and budget

Do not load all sections at once. See [`LOADING-GUIDE.md`](LOADING-GUIDE.md).
Typical Reel job: `02` → `05` → `03` → `06`. Translation job: `03` → `04` → `06`.
News video: `07` → `02` → `06`.

## 6. Where each rule comes from

| Rule group | Source | File |
|---|---|---|
| Safe zones, durations, hooks, pacing, caption specs | OpenMontage `creative/short-form` | `sections/01`, `sections/02` |
| Cut decisions, filler/dead-air removal, J/L cuts | OpenMontage `creative/video-editing` | `sections/05` |
| Typography, sizes, easing, contrast, dwell time | OpenMontage `creative/typography` | `sections/03` |
| Audio levels, ducking, duck targets, VO pace | OpenMontage `creative/sound-design` | `sections/04` |
| Enhancement order, overlay density and placement | OpenMontage `creative/enhancement-strategy` | `sections/05` |
| Explainers arc, but/therefore, hook types | OpenMontage `creative/storytelling` | `references/story-arcs-and-hooks.md` |
| Batch template, first-3-second rule, publish order, per-platform copy | OpenMontage `pipelines/clip-factory/*` | `sections/07` |
| Review severity, decision log, differentiation checks | OpenMontage `meta/reviewer`, `meta/taste-direction` | `sections/06` |
| Language, fonts, emoji, transcript, news source, queue contract | BAZINO owner laws (§6–§7) | this file + `SKILL-fa.md` |

## 7. Completion gate

An edit is finished only when **all** of these are true:

- [ ] every cut is at a word or beat boundary, no mid-word trim, no trailing silence;
- [ ] all text sits inside the 900×1400 safe zone and uses approved fonts;
- [ ] captions match the audio, ≤2 lines, ≤42 characters per line, no spelling errors;
- [ ] the rendered language is Turkish or English only;
- [ ] audio is normalised to −14 LUFS / −1 dBTP with voice on top of music;
- [ ] cover frame is chosen deliberately (it is the thumbnail, not frame 0);
- [ ] caption ≤2200 characters, CTA ≤500, 3–5 sized hashtags in the item's language;
- [ ] transcript attached (video items);
- [ ] `sections/06-review-qc.md` self-review passed and recorded;
- [ ] preview rendered and watched as a real user before the card is queued.
