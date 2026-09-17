# سیستم بازخورد روزانه محتوای منتشرشده (اینستاگرام + تلگرام)

**تاریخ:** 2026-09-17
**هدف:** بستن حلقه `Publish → Measure → Learn → Improve` که در حافظه سیستمی (§۲۳ گام‌های ۸–۱۰) الزامی شده ولی امروز داده‌ای برای تغذیه‌اش وجود ندارد.

---

## بخش ۱ — طراحی سیستم

### ۱.۱ اصل طراحی

سیستم بر دو قاعده بنا می‌شود:

1. **تفکیک واقعیت از تفسیر.** عدد خام (Reach، Views، Likes) «واقعیت مشاهده‌شده» است؛ نتیجه‌گیری درباره علت عملکرد «تفسیر» است. این دو هرگز در یک ستون قرار نمی‌گیرند. (حافظه سیستمی §۴ و §۱۸)
2. **Manus مغز تحلیل است، نه منبع داده.** Manus شبکه اجتماعی را نمی‌خواند و به Meta وصل نیست. منبع اعداد، خط آنالیتیکس Zernio و گیت‌وی تلگرام است. نقش Manus **تبدیل اعداد به درس قابل اقدام** است.

> ⚠️ اگر Manus را منبع عدد فرض کنیم، ایجنت شروع به ساختن آمار می‌کند — که نقض مستقیم §۲۹ حافظه سیستمی است.

### ۱.۲ معماری

```
┌──── منابع واقعیت (اعداد خام) ────────────────────────────────┐
│  اینستاگرام: Zernio → /api/webhooks/zernio/analytics         │
│              → /v1/analytics/delta?cursor=…                   │
│              → جدول pub-metrics                               │
│                                                               │
│  تلگرام:   Telegram Gateway → /internal/dialogs/{id}/…       │
│              → ❌ امروز متریک پست نمی‌دهد (شکاف)              │
└───────────────────────┬───────────────────────────────────────┘
                        │ جمع‌آوری روزانه (snapshot ساعت ۰۹:۰۰ قبرس)
                        ▼
┌──── لایه تجمیع پورتال ───────────────────────────────────────┐
│  content_daily_metrics : عدد هر محتوا در هر روز              │
│  content_metrics_delta : تغییر نسبت به روز قبل                │
│  بستن به: campaign_id · pillar · language · format · Mona?   │
└───────────────────────┬───────────────────────────────────────┘
                        │ فقط اعداد + متادیتا (بدون داده شخصی)
                        ▼
┌──── تحلیل Manus (task.create با structured_output_schema) ───┐
│  ورودی: جدول عملکرد ۲۴ ساعت + پایه مقایسه ۷/۳۰ روزه          │
│  خروجی: JSON ساختاریافته — نه متن آزاد                       │
└───────────────────────┬───────────────────────────────────────┘
                        ▼
┌──── حافظه یادگیری + گزارش روزانه ────────────────────────────┐
│  learning_memory : What worked / underperformed / uncertain  │
│  گزارش روزانه §۲۴ + ورودی پلن فردا §۵                        │
└───────────────────────────────────────────────────────────────┘
```

### ۱.۳ متریک‌های هدف

| پلتفرم | متریک | منبع | وضعیت |
|---|---|---|---|
| اینستاگرام | reach, impressions, views, shares, comments, likes, saved, follows | Zernio analytics delta | ✅ در کد پورتال هست |
| تلگرام | views, forwards, reactions, joins | Telegram Gateway | ❌ **وجود ندارد** |
| هر دو | کلیک لینک ارجاع | `pub-click` | ✅ هست |
| هر دو | تبدیل (lead/رزرو/پرداخت) | `pub-claim` + سفارش‌ها | ✅ هست |

### ۱.۴ ابعاد تحلیل (Dimensions)

هر رکورد متریک باید به این ابعاد وصل شود تا تحلیل معنادار باشد:

`campaign_id` · `content_pillar` · `language` (tr/fa/en/ru) · `format` (reel/carousel/story/post/telegram) · `hook_type` · `posting_time` · `mona_featured` (bool) · `cta_type`

