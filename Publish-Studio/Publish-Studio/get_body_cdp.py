import os
import sys
import base64
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

TARGET_URL = "https://www.instagram.com/reel/Dca0mE8Sh8b/"
OUT_FILE = r"C:\Jinus-Sandbox\original_ref_dca0me8sh8b.mp4"

async def get_body_cdp():
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    req = urllib.request.Request(f"http://127.0.0.1:9335/json/new?{TARGET_URL}", method='PUT')
    tab_data = json.loads(opener.open(req).read().decode())
    tab_id = tab_data['id']
    ws_url = tab_data['webSocketDebuggerUrl']
    print(f"Opened tab: {tab_id}")
    
    video_request_ids = []
    
    try:
        async with websockets.connect(ws_url, max_size=100*1024*1024) as ws:
            # Enable network to capture response bodies
            await ws.send(json.dumps({'id': 1, 'method': 'Network.enable'}))
            await ws.recv()
            
            # Listen for network requests
            start_t = asyncio.get_event_loop().time()
            while asyncio.get_event_loop().time() - start_t < 10:
                try:
                    msg = await asyncio.wait_for(ws.recv(), timeout=1.0)
                    data = json.loads(msg)
                    method = data.get('method', '')
                    if method == 'Network.responseReceived':
                        url = data.get('params', {}).get('response', {}).get('url', '')
                        mime = data.get('params', {}).get('response', {}).get('mimeType', '')
                        req_id = data.get('params', {}).get('requestId')
                        if ('video' in mime or '.mp4' in url) and 'cdninstagram.com' in url:
                            print(f"Found Video response ID: {req_id} -> {url[:70]}...")
                            video_request_ids.append((req_id, url))
                except asyncio.TimeoutError:
                    js_play = "document.querySelector('video')?.play();"
                    await ws.send(json.dumps({'id': 99, 'method': 'Runtime.evaluate', 'params': {'expression': js_play}}))
                    await ws.recv()
                    
            print(f"Captured {len(video_request_ids)} video responses.")
            for req_id, url in reversed(video_request_ids):
                print(f"Requesting body for {req_id}...")
                await ws.send(json.dumps({'id': 200, 'method': 'Network.getResponseBody', 'params': {'requestId': req_id}}))
                raw_body = await ws.recv()
                body_res = json.loads(raw_body)
                if 'result' in body_res:
                    b64 = body_res['result'].get('body')
                    is_b64 = body_res['result'].get('base64Encoded', False)
                    if b64:
                        data = base64.b64decode(b64) if is_b64 else b64.encode('utf-8')
                        if len(data) > 50000:
                            with open(OUT_FILE, 'wb') as f:
                                f.write(data)
                            print(f"SUCCESS! Saved {len(data):,} bytes via Network.getResponseBody to {OUT_FILE}")
                            return
                else:
                    print("Error getting body:", body_res.get('error'))
    finally:
        close_req = urllib.request.Request(f"http://127.0.0.1:9335/json/close/{tab_id}", method='GET')
        opener.open(close_req)
        print("Closed tab")

if __name__ == "__main__":
    asyncio.run(get_body_cdp())
