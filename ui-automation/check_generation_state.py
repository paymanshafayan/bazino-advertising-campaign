import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def check_generation_state():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            // Find any progress bars, spinners, loading indicators
            const loaders = Array.from(document.querySelectorAll('.loading-percentage, [role="progressbar"], .loader, .spinner, mat-spinner, flow-spinner')).map(l => ({
                tag: l.tagName,
                text: l.innerText.trim(),
                className: l.className
            }));
            
            // Find all chat/agent turns or messages
            const messages = Array.from(document.querySelectorAll('.chat-turn, .turn, .message, flow-chat-turn, flow-message, .agent-response')).map(m => ({
                tag: m.tagName,
                text: m.innerText.replace(/\\n/g, ' ').substring(0, 150)
            }));
            
            // Find all tiles in canvas / grid
            const tiles = Array.from(document.querySelectorAll('flow-video-tile, flow-media-tile, flow-tile-container, flow-character-tile, flow-generation-tile')).map(t => ({
                tag: t.tagName,
                text: t.innerText.replace(/\\n/g, ' ').substring(0, 100),
                aria: t.getAttribute('aria-label')
            }));
            
            // Find all videos
            const videos = Array.from(document.querySelectorAll('video')).map(v => ({
                src: v.src || v.currentSrc,
                paused: v.paused
            }));
            
            return {
                loaders: loaders,
                messages: messages,
                tilesCount: tiles.length,
                tiles: tiles.slice(-5),
                videosCount: videos.length,
                videos: videos
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw1 = await ws.recv()
        res = json.loads(raw1).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(check_generation_state())
