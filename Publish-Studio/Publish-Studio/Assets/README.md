# مرجع جامع دارایی‌ها، کاراکتر شیت‌ها و شناسنامه شخصیت‌های بازینو (Assets & Character Registry v2.3)

این پوشه مرجع رسمی تمام تصاویر، کاراکتر شیت‌های استودیویی، پرامپت‌های توصیفی قفل هویت (`CHARACTER_PROMPT.md` و `character_spec.json`) و نمونه صداهای ۳ زبانه (`tr`, `fa`, `en`) برای شخصیت‌های برند **بازینو (Bazino Gaming Club - Famagusta, Northern Cyprus)** است.

تمام این منابع به‌صورت یکپارچه در سرور استودیوی رسانه **RTX 5090** در مسیر `/root/ComfyUI/input/characters/` و رجیستری مرکزی `/root/ComfyUI/input/characters/characters_registry.json` ثبت شده‌اند. هرگاه در پرامپت ارسالی به API سرور نام هر یک از این شخصیت‌ها (به ترکی، فارسی یا انگلیسی) ذکر شود، سرور **به‌صورت خودکار** به پوشهٔ آن کاراکتر مراجعه کرده و تصویر مرجع، پرامپت دقیق چهره/لباس و نمونه صدای متناسب با زبان درخواست‌شده را تزریق می‌کند.

---

## ۱. جدول کامل شخصیت‌های بازینو (۸ کاراکتر ثبت‌شده در سرور)

| شناسه (`character_id`) | نام شخصیت | دامنه و موتور رندر | کاراکتر شیت استودیویی | آواتار پرتره و تمام‌قد | پرامپت و شناسنامه | نمونه صداها (TR / FA / EN) | قوانین ایمنی و نقش |
|---|---|---|---|---|---|---|---|
| **`mona`** | **مونا (Mona)** | انسانی واقع‌گرایانه (`hunyuan-13b` + Character Lock) | `mona/mona-brandwear-character-sheet.png` | `mona/mona-portrait-avatar-01.png`<br>`mona/mona-brandwear-fullbody-01.png`<br>`mona/mona-bazino-club-shorts.jpg` | `mona/CHARACTER_PROMPT.md`<br>`mona/character_spec.json` | `voice_ref_tr.mp3`<br>`voice_ref_fa.mp3`<br>`voice_ref_en.mp3`<br>`voice_ref_original.wav` | سفیر و مجری رسمی بازینو (۲۴ ساله، ۱۶۸ سانتی‌متر) |
| **`farid`** | **فرید (Farid / Ferit)** | انسانی واقع‌گرایانه (`hunyuan-13b` + Character Lock) | `farid/farid-character-sheet.jpg` | `farid/farid-portrait-avatar-01.jpg`<br>`farid/farid-fullbody-avatar-01.jpg` | `farid/CHARACTER_PROMPT.md`<br>`farid/character_spec.json` | `voice_ref_tr.mp3`<br>`voice_ref_fa.mp3`<br>`voice_ref_en.mp3` | گیمر جوان و رقیب حرفه‌ای (۲۴ ساله، ۱۷۸ سانتی‌متر) |
| **`hasti`** | **هستی (Hasti)** | انسانی واقع‌گرایانه (`hunyuan-13b` + Character Lock) | `hasti/hasti-character-sheet.jpg` | `hasti/hasti-portrait-avatar-01.jpg`<br>`hasti/hasti-fullbody-avatar-01.jpg` | `hasti/CHARACTER_PROMPT.md`<br>`hasti/character_spec.json` | `voice_ref_tr.mp3`<br>`voice_ref_fa.mp3`<br>`voice_ref_en.mp3` | دختر گیمر جوان و پرانرژی (۲۳ ساله، ۱۶۵ سانتی‌متر) |
| **`youna`** | **یونا (Youna / Yuna)** | انسانی واقع‌گرایانه (`hunyuan-13b` + Character Lock) | `youna/youna-character-sheet.jpg` | `youna/youna-portrait-avatar-01.jpg`<br>`youna/youna-fullbody-avatar-01.jpg` | `youna/CHARACTER_PROMPT.md`<br>`youna/character_spec.json` | `voice_ref_tr.mp3`<br>`voice_ref_fa.mp3`<br>`voice_ref_en.mp3` | نوجوان ۱۵ ساله (**زیر ۱۸ سال: فقط بازی‌های ورزشی و مسابقه‌ای مجاز است**) |
| **`lea`** | **لیا (Lea)** | انسانی واقع‌گرایانه (`hunyuan-13b` + Character Lock) | `lea/lea-character-sheet.jpg` | `lea/lea-portrait-avatar-01.jpg`<br>`lea/lea-fullbody-avatar-01.jpg` | `lea/CHARACTER_PROMPT.md`<br>`lea/character_spec.json` | `voice_ref_tr.mp3`<br>`voice_ref_fa.mp3`<br>`voice_ref_en.mp3` | دختر ۱۲ ساله (**زیر ۱۸ سال: فقط بازی‌های خانوادگی، شاد و مسابقه‌ای مجاز است**) |
| **`boy_bazino`** | **پسر بازینو (Boy Bazino / Can)** | انیمیشنی سه‌بعدی (`wan-2.2` A14B) | `animated-family/boy-bazino-studio-sheet.jpg` | `animated-family/boy-bazino-avatar-01.png`<br>`animated-family/boy-bazino-avatar-02.png` | `animated-family/boy_bazino/CHARACTER_PROMPT.md`<br>`character_spec.json` | `voice_ref_tr.mp3`<br>`voice_ref_fa.mp3`<br>`voice_ref_en.mp3` | پسر ۶ ساله انیمیشنی خانواده بازینو (سبک Pixar 3D) |
| **`father_bazino`** | **پدر بازینو (Father / Baba)** | انیمیشنی سه‌بعدی (`wan-2.2` A14B) | `animated-family/father-studio-sheet.jpg` | `animated-family/father-avatar-01.png`<br>`animated-family/father-avatar-02.png` | `animated-family/father_bazino/CHARACTER_PROMPT.md`<br>`character_spec.json` | `voice_ref_tr.mp3`<br>`voice_ref_fa.mp3`<br>`voice_ref_en.mp3` | پدر ۳۶ ساله انیمیشنی خانواده بازینو (سبک Pixar 3D) |
| **`mother_bazino`** | **مادر بازینو (Mother / Anne)** | انیمیشنی سه‌بعدی (`wan-2.2` A14B) | `animated-family/mother-studio-sheet.jpg` | `animated-family/mother-avatar-01.png`<br>`animated-family/mother-avatar-02.png` | `animated-family/mother_bazino/CHARACTER_PROMPT.md`<br>`character_spec.json` | `voice_ref_tr.mp3`<br>`voice_ref_fa.mp3`<br>`voice_ref_en.mp3` | مادر ۳۳ ساله انیمیشنی خانواده بازینو (سبک Pixar 3D) |

