# Video production preparation workflow

> Reference file of the BAZINO project skill. Loaded on demand — see `../SKILL.md`.

---

## ✅ چک‌لیست آماده‌سازی ویدئو

```
□ اسکیل دانش را بارگذاری کرده‌ام؟ (LOADING-GUIDE ترتیب را می‌گوید)
□ شش قانون آهنین را خوانده‌ام؟
□ فقط یک پرامپت در این پاسخ؟
□ بلوک یکپارچه و آماده کپی؟
□ ترجمه کامل فارسی دارد؟
□ قبل از تأیید چیزی ذخیره نکرده‌ام؟
```

---

# 6-B. VIDEO PRODUCTION PREPARATION WORKFLOW

Triggered when the owner says "prepare the video", "let's make today's Reel",
"مقدمات ساخت ویدئو را آماده کن" or any equivalent.

This section governs the PREPARATION of everything needed before generation:
scenario, prompts, first frames, reference files. It does not cover the
generation itself — the owner runs that in Google Flow.

Craft and syntax live in `skills/knowledge/video-production/`. Read it in
full before writing a single prompt.

## 6-B.0 The six iron rules

Learned from real production runs. Each one was violated once and cost a rebuild.

1. **Never save before approval.** Present in chat, wait for explicit approval,
   then commit. A saved unapproved file pollutes the repo and the owner has to
   catch it.
2. **One prompt at a time.** Build prompt N, present it, wait. Do not run ahead
   and produce N+1 before N is approved. Do not rebuild a prompt that is
   already approved.
3. **State the target dimensions before generating an image**, then verify the
   actual output. Image models silently return 1:1 or 2:3 when asked for 9:16.
4. **Always pass character reference images to the image model.** Text
   description alone produces a different face every time.
5. **Omni renders 3-10 seconds.** Never write 8-second prompts — that is Veo,
   a different model.
6. **Every prompt containing dialogue carries a phoneme-level lip-sync block.**
   Writing "accurate lip sync" is not enough — it is generic and the model
   ignores it. The actual Turkish line must be broken down sound by sound. A
   dialogue prompt without this block is not delivered.

## 6-B.1 Phase 0 — Orientation (before asking anything)

Establish, from the repo and the calendar, without bothering the owner:

| Question | Where to look |
|---|---|
| Which slot is today? | Date parity: odd → BAZINO SAFE, even → PRESTIGE (§5-A.1) |
| What did yesterday run? | Previous `Daily/reels-*/README-fa.md` |
| Which characters exist? | `Doc/brand-characters-fa.md` |
| Which venue photos exist? | `Assets/club/` |
| What is still unverified? | `HANDOFF.md` §3 blockers |

Report the slot conclusion in one line so the owner can correct it.

## 6-B.2 Phase 1 — Scenario

The owner usually supplies the scenario. If not, propose one that fits the
slot and wait.

Once the scenario exists:

- Split it into N clips of up to 10 seconds.
- For each clip, name the **cut point** — the motion the next clip continues
  from. Cut on action, never on stillness.
- Flag anything in the scenario that touches an unverified fact (prices,
  prizes, the parent app, tournament times) and say so before writing.

## 6-B.3 Phase 2 — Characters

For every person on screen:

1. Does a registered character already fit? Check `Doc/brand-characters-fa.md`.
2. If not, generate a portrait, get approval, then register it: reference
   image under `Assets/<name>/`, identity sheet in `Doc/brand-characters-fa.md`,
   row in §6-A.4 and in `HANDOFF.md`.
3. Write the CHARACTER LOCK block once. It is reused **verbatim** in every
   prompt of the batch — same wording, no paraphrasing.

Mona is never uploaded as an image: she lives in the Google Flow project
library. Say so explicitly in the prompt.

## 6-B.4 Phase 3 — Prompts, one at a time

For each clip, in order:

1. Build the prompt per `knowledge/video-production`.
2. Run the three-gate check (dramaturgy · Omni syntax · BAZINO rules).
3. Present it in chat as a copy-ready block, plus the list of files to attach.
4. State what you changed and why, if revising.
5. **Wait.** On approval, write `Daily/reels-YYYY-MM-DD/prompt-N.md` and commit.
6. Only then move to clip N+1.

### Worked example — the rule that is broken most often

A rule survives context pressure far better when it carries an example.
This one has been violated three times, so here is exactly what each side
looks like.

**✅ Correct — one reply, one prompt, complete, then stop**

> ## 🎬 Prompt 1 of 3 — the hook
>
> ```text
> Create a 10-second vertical video, 9:16, 1080x1920, 24fps.
> [every section inside this single block: environment, people,
>  screen content, shot list with timecodes, audio, on-screen text
>  in BOTH languages, grade, final frame, constraints]
> ```
>
> ## 📖 Persian translation of the prompt above
> [the whole block translated, section by section]
>
> ## Files to attach
> | Role | File |
>
> Shall I save this and move to prompt 2?

⟵ **Stop here. Wait for the answer.**

**❌ Wrong — three real failures**

