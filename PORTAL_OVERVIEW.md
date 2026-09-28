# پورتال بازی‌نو (BAZINO Pro) — شرح کامل امکانات و ساختار

**ریپو:** `paymanshafayan/bazino-gamenet-portal`
**برنچ بررسی‌شده:** `arena/01a0a0e1-bazino-gamenet-portal` (کامیت `ec979b3`)
**تاریخ بررسی:** 2026-09-17
**دامنه پروداکشن:** `bazino.pro` — **دیپلوی:** Railway (هر push روی این برنچ = دیپلوی پروداکشن)

> این سند خلاصه‌ای است از بررسی کامل ریپوی پورتال — برای استفاده تیم کمپین تبلیغاتی، تا بدانیم پورتال چه امکاناتی می‌دهد، چطور کار می‌کند و کمپین روی کدام قابلیت‌ها می‌تواند تکیه کند.
> سند خواهر: [`PORTAL_SERVER_AND_BROWSER_BRIDGE.md`](PORTAL_SERVER_AND_BROWSER_BRIDGE.md) — رویه راه‌اندازی سرور، Chromium و پل مرورگر.

---

## ۱. پورتال در یک نگاه

**BAZINO Pro** یک سامانه جامع مدیریت کلوپ گیمینگ (گیم‌نت) است — نه فقط یک وب‌سایت. شامل چهار محصول نرم‌افزاری مرتبط:

| محصول | فناوری | نقش |
|---|---|---|
| **وب‌سایت + API** | React 19 + Express + Vite | سایت عمومی، پنل کاربر، پنل ادمین، همه APIها |
| **اپ مدیریت** (`Management App/Bazino`) | React + Vite (پروژه مستقل) | نرم‌افزار دسکتاپ مدیریت سالن — صندوق، نوبت، کیف پول |
| **اپ موبایل** (`flutter_app`) | Flutter | اپ مشتری |
| **اپ والدین** (`flutter_parent_app`) | Flutter | اپ نظارت والدین — **در حال توسعه** |
| **گیت‌وی تلگرام** (`telegram-gateway`) | Python + Docker | سرویس جدا برای کمپین تلگرام |

---

## ۲. امکانات اصلی (سمت مشتری)

| بخش | توضیح |
|---|---|
| 🖥️ **رزرو سیستم** | رزرو آنلاین PC / کنسول با تعیین ساعت و محاسبه قیمت؛ چک تداخل زمانی؛ چک‌این حضوری |
| ☕ **کافه و بوفه** | سفارش آنلاین غذا و نوشیدنی، تحویل پشت سیستم، کسر موجودی |
| 🛒 **فروشگاه جانبی** | تجهیزات گیمینگ با گارانتی کلوپ |
| 🏆 **مسابقات** | تورنمنت‌های CS2، Dota 2، FIFA و… با ثبت‌نام تیمی، براکت، رتبه‌بندی فصلی |
| 🎮 **صفحه بازی‌ها** (`/games`) | کاتالوگ بازی‌های موجود |
| ⭐ **باشگاه وفاداری** | امتیازدهی و تبدیل امتیاز به کد تخفیف |
| 👛 **کیف پول بازینو** | شارژ **فقط حضوری** از اپ مدیریت/ادمین، هرگز منفی نمی‌شود |
| 💳 **پرداخت در محل** | مهلت ۱۰ دقیقه (رزرو) / ۴۸ ساعت (تورنمنت) با ابطال خودکار؛ بوفه و فروشگاه فقط در محل |
| 🪙 **کردیت بازینو (BC)** | پرداخت رزرو با کردیت به نرخ تخت زمانی + قیمت کردیتی محصولات کاتالوگ |
| 🌐 **درگاه آنلاین PayTR** | پیاده‌شده ولی **پیش‌فرض خاموش** (`PAYMENT_ONLINE_ENABLED`) |
| 📱 **ورود با پیامک (OTP)** | چند درایور: SMS.to، EasySendSMS، Messaggio، mock |
| 👤 **پروفایل کاربر** (`/profile`) | کیف پول، امتیاز، رزرو، سفارش، تورنمنت، پشتیبانی، امنیت |
| 🎫 **تیکت پشتیبانی** | سیستم تیکتینگ کامل |
| 💬 **چت زنده** | اتاق‌های گفتگو با WebSocket — **فعلاً غیرفعال** (پرچم `chat_enabled`) |
| 📰 **بلاگ** | مقالات، دسته‌بندی، کامنت، لایک |
| 📲 **دانلود اپ موبایل** | صفحه دانلود + QR + مدیریت APK از پنل |
| 🌍 **چندزبانه** | فارسی، انگلیسی، روسی، ترکی — تشخیص خودکار زبان از IP (`geoip-lite`) |

