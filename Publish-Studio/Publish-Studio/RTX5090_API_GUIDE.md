# راهنمای کامل API سرور RTX 5090 (`FreeVideo / MiniMax H3 Ref2VA`)

> **مسیر سند:** `C:\projects\Antigravity Bazino\Publish-Studio\RTX5090_API_GUIDE.md`
> **آدرس API روی سرور:** `http://194.93.48.125:8000`
> **سرویس سرور:** `freevideo-api.service` (`/root/FreeVideo/api_server.py`)
> **پوشه ابزارها در ویندوز:** `C:\projects\Antigravity Bazino\Publish-Studio\rtx5090-api\`

---

## ۱. فایل‌ها و ساختار پکیج API

| فایل | محل | نقش |
| :--- | :--- | :--- |
| `api_server.py` | سرور (`/root/FreeVideo/api_server.py`) | سرویس `FastAPI` شامل اندپوینت‌های تولید ویدیو، آپلود تکه‌ای، وضعیت، دانلود و وب‌هوک |
| `freevideo-api.service` | سرور (`/etc/systemd/system/freevideo-api.service`) | اجرای دائمی و خودکار API با `uvicorn` روی پورت `8000` |
| `.api_env` | سرور (`/root/FreeVideo/.api_env`) | نگهداری کلید امنیتی `FREEVIDEO_API_KEY` |
| `rtx5090_api_client.py` | ویندوز (`rtx5090-api\rtx5090_api_client.py`) | کلاینت استاندارد ویندوز برای ارسال ۱ تا ۶ تصویر، پایش، دانلود `.mp4` و تایید `SHA-256` |
| `rtx5090_webhook_listener.py` | ویندوز (`rtx5090-api\rtx5090_webhook_listener.py`) | شنونده محلی وب‌هوک روی `http://127.0.0.1:8787/webhook` برای دریافت فوری اعلان‌ها |
| `deploy_to_server.py` | ویندوز (`rtx5090-api\deploy_to_server.py`) | اسکریپت استقرار امن با تکه‌های `3KB` روی سرور از طریق SSH |
| `.api_config.json` | ویندوز (`rtx5090-api\.api_config.json`) | ذخیره آدرس سرور و کلید API (`base_url` و `api_key`) |

---

## ۲. چرا این API مشکل محدودیت حجم آپلود و دانلود را برطرف می‌کند؟

1. **حل مشکل محدودیت `12KB–28KB` و `WinError 10054` در آپلود از ویندوز:**
   - کلاینت `rtx5090_api_client.py` تصاویر را در تکه‌های امن **`4KB`** با پارامتر دقیق `offset` (`POST /jobs/{job_id}/chunk`) ارسال می‌کند و تا ۶ بار در صورت خطای شبکه با حفظ بایت‌های قبلی تلاش مجدد (`Retry`) انجام می‌دهد.
2. **حل مشکل پروکسی ویندوز (`socks5h`):**
   - کلاینت از `session.trust_env = False` استفاده می‌کند تا مستقیماً و بدون عبور از پروکسی سیستم به `194.93.48.125:8000` متصل شود.
3. **حل مشکل قطعی دانلود و اطمینان از سلامت فایل:**
   - فایل `.mp4` به صورت استریم (`512KB` به `512KB`) دانلود شده و هش `SHA-256` آن با هدر `X-File-SHA256` سرور تطبیق داده می‌شود. در صورت نیاز به دریافت مجدد بدون رندر دوباره، اندپوینت `GET /jobs/{job_id}/video` در دسترس است.
4. **ممنوعیت مطلق اجرای `FFmpeg` روی سرور:**
   - سرور `RTX 5090` صرفاً برای تولید خام هر صحنه استفاده می‌شود. هیچ‌گونه عملیات `ffmpeg` (مانند چسباندن صحنه‌ها، زیرنویس یا تغییر فرمت) روی سرور انجام نمی‌شود و تمام کارهای `ffmpeg` باید **فقط روی سیستم لوکال (ویندوز)** اجرا گردند.

---

## ۳. احراز هویت (Authentication)

تمامی اندپوینت‌ها (به جز مسیر دریافت وب‌هوک محلی) نیازمند ارسال کلید موجود در `.api_config.json` از طریق یکی از دو هدر زیر هستند:
- `X-API-Key: <API_KEY>`
- یا `Authorization: Bearer <API_KEY>`

---

## ۴. مستندات اندپوینت‌های سرور

