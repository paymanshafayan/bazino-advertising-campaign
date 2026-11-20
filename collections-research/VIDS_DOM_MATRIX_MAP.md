# نقشه ماتریکسی عناصر رابط کاربری Google Vids (VIDS DOM Matrix Map)

این سند نقشه کامل صفحات، منوها، نوار ابزار، پنل‌های کناری هوش مصنوعی و تایم‌لاین وب‌سایت **Google Vids (`docs.google.com/videos`)** را بر اساس اسکن زندهٔ DOM از طریق **Chrome CDP (پورت `9335`)** روی پنجره با ابعاد `1280×788` (Viewport: `1264×693`) ثبت کرده است.

---

## ۱. ماتریکس صفحات و نشانی‌ها

| صفحه | الگوی آدرس URL | کارکرد اصلی |
| :--- | :--- | :--- |
| **داشبورد اصلی (Home)** | `https://docs.google.com/videos/u/0/` | ایجاد پروژه جدید، الگوها، Docs/Slides to video و فهرست پروژه‌های اخیر |
| **پروژه جدید آنی** | `https://vids.new` یا `https://docs.google.com/videos/create` | ساخت مستقیم پروژه تازه در Google Vids |
| **محیط ویرایشگر (Editor)** | `https://docs.google.com/videos/d/{DOC_ID}/edit` | استیج صحنه‌ها، تایم‌لاین، پنل‌های AI، تنظیم ابعاد و خروجی MP4 |

---

## ۲. ماتریکس صفحه داشبورد اصلی (`https://docs.google.com/videos/u/0/`)

| المان | نوع / تگ | سلکتور / ویژگی شناسایی | مختصات مرجع (`x, y, w, h`) | وضعیت حساب فعلی |
| :--- | :--- | :--- | :--- | :--- |
| **جست‌وجو (Search)** | `input[type="text"]` | `input[aria-label="Search bar"][placeholder="Search"]` | `321, 23, 662×20` | فعال |
| **ویدیوی خام (Blank video)** | `button` | `button` با متن `Blank video` | `323, 132, 154×48` | فعال |
| **ضبط (Record)** | `button` | `button` با متن `Record` | `493, 132, 126×48` | فعال |
| **آپلود (Upload)** | `button` | `button` با متن `Upload` | `635, 132, 126×48` | فعال |
| **قالب‌ها (Templates)** | `button` | `button` با متن `Templates` | `777, 132, 148×48` | فعال |
| **آواتار هوش مصنوعی (AI avatar)** | `div[role="button"]` | `.appsDocsGettingStartedEntryPointSelectionViewButton` با متن `AI avatar` | `266, 196, 167×178` | فعال |
| **تبدیل داک به ویدیو (Docs to video)** | `div[role="button"]` | `.appsDocsGettingStartedEntryPointSelectionViewButton` با متن `Docs to video` | `449, 196, 167×178` | فعال |
| **تبدیل اسلاید به ویدیو (Slides to video)** | `div[role="button"]` | `.appsDocsGettingStartedEntryPointSelectionViewButton` با متن `Slides to video` | `632, 196, 167×178` | فعال |
| **آواتار شخصی (Personal avatar)** | `div[role="button"]` | `.getting-started-dialog-navigation-button-premium-disabled` با متن `Upgrade Personal avatar` | `815, 196, 167×178` | **غیرفعال (نیاز به ارتقای پلن)** |
| **کارت پروژه‌های اخیر** | `div[role="option"]` | `.docs-homescreen-item-section .docs-homescreen-grid-item` | مثال: `164, 482, 210×196` | فعال |
| **منوی بیشتر پروژه** | `div[role="button"]` | `.docs-homescreen-item-overflow[aria-label="More actions. Popup button."]` | مثال: `335, 636, 30×30` | فعال |

---

## ۳. ماتریکس نوار بالا و منوها در ویرایشگر (`.../videos/d/{DOC_ID}/edit`)

### الف) نوار عنوان و دکمه‌های اصلی بالا
| المان | سلکتور دقیق | کارکرد / میانبر | مختصات مرجع |
| :--- | :--- | :--- | :--- |
| **نام فایل (Rename)** | `input.docs-title-input[aria-label="Rename"]` | تغییر نام پروژه | `54, 10, 97×24` |
| **نشانگر ذخیره** | `#docs-save-indicator-badge` | نمایش وضعیت `Saved to Drive` | `189, 10, 118×28` |
| **پخش کل ویدیو (Play)** | `#sketchy-play-doc-as-video` | پیش‌نمایش تمام‌صفحه (`Ctrl+F5`) | `970, 10, 92×40` |
| **اشتراک‌گذاری (Share)** | `div[role="button"].scb-split-button` | تنظیمات دسترسی پروژه | `1072, 10, 91×40` |

