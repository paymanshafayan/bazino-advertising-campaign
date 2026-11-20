# شناسنامه و پرامپت استاندارد کاراکتر: Youna / Yuna (یونا)
**شناسه سیستمی (`character_id`):** `youna`
**نقش:** Teenage Boy Gamer - Under 18 Safe Sports/Racing Only (نوجوان گیمر - ویژه بازی‌های ورزشی و مسابقه‌ای)
**دامنه و موتور رندر تخصصی:** `realistic_human` -> **`hunyuan-13b`**
**سن و قد:** 15 سال | 162 سانتی‌متر
**محدودیت سنی زیر ۱۸ سال:** `بله (فقط بازی‌های ورزشی، مسابقه‌ای و خانوادگی مجاز است)`

---

## ۱. کلمات کلیدی فعال‌ساز خودکار در سرور (Trigger Keywords - TR / FA / EN)
هرگاه هر یک از کلمات زیر در پرامپت ارسالی دیده شود، سرور RTX 5090 به‌صورت خودکار به پوشهٔ `youna` مراجعه کرده و تصاویر مرجع، پرامپت قفل هویت چهره و نمونه صدای این کاراکتر را تزریق می‌کند:
- `youna, yuna, یونا`

---

## ۲. منابع تصویری و کاراکتر شیت (Visual Assets in Server `/root/ComfyUI/input/`)
- **کاراکتر شیت کامل استودیویی (`character_sheet`):** `characters/youna/youna-character-sheet.jpg`
- **تصویر پرتره مرجع چهره (`portrait_image`):** `characters/youna/youna-portrait-avatar-01.jpg`
- **تصویر تمام‌قد مرجع (`fullbody_image`):** `characters/youna/youna-fullbody-avatar-01.jpg`
- **تصویر در محیط کلوپ بازینو (`club_image`):** `characters/youna/youna-fullbody-avatar-01.jpg`

---

## ۳. پرامپت توصیفی قطعی و بدون خطا (Locked Visual Identity Prompt)
```text
Photorealistic 15-year-old teenage boy named Youna, 162cm tall, youthful friendly face, short tousled dark brown hair, bright brown eyes, enthusiastic teenager smile, wearing a casual heather-grey hoodie over a dark t-shirt, blue jeans, and sporty sneakers, holding a white PS5 DualSense controller inside Bazino PS5 gaming club, family-friendly bright atmosphere, TV screen in background ONLY showing football (EA Sports FC 26) or car racing (Gran Turismo 7).
```

### پرامپت منفی محافظ هویت (Locked Negative Prompt)
```text
beard, mustache, adult man, old, violence, guns, blood, Mortal Kombat, GTA, Call of Duty, distorted face, bulging eyes, cartoon, 3d render, blurry
```

---

## ۴. پروفایل صوتی ۳ زبانه (با اولویت زبان ترکی قبرس شمالی `tr`)
- **ترکی استانبولی (`tr` - زبان اول و پیش‌فرض):**
  - موتور پایه: `tr-TR-AhmetNeural` (`rate=+8%`, `pitch=+12Hz`)
  - فایل نمونه صدا: `characters/youna/voice_ref_tr.mp3`
  - دیالوگ نمونه: *"Selam! Ben Yuna! Bazino'da FC 26 ve Gran Turismo 7 oynamak 진짜 süper heyecanlı!"*
- **فارسی (`fa`):**
  - موتور پایه: `fa-IR-FaridNeural` (`rate=+8%`, `pitch=+12Hz`)
  - فایل نمونه صدا: `characters/youna/voice_ref_fa.mp3`
  - دیالوگ نمونه: *"سلام! من یونا هستم! بازی کردن اف‌سی ۲۶ و گرن توریسمو روی تلویزیون‌های بزرگ بازینو فوق‌العاده‌ست!"*
- **انگلیسی (`en`):**
  - موتور پایه: `en-US-GuyNeural` (`rate=+8%`, `pitch=+10Hz`)
  - فایل نمونه صدا: `characters/youna/voice_ref_en.mp3`
  - دیالوگ نمونه: *"Hey! I'm Youna! Playing FC 26 and Gran Turismo 7 on the huge 4K screens at Bazino is awesome!"*

---

## ۵. قوانین طلایی کاراکتر (Mandatory Generation Rules)
1. **ممنوعیت مطلق تولید بدون تصویر مرجع:** تحت هیچ شرایطی نباید برای `Youna / Yuna (یونا)` فقط از متن (Pure Text-to-Video بدون تصویر مرجع) استفاده شود؛ سرور موظف است همیشه تصویر مرجع (`portrait_image` یا `fullbody_image`) را به ورودی مدل تزریق کند تا چهره و لباس ۱۰۰٪ ثابت بماند.
2. **یادداشت شخصیت و ایمنی:** MANDATORY SAFETY RULE (UNDER 18): Youna is 15 years old. He MUST NEVER appear near violent or 18+ games (NO GTA, NO Mortal Kombat, NO Call of Duty, NO Resident Evil). ONLY sports, football (FC 26, NBA 2K), and racing (Gran Turismo 7, Sonic, Astro Bot)!
