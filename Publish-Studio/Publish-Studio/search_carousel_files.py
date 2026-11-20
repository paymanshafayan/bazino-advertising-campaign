import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

for root, dirs, files in os.walk(r"C:\projects"):
    if "node_modules" in root or ".git" in root:
        continue
    if "carousel-template" in root.lower():
        print("MATCH DIR:", root)
    for f in files:
        if "carousel-template" in f.lower() or "bazino-lockup-top" in f.lower():
            print("MATCH FILE:", os.path.join(root, f))
