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
PACK_DIR = r"C:\projects\Antigravity Bazino\Publish-Studio\ig-reel\viral-01-paralysis-pack"

async def download_video():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    # Snapshot existing files in C:\Jinus-Media
    before_files = set(glob.glob(os.path.join(DOWNLOAD_DIR, "*")))
    print(f"Existing files in {DOWNLOAD_DIR}: {len(before_files)}")
    
    async with websockets.connect(ws_url) as ws:
        # Step 1: Set download behavior via CDP
        await ws.send(json.dumps({
            'id': 1,
            'method': 'Browser.setDownloadBehavior',
            'params': {
                'behavior': 'allow',
                'downloadPath': DOWNLOAD_DIR,
                'eventsEnabled': True
            }
        }))
        raw1 = await ws.recv()
        print("Set download behavior:", raw1)
        
        # Also enable Page download behavior just in case
        await ws.send(json.dumps({
            'id': 2,
            'method': 'Page.setDownloadBehavior',
            'params': {
                'behavior': 'allow',
                'downloadPath': DOWNLOAD_DIR
            }
        }))
        raw2 = await ws.recv()
        print("Page set download behavior:", raw2)
        
        # Step 2: Click the "Download media" button
        js_click_download = """
        (() => {
            const dlBtn = document.querySelector('button[aria-label="Download media"]');
            if (dlBtn) {
                dlBtn.click();
                return { clicked: true, text: dlBtn.innerText.trim() };
            }
            return { clicked: false };
        })()
        """
        await ws.send(json.dumps({'id': 3, 'method': 'Runtime.evaluate', 'params': {'expression': js_click_download, 'returnByValue': True}}))
        raw3 = await ws.recv()
        print("Click download result:", json.loads(raw3).get('result', {}).get('result', {}).get('value'))
        
        # Step 3: Wait for downloaded file to appear and finish (.crdownload -> .mp4)
        print("Waiting for download to complete in C:\\Jinus-Media...")
        start_t = time.time()
        new_video_file = None
        
        while time.time() - start_t < 60:
            await asyncio.sleep(2)
            current_files = set(glob.glob(os.path.join(DOWNLOAD_DIR, "*")))
            diff = current_files - before_files
            
            # Check if any new file is complete (not ending in .crdownload or .tmp)
            for f in diff:
                if f.endswith('.mp4') or f.endswith('.webm') or f.endswith('.mov'):
                    # Check size stable
                    s1 = os.path.getsize(f)
                    await asyncio.sleep(1)
                    s2 = os.path.getsize(f)
                    if s1 == s2 and s1 > 100000:
                        new_video_file = f
                        break
            if new_video_file:
                break
                
        if new_video_file:
            print(f"[Flow] DOWNLOAD SUCCESS: {new_video_file} ({os.path.getsize(new_video_file):,} bytes)")
            # Copy to pack dir
            import shutil
            pack_dest = os.path.join(PACK_DIR, "viral01_paralysis_flow_raw.mp4")
            shutil.copy2(new_video_file, pack_dest)
            print(f"[Flow] Copied to pack: {pack_dest}")
        else:
            print("[Flow] No new completed video file detected yet. Checking diff:", set(glob.glob(os.path.join(DOWNLOAD_DIR, "*"))) - before_files)

if __name__ == "__main__":
    asyncio.run(download_video())
