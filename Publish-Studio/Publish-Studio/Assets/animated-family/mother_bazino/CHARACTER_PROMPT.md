# شناسنامه و پرامپت استاندارد کاراکتر: Mother Bazino / Anne (مادر خانواده بازینو)
**شناسه سیستمی (`character_id`):** `mother_bazino`
**نقش:** 3D Pixar-Style Animated Mother of Bazino Family (مادر ۳۳ ساله انیمیشنی خانواده بازینو)
**دامنه و موتور رندر تخصصی:** `3d_animated` -> **`wan-2.2`**
**سن و قد:** 33 سال | 168 سانتی‌متر
**محدودیت سنی زیر ۱۸ سال:** `خیر (کلیه بازی‌ها مجاز است)`

---

## ۱. کلمات کلیدی فعال‌ساز خودکار در سرور (Trigger Keywords - TR / FA / EN)
هرگاه هر یک از کلمات زیر در پرامپت ارسالی دیده شود، سرور RTX 5090 به‌صورت خودکار به پوشهٔ `animated-family/mother_bazino` مراجعه کرده و تصاویر مرجع، پرامپت قفل هویت چهره و نمونه صدای این کاراکتر را تزریق می‌کند:
- `mother bazino, mother_bazino, anne bazino, مادر بازینو, مامان بازینو, مادر خانواده بازینو`

---

## ۲. منابع تصویری و کاراکتر شیت (Visual Assets in Server `/root/ComfyUI/input/`)
- **کاراکتر شیت کامل استودیویی (`character_sheet`):** `characters/animated-family/mother_bazino/mother-studio-sheet.jpg`
- **تصویر پرتره مرجع چهره (`portrait_image`):** `characters/animated-family/mother_bazino/mother-avatar-01.png`
- **تصویر تمام‌قد مرجع (`fullbody_image`):** `characters/animated-family/mother_bazino/mother-avatar-02.png`
- **تصویر در محیط کلوپ بازینو (`club_image`):** `characters/animated-family/mother_bazino/mother-avatar-03.png`

---

## ۳. پرامپت توصیفی قطعی و بدون خطا (Locked Visual Identity Prompt)
```text
Lovely 3D Pixar/Disney style animated 33-year-old mother named Mother Bazino, expressive warm hazel-brown eyes, wavy chestnut-brown hair styled half-up, kind joyful smile, wearing a stylish purple/magenta and navy sporty top with the BAZINO logo on the chest, dark jeans, and white sneakers, holding a white PS5 controller, ultra-clean 3D CGI animation render.
```

### پرامپت منفی محافظ هویت (Locked Negative Prompt)
```text
photorealistic human, real photo, old grandma, ugly, deformed face, blurry
```

---

## ۴. پروفایل صوتی ۳ زبانه (با اولویت زبان ترکی قبرس شمالی `tr`)
- **ترکی استانبولی (`tr` - زبان اول و پیش‌فرض):**
  - موتور پایه: `tr-TR-EmelNeural` (`rate=+3%`, `pitch=+0Hz`)
  - فایل نمونه صدا: `characters/animated-family/mother_bazino/voice_ref_tr.mp3`
  - دیالوگ نمونه: *"Bazino hem çok temiz hem de aileler için harika bir ortam! Bu akşam kupayı ben kaldıracağım!"*
- **فارسی (`fa`):**
  - موتور پایه: `fa-IR-DilaraNeural` (`rate=+2%`, `pitch=+0Hz`)
  - فایل نمونه صدا: `characters/animated-family/mother_bazino/voice_ref_fa.mp3`
  - دیالوگ نمونه: *"محیط بازینو هم فوق‌العاده تمیز و شیکه و هم برای خانواده‌ها عالیه! امشب جام قهرمانی مال خودمه!"*
- **انگلیسی (`en`):**
  - موتور پایه: `en-US-AvaMultilingualNeural` (`rate=+2%`, `pitch=+0Hz`)
  - فایل نمونه صدا: `characters/animated-family/mother_bazino/voice_ref_en.mp3`
  - دیالوگ نمونه: *"Bazino is so clean, modern, and welcoming for families! Tonight I'm winning the family tournament!"*

---

## ۵. قوانین طلایی کاراکتر (Mandatory Generation Rules)
1. **ممنوعیت مطلق تولید بدون تصویر مرجع:** تحت هیچ شرایطی نباید برای `Mother Bazino / Anne (مادر خانواده بازینو)` فقط از متن (Pure Text-to-Video بدون تصویر مرجع) استفاده شود؛ سرور موظف است همیشه تصویر مرجع (`portrait_image` یا `fullbody_image`) را به ورودی مدل تزریق کند تا چهره و لباس ۱۰۰٪ ثابت بماند.
2. **یادداشت شخصیت و ایمنی:** 3D Pixar-style animated mother who enjoys family gaming nights at Bazino Club. Always routed to Wan 2.2.
