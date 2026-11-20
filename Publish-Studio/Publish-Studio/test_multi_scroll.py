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

async def main():
    tab = get_instagram_tab()
    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=20_000_000) as ws:
        msg_id = 1
        
        # Navigate
        url = "https://www.instagram.com/fartash_v/saved/_/3535547046633112/"
        print("Navigating to:", url)
        await ws.send(json.dumps({'id': msg_id, 'method': 'Page.navigate', 'params': {'url': url}}))
        msg_id += 1
        await asyncio.sleep(4)
        
        # Now scroll 10 times with 3s pause each
        for i in range(10):
            # Check current DOM posts
            await ws.send(json.dumps({'id': msg_id, 'method': 'Runtime.evaluate', 'params': {
                'expression': 'Array.from(new Set(Array.from(document.querySelectorAll("a[href*=\'/p/\'], a[href*=\'/reel/\']")).map(a => a.href))).length',
                'returnByValue': True
            }}))
            while True:
                raw = await ws.recv()
                d = json.loads(raw)
                if d.get('id') == msg_id:
                    cnt = d.get('result', {}).get('result', {}).get('value', 0)
                    print(f"Before scroll #{i+1}: unique DOM posts = {cnt}", flush=True)
                    break
            msg_id += 1
            
            # Scroll to bottom
            await ws.send(json.dumps({'id': msg_id, 'method': 'Runtime.evaluate', 'params': {
                'expression': 'window.scrollTo(0, document.body.scrollHeight);',
                'returnByValue': True
            }}))
            while True:
                raw = await ws.recv()
                d = json.loads(raw)
                if d.get('id') == msg_id:
                    break
            msg_id += 1
            
            await asyncio.sleep(3.0)

if __name__ == '__main__':
    asyncio.run(main())
