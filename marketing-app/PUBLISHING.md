# راهنمای صف انتشار، اتوماسیون زرنیو و گزارش روزانه

> وضعیت مرور سورس/UI: ۲۰۲۶-۱۰-۰۲ · app 0.8.1. صفحهٔ Settings در کارت Portal Ingest فقط URL پایه، فیلد Bearer token، نشانگر وجود توکن، Save و Delete دارد؛ **دکمهٔ Test Connection و GET probe در پیاده‌سازی فعلی وجود ندارد**. Endpoint `/api/integrations/instagram/published-media` یک مسیر ثبت POST است؛ برای آزمون آن را فراخوانی نکنید. اگر health check لازم شد، GET فقط‌خواندنی باید به‌عنوان کار آینده با قرارداد رسمی و تأیید مالک طراحی و پیاده‌سازی شود. این کار webhook/callback نیست.

## مرزهای اجرایی

- انتشار Instagram و اتوماسیون DM از برنامهٔ Windows و اتصال Zernio انجام می‌شوند. Affiliate فقط به‌صورت Reel در Instagram منتشر می‌شود و به مقصدهای دیگر بازنشر نمی‌شود؛ مقصد رسمی HTTPS و توکن امن پورتال باید پیش از انتشار Affiliate آماده باشند. پس از انتشار موفق، ثبت آن به endpoint ingest موجودِ پورتال فرستاده می‌شود و از اتصال/Zernio automation استفاده نمی‌کند.
- تا وقتی مالک همان مورد را در صف تأیید نکرده، هیچ پست یا DM عمومی ارسال نمی‌شود. تأیید پلن اجازهٔ انتشار محتوای آینده نیست.
- برنامه هیچ webhook یا callback تازه‌ای برای Zernio/پورتال ثبت نمی‌کند. توکن ingest پورتال مستقل از کلید Zernio و فقط در حافظهٔ امن تنظیمات برنامه نگهداری می‌شود؛ توکن به صف، گزارش یا لاگ راه ندارد.
- comment-to-DM فقط برای `contentType=DM` از مسیر Zernio per-post ساخته می‌شود. Affiliate پس از انتشار موفق فقط media_id واقعی را به endpoint موجود می‌فرستد؛ برای آن پاسخ‌گوی Zernio ساخته نمی‌شود.
- **قید تازهٔ مالک:** `contentType=Affiliate` فقط با `mediaFormat=reel` مجاز است. Post/Affiliate، Carousel/Affiliate و Story/Affiliate پیش از صف fail-closed رد می‌شوند، حتی اگر قرارداد فنی پورتال `post` را هم بپذیرد. دستور جدید مالک از قرارداد endpoint محدودکننده‌تر است.

## صف بازبینی

