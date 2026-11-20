import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def click_agent_settings():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            const btn = document.querySelector('button.agent-action-button[aria-label="Settings"]');
            if (btn) {
                btn.click();
                return { clicked: true };
            }
            return { clicked: false };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("Click agent settings:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(1)
        
        # Check drawer / panel
        js_inspect = """
        (() => {
            const drawer = document.querySelector('flow-settings-drawer, .settings-panel, mat-sidenav, .cdk-overlay-pane');
            const allElements = Array.from(document.querySelectorAll('*')).filter(el => {
                const txt = el.innerText || '';
                return txt.includes('9:16') || txt.includes('16:9') || txt.includes('confirmation') || txt.includes('Always ask');
            }).map(el => ({
                tag: el.tagName,
                text: el.innerText.replace(/\\n/g, ' ').substring(0, 80),
                aria: el.getAttribute('aria-label')
            }));
            
            return {
                matched: allElements.slice(0, 10),
                drawerHTML: drawer ? drawer.innerHTML.substring(0, 1000) : null
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_inspect, 'returnByValue': True}}))
        raw2 = await ws.recv()
        res = json.loads(raw2).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(click_agent_settings())
