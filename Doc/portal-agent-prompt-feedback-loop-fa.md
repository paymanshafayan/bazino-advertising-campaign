# پرامپت ایجنت پورتال — حلقه بازخورد و یادگیری روزانه محتوا

**نسخه:** ۲ (۲۰۲۶-۰۹-۱۷) — پوشش هر سه کانال: اینستاگرام، تلگرام، بلاگ
**مقصد:** ایجنت ریپوی `bazino-gamenet-portal`
**مرجع طراحی:** `DAILY_FEEDBACK_SYSTEM.md`
**مرجع قواعد:** `skills/bazino-advertising-growth-agent/SKILL.md` (اسکیل پروژه) — به‌ویژه §۵-A، §۵-B، §۵-C، §۱۷، §۱۸، §۲۳، §۲۶، §۲۷، §۲۸
**سند اجرایی تلگرام:** `Doc/telegram-daily-campaign-complete-fa.md`

> ⚠️ برنچ پورتال مستقیماً روی Railway دیپلوی می‌شود. هر push یعنی production.

---

### PROMPT — Daily Content Feedback & Learning Loop

**زمینه:** ایجنت تبلیغات بازینو روی **سه کانال** کار می‌کند — اینستاگرام، تلگرام و بلاگ bazino.pro — و موظف است حلقه `Publish → Measure → Learn → Improve` را روی هر سه اجرا کند. اما پورتال امروز داده‌ای برای تغذیه این حلقه تولید نمی‌کند: متریک اینستاگرام از Zernio می‌آید ولی **تاریخچه‌اش نگه داشته نمی‌شود**، متریک تلگرام **اصلاً جمع‌آوری نمی‌شود**، متریک بلاگ به محتوا وصل نیست، و Manus فقط برای تولید محتوا استفاده می‌شود نه تحلیل عملکرد.

**آنچه از قبل موجود است (دوباره نساز):**
- `server/publishing/webhooks.ts` — دریافت `analytics.synced`، کشیدن `/v1/analytics/delta` با cursor، ذخیره در `pub-metrics`
- `server/publishing/manus.ts` — `ManusClient` با `task.create` و پشتیبانی از `structured_output_schema`
- `server/publishing/registry.ts` — نگاشت `platformPostId` به محتوا
- `server/publishing/reports.ts` — گزارش کمپین با فیلد `insights`
- الگوی `setInterval` در `publicationRoutes.ts:59` و `routes.ts:42`
- `telegram-gateway/` — FastAPI با `dialogs`, `permissions`, `search`, `send`

**طبق §۲۷ اسکیل (API-First): از زیرساخت موجود استفاده کن، سیستم دوم نساز.**

#### کاری که باید انجام شود

**بچ ۱ — تاریخچه متریک (اولویت ۱)**

1. جدول جدید `content_daily_metrics` با کلید یکتای `(content_id, platform, date)` بساز. `pub-metrics` را بازنویسی نکن؛ به‌جای آن هر sync یک **snapshot روزانه** هم بنویس تا تاریخچه حفظ شود.
2. فیلدها: `content_id`, `platform`, `date`, `reach`, `impressions`, `views`, `shares`, `comments`, `likes`, `saved`, `follows`, `link_clicks`, `synced_at`, `source`.
3. جدول `content_metrics_delta` برای تغییر نسبت به روز قبل و پایه ۷ و ۳۰ روزه.
4. ابعاد تحلیل را به رکورد محتوا اضافه کن: `channel` (instagram/telegram/blog), `content_pillar`, `language`, `format`, `hook_type`, `posting_time`, `mona_featured` (boolean), `cta_type`. اگر جای مناسبی در `pub-media` هست همان‌جا، وگرنه جدول جانبی.

**بچ ۲ — متریک تلگرام (اولویت ۱)**

5. در `telegram-gateway/src/adapter.py` متد جدید `post_metrics(dialog_id, message_ids)` اضافه کن که با Telethon مقادیر `views`, `forwards`, `reactions` را برای پیام‌های کانال برمی‌گرداند.
6. endpoint متناظر در `app.py`: `GET /internal/dialogs/{dialog_id}/messages/metrics` با همان احراز Bearer موجود.
7. در سمت پورتال، این متریک‌ها را در همان `content_daily_metrics` با `platform='telegram'` ذخیره کن.
8. ⚠️ اگر Telethon برای نوع خاصی از dialog متریک نمی‌دهد، وضعیت را `unsupported` ثبت کن — **هرگز صفر ننویس**، چون صفر با «نبود داده» اشتباه گرفته می‌شود.

**بچ ۲.۵ — متریک بلاگ و آمار outreach تلگرام (اولویت ۲)**

8.1. بررسی کن پورتال چه آنالیتیکس سایتی دارد. اگر دارد، pageviews، بازدیدکننده یکتا، میانگین زمان ماندگاری و منبع ترافیک هر مقاله بلاگ را با `platform='blog'` در همان `content_daily_metrics` بنویس. **اگر آنالیتیکس سایت وجود ندارد، این را صریحاً گزارش کن و چیزی از خودت نساز** — در آن صورت بلاگ فعلاً `data_completeness=missing` می‌ماند.

8.2. برای کمپین محلی تلگرام (`telegram-local-gaming`) یک جدول شمارشی روزانه بساز: `destinations_evaluated`, `destinations_eligible`, `messages_sent`, `no_destination_found` (bool), `rejection_reasons` به تفکیک (`not_member`, `no_permission`, `no_context`, `already_used`), `approvals_invalidated_by_hash`, `flood_wait_events`. این داده در جریان draft/approval موجود است و فقط باید تجمیع شود.

