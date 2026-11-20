import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def enter_project():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Dismiss cookie/promotion if present
        js_dismiss = """
        (() => {
            const cookieBtn = document.querySelector('.glue-cookie-notification-bar__accept');
            if (cookieBtn) cookieBtn.click();
            const closeBtn = document.querySelector('.promotion-banner-close-button');
            if (closeBtn) closeBtn.click();
            
            // Click the first project card
            const projLink = document.querySelector('a[href*="/project/"]');
            if (projLink) {
                projLink.click();
                return { clicked: true, href: projLink.href };
            }
            return { clicked: false };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_dismiss, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("Click result:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(4)
        
        # Check current URL and project UI
        js_check = """
        (() => {
            return {
                url: window.location.href,
                title: document.title,
                hasEditor: !!document.querySelector('div.ProseMirror'),
                buttons: Array.from(document.querySelectorAll('button')).map(b => b.innerText.trim()).filter(Boolean).slice(0, 10)
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_check, 'returnByValue': True}}))
        raw2 = await ws.recv()
        print("Project inside check:", json.loads(raw2).get('result', {}).get('result', {}).get('value'))

if __name__ == "__main__":
    asyncio.run(enter_project())
