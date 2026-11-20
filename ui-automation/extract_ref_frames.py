import os
import sys
import json
import asyncio
import urllib.request
import websockets
import base64

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

TARGET_URL = "https://www.instagram.com/reel/Dca0mE8Sh8b/"

async def capture_timeline():
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    req = urllib.request.Request(f"http://127.0.0.1:9335/json/new?{TARGET_URL}", method='PUT')
    tab_data = json.loads(opener.open(req).read().decode())
    tab_id = tab_data['id']
    ws_url = tab_data['webSocketDebuggerUrl']
    
    try:
        async with websockets.connect(ws_url) as ws:
            await asyncio.sleep(4)
            
            # Seek video to 2s, 5s, 8s and capture frame
            for sec in [2, 5, 8]:
                js_seek = f"""
                (() => {{
                    const vid = document.querySelector('video');
                    if (vid) {{
                        vid.currentTime = {sec};
                        vid.pause();
                        return {{ currentTime: vid.currentTime, duration: vid.duration }};
                    }}
                    return null;
                }})()
                """
                await ws.send(json.dumps({'id': 10 + sec, 'method': 'Runtime.evaluate', 'params': {'expression': js_seek, 'returnByValue': True}}))
                await ws.recv()
                await asyncio.sleep(0.8)
                
                # Take screenshot
                await ws.send(json.dumps({'id': 20 + sec, 'method': 'Page.captureScreenshot', 'params': {'format': 'jpeg', 'quality': 70}}))
                raw_shot = await ws.recv()
                data = json.loads(raw_shot).get('result', {}).get('data')
                if data:
                    out_path = rf"C:\Jinus-Sandbox\ref_frame_{sec}s.jpg"
                    with open(out_path, "wb") as f:
                        f.write(base64.b64decode(data))
                    print(f"Saved: {out_path}")
    finally:
        close_req = urllib.request.Request(f"http://127.0.0.1:9335/json/close/{tab_id}", method='GET')
        opener.open(close_req)
        print("Closed tab")

if __name__ == "__main__":
    asyncio.run(capture_timeline())
