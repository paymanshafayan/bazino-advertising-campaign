# گزارش بررسی سیستم کمپین تبلیغاتی پورتال

**تاریخ:** ۲۰۲۶-۰۹-۱۷
**ریپو:** `bazino-gamenet-portal` — برنچ `arena/01a0a0e1-bazino-gamenet-portal`، کامیت `ec979b3`
**روش:** خواندن کد + بازرسی مستقیم دیتابیس واقعی (`bazino.sqlite3`)
**زمینه‌ای که مالک داد:** «Zernio در حال حاضر فقط ارسال دایرکت خودکار را انجام می‌دهد. انتشار و بررسی بازخورد توسط Manus و **به‌صورت دستی** انجام می‌شود، نه از طریق پورتال.»

---

## بخش ۰ — خلاصه اجرایی

پورتال یک سیستم کمپین **کامل و بالغ** دارد که در سه لایه ساخته شده. اما یافته اصلی این است:

> ### 🔴 سیستم کمپین پورتال ساخته شده ولی **روشن نیست**.
>
> در دیتابیس واقعی فقط **۳ رکورد** وجود دارد: یک agent، یک campaign، یک config. هر سه **مقدار پیش‌فرض seed** هستند. تمام جدول‌های عملیاتی **صفر رکورد** دارند.

| جدول | رکورد |
|---|---|
| `ig_media` (رسانه‌های ثبت‌شده کمپین) | **۰** |
| `ig_members` (همکار و دوست) | **۰** |
| `ig_events` | **۰** |
| `affiliates` | **۰** |
| `affiliate_clicks` | **۰** |
| `affiliate_commissions` | **۰** |
| `pub-draft` / `pub-publication` / `pub-metrics` | **۰** (اصلاً ایجاد نشده‌اند) |

و سه سوئیچ اصلی خاموش‌اند:

| تنظیم | مقدار واقعی | یعنی |
|---|---|---|
| `pub-campaign.SQUAD26.active` | `false` | کمپین Affiliate **غیرفعال** |
| `pub-config.outboundEnabled` | `false` | هیچ پیام خروجی ارسال نمی‌شود |
| `pub-config.selectedMode` | `null` | حالت انتشار (دستی/ایجنت) انتخاب نشده |
| `pub-config.zernioAccountId` | `""` | حساب اینستاگرام به Zernio وصل نشده |
| `pub-campaign.SQUAD26.commissionPct` | `0` | درصد کمیسیون تعیین نشده |
| `pub-campaign.SQUAD26.financialApproved` | `false` | سیاست مالی تأیید نشده |

این با حرف شما کاملاً سازگار است: کار عملاً دستی و بیرون از پورتال انجام می‌شود. پورتال آماده است ولی به‌کار گرفته نشده.

---

## بخش ۱ — معماری واقعی سیستم کمپین

پورتال **سه زیرسیستم مجزا** دارد که هر سه زنده و mount شده‌اند:

```
┌─ A. سیستم انتشار (Publishing) ───────── server/publishing/
│   Draft → Validate → Approve → Schedule(Batch۳) → Publish → Poll
│   مقصد انتشار: Zernio یا Manus (به‌عنوان «agent»)
│   وضعیت: کد کامل · داده صفر · outboundEnabled=false
│
├─ B. کمپین Affiliate اینستاگرام ──────── server/affiliate/
│   دو موتور موازی! igEngine.ts (v3) + campaignV4.ts (v4)
│   وضعیت: کد کامل · داده صفر · campaign.active=false
│
└─ C. کمپین تلگرام (Manus) ────────────── server/manus/
    Draft → Approve → Signed Command → Railway Gateway → Telegram
    وضعیت: کد کامل · gateway پیکربندی نشده
```

### ۱.۱ نقش واقعی Zernio در کد

تأیید حرف شما با شاهد کد. Zernio در پورتال **فقط سه کار** می‌کند:

