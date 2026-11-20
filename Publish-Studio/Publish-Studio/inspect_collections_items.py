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

async def inspect_collection(url, name):
    tab = get_instagram_tab()
    ws_url = tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url, max_size=10_000_000) as ws:
        msg_id = 1
        print(f"\n>>> Navigating to {name}: {url}")
        # Navigate to collection
        await call_cdp(ws, msg_id, "Page.navigate", {"url": url})
        msg_id += 1
        await asyncio.sleep(4) # wait for render
        
        # Scroll a bit to load items if needed
        for _ in range(3):
            await call_cdp(ws, msg_id, "Runtime.evaluate", {
                "expression": "window.scrollBy(0, 1000);",
                "returnByValue": True
            })
            msg_id += 1
            await asyncio.sleep(1.5)
            
        # Extract post links
        js = """
        (() => {
            const anchors = Array.from(document.querySelectorAll('a'));
            const postLinks = anchors.map(a => a.href).filter(h => h.includes('/p/') || h.includes('/reel/'));
            const unique = Array.from(new Set(postLinks));
            return {
                title: document.title,
                count: unique.length,
                links: unique
            };
        })()
        """
        res = await call_cdp(ws, msg_id, "Runtime.evaluate", {
            "expression": js,
            "returnByValue": True,
            "awaitPromise": True
        })
        val = res.get("result", {}).get("result", {}).get("value", {})
        print(f"Collection [{name}] has {val.get('count')} unique posts:")
        for l in val.get('links', [])[:15]:
            print("  ", l)
        return val.get('links', [])

async def main():
    # 1. آموزش گیم
    amoozesh_url = "https://www.instagram.com/fartash_v/saved/_/3535547046633112/"
    links1 = await inspect_collection(amoozesh_url, "آموزش گیم")
    
    # 2. game
    game_url = "https://www.instagram.com/fartash_v/saved/game/1050777357950348/"
    links2 = await inspect_collection(game_url, "game")

if __name__ == '__main__':
    asyncio.run(main())
