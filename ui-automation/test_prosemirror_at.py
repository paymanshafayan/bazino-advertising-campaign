import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def test_prosemirror():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Focus ProseMirror editor
        js_focus = """
        (() => {
            const editor = document.querySelector('div.ProseMirror');
            if (editor) {
                editor.focus();
                return { focused: true };
            }
            return { focused: false };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_focus, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("Focus:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        # Insert text '@'
        await ws.send(json.dumps({'id': 2, 'method': 'Input.insertText', 'params': {'text': '@'}}))
        await ws.recv()
        await asyncio.sleep(1)
        
        # Check what popups or autocomplete appeared
        js_check_popup = """
        (() => {
            const editor = document.querySelector('div.ProseMirror');
            const popups = Array.from(document.querySelectorAll('.cdk-overlay-container *, .autocomplete *, [role="listbox"] *')).map(el => ({
                text: el.innerText ? el.innerText.trim() : '',
                aria: el.getAttribute('aria-label')
            })).filter(x => x.text || x.aria);
            
            return {
                editorText: editor ? editor.innerText : null,
                popups: popups.slice(0, 15)
            };
        })()
        """
        await ws.send(json.dumps({'id': 3, 'method': 'Runtime.evaluate', 'params': {'expression': js_check_popup, 'returnByValue': True}}))
        raw3 = await ws.recv()
        res = json.loads(raw3).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(test_prosemirror())