### ب) نوار منوها (`[role="menubar"]`)
| منو | شناسه (`id`) | مختصات مرجع | آیتم‌های کلیدی داخل منو |
| :--- | :--- | :--- | :--- |
| **File** | `#docs-file-menu` | `54, 34, 38×24` | `New video`, `Open Ctrl+O`, `Slides to video`, `Docs to video`, `Make a copy ►`, `Share`, **`Download ►`** (`MP4 video (.mp4)`, `GIF animation (.gif)`), `Export to Drive`, `Export to YouTube`, `Rename`, `Language ►`, **`Video size`** |
| **Edit** | `#docs-edit-menu` | `92, 34, 41×24` | ویرایش، Undo/Redo، کپی/پیست |
| **View** | `#docs-view-menu` | `133, 34, 46×24` | نمایش/بزرگ‌نمایی/حالت تمام‌صفحه |
| **Insert** | `#docs-insert-menu` | `179, 34, 53×24` | `Generate an avatar`, `Generate a voiceover`, `Generate music`, `Generate an image`, `Video recording`, `Audio recording`, `Upload`, `Drive & Photos`, `Stock & web`, `Captions`, `Text`, `Shapes and lines`, `New scene Ctrl+M`, `Templates` |
| **Format** | `#docs-format-menu` | `232, 34, 62×24` | قالب‌بندی متن و اجزای صحنه |
| **Scene** | `#flix-scene-menu` | `294, 34, 56×24` | `New scene Ctrl+M`, `Templates`, `Styles ►`, `Duplicate scenes`, `Delete scenes`, `Split scene at playhead Ctrl+Shift+S`, `Background ►`, `Transition Ctrl+Alt+R`, `Animation Ctrl+Shift+M` |
| **Arrange** | `#sketchy-arrange-menu` | `350, 34, 68×24` | چینش و لایه‌بندی اشیاء روی بوم |
| **Tools** | `#docs-tools-menu` | `418, 34, 50×24` | `Spelling ►`, `Linked objects`, `Closed captions`, `Preferences`, `Accessibility` |
| **Help** | `#docs-help-menu` | `468, 34, 45×24` | راهنما و جست‌وجوی منوها (`Alt+/`) |

> **نکته اتوماسیون منوها (Closure `goog-menuitem`):** منوهای بالای Google Vids از کتابخانه Google Closure استفاده می‌کنند. برای باز کردن منو، کلیک ماوس نیتیو CDP (`Input.dispatchMouseEvent` با `mousePressed` و `mouseReleased`) روی مرکز آیتم انجام شود. برای زیرمنوها (مثل `Download ►`) با `mouseMoved` یا کلید `ArrowRight` زیرمنو باز می‌شود و برای آیتم‌های انتهایی (مثل `Video size`) ناوبری با `ArrowDown` + `Enter` مطمئن‌تر است.

---

## ۴. ماتریکس نوار ابزار سریع (Toolbar - ردیف `y = 70`)

| المان | شناسه (`id`) / سلکتور | کارکرد | مختصات مرجع |
| :--- | :--- | :--- | :--- |
| **جست‌وجوی منوها** | `button.assisted-actions-omnibox-button` / `input.docs-omnibox-input` | میانبر `Alt+/` | `24, 71, 35×28` |
| **صحنه جدید** | `#newSceneButton` | افزودن صحنه (`Ctrl+M`) | `62, 70, 30×30` |
| **بازگشت / جلو** | `#undoButton` / `#redoButton` | `Ctrl+Z` / `Ctrl+Y` | `101, 70` / `133, 70` |
| **بزرگ‌نمایی** | `#zoomButton` / `input[aria-label="Zoom"]` | زوم بوم | `165, 70` / `199, 71` |
| **انتخاب (Select)** | `#selectButton` | ابزار انتخاب (`Esc`) | `284, 70, 30×30` |
| **استایل / پس‌زمینه / انیمیشن** | `#contentStylingButton`, `#backgroundButton`, `#transitionButton`, `#animationButton` | شخصی‌سازی استایل و حرکت صحنه | `450..587, 70` |
| **مخفی‌کردن منوها** | `#viewModeButton` | تمام‌فضاکردن ویرایشگر (`Ctrl+Shift+F`) | `1117, 70, 30×30` |

---

## ۵. ماتریکس ریل ابزارهای سمت راست (`Right Rail - x = 1190`)

تمام ۱۱ ابزار اصلی تولید محتوا و دارایی در ستون راست داخل کانتینر `#content-library-rail-server-rendered-refreshed` قرار دارند (همگی تگ `button`):