| What happened | Why it fails |
|---|---|
| All three prompts in one reply | The owner cannot approve them one by one |
| "Shared prompt" + "clip-specific" + "texts" as separate pieces | Unusable in Flow — it takes one block |
| Only the on-screen text translated | The owner wants to understand the *whole* prompt |

**Both languages go inside the one block**, never as a separate message:

```text
ON-SCREEN TEXT
[font, position, timing — stated once]

  VERSION A — TURKISH:
  [Turkish text]

  VERSION B — PERSIAN:
  [Persian text]

  For Version B use a typeface with full Persian script support and
  right-to-left direction. Keep position, size, timing, colour and line
  count identical to Version A.
```

If the owner supplies their own draft prompt, do not discard it. Compare it
against the skill, keep what is stronger in theirs, and list the specific
corrections with reasons.

### 6-B.4.1 The lip-sync block — mandatory for every line of dialogue

Omni does native lip sync and is the best available model for it — but only
when the prompt specifies the phonetics. A generic instruction produces a
floating, mumbling mouth.

**Three steps for every spoken line:**

**1. Break the sentence into words** and give the mouth shape for each.

**2. Cover these four Turkish phoneme groups:**

| Group | Letters | Mouth shape |
|---|---|---|
| Closed-lip consonants | `b` `p` `m` | Lips fully meet and release |
| Rounded vowels | `o` `ö` `u` `ü` | Lips clearly rounded, slightly protruded |
| Open vowels | `a` `e` | Jaw correspondingly open |
| Turkish-specific | `ç` `ş` `c` `j` | Visible lip protrusion |

**3. Always append the three closing constraints:**

```text
Mouth movement must start and stop exactly with the audio — no drift, no
floating or mumbling mouth, no continued movement after the line ends. Jaw,
lips, tongue and cheek motion must all read as genuine Turkish speech, not
generic talking. Face the camera straight on throughout the dialogue so the
mouth is fully visible and unobstructed.
```

The straight-to-camera rule matters most: lip sync degrades badly on head turns.

**Time budget.** Roughly 5 seconds of speech maximum inside a 10-second clip.
A longer line makes the model rush the delivery and it reads as unnatural.
Shorten the line; never speed up the delivery.

**Closing frame.** After the line ends, state explicitly that the mouth is
closed and still — otherwise the model keeps it moving.

**Worked example:**

```text
DIALOGUE — Turkish, natural delivery
Mona says warmly: "Ne zaman huzur istersen, Bazino burada."
Delivered warmly and unhurried across roughly four seconds, with a small
natural pause after "istersen".

LIP SYNC — HIGHEST PRIORITY
Mona's lip synchronisation must be frame-accurate to the Turkish audio. Every
syllable must match the mouth shape precisely:
- "Ne zaman" — open jaw on the two "a" vowels, tongue tip visible on the "n"
  sounds, lips relaxed and apart.
- "huzur" — breathy open "h", then clearly rounded and slightly protruded lips
  through both "u" vowels.
- "istersen" — wide flat lips on the "i" and "e" vowels, teeth close together
  on the "s" sounds.
- "Bazino" — the lips must fully meet and release on the "B", then round
  distinctly on the closing "o".
- "burada" — lips meet again on the "b", round on "u", then open wide on the
  two "a" vowels.
Mouth movement must start and stop exactly with the audio — no drift, no
floating or mumbling mouth, no continued movement after the line ends. Jaw,
lips, tongue and cheek motion must all read as genuine Turkish speech, not
generic talking. Face the camera straight on throughout the dialogue so the
mouth is fully visible and unobstructed.
```

⚠️ **A copied block is not acceptable.** When the dialogue changes, the phoneme
breakdown must be rewritten for the new words. This happened once: the line was
replaced but the lip-sync block still referenced `gönderin` and `Çocuklarınızı`,
words no longer in the sentence.

## 6-B.5 Phase 4 — First frames

One image per clip, each the literal opening frame.

1. State the target: 9:16, 1080x1920.
2. Generate with the character reference images attached.
3. Verify the real dimensions. Reject anything outside ratio 0.55-0.57.
4. Present for approval.
5. Save only after approval, as `frame_1-videoN.jpg`.

Clip 1's frame may be generated. Clips 2 and 3 should ideally use the real last
frame of the previous clip — a generated frame is the fallback.

## 6-B.6 Phase 5 — Handoff package

The batch folder `Daily/reels-YYYY-MM-DD/` must contain:

| File | Content |
|---|---|
| `README-fa.md` | Slot, scenario, character lock, on-screen text, reference chain, unverified flags |
| `prompt-1.md` … `prompt-N.md` | Each with status, its frame, its reference list, changelog |
| `frame_1-videoN.jpg` | One per clip, 9:16 |
| portraits | Any new character portraits produced in this batch |

Close by telling the owner exactly which files to attach to which generation.

## 6-B.7 What is NOT part of this workflow

Caption, hashtags, posting time, approval to publish — those belong to §5-A.
Editing and stitching the generated clips is the owner's job, not the agent's.

---

---

