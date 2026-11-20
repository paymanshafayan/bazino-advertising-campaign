import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def check_chars_in_menu():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Click "Characters" in the ingredients menu
        js_click_chars = """
        (() => {
            const charItem = Array.from(document.querySelectorAll('.mat-mdc-list-item, [role="menuitem"]')).find(el => el.innerText.includes('Characters'));
            if (charItem) {
                charItem.click();
                return { clicked: true };
            }
            return { clicked: false };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_click_chars, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("Click Characters item:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(2)
        
        # See what cards/characters appear
        js_items = """
        (() => {
            // Find all elements in the open picker/dialog
            const allElements = Array.from(document.querySelectorAll('*')).filter(el => {
                const txt = el.innerText || '';
                return txt.toLowerCase().includes('mona') || txt.toLowerCase().includes('untitled character');
            }).map(el => ({
                tag: el.tagName,
                text: el.innerText.replace(/\\n/g, ' ').substring(0, 100),
                aria: el.getAttribute('aria-label')
            }));
            
            // Look for any grid items, cards, or clickable items in the picker
            const pickerCards = Array.from(document.querySelectorAll('.asset-grid-item, .grid-item, .card, mat-card, [role="gridcell"]')).map(el => ({
                text: el.innerText.replace(/\\n/g, ' ').trim(),
                aria: el.getAttribute('aria-label'),
                className: el.className
            }));
            
            return {
                matched: allElements.slice(0, 10),
                pickerCards: pickerCards.slice(0, 10)
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_items, 'returnByValue': True}}))
        raw2 = await ws.recv()
        res = json.loads(raw2).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(check_chars_in_menu())