### الف) `GET /health`
بررسی سلامت سرویس، آزاد یا مشغول بودن کارت گرافیک و مصرف VRAM.
- **خروجی نمونه:**
  ```json
  {
    "status": "ok",
    "busy": false,
    "current_job": null,
    "gpu": "NVIDIA GeForce RTX 5090, 1011 MiB, 32607 MiB, 0 %"
  }
  ```

### ب) `POST /generate` (تک‌مرحله‌ای با تحویل مستقیم MP4)
مناسب برای کلاینت‌ها یا شبکه‌هایی که محدودیت بسته یکجا ندارند (`multipart/form-data`).
- **ورودی‌ها:** `images` (۱ تا ۶ فایل)، `prompt` (۶ بخشی `Ref2VA` یا متن ساده)، `width` (`768`)، `height` (`1344`)، `seconds` (`10`)، `seed` (`0` = تصادفی)، `two_pass` (`true`)، `upscale_factor` (`1.5`)، `webhook_url` (اختیاری).
- **خروجی:** فایل مستقیم `video/mp4` با هدرهای `X-Job-Id`، `X-File-SHA256`، `X-Generation-Seconds` و `X-Seed`.

### ج) چرخه مقاوم آپلود تکه‌ای و دریافت ویدیو (پیشنهادی برای ویندوز)
1. `POST /jobs/create` -> ایجاد پوشه کار و دریافت `job_id`.
2. `POST /jobs/{job_id}/chunk?name=raw_1&offset=0` -> ارسال تکه‌های `4KB` هر تصویر (`raw_1` تا `raw_6`) و `prompt_raw.txt`.
3. `POST /jobs/{job_id}/start` -> شروع رندر روی GPU (با `num_images`, `width`, `height`, `seconds`, `seed`, `two_pass`, `upscale_factor`, `webhook_url`).
4. `GET /jobs/{job_id}/status` -> دریافت وضعیت (`running` / `completed` / `failed` / `timeout`).
5. `GET /jobs/{job_id}/video` -> دریافت استریمی فایل `.mp4` با تایید `X-File-SHA256`.

---

## ۵. روش استفاده از کلاینت ویندوز (`rtx5090_api_client.py`)

### اجرای خط فرمان (CLI):
```powershell
python "rtx5090-api\rtx5090_api_client.py" `
  --images "img1.jpg" "img2.jpg" `
  --prompt-file "prompt.txt" `
  --seconds 10 `
  --out "C:\Jinus-Media\output.mp4" `
  --webhook-url "http://127.0.0.1:8787/webhook"
```

### دانلود مجدد یک کار تکمیل‌شده بدون رندر دوباره:
```powershell
python "rtx5090-api\rtx5090_api_client.py" --resume-job "20261120_062104_da6b1ad8" --out "C:\Jinus-Media\output.mp4"
```

---

## ۶. سیستم وب‌هوک (Webhook Notification)

سیستم در دو سطح اعلان وب‌هوک (`HTTP POST JSON`) ارسال می‌کند:
1. **وب‌هوک سمت سرور (`video.completed` / `video.failed` / `video.timeout`):**
   - اگر `webhook_url` با آدرس عمومی/قابل‌دسترس برای سرور تنظیم شود، بلافاصله پس از پایان رندر روی GPU فراخوانی می‌شود.
2. **وب‌هوک سمت کلاینت (`video.downloaded`):**
   - بلافاصله پس از اتمام دانلود فایل `.mp4` روی ویندوز و تایید کامل `SHA-256`، کلاینت پیام JSON زیر را به `webhook_url` ارسال می‌کند:
     ```json
     {
       "event": "video.downloaded",
       "job_id": "20261120_062104_da6b1ad8",
       "local_path": "C:\\Jinus-Media\\output.mp4",
       "size_bytes": 4189412,
       "sha256": "...",
       "elapsed_seconds": 188.66
     }
     ```
   - *(اختیاری)* می‌توانید آدرس `webhook_url` را یک‌بار داخل فایل `rtx5090-api\.api_config.json` اضافه کنید تا در تمام اجراها به‌صورت خودکار فعال باشد.

### راه‌اندازی شنونده محلی وب‌هوک در ویندوز:
```powershell
python "rtx5090-api\rtx5090_webhook_listener.py" --host 127.0.0.1 --port 8787
```
تمامی رویدادهای دریافتی در فایل `rtx5090-api\webhook_events.jsonl` ذخیره می‌شوند.

---

## ۷. دستورات مدیریت سرویس روی سرور

```bash
# وضعیت سرویس
systemctl status freevideo-api.service

# مشاهده لاگ زنده
journalctl -u freevideo-api.service -f

# ری‌استارت سرویس
systemctl restart freevideo-api.service
```
