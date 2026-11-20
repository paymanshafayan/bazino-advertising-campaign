import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

for k in ["ALL_PROXY", "all_proxy", "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy"]:
    os.environ.pop(k, None)

import asyncio
import json
import html
import urllib.request
import websockets

CDP_PORT = 9335

def get_instagram_tab():
    req = urllib.request.Request(f"http://127.0.0.1:{CDP_PORT}/json")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(req, timeout=5) as resp:
        tabs = json.loads(resp.read().decode())
        for t in tabs:
            if t.get('type') == 'page' and 'instagram.com' in t.get('url', ''):
                return t
    return None

async def run():
    collections_path = r"C:\projects\Antigravity Bazino\complete_collections_data.json"
    creators_path = r"C:\projects\Antigravity Bazino\Publish-Studio\all_collections_creators_ranked.json"

    with open(collections_path, "r", encoding="utf-8") as f:
        col_data = json.load(f)

    with open(creators_path, "r", encoding="utf-8") as f:
        creators = {c["username"]: c for c in json.load(f)}

    items = col_data.get("amoozesh_game", {}).get("items", [])
    posts = []
    for it in items:
        u = it.get("user") or {}
        uname = u.get("username")
        cinfo = creators.get(uname, {})
        followers = cinfo.get("followersCount", 0)
        likes = it.get("like_count") or 0
        comments = it.get("comment_count") or 0
        total_eng = likes + comments
        expected_eng = max(followers * 0.025, 100) if followers > 0 else 500
        outlier = total_eng / expected_eng
        posts.append({
            "code": it.get("code"),
            "username": uname,
            "followers": followers,
            "followersRaw": cinfo.get("followersRaw", "0"),
            "likes": likes,
            "comments": comments,
            "outlier_multiple": round(outlier, 2),
            "url": f"https://www.instagram.com/reel/{it.get('code')}/"
        })

    # Sort and pick top 10
    top10 = sorted(posts, key=lambda x: (x["outlier_multiple"], x["likes"]), reverse=True)[:10]

    # Fetch metadata via CDP
    tab = get_instagram_tab()
    if tab:
        async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=40_000_000) as ws:
            for idx, p in enumerate(top10, 1):
                code = p["code"]
                js = f"""
                (async () => {{
                    try {{
                        const res = await fetch('/reel/{code}/');
                        if (res.ok) {{
                            const text = await res.text();
                            const titleMatch = text.match(/<meta property="og:title" content="([^"]+)"/);
                            return titleMatch ? titleMatch[1] : '';
                        }}
                    }} catch(e) {{}}
                    return '';
                }})()
                """
                msg = {"id": idx, "method": "Runtime.evaluate", "params": {"expression": js, "awaitPromise": True, "returnByValue": True}}
                await ws.send(json.dumps(msg))
                resp = await ws.recv()
                title = json.loads(resp).get('result', {}).get('result', {}).get('value', '')
                p["title"] = html.unescape(title)
                await asyncio.sleep(0.3)

    out_json = r"C:\projects\Antigravity Bazino\Publish-Studio\ig-viral\educational_viral_posts.json"
    with open(out_json, "w", encoding="utf-8") as f:
        json.dump(top10, f, ensure_ascii=False, indent=2)
    print(f"جدول آموزشی در {out_json} با موفقیت به‌روزرسانی شد.")

if __name__ == "__main__":
    asyncio.run(run())
