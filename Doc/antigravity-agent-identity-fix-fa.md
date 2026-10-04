# دستور دقیق برای ایجنت مخزن `bazino-advertising-campaign` — انتشار هویت درست در صندوق فرمان

> **برای:** ایجنت همان مخزن (نشستی که آن شاخه را مدیریت می‌کند)
> **از طرف:** نشست `arena/01a10048-bazino-gamenet-portal` · تاریخ: ۲۰۲۶-۱۰-۰۴
> **موضوع:** فایل هویت ایجنت روی برنچ `Antigravity` نامعتبر است و برنامهٔ ژینوس آن را رد می‌کند؛ این سند گام‌به‌گام و دستوربه‌دستور می‌گوید چه کار کن تا هویت درست منتشر شود.

---

## ۱. مشکل، دقیق و با شاهد

- برنامهٔ ژینوس (نسخهٔ ۰٫۸.۵) برنچ `Antigravity` از مخزن `paymanshafayan/bazino-advertising-campaign` را به‌درستی ثبت کرده و **واقعاً همان‌جا کار می‌کند**: هر ۵ دقیقه وضعیت امضاشدهٔ خودش را در `marketing-app-mailbox/state.json` همان برنچ می‌نویسد و `app-identity.json` خودش (اثر انگشت `56cf:8ce5:270d:9498`) آنجاست.
- اما فایل `marketing-app-mailbox/agent-identity.json` روی همان برنچ **با ابزار استاندارد ساخته نشده** و دو ایراد دارد:
  1. **اثر انگشت با کلیدها نمی‌خواند:** کلیدهای داخل فایل، اثر انگشت `5ca3:0748:8a0d:c19f` را می‌سازند، ولی در فایل `c4eb:c220:44a5:a53b` نوشته شده است.
  2. **یک کلید برای هر دو نقش:** مقدار `exchange` و `signing` **دقیقاً یکی** است؛ ابزار استاندارد دو کلید جدا می‌سازد.
- شاهد از لاگ خود برنامه: ساعت ۱۶:۴۲ و ۱۶:۴۸ امروز ثبت شده — «پروندهٔ هویت ایجنت معتبر نیست … `identity fingerprint does not match its keys`» برای منبع `…bazino-advertising-campaign@Antigravity#marketing-app-mailbox`.
- نتیجه: برنامه هیچ ایجنتی برای آن برنچ ثبت نمی‌کند، پس وضعیت آن برنچ «فعال» نمی‌شود و ردیفش روی «در حال اتصال» می‌ماند.

**پس کار لازم، فقط یک چیز است: فایل هویت را با ابزار استاندارد دوباره بساز و همان فایل را روی برنچ `Antigravity` جایگزین/منتشر کن.**

---

## ۲. پیش‌نیازها (یک‌بار بررسی کن)

| مورد | شرط |
|---|---|
| ابزار استاندارد | در همان مخزن موجود است: `marketing-app/agent/mailbox.cjs` — باید ۳۰٬۳۲۷ بایت و SHA-256 آن `488a33905c284b550639ab677b9237387749b4dbfac14813d7c3287b7b0708df` باشد (با ابزار نسخهٔ ۰٫۸٫۵ مو‌به‌مو یکی است). |
| Node.js | نسخهٔ ۱۸ یا بالاتر (`node --version`). |
| گیت | دسترسی push به مخزن `paymanshafayan/bazino-advertising-campaign` و اجازهٔ نوشتن روی برنچ `Antigravity`. |
| پوشهٔ کلید خصوصی | پیش‌فرض `~/.bazino-agent` (ویندوز: `%USERPROFILE%\.bazino-agent`). اگر متغیر `BAZINO_AGENT_HOME` را جایی ست کرده‌اید، همان مسیر استفاده می‌شود. **این پوشه هرگز نباید داخل گیت برود.** |

بررسی سریع ابزار:

```bash
sha256sum marketing-app/agent/mailbox.cjs
# انتظار: 488a33905c284b550639ab677b9237387749b4dbfac14813d7c3287b7b0708df
```

```powershell
Get-FileHash marketing-app\agent\mailbox.cjs -Algorithm SHA256
# انتظار: 488A33905C284B550639AB677B9237387749B4DBFAC14813D7C3287B7B0708DF
```

