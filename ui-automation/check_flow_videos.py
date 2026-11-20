import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def check_videos_page():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Click "Videos" in left navigation
        js_click_vids = """
        (() => {
            const vidBtn = Array.from(document.querySelectorAll('mat-list-item, button')).find(b => b.innerText && b.innerText.includes('Videos'));
            if (vidBtn) {
                vidBtn.click();
                return { clicked: true, text: vidBtn.innerText.trim() };
            }
            return { clicked: false };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_click_vids, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("Click Videos nav:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(2)
        
        # Check all tiles and video elements in the main workspace
        js_inspect = """
        (() => {
            const tiles = Array.from(document.querySelectorAll('flow-video-tile, flow-media-tile, flow-tile-container, .card, mat-card')).map(t => ({
                tag: t.tagName,
                text: t.innerText.replace(/\\n/g, ' ').substring(0, 150),
                aria: t.getAttribute('aria-label'),
                className: t.className
            }));
            
            // Check video elements
            const videos = Array.from(document.querySelectorAll('video')).map(v => ({
                src: v.src || v.currentSrc,
                paused: v.paused,
                duration: v.duration
            }));
            
            // Check any error or warning messages
            const notices = Array.from(document.querySelectorAll('.banner, .notice, .snackbar, .toast, mat-snack-bar-container')).map(el => el.innerText.replace(/\\n/g, ' '));
            
            return {
                tilesCount: tiles.length,
                tiles: tiles.slice(0, 8),
                videosCount: videos.length,
                videos: videos,
                notices: notices
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_inspect, 'returnByValue': True}}))
        raw2 = await ws.recv()
        res = json.loads(raw2).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(check_videos_page())