| کار | مسیر در کد |
|---|---|
| ارسال Private Reply | `provider.ts` → `POST /v1/inbox/comments/{mediaId}/{commentId}/private-reply` |
| ارسال DM | `provider.ts` → `POST /v1/inbox/conversations/{id}/messages` |
| بررسی وضعیت فالو | `provider.ts` → `GET /v1/accounts/{id}/follow-status/{userId}` |

در فایل `affiliate/zernio.ts` خود توسعه‌دهنده نوشته:

> «پورتال به Meta وصل نمی‌شود؛ فقط فرمان PR/DM به عامل.»

**اما** کد دو قابلیت دیگر هم از Zernio *انتظار* دارد که امروز استفاده نمی‌شوند:

- **انتشار پست** — `publish.ts` مسیر `provider==='zernio'` دارد و منتظر `platformPostId` است.
- **آنالیتیکس** — `webhooks.ts:108` کشیدن `GET /v1/analytics/delta?cursor=` و وب‌هوک `POST /api/webhooks/zernio/analytics`.

یعنی **گزارش قبلی من در `DAILY_FEEDBACK_SYSTEM.md` §۲.۲ گمراه‌کننده بود.** من نوشته بودم «دریافت متریک اینستاگرام ✅ در کد پورتال هست». درست‌تر این است: **کد آماده است ولی این جریان هرگز داده‌ای دریافت نکرده** — چون Zernio این نقش را ندارد و `pub-metrics` صفر رکورد است. تصحیح در بخش ۵.

---

## بخش ۲ — سیستم انتشار (Publishing)

### ۲.۱ چرخه حیات

`draft → approved → submitted → published` با کنترل‌های جدی:

| کنترل | کد |
|---|---|
| قفل نسخه (خوش‌بینانه) | `VERSION_CONFLICT` در هر نوشتن |
| تأیید صریح دومرحله‌ای | `confirmed!==true` → `APPROVAL_CONFIRMATION_REQUIRED` |
| **الزام Batch سه‌تایی** | `scheduleBatch`: `draftIds.length!==3` → `THREE_POSTS_REQUIRED` |
| Idempotency | `core.command(actor, idempotencyKey, ...)` |
| منع لینک خصوصی | regex روی کپشن → `PRIVATE_LINK_FORBIDDEN` |
| اعتبارسنجی رسانه | نسبت تصویر، تعداد، نوع، هم‌نسبتی کاروسل |
| منبع رسانه مورد اعتماد | فقط `media.zernio.com`، R2، GCS، S3 |

**اصل سه‌تایی که در اسکیل §۵-A.۳ نوشتیم، در کد پورتال اجباری است** — نه توصیه. این تطابق خوبی است.

### ۲.۲ قواعد Affiliate در اعتبارسنجی

در `publish.ts:58` سه قاعده سخت وجود دارد که **در اسناد ما نبود**:

| قاعده | کد خطا |
|---|---|
| محتوای Affiliate نمی‌تواند Story باشد | `STORY_NOT_AFFILIATE` |
| محتوای Affiliate نمی‌تواند تک‌عکس باشد | `AFFILIATE_FOUR_SLIDES_REQUIRED` |
| کاروسل Affiliate باید **دقیقاً ۴ اسلاید و کپشن ترکی** باشد | `AFFILIATE_FOUR_SLIDES_TURKISH_CAPTION` |

این دقیقاً همان کاروسل چهارزبانه (ترکی→فارسی→انگلیسی→روسی) است که در سند اینستاگرام آمده — **ولی پورتال آن را اجباری کرده و کپشن را هم ترکی الزام کرده.** این را باید به اسکیل اضافه کنیم.

### ۲.۳ Manus به‌عنوان agent انتشار

در دیتابیس یک agent ثبت است:

```json
{ "name": "Manus", "adapterId": "manus", "enabled": true,
  "credentialRef": "agent:manus", "profile": "standard" }
```