بدون این ابعاد، Manus فقط می‌تواند بگوید «این پست بهتر بود» — با آن‌ها می‌تواند بگوید «Reelهای ترکی با حضور مونا و هوک سؤالی، نرخ ذخیره بالاتری دارند».

### ۱.۵ قرارداد خروجی Manus

Manus باید با `structured_output_schema` صدا زده شود تا خروجی **JSON قابل‌اتکا** باشد، نه متن آزاد:

```json
{
  "analysis_date": "2026-09-17",
  "window": "24h",
  "data_completeness": {
    "instagram": "complete|partial|missing",
    "telegram": "complete|partial|missing",
    "note": "چه چیزی در دسترس نبود"
  },
  "top_performers": [
    { "content_id": "...", "platform": "instagram", "metric": "saves",
      "value": 0, "vs_baseline_pct": 0, "observed_traits": ["..."] }
  ],
  "underperformers": [
    { "content_id": "...", "platform": "...", "metric": "...",
      "value": 0, "vs_baseline_pct": 0, "observed_traits": ["..."] }
  ],
  "patterns": [
    { "pattern": "...", "evidence_content_ids": ["..."],
      "confidence": "high|medium|low",
      "sample_size": 0,
      "classification": "verified_fact|observed_trend|interpretation|assumption" }
  ],
  "recommendations": [
    { "action": "...", "rationale": "...", "expected_effect": "...",
      "priority": "high|medium|low", "is_experiment": true }
  ],
  "uncertainties": ["چه چیزی با این داده قابل نتیجه‌گیری نیست"]
}
```

**قواعد الزامی روی این خروجی:**

- هر `pattern` باید `classification` داشته باشد. اگر `sample_size < 3` حداکثر `confidence` برابر `low` است.
- اگر `data_completeness` هر پلتفرمی `missing` باشد، Manus **حق ندارد** درباره آن پلتفرم الگو استخراج کند.
- `recommendations` نباید شامل عدد، تخفیف، جایزه یا تاریخ تأییدنشده باشد.

### ۱.۶ چرخه روزانه

| ساعت (قبرس) | گام | مسئول |
|---|---|---|
| ۰۸:۳۰ | جمع‌آوری snapshot متریک ۲۴ ساعت گذشته | پورتال (job) |
| ۰۸:۴۵ | محاسبه delta نسبت به روز قبل + پایه ۷/۳۰ روزه | پورتال |
| ۰۹:۰۰ | ارسال جدول به Manus + دریافت JSON تحلیل | پورتال → Manus |
| ۰۹:۱۵ | ثبت در `learning_memory` + تولید گزارش روزانه | پورتال |
| ۰۹:۳۰ | تغذیه پلن روز به ایجنت تبلیغات | ایجنت |

### ۱.۷ قواعد ایمنی

1. **بدون داده شخصی.** فقط عدد تجمیعی و متادیتای محتوا به Manus می‌رود. نام، شماره تلفن، شناسه کاربر هرگز.
2. **بدون کلید.** طبق §۲۸ حافظه سیستمی هیچ کلید یا سکرتی در prompt قرار نمی‌گیرد.
3. **Idempotency.** هر روز یک تحلیل؛ کلید `analysis:{date}` تا اجرای دوباره، رکورد تکراری نسازد.
4. **شکست بی‌صدا ممنوع.** اگر Zernio یا Manus پاسخ ندهد، گزارش روزانه باید صریحاً بگوید «داده در دسترس نبود» — نه اینکه بخش خالی بماند. (§۲۶)
5. **عدم انتشار.** این سیستم فقط می‌خواند و تحلیل می‌کند؛ هیچ مسیری به انتشار خودکار ندارد.

---

## بخش ۲ — بررسی پورتال: آیا این امکان وجود دارد؟

بررسی روی ریپوی `bazino-gamenet-portal`، برنچ `arena/01a0a0e1`، با خواندن کد انجام شد.

### ۲.۱ نتیجه کلی

