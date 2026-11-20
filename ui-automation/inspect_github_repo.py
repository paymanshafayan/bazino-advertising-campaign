import urllib.request
import json
import jwt
import time
import base64
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

# List root directory of branch
def list_repo_path(path=""):
    encoded_path = urllib.parse.quote(path)
    url = f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/contents/{encoded_path}?ref={urllib.parse.quote(BRANCH)}"
    req = urllib.request.Request(
        url,
        headers={"Authorization": f"Bearer {token}", "Accept": "application/vnd.github.v3+json", "User-Agent": "BazinoApp"}
    )
    try:
        items = json.loads(urllib.request.urlopen(req).read().decode())
        return items
    except Exception as e:
        print(f"Error listing {path}: {e}")
        return []

root_items = list_repo_path("")
print("Root items in repo:")
for it in root_items:
    print(f" - [{it.get('type')}] {it.get('path')}")

# Check inside 'استودیوی تبلیغات و بازاریابی' if present
for it in root_items:
    if 'تبلیغات' in it.get('name', '') or 'Doc' in it.get('name', '') or 'Assets' in it.get('name', ''):
        sub_items = list_repo_path(it.get('path'))
        print(f"\nItems in {it.get('path')}:")
        for sit in sub_items:
            print(f"   -> [{sit.get('type')}] {sit.get('path')}")
