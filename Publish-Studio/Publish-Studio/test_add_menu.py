import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def test_add_menu_characters():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Step 1: Click Add ingredients
        js_step1 = """
        (() => {
            const btn = document.querySelector('button[aria-label="Add ingredients to the prompt box"]');
            if (btn) btn.click();
            return { clicked: !!btn };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_step1, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("Step 1 (Add ingredients):", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        await asyncio.sleep(0.5)

        # Step 2: Find all items in overlay
        js_step2 = """
        (() => {
            const overlay = document.querySelector('.cdk-overlay-container');
            const items = Array.from(overlay.querySelectorAll('*')).filter(el => el.innerText && el.innerText.trim()).map(el => ({
                tag: el.tagName,
                text: el.innerText.trim().replace(/\\n/g, ' '),
                className: el.className
            }));
            
            // Click Characters item
            const charItem = Array.from(overlay.querySelectorAll('.mat-mdc-list-item, [role="menuitem"], button')).find(el => el.innerText.includes('Characters'));
            if (charItem) charItem.click();
            
            return {
                clickedCharItem: !!charItem,
                overlayItemsCount: items.length,
                sampleItems: items.slice(0, 10)
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_step2, 'returnByValue': True}}))
        raw2 = await ws.recv()
        print("Step 2 (Click Characters):", json.loads(raw2).get('result', {}).get('result', {}).get('value'))
        await asyncio.sleep(1)

        # Step 3: Inspect overlay now
        js_step3 = """
        (() => {
            const overlay = document.querySelector('.cdk-overlay-container');
            const elements = Array.from(overlay.querySelectorAll('*')).map(el => ({
                tag: el.tagName,
                text: el.innerText ? el.innerText.trim().replace(/\\n/g, ' ') : '',
                className: el.className,
                aria: el.getAttribute('aria-label')
            })).filter(x => x.text || x.aria);
            
            return {
                overlayElements: elements.slice(0, 25)
            };
        })()
        """
        await ws.send(json.dumps({'id': 3, 'method': 'Runtime.evaluate', 'params': {'expression': js_step3, 'returnByValue': True}}))
        raw3 = await ws.recv()
        print("Step 3 (Overlay after clicking Characters):", json.dumps(json.loads(raw3).get('result', {}).get('result', {}).get('value'), indent=2))

if __name__ == "__main__":
    asyncio.run(test_add_menu_characters())
