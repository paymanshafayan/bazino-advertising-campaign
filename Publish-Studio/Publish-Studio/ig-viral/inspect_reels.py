import urllib.request
import json
import sys

sys.stdout.reconfigure(encoding='utf-8')

req = urllib.request.urlopen('http://127.0.0.1:9335/json')
tabs = json.loads(req.read().decode('utf-8'))
for t in tabs:
    if t.get('type') == 'page':
        print(f"Tab ID: {t.get('id')}, URL: {t.get('url')}")
