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

async def test_fetch_post_html(shortcode):
    tab = get_instagram_tab()
    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=40_000_000) as ws:
        js = f"""
        (async () => {{
            try {{
                const res = await fetch('/reel/{shortcode}/');
                if (res.ok) {{
                    const html = await res.text();
                    const descMatch = html.match(/<meta property="og:description" content="([^"]+)"/);
                    const titleMatch = html.match(/<meta property="og:title" content="([^"]+)"/);
                    return {{
                        ok: true,
                        title: titleMatch ? titleMatch[1] : null,
                        desc: descMatch ? descMatch[1] : null
                    }};
                }} else {{
                    return {{ ok: false, status: res.status }};
                }}
            }} catch(e) {{
                return {{ ok: false, error: e.message }};
            }}
        }})()
        """
        msg = {
            "id": 1,
            "method": "Runtime.evaluate",
            "params": {
                "expression": js,
                "awaitPromise": True,
                "returnByValue": True
            }
        }
        await ws.send(json.dumps(msg))
        resp = await ws.recv()
        result = json.loads(resp)
        val = result.get('result', {}).get('result', {}).get('value', {})
        print("HTML Meta extraction:", val)

asyncio.run(test_fetch_post_html("Ddh3_69Mrt0"))
