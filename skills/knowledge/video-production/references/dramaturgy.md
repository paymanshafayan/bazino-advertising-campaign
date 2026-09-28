# Dramaturgy layer

> Adapted from `smixs/visual-skills` (CC BY 4.0 — Serge Shima,
> github.com/smixs/visual-skills), compressed to what BAZINO's 10-second
> commercial work actually uses, with BAZINO examples substituted throughout.

A beautiful frame without dramaturgy is wallpaper. This layer decides whether a
shot deserves to exist before any syntax is written.

---

## 1. The scene formula

```text
Scene = desire + obstacle + geometry + controlled gaze + rhythm
```

Name each in one sentence. If you cannot, the scene is not ready.

**Worked example — the BAZINO SAFE Reel:**

| Element | Value |
|---|---|
| Desire | Hasti wants twenty minutes of quiet |
| Obstacle | Two children with more energy than the room can hold |
| Geometry | She stands trapped at centre frame; the children own the edges; the door is the exit |
| Gaze | Forced to her face, then to the phone screen, then to the door |
| Rhythm | Chaos (long) → decision (medium) → release (short) → relief (pause) |

---

## 2. The Details Law

Every shot owns three concrete physical facts.

### 2.1 Environmental pressure

A fact about the space that carries the emotion.

**BAZINO's library:**

| Pressure | Carries |
|---|---|
| Deep blue LED glow behind wall-mounted screens | Venue identity, arrival |
| Warm window light in a bright home | Comfort under strain |
| Matte black walls absorbing light | Immersion, focus |
| Spotlight grid on black ceiling | Order, premium care |
| Controller clicks in a quiet room | Concentration |
| Warm wood floor under cold blue light | Comfortable, not clinical |
| Cushions scattered across marble | Domestic chaos |

Pick one per scene. Two compete; three is noise.

### 2.2 Physical micro-action

Emotion rendered in the body. The model cannot render a feeling; it renders a jaw.

| Instead of | Write |
|---|---|
| She is exhausted | Her hand rises to her temple and stays there a beat too long |
| They are excited | Both heads snap toward her at once; one drops a cushion mid-air |
| She is relieved | Her shoulders drop a full inch as she exhales |
| He is absorbed | His thumbs move; the rest of him does not |

Calibration: **2–4 observable cues** per emotional transition. Fewer leaves the
model guessing. More reads as overacting.

### 2.3 Sound anchor or visual motif

A recurring perceptual hook.

BAZINO's natural motif: **the colour transition.** Warm domestic gold → cool
venue blue. It recurs in every family-facing piece and is the visual signature
of the brand promise.

---

## 3. The three-jobs rule

Every shot changes emotion, advances action, or increases pressure. A shot doing
none is deleted.

"Beautiful shot of the venue" is not a job. "The blue light hits their faces as
they step in, and the noise of the street cuts out" is three jobs at once.

---

## 4. Murch's Rule of Six — where to cut

Priority order. Each item outweighs everything below it combined.

```text
emotion       51%
story         23%
rhythm        10%
eye-trace      7%
screen plane   5%
3D space       4%
```

Cutting "for pace" is item three. Serving pace ahead of emotion is how
forgettable social video gets made — and it is the default behaviour of every
model you will ever prompt.

---

## 5. Camera must have a reason

Every move answers "what changed?" If nothing changed, the camera is static.

Valid reasons: a decision was made · new information entered frame · pressure
escalated · the character looked and we reveal what they saw · a gesture pulled
focus · the space changed.

- ❌ "smooth cinematic camera movement"
- ✅ "the dolly-in starts the frame her shoulders drop and stops on her face"

---

## 6. Spatial clarity

Spielberg principle: even in chaos the viewer knows where the hero is, where the
pressure is, and where the exit is.

Before writing a fast sequence, sketch the geography in one sentence:
"Children move left-to-right across the room; mother is fixed at centre; the
door is off-frame right."

---

## 7. Rhythm ladder

Rhythm is stepped, not uniform.

```text
long → shorter → shorter → pause → impact
```

**Inside a 10-second BAZINO clip:**

```text
3.0s  wide — the situation
2.5s  medium — the decision
1.5s  insert — the object
1.0s  reaction
0.5s  pause              ← the pause matters more than the speed
1.5s  impact / exit
```

The pause before the impact is more important than the speed of the cuts.

---

## 8. Five anchors — exactly five, no more

For any short piece commit to:

1. One main emotion
2. One visual motif
3. One anchor object
4. One break
5. One final image

**BAZINO SAFE Reel:**

| Anchor | Value |
|---|---|
| Emotion | Relief |
| Motif | Warm gold → cool blue |
| Object | The phone |
| Break | She turns the screen toward them |
| Final image | Mona's steady look into the lens, children playing behind her |

---

## 9. Shot card — fill every field

Empty fields reveal missing direction.

`Shot ID` · `Beat` · `Emotion` · `Frame` · `Composition` · `Camera` ·
`Movement reason` · `Action` · `Eye trace` · `Duration` · `Cut type` · `Sound` ·
`Light / colour` · `Production note`

---

## 10. The final image rule

Every clip needs a named final frame. The model uses the ending as its emotional
destination.

- ❌ "they look happy"
- ✅ "ends on both faces lit blue by the screen, grinning, the room silent behind them"

For BAZINO's chained Reels the final image is also the **next clip's first
frame** — so it is doing double work and cannot be vague.

---

## 11. Banned vocabulary

`cinematic`\* · `epic` · `stunning` · `masterpiece` · `beautiful lighting` ·
`dynamic camera` · `professional` · `high quality` · `amazing` · `intense` ·
`powerful` · any emotion named without a body.

\* See `SKILL.md` §2.2 — `cinematic` is permitted for Omni **only** as a
trailing style token, never as a substitute for content.

---

## 12. Dramaturgy check

1. Scene formula complete?
2. Three details on every shot?
3. Three jobs on every shot?
4. Motivated camera?
5. Readable geometry?
6. Five anchors named?

Step 2 is the most violated. Check it twice.

---

*Dramaturgy layer adapted from [smixs/visual-skills](https://github.com/smixs/visual-skills)
by Serge Shima, CC BY 4.0. Examples and venue-specific material original to BAZINO.*
