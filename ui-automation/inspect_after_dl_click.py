import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def inspect_after_click():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            const overlay = document.querySelector('.cdk-overlay-container');
            const overlayText = overlay ? overlay.innerText.replace(/\\n/g, ' ') : null;
            const menuItems = Array.from(document.querySelectorAll('.mat-mdc-menu-item, [role="menuitem"], .cdk-overlay-container button')).map(el => ({
                text: el.innerText.trim(),
                aria: el.getAttribute('aria-label')
            }));
            
            // Check toasts/snackbars
            const snackbar = document.querySelector('mat-snack-bar-container, .mat-mdc-snack-bar-container');
            
            // Check if there are any links with download attribute or blob:
            const links = Array.from(document.querySelectorAll('a[download], a[href*="blob"], a[href*=".mp4"]')).map(a => ({
                href: a.href,
                download: a.download
            }));
            
            return {
                overlayText: overlayText,
                menuItems: menuItems,
                snackbar: snackbar ? snackbar.innerText.replace(/\\n/g, ' ') : null,
                downloadLinks: links
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw1 = await ws.recv()
        res = json.loads(raw1).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(inspect_after_click())
