import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def inspect_agent_drawer():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            // Find all text and interactive elements in any open drawer / right panel
            const drawer = document.querySelector('flow-agent-settings, flow-settings-drawer, mat-sidenav[position="end"], .agent-settings');
            
            const radios = Array.from(document.querySelectorAll('mat-radio-button, [role="radio"]')).map(r => ({
                id: r.id,
                text: r.innerText.replace(/\\n/g, ' ').trim(),
                checked: r.classList.contains('mat-mdc-radio-checked') || r.getAttribute('aria-checked') === 'true'
            }));
            
            const buttons = Array.from(document.querySelectorAll('mat-button-toggle, [role="button"], button')).filter(b => {
                const txt = b.innerText || '';
                return txt.includes('9:16') || txt.includes('16:9') || txt.includes('1:1') || txt.includes('Portrait') || txt.includes('Save');
            }).map(b => ({
                id: b.id,
                text: b.innerText.replace(/\\n/g, ' ').trim(),
                selected: b.classList.contains('mat-button-toggle-checked') || b.getAttribute('aria-pressed') === 'true'
            }));
            
            return {
                radios: radios,
                aspectRatios: buttons,
                drawerText: drawer ? drawer.innerText.replace(/\\n/g, ' ') : null
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw = await ws.recv()
        res = json.loads(raw).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(inspect_agent_drawer())
