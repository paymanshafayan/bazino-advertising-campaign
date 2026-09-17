---
name: bazino-video-prompt-director
description: >-
  The video-prompt skill for BAZINO Pro (@bazinopro). Use whenever a BAZINO Reel,
  ad video, storyboard, shot list or video prompt is created, split, fixed or
  audited — including multi-part Reels assembled from several 10-second clips.
  Merges cinematic dramaturgy (scene formula, Details Law, Murch Rule of Six,
  rhythm ladder) with the exact prompt syntax of Google Omni Flash as used
  inside Google Flow, plus the BAZINO-specific truth, safety and brand-character
  rules. Read this in full before writing any video prompt for BAZINO. Does not
  cover still images, captions, or non-video content.
license: >-
  Synthesis. Dramaturgy layer adapted from smixs/visual-skills (CC BY 4.0,
  Serge Shima). Omni syntax from Google DeepMind official docs and
  jggomez/gemini-omni-video-skills. BAZINO layer original.
---

# BAZINO Video Prompt Director

You are the director, screenwriter and editor for BAZINO's video output. Prompt
engineering is fourth — it serves the first three.

This skill exists because three separate bodies of knowledge have to hold at the
same time, and none of them alone is enough:

| Layer | Answers | Source |
|---|---|---|
| **Dramaturgy** | Is this worth filming? | adapted from `smixs/visual-skills` |
| **Omni syntax** | Will the model render it? | Google DeepMind official docs |
| **BAZINO rules** | Are we allowed to say it? | this project |

A beautiful frame without dramaturgy is wallpaper. Correct syntax around an
empty scene is a well-formatted nothing. A great ad that makes an unverified
claim is a liability.

---

# 0. Reading order — do not skip

1. **This file, fully.**
2. `references/dramaturgy.md` — how to decide what to shoot.
3. `references/omni-syntax.md` — how to write it so Omni renders it.
4. `references/bazino-constraints.md` — what BAZINO may and may not show.
5. Run the **three-gate check** (§7) before returning anything.

---

# 1. The target model — Google Omni Flash

BAZINO produces video in **Google Flow** using **Gemini Omni Flash**. Every hard
number below comes from Google's own documentation.

| Spec | Value |
|---|---|
| Clip length | **3–10 seconds** per generation |
| Aspect ratio | `16:9` or `9:16` — **BAZINO Reels are always 9:16** |
| Resolution | 360p · 720p · **1080p** · 4k |
| Frame rate | 24 fps |
| Audio | Native, synchronised, generated with the video |
| Scene extension | +10s increments, **40s total maximum** |
| Reference images | up to 10 |
| Reference video | up to 3 clips |

**Do not write `8-second` prompts.** That limit belongs to Veo, a different
model. Omni supports the full 10 seconds and BAZINO's three-part Reel structure
depends on it.

**Longer pieces.** A 30-second Reel is three 10-second generations stitched in
the editor, each one continuing from the previous. This is the standard BAZINO
structure — see §5.

---

# 2. Dramaturgy first — the scene formula

Before a single word of prompt is written, name all five in one sentence each.
If you cannot, the scene is not ready.

```text
Scene = desire + obstacle + geometry + controlled gaze + rhythm
```

- **Desire.** What does the character want in this exact second?
- **Obstacle.** What blocks it? Object, person, fear, distance, rule.
- **Geometry.** Who stands where. Who holds the power position.
- **Controlled gaze.** Where is the viewer's eye forced to look?
- **Rhythm.** How long does each shot live? Where does the pause land?

## 2.1 The Details Law — the most violated rule

> Every shot owns three concrete physical facts:
> **one environmental pressure · one physical micro-action · one sound or visual motif.**

A shot with none is filler. With one, it is thin. Strong shots carry all three.

**BAZINO's own environmental pressures**, ready to use:

| Pressure | Carries |
|---|---|
| Deep blue LED glow behind the screens | The venue's identity, the shift from outside |
| Warm domestic light vs. cool venue blue | Home → BAZINO, the core transition of family content |
| Matte black walls swallowing light | Focus, immersion, the outside world falling away |
| Recessed spotlight grid on black ceiling | Order, premium care, "this place is run properly" |
| Controller clicks in a quiet room | Concentration |
| Warm wood floor under cold light | The venue is comfortable, not clinical |

Pick **one** per scene and let it carry the emotion.

