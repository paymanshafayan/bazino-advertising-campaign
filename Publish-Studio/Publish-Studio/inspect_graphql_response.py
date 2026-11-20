import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

for k in ["ALL_PROXY", "all_proxy", "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy"]:
    os.environ.pop(k, None)

import asyncio
import json
import urllib.request
import websockets

CDP_PORT = 9335

def get_instagram_tab():
    req = urllib.request.Request(f"http://127.0.0.1:{CDP_PORT}/json")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(req, timeout=5) as resp:
        tabs = json.loads(resp.read().decode())
        for t in tabs:
            if t.get('type') == 'page' and 'instagram.com' in t.get('url', ''):
                return t
    return None

async def main():
    tab = get_instagram_tab()
    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=20_000_000) as ws:
        msg_id = 1
        
        # Enable network
        await ws.send(json.dumps({'id': msg_id, 'method': 'Network.enable'}))
        msg_id += 1
        
        url = "https://www.instagram.com/fartash_v/saved/_/3535547046633112/"
        print("Navigating to:", url)
        await ws.send(json.dumps({'id': msg_id, 'method': 'Page.navigate', 'params': {'url': url}}))
        msg_id += 1
        await asyncio.sleep(4)
        
        # Scroll to bottom
        print("Triggering scroll to bottom...")
        await ws.send(json.dumps({'id': msg_id, 'method': 'Runtime.evaluate', 'params': {
            'expression': 'window.scrollTo(0, document.body.scrollHeight);',
            'returnByValue': True
        }}))
        msg_id += 1
        
        # Listen for GraphQL responses
        graphql_req_ids = []
        end_time = asyncio.get_event_loop().time() + 10
        while asyncio.get_event_loop().time() < end_time:
            try:
                raw = await asyncio.wait_for(ws.recv(), timeout=1.0)
                data = json.loads(raw)
                method = data.get('method', '')
                if method == 'Network.responseReceived':
                    resp_url = data.get('params', {}).get('response', {}).get('url', '')
                    if 'graphql' in resp_url:
                        req_id = data.get('params', {}).get('requestId')
                        graphql_req_ids.append(req_id)
                        print(f"Captured GraphQL response! ReqId: {req_id}")
            except asyncio.TimeoutError:
                pass
                
        # Now fetch response bodies
        for rid in graphql_req_ids:
            msg_id += 1
            await ws.send(json.dumps({'id': msg_id, 'method': 'Network.getResponseBody', 'params': {'requestId': rid}}))
            while True:
                raw = await ws.recv()
                data = json.loads(raw)
                if data.get('id') == msg_id:
                    body = data.get('result', {}).get('body', '')
                    print(f"\n--- Response body for {rid} (length {len(body)}) ---")
                    try:
                        parsed = json.loads(body)
                        # Save to inspect
                        with open(rf"C:\projects\Antigravity Bazino\graphql_resp_{rid}.json", "w", encoding="utf-8") as f:
                            json.dump(parsed, f, ensure_ascii=False, indent=2)
                        print(f"Saved to graphql_resp_{rid}.json")
                        # Print summary
                        data_obj = parsed.get('data', {})
                        print("Keys in data:", list(data_obj.keys()))
                    except Exception as e:
                        print(f"Error parsing body: {e}\nSample: {body[:300]}")
                    break

if __name__ == '__main__':
    asyncio.run(main())
