# مستندات و روش کار مرحله ۱: ig-viral (Viral Discovery & Outlier Engineering)

این پوشه حاوی کلیه ابزارها، اسکریپت‌ها و نتایج مرحله اول چرخه تولید محتوای اینستاگرام بازی‌نو (`ig-viral`) است.

---

## ۱. هدف و فلسفه مهارت `ig-viral`
- تولید محتوا بر مبنای فرضیات و حدسیات در اینستاگرام محکوم به شکست است.
- مهارت `ig-viral` با تکیه بر شاخص ریاضی **Outlier Multiple**، پست‌هایی را پیدا می‌کند که عملکرد آن‌ها بسیار فراتر از فالوورهای پیج سازنده بوده است (بیش از ۳ برابر میانگین یا Outlier > 3x).
- این پست‌ها اثبات کرده‌اند که ساختار قلاب (Hook)، سرعت ریتم (Pacing) و زاویه موضوعی آن‌ها الگوریتم و ذهن مخاطب را قفل کرده است.

---

## ۲. ساختار فایل‌های این زیرپوشه
- **`swipe.md`**: بانک سوژه‌های وایرال شامل ۱۰ ریلز برتر تاییدشده با لینک، آمار تعامل، فرمول و سناریوی پیشنهادی برای مونا.
- **`find_outliers.py`**: اسکریپت اصلی تحلیل داده‌های استخراج‌شده، محاسبه شاخص Outlier بر مبنای نسبت فالوورها و رتبه‌بندی تعامل.
- **`batch_fetch_top_posts_meta.py`**: اسکریپت استخراج عنوان، کپشن و متادیتای واقعی پست‌ها از طریق اینستاگرام با وب‌سوکت Chrome CDP.
- **`candidates_with_meta.json`**: خروجی متادیتای خام ۳۰ پست وایرال بررسی‌شده.
- **`WORKFLOW.md`**: همین سند، تشریح‌کننده متدولوژی و دستورالعمل اجرایی.

---

## ۳. فرمول محاسبه شاخص Outlier در اسکریپت
$$\text{Expected Engagement} = \max(\text{Followers} \times 0.025, 100)$$
$$\text{Outlier Multiple} = \frac{\text{Likes} + \text{Comments}}{\text{Expected Engagement}}$$

- نرخ پایه تعامل برای ریلزهای اینستاگرام ۲.۵ درصد در نظر گرفته شده است.
- اگر پیجی ۱۰,۰۰۰ فالوور دارد و ریلز آن ۲۸,۰۰۰ لایک و کامنت گرفته است:
  $$\text{Expected} = 250 \implies \text{Outlier Multiple} = \frac{28000}{250} = 112\times$$

---

## ۴. نحوه اجرای مجدد برای کالکشن‌ها یا پست‌های جدید
برای اسکن و به‌روزرسانی ریلزهای جدید، دستور زیر در محیط کنسول اجرا می‌شود:
```powershell
python "C:\projects\Antigravity Bazino\Publish-Studio\ig-viral\find_outliers.py"
python "C:\projects\Antigravity Bazino\Publish-Studio\ig-viral\batch_fetch_top_posts_meta.py"
```
نتایج به صورت خودکار در `candidates_with_meta.json` و `swipe.md` بروزرسانی خواهند شد.
