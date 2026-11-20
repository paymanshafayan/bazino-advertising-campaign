import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

EMBED_URL = "https://www.instagram.com/reel/Dca0mE8Sh8b/embed/captioned/"
OUT_FILE = r"C:\Jinus-Sandbox\reference_reel.mp4"

async def fetch_embed():
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    req = urllib.request.Request(f"http://127.0.0.1:9335/json/new?{EMBED_URL}", method='PUT')
    tab_data = json.loads(opener.open(req).read().decode())
    tab_id = tab_data['id']
    ws_url = tab_data['webSocketDebuggerUrl']
    print(f"Opened embed tab: {tab_id}")
    
    try:
        async with websockets.connect(ws_url) as ws:
            await asyncio.sleep(4)
            
            # Inspect embed DOM
            js = """
            (() => {
                const video = document.querySelector('video');
                const caption = document.querySelector('.Caption, .EmbeddedCaption, .CaptionComments')?.innerText;
                const allText = document.body.innerText;
                
                return {
                    videoSrc: video ? (video.src || video.currentSrc) : null,
                    caption: caption,
                    allText: allText.replace(/\\n/g, ' | ')
                };
            })()
            """
            await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
            raw1 = await ws.recv()
            info = json.loads(raw1).get('result', {}).get('result', {}).get('value')
            print("Embed info:", json.dumps(info, indent=2))
            
            vid_url = info.get('videoSrc')
            if vid_url:
                print(f"Downloading from {vid_url[:80]}...")
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
    asyncio.run(fetch_embed())