| ردیف | عنوان ابزار | شناسه ثابت (`id`) | `aria-label` | مختصات مرجع |
| :---: | :--- | :--- | :--- | :--- |
| ۱ | **آواتار AI** | `#content-library-rail-avatars-element` | `Generate an avatar` | `1190, 71, 60×32` |
| ۲ | **صدابردار AI (Voiceover)** | `#content-library-rail-voiceovers-element` | `Generate a voiceover` | `1190, 133, 60×32` |
| ۳ | **ساخت موسیقی (Lyria 3.5)** | `#content-library-rail-music-generation-element` | `Generate music` | `1190, 195, 60×32` |
| ۴ | **ساخت تصویر AI** | `#content-library-rail-image-synthesis-element` | `Generate an image` | `1190, 257, 60×32` |
| ۵ | **ضبط تصویر/صدا** | `#content-library-rail-recording-element` | `Record` | `1190, 319, 60×32` |
| ۶ | **درایو و فوتوز (Uploads)** | `#content-library-rail-my-media-element` | `Drive and Photos` | `1190, 381, 60×32` |
| ۷ | **استاک و وب** | `#content-library-rail-stock-media-element` | `Stock and web` | `1190, 443, 60×32` |
| ۸ | **زیرنویس (Captions)** | `#content-library-rail-captions-element` | `Captions` | `1190, 505, 60×32` |
| ۹ | **متن روی تصویر (Text)** | `#content-library-rail-text-element` | `Insert text` | `1190, 567, 60×32` |
| ۱۰ | **قالب‌ها (Templates)** | `#content-library-rail-templates-element` | `Insert from templates` | `1190, 629, 60×32` |
| ۱۱ | **اشکال و خطوط (Shapes)** | `#content-library-rail-shape-and-line-element` | `Insert shapes and lines` | `1190, 691, 60×32` |

> **نکته کلیدی اسکن حساب فعلی (`fartashvarahram@gmail.com`):** دکمهٔ مستقل `Create AI videos` / `AI video` (موتور Veo / Omni) در نوار راست و داشبورد این اکانت ظاهر نشده است. برای تولید کلیپ‌های ویدئویی AI کاراکتر مونا همچنان **Google Flow** یا آپلود کلیپ از طریق **Drive and Photos** (`#content-library-rail-my-media-element`) استفاده می‌شود.

---

## ۶. ماتریکس داخلی پنل‌های کناری AI (Sidebars)

وقتی یکی از دکمه‌های ریل راست کلیک شود (`aria-pressed="true"`), پنل کناری بین `x ≈ 866..1185` (یا `974..1262` در سایدشیت‌های جدیدتر Material 3) باز می‌شود.

### الف) پنل آواتار (`#content-library-rail-avatars-element`) و صداگذاری (`#content-library-rail-voiceovers-element`)
- **کانتینر اصلی:** `div.appsFlixScriptsSidebarContainer[role="complementary"]` (در حالت آواتار کلاس `appsFlixScriptsSidebarAvatarMode` هم دارد).
- **دکمه بستن پنل:** `div[role="button"][aria-label="Close"]` داخل `.appsDocsUiSidebarClose`
- **تب محدوده صحنه:** `[role="tab"]` با متن `Current scene` و `All scenes` (`.docs-sidebar-toggle-tab`)
- **دکمه ویرایش اسکریپت:** `button.appsFlixScriptsSidebarKeyboardEditButton` (متن: `Edit script`)
- **ناحیه ویرایش متن اسکریپت:** `div[role="form"][aria-label="Scripts"]` داخل `.appsFlixScriptsSidebarWorkspaceContainer`
- **ناوبری بین صحنه‌ها:** `[role="button"][aria-label="Previous script"]` و `[aria-label="Next script"]`
- **تغییر آواتار / صدا:**
  - در آواتار: `[role="button"][aria-label="Change the avatar"]` (متن: `Change`؛ لیست آواتارهای پیش‌فرض شامل `Finley`, `Levi`, `Jamal`, `Jack`, `Aditi`, `Leo`, `Amal`, `Reynold`, `Benjamin`, `Kai`, `Lux`, `Joseph`, `Lennon` و دکمه `Select`)
  - در صداگذاری: `[role="button"][aria-label="Change the voice"]` (پیش‌فرض مشاهده‌شده: `Vira - Forward, medium pitch`)
- **دکمه ساخت/درج:**
  - آواتار: `.docs-gm3-filled-button[role="button"]` (متن: `Preview` و پس از تایپ اسکریپت `Insert`)
  - صداگذاری: `.docs-gm3-filled-button[role="button"][aria-label="Insert voiceover"]`

