# راهنمای کامل انتشار ریلز با Zernio API (`ZERNIO_REEL_PUBLISHING_GUIDE.md`)

> **مسیر اسکریپت رسمی انتشار:** `C:\projects\Antigravity Bazino\Publish-Studio\publish_zernio_reel.mjs`
> **آدرس پایهٔ API:** `https://zernio.com/api`

---

## ۱. نکات فنی و طلایی (یافته‌شده در تست واقعی)

1. **کاور اختصاصی ریلز اینستاگرام (`instagramThumbnail`):**
   - Zernio فیلد `thumbnailUrl` داخل `mediaItems` را برای اینستاگرام نادیده می‌گیرد.
   - برای ثبت کاور اختصاصی روی ریلز اینستاگرام، حتماً لینک کاور آپلودشده باید داخل شیء پلتفرم اینستاگرام به این شکل ارسال شود:
     ```json
     {
       "platform": "instagram",
       "accountId": "6ab391ba8d284ffb21332381",
       "platformSpecificData": {
         "instagramThumbnail": "https://media.zernio.com/.../cover.jpg",
         "shareToFeed": true
       }
     }
     ```
   - **محدودیت متا:** پس از انتشار ریلز، امکان تغییر کاور از طریق API وجود ندارد؛ کاور باید حتماً در لحظهٔ ساخت پست (`POST /v1/posts`) ارسال شود.

2. **نام فیلد اجباری در آپلود فایل (`/v1/media/presign`):**
   - نام فایل باید حتماً با کلید کوچک `filename` (و نه `fileName`) ارسال شود.
   - برای آپلود فایل‌های ویدیویی بزرگ (`50MB+`)، استفاده از `curl.exe --upload-file` بسیار پایدارتر از `fetch` است و از خطای قطع سوکت جلوگیری می‌کند.

---

## ۲. حساب‌های متصل بازینو در Zernio

| پلتفرم | Account ID | نام کاربری |
|---|---|---|
| `instagram` | `6ab391ba8d284ffb21332381` | `bazinopro` |
| `youtube` | `6ab964044d0196fa5a722376` | `bazinopro` |
| `facebook` | `6aba79525ad41c33d8cdeba7` | `bazinopro` |
| `tiktok` | `6aba84ea5be037602e0dc96f` | `bazinopro` |
| `telegram` | `6abaaaff42b1748681d57975` | `Bazinopro` |

*(واتس‌اپ در انتشار خودکار فید/ریلز استفاده نمی‌شود).*

---

## ۳. دستورات آماده برای انتشار (تست ترایال / انتشار نهایی)

پیش از اجرا، کلید `ZERNIO_API_KEY` را در محیط PowerShell تنظیم کنید:

```powershell
$env:ZERNIO_API_KEY="YOUR_ZERNIO_API_KEY"
```

### الف) تست خشک (Dry-Run بدون انتشار واقعی)
```powershell
node "C:\projects\Antigravity Bazino\Publish-Studio\publish_zernio_reel.mjs" `
  --dir "C:\projects\Antigravity Bazino\Publish-Studio\Ready-Media\reel-gta-vistamare-heist" `
  --video "reel-gta-vistamare-heist-en.mp4" `
  --cover "cover.jpg" `
  --dry-run
```

### ب) انتشار فوری ریل Vistamare GTA با کاور اختصاصی
```powershell
node "C:\projects\Antigravity Bazino\Publish-Studio\publish_zernio_reel.mjs" `
  --dir "C:\projects\Antigravity Bazino\Publish-Studio\Ready-Media\reel-gta-vistamare-heist" `
  --video "reel-gta-vistamare-heist-en.mp4" `
  --cover "cover.jpg"
```

### ج) انتشار فوری ریل مونا زیر آب با کاور اختصاصی
```powershell
node "C:\projects\Antigravity Bazino\Publish-Studio\publish_zernio_reel.mjs" `
  --dir "C:\projects\Antigravity Bazino\Publish-Studio\Ready-Media\reel-mona-hourly-peace" `
  --video "reel-mona-hourly-peace.mp4" `
  --cover "cover.jpg"
```

### د) زمان‌بندی انتشار (مثلاً ۲ ساعت بعد به وقت قبرس `+03:00`)
کافی است `--schedule "2026-11-19T17:15:00+03:00"` را به انتهای دستور اضافه کنید.
