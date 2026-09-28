#!/usr/bin/env bash
#
# Deterministic pre-flight gate.
#
# Why this exists: an agent's attention to its own rules measurably decays as a
# session grows — by roughly turn 16 compliance drops to about a third of what
# it was at turn 5, with no visible failure signal. A rule the agent reads can
# be skimmed past. A non-zero exit code cannot.
#
# This script runs OUTSIDE the model's context window. Its judgement does not
# degrade. Run it before returning any significant output.
#
# Usage:
#   bash scripts/check.sh            full gate
#   bash scripts/check.sh --quiet    only failures
#
# Exit: 0 = clear · 1 = blocking failure · 2 = warnings only

set -uo pipefail
cd "$(dirname "$0")/.." || exit 1

STATE=".agent/state.json"
QUIET=0
[ "${1:-}" = "--quiet" ] && QUIET=1

FAIL=0
WARN=0

red()   { printf '\033[31m%s\033[0m\n' "$1"; }
yellow(){ printf '\033[33m%s\033[0m\n' "$1"; }
green() { printf '\033[32m%s\033[0m\n' "$1"; }
dim()   { [ $QUIET -eq 0 ] && printf '\033[2m%s\033[0m\n' "$1"; }

fail() { red   "  ❌ $1"; FAIL=$((FAIL+1)); }
warn() { yellow "  ⚠️  $1"; WARN=$((WARN+1)); }
ok()   { [ $QUIET -eq 0 ] && green "  ✅ $1"; }

[ $QUIET -eq 0 ] && echo "── pre-flight gate ──────────────────────────────"

# ---------------------------------------------------------------- state file

if [ ! -f "$STATE" ]; then
  fail "no .agent/state.json — nothing can be verified"
  echo
  red "BLOCKED ($FAIL)"
  exit 1
fi

if ! python3 -c "import json,sys; json.load(open('$STATE'))" 2>/dev/null; then
  fail "state.json is not valid JSON"
  echo
  red "BLOCKED ($FAIL)"
  exit 1
fi

read -r CALLS ZONE MISSION PHASE APPROVED ONEONLY COPYREADY VISUAL KNOW PATT \
        NGEN NREV NBLOCK NSEC <<< "$(python3 - <<'PY'
import json
s = json.load(open('.agent/state.json'))
g = s.get('gates', {})
print(
  s.get('session',{}).get('tool_calls',0),
  s.get('session',{}).get('zone','green'),
  s.get('mission',{}).get('name') or 'none',
  s.get('mission',{}).get('phase') or '-',
  g.get('owner_approved_current'),
  g.get('one_deliverable_only'),
  g.get('output_is_copy_ready'),
  g.get('visual_reviewed'),
  g.get('knowledge_loaded'),
  g.get('pattern_checked'),
  len(s.get('images_generated',[])),
  len(s.get('images_reviewed',[])),
  len(s.get('blocked_on_owner',[])),
  len(s.get('loaded_sections',[])),
)
PY
)"

# --------------------------------------------------------------- rule 0 gate

[ $QUIET -eq 0 ] && echo "Rule 0 — nothing without permission"

if [ "$NBLOCK" -gt 0 ]; then
  fail "$NBLOCK item(s) waiting on the owner — do not proceed past them"
  python3 -c "
import json
for b in json.load(open('.agent/state.json')).get('blocked_on_owner',[]):
    print('       •', b)
"
else
  ok "nothing blocked on the owner"
fi

# ------------------------------------------------- one deliverable at a time

[ $QUIET -eq 0 ] && echo
[ $QUIET -eq 0 ] && echo "One deliverable per reply"

python3 - <<'PY'
import json, sys
s = json.load(open('.agent/state.json'))
ds = s.get('deliverables', [])
waiting = [d for d in ds if d.get('status') == 'awaiting_approval']
building = [d for d in ds if d.get('status') == 'building']

bad = False
if len(waiting) > 1:
    print(f"  ❌ {len(waiting)} deliverables awaiting approval at once"); bad = True
if len(building) > 1:
    print(f"  ❌ {len(building)} deliverables being built at once"); bad = True

for d in building:
    dep = str(d.get('status_detail', '')) + ' ' + str(d.get('blocked_by', ''))
    for other in ds:
        oid = other.get('id')
        if oid and oid in dep and other.get('status') != 'approved':
            print(f"  ❌ {d.get('id')} is being built but {oid} is not approved")
            bad = True

if not bad and ds:
    print("  ✅ deliverable sequencing is clean")
elif not ds:
    print("  ✅ no deliverables in flight")
sys.exit(1 if bad else 0)
PY
[ $? -ne 0 ] && FAIL=$((FAIL+1))

# ------------------------------------------------------------ visual review

[ $QUIET -eq 0 ] && echo
[ $QUIET -eq 0 ] && echo "Visual review"

if [ "$NGEN" -gt "$NREV" ]; then
  fail "$NGEN image(s) generated, only $NREV opened with read_file"
elif [ "$NGEN" -gt 0 ]; then
  ok "all $NGEN image(s) reviewed"
else
  dim "  no images this session"
fi

# ------------------------------------------------------------ mission gates

[ $QUIET -eq 0 ] && echo
[ $QUIET -eq 0 ] && echo "Mission cycle"

if [ "$MISSION" != "none" ]; then
  [ "$KNOW" = "False" ] && fail "mission running but knowledge skill not loaded (§32)" || ok "knowledge loaded"
  [ "$PATT" = "False" ] && warn "behaviour pattern not checked (§31)" || ok "pattern checked"
  dim "  mission: $MISSION · phase $PHASE"
else
  dim "  no mission active"
fi

# ------------------------------------------------------------ memory budget

[ $QUIET -eq 0 ] && echo
[ $QUIET -eq 0 ] && echo "Memory budget"

if [ "$CALLS" -gt 20 ]; then
  fail "$CALLS tool calls — RED. Tell the owner and offer a fresh session."
elif [ "$CALLS" -gt 12 ]; then
  warn "$CALLS tool calls — YELLOW. Re-read CHECKLIST.md, release finished sections."
else
  ok "$CALLS tool calls — green"
fi

if [ "$NSEC" -gt 1 ]; then
  warn "$NSEC knowledge sections held at once — release all but the current one"
fi

# --------------------------------------------------------- repo sanity

[ $QUIET -eq 0 ] && echo
[ $QUIET -eq 0 ] && echo "Repository"

for f in CHECKLIST.md Doc/user-rules-fa.md BOOTSTRAP.md; do
  [ -f "$f" ] || fail "missing: $f"
done
[ $FAIL -eq 0 ] && ok "governing documents present"

if git rev-parse --git-dir >/dev/null 2>&1; then
  if [ -n "$(git status --porcelain 2>/dev/null)" ] && [ "$APPROVED" = "False" ]; then
    warn "uncommitted changes while nothing is approved — do not commit yet"
  fi
fi

# ------------------------------------------------------------------ verdict

echo
if [ $FAIL -gt 0 ]; then
  red "BLOCKED — $FAIL failure(s), $WARN warning(s)"
  red "Fix these before replying."
  exit 1
elif [ $WARN -gt 0 ]; then
  yellow "PASS WITH WARNINGS — $WARN"
  exit 2
else
  green "CLEAR"
  exit 0
fi
