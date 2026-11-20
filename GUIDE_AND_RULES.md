# دستورالعمل دائمی تولید محتوا و اتوماسیون گوگل فلو و گوگل ویدز (Google Flow & Google Vids Studio Guide)

این سند مرجع رسمی و دائمی تولید ویدئو، کاراکترها و اتوماسیون **Google Flow** و **Google Vids** برای کمپین تبلیغاتی بازینو است.
**مرجع چرخه جامع تولید محتوا با ۱۳ اسکیل اینستاگرام:** `C:\projects\Antigravity Bazino\Doc\instagram-agent-skill-production-guide.md`
**مرجع اختصاصی تولید ویدئو با گوگل ویدز:** `C:\projects\Antigravity Bazino\Publish-Studio\GOOGLE_VIDS_PRODUCTION_GUIDE.md` و `VIDS_DOM_MATRIX_MAP.md`

---

## ۱. اصول و قوانین بنیادین تولید محتوا

### قانون ۱: زبان پیش‌فرض تولید محتوا (ترکی استانبولی - Türkçe)
- **تا زمانی که مالک صریحاً زبان دیگری را مشخص نکرده باشد، زبان اصلی سناریو، دیالوگ و گفتار در ویدئوها همواره «ترکی استانبولی (Türkçe)» است.**
- مزیت فنی: مدل‌های هوش مصنوعی صوتی-تصویری گوگل (Google Flow / Google Vids / Omni / Veo) بر روی زبان‌های با الفبای لاتین به‌ویژه ترکی استانبولی همگام‌سازی لب (Lip-sync) و تلفظ صوتی فوق‌العاده طبیعی و ارگانیک تولید می‌کنند و خطاهای اعراب‌گذاری و گلیچ‌های رسم‌الخطی زبان فارسی را ندارند.

### قانون ۲: تولید ویدئو با صدای همگام محلی (Native In-Video Audio & Lip-Sync)
- ویدئو همراه با دیالوگ مستقیم کاراکتر درون **Google Flow** یا با ابزارهای صداگذاری/آواتار/تایم‌لاین **Google Vids** جنریت و مونتاژ می‌شود.
- صدای ارگانیک حاصل از موتور فلو و صداگذاری طبیعی گوگل ویدز به دلیل هماهنگی دقیق حرکت لب‌ها و ریتم گفتار، بسیار زنده‌تر و باورپذیرتر از دوبله‌های غیرهمگام است.

### قانون ۳: دکترین یکپارچگی کاراکتر با کاراکتر شیت (Character Sheet)
- برای ساخت ویدئو از شخصیت‌های رسمی بازینو (مونا و سایر کاراکترها)، ثبت اولیه **همواره از روی کاراکتر شیت چندزاویه‌ای (Character Sheet)** انجام می‌شود.
- محل ذخیره کاراکتر شیت مونا:
  `C:\projects\Antigravity Bazino\Assets\mona\mona-bazino-character-sheet.jpg`
- **ممنوعیت قطعی توصیف ظاهری شخصیت، صحنه و جزئیات تصویر در پرامپت:** وقتی کاراکتر شیت یا تصویر رفرنس صحنه به عنوان مرجع تصویری به مدل ساخت ویدئو الصاق می‌شود، ایجنت به هیچ عنوان نباید خصوصیات ظاهری شخصیت‌ها، چیدمان افراد، محیط یا محتوای نمایشگرها/تلویزیون‌ها را در پرامپت متنی بازنویسی و توصیف کند (مثلاً هرگز نباید بنویسد «تصویر بازی GTA 6 روی تلویزیون» یا لباس و جای افراد را توصیف کند)؛ چرا که توصیف متنی مجدد باعث می‌شود مدل به جای کپی دقیق تصویر رفرنس، جزئیات من‌درآوردی خودش را جایگزین کند و ترکیب زوج‌ها، جای نشستن افراد یا تصویر داخل تلویزیون‌ها به هم بریزد.
- **جایگزین الزامی:** در پرامپت فقط و فقط باید بر **حفظ ۱۰۰٪ ثبات و تطابق کامل تمام جزئیات، جایگاه افراد، محیط و تصاویر داخل نمایشگرها دقیقاً مطابق تصویر رفرنس** تأکید شود و صرفاً حرکت/تغییر بین تصویر رفرنس اول و تصویر رفرنس دوم بیان گردد.

