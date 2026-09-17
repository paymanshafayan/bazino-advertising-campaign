# پرامپت ایجنت پورتال — اصلاح و راه‌اندازی سیستم کمپین تبلیغاتی

**نسخه:** ۱ · **تاریخ:** ۲۰۲۶-۰۹-۱۷
**مقصد:** ایجنت ریپوی `bazino-gamenet-portal`، برنچ `arena/01a0a0e1-bazino-gamenet-portal`
**مبنای بررسی:** کامیت `ec979b3` + بازرسی مستقیم `bazino.sqlite3`
**گزارش تحلیلی:** `PORTAL_CAMPAIGN_AUDIT.md` (ریپوی advertising-campaign)
**مرجع قواعد:** اسکیل پروژه — §۵-A، §۵-B، §۵-C، §۱۴-A، §۱۷، §۱۸، §۲۳، §۲۶، §۲۷، §۲۸، §۲۹

> ⚠️ **این برنچ مستقیماً روی Railway دیپلوی می‌شود. هر push یعنی production.**
> بچ‌های ۱ تا ۳ رفتار مسیر Affiliate را تغییر می‌دهند. چون امروز `outboundEnabled=false` و کمپین `active=false` است، ریسک فعلی صفر است — **ولی ترتیب اجرا را رعایت کن: اول کد، بعد فعال‌سازی.**

---

## ۰. زمینه و واقعیت فعلی

پورتال یک سیستم کمپین کامل دارد که **ساخته شده ولی هرگز روشن نشده**. در دیتابیس واقعی فقط سه رکورد seed وجود دارد (`pub-agent`, `pub-campaign`, `pub-config`) و تمام جدول‌های عملیاتی صفر رکورد دارند: `ig_media`, `ig_members`, `ig_events`, `affiliates`, `affiliate_clicks`, `affiliate_commissions`, `pub-metrics`.

تقسیم کار واقعی امروز:

| جزء | نقش واقعی |
|---|---|
| **Zernio** | فقط ارسال دایرکت خودکار: Private Reply، DM، و بررسی وضعیت فالو. **انتشار پست انجام نمی‌دهد. آنالیتیکس نمی‌دهد.** |
| **Manus** | انتشار محتوا و بررسی بازخورد — **به‌صورت دستی، بیرون از پورتال** |
| **پورتال** | امروز عملاً در مسیر کمپین نقشی ایفا نمی‌کند |

هدف این پرامپت: بستن سه شکاف ساختاری، سپس آماده‌سازی پورتال برای اینکه بتواند نقش واقعی خودش را بگیرد.

**اصل حاکم (§۲۷ API-First):** از زیرساخت موجود استفاده کن. سیستم دوم نساز.

---

## ۱. سه تصمیم مالک — اجرا کن

### تصمیم ۱: موتور `igEngine.ts` (v3) بازنشسته شود؛ `campaignV4.ts` (v4) بماند

دلیل فنی: بررسی نشان داد **توابع کمپین v3 اصلاً فراخوانی نمی‌شوند**. در `igRoutes.ts:10` پنج تابع import شده‌اند ولی فقط `registerPublishedMedia` واقعاً استفاده می‌شود (خط ۱۲۵). `onCampaignComment`، `onFollowButton`، `parseFollowPayload` و `createPartnerInvite` **در هیچ مسیری صدا زده نمی‌شوند — کد مرده‌اند.** پس این بازنشستگی کم‌ریسک است و فقط رسمی‌سازی وضع موجود است.

### تصمیم ۲: فلو «دوست کد عددی را کامنت می‌کند» حذف شود

مالک تصمیم گرفت این فلو **برود**. فلو معتبر فقط نسخه ۲ است: همکار لینک اختصاصی چندبارمصرف می‌گیرد و خودش پخش می‌کند؛ سیستم هرگز به دوست پیام نمی‌دهد.

### تصمیم ۳: درصد کمیسیون در پنل مدیریت توسط ادمین تعیین می‌شود

هیچ درصدی در کد hardcode نشود. فیلدهای `commissionPct`، `attributionDays`، `refundDays`، `payoutMin` در `Studio.tsx:90` قابل ویرایش‌اند و همان‌جا باقی می‌مانند. کار تو فقط این است که مطمئن شوی تا وقتی ادمین این اعداد را نگذاشته، کمپین **نمی‌تواند** فعال شود.

---

## ۲. بچ ۱ — بازنشستگی موتور v3 (اولویت ۱)

**آنچه باید بماند:** `registerPublishedMedia` (در `/api/admin/ig/register-media` استفاده می‌شود) و کلیدهای تنظیمات `ig_*` که هنوز برای پیام‌ها خوانده می‌شوند.

