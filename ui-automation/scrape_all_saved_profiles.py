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
    print(f"Scraping {username} -> {url} ...", end=" ", flush=True)
    msg_id += 1
    await call_cdp(ws, msg_id, "Page.navigate", {"url": url})
    await asyncio.sleep(3.5)
    
    js = """
    (() => {
        const metaDesc = document.querySelector('meta[name="description"]')?.content || '';
        const ogDesc = document.querySelector('meta[property="og:description"]')?.content || '';
        const title = document.title || '';
        
        let followersText = '';
        let followingText = '';
        let postsText = '';
        
        // Match from meta desc: "123K Followers, 456 Following, 789 Posts"
        const desc = metaDesc || ogDesc;
        const m = desc.match(/([\\d,\\.KkMm]+)\\s+Followers?,\\s+([\\d,\\.KkMm]+)\\s+Following,\\s+([\\d,\\.KkMm]+)\\s+Posts/i);
        if (m) {
            followersText = m[1];
            followingText = m[2];
            postsText = m[3];
        } else {
            // Try DOM header elements
            const lis = Array.from(document.querySelectorAll('header section ul li, header li'));
            lis.forEach(li => {
                const t = li.innerText;
                if (/follower/i.test(t)) followersText = t;
                else if (/following/i.test(t)) followingText = t;
                else if (/post/i.test(t)) postsText = t;
            });
        }
        
        // Full name / bio
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
    print(f"Followers: {followers_num:,} ({val.get('followersRaw')})")
    return val, msg_id

async def main():
    tab = get_instagram_tab()
    ws_url = tab['webSocketDebuggerUrl']
    
    with open(r"C:\projects\Antigravity Bazino\saved_collections_raw.json", "r", encoding="utf-8") as f:
        collections = json.load(f)
        
    amoozesh_posts = collections.get("amoozesh_game", [])
    game_posts = collections.get("game", [])
    
    # Extract unique usernames
    def extract_user(link):
        m = re.search(r'instagram\.com/([^/]+)/', link)
        return m.group(1) if m else None
        
    amoozesh_users = []
    for p in amoozesh_posts:
        u = extract_user(p)
        if u and u not in amoozesh_users:
            amoozesh_users.append(u)
            
    game_users = []
    for p in game_posts:
        u = extract_user(p)
        if u and u not in game_users:
            game_users.append(u)
            
    all_users = list(dict.fromkeys(amoozesh_users + game_users))
    print(f"Total unique accounts to scrape: {len(all_users)}")
    print(f"Amoozesh Game accounts ({len(amoozesh_users)}): {amoozesh_users}")
    print(f"Game accounts ({len(game_users)}): {game_users}\n")
    
    profiles_data = {}
    
    async with websockets.connect(ws_url, max_size=10_000_000) as ws:
        msg_id = 1
        for u in all_users:
            try:
                prof, msg_id = await scrape_profile(ws, msg_id, u)
                profiles_data[u] = prof
            except Exception as e:
                print(f"Error scraping {u}: {e}")
                profiles_data[u] = {"username": u, "followersCount": 0, "url": f"https://www.instagram.com/{u}/", "error": str(e)}
            await asyncio.sleep(1)
            
    # Save full profiles
    with open(r"C:\projects\Antigravity Bazino\scraped_profiles.json", "w", encoding="utf-8") as f:
        json.dump(profiles_data, f, ensure_ascii=False, indent=2)
    print("\nAll profiles scraped and saved to scraped_profiles.json")

if __name__ == '__main__':
    asyncio.run(main())
