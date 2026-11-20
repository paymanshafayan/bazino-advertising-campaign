import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def check_chars():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Click on Characters button in left sidebar
        js_chars = """
        (() => {
            const charBtn = Array.from(document.querySelectorAll('button')).find(b => b.innerText && b.innerText.includes('Characters'));
            if (charBtn) {
                charBtn.click();
                return { clicked: true };
            }
            return { clicked: false };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_chars, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("Click characters:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(2)
        
        # Now see what's in the media/characters list
        js_inspect = """
        (() => {
            const items = Array.from(document.querySelectorAll('*')).filter(el => {
                const txt = el.innerText || '';
                return txt.includes('character') || txt.includes('Character') || txt.includes('Mona') || txt.includes('mona');
            }).slice(0, 15).map(el => ({
                tag: el.tagName,
                className: el.className,
                text: el.innerText ? el.innerText.substring(0, 80).replace(/\\n/g, ' ') : '',
                aria: el.getAttribute('aria-label')
            }));
            
            // Check cards in the main grid
            const cards = Array.from(document.querySelectorAll('[role="gridcell"], [role="listitem"], .card, mat-card')).map(c => ({
                text: c.innerText ? c.innerText.replace(/\\n/g, ' ') : '',
                aria: c.getAttribute('aria-label')
            }));
            
            return {
                matchedItems: items,
                gridCards: cards.slice(0, 10)
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_inspect, 'returnByValue': True}}))
        raw2 = await ws.recv()
        res = json.loads(raw2).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(check_chars())
