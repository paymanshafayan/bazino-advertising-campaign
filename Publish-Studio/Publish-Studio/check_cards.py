import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

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
    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=10_000_000) as ws:
        msg_id = 1
        
        # Navigate to saved main page
        print("Navigating to saved root...")
        await call_cdp(ws, msg_id, "Page.navigate", {"url": "https://www.instagram.com/fartash_v/saved/"})
        msg_id += 1
        await asyncio.sleep(4)
        
        # Inspect all collection cards by searching for text
        js = """
        (() => {
            const allA = Array.from(document.querySelectorAll('a')).map(a => ({
                href: a.href,
                text: a.innerText.replace(/\\n/g, ' | ')
            }));
            
            const matchedEls = Array.from(document.querySelectorAll('div, span, a, h1, h2, h3')).filter(el => {
                const t = el.innerText || '';
                return t.includes('آموزش گیم') || t.toLowerCase().includes('game');
            }).slice(0, 30).map(el => ({
                tag: el.tagName,
                text: el.innerText.replace(/\\n/g, ' | ').substring(0, 80),
                href: el.tagName === 'A' ? el.href : (el.closest('a') ? el.closest('a').href : null)
            }));
            
            return {
                url: window.location.href,
                allLinksCount: allA.length,
                allLinks: allA,
                matchedEls: matchedEls
            };
        })()
        """
        res = await call_cdp(ws, msg_id, "Runtime.evaluate", {
            "expression": js,
            "returnByValue": True
        })
        msg_id += 1
        data = res.get("result", {}).get("result", {}).get("value", {})
        print("URL:", data.get('url'))
        print("All links count:", data.get('allLinksCount'))
        print("All links:")
        allA = data.get('allLinks', [])
        for a in allA:
            print(f"  [{a['text']}] -> {a['href']}")

if __name__ == '__main__':
    asyncio.run(main())
