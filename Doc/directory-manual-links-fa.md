# لینک‌های مستقیم کارهای دستی باقی‌مانده

**تاریخ:** ۲۰۲۶-۰۹-۱۷
**وضعیت:** ۴ مورد نیاز به دخالت دستی دارند

---

## ۱. nerdebulayim.com 🟡 نزدیک‌ترین به اتمام

🔗 **https://www.nerdebulayim.com/panel/firma-ekle**

**ورود:** کاربر `bazinopro` — رمز در فایل محلی (به ریپو کامیت نمی‌شود)

### وضعیت
حساب ساخته شده و **کل فرم مرحله ۱ پر است**. فقط دکمه **«DEVAM ET»** به مرحله ۲ نمی‌رود.

### کار شما
دکمه **DEVAM ET** را بزنید. احتمالاً پیام خطای قرمز نشان می‌دهد که کدام فیلد اجباری خالی است. آن را پر کنید و ادامه دهید.

مشکوک‌ترین فیلدهای خالی‌مانده (عمداً پر نشدند چون داده تأییدشده نداشتیم):
- `Kuruluş Türü` (نوع شرکت)
- `Ciro` (گردش مالی)
- `Çalışan Sayısı` (تعداد کارمند)
- `Yetkili Kişi` (شخص مسئول)
- `Servis Bölgesi`

### اگر خالی باشد و اجباری شد
| فیلد | پیشنهاد |
|---|---|
| Kuruluş Türü | Şahıs / Limited (هرکدام درست است) |
| Yetkili | نام مالک |
| Servis Bölgesi | İskele |
| Ciro / Çalışan | اگر اجباری نیست خالی بگذارید |

⚠️ گردش مالی و تعداد کارمند را حدس نزنید — اگر اجباری نیستند رد کنید.

---

## ۲. kibrisisrehberi.com 🔴 ثبت‌نام

🔗 **https://kibrisisrehberi.com/index.php/firma-ekle/**

### مشکل
تب **«Kayıt ol»** با کلیک برنامه‌ای باز نمی‌شود — هیچ فیلدی رندر نمی‌شود.

### کار شما
۱. لینک بالا را باز کنید
۲. روی **«Kayıt ol»** بزنید (کنار فرم ورود)
۳. ثبت‌نام با `Bazinopro@gmail.com`
۴. ایمیل را تأیید کنید

بعد به من بگویید تا فرم شرکت را پر کنم.

---

## ۳. nerede360.com 🔴 دیالوگ کوکی

🔗 **https://www.nerede360.com/firmani-ekle**

### مشکل
دیالوگ **«Gizlilik ve Çerez Tercihleri»** با کلیک برنامه‌ای بسته نمی‌شود و جلوی فرم را گرفته.

### کار شما
دکمه **«Accept all»** / **«Tümünü Kabul Et»** را بزنید. بعد بگویید تا فرم را پر کنم.

---

## ۴. cyprus-faq.com 🔴 ثبت‌نام بسته است

🔗 صفحه ثبت‌نام: **https://cyprus-faq.com/en/user?register=1**
🔗 بخش سازمان‌ها: **https://cyprus-faq.com/en/north/organizations/**
🔗 بخش رویدادها: **https://cyprus-faq.com/en/north/events/**

### مشکل
پیام صریح سایت: **«Registration disallowed»** — ثبت‌نام عمومی غیرفعال است. این محدودیت سایت است، نه مشکل فنی ما.

### تنها راه باقی‌مانده
ایمیل مستقیم به تحریریه. صفحه تماس:
🔗 **https://cyprus-faq.com/en/contacts/**

⚠️ **اما این بلاک است:** برای درج رویداد تورنمنت به **ساعت رسمی شروع** نیاز است که هنوز اعلام نشده. تا آن موقع نمی‌توان ایمیل معناداری فرستاد.

---

## ۵. دو سایت مرده ❌

- `finditnorthcyprus.com` — خطای اتصال (HTTPS کار نمی‌کند، HTTP صفحه خالی)
- `firma.kktc.com` — خطای اتصال

از فهرست کنار گذاشته شدند.

---

## 🔐 رمزهای عبور

⚠️ **این ریپو عمومی است — هیچ رمزی اینجا ذخیره نمی‌شود.**

رمزها در فایل محلی `credentials-local.md` در ریشه ریپو هستند که در `.gitignore` قرار دارد و هرگز کامیت نمی‌شود.

| سایت | کاربر |
|---|---|
| OpenStreetMap | `BazinoPro` |
| kibrisisletmeleri.com | `Bazinopro@gmail.com` |
| nerdebulayim.com | `bazinopro` |
| nerede360.com | `Bazinopro@gmail.com` |
| kimibilin.com | `Bazinopro` |

## ✅ کارهای تمام‌شده (نیازی به اقدام نیست)

| سایت | وضعیت | لینک |
|---|---|---|
| OpenStreetMap | منتشر شد | https://www.openstreetmap.org/#map=19/35.2634752/33.9088575 |
| kimibilin.com | صف تأیید | https://kimibilin.com/yonetim-paneli/ |
| kibrisisletmeleri.com | صف تأیید | https://kibrisisletmeleri.com/panel/firmalar |
