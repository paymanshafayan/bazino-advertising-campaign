import time
import json
import base64
import urllib.request
import urllib.parse
import jwt
import sys

sys.stdout.reconfigure(encoding='utf-8')

APP_ID = "5185952"
KEY_PATH = r"C:\projects\bazino-app.pem"
REPO_OWNER = "paymanshafayan"
REPO_NAME = "bazino-gamenet-portal"
BRANCH = "arena/d84db5cf-bazino-gamenet-portal"
FILE_PATH = "استودیوی تبلیغات و بازاریابی/Doc/daily-content-production-guide-prompt.md"

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

# URL encode file path
encoded_path = urllib.parse.quote(FILE_PATH)
api_url = f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/contents/{encoded_path}?ref={urllib.parse.quote(BRANCH)}"
print(f"Fetching: {api_url}")

req = urllib.request.Request(
    api_url,
    headers={"Authorization": f"Bearer {token}", "Accept": "application/vnd.github.v3+json", "User-Agent": "BazinoApp"}
)

try:
    with urllib.request.urlopen(req) as resp:
        data = json.loads(resp.read().decode())
    content = base64.b64decode(data.get("content", "")).decode("utf-8")
    print(f"Successfully retrieved file ({len(content)} characters) from GitHub!")
    
    out_file = r"C:\projects\Antigravity Bazino\Publish-Studio\github_daily_content_production_guide.md"
    with open(out_file, "w", encoding="utf-8") as f:
        f.write(content)
    print(f"Saved to: {out_file}")
    print("\n--- CONTENT PREVIEW ---\n")
    print(content[:1500])
except Exception as e:
    print("Error:", e)
