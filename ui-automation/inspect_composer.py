import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def inspect_composer():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Click the "add" ingredient button (aria-label="Add ingredients to the prompt box")
        js_click_add = """
        (() => {
            const addBtn = document.querySelector('button[aria-label="Add ingredients to the prompt box"]');
            if (addBtn) {
                addBtn.click();
                return { clicked: true };
            }
            return { clicked: false };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_click_add, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("Click Add ingredient:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(1.5)
        
        # Check what popover/menu appeared
        js_menu = """
        (() => {
            const menuItems = Array.from(document.querySelectorAll('[role="menuitem"], mat-list-item, .mat-mdc-menu-item, .ingredient-picker-item, div[role="option"]')).map(el => ({
                text: el.innerText.replace(/\\n/g, ' ').trim(),
                aria: el.getAttribute('aria-label'),
                className: el.className
            }));
            
            // Also find prompt editor
            const editor = document.querySelector('div.ProseMirror, textarea, input[type="text"]');
            
            return {
                menuItems: menuItems,
                hasEditor: !!editor,
                editorTag: editor ? editor.tagName : null,
                editorClasses: editor ? editor.className : null
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_menu, 'returnByValue': True}}))
        raw2 = await ws.recv()
        res = json.loads(raw2).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(inspect_composer())
