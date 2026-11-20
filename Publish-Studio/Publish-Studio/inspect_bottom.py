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
        
        js = """
        (() => {
            // Find all buttons, spinners, svgs, or text at bottom
            const buttons = Array.from(document.querySelectorAll('button, [role="button"]')).map(b => ({
                text: b.innerText,
                ariaLabel: b.getAttribute('aria-label'),
                class: b.className
            }));
            
            // Check main content container
            const main = document.querySelector('main');
            const mainChildren = main ? Array.from(main.children).map(c => ({
                tag: c.tagName,
                class: c.className,
                textSample: c.innerText.substring(0, 100)
            })) : [];
            
            // Get all images/thumbnails in main
            const imgs = Array.from(document.querySelectorAll('main img')).map(img => ({
                src: img.src.substring(0, 80),
                alt: img.alt
            }));
            
            return {
                url: window.location.href,
                buttons: buttons,
                mainChildren: mainChildren,
                imgCount: imgs.length
            };
        })()
        """
        res = await call_cdp(ws, msg_id, "Runtime.evaluate", {
            "expression": js,
            "returnByValue": True
        })
        print(json.dumps(res.get("result", {}).get("result", {}).get("value", {}), ensure_ascii=False, indent=2))

if __name__ == '__main__':
    asyncio.run(main())
