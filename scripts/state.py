#!/usr/bin/env python3
"""
Update .agent/state.json.

The state file is what scripts/check.sh reads. Keeping it current is what
makes the gate meaningful — a stale state file passes checks it should fail.

  python3 scripts/state.py call                      count one tool call
  python3 scripts/state.py mission <name> <pattern> <phases>
  python3 scripts/state.py phase <n>
  python3 scripts/state.py add <id> <title>          new deliverable (building)
  python3 scripts/state.py status <id> <status>      building|awaiting_approval|approved
  python3 scripts/state.py gate <name> <true|false|null>
  python3 scripts/state.py img <path>                record a generated image
  python3 scripts/state.py rev <path>                record that it was reviewed
  python3 scripts/state.py block <text>              add a blocker on the owner
  python3 scripts/state.py unblock <index>
  python3 scripts/state.py section <name>            note a loaded section
  python3 scripts/state.py release [name]            release one / all sections
  python3 scripts/state.py reset                     new session
  python3 scripts/state.py show
"""
import json
import os
import sys
from datetime import datetime, timezone

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATH = os.path.join(ROOT, '.agent', 'state.json')

BLANK = {
    "_comment": "Machine-checkable session state. Read by scripts/check.sh, "
                "which runs OUTSIDE the model's context and therefore does not "
                "degrade as the session grows.",
    "_schema": 1,
    "session": {"started": None, "tool_calls": 0, "zone": "green",
                "note": "green 0-12 | yellow 13-20 | red 21+"},
    "mission": {"name": None, "pattern": None, "phase": None,
                "phase_total": None, "started": None},
    "deliverables": [],
    "gates": {"owner_approved_current": None, "one_deliverable_only": True,
              "output_is_copy_ready": None, "visual_reviewed": None,
              "knowledge_loaded": None, "pattern_checked": None},
    "loaded_sections": [],
    "images_generated": [],
    "images_reviewed": [],
    "blocked_on_owner": [],
}


def now():
    return datetime.now(timezone.utc).isoformat(timespec='seconds')


def load():
    if not os.path.exists(PATH):
        return json.loads(json.dumps(BLANK))
    with open(PATH, encoding='utf-8') as f:
        return json.load(f)


def save(s):
    os.makedirs(os.path.dirname(PATH), exist_ok=True)
    calls = s['session']['tool_calls']
    s['session']['zone'] = 'red' if calls > 20 else 'yellow' if calls > 12 else 'green'
    with open(PATH, 'w', encoding='utf-8') as f:
        json.dump(s, f, indent=2, ensure_ascii=False)
        f.write('\n')


def as_bool(v):
    return {'true': True, 'false': False, 'null': None}.get(v.lower(), v)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    cmd = sys.argv[1]
    a = sys.argv[2:]
    s = load()

    if cmd == 'call':
        s['session']['tool_calls'] += 1
        if not s['session']['started']:
            s['session']['started'] = now()
        c = s['session']['tool_calls']
        save(s)
        zone = s['session']['zone']
        flag = {'green': '🟢', 'yellow': '🟡', 'red': '🔴'}[zone]
        print(f"{flag} {c} tool calls — {zone}")
        if zone == 'red':
            print("   Tell the owner. Offer a fresh session.")
        elif zone == 'yellow':
            print("   Re-read CHECKLIST.md. Release finished sections.")

    elif cmd == 'mission':
        s['mission'] = {"name": a[0], "pattern": a[1] if len(a) > 1 else None,
                        "phase": 0, "phase_total": int(a[2]) if len(a) > 2 else None,
                        "started": now()}
        s['gates']['knowledge_loaded'] = False
        s['gates']['pattern_checked'] = False
        save(s)
        print(f"mission: {a[0]}")

    elif cmd == 'phase':
        s['mission']['phase'] = int(a[0])
        save(s)
        print(f"phase {a[0]}")

    elif cmd == 'add':
        s['deliverables'].append({"id": a[0], "title": " ".join(a[1:]),
                                  "status": "building", "created": now()})
        save(s)
        print(f"+ {a[0]} (building)")

    elif cmd == 'status':
        for d in s['deliverables']:
            if d['id'] == a[0]:
                d['status'] = a[1]
                if a[1] == 'approved':
                    s['gates']['owner_approved_current'] = True
                elif a[1] == 'awaiting_approval':
                    s['gates']['owner_approved_current'] = False
                save(s)
                print(f"{a[0]} → {a[1]}")
                return 0
        print(f"no deliverable {a[0]}")
        return 1

    elif cmd == 'gate':
        s['gates'][a[0]] = as_bool(a[1])
        save(s)
        print(f"{a[0]} = {a[1]}")

    elif cmd == 'img':
        s['images_generated'].append(a[0])
        save(s)
        n = len(s['images_generated']) - len(s['images_reviewed'])
        print(f"image recorded — {n} awaiting review")

    elif cmd == 'rev':
        s['images_reviewed'].append(a[0])
        if len(s['images_reviewed']) >= len(s['images_generated']):
            s['gates']['visual_reviewed'] = True
        save(s)
        print("reviewed")

    elif cmd == 'block':
        s['blocked_on_owner'].append(" ".join(a))
        save(s)
        print(f"🔴 blocked: {' '.join(a)}")

    elif cmd == 'unblock':
        i = int(a[0])
        if 0 <= i < len(s['blocked_on_owner']):
            print(f"cleared: {s['blocked_on_owner'].pop(i)}")
            save(s)

    elif cmd == 'section':
        if a[0] not in s['loaded_sections']:
            s['loaded_sections'].append(a[0])
        save(s)
        if len(s['loaded_sections']) > 1:
            print(f"⚠️  {len(s['loaded_sections'])} sections held: {s['loaded_sections']}")
        else:
            print(f"loaded: {a[0]}")

    elif cmd == 'release':
        if a:
            s['loaded_sections'] = [x for x in s['loaded_sections'] if x != a[0]]
        else:
            s['loaded_sections'] = []
        save(s)
        print(f"held: {s['loaded_sections'] or 'none'}")

    elif cmd == 'reset':
        fresh = json.loads(json.dumps(BLANK))
        fresh['session']['started'] = now()
        save(fresh)
        print("state reset")

    elif cmd == 'show':
        print(json.dumps(s, indent=2, ensure_ascii=False))

    else:
        print(__doc__)
        return 1

    return 0


if __name__ == '__main__':
    sys.exit(main())
