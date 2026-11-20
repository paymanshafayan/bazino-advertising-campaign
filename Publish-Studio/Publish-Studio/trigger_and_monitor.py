import os
import sys
import json
import time
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

TARGET_LOCAL_MEDIA = r"C:\Jinus-Media\viral01_paralysis_flow_raw.mp4"
TARGET_PACK_MEDIA = r"C:\projects\Antigravity Bazino\Publish-Studio\ig-reel\viral-01-paralysis-pack\viral01_paralysis_flow_raw.mp4"

async def trigger_and_watch():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Step 1: Get coordinates of Generate button
        js_get_btn = """
        (() => {
            const btn = document.querySelector('button[aria-label="Start generation"]') || document.querySelector('button.generate-icon-button');
            if (!btn) return null;
            const rect = btn.getBoundingClientRect();
            return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2, disabled: btn.disabled };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_get_btn, 'returnByValue': True}}))
        raw1 = await ws.recv()
        coords = json.loads(raw1).get('result', {}).get('result', {}).get('value')
        print("[Flow] Generate button coords:", coords)
        
        if not coords or coords.get('disabled'):
            print("[Flow] ERROR: Generate button not ready or disabled!")
            return

        # Step 2: Native mouse click dispatch
        x, y = coords['x'], coords['y']
        print(f"[Flow] Dispatching native mouse click at ({x}, {y})...")
        for event_type in ["mousePressed", "mouseReleased"]:
            await ws.send(json.dumps({
                'id': 2,
                'method': 'Input.dispatchMouseEvent',
                'params': {'type': event_type, 'x': x, 'y': y, 'button': 'left', 'clickCount': 1}
            }))
            await ws.recv()
            await asyncio.sleep(0.1)

        await asyncio.sleep(2)
        
        # Step 3: Handle permission dialog if present
        js_perm = """
        (() => {
            const perm = document.querySelector('flow-permission-message, mat-dialog-container');
            if (!perm) return { dialog: false };
            const btns = Array.from(perm.querySelectorAll('button, .option-row'));
            const approveBtn = btns.find(b => b.innerText.includes('Always approve') || b.innerText.includes('Approve') || b.innerText.includes('Generate') || b.innerText.includes('OK'));
            if (approveBtn) {
                approveBtn.click();
                return { dialog: true, clicked: approveBtn.innerText.trim() };
            }
            return { dialog: true, clicked: false, text: perm.innerText.replace(/\\n/g, ' ') };
        })()
        """
        await ws.send(json.dumps({'id': 3, 'method': 'Runtime.evaluate', 'params': {'expression': js_perm, 'returnByValue': True}}))
        raw3 = await ws.recv()
        print("[Flow] Permission check:", json.loads(raw3).get('result', {}).get('result', {}).get('value'))
        
        # Step 4: Monitor generation loop
        print("[Flow] Entering monitoring loop (max 10 minutes)...")
        start_time = time.time()
        poll_count = 0
        
        while time.time() - start_time < 600:
            poll_count += 1
            await asyncio.sleep(10)
            
            js_status = """
            (() => {
                // Find all video elements
                const videos = Array.from(document.querySelectorAll('video')).map(v => ({
                    src: v.src || v.currentSrc,
                    duration: v.duration,
                    paused: v.paused
                })).filter(v => v.src && !v.src.startsWith('blob:'));
                
                // Find loading / progress indicators
                const loaders = Array.from(document.querySelectorAll('.loading-percentage, [role="progressbar"], .loader, flow-generation-tile, .generation-status')).map(el => el.innerText.replace(/\\n/g, ' ').trim()).filter(Boolean);
                
                // Check recent tiles
                const tiles = Array.from(document.querySelectorAll('flow-video-tile, .video-tile, flow-media-tile')).map(t => ({
                    text: t.innerText.replace(/\\n/g, ' ').trim(),
                    aria: t.getAttribute('aria-label')
                }));
                
                return {
                    videoCount: videos.length,
                    videos: videos,
                    loaders: loaders.slice(0, 5),
                    tilesSample: tiles.slice(0, 3)
                };
            })()
            """
            await ws.send(json.dumps({'id': 100 + poll_count, 'method': 'Runtime.evaluate', 'params': {'expression': js_status, 'returnByValue': True}}))
            raw_status = await ws.recv()
            status = json.loads(raw_status).get('result', {}).get('result', {}).get('value')
            
            elapsed = int(time.time() - start_time)
            print(f"[Flow] T+{elapsed}s | Videos found: {status.get('videoCount')} | Loaders: {status.get('loaders')} | Tiles: {status.get('tilesSample')}")
            
            # If a video with valid src is found, or if downloadable
            if status.get('videoCount', 0) > 0:
                vid_url = status['videos'][0]['src']
                print(f"[Flow] SUCCESS! Generated video found: {vid_url}")
                return vid_url
                
        print("[Flow] Timeout waiting for video generation.")

if __name__ == "__main__":
    asyncio.run(trigger_and_watch())