---

## ۳. پنل ادمین — بازطراحی v2

پنل قبلاً ۱۳ تب تخت بود («شیر تو شیر» به تعبیر کارفرما). در نسخه ۲ به **۶ گروه منطقی + جستجوی زنده** تبدیل شد:

| گروه | بخش‌ها |
|---|---|
| 🏠 **داشبورد** | آمار و وضعیت سالن |
| 🎮 **سالن** | سیستم‌ها، تورنمنت‌ها، عملیات تورنمنت، کافه، فروشگاه |
| 👥 **مشتریان** | کیف پول، پروموشن، همکاران فروش، پیام‌رسانی، پیام‌ها، چت، تیکت‌ها |
| 📣 **محتوا** | استودیوی محتوا، بلاگ، اسلایدر اپ |
| 🎨 **سایت** | قالب‌ها، سفارشی‌سازی، دانلود اپ، پرزنتیشن |
| 🤖 **فنی** | جارویس (AI)، مرکز کلیدها، لاگ دیتابیس، مهاجرت‌ها |

### ۳.۱ مرکز کلیدها (`/admin/apiKeys`)

قبلاً ۱۱ کلید API در ۴ تب مختلف پراکنده بود. حالا همه در یک صفحه با دسته‌بندی:

- **پیامک:** `SMS_PROVIDER`, `MESSAGGIO_PROJECT_LOGIN`, `MESSAGGIO_SENDE_CODE`
- **اینستاگرام:** `ZERNIO_API_KEY`, `ZERNIO_IG_ACCOUNT_ID`, `ZERNIO_WEBHOOK_SECRET`, `IG_INGEST_TOKEN`
- **تلگرام:** `TG_GATEWAY_URL`, `TG_GATEWAY_BEARER`, `TG_GATEWAY_HMAC_SECRET`
- **هوش مصنوعی:** `GROQ_API_KEY`, `OPENROUTER_API_KEY`, `OPENAI_API_KEY`, `MANUS_API_KEY`

هر کلید: input ماسک‌شده + دکمه نمایش/مخفی + وضعیت (ست شده/نشده) + لینک راهنما. ذخیره از طریق SecretVault رمزگذاری‌شده با env fallback.

### ۳.۲ استودیوی انتشار v2

قبلاً ۱۳ تب داخلی شلوغ بود؛ حالا **۴ کارت بزرگ**. شامل: کتابخانه رسانه، Composer (تصویر/ویدئو/Carousel)، صف انتشار، گزارش‌ها، صندوق ورودی اینستاگرام، حالت دستی/عامل.

---

## ۴. 🤖 جارویس — دستیار هوشمند ادمین

مهم‌ترین قابلیت جدید پورتال. یک دستیار AI که می‌تواند کارهای واقعی ادمین را انجام دهد.

### موتور و زنجیره ارائه‌دهنده

```
Groq (اصلی) → OpenRouter (پشتیبان ۱) → OpenAI (پشتیبان ۲)
```

- **Groq** پیش‌فرض: `llama-3.3-70b-versatile` (اصلی) و `llama-3.1-8b-instant` (سبک/پشتیبان)، سقف ۸۰۰ فراخوانی/روز
- پشتیبان‌ها **فقط وقتی فعال می‌شوند که Groq جواب ندهد** (429، سقف روزانه، خطای شبکه، کلید نامعتبر، 5xx)
- پشتیبان‌ها **فقط به امور پشتیبانی** دسترسی دارند (۱۳ مهارت) — نه بازاریابی
- Circuit breaker ۶۰ ثانیه‌ای per-engine؛ شمارنده روزانه جدا برای هر ارائه‌دهنده

### ۳۶ مهارت در سه لایه ریسک

