import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def test_click_generate():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            const btn = document.querySelector('button[aria-label="Start generation"]');
            if (!btn) return { error: 'No button' };
            
            // Try JS click
            btn.click();
            
            return {
                clicked: true,
                disabled: btn.disabled,
                ariaLabel: btn.getAttribute('aria-label'),
                className: btn.className,
                type: btn.type
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("JS click result:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(2)
        
        # Check if anything changed in DOM (new tile, message, loading, etc.)
        js_check = """
        (() => {
            const body = document.body.innerText;
            const hasGenerating = body.includes('Generating') || body.includes('generating') || body.includes('Generating video');
            const tiles = Array.from(document.querySelectorAll('flow-video-tile, .video-tile, flow-media-tile, flow-generation-tile, .generation-status, .loading-percentage, [role="progressbar"]')).map(t => ({
                tag: t.tagName,
                text: t.innerText.replace(/\\n/g, ' ').substring(0, 100),
                className: t.className
            }));
            const dialogs = Array.from(document.querySelectorAll('mat-dialog-container, flow-permission-message')).map(d => d.innerText.replace(/\\n/g, ' '));
            
            return {
                hasGenerating: hasGenerating,
                tiles: tiles,
                dialogs: dialogs,
                bodySnippet: body.substring(0, 400)
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_check, 'returnByValue': True}}))
        raw2 = await ws.recv()
        res = json.loads(raw2).get('result', {}).get('result', {}).get('value')
        print("DOM state after click:", json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(test_click_generate())
