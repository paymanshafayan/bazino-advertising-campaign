# bazino-advertising-campaign

## آوانگار ۲٫۱ — تبدیل محلی یا ابری گفتار به متن برای ویندوز

برنامهٔ مستقل در [`avanegar`](avanegar) است؛ اسناد کمپین دست‌نخورده‌اند.

- نگه داشتن **Ctrl + Win** برای ضبط؛ رها کردن برای تبدیل در حالت انتخابی
- انتخاب محلی (Whisper روی دستگاه) یا ابری (OpenAI API)
- استفاده از مدل‌های دانلودشدهٔ قبلی در `%LOCALAPPDATA%\Avanegar\models` بدون دانلود مجدد؛ اینترنت و اعتبار API فقط برای حالت ابری لازم است
- بدون اعلان ویندوز؛ بازیابی محدود میکروفون و صفحهٔ Log با کپی
- ذخیرهٔ کلید رمز‌شده با Windows DPAPI و تأیید صریح ارسال صدا

[راهنمای فارسی و حریم خصوصی](avanegar/README.fa.md) · [تست‌ها](avanegar/TESTING.md)

اجرا از کد: `avanegar/run-windows.bat` (Python 3.11 x64). ساخت EXE: `avanegar/build-windows.bat`.
