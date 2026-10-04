# Operations — Manus, Zernio, lifecycle, errors, security, brand

> Reference file of the BAZINO project skill. Loaded on demand — see `../SKILL.md`.

---

## ✅ چک‌لیست عملیات

```
□ از زیرساخت موجود استفاده می‌کنم، نه سیستم دوم؟ (§۲۷)
□ Zernio فقط DM، انتشار با Manus دستی؟
□ سکرتی در کد یا لاگ نیست؟
□ قواعد برند رعایت شده؟
```

---

# 7. MANUS

Manus is the external media/content-generation service available to the BAZINO system.

Use Manus when the task requires:

- image generation
- video generation
- media creation
- structured content-generation workflows
- other supported Manus capabilities

Respect the existing BAZINO content lifecycle:

> draft → generate → review → approve → schedule → publish

Do not assume that generation automatically means publication.

Maintain the distinction between:

- generated media
- content draft
- approved content
- publishing job
- published media

---

---

# 8. ZERNIO

Zernio is the operational publishing service used to distribute approved content to connected social platforms.

Use Zernio when publishing to supported social channels.

The agent must:

1. Prepare the content.
2. Ensure the content is ready for publication.
3. Create/submit the publishing job.
4. Monitor the publishing result.
5. Record the result.
6. Detect failures.
7. Avoid duplicate publication.
8. Verify the final publishing state whenever the available API/reporting system supports verification.

Never blindly publish the same content again because of an uncertain response.

Use the existing publishing queue and publishing records.

---

---

# 9. CONTENT LIFECYCLE

Maintain the distinction between:

**Idea**
→ **Draft**
→ **Generated**
→ **Review**
→ **Approved**
→ **Scheduled**
→ **Publishing**
→ **Published**
→ **Measured**
→ **Analyzed**
→ **Learned**

A draft must never accidentally become public.

A failed publishing attempt must not automatically create an uncontrolled duplicate publication.

Every publication should remain traceable to:

- content
- channel
- account
- publishing job
- occurrence/attempt
- resulting native media ID where available
- publication status

---

---

# 26. ERROR HANDLING

When an operation fails:

1. Detect the failure.
2. Record it.
3. Determine whether it is transient or structural.
4. Retry only when safe.
5. Never create duplicate publications through blind retries.
6. Continue unrelated tasks when possible.
7. Report unresolved operational problems clearly.

Do not hide failures.

---

---

# 27. API-FIRST PRINCIPLE

The Advertising Agent operates through APIs and existing services.

Before proposing new infrastructure:

1. Check whether the required capability already exists.
2. Identify the relevant API/service.
3. Use the existing capability.
4. Only propose development when the required capability genuinely does not exist.

Never create a second system for a function already supported by BAZINO.

---

---

# 28. SECURITY

Never expose:

- API keys
- tokens
- webhook secrets
- passwords
- encryption secrets
- private credentials
- internal security configuration

Never place credentials inside generated content.

Use server-side integrations and the existing secure configuration mechanisms.

---

---

# 29. BRAND RULES

BAZINO should be presented as:

- modern
- professional
- gaming-focused
- energetic
- premium where appropriate
- welcoming
- community-oriented
- trustworthy
- safe

Do not insult competitors.

Do not make unsupported claims.

Do not fabricate:

- tournaments
- prizes
- promotions
- prices
- partnerships
- customer numbers
- performance statistics
- product availability
- events

If information is unknown, retrieve it through the available API/data source or clearly mark it as unknown.

---

---

