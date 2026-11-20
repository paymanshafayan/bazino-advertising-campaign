# راهنمای جامع و عملیاتی تولید ویدیو روی سرور اختصاصی RTX 5090 (`RTX5090_SERVER_PRODUCTION_GUIDE.md`)

> **مسیر سند:** `C:\projects\Antigravity Bazino\Publish-Studio\RTX5090_SERVER_PRODUCTION_GUIDE.md`
> **آخرین به‌روزرسانی:** ۲۰۲۶-۱۱-۱۸ (به‌روزرسانی زنده هم‌زمان با تولید ویدیوی ۵ صحنه‌ای VistaMare / BAZINO)
> **هدف سند:** حذف کامل آزمون و خطا در جلسات بعدی و اجرای فوری، بدون توقف و ۱۰۰٪ استاندارد تولید ویدیو روی سرور `RTX 5090`.

---

## ۱. مشخصات اتصال و توپولوژی فعلی سرور RTX 5090

| پارامتر | مقدار دقیق و تست‌شده |
|---|---|
| **آدرس سرور (IP جدید)** | `root@194.93.48.125` (پورت `22`) |
| **کلید خصوصی SSH روی ویندوز** | `C:\Users\payman\.ssh\id_ed25519` |
| **کارت گرافیک و منابع** | `NVIDIA GeForce RTX 5090` (`32,607 MiB VRAM`، `88 GB System RAM`، `CUDA 13.0 / PyTorch 2.13.0+cu130`) |
| **موتور اصلی تولید ویدیو و صدا** | **FreeVideo (`MiniMax H3 Ref2VA`)** در مسیر `/root/FreeVideo` |
| **پایتون و باینری صحیح موتور** | `/root/FreeVideo/envs/unified/bin/python` و `/root/FreeVideo/freevideo` *(مسیر قدیمی `venv/bin` وجود ندارد)* |
| **وزن‌های نصب‌شدهٔ MiniMax H3** | Base: `/root/FreeVideo/models/vdn/h3-base` · Checkpoint: `/root/FreeVideo/models/vdn/stage-dmd-step-250` · Encoder: `qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors` |
| **پشتهٔ مکمل جایگزینی کاراکتر** | **Openjutsu** در مسیر `/root/Openjutsu` و اسکریپت `/root/Openjutsu-scripts/run-swap.py` |
| **پوشهٔ کاری استاندارد هر پروژه** | `/root/freevideo-work-<task_name>/` (شامل `refs/`، `out/`، `media.json`، `prompt.txt`) |

> ⚠️ **تغییر معماری مهم نسبت به هندآف قدیمی:**
> روی این سرور دیگر سرویس قدیمی `kling_gateway.py` (پورت `8000` و مدل‌های Wan2.2 / Hunyuan) اجرا نمی‌شود؛ کل زیرساخت به موتور بسیار پیشرفته‌تر **FreeVideo (`MiniMax H3 Ref2VA` با تولید هم‌زمان تصویر، گفتار و افکت صوتی)** و **Openjutsu** ارتقا یافته است.

---

## ۲. درس‌های طلایی از آزمون و خطاها (ممنوعیت تکرار خطاها)

### خطای ۱: قفل شدن `scp`، پایپ `stdin` و پکت‌های بزرگ SSH از ویندوز به سرور
* **علت فنی:** در مسیر شبکه بین ویندوز محلی و `194.93.48.125`، دستور `scp` و ارسال داده از طریق `stdin` در OpenSSH ویندوز روی EOF قفل می‌شود. همچنین ارسال رشته‌های آرگومان بزرگ‌تر از `12KB–28KB` در یک دستور SSH به دلیل محدودیت MTU/TCP Window باعث Timeout می‌شود و باز کردن ده‌ها نشست SSH بدون وقفه به محدودیت امنیتی `MaxStartups` (کد خروجی `255`) برخورد می‌کند.
* **راه‌حل قطعی و تست‌شده (Fast 3KB-Chunk Uploader):**
  1. همیشه در `subprocess.run` پارامتر `stdin=subprocess.DEVNULL` را قرار دهید.
  2. تصاویر مرجع را پیش از آپلود با `PIL` به ابعاد استاندارد مرجع (`512x686` یا `640x896` با کیفیت JPEG `75` تا `82` — حجم حدود `25KB` تا `50KB`) تغییر سایز دهید.
  3. هر فایل را به رشته‌های Base64 با طول حداکثر **`3000` کاراکتر** تقسیم کرده و با وقفهٔ `0.3s` ارسال کنید.
  4. **صحت‌سنجی پیکسلی اجباری روی سرور:** هرگز به `Image.open(p).verify()` اکتفا نکنید (چون فقط هدر JPEG را چک می‌کند و فایل ناقص را تشخیص نمی‌دهد)؛ حتماً `Image.open(p).load()` را روی سرور اجرا کنید تا از سلامت ۱۰۰٪ بایت‌های تصویر مطمئن شوید.

