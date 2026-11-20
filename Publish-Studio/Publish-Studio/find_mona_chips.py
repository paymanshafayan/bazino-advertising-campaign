import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def find_chips():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            // Find all elements containing "Mona"
            const monaElements = Array.from(document.querySelectorAll('*')).filter(el => {
                return el.children.length === 0 && el.innerText && el.innerText.includes('Mona');
            }).map(el => ({
                tag: el.tagName,
                className: el.className,
                text: el.innerText,
                parentTag: el.parentElement ? el.parentElement.tagName : null,
                parentClass: el.parentElement ? el.parentElement.className : null
            }));
            
            // Check submit-controls or prompt container
            const submitControls = document.querySelector('.submit-controls')?.parentElement;
            
            return {
                monaElements: monaElements,
                promptAreaText: submitControls ? submitControls.innerText.replace(/\\n/g, ' ') : null,
                promptAreaHTML: submitControls ? submitControls.innerHTML.substring(0, 1000) : null
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw = await ws.recv()
        res = json.loads(raw).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(find_chips())
