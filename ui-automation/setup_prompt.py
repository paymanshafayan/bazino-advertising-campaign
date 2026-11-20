import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

PROMPT_TEXT = (
    "sitting on a gaming lounge sofa holding a PS5 DualSense controller, "
    "scrolling quickly through game menus on a large 4K screen, looking puzzled and overwhelmed by choice paralysis. "
    "She turns towards the camera, gestures enthusiastically with her hands while smiling confidently, "
    "explaining the solution inside the modern Bazino gaming lounge in Famagusta with wooden slatted walls, "
    "glowing purple and cyan RGB neon lighting, high-energy gaming lounge ambiance, cinematic 9:16 vertical frame, "
    "photorealistic 8k, natural facial expression and fluid lifelike motion."
)

async def setup_prompt_and_check():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Step 1: Ensure Mona chip is in ProseMirror editor
        js_prep = """
        (() => {
            const editor = document.querySelector('div.ProseMirror');
            if (!editor) return { error: 'No editor' };
            
            // Check if Mona chip already exists
            let chip = editor.querySelector('span.mention-chip');
            if (!chip) {
                // If no chip, we can re-add it or inject it
                return { needsChip: true };
            }
            return { hasChip: true, chipText: chip.innerText };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_prep, 'returnByValue': True}}))
        raw1 = await ws.recv()
        status1 = json.loads(raw1).get('result', {}).get('result', {}).get('value')
        print("Editor chip status:", status1)
        
        # If chip is missing, re-attach via @ mention
        if status1.get('needsChip'):
            print("Re-attaching Mona chip...")
            # Click Add ingredients
            await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {
                'expression': "document.querySelector('button[aria-label=\"Add ingredients to the prompt box\"]').click(); true;",
                'returnByValue': True
            }}))
            await ws.recv()
            await asyncio.sleep(0.8)
            
            # Click Add to prompt for Mona
            js_add = """
            (() => {
                const btns = Array.from(document.querySelectorAll('.cdk-overlay-container button, .cdk-overlay-container [role="button"]'));
                const monaBtn = btns.find(b => b.innerText && b.innerText.includes('Add to prompt'));
                if (monaBtn) { monaBtn.click(); return true; }
                return false;
            })()
            """
            await ws.send(json.dumps({'id': 3, 'method': 'Runtime.evaluate', 'params': {'expression': js_add, 'returnByValue': True}}))
            await ws.recv()
            await asyncio.sleep(0.8)

        # Step 2: Focus ProseMirror and type prompt text after the chip
        js_focus_end = """
        (() => {
            const editor = document.querySelector('div.ProseMirror');
            editor.focus();
            
            // Set selection to the end of editor
            const range = document.createRange();
            range.selectNodeContents(editor);
            range.collapse(false); // collapse to end
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
            return true;
        })()
        """
        await ws.send(json.dumps({'id': 4, 'method': 'Runtime.evaluate', 'params': {'expression': js_focus_end, 'returnByValue': True}}))
        await ws.recv()
        
        # Insert the text via Input.insertText
        print("Inserting prompt text...")
        await ws.send(json.dumps({'id': 5, 'method': 'Input.insertText', 'params': {'text': PROMPT_TEXT}}))
        await ws.recv()
        await asyncio.sleep(1)
        
        # Step 3: Check editor content and Generate button
        js_check_btn = """
        (() => {
            const editor = document.querySelector('div.ProseMirror');
            const btn = document.querySelector('button[aria-label="Start generation"]') || document.querySelector('button.generate-icon-button');
            const rect = btn ? btn.getBoundingClientRect() : null;
            
            return {
                editorText: editor ? editor.innerText.substring(0, 150) : null,
                hasBtn: !!btn,
                btnDisabled: btn ? btn.disabled : null,
                coords: rect ? { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 } : null
            };
        })()
        """
        await ws.send(json.dumps({'id': 6, 'method': 'Runtime.evaluate', 'params': {'expression': js_check_btn, 'returnByValue': True}}))
        raw6 = await ws.recv()
        res6 = json.loads(raw6).get('result', {}).get('result', {}).get('value')
        print("Generate button & prompt state:", json.dumps(res6, indent=2))

if __name__ == "__main__":
    asyncio.run(setup_prompt_and_check())
