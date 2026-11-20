import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def test_attach():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Step 1: Click the character tile's More options
        js_click_more = """
        (() => {
            const tile = document.querySelector('flow-character-tile');
            if (!tile) return { foundTile: false };
            const moreBtn = tile.querySelector('button[aria-label="More options"]');
            if (moreBtn) {
                moreBtn.click();
                return { foundTile: true, clickedMore: true };
            }
            return { foundTile: true, clickedMore: false };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_click_more, 'returnByValue': True}}))
        raw1 = await ws.recv()
        print("More btn click:", json.loads(raw1).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(0.8)
        
        # Step 2: Click "Add to prompt"
        js_click_add = """
        (() => {
            const items = Array.from(document.querySelectorAll('.mat-mdc-menu-item, [role="menuitem"]'));
            const addPromptItem = items.find(el => el.innerText.includes('Add to prompt'));
            if (addPromptItem) {
                addPromptItem.click();
                return { clickedAdd: true, text: addPromptItem.innerText };
            }
            return { clickedAdd: false, availableItems: items.map(x => x.innerText.trim()) };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_click_add, 'returnByValue': True}}))
        raw2 = await ws.recv()
        print("Add to prompt click:", json.loads(raw2).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(1)
        
        # Step 3: Inspect ProseMirror content / chips
        js_inspect_editor = """
        (() => {
            const editor = document.querySelector('div.ProseMirror');
            const chips = Array.from(document.querySelectorAll('.ingredient-chip, .chip, mat-chip, .mat-mdc-chip, [data-type="character"]')).map(c => ({
                text: c.innerText.trim(),
                className: c.className
            }));
            return {
                editorHTML: editor ? editor.innerHTML : null,
                editorText: editor ? editor.innerText : null,
                chips: chips
            };
        })()
        """
        await ws.send(json.dumps({'id': 3, 'method': 'Runtime.evaluate', 'params': {'expression': js_inspect_editor, 'returnByValue': True}}))
        raw3 = await ws.recv()
        print("Editor after Add:", json.loads(raw3).get('result', {}).get('result', {}).get('value'))

if __name__ == "__main__":
    asyncio.run(test_attach())
