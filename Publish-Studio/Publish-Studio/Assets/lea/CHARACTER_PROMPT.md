# شناسنامه و پرامپت استاندارد کاراکتر: Lea (لیا)
**شناسه سیستمی (`character_id`):** `lea`
**نقش:** Young Girl Gamer - Under 18 Family/Kids Games Only (دختر نوجوان گیمر - ویژه بازی‌های خانوادگی و شاد)
**دامنه و موتور رندر تخصصی:** `realistic_human` -> **`hunyuan-13b`**
**سن و قد:** 12 سال | 148 سانتی‌متر
**محدودیت سنی زیر ۱۸ سال:** `بله (فقط بازی‌های ورزشی، مسابقه‌ای و خانوادگی مجاز است)`

---

## ۱. کلمات کلیدی فعال‌ساز خودکار در سرور (Trigger Keywords - TR / FA / EN)
هرگاه هر یک از کلمات زیر در پرامپت ارسالی دیده شود، سرور RTX 5090 به‌صورت خودکار به پوشهٔ `lea` مراجعه کرده و تصاویر مرجع، پرامپت قفل هویت چهره و نمونه صدای این کاراکتر را تزریق می‌کند:
- `lea, لیا, لی‌آ`

---

## ۲. منابع تصویری و کاراکتر شیت (Visual Assets in Server `/root/ComfyUI/input/`)
- **کاراکتر شیت کامل استودیویی (`character_sheet`):** `characters/lea/lea-character-sheet.jpg`
- **تصویر پرتره مرجع چهره (`portrait_image`):** `characters/lea/lea-portrait-avatar-01.jpg`
- **تصویر تمام‌قد مرجع (`fullbody_image`):** `characters/lea/lea-fullbody-avatar-01.jpg`
- **تصویر در محیط کلوپ بازینو (`club_image`):** `characters/lea/lea-fullbody-avatar-01.jpg`

---

## ۳. پرامپت توصیفی قطعی و بدون خطا (Locked Visual Identity Prompt)
```text
Photorealistic 12-year-old girl named Lea, 148cm tall, sweet cheerful child face, straight dark brown shoulder-length hair with neat bangs (fringe) across her forehead, bright brown eyes, happy innocent smile, wearing a modest soft pastel-pink long-sleeve sweatshirt, blue jeans, and white sneakers, holding a white PS5 DualSense controller in a bright, safe, family-friendly Bazino gaming lounge, TV screen in background ONLY showing colorful family/kids games like Astro Bot or kart racing.
```

### پرامپت منفی محافظ هویت (Locked Negative Prompt)
```text
adult woman, makeup, violence, guns, blood, horror, Mortal Kombat, GTA, Call of Duty, distorted face, bulging eyes, cartoon, blurry
```

---

## ۴. پروفایل صوتی ۳ زبانه (با اولویت زبان ترکی قبرس شمالی `tr`)
- **ترکی استانبولی (`tr` - زبان اول و پیش‌فرض):**
  - موتور پایه: `tr-TR-EmelNeural` (`rate=+9%`, `pitch=+16Hz`)
  - فایل نمونه صدا: `characters/lea/voice_ref_tr.mp3`
  - دیالوگ نمونه: *"Merhaba! Ben Lea! Bazino'da ailemle birlikte Astro Bot ve yarış oyunları oynamayı çok seviyorum!"*
- **فارسی (`fa`):**
  - موتور پایه: `fa-IR-DilaraNeural` (`rate=+8%`, `pitch=+16Hz`)
  - فایل نمونه صدا: `characters/lea/voice_ref_fa.mp3`
  - دیالوگ نمونه: *"سلام! من لیا هستم! توی بازینو با خونواده‌م کلی بازی‌های شاد و مسابقه‌ای بازی می‌کنیم!"*
- **انگلیسی (`en`):**
  - موتور پایه: `en-US-AnaNeural` (`rate=+5%`, `pitch=+4Hz`)
  - فایل نمونه صدا: `characters/lea/voice_ref_en.mp3`
  - دیالوگ نمونه: *"Hi! I'm Lea! I love playing Astro Bot and fun family games at Bazino Club!"*

---

## ۵. قوانین طلایی کاراکتر (Mandatory Generation Rules)
1. **ممنوعیت مطلق تولید بدون تصویر مرجع:** تحت هیچ شرایطی نباید برای `Lea (لیا)` فقط از متن (Pure Text-to-Video بدون تصویر مرجع) استفاده شود؛ سرور موظف است همیشه تصویر مرجع (`portrait_image` یا `fullbody_image`) را به ورودی مدل تزریق کند تا چهره و لباس ۱۰۰٪ ثابت بماند.
2. **یادداشت شخصیت و ایمنی:** MANDATORY SAFETY RULE (UNDER 18): Lea is 12 years old. She MUST NEVER appear in any adult, violent, or 18+ gaming scenes. ONLY family-friendly, cheerful, colorful games (Astro Bot, It Takes Two, Sackboy, Overcooked, Racing, Sports)!
