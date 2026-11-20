import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

TARGET_URL = "https://www.instagram.com/reel/Dca0mE8Sh8b/"

async def inspect_reel():
    # 1. Open new tab on port 9335
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    req = urllib.request.Request(f"http://127.0.0.1:9335/json/new?{TARGET_URL}", method='PUT')
    tab_data = json.loads(opener.open(req).read().decode())
    tab_id = tab_data['id']
    ws_url = tab_data['webSocketDebuggerUrl']
    print(f"Opened tab: {tab_id} -> {TARGET_URL}")
    
    try:
        async with websockets.connect(ws_url) as ws:
            # Wait 6 seconds for Instagram reel to load
            await asyncio.sleep(6)
            
            # Extract video URL, title, caption, transcript, duration
            js = """
            (() => {
                const title = document.title;
                const video = document.querySelector('video');
                const videoSrc = video ? (video.src || video.currentSrc) : null;
                
                // Caption text
                const captionEl = document.querySelector('h1, [role="main"] h1, span[dir="auto"], article');
                const textNodes = Array.from(document.querySelectorAll('h1, span[dir="auto"]')).map(el => el.innerText.trim()).filter(Boolean);
                
                // Meta description
                const metaDesc = document.querySelector('meta[property="og:description"]')?.content ||
                                 document.querySelector('meta[name="description"]')?.content;
                const ogTitle = document.querySelector('meta[property="og:title"]')?.content;
                
                return {
                    title: title,
                    ogTitle: ogTitle,
                    metaDesc: metaDesc,
                    videoSrc: videoSrc,
                    textSamples: textNodes.slice(0, 10),
                    bodyText: document.body.innerText.substring(0, 1000)
                };
            })()
            """
            await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
            raw1 = await ws.recv()
            res = json.loads(raw1).get('result', {}).get('result', {}).get('value')
            print(json.dumps(res, indent=2))
            
            # If videoSrc exists, download the reference video so we can inspect it frame by frame
            if res.get('videoSrc'):
                print(f"Reference video URL found: {res['videoSrc'][:100]}...")
    finally:
        # Close tab
        close_req = urllib.request.Request(f"http://127.0.0.1:9335/json/close/{tab_id}", method='GET')
        opener.open(close_req)
        print(f"Closed tab {tab_id}")

if __name__ == "__main__":
    asyncio.run(inspect_reel())
