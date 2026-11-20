import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

for k in ["ALL_PROXY", "all_proxy", "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy"]:
    os.environ.pop(k, None)

import asyncio
import json
import urllib.request
import websockets
import re

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

async def call_cdp(ws, msg_id, method, params=None):
    payload = {"id": msg_id, "method": method}
    if params is not None:
        payload["params"] = params
    await ws.send(json.dumps(payload))
    while True:
        raw = await ws.recv()
        data = json.loads(raw)
        if data.get("id") == msg_id:
            return data

def parse_count(text):
    if not text:
        return 0
    t = text.strip().replace(',', '').lower()
    m = re.search(r'([\d\.]+)\s*([km]?)', t)
    if not m:
        return 0
    val = float(m.group(1))
    unit = m.group(2)
    if unit == 'k':
        return int(val * 1000)
    elif unit == 'm':
        return int(val * 1_000_000)
    return int(val)

async def scrape_profile(ws, msg_id, username):
    url = f"https://www.instagram.com/{username}/"
    print(f"[{username}]", end=" ", flush=True)
    msg_id += 1
    await call_cdp(ws, msg_id, "Page.navigate", {"url": url})
    await asyncio.sleep(1.3)
    
    js = """
    (() => {
        const metaDesc = document.querySelector('meta[name="description"]')?.content || '';
        const ogDesc = document.querySelector('meta[property="og:description"]')?.content || '';
        const title = document.title || '';
        
        let followersText = '';
        let followingText = '';
        let postsText = '';
        
        const desc = metaDesc || ogDesc;
        const m = desc.match(/([\\d,\\.KkMm]+)\\s+Followers?,\\s+([\\d,\\.KkMm]+)\\s+Following,\\s+([\\d,\\.KkMm]+)\\s+Posts/i);
        if (m) {
            followersText = m[1];
            followingText = m[2];
            postsText = m[3];
        } else {
            const lis = Array.from(document.querySelectorAll('header section ul li, header li'));
            lis.forEach(li => {
                const t = li.innerText;
                if (/follower/i.test(t)) followersText = t;
                else if (/following/i.test(t)) followingText = t;
                else if (/post/i.test(t)) postsText = t;
            });
        }
        
        const h1 = document.querySelector('header h1, header h2')?.innerText || '';
        const bioEl = document.querySelector('header section > div:last-child, header section')?.innerText || '';
        
        return {
            username: window.location.pathname.replace(/\\//g, ''),
            metaDesc: desc,
            title: title,
            followersRaw: followersText,
            followingRaw: followingText,
            postsRaw: postsText,
            bioSample: bioEl.substring(0, 150)
        };
    })()
    """
    msg_id += 1
    res = await call_cdp(ws, msg_id, "Runtime.evaluate", {
        "expression": js,
        "returnByValue": True,
        "awaitPromise": True
    })
    val = res.get("result", {}).get("result", {}).get("value", {})
    followers_num = parse_count(val.get('followersRaw', ''))
    val['followersCount'] = followers_num
    val['url'] = url
    print(f"-> {followers_num:,}", flush=True)
    return val, msg_id