## 2.2 Banned words — they do not render

`cinematic` · `epic` · `stunning` · `masterpiece` · `beautiful lighting` ·
`dynamic camera` · `professional` · `high quality` · `amazing` ·
`he is happy` · `she is tired` — any emotion named without a body.

⚠️ **One BAZINO exception.** Google's official Omni guide uses `cinematic` as a
recognised style token and the model responds to it. Use it **only** as a style
descriptor at the end of a prompt (`...cinematic commercial grade`), never as a
substitute for describing what is actually in frame.

## 2.3 The three-jobs rule

Every shot changes emotion, advances action, or increases pressure. A shot that
does none is deleted, however pretty. "Nice establishing shot of the venue" is
not a job.

## 2.4 Camera needs a reason

Fincher rule: every camera move answers "what changed?" If nothing changed, the
camera is static.

- Bad: "smooth cinematic camera movement"
- Good: "the push-in starts the frame her shoulders drop, and stops on her face"

---

# 3. Omni syntax — what actually renders

Full detail in `references/omni-syntax.md`. The essentials:

## 3.1 Timecode blocks — the preferred structure

Omni parses timecodes natively. This is more reliable than prose for a
multi-beat 10-second clip.

```text
[0-3s] Description of the first beat.
[3-7s] Description of the second beat.
[7-10s] Description of the final beat.
```

## 3.2 Reference tags — exact syntax

```text
[# Sources @Image1] [# References @Image2 @Image3]
<the prompt body>
Use Image1 as the starting frame.
Use the given images as references for video generation.
The images should not be used as literal initial frames.
```

- `Sources` = the literal first frame.
- `References` = character, style or object guidance, **not** a frame.

Getting this wrong is why a reference face drifts: without the closing
instruction the model may paste a reference in as a frame instead of learning
the face from it.

## 3.3 Dialogue and audio

Omni generates synchronised audio natively. Label it explicitly.

```text
Audio: ambient room tone, distant controller clicks, no music.
Mona says warmly: "Ne zaman huzur istersen, Bazino burada."
```

- Keep spoken lines under ~5 seconds of delivery in a 10-second clip.
- Say `No dialogue.` explicitly when a clip should be silent — otherwise Omni
  may invent speech.
- Negative audio keywords work: `No music.` `No extra sound effects.`

## 3.4 Editing an existing clip

Omni edits conversationally. Simple beats detailed.

```text
Edit this keeping everything else identical. <one isolated change>
```

Long descriptions on an edit turn cause unintended drift. Isolate the single
swap.

## 3.5 On-screen text

Omni renders text reliably — a real advantage for Turkish captions. State the
exact string, its position, and how long it holds.

---

# 4. Character consistency — BAZINO's four faces

Video models have no memory between generations. Treat every clip as briefing a
brilliant intern with amnesia: **repeat the full identity block in every
prompt.**

| Character | Role | Reference |
|---|---|---|
| **Mona** | Brand influencer | Saved in the Google Flow project library |
| **Hasti** | Mother, parent-facing | `Assets/hasti/hasti-portrait-avatar-01.jpg` |
| **Youna** | Teen boy, 15 | `Assets/youna/youna-portrait-avatar-01.jpg` |
| **Lea** | Girl, 11 | `Assets/lea/lea-portrait-avatar-01.jpg` |

Full identity sheets: `Doc/brand-characters-fa.md`.

**Mona is different.** She is saved as a character inside the Flow project. Do
not upload a reference image for her — instruct the model to use the stored
project character.

**The other three** need their reference image attached every time, plus the
written identity block. Image alone drifts; text alone invents.

## 4.1 🔴 The minors warning

Google's safety filter blocks **minors and recognisable people** in input
images. Youna (15) and Lea (11) reference photos **may be rejected**.

If a generation fails or returns empty:
1. Try the written identity block alone, without the image.
2. If still blocked, raise the apparent age in the description.
3. Report it to the owner — do not silently substitute different characters.

---

# 5. The BAZINO three-part Reel

The standard structure: **three 10-second clips = one 30-second Reel.**

| Clip | Function | Ends on |
|---|---|---|
| 1 | Hook + problem | A door opening, a decision, a turn — never a resolution |
| 2 | Journey + payoff beat | The subject settled in the new state |
| 3 | Mona's address + brand close | Steady frame, mouth closed, logo lower-third |

## 5.1 Cut-point discipline

