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
    # Load analyzed outliers
    collections_path = r"C:\projects\Antigravity Bazino\complete_collections_data.json"
    creators_path = r"C:\projects\Antigravity Bazino\Publish-Studio\all_collections_creators_ranked.json"

    with open(collections_path, "r", encoding="utf-8") as f:
        col_data = json.load(f)

    with open(creators_path, "r", encoding="utf-8") as f:
        creators = {c["username"]: c for c in json.load(f)}

    all_posts = []
    for col_name, cinfo in col_data.items():
        for item in cinfo["items"]:
            u = item.get("user") or {}
            uname = u.get("username")
            creator_info = creators.get(uname, {})
            followers = creator_info.get("followersCount", 0)
            likes = item.get("like_count") or 0
            comments = item.get("comment_count") or 0
            total_eng = likes + comments
            expected_eng = max(followers * 0.025, 100) if followers > 0 else 500
            outlier_mult = total_eng / expected_eng
            all_posts.append({
                "code": item.get("code"),
                "collection": col_name,
                "username": uname,
                "full_name": u.get("full_name") or creator_info.get("full_name", ""),
                "followers": followers,
                "followersRaw": creator_info.get("followersRaw", "0"),
                "likes": likes,
                "comments": comments,
                "total_eng": total_eng,
                "outlier_mult": round(outlier_mult, 2),
                "url": f"https://www.instagram.com/reel/{item.get('code')}/"
            })

    # Sort amoozesh_game and select top 20
    amoozesh = [p for p in all_posts if p["collection"] == "amoozesh_game"]
    amoozesh_top = sorted(amoozesh, key=lambda x: (x["outlier_mult"], x["likes"]), reverse=True)[:20]

    # Select gaming net / console / gaming creators in general game collection
    game_posts = [p for p in all_posts if p["collection"] == "game"]
    # Filter for accounts with Persian or gaming keywords in full_name or username
    persian_gaming_in_game = []
    for p in game_posts:
        fn = p["full_name"] or ""
        un = p["username"] or ""
        # Check if Persian letters or gaming keywords
        is_relevant = any(c in fn for c in "ابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی") or any(kw in un.lower() for kw in ["game", "play", "ps5", "fifa", "fut", "tech"])
        if is_relevant:
            persian_gaming_in_game.append(p)
    
    game_top = sorted(persian_gaming_in_game, key=lambda x: (x["outlier_mult"], x["likes"]), reverse=True)[:10]

    target_candidates = amoozesh_top + game_top
    # Remove duplicate codes
    seen_codes = set()
    unique_candidates = []
    for c in target_candidates:
        if c["code"] not in seen_codes:
            seen_codes.add(c["code"])
            unique_candidates.append(c)

    print(f"Total target candidates to fetch meta for: {len(unique_candidates)}")

    tab = get_instagram_tab()
    if not tab:
        print("Error: No Instagram tab found")
        return

    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=40_000_000) as ws:
        for idx, item in enumerate(unique_candidates, 1):
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
                # Extract caption from title: "Username on Instagram: "Caption""
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
                print(f"[{idx}/{len(unique_candidates)}] @{item['username']} ({item['outlier_mult']}x) -> {repr(caption[:60])}")
            else:
                print(f"[{idx}/{len(unique_candidates)}] @{item['username']} Failed: {data}")
            await asyncio.sleep(0.4)

    output_path = r"C:\projects\Antigravity Bazino\Publish-Studio\ig-viral\candidates_with_meta.json"
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(unique_candidates, f, ensure_ascii=False, indent=2)
    print(f"Successfully saved {len(unique_candidates)} candidates to {output_path}")

if __name__ == "__main__":
    asyncio.run(main())
