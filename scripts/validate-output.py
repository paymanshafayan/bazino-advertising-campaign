#!/usr/bin/env python3
"""
Validate a draft reply before it is sent.

Research finding (2026): intrinsic self-correction — asking a model to "check
your work" without external grounding — does not reliably help and often
degrades output. What does work is a *deterministic validator* run outside the
model, reserving judgement for what a schema genuinely cannot express.

So this checks the measurable shape of an output, not its quality:

  • fragmentation  — a prompt split across several code blocks
  • completeness   — a video prompt missing its required sections
  • translation    — a video prompt delivered without its Persian translation
  • leakage        — a Reel carrying a number, time or price
  • batching       — more than one numbered deliverable in one reply

Usage:
    python3 scripts/validate-output.py draft.md
    python3 scripts/validate-output.py draft.md --type video-prompt
    cat draft.md | python3 scripts/validate-output.py -

Exit: 0 clear · 1 blocking · 2 warnings
"""
import re
import sys

# Aspect ratios look exactly like clock times (9:16 is 09:16). Strip the known
# ones before looking for a real time.
ASPECTS = ('9:16', '16:9', '4:3', '3:4', '1:1', '21:9', '2:1')
CLOCK = r'(?<![\dx])\b([01]?\d|2[0-3]):[0-5]\d\b(?![\dx])'


def has_clock(s):
    for a in ASPECTS:
        s = s.replace(a, '')
    return re.search(CLOCK, s)

FAIL, WARN = [], []


def fail(m):
    FAIL.append(m)


def warn(m):
    WARN.append(m)


def blocks(text):
    """Fenced code blocks, ignoring language tag."""
    return re.findall(r'```[a-zA-Z]*\n(.*?)```', text, re.S)


def detect(text):
    t = text.lower()
    if re.search(r'\bprompt\s*[\u06F0-\u06F90-9۱-۹]+\s*(of|از)\s*[\u06F0-\u06F90-9۱-۹]+', t) \
       or 'پرامپت' in text and ('ثانیه' in text or 'second' in t):
        return 'video-prompt'
    if 'workflow' in t and ('yaml' in t or '.yml' in t):
        return 'workflow'
    if 'پرامپت ایجنت پورتال' in text or 'portal agent' in t:
        return 'portal-prompt'
    return 'generic'


def check_fragmentation(text, kind):
    if kind not in ('video-prompt', 'portal-prompt'):
        return
    bs = blocks(text)
    # A prompt is one block. Anything with real content in a second block means
    # the owner would have to stitch pieces together before pasting.
    substantive = [b for b in bs if b.count('\n') >= 4]
    if len(substantive) > 1:
        fail(f"{len(substantive)} separate prompt blocks — must be ONE copy-ready block")

    # "shared prompt" + "per-clip prompt" was a real failure; name it directly.
    if re.search(r'(پرامپت مشترک|shared prompt|common prompt)', text, re.I):
        fail("a 'shared prompt' section — every prompt must stand alone")


def check_video_prompt(text):
    bs = blocks(text)
    if not bs:
        fail("no code block — the prompt must be copy-ready")
        return
    body = max(bs, key=len)

    required = {
        'duration':    r'\b\d+\s*-?\s*second|\bseconds\b',
        'aspect':      r'9:16',
        'environment': r'ENVIRONMENT|environment',
        'shot list':   r'\[\d+\s*-\s*\d+s\]',
        'audio':       r'AUDIO|Audio:',
        'on-screen':   r'ON-SCREEN TEXT',
        'final frame': r'FINAL FRAME|final frame',
    }
    missing = [k for k, p in required.items() if not re.search(p, body, re.I)]
    if missing:
        fail("prompt block missing: " + ", ".join(missing))

    # both languages must live inside the one block
    has_tr = bool(re.search(r'TURKISH|Türk|ş|ğ|ı', body))
    has_fa = bool(re.search(r'PERSIAN|[\u0600-\u06FF]', body))
    if has_tr and not has_fa:
        fail("Turkish text present but Persian missing from the same block")

    # a full Persian explanation must accompany it
    outside = re.sub(r'```.*?```', '', text, flags=re.S)
    fa_chars = len(re.findall(r'[\u0600-\u06FF]', outside))
    if fa_chars < 400:
        fail(f"Persian translation of the prompt looks absent or too short ({fa_chars} chars)")

    # 8 seconds is Veo, not Omni
    if re.search(r'\b8\s*-?\s*second', body, re.I):
        fail("8-second duration — that is Veo. Omni renders 3-10 seconds")

    # no facts in a Reel
    if has_clock(body):
        fail("Reel contains a clock time — facts belong on the site, not in the Reel")
    for pat, label in [
        (r'\b\d+\s*(TL|₺|lira|لیر)\b',          'a price'),
        (r'\b(ödül|prize|جایزه)\b',             'a prize'),
        (r'\b\d+\s*(kişi|player|oyuncu|نفر)\b', 'a capacity'),
    ]:
        if re.search(pat, body, re.I):
            fail(f"Reel contains {label} — facts belong on the site, not in the Reel")


def check_batching(text):
    nums = re.findall(r'(?:prompt|پرامپت)\s*([\u06F0-\u06F9\u0660-\u06690-9]+)\s*(?:of|از)', text, re.I)
    if len(set(nums)) > 1:
        fail(f"{len(set(nums))} numbered deliverables in one reply — send one, then stop")


def check_workflow(text):
    bs = blocks(text)
    if not bs:
        return
    y = max(bs, key=len)
    if 'jobs:' in y and not re.search(r'^\s*(name|on):', y, re.M):
        fail("workflow looks partial — the owner needs the complete file")


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    src = sys.argv[1]
    text = sys.stdin.read() if src == '-' else open(src, encoding='utf-8').read()

    kind = sys.argv[3] if len(sys.argv) > 3 and sys.argv[2] == '--type' else detect(text)

    print(f"── output validator ── type: {kind}")

    check_fragmentation(text, kind)
    check_batching(text)
    if kind == 'video-prompt':
        check_video_prompt(text)
    elif kind == 'workflow':
        check_workflow(text)

    print()
    for f in FAIL:
        print(f"  \033[31m❌ {f}\033[0m")
    for w in WARN:
        print(f"  \033[33m⚠️  {w}\033[0m")

    print()
    if FAIL:
        print(f"\033[31mBLOCKED — {len(FAIL)} problem(s). Fix before sending.\033[0m")
        return 1
    if WARN:
        print(f"\033[33mPASS WITH WARNINGS — {len(WARN)}\033[0m")
        return 2
    print("\033[32mCLEAR\033[0m")
    return 0


if __name__ == '__main__':
    sys.exit(main())