### خطای ۲: نگه‌داشتن نشست SSH توسط `nohup` و اجرای تکراری در حلقهٔ Retry
* **علت فنی:** وقتی دستور `nohup ... > generate.log 2>&1 &` به همراه `sleep` در انتهای یک اسکریپت دارای حلقهٔ Retry اجرا شود، اگر نشست SSH به دلیل باز ماندن استریم خروجی تایم‌اوت بخورد، حلقهٔ Retry دستور را دوباره اجرا می‌کند (که به قفل `ENGINE_BUSY` می‌خورد).
* **راه‌حل قطعی:**
  * دستور استارت موتور را **بدون حلقهٔ Retry** و با بستن کامل استریم‌ها (`> generate.log 2>&1 < /dev/null &`) یا از طریق `python -c "import subprocess; subprocess.Popen(..., start_new_session=True)"` اجرا کنید تا نشست SSH در کسری از ثانیه و بلافاصله پس از ثبت PID آزاد شود.

### خطای ۳: پرچم‌های منسوخ در برابر دستور استاندارد `freevideo_engine.managed`
* **علت فنی:** پرچم‌های قدیمی مانند `--media-json`، `--steps`، `--cfg`، `--fps`، `--output-dir` دیگر وجود ندارند. همچنین اجرای خام `./freevideo generate` نیازمند وارد کردن دستی `--cache`، `--base` و `--checkpoint` است.
* **راه‌حل قطعی:** همیشه از ماژول مدیریت‌شدهٔ داخلی موتور (`freevideo_engine.managed --root /root/FreeVideo generate`) استفاده کنید که تمام مسیرهای کش و مدل‌ها را خودکار تنظیم می‌کند.

### خطای ۴: گیومه (`"..."`) دور دیالوگ‌ها در پرامپت MiniMax H3
* **علت فنی:** در موتور `MiniMax H3`، هر متنی که داخل علامت نقل‌قول (`"..."`) قرار بگیرد به عنوان **متن نوشتاری روی تصویر (On-Screen Text / Sign)** تفسیر شده و روی ویدیو حک می‌شود! (تنها استثنا زمانی است که واقعاً می‌خواهید تابلوی روی دیوار مثل `"BAZINO"` یا `"GTA VI"` دیده شود).
* **راه‌حل قطعی:**
  1. دیالوگ‌های گفتاری را **منحصراً** داخل تگ `<d>[Language] ...</d>` و **بدون هیچ علامت گیومه‌ای** بنویسید؛ مثال:
     `<Subject 1> (S1), speaking in a fierce, commanding young female voice, shouts: <d>[Turkish] Kimse yerinden kıpırdamasın!</d>`
  2. در انتهای بخش `detailed_description` همیشه عبارت زیر را بنویسید:
     `No subtitles, no on-screen text, no watermarks.`

