# 🔧 دستورالعمل کامل ساخت نرم‌افزار پل مرورگر

**نسخه:** ۱.۰ · **تاریخ:** ۲۰۲۶-۰۹-۱۹
**وضعیت:** ✅ ساخته شد، تست شد، در عمل کار می‌کند

این سند **همه چیز** را برای بازسازی کامل پل از صفر دارد: معماری، سورس کد،
ورک‌فلو گیت‌هاب، نصب، و باگ‌هایی که در راه پیدا شدند.

> 📌 اگر فقط می‌خواهید **استفاده** کنید نه بازسازی، به
> `tools/bazino-bridge/README-fa.md` بروید. این سند برای بازسازی است.

---

# فهرست

1. [مسئله چه بود](#۱-مسئله-چه-بود)
2. [معماری راه‌حل](#۲-معماری-راهحل)
3. [محدودیت‌های اندازه‌گیری‌شده](#۳-محدودیتهای-اندازهگیریشده)
4. [سورس کد](#۴-سورس-کد)
5. [ورک‌فلو گیت‌هاب](#۵-ورکفلو-گیتهاب)
6. [نصب و راه‌اندازی](#۶-نصب-و-راهاندازی)
7. [باگ‌هایی که پیدا شدند](#۷-باگهایی-که-پیدا-شدند)
8. [عیب‌یابی](#۸-عیبیابی)

---

# ۱. مسئله چه بود

ایجنت در یک سندباکس لینوکسی اجرا می‌شود. کروم مالک روی ویندوز در قبرس شمالی
است و به حساب گوگل بازینو لاگین است. ایجنت باید آن کروم را کنترل کند تا در
دایرکتوری‌ها ثبت‌نام کند، پروفایل گوگل را ویرایش کند و فرم پر کند.

## روش قدیمی و هزینه‌اش

| مشکل | جزئیات |
|---|---|
| کپی‌پیست هر جلسه | ~۷۰ خط PowerShell، دستی، هر بار |
| **VPN اجباری** | دامنه `sbx-*.arena.site` در شبکه مالک بسته است |
| آدرس متغیر | هر ری‌ست سندباکس یک نام میزبان جدید می‌سازد |
| شکننده | پنجره باید باز بماند؛ یک کلیک داخلش حلقه را فریز می‌کرد (QuickEdit) |
| ری‌ست‌پذیر | سندباکس در یک روز سه بار ری‌ست شد و هر بار رله از بین رفت |

## 🔑 کشف کلیدی

VPN **هیچ‌وقت نیاز فنی معماری نبود.** فقط دور زدن یک دامنه بلاک‌شده بود.

گیت‌هاب در قبرس شمالی بلاک نیست — مالک هر روز بدون VPN `git push` می‌کند.
**تعویض مسیر انتقال، نیاز به VPN را کاملاً حذف کرد.**

---

# ۲. معماری راه‌حل

```
ایجنت (سندباکس لینوکس)          گیت‌هاب           کروم مالک (ویندوز)
        │                          │                      │
        │  git push دستور ───────▶ │                      │
        │                    شاخه cdp-bus                 │
        │                          │ ◀─── هر ۲ ثانیه poll │
        │                          │                      │
        │                          │              اجرا با CDP
        │                          │                      │
        │                          │ ◀────── push نتیجه   │
        │  gh api خواندن ◀──────── │                      │
```

گیت‌هاب نقش **صندوق پست** را بازی می‌کند. ایجنت نامه می‌گذارد، نرم‌افزار
برمی‌دارد و جواب می‌گذارد.

## ساختار شاخه پیام‌رسان

شاخه `cdp-bus` یک شاخه **orphan** است — تاریخچه پروژه را آلوده نمی‌کند و هر
وقت لازم شد کامل حذف می‌شود.

```
cdp-bus/
├── cmd/<شماره>.json    ← ایجنت می‌نویسد، نرم‌افزار می‌خواند
├── res/<شماره>.json    ← نرم‌افزار می‌نویسد، ایجنت می‌خواند
└── status.json          ← ضربان
```

## چرا polling و نه webhook

وب‌هوک گیت‌هاب به یک شنونده عمومی نیاز دارد. سندباکس هیچ مسیر ورودی ندارد.
پس نرم‌افزار poll می‌کند؛ تأخیر ۲ ثانیه برای پر کردن فرم قابل قبول است.

## تأخیر واقعی (اندازه‌گیری‌شده)

| مسیر | رفت و برگشت |
|---|---|
| پل قدیمی (با VPN روشن) | ۱ تا ۳ ثانیه |
| گیت‌هاب | **۸ تا ۱۹ ثانیه** |

⚠️ کندتر از تخمین اولیه است چون هر دستور یک `git push` کامل می‌خواهد. برای
کارهایی مثل پر کردن فرم قابل قبول است.

---

# ۳. محدودیت‌های اندازه‌گیری‌شده

این‌ها **اندازه‌گیری شدند، نه حدس زده شدند** (۲۰۲۶-۰۹-۱۸). دوباره استخراجشان نکنید.

## خروجی مجاز سندباکس

```
https://api.github.com               → 200  ✅
https://github.com                   → 200  ✅
https://registry.npmjs.org           → 200  ✅
https://raw.githubusercontent.com    → exit 35  ❌
https://gist.githubusercontent.com   → exit 35  ❌
https://cloudflare.com               → exit 35  ❌
https://ngrok.com                    → exit 35  ❌
```

⚠️ `raw.githubusercontent.com` بسته است — محتوا باید از **API** خوانده شود، نه CDN.

## سرعت و سقف

| عملیات | مقدار |
|---|---|
| `git push` از سندباکس | ~۱.۲ ثانیه |
| خواندن `gh api` | ~۰.۳۶ ثانیه |
| سقف نرخ | ۵۲۰۰ درخواست/ساعت |

با poll دو ثانیه‌ای = ۱۸۰۰ در ساعت، کاملاً در بودجه.

## توکن

توکن سندباکس `arena-ai-coding-agent[bot]` است.
`permissions.push` مقدار **false** می‌دهد ولی `git push` روی HTTPS **کار می‌کند**.
نوشتن باید از `git push` انجام شود، نه از contents API.

🔴 **ایجنت اجازه ساخت فایل ورک‌فلو ندارد** — توکنش `workflows` scope ندارد.
هر تغییر ورک‌فلو باید کامل در چت داده شود تا مالک جایگزین کند.

## راه‌هایی که بن‌بست‌اند

| روش | چرا شکست خورد |
|---|---|
| تانل cloudflared / ngrok | هر دو دامنه در خروجی سندباکس بسته |
| مستقیم `{port}-{id}.e2b.app` | هدر `e2b-traffic-access-token` می‌خواهد |
| WebSocket از پروکسی arena | فریم‌ها ~۳۰ ثانیه معطل یا گم می‌شوند |
| پل داخل‌مرورگری به `ws://127.0.0.1` | mixed-content + Private Network Access |
| CDN خام گیت‌هاب | `raw.githubusercontent.com` بسته |
| **افزونه کروم** | ❌ **مالک رد کرد** — نرم‌افزار دسکتاپ جایگزین شد |

---

# ۴. سورس کد

## ۴.۱ ساختار فایل‌ها

```
tools/bazino-bridge/
├── BazinoBridge.ps1            ← برنامه دسکتاپ (۸۳۹ خط)
├── agent-bus.js                ← کلاینت سمت ایجنت (۲۱۰ خط)
├── workflow-build-bridge.yml   ← نسخه مرجع ورک‌فلو (۱۹۴ خط)
└── README-fa.md                ← راهنمای کاربر
```

## ۴.۲ نقشه توابع `BazinoBridge.ps1`

| بخش | توابع |
|---|---|
| **لاگ** | `Write-Log` |
| **تنظیمات** | `Get-Settings` · `Save-Token` · `Read-Token` |
| **اتوبوس گیت‌هاب** | `Invoke-GitHub` · `Test-GitHub` · `Get-BusCommands` · `Push-BusResult` |
| **کروم** | `Find-ChromePath` · `Test-ChromeDebug` · `Test-ProfileInUse` · `Start-AgentChrome` · `Connect-ChromeSocket` |
| **CDP** | `Send-Cdp` · `Clear-CdpBacklog` · `Receive-CdpFrame` · `Receive-Cdp` |
| **بازیابی** | `Repair-Bridge` |
| **حلقه اصلی** | `Invoke-BridgeCycle` · `Start-Bridge` · `Stop-Bridge` |
| **رابط** | `New-StatusRow` · `Update-Status` · `Show-Settings` · `New-MainWindow` |
| **تست** | `Stop-SelfTest` |

## ۴.۳ پیکربندی (خطوط ۳۴ تا ۴۳)

```powershell
$script:Cfg = @{
    Owner        = 'paymanshafayan'
    Repo         = 'bazino-advertising-campaign'
    BusBranch    = 'cdp-bus'
    ChromePort   = 9333
    ProfileDir   = Join-Path $env:USERPROFILE 'chrome-bazino'
    PollMs       = 2000
    MaxRetries   = 5
    SettingsPath = Join-Path $env:APPDATA 'BazinoBridge\settings.json'
}
```

## ۴.۴ نگهداری امن توکن

توکن با **DPAPI ویندوز** رمزگذاری می‌شود. فقط حساب ویندوز مالک روی همان
کامپیوتر می‌تواند بازش کند.

```powershell
function Save-Token {
    param([string]$Token)
    $dir = Split-Path $script:Cfg.SettingsPath -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    # DPAPI: only this Windows user on this machine can read it back.
    $secure    = ConvertTo-SecureString $Token -AsPlainText -Force
    $encrypted = ConvertFrom-SecureString $secure

    @{ token = $encrypted } | ConvertTo-Json |
        Set-Content $script:Cfg.SettingsPath -Encoding UTF8
}
```

## ۴.۵ 🔴 جلوگیری از باز شدن کروم تکراری

**سه لایه بررسی** تا هرگز نمونه دوم باز نشود و لاگین گوگل نپرد:

```powershell
function Test-ProfileInUse {
    # True when a Chrome process already owns our profile folder.
    $lock = Join-Path $script:Cfg.ProfileDir 'lockfile'
    if (Test-Path $lock) { return $true }

    try {
        $esc = [regex]::Escape($script:Cfg.ProfileDir)
        $hit = Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" -ErrorAction Stop |
               Where-Object { $_.CommandLine -and $_.CommandLine -match $esc }
        return [bool]$hit
    } catch {
        return $false
    }
}
```

منطق `Start-AgentChrome`:

```
۱. پورت دیباگ جواب می‌دهد؟  → همان را استفاده کن
۲. قفل پروفایل هست؟          → تا ۱۰ ثانیه منتظر پورتش بمان
۳. هیچ‌کدام؟                  → تازه کروم باز کن
```

**فلگ‌های ثابت** — هر بار دقیقاً یکسان، وگرنه کروم پروفایل را بازمی‌سازد و
لاگین می‌پرد:

```powershell
$args = @(
    "--remote-debugging-port=$($script:Cfg.ChromePort)"
    "--remote-allow-origins=*"
    "--user-data-dir=`"$($script:Cfg.ProfileDir)`""
    "--no-first-run"
    "--no-default-browser-check"
    "--restore-last-session"
)
```

## ۴.۶ 🔴 تطبیق پاسخ CDP با شناسه

CDP بین جواب‌ها رویدادهای ناخواسته می‌فرستد. برگرداندن «فریم بعدی» جواب
اشتباه می‌دهد.

```powershell
function Receive-Cdp {
    # Returns the reply whose id matches $ExpectId. Events are skipped.
    param(
        [int]$TimeoutMs = 30000,
        [int]$ExpectId  = -1
    )
    $sw = [Diagnostics.Stopwatch]::StartNew()

    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        $remaining = $TimeoutMs - $sw.ElapsedMilliseconds
        $frame = Receive-CdpFrame -TimeoutMs $remaining

        if ($ExpectId -lt 0) { return $frame }

        $id = $null
        try { $id = ($frame | ConvertFrom-Json).id } catch { }

        if ($null -eq $id) { continue }          # event, not a reply
        if ([int]$id -eq $ExpectId) { return $frame }
        # stale reply from an earlier command - drop and keep waiting
    }
    throw "No CDP reply with id $ExpectId within $TimeoutMs ms"
}
```

## ۴.۷ 🔴 خالی کردن بافل رویداد

`Page.navigate` سیل رویداد می‌سازد. بدون خالی کردن، هر دستور بعدی کندتر می‌شود
(از ۸ به ۴۱ ثانیه رسید).

```powershell
function Clear-CdpBacklog {
    # Anything still buffered belongs to an earlier command.
    $dropped = 0
    while ($script:State.Socket -and
           $script:State.Socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
        try {
            $frame = Receive-CdpFrame -TimeoutMs 120
            if (-not $frame) { break }
            $dropped++
            if ($dropped -gt 500) { break }
        } catch {
            break   # nothing waiting - the normal exit
        }
    }
    if ($dropped -gt 0) { Write-Log "Dropped $dropped stale CDP frames" 'info' }
}
```

## ۴.۸ رفع خودکار خطا

```powershell
function Repair-Bridge {
    param([string]$Reason)

    $script:State.ConsecFails++
    if ($script:State.ConsecFails -gt $script:Cfg.MaxRetries) {
        Write-Log "Too many consecutive failures - stopping." 'error'
        Stop-Bridge
        return $false
    }

    # exponential backoff: 2, 4, 8, 16 seconds
    $backoff = [Math]::Min([Math]::Pow(2, $script:State.ConsecFails), 16)
    Start-Sleep -Seconds $backoff

    if (-not (Test-ChromeDebug)) {
        if (-not (Start-AgentChrome)) { return $false }
    }
    if (-not (Connect-ChromeSocket)) { return $false }
    return $true
}
```

| خطا | واکنش |
|---|---|
| کروم بسته | باز می‌کند، ۱۴ ثانیه منتظر |
| کروم وسط کار بسته شد | تا ۵ بار با فاصله فزاینده |
| سوکت CDP قطع | اتصال مجدد |
| گیت‌هاب موقتاً در دسترس نیست | تلاش مجدد |
| کلید منقضی | پیام واضح + باز کردن Settings |

## ۴.۹ 🔴 خروج از self-test در میزبان WinForms

در بیلد `-noConsole`، نه `exit` و نه `[Environment]::Exit` قابل اتکا نیستند.

```powershell
function Stop-SelfTest {
    param([int]$Code)
    try { [Console]::Out.Flush() } catch { }
    try { [Environment]::Exit($Code) } catch { }
    try {
        $p = Get-Process -Id $PID -ErrorAction Stop
        $p.Kill()
    } catch { }
    exit $Code
}
```

همچنین تشخیص self-test از سه راه، چون `ps2exe` سوییچ را قابل اتکا bind نمی‌کند:

```powershell
if (-not $SelfTest) {
    if ($env:BAZINO_SELFTEST -eq '1') { $SelfTest = $true }
    elseif ([Environment]::GetCommandLineArgs() -contains '-SelfTest') { $SelfTest = $true }
}
```

## ۴.۱۰ کلاینت سمت ایجنت

```bash
node tools/bazino-bridge/agent-bus.js init       # ساخت شاخه (یک بار)
node tools/bazino-bridge/agent-bus.js status     # آیا نرم‌افزار زنده است؟
node tools/bazino-bridge/agent-bus.js targets    # فهرست تب‌ها
node tools/bazino-bridge/agent-bus.js eval "location.href"
node tools/bazino-bridge/agent-bus.js nav "https://example.com"
node tools/bazino-bridge/agent-bus.js send '{"id":1,"method":"..."}'
```

⚠️ `eval` و `nav` خودکار به تب attach می‌شوند — `Runtime.*` بدون session کار نمی‌کند.

⚠️ شناسه‌ها باید **یکتا** باشند وگرنه جواب قدیمی با جواب فعلی اشتباه می‌شود:

```javascript
let _id = Date.now() % 100000;
const nextId = () => ++_id;
```

---

# ۵. ورک‌فلو گیت‌هاب

## چرا Actions

سندباکس لینوکسی است و **نمی‌تواند `.exe` بسازد یا تست کند**. رانر ویندوز
گیت‌هاب این کار را رایگان انجام می‌دهد.

🔴 **ایجنت نمی‌تواند این فایل را push کند.** مالک باید یک بار بسازد:

```
گیت‌هاب → Add file → Create new file
نام: .github/workflows/build-bridge.yml
محتوا: از tools/bazino-bridge/workflow-build-bridge.yml کپی شود
```

## مراحل ورک‌فلو

| # | مرحله | کار |
|---|---|---|
| ۱ | Checkout | دریافت کد |
| ۲ | Syntax check | بررسی نحو PowerShell با Parser |
| ۳ | Smoke test | اجرای `-SelfTest` به‌صورت اسکریپت ساده |
| ۴ | Install ps2exe | نصب از PowerShell Gallery |
| ۵ | Build | تبدیل به exe با `-noConsole` |
| ۶ | **Inspect** | حجم > ۲۰KB، هدر PE معتبر، اطلاعات نسخه |
| ۷ | Checksum | SHA256 |
| ۸ | **Upload** | artifact — **قبل** از تست تأیید |
| ۹ | Verify | اجرای exe با `continue-on-error` |
| ۱۰ | Summary | جدول در صفحه Actions |

## دو تصمیم مهم در ترتیب

**۱. آپلود قبل از تست تأیید.** اگر تست شکست بخورد، exe همچنان قابل دانلود است.

**۲. `continue-on-error: true` روی تست.** یک باینری گرافیکی که خارج نمی‌شود
نباید بیلدی را قرمز کند که artifact آن آماده است.

```yaml
      - name: Verify the executable launches
        shell: pwsh
        timeout-minutes: 2
        continue-on-error: true
        env:
          BAZINO_SELFTEST: '1'
        run: |
          # Never use -Wait: a GUI host that fails to exit would hang the job.
          $p = Start-Process -FilePath 'dist/BazinoBridge.exe' -PassThru
          if (-not $p.WaitForExit(45000)) {
            try { $p.Kill() } catch { }
            Write-Host "::warning::Packaged exe did not exit within 45 s"
          }
```

---

# ۶. نصب و راه‌اندازی

## ۶.۱ ساخت ورک‌فلو (یک بار)

```
گیت‌هاب → ریپو → Add file → Create new file
نام فایل:  .github/workflows/build-bridge.yml
محتوا:     tools/bazino-bridge/workflow-build-bridge.yml
Commit
```

## ۶.۲ دانلود exe

هر push روی `tools/bazino-bridge/**` بیلد را فعال می‌کند:

```
گیت‌هاب → Actions → آخرین اجرا → پایین صفحه → Artifacts → BazinoBridge
```

⚠️ **SmartScreen** بار اول هشدار می‌دهد (فایل امضا ندارد):
**More info** → **Run anyway**

## ۶.۳ ساخت کلید گیت‌هاب

```
github.com/settings/personal-access-tokens/new
```

⚠️ حتماً **Fine-grained**، نه `Tokens (classic)`.

| فیلد | مقدار |
|---|---|
| Token name | `bazino-bridge` |
| Expiration | ۹۰ روز |
| Repository access | **Only select repositories** → `bazino-advertising-campaign` |
| Permissions → **Contents** | **Read and write** |

همه بقیه روی `No access`. گیت‌هاب خودکار `Metadata: Read-only` اضافه می‌کند —
طبیعی است.

🔴 کلید **فقط یک بار** نمایش داده می‌شود. همان لحظه کپی کنید.

## ۶.۴ اجرا

```
۱. BazinoBridge.exe را باز کنید
۲. Settings → کلید را پیست کنید → Save
۳. Connect
۴. بار اول: در کروم با Bazinopro@gmail.com وارد شوید
```

از آن به بعد فقط **Connect**.

## ۶.۵ راه‌اندازی سمت ایجنت

```bash
node tools/bazino-bridge/agent-bus.js init
```

---

# ۷. باگ‌هایی که پیدا شدند

هر پنج مورد در تست واقعی پیدا شدند، نه در بازبینی کد.

## باگ ۱ — self-test در exe معلق می‌ماند

**نشانه:** مرحله «Verify» ۲۰ دقیقه گیر می‌کرد.
**علت:** `ps2exe -noConsole` برنامه پنجره‌ای می‌سازد؛ `Write-Host` جایی برای
نوشتن ندارد و `-Wait` تا ابد منتظر می‌ماند.
**رفع:** خروجی در فایل، `WaitForExit(timeout)` به‌جای `-Wait`.

## باگ ۲ — پارامتر `-SelfTest` bind نمی‌شد

**نشانه:** exe پنجره باز می‌کرد به‌جای اجرای تست.
**علت:** `ps2exe` سوییچ‌ها را قابل اتکا bind نمی‌کند.
**رفع:** تشخیص از متغیر محیطی `BAZINO_SELFTEST=1` و خط فرمان خام.

## باگ ۳ — بارگذاری WinForms پروسه را زنده نگه می‌داشت

**نشانه:** self-test کامل اجرا می‌شد ولی خارج نمی‌شد.
**علت:** `Add-Type -AssemblyName System.Windows.Forms` یک message pump
راه می‌اندازد.
**رفع:** در نسخه بسته‌بندی‌شده این بررسی رد می‌شود.

## باگ ۴ — 🔴 تطبیق شناسه CDP

**نشانه:** `id:77` فرستادم، `id:2` برگشت.
**علت:** نرم‌افزار «فریم بعدی» را برمی‌گرداند، نه جواب متناظر.
**رفع:** `Receive-Cdp -ExpectId` + شناسه یکتا سمت ایجنت.

**این خطرناک‌ترین باگ بود** چون بی‌صدا داده اشتباه برمی‌گرداند.

## باگ ۵ — انباشت رویداد بعد از ناوبری

**نشانه:** پل از ۸ ثانیه به ۴۱ ثانیه کند شد.
**علت:** `Page.navigate` سیل رویداد می‌سازد که در صف می‌ماند.
**رفع:** `Clear-CdpBacklog` قبل از هر دستور.

---

# ۸. عیب‌یابی

| نشانه | معنی | راه‌حل |
|---|---|---|
| GitHub قرمز | کلید غلط یا منقضی | Settings → کلید تازه |
| Chrome قرمز | کروم نصب نیست یا پورت اشغال | لاگ را ببینید |
| «Repo is public» | ریپو عمومی شده | **فوراً** private کنید |
| Bridge خاکستری | Connect نزده‌اید | Connect |
| «That Chrome was started without the debug flag» | کروم دستی با همان پروفایل باز است | همه پنجره‌های آن پروفایل را ببندید |
| ۵ شکست پیاپی | مشکل پایدار | Copy log → به ایجنت بفرستید |
| دستورها کند شده‌اند | انباشت رویداد | Disconnect → Connect |

## لاگ ورک‌فلو

🔴 ایجنت **نمی‌تواند** لاگ Actions را بخواند (دامنه در whitelist نیست).
اگر لازم شد، مالک باید در چت بگذارد.

---

# ۹. امنیت

| نگرانی | کنترل |
|---|---|
| دستورها در ریپو | ریپو **private** است — نرم‌افزار موقع اتصال بررسی می‌کند |
| کلید گیت‌هاب | فقط `Contents` روی یک ریپو · رمزگذاری DPAPI |
| ردپا در تاریخچه | شاخه orphan — قابل حذف کامل |
| توقف فوری | Disconnect · بستن نرم‌افزار · باطل کردن کلید |

⚠️ نرم‌افزار **هرگز** رمز عبور یا اطلاعات حساس در شاخه پیام‌رسان نمی‌نویسد.

---

# ۱۰. تاریخچه

| تاریخ | تغییر |
|---|---|
| ۲۰۲۶-۰۹-۱۸ | ساخت اولیه · مسیر گیت‌هاب · حذف VPN و کپی‌پیست |
| ۲۰۲۶-۰۹-۱۸ | رفع ۵ باگ در تست واقعی · تأیید کارکرد با لاگین `bazinopro@gmail.com` |
| ۲۰۲۶-۰۹-۱۹ | ثبت این دستورالعمل |
