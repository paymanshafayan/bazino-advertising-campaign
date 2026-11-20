import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def check_settings():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Click the tune/settings button (aria-label="Settings")
        js_click_tune = """
        (() => {
            const tuneBtn = document.querySelector('button[aria-label="Settings"]');
            if (tuneBtn) {
                tuneBtn.click();
                return { clicked: true };
            }
            return { clicked: false };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_click_tune, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("Click tune:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(1)
        
        # Check what opened in the settings panel
        js_inspect_settings = """
        (() => {
            const drawer = document.querySelector('flow-settings-drawer, .settings-panel, mat-sidenav, .cdk-overlay-pane');
            const radios = Array.from(document.querySelectorAll('mat-radio-button, [role="radio"]')).map(r => ({
                id: r.id,
                text: r.innerText.trim(),
                checked: r.getAttribute('aria-checked') || r.classList.contains('mat-mdc-radio-checked')
            }));
            const toggles = Array.from(document.querySelectorAll('mat-button-toggle, [role="button"], button')).filter(b => {
                const txt = b.innerText || '';
                return txt.includes('9:16') || txt.includes('16:9') || txt.includes('1:1') || txt.includes('Portrait');
            }).map(b => ({
                id: b.id,
                text: b.innerText.trim(),
                selected: b.classList.contains('mat-button-toggle-checked') || b.getAttribute('aria-pressed') === 'true'
            }));
            
            return {
                radios: radios,
                aspectRatios: toggles,
                allButtons: Array.from(document.querySelectorAll('.settings-panel button, flow-settings-drawer button')).map(b => b.innerText.trim())
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_inspect_settings, 'returnByValue': True}}))
        raw2 = await ws.recv()
        res = json.loads(raw2).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(check_settings())