| لایه | رفتار | نمونه مهارت‌ها |
|---|---|---|
| **read** (۲۱ مهارت) | اجرای خودکار | `portal_stats`, `portal_health`, `list_reservations`, `search_user`, `list_tickets`, `list_cafe_menu`, `list_tournaments`, `list_coupons`, `list_transactions`, `list_articles`, `ig_inbox_summary`, `publishing_report`, `audit_recent`, `chat_rooms` |
| **write** | اجرای خودکار + ثبت audit | `send_user_message`, `create_content_draft` |
| **sensitive** | **هرگز از چت اجرا نمی‌شود** → صف تأیید انسانی | `adjust_credits`, `update_cafe_item`, `create_coupon`, `answer_ticket`, `publish_content`, `send_ig_reply`, `create_tournament`, `create_article`, `create_app_slider`, `delete_app_slider` |

### منع‌های طراحی‌شده (تست گارد دارد)

جارویس **هرگز** به این‌ها دسترسی ندارد: کلید/توکن/سکرت، مدیریت استف و اپراتور، رمز و سطح دسترسی، ریست یا پاک‌کردن دیتابیس، تعویض data-source، تنظیمات درگاه پرداخت.

### اتوماسیون‌های زمان‌بندی‌شده

- **بریف روزانه ۹:۰۰** (وقت قبرس): خلاصه + ۳ ایده محتوا → پیشنهاد پیش‌نویس در صف تأیید
- **دایجست هفتگی** دوشنبه ۱۰:۰۰
- **پیشنهاد پاسخ دایرکت** برای ig-inbox بی‌پاسخ (≤۵/روز، صف تأیید)
- **پیشنهاد پاسخ تیکت** (≤۵/روز؛ فقط FAQ مطمئن با تاگل ارسال خودکار)

### نظارت (monitor)

snapshot سلامت: صف/خطای انتشار Zernio، healthz گیت‌وی تلگرام، آپ‌تایم/حافظه/تأخیر DB پورتال + هشدارها (`OUTBOX_FAILURES`, `TELEGRAM_GATEWAY_DOWN`, `DB_SLOW`) + تاریخچه غلتان.

---

## ۵. 📸 سیستم Affiliate اینستاگرام (مستقیماً مرتبط با کمپین ما)

> ⚠️ **این بخش برای تیم کمپین حیاتی است — فلو در ۲۰۲۶-۰۹-۱۴ کاملاً عوض شد.**

### ۵.۱ اصل معماری: Media-ID-only

پورتال **مستقیماً به Meta یا Instagram API وصل نمی‌شود.** یک ناشر بیرونی (Zernio / Manus) عملیات اینستاگرام را انجام می‌دهد و فقط `media_id` را به پورتال می‌دهد. پورتال مغز تصمیم و منبع حقیقت داده‌های Affiliate است؛ ناشر فقط «دست» است.

### ۵.۲ فلو فعلی (v2) — ۸ مرحله

1. همکار کلیدواژه تعریف‌شده را زیر آخرین ریلز/پست کامنت می‌کند
2. سیستم Private Reply عمومی می‌دهد (فقط راهنما + دکمه — **هیچ لینکی در PR نیست**) و می‌گوید `@bazinopro` را فالو کند
3. همکار پیج را فالو می‌کند
4. همکار روی دکمه «فالو دارم» می‌زند (`action=partner_follow_check`)
5. سیستم فالو را چک می‌کند → اگر OK، **لینک دعوت اختصاصی reusable** را در DM همکار می‌فرستد (اعتبار ۳۶۵ روز)
6. همکار لینک را بین دوستانش پخش می‌کند
7. دوست از طریق لینک (`/ig/invite?token=…`) ثبت‌نام می‌کند → کوپن `IG-{hex}` مستقل می‌گیرد
8. به‌ازای هر پرداخت دوست، همکار پورسانت می‌گیرد

### ۵.۳ ⚠️ آنچه از فلو قدیمی حذف شد

| فلو قدیمی (بازنشسته) | وضعیت |
|---|---|
| ارسال پیام و پست توسط سیستم برای دوست | ❌ حذف شد |
| کامنت‌کردن کد عددی همکار توسط دوست | ❌ حذف — اگر کامنت شود → پیام `friend_flow_retired` |
| دریافت لینک دعوت توسط دوست از سیستم | ❌ حذف — دوست فقط از لینک همکار وارد می‌شود |

> **🔴 نکته بسیار مهم برای تیم کمپین:** سند استراتژی ما (`Doc/affiliate-instagram-plan-fa.md`) هنوز **فلو قدیمی** را توصیف می‌کند (کامنت کد عددی دوست، `share_confirmed_by_friend_code`). کد پورتال آن فلو را بازنشسته کرده است. این تناقض باید رفع شود.