- عامل یک JSON با شناسهٔ یکتا در `marketing-app-mailbox/publish-queue/ready/` قرار می‌دهد. فایل‌های واقعی رسانه باید در `marketing-app-mailbox/publish-queue/media/` باشند.
- هر کارت `contentType`, `topic`, `guideVersion`, `mediaFormat`, `topicCycle`, `contentSlot`, زبان، حساب/پلتفرم، CTA، زمان و timezone، وضعیت رسانهٔ نهایی و بازبینی پیش‌نمایش را صریح ثبت می‌کند. موضوع از کاتالوگ نسخه‌دار راهنمای روزانه است؛ مقدار خالی/ناسازگار یا رکورد قدیمیِ فاقد این قرارداد وارد صف تأیید نمی‌شود.
- برنامه فایل و رسانه را می‌خواند و پیش‌نمایش را به شکل فید Instagram نشان می‌دهد؛ نوع، قالب رسانه، موضوع، زبان، زمان محلیِ انتشار، کپشن، CTA و افشای Affiliate واضح‌اند.
- کدهای موضوع جاری عبارت‌اند از `daily-reels`, `daily-didactic`, `daily-game`, `gaming-news`, `active-tournaments`, `bazino-safe`, `affiliate-reel` و چهار زیرموضوع Story در این راهنما. تنها `Affiliate` با `affiliate-reel` سازگار است.
- دکمهٔ ارسال فقط رسید تأیید همان مورد را ثبت می‌کند. نظر در `feedback/` ذخیره می‌شود و پست منتشر نمی‌شود. رسید به Git blob SHA فایل JSON و تمام رسانه‌ها، از جمله فایل پیش‌نمایش، بسته است. نسخهٔ محلیِ دانلودشده نیز پیش از نمایش/ارسال با SHA مخزن تطبیق داده می‌شود؛ هر تغییر پس از تأیید باید به reapproval منجر شود.
- زمان‌بند برنامه هر ۳۰ ثانیه کار می‌کند و فقط وقتی برنامه باز است. موردی که بیش از ۵ دقیقه از موعدش گذشته باشد به `failed/` می‌رود و به زمان و تأیید تازه نیاز دارد.
- درست پیش از اولین `POST /v1/posts`، برنامه رسید پایدار `attempted/{id}.json` می‌نویسد. changelog جاریِ endpoint، `Idempotency-Key` با پنجرهٔ ۲۴ساعته و UUID برای هر logical post را مستند می‌کند؛ glossary هنوز متن قدیمیِ `x-request-id` و حدود ۵ دقیقه را نشان می‌دهد. برنامه از قرارداد دقیق‌ترِ changelog پیروی می‌کند، اما به‌علت انقضای idempotency روی retry خودکار تکیه نمی‌کند. اگر اجرا پس از ثبت attempt قطع شود و نتیجه منتشرشده/ناموفق ثبت نشده باشد، صف «نتیجه نامشخص» نشان می‌دهد و درخواست را هرگز خودکار دوباره نمی‌فرستد (کلید Idempotency رسمی روی همان شناسهٔ قطعی می‌ماند). زمان‌بند در اجراهای بعدی، وضعیت همان پست را با `metadata.contentId` از زرنیو می‌خواند و به‌محض قطعی‌شدن، رکورد `published/` یا `failed/` استاندارد را می‌نویسد (بدون ارسال دوبارهٔ درخواست). پیش از هر پاک‌سازی یا اقدام دستی، وضعیت پست در Zernio و شبکه بررسی شود.
- وضعیت هر مقصد و هشدارهای قابل‌اقدام در `published/` یا `failed/` ثبت می‌شود؛ attemptهای بی‌نتیجه هم در گزارش «بررسی دستی لازم است» دیده می‌شوند. صفحهٔ برنامه هشت نتیجهٔ اخیر را نشان می‌دهد.

## قالب‌های رسانه و ترتیب انتشار

- قالب‌های قابل‌قبول صف: پست عکس، کاروسل ۲ تا ۱۰ رسانه و Reel یک‌ویدئویی. Reel باید ۹:۱۶ و حداکثر ۹۰ ثانیه باشد. برش یا تغییر اندازهٔ خودکار انجام نمی‌شود.
- Instagram اولین مقصد است. حساب و مجوز انتشار از همان اتصال برنامه به‌صورت زنده خوانده می‌شود. طبق قرارداد رسمی زرنیو وضعیت هر مقصد در همان پاسخ می‌تواند موقت باشد (`pending/processing/uploading`) و پاسخ ۲۰۷ هم (که ۲xx است) یعنی «بخشی منتشر شد»؛ بنابراین برنامه فقط `failed`/`cancelled` را شکست می‌داند، `published` را موفق، و هر حالت غیرقطعی را تا ۹۰ ثانیه با `GET /v1/posts/{id}` (و در نبود شناسه، جست‌وجوی فهرست اخیر با `metadata.contentId` خودمان) پیگیری می‌کند. شاهد واقعی موفقیت `platformPostId`/`platformPostUrl` است. مهلت درخواست انتشار اینستاگرام ۳ دقیقه است. اگر نتیجه در این مهلت قطعی نشود، هیچ رکورد «ناموفق» نوشته نمی‌شود: کارت با وضعیت «نتیجهٔ ارسال نامشخص؛ بررسی دستی لازم است» می‌ماند و مقصد دیگری ارسال نمی‌شود.
- برای Feed/Post، Reel و Carousel Instagram، `locationId` همان Facebook Page ID عددی `1091945074011846` است و پیش از ارسال در `platformSpecificData` می‌آید. این شناسه با ID داخلی حساب Facebook زرنیو `6aba79525ad41c33d8cdeba7` و حساب Instagram زرنیو `6ab391ba8d284ffb21332381` یکی نیست. پیش از upload رسانه، برنامه اتصال Facebook فعال در همان profile زرنیو را می‌سنجد و باید `metadata.selectedPageId` دقیقاً با Page ID Manus برابر باشد. این کنترل، وجود دادهٔ location یا tagپذیری Page را ثابت نمی‌کند؛ طبق قرارداد رسمی، اعتبار نهایی را Meta/زرنیو هنگام انتشار انجام می‌دهد. اگر رد شود، نتیجه شکست است و نباید بی‌مکان دوباره ارسال شود.
- **استثنای مالک برای Story:** برنامه هیچ جست‌وجو، انتخاب یا اعتبارسنجی مکان انجام نمی‌دهد و `locationId` را به درخواست Story اضافه نمی‌کند؛ نبود مکان مانع Story نیست. Story فقط در Instagram می‌ماند و به مقصدهای دیگر cross-post نمی‌شود. این استثنا تضمین نمی‌کند Story در هر حساب/شرایطی منتشر شود؛ قالب و دسترسی حساب و محدودیت‌های غیرمکانی همچنان برقرارند.
- فقط پس از موفقیت تأییدشدهٔ Instagram، برنامه حساب‌های دیگر را دوباره می‌سنجد و مقصدهای فعال/مجاز/سازگار را ارسال می‌کند. حساب به‌تنهایی مجوز پست نیست؛ WhatsApp مقصد پست همگانی محسوب نمی‌شود.
- YouTube فقط یک ویدئوی عمودی ۹:۱۶ حداکثر ۱۸۰ ثانیه؛ TikTok فقط وقتی سطح `PUBLIC_TO_EVERYONE` در اطلاعات زندهٔ سازنده مجاز است و consentهای لازم حاضرند؛ Telegram و Facebook فقط برای نوع محتوای پشتیبانی‌شده. مقصد ناسازگار از انتشار همان پست حذف و علت آن گزارش می‌شود؛ رسانه crop نمی‌شود.
- Reel اصلی فقط وقتی مجاز است که Reel آزمایشی همان گروه واقعاً منتشر شده باشد و زمان اصلی دست‌کم یک ساعت پس از زمان موفقیت ثبت‌شدهٔ Reel آزمایشی باشد.

