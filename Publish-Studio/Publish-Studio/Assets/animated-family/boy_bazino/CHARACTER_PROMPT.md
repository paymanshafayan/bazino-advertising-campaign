# شناسنامه و پرامپت استاندارد کاراکتر: Boy Bazino / Can (پسر بازینو / کوچولو بازینو)
**شناسه سیستمی (`character_id`):** `boy_bazino`
**نقش:** 3D Pixar-Style Animated Boy Mascot of Bazino Family (پسر ۶ ساله انیمیشنی خانواده بازینو)
**دامنه و موتور رندر تخصصی:** `3d_animated` -> **`wan-2.2`**
**سن و قد:** 6 سال | 115 سانتی‌متر
**محدودیت سنی زیر ۱۸ سال:** `بله (فقط بازی‌های ورزشی، مسابقه‌ای و خانوادگی مجاز است)`

---

## ۱. کلمات کلیدی فعال‌ساز خودکار در سرور (Trigger Keywords - TR / FA / EN)
هرگاه هر یک از کلمات زیر در پرامپت ارسالی دیده شود، سرور RTX 5090 به‌صورت خودکار به پوشهٔ `animated-family/boy_bazino` مراجعه کرده و تصاویر مرجع، پرامپت قفل هویت چهره و نمونه صدای این کاراکتر را تزریق می‌کند:
- `boy bazino, boy_bazino, boy-bazino, can bazino, پسر بازینو, کوچولو بازینو, بچه بازینو, پسر انیمیشنی`

---

## ۲. منابع تصویری و کاراکتر شیت (Visual Assets in Server `/root/ComfyUI/input/`)
- **کاراکتر شیت کامل استودیویی (`character_sheet`):** `characters/animated-family/boy_bazino/boy-bazino-studio-sheet.jpg`
- **تصویر پرتره مرجع چهره (`portrait_image`):** `characters/animated-family/boy_bazino/boy-bazino-avatar-01.png`
- **تصویر تمام‌قد مرجع (`fullbody_image`):** `characters/animated-family/boy_bazino/boy-bazino-avatar-02.png`
- **تصویر در محیط کلوپ بازینو (`club_image`):** `characters/animated-family/boy_bazino/boy-bazino-avatar-03.png`

---

## ۳. پرامپت توصیفی قطعی و بدون خطا (Locked Visual Identity Prompt)
```text
Cute 3D Pixar/Disney style animated 6-year-old boy named Boy Bazino, big expressive glossy brown eyes, fluffy styled brown hair with a cute front curl, rosy cheeks, cheerful excited smile, wearing a royal-blue and yellow sporty hoodie with the colorful BAZINO joystick logo on the chest, blue denim shorts, and blue-yellow sneakers, holding a white PS5 controller, ultra-clean 3D CGI animation render.
```

### پرامپت منفی محافظ هویت (Locked Negative Prompt)
```text
photorealistic human, real photo, creepy, horror, ugly, deformed face, extra limbs, low resolution, blurry
```

---

## ۴. پروفایل صوتی ۳ زبانه (با اولویت زبان ترکی قبرس شمالی `tr`)
- **ترکی استانبولی (`tr` - زبان اول و پیش‌فرض):**
  - موتور پایه: `tr-TR-EmelNeural` (`rate=+12%`, `pitch=+22Hz`)
  - فایل نمونه صدا: `characters/animated-family/boy_bazino/voice_ref_tr.mp3`
  - دیالوگ نمونه: *"Yaşasın! Baba, anne, hadi çabuk Bazino'ya gidelim! PS5'te oyun oynamak istiyorum!"*
- **فارسی (`fa`):**
  - موتور پایه: `fa-IR-DilaraNeural` (`rate=+10%`, `pitch=+22Hz`)
  - فایل نمونه صدا: `characters/animated-family/boy_bazino/voice_ref_fa.mp3`
  - دیالوگ نمونه: *"آخ جووون! بابایی، مامانی، زود باشین بریم بازینو! می‌خوام با پی‌اس‌فایو بازی کنم!"*
- **انگلیسی (`en`):**
  - موتور پایه: `en-US-AnaNeural` (`rate=+10%`, `pitch=+12Hz`)
  - فایل نمونه صدا: `characters/animated-family/boy_bazino/voice_ref_en.mp3`
  - دیالوگ نمونه: *"Yay! Dad, Mom, let's go to Bazino Gaming Club! I want to play on the PS5!"*

---

## ۵. قوانین طلایی کاراکتر (Mandatory Generation Rules)
1. **ممنوعیت مطلق تولید بدون تصویر مرجع:** تحت هیچ شرایطی نباید برای `Boy Bazino / Can (پسر بازینو / کوچولو بازینو)` فقط از متن (Pure Text-to-Video بدون تصویر مرجع) استفاده شود؛ سرور موظف است همیشه تصویر مرجع (`portrait_image` یا `fullbody_image`) را به ورودی مدل تزریق کند تا چهره و لباس ۱۰۰٪ ثابت بماند.
2. **یادداشت شخصیت و ایمنی:** 3D Pixar-style animated kid mascot. Always routed to Wan 2.2 (wan22_14b_i2v) using boy-bazino-avatar-01.png as start_image for 100% 3D character consistency.
