import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def monitor_download_network():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Enable network
        await ws.send(json.dumps({'id': 1, 'method': 'Network.enable'}))
        await ws.recv()
        
        # Click Download media
        js_click = """
        (() => {
            const btn = document.querySelector('button[aria-label="Download media"]');
            if (btn) {
                btn.click();
                return { clicked: true };
            }
            return { clicked: false };
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_click, 'returnByValue': True}}))
        raw2 = await ws.recv()
        print("Click res:", json.loads(raw2).get('result', {}).get('result', {}).get('value'))
        
        # Collect network events for 5 seconds
        start_t = asyncio.get_event_loop().time()
        network_events = []
        
        while asyncio.get_event_loop().time() - start_t < 5:
            try:
                msg = await asyncio.wait_for(ws.recv(), timeout=1.0)
                data = json.loads(msg)
                method = data.get('method', '')
                if 'Network.' in method or 'Browser.' in method or 'Page.' in method:
                    url = data.get('params', {}).get('request', {}).get('url') or data.get('params', {}).get('response', {}).get('url')
                    if url or 'download' in method.lower():
                        network_events.append({
                            'method': method,
                            'url': url[:120] if url else None,
                            'params': {k: str(v)[:100] for k, v in data.get('params', {}).items() if k not in ['request', 'response']}
                        })
            except asyncio.TimeoutError:
                pass
                
        print(f"Captured {len(network_events)} events:")
        for ev in network_events[:15]:
            print(ev)

if __name__ == "__main__":
    asyncio.run(monitor_download_network())
