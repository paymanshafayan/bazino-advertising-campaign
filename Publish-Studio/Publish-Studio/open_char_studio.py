import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

CHARACTER_SHEET = r"C:\projects\Antigravity Bazino\Assets\mona\mona-bazino-character-sheet.jpg"

async def setup_character():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Navigate to character studio
        target_url = "https://flow.google.com/project/5afedb73-697f-45bb-9b00-67961ad40402/character"
        print("Navigating to:", target_url)
        await ws.send(json.dumps({'id': 1, 'method': 'Page.navigate', 'params': {'url': target_url}}))
        raw = await ws.recv()
        print("Navigate response:", raw)
        
        await asyncio.sleep(4)
        
        # Check elements
        js = """
        (() => {
            const fileInputs = Array.from(document.querySelectorAll('input[type="file"]')).map(i => ({
                id: i.id,
                className: i.className,
                accept: i.getAttribute('accept')
            }));
            const nameInputs = Array.from(document.querySelectorAll('input')).map(i => ({
                placeholder: i.placeholder,
                aria: i.getAttribute('aria-label'),
                className: i.className,
                type: i.type
            }));
            const buttons = Array.from(document.querySelectorAll('button')).map(b => b.innerText.trim()).filter(Boolean);
            return { fileInputs, nameInputs, buttons, currentUrl: window.location.href };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw2 = await ws.recv()
        res = json.loads(raw2).get('result', {}).get('result', {}).get('value')
        print("Character Studio Elements:\n", json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(setup_character())