### قانون ۴: پرامپت‌نویسی ساختاریافته استاندارد با اتکا به تصاویر رفرنس و کاراکتر شیت
- ساختار پرامپت استاندارد برای ایجاد ویدئوی سخنگو با کاراکتر شیت:
  `Rely strictly on the provided character sheet for character identity, facial features, and styling. Maintain 100% character visual consistency throughout the shot. In [Location / Atmosphere with lighting]. [Action & Facial Expression], speaking in natural Istanbul Turkish with realistic lip-sync: "[Turkish Dialogue in Quotes]". Cinematic 9:16 vertical video, high resolution.`
- ساختار پرامپت استاندارد برای صحنه‌های دارای تصاویر رفرنس (شروع و پایان صحنه):
  `Strictly preserve 100% of the exact visual details, people, positions, framing, camera angle, background, and screen contents from the provided reference images without altering or inventing any visual elements. Start with the exact scene and positions shown in the first reference image, perform ONLY [the specific action/transition], and transition directly into the exact scene and positions shown in the second reference image.`

### قانون ۵: نسبت تصویر و دانلود مستقیم با کیفیت ۱۰۸۰p یا بالاتر (Native 1080p+ Export)
- نسبت ابعاد همواره **عمودی ریلز و شورتس (9:16)** است.
- **دانلود با کیفیت بالا:** در Google Flow مستقیماً از منوی کیفیت‌ها رزولوشن **1080p یا بالاتر** انتخاب و دریافت می‌شود، و در Google Vids از مسیر `File -> Download -> MP4 video (.mp4)` (روی بوم `9:16`) خروجی نهایی دریافت می‌گردد و نیازی به ارتقای مصنوعی با FFmpeg نیست. فایل نهایی با کیفیت بالا مستقیماً درون پوشه رسانه‌ها در `C:\Jinus-Media\` ذخیره می‌شود.

### قانون ۶: استانداردهای رسمی زیرنویس (Whisper Standard & Viral Anton Style)

#### الف) دکترین تقطیع کلاستری OpenAI Whisper
- **اندازه بهینه کلاستر:** طبق استانداردهای رسمی Whisper برای ویدیوهای عمودی شبکه‌های اجتماعی، هر خط زیرنویس باید به خوشه‌های ارگونومیک **۲ تا ۳ کلمه‌ای** شکسته شود تا در کسری از ثانیه در موبایل خوانده شود.
- **انطباق آکوستیک شکل‌موج:** لبه‌های زمانی هر کلاستر و تک‌تک کلمات بر اساس اسکن پوش انرژی صوت (RMS Waveform) با دقت صدم ثانیه تعیین می‌شود.

#### ب) تایپوگرافی، کپسول و کارائوکه زنده
- **فونت رسمی ریلز وایرال:** قلم **`Anton`** (Bold Condensed، کشیده، پرقدرت و خوانا).
- **اندازه قلم:** **۷۶ تا ۸۰ پیکسل** (افزایش از ۵۴px قدیمی جهت خوانایی کامل روی موبایل).
- **کپسول پس‌زمینه (Pill Box):** کادر مستطیلی با گوشه‌های کاملاً گرد، رنگ زغالی نیمه‌شفاف با اوپاسیتی ۵۰ تا ۶۰ درصد (`BackColour=&H5014181C&`، `BorderStyle=3`، `Outline=20`). کپسول کاملاً ثابت بوده و هیچ‌گونه پرش یا لرزش اندازه ندارد.
- **هایلایت کلمه‌به‌کلمه (Word-by-Word Active Highlight):**
  - کلمه در حال گفتار: فیروزه‌ای درخشان نئونی (`#22D3EE` / `&HEED322&`).
  - سایر کلمات خط: سفید خالص (`#FFFFFF` / `&HFFFFFF&`).
- **حاشیه امن:** مرکز پایین تصویر (`bottom-center`) در ارتفاع امن بالای دکمه‌های اینستاگرام (`MarginV=390`).