## مسیر DM در Zernio و مسیر یک‌طرفهٔ Affiliate به پورتال

`engagement` فقط برای `contentType=DM` و با `kind=interactive` معتبر است. زرنیو برای هر پست حداکثر یک automation فعالِ per-post می‌پذیرد؛ automation فقط بعد از موفقیت Instagram ساخته می‌شود و شناسهٔ دقیق `platformPostId` همان پست لازم است. `Affiliate` از این مسیر جداست و هیچ automation زرنیو نمی‌گیرد.

### Affiliate Reel → پورتال

- برای `contentType=Affiliate` فقط `mediaFormat=reel` مجاز است. Post/Carousel/Story افیلیت هم در مدل صف و هم پیش از ثبت/انتشار رد می‌شوند.
- فقط پس از تأیید مالک و پاسخ موفق Instagram، کلاینت برنامه `media_id` واقعی را با `media_type=reel`, `published_at` و `Idempotency-Key: instagram:<media_id>` به `POST /api/integrations/instagram/published-media` می‌فرستد.
- بدنه فقط `media_id`, `media_type`, `published_at` دارد؛ `campaign_id`، شناسهٔ Zernio یا دادهٔ شریک فرستاده نمی‌شود. توکن Bearer جداگانه و رمزگذاری‌شده در تنظیمات است و هرگز در JSON صف یا گزارش نیست.
- HTTP 200 با `accepted=true`—از جمله duplicate—ثبت موفق است؛ HTTP 409 گزارش تعارض و بدون retry است؛ HTTP 401 توقف فوری دارد؛ پاسخ‌های دیگر/خطاهای شبکه حداکثر سه تلاش کل با همان idempotency key دارند. ثبت پورتال نتیجه‌ای جدا از انتشار Instagram است و شکست آن باعث تکرار انتشار نمی‌شود.

### محتوای تعاملیِ فالو

`engagement.kind = "interactive"`. برنامه `audience.followerStatus = "follower"`، `audience.whenUnknown = "verify"` و `followGate` با متن و دکمهٔ هم‌زبان محتوا را می‌فرستد. شناسهٔ داخلی پست زرنیو در گزارش برنامه نگه داشته می‌شود، اما طبق قرارداد رسمی جاری، برای پستی که از قبل منتشر شده درخواست `POST /v1/comment-automations` باید `platformPostId` را بفرستد و `postId` را حذف کند.

