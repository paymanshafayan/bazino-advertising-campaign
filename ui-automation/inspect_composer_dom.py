import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def inspect_composer_dom():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            // Find character name
            const charName = document.querySelector('.character-tile-footer') ? document.querySelector('.character-tile-footer').innerText.trim() : null;
            
            // Look for the bottom composer / prompt container
            const composer = document.querySelector('flow-prompt-box, .prompt-box, .composer, .input-container') ||
                             document.querySelector('[aria-label="Start generation"]')?.closest('div, section, footer');
            
            // Find all editable fields
            const editables = Array.from(document.querySelectorAll('[contenteditable="true"], textarea, input')).map(el => ({
                tag: el.tagName,
                contentEditable: el.getAttribute('contenteditable'),
                placeholder: el.getAttribute('placeholder'),
                ariaLabel: el.getAttribute('aria-label'),
                className: el.className,
                value: el.value || el.innerText
            }));
            
            return {
                charName: charName,
                editables: editables,
                composerHTML: composer ? composer.outerHTML.substring(0, 800) : null
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw = await ws.recv()
        res = json.loads(raw).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(inspect_composer_dom())