async def main():
    with open(r"C:\projects\Antigravity Bazino\complete_collections_data.json", encoding="utf-8") as f:
        data = json.load(f)
        
    c1 = data['amoozesh_game']
    c2 = data['game']
    
    u1 = {}
    for item in c1['items']:
        u = item.get('user')
        if u and u.get('username'):
            uname = u['username']
            if uname not in u1:
                u1[uname] = {"user": u, "post_codes": []}
            u1[uname]["post_codes"].append(item.get('code'))
            
    u2 = {}
    for item in c2['items']:
        u = item.get('user')
        if u and u.get('username'):
            uname = u['username']
            if uname not in u2:
                u2[uname] = {"user": u, "post_codes": []}
            u2[uname]["post_codes"].append(item.get('code'))
            
    print(f"Collection 1 [آموزش گیم]: {len(c1['items'])} posts, {len(u1)} unique creators.")
    print(f"Collection 2 [game]: {len(c2['items'])} posts, {len(u2)} unique creators.")
    
    cache_file = r"C:\projects\Antigravity Bazino\scraped_profiles.json"
    profiles_cache = {}
    if os.path.exists(cache_file):
        with open(cache_file, encoding="utf-8") as f:
            profiles_cache = json.load(f)
            
    # Prioritize: آموزش گیم accounts first, then game accounts
    u1_needed = [u for u in u1.keys() if u not in profiles_cache or profiles_cache[u].get('followersCount', 0) == 0]
    u2_needed = [u for u in u2.keys() if u not in profiles_cache or profiles_cache[u].get('followersCount', 0) == 0 and u not in u1_needed]
    to_scrape = u1_needed + u2_needed
    
    print(f"Need to scrape: {len(u1_needed)} for آموزش گیم, {len(u2_needed)} additional for game. Total: {len(to_scrape)}")
    
    if to_scrape:
        tab = get_instagram_tab()
        async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=10_000_000) as ws:
            msg_id = 1
            for idx, uname in enumerate(to_scrape, 1):
                print(f"[{idx}/{len(to_scrape)}]", end=" ")
                try:
                    prof, msg_id = await scrape_profile(ws, msg_id, uname)
                    profiles_cache[uname] = prof
                except Exception as ex:
                    print(f"Err {uname}: {ex}")
                    profiles_cache[uname] = {
                        "username": uname,
                        "followersCount": 0,
                        "url": f"https://www.instagram.com/{uname}/"
                    }
                    
                # Save cache every 5 profiles or at end
                if idx % 5 == 0 or idx == len(to_scrape):
                    with open(cache_file, "w", encoding="utf-8") as f:
                        json.dump(profiles_cache, f, ensure_ascii=False, indent=2)
                        
                await asyncio.sleep(0.3)

    # Save final cache
    with open(cache_file, "w", encoding="utf-8") as f:
        json.dump(profiles_cache, f, ensure_ascii=False, indent=2)

    # Rank Collection 1 (آموزش گیم)
    c1_ranked = []
    for uname, info in u1.items():
        prof = profiles_cache.get(uname, {})
        c1_ranked.append({
            "username": uname,
            "full_name": info['user'].get('full_name', ''),
            "is_verified": info['user'].get('is_verified', False),
            "followersCount": prof.get('followersCount', 0),
            "followersRaw": prof.get('followersRaw', ''),
            "postsRaw": prof.get('postsRaw', ''),
            "metaDesc": prof.get('metaDesc', ''),
            "url": f"https://www.instagram.com/{uname}/",
            "saved_posts_count": len(info['post_codes'])
        })
    c1_ranked.sort(key=lambda x: x['followersCount'], reverse=True)
    top10_c1 = c1_ranked[:10]
    top10_c1_usernames = set(x['username'] for x in top10_c1)
    
    # Rank Collection 2 (game), EXCLUDING any usernames in Collection 1's top list
    c2_ranked = []
    for uname, info in u2.items():
        if uname in top10_c1_usernames:
            continue # STRICT ZERO OVERLAP
        prof = profiles_cache.get(uname, {})
        c2_ranked.append({
            "username": uname,
            "full_name": info['user'].get('full_name', ''),
            "is_verified": info['user'].get('is_verified', False),
            "followersCount": prof.get('followersCount', 0),
            "followersRaw": prof.get('followersRaw', ''),
            "postsRaw": prof.get('postsRaw', ''),
            "metaDesc": prof.get('metaDesc', ''),
            "url": f"https://www.instagram.com/{uname}/",
            "saved_posts_count": len(info['post_codes'])
        })
    c2_ranked.sort(key=lambda x: x['followersCount'], reverse=True)
    top10_c2 = c2_ranked[:10]
    
    final_output = {
        "amoozesh_game_top10": top10_c1,
        "game_top10": top10_c2,
        "total_posts_amoozesh": len(c1['items']),
        "total_posts_game": len(c2['items']),
        "total_creators_amoozesh": len(u1),
        "total_creators_game": len(u2)
    }
    
    with open(r"C:\projects\Antigravity Bazino\top10_ranked_collections.json", "w", encoding="utf-8") as f:
        json.dump(final_output, f, ensure_ascii=False, indent=2)
        
    print("\n=======================================================")
    print("TOP 10 - آموزش گیم:")
    for i, x in enumerate(top10_c1, 1):
        print(f"  {i}. @{x['username']} ({x['full_name']}): {x['followersCount']:,} followers ({x['saved_posts_count']} posts)")
        
    print("\nTOP 10 - game (NO OVERLAP WITH آموزش گیم):")
    for i, x in enumerate(top10_c2, 1):
        print(f"  {i}. @{x['username']} ({x['full_name']}): {x['followersCount']:,} followers ({x['saved_posts_count']} posts)")

if __name__ == '__main__':
    asyncio.run(main())
