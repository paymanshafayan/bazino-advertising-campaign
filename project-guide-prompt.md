> *آیا تو دارای قابلیت ویژن هستی و آیا این قابلیت در این جلسه فعال است ؟ ابتدا با یک تصویر داخل این پوشه این قابلیت را تست کن و جواب صادقانه بده. اگر جواب منفی است و در این جلسه قابلیت ویژن برایت فعال نیست ، خواندن فایل را ادامه نده . کار را متوقف کن و نتیجه تست قابلیت ویژن را به کارفرما اعلام کن.

> *(Owner's standing instruction, quoted verbatim above — obey it **before** reading anything else in this file: test image viewing with a file in this folder; if vision is unavailable in the session, stop reading, stop the work, and report the test result to the owner honestly.)*

---

# BAZINO PROJECT — RULES BRIEF FOR THE AGENT (EN)

> ## ⚠️ FIRST, READ THE STATUS FILE
> **This file contains NO project status.** Before any work, open **[`HANDOFF-prompt.md`](HANDOFF-prompt.md)** (agent copy of `HANDOFF.md`) and read the **current status**: installed version, what was published, what is in the queue, what is done, what is still open, and the plan register.
> Only **after** that, use this file for the rules — and before content production, also read [`Doc/daily-content-production-guide-prompt.md`](Doc/daily-content-production-guide-prompt.md).
> Rule: status lives **only** in `HANDOFF.md` / `HANDOFF-prompt.md`; rules live **only** in `project-guide-fa.md` / `project-guide-prompt.md`.

Agent copy of `project-guide-fa.md` (naming law 2026-10-05: owner doc name minus language suffix + `-prompt`).
**Status/changes = `HANDOFF-prompt.md`. Content production = `Doc/daily-content-production-guide-prompt.md`. This file = rules, boundaries and references.**
Authority order: newest owner instruction ≫ this file ≫ technical contracts (`marketing-app/PUBLISHING.md`) ≫ historical docs.

## 1. PURPOSE & SCOPE
Reliable preparation and publishing of Bazino Instagram content: gaming news and tutorials, entertainment, tournament intros, family & safety, and affiliate limited to **Reel**. Language mix, schedule and content standard come from the daily content guide; no growth or business result is claimed without real data.
The Windows app **Jinus** (`marketing-app/`) is the agent's tool for preparing and reviewing content, working with media and approved connections, completing the publish queue, and running approved workflows — it is not a project where the owner does manual work inside the app.
**Product name (owner, 2026-10-03): Jinus.**
Target workflow: (1) agent checks valid daily data/metrics, builds final media and completes **every card field** before queueing · (2) app shows media and card details for review — queueing is not permission to run external actions · (3) while open and connected, the app runs permitted actions through supported paths and records a clear result or error — an unknown outcome must never be blindly repeated · (4) Insights data and scheduled-job status must be provable from a real run; code or mock tests do not replace live evidence.

## 2. MANDATORY RULES
- All media production/preparation and sensitive publishing steps go **through Jinus**; cloud research/preview preparation is only a supplement.
- While the app is `listening`, a valid mailbox command may execute **without an in-app confirmation window**; open/closed state and `Stop listening` are the access boundary.
- Public posting and real DMs only with **per-item approval** of that exact content/action. "Start" for a plan approves no specific post/message.
- **Plan law (owner, 2026-10-03):** every large task gets a **numbered plan document** in `plans/`, executed **phase by phase**, with a short report after each phase.
- **Affiliate = Instagram Reel only**; other destinations and Post/Carousel/Story affiliate are forbidden. After a confirmed successful publish, one-way registration of the real `media_id` to the Portal; no Zernio automation for affiliate.
- Never treat the two Zernio connections/credentials or the Portal credential as interchangeable. **No credential, token, cookie or personal data in Git, handoff, prompt, reply or log.**
- Portal ingest has no test tool — **never call the registration endpoint with dummy data.** Record Portal errors separately from the Instagram publish result.
- Location id `1091945074011846` only for Feed/Reel/Carousel per the Page-match rule; **Story performs no location operation at all.** Zernio internal account ids are not the Page id.
- No claim of price, prize, condition, commission or business result without a fresh official source; no numeric/conditional event details (time, price, prize, capacity) in Reels.
- **Handoff-first (owner, 2026-10-04):** before any work, re-read `HANDOFF.md` and the relevant plan document.
- **Code/build/release changes only with an approved plan and explicit owner permission.** Work happens on the fixed session branch only.

### Report format after every operation (owner directive 2026-10-02)
Plain fluent Persian, understandable without help, no technical jargon (rename services to Persian where possible: اینستاگرام، زرنیو).
Four fixed parts in this order, title «گزارش به زبان ساده»: **۱) چه شد؟** (one short line per item, optional 🕐 🖼️ ✈️) · **۲) پلن … (منتظر «شروع کن» شما)** · **۳) چه نشد و چرا؟** (including anything awaiting owner approval) · **۴) قدم بعدی من** · **final line:** storage confirmation with the record id, only after verifying it reached the repo.

