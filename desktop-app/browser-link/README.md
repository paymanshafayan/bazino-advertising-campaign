# Bazino Studio Bridge — پل موقت مرورگر روی شاخهٔ مجاز

پل فعلیِ `BazinoBridge.exe` به شاخهٔ `cdp-bus` متصل است. این ایجنت طبق محدودیت نشست حق ارسال فرمان/پوش به آن شاخه را ندارد. پل جدید با نام متمایز **`BazinoStudioBridge.exe`** و عنوان پنجرهٔ **Bazino Studio Bridge** از [`BazinoStudioBridge.ps1`](BazinoStudioBridge.ps1) (اقتباس از سورس آزمودهٔ پل مرجع `843cfb3`) ساخته می‌شود. فرمان و پاسخ فقط در `marketing-browser-bus/` روی `arena/01a0d4ee-bazino-gamenet-portal` ردوبدل می‌شود؛ `agent-session.cjs` نیز تنها همین شاخه را پوش می‌کند. این پل برای یک جلسهٔ کوتاهِ اتصال است، نه برنامهٔ بازاریابی دائمی.

## ساخت و اجرای مالک

1. مالک workflow `.github/workflows/build-studio-bridge.yml` را روی **شاخهٔ ثابت این نشست** ساخته است؛ الگوی آن [`workflow-build-studio-bridge.yml`](workflow-build-studio-bridge.yml) است. اجرای نخست و تلاش‌های مجدد تا ۲۰۲۶-۰۹-۲۶ در آپلود artifact به سهمیهٔ GitHub خوردند؛ هنوز EXE قابل دانلود از Actions تأیید نشده است. **جایگزین محلی:** از پوشهٔ `desktop-app` روی Windows با `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-local.ps1 -Target Bridge -InstallPs2Exe` فایل `dist\local\BazinoStudioBridge.exe` را بسازید (آزمون واقعی این مسیر روی Windows هنوز انجام نشده است؛ [راهنمای ساخت محلی](../README.md)). یا پس از رفع سهمیه و موفقیت اجرای مجدد، artifact **`BazinoStudioBridge-Windows`** را دانلود کنید. این نسخه امضای دیجیتال ندارد؛ سورس و هش SHA256 چاپ‌شده را بررسی کنید.
2. در پل قبلی **Disconnect** بزنید ولی Chrome با پروفایل اختصاصی/پورت ۹۳۳۴ را باز نگه دارید. `BazinoStudioBridge.exe` را روی **همان حساب Windows** باز کنید و **Connect** را بزنید.
3. برنامه توکن GitHub قبلی را فقط از DPAPI همان کاربر در `%APPDATA%\BazinoBridge\settings.json` می‌خواند؛ این مسیر قدیمی صرفاً برای استفادهٔ دوباره از تنظیماتِ امن است، عنوان/نام/لاگ پل جدید مستقل‌اند. اگر تنظیم موجود نیست، توکن Fine-grained مخصوص همین ریپوی private با `Contents: Read and write` را **فقط در Settings برنامهٔ خودتان** وارد کنید؛ هیچ توکن یا کد ورود را در چت نفرستید. اگر Chrome بسته باشد، با همان پروفایل جداگانه باز می‌شود.
4. بعد از سبز شدن وضعیت GitHub/Chrome/Bridge، فقط خبر بدهید «پل شاخهٔ نشست Connect شد». ایجنت از **فایل `marketing-browser-bus/status.json` و تازه‌بودن heartbeat** اتصال را مستقلاً بررسی می‌کند و سپس روی مرورگر شما کار می‌کند. اگر پروژهٔ GitHub/Kling نیاز به ورود یا consent داشت، خودتان فقط در مرورگر Windows انجام دهید؛ ایجنت رمز، کوکی و authorization code را نخواهد دید/در Git ثبت نخواهد کرد.

**توقف:** دکمهٔ Disconnect یا بستن پنجره. پل پس از ۳۰ دقیقه خودکار قطع می‌شود تا تاریخچهٔ Git با ضربان دائمی پر نشود؛ برای ادامه Connect را دوباره بزنید.

## امنیت و محدودیت مهم

- این انتقالِ CDP **رمزگذاری انتها‌به‌انتها ندارد** و فرمان/پاسخ در تاریخچهٔ ریپوی private باقی می‌مانند. برای ورود رمز، چالش دو‌مرحله‌ای و اجازهٔ OAuth، مالک خودش در مرورگر عمل می‌کند؛ با این کانال **هیچ کوکی، کد OAuth، token، متن خصوصی حساب، HTML کامل صفحه یا screenshot** خوانده/منتقل نمی‌شود. پاسخ‌های بزرگ یا دارای الگوی راز در Windows بلوکه می‌شوند.
- برنامه هنگام public شدن ریپو اتصال را رد می‌کند. ایجنت هرگز شاخهٔ `cdp-bus` را نمی‌نویسد. این پل فقط کمک می‌کند **workflow را در GitHub UI روی شاخهٔ مجاز** ایجاد کنیم؛ ساخته‌شدن فایل workflow، موفق‌شدن build و تکمیل OAuth را جداگانه با شاهد بررسی می‌کنیم.
- برنامهٔ بازاریابی مستقل و Kling MCP در [`../README.md`](../README.md) توضیح داده شده‌اند. پس از ساخت بستهٔ Windows، برای خواندن پاسخ خصوصی Kling از ارتباط **رمزگذاری‌شدهٔ خود برنامه** استفاده می‌شود، نه این CDP bus خام.
