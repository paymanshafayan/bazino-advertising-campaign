# راه‌اندازی سرور پورتال + Chromium + پل اتصال به مرورگر کارفرما

**منبع:** استخراج‌شده از ریپوی `paymanshafayan/bazino-gamenet-portal`، برنچ `arena/01a0a0e1-bazino-gamenet-portal`
**فایل‌های مرجع اصلی:** `HANDOFF_PROMPT.md` (بخش‌های ۰–۴)، `docs/ops/BROWSER_CONNECTION_GUIDE.md`، `docs/ops/CDP_BROWSER_BRIDGE.md`، `cdp-tools/bridge-full.ps1`
**تاریخ استخراج:** 2026-09-17

> این سند سه رویه مستقل را کنار هم می‌گذارد:
> **الف)** بالا آوردن سرور زنده پورتال در سندباکس · **ب)** راه‌اندازی Chromium واقعی برای تست بصری · **ج)** پل CDP برای هدایت مرورگر کارفرما از داخل سندباکس.

---

## ۰. خلاصه اجرایی (اگر عجله دارید)

```bash
# ── ۱) همگام‌سازی گیت (سندباکس ری‌ست می‌شود) ────────────────────────────────
cd /home/user/bazino-gamenet-portal
git fetch origin <branch> && git reset --hard FETCH_HEAD && git clean -fd

# ── ۲) وابستگی‌ها + کامپایل ماژول native + ترمیم ابزار رسانه ────────────────
npm install --ignore-scripts --no-audit --no-fund
(cd node_modules/better-sqlite3 && npx node-gyp rebuild --release --nodedir=/usr/local)
node -e "const D=require('better-sqlite3'); new D(':memory:'); console.log('SQLITE OK')"
node scripts/prepare-media-tools.mjs      # بدون این، تست‌های رسانه با EACCES می‌میرند

# ── ۳) سرور زنده ────────────────────────────────────────────────────────────
npx tsx server.ts        # → http://0.0.0.0:3000   (حتماً با start_process)

# ── ۴) محیط مرورگر ──────────────────────────────────────────────────────────
mkdir -p /home/user/browser-test && cp -r tests/e2e-browser/* /home/user/browser-test/
cd /home/user/browser-test
PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm ci
node bootstrap.cjs --ready
mkdir -p /tmp/fonts/Vazirmatn && cp node_modules/vazirmatn/fonts/ttf/*.ttf /tmp/fonts/Vazirmatn/
rm -rf /tmp/fonts-cache
export CHROMIUM_EXECUTABLE_PATH=/tmp/chromium LD_LIBRARY_PATH=/tmp/al2023 \
       FONTCONFIG_PATH=/tmp/fonts HOME=/tmp

# ── ۵) تست ──────────────────────────────────────────────────────────────────
node verify-env.mjs      # باید بگوید: OK: browser=149.0.7827.0 h1=hello bazino
node e2e-journey.mjs

# ── ۶) سوئیت کامل (از ریشه ریپو) ────────────────────────────────────────────
cd /home/user/bazino-gamenet-portal
npm run build            # اگر server.ts یا server/** عوض شده — تست API از dist/server.cjs اجرا می‌شود
npm test
```

**زمان تقریبی کل: ۴ تا ۵ دقیقه** (بیشترش کامپایل `better-sqlite3` ≈ ۷۰ ثانیه و build ≈ ۲۰ ثانیه).

---

## ۰٫۵ ✅ اجرای تأییدشده در این سندباکس (2026-09-17)

کل رویه بخش‌های الف و ب در این سندباکس **واقعاً اجرا و تأیید شد**:

| گام | نتیجه واقعی |
|---|---|
| `npm install --ignore-scripts` | ✅ 597 پکیج در ۱۳ ثانیه |
| `node-gyp rebuild --nodedir=/usr/local` | ✅ `gyp info ok` — ۱۰۲ ثانیه |
| تست SQLite | ✅ `SQLITE OK { a: 42 }` |
| بوت سرور (`npx tsx server.ts`) | ✅ پورت 3000 + 24678 روی `0.0.0.0` |
| `GET /` | ✅ HTTP 200 |
| `GET /api/systems` | ✅ JSON چهارزبانه سیستم‌ها |
| `GET /api/state` بدون توکن | ✅ `AUTH_REQUIRED` (رفتار درست) |
| `npm ci` در browser-test | ✅ بدون رفتن به CDN پلی‌رایت |
| `node bootstrap.cjs --ready` | ✅ `/tmp/chromium` ≈ ۲۰۰MB |
| `ldd /tmp/chromium` | ✅ صفر `not found` |
| `node verify-env.mjs` | ✅ `OK: browser=149.0.7827.0 h1=hello bazino` |
| کپی فونت Vazirmatn | ✅ ۹ فایل TTF در `/tmp/fonts/Vazirmatn/` |
| اسکرین‌شات صفحه اصلی | ✅ رندر کامل، تصویر Hero = عکس واقعی سالن |
| رندر متن فارسی | ✅ حروف چسبیده، اعداد فارسی، راست‌چین |

**لاگ بوت واقعی:**