### ب) پنل تولید موسیقی (`#content-library-rail-music-generation-element` — موتور `Lyria 3.5`)
- **تیتر پنل:** `h2.appsElementsSideSheetTitle` (`Generate a song` — موتور `Lyria 3.5`)
- **کادر پرامپت موسیقی:** `textarea[aria-label^="Describe your song"]` (`placeholder="Describe your song. You can include genre, mood, instruments, or add your own lyrics."`)
- **تنظیم خواننده/بی‌کلام:** `button.javascriptMaterialdesignGm3WizButtonDropdownFilled-button` (پیش‌فرض: `Full • Vocal settings`)
- **دکمه ساخت:** `button.javascriptMaterialdesignGm3WizButtonFilled-button` (متن: `Generate`)

### ج) پنل تولید تصویر (`#content-library-rail-image-synthesis-element`)
- **تیتر پنل:** `h2.appsDocsUiSidebarHeading` (`Generate an image`)
- **کادر پرامپت تصویر:** `textarea.docs-image-synthesis-search-bar-input` (`placeholder="Describe your idea.\ne.g., an object in a location in color"`)
- **نسبت ابعاد تصویر:** `[role="button"][aria-label="Aspect ratio"].appsDocsAiGenerativeaiImageGenerateImagesInputAspectRatioPicker`
- **استایل تصویر:** `[role="listbox"][aria-label="Add a style"].imageGenerationStylePresetsSelect`

### د) پنل زیرنویس (`#content-library-rail-captions-element`)
- **تیتر پنل:** `h2.appsElementsSideSheetTitle` (`Captions`)
- **محدوده اعمال:** `button[role="radio"]` با متن `All scenes` و `Current scene`
- **استایل‌های آماده زیرنویس:** `button.appsFlixCaptionsSelectionButtonTemplateButton` (دارای `aria-label` توصیفی برای هر استایل تایپوگرافی)

### هـ) پنل Drive & Photos (`#content-library-rail-my-media-element`)
- از طریق `iframe` داخلی `https://docs.google.com/picker/v2/home?...` (پشتیبانی از ویدئوهای `video/mp4`, `video/quicktime`, `video/webm`, صداهای `mp3/wav/m4a/ogg/flac` و تصاویر `jpg/png/gif`) محتوا را مستقیماً از Google Drive یا Photos وارد صحنه می‌کند.

---

## ۷. ماتریکس تایم‌لاین و صحنه‌ها (`y ≈ 450..693`)

| المان | سلکتور دقیق | کارکرد | مختصات مرجع |
| :--- | :--- | :--- | :--- |
| **نمایش/مخفی کردن زمان‌بندی** | `button[aria-label="Show timing"]` (متن: `Hide timing`) | سوییچ نمای استوری‌بورد و تایم‌لاین | `48, 452, 105×32` |
| **رفتن به ابتدا** | `button[aria-label="Go to beginning"]` | پرش هد پخش به `00:00.0` | `510, 455, 26×26` |
| **پخش تایم‌لاین** | `button[aria-label="Preview"]` | پخش/توقف داخل ویرایشگر | `542, 452, 38×32` |
| **زوم متناسب با تایم‌لاین** | `button.appsFlixTimelineZoomControlsZoomToFitButton[aria-label="Zoom to fit timeline"]` | فیت کل صحنه‌ها در عرض صفحه | `901, 455, 32×26` |
| **اسلایدر زوم تایم‌لاین** | `button[aria-label="Zoom out"]` / `div[role="slider"].docs-material-slider` / `button[aria-label="Zoom in"]` | کنترل دقت تایم‌لاین | `943..1068, 455` |
| **تایل‌های صحنه (Scenes)** | `rect[role="button"][aria-label^="Scene "]` | انتخاب صحنه (`Scene 1 of 3`, ...) | `48, 524, 338×72` |
| **ترنزیشن بین صحنه‌ها** | `svg[role="button"][data-tooltip="Add transition"]` | افزودن ترنزیشن بین صحنه‌ها | `383, 541, 24×36` |
| **دستگیره برش ویدیو/صحنه** | `div[role="slider"][aria-label="Trim start"]` و `[aria-label="Trim end"]` | تنظیم ابتدا و انتهای کلیپ/صحنه | `48, 601` / `374, 601` |
| **دستگیره برش صوت** | `div[role="slider"][aria-label="Trim audio start"]` و `[aria-label="Trim audio end"]` | برش ترک موسیقی/صدا (مثل `ara_beni_30s_clean.mp3`) | `48, 633` / `1086, 633` |
| **افزودن صحنه در تایم‌لاین** | `div[role="button"][aria-label="New scene (Ctrl+M)"]` | افزودن صحنه جدید در انتهای تایم‌لاین | `1092, 539, 40×40` |
| **نسبت بوم صحنه (SVG ViewBox)** | `svg[viewBox]` داخل استیج | در پروژه `GTA_Reel`: `viewBox="0 0 205740 361299.51"` (نسبت عمودی `9:16`) | مرکز استیج |
