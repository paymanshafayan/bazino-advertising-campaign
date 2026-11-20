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
        
        # Enable Network and Console to see what happens
        await call_cdp(ws, msg_id, "Network.enable"); msg_id += 1
        
        print("Testing mouse wheel scrolling on active page...")
        for i in range(10):
            # Dispatch real mouse wheel
            await call_cdp(ws, msg_id, "Input.dispatchMouseEvent", {
                "type": "mouseWheel",
                "x": 600,
                "y": 400,
                "deltaX": 0,
                "deltaY": 800
            })
            msg_id += 1
            await asyncio.sleep(1)
            
            # Check unique links count
            res = await call_cdp(ws, msg_id, "Runtime.evaluate", {
                "expression": "Array.from(document.querySelectorAll('a')).map(a => a.href).filter(h => h.includes('/p/') || h.includes('/reel/')).length",
                "returnByValue": True
            })
            msg_id += 1
            count = res.get("result", {}).get("result", {}).get("value", 0)
            print(f"Wheel tick {i+1}: links count = {count}")
            if count > 12:
                print(">>> SUCCESS: New posts loaded!")
                break

if __name__ == '__main__':
    asyncio.run(main())
