# BAZINO constraints — what a video may and may not contain

Original to this project. Sources: `HANDOFF.md`,
`skills/bazino-advertising-growth-agent/SKILL.md`, `Doc/brand-characters-fa.md`,
and verified facts from the live Google Business Profile.

---

## 1. The venue — describe it accurately

BAZINO is a **console gaming lounge**: large wall-mounted televisions with low
sofa seating.

### 1.1 Confirmed and safe to show

PS5 · Xbox Series X · 85-inch screens · VIP console area · gaming café ·
open 24/7 · cafe service (snacks and drinks) · gaming accessories on sale

### 1.2 ❌ Never show or mention

Desktop computers · desk monitors · gaming chairs · PC esports arena · RTX ·
Core i9 · any specific hardware spec not listed above

Writing "monitors" or "gaming chairs" in a prompt makes the model build a PC
esports arena, which is not this business.

### 1.3 Physical description for prompts

> A long narrow room with matte black walls and ceiling, warm light wood-plank
> flooring, a grid of recessed white ceiling spotlights, very large flat-screen
> televisions mounted along one wall with deep blue and purple LED backlighting
> glowing behind them, low black sofas facing the screens, and a dark slatted
> wood reception counter along one side.

### 1.4 Reference photographs

| File | Use |
|---|---|
| `Assets/club/club-tall-02.jpeg` | Interior, portrait — best for 9:16 |
| `Assets/club/club-wide-03.jpeg` | Interior, wide |
| `Assets/club/club-tall-*.jpeg` · `club-wide-*.jpeg` | 25 further venue photos |

---

## 2. 🔴 The age-rating contradiction

Recorded as blocker 3 in `HANDOFF.md`.

**The conflict.** The real screens show UFC 6, Call of Duty MW and Elden Ring —
16+ and 18+ titles. BAZINO simultaneously markets itself as a safe place for
children.

**The rule.** In any video featuring Youna, Lea, or family-safety messaging:

```text
ON-SCREEN GAME CONTENT — IMPORTANT
The television screens must show only age-appropriate content: a football or
racing game, or colourful abstract motion graphics. Absolutely no combat, no
fighting, no weapons, no shooters, no blood, no horror imagery on any visible
screen.
```

State it explicitly every time. Given the venue photo as reference, the model
will otherwise reproduce the UFC and Call of Duty posters faithfully.

---

## 3. The truth rule

Never invent what has not been verified.

### 3.1 ❌ Never state

Prices or tariffs · discounts · tournament prizes · tournament rules · capacity ·
number of stations · customer counts · rankings ("the best in Cyprus") ·
"100% safe" · "constantly supervised" · commission figures

### 3.2 The tournament

An FC 2026 tournament runs **every Saturday** since 12 September 2026. The
**time, prize and rules are unannounced.** Never guess them.

Approved tournament CTA: `Detaylar ve kayıt: bazino.pro`

### 3.3 ⚠️ The parent app

A "BAZINO parent app" appears in creative scenarios, but whether it exists and
is live is **unconfirmed** — blocker 2 in `HANDOFF.md`.

When a phone screen appears in a video, keep the interface deliberately vague:
the BAZINO wordmark, a clean dark-blue UI, no readable prices, no numbers, no
specific feature claims. This shows intent without asserting a product that may
not ship.

---

## 4. Verified NAP — safe to show

| Field | Value |
|---|---|
| Name | `BAZINO - PS5 Gaming Center` |
| Address (docs, non-Google) | İskele, Long Beach, Mackenzie Square, Vista Mare, Lobby, Unit No. 5 |
| Phone / WhatsApp | `+90 539 133 37 47` |
| Website | `https://bazino.pro` |
| Instagram | `@bazinopro` |
| Facebook | `facebook.com/bazinopro` |
| Hours | 24/7 |
| GPS | `35.2634752, 33.9088575` |

Retired: `Bazino Pro` as a name · `Derviş İzzigil Sokak No.12`