۱. `onCampaignComment`، `onFollowButton`، `parseFollowPayload` و `createPartnerInvite` را از `server/affiliate/igEngine.ts` **حذف کن** و import بلااستفاده در `igRoutes.ts:10` را پاک کن.

۲. کلید تنظیمات `ig_campaign_keyword` (مقدار فعلی `SQUAD`) را **حذف کن** — هم از `IG_SETTING_DEFAULTS` در `igSettings.ts` و هم با یک migration که رکورد را از جدول `settings` پاک کند. تنها منبع کلیدواژه از این پس `pub-campaign.keywords` است.

۳. اگر جایی از UI ادمین این کلید را نمایش می‌دهد، آن را هم بردار.

۴. در بالای `igEngine.ts` کامنت بگذار که این فایل فقط برای ثبت رسانه باقی مانده و منطق کمپین به `campaignV4.ts` منتقل شده است.

⚠️ **جدول‌های `ig_media` / `ig_members` / `ig_events` را حذف نکن.** `campaignV4.ts` هنوز از آن‌ها به‌عنوان لایه سازگاری استفاده می‌کند (`createIgMember`, `updateIgMember`, `createIgEvent`).

---

## ۳. بچ ۲ — حذف فلو کد عددی دوست (اولویت ۱)

همه در `server/affiliate/campaignV4.ts`:

۵. در `onComment` (حدود خط ۶۶) بلوک `if(/^\d{6}$/.test(numeric)){...}` را حذف کن. به‌جای ساختن نقش `friend`، کامنت شش‌رقمی باید **رد شود** با:

```ts
return { ignored:true, reason:'friend_flow_retired' };
```

۶. مقدار `shareStatus:'share_confirmed_by_friend_code'` را حذف کن. نقش `friend` دیگر از مسیر کامنت ساخته نمی‌شود.

۷. `onMessage`: حالت `friend_follow_pending` و شاخه `friend_link` که از این مسیر می‌آمد را حذف کن. **توجه:** نقش `friend` همچنان از مسیر گیت لینک دعوت (`friendGate.ts` و `verifyLink`) ساخته می‌شود و آن مسیر **باید دست‌نخورده بماند** — فقط ورودی «کامنت کد عددی» حذف می‌شود.

۸. متن‌های `partner2` و `friend` را در `shared/publishing/messages.ts` برای **هر چهار زبان** بازنویسی کن:
   - `partner2` دیگر نباید بگوید «این کد را کامنت کن» یا «دوستت باید عدد را کامنت کند». باید بگوید لینک اختصاصی برایت ارسال شده، آن را برای دوستانت بفرست.
   - متن مصوب مالک (فارسی؛ به سه زبان دیگر با همین معنا ترجمه شود):

   > پیج را فالو کن و بر روی دکمه فالو دارم بزن تا لینک دعوت اختصاصی خودت برات ارسال بشه بعد این لینک را برای دوستات بفرست. دوستانت با ثبت نام از طریق این لینک، کوپن تخفیف دریافت می کنند و تو هم از این به بعد از هر بار پرداخت آن ها در Bazino کمسیون دریافت می کنی.

   - کلید `friend` از `CampaignPolicy['messages']` حذف شود (دیگر پیامی به دوست فرستاده نمی‌شود).

۹. همان کلیدهای معادل در `igSettings.ts` (`ig_msg_partner2_*`، `ig_msg_friend_*`) را هم اصلاح یا حذف کن و migration برای پاک‌سازی رکوردهای قدیمی در جدول `settings` بنویس.

۱۰. گارد `PRIVATE_LINK_FORBIDDEN` روی `partner1` و `partner2` **باید باقی بماند** — لینک فقط هنگام dispatch در DM جایگزین می‌شود، هرگز در Private Reply عمومی.

۱۱. یک ثابت صادر کن، مثلاً `export const RETIRED_REASONS = ['friend_flow_retired'] as const`، و رویداد رد شده را در `ig_events` ثبت کن تا در گزارش دیده شود — **سکوت نکن** (§۲۶).

---

## ۴. بچ ۳ — گارد فعال‌سازی کمپین (اولویت ۱)

۱۲. در `saveCampaign` (فایل `server/publishing/settings.ts`) شرط فعال‌سازی را سخت‌تر کن. امروز `active=true` فقط `accountId` و `policyConfirmed` می‌خواهد. اضافه کن: اگر `active===true` ولی هر کدام از این‌ها برقرار نبود، خطا بده:

| شرط | کد خطا |
|---|---|
| `commissionPct > 0` | `COMMISSION_NOT_SET` |
| `financialApproved === true` | `FINANCIAL_POLICY_REQUIRED` (موجود است) |
| `responsible` غیرخالی | `FINANCIAL_POLICY_REQUIRED` (موجود است) |
| `attributionDays >= 1` | `INVALID_POLICY_VALUE` (موجود است) |

