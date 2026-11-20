import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def rename_and_test_add():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # First check current title
        js_get_name = """
        (() => {
            const footer = document.querySelector('.character-tile-footer');
            return footer ? footer.innerText.trim() : null;
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_get_name, 'returnByValue': True}}))
        raw1 = await ws.recv()
        curr_name = json.loads(raw1).get('result', {}).get('result', {}).get('value')
        print("Current character name:", curr_name)
        
        # If untitled, click Rename
        if curr_name != "Mona":
            print("Renaming to Mona...")
            js_rename = """
            (() => {
                const tile = document.querySelector('flow-character-tile');
                const moreBtn = tile.querySelector('button[aria-label="More options"]');
                if (moreBtn) moreBtn.click();
                return { clickedMore: !!moreBtn };
            })()
            """
            await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_rename, 'returnByValue': True}}))
            await ws.recv()
            await asyncio.sleep(0.5)
            
            js_click_rename_item = """
            (() => {
                const renameItem = Array.from(document.querySelectorAll('.mat-mdc-menu-item, [role="menuitem"]')).find(el => el.innerText.includes('Rename'));
                if (renameItem) {
                    renameItem.click();
                    return { clickedRename: true };
                }
                return { clickedRename: false };
            })()
            """
            await ws.send(json.dumps({'id': 3, 'method': 'Runtime.evaluate', 'params': {'expression': js_click_rename_item, 'returnByValue': True}}))
            await ws.recv()
            await asyncio.sleep(0.5)
            
            # Now find the input for renaming and type "Mona"
            js_type_name = """
            (() => {
                const input = document.querySelector('input:focus, mat-dialog-container input, .character-tile-footer input');
                if (input) {
                    input.value = "Mona";
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true }));
                    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true }));
                    return { foundInput: true, value: input.value };
                }
                // Check if dialog has a save/done button
                return { foundInput: false };
            })()
            """
            await ws.send(json.dumps({'id': 4, 'method': 'Runtime.evaluate', 'params': {'expression': js_type_name, 'returnByValue': True}}))
            raw4 = await ws.recv()
            print("Type name res:", json.loads(raw4).get('result', {}).get('result', {}).get('value'))
            await asyncio.sleep(1)

        # Now test clicking "Add to prompt"
        print("Testing Add to prompt...")
        js_add_prompt = """
        (() => {
            const tile = document.querySelector('flow-character-tile');
            const moreBtn = tile.querySelector('button[aria-label="More options"]');
            if (moreBtn) moreBtn.click();
            return { clickedMore: !!moreBtn };
        })()
        """
        await ws.send(json.dumps({'id': 5, 'method': 'Runtime.evaluate', 'params': {'expression': js_add_prompt, 'returnByValue': True}}))
        await ws.recv()
        await asyncio.sleep(0.5)
        
        js_click_add_item = """
        (() => {
            const addItem = Array.from(document.querySelectorAll('.mat-mdc-menu-item, [role="menuitem"]')).find(el => el.innerText.includes('Add to prompt'));
            if (addItem) {
                addItem.click();
                return { clickedAdd: true };
            }
            return { clickedAdd: false };
        })()
        """
        await ws.send(json.dumps({'id': 6, 'method': 'Runtime.evaluate', 'params': {'expression': js_click_add_item, 'returnByValue': True}}))
        raw6 = await ws.recv()
        print("Click Add to prompt:", json.loads(raw6).get('result', {}).get('result', {}).get('value'))
        
        await asyncio.sleep(1)
        
        # Check prompt box for character chip
        js_check_chips = """
        (() => {
            const chips = Array.from(document.querySelectorAll('.mat-mdc-chip, mat-chip, .ingredient-chip, .chip, [role="row"]')).map(c => c.innerText.replace(/\\n/g, ' ').trim());
            const promptBox = document.querySelector('div.ProseMirror, textarea, input[type="text"]');
            return {
                chips: chips,
                promptText: promptBox ? (promptBox.innerText || promptBox.value) : null
            };
        })()
        """
        await ws.send(json.dumps({'id': 7, 'method': 'Runtime.evaluate', 'params': {'expression': js_check_chips, 'returnByValue': True}}))
        raw7 = await ws.recv()
        print("Prompt state:", json.loads(raw7).get('result', {}).get('result', {}).get('value'))

if __name__ == "__main__":
    asyncio.run(rename_and_test_add())