```
[Security] JWT_SECRET is not set in the environment. Using an INSECURE development-only fallback secret.
[Database Engine] Active provider initialized: SQLite
[Storage] data dir: /home/user/bazino-gamenet-portal (cwd — set BAZINO_DATA_DIR for persistence)
[SQLite] No users found. Creating a minimal fallback admin.
[SQLite] Seeded 28 Instagram campaign setting row(s) (existing keys left untouched).
[Parent API] Parent routes registered: /api/parent/*
[BAZINO Backend Server] is running beautifully with SQLite on http://0.0.0.0:3000
```

> **نکته‌ای که در سند اصلی نبود:** اسکریپت‌های Playwright باید **داخل پوشه `browser-test`** اجرا شوند (مثلاً `/home/user/browser-test/shot.mjs`)، نه در `/tmp`. اگر بیرون آن پوشه باشند، `ERR_MODULE_NOT_FOUND: Cannot find package 'playwright'` می‌گیرید چون `node_modules` آنجاست.

---

## ۱. مشخصات محیط سندباکس

| مورد | مقدار |
|---|---|
| Node | `v22.22.3` (در `/usr/local/bin`) |
| npm | `10.9.8` |
| Python | `3.11.2` (`/usr/bin/python3`) |
| کامپایلر | `g++ 12.2.0` (Debian) + `make` |
| هدرهای Node | `/usr/local/include/node/` — از قبل نصب، دقیقاً هم‌نسخه با runtime |
| سیستم‌عامل | Debian 12 |

### شبکه — چه چیزی باز است و چه چیزی بسته

| دامنه | وضعیت |
|---|---|
| `registry.npmjs.org` | ✅ باز |
| `github.com` | ✅ باز |
| `nodejs.org` | ❌ بسته |
| `cdn.playwright.dev` · `playwright.azureedge.net` | ❌ بسته |
| `fonts.googleapis.com` · `fonts.gstatic.com` | ❌ بسته |
| `api.qrserver.com` · `api.dicebear.com` · `cdn.jsdelivr.net` · `openstreetmap.org` | ❌ بسته |
| `deb.debian.org` · `storage.googleapis.com` | ❌ بسته |
| `api.groq.com` · `openrouter.ai` · `api.openai.com` | ❌ بسته (تست LLM فقط mock) |

> **قاعده طلایی:** هر چیزی که لازم دارید باید از `registry.npmjs.org` (یعنی از یک پکیج npm) بیاید.
> **هرگز `npx playwright install` نزنید** — CDN آن بسته است و فقط وقت تلف می‌کند.

### ⚠️ آنچه بین پیام‌ها پاک می‌شود

* `node_modules/` (هم ریپو، هم `browser-test`)
* `/tmp/*` (شامل `/tmp/chromium`، `/tmp/al2023`، `/tmp/fonts`)
* کل پوشه `/home/user/browser-test/` — به همین دلیل هارنس در ریپو زیر `tests/e2e-browser/` نگه داشته می‌شود
* پروسه‌های `start_process` (سرور)
* `bazino.sqlite3` (gitignore است؛ در بوت بعدی از نو ساخته می‌شود)
* **`.git` محلی به کامیت قبلی برمی‌گردد** — ولی push های قبلی روی GitHub سالم‌اند
  ➜ همیشه اول جلسه: `git fetch origin <branch> && git reset FETCH_HEAD`

---

## بخش الف — راه‌اندازی سرور زنده

### الف-۱. مسئله اصلی: ماژول native

`server.ts` به `better-sqlite3` نیاز دارد که ماژول native است. دو مسیر معمول کامپایل هر دو شکست می‌خورند:

```
npm rebuild better-sqlite3
  → prebuild-install warn: unable to verify the first certificate   (دانلود باینری از GitHub)
  → node-gyp http GET https://nodejs.org/.../node-v22.22.3-headers.tar.gz
    attempt 1 failed with ECONNRESET                                (nodejs.org بسته است)
```

### الف-۲. راه‌حل

`node-gyp` فقط برای **دانلود هدرها** به `nodejs.org` می‌رود — ولی هدرها **از قبل روی ایمیج هستند**:

```
/usr/local/include/node/node_version.h   → NODE 22.22.3 (دقیقاً هم‌نسخه runtime)
/usr/local/include/node/common.gypi      → فایلی که node-gyp لازم دارد
```

پس کافی است با `--nodedir` به همان‌جا اشاره کنیم:

```bash
cd node_modules/better-sqlite3
npx node-gyp rebuild --release --nodedir=/usr/local
```

خروجی موفق (≈۷۰ ثانیه، فقط چند warning بی‌ضرر `-Wcast-function-type`):

```
  SOLINK_MODULE(target) Release/obj.target/better_sqlite3.node
  COPY Release/better_sqlite3.node
gyp info ok
```

راستی‌آزمایی:

```bash
node -e "const D=require('better-sqlite3'); const d=new D(':memory:');
         d.exec('create table t(a)'); d.prepare('insert into t values (?)').run(42);
         console.log('SQLITE OK', d.prepare('select * from t').get());"
# → SQLITE OK { a: 42 }
```

