# 📘 پلی‌بوک اتصال به ژینوس 1.1.0 از محیط آرنا — نسخهٔ کامل (۲۰۲۶-۱۰-۲۲)

> **هدف این سند:** هر نشست آرنا (مثل ایجنت پورتال) با خواندن همین فایل، **بدون هیچ حدس و جستجویی**، بتواند ظرف چند دقیقه از طریق میل‌باکس به ژینوس وصل شود، فرمان بدهد، پاسخ بگیرد و حتی با «مرورگر ژینوس» به وب (ریل‌وی، گیت‌هاب…) دسترسی داشته باشد. همهٔ اطلاعات لازم در همین‌جاست.
> **منبع حقیقت سورس ژینوس:** پوشهٔ `Jinus/source/` (میرور 1.1.0 — فقط مرجع؛ برنامهٔ زنده روی پی‌سی مالک می‌چرخد). راهنمای کلانِ ژینوس هم `Jinus/jinus-architecture-and-operation-guide.md` کنار همین فایل است.

---

## ۱. اجزا و جایگاه‌ها (قطعی)

| مؤلفه | جای واقعی |
|---|---|
| ریشهٔ سورس زنده (تأییدشده با os.run در ۲۰۲۶-۱۰-۲۲) | پی‌سی مالک، ویندوز — **`C:\projects\jinus-v1.1.0`** — پوشه‌ها: `server\`، `src\`، `scripts\`، `Assets\`، `public\`، `release\`، `release-next\`، `mailbox110\`، `model-stash\`، `node_modules\` |
| برنامهٔ زنده ژینوس | پی‌سی مالک، ویندوز — `C:\projects\jinus-v1.1.0\release\win-unpacked\Jinus.exe` (باینری با `dir` تأیید شد، ۲۰۲۶-۱۰-۲۲) ، پایتون 3.14 روی پورت 8765 |
| پوشهٔ داده سرور بستهٔ نصب‌شده (از پاسخ `ping`) | `C:\projects\jinus-v1.1.0\release\win-unpacked\resources\app\jinus-v1-desktop\server\data` |
| منابعی که ژینوس می‌خواند/می‌نویسد (می‌باکس گیت‌هاب) | همین ریپو — **برنچ نشست آرنا** (`arena/<id>-bazino-gamenet-portal`) — مسیر **`marketing-app-mailbox-110/`** (پوشهٔ کانونی۱.۱.۰) — سورس‌ها در تنظیمات ژینوس ثبت می‌شوند؛ برای برنچ جدید باید یک‌بار توسط مالک ثبت شود (نشست 136c4b5f ✅ و نشست 261c806a در ۲۰۲۶-۱۰-۲۲ ✅) |
| کانال قدیمی | `marketing-app-mailbox/` (بدون `-110`) — **وابسته به دوره 0.9.9؛ استفاده نکن** |
| راهنمای سیستم از سمت ژینوس | `Jinus/source/server/mailbox_manager.py` (پروتکل)، `Jinus/source/server/command_executor.py` (کاتالوگ فرمان‌ها)، `Jinus/source/server/agent_signature.py` (تأیید امضا) |

---

## ۲. هویت ایجنت آرنا (Registered Agent) — وضع فعلی

| | |
|---|---|
| اثرانگشت | **`4a8a:f3a5:7c9a:dbf6`** |
| برچسب | Arena portal agent 136c4b5f (bazino-gamenet-portal) — reissue #7 (sandbox reset wiped local keys) — 2026-10-22 |
| وضعیت | **تأییدشده توسط مالک در ژینوس — ۲۰۲۶-۱۰-۲۲** ✅ (معمولاً reissue جدید نیازمند تأیید مالک است) |
| کلید خصوصی (فقط اینجا!) | `~/.arena-mailbox/home/agent-identity-private.json` (chmod 600) — **هرگز وارد گیت نشود** |
| اسکریپت امضا | `/home/user/.arena-mailbox/send-task.mjs` — امضا + ساخت پاکت |
| Helper ارسال فرمان | `/home/user/.arena-mailbox/mb_task.sh <id> <cmd> <argsJSON> [note]` — امضا + کامیت + پوش + انتظار پاسخ |

### فرمول اثرانگشت (رسمی — از `mailbox.cjs` ابزار رسمی، نسخه 488a3390…)
```
fingerprint = sha256( exchangePubSPKI_base64 + "|" + signingPubSPKI_base64 ).hex[0:16]  →  با «:» به ۴ گروه چهارتایی
```
ساختار فایل هویت منتشرشده (`marketing-app-mailbox-110/agent-identity.json`):
```json
{"v":1,"exchange":"<SPKI base64>","signing":"<SPKI base64>","fingerprint":"<...>","label":"...","updatedAt":"..."}
```

### ۲-الف. ایجنت دوم ثبت‌شده — نشست `arena/261c806a` (۲۰۲۶-۱۰-۲۲) ⚠️ با ایجنت §۲ اشتباه نشود

هر برنچ نشست، ایجنت مستقل خودش را دارد؛ جدول بالا متعلق به نشست `136c4b5f` است و این جدول متعلق به نشست جاری:

| | |
|---|---|
| اثرانگشت | **`6468:1b74:0aaa:41d1`** |
| برچسب | Arena portal agent 261c806a (bazino-gamenet-portal) — branch arena/261c806a — initial key 2026-10-22 |
| برنچ | `arena/261c806a-bazino-gamenet-portal` |
| وضعیت | **تأییدشده توسط مالک در ژینوس — ۲۰۲۶-۱۰-۲۲** ✅ (ping واقعی پاسخ `pong` گرفت، ژینوس روی همین برنچ state/reply کامیت می‌زند) |
| فایل بازیابی درایو | `arena-agent-261c806a-fingerprint-6468-1b74-0aaa-41d1.recovery.json` (id: `17QVUD0T_G2vS0AFxjPBvIphpEUGv72AG`) در همان پوشهٔ `Bazino Arena Agents` |
| کلید خصوصی لوکال | `~/.arena-mailbox/home/agent-identity-private.json` (chmod 600؛ کلید ایجنت قبلی در همان پوشه با پسوند `.backup-*.json`) |
| ابزار | `~/.arena-mailbox/send-task.mjs` (زیرفرمان‌ها: `keygen`، `identity-public <label>`، `fingerprint`، `verifysanity`، `sign`، `envelope`) و `mb_task.sh` |

قواعد تفکیک دو ایجنت:
- `agent-identity.json` هر برنچ فقط هویت همان برنچ را دارد؛ روی برنچ `261c806a` فینگرپرینت باید `6468:1b74:0aaa:41d1` باشد و روی `136c4b5f` همان `4a8a:f3a5:7c9a:dbf6`.
- اگر سندباکس ریست شد: فایل بازیابی **هم‌نام برنچ خودت** را از درایو برگردان (§۳)، نه فایل برنچ دیگر. هر فایل فقط با فینگرپرینت داخل نامش سازگار است.
- `keygen` فقط وقتی مجاز است که هویت قبلی قابل بازیابی نباشد؛ فینگرپرینت جدید نیازمند Approve دوبارهٔ مالک و آپلود فایل بازیابی جدید با نام هم‌نام برنچ است.

---

## ۳. بازیابی هویت پس از ریست سندباکس — گوگل درایو 🗂

> بعد از هر reset سندباکس، کلید خصوصی لوکال پاک می‌شد (تا reissue #۶ خواب ما بود!). از #۷، نسخهٔ نجات روی درایو مالک است:

1. **مکان:** پوشهٔ `Bazino Arena Agents` در گوگل درایو (id پوشه: `15ceMwIJlEgONDUj46lxAELn-Tm7fUTnk`)
2. **فایل‌ها (یکی را هم‌نام برنچ نشستت انتخاب کن — جابه‌جایی ممنوع):**
   - نشست `136c4b5f` → `arena-agent-136c4b5f-fingerprint-4a8a-f3a5-7c9a-dbf6.recovery.json` (id: `1ceADM70ONeW7_t4rlTIKrDX1JDqViAS0`)
   - نشست `261c806a` → `arena-agent-261c806a-fingerprint-6468-1b74-0aaa-41d1.recovery.json` (id: `17QVUD0T_G2vS0AFxjPBvIphpEUGv72AG`)
   - نشست `9a3c249c` → `arena-agent-9a3c249c-fingerprint-9483-b89a-02d1-13b5.recovery.json` (id: `1-16l_LfEHxrmwVI4HHoAQXnFMmPewl3j`) — **تأییدشده توسط مالک ۲۰۲۶-۱۰-۲۵** ✅ (پینگ واقعی `ok:true` گرفت)
   - اگر نشست تازه است، با جستجو/فهرست درایو پیداش کن (`search_files` کانکتور google_drive) و نام فایل را با برنچ خودت تطبیق بده.
3. **بازیابی:**
   ```bash
   mkdir -p ~/.arena-mailbox/home
   # ⚠️ cp خالی کار نمی‌کند: فایل درایو کلید را به شکل base64-DER نگه می‌دارد،
   # ولی send-task.mjs فیلدهای JWK می‌خواهد. از نشست 9a3c249c به بعد فایل هر دو
   # نمایش را دارد؛ فقط چهار فیلد لازم را بیرون بکش:
   python3 - <<'EOF'
   import json, os
   d = json.load(open("<downloaded>.recovery.json"))
   out = {k: d[k] for k in ("v", "createdAt", "branch")}
   out["exchangePrivateJwk"] = d["exchangePrivateJwk"]
   out["signingPrivateJwk"]  = d["signingPrivateJwk"]
   p = os.path.expanduser("~/.arena-mailbox/home/agent-identity-private.json")
   os.makedirs(os.path.dirname(p), exist_ok=True)
   json.dump(out, open(p, "w"), indent=2); os.chmod(p, 0o600)
   EOF
   node ~/.arena-mailbox/send-task.mjs fingerprint     # باید <اثرانگشت خودت> برگردد
   node ~/.arena-mailbox/send-task.mjs verifysanity    # باید {"ok":true}
   ```
   > اگر فایل بازیابی قدیمی (فقط base64-DER، بدون `*PrivateJwk`) را داری: با
   > `crypto.createPrivateKey({key:<b64>,format:"der",type:"pkcs8"})` به JWK تبدیلش کن
   > (`.export({format:"jwk"})`).
4. **چرا مهم است:** همان fingerprint است → **هیچ تأیید مجدد مالک لازم نیست**. اگر روزگاری کلید جدید ساختی (`keygen`): fingerprint جدید را به مالک بفرست تا دوباره Approve کند و فایل بازیابی جدید را هم روی درایو آپدیت کن.

راه‌نمای کانکتور: google_drive → `search_files` (جستجوی فایل) → `download_file` یا `read_file_text` (محتوای JSON)، برای آپلود `upload_file` (به id پوشهٔ بالا).

---

## ۴. پروتکل میل‌باکس (Wire format) — آمادهٔ use

### پاکت فرمان در `marketing-app-mailbox-110/inbox/<id>.json`
```json
{"v":1,"id":"task-YYYYMMDD-slug-NNN","from":"4a8a:f3a5:7c9a:dbf6","cmd":"ping","args":{},"sig":"<base64 sig>","note":"(اختیاری)"}
```

### امضا — ECDSA P-256 / SHA-256
- رشتهٔ متعارف (UTF-8، دقیقاً):
  ```
  {id}\n{cmd}\n{JSON.stringify(args, کلیدهای-مرتب-بازگشتی, بدون فاصله, non-ASCII خام)}
  ```
  یعنی سورت بازگشتی تمام کلیدها (معادل `json.dumps(..., sort_keys=True, separators=(",",":"), ensure_ascii=False)` پایتون). فرمت امضا: **IEEE-P1363** یا DER، Base64 — هر دو پذیرفته‌است (verify_signature در agent_signature.py).
- در Node:
  ```js
  crypto.sign('sha256', Buffer.from(payload,'utf8'), {key: privEC, dsaEncoding:'ieee-p1363'})
  ```
- **بعد از ساخت پاکت همیشه لوکال اختبار كن** (agent_signature.py موجود در Jinus/source/server/):
  ```bash
  cd Jinus/source/server && python3 -c "from agent_signature import verify_signature; import json; d=json.load(open(sys.argv[1])); assert verify_signature(d_priv_signingPub, d['id'], d['cmd'], d['args'], d['sig'])"
  ```

### پاسخ در `marketing-app-mailbox-110/outbox/reply_<id>.json`
```json
{"v":1,"id":"...","replyTo":"...","cmd":"...","ok":true,"at":"...","result":{...,"success":true,"duration_ms":0}}
```
- ژینوس بعد از مصرف فرمان، فایل inbox را **حذف** می‌کند؛ پاسخ را در outbox **نمی‌نویسد حذف کنی** (تاریخچه می‌ماند).
- تاخیر مشاهده‌شده: **۱۵–۳۰ ثانیه** از پوش تا شناسایی پاسخ (شنوندهٔ صندوق فعال — وضعیت state.json = `active/listening`).
- کانال تسریعی تک‑فایلی: `fast-inbox.json` / `fast-outbox.json` (همین قواعد؛ فقط یک پاکت در لحظه).
- تازیدنیا: قبل/بعد از هر `git push` روی شاخهٔ نشستت، حتماً **`git fetch + git rebase origin/<branch>`** — ژینوس روی همان برنچ commit‌های state/reply می‌زند و push اگر non-fast-forward بشود reject می‌شود.

---

## ۵. کاتالوگ کامل فرمان‌ها (CommandExecutor 1.1.0)

| فرمان | آرگ | کار |
|---|---|---|
| `ping` | `{}` | تست دوربردی/سلامت (pong، نسخه، سیستم‌عامل) |
| `diag` | `{}` | تشخیص سلامت جامع سرویس |
| `tool.check` | `{}` | وضعیت ابزارها/داخلی |
| `log.tail` | `{}` | آخرین خط‌های لاگ ژینوس |
| `settings.get` / `settings.set` | `{"key":..,"value":..}` (set) | تنظیمات (مثل gitHub.sources و …) |
| `secret.status` / `secret.set` | ماسک/صرفاً وضعیت حساس — برای تنظیم سکرت نیازمند روش مخصوص | گاوصندوق DPAPI (کلیدها ماسک) |
| `os.run` | `{"command":"python -c ..."}` (الگوی پیش‌فرض پایتونی) | **اجرای دستور روی پی‌سی مالک** ⚠️ قاعدهٔ ۱۴ (بازنگری ۲۰۲۶-۱۰-۲۲): پایتون پیش‌فرض؛ CMD/پاورشل فقط در صورت ضرورت — §۹ |
| `window.state` | — | صادقانه خطا می‌دهد (دترونم 1.1.0 پشتیبانی نمی‌کند) |
| **برازگر سی‌دی‌پی (پورت ۹۳۳۵، پروفایل `C:\Jinus-Sandbox\chrome-agent`)** | | |
| `browser.status` | `{}` | running/port/profile/tabs_count |
| `browser.launch` | `{"url":...}` | بالا آوردن کروم ایجنت |
| `browser.targets` | `{}` | همهٔ تب‌ها (id/title/url) |
| `browser.select` | `{"targetId":...}` | انتخاب تب |
| `browser.open` | `{"url":...}` | باز کردن تب جدید |
| `browser.nav` | `{"targetId":...,"url":...}` | ناوبری همان تب |
| `browser.read` | `{"targetId":...,"maxChars":20000}` | متن صفحه (SPA-آماده) |
| `browser.eval` | `{"targetId":...,"expression":"..."}` | اجرای JS با `awaitPromise`+`returnByValue` (تا ~۱۰ث) — برای ارساولین UI (کلیک/فرم) و استخراج DOM |
| `browser.close` | `{"targetId":...}` | بستن تب |
| **زارنیو (۶ کانال شبکهٔ اجتماعی)** | | |
| `zernio.accounts` | `{}` | حساب‌ها (6ab391…=اینستاگرام @bazinopro و …) |
| `zernio.posts` | `{"limit":N}` | پست‌های لیو |
| `zernio.comment-automation.create` | `{postId, keywordTheories…}` | کامنت→دایرکت |
| **رسانه و زانر** | | |
| `instagram.download` | `{url}` | دانلود پست به `C:\Jinus-Media` |
| `media.list` / `media.upload` / `media.ffmpeg` / `media.transcribe` | سند داکیومنت در command_executor | پایپ‌لاین رسانه + Groq Whisper + karaoke |
| `kling.cli` | `{...}` | تولید ویدیو Kling |
| `video.generate_5090` | `{...}` | سرور RTX 5090 (Wan2.2/Hunyuan/LivePortrait) |
| **صف انتشار** | | |
| `queue.list` / `queue.approve` / `queue.publish_now` | `{state}` / `{cardId}` / `{cardId}` | کارت‌های content pipeline |
| **پیا میاد ایجنت‌ها** | | |
| `portal.send_message` / `portal.list_messages` | `{taskType,title,payload}` | کارت پیام به پورتال ایجنت‌ها |
| **گفتگو با سوآرم** | | |
| `chat.ask` (همچنین `chat`/`ask`) | `{"instruction":"..."}` | دستور زبانی به رهبر سوآرم ژینوس |
| `remote.task` | — | (آن از طرف ژینوس/مالک برای ایجنت خارجی parqued می شود — به آن پاسخ را در outbox بنویس) |

---

## ۶. امکاناتی که با همین اتصال در اختیار است (عملی)

1. **برازگر کروم ایجنت (CDP :9335)** — ایزولهٔ chrome-agent روی پی‌سی مالک؛ **هر سایتی: ریل‌وی، گیت‌هاب، اینستاگرام،…**. سشن‌های لاگین همان پروفایل حفظ می‌شوند (تجربه: برای ورود اینترنسیونال ریل‌وی فقط تأیید دو‌مرحله‌ای گیت‌هاب روی گوشی مالک لازم بود). با `browser.eval` کنترل DOM کامل (label→input→Enter در UI، استخراج organized dữ liệu).
2. **شل ویندوز پی‌سی مالک (`os.run`)** — هر ابزار روی سیستم مالک قابل اجرا (مثلاً خواندن فایل، اجرای اسکریپت) — الگوی استاندارد جدید: دستورها با پایتون نوشته می‌شوند؛ قاعدهٔ ۱۴ بازنگری‌شده (§۹).
3. **پایپ‌لاین محتوا**: ایرانی Whisper (Groq), Kling/5090 render، برن زیرنویس کارائوکهفیروزه‌ای بازینو، دانلود اینستاگرام، صف انتشار ۶ کاناله زرنیو (اینستاگرام/یوتیوب/فیسبوک/تیک‌تاک/تلگرام/واتس‌اپ).
4. **تنظیمات ژینوس (`settings.*`)** — از قبیل مسیرهای سورس گیت‌هاب، poling interval،… (with care).
5. **گفت‌وگو با سوآرم ۹ ایجنتی ژینوس (`chat.ask`)** — رهبر ژینوس ترجمه به پرامپت‌ها و ابزارها می‌کند.
6. گاوصندوق (فقط وضعیت ماسک‌شده), لاگ, diag, صفحه‌نمایش.

---

## ۷. پروندهٔ کاری انجام‌شدهٔ مرجع (۲۰۲۶-۱۰-۲۲) — ریل‌وی

برای مرجع زمانی آینده، نتایج کلیدی مشارکت با ریل‌وی در داشبورد مالک (پروژه `noble-flexibility`):

| پارامتر | مقدار |
|---|---|
| Railway Project ID | `7c152a6e-f985-471e-8ce0-996a10279703` |
| Service: bazino-gamenet-portal | `89b8d4e7-f88f-4072-80da-b224312a1880` |
| Branch connected to production | `arena/136c4b5f-bazino-gamenet-portal` ✅ |
| شاخه‌ی فعال | **Watch Paths تنظیم شد** (۱۴ الگو): package*, server.ts, index.html, vite/tsconfig, `/src/**`, `/server/**`, `/public/**`, `/design-candidate/**`, `/docs/**`, `/scripts/**`, `/Management App/**`, `/theme-packages/**` → **پوش‌های `marketing-app-mailbox*/` هرگز دیپلوی تریگر نمی‌کنند** |
| ریشهٔ پوشهٔ Jinus در ریپو | از تراک خارج و در `Jinus/` آرشیو شد — `.gitignore` و `.dockerignore` هم به‌روز |
| نکتهٔ حیاتی | اگر الگوی Watch Paths روزی width شود، مسیرهای ایمیل ملّباکس و `Jinus/` **✕ الگوها اضافه نکن** |

---

## ۸. چک‌لیست سریع شروع نشست (۲ دقیقه)

```bash
# ۱. هویت: بازیابی یا سرد
node ~/.arena-mailbox/send-task.mjs fingerprint     # 4a8a:f3a5:7c9a:dbf6 ؟ اگر نه → §۳