`publish.ts` می‌تواند تولید محتوا را به Manus بسپارد (`generate`, `workGenerations`) و انتشار را هم (`provider==='manus'`). محافظت‌ها: `AGENT_CREDENTIAL_CHANGED`، `AGENT_CONFIGURATION_CHANGED`، `AGENT_COST_CONFIRMATION_REQUIRED` (تأیید هزینه اجباری).

⚠️ ولی `outboundEnabled=false` است، پس `workGenerations()` در همان خط اول برمی‌گردد و هیچ کاری نمی‌کند.

---

## بخش ۳ — کمپین Affiliate اینستاگرام

### 🔴 ۳.۱ یافته مهم: دو موتور موازی و ناسازگار

این جدی‌ترین مشکل ساختاری است که پیدا کردم.

| | `igEngine.ts` (v3) | `campaignV4.ts` (v4) |
|---|---|---|
| خطوط | ۳۷۰ | ۱۶۰ |
| منبع تنظیمات | جدول `settings` (کلیدهای `ig_*`) | رکورد `pub-campaign` |
| **کلیدواژه** | `ig_campaign_keyword` = **`SQUAD`** (تک‌کلمه) | `keywords` = **`Hazır`/`آماده`/`Ready`/`Готово`** |
| تشخیص زبان | ندارد — یک کلیدواژه برای همه | `commentLanguage()` بر اساس کلیدواژه |
| ورودی | `POST /api/integrations/instagram/published-media` + `onCampaignComment` | صف رویداد وب‌هوک Zernio |
| mount | `server.ts:2597` `registerIgRoutes` | `publishing/routes.ts:18` |

**هر دو mount شده‌اند و هر دو زنده‌اند.**

و در دیتابیس واقعی:

```
ig_campaign_keyword = SQUAD          ← موتور v3
pub-campaign.keywords = {tr:Hazır, fa:آماده, en:Ready, ru:Готово}   ← موتور v4
```

پس جواب سؤال جلسه قبل روشن شد: **هر دو ما نیمی درست می‌گفتیم.** چهار کلیدواژه در v4 واقعاً هست (حرف شما)، و `SQUAD` هم واقعاً هست (چیزی که من دیدم) — ولی در موتور قدیمی‌تر.

**ریسک عملی:** اگر روزی مسیر v3 فعال شود، کامنت `Hazır` را نمی‌شناسد. اگر v4 فعال شود، `SQUAD` را نمی‌شناسد. تا وقتی یکی رسماً بازنشسته نشود، رفتار سیستم به این بستگی دارد که رویداد از کدام در وارد شود.

### ۳.۲ یافته دوم: فلو نسخه ۱ هنوز در کد زنده است

سند `Doc/affiliate-instagram-plan-fa.md` نسخه ۲ می‌گوید فلو «دوست کد عددی کامنت می‌کند» بازنشسته شده. **در کد این‌طور نیست:**

- `campaignV4.ts:66` — اگر کامنت `^\d{6}$` باشد، نقش `friend` ساخته می‌شود (فلو v1).
- `campaignV4.ts:83` — `shareStatus: 'share_confirmed_by_friend_code'` هنوز نوشته می‌شود.
- عبارت `friend_flow_retired` **در کل ریپو وجود ندارد** (grep شد).
- پیام‌های `partner2` در هر چهار زبان هنوز می‌گویند: «این کد را زیر پست کامنت کن... دوستت باید عدد داخل پیام را زیر همان پست کامنت کند».

پس یافته قبلی من در `DAILY_FEEDBACK_SYSTEM.md` که گفتم فلو دوست بازنشسته شده، **غلط بود**. تنها چیزی که واقعاً بازنشسته شده، endpoint زیر است:

```
POST /api/integrations/instagram/partner-invite  →  HTTP 410 MEDIA_INGEST_ONLY
```

### ۳.۳ آنچه درست و محکم است ✅

