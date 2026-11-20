import os
import sys
import glob
import time
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

DOWNLOAD_DIR = r"C:\Jinus-Media"

async def click_dl_native():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    # Snapshot existing files in C:\Users\payman\Downloads and C:\Jinus-Media
    before_media = set(glob.glob(os.path.join(DOWNLOAD_DIR, "*")))
    before_dl = set(glob.glob(r"C:\Users\payman\Downloads\*"))
    
    async with websockets.connect(ws_url) as ws:
        # Step 1: Set download behavior to C:\Jinus-Media
        await ws.send(json.dumps({
            'id': 1,
            'method': 'Browser.setDownloadBehavior',
            'params': {'behavior': 'allow', 'downloadPath': DOWNLOAD_DIR, 'eventsEnabled': True}
        }))
        await ws.recv()
        
        # Step 2: Get coordinates of Download media button
        js_get_btn = """
        (() => {
            const btn = document.querySelector('button[aria-label="Download media"]');
            if (!btn) return null;
            const rect = btn.getBoundingClientRect();
            return {
                left: rect.left, top: rect.top, width: rect.width, height: rect.height,
                x: rect.left + rect.width / 2, y: rect.top + rect.height / 2
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_get_btn, 'returnByValue': True}}))
        raw2 = await ws.recv()
        coords = json.loads(raw2).get('result', {}).get('result', {}).get('value')
        print("Download button coords:", coords)
        
        if not coords:
            print("ERROR: Download button not found!")
            return
            
        cx, cy = coords['x'], coords['y']
        
        # Dispatch native mouse click
        print(f"Dispatching native click at ({cx}, {cy})...")
        await ws.send(json.dumps({'id': 3, 'method': 'Input.dispatchMouseEvent', 'params': {'type': 'mouseMoved', 'x': cx, 'y': cy}}))
        await ws.recv()
        await ws.send(json.dumps({'id': 4, 'method': 'Input.dispatchMouseEvent', 'params': {'type': 'mousePressed', 'x': cx, 'y': cy, 'button': 'left', 'clickCount': 1}}))
        await ws.recv()
        await asyncio.sleep(0.05)
        await ws.send(json.dumps({'id': 5, 'method': 'Input.dispatchMouseEvent', 'params': {'type': 'mouseReleased', 'x': cx, 'y': cy, 'button': 'left', 'clickCount': 1}}))
        await ws.recv()
        
        # Wait up to 15 seconds to see if a download starts
        print("Waiting for download to appear...")
        for i in range(15):
            await asyncio.sleep(1)
            after_media = set(glob.glob(os.path.join(DOWNLOAD_DIR, "*")))
            after_dl = set(glob.glob(r"C:\Users\payman\Downloads\*"))
            
            diff_m = after_media - before_media
            diff_d = after_dl - before_dl
            
            if diff_m or diff_d:
                print(f"NEW FILE FOUND! Media: {diff_m} | Downloads: {diff_d}")
                return
                
        print("No new file appeared after 15 seconds.")

if __name__ == "__main__":
    asyncio.run(click_dl_native())