### خطای ۵: ویرایش تصاویر روی ویندوز و آپلود مجدد آن‌ها به سرور (ممنوعیت مطلق آپلود تکراری)
* **علت فنی:** باز کردن ده‌ها نشست SSH پیاپی برای آپلود تصاویر ویرایش‌شده از ویندوز به سرور، به محدودیت فایروال SSH سرور (`MaxStartups`) برخورد کرده و باعث قفل شدن اسکریپت می‌شود.
* **راه‌حل قطعی (ویرایش سمت سرور در ۰.۱ ثانیه + استریم تک‌نشستی):**
  1. از آنجا که پایتون (`/root/FreeVideo/envs/unified/bin/python`)، کتابخانهٔ `PIL` (`Pillow`) و `ffmpeg` روی خود سرور ۵۰۹۰ نصب هستند و تصاویر پایه در `/root/freevideo-work-heist/refs/` قرار دارند، **هرگونه ویرایش تصویر، کراپ، افزودن تابلوی نئونی `BAZINO` یا درج تصویر `GTA VI` روی تلویزیون باید مستقیماً با اجرای یک اسکریپت پایتون داخل خود سرور انجام شود (صفر بایت آپلود از ویندوز!).**
  2. در مواقعی که واقعاً نیاز به ارسال فایل جدید از ویندوز است، به‌جای باز کردن ده‌ها نشست SSH جداگانه در حلقه، کل فایل‌های متنی/پرامپت‌ها را در یک فایل `.tar.gz` فشرده کرده و با وقفهٔ امن یا از طریق دانلود مستقیم توسط خود سرور منتقل کنید.
  3. **جلوگیری از نشستن برعکس کاراکترها روی مبل:** وقتی قرار است کاراکترها در یک صحنه از پشت سر (رو به تلویزیون و پشت به دوربین) دیده شوند، **هرگز عکس پرترهٔ تمام‌رخ صورت آن‌ها را در `media.json` آن صحنه قرار ندهید**؛ چون مدل برای نمایش چهرهٔ رفرنس، آن‌ها را پشت به تلویزیون و رو به دوربین می‌نشاند! عکس صورت کاراکترها را فقط در صحنه‌ای بدهید که سرشان را به سمت دوربین برمی‌گردانند.
  4. **جلوگیری از صدای کفش پاشنه‌دار روی سنگ مرمر:** مدل در فضاهای لابی مرمری به‌طور پیش‌فرض صدای کفش پاشنه‌دار تولید می‌کند؛ همیشه در `overall_soundscape` عبارت `soft, muffled rubber-soled sneaker footsteps on marble (strictly no high heels or clicking shoes)` را بنویسید.

### قانون ۶: ممنوعیت مطلق اجرای کارهای `FFmpeg` روی سرور (مونتاژ و چسباندن منحصراً روی لوکال)
* **علت فنی و اقتصادی:** سرور `RTX 5090` اختصاصاً برای رندر و تولید خام ویدیو با GPU است. انجام کارهای `ffmpeg` (مانند چسباندن ویدیوها، کراپ، زیرنویس یا تبدیل فرمت) روی سرور فقط هزینهٔ زمانی و سرور را بالا می‌برد و زمان تولید را طولانی‌تر می‌کند.
* **قانون قطعی:** هیچ‌گونه پردازش یا مونتاژ با `ffmpeg` (از جمله `concat` و چسباندن صحنه‌ها) **هرگز نباید روی سرور ۵۰۹۰ انجام شود**. سرور فقط فایل‌های خام هر صحنه را تحویل می‌دهد و تمام عملیات `ffmpeg` باید **منحصراً روی سیستم لوکال (ویندوز)** انجام شود.

### قانون ۷: ممنوعیت مطلق توصیف متنی جزئیات تصاویر رفرنس در پرامپت (تأکید صرف بر تطابق ۱۰۰٪ با رفرنس)
* **علت فنی:** وقتی تصویر رفرنس به مدل داده می‌شود، اگر در پرامپت متنی دوباره جزئیات صحنه، ترکیب افراد، لباس‌ها یا محتوای داخل تلویزیون‌ها را توصیف کنید (مثلاً بنویسید «سه زوج در خیابان» یا «تصویر بازی GTA 6 روی تلویزیون»)، موتور ساخت ویدیو به جای بازسازی دقیق پیکسل‌ها و چیدمان تصویر رفرنس، از توصیف متنی شما استفاده کرده و صحنه، جای افراد یا تصویر روی تلویزیون را از خودش می‌سازد!
* **قانون قطعی:** هنگام دادن تصویر رفرنس، **هرگز جزئیات صحنه یا محتوای نمایشگرها را در پرامپت توصیف نکنید**. فقط تأکید کنید که ۱۰۰٪ جزئیات، زاویه دوربین، جایگاه افراد و تصاویر روی نمایشگرها دقیقاً منطبق با تصویر رفرنس حفظ شود و صرفاً حرکت/انتقال از تصویر رفرنس اول به تصویر رفرنس دوم (و دیالوگ دقیقه/ثانیه دقیق) بیان گردد.


---

## ۳. الگوی آماده و کپی‌پیست آپلود و استارت فوری (Zero-Friction Script)

