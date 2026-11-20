import os
import sys
import base64
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

VIDEO_URL = "https://flow-content.google/video/1d67f4fb-6261-4542-ab9f-7fc76b9494c9?Expires=1793372163&KeyName=labs-flow-prod-cdn-key&Signature=MgMorA4WnQtHeHDfvfOWR_NrqPY"
TARGET_MEDIA = r"C:\Jinus-Media\viral01_paralysis_scene2.mp4"

async def fetch_scene2():
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
        await ws.send(json.dumps({
            'id': 1,
            'method': 'Runtime.evaluate',
            'params': {'expression': js_fetch, 'returnByValue': True, 'awaitPromise': True}
        }))
        raw1 = await ws.recv()
        res = json.loads(raw1).get('result', {}).get('result', {}).get('value')
        
        if res and res.get('ok'):
            data = base64.b64decode(res['b64'])
            with open(TARGET_MEDIA, 'wb') as f:
                f.write(data)
            print(f"Saved: {TARGET_MEDIA} ({len(data):,} bytes)")
        else:
            print("Fetch failed:", res)

if __name__ == "__main__":
    asyncio.run(fetch_scene2())
