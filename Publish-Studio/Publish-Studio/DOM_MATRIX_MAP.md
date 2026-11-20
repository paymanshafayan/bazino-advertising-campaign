# نقشه ماتریکسی عناصر رابط کاربری Google Flow (DOM Matrix Map)

این سند نقشه کامل، آدرس‌ها، المان‌ها و الگوهای تعامل با وب‌سایت **Google Flow (`flow.google.com`)** را از طریق پروتکل Chrome DevTools Protocol (CDP) ثبت کرده است تا هرگونه اقدام اتوماسیون بدون آزمون و خطا و به‌صورت آنی اجرا شود.

---

## ۱. ماتریکس صفحات و آدرس‌ها

| صفحه / کامپوننت | الگو / آدرس URL | عملکرد اصلی |
| :--- | :--- | :--- |
| **صفحه کار کارگاهی (Project)** | `https://flow.google.com/project/{project_id}` | محیط اصلی صحنه‌ها، ارسال پرامپت و گالری تولیدات |
| **استودیوی کاراکتر (Character Studio)** | `https://flow.google.com/project/{project_id}/character` | آپلود کاراکتر شیت، نام‌گذاری، پیش‌نمایش و ذخیره کاراکتر |

---

## ۲. ماتریکس المان‌ها و دستورات CDP

### الف) استودیوی ثبت کاراکتر (Character Studio)
1. **المان آپلود فایل (`input[type="file"]`):**
   - **سلکتور:** `input[type="file"][accept*=".png,.jpg,.jpeg"]`
   - **الگوی CDP:** چون این المان به صورت مخفی رندر می‌شود، ارسال فایل از طریق متد CDP زیر با استفاده از `objectId` انجام می‌شود:
     ```json
     {
       "method": "DOM.setFileInputFiles",
       "params": {
         "files": ["C:\\projects\\Antigravity Bazino\\Assets\\mona\\mona-bazino-character-sheet.jpg"],
         "objectId": "<EVALUATED_OBJECT_ID>"
       }
     }
     ```
2. **ورودی نام کاراکتر:**
   - **سلکتور:** `input.name-input` یا `input[aria-label="Character name"]`
   - **رویداد:** مقداردهی `.value = 'Mona'` به همراه انتشار رویدادهای `input`، `change` و `Enter`.
3. **عنوان بالای صفحه:**
   - **سلکتور:** `input.editable-text-input`
4. **دکمه ذخیره و تایید (Done):**
   - **سلکتور:** `button.flow-button-secondary` حاوی متن `Done`.

---

### ب) نوار ابزار و کادر پرامپت (Composer & Prompt Area)
1. **ادیتور متن پرامپت (ProseMirror Rich Text):**
   - **سلکتور:** `div.ProseMirror`
   - **نکته کلیدی:** ادیتور از کتابخانه ProseMirror استفاده می‌کند. تنظیم مستقیم `innerHTML` استیت درونی را فعال نمی‌کند. روش استاندارد: فوکوس ادیتور + استفاده از `Input.insertText` در CDP.
2. **دکمه پاکسازی پرامپت (Clear Prompt):**
   - **سلکتور:** `button[aria-label="Clear prompt"]` یا `button.clear-button`
3. **دکمه افزودن محتویات/کاراکتر (Add Ingredients):**
   - **سلکتور:** `button[aria-label="Add ingredients to the prompt box"]` یا `button.add-menu-trigger`
4. **پنل افزودن کاراکتر (Popover Overlay):**
   - **کانتینر:** `.add-menu-popover-container` در `.cdk-overlay-pane`
   - **دکمه الصاق به پرامپت:** `button.detail-add-to-prompt-btn` (متن: `Add to prompt`)
5. **تراشه (Chip) کاراکتر الصاق‌شده:**
   - **سلکتور:** `.chip-container` با فرزند `img.chip-image`
6. **دکمه جنریت اصلی (Generate Button):**
   - **سلکتور:** `button.generate-icon-button` یا `button[aria-label="Start generation"]`
   - **نحوه کلیک:** کلیک ماوس نیتیو از طریق CDP (`Input.dispatchMouseEvent` با مختصات مرکز دکمه).

---

### ج) تنظیمات پروژه (Project Settings Drawer)
برای باز کردن کشوی تنظیمات هوش مصنوعی از دکمه `button.agent-action-button[aria-label="Settings"]` با آیکون `tune` استفاده می‌شود:
1. **تاییدیه خودکار قبل از جنریت (Confirm Before Generating):**
   - گزینه «هرگز تایید نخواه / خودکار بساز»: `#mat-radio-1` یا `#mat-radio-1-input` (متن: `Never`)
   - گزینه پیش‌فرض: `#mat-radio-0` (`Always`)
2. **ابعاد پیش‌فرض ویدئو (Video Generation Default):**
   - دکمه ابعاد 9:16 عمودی: `#mat-button-toggle-20-button` (متن: `crop_9_16 9:16`)
3. **ابعاد پیش‌فرض تصویر (Image Generation Default):**
   - دکمه ابعاد 9:16 عمودی: `#mat-button-toggle-14-button` (متن: `crop_9_16 9:16`)
4. **دکمه ذخیره تنظیمات:**
   - **سلکتور:** `button.settings-save-button`

---

### د) پیام‌های سیستم و تاییدیه هوش مصنوعی (Flow Agent Session)
1. **کامپوننت پیام تایید اعتبار:**
   - **تگ:** `<flow-permission-message>`
2. **گزینه «همیشه تایید کن» (Always Approve):**
   - **سلکتور:** `flow-permission-message .option-row[role="radio"]` حاوی متن `Always approve`.
3. **بستن پنل چت ایجنت برای مشاهده گالری:**
   - **سلکتور:** `button.header-action[aria-label="Close"]`

---

### ه) گالری و دانلود با کیفیت ۱۰۸۰p+ مستقیم (Media Gallery & Native 1080p Export)
1. **تایل‌های ویدئویی:** `.media-tile`, `.generation-tile`
2. **پخش‌کننده ویدئو:** تگ `video`
3. **دکمه و منوی دانلود مستقیم با کیفیت بالا:**
   - **دکمه دانلود:** `button[aria-label="Download"]`
   - **انتخاب رزولوشن:** در منوی بازشده، گزینه‌های کیفیتی شامل `1080p` (یا بالاتر / 4K در صورت وجود) مستقیماً انتخاب می‌شود تا فلو ویدئو را با انکودینگ سروری با بالاترین رزولوشن رندر و تحویل دهد.
4. **ذخیره‌سازی مستقیم:** دریافت مستقیم ویدئوی باکیفیت و ذخیره درون دایرکتوری اختصاصی `C:\Jinus-Media\` (بدون نیاز به مراحل اضافی FFmpeg).
