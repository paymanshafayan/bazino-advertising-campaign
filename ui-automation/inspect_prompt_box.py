import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def check_prompt_box():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # First close any open menus/overlays by clicking outside or pressing Escape
        await ws.send(json.dumps({'id': 1, 'method': 'Input.dispatchKeyEvent', 'params': {'type': 'rawKeyDown', 'windowsVirtualKeyCode': 27}}))
        await ws.send(json.dumps({'id': 2, 'method': 'Input.dispatchKeyEvent', 'params': {'type': 'keyUp', 'windowsVirtualKeyCode': 27}}))
        await asyncio.sleep(0.5)

        # Inspect prompt box container in detail
        js_inspect = """
        (() => {
            const editor = document.querySelector('div.ProseMirror');
            const container = editor ? editor.closest('flow-prompt-box, .prompt-box, form, .input-container') || editor.parentElement.parentElement : null;
            
            return {
                containerTag: container ? container.tagName : null,
                containerClass: container ? container.className : null,
                containerHTML: container ? container.innerHTML.substring(0, 1500) : null
            };
        })()
        """
        await ws.send(json.dumps({'id': 3, 'method': 'Runtime.evaluate', 'params': {'expression': js_inspect, 'returnByValue': True}}))
        raw3 = await ws.recv()
        res = json.loads(raw3).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(check_prompt_box())
