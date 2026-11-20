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

async def main():
    tab = get_instagram_tab()
    url = "https://www.instagram.com/fartash_v/saved/_/3535547046633112/"
    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=10_000_000) as ws:
        msg_id = 1
        print("Navigating to collection:", url)
        await call_cdp(ws, msg_id, "Page.navigate", {"url": url})
        msg_id += 1
        await asyncio.sleep(4)
        
        # Check layout & scrolling containers
        js = """
        (() => {
            const sc = document.scrollingElement;
            const scrollableDivs = Array.from(document.querySelectorAll('*')).filter(el => {
                const s = window.getComputedStyle(el);
                return (s.overflowY === 'scroll' || s.overflowY === 'auto') && el.scrollHeight > el.clientHeight;
            }).map(el => ({
                tag: el.tagName,
                class: (el.className && typeof el.className === 'string') ? el.className.substring(0, 50) : '',
                scrollHeight: el.scrollHeight,
                clientHeight: el.clientHeight,
                scrollTop: el.scrollTop
            }));
            
            return {
                windowHeight: window.innerHeight,
                scrollY: window.scrollY,
                docHeight: document.body.scrollHeight,
                scrollingElementHeight: sc ? sc.scrollHeight : 0,
                scrollableContainers: scrollableDivs
            };
        })()
        """
        res = await call_cdp(ws, msg_id, "Runtime.evaluate", {
            "expression": js,
            "returnByValue": True,
            "awaitPromise": True
        })
        print("Layout info:")
        print(json.dumps(res.get("result", {}).get("result", {}).get("value", {}), indent=2))

if __name__ == '__main__':
    asyncio.run(main())
