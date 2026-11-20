import os
import sys
import json
import asyncio
import urllib.request
import urllib.parse
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

TARGET_URL = "https://www.instagram.com/reel/Dca0mE8Sh8b/"
OUT_FILE = r"C:\Jinus-Sandbox\original_ref_dca0me8sh8b.mp4"

async def extract_mp4():
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    req = urllib.request.Request(f"http://127.0.0.1:9335/json/new?{TARGET_URL}", method='PUT')
    tab_data = json.loads(opener.open(req).read().decode())
    tab_id = tab_data['id']
    ws_url = tab_data['webSocketDebuggerUrl']
    print(f"Opened tab: {tab_id}")
    
    mp4_url = None
    
    try:
        async with websockets.connect(ws_url) as ws:
            # Enable network to intercept media URLs
            await ws.send(json.dumps({'id': 1, 'method': 'Network.enable'}))
            await ws.recv()
            
            # Listen for network requests containing cdninstagram or mp4
            start_t = asyncio.get_event_loop().time()
            while asyncio.get_event_loop().time() - start_t < 12:
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
                    # Trigger play if paused
                    js_play = "document.querySelector('video')?.play();"
                    await ws.send(json.dumps({'id': 99, 'method': 'Runtime.evaluate', 'params': {'expression': js_play}}))
                    await ws.recv()
                    
            if not mp4_url:
                # Try getting from video element
                js_src = """
                (() => {
                    const v = document.querySelector('video');
                    return v ? (v.src || v.currentSrc) : null;
                })()
                """
                await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_src, 'returnByValue': True}}))
                raw2 = await ws.recv()
                src = json.loads(raw2).get('result', {}).get('result', {}).get('value')
                if src and not src.startswith('blob:'):
                    mp4_url = src
                else:
                    print("Video src in DOM is:", src)
                    
            if mp4_url:
                parsed = urllib.parse.urlparse(mp4_url)
                qs = urllib.parse.parse_qs(parsed.query)
                qs.pop('bytestart', None)
                qs.pop('byteend', None)
                clean_query = urllib.parse.urlencode(qs, doseq=True)
                clean_url = urllib.parse.urlunparse(parsed._replace(query=clean_query))
                
                print(f"Downloading video from: {clean_url[:90]}...")
                dl_req = urllib.request.Request(clean_url, headers={
                    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36',
                    'Referer': 'https://www.instagram.com/'
                })
                with opener.open(dl_req, timeout=30) as resp:
                    data = resp.read()
                    with open(OUT_FILE, 'wb') as f:
                        f.write(data)
                print(f"SUCCESS! Downloaded {len(data):,} bytes to {OUT_FILE}")
            else:
                print("Could not find direct MP4 URL from network events.")
    finally:
        close_req = urllib.request.Request(f"http://127.0.0.1:9335/json/close/{tab_id}", method='GET')
        opener.open(close_req)
        print("Closed tab")

if __name__ == "__main__":
    asyncio.run(extract_mp4())
