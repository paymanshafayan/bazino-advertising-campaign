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
        # Enable Network
        await ws.send(json.dumps({'id': 1, 'method': 'Network.enable'}))
        
        url = "https://www.instagram.com/fartash_v/saved/_/3535547046633112/"
        print("Navigating to:", url)
        await ws.send(json.dumps({'id': 2, 'method': 'Page.navigate', 'params': {'url': url}}))
        
        # Listen for requestWillBeSent for graphql
        req_info = None
        end_time = asyncio.get_event_loop().time() + 8
        while asyncio.get_event_loop().time() < end_time:
            try:
                raw = await asyncio.wait_for(ws.recv(), timeout=1.0)
                data = json.loads(raw)
                if data.get('method') == 'Network.requestWillBeSent':
                    req = data.get('params', {}).get('request', {})
                    if 'graphql' in req.get('url', ''):
                        post_data = req.get('postData', '')
                        if 'MediaCollection' in post_data or 'collection' in post_data or '3535547046633112' in post_data:
                            print("\n>>> MATCHED GRAPHQL REQUEST:")
                            print("URL:", req.get('url'))
                            print("Headers:", json.dumps(req.get('headers', {}), indent=2))
                            print("PostData:", post_data)
                            req_info = req
                            break
            except asyncio.TimeoutError:
                pass

if __name__ == '__main__':
    asyncio.run(main())
