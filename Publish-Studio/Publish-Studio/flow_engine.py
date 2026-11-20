"""
Google Flow Studio Automation Engine
Official Content Production Automation for Bazino Campaign
"""

import os
import sys

# 1. Clean proxy variables for local CDP loopback
for k in ["ALL_PROXY", "all_proxy", "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy"]:
    os.environ.pop(k, None)

import asyncio
import json
import time
import urllib.request
import subprocess
import websockets

CDP_HTTP_URL = "http://127.0.0.1:9222"
MEDIA_DIR = r"C:\Jinus-Media"

class FlowEngine:
    def __init__(self, cdp_port=9222):
        self.cdp_url = f"http://127.0.0.1:{cdp_port}"
        self.ws_url = None
        self.msg_id = 0

    def get_flow_tab_ws(self):
        req = urllib.request.Request(f"{self.cdp_url}/json")
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        with opener.open(req, timeout=5) as resp:
            tabs = json.loads(resp.read().decode())
            flow_tab = next((t for t in tabs if 'flow.google.com' in t.get('url', '')), None)
            if flow_tab:
                self.ws_url = flow_tab['webSocketDebuggerUrl']
                return self.ws_url
        raise RuntimeError("No Google Flow tab found on Chrome CDP port 9222!")

    async def call_cdp(self, ws, method, params=None):
        self.msg_id += 1
        req_id = self.msg_id
        payload = {"id": req_id, "method": method}
        if params is not None:
            payload["params"] = params
        await ws.send(json.dumps(payload))
        while True:
            raw = await ws.recv()
            data = json.loads(raw)
            if data.get("id") == req_id:
                return data

    async def evaluate_js(self, expression):
        ws_url = self.get_flow_tab_ws()
        async with websockets.connect(ws_url) as ws:
            res = await self.call_cdp(ws, "Runtime.evaluate", {
                "expression": expression,
                "returnByValue": True,
                "awaitPromise": True
            })
            return res.get("result", {}).get("result", {}).get("value")

    async def upload_character(self, image_path, character_name="Mona"):
        """Uploads a character sheet to Google Flow and names the character."""
        if not os.path.exists(image_path):
            raise FileNotFoundError(f"Character sheet not found at {image_path}")

        ws_url = self.get_flow_tab_ws()
        async with websockets.connect(ws_url) as ws:
            # 1. Locate file input
            eval_res = await self.call_cdp(ws, "Runtime.evaluate", {
                "expression": "document.querySelector('input[type=\"file\"]')",
                "returnByValue": False
            })
            object_id = eval_res.get("result", {}).get("result", {}).get("objectId")
            if not object_id:
                raise RuntimeError("file input element not found in Character Studio!")

            # 2. Upload file via CDP
            await self.call_cdp(ws, "DOM.setFileInputFiles", {
                "files": [image_path],
                "objectId": object_id
            })
            print(f"[FlowEngine] Uploaded character image: {image_path}")
            await asyncio.sleep(2)

            # 3. Set character name
            set_name_js = f"""
            (() => {{
                const inp = document.querySelector('input.name-input') || document.querySelector('input[aria-label="Character name"]');
                if (inp) {{
                    inp.value = "{character_name}";
                    inp.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    inp.dispatchEvent(new Event('change', {{ bubbles: true }}));
                }}
                const header = document.querySelector('.editable-text-input');
                if (header) {{
                    header.value = "{character_name}";
                    header.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    header.dispatchEvent(new Event('change', {{ bubbles: true }}));
                }}
                return true;
            }})()
            """
            await self.call_cdp(ws, "Runtime.evaluate", {"expression": set_name_js, "returnByValue": True})
            print(f"[FlowEngine] Set character name to '{character_name}'")

            # 4. Click Done to save
            done_js = """
            (() => {
                const btn = Array.from(document.querySelectorAll('button')).find(b => b.innerText && b.innerText.trim() === 'Done');
                if (btn) { btn.click(); return true; }
                return false;
            })()
            """
            await self.call_cdp(ws, "Runtime.evaluate", {"expression": done_js, "returnByValue": True})
            print("[FlowEngine] Clicked Done to save character.")

    async def configure_project_settings(self):
        """Sets video aspect ratio to 9:16 and confirmation to Never."""
        js = """
        (() => {
            // Open settings drawer if not open
            const tuneBtn = document.querySelector('button.agent-action-button[aria-label="Settings"]');
            if (tuneBtn) tuneBtn.click();
            
            // Set Never confirm
            const neverRadio = document.querySelector('#mat-radio-1') || document.querySelector('#mat-radio-1-input');
            if (neverRadio) neverRadio.click();

            // Set Video 9:16
            const video916 = document.querySelector('#mat-button-toggle-20-button');
            if (video916) video916.click();

            // Set Image 9:16
            const img916 = document.querySelector('#mat-button-toggle-14-button');
            if (img916) img916.click();

            // Click save
            const saveBtn = document.querySelector('.settings-save-button');
            if (saveBtn) saveBtn.click();

            return true;
        })()
        """
        res = await self.evaluate_js(js)
        print("[FlowEngine] Configured project settings (9:16, Never confirm):", res)

    async def attach_character_to_prompt(self, character_name="Mona"):
        """Attaches the registered character from the popover to the composer."""
        ws_url = self.get_flow_tab_ws()
        async with websockets.connect(ws_url) as ws:
            # 1. Click Add Ingredients
            add_js = """
            (() => {
                const btn = document.querySelector('button[aria-label="Add ingredients to the prompt box"]') || document.querySelector('.add-menu-trigger');
                if (btn) { btn.click(); return true; }
                return false;
            })()
            """
            await self.call_cdp(ws, "Runtime.evaluate", {"expression": add_js, "returnByValue": True})
            await asyncio.sleep(1)

            # 2. Click Add to prompt
            attach_js = f"""
            (() => {{
                const addBtn = document.querySelector('button.detail-add-to-prompt-btn');
                if (addBtn) {{ addBtn.click(); return true; }}
                return false;
            }})()
            """
            res = await self.call_cdp(ws, "Runtime.evaluate", {"expression": attach_js, "returnByValue": True})
            print("[FlowEngine] Attached character chip to prompt:", res.get("result", {}).get("result", {}).get("value"))

    async def set_prompt_and_generate(self, prompt_text):
        """Sets prompt text via CDP and clicks Generate button natively."""
        ws_url = self.get_flow_tab_ws()
        async with websockets.connect(ws_url) as ws:
            # 1. Clear existing text
            clear_js = """
            (() => {
                const clearBtn = document.querySelector('button[aria-label="Clear prompt"]') || document.querySelector('.clear-button');
                if (clearBtn) clearBtn.click();
                const editor = document.querySelector('div.ProseMirror');
                if (editor) {
                    editor.focus();
                    const range = document.createRange();
                    range.selectNodeContents(editor);
                    const sel = window.getSelection();
                    sel.removeAllRanges();
                    sel.addRange(range);
                    return true;
                }
                return false;
            })()
            """
            await self.call_cdp(ws, "Runtime.evaluate", {"expression": clear_js, "returnByValue": True})

            # 2. Insert prompt text
            await self.call_cdp(ws, "Input.insertText", {"text": prompt_text})
            print(f"[FlowEngine] Inserted prompt text: {prompt_text}")

            # 3. Get Generate button coordinates
            get_btn_js = """
            (() => {
                const btn = document.querySelector('button.generate-icon-button') || document.querySelector('button[aria-label="Start generation"]');
                if (!btn) return null;
                const rect = btn.getBoundingClientRect();
                return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2, disabled: btn.disabled };
            })()
            """
            btn_res = await self.call_cdp(ws, "Runtime.evaluate", {"expression": get_btn_js, "returnByValue": True})
            coords = btn_res.get("result", {}).get("result", {}).get("value")
            if not coords or coords.get("disabled"):
                raise RuntimeError(f"Generate button not clickable! Coords: {coords}")

            # 4. Dispatch native click
            x, y = coords["x"], coords["y"]
            for event_type in ["mousePressed", "mouseReleased"]:
                await self.call_cdp(ws, "Input.dispatchMouseEvent", {
                    "type": event_type,
                    "x": x,
                    "y": y,
                    "button": "left",
                    "clickCount": 1
                })
            print("[FlowEngine] Triggered native click on Start Generation!")

            # 5. Handle permission message if prompted
            await asyncio.sleep(2)
            perm_js = """
            (() => {
                const perm = document.querySelector('flow-permission-message');
                if (!perm) return false;
                const rows = Array.from(perm.querySelectorAll('.option-row'));
                const target = rows.find(r => r.innerText.includes('Always approve')) || rows.find(r => r.innerText.includes('Approve'));
                if (target) { target.click(); return true; }
                return false;
            })()
            """
            perm_res = await self.call_cdp(ws, "Runtime.evaluate", {"expression": perm_js, "returnByValue": True})
            if perm_res.get("result", {}).get("result", {}).get("value"):
                print("[FlowEngine] Handled and approved permission dialog.")

    async def check_generation_status(self):
        """Inspects generation tile and returns status ('generating', 'error', 'completed', or 'unknown')."""
        js = """
        (() => {
            const errorTile = document.querySelector('flow-error-tile, .error-tile');
            if (errorTile) {
                return {
                    status: 'error',
                    message: errorTile.innerText ? errorTile.innerText.slice(0, 250).replace(/\\n/g, ' ') : 'Unknown error'
                };
            }
            const videoEl = document.querySelector('video');
            if (videoEl && (videoEl.src || videoEl.currentSrc)) {
                return {
                    status: 'completed',
                    videoSrc: videoEl.src || videoEl.currentSrc
                };
            }
            const loader = document.querySelector('.loading-percentage, [role="progressbar"], .loader');
            if (loader) {
                return {
                    status: 'generating',
                    progress: loader.innerText.trim()
                };
            }
            return { status: 'pending' };
        })()
        """
        return await self.evaluate_js(js)

    async def monitor_generation_loop(self, timeout_minutes=10, interval_seconds=60):
        """Active 1-minute polling loop to check generation box next to character sheet."""
        start_time = time.time()
        max_duration = timeout_minutes * 60
        print(f"[FlowEngine] Starting active {interval_seconds}s polling loop (max {timeout_minutes} min)...")
        
        while time.time() - start_time < max_duration:
            status = await self.check_generation_status()
            elapsed_m = round((time.time() - start_time) / 60, 1)
            print(f"[FlowEngine] [{elapsed_m}m] Status: {status}")

            if status.get("status") == "error":
                raise RuntimeError(f"Generation error detected: {status.get('message')}")
            elif status.get("status") == "completed":
                print("[FlowEngine] Generation completed successfully!")
                return status
            
            await asyncio.sleep(interval_seconds)
            
        raise TimeoutError(f"Generation timed out after {timeout_minutes} minutes!")

    async def download_video_direct_high_res(self, output_filename="bazino_mona_reel_1080p.mp4"):
        """Downloads video directly from Google Flow at 1080p+ to C:\\Jinus-Media."""
        os.makedirs(MEDIA_DIR, exist_ok=True)
        target_path = os.path.join(MEDIA_DIR, output_filename)
        
        # Trigger native download button in Flow UI (select 1080p if resolution menu opens)
        js = """
        (() => {
            const downloadBtn = document.querySelector('button[aria-label="Download"]') || 
                                document.querySelector('button.top-action-button[aria-label="Download image"]');
            if (downloadBtn) {
                downloadBtn.click();
                setTimeout(() => {
                    const opt1080 = Array.from(document.querySelectorAll('[role="menuitem"], .mat-mdc-menu-item')).find(el => el.innerText.includes('1080'));
                    if (opt1080) opt1080.click();
                }, 300);
                return { clickedDownload: true };
            }
            // Fallback: extract latest video element src
            const videoEl = document.querySelector('video');
            return {
                videoSrc: videoEl ? (videoEl.src || videoEl.currentSrc) : null
            };
        })()
        """
        res = await self.evaluate_js(js)
        print("[FlowEngine] Triggered direct high-res download:", res)
        return target_path

    def burn_karaoke_subtitles(self, clean_mp4, srt_file, output_mp4=None):
        """Burns word-by-word animated karaoke subtitles + BAZINO badge via scripts/karaoke_burn.py."""
        if output_mp4 is None:
            base, ext = os.path.splitext(clean_mp4)
            output_mp4 = f"{base}_karaoke{ext}"
        
        script_path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "scripts", "karaoke_burn.py")
        cmd = [
            sys.executable, script_path,
            "--src", clean_mp4,
            "--srt", srt_file,
            "--out", output_mp4
        ]
        print(f"[FlowEngine] Running karaoke subtitle burn: {' '.join(cmd)}")
        res = subprocess.run(cmd, capture_output=True, text=True)
        if res.returncode == 0:
            print(f"[FlowEngine] Successfully created karaoke subtitled video: {output_mp4}")
            return output_mp4
        else:
            print(f"[FlowEngine] Karaoke burn error: {res.stderr}")
            raise RuntimeError(f"karaoke_burn failed: {res.stderr}")

if __name__ == "__main__":
    engine = FlowEngine()
    print("Google Flow Engine initialized.")
    ws = engine.get_flow_tab_ws()
    print("Connected to Flow tab:", ws)
