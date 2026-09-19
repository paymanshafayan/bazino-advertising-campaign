# Campaign management and the affiliate flow v2

> Reference file of the BAZINO project skill. Loaded on demand — see `../SKILL.md`.

---

## ✅ چک‌لیست افیلیت

```
□ فلو نسخه ۲؟ (نسخه کد عددی بازنشسته شده)
□ کلیدواژه با زبان محتوا می‌خواند؟ (Hazır/آماده/Ready/Готово)
□ SQUAD26 فقط شناسه کمپین، نه کلیدواژه کامنت؟
□ هیچ لینکی در Private Reply عمومی نیست؟
□ درصد کمیسیون از پنل، نه hardcode؟
```

---

# 14. CAMPAIGN MANAGEMENT

Do not treat individual posts as isolated objects.

Build campaigns.

A campaign may contain:

- campaign objective
- audience
- core message
- Instagram content
- Telegram content
- blog article
- website slider
- promotional content
- supporting Stories
- CTA
- follow-up content
- performance measurement

Examples:

### Tournament Campaign
Announcement → anticipation → registration → reminder → event → result → community content.

### Affiliate Campaign
Awareness → explanation → invitation → participation → proof/social validation → continued recruitment.

### Prestige Campaign
Brand positioning → gaming experience → status → benefits → community → loyalty.

### Safety Campaign
Parent awareness → safe environment → trust → BAZINO environment → informative CTA.

---

---

# 14-A. INSTAGRAM AFFILIATE FLOW — VERSION 2 (CANONICAL)

This is the only valid affiliate flow. It matches the portal code as of
2026-09-14 and the document `Doc/affiliate-instagram-plan-fa.md` (v2). Every
piece of affiliate content must be built on it.

1. The partner comments the campaign keyword under the Reel/post. The keyword
   is **language-specific and must match the language of that content**:

   | Language | Comment keyword |
   |---|---|
   | Turkish | `Hazır` |
   | Persian | `آماده` |
   | English | `Ready` |
   | Russian | `Готово` |

   These four keywords are **live in production** — Zernio matches them today.
   `SQUAD` was the earlier single-keyword default and is **retired as a comment
   keyword**. Do not publish content asking for `SQUAD`.

   Not to be confused with the **campaign identifier** `SQUAD26`, which is a
   separate thing: it is used in attribution links and UTM parameters
   (`utm_campaign=SQUAD26`) and remains valid.

   For Turkish content the approved hook may open with `Hazır mısın?`.
2. The system sends a Private Reply with guidance and a button — **containing no link**.
3. The partner follows `@bazinopro`.
4. The partner presses the "I followed" button (`partner_follow_check`).
5. After the follow is verified, the partner's **personal, reusable invite link**
   (valid 365 days, `role=partner`) is sent to their own Direct.
6. The partner distributes that link to their friends themselves — the system
   never messages the friend.
7. The friend registers through the link and receives an independent
   single-use coupon `IG-{hex}`.
8. The partner earns commission for each paid reservation/attendance by a friend.

**Security rule.** `{{invite_url}}` is substituted only at dispatch time; the
`PRIVATE_LINK_FORBIDDEN` guard blocks any link in a public Private Reply.

❌ **Forbidden:** asking the friend to comment a numeric code, sending any
system message or post to the friend, promising a coupon before the link gate is
passed, and placing a link in a public Private Reply.

**Portal-enforced format rules** (`server/publishing/publish.ts` rejects the
post otherwise — these are not preferences):

| Rule | Error code |
|---|---|
| Affiliate content can never be a Story | `STORY_NOT_AFFILIATE` |
| Affiliate content can never be a single image | `AFFILIATE_FOUR_SLIDES_REQUIRED` |
| An affiliate carousel must be **exactly 4 slides with a Turkish caption** | `AFFILIATE_FOUR_SLIDES_TURKISH_CAPTION` |
| Affiliate posts are scheduled only in **batches of exactly 3** | `THREE_POSTS_REQUIRED` |

Owner decision (2026-09-17): the v3 engine (`igEngine.ts`, single `SQUAD`
keyword) is **retired**; `campaignV4.ts` with the four language keywords is the
only engine. Removal is specified in
`Doc/portal-agent-prompt-campaign-fix-fa.md`.

**Intended to be retired in v1:** system messaging the friend, the friend
commenting a numeric code, and `share_confirmed_by_friend_code`.

**Owner decision (2026-09-17): the friend code flow goes.** It was still live
in `campaignV4.ts:66` at the time of the audit; removal is specified in
`Doc/portal-agent-prompt-campaign-fix-fa.md` (a six-digit comment will be
rejected with `friend_flow_retired`). The `partner-invite` endpoint is already
retired (HTTP 410). Do not produce content that asks a friend to comment a code.

**Commission rates are never stated in content.** `commissionPct`,
`attributionDays`, `refundDays` and coupon values are set by the admin in the
management panel and are the only valid source. Never quote a rate, and never
promise guaranteed earnings.

**Approved guidance message** (the fixed wording sent after the keyword
comment; translated per content language, meaning must not change without
authorization):

> پیج را فالو کن و بر روی دکمه فالو دارم بزن تا لینک دعوت اختصاصی خودت برات ارسال بشه بعد این لینک را برای دوستات بفرست. دوستانت با ثبت نام از طریق این لینک، کوپن تخفیف دریافت می کنند و تو هم از این به بعد از هر بار پرداخت آن ها در Bazino کمسیون دریافت می کنی.

**Hard affiliate rules:**

- Never fabricate a code or an invite link, and never hand-edit an issued link.
- Never ask for an Instagram password.
- Never claim an individual share, like or follow is confirmed by Meta unless it
  was actually verified through the permitted path.
- Never promise guaranteed earnings. Commission counts and payment terms are
  stated only from the official source.
- Media ID is taken from the official publish/Meta API response only (§5-A.6).

KPIs: `Link activation rate` and `Link spread rate`.


---

---

