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

async def main():
    collections_path = r"C:\projects\Antigravity Bazino\complete_collections_data.json"
    creators_path = r"C:\projects\Antigravity Bazino\Publish-Studio\all_collections_creators_ranked.json"

    with open(collections_path, "r", encoding="utf-8") as f:
        col_data = json.load(f)

    with open(creators_path, "r", encoding="utf-8") as f:
        creators = {c["username"]: c for c in json.load(f)}

    game_items = col_data.get("game", {}).get("items", [])
    analyzed_game_posts = []
    for item in game_items:
        u = item.get("user") or {}
        uname = u.get("username")
        creator_info = creators.get(uname, {})
        followers = creator_info.get("followersCount", 0)
        likes = item.get("like_count") or 0
        comments = item.get("comment_count") or 0
        total_eng = likes + comments
        expected_eng = max(followers * 0.025, 100) if followers > 0 else 500
        outlier_mult = total_eng / expected_eng
        shortcode = item.get("code")
        analyzed_game_posts.append({
            "code": shortcode,
            "username": uname,
            "full_name": u.get("full_name") or creator_info.get("full_name", ""),
            "followers": followers,
            "followersRaw": creator_info.get("followersRaw", "0"),
            "likes": likes,
            "comments": comments,
            "total_eng": total_eng,
            "outlier_mult": round(outlier_mult, 2),
            "url": f"https://www.instagram.com/reel/{shortcode}/"
        })

    # Pick top by Outlier Multiple AND top by raw Likes
    ranked_by_outlier = sorted(analyzed_game_posts, key=lambda x: (x["outlier_mult"], x["likes"]), reverse=True)
    ranked_by_likes = sorted(analyzed_game_posts, key=lambda x: x["likes"], reverse=True)

    candidates = []
    seen = set()

    # Add top 12 outliers
    for p in ranked_by_outlier[:12]:
        if p["code"] not in seen:
            seen.add(p["code"])
            candidates.append(p)

    # Add top likes
    for p in ranked_by_likes[:10]:
        if p["code"] not in seen:
            seen.add(p["code"])
            candidates.append(p)

    print(f"Total candidates to fetch meta: {len(candidates)}")

    tab = get_instagram_tab()
    if not tab:
        print("Error: No Instagram tab found")
        return

    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=40_000_000) as ws:
        for idx, item in enumerate(candidates, 1):
            code = item["code"]
            js = f"""
            (async () => {{
                try {{
                    const res = await fetch('/reel/{code}/');
                    if (res.ok) {{
                        const text = await res.text();
                        const descMatch = text.match(/<meta property="og:description" content="([^"]+)"/);
                        const titleMatch = text.match(/<meta property="og:title" content="([^"]+)"/);
                        return {{
                            ok: true,
                            title: titleMatch ? titleMatch[1] : '',
                            desc: descMatch ? descMatch[1] : ''
                        }};
                    }}
                    return {{ ok: false, status: res.status }};
                }} catch(e) {{
                    return {{ ok: false, error: e.message }};
                }}
            }})()
            """
            msg = {
                "id": idx,
                "method": "Runtime.evaluate",
                "params": {"expression": js, "awaitPromise": True, "returnByValue": True}
            }
            await ws.send(json.dumps(msg))
            resp = await ws.recv()
            data = json.loads(resp).get('result', {}).get('result', {}).get('value', {})
            if data.get('ok'):
                raw_title = html.unescape(data.get('title', ''))
                raw_desc = html.unescape(data.get('desc', ''))
                caption = ""
                if 'on Instagram: "' in raw_title:
                    caption = raw_title.split('on Instagram: "', 1)[1].rstrip('"')
                elif 'on Instagram: “' in raw_title:
                    caption = raw_title.split('on Instagram: “', 1)[1].rstrip('”')
                else:
                    caption = raw_desc

                item["title"] = raw_title
                item["caption"] = caption
                item["meta_desc"] = raw_desc
                print(f"[{idx}/{len(candidates)}] @{item['username']} ({item['outlier_mult']}x) -> {repr(caption[:60])}")
            else:
                print(f"[{idx}/{len(candidates)}] @{item['username']} Failed: {data}")
            await asyncio.sleep(0.3)

    output_path = r"C:\projects\Antigravity Bazino\Publish-Studio\ig-viral\game_collection_viral_meta.json"
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(candidates, f, ensure_ascii=False, indent=2)
    print(f"Saved {len(candidates)} game collection viral items to {output_path}")

if __name__ == "__main__":
    asyncio.run(main())