### ۵.۴ نکات امنیتی فلو

- لینک واقعی فقط هنگام dispatch ساخته می‌شود؛ در DB و لاگ فقط placeholder `{{invite_url}}` ذخیره می‌شود
- وب‌هوک‌های Zernio با HMAC روی raw body امضا می‌شوند
- endpoint قدیمی `partner-invite` و simulator → HTTP 410

### ۵.۵ نقاط اتصال API

| مسیر | نقش |
|---|---|
| `POST /api/integrations/instagram/published-media` | ثبت `media_id` منتشرشده (توکن ingest + Idempotency-Key) |
| `POST /api/webhooks/zernio` · `/api/integrations/zernio/webhook` | رویدادهای ناشر (HMAC) |
| `POST /api/webhooks/zernio/analytics` | آنالیتیکس (stream و secret جدا) |
| `/ig/invite/:id` | Gate واقعی دوست، consent، ورود، کوپن |
| `/api/affiliate/click` · `/lookup` · `/claim` | انتساب و مطالبه |
| `/api/me/affiliate` | داشبورد همکار |
| `/api/admin/affiliates/commissions/:id/approve|reject` | تأیید/رد کمیسیون |

---

## ۶. 🎨 موتور قالب (Theme Engine)

قابلیت قابل‌توجهی که به تیم بازاریابی اجازه می‌دهد ظاهر سایت را بدون کدنویسی عوض کند.

- هر قالب یک پکیج **ZIP**: `theme.json` + `theme.css` اجباری، `theme.js` اختیاری، `assets/`
- نصب مستقیم از پنل ادمین (`POST /api/admin/themes/install`)
- **SDK کامپوننت v2:** `theme.js` می‌تواند هر بخش سایت را جداگانه جایگزین کند — `header`, `hero`, `home.*`, `footer`, `mobileNav`
- رنگ و فونت از طریق متغیرهای CSS روی `body[data-theme]` (`--primary-color`, `--bz-*`)
- متن‌های قالب چهارزبانه در `theme.json.strings`
- اسلایدهای ادمین چهارزبانه (عنوان + توضیح)
- ⚠️ فونت تیتر باید برای فارسی/روسی fallback داشته باشد (Orbitron حروف فارسی ندارد)

قالب‌های موجود در ریپو: `DarkGoldHome`, `GamingAmpHome`, `GecoPurpleHome`, `ConsoleGridClassic`, `ConsoleHubView`, `CyberUI`

---

## ۷. ساختار فنی

### ۷.۱ درخت پروژه

```
bazino-gamenet-portal/
├── server.ts                  ← سرور Express + WebSocket (فایل بزرگ، ~۲۴۳KB)
├── server/                    ← ماژول‌های بک‌اند
│   ├── dataProviders.ts       ← لایه انتزاعی دیتابیس
│   ├── sampleData.ts          ← داده نمونه
│   ├── affiliate/             ← engine, campaignV4, friendGate, igEngine, zernio, awayPolicy
│   ├── jarvis/                ← skills, monitor (+ config, agent, approvals, automation, chain, incidents)
│   ├── management/            ← core, bookings, orders, finance, reports, sessions, content, …
│   ├── publishing/            ← queue, publish, agents, assets, webhooks, registry, manus
│   ├── payments/              ← paytr, routes
│   ├── wallet/                ← routes
│   ├── sms/                   ← درایورهای پیامک
│   ├── manus/                 ← gateway, policy
│   └── parent.ts              ← API اپ والدین
│
├── src/                       ← فرانت‌اند React 19
│   ├── components/            ← ~۴۰ کامپوننت اصلی + admin/ affiliate/ profile/ tournaments/
│   ├── themes/                ← موتور قالب
│   ├── themeSdk/              ← SDK کامپوننت قالب v2
│   ├── context/ services/ utils/ types/ legal/
│
├── shared/                    ← کد مشترک بین سایت و اپ مدیریت (Jarvis.tsx, StudioV2.tsx, messages.ts)
├── Management App/Bazino/     ← اپ مدیریت (پروژه Vite مستقل)
├── flutter_app/               ← اپ موبایل مشتری
├── flutter_parent_app/        ← اپ والدین (در حال توسعه)
├── telegram-gateway/          ← سرویس Python + Docker
├── cdp-tools/                 ← پل CDP مرورگر (relay.js, agent.js, bridge-*.ps1)
├── tests/                     ← سوئیت تست (unit, db, api, ui, providers) + e2e-browser/
├── docs/                      ← ops/, payments/, publishing/, management/, sms/, manus/
└── theme-packages/ hub/ public/ scripts/ visual-testing/
```