> **نکته مهم:** `npm install` را حتماً با `--ignore-scripts` بزنید. بدون آن npm خودش سعی می‌کند `better-sqlite3` را بیلد کند، شکست می‌خورد و کل نصب را کند و پرخطا می‌کند.
> `sharp` prebuilt دارد و بدون مشکل کار می‌کند.

### الف-۳. بوت سرور

```bash
npx tsx server.ts
```

خروجی موفق:

```
[Security] JWT_SECRET is not set in the environment. Using an INSECURE development-only fallback secret.
[Database Engine] Active provider initialized: SQLite
[SQLite] No users found. Creating a minimal fallback admin (no sample data will be loaded automatically).
[BAZINO Backend Server] is running beautifully with SQLite on http://0.0.0.0:3000
```

* پورت **3000** = سایت + API · پورت **24678** = Vite HMR
* حتماً با ابزار `start_process` اجرا شود، نه `bash` (وگرنه با تایم‌اوت کشته می‌شود)
* روی `0.0.0.0` گوش می‌دهد ✅ و `vite.config.ts` از قبل `allowedHosts: ['.e2b.app', '.localhost']` دارد و `server.ts` در حالت dev `allowedHosts: true` می‌دهد ✅ — پس **پیش‌نمایش زنده سندباکس بدون هیچ تغییری کار می‌کند**

### الف-۴. حساب آماده بعد از بوت

| کاربر | رمز | نقش | توضیح |
|---|---|---|---|
| `admin` | `admin` | admin | فقط اگر جدول `users` خالی باشد ساخته می‌شود |

پیش‌فرض `data_source = sample` — کاتالوگ‌ها (سیستم‌ها، منوی کافه، محصولات، تورنمنت‌ها، مقالات) از `server/sampleData.ts` پر می‌شوند و سایت هیچ‌وقت خالی نیست.

