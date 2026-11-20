import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def inspect_agent_session_text():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        js = """
        (() => {
            // Find all elements in the right pane (agent session)
            const rightPane = document.querySelector('flow-agent-panel, .agent-panel, mat-drawer[position="end"], .session-container');
            const body = document.body.innerText;
            
            return {
                rightPaneText: rightPane ? rightPane.innerText.replace(/\\n/g, ' ') : null,
                bodyText: body.replace(/\\n/g, ' | ')
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js, 'returnByValue': True}}))
        raw1 = await ws.recv()
        res = json.loads(raw1).get('result', {}).get('result', {}).get('value')
        print("Right pane text:", res.get('rightPaneText'))
        print("Body text sample:", res.get('bodyText', '')[:500])

if __name__ == "__main__":
    asyncio.run(inspect_agent_session_text())
