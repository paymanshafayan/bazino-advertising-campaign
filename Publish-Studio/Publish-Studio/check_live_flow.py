import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def check_live_status():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            const editor = document.querySelector('div.ProseMirror');
            const btn = document.querySelector('button[aria-label="Start generation"]') || document.querySelector('button.generate-icon-button');
            
            // Check for progress or loading bars
            const progress = Array.from(document.querySelectorAll('.loading-percentage, [role="progressbar"], .loader, .generation-status, flow-generation-tile, .generating')).map(el => el.innerText.replace(/\\n/g, ' ').trim()).filter(Boolean);
            
            // Check all video elements
            const videos = Array.from(document.querySelectorAll('video')).map(v => ({
                src: v.src || v.currentSrc,
                duration: v.duration,
                paused: v.paused
            }));
            
            // Check recent cards/tiles in workspace
            const tiles = Array.from(document.querySelectorAll('flow-video-tile, .video-tile, flow-media-tile, flow-character-tile')).map(t => ({
                tag: t.tagName,
                text: t.innerText.replace(/\\n/g, ' ').trim(),
                aria: t.getAttribute('aria-label')
            }));
            
            return {
                editorText: editor ? editor.innerText.substring(0, 100) : null,
                btnDisabled: btn ? btn.disabled : null,
                progress: progress,
                videos: videos,
                tiles: tiles.slice(0, 5)
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw = await ws.recv()
        res = json.loads(raw).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(check_live_status())
