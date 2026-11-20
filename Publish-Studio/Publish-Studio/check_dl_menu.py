import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def check_download_menu():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Click Download media button
        js_click = """
        (() => {
            const btn = document.querySelector('button[aria-label="Download media"]');
            if (btn) {
                btn.click();
                return { clicked: true };
            }
            return { clicked: false };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_click, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("Click download button:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(1)
        
        # Check menus, popups, dialogs, or links
        js_check_menu = """
        (() => {
            const menuItems = Array.from(document.querySelectorAll('.mat-mdc-menu-item, [role="menuitem"], mat-dialog-container button, .cdk-overlay-container *')).map(el => ({
                tag: el.tagName,
                text: el.innerText ? el.innerText.replace(/\\n/g, ' ').trim() : '',
                aria: el.getAttribute('aria-label'),
                href: el.getAttribute('href')
            })).filter(x => x.text || x.aria || x.href);
            
            return {
                menuItems: menuItems.slice(0, 15)
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_check_menu, 'returnByValue': True}}))
        raw2 = await ws.recv()
        res = json.loads(raw2).get('result', {}).get('result', {}).get('value')
        print("Menu after clicking download:", json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(check_download_menu())