---

## ۳. دستورهای اجرا (به ترتیب — همه را در ریشهٔ همین مخزن اجرا کن)

### گام ۱ — روی برنچ درست قرار بگیر

```bash
cd /path/to/bazino-advertising-campaign
git fetch origin Antigravity
git switch Antigravity
# اگر برنچ لوکال نداری:
# git switch -c Antigravity origin/Antigravity
git status --short          # باید تمیز باشد؛ اگر تغییر نیمه‌کاره داری اول تعیین تکلیف کن
```

```powershell
Set-Location C:\path\to\bazino-advertising-campaign
git fetch origin Antigravity
git switch Antigravity
git status --short
```

### گام ۲ — ساخت هویت با ابزار استاندارد (کلید خصوصی فقط روی همین ماشین می‌ماند)

```bash
node marketing-app/agent/mailbox.cjs init --label "Antigravity agent — bazino-advertising-campaign"
```

```powershell
node marketing-app\agent\mailbox.cjs init --label "Antigravity agent — bazino-advertising-campaign"
```

خروجی، `fingerprint` و مسیر `privateKeyFile` را چاپ می‌کند. آن مسیر باید **بیرون از مخزن** باشد (پیش‌فرض `~/.bazino-agent/agent-identity-private.json`). اثر انگشت چاپ‌شده را یادداشت کن؛ همین در برنامه برای تأیید مالک لازم است.

### گام ۳ — انتشار فایل هویت در پوشهٔ صندوق (جایگزین فایل خراب)

```bash
node marketing-app/agent/mailbox.cjs publish --label "Antigravity agent — bazino-advertising-campaign"
```

```powershell
node marketing-app\agent\mailbox.cjs publish --label "Antigravity agent — bazino-advertising-campaign"
```

این دستور فایل `marketing-app-mailbox/agent-identity.json` را **بازنویسی** می‌کند؛ فایل نامعتبر قبلی خودش جایگزین می‌شود.

### گام ۴ — بازبینی پیش از کامیت (اجباری)

```bash
# ۱) اثر انگشت فایل باید دقیقاً با اثر انگشت چاپ‌شده در گام ۲ یکی باشد
node marketing-app/agent/mailbox.cjs fingerprint marketing-app-mailbox/agent-identity.json

# ۲) دو کلید باید جدا باشند (خروجی باید false باشد)
node -e "const d=require('./marketing-app-mailbox/agent-identity.json');console.log('same key:', d.exchange===d.signing)"

# ۳) فقط همان یک فایل تغییر کرده باشد و کلید خصوصی جایی در گیت نباشد
git status --short
git diff --stat
```

```powershell
node marketing-app\agent\mailbox.cjs fingerprint marketing-app-mailbox\agent-identity.json
node -e "const d=require('./marketing-app-mailbox/agent-identity.json');console.log('same key:', d.exchange===d.signing)"
git status --short
```

### گام ۵ — کامیت و پوش روی همان برنچ

```bash
git add marketing-app-mailbox/agent-identity.json
git commit -m "mailbox: publish tool-generated agent identity (fixes fingerprint mismatch)"
git push origin Antigravity
git log -1 --stat     # تأیید کن همین یک فایل در کامیت است
```

```powershell
git add marketing-app-mailbox\agent-identity.json
git commit -m "mailbox: publish tool-generated agent identity (fixes fingerprint mismatch)"
git push origin Antigravity
git log -1 --stat
```

---

## ۴. بعد از پوش — چه اتفاقی می‌افتد

1. ظرف حدود ۵ ثانیه، برنامهٔ ژینوس فایل را می‌خواند و در لاگش می‌نویسد «هویت ایجنت وصل شد» و اثر انگشت تازه را در فهرست **«در انتظار تأیید»** نشان می‌دهد.
2. **تأیید مالک لازم است** (یک کلیک، سمت مالک): برنامه → صفحهٔ «اتصال» → کارت «صندوق فرمان» → بخش «ایجنت‌های ثبت‌شده» → دکمهٔ «تأیید» روی همان اثر انگشت. تا آن لحظه، هر فرمان این ایجنت با کد `agent_not_approved` رد می‌شود (این رفتار خواستهٔ مالک است).
3. بعد از تأیید، آزمون پایانی را اجرا کن (نیاز به توکن گیت‌هاب: `GITHUB_TOKEN` یا `gh auth token`):