> **پاسخ: نیمه‌مثبت.** زیرساخت دریافت متریک **اینستاگرام** وجود دارد و کار می‌کند. اما سیستم بازخورد روزانه با تحلیل Manus **وجود ندارد** و متریک **تلگرام اصلاً جمع‌آوری نمی‌شود**.

### ۲.۲ آنچه هست ✅

| قابلیت | شاهد در کد |
|---|---|
| دریافت وب‌هوک آنالیتیکس | `server/publishing/webhooks.ts:128` → `POST /api/webhooks/zernio/analytics` با HMAC و stream جدا |
| کشیدن delta با cursor | `webhooks.ts:108` → `GET /v1/analytics/delta?cursor=…` + رکورد `pub-analytics-cursor` |
| ذخیره متریک | `webhooks.ts:114-115` → جدول `pub-metrics`، فیلدها: `reach, impressions, views, shares, comments, likes, saved, follows` |
| نگاشت پست به محتوا | `registry.lookup(accountId, platformPostId)` |
| گزارش کمپین | `server/publishing/reports.ts:27` → فیلد `insights` |
| کلیک و تبدیل | `pub-click`، `pub-claim`، کمیسیون‌ها |
| اتصال Manus | `server/publishing/manus.ts` → `ManusClient` با `task.create` و **`structured_output_schema`** ✅ |
| زمان‌بند | `publicationRoutes.ts:59` و `routes.ts:42` → `setInterval` موجود است |

**نکته مهم:** `ManusClient.create()` از قبل پارامتر `structured_output_schema` را پشتیبانی می‌کند — یعنی قرارداد JSON بند ۱.۵ **بدون تغییر در کلاینت** قابل پیاده‌سازی است.

### ۲.۳ آنچه نیست ❌

| شکاف | توضیح | شدت |
|---|---|---|
| **متریک تلگرام** | `telegram-gateway/src/adapter.py` فقط `dialogs`, `permissions`, `search`, `send` دارد. هیچ متد `views/forwards/reactions` وجود ندارد. در `webhooks.ts` و `reports.ts` کلمه telegram اصلاً نیست | 🔴 بحرانی |
| **سیستم بازخورد روزانه** | هیچ job زمان‌بندی‌شده‌ای متریک را snapshot نمی‌کند. `pub-metrics` فقط آخرین وضعیت را نگه می‌دارد و **با هر sync بازنویسی می‌شود** — تاریخچه از بین می‌رود | 🔴 بحرانی |
| **تحلیل Manus روی عملکرد** | `ManusClient` فقط برای **تولید محتوا** استفاده می‌شود. هیچ فراخوانی برای تحلیل متریک وجود ندارد | 🔴 بحرانی |
| **حافظه یادگیری** | هیچ جدولی معادل `learning_memory` (§۱۸ حافظه سیستمی) وجود ندارد | 🟠 مهم |
| **ابعاد تحلیل** | `pillar`, `language`, `format`, `hook_type`, `mona_featured` روی محتوا ذخیره نمی‌شوند → تحلیل الگو ممکن نیست | 🟠 مهم |
| **گزارش روزانه بازاریابی** | بریف روزانه جارویس وجود دارد ولی آن **برای ادمین** است و طبق §۳ حافظه سیستمی ایجنت تبلیغات **نباید** به جارویس وابسته باشد | 🟠 مهم |
| **trigger آنالیتیکس** | فقط واکنشی است: `routes.ts:44` وقتی رویداد `analytics.synced` برسد پردازش می‌کند. هیچ کشیدن فعال روزانه‌ای نیست | 🟡 متوسط |

### ۲.۴ جمع‌بندی بررسی

| سؤال | پاسخ |
|---|---|
| آیا پورتال می‌تواند متریک اینستاگرام بگیرد؟ | ✅ بله، از طریق Zernio |
| آیا تاریخچه روزانه نگه می‌دارد؟ | ❌ خیر، بازنویسی می‌شود |
| آیا متریک تلگرام می‌گیرد؟ | ❌ خیر، اصلاً |
| آیا Manus را برای تحلیل صدا می‌زند؟ | ❌ خیر، فقط برای تولید |
| آیا حافظه یادگیری دارد؟ | ❌ خیر |
| آیا `structured_output_schema` پشتیبانی می‌شود؟ | ✅ بله، آماده است |

