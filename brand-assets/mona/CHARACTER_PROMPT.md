# شناسنامه و پرامپت استاندارد کاراکتر: Mona (مونا)
**شناسه سیستمی (`character_id`):** `mona`
**نقش:** Bazino Official Brand Ambassador & Presenter (سفیر و مجری رسمی بازینو)
**دامنه و موتور رندر تخصصی:** `realistic_human` -> **`hunyuan-13b`**
**سن و قد:** 24 سال | 168 سانتی‌متر
**محدودیت سنی زیر ۱۸ سال:** `خیر (کلیه بازی‌ها مجاز است)`

---

## ۱. کلمات کلیدی فعال‌ساز خودکار در سرور (Trigger Keywords - TR / FA / EN)
هرگاه هر یک از کلمات زیر در پرامپت ارسالی دیده شود، سرور RTX 5090 به‌صورت خودکار به پوشهٔ `mona` مراجعه کرده و تصاویر مرجع، پرامپت قفل هویت چهره و نمونه صدای این کاراکتر را تزریق می‌کند:
- `mona, مونا, presenter mona, ambassador mona, sunucu mona`

---

## ۲. منابع تصویری و کاراکتر شیت (Visual Assets in Server `/root/ComfyUI/input/`)
- **کاراکتر شیت کامل استودیویی (`character_sheet`):** `characters/mona/mona-brandwear-character-sheet.png`
- **تصویر پرتره مرجع چهره (`portrait_image`):** `characters/mona/mona-portrait-avatar-01.png`
- **تصویر تمام‌قد مرجع (`fullbody_image`):** `characters/mona/mona-brandwear-fullbody-01.png`
- **تصویر در محیط کلوپ بازینو (`club_image`):** `characters/mona/mona-bazino-club-shorts.jpg`

---

## ۳. پرامپت توصیفی قطعی و بدون خطا (Locked Visual Identity Prompt)
```text
Photorealistic 24-year-old Mediterranean/Iranian young woman named Mona, 168cm tall, athletic slim build, warm olive skin with natural texture, expressive hazel-brown almond eyes, well-defined natural eyebrows, warm charismatic smile, sleek dark brown hair styled in a high ponytail with soft face-framing strands, wearing Bazino official navy-blue and white zip-up gaming jersey with the colorful BAZINO joystick logo on the chest, white sporty shorts or fitted dark jeans, white sneakers, standing inside the modern Bazino PS5 gaming lounge in Famagusta with warm wooden slatted walls, glowing purple/blue/magenta RGB LED strips, and 65-inch 4K TVs.
```

### پرامپت منفی محافظ هویت (Locked Negative Prompt)
```text
different face, blonde hair, curly hair, glasses, old, chubby, distorted eyes, wide bulging eyes, extra fingers, deformed hands, blurry, low quality, cartoon, 3d render, watermark, text overlay
```

---

## ۴. پروفایل صوتی ۳ زبانه (با اولویت زبان ترکی قبرس شمالی `tr`)
- **ترکی استانبولی (`tr` - زبان اول و پیش‌فرض):**
  - موتور پایه: `tr-TR-EmelNeural` (`rate=+4%`, `pitch=+2Hz`)
  - فایل نمونه صدا: `characters/mona/voice_ref_tr.mp3`
  - دیالوگ نمونه: *"Merhaba arkadaşlar! Ben Mona, Mağusa'nın en heyecanlı PS5 kulübü Bazino'ya hoş geldiniz!"*
- **فارسی (`fa`):**
  - موتور پایه: `fa-IR-DilaraNeural` (`rate=+4%`, `pitch=+2Hz`)
  - فایل نمونه صدا: `characters/mona/voice_ref_fa.mp3`
  - دیالوگ نمونه: *"سلام بچه‌ها! من مونا هستم، به بازینو، هیجان‌انگیزترین باشگاه پی‌اس‌فایو فاماگوستا خوش اومدین!"*
- **انگلیسی (`en`):**
  - موتور پایه: `en-US-AvaMultilingualNeural` (`rate=+3%`, `pitch=+1Hz`)
  - فایل نمونه صدا: `characters/mona/voice_ref_en.mp3`
  - دیالوگ نمونه: *"Hey everyone! I'm Mona, welcome to Bazino Gaming Club in Famagusta, where every game feels like a final!"*

---

## ۵. قوانین طلایی کاراکتر (Mandatory Generation Rules)
1. **ممنوعیت مطلق تولید بدون تصویر مرجع:** تحت هیچ شرایطی نباید برای `Mona (مونا)` فقط از متن (Pure Text-to-Video بدون تصویر مرجع) استفاده شود؛ سرور موظف است همیشه تصویر مرجع (`portrait_image` یا `fullbody_image`) را به ورودی مدل تزریق کند تا چهره و لباس ۱۰۰٪ ثابت بماند.
2. **یادداشت شخصیت و ایمنی:** Energetic, welcoming, confident, gamer-savvy presenter. Speaks primarily Turkish (for Northern Cyprus / Famagusta audience), as well as Persian and English. Always maintains exact facial identity from mona-portrait-avatar-01.png and mona-brandwear-fullbody-01.png.
