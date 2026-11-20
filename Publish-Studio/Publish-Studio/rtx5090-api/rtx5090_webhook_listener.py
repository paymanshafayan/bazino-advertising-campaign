"""
Lightweight local HTTP webhook listener for FreeVideo RTX 5090 notifications.
Listens on http://127.0.0.1:8787/webhook and records all events to webhook_events.jsonl.
"""

import argparse
import json
import time
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent
EVENTS_LOG = BASE_DIR / "webhook_events.jsonl"


class WebhookHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        if self.path not in ("/webhook", "/webhook/"):
            self.send_response(404)
            self.end_headers()
            return

        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length) if length > 0 else b"{}"
        try:
            payload = json.loads(raw.decode("utf-8"))
        except Exception:
            payload = {"raw": raw.decode("utf-8", errors="replace")}

        record = {"received_at": time.strftime("%Y-%m-%d %H:%M:%S"), **payload}
        with EVENTS_LOG.open("a", encoding="utf-8") as f:
            f.write(json.dumps(record, ensure_ascii=False) + "\n")

        print(f"[WEBHOOK] {record.get('event', 'event')} -> {json.dumps(record, ensure_ascii=False)}", flush=True)

        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(b'{"ok":true}')

    def log_message(self, format, *args):
        return


def main():
    parser = argparse.ArgumentParser(description="FreeVideo RTX 5090 Webhook Listener")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8787)
    args = parser.parse_args()

    server = HTTPServer((args.host, args.port), WebhookHandler)
    print(f"Listening for webhooks on http://{args.host}:{args.port}/webhook ...", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
