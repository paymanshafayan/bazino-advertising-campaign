import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def click_mona_add_to_prompt():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Click the "Add to prompt" button for Mona in the popup
        js_click = """
        (() => {
            const btns = Array.from(document.querySelectorAll('.cdk-overlay-container button, .cdk-overlay-container [role="button"]'));
            const monaBtn = btns.find(b => b.innerText && b.innerText.includes('Add to prompt')) ||
                            btns.find(b => b.getAttribute('aria-label') && b.getAttribute('aria-label').includes('Add to prompt'));
            if (monaBtn) {
                monaBtn.click();
                return { clicked: true, text: monaBtn.innerText.trim(), aria: monaBtn.getAttribute('aria-label') };
            }
            return { clicked: false, availableButtons: btns.map(b => b.innerText.trim() || b.getAttribute('aria-label')) };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_click, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("Click res:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(1)
        
        # Check ProseMirror editor content now
        js_editor = """
        (() => {
            const editor = document.querySelector('div.ProseMirror');
            return {
                html: editor ? editor.innerHTML : null,
                text: editor ? editor.innerText : null
            };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_editor, 'returnByValue': True}}))
        raw2 = await ws.recv()
        print("Editor content:", json.loads(raw2).get('result', {}).get('result', {}).get('value'))

if __name__ == "__main__":
    asyncio.run(click_mona_add_to_prompt())
