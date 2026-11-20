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
        
        # Test scrolling down smoothly in small increments like a human
        print("Starting human-like incremental scroll test...")
        
        js_scroll = """
        (() => {
            return new Promise((resolve) => {
                let initialCount = document.querySelectorAll('a[href*="/p/"], a[href*="/reel/"]').length;
                let step = 0;
                let maxSteps = 40;
                
                let interval = setInterval(() => {
                    step++;
                    window.scrollBy({ top: 400, behavior: 'smooth' });
                    
                    let currentCount = document.querySelectorAll('a[href*="/p/"], a[href*="/reel/"]').length;
                    if (currentCount > initialCount) {
                        clearInterval(interval);
                        resolve({ success: true, step: step, count: currentCount });
                        return;
                    }
                    
                    if (step >= maxSteps) {
                        clearInterval(interval);
                        resolve({ success: false, step: step, count: currentCount });
                    }
                }, 400);
            });
        })()
        """
        
        res = await call_cdp(ws, msg_id, "Runtime.evaluate", {
            "expression": js_scroll,
            "returnByValue": True,
            "awaitPromise": True
        })
        msg_id += 1
        print("Incremental scroll result:", res.get("result", {}).get("result", {}).get("value"))

if __name__ == '__main__':
    asyncio.run(main())
