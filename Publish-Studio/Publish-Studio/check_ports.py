import urllib.request
import json
import os

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
for port in [9334, 9335]:
    try:
        req = urllib.request.Request(f'http://127.0.0.1:{port}/json')
        tabs = json.loads(opener.open(req, timeout=3).read().decode())
        print(f"Port {port}: {len(tabs)} tabs")
        for t in tabs:
            if t.get('type') == 'page':
                print("  -", t.get('title'), "|", t.get('url')[:80])
    except Exception as e:
        print(f"Port {port} error:", e)
