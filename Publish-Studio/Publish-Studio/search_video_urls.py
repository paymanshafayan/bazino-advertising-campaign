import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def search_video_urls():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            // Check performance resource timings for any video or videofx URLs
            const entries = performance.getEntriesByType('resource').map(r => r.name);
            const videoEntries = entries.filter(url => {
                const lower = url.toLowerCase();
                return lower.includes('.mp4') || lower.includes('videofx') || lower.includes('flow-content.google/video') || lower.includes('video/');
            });
            
            // Search all attributes on all elements
            const attrUrls = [];
            const allElements = document.querySelectorAll('*');
            for (const el of allElements) {
                for (const attr of el.attributes) {
                    const val = attr.value;
                    if (typeof val === 'string' && (val.includes('.mp4') || val.includes('videofx') || val.includes('/video/'))) {
                        attrUrls.push({ tag: el.tagName, attr: attr.name, val: val.substring(0, 150) });
                    }
                }
            }
            
            return {
                videoResourceEntries: videoEntries,
                attrUrls: attrUrls
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw1 = await ws.recv()
        res = json.loads(raw1).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(search_video_urls())
