import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

TARGET_URL = "https://www.instagram.com/reel/Dca0mE8Sh8b/"
OUT_FILE = r"C:\Jinus-Sandbox\reference_reel.mp4"

async def download_ref():
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    req = urllib.request.Request(f"http://127.0.0.1:9335/json/new?{TARGET_URL}", method='PUT')
    tab_data = json.loads(opener.open(req).read().decode())
    tab_id = tab_data['id']
    ws_url = tab_data['webSocketDebuggerUrl']
    print(f"Opened tab: {tab_id}")
    
    try:
        async with websockets.connect(ws_url) as ws:
            # Enable network to capture video requests
            await ws.send(json.dumps({'id': 1, 'method': 'Network.enable'}))
            await ws.recv()
            
            # Wait for video playback / load
            await asyncio.sleep(4)
            
            # Click play on video if needed
            js_play = """
            (() => {
                const vid = document.querySelector('video');
                if (vid) {
                    vid.muted = true;
                    vid.play();
                    return { src: vid.src || vid.currentSrc, duration: vid.duration };
                }
                return { src: null };
            })()
            """
            await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_play, 'returnByValue': True}}))
            raw2 = await ws.recv()
            vid_info = json.loads(raw2).get('result', {}).get('result', {}).get('value')
            print("Video info in DOM:", vid_info)
            
            vid_url = vid_info.get('src')
            if vid_url:
                print(f"Downloading from {vid_url[:100]}...")
                dl_req = urllib.request.Request(vid_url, headers={'User-Agent': 'Mozilla/5.0'})
                with opener.open(dl_req, timeout=30) as resp:
                    data = resp.read()
                    with open(OUT_FILE, 'wb') as f:
                        f.write(data)
                print(f"SUCCESS: Saved reference reel to {OUT_FILE} ({len(data):,} bytes)")
    finally:
        close_req = urllib.request.Request(f"http://127.0.0.1:9335/json/close/{tab_id}", method='GET')
        opener.open(close_req)
        print("Closed tab")

if __name__ == "__main__":
    asyncio.run(download_ref())