```bash
GITHUB_TOKEN=$(gh auth token) node marketing-app/agent/mailbox.cjs call \
  --cmd ping \
  --note "verify after owner approval" \
  --repo paymanshafayan/bazino-advertising-campaign \
  --branch Antigravity
```

```powershell
$env:GITHUB_TOKEN = (gh auth token)
node marketing-app\agent\mailbox.cjs call --cmd ping --note "verify after owner approval" --repo paymanshafayan/bazino-advertising-campaign --branch Antigravity
```

**خروجی درست:** `{"ok": true, "result": {"pong": true, "version": "0.8.5", ...}}`
**اگر `agent_not_approved` گرفتی:** یعنی مالک هنوز تأیید نکرده؛ دوباره تلاش نکن تا تأیید انجام شود.

---

## ۵. کارهایی که نباید بکنی (جامعهٔ خطاها همین‌جاست)

- **دست‌کاری دستی فایل هویت ممنوع.** اثر انگشت داخل فایل باید خروجی دقیق خود کلیدها باشد؛ هر ویرایش دستی، همان خطای `identity fingerprint does not match its keys` را برمی‌گرداند.
- **یک کلید را برای هر دو نقش `exchange` و `signing` نگذار.** ابزار استاندارد دو کلید مجزا می‌سازد.
- **کلید خصوصی (`agent-identity-private.json`) را کامیت نکن.** فقط فایل عمومی `agent-identity.json` در `marketing-app-mailbox/` می‌رود.
- **فایل هویت را روی برنچ دیگری نگذار.** برنامه فقط همان برنچی را می‌خواند که مالک ثبت کرده (`Antigravity`).
- **بدون تأیید مالک، فرمان نفرست.** (پاسخ `agent_not_approved` طبیعی است، ولی تکرار پشت‌سرهم فایده ندارد.)
- **از هر برنچ فقط یک ایجنت.** فایل هویت در هر برنچ فقط یک جا دارد؛ اگر ایجنت دیگری هم لازم است، برنچ جداگانه لازم دارد.
- ابزار استاندارد را با نسخهٔ دیگری عوض نکن؛ همان فایل با SHA-256 بالا درست است.

---

## ۶. اگر مشکلی پیش آمد

| نشانه | معنا | کار |
|---|---|---|
| `sha256` ابزار فرق داشت | ابزار قدیمی/تغییریافته | همان فایل `marketing-app/agent/mailbox.cjs` مخزن را استفاده کن، نه کپی‌های دیگر |
| خروجی `publish` می‌گوید فایل نوشته شد ولی در `git status` نیست | در پوشهٔ اشتباه اجرا شده | در ریشهٔ همان مخزن و روی برنچ `Antigravity` اجرا کن |
| در لاگ برنامه باز هم «پروندهٔ هویت ایجنت معتبر نیست» هست | فایل قدیمی هنوز سر جایش است یا پوش نشده | گام ۴ و ۵ را دوباره چک کن (`git log -1 --stat` و محتوای فایل روی برنچ) |
| اثر انگشت در برنامه دیده نمی‌شود | پوش نرسیده یا ۵ ثانیه نگذشته | چند ثانیه صبر؛ سپس `git ls-remote origin Antigravity` و آخرین کامیت را چک کن |
| `call` پشت سر هم `agent_not_approved` می‌دهد | تأیید مالک انجام نشده | از مالک بخواه در فهرست «ایجنت‌های ثبت‌شده» تأیید کند |

---

## ۷. پیام کوتاهی که می‌توانی به مالک بدهی

> فایل هویت صندوق فرمان روی برنچ `Antigravity` با ابزار استاندارد نامعتبر ساخته شده بود (اثر انگشت با کلیدها نمی‌خواند). با ابزار خود مخزن دوباره ساخته و روی همان برنچ منتشر شد. اثر انگشت تازه: `………………`. لطفاً در برنامه: «اتصال» → «صندوق فرمان» → «ایجنت‌های ثبت‌شده» آن را تأیید کنید تا پیام‌ها اجرا شوند.
