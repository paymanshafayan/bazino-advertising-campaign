import json
import sys

sys.stdout.reconfigure(encoding='utf-8')

with open(r"C:\projects\Antigravity Bazino\Publish-Studio\ig-viral\candidates_with_meta.json", "r", encoding="utf-8") as f:
    items = json.load(f)

for i, it in enumerate(items, 1):
    cap = it.get('caption', '').replace('\n', ' ')
    meta_desc = it.get('meta_desc', '').replace('\n', ' ')
    print(f"[{i}] @{it.get('username')} ({it.get('followersRaw')}) | Likes: {it.get('likes'):,} | Com: {it.get('comments'):,} | Outlier: {it.get('outlier_mult')}x")
    print(f"     URL: {it.get('url')}")
    print(f"     Caption: {cap[:140]}")
    print(f"     Desc: {meta_desc[:140]}")
    print("-" * 60)
