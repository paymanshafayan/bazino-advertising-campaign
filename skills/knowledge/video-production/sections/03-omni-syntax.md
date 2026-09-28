# ۳. نحو Omni — چطور نوشته شود تا رندر شود

> **بخش 3 از اسکیل دانش تولید ویدئو** · ~285 خط
> **کِی بارگذاری شود:** **هنگام نوشتن خود پرامپت.** بعد از اینکه صحنه و دوربین مشخص شد.
> **بعد از استفاده:** رها کن و برو بخش بعد.

---

# Google Omni Flash — prompt syntax

> Sources: Google DeepMind official Omni prompt guide · `ai.google.dev` Gemini
> Omni docs · `google-gemini/gemini-skills` · the 6-dimension framework from
> `jggomez/gemini-omni-video-skills`.
>
> ⚠️ This is **not** Veo. Veo caps at 8 seconds and uses `says:` dialogue
> syntax. Omni runs to 10 seconds and uses timecodes and reference tags. Do not
> mix the two rule sets.

---

## 1. Hard specifications

| Spec | Value |
|---|---|
| Clip length | **3–10 seconds** |
| Aspect ratio | `16:9` · `9:16` — BAZINO always `9:16` |
| Resolution | 360p · 720p · **1080p** · 4k |
| Frame rate | 24 fps |
| Audio | Native, synchronised, generated with the video |
| Scene extension | +10s increments, 40s total maximum |
| Reference images | up to 10 |
| Reference video | up to 3 clips |
| Watermark | SynthID on every output |

---

## 2. The six dimensions

Cover all six, written as natural prose rather than tag lists.

### 2.1 Shot framing and motion

`film camera` · `natural smartphone zoom` · `close-up on [subject]` ·
`over-the-shoulder` · `bird's eye view` · `worm's eye angle` · `dolly zoom` ·
`push in slowly` · `static locked-off` · `handheld with natural shake` ·
`glide gently` · `rush suddenly`

Single-shot enforcement: `In a single unbroken scene` · `In one continuous shot` ·
`No scene cuts` · `oner`

### 2.2 Style

State the rendering medium. For BAZINO: `photorealistic premium commercial
style`, `realistic advertising film`, `documentary handheld`.

### 2.3 Lighting and volumetrics

State origin and physics: `deep blue LED backlighting glowing behind each
screen` · `warm afternoon light through sheer curtains` · `recessed white
ceiling spotlights` · `cool screen glow as the only light on their faces`.

### 2.4 Location and environment

The spatial anchor. BAZINO's venue described in `SKILL.md` §6.1.

### 2.5 Action and interaction

Explicit choreography. `She turns the phone toward them` beats `she shows them`.

### 2.6 Text rendering

Omni renders on-screen text reliably — a genuine advantage for Turkish captions.
State the exact string, position and hold duration.

```text
On-screen text, top third, held for the full 10 seconds:
"Çocuklar oynasın, siz rahatlayın!"
Bold modern sans-serif, white with a soft drop shadow.
```

---

## 3. Timecode syntax — preferred for multi-beat clips

Omni parses timecodes natively and this beats prose for a 10-second clip with
several beats.

```text
[0-3s] Wide shot. The two children run across the living room, laughing.
       Hasti stands at centre, one hand at her temple.
[3-7s] She picks up her phone. Over-the-shoulder insert on the screen.
       She taps once and her shoulders drop.
[7-10s] She turns the screen toward them. Both heads snap around.
        They run for the door.
```

Natural-language timing also works and can be mixed in:

- `After 3 seconds, she picks up the phone.`
- `At 5s the music lifts.`
- `Every 2s cut to a new frame.`

⚠️ When extending a clip, `0s` refers to the **start of the extension**, not the
original video.

---

## 4. Reference tags — exact syntax

This is where face drift comes from. Get it exactly right.

### 4.1 Simple tags

```text
<frame> — use the image as the starting frame
<ref>   — use the image as a style or character reference
```

### 4.2 Explicit declaration — preferred for BAZINO

```text
[# Sources @Image1] [# References @Image2 @Image3]

<prompt body>

Use Image1 as the starting frame.
Use the given images as references for video generation.
The images should not be used as literal initial frames.
```

**The closing instruction is mandatory.** Without it Omni may paste a reference
photo in as a frame instead of learning the face from it.

### 4.3 Patterns

| Goal | Syntax |
|---|---|
| First frame only | `[# Sources @Image1]` + `Use Image1 as the starting frame.` |
| Looping clip | `[# Sources @Image1 @Image1]` — same image as first and last |
| Frame + character refs | `[# Sources @Image1] [# References @Image2 @Image3]` |
| Character refs only | `[# References @Image1 @Image2]` + the non-literal instruction |

---

## 5. Audio

Omni generates audio with the video. It cannot be edited separately afterwards —
a change means regenerating the whole clip.

```text
Audio: ambient room tone, distant controller clicks, children laughing softly.
No music.
```

