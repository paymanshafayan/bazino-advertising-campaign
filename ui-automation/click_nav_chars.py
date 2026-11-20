import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def click_nav_characters():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            const sideNav = document.querySelector('flow-add-menu-side-nav');
            if (!sideNav) return { error: 'No sideNav' };
            const charItem = Array.from(sideNav.querySelectorAll('mat-list-item')).find(el => el.innerText.includes('Characters'));
            if (charItem) {
                charItem.click();
                return { clicked: true, text: charItem.innerText.trim() };
            }
            return { clicked: false };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("Click Characters in sideNav:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(1.5)
        
        js_inspect_content = """
        (() => {
            const content = document.querySelector('flow-add-menu-popover-content');
            if (!content) return { error: 'No content' };
            
            // Find all cards or elements in the main panel of this popover
            const items = Array.from(content.querySelectorAll('.panel-body *, .cards-grid *, .grid *')).map(el => ({
                tag: el.tagName,
                text: el.innerText ? el.innerText.replace(/\\n/g, ' ').trim() : '',
                aria: el.getAttribute('aria-label')
            })).filter(x => x.text || x.aria);
            
            return {
                text: content.innerText.replace(/\\n/g, ' '),
                items: items.slice(0, 15)
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_inspect_content, 'returnByValue': True}}))
        raw2 = await ws.recv()
        print("Content after clicking Characters:", json.dumps(json.loads(raw2).get('result', {}).get('result', {}).get('value'), indent=2))

if __name__ == "__main__":
    asyncio.run(click_nav_characters())
