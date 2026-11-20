import os
import sys
import base64
import json
import asyncio
import urllib.request
import urllib.parse
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

TARGET_URL = "https://www.instagram.com/reel/Dca0mE8Sh8b/"
OUT_FILE = r"C:\Jinus-Sandbox\original_ref_dca0me8sh8b.mp4"

async def get_mp4_direct():
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    req = urllib.request.Request(f"http://127.0.0.1:9335/json/new?{TARGET_URL}", method='PUT')
    tab_data = json.loads(opener.open(req).read().decode())
    tab_id = tab_data['id']
    ws_url = tab_data['webSocketDebuggerUrl']
    print(f"Opened tab: {tab_id}")
    
    mp4_url = None
    
    try:
        async with websockets.connect(ws_url, max_size=100*1024*1024) as ws:
            # Enable network to intercept media URLs
            await ws.send(json.dumps({'id': 1, 'method': 'Network.enable'}))
            await ws.recv()
            
            # Listen for network requests
            start_t = asyncio.get_event_loop().time()
            while asyncio.get_event_loop().time() - start_t < 10:
                try:
                    msg = await asyncio.wait_for(ws.recv(), timeout=1.0)
                    data = json.loads(msg)
                    method = data.get('method', '')
                    if method == 'Network.requestWillBeSent':
                        url = data.get('params', {}).get('request', {}).get('url', '')
                        if ('.mp4' in url or 'bytestart' in url) and 'cdninstagram.com' in url:
                            print(f"Found MP4 URL: {url[:100]}...")
                            mp4_url = url
                            break
                    elif method == 'Network.responseReceived':
                        url = data.get('params', {}).get('response', {}).get('url', '')
                        mime = data.get('params', {}).get('response', {}).get('mimeType', '')
                        if 'video' in mime and 'cdninstagram.com' in url:
                            print(f"Found Video response: {url[:100]}...")
                            mp4_url = url
                            break
                except asyncio.TimeoutError:
                    js_play = "document.querySelector('video')?.play();"
                    await ws.send(json.dumps({'id': 99, 'method': 'Runtime.evaluate', 'params': {'expression': js_play}}))
                    await ws.recv()
                    
            if not mp4_url:
                print("Could not find MP4 URL.")
                return
                
            parsed = urllib.parse.urlparse(mp4_url)
            qs = urllib.parse.parse_qs(parsed.query)
            qs.pop('bytestart', None)
            qs.pop('byteend', None)
            clean_query = urllib.parse.urlencode(qs, doseq=True)
            clean_url = urllib.parse.urlunparse(parsed._replace(query=clean_query))
            
            # Navigate the tab directly to clean_url!
            print(f"Navigating tab to clean MP4 URL...")
            await ws.send(json.dumps({'id': 2, 'method': 'Page.navigate', 'params': {'url': clean_url}}))
            await ws.recv()
            await asyncio.sleep(4)
            
            # Now fetch same-origin!
            js_fetch = """
            (async () => {
                try {
                    const resp = await fetch(window.location.href);
                    const buffer = await resp.arrayBuffer();
                    const bytes = new Uint8Array(buffer);
                    let binary = '';
                    const len = bytes.byteLength;
                    const chunkSize = 8192;
                    for (let i = 0; i < len; i += chunkSize) {
                        const sub = bytes.subarray(i, Math.min(i + chunkSize, len));
                        binary += String.fromCharCode.apply(null, sub);
                    }
                    return { ok: true, size: len, b64: btoa(binary) };
                } catch (e) {
                    return { ok: false, error: e.toString() };
                }
            })()
            """
            await ws.send(json.dumps({
                'id': 3,
                'method': 'Runtime.evaluate',
                'params': {'expression': js_fetch, 'returnByValue': True, 'awaitPromise': True}
            }))
            raw3 = await ws.recv()
            res = json.loads(raw3).get('result', {}).get('result', {}).get('value')
            
            if not res or not res.get('ok'):
                print("Same-origin fetch failed:", res)
                return
                
            print(f"Same-origin fetch success! Received {res.get('size'):,} bytes.")
            data = base64.b64decode(res['b64'])
            with open(OUT_FILE, 'wb') as f:
                f.write(data)
            print(f"SUCCESS! Saved reference video to {OUT_FILE} ({len(data):,} bytes)")
    finally:
        close_req = urllib.request.Request(f"http://127.0.0.1:9335/json/close/{tab_id}", method='GET')
        opener.open(close_req)
        print("Closed tab")

if __name__ == "__main__":
    asyncio.run(get_mp4_direct())
