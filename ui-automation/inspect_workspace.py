import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def inspect_page():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Dismiss any dialog like "OK, got it"
        js = """
        (() => {
            const okBtn = Array.from(document.querySelectorAll('button')).find(b => b.innerText.includes('OK, got it'));
            if (okBtn) okBtn.click();
            
            return {
                url: window.location.href,
                dismissedOk: !!okBtn,
                bodyText: document.body.innerText.substring(0, 1500),
                allButtons: Array.from(document.querySelectorAll('button')).map(b => ({
                    text: b.innerText.trim(),
                    aria: b.getAttribute('aria-label'),
                    title: b.getAttribute('title')
                }))
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw = await ws.recv()
        res = json.loads(raw).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(inspect_page())