#### ج) قانون لوگوی بازینو (عدم الصاق روی ویدئو)
- **ممنوعیت الصاق لوگوی بازینو:** لوگوی بازینو نباید روی ویدئو الصاق شود مگر آنکه مالک صراحتاً دستور دهد، زیرا پیج رسمی بازینو در اینستاگرام و پلتفرم انتشار به صورت خودکار واترمرک و نشان را روی ویدئو اعمال می‌کند.

### قانون ۷: پایش فعال و بررسی ۱ دقیقه‌ای وضعیت کادر جنریت و خطاها (Active 1-Minute Polling)
- **پایش منظم پس از ارسال پرامپت:** بلافاصله پس از کلیک روی دکمهٔ Start generation و ارسال پرامپت، ایجنت موظف است وضعیت تایل جنریت در بالای صفحه کنار کاراکتر شیت را **دقیقاً هر ۱ دقیقه یک بار** پایش کند.
### قانون ۸: رویه استاندارد بسته‌بندی محتواهای آماده انتشار (Publishing Package Doctrine)
- **پوشه‌بندی اختصاصی برای هر محتوا:** برای هر قطعه محتوای آماده انتشار (ریلز، کروسل، استوری)، باید یک پوشه مجزا درون `Ready-Media/` ساخته شود (مثال: `Ready-Media/reel-01-mona-loss-discipline/`).
- **پاکسازی ریشه پوشه رسانه‌های آماده:** کلیه فایل‌های تست، فریم‌های QC و فایل‌های میانی باید از ریشه `Ready-Media/` حذف شده و ریشه همواره پاکیزه باشد.
- **محتویات اجباری هر پک انتشار:**
  1. **فایل رسانه اصلی:** `<slug>.mp4` یا اسلایدهای کروسل (بدون واترمرک دستی و با زیرنویس نهایی کارائوکه).
  2. **فایل کپشن مصوب (`caption.txt`):** متنی که تمامی بررسی‌های لینتر `ig-caption` (طول، خط اول زیر ۱۲۵ کاراکتر، واژگان کلیدی سئو، تک‌درخواست CTA و هشتگ‌های محدود) را با وضعیت `READY` پشت سر گذاشته باشد.
  3. **شناسنامه انتشار (`metadata.json`):** حاوی شناسه پست، عنوان، فایل‌های پیوست، زبان‌ها، پلتفرم‌های هدف (اینستاگرام، یوتیوب شورتس، تیک‌تاک، تلگرام، فیسبوک)، کلمه کلیدی CTA و وضعیت انتشار.

---

## ۲. مسیرهای کلیدی پروژه

| عنوان | مسیر |
| :--- | :--- |
| **پوشه رسانه‌های آماده نهایی پروژه** | `C:\projects\Antigravity Bazino\Ready-Media\` |
| **مخزن اصلی ابزارها و اسناد فلو و ویدز** | `C:\projects\Antigravity Bazino\Publish-Studio\` |
| **راهنمای تولید ویدئو با گوگل ویدز** | `C:\projects\Antigravity Bazino\Publish-Studio\GOOGLE_VIDS_PRODUCTION_GUIDE.md` |
| **ماتریکس DOM گوگل فلو** | `C:\projects\Antigravity Bazino\Publish-Studio\DOM_MATRIX_MAP.md` |
| **ماتریکس DOM گوگل ویدز** | `C:\projects\Antigravity Bazino\Publish-Studio\VIDS_DOM_MATRIX_MAP.md` |
| **پوشه پردازش خام و دانلود اولیه رسانه‌ها** | `C:\Jinus-Media\` |
| **کاراکتر شیت مونا** | `C:\projects\Antigravity Bazino\Assets\mona\mona-bazino-character-sheet.jpg` |
| **تصویر تمام‌قد مونا** | `C:\projects\Antigravity Bazino\Assets\mona\mona-bazino-fullbody-crop.png` |
| **اسکریپت سوزاندن زیرنویس کارائوکه و بج** | `C:\projects\Antigravity Bazino\scripts\karaoke_burn.py` |
| **فونت رسمی پوپینز بلد** | `C:\projects\Antigravity Bazino\Assets\fonts\Poppins-Bold.ttf` |
| **نشان رسمی بازینو برای ویدیوها** | `C:\projects\Antigravity Bazino\Assets\bazino-badge.png` |
| **آدرس پورت CDP کروم کاربر** | `http://127.0.0.1:9222` |
| **آدرس پورت CDP کروم اختصاصی ایجنت** | `http://127.0.0.1:9335` |
| **پروفایل اختصاصی کروم فلو و ویدز** | `C:\Users\payman\.gemini\chrome-agent` |


