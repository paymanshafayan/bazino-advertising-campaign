import os
import sys
import shutil
import urllib.request

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

URLS = [
    ("video_85920248", "https://flow-content.google/video/85920248-23c3-4c2a-8896-ca6d44a68720?Expires=1793373145&KeyName=labs-flow-prod-cdn-key&Signature=tSHGNDlIfMx6wAKfGrEMfleLe8c"),
    ("video_1d67f4fb", "https://flow-content.google/video/1d67f4fb-6261-4542-ab9f-7fc76b9494c9?Expires=1793372163&KeyName=labs-flow-prod-cdn-key&Signature=MgMorA4WnQtHeHDfvfOWR_NrqPY")
]

MEDIA_DIR = r"C:\Jinus-Media"
PACK_DIR = r"C:\projects\Antigravity Bazino\Publish-Studio\ig-reel\viral-01-paralysis-pack"

headers = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36",
    "Referer": "https://flow.google.com/"
}

opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

for name, url in URLS:
    dest_media = os.path.join(MEDIA_DIR, f"{name}.mp4")
    print(f"Downloading {name} to {dest_media}...")
    try:
        req = urllib.request.Request(url, headers=headers)
        with opener.open(req, timeout=30) as resp:
            data = resp.read()
            with open(dest_media, "wb") as f:
                f.write(data)
            print(f"SUCCESS: {dest_media} ({len(data):,} bytes)")
    except Exception as e:
        print(f"Error downloading {name}:", e)

# Copy the newest (video_85920248) to raw pack target
src_best = os.path.join(MEDIA_DIR, "video_85920248.mp4")
if os.path.exists(src_best) and os.path.getsize(src_best) > 100000:
    pack_dest = os.path.join(PACK_DIR, "viral01_paralysis_flow_raw.mp4")
    shutil.copy2(src_best, pack_dest)
    print(f"Copied {src_best} -> {pack_dest} ({os.path.getsize(pack_dest):,} bytes)")
