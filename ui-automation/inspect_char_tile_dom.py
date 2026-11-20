import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def inspect_tile():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            const tile = document.querySelector('flow-character-tile');
            if (!tile) return { found: false };
            
            const buttons = Array.from(tile.querySelectorAll('button, [role="button"]')).map(b => ({
                text: b.innerText.trim(),
                aria: b.getAttribute('aria-label'),
                className: b.className
            }));
            
            return {
                found: true,
                outerHTML: tile.outerHTML.substring(0, 500),
                buttons: buttons,
                parentTag: tile.parentElement ? tile.parentElement.tagName : null,
                parentHTML: tile.parentElement ? tile.parentElement.outerHTML.substring(0, 500) : null
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw = await ws.recv()
        res = json.loads(raw).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(inspect_tile())