---

## 5. Brand characters

Full sheets in `Doc/brand-characters-fa.md`.

| Character | Role | Age | Reference |
|---|---|---|---|
| **Mona** | Brand influencer | Young adult | Saved in the Flow project library |
| **Hasti** | Mother | 32–38 | `Assets/hasti/hasti-portrait-avatar-01.jpg` |
| **Youna** | Teen boy | 15–16 | `Assets/youna/youna-portrait-avatar-01.jpg` |
| **Lea** | Girl | 10–12 | `Assets/lea/lea-portrait-avatar-01.jpg` |

### 5.1 Mona

- Never upload a reference image — she lives in the Flow project library.
- Never depict her in a glamorous or sexualised way. Her appeal is her face,
  confidence and personality.
- She never announces an unverified promise, prize, discount or date.
- Disclosed as a digital character in the profile bio; content is labelled
  AI-generated per Meta policy.

### 5.2 Youna and Lea — minors

- Never alongside violent content, weapons, or 16+/18+ games.
- Never depicted glamorously or with emphasis on appearance.
- Never claimed to be real BAZINO customers.
- Subject to Google's safety filter — see `omni-syntax.md` §10.

### 5.3 Identity blocks — copy verbatim

```text
HASTI, the mother, age 35: long wavy golden light-brown hair with blonde
sun-kissed highlights falling past her shoulders, golden tanned Mediterranean
skin, warm brown eyes, high defined cheekbones. Cream linen shirt, beige
trousers.

YOUNA, age 15: short dark brown hair, warm brown eyes, defined young jawline,
clear skin. Plain white t-shirt, dark blue denim jeans.

LEA, age 11: long wavy light brown hair past her shoulders, warm brown eyes,
round youthful face. Plain white t-shirt, blue denim jeans.
```

---

## 6. Language

| Element | Language |
|---|---|
| Reel ratio | 80% Turkish · 10% Persian · 10% English |
| On-screen text | **Turkish** |
| Dialogue | **Turkish** |
| Persian translation | Internal review only, never published unless briefed |
| Russian | Not a default for voice content |

---

## 7. Technical specification

| Spec | Value |
|---|---|
| Aspect ratio | 9:16 |
| Resolution | 1080×1920 |
| Clip length | 3–10 seconds (Omni limit) |
| Reel structure | Three 10-second clips stitched to 30 seconds |
| First frame | Instantly legible, never a black fade |
| Text safe zone | Clear of the top 250px and bottom 400px |
| Cover | Consistent template, text never over Mona's face |

---

## 8. Links in captions

No direct site link in promotional captions — bio link only.

- Tournament exception: `Detaylar ve kayıt: bazino.pro`
- General CTA: `Detaylar ve rezervasyon için Bio'daki bağlantıya göz at.`
- Telegram link: never in Instagram content.

---

## 9. Affiliate content

The keyword is language-specific and must match the content's language:

| Language | Keyword |
|---|---|
| Turkish | `Hazır` |
| Persian | `آماده` |
| English | `Ready` |
| Russian | `Готово` |

`SQUAD` is **retired** as a comment keyword. `SQUAD26` remains valid as the
campaign identifier in attribution links and `utm_campaign` — do not confuse
the two.

Approved Turkish affiliate hook opener: `Hazır mısın?`

Full flow in `skills/bazino-advertising-growth-agent/SKILL.md` §14-A.

---

## 10. Pre-flight checklist

- [ ] Console-and-sofa venue, no PCs or gaming chairs
- [ ] Screen content age-appropriate where children appear
- [ ] No price, prize, date, capacity or ranking claim
- [ ] Phone UI vague if the parent app appears
- [ ] Turkish dialogue and on-screen text
- [ ] Text inside the Instagram-safe zone
- [ ] Character identity block repeated in full
- [ ] Mona from the Flow library, not an uploaded image
- [ ] 9:16, 1080×1920, 3–10 seconds
- [ ] Final frame named for chaining