## 3. PRODUCT & DOCUMENT BOUNDARIES
- Current behaviour lives in `marketing-app/` source, its `README`, `PUBLISHING.md` and the current user guide; if a historical document conflicts with them or with a newer owner order, it is historical.
- `desktop-app/`, the old standalone bridges, samples and the mobile prototype are previous product snapshots — do not infer today's capabilities from them.
- Detailed implementation criteria: plans 1 and 2 · API details: `marketing-app/PUBLISHING.md` · daily content rules: `Doc/daily-content-production-guide-fa.md` · owner/brand rules: `Doc/campaign/user-rules-fa.md` and `Doc/campaign/brand-characters-fa.md`. This brief intentionally does not duplicate those texts.
- Maintenance rule: current status, done work and open work are recorded **only in their own sections**; the current-version record is updated in the final "last installed version" section and older versions stay in the history table. Never repeat the same status in several sections or present an old record as the latest version.

## 4. STANDING OWNER LAWS (index; full text in the source document)
| Date | Law | Source |
|---|---|---|
| 10-02 | No code/build/release/publish without an approved plan and "start" | `قانون-مالک.md` |
| 10-02 | Public posting and DMs only with per-item approval | §2 here |
| 10-02 | Content planning only from real feedback (Zernio) | content guide |
| 10-02 | Affiliate = Reel only | content guide / `PUBLISHING.md` |
| 10-03 | Large task = numbered plan in `plans/` + phased execution + report per phase | `plans/README.md` |
| 10-03 | Product name = Jinus | §1 here |
| 10-03 | Content laws 7.1–7.7 and the QC checklist | content guide |
| 10-04 | **All times North Cyprus (UTC+03:00); nothing after 21:30** | content guide L5/L6 |
| 10-04 | **Official logo beside the BAZINO wordmark on all published content** | content guide L10 |
| 10-04 | On-image text written by the **image model** + character-by-character review (`BAZINO`, not `BAZİNO`) | content guide L9 |
| 10-04 | The portal repo is the version source of truth; no sync with the other agent's repo for releases | `HANDOFF.md` |
| 10-04 | Re-read handoff and the relevant plan before every task | §2 here |
| 10-05 | **HANDOFF = status/changes only; law = project guide** | this file |
| 10-05 | **Agent copy naming: owner doc name minus language suffix + `-prompt`** | §5 here |

## 5. DOCUMENT MAP & AGENT-COPY NAMING LAW
| Document | Role | Agent copy |
|---|---|---|
| `HANDOFF.md` | status, changes, remaining work, plan register | `HANDOFF-prompt.md` |
| `project-guide-fa.md` | rules, boundaries, report format, document index | `project-guide-prompt.md` |
| `Doc/daily-content-production-guide-fa.md` | daily content standard (owner-facing) | `Doc/daily-content-production-guide-prompt.md` |
| `marketing-app/PUBLISHING.md` | technical queue/publish contract | — |
| `plans/README.md` | plan register | — |
| `قانون-مالک.md` | absolute owner law before any code change | — |

**Naming law (owner, 2026-10-05):** for any document the owner wants an agent version of, the name = **original document name without the language suffix (`-fa`/`-en`) + `-prompt`**; content is English, compact and prompt-like; it lives in the same folder, is linked from the owner document, and is updated in the same moment as the owner document — the two are never allowed to drift apart.

## 6. QUICK LINKS
Status: `HANDOFF.md` · Rules: `project-guide-fa.md` · Content: `Doc/daily-content-production-guide-fa.md` · Technical: `marketing-app/PUBLISHING.md` · Plans: `plans/README.md` · Doc index: `Doc/README.md` · Brand/characters: `Doc/campaign/user-rules-fa.md`, `Doc/campaign/brand-characters-fa.md`.
