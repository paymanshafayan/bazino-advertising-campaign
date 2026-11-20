import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

for k in ["ALL_PROXY", "all_proxy", "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy"]:
    os.environ.pop(k, None)

import asyncio
import json
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

async def deep_scroll_collection(ws, msg_id, url, name):
    print(f"\n[Deep Scroll] Navigating to {name}: {url}")
    await call_cdp(ws, msg_id, "Page.navigate", {"url": url})
    await asyncio.sleep(4)
    
    prev_count = 0
    stable_repeats = 0
    all_links = set()
    
    for scroll_i in range(15):
        msg_id += 1
        # Extract links
        js = """
        (() => {
            const anchors = Array.from(document.querySelectorAll('a'));
            return anchors.map(a => a.href).filter(h => h.includes('/p/') || h.includes('/reel/'));
        })()
        """
        res = await call_cdp(ws, msg_id, "Runtime.evaluate", {
            "expression": js,
            "returnByValue": True,
            "awaitPromise": True
        })
        current_links = res.get("result", {}).get("result", {}).get("value", [])
        for l in current_links:
            all_links.add(l)
            
        print(f"  Scroll {scroll_i + 1}: found {len(all_links)} total unique posts so far...")
        
        if len(all_links) == prev_count:
            stable_repeats += 1
            if stable_repeats >= 3:
                print("  Reached end of collection.")
                break
        else:
            stable_repeats = 0
            prev_count = len(all_links)
            
        msg_id += 1
        await call_cdp(ws, msg_id, "Runtime.evaluate", {
            "expression": "window.scrollBy(0, 1500);",
            "returnByValue": True
        })
        await asyncio.sleep(2)
        
    return all_links, msg_id

async def main():
    tab = get_instagram_tab()
    ws_url = tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url, max_size=10_000_000) as ws:
        msg_id = 1
        
        amoozesh_url = "https://www.instagram.com/fartash_v/saved/_/3535547046633112/"
        links1, msg_id = await deep_scroll_collection(ws, msg_id, amoozesh_url, "آموزش گیم")
        
        game_url = "https://www.instagram.com/fartash_v/saved/game/1050777357950348/"
        links2, msg_id = await deep_scroll_collection(ws, msg_id, game_url, "game")
        
        # Save post links
        result = {
            "amoozesh_game": list(links1),
            "game": list(links2)
        }
        with open(r"C:\projects\Antigravity Bazino\saved_collections_raw.json", "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
        print(f"\nFinished: آموزش گیم has {len(links1)} posts, game has {len(links2)} posts.")

if __name__ == '__main__':
    asyncio.run(main())
