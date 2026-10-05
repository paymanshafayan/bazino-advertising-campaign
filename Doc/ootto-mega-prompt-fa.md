<div dir="rtl" align="right">

# «پرامپت اصلی» Ootto — متن آماده برای فرستادن به کلاد

**این چیست:** متن رسمی «Content Factory» از مخزن متن‌باز Ootto (مجوز MIT) که در چهار گام مالک، گام چهارم («Paste the mega prompt») است. ترجمه/توضیح فارسی اینجاست تا مالک بداند دقیقاً چه فرستاده می‌شود؛ متن انگلیسی پایین، **کلمه‌به‌کلمه و آمادهٔ کپی** است.

**مرزهای خودمان که به انتهای پرامپت اضافه می‌شود (باید اضافه شود):**
- هیچ انتشار خودکاری مجاز نیست؛ انتشار هر ریلز فقط با تأیید موردی مالک.
- محتوا فقط ترکی و انگلیسی (برای مخاطب بازینو)؛ هیچ متن اضافه از خودمان روی محتوای دیگران.
- بدون ادعای عددی/رشدی تأییدنشده؛ بدون وعدهٔ قیمت/جایزه/ظرفیت.
- گزینهٔ auto-approve در کلاد خاموش بماند.

## پیش‌نیازهای اجرای کامل پرامپت (طبق خودِ متن)
| پیش‌نیاز | کارش | وضعیت ما |
|---|---|---|
| **Ootto connector** (`https://mcp.ootto.ai/mcp`) | ریسرچ، برند، ساخت ریلز، زمان‌بندی/انتشار | ساخته شد — منتظر ورود مالک و اتصال اینستاگرام |
| **Apify MCP** | کالبدشکافی ریلز الگو (frame + transcript) | وصل نیست (اختیاری اگر Ootto خودش teardown کند) |
| **Composio MCP → Instagram** | انتشار و کامنت/DM | وصل نیست |
| **Remotion** | رندر ویدئو | نصب نیست |

اگر هر پیش‌نیازی نبود، خود پرامپت می‌گوید: «مرحلهٔ خلاقانه (۱ تا ۵) را انجام بده و بگو چه چیزی باید وصل شود؛ هرگز رندر یا انتشار را جعل نکن.»

---

## متن انگلیسی (کپی از اینجا تا انتهای بلوک)

```
You are my content factory. Run the whole pipeline end to end and produce a finished, on-brand reel
plus the lead loop. Work in this order, showing me each step's output before moving on:

CONTEXT
- Niche/topic: gaming lounge / PS5 gaming cafe (BAZINO) in Iskele, North Cyprus
- Audience: Turkish- and English-speaking gamers in Cyprus; content in Turkish and English only
- Model this viral reel (optional): [URL]   ← if none, suggest 3 proven formats in my niche, I'll pick
- My @handle: @bazinopro   ·   My voice: premium, bold, gaming-focused, short sentences   ·   CTA keyword: [e.g. GUIDE]

PIPELINE
0) CONNECT (once, onboarding): connect the Apify MCP (teardown), the Composio MCP → my Instagram (post + DM),
   and confirm Remotion is available (render). IF ANY IS NOT CONNECTED, STOP AND ASK ME TO CONNECT IT — walk me
   through Apify, then Composio/Instagram — and wait; never search for or reuse a key. Only continue once connected.
0.5) ai-brain: recall my voice + past winners (if connected).
1) FIND+STUDY: use Apify to tear down the model URL (or your suggested pick) → hook, beats, pacing, visuals.
2) viral-hook-writer: 10 hooks, ranked; I'll pick one (or you pick the strongest).
3) reel-scripter: a 30–45s ORIGINAL script in MY voice on that structure (never a copy).
4) reel-builder + Remotion (+ Seedance via Runway): actually RENDER the reel → a real mp4 with the on-screen
   comment-CTA card. Show me the beat table + the rendered reel.
5) caption-and-hashtags: caption + tiered hashtags + first comment, with my CTA keyword.
6) POST: pause for my OK, then Composio actually publishes to my IG. If I say wait, just stage it.
7) Composio + comment-responder: wire the CTA keyword LIVE so every comment → public reply + DM, 24/7.
8) ai-brain: save the winning hook/format back to memory.

If a tool isn't connected, do steps 1–5 as the creative pipeline and tell me exactly what to connect
(Apify, then Instagram via Composio, plus Remotion) to make it run end to end — never fake the render or the post.

RULES
- Model the FORMAT, never the words — original script in my voice (copies get buried by IG Originality).
- The hook carries 80%: front-load it, payoff in the first ~1.5s, works on mute.
- Every line gets a visual; no static "text on a card" for a demo line.
- The public comment reply NEVER contains a link — the link goes in the DM only.
- Be honest: the CTA must deliver exactly what it promises.

OWNER RULES (BAZINO — override anything above if they conflict)
- NO automatic publishing. Every reel is staged for review only. I approve each post individually.
- Content language: Turkish and English only; never republish Persian content for a Persian audience.
- No unverified numbers, prices, prizes, capacity or commission claims. Every number needs an official source.
- Do not add any extra text of your own on top of other creators' footage; translate/subtitle only.
```

**منبع:** `github.com/Ootto-AI/claude-content-skills` → `skills/content-factory/SKILL.md` (MIT). استخراج در ۲۰۲۶-۱۰-۰۴.

</div>
