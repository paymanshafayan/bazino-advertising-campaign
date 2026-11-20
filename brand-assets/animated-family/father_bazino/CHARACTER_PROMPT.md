# شناسنامه و پرامپت استاندارد کاراکتر: Father Bazino / Baba (پدر خانواده بازینو)
**شناسه سیستمی (`character_id`):** `father_bazino`
**نقش:** 3D Pixar-Style Animated Father of Bazino Family (پدر ۳۶ ساله انیمیشنی خانواده بازینو)
**دامنه و موتور رندر تخصصی:** `3d_animated` -> **`wan-2.2`**
**سن و قد:** 36 سال | 180 سانتی‌متر
**محدودیت سنی زیر ۱۸ سال:** `خیر (کلیه بازی‌ها مجاز است)`

---

## ۱. کلمات کلیدی فعال‌ساز خودکار در سرور (Trigger Keywords - TR / FA / EN)
هرگاه هر یک از کلمات زیر در پرامپت ارسالی دیده شود، سرور RTX 5090 به‌صورت خودکار به پوشهٔ `animated-family/father_bazino` مراجعه کرده و تصاویر مرجع، پرامپت قفل هویت چهره و نمونه صدای این کاراکتر را تزریق می‌کند:
- `father bazino, father_bazino, baba bazino, پدر بازینو, بابا بازینو, پدر خانواده بازینو`

---

## ۲. منابع تصویری و کاراکتر شیت (Visual Assets in Server `/root/ComfyUI/input/`)
- **کاراکتر شیت کامل استودیویی (`character_sheet`):** `characters/animated-family/father_bazino/father-studio-sheet.jpg`
- **تصویر پرتره مرجع چهره (`portrait_image`):** `characters/animated-family/father_bazino/father-avatar-01.png`
- **تصویر تمام‌قد مرجع (`fullbody_image`):** `characters/animated-family/father_bazino/father-avatar-02.png`
- **تصویر در محیط کلوپ بازینو (`club_image`):** `characters/animated-family/father_bazino/father-avatar-03.png`

---

## ۳. پرامپت توصیفی قطعی و بدون خطا (Locked Visual Identity Prompt)
```text
Handsome 3D Pixar/Disney style animated 36-year-old father named Father Bazino, warm brown eyes, neatly styled dark brown hair and groomed short beard/mustache, warm friendly dad smile, wearing a navy-blue and teal polo shirt or zip jacket with the BAZINO logo on the chest, khaki chinos, and white sneakers, holding a white PS5 controller, ultra-clean 3D CGI animation render.
```

### پرامپت منفی محافظ هویت (Locked Negative Prompt)
```text
photorealistic human, real photo, bald, old grandpa, ugly, deformed hands, blurry
```

---

## ۴. پروفایل صوتی ۳ زبانه (با اولویت زبان ترکی قبرس شمالی `tr`)
- **ترکی استانبولی (`tr` - زبان اول و پیش‌فرض):**
  - موتور پایه: `tr-TR-AhmetNeural` (`rate=+2%`, `pitch=-2Hz`)
  - فایل نمونه صدا: `characters/animated-family/father_bazino/voice_ref_tr.mp3`
  - دیالوگ نمونه: *"Tamam şampiyon! Hafta sonu tüm aile Bazino'ya gidiyoruz, ama FC 26'da seni yenerim haberin olsun!"*
- **فارسی (`fa`):**
  - موتور پایه: `fa-IR-FaridNeural` (`rate=+2%`, `pitch=-2Hz`)
  - فایل نمونه صدا: `characters/animated-family/father_bazino/voice_ref_fa.mp3`
  - دیالوگ نمونه: *"حله قهرمان! آخر هفته دسته‌جمعی میریم بازینو، ولی توی فوتبال خودم برنده میشم‌ها!"*
- **انگلیسی (`en`):**
  - موتور پایه: `en-US-BrianMultilingualNeural` (`rate=+2%`, `pitch=-2Hz`)
  - فایل نمونه صدا: `characters/animated-family/father_bazino/voice_ref_en.mp3`
  - دیالوگ نمونه: *"Alright champion! This weekend the whole family is going to Bazino Club, get ready for our match!"*

---

## ۵. قوانین طلایی کاراکتر (Mandatory Generation Rules)
1. **ممنوعیت مطلق تولید بدون تصویر مرجع:** تحت هیچ شرایطی نباید برای `Father Bazino / Baba (پدر خانواده بازینو)` فقط از متن (Pure Text-to-Video بدون تصویر مرجع) استفاده شود؛ سرور موظف است همیشه تصویر مرجع (`portrait_image` یا `fullbody_image`) را به ورودی مدل تزریق کند تا چهره و لباس ۱۰۰٪ ثابت بماند.
2. **یادداشت شخصیت و ایمنی:** 3D Pixar-style animated father who loves playing PS5 games with his son (Boy Bazino) and wife (Mother Bazino). Always routed to Wan 2.2.
