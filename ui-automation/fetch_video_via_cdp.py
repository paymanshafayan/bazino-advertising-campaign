import os
import sys
import base64
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

VIDEO_URL = "https://flow-content.google/video/85920248-23c3-4c2a-8896-ca6d44a68720?Expires=1793373145&KeyName=labs-flow-prod-cdn-key&Signature=tSHGNDlIfMx6wAKfGrEMfleLe8c"
TARGET_MEDIA = r"C:\Jinus-Media\viral01_paralysis_flow_raw.mp4"
TARGET_PACK = r"C:\projects\Antigravity Bazino\Publish-Studio\ig-reel\viral-01-paralysis-pack\viral01_paralysis_flow_raw.mp4"

async def fetch_video():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url, max_size=100*1024*1024) as ws:
        js_fetch = f"""
        (async () => {{
            try {{
                const resp = await fetch("{VIDEO_URL}");
                if (!resp.ok) return {{ ok: false, status: resp.status, statusText: resp.statusText }};
                const buffer = await resp.arrayBuffer();
                const bytes = new Uint8Array(buffer);
                let binary = '';
                const len = bytes.byteLength;
                const chunkSize = 8192;
                for (let i = 0; i < len; i += chunkSize) {{
                    const sub = bytes.subarray(i, Math.min(i + chunkSize, len));
                    binary += String.fromCharCode.apply(null, sub);
                }}
                return {{ ok: true, size: len, b64: btoa(binary) }};
            }} catch (e) {{
                return {{ ok: false, error: e.toString() }};
            }}
        }})()
        """
        print(f"Fetching video via browser session: {VIDEO_URL[:80]}...")
        await ws.send(json.dumps({
            'id': 1,
            'method': 'Runtime.evaluate',
            'params': {'expression': js_fetch, 'returnByValue': True, 'awaitPromise': True}
        }))
        raw1 = await ws.recv()
        res = json.loads(raw1).get('result', {}).get('result', {}).get('value')
        
        if not res or not res.get('ok'):
            print("Fetch failed:", res)
            return
            
        print(f"Fetch success! Received {res.get('size'):,} bytes.")
        data = base64.b64decode(res['b64'])
        
        with open(TARGET_MEDIA, 'wb') as f:
            f.write(data)
        print(f"Saved: {TARGET_MEDIA} ({len(data):,} bytes)")
        
        with open(TARGET_PACK, 'wb') as f:
            f.write(data)
        print(f"Saved: {TARGET_PACK} ({len(data):,} bytes)")

if __name__ == "__main__":
    asyncio.run(fetch_video())
