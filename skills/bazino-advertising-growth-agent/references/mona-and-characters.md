# Content production and the brand characters

> Reference file of the BAZINO project skill. Loaded on demand — see `../SKILL.md`.

---

## ✅ چک‌لیست شخصیت‌ها

```
□ تصویر مرجع شخصیت را به مدل داده‌ام؟
□ مونا از کتابخانه Flow، نه تصویر آپلودی؟
□ بلوک قفل شخصیت عیناً تکرار شده؟
□ اگر Youna/Lea هست، محتوای صفحه مناسب سن است؟
□ تصویر تولیدشده را با چشم دیده‌ام؟
```

---

# 6. CONTENT PRODUCTION

You are responsible for coordinating the complete content-production process.

Depending on the content type, create or request:

- text
- captions
- headlines
- hooks
- CTAs
- articles
- promotional copy
- Instagram content
- Telegram content
- visual concepts
- image/video generation tasks
- Manus-generated media
- website content
- slider content

When media generation is required, use the existing Manus integration.

When publishing is required, use the existing Zernio publishing infrastructure.

Manus and Zernio are established operational components of the BAZINO system and have been tested in real daily publishing workflows.

---

---

# 6-A. MONA — BAZINO VIRTUAL INFLUENCER

**Mona is the official virtual influencer and digital face of BAZINO.**

She is a fully AI-generated digital persona who acts as the human voice of the brand on Instagram `@bazinopro`. She is not a logo or a mannequin: she is a **consistent, recognizable character** with her own personality, taste, humor and genuine interest in gaming. The audience should follow her because they enjoy *her*, not because she advertises.

Full character reference: `Mona/mona-character-en.txt` (EN) and `Mona/mona-character-fa.txt` (FA).

## 6-A.1 GOLDEN RULE — FACE AND BODY CONSISTENCY (NON-NEGOTIABLE)

> **Mona's face and body must remain identical in every advertisement, post, Reel, Story, banner and any other visual output — always, without exception.**

This is the single most important visual rule of the project. The audience must recognize Mona instantly in any image, from any angle, in any outfit. If her face or body proportions change between two posts, the entire investment in the character is destroyed and she becomes "a random AI-generated woman".

**Immutable facial identity:**

| Attribute | Fixed value |
|---|---|
| Apparent age | Young adult woman (mid-20s) |
| Skin | Light olive, Mediterranean tone |
| Hair | Long, dark brown with warm highlights |
| Eyes | Brown |
| Eyebrows | Natural, well-defined |
| Facial proportions | Balanced, feminine, defined cheekbones |
| Default expression | Subtle half-smile, confident but warm gaze |

**Immutable body identity:**

| Attribute | Fixed value |
|---|---|
| Apparent height | Medium to tall, proportionate |
| Build | Fit and proportionate — neither extremely thin nor exaggerated |
| Proportions | Natural and believable; no anatomical exaggeration in any image |

**May change:** hairstyle (down, ponytail, tied), clothing, makeup, environment, lighting, pose, camera angle.

**Must never change:** facial structure, skin tone, eye color, base hair color, body proportions.

## 6-A.2 ENFORCEMENT PROCEDURE

1. **Reference image is mandatory.** Every time a new Mona image is generated, `Assets/mona/mona-portrait-avatar-01.png` must be supplied to the model as a reference image. Generation without a reference image is forbidden.
2. **Full-body reference.** For full-body images, also attach `Assets/mona/mona-brandwear-fullbody-01.png` as the body reference.
3. **Pre-publication QC.** Place every new image next to the reference and compare. If the face is "slightly different", the image is rejected — not published.
4. **Traceability.** Store every approved image together with its prompt and reference images in that day's output folder so it can be reproduced.
5. **Reference upgrade.** If a better reference is ever produced, it replaces the current one only with owner approval, and the whole team is notified.

## 6-A.3 CONTENT RULES FOR MONA

- ❌ Never depict Mona in an overly glamorous or sexualized way. Her appeal comes from her face, confidence and personality.
- ❌ Not every post may be an advertisement. BAZINO exists naturally inside her world.
- ❌ Mona must never announce an unverified promise, prize, discount or date.
- ✅ **AI disclosure.** Mona is identified as a digital/virtual character of BAZINO in the profile bio. She does not repeatedly call herself an AI in content, but the brand never claims she is a real human. AI-generated content is labeled according to Meta policy.
- ✅ In content that earns commission, the Paid Partnership label is used.

## 6-A.4 OTHER BRAND CHARACTERS — YOUNA, LEA AND HASTI

Besides Mona, the project has three further recurring characters. They follow
the **same face-consistency golden rule** as Mona (§6-A.1) and the same
enforcement procedure (§6-A.2).

| Character | Role | Apparent age | Mandatory reference |
|---|---|---|---|
| **Youna** | Teenage boy — competitive gaming, tournaments, affiliate | 15-16 | `Assets/youna/youna-portrait-avatar-01.jpg` |
| **Lea** | Young girl — BAZINO SAFE, family content | 10-12 | `Assets/lea/lea-portrait-avatar-01.jpg` |
| **Hasti** | Mother — parent-facing content, BAZINO SAFE | 32-38 | `Assets/hasti/hasti-portrait-avatar-01.jpg` |

Full identity sheets: **`Doc/brand-characters-fa.md`**.

**Additional rules for minors.** Because Youna and Lea are under 18:

- They must **never** appear alongside violent content, weapons, or 16+/18+
  games. Screens behind them show only sport, racing or abstract visuals.
  (This is the age-rating contradiction recorded in `HANDOFF.md` blocker 3.)
- They are never depicted in a glamorous way or with emphasis on appearance.
- The brand never claims they are real BAZINO customers — they are AI-generated
  characters, disclosed per Meta policy.

## 6-A.5 RELATION TO MANUS

When Manus generates Mona media, the reference images and the identity constraints above must be part of the generation task. A Manus output that violates facial or body consistency is a **failed generation** and must be regenerated, never published.

---

---

