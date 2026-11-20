import urllib.request
import json
import jwt
import time
import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

APP_ID = "5185952"
KEY_PATH = r"C:\projects\bazino-app.pem"
REPO_OWNER = "paymanshafayan"
REPO_NAME = "bazino-gamenet-portal"
BRANCH = "arena/d84db5cf-bazino-gamenet-portal"

with open(KEY_PATH, "r", encoding="utf-8") as f:
    private_key = f.read()

now = int(time.time())
payload = {"iat": now - 60, "exp": now + 600, "iss": APP_ID}
encoded_jwt = jwt.encode(payload, private_key, algorithm="RS256")

req = urllib.request.Request(
    f"https://api.github.com/app/installations/167818802/access_tokens",
    headers={"Authorization": f"Bearer {encoded_jwt}", "Accept": "application/vnd.github.v3+json", "User-Agent": "BazinoApp"},
    data=b""
)
token = json.loads(urllib.request.urlopen(req).read().decode()).get("token")

def list_path(path):
    url = f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/contents/{urllib.parse.quote(path)}?ref={urllib.parse.quote(BRANCH)}"
    req = urllib.request.Request(url, headers={"Authorization": f"Bearer {token}", "Accept": "application/vnd.github.v3+json", "User-Agent": "BazinoApp"})
    try:
        return json.loads(urllib.request.urlopen(req).read().decode())
    except Exception as e:
        print(f"Error {path}: {e}")
        return []

assets = list_path("استودیوی تبلیغات و بازاریابی/Assets")
print("Top-level in Assets:")
for a in assets:
    print(f"[{a.get('type')}] {a.get('name')}")
    if 'carousel' in a.get('name', '').lower():
        sub = list_path(a.get('path'))
        for s in sub:
            print(f"   -> {s.get('name')} ({s.get('path')})")

# Also check root 'assets' or 'Doc'
print("\nAlso checking Doc/carousel-content-pattern-fa.md:")
for d in list_path("استودیوی تبلیغات و بازاریابی/Doc"):
    if 'carousel' in d.get('name', '').lower():
        print(f" - {d.get('path')}")