### الف-۵. راستی‌آزمایی سریع

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:3000/     # → 200
curl -s http://localhost:3000/api/systems | head -c 200             # → JSON سیستم‌ها
```

---

## بخش ب — Chromium واقعی (بدون CDN)

باینری Chromium از پکیج npm **`@sparticuz/chromium`** می‌آید (باینری + کتابخانه‌های اشتراکی Amazon Linux 2023 + فونت‌ها، همه داخل تاربال npm).

### ب-۱. نسخه‌های پین‌شده (تغییرشان ندهید)

```json
"playwright":                    "1.62.1",
"@playwright/test":              "1.62.1",
"@sparticuz/chromium":           "149.0.0",     →  Chromium 149.0.7827.0
"vazirmatn":                     "^33.0.3",
"@fontsource/noto-naskh-arabic": "^5.3.0"
```

### ب-۲. راه‌اندازی

```bash
mkdir -p /home/user/browser-test
cp -r /home/user/bazino-gamenet-portal/tests/e2e-browser/* /home/user/browser-test/
cd /home/user/browser-test
PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm ci     # بدون این متغیر، Playwright به CDN بسته می‌رود
node bootstrap.cjs --ready                     # باینری را در /tmp باز می‌کند؛ idempotent است
```

**`bootstrap.cjs` چه می‌کند:**
* `@sparticuz/chromium@149` کاملاً ESM شده (پوشه `build/cjs/` ندارد) → با `import(pathToFileURL(...))` داینامیک لود می‌شود
* فایل `al2023.tar.br` (شامل `libnspr4.so` و بقیه) فقط روی Amazon Linux 2023 خودکار باز می‌شود؛ اینجا **دستی** با `mod.inflate(...)` در `/tmp/al2023` باز می‌شود

### ب-۳. متغیرهای محیطی لازم

```bash
export CHROMIUM_EXECUTABLE_PATH=/tmp/chromium
export LD_LIBRARY_PATH=/tmp/al2023/lib
export FONTCONFIG_PATH=/tmp/fonts
export HOME=/tmp
```

### ب-۴. 🔴 فونت فارسی — بدون این کار همه اسکرین‌شات‌ها بی‌ارزش‌اند

`@sparticuz/chromium` فقط `Open Sans` را همراه دارد. بدون فونت عربی/فارسی، **تمام متن فارسی کاملاً خالی رندر می‌شود** در حالی که متن لاتین سالم است.

این خطرناک است چون اسکرین‌شات در نگاه اول «شبیه یک باگ چیدمان در سایت» به‌نظر می‌رسد، در حالی که مشکل از محیط تست است.

```bash
cd /home/user/browser-test
npm i vazirmatn
mkdir -p /tmp/fonts/Vazirmatn
cp node_modules/vazirmatn/fonts/ttf/*.ttf /tmp/fonts/Vazirmatn/
rm -rf /tmp/fonts-cache          # fontconfig باید کش را از نو بسازد
```

(`/tmp/fonts/fonts.conf` که `bootstrap.cjs` می‌سازد از قبل `<dir>/tmp/fonts</dir>` دارد.)

### ب-۵. 🔴 بلاک‌کردن منابع خارجی در هارنس — اجباری

سایت در زمان اجرا `fonts.googleapis.com` را صدا می‌زند. چون آن دامنه بسته است، `page.screenshot()` در حالت `waiting for fonts to load` **۳۰ ثانیه تایم‌اوت می‌خورد** و اسکرین‌شات اصلاً ذخیره نمی‌شود (و چون معمولاً `.catch()` دارد، بی‌سروصدا رد می‌شود!).

در `lib.mjs` این کار انجام شده:

```js
const EXTERNAL = /fonts\.googleapis\.com|fonts\.gstatic\.com|cdn\.jsdelivr\.net|api\.qrserver\.com|api\.dicebear\.com|openstreetmap\.org|unpkg\.com|cdnjs\./;
await context.route(EXTERNAL, (route) => route.abort());
```

### ب-۶. راستی‌آزمایی

```bash
node verify-env.mjs
# → OK: browser=149.0.7827.0 h1=hello bazino
# → screenshot=/home/user/browser-test/shots/verify.png
```

اگر خطا داد: `ldd /tmp/chromium` را بزنید؛ نباید هیچ `not found` داشته باشد.

### ب-۷. هارنس تست (`tests/e2e-browser/`)

| فایل | کار |
|---|---|
| `lib.mjs` | لانچر مشترک: باز کردن Chromium، بلاک CDNها، جمع‌آوری خطاهای console و `pageerror` |
| `bootstrap.cjs` · `env.sh` · `verify-env.mjs` | آماده‌سازی و صحت‌سنجی محیط |
| `e2e-journey.mjs` · `e2e-admin.mjs` · `e2e-admin-full.mjs` · `e2e-management.mjs` | سناریوهای کاربر، ادمین و اپ مدیریت |
| `v4-studio.mjs` · `v4-composer.mjs` · `v4-gate.mjs` · `v4-management.mjs` · `v4-profile.mjs` | سناریوهای استودیوی انتشار V4 |
| `explore.mjs` · `explore-tabs.mjs` · `games-page.mjs` | پیمایش اکتشافی صفحات |

---

## بخش ج — پل CDP: اتصال ایجنت به مرورگر کارفرما

**هدف:** ایجنت (داخل سندباکس Arena) باید بتواند Chrome لوکال **کارفرما** را از راه دور هدایت کند — فهرست تب‌ها، ناوبری، اسکرین‌شات، اجرای JavaScript — برای تست زنده سایت پروداکشن (`bazino.pro`) بدون دسترسی مستقیم ایجنت به اینترنت آزاد.

### ج-۰. 📜 قواعد ماندگار (درخواست صریح و مکرر کارفرما)

1. **اسکریپت پل را همیشه کامل و با مقادیر جلسه پرشده بده** — هرگز «فقط دو خط عوض شود» نگو. نسخه آماده: `cdp-tools/bridge-full.ps1`
2. **کروم ایجنت مستقل** (پروفایل `chrome-agent` با `--user-data-dir` جدا) — **پروسه کروم اصلی کارفرما هرگز بسته یا ری‌استارت نشود** (چت Arena داخل آن است)
3. **محیط اپراتور: ویندوز + PowerShell.** هر دستوری که برای اجرای *کاربر* داده می‌شود باید PowerShell-native باشد: به‌جای `openssl` از `[Security.Cryptography.RandomNumberGenerator]`، به‌جای `curl` از `Invoke-RestMethod`، به‌جای `export` از `$env:`. دستورهای bash فقط برای سندباکس لینوکسی ایجنت‌اند
4. پیام‌های کنسول اسکریپت انگلیسی باشند
5. بعد از ری‌بیلد سندباکس فقط `$Base` عوض می‌شود؛ `$Code` پایدار است

### ج-۱. معماری (v7 — رله HTTP معکوس)

```
┌────────────── کامپیوتر کارفرما (ویندوز) ──────────────────────────────┐
│  Chrome ایجنت (پروفایل جدا: chrome-agent)                              │
│    --remote-debugging-port=9222 --remote-allow-origins=*               │
│         ▲ ws://127.0.0.1:9222  (WebSocket لوکال — سریع و بدون محدودیت) │
│         │                                                              │
│  پل PowerShell (فقط .NET داخلی — بدون نصب هیچ چیز)                     │
│         │ HTTP polling (هر ~۱۲۰ms)                                     │
└─────────┼──────────────────────────────────────────────────────────────┘
          ▼  https://sbx-<id>.arena.site   ← تنها URL عمومی هر سندباکس (پورت 8787)
┌────────────── سندباکس ایجنت ───────────────────────────────────────────┐
│  cdp-tools/relay.js :8787  (رله v7 — HTTP واحد، zero-dependency)        │
│    /down → فرمان‌های ایجنت به کروم   /up → پاسخ‌های کروم به ایجنت        │
│    /agent/cmd + /agent/poll (long-poll) → API ایجنت                     │
│    /status · /report · / (صفحه دیاگ bridge.html)                        │
│  ابزارهای ایجنت: agent.js / analyze-current.js / scroll-shots.js / …   │
└─────────────────────────────────────────────────────────────────────────┘
```

> **اصل طلایی:** هر ارتباطی که از پروکسی پلتفرم رد می‌شود باید **HTTP ساده** باشد؛ WebSocket فقط در حلقه‌های لوکال (کامپیوتر کارفرما↔کروم، و داخل سندباکس↔رله).

### ج-۲. چرا این معماری؟ (هر مانع با شاهد بسته شد)

| # | مانع کشف‌شده | نتیجه |
|---|---|---|
| ۱ | خروجی HTTPS سندباکس whitelist است؛ فقط `github.com` و `registry.npmjs.org` بازند (cloudflare/ngrok/bazino.pro همه exit=35) | پلن «تانل cloudflared» مرده |
| ۲ | دامنه `{port}-{id}.e2b.app` هدر `e2b-traffic-access-token` می‌خواهد که فقط مرورگر خود کارفرما دارد | PowerShell نمی‌تواند مستقیم به e2b.app وصل شود (403/502) |
| ۳ | دامنه `sbx-<id>.arena.site` از دستگاه کارفرما بدون توکن کار می‌کند و به **اولین پورت ثبت‌شده** سندباکس می‌رسد | این دامنه = تنها مسیر عمومی مجاز |
| ۴ | پروکسی فقط **یک** URL عمومی می‌سازد؛ `8788-….arena.site` وجود ندارد | همه سرویس‌ها باید روی همان یک پورت باشند |
| ۵ | از مسیر پروکسی، handshake وب‌سوکت رد می‌شود ولی **فریم‌های داده WS یا ~۳۰ ثانیه معطل می‌شوند یا گم می‌شوند** | WS از مسیر پروکسی برای CDP مرده است |
| ۶ | HTTP از همان مسیر **همان لحظه** می‌رسد (حتی POST بدنه ۲۵KB) | HTTP polling = ترنسپورت پل |
| ۷ | صفحه https نمی‌تواند به `ws://127.0.0.1` وصل شود (mixed-content + Private Network Access) | پل داخل‌مرورگری کنار گذاشته شد؛ PowerShell = مبدأ غیرمحدود |
| ۸ | چک Host کروم (ضد DNS-rebinding) فقط روی HTTP اعمال می‌شود، نه WebSocket | PowerShell می‌تواند مستقیم ws لوکال کروم را باز کند |

### ج-۳. اجزا (`cdp-tools/` — کامیت‌شده در ریپوی پورتال)

| فایل | نقش |
|---|---|
| `relay.js` | رله v7 — صف فریم، لاگ ماندگار `relay-events.log`، صفحه دیاگ |
| `bridge.html` | صفحه دیاگ روی `/` — Base و کد جلسه را نشان می‌دهد + heartbeat هر ۳۰s |
| `lib.js` | کلاینت ایجنت: کلاس `Cdp` با send/poll/attach/evalRaw/waitEvent |
| `agent.js` | CLI: `status` / `tabs` / `nav <url>` / `eval "<js>"` / `shot <file>` |
| `analyze-current.js` | تحلیل تب جاری: DOM + صحنه three.js + شنونده‌ها + اسکرین‌شات |
| `scroll-shots.js` | اسکرول‌شات مرحله‌ای (فعال‌سازی تب + حرکت ماوس + عکس هر مرحله) |
| `capture-errors.js` | هوک خطای console/window قبل از لود — ⚠️ فقط **یک تزریق در هر جلسه** |
| `upload-zip.js` | آپلود تکه‌ای ZIP قالب + نصب روی bazino.pro |
| `mock_bridge.js` | شبیه‌ساز پل برای drill بدون کارفرما |
| `bridge-full.ps1` · `bridge-ps-v3.ps1` | اسکریپت پل سمت کارفرما |
| `.session-code` | کد جفت‌سازی جلسه — **gitignored، هرگز کامیت نشود** |

### ج-۴. گام ۰ — سمت ایجنت (سندباکس)

```bash
# اگر ری‌بیلد اتفاق افتاده: بازیابی ابزارها
cd bazino-gamenet-portal
git fetch origin <branch> && git reset --hard FETCH_HEAD

# کد جفت‌سازی تازه برای هر جلسه (هرگز در چت عمومی/ریپو ذخیره نکن)
cd cdp-tools && printf '<16-hex-code>' > .session-code

# رله را به‌صورت پروسه بلندمدت اجرا کن (با start_process، نه bash)
node relay.js
```

> ⚠️ **پورت ۸۷۸۷ باید اولین پورت در حال گوش‌دادن سندباکس باشد** تا دامنه `sbx-….arena.site` به آن برسد. اگر پورت دیگری زودتر باز شود، دامنه عمومی به آن می‌رسد و پل کار نمی‌کند.

**کشف Base عمومی:**

```bash
# از کارفرما بخواه پنل Live Preview را باز کند → bridge.html بارگذاری می‌شود
tail -5 cdp-tools/relay-events.log
# → REPORT {"ev":"page_loaded","d":"https://sbx-xxxx.arena.site/"}
```

> ⚠️ **نام میزبان عمومی هرگز از `E2B_SANDBOX_ID` حدس زده نشود** — این دو با هم فرق دارند (نمونه واقعی: sandbox id = `i6kbsthngkkzntvgolcy2` ولی میزبان = `sbx-pfz36v42usj4yd6v.arena.site`). حدس اشتباه علامتش: DNS «could not be resolved» یا 403/502 بی‌دلیل.
> ⚠️ سندباکس خودش نمی‌تواند به `sbx-…` وصل شود (`curl` → exit 35) — **این طبیعی است**. فقط مرورگر کارفرما می‌تواند.

**drill داخلی (توصیه‌شده):** پروسه جدا `node mock_bridge.js` → سپس `node agent.js tabs`. اگر پاسخ mock آمد، زنجیره رله↔پروتکل سالم است.

### ج-۵. گام ۱ — سمت کارفرما: باز کردن Chrome ایجنت

پروفایل جدا؛ کروم اصلی کارفرما دست‌نخورده می‌ماند.

**روش ۱ — Run (Win+R) یا CMD:**
```
"C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="%USERPROFILE%\chrome-agent" --no-first-run --remote-allow-origins=* https://bazino.pro
```

**روش ۲ — PowerShell:**
```powershell
& "C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="$env:USERPROFILE\chrome-agent" --no-first-run --remote-allow-origins=* https://bazino.pro
```

- پنجره‌های قبلی این پروفایل کامل بسته شوند
- این کروم فقط برای ایجنت است؛ اکانت فرعی یا بدون لاگین کافی است

### ج-۶. گام ۲ — سمت کارفرما: اجرای پل (PowerShell، بدون هیچ نصب)

پنجره PowerShell معمولی باز کنید، **کل اسکریپت** را Paste و Enter. فقط `$Code` و `$Base` را ایجنت با مقادیر جلسه پر می‌کند.

```powershell
# ═══ Bazino CDP Bridge (HTTP transport) — نسخه کامل ═══
$Code = '<PAIRING-CODE>'                  # از ایجنت
$Base = 'https://sbx-<id>.arena.site'     # از ایجنت (از Live Preview)
$ChromeDebug = 'http://127.0.0.1:9222'
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Host "=== Bazino CDP Bridge ===" -ForegroundColor Cyan
Write-Host "Code: $Code | Base: $Base" -ForegroundColor Yellow

# ── پله ۱: تست رله ──
Write-Host "Testing relay..." -ForegroundColor Cyan
try {
  $st = Invoke-RestMethod "$Base/status?code=$Code" -TimeoutSec 15
  Write-Host "Relay OK: gen=$($st.gen) bridge_alive=$($st.http_bridge_alive) chrome_q=$($st.chrome_queue) agent_q=$($st.agent_queue)" -ForegroundColor Green
} catch {
  Write-Host "Relay unreachable: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}

# ── پله ۲: تست کروم ──
Write-Host "Testing Chrome..." -ForegroundColor Cyan
try {
  $v = Invoke-RestMethod "$ChromeDebug/json/version" -TimeoutSec 5
  Write-Host "Chrome OK: $($v.Browser)" -ForegroundColor Green
  $chromeWs = $v.webSocketDebuggerUrl
} catch {
  Write-Host "Chrome debug port 9222 not responding. Open agent Chrome first." -ForegroundColor Red
  exit 1
}

# ── پله ۳: WebSocket لوکال به کروم ──
$ct = [Threading.CancellationToken]::None
$inner = [System.Net.WebSockets.ClientWebSocket]::new()
$inner.Options.KeepAliveInterval = [TimeSpan]::FromSeconds(20)
[void]$inner.ConnectAsync($chromeWs, $ct).GetAwaiter().GetResult()
Write-Host 'BRIDGE UP — Chrome + Relay Connected' -ForegroundColor Green
Write-Host 'Keep this window open. Do NOT click inside it (QuickEdit freezes the loop; press Enter if frozen).' -ForegroundColor Yellow

# ── پله ۴: حلقه اصلی ──
$MT = [System.Net.WebSockets.WebSocketMessageType]
$UTF8 = [Text.Encoding]::UTF8
$bufIn = [byte[]]::new(16*1024*1024)
$last = 0; $gen = $null; $n = 0; $errStreak = 0
$tIn = $inner.ReceiveAsync($bufIn, $ct)
try {
  while ($true) {
    $out = @()
    while ($tIn.Wait(0)) {                                   # کروم → رله
      $res = $tIn.Result
      if ($res.MessageType -eq $MT::Close) { throw 'chrome WS closed' }
      $ms = New-Object System.IO.MemoryStream
      $ms.Write($bufIn, 0, $res.Count)
      while (-not $res.EndOfMessage) {
        $res = $inner.ReceiveAsync($bufIn, $ct).GetAwaiter().GetResult()
        $ms.Write($bufIn, 0, $res.Count)
      }
      $txt = $UTF8.GetString($ms.ToArray())
      $n++
      $out += $txt
      $tIn = $inner.ReceiveAsync($bufIn, $ct)
    }
    if ($out.Count -gt 0) {
      try {
        Invoke-RestMethod -Method Post -Uri "$Base/up?code=$Code" -Body ($out -join "`n") `
          -ContentType 'application/json; charset=utf-8' -TimeoutSec 30 | Out-Null
      } catch {}
    }
    try {                                                    # رله → کروم
      $r = Invoke-RestMethod "$Base/down?code=$Code&after=$last" -TimeoutSec 20
      $errStreak = 0
    } catch {
      $errStreak++
      if ($errStreak -ge 10) { throw }
      Start-Sleep -Milliseconds 500; continue
    }
    if ($gen -ne $r.gen) { $gen = $r.gen; $last = 0; Write-Host "Gen $gen" -ForegroundColor Magenta }
    foreach ($f in $r.frames) {
      $last = $f.seq
      $bytes = $UTF8.GetBytes($f.d)
      try { $m = ($f.d | ConvertFrom-Json).method; if ($m) { Write-Host "[$n] -> $m seq=$($f.seq)" -ForegroundColor Green } } catch {}
      [void]$inner.SendAsync($bytes, $MT::Text, $true, $ct)
      $n++
    }
    if (-not ($tIn.Wait(120))) { Start-Sleep -Milliseconds 50 }
  }
} finally {
  foreach ($w in @($inner)) { try { $w.Dispose() } catch {} }
  Write-Host 'Bridge disconnected.' -ForegroundColor Yellow
}
```

### ج-۷. ⚠️ قواعد حیاتی سمت کارفرما

- **داخل پنجره PowerShell کلیک نکنید** — QuickEdit ویندوز حلقه را فریز می‌کند. اگر فریز شد فقط Enter بزنید
- پنجره را تا پایان کار باز نگه دارید (بستن = kill-switch فوری)
- VPN کارفرما حین پل روشن باشد (دامنه `sbx` از برخی شبکه‌ها بسته است)
- اگر `Unable to connect` آمد → اول VPN، بعد از ایجنت بخواه Base جدید را چک کند

### ج-۸. گام ۳ — کارهای ایجنت پس از اتصال

```bash
export CDP_RELAY_URL=http://127.0.0.1:8787 CDP_CODE=<code>
node agent.js status                             # سلامت پل
node agent.js tabs                               # لیست تب‌ها
node agent.js nav 'https://bazino.pro/club' ''   # ناوبری + انتظار لود
node agent.js eval "2+2" ''                      # اجرای JS
node agent.js shot shots/x.jpg 'bazino'          # اسکرین‌شات

node analyze-current.js shots/x.jpg 'match'      # تحلیل کامل تب جاری
node scroll-shots.js 'match' shots/pre 5         # اسکرول‌شات ۵ مرحله‌ای
node capture-errors.js 'https://bazino.pro/club' # خطاهای واقعی صفحه (فقط یک بار!)
node upload-zip.js /tmp/theme.zip                # نصب ZIP قالب روی پروداکشن
```

**قواعد کار ایجنت:**
- تب هدف را قبل از هر کار با `Target.activateTarget` جلو بیاور — **تب‌های پس‌زمینه کروم فریز می‌شوند** و فرمان‌ها timeout می‌خورند
- `location.reload()` یا ناوبری، سشن CDP را می‌کشد — بعدش دوباره `attach` کن
- هر فرمان CDP = یک POST به رله + poll پاسخ (RTT از مسیر کارفرما: ~۱–۳ ثانیه)
- اسکرین‌شات base64 بزرگ است؛ تا ~۱MB فریم بدون مشکل رد می‌شود

### ج-۹. گام ۴ — بستن جلسه

کارفرما پنجره PowerShell را می‌بندد (kill-switch فوری) → ایجنت رله را stop می‌کند.

### ج-۱۰. پروتکل HTTP رله (مرجع)

| Endpoint | متد | نقش |
|---|---|---|
| `/down?code=…&after=N` | GET | `{"gen":G,"frames":[{"seq":n,"d":"…"}]}` — فریم‌های seq>N برای کروم |
| `/up?code=…` | POST | بدنه = NDJSON پیام‌های CDP از کروم → صف ایجنت |
| `/agent/cmd?code=…` | POST | ایجنت فرمان CDP را به صف کروم می‌فرستد |
| `/agent/poll?code=…&after=N&wait=ms` | GET | long-poll پاسخ‌ها/رویدادها برای ایجنت |
| `/status?code=…` | GET | `http_bridge_alive` (down<15s)، `agent_alive`، شمارنده‌ها، ۱۲ گزارش آخر |
| `/report?ev=&d=` | GET | گزارش صفحه دیاگ (بدون code) — `page_loaded` / `page_heartbeat` هر ۳۰s |
| `/` | GET | صفحه دیاگ bridge.html (کد جلسه embed شده) |

**نکات:** `gen` با هر ری‌استارت رله عوض می‌شود و پل `after` را صفر می‌کند؛ فریم‌های خوش‌آمد `id:-1` هستند و کروم به آن‌ها خطای `-32601` می‌دهد که **طبیعی و سالم** است. صف‌ها: حداکثر ۴۰۰ فریم / ۴۸MB.

### ج-۱۱. عیب‌یابی (همه موارد واقعاً پیش آمده)

| علامت | تشخیص | درمان |
|---|---|---|
| `/status` پاسخ نمی‌دهد | رله مرده | `node relay.js` دوباره (پروسه بلندمدت) |
| پنل باز است ولی `http_bridge_alive:false` | PowerShell اجرا نیست یا فریز | Paste دوباره اسکریپت؛ QuickEdit → Enter |
| PowerShell: `Unable to connect` روی sbx | Base عوض شده (ری‌بیلد) یا VPN | Base جدید از `relay-events.log` + VPN |
| PowerShell: پورت 9222 پاسخ نداد | کروم ایجنت بسته است | گام ۱ سمت کارفرما |
| فرمان ایجنت timeout ولی down_count بالا | **تب پس‌زمینه فریز** | `Target.activateTarget` قبل از کار |
| `Session with given id not found` (-32001) | ناوبری/ریلود سشن را کشته | دوباره `attach()` بگیر |
| اسکرین‌شات‌های متوالی یکسان | انیمیشن one-shot تمام شده | `capture-entrance.js` یا `capture-anim2.js` |
| 502 روی sbx | فریز موقت سندباکس | صبر؛ رله معمولاً زنده است (لاگ فایل را ببینید) |
| curl از سندباکس به sbx → exit 35 | **طبیعی است** — خروجی سندباکس whitelist | فقط مرورگر کارفرما به sbx وصل می‌شود |
| `SyntaxError __push` بعد از تست خطا | تزریق دوباره هوک capture-errors | فقط یک بار در هر جلسه تزریق کن |

### ج-۱۲. بازیابی بعد از ری‌بیلد سندباکس (چک‌لیست ۵ دقیقه‌ای)

ری‌بیلد: HEAD → نقطه انشعاب، فایل‌های untracked غیرریپو پاک، پکیج‌های pip و node_modules پاک. ابزارهای کامیت‌شده در `cdp-tools/` **زنده می‌مانند** (بعد از reset).

```bash
cd bazino-gamenet-portal
git fetch origin <branch> && git reset --hard FETCH_HEAD
cd cdp-tools && printf '<code>' > .session-code
node relay.js                       # پروسه بلندمدت — پورت 8787 اولین پورت
cd .. && npm install --ignore-scripts
```

سپس کارفرما پنل Live Preview را باز می‌کند → `page_loaded` در `relay-events.log` → فقط `$Base` اسکریپت PowerShell عوض می‌شود (`$Code` همان می‌ماند).

### ج-۱۳. امنیت

- **کد جفت‌سازی** تنها کلید احراز `/up` و `/down` است؛ برای هر جلسه تازه ساخته شود و در `.session-code` (gitignored) بماند. **هرگز در ریپو یا چت عمومی ثبت نشود**
- **kill-switch:** بستن پنجره PowerShell کارفرما = قطع فوری دسترسی
- فقط پروفایل `chrome-agent` در معرض است؛ کروم اصلی کارفرما دست‌نخورده
- اسکرین‌شات‌ها و لاگ‌های CDP هرگز commit نشوند (محتوای پنل ادمین)
- توکن ادمین `bazino.pro` فقط در localStorage مرورگر کارفرما می‌ماند
- توصیه: VPN کارفرما فقط حین پل روشن باشد

---

## ۴. درس‌آموخته‌های عملی

1. **هرگز پل زنده را بدون drill داخلی (mock_bridge) تحویل کارفرما نکن** — تمرین ۴ بار جان این پروژه را داد
2. **لاگ ماندگار بنویس** (`relay-events.log`) — دو بار پروسه رله بین نوبت‌ها مرد و بدون لاگ فایلی شواهد گم می‌شد
3. سندباکس بین نوبت‌ها گاهی فایل‌سیستم را به اسنپ‌شات برمی‌گرداند — قبل از هر نوبت صحت فایل‌ها را چک کن
4. فریم‌های خوش‌آمد خودکار (`_relay_welcome_*`) تست «حلقه داده در هر دو جهت» را بدون ایجنت ممکن می‌کنند — اولین چیزی که باید در پنجره PowerShell دیده شود
5. **اول شاهد مستقیم بخواه، بعد نظر بده** — وقتی کاربر خطایی گزارش می‌دهد، اسکرین‌شات/لاگ کامل بخواه. حدس‌زدن از روی استدلال ساعت‌ها وقت هدر می‌دهد
6. بعد از ری‌بیلد سندباکس URL پیش‌نمایش عوض می‌شود — URL قدیمی `502` می‌دهد و URL جدید را نمی‌توان حدس زد

### نتیجه جلسه زنده اثبات (2026-09-10)

- پل HTTP کارفرما ↔ رله ↔ ایجنت: **برقرار** ✓
- لیست targetهای واقعی کروم کارفرما (۵ عدد): ✓
- اسکرین‌شات واقعی صفحه (`proof.jpg`، JPEG سالم): ✓
- خواندن عنوان/URL صفحه از مرورگر کارفرما: ✓
- RTT فریم‌ها ۱–۳ ثانیه؛ انتقال فریم ۲۵KB بدون مشکل: ✓
