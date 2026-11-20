import urllib.request
import json
import jwt
import time
import sys

sys.stdout.reconfigure(encoding='utf-8')

APP_ID = "5185952"
KEY_PATH = r"C:\projects\bazino-app.pem"
REPO_OWNER = "paymanshafayan"
REPO_NAME = "bazino-gamenet-portal"

with open(KEY_PATH, "r", encoding="utf-8") as f:
    private_key = f.read()

now = int(time.time())
payload = {"iat": now - 60, "exp": now + 600, "iss": APP_ID}
encoded_jwt = jwt.encode(payload, private_key, algorithm="RS256")

# Get token
req = urllib.request.Request(
    f"https://api.github.com/app/installations/167818802/access_tokens",
    headers={"Authorization": f"Bearer {encoded_jwt}", "Accept": "application/vnd.github.v3+json", "User-Agent": "BazinoApp"},
    data=b""
)
token = json.loads(urllib.request.urlopen(req).read().decode()).get("token")

# List branches
req = urllib.request.Request(
    f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/branches",
    headers={"Authorization": f"Bearer {token}", "Accept": "application/vnd.github.v3+json", "User-Agent": "BazinoApp"}
)
branches = json.loads(urllib.request.urlopen(req).read().decode())
print("Branches in bazino-gamenet-portal:", [b.get("name") for b in branches])
