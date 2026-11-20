# شناسنامه و پرامپت استاندارد کاراکتر: Farid / Ferit (فرید)
**شناسه سیستمی (`character_id`):** `farid`
**نقش:** Young Male Competitive Gamer & Friend (گیمر جوان و رقیب حرفه‌ای)
**دامنه و موتور رندر تخصصی:** `realistic_human` -> **`hunyuan-13b`**
**سن و قد:** 24 سال | 178 سانتی‌متر
**محدودیت سنی زیر ۱۸ سال:** `خیر (کلیه بازی‌ها مجاز است)`

---

## ۱. کلمات کلیدی فعال‌ساز خودکار در سرور (Trigger Keywords - TR / FA / EN)
هرگاه هر یک از کلمات زیر در پرامپت ارسالی دیده شود، سرور RTX 5090 به‌صورت خودکار به پوشهٔ `farid` مراجعه کرده و تصاویر مرجع، پرامپت قفل هویت چهره و نمونه صدای این کاراکتر را تزریق می‌کند:
- `farid, ferit, فرید, gamer farid`

---

## ۲. منابع تصویری و کاراکتر شیت (Visual Assets in Server `/root/ComfyUI/input/`)
- **کاراکتر شیت کامل استودیویی (`character_sheet`):** `characters/farid/farid-character-sheet.jpg`
- **تصویر پرتره مرجع چهره (`portrait_image`):** `characters/farid/farid-portrait-avatar-01.jpg`
- **تصویر تمام‌قد مرجع (`fullbody_image`):** `characters/farid/farid-fullbody-avatar-01.jpg`
- **تصویر در محیط کلوپ بازینو (`club_image`):** `characters/farid/farid-fullbody-avatar-01.jpg`

---

## ۳. پرامپت توصیفی قطعی و بدون خطا (Locked Visual Identity Prompt)
```text
Photorealistic 24-year-old Mediterranean/Iranian young man named Farid, 178cm tall, athletic build, short styled dark brown/black hair with clean fade, neatly trimmed short stubble beard, warm dark brown eyes, charismatic confident smile, wearing a sleek dark navy/charcoal casual jacket over a black crew-neck t-shirt, dark slim-fit jeans and clean sneakers, holding a white PlayStation 5 DualSense controller inside Bazino PS5 gaming club with wooden slatted walls and neon RGB lighting.
```

### پرامپت منفی محافظ هویت (Locked Negative Prompt)
```text
different face, long hair, bald, clean shaven baby face, long bushy beard, glasses, old, deformed hands, wide bulging eyes, cartoon, 3d render, blurry, watermark
```

---

## ۴. پروفایل صوتی ۳ زبانه (با اولویت زبان ترکی قبرس شمالی `tr`)
- **ترکی استانبولی (`tr` - زبان اول و پیش‌فرض):**
  - موتور پایه: `tr-TR-AhmetNeural` (`rate=+5%`, `pitch=+0Hz`)
  - فایل نمونه صدا: `characters/farid/voice_ref_tr.mp3`
  - دیالوگ نمونه: *"Selam millet! Ben Ferit. Bazino'da FC 26 turnuvası başlıyor, kendine güvenen varsa kolu alsın gelsin!"*
- **فارسی (`fa`):**
  - موتور پایه: `fa-IR-FaridNeural` (`rate=+4%`, `pitch=+0Hz`)
  - فایل نمونه صدا: `characters/farid/voice_ref_fa.mp3`
  - دیالوگ نمونه: *"سلام رفقا! من فریدم. توی بازینو هیچ‌کس حریف من توی اف‌سی ۲۶ نمیشه، اگه ادعات میشه بیا بشین پای دستگاه!"*
- **انگلیسی (`en`):**
  - موتور پایه: `en-US-AndrewMultilingualNeural` (`rate=+4%`, `pitch=+0Hz`)
  - فایل نمونه صدا: `characters/farid/voice_ref_en.mp3`
  - دیالوگ نمونه: *"What's up guys! I'm Farid. Ready for the ultimate PS5 showdown at Bazino Club in Famagusta?"*

---

## ۵. قوانین طلایی کاراکتر (Mandatory Generation Rules)
1. **ممنوعیت مطلق تولید بدون تصویر مرجع:** تحت هیچ شرایطی نباید برای `Farid / Ferit (فرید)` فقط از متن (Pure Text-to-Video بدون تصویر مرجع) استفاده شود؛ سرور موظف است همیشه تصویر مرجع (`portrait_image` یا `fullbody_image`) را به ورودی مدل تزریق کند تا چهره و لباس ۱۰۰٪ ثابت بماند.
2. **یادداشت شخصیت و ایمنی:** Competitive, friendly, energetic 24-year-old pro gamer. Excels at FC 26, Tekken 8, Call of Duty, and Mortal Kombat 1. Can participate in all adult and competitive gaming scenarios.
