# Analysis, learning memory, self-learning and knowledge

> Reference file of the BAZINO project skill. Loaded on demand — see `../SKILL.md`.

---

# 17. PERFORMANCE ANALYSIS

After publication, collect available performance data.

Evaluate:

- reach
- impressions
- views
- engagement
- likes
- comments
- shares
- saves
- profile visits
- clicks
- conversions where measurable
- registrations
- reservations
- tournament participation
- affiliate activity
- content-level performance
- campaign-level performance

Compare content against BAZINO's own historical performance.

Avoid drawing conclusions from extremely small samples.

---

---

# 18. LEARNING MEMORY

Create a continuous marketing knowledge base.

Record:

### What worked
- topic
- hook
- format
- platform
- audience
- timing
- CTA

### What underperformed
- topic
- format
- hook
- audience
- timing
- CTA
- possible reason

### What remains uncertain
- hypotheses requiring another test

Do not permanently label a format successful or unsuccessful based on one post.

Use repeated evidence.

---

---

# 19. EXPERIMENT ENGINE

Continuously run controlled creative experiments.

Test variables such as:

- hook
- opening frame
- video length
- CTA
- caption style
- topic
- visual style
- posting time
- storytelling structure
- promotional intensity
- educational vs entertainment format

Change a limited number of variables at a time whenever possible so that results remain interpretable.

---

---

# 20. CONTENT DIVERSITY

Prevent repetitive publishing.

Before creating new content, check:

- recent posts
- recent topics
- recent hooks
- recent campaigns
- recent visual styles
- current promotions
- recent tournament communication

Avoid publishing essentially the same idea repeatedly unless it is intentionally part of a campaign sequence.

---

---

# 21. CUSTOMER FEEDBACK LOOP

Use available customer signals.

Review:

- Instagram comments
- Instagram messages where available
- Telegram reactions/messages where available
- tickets
- customer activity
- campaign responses
- tournament participation
- reservations
- content performance

Extract recurring questions and complaints.

Convert useful recurring questions into:

- FAQ content
- educational posts
- Reels
- Telegram posts
- blog articles
- campaign improvements

---

---

# 22. TREND → BAZINO TRANSLATION

When a trend is discovered, do not automatically publish it.

Translate it through this decision chain:

**Trend**
↓
**Audience relevance**
↓
**BAZINO relevance**
↓
**Creative angle**
↓
**Platform**
↓
**CTA**
↓
**Campaign relationship**
↓
**Measurement**

If the trend cannot be meaningfully connected to BAZINO, discard it.

---

---

# 31. SELF-LEARNING — BEHAVIOUR PATTERN CAPTURE

After every completed mission, capture how the work was done, so the next run of
the same mission starts from the best known method instead of from zero.

This is **procedural** knowledge — *how I work*. It is distinct from §18 Learning
Memory, which holds **marketing** knowledge — *what content performed*.

Repository: **`Doc/behavior-patterns-fa.md`**

## 31.1 The four-step cycle — runs after EVERY completed mission

**Step 1 — Extract the pattern.**
Write down how the mission was actually done:
- the phases, in order
- decisions that had to be made, and what informed them
- errors made and what they cost
- what the owner corrected
- which files, tools or references were needed

**Step 2 — Check the repository.**
Read `Doc/behavior-patterns-fa.md`. Does a pattern for this mission already
exist? Match on the nature of the work, not on wording.

**Step 3 — If it exists → compare and update AUTOMATICALLY.**
Compare this run against the recorded pattern. If the run revealed anything the
pattern lacked — a step, an error, a shortcut, a constraint — **update the
pattern immediately and commit it. No approval needed** (owner instruction,
2026-09-18; consistent with Rule 3 on automatic documentation).
Log every change in the pattern's changelog with its date.
If nothing is new, state that and change nothing.

**Step 4 — If it does not exist → propose, then save.**
Present to the owner:
- a proposed mission name
- the full draft pattern
- one line on what triggered it

**Wait for approval.** On approval, add it to `Doc/behavior-patterns-fa.md`.
Without approval, nothing is saved — Rule 0.

> **Asymmetry is deliberate.** Updating an existing pattern is automatic because
> the owner already approved that pattern's existence. Creating a new one needs
> approval because it is a new claim about how work should be done.

## 31.2 What counts as a mission

A unit of work with a recognisable start and end that could plausibly recur.

| Is a mission | Is not |
|---|---|
| Preparing a video batch | Answering one question |
| Registering in a directory | Reading one file |
| Building a prompt for the portal agent | Fixing one typo |
| Auditing code against documentation | A single git push |
| Extracting rules from a document | A one-line edit |

If unsure whether something is a mission — ask.

## 31.3 Pattern structure

Every pattern carries:

1. **Name** — what the mission is called
2. **Trigger** — the words or situation that start it
3. **Status** — how many times run · `⚠️ untested` until run twice
4. **Phases** — the ordered steps
5. **Iron rules** — each paired with the real error that produced it
6. **Required files** — what must be read or attached
7. **Deliverable** — what "done" looks like
8. **Not part of this** — explicit boundaries
9. **Changelog** — dated updates

## 31.4 Rules

- Errors are recorded as iron rules **together with the mistake that caused
  them**. A rule without its story gets ignored; a rule with a scar does not.
- **An owner correction is the highest-value input.** It always enters the
  pattern, immediately.
- Patterns describe **how**, never **whether**. Rule 0 still governs — a pattern
  is not permission to act without approval.
- A pattern that has not been used twice stays marked `⚠️ untested`.
- Patterns may point at a fuller document (e.g. §6-B) rather than duplicating
  it. The repository is an index of method, not a second copy of the skill.

---

---

---

# 32. KNOWLEDGE — MISSION-SPECIFIC EXPERTISE

Before starting any mission, load the external expertise for it.

| §31 Behaviour patterns | §32 Knowledge |
|---|---|
| *How we did it* | *How it is best done* |
| From our own experience | From external authoritative sources |
| Our errors and the owner's corrections | Industry standard and research |

Repository: **`skills/knowledge/`** — one knowledge **skill** per mission type,
not a plain document. A skill can carry reference files, a frontmatter
description and a loading order; a flat document cannot.

⚠️ Unlike §31, this section is **not** permanently in memory. It is consulted
**at the start of a mission**, and only the relevant skill is loaded.

## 32.1 The three-step cycle — runs BEFORE every mission

Applies to every mission, including daily and pre-defined ones.

### Step 1 — Check

Does `skills/knowledge/<mission>/SKILL.md` exist? Use the behaviour-pattern name
from §31 so the two stay aligned.

### Step 2 — If missing → research and build it

**2a. GitHub first — always.**

Search GitHub before the open web. Reasons: agent skills are published there in
a directly reusable form, and **feedback is measurable** — stars, forks, open
issues, commit recency, and whether real people report it working.

What to check on a candidate repository:

| Signal | What it tells you |
|---|---|
| Stars and forks | Adoption |
| Last commit date | Whether it tracks a moving target |
| Open vs. closed issues | Whether the author maintains it |
| README specificity | Whether it is real work or a keyword farm |
| Licence | Whether we may reuse it, and under what attribution |

If a strong, current skill exists — **use it**. Adapt it to BAZINO rather than
writing from nothing. Honour its licence and record attribution.

If several good ones exist and their strengths do not conflict, **merge** them.
That is how `skills/knowledge/video-production/` was built.

**2b. Then the open web.**

Official documentation, peer-reviewed or industry-standard material, and sources
with strong real-world feedback. Read several; a single blog post is not a
knowledge base.

**2c. Merge into one skill.**

- Merge, do not stack side by side
- Resolve contradictions explicitly — state which source won and why
- Note what is disputed rather than smoothing it over
- Record every source URL

Then use it immediately for this mission.

### Step 3 — If present → load and apply

Read it fully before starting. Use it throughout. If the mission shows the
knowledge is wrong, outdated or incomplete, **update it and log the change** —
same automatic-update principle as §31.

## 32.2 Knowledge skill structure

Frontmatter `name` and `description`, then:

1. **Mission** — which mission this serves
2. **Last researched** — date, so staleness is visible
3. **Primary sources** — where it came from
4. **Core principles** — the non-negotiable fundamentals
5. **Standards and specifications** — concrete numbers, formats, limits
6. **Best practice** — what strong operators actually do
7. **Common mistakes** — documented failure modes
8. **Disputed or unverified** — where sources disagree
9. **Sources** — every URL with a one-line note on what it contributed
10. **Changelog** — dated updates

Reference files go in `references/` beside the SKILL.md.

## 32.3 Monthly staleness check — automatic

Every night, after the owner declares the day finished, **every skill in
`skills/knowledge/` is checked**. This runs **without asking permission**
(owner instruction, 2026-09-18).

For each skill older than one month since its last update:

1. **Check the original source first.** If it is a GitHub repository, look for
   new commits or releases since our last update. If there is a newer version,
   update our skill from that same source.
2. **If the original has not moved**, search more widely for newer sources and
   update from those.
3. **Log the result either way** — including "checked, nothing new", so the same
   check is not repeated tomorrow.

Full routine in `HANDOFF.md`.

## 32.4 Quality rules

- **Merge, never stack.** A knowledge skill is a synthesis, not a link dump.
- **Cite everything.** A claim without a source is an opinion.
- **Date everything.** Platform specs move; a figure from last year may be wrong.
- **Contradiction is information.** When sources disagree, say so and choose,
  with the reason.
- **Project constraints win.** Where best practice conflicts with a BAZINO rule
  or an owner instruction, the BAZINO rule governs — record the conflict so it
  is not rediscovered every time.
- **Knowledge is not permission.** Finding a best practice is not authority to
  act on it. Rule 0 still governs.

---

