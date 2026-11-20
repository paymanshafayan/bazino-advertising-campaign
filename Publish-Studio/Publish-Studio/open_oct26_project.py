import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

PROJ_URL = "https://flow.google.com/project/5afedb73-697f-45bb-9b00-67961ad40402"

async def open_project():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Navigate directly to the project URL
        print(f"Navigating to {PROJ_URL}...")
        await ws.send(json.dumps({'id': 1, 'method': 'Page.navigate', 'params': {'url': PROJ_URL}}))
        raw1 = await ws.recv()
        print("Navigate res:", raw1)
        
        # Wait for page to load
        await asyncio.sleep(5)
        
        # Check DOM state
        js_inspect = """
        (() => {
            const text = document.body.innerText;
            const buttons = Array.from(document.querySelectorAll('button, a')).map(b => b.innerText.trim()).filter(Boolean);
            const characters = Array.from(document.querySelectorAll('*')).filter(el => {
                return (el.innerText && el.innerText.toLowerCase().includes('mona')) ||
                       (el.getAttribute('aria-label') && el.getAttribute('aria-label').toLowerCase().includes('mona'));
            }).map(el => el.tagName + ': ' + (el.getAttribute('aria-label') || el.innerText.substring(0, 50)));
            
            return {
                url: window.location.href,
                hasMona: text.toLowerCase().includes('mona'),
                monaElements: characters.slice(0, 10),
                buttonsSample: buttons.slice(0, 25)
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_inspect, 'returnByValue': True}}))
        raw2 = await ws.recv()
        res = json.loads(raw2).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(open_project())