**نتیجه: پاسخ منفی است → پرامپت ساخت سیستم در بخش ۳.**

---

## بخش ۳ — پرامپت برای ایجنت پورتال

> این متن را به ایجنت ریپوی `bazino-gamenet-portal` بدهید.

---

### PROMPT — Daily Content Feedback & Learning Loop

**زمینه:** ایجنت تبلیغات بازینو موظف است حلقه `Publish → Measure → Learn → Improve` را اجرا کند، اما پورتال امروز داده‌ای برای تغذیه این حلقه تولید نمی‌کند. متریک اینستاگرام از Zernio می‌آید ولی **تاریخچه‌اش نگه داشته نمی‌شود**، متریک تلگرام **اصلاً جمع‌آوری نمی‌شود**، و Manus فقط برای تولید محتوا استفاده می‌شود نه تحلیل عملکرد.

**آنچه از قبل موجود است (دوباره نساز):**
- `server/publishing/webhooks.ts` — دریافت `analytics.synced`، کشیدن `/v1/analytics/delta` با cursor، ذخیره در `pub-metrics`
- `server/publishing/manus.ts` — `ManusClient` با `task.create` و پشتیبانی از `structured_output_schema`
- `server/publishing/registry.ts` — نگاشت `platformPostId` به محتوا
- `server/publishing/reports.ts` — گزارش کمپین با فیلد `insights`
- الگوی `setInterval` در `publicationRoutes.ts:59` و `routes.ts:42`
- `telegram-gateway/` — FastAPI با `dialogs`, `permissions`, `search`, `send`

**طبق §۲۷ حافظه سیستمی (API-First): از زیرساخت موجود استفاده کن، سیستم دوم نساز.**

#### کاری که باید انجام شود

**بچ ۱ — تاریخچه متریک (اولویت ۱)**

1. جدول جدید `content_daily_metrics` با کلید یکتای `(content_id, platform, date)` بساز. `pub-metrics` را بازنویسی نکن؛ به‌جای آن هر sync یک **snapshot روزانه** هم بنویس تا تاریخچه حفظ شود.
2. فیلدها: `content_id`, `platform`, `date`, `reach`, `impressions`, `views`, `shares`, `comments`, `likes`, `saved`, `follows`, `link_clicks`, `synced_at`, `source`.
3. جدول `content_metrics_delta` برای تغییر نسبت به روز قبل و پایه ۷ و ۳۰ روزه.
4. ابعاد تحلیل را به رکورد محتوا اضافه کن: `content_pillar`, `language`, `format`, `hook_type`, `posting_time`, `mona_featured` (boolean), `cta_type`. اگر جای مناسبی در `pub-media` هست همان‌جا، وگرنه جدول جانبی.

**بچ ۲ — متریک تلگرام (اولویت ۱)**

5. در `telegram-gateway/src/adapter.py` متد جدید `post_metrics(dialog_id, message_ids)` اضافه کن که با Telethon مقادیر `views`, `forwards`, `reactions` را برای پیام‌های کانال برمی‌گرداند.
6. endpoint متناظر در `app.py`: `GET /internal/dialogs/{dialog_id}/messages/metrics` با همان احراز Bearer موجود.
7. در سمت پورتال، این متریک‌ها را در همان `content_daily_metrics` با `platform='telegram'` ذخیره کن.
8. ⚠️ اگر Telethon برای نوع خاصی از dialog متریک نمی‌دهد، وضعیت را `unsupported` ثبت کن — **هرگز صفر ننویس**، چون صفر با «نبود داده» اشتباه گرفته می‌شود.

**بچ ۳ — Job جمع‌آوری روزانه (اولویت ۱)**

