import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def inspect_video_tiles():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            const tiles = Array.from(document.querySelectorAll('flow-video-tile')).map((t, idx) => {
                const img = t.querySelector('img');
                const video = t.querySelector('video');
                const btns = Array.from(t.querySelectorAll('button')).map(b => b.getAttribute('aria-label') || b.innerText.trim());
                return {
                    index: idx,
                    outerHTML: t.outerHTML.substring(0, 300),
                    imgSrc: img ? img.src : null,
                    videoSrc: video ? (video.src || video.currentSrc) : null,
                    buttons: btns,
                    text: t.innerText.replace(/\\n/g, ' ').trim()
                };
            });
            return { count: tiles.length, tiles: tiles };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw1 = await ws.recv()
        res = json.loads(raw1).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(inspect_video_tiles())