### نکتهٔ ساختار ریپو (به‌روزرسانی‌شده)
- مسیرهای **`C:\projects\...`** بالا مربوط به **PC مالک** هستند و همان‌طور که هستند معتبرند؛ تغییر نکرده‌اند.
- در **ریپوی گیت** (برنچ `arena/d7168c76-bazino-advertising-campaign`)، محتوای پوشهٔ `Publish-Studio` دسته‌بندی شده و ساختار قدیمی `Publish-Studio/` دیگر وجود ندارد. نگاشت:

| مسیر قدیمی (`Publish-Studio/...`) | مسیر در ریپو |
| :--- | :--- |
| `GOOGLE_VIDS_PRODUCTION_GUIDE.md` | `GOOGLE_VIDS_PRODUCTION_GUIDE.md` (ریشه) |
| `RTX5090_SERVER_PRODUCTION_GUIDE.md` | `RTX5090_SERVER_PRODUCTION_GUIDE.md` (ریشه) |
| `GUIDE_AND_RULES.md` (همین سند) | `GUIDE_AND_RULES.md` (ریشه) |
| `ig-reel/*` | `instagram-reels/*` |
| `ig-carousel/*`، `carousel-template-v2/*` | `instagram-carousels/ig-carousel/*`، `instagram-carousels/carousel-template-v2/*` |
| `ig-plan/*` | `instagram-plans/*` |
| `ig-viral/*` | `viral-research/*` |
| `Assets/*` (داخل Publish-Studio) | `brand-assets/*` |
| `VistaMare/*` | `vista-mare/*` |
| `rtx5090-api/*`، `rtx5090-server-handoff.md` | `rtx5090-api/*` |
| `DOM_MATRIX_MAP.*`، `VIDS_DOM_MATRIX_MAP.*`، `COLLECTION-CYCLE.md`، رتبه‌بندی‌ها و `educational/game_collection_viral_posts.md` | `collections-research/*` |
| اسکریپت‌های تولید و رتبه‌بندی (`generate_carousel_v2.py`، `assemble_final_reel.py`، `publish_zernio_reel.mjs`، `build_all_rankings_reports.py`، `rank_collections_creators.py`) | `content-scripts/*` |
| اسکریپت‌های مرورگر و CDP (`*.py` و `open-chrome-cdp.ps1` باقی‌مانده) | `ui-automation/*` |

- **فایل‌هایی که در ریپو موجود نیستند:** `Doc/instagram-agent-skill-production-guide.md`، `scripts/karaoke_burn.py`، `Assets/bazino-badge.png`. اگر لازم‌اند باید از PC به ریپو منتقل شوند.


---

## ۳. اسکریپت‌ها، ابزارها و کالکشن‌های ذخیرهٔ اینستاگرام بازینو

این بخش مرجع رسمی اسکریپت‌ها، کالکشن‌های مرجع و داده‌های استخراج‌شده برای مهندسی معکوس و تحلیل محتوای وایرال است:

### الف) شناسه‌ها و نشانی‌های ثابت کالکشن‌های اکانت (`fartash_v`)
| نام کالکشن | شناسه یکتای اینستاگرام (Collection ID) | تعداد پست‌های واقعی | تعداد پیج‌های یکتا | نشانی اینستاگرام |
| :--- | :--- | :---: | :---: | :--- |
| **آموزش گیم** | `3535547046633112` | ۷۴ پست | ۵۵ پیج | `https://www.instagram.com/fartash_v/saved/_/3535547046633112/` |
| **game** | `1050777357950348` | ۱۷۰ پست | ۱۴۰ پیج | `https://www.instagram.com/fartash_v/saved/game/1050777357950348/` |

---