### ۷.۲ پشته فناوری

| لایه | فناوری |
|---|---|
| فرانت‌اند | React 19 + Vite 6 + Tailwind 4 + Motion + lucide-react |
| بک‌اند | Node 22 + Express 4 + TypeScript 5.8 + WebSocket (`ws`) |
| دیتابیس | `better-sqlite3` (پیش‌فرض) · MongoDB · MSSQL |
| احراز هویت | JWT + bcryptjs + OTP پیامکی |
| رسانه | `sharp` (تصویر) + ffmpeg/ffprobe (ویدئو) |
| AI | Groq / OpenRouter / OpenAI + `@google/genai` (ترجمه) |
| ابزار | esbuild (باندل سرور)، tsx (اجرا)، Playwright (تست مرورگر) |

### ۷.۳ لایه دیتابیس — ⚠️ هشدار مهم

سایت صفحه نصب شبیه nopCommerce دارد (`InstallPage.tsx` + `POST /api/install/setup`) با انتخاب بین SQLite / SQL Server / MongoDB.

| Provider | وضعیت واقعی |
|---|---|
| `SQLiteDataProvider` | ✅ **واقعی** |
| `SQLServerDataProvider` | ❌ **نمایشی** — لاگ‌های باورپذیر MSSQL می‌سازد ولی داخلش SQLite است |
| `MongoDBDataProvider` | ❌ **نمایشی/شبیه‌سازی‌شده** |

اگر `MONGO_URL` ست شود، سرور بدون پنل نصب مستقیماً به MongoDB وصل می‌شود (مسیر جدا از provider بالا).

### ۷.۴ API — ۱۵۶ روت

| پیشوند | تعداد | نمونه |
|---|---|---|
| `/api/admin/*` | ۵۳ | `stats`, `users`, `systems`, `cafe`, `tournaments`, `themes`, `wallet/adjust`, `credits/adjust`, `affiliates`, `api-tokens`, `db-logs`, `translate` |
| `/api/sync/*` | ۱۲ | همگام‌سازی با اپ مدیریت: `reservations`, `wallet/:phone`, `onsite-orders`, `logs` |
| `/api/tournaments/*` | ۷ | ثبت‌نام، براکت، تیم |
| `/api/payments/*` | ۷ | PayTR: `create`, `callback`, `mock` |
| `/api/themes/*` | ۵ | نصب، فعال‌سازی، لیست |
| `/api/checkout/*` | ۴ | `wallet`, `credits`, `onsite` |
| `/api/auth/*` | ۴ | `login`, `logout`, `register`, `me` |
| `/api/management/*` | + | جارویس و گزارش‌ها زیر `/api/management/jarvis/*` |
| سایر | — | `reservations`, `cafe`, `accessories`, `articles`, `coupons`, `loyalty`, `me/wallet`, `geo/lang`, `instagram/invites`, `webhooks`, `install`, `desktop`, `mobile-app` |

---

## ۸. تست و کیفیت

| سوئیت | دستور |
|---|---|
| همه | `npm test` (unit, db, providers, api, ui) |
| API/E2E | `npm run test:api` (سرور واقعی روی پورت 3457) |
| تایپ‌چک | `npm run lint` |
| قالب | `npx tsx scripts/verify-themes.ts` |
| مرورگری | هارنس `tests/e2e-browser/` با Chromium واقعی |

**آخرین وضعیت گزارش‌شده:** ~۵۰۶–۶۰۷ تست موفق (بسته به نشست)؛ ۶ خطای محیطی pre-existing (۵ ffprobe + ۱ payment). build حدود ۱.۱MB.

**تست‌های انسانی اخیر:** ۲۴ بخش پنل ادمین با Chromium واقعی (۰ خطا) + تست جارویس با ۳۴ مهارت شامل ساخت واقعی مسابقه، مقاله و اسلایدر.

CI: ورک‌فلو GitHub Actions در `ci/build-test.workflow.yml`.

---

## ۹. متغیرهای محیطی کلیدی