برای هر پروژهٔ جدید، کافی است اسکریپت پایتون زیر را با تصاویر و پرامپت جدید اجرا کنید:

```python
import base64, json, os, subprocess, time
from PIL import Image

KEY = r"C:\Users\payman\.ssh\id_ed25519"
HOST = "root@194.93.48.125"
REMOTE_WORK = "/root/freevideo-work-heist"

def ssh_run(cmd, timeout=15):
    for attempt in range(4):
        try:
            r = subprocess.run(
                ["ssh", "-i", KEY, "-o", "StrictHostKeyChecking=no", HOST, cmd],
                stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=timeout
            )
            if r.returncode == 0:
                return r.stdout
        except subprocess.TimeoutExpired:
            pass
        time.sleep(1.5)
    raise RuntimeError(f"SSH failed: {cmd[:120]}")

def upload_bytes(raw_bytes, remote_path):
    b64 = base64.b64encode(raw_bytes).decode("ascii")
    tmp_b64 = f"/tmp/{os.path.basename(remote_path)}.b64"
    ssh_run(f"rm -f {tmp_b64}")
    chunk_size = 3000
    for i in range(0, len(b64), chunk_size):
        chunk = b64[i:i+chunk_size]
        ssh_run(f"printf '%s' '{chunk}' >> {tmp_b64}")
        time.sleep(0.25)
    ssh_run(f"base64 -d {tmp_b64} > {remote_path} && rm -f {tmp_b64}")
```

### دستور استاندارد استارت تولید روی سرور (Canonical Launch):
```bash
cd /root/freevideo-work-<task> && mkdir -p out &&
PYTHONPATH=/root/FreeVideo nohup /root/FreeVideo/envs/unified/bin/python -B -m freevideo_engine.managed \
  --root /root/FreeVideo generate \
  --prompt-file prompt.txt --media media.json --task ref2va \
  --out out/video.mp4 --width 768 --height 1344 --seconds 15 \
  --seed 20261118 --two-pass --upscale-factor 1.5 \
  > generate.log 2>&1 < /dev/null &
```

---

## ۴. قرارداد دقیق `media.json` و `prompt.txt` (فرمت ۶ بخشی `Ref2VA`)

### الف) ساختار `media.json`
```json
{
  "version": 1,
  "first": null,
  "last": null,
  "references": [
    {"path": "refs/lucia.jpg", "kind": "image"},
    {"path": "refs/jason.jpg", "kind": "image"},
    {"path": "refs/vm_lounge_1.jpg", "kind": "image"}
  ],
  "loras": []
}
```
* ترتیب فایل‌های `kind: "image"` در آرایهٔ `references` مستقیماً به `<Picture 1>`، `<Picture 2>`، `<Picture 3>` و ... در پرامپت نگاشت می‌شود.
* هرگز `first`/`last` را هم‌زمان با `references` پر نکنید.

### ب) ساختار ۶ بخشی اجباری `prompt.txt` برای `ref2va`
هر بخش باید در خط جدید با `نام_بخش:` شروع شود و با **یک خط خالی** از بخش بعدی جدا گردد:
1. `subject_definitions:` معرفی هر `<Subject N>` و ارتباط آن با `<Picture N>` (برای کاراکترها تصریح کنید: `Only her/his appearance is used from <Picture N>; the background of <Picture N> is ignored.`).
2. `summary:` شروع با `[reference generation]` و خلاصهٔ یک‌پاراگرافی کل ویدیو با تگ‌های `<Subject N>`.
3. `retention_analysis:` یک خط به ازای هر `<Subject N>` با نشانگر `fully_preserved` و ذکر شات‌های حضور.
4. `detailed_description:` شروع با `[Shot 1]` و برش‌های بعدی با `[Shot 2] At MM:SS.mmm, ...`؛ دیالوگ‌ها با `(S1)` و `<d>[Turkish] ...</d>`؛ پایان با `No subtitles, no on-screen text, no watermarks.`
5. `overall_soundscape:` توصیف ۱ تا ۴ جمله‌ای صداهای محیطی، قدم‌ها، نفس کشیدن و خنده (بدون تکرار متن دیالوگ).
6. `non_diegetic_music:` مقدار `N/A` (تا موسیقی پرش‌دار تولید نشود و موسیقی یکپارچه در Google Vids یا پس‌پردازش اضافه گردد).

