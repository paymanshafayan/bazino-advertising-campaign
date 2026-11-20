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
    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=10_000_000) as ws:
        # Navigate to آموزش گیم
        url = "https://www.instagram.com/fartash_v/saved/_/3535547046633112/"
        print("Navigating to:", url)
        await ws.send(json.dumps({'id': 1, 'method': 'Page.navigate', 'params': {'url': url}}))
        await asyncio.sleep(5)
        
        # Listen to network and scroll
        await ws.send(json.dumps({'id': 2, 'method': 'Network.enable'}))
        
        print("Now scrolling to bottom...")
        await ws.send(json.dumps({'id': 3, 'method': 'Runtime.evaluate', 'params': {
            'expression': 'window.scrollTo(0, document.body.scrollHeight);',
            'returnByValue': True
        }}))
        
        # Collect events for 8 seconds
        end_time = asyncio.get_event_loop().time() + 8
        network_reqs = []
        while asyncio.get_event_loop().time() < end_time:
            try:
                raw = await asyncio.wait_for(ws.recv(), timeout=1.0)
                data = json.loads(raw)
                method = data.get('method', '')
                if method == 'Network.requestWillBeSent':
                    req_url = data.get('params', {}).get('request', {}).get('url', '')
                    if 'graphql' in req_url or 'api' in req_url:
                        network_reqs.append(req_url)
                        print("  >> Network req:", req_url[:100])
            except asyncio.TimeoutError:
                pass
                
        # Check links count now
        await ws.send(json.dumps({'id': 4, 'method': 'Runtime.evaluate', 'params': {
            'expression': 'Array.from(document.querySelectorAll("a[href*=\'/p/\'], a[href*=\'/reel/\']")).length',
            'returnByValue': True
        }}))
        while True:
            raw = await ws.recv()
            data = json.loads(raw)
            if data.get('id') == 4:
                print("Posts count after scroll:", data.get('result', {}).get('result', {}).get('value'))
                break

if __name__ == '__main__':
    asyncio.run(main())
