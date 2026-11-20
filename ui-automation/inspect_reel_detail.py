import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

TARGET_URL = "https://www.instagram.com/reel/Dca0mE8Sh8b/"

async def inspect_reel_detail():
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    req = urllib.request.Request(f"http://127.0.0.1:9335/json/new?{TARGET_URL}", method='PUT')
    tab_data = json.loads(opener.open(req).read().decode())
    tab_id = tab_data['id']
    ws_url = tab_data['webSocketDebuggerUrl']
    print(f"Opened tab: {tab_id}")
    
    try:
        async with websockets.connect(ws_url) as ws:
            # Wait for Instagram page to load
            await asyncio.sleep(5)
            
            # Inspect all video elements, buttons, text, and extract video stream
            js = """
            (() => {
                const videos = Array.from(document.querySelectorAll('video')).map(v => ({
                    src: v.src,
                    currentSrc: v.currentSrc,
                    duration: v.duration,
                    paused: v.paused,
                    videoWidth: v.videoWidth,
                    videoHeight: v.videoHeight
                }));
                
                // Get all text in the main dialog / reel view
                const textNodes = Array.from(document.querySelectorAll('article, main, [role="main"]')).map(el => el.innerText.replace(/\\n/g, ' '));
                
                return {
                    videos: videos,
                    mainText: textNodes
                };
            })()
            """
            await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
            raw1 = await ws.recv()
            res = json.loads(raw1).get('result', {}).get('result', {}).get('value')
            print(json.dumps(res, indent=2))
            
            # Also capture a screenshot of the reference reel page to inspect the visual layout
            await ws.send(json.dumps({'id': 2, 'method': 'Page.captureScreenshot', 'params': {'format': 'jpeg', 'quality': 80}}))
            raw2 = await ws.recv()
            b64_data = json.loads(raw2).get('result', {}).get('data')
            if b64_data:
                import base64
                screenshot_path = r"C:\Jinus-Sandbox\reference_reel_screenshot.jpg"
                with open(screenshot_path, "wb") as f:
                    f.write(base64.b64decode(b64_data))
                print(f"Saved screenshot: {screenshot_path}")
                
    finally:
        close_req = urllib.request.Request(f"http://127.0.0.1:9335/json/close/{tab_id}", method='GET')
        opener.open(close_req)
        print("Closed tab")

if __name__ == "__main__":
    asyncio.run(inspect_reel_detail())