---

## ۵. پایش زنده (Live Monitoring) و زمان‌بندی واقعی روی RTX 5090

برای مشاهدهٔ پیشرفت دقیق گام‌های نمونه‌برداری (`11` گام در حالت `two-pass`: شامل `8` گام پاس اول در رزولوشن `512x896` + آپ‌اسکیل نهفته `1.5x` + `3` گام پالایش در رزولوشن `768x1344`):

```bash
ssh -i "C:\Users\payman\.ssh\id_ed25519" root@194.93.48.125 "tail -n 15 /root/freevideo-work-heist/out/vistamare-heist.engine.log"
```

### رویدادهای کلیدی در `<out>.engine.log`:
1. `encoding_start` -> انکود کردن متن و ۱۱ تصویر مرجع با `Qwen3-VL 32B` و `H3 VAE`.
2. `prepared_blocks (1..50)` -> بارگذاری ۵۰ بلوک مدل `VDN-H3` در VRAM/RAM (حدود ۱۰ ثانیه).
3. `sampling_progress` -> نمایش `step` فعلی از `total: 11`، شماره `block` از `50` و `estimated_step_seconds`.
4. `decode_phase` -> شامل `Loading video VAE` -> `Decoding the video` -> `Decoding audio` -> `Saving MP4`.

---

## ۶. الگوی تست‌شدهٔ دانلود سریع و بدون خطای ویدیو از سرور (`dd + base64` با تایید `SHA-256`)

از آنجا که `scp` در مسیر ویندوز به سرور قفل می‌شود، اما استریم خروجی (`stdout`) دستور `ssh` از سرور به ویندوز با سرعت بالا کار می‌کند، بهترین و مطمئن‌ترین روش دانلود فایل‌های `.mp4`، خواندن بلوک‌های `512KB` با `dd` و تایید نهایی هش `SHA-256` است:

```python
import base64, hashlib, subprocess, time

KEY = r"C:\Users\payman\.ssh\id_ed25519"
HOST = "root@194.93.48.125"
REMOTE_MP4 = "/root/freevideo-work-heist/out/vistamare-heist.mp4"
LOCAL_MP4 = r"C:\Jinus-Media\vistamare-heist-raw.mp4"

def ssh_out(cmd, timeout=60):
    r = subprocess.run(
        ["ssh", "-i", KEY, "-o", "StrictHostKeyChecking=no", HOST, cmd],
        stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=timeout
    )
    return r.returncode, r.stdout, r.stderr

code, out, _ = ssh_out(f"sha256sum {REMOTE_MP4}; stat -c '%s' {REMOTE_MP4}")
lines = [x.strip() for x in out.strip().splitlines() if x.strip()]
remote_sha = lines[0].split()[0]
total_size = int(lines[1])

chunk_bytes = 524288  # 512 KB per block
received = bytearray()
block_idx = 0
while len(received) < total_size:
    code, b64_out, _ = ssh_out(
        f"dd if={REMOTE_MP4} bs={chunk_bytes} skip={block_idx} count=1 status=none | base64 -w 0",
        timeout=45
    )
    if code != 0 or not b64_out.strip():
        time.sleep(2)
        continue
    received.extend(base64.b64decode(b64_out.strip()))
    block_idx += 1

open(LOCAL_MP4, "wb").write(received)
assert hashlib.sha256(received).hexdigest() == remote_sha, "SHA256 MISMATCH!"
```

---

## ۷. آمار واقعی اجرا و نتایج رندر پروژهٔ ۵ صحنه‌ای VistaMare / BAZINO

* **پوشهٔ کاری سرور:** `/root/freevideo-work-heist/`
* **تصاویر مرجع تاییدشده (`11/11 VALID` با `Image.load()`):**
  `lucia.jpg`, `jason.jpg`, `michael.jpg`, `franklin.jpg`, `trevor.jpg`, `vm_exterior.jpg`, `vm_lobby.jpg`, `vm_corridor.jpg`, `vm_entrance.jpg`, `vm_lounge_1.jpg`, `vm_lounge_2.jpg`
