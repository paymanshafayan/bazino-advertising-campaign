import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def check_projects():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Extract all project links/cards from the page
        js = """
        (() => {
            const links = Array.from(document.querySelectorAll('a[href*="/project/"]')).map(a => ({
                href: a.href,
                text: a.innerText.replace(/\\n/g, ' ').trim(),
                title: a.getAttribute('title') || a.getAttribute('aria-label') || ''
            }));
            const cards = Array.from(document.querySelectorAll('div, li, section')).filter(el => {
                return el.innerText && (el.innerText.includes('Oct 26') || el.innerText.includes('Mona') || el.innerText.includes('Bazino'));
            }).slice(0, 5).map(el => el.innerText.replace(/\\n/g, ' | '));
            return {
                currentUrl: window.location.href,
                links: links,
                cards: cards
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw = await ws.recv()
        res = json.loads(raw).get('result', {}).get('result', {}).get('value')
        print(json.dumps(res, indent=2))

if __name__ == "__main__":
    asyncio.run(check_projects())
