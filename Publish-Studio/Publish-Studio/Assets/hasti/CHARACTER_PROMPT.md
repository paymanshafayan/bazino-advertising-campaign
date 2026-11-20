# شناسنامه و پرامپت استاندارد کاراکتر: Hasti (هستی)
**شناسه سیستمی (`character_id`):** `hasti`
**نقش:** Young Female Gamer & Friend (دختر گیمر جوان و پرانرژی)
**دامنه و موتور رندر تخصصی:** `realistic_human` -> **`hunyuan-13b`**
**سن و قد:** 23 سال | 165 سانتی‌متر
**محدودیت سنی زیر ۱۸ سال:** `خیر (کلیه بازی‌ها مجاز است)`

---

## ۱. کلمات کلیدی فعال‌ساز خودکار در سرور (Trigger Keywords - TR / FA / EN)
هرگاه هر یک از کلمات زیر در پرامپت ارسالی دیده شود، سرور RTX 5090 به‌صورت خودکار به پوشهٔ `hasti` مراجعه کرده و تصاویر مرجع، پرامپت قفل هویت چهره و نمونه صدای این کاراکتر را تزریق می‌کند:
- `hasti, هستی, gamer hasti`

---

## ۲. منابع تصویری و کاراکتر شیت (Visual Assets in Server `/root/ComfyUI/input/`)
- **کاراکتر شیت کامل استودیویی (`character_sheet`):** `characters/hasti/hasti-character-sheet.jpg`
- **تصویر پرتره مرجع چهره (`portrait_image`):** `characters/hasti/hasti-portrait-avatar-01.jpg`
- **تصویر تمام‌قد مرجع (`fullbody_image`):** `characters/hasti/hasti-fullbody-avatar-01.jpg`
- **تصویر در محیط کلوپ بازینو (`club_image`):** `characters/hasti/hasti-fullbody-avatar-01.jpg`

---

## ۳. پرامپت توصیفی قطعی و بدون خطا (Locked Visual Identity Prompt)
```text
Photorealistic 23-year-old Mediterranean/Iranian young woman named Hasti, 165cm tall, slim build, long wavy chestnut-brown hair worn down over her shoulders, warm brown eyes, cheerful friendly smile, wearing a cozy stylish beige/cream knitted sweater, blue high-waisted jeans, and white sneakers, holding a white PS5 DualSense controller inside Bazino gaming lounge with warm wooden walls and RGB neon lights.
```

### پرامپت منفی محافظ هویت (Locked Negative Prompt)
```text
ponytail, short hair, blonde hair, glasses, old, distorted face, bulging eyes, extra fingers, cartoon, 3d render, blurry
```

---

## ۴. پروفایل صوتی ۳ زبانه (با اولویت زبان ترکی قبرس شمالی `tr`)
- **ترکی استانبولی (`tr` - زبان اول و پیش‌فرض):**
  - موتور پایه: `tr-TR-EmelNeural` (`rate=+7%`, `pitch=+5Hz`)
  - فایل نمونه صدا: `characters/hasti/voice_ref_tr.mp3`
  - دیالوگ نمونه: *"Selam! Ben Hasti! Bazino'da arkadaşlarla oyun keyfi bir başka, hadi sen de aramıza katıl!"*
- **فارسی (`fa`):**
  - موتور پایه: `fa-IR-DilaraNeural` (`rate=+6%`, `pitch=+5Hz`)
  - فایل نمونه صدا: `characters/hasti/voice_ref_fa.mp3`
  - دیالوگ نمونه: *"سلام! من هستی هستم! هیچ جایی مثل بازینو واسه دورهمی و رقابت با بچه‌ها حال نمیده!"*
- **انگلیسی (`en`):**
  - موتور پایه: `en-US-EmmaMultilingualNeural` (`rate=+5%`, `pitch=+3Hz`)
  - فایل نمونه صدا: `characters/hasti/voice_ref_en.mp3`
  - دیالوگ نمونه: *"Hi guys, I'm Hasti! Gaming nights with friends at Bazino are always unforgettable!"*

---

## ۵. قوانین طلایی کاراکتر (Mandatory Generation Rules)
1. **ممنوعیت مطلق تولید بدون تصویر مرجع:** تحت هیچ شرایطی نباید برای `Hasti (هستی)` فقط از متن (Pure Text-to-Video بدون تصویر مرجع) استفاده شود؛ سرور موظف است همیشه تصویر مرجع (`portrait_image` یا `fullbody_image`) را به ورودی مدل تزریق کند تا چهره و لباس ۱۰۰٪ ثابت بماند.
2. **یادداشت شخصیت و ایمنی:** Warm, cheerful, relatable 23-year-old student and gamer who loves co-op games, FC 26, Tekken 8, and group hangouts with Mona and Farid.