۱۳. هیچ مقدار پیش‌فرض غیرصفری برای `commissionPct` نگذار. ادمین باید آگاهانه آن را وارد کند.

۱۴. در `Studio.tsx` کنار فیلد کمیسیون یک پیام وضعیت نشان بده که تا پر نشدن این فیلدها کمپین قابل فعال‌سازی نیست، و دلیلش را بگوید.

---

## ۵. بچ ۴ — هم‌راستاسازی با قواعد محتوایی (اولویت ۲)

۱۵. قواعد فرمت Affiliate در `publish.ts:58` درست‌اند و باید بمانند: نه Story، نه تک‌عکس، کاروسل دقیقاً ۴ اسلاید با کپشن ترکی، Batch دقیقاً ۳تایی. **تغییرشان نده.** فقط پیام خطا را برای ادمین قابل‌فهم کن (امروز فقط کد خطا برمی‌گردد).

۱۶. ترتیب زبان اسلایدهای کاروسل باید **ترکی → فارسی → انگلیسی → روسی** باشد. بررسی کن آیا این ترتیب جایی اعتبارسنجی می‌شود؛ اگر نه، اعتبارسنجی اضافه کن با خطای `CAROUSEL_LANGUAGE_ORDER`.

---

## ۶. بچ ۵ — ابعاد تحلیل روی محتوا (اولویت ۲)

بدون این ابعاد، هر تحلیل عملکردی فقط می‌تواند بگوید «این پست بهتر بود» و نه «چرا».

۱۷. به رکورد محتوا (`pub-media` یا جدول جانبی) این فیلدها را اضافه کن:

`channel` (instagram/telegram/blog) · `content_pillar` · `language` · `format` · `hook_type` · `posting_time` · `mona_featured` (boolean) · `cta_type` · `campaign_id`

۱۸. `content_pillar` باید یکی از هفت دسته اسکیل §۵-A.۱ باشد: `safe`, `tournament`, `affiliate`, `fun`, `venue`, `community`, `trend`.

۱۹. این فیلدها هنگام ثبت رسانه (`registerPublishedMedia` / `MediaRegistry.register`) قابل ارسال باشند و در API خروجی هم برگردند.

---

## ۷. بچ ۶ — تاریخچه متریک و ورودی دستی (اولویت ۲)

⚠️ **تغییر مهم نسبت به طراحی قبلی.** چون Zernio آنالیتیکس نمی‌دهد و Manus دستی کار می‌کند، سیستم باید **ورودی دستی متریک** را به‌عنوان مسیر درجه‌یک بپذیرد — نه فقط وب‌هوک.

۲۰. جدول `content_daily_metrics` با کلید یکتای `(content_id, platform, date)`. فیلدها: `content_id`, `platform`, `date`, `reach`, `impressions`, `views`, `shares`, `comments`, `likes`, `saved`, `follows`, `link_clicks`, `source`, `entered_by`, `synced_at`.

۲۱. فیلد `source` الزامی با مقادیر: `zernio_webhook` · `manual_entry` · `manus_report` · `unsupported`. **هر عدد باید منبعش معلوم باشد.**

۲۲. endpoint ورودی دستی: `POST /api/publishing/metrics/manual` (فقط ادمین) که یک یا چند رکورد روزانه می‌گیرد. اعتبارسنجی: عدد منفی رد شود، تاریخ آینده رد شود، تکراری idempotent باشد.

۲۳. یک فرم ساده در پنل ادمین برای وارد کردن متریک روزانه هر محتوا.

۲۴. ⚠️ **صفر با «نبود داده» فرق دارد.** اگر متریکی در دسترس نیست، `null` یا `unsupported` ثبت شود، هرگز `0`.

۲۵. `pub-metrics` موجود را نگه دار ولی هر sync **علاوه بر آن** یک snapshot روزانه در جدول جدید بنویسد تا تاریخچه از بین نرود.

۲۶. جدول `content_metrics_delta` برای تغییر نسبت به روز قبل و پایه ۷ و ۳۰ روزه.

---

## ۸. بچ ۷ — گزارش سلامت کمپین (اولویت ۳)

۲۷. endpoint جدید `GET /api/publishing/campaign/health` که وضعیت واقعی راه‌اندازی را برمی‌گرداند — تا دیگر کسی مجبور نباشد دیتابیس را دستی باز کند:

```json
{
  "campaign": { "id": "SQUAD26", "active": false, "commissionPct": 0,
                "financialApproved": false, "responsible": "" },
  "config": { "outboundEnabled": false, "selectedMode": null,
              "zernioAccountId": "", "defaultAgentId": "builtin-manus" },
  "integrations": { "zernio": "unconfigured", "manus": "unconfigured",
                    "telegram_gateway": "unreachable" },
  "counts": { "media": 0, "members": 0, "events": 0,
              "affiliates": 0, "clicks": 0, "commissions": 0, "metrics_days": 0 },
  "blockers": ["COMMISSION_NOT_SET", "ACCOUNT_NOT_BOUND", "OUTBOUND_DISABLED"]
}
```