9. یک job زمان‌بندی‌شده روزانه ساعت **۰۸:۳۰ به وقت قبرس** بساز (همان الگوی dateKey قبرس که در جارویس استفاده شده) که: برای هر محتوای منتشرشده فعال ۳۰ روز گذشته، snapshot متریک را بگیرد و delta محاسبه کند.
10. به‌جای اتکای صرف به وب‌هوک، یک **کشیدن فعال** از `/v1/analytics/delta` هم انجام بده (وب‌هوک ممکن است نرسد).
11. Idempotency با کلید `metrics-snapshot:{date}`.
12. خطا نباید تایمر را بکشد؛ خطا ثبت و job ادامه پیدا کند.

**بچ ۴ — تحلیل Manus (اولویت ۲)**

13. سرویس جدید `server/publishing/feedback.ts` که ساعت **۰۹:۰۰ قبرس** جدول عملکرد ۲۴ ساعت + پایه ۷/۳۰ روزه را به Manus می‌فرستد.
14. از `ManusClient.create()` با `structured_output_schema` استفاده کن — schema دقیقاً مطابق قرارداد JSON بالا (`data_completeness`, `top_performers`, `underperformers`, `patterns`, `recommendations`, `uncertainties`).
15. **گاردهای الزامی روی خروجی:**
    - هر `pattern` باید `classification` داشته باشد ∈ `verified_fact|observed_trend|interpretation|assumption`
    - اگر `sample_size < 3` → `confidence` را به `low` تنزل بده
    - اگر `data_completeness.{platform}` برابر `missing` بود → الگوهای مربوط به آن پلتفرم را **رد کن**
    - خروجی حاوی عدد تخفیف، جایزه یا تاریخ تأییدنشده → رد و ثبت به‌عنوان نقض
16. **هیچ داده شخصی در prompt نرود** — فقط عدد تجمیعی و متادیتای محتوا.
17. Idempotency با کلید `analysis:{date}`.

**بچ ۵ — حافظه یادگیری و API (اولویت ۲)**

18. جدول `learning_memory` با سه دسته §۱۸ حافظه سیستمی: `what_worked`, `what_underperformed`, `what_remains_uncertain`. هر رکورد با شواهد (`content_ids`), `confidence`, `first_observed`, `last_confirmed`.
19. اگر یک الگو در چند روز تکرار شد، `confidence` بالا برود؛ اگر نقض شد، به `uncertain` منتقل شود.
20. endpointهای جدید:
    - `GET /api/publishing/feedback/daily?date=` — تحلیل روز
    - `GET /api/publishing/feedback/learning` — حافظه یادگیری
    - `GET /api/publishing/feedback/metrics?content_id=` — تاریخچه یک محتوا
    - `POST /api/publishing/feedback/run` — اجرای دستی (فقط ادمین)
21. ⚠️ این endpointها باید **مستقل از جارویس** باشند. طبق §۳ حافظه سیستمی ایجنت تبلیغات نباید از مسیر جارویس عبور کند.

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
- عدم دسترسی Manus → گزارش روزانه با `missing` تولید شود، بدون crash
- هیچ endpoint جدیدی بدون توکن پاسخ ندهد (401)

#### تحویل

گزارش کن: چه چیزی ساخته شد، کدام تست‌ها سبزند، چه چیزی روی Railway تأیید نشده، و اگر جایی محدودیت واقعی وجود داشت صریحاً اعلام کن (§۲۶ — شکست را پنهان نکن).

---

## بخش ۴ — تا زمان پیاده‌سازی

تا وقتی این سیستم ساخته نشده، گزارش روزانه ایجنت تبلیغات باید در بخش Performance صریحاً بنویسد:

> **داده عملکرد در دسترس نیست.** متریک اینستاگرام تاریخچه ندارد و متریک تلگرام جمع‌آوری نمی‌شود. هیچ نتیجه‌گیری درباره عملکرد محتوا در این گزارش مبنای داده‌ای ندارد.

این دقیقاً همان رفتاری است که §۴ («هرگز فرض را به‌عنوان واقعیت ارائه نکن») و §۲۶ («شکست را پنهان نکن») الزام کرده‌اند.
