import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

# Clean proxy variables for local loopback
for k in ["ALL_PROXY", "all_proxy", "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy"]:
    os.environ.pop(k, None)

import asyncio
import json
import urllib.request
import websockets

CDP_PORT = 9335

def get_instagram_tab():
    req = urllib.request.Request(f"http://127.0.0.1:{CDP_PORT}/json")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(req, timeout=5) as resp:
        tabs = json.loads(resp.read().decode())
        for t in tabs:
            if t.get('type') == 'page' and 'instagram.com' in t.get('url', ''):
                return t
    return None

async def call_cdp(ws, msg_id, method, params=None):
    payload = {"id": msg_id, "method": method}
    if params is not None:
        payload["params"] = params
    await ws.send(json.dumps(payload))
    while True:
        raw = await ws.recv()
        data = json.loads(raw)
        if data.get("id") == msg_id:
            return data

async def main():
    tab = get_instagram_tab()
    if not tab:
        print("Instagram tab not found on port", CDP_PORT)
        return
    print(f"Found Instagram tab: {tab['title']} -> {tab['url']}")
    ws_url = tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url, max_size=10_000_000) as ws:
        msg_id = 1
        # Evaluate script to get all links, text, and collection names on the page
        js = """
        (() => {
            const links = Array.from(document.querySelectorAll('a')).map(a => ({
                href: a.href,
                text: a.innerText.trim(),
                ariaLabel: a.getAttribute('aria-label') || ''
            }));
            const headings = Array.from(document.querySelectorAll('h1, h2, h3, span, div')).filter(el => {
                const t = el.innerText ? el.innerText.trim() : '';
                return t.includes('game') || t.includes('Game') || t.includes('آموزش');
            }).map(el => ({
                tag: el.tagName,
                text: el.innerText.trim(),
                parentLink: el.closest('a') ? el.closest('a').href : null
            }));
            return {
                url: window.location.href,
                title: document.title,
                links: links.filter(l => l.href.includes('/saved/')),
                matchedHeadings: headings.slice(0, 20)
            };
        })()
        """
        res = await call_cdp(ws, msg_id, "Runtime.evaluate", {
            "expression": js,
            "returnByValue": True,
            "awaitPromise": True
        })
        val = res.get("result", {}).get("result", {}).get("value", {})
        links = val.get("links", [])
        print("=== ALL SAVED COLLECTIONS FOUND ===")
        for l in links:
            print(f"[{l.get('text')}] -> {l.get('href')}")
        headings = val.get("matchedHeadings", [])
        print("\n=== MATCHED HEADINGS ===")
        for h in headings:
            if h.get('parentLink'):
                print(f"Heading [{h.get('text')}] -> {h.get('parentLink')}")

if __name__ == '__main__':
    asyncio.run(main())