| متغیر | توضیح |
|---|---|
| `JWT_SECRET` | کلید امضای توکن — **در production الزامی** |
| `APP_URL` | آدرس عمومی برنامه |
| `BAZINO_DATA_DIR` | پوشه داده ماندگار (قالب‌ها، SQLite، APK). روی Railway یک Volume با مسیر `/data` بسازید و این را `/data` بدهید — **وگرنه قالب‌ها با هر دیپلوی پاک می‌شوند** |
| `MONGO_URL` | اتصال مستقیم به MongoDB بدون پنل نصب |
| `SMS_PROVIDER` | `smsto` / `easysendsms` / `messaggio` / `mock` (پیش‌فرض — فقط لاگ، برای production مناسب نیست) |
| `PAYMENT_ONLINE_ENABLED` | پیش‌فرض خاموش؛ با `1` درگاه PayTR فعال می‌شود |
| `GEMINI_API_KEY` | ترجمه خودکار در پنل ادمین |
| `GROQ_API_KEY` | جارویس (اصلی) |
| `OPENROUTER_API_KEY` · `OPENAI_API_KEY` | جارویس (پشتیبان) |

---

## ۱۰. ⚠️ شکاف‌ها و نکات باز

1. **Providerهای SQL Server و MongoDB نمایشی‌اند** — لاگ واقعی می‌سازند ولی داده در SQLite می‌رود
2. **قوانین منطق تجاری سمت سرور بازمحاسبه نمی‌شوند** — طبق `ARCHITECTURE.md` بندهای لویالتی، اعتبارسنجی کد تخفیف، چک رزرو هم‌پوشان و کسر موجودی یا اجرا نمی‌شوند یا از مقادیر ارسالی خود کلاینت (`req.body.totalPrice`, `pointsEarned`) استفاده می‌کنند بدون بازمحاسبه سروری. **این یک شکاف امنیتی است**
3. **چت زنده غیرفعال است** (از منو حذف شده)
4. **اپ والدین هنوز کامل نیست** — در حالی که PHASE 1 کمپین ما روی آن بنا شده
5. **درگاه آنلاین خاموش است** — پرداخت فقط کیف پول یا در محل
6. **استقرار production برخی قابلیت‌های V4 تأیید نشده** — تست‌ها محلی بوده‌اند
7. **پرونده سخت‌افزار POS متوقف است**
8. **تست LLM در سندباکس ممکن نیست** — egress به Groq/OpenRouter/OpenAI بسته است؛ همه تست‌ها mock، اولین تست واقعی روی Railway

---

## ۱۱. جمع‌بندی برای تیم کمپین

**آنچه پورتال امروز به کمپین می‌دهد:**

| نیاز کمپین | پشتیبانی پورتال |
|---|---|
| لینک ارجاع با `?ref=` و UTM | ✅ کامل — انتساب، کلیک، Lead، رزرو، کمیسیون |
| کوپن یک‌بارمصرف برای دوست | ✅ `IG-{hex}` مستقل برای هر دوست |
| داشبورد همکار | ✅ `/api/me/affiliate` |
| تأیید و تسویه کمیسیون | ✅ پنل ادمین با approve/reject |
| ثبت پست منتشرشده اینستاگرام | ✅ Media-ID-only + Idempotency |
| صندوق ورودی و پاسخ دایرکت | ✅ از استودیوی انتشار + جارویس |
| تولید و زمان‌بندی محتوا | ✅ Composer + صف انتشار + بریف روزانه AI |
| سیستم رتبه‌بندی BAZINO CLUB | ⚠️ وفاداری و امتیاز هست، ولی **پنج رتبه KING…BARON پیاده نشده** |
| اپ نظارت والدین | ⚠️ در حال توسعه |

**سه اقدام فوری پیشنهادی:**

1. **رفع تناقض فلو Affiliate** — سند `Doc/affiliate-instagram-plan-fa.md` ما فلو قدیمی (کامنت کد عددی دوست) را توصیف می‌کند که پورتال بازنشسته کرده. سند باید به فلو v2 (لینک مستقیم همکار پس از فالو-چک) به‌روزرسانی شود.
2. **تعیین وضعیت اپ والدین** — قبل از هر تبلیغی در PHASE 1.
3. **تصمیم درباره BAZINO CLUB** — اگر پنج رتبه قرار است تبلیغ شود، باید ابتدا در پورتال پیاده شود.
