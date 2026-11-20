import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def check():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Click characters button
        js_click = """
        (() => {
            const charBtn = Array.from(document.querySelectorAll('button')).find(b => b.innerText && b.innerText.includes('Characters'));
            if (charBtn) charBtn.click();
            return { clicked: !!charBtn };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_click, 'returnByValue': True}}))
        raw1 = await ws.recv()
        
        await asyncio.sleep(1.5)
        
        js_inspect = """
        (() => {
            const body = document.body.innerText;
            const hasMona = body.includes('Mona') || body.includes('mona');
            const items = Array.from(document.querySelectorAll('.character-card, .character-item, .asset-card, .gallery-item, mat-card')).map(x => x.innerText.trim());
            return { hasMona, bodySample: body.substring(0, 500), items: items.slice(0, 10) };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_inspect, 'returnByValue': True}}))
        raw2 = await ws.recv()
        res = json.loads(raw2).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(check())