| قابلیت | شاهد |
|---|---|
| منع لینک در Private Reply عمومی | دو گارد جدا: `settings.ts:116` و `campaignV4.ts:88` |
| امضای HMAC روی توکن دکمه | `button()` با `invitationKey` + nonce + بررسی مالکیت |
| انقضای دکمه | ۳۰ روز |
| انقضای کامنت | کامنت قدیمی‌تر از ۷ روز رد می‌شود |
| ضد تکرار | `pub-comment` با fingerprint + `getIgMemberByCommentId` |
| تشخیص ابهام زبانی | دو کلیدواژه در یک کامنت → `ambiguous_language` و رد |
| نرمال‌سازی ارقام | `normalizeDigits` — ارقام فارسی و عربی به لاتین |
| تفکیک صادقانه سطح اثبات | `follow_verified` در برابر `button_event_only` — ادعای تأیید Meta نمی‌کند |
| کد یکتای ۶ رقمی | ۴۰ بار تلاش، بررسی تصادم، وگرنه `CODE_POOL_EXHAUSTED` |

بند آخر ارزش تأکید دارد: کد صراحتاً بین «فالو واقعاً توسط Zernio تأیید شد» و «کاربر فقط دکمه را زد» تفاوت می‌گذارد و در پیام‌ها هم نوشته «این کامنت شاهد عملی است، نه تأیید رسمی Share فردی». **این دقیقاً همان قانون صداقت اسکیل §۲۹ است، پیاده‌شده در کد.**

---

## بخش ۴ — کمپین تلگرام (Manus)

این زیرسیستم **دقیقاً همان چیزی است که سند `Doc/telegram-daily-campaign-complete-fa.md` توصیف می‌کند** — یعنی سند شما با کد می‌خواند.

### ۴.۱ Endpointهای موجود

همه با dual-mount روی `/api/manus/*` و `/api/management/telegram/*`:

| متد | مسیر | کار |
|---|---|---|
| GET | `/health` | وضعیت gateway |
| GET | `/telegram/dialogs` | فیلتر `member_only`, `sendable_only`, `type`, `language` |
| GET | `/telegram/dialogs/:id/permissions` | `is_member`, `can_send`, `can_send_media` |
| GET | `/telegram/dialogs/:id/messages/search` | جست‌وجوی context |
| POST | `/campaign/drafts` | ثبت draft |
| GET | `/campaign/drafts`, `/campaign/drafts/:id`, `/campaign/decisions` | خواندن |
| POST | `/campaigns`, `/campaigns/:id/approve|pause|revoke` | مدیریت کمپین |
| POST | `/campaign/drafts/:id/resolve` | تأیید/رد |
| POST | `/telegram/send-direct` | ارسال مستقیم |
| POST/GET | `/admin/kill-switch` | **توقف اضطراری** |
| GET | `/reports/affiliate/daily` | گزارش روزانه Affiliate |

### ۴.۲ محافظت‌ها

- امضای HMAC روی فرمان (`x-portal-signature`) — gateway پایتونی همان را تأیید می‌کند.
- انقضای ۱۰ دقیقه‌ای روی هر فرمان ارسال.
- `duplicateSent` بر اساس `dialogId + textHash` — همان قاعده hash سند شما.
- `sentToday` برای سقف روزانه.
- **`AUTO_STOP`**: خطای در فهرست `TG_AUTOSTOP_ERRORS` → کمپین `status='paused'`.
- مجوز نامعلوم → `can_send=false` و مقصد رد می‌شود (fail-closed، درست).

### ۴.۳ آنچه نیست

- `readGatewayConfig()` اگر env نباشد `null` برمی‌گرداند → health = `unreachable`. **امروز پیکربندی نشده.**
- در `adapter.py` فقط چهار متد: `dialogs`, `permissions`, `search`, `send`. **هیچ متد متریکی نیست** — این یافته قبلی من درست بود.

---

## بخش ۵ — تصحیح گزارش قبلی

سه یافته در `DAILY_FEEDBACK_SYSTEM.md` §۲ باید اصلاح شوند:

| یافته قبلی | واقعیت | اثر |
|---|---|---|
| «دریافت متریک اینستاگرام ✅ در کد پورتال هست» | کد هست ولی **جریانش زنده نیست**؛ Zernio این نقش را ندارد و `pub-metrics` صفر رکورد است | 🔴 وضعیت بدتر از گزارش‌شده |
| «`pub-metrics` با هر sync بازنویسی می‌شود و تاریخچه از بین می‌رود» | ✅ درست است (`webhooks.ts:115`) — ولی امروز اصلاً چیزی نوشته نمی‌شود | ⚪ بی‌اثر فعلاً |
| «فلو کد عددی دوست بازنشسته شده» | ❌ **غلط بود** — در `campaignV4.ts:66` زنده است و `friend_flow_retired` در ریپو وجود ندارد | 🔴 سند Affiliate ما نسخه ۲ را واقعیت فرض کرده |

درس روش‌شناختی: **خواندن کد بدون بازرسی داده کافی نیست.** کد وجود یک قابلیت را نشان می‌دهد، نه فعال بودنش. از این به بعد هر ادعای «پورتال این را دارد» باید با شمارش رکورد در دیتابیس تأیید شود.

---

## بخش ۶ — فاصله تا وضعیت مطلوب

| # | شکاف | شدت |
|---|---|---|
| ۱ | دو موتور Affiliate موازی با کلیدواژه‌های متفاوت — باید یکی رسماً بازنشسته شود | 🔴 بحرانی |
| ۲ | فلو کد عددی دوست در کد زنده است ولی سند ما آن را بازنشسته اعلام کرده | 🔴 بحرانی |
| ۳ | کمپین `SQUAD26` غیرفعال، `commissionPct=0`، `financialApproved=false` | 🔴 مسدودکننده راه‌اندازی |
| ۴ | `zernioAccountId` خالی — حساب اینستاگرام وصل نیست | 🔴 مسدودکننده |
| ۵ | `outboundEnabled=false` — هیچ چیز ارسال نمی‌شود | 🔴 مسدودکننده |
| ۶ | جریان آنالیتیکس هرگز داده نگرفته | 🔴 حلقه بازخورد بسته است |
| ۷ | متریک تلگرام در `adapter.py` وجود ندارد | 🔴 |
| ۸ | Telegram Gateway پیکربندی نشده (`health: unreachable`) | 🟠 |
| ۹ | بلاگ به هیچ جریان متریکی وصل نیست | 🟠 |
| ۱۰ | Manus فقط برای تولید صدا زده می‌شود، نه تحلیل | 🟠 |
| ۱۱ | قواعد کاروسل چهاراسلایدی Affiliate در اسناد ما نبود | 🟡 حل شد — به اسکیل اضافه می‌شود |

---

## بخش ۷ — پیشنهاد ترتیب اقدام

**پیش از هر توسعه‌ای، سه تصمیم لازم است که فقط مالک می‌تواند بگیرد:**

۱. **کدام موتور Affiliate بماند؟** v4 (چهار کلیدواژه زبانی، منطبق با اسناد جدید) یا v3؟ پیشنهاد من v4 و بازنشستگی v3.
۲. **فلو دوست: کد عددی بماند یا برود؟** سند ما نسخه ۲ را نوشته ولی کد نسخه ۱ را اجرا می‌کند. یکی باید تغییر کند.
۳. **درصد کمیسیون، پنجره انتساب و مسئول مالی** — بدون این‌ها `financialApproved` نمی‌شود و کمپین فعال نمی‌شود.

بعد از آن به ترتیب: وصل‌کردن حساب Zernio → روشن‌کردن `outboundEnabled` → فعال‌کردن کمپین → و تازه آن‌وقت حلقه بازخورد (پرامپت `Doc/portal-agent-prompt-feedback-loop-fa.md`) معنا پیدا می‌کند، چون تا داده‌ای تولید نشود چیزی برای تحلیل نیست.