### 5.1 Dialogue

```text
Mona says warmly: "Ne zaman huzur istersen, Bazino burada."
```

- Keep spoken delivery under ~5 seconds inside a 10-second clip. Cramming speeds
  up the delivery unnaturally.
- Say `No dialogue.` explicitly for silent clips or Omni may invent speech.

### 5.2 Negative audio

`No dialogue.` · `No music.` · `No extra sound effects.` · `No embellishments.`

### 5.3 Voices in Flow

Flow exposes a voice picker (`@Voice`) and custom voices built on a preset base.
For a recurring character like Mona, choose one voice and reuse it — voice
identity matters as much as face identity.

---

## 6. Lip sync

Omni does native lip sync. For a dialogue close-up, spell out the phonetics.

```text
LIP SYNC — HIGHEST PRIORITY
Frame-accurate to the Turkish audio. Every syllable matches the mouth shape:
- closed-lip consonants (b, p, m) show the lips fully meeting
- rounded vowels (o, ö, u, ü) show clearly rounded lips
- open vowels (a, e) show a correspondingly open jaw
- Turkish "ç" and "ş" require visible lip protrusion
Mouth movement starts and stops exactly with the audio. No drift, no mumbling,
no continued movement after the line ends. Face the camera straight on
throughout the dialogue so the mouth is fully visible.
```

Keep the head facing camera during speech — lip sync degrades badly on turns.

---

## 7. Editing an existing clip

Simple beats detailed. Always lead with the preservation phrase.

```text
Edit this keeping everything else identical. <one isolated change>
```

| Avoid | Write |
|---|---|
| "In the video of the man on the sofa, please add a small black cat that runs in from the right, jumps onto his lap, and then he strokes its head…" | "Add a cat that jumps onto his lap, he begins to pet it. Keep everything else the same." |
| "Please remove the phone he is holding and fill in the background…" | "Make the phone invisible. Keep everything else the same." |

---

## 8. Scene extension

Extend in 10-second increments up to 40 seconds total. Omni uses the last 10
seconds as context and will edit some final frames to make the join seamless.

⚠️ Uploading your own video for extension or editing is **blocked in the EEA,
Switzerland and the UK**. Extending model-generated video works everywhere. A
video-to-video edit that finishes instantly with empty output is this
restriction, not a failure.

---

## 9. Anti-patterns

| Anti-pattern | Why | Instead |
|---|---|---|
| "make it cool" | Unstructured | Define style and lighting |
| "animate it" | No kinetic spec | Describe the motion vector |
| "better quality" | Subjective | Name the rendering style |
| Long adjective stacks | Over-specifies | Trust the model's world knowledge |
| Describing physics in detail | Handled natively | State macro intent |
| Uploading audio references | Unsupported | Describe the audio in text |
| Multiple video references | Unsupported | Use one |
| YouTube URLs | Unsupported | Upload the file |
| **Minors in input images** | **Safety filter** | See below |
| Contradictions | Model obeys the strongest signal | Remove one side |

---

## 10. 🔴 The minors restriction

Google's safety filter blocks **minors and recognisable people** in input
images. BAZINO's Youna (15) and Lea (11) references may be rejected.

Escalation path:
1. Attach the reference and try.
2. If blocked, use the written identity block alone.
3. If still blocked, raise the apparent age in the description.
4. Tell the owner. Never silently swap in different characters.

Symptom: the generation completes suspiciously fast with empty output, or
returns a generic face ignoring the reference entirely.

---

## 11. Structural template for BAZINO

```text
Create a 10-second vertical 9:16 video, 1080x1920, photorealistic premium
commercial style.

[# Sources @Image1] [# References @Image2 @Image3]

CHARACTER LOCK
<full identity block for every person on screen — repeated in every clip>

ENVIRONMENT
<venue or location, referencing the attached interior photo>

[0-3s]  <beat one>
[3-7s]  <beat two>
[7-10s] <beat three>

ON-SCREEN GAME CONTENT
<age-appropriate constraint when children are present>

DIALOGUE
<character> says <manner>: "<Turkish line>"

Audio: <ambient design>. <negative audio keywords>

ON-SCREEN TEXT, top third, held for the full clip:
"<Turkish string>"
Clear of the top 250px and bottom 400px.

FINAL FRAME
<the exact end state, so the next clip can chain from it>

Use Image1 as the starting frame.
Use the given images as references for video generation.
The images should not be used as literal initial frames.
```


---

## ✅ چک‌لیست این بخش

```
□ مدت بین ۳ تا ۱۰ ثانیه؟ (نه ۸ — آن Veo است)
□ نسبت 9:16 نوشته شده؟
□ تایم‌کد [0-3s] استفاده شده؟
□ تگ مرجع درست با دستور پایانی؟
□ صدا صریح مشخص شده؟
□ اگر دیالوگ هست، بلوک لیپ‌سینک آوایی دارد؟
```

---

**بخش بعد:** `04-bazino-rules.md`