۲۸. فهرست `blockers` باید دقیقاً بگوید چه چیزی مانع راه‌اندازی است. این endpoint **نباید هیچ کلید یا سکرتی برگرداند** (§۲۸).

---

## ۹. قیدهای الزامی

- **هیچ کلید یا سکرتی در خروجی، لاگ یا گزارش ظاهر نشود** (§۲۸).
- **هیچ عدد مالی hardcode نشود** — کمیسیون، کوپن و پنجره انتساب فقط از تنظیمات ادمین.
- **شکست بی‌صدا ممنوع** (§۲۶). هر رد شدن باید `reason` داشته باشد و در `ig_events` ثبت شود.
- **صفر ≠ نبود داده.** در همه مسیرهای متریک.
- **مستقل از جارویس** (§۳). endpointهای جدید نباید از مسیر جارویس عبور کنند.
- `PRIVATE_LINK_FORBIDDEN` و امضای HMAC روی توکن دکمه دست‌نخورده بمانند.
- تفکیک `follow_verified` از `button_event_only` **حفظ شود** — این تفکیک صادقانه یکی از نقاط قوت فعلی کد است و نباید ساده‌سازی شود.
- egress سندباکس به `api.manus.ai` بسته است → تست با mock؛ اولین تست واقعی روی Railway.

---

## ۱۰. تست‌های مورد انتظار

| # | تست | انتظار |
|---|---|---|
| ۱ | کامنت شش‌رقمی زیر پست کمپین | `ignored`, `reason='friend_flow_retired'`، هیچ عضوی ساخته نشود |
| ۲ | کامنت `Hazır` روی محتوای ترکی | همکار ساخته شود، PR بدون لینک ارسال شود |
| ۳ | کامنت `SQUAD` | دیگر شناخته نشود (کلید حذف شده) |
| ۴ | کامنت با دو کلیدواژه از دو زبان | `ambiguous_language` |
| ۵ | فعال‌سازی کمپین با `commissionPct=0` | `COMMISSION_NOT_SET` |
| ۶ | فعال‌سازی با کمیسیون معتبر + مسئول + تأیید مالی | موفق |
| ۷ | لینک دعوت در متن `partner1` یا `partner2` | `PRIVATE_LINK_FORBIDDEN` |
| ۸ | ورود دوست از لینک دعوت | مسیر `friendGate` سالم کار کند |
| ۹ | Affiliate به‌صورت Story | `STORY_NOT_AFFILIATE` |
| ۱۰ | کاروسل Affiliate با ۳ اسلاید | `AFFILIATE_FOUR_SLIDES_REQUIRED` |
| ۱۱ | Batch با ۲ پست | `THREE_POSTS_REQUIRED` |
| ۱۲ | ورودی دستی متریک، دو بار برای یک روز | یک رکورد (idempotent) |
| ۱۳ | ورودی متریک دو روز متفاوت | دو رکورد (تاریخچه حفظ شود) |
| ۱۴ | ورودی متریک با عدد منفی یا تاریخ آینده | رد شود |
| ۱۵ | `GET /campaign/health` | blockers درست، بدون هیچ سکرتی |
| ۱۶ | همه endpointهای جدید بدون توکن | `401` |

تست‌های موجود (`npm test` → `tsx tests/run-all.mts`) نباید بشکنند. ۶ شکست محیطی از قبل وجود دارد و مربوط به این کار نیستند.

---

## ۱۱. تحویل

گزارش بده:

۱. چه چیزی ساخته/حذف شد، به تفکیک بچ.
۲. کدام تست‌ها سبزند و کدام قرمز.
۳. **چه چیزی روی Railway تأیید نشده** — صریح بگو.
۴. اگر جایی محدودیت واقعی وجود داشت یا تصمیمی لازم بود که در این پرامپت نیامده، **آن را اعلام کن و حدس نزن** (§۲۶ — شکست را پنهان نکن).
۵. پس از اجرا، خروجی `GET /api/publishing/campaign/health` را در گزارش بیاور تا وضعیت واقعی راه‌اندازی مستند شود.

**آنچه انجام نده:**

- کمپین را خودت فعال نکن. `outboundEnabled` را خودت روشن نکن. این‌ها تصمیم مالک‌اند.
- جدول‌های `ig_*` را حذف نکن.
- قواعد فرمت Affiliate در `publish.ts` را شل نکن.
- مسیر `friendGate` را دست نزن.