### ب) اسکریپت‌های اتوماسیون مستقر در این پوشه (`Publish-Studio`)
1. **راه‌اندازی مرورگر کروم با CDP و پروفایل رسمی:**
   - **فایل:** `open-chrome-cdp.ps1` (ریپو: `ui-automation/open-chrome-cdp.ps1`)
   - **کارکرد:** باز کردن پنجره کروم مستقل با پورت اشکال‌زدایی `9335` و اکانت `Profile 1` (`fartashvarahram@gmail.com`).
   - **دستور اجرا در پاورشل:**
     ```powershell
     powershell -ExecutionPolicy Bypass -File "C:\projects\Antigravity Bazino\Publish-Studio\open-chrome-cdp.ps1"
     ```

2. **واکشی کامل کالکشن‌ها (GraphQL Pagination Harvester):**
   - **فایل:** `fetch_complete_collections.py` (ریپو: `ui-automation/fetch_complete_collections.py`)
   - **کارکرد:** استخراج کامل تمامی صفحات و پست‌ها از طریق GraphQL اینستاگرام بدون باگ‌های اسکرول DOM.
   - **دستور اجرا:**
     ```powershell
     python "C:\projects\Antigravity Bazino\Publish-Studio\fetch_complete_collections.py"
     ```

3. **رتبه‌بندی، اعتبارسنجی فالوورها و تفکیک عدم تکرار:**
   - **فایل:** `rank_collections_creators.py` (ریپو: `content-scripts/rank_collections_creators.py`)
   - **کارکرد:** استخراج لیست اکانت‌ها، واکشی آمار واقعی فالوورها و سورت نزولی با تضمین عدم تکرار آدرس‌ها در دو لیست.
   - **دستور اجرا:**
     ```powershell
     python "C:\projects\Antigravity Bazino\Publish-Studio\rank_collections_creators.py"
     ```

4. **تولید گزارش‌های ساختاریافته جامع و تفکیکی:**
   - **فایل:** `build_all_rankings_reports.py` (ریپو: `content-scripts/build_all_rankings_reports.py`)
   - **کارکرد:** ساخت فایل‌های خروجی Markdown و JSON برای ۱۰ پیج برتر هر دسته و لیست کامل ۱۸۸ تولیدکننده.
   - **دستور اجرا:**
     ```powershell
     python "C:\projects\Antigravity Bazino\Publish-Studio\build_all_rankings_reports.py"
     ```

---

### ج) اسناد و داده‌های ذخیره‌شده (مسیر در ریپو)
1. **۱۰ پیج برتر هر کالکشن:** `collections-research/top10_collections_ranked.md` و `collections-research/top10_collections_ranked.json`
2. **فهرست کامل ۱۸۸ پیج تولیدکننده محتوا:** `collections-research/all_collections_creators_ranked.md` و `collections-research/all_collections_creators_ranked.json`
3. **پست‌های وایرال آموزشی:** `viral-research/educational_viral_posts.md`، `viral-research/educational_viral_posts.json` و سند مهندسی معکوس `viral-research/swipe.md`
4. **پست‌های وایرال کالکشن Game:** `viral-research/game_collection_viral_posts.md` و `viral-research/game_collection_viral_posts.json`
5. **استراتژی و تقویم هفتگی (ig-plan):** `instagram-plans/strategy.md`، `instagram-plans/weekly_content_calendar.md`، `instagram-plans/schedule_tracker.py` و `instagram-plans/schedule.json`
6. **ریلزها (ig-reel):** `instagram-reels/REEL_PRODUCTION_SPEC.md`، `instagram-reels/generate_reel_package.py`، `instagram-reels/day1_reels_package.json` و `instagram-reels/WORKFLOW.md`
7. **کاروسل (ig-carousel):** `instagram-carousels/ig-carousel/` (شامل `carousel-post.md`، `carousel-manifest.json` و `README-fa.md`) و قالب `instagram-carousels/carousel-template-v2/`
8. **ماتریکس‌های رابط گوگل:** `collections-research/DOM_MATRIX_MAP.md` و `.json`، `collections-research/VIDS_DOM_MATRIX_MAP.md` و `.json`
9. **راهنمای سرور RTX 5090:** `RTX5090_SERVER_PRODUCTION_GUIDE.md` (ریشه) و `rtx5090-api/rtx5090-server-handoff.md`
10. **راهنمای ویدیو با گوگل ویدز:** `GOOGLE_VIDS_PRODUCTION_GUIDE.md` (ریشه)