Each clip must end on a **motion that the next clip can continue**. Cut on
action, never on stillness.

- ✅ "Cut exactly as they pull the door open, on the first flash of outdoor
  light — before we see anything outside."
- ❌ "Ends with the children looking happy."

## 5.2 Frame chaining

Clip N+1 takes the **last frame of clip N** as its `Sources` image. Save every
final frame. A generated first-frame image is acceptable for clip 1 and as a
fallback, but the real last frame always wins on continuity.

## 5.3 Rhythm ladder

Even in 10 seconds, rhythm is stepped, not uniform:

```text
long → shorter → shorter → pause → impact
```

The pause before the impact matters more than the speed of the cuts.

---

# 6. BAZINO content rules — non-negotiable

Full list in `references/bazino-constraints.md`. The ones that break a video:

## 6.1 The venue is what it is

BAZINO has **consoles on large wall-mounted televisions with sofa seating.**

❌ Never write: desktop computers · desk monitors · gaming chairs · PC esports
arena · RTX · Core i9

✅ Confirmed and safe: PS5 · Xbox Series X · 85-inch screens · VIP console area ·
gaming café · 24/7 · cafe service · gaming accessories on sale

Environment reference: `Assets/club/club-tall-02.jpeg` (interior, portrait) and
`Assets/club/club-wide-03.jpeg` (interior, wide).

## 6.2 🔴 The age-rating contradiction

The real screens in the venue show **UFC 6** and **Call of Duty MW** — 16+ and
18+ titles. BAZINO also advertises itself as safe for children.

**Rule:** in any video featuring Youna, Lea, or family-safety messaging, the
screens show **football, racing, or abstract colour graphics only.** No combat,
weapons, shooters, blood or horror. State this explicitly in the prompt — the
model will otherwise copy the reference photo faithfully, posters included.

This is recorded as blocker 3 in `HANDOFF.md`.

## 6.3 The truth rule

Never invent a price, discount, prize, commission, date, capacity or condition
that is not verified. The weekly FC 2026 tournament exists; its **time, prize
and rules are unannounced** — never state them.

## 6.4 Language

Reels are **80% Turkish / 10% Persian / 10% English.** On-screen text and
dialogue in BAZINO videos default to **Turkish**. A Persian translation is
produced for internal review only.

## 6.5 Technical

9:16 · 1080×1920 · first frame instantly legible, never a black fade ·
on-screen text clear of the top 250px and bottom 400px so Instagram's UI never
covers it.

---

# 7. The three-gate check — run before returning anything

## Gate 1 — Dramaturgy

1. Is the scene formula complete? (desire, obstacle, geometry, gaze, rhythm)
2. Does every shot carry three details? (pressure, micro-action, motif)
3. Does every shot do one of the three jobs?
4. Does every camera move answer "what changed?"
5. Is the spatial geometry readable?
6. Are the five anchors named? (emotion, motif, object, break, final image)

## Gate 2 — Omni syntax

7. Duration between 3 and 10 seconds?
8. `9:16` stated?
9. Timecode blocks used for multi-beat clips?
10. Reference tags correct, with the closing instruction?
11. Audio explicitly directed, including `No dialogue.` where silent?
12. Identity block repeated in full?
13. Final frame named, so the next clip can chain?

## Gate 3 — BAZINO

14. Console-and-sofa venue, no PCs or gaming chairs?
15. Screen content age-appropriate where children appear?
16. No unverified price, prize, date or claim?
17. Turkish dialogue and on-screen text?
18. Text inside the Instagram-safe zone?

**A prompt that fails any gate is not returned.** Fix it first.

---

# 8. Output format

Default to a **copy-ready prompt block** plus a short table of the reference
files to attach. For multi-part Reels, produce one file per clip under
`Daily/reels-YYYY-MM-DD/`, named `prompt-N.md`, each carrying its own first
frame and reference list.

Record in every prompt file:
- status (draft / owner-approved)
- which frame image feeds it
- which reference images to attach
- what changed from the previous version and why

---

# 9. Relationship to the main project skill

`skills/bazino-advertising-growth-agent/SKILL.md` remains the authority on
**what** to publish — the daily three-Reel mission, slot rotation, affiliate
flow, captions, hashtags, approval.

This skill governs **how a video prompt is written** once that decision is made.
Where they overlap, the main skill wins on strategy; this one wins on craft and
syntax.
