import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

for k in ["ALL_PROXY", "all_proxy", "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy"]:
    os.environ.pop(k, None)

import asyncio
import json
import html
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

async def main():
    tab = get_instagram_tab()
    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=40_000_000) as ws:
        js = """
        (async () => {
            const res = await fetch('/reel/DcWaWhTCxIa/');
            if (res.ok) {
                const text = await res.text();
                const titleMatch = text.match(/<meta property="og:title" content="([^"]+)"/);
                const descMatch = text.match(/<meta property="og:description" content="([^"]+)"/);
                return {
                    title: titleMatch ? titleMatch[1] : '',
                    desc: descMatch ? descMatch[1] : ''
                };
            }
            return { error: res.status };
        })()
        """
        msg = {
            "id": 1,
            "method": "Runtime.evaluate",
            "params": {"expression": js, "awaitPromise": True, "returnByValue": True}
        }
        await ws.send(json.dumps(msg))
        resp = await ws.recv()
        data = json.loads(resp).get('result', {}).get('result', {}).get('value', {})
        print("Footyverse result:", data)

if __name__ == "__main__":
    asyncio.run(main())