* **مشخصات رندر:** `768x1344` عمودی (`9:16`)، مدت `15s`، دوپاسه (`8` گام پایه در `512x896` + آپ‌اسکیل نهفته `1.5x` + `3` گام پالایش در `768x1344` = مجموعاً `11` گام)، بذر `20261118`.
* **زمان واقعی اجرا روی RTX 5090:**
  * بارگذاری ۵۰ بلوک مدل و Pin حافظه: `~10.2 ثانیه`
  * ۸ گام پاس اول (`512x896`): `~120 ثانیه` (حدود ۱۵ ثانیه به ازای هر گام)
  * ۳ گام پالایش پاس دوم (`768x1344`): `~92 ثانیه` (حدود ۲۸ تا ۳۵ ثانیه به ازای هر گام در توان `575W`)
  * دیکود `Video VAE` + `Audio VAE` و ذخیرهٔ MP4: `~25 ثانیه`
  * **کل زمان تولید ویدیوی ۱۵ ثانیه‌ای ۵ شاته با ۱۱ رفرنس:** **حدود ۴ دقیقه و ۱۰ ثانیه!**
* **مشخصات فایل دانلودشده:**
  * مسیر محلی: `C:\Jinus-Media\vistamare-heist-raw.mp4`
  * حجم دقیق: `7,047,193 bytes` (`~7.05 MB`)
  * هش تاییدشده (`SHA-256`): `a21664ebc72b4981357653f4f71336ae148d5e550fbbdf02507811699ce0f5b3` (تطابق ۱۰۰٪ با سرور)

---

## ۸. کنترل کیفیت بصری و صوتی (QC) و پکیج نهایی انتشار

1. **کنترل کیفیت فریم‌به‌فریم هر ۵ شات (`C:\Jinus-Media\heist_qc_frames\`):**
   * **شات ۱ (`00:00–00:03` — `frame_01.0s.jpg` و `frame_02.5s.jpg`):** توقف خودروی اسپرت کروک سفید دقیقاً مقابل نمای شیشه‌ای هتل VistaMare و پیاده شدن لوسیا و جیسون با اسلحهٔ کمری.
   * **شات ۲ (`00:03–00:06` — `frame_04.5s.jpg`):** حرکت سریع لوسیا و جیسون در راهروی مرمری سفید VistaMare به سمت درب شیشه‌ای تیرهٔ گیم‌نت BAZINO.
   * **شات ۳ (`00:06–00:09` — `frame_07.5s.jpg`):** ورود ناگهانی به داخل سالن گیم‌نت، نشانه رفتن اسلحه و فریاد لوسیا به زبان ترکی استانبولی (`Kimse yerinden kıpırdamasın!`) بدون هیچ‌گونه متن سوخته‌شده یا واترمارک روی تصویر.
   * **شات ۴ (`00:09–00:12` — `frame_10.5s.jpg`):** چرخیدن سر ۳ گیمر نشسته روی مبل چرمی مشکی مقابل مانیتور بازی GTA 6 و نمایان شدن چهرهٔ واقعی **مایکل، فرانکلین و ترور** با دستهٔ پلی‌استیشن در دست و بهت و تعجب طرفین.
   * **شات ۵ (`00:12–00:15` — `frame_13.5s.jpg`):** خندهٔ دسته‌جمعی هر ۵ قهرمان GTA 5 و GTA 6 و در آغوش گرفتن صمیمانهٔ یکدیگر در سالن نئونی BAZINO.
2. **کنترل کیفیت صدا (`ffmpeg volumedetect`):**
   * ترک صوتی استریو AAC با `964,800` سمپل، میانگین بلندی صدای `-14.6 dB` و پیک `0.0 dB` (کاملاً زنده و استاندارد).
3. **پکیج نهایی آمادهٔ انتشار (`status: "ready"`):**
   * نسخهٔ زیرنویس‌دار ترکی (`1080x1920`): `C:\projects\Antigravity Bazino\Publish-Studio\Ready-Media\reel-gta-vistamare-heist\reel-gta-vistamare-heist.mp4`
   * نسخهٔ خام بدون زیرنویس (`768x1344`): `C:\projects\Antigravity Bazino\Publish-Studio\Ready-Media\reel-gta-vistamare-heist\reel-gta-vistamare-heist-clean.mp4`
   * کپشن ترکی استانبولی و متادیتا: `caption.txt` و `metadata.json` در همان پوشه + کارت صف انتشار در `C:\projects\Antigravity Bazino\marketing-app-mailbox\publish-queue\ready\reel-gta-vistamare-heist.json`.