# ۲. هلپر فرمان (در صورت نبودن از این مسیرها هر دو را بازنویسی کن — محتوا در همین repo/source نزن)
ls ~/.arena-mailbox/send-task.mjs ~/.arena-mailbox/mb_task.sh

# ۳. تست پین
~/.arena-mailbox/mb_task.sh task-$(date +%Y%m%d)-ping-001 ping '{}' "e2e"

# ۴. فرمان‌های بعدی راحت — فقط: mb_task.sh <id> <cmd> '<args json>'
```

⚠️ **تله‌های شناخته‌شدهٔ این محیط** (از خطاهای واقعی ۲۰۲۶-۱۰-۲۲):
- در bash هرگز `${3:-{}}` ننویس — باگ brace — مثل `mb_task.sh`: `if [ $# -ge 3 ]; then ARGS="$3"; else ARGS='{}'; fi`
- `git push` non-fast-forward می‌شود چون ژینوس هم‌زمان روی برنچتو می‌نویسد → قبل از پوش `fetch+rebase` (mb_task.sh دارد).
- eval طولانی‌تر از ~۱۰ث تایم‌اوت وب‌سوکت می‌خورد → عملیات UI را ریزگرایانه چیده و مرحله‌ای اجرا كن. (در عمل: چندین فرمان موفق — فقط پاسخش برنگشت.)
- خواندنی‌هایی پترن‌هایی mailbox قدیمی `marketing-app-mailbox/` را نگر — کانال فعلی `marketing-app-mailbox-110/` است.
- فایل‌هایی private هویت در `.arena-mailbox/home/` و **هیچ‌وقت** در repo — merge کرون پوشهٔ Jinus/source آف‌لاین صرفاً مرجع.

- `os.run` و نقل‌قول مسیرها (تجربهٔ ۲۰۲۶-۱۰-۲۲): فرمان `dir "C:\\projects\\..." /b` با کوتیشن داخلی با خطای `The filename, directory name, or volume label syntax is incorrect.` شکست خورد (لایه‌بندی کوتیشن `cmd.exe /c` از طریق subprocess). مسیرهای بدون فاصله را **بدون کوتیشن داخلی** بفرست: `{"command":"dir C:\\projects\\jinus-v1.1.0 /b"}` — موفق بود. برای مسیرهای دارای فاصله، اول با یک `echo` پروب بزن و شکل سالم را پیدا کن.

## ۹. قواعد امنیت/صداقت ژینوس (خطا قرمزها — از سند راهنما §۲/الحاقیه ۱.۱.۰)

- امضا+اثرانگشت بدون **تأیید مالک** = اجرا نمی‌شود (`sender_not_authorized`). اطلاعات لوکیِ هر یک از ژینوس ماسک‌شده است.
- **قاعدهٔ ۱۴ (بازنگری‌شده ۲۰۲۶-۱۰-۲۲ — این متن بر نسخهٔ قبلی «ممنوعیت مطلق پاورشل» حاکم است):** تمام دستورهای `os.run` باید با پایتون نوشته و اجرا شوند. استفاده از پاورشل و CMD تنها در مواقع ضروری که پایتون پاسخگو نباشد مجاز است.
  - الگوی پیش‌فرض: `{"command":"python -c \"...\""}` با کوتیشن تکی داخل اسکریپت پایتون (تلهٔ کوتیشن cmd در §۸).
  - **وضعیت صادقانهٔ کد نسخهٔ ۱.۱.۰:** گارد `command_executor.py` فعلاً کلمهٔ `powershell` را مسدود می‌کند و هر دستور را از `cmd.exe /c` عبور می‌دهد؛ انطباق کد با سیاست جدید نیازمند تغییر کد جداگانه با تأیید مالک است (روی سورس زنده `C:\projects\jinus-v1.1.0`، نه فقط میرور `Jinus/source/`).
- دكترین ۳ پوشه: اجرای ژینوس فقط `C:\Jinus-Media`, `C:\projects\Antigravity Bazino`, `C:\Jinus-Sandbox` می‌نویسد.
- Zero Fake: هیچ‌کدام جعل/نمایش ساختگی — اگر خطا بود صادقانه خطا برمی‌گردد.
- سکرت‌ها دور از git و دور از chat؛ با مالک Rotate. ایمیجی لینک `C:\Jinus-Media` می‌نشیند.

## ۱۰. Bazino Agent App (GitHub App اختصاصی، App ID 5185952) — ۲۰۲۶-۱۰-۲۳

| مورد | مقدار |
|---|---|
| راهنمای کامل اتصال | گوگل‌درایو، پوشهٔ «Bazino Arena Agents» → `Bazino-GitHub-App-GUIDE.md` (Drive id: `13t_wcWvkF-CV46ENBBaVsSb2HPmgo0XP`) |
| وضعیت احراز | **فعال و آزموده از هر دو مسیر (HTTP 200، ۲۰۲۶-۱۰-۲۳): پی‌سی مالک و پل Actions از سندباکس** |
| Installation ID | `167818802` (حساب paymanshafayan، انتخاب all، ۱۰ مخزن شامل دو مخزن خصوصی) |
| دسترسی‌ها | actions:write، contents:write، pull_requests:write، workflows:write، metadata:read — وب‌هوک غیرفعال (events=[]) |
| کلید کارکننده | پی‌سی `C:\projects\bazino-app.pem` = فایل قدیمی درایو `bazino-app.pem` (id: `1A5O91Ro61TwfvrjFC--q3psmsJxqopAo`، هش نرمال `8f850b82…`، فایل خام BOM+CRLF)؛ کلید تازهٔ `bazino-agent-app.2026-10-23.private-key.pem` فقط روی درایو و نصب‌نشده |
| Client ID | `Iv23liM11VBfajDjmurY` |
| قید محیطی (اثبات‌شده) | پروکسی E2B credential broker هدر Authorization هر درخواست `api.github.com` را اجباری با توکن OAuth آرنا بازنویسی می‌کند (گواهی «E2B Proxy CA»)؛ JWT اپ به هیچ شکلی از سندباکس عبور نمی‌کند. این محدودیت کلید را خراب نمی‌کند — همان کلید از پی‌سی و از رانر Actions پاسخ ۲۰۰ می‌گیرد |
| **راه حل سندباکس: Bazino App Bridge** | فرمان در `.bazino-bridge/queue/` کامیت می‌شود → workflow `bazino-app-bridge.yml` روی رانر (شبکهٔ تمیز) با سکرت `BAZINO_APP_PEM` توکن می‌سازد و `scripts/bazino_bridge_dispatch.py` فرمان را اجرا و نتیجه را در `.bazino-bridge/results/` کامیت می‌کند → `scripts/bazino_bridge_client.sh` نتیجه را می‌خواند. احراز دوگانه: JWT اپ برای `/app*`، توکن نصب (کل ۱۰ مخزن، ورودی owner) برای بقیه. استفاده: `bash scripts/bazino_bridge_client.sh <METHOD> <PATH> [body\|@file]` |
| سکرت Actions | `BAZINO_APP_PEM` روی مخزن portal — کلید نرمال‌شده بدون BOM/LF؛ بازتنظیم فقط از پی‌سی با `gh secret set` |
| مسیر دوم (پی‌سی) | فراخوانی مستقیم via mailbox `os.run` (الگوی base64→certutil→py)؛ پیاده‌سازی مرجع `Jinus/source/server/multi_agent_relay.py` — JWT RS256 (PyJWT نصب است) → installation token یک‌ساعته، ماندگار نشود |
| محدودیت‌های پل v1 | لاگ باینری اجراها (zip) فقط تا ۲۰۰KB متنی ذخیره می‌شود (v2: artifact)؛ تحریک فقط با push (توکن آرنا actions:write ندارد)؛ تأخیر ۱۵–۳۰ ثانیه |
| در سندباکس | کلیدها خارج از مخزن در `~/.config/bazino/` با ۰۶۰۰؛ CA محیط: `/run/e2b/certs/ca-certificates.crt` |

—

**نسخه:** v2 — ۲۰۲۶-۱۰-۲۳ (نشست 261c806a) — بن‌بست پروکسی سندباکس با معماری **Bazino App Bridge** شکسته شد: فراخوانی GitHub App با هویت اپ ۵۱۸۵۹۵۲ اکنون از خود سندباکس end-to-end پاسخ ۲۰۰ می‌گیرد (آزمون‌های /app، /installation/repositories با ۱۰ مخزن، actions/runs همه سبز). v1 — ۲۰۲۶-۱۰-۲۲ (نشست 136c4b5f).

---

## ۹-الف. اجرای `os.run` — آنچه با آزمون واقعی (۲۰۲۶-۱۰-۲۵) درآمد

سه بار هزینه داد؛ ثبت می‌شود تا تکرار نشود.

| یافته | جزئیات |
|---|---|
| **بدون شل اجرا می‌شود** | دستور با argv مستقیم و split روی فاصله اجرا می‌شود، نه `cmd /c`. پس `python -c "…"` با نقل‌قول دوگانه می‌شکند و پایتون فقط `"import` را می‌گیرد (خطای `unterminated string literal`). |
| **قالب درست** | `python -c exec(__import__('base64').b64decode('<B64>').decode())` — سه توکن، صفر نقل‌قول دوگانه. |
| **سقف طول فرمان** | حدود ۴ کیلوبایت. فرمان ۶٫۴KB با «The command line is too long» رد شد. تنظیم امن: ≤ ۳٬۲۰۰ نویسه. |
| **تأخیر پاسخ** | ۹۰ تا ۲۴۰ ثانیه (پولینگ ژینوس + پوش گیت‌هاب). پنجرهٔ ۶۰ ثانیه‌ای همیشه دیر است. |
| **ترتیب تحویل تضمینی نیست** | چند پاکت پشت‌سرهم ممکن است خارج از ترتیب پردازش شوند. هرگز «append» نکنید؛ هر تکه فایل مستقل بنویسد و فرمان آخر به ترتیب نام بخواند. |
| **تشخیص پاسخ از working tree خطاپذیر است** | در حلقهٔ انتظار، rebase/fetch ممکن است به‌خاطر فایل‌هایی که خود ژینوس کامیت می‌کند گیر کند. پاسخ را از `git show origin/<branch>:marketing-app-mailbox-110/outbox/reply_<id>.json` بخوانید. |
| **انتقال فایل بزرگ** | `zlib → base64 → تکه‌های ۱۴۰۰ نویسه` در پوشهٔ `.stage`، سپس فرمان نهایی که می‌چسباند، از فشرده درمی‌آورد و **sha256 را تأیید می‌کند**. بدون تأیید هش، انتقال را موفق نشمارید. |
| **`start /b` ممنوع** | برای اجرای طولانی، `subprocess.Popen(..., creationflags=0x00000008)` (DETACHED_PROCESS) با هدایت خروجی به فایل لاگ. |

## ۹-ب. اجرای ورک‌فلوی بومی از خط فرمان (مسیر کشف‌شده)

- CLI واقعی داخل نصب، `resources\glm\zcode.cjs` است (۱۵MB، `#!/usr/bin/env node`) — **نه** `zcode.EXE`؛ آن دومی خودِ برنامهٔ گرافیکی است و با `--help` هم تی‌آی‌یو باز می‌کند (۲۵ ثانیه تایم‌اوت).
- اجرای بدون رابط: `node zcode.cjs -p "<prompt>" --workflow-mode alwaysOn --mode yolo --json --surface terminal --cwd <dir>`
- `--workflow-mode` پیش‌فرض **disabled** است؛ بدون `alwaysOn`/`onDemand` ابزار `CreateWorkflow` در دسترس نیست.
- CLI فایل کانفیگ پرووایدر را در `resources\glm\provider\zcode-builtin.json` می‌جوید، ولی نصب آن را در `resources\config\provider\zcode-builtin.json` دارد → یک کپی لازم است.
- **وابستگی مدل:** پرووایدر روی `http://127.0.0.1:5377/v1/chat/completions` (رلهٔ ژینوس، مدل `Jinus`) تنظیم شده است. اگر رله بالا نباشد و/یا پروکسی `127.0.0.1:10808` مرده باشد، هر اجرا با `ECONNREFUSED` می‌میرد، پیش از آنکه یک ورک‌فلو شروع شود.

## ۹-ج. زنجیرهٔ مدل و رله — تشخیص قطعی (۲۰۲۶-۱۰-۲۵)

اجرای headless ورک‌فلو تا وقتی مدل در دسترس نباشد شروع نمی‌شود. زنجیرهٔ واقعی:

```
CLI (resources\glm\zcode.cjs) → provider "Jinus" → http://127.0.0.1:5377/v1/chat/completions
                                                        └─ tools\jinus-relay.py
                                                             ├─ OmniRoute  (مسیر اصلی)
                                                             └─ Groq       (فیل‌بک کنترل‌شده)
```

| علامت | معنا | اقدام |
|---|---|---|
| `ECONNREFUSED 127.0.0.1:10808` | VPN/پروکسی قطع است | **پروکسی را دست نزن**؛ منتظر وصل شدن VPN بمان |
| `503 Service Unavailable` با هدر `proxy-connection` | رله خاموش است و پروکسی برای `127.0.0.1` جواب ندارد | رله را بالا بیاور |
| `jinus-relay: upstream failed (omni=…; groq=groq-exhausted)` | رله بالاست ولی هر دو upstream شکست خورده‌اند | مشکل کلید/سهمیه/شبکه — بیرون از کد |

- رله را همان‌طور که لانچر بالا می‌آورد اجرا کن: `py <ZCode>\tools\jinus-relay.py` با `JINUS_RELAY_PORT=5377`، detached. رله اگر پورت گرفته باشد بی‌صدا خارج می‌شود (بی‌خطر است).
- **`start-zcode.cmd` را وقتی اپ باز است اجرا نکن:** گاردش فقط `ZCode.exe` را می‌بیند، ولی نمونهٔ جداشده با نام `Jinus.exe` اجرا می‌شود → نمونهٔ دوم باز می‌شود (نقض قاعدهٔ ۹).

## ۹-د. درس ریست سندباکس (۲۰۲۶-۱۰-۲۵)

ریست، هم `~/.arena-mailbox` (کلید + ابزار ارسال) و هم ورک‌اسپیس را به یک اسنپ‌شات **قدیمی** برگرداند.

- بازیابی هویت: فایل درایو → اسنیپت playbook §۳ → `fingerprint` + `verifysanity`. اثرانگشت همان بود، **تأیید دوبارهٔ مالک لازم نشد**.
- پس از ریست، **همیشه** `git reset --hard origin/arena/9a3c249c-jinus` بزن: ورک‌اسپیس ممکن است چند کامیت عقب‌تر باشد و `rebase` با تغییرات ناخواسته شکست بخورد.
- **ابزار ارسال باید در ریپو باشد، نه فقط در `~`:** اکنون در `tools/mailbox/send-task.mjs` (بدون کلید) نسخه‌دار است؛ فقط کلید خصوصی بیرون از گیت می‌ماند.