**محدودیت را پنهان نکن:** مستند جاری Zernio `verify` را یک تأیید یک‌مرحله‌ای توصیف می‌کند: کاربر دکمه را می‌زند و زرنیو وضعیت را بررسی می‌کند؛ در صورت فالو پیام اصلی می‌رود و در غیر این صورت `notFollowingMessage` ارسال می‌شود. مستند، تکرار خودکار دکمه و بررسی پی‌درپی تا زمان فالو را تضمین نمی‌کند. تا وقتی این تکرار در محیط کنترل‌شده تأیید نشده یا API رسمی آن را مستند نکرده، این حالت را «کامل مطابق درخواست مالک» گزارش نکن و برای آن callback پورتال نساز.

### وب‌هوک رویداد در برابر اتوماسیون کامنت

`POST /v1/webhooks/settings` یک نشانی عمومی می‌خواهد که Zernio بتواند به آن درخواست بفرستد. برنامهٔ Windows پشت رایانهٔ مالک به‌خودی‌خود چنین نشانی‌ای نیست. اتوماسیون per-post یک منبع جداگانه با `POST /v1/comment-automations` است که پیام را در خود Zernio مدیریت می‌کند. این دو را یکی ندان؛ event webhook به پورتال، secret پورتال یا endpoint حدسی هرگز ایجاد نکن. اگر مالک یک رویداد ورودی به خود برنامه لازم بداند، ایجاد گیرندهٔ امن متعلق به برنامه یک blocker جدا و نیازمند طرح و تأیید است.

## Insights و Task ویندوز

- گزارش read-only روزانه در `marketing-app-mailbox/insights/daily/YYYY-MM-DD.json` می‌رود؛ زمان snapshot و timezone محلی، بازه، وضعیت تازگی، بخش‌های در دسترس، خطا و نقص داده ثبت می‌شود. دادهٔ گمشده هرگز صفر فرض نمی‌شود.
- Account insights حداکثر ۹۰ روز (پیش‌فرض ۳۰ روز)، follower history حداکثر ۸۹ روز (پیش‌فرض ۳۰ روز)، post analytics حداکثر ۳۶۶ روز است. گزارش روزانه post analytics تمام صفحه‌ها را با `limit=100` می‌خواند (سقف امن ۱۰۰ صفحه/۱۰٬۰۰۰ ردیف)؛ اگر یک صفحه شکست بخورد یا سقف تمام شود، بخش unavailable می‌شود و ناقص به‌عنوان کامل گزارش نمی‌شود. Analytics add-on لازم است و تأخیر Meta می‌تواند تا ۴۸ ساعت باشد. فقط Reach سری زمانی account-level دارد.
- Demographics فقط برای حساب‌های دست‌کم ۱۰۰ فالوئر قابل‌دریافت است؛ خطای آن بخش به‌صورت نقص داده گزارش می‌شود.
- Storyهای فعال از endpoint رسمی خوانده می‌شوند و برای هرکدام نتیجهٔ `live`، `cached` یا `unavailable` حفظ می‌شود. پس از انقضا، داده فقط در صورت دریافت webhook `story_insights` توسط خود Zernio ممکن است در cache باشد؛ برنامه مقدار ناموجود را حدس نمی‌زند. فعلاً برای دریافت آن webhook، subscription جدیدی به پورتال ثبت نمی‌شود.
- برنامه Task روزانهٔ Windows را برای ساعت ۰۸:۰۰ محلی، با حساب همان کاربر و دسترسی حداقلی می‌سازد؛ `StartWhenAvailable` برای فرصت بعدی پس از خواب/خاموشی فعال است. Task فقط گزارش می‌گیرد و پست یا DM نمی‌فرستد.

## مستندهای مرجع بررسی‌شده

- [Zernio Instagram API — مکان، قالب Story، Inbox و follower gate](https://docs.zernio.com/platforms/instagram)
- [Create comment-to-DM automation — قرارداد `platformPostId`, `postId`, `audience` و `followGate`](https://docs.zernio.com/comment-automations/create-comment-automation)
- [Webhooks — endpoint عمومی و روش امضای رویداد](https://docs.zernio.com/webhooks)
- [Instagram account insights](https://docs.zernio.com/analytics/get-instagram-account-insights)
- [Instagram follower history](https://docs.zernio.com/analytics/get-instagram-follower-history)
- [Instagram demographics](https://docs.zernio.com/analytics/get-instagram-demographics)
- [Post analytics and pagination](https://docs.zernio.com/analytics/get-analytics)
- [Zernio changelog — `POST /v1/posts` idempotency contract](https://docs.zernio.com/changelog)
- [Presigned media uploads](https://docs.zernio.com/guides/media-uploads)