⚠️ **معیار موفقیت این جریان «تعداد ارسال» نیست.** روزی که هیچ مقصد واجد شرایطی پیدا نشود یک نتیجه درست است. این را در داده به‌صورت `no_destination_found=true` ثبت کن، نه به‌صورت صفرِ عملکرد.

**بچ ۳ — Job جمع‌آوری روزانه (اولویت ۱)**

9. یک job زمان‌بندی‌شده روزانه ساعت **۰۸:۳۰ به وقت قبرس** بساز (همان الگوی dateKey قبرس که در جارویس استفاده شده) که: برای هر محتوای منتشرشده فعال ۳۰ روز گذشته، snapshot متریک را بگیرد و delta محاسبه کند.
10. به‌جای اتکای صرف به وب‌هوک، یک **کشیدن فعال** از `/v1/analytics/delta` هم انجام بده (وب‌هوک ممکن است نرسد).
11. Idempotency با کلید `metrics-snapshot:{date}`.
12. خطا نباید تایمر را بکشد؛ خطا ثبت و job ادامه پیدا کند.

**بچ ۴ — تحلیل Manus (اولویت ۲)**

13. سرویس جدید `server/publishing/feedback.ts` که ساعت **۰۹:۰۰ قبرس** جدول عملکرد ۲۴ ساعت + پایه ۷/۳۰ روزه را به Manus می‌فرستد.
14. از `ManusClient.create()` با `structured_output_schema` استفاده کن — schema دقیقاً مطابق قرارداد JSON بالا (`data_completeness` با هر سه کانال، `telegram_outreach`, `top_performers`, `underperformers`, `patterns`, `recommendations`, `uncertainties`).
15. **گاردهای الزامی روی خروجی:**
    - هر `pattern` باید `classification` داشته باشد ∈ `verified_fact|observed_trend|interpretation|assumption`
    - اگر `sample_size < 3` → `confidence` را به `low` تنزل بده
    - اگر `data_completeness.{platform}` برابر `missing` بود → الگوهای مربوط به آن پلتفرم را **رد کن**
    - خروجی حاوی عدد تخفیف، جایزه یا تاریخ تأییدنشده → رد و ثبت به‌عنوان نقض
    - ⚠️ توصیه‌ای که بگوید سقف ۱–۲ مقصد روزانه تلگرام بالا برود، گیت‌های عضویت/مجوز/context شل شوند، یا برای پوشش بیشتر به مقصد بی‌ربط پیام برود → **رد شود**. این‌ها قید ایمنی حساب‌اند، نه پارامتر قابل بهینه‌سازی
    - روزی با `no_destination_found=true` نباید underperformance تفسیر شود
16. **هیچ داده شخصی در prompt نرود** — فقط عدد تجمیعی و متادیتای محتوا.
17. Idempotency با کلید `analysis:{date}`.

**بچ ۵ — حافظه یادگیری و API (اولویت ۲)**

18. جدول `learning_memory` با سه دسته §۱۸ اسکیل: `what_worked`, `what_underperformed`, `what_remains_uncertain`. هر رکورد با شواهد (`content_ids`), `confidence`, `first_observed`, `last_confirmed`.
19. اگر یک الگو در چند روز تکرار شد، `confidence` بالا برود؛ اگر نقض شد، به `uncertain` منتقل شود.
20. endpointهای جدید:
    - `GET /api/publishing/feedback/daily?date=` — تحلیل روز
    - `GET /api/publishing/feedback/learning` — حافظه یادگیری
    - `GET /api/publishing/feedback/metrics?content_id=` — تاریخچه یک محتوا
    - `POST /api/publishing/feedback/run` — اجرای دستی (فقط ادمین)
21. ⚠️ این endpointها باید **مستقل از جارویس** باشند. طبق §۳ اسکیل ایجنت تبلیغات نباید از مسیر جارویس عبور کند.

#### قیدهای الزامی

- **فقط خواندن و تحلیل.** این سیستم هیچ مسیری به انتشار خودکار ندارد.
- **شکست بی‌صدا ممنوع.** اگر Zernio یا Manus یا گیت‌وی پاسخ ندهد، در خروجی صریحاً `missing` ثبت شود، نه بخش خالی.
- **بدون کلید در prompt** (§۲۸).
- **تفکیک واقعیت از تفسیر** در کل مسیر داده (§۴).
- egress سندباکس به `api.manus.ai` بسته است → تست‌ها با mock با شکل دقیق پاسخ Manus؛ اولین تست واقعی روی Railway.

#### تست مورد انتظار

- snapshot روزانه: دو sync در یک روز → یک رکورد (idempotent)؛ دو روز → دو رکورد (تاریخچه حفظ شود)
- تلگرام: dialog بدون پشتیبانی متریک → `unsupported` نه صفر
- گارد Manus: خروجی با `sample_size=1` و `confidence=high` → تنزل به `low`
- گارد Manus: `data_completeness.telegram=missing` + الگوی تلگرام → الگو رد شود
- گارد Manus: توصیه «تعداد مقصد روزانه تلگرام را زیاد کن» → رد شود
- بلاگ: نبود آنالیتیکس سایت → `data_completeness.blog=missing`، نه صفر
- روز با `no_destination_found=true` → در گزارش به‌عنوان نتیجه معتبر بیاید نه شکست
- عدم دسترسی Manus → گزارش روزانه با `missing` تولید شود، بدون crash
- هیچ endpoint جدیدی بدون توکن پاسخ ندهد (401)

#### تحویل

گزارش کن: چه چیزی ساخته شد، کدام تست‌ها سبزند، چه چیزی روی Railway تأیید نشده، و اگر جایی محدودیت واقعی وجود داشت صریحاً اعلام کن (§۲۶ — شکست را پنهان نکن).
