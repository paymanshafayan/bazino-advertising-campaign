import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def find_all_inputs():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            const allElements = Array.from(document.querySelectorAll('*')).filter(el => {
                return el.getAttribute('contenteditable') === 'true' ||
                       el.tagName === 'TEXTAREA' ||
                       (el.tagName === 'INPUT' && el.type !== 'hidden');
            }).map(el => ({
                tag: el.tagName,
                type: el.type,
                contentEditable: el.getAttribute('contenteditable'),
                aria: el.getAttribute('aria-label'),
                placeholder: el.getAttribute('placeholder'),
                className: el.className
            }));
            
            return {
                url: window.location.href,
                inputs: allElements
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw = await ws.recv()
        res = json.loads(raw).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(find_all_inputs())
