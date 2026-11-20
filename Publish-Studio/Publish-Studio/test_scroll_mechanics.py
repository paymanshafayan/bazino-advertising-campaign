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
    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=10_000_000) as ws:
        msg_id = 1
        
        # Test scrolling down repeatedly and checking post count
        for i in range(15):
            msg_id += 1
            js = """
            (() => {
                window.scrollTo(0, document.body.scrollHeight);
                const posts = Array.from(document.querySelectorAll('a')).map(a => a.href).filter(h => h.includes('/p/') || h.includes('/reel/'));
                return {
                    scrollY: window.scrollY,
                    scrollHeight: document.body.scrollHeight,
                    uniquePosts: Array.from(new Set(posts)).length
                };
            })()
            """
            res = await call_cdp(ws, msg_id, "Runtime.evaluate", {
                "expression": js,
                "returnByValue": True
            })
            val = res.get("result", {}).get("result", {}).get("value", {})
            print(f"Step {i+1}: scrollY={val.get('scrollY')} height={val.get('scrollHeight')} uniquePosts={val.get('uniquePosts')}")
            
            # Also try dispatching PageDown key or mouse wheel
            msg_id += 1
            await call_cdp(ws, msg_id, "Input.dispatchKeyEvent", {
                "type": "keyDown",
                "windowsVirtualKeyCode": 34, # PageDown
                "key": "PageDown"
            })
            msg_id += 1
            await call_cdp(ws, msg_id, "Input.dispatchKeyEvent", {
                "type": "keyUp",
                "windowsVirtualKeyCode": 34,
                "key": "PageDown"
            })
            
            # Wait 2 seconds for Instagram network response
            await asyncio.sleep(2)

if __name__ == '__main__':
    asyncio.run(main())
