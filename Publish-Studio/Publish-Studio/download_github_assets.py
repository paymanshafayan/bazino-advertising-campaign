import urllib.request
import urllib.parse
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
BASE_DIR = r"C:\projects\Antigravity Bazino\Publish-Studio"

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

def download_github_file(repo_path, local_path):
    os.makedirs(os.path.dirname(local_path), exist_ok=True)
    url = f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/contents/{urllib.parse.quote(repo_path)}?ref={urllib.parse.quote(BRANCH)}"
    req = urllib.request.Request(
        url,
        headers={"Authorization": f"Bearer {token}", "Accept": "application/vnd.github.v3+json", "User-Agent": "BazinoApp"}
    )
    try:
        resp = urllib.request.urlopen(req)
        data = json.loads(resp.read().decode())
        content_b64 = data.get("content", "")
        # GitHub may omit content for files > 1MB and provide download_url
        if content_b64:
            file_bytes = base64.b64decode(content_b64)
        elif data.get("download_url"):
            d_req = urllib.request.Request(
                data["download_url"],
                headers={"Authorization": f"Bearer {token}", "User-Agent": "BazinoApp"}
            )
            file_bytes = urllib.request.urlopen(d_req).read()
        else:
            print(f"No content found for {repo_path}")
            return False

        with open(local_path, "wb") as f:
            f.write(file_bytes)
        print(f"Downloaded: {repo_path} -> {local_path} ({len(file_bytes):,} bytes)")
        return True
    except Exception as e:
        print(f"Failed to download {repo_path}: {e}")
        return False

# Target files to download
files_to_download = [
    # Guide files
    ("استودیوی تبلیغات و بازاریابی/Doc/daily-content-production-guide-prompt.md", os.path.join(BASE_DIR, "daily-content-production-guide-prompt.md")),
    ("استودیوی تبلیغات و بازاریابی/Doc/daily-content-production-guide-fa.md", os.path.join(BASE_DIR, "daily-content-production-guide-fa.md")),
    ("استودیوی تبلیغات و بازاریابی/Doc/carousel-content-pattern-fa.md", os.path.join(BASE_DIR, "carousel-content-pattern-fa.md")),
    # Carousel Template v2 Attachments
    ("استودیوی تبلیغات و بازاریابی/Assets/carousel-template-v2/README-fa.md", os.path.join(BASE_DIR, "carousel-template-v2", "README-fa.md")),
    ("استودیوی تبلیغات و بازاریابی/Assets/carousel-template-v2/BAZINO-carousel-template-sheet-v2.jpg", os.path.join(BASE_DIR, "carousel-template-v2", "BAZINO-carousel-template-sheet-v2.jpg")),
    ("استودیوی تبلیغات و بازاریابی/Assets/carousel-template-v2/BAZINO-carousel-template-sheet-v2.png", os.path.join(BASE_DIR, "carousel-template-v2", "BAZINO-carousel-template-sheet-v2.png")),
    ("استودیوی تبلیغات و بازاریابی/Assets/carousel-template-v2/bazino-lockup-top.png", os.path.join(BASE_DIR, "carousel-template-v2", "bazino-lockup-top.png")),
    ("استودیوی تبلیغات و بازاریابی/Assets/carousel-template-v2/chosen-cover-preview.jpg", os.path.join(BASE_DIR, "carousel-template-v2", "chosen-cover-preview.jpg")),
    # Also save a copy inside ig-carousel if needed
    ("استودیوی تبلیغات و بازاریابی/Assets/carousel-template-v2/README-fa.md", os.path.join(BASE_DIR, "ig-carousel", "README-fa.md")),
    ("استودیوی تبلیغات و بازاریابی/Assets/carousel-template-v2/BAZINO-carousel-template-sheet-v2.jpg", os.path.join(BASE_DIR, "ig-carousel", "BAZINO-carousel-template-sheet-v2.jpg")),
    ("استودیوی تبلیغات و بازاریابی/Assets/carousel-template-v2/bazino-lockup-top.png", os.path.join(BASE_DIR, "ig-carousel", "bazino-lockup-top.png")),
]

print("Starting download of all referenced files from GitHub repository...")
success_count = 0
for repo_f, local_f in files_to_download:
    if download_github_file(repo_f, local_f):
        success_count += 1

print(f"\nFinished: {success_count}/{len(files_to_download)} files downloaded successfully.")
