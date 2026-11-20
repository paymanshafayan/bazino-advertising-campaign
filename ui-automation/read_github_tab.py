import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

for k in ["ALL_PROXY", "all_proxy", "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy"]:
    os.environ.pop(k, None)

import asyncio
import json
import urllib.request
import websockets

async def main():
    await asyncio.sleep(2)
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open('http://127.0.0.1:9335/json').read().decode())
    target_tab = None
    for t in tabs:
        if 'bazino-gamenet-portal' in t.get('url', ''):
            target_tab = t
            break

    if not target_tab:
        print("Target tab not found")
        return

    print("Found tab:", target_tab.get('url'))
    async with websockets.connect(target_tab['webSocketDebuggerUrl'], max_size=50_000_000) as ws:
        js = """
        (() => {
            const el = document.querySelector('article.markdown-body') || document.querySelector('.markdown-body');
            if (el) {
                return { ok: true, text: el.innerText };
            }
            // Check raw line numbers or blob text
            const blobEl = document.querySelector('[data-target="react-app.embeddedData"]') || document.querySelector('#read-only-cursor-text-area');
            if (blobEl) {
                return { ok: true, text: blobEl.innerText || blobEl.value };
            }
            // React embedded data json
            const embedded = document.querySelector('script[data-target="react-app.embeddedData"]');
            if (embedded) {
                try {
                    const parsed = JSON.parse(embedded.textContent);
                    const rawLines = parsed?.payload?.blob?.rawLines;
                    if (rawLines) {
                        return { ok: true, text: rawLines.join('\\n') };
                    }
                } catch(e) {}
            }
            return { ok: false, title: document.title, bodySnippet: document.body.innerText.substring(0, 500) };
        })()
        """
        msg = {
            "id": 1,
            "method": "Runtime.evaluate",
            "params": {"expression": js, "returnByValue": True}
        }
        await ws.send(json.dumps(msg))
        resp = await ws.recv()
        res = json.loads(resp).get('result', {}).get('result', {}).get('value', {})
        print("OK:", res.get('ok'))
        if res.get('ok'):
            text = res.get('text', '')
            print(f"Retrieved {len(text)} characters from GitHub.")
            out_file = r"C:\projects\Antigravity Bazino\Publish-Studio\github_daily_content_production_guide.md"
            with open(out_file, "w", encoding="utf-8") as f:
                f.write(text)
            print(f"Saved to {out_file}")
            print("\nPreview of the guide:\n" + "="*50)
            print(text[:1200])
        else:
            print("Failed:", res)

if __name__ == "__main__":
    asyncio.run(main())