---

## ۲. پشتیبانی ۳ زبانه با محوریت قبرس شمالی (`language`: `tr` پیش‌فرض، `fa`، `en`)

با توجه به استقرار کلوپ بازینو در **قبرس شمالی (فاماگوستا / Mağusa)**، زبان اصلی و پیش‌فرض سرور **`tr` (ترکی استانبولی)** است و پس از آن زبان‌های **`fa` (فارسی)** و **`en` (انگلیسی)** قرار دارند.
در هر درخواست به API سرور (`/v1/videos/text2video`، `/v1/videos/image2video`، `/v1/videos/video2video`، `/v1/videos/motion-control`) می‌توان فیلد `language` و `character_id` را مشخص کرد:

```json
{
  "prompt": "Mona standing in Bazino club inviting gamers to the weekend FC 26 tournament",
  "language": "tr",
  "dialogue_text": "Merhaba arkadaşlar! Ben Mona, Mağusa'nın en heyecanlı PS5 kulübü Bazino'ya hoş geldiniz!",
  "aspect_ratio": "9:16",
  "upscale_1080p": true
}
```

---

## ۳. تصاویر محیط واقعی کلوپ (`Assets/club/`)
- `bazino-interior-01.jpg` تا `bazino-interior-05.jpg`: نمای واقعی داخل کلوپ بازینو (دیوارهای چوبی ترمووود، نورپردازی نئون بنفش و آبی، تلویزیون‌های 4K و کنسول‌های PS5).
- `bazino-logo-01.png` تا `bazino-logo-03.png`: لوگوی رسمی بازینو.
