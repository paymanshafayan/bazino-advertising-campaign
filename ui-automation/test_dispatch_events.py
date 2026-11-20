import os
import sys
import json
import asyncio
import urllib.request
import websockets

for k in ['ALL_PROXY', 'all_proxy', 'HTTP_PROXY', 'http_proxy', 'HTTPS_PROXY', 'https_proxy']:
    os.environ.pop(k, None)

async def test_dispatch():
    req = urllib.request.Request('http://127.0.0.1:9335/json', headers={'User-Agent': 'Mozilla/5.0'})
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    tabs = json.loads(opener.open(req).read().decode())
    flow_tab = next(t for t in tabs if 'flow.google.com' in t.get('url', ''))
    ws_url = flow_tab['webSocketDebuggerUrl']
    
    async with websockets.connect(ws_url) as ws:
        # Check coordinates and element at point
        js_coords = """
        (() => {
            const btn = document.querySelector('button[aria-label="Start generation"]');
            const rect = btn.getBoundingClientRect();
            const elAtPoint = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
            
            return {
                btnRect: { left: rect.left, top: rect.top, width: rect.width, height: rect.height },
                center: { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 },
                elAtPointTag: elAtPoint ? elAtPoint.tagName : null,
                elAtPointClass: elAtPoint ? elAtPoint.className : null,
                isSame: elAtPoint === btn || btn.contains(elAtPoint)
            };
        })()
        """
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {'expression': js_coords, 'returnByValue': True}}))
        raw1 = await ws.recv()
        info = json.loads(raw1).get('result', {}).get('result', {}).get('value')
        print("Coords info:", info)
        
        # Test 1: Press Enter in ProseMirror
        print("Testing Enter in ProseMirror...")
        js_focus = """
        (() => {
            const editor = document.querySelector('div.ProseMirror');
            editor.focus();
            return true;
        })()
        """
        await ws.send(json.dumps({'id': 2, 'method': 'Runtime.evaluate', 'params': {'expression': js_focus, 'returnByValue': True}}))
        await ws.recv()
        
        # Dispatch Enter key
        await ws.send(json.dumps({'id': 3, 'method': 'Input.dispatchKeyEvent', 'params': {
            'type': 'rawKeyDown',
            'windowsVirtualKeyCode': 13,
            'unmodifiedText': '\r',
            'text': '\r'
        }}))
        await ws.recv()
        await ws.send(json.dumps({'id': 4, 'method': 'Input.dispatchKeyEvent', 'params': {
            'type': 'keyUp',
            'windowsVirtualKeyCode': 13
        }}))
        await ws.recv()
        
        await asyncio.sleep(1)
        
        # Test 2: If Enter didn't submit, dispatch mouse events to elAtPoint
        js_check_submitted = """
        (() => {
            const editor = document.querySelector('div.ProseMirror');
            const turns = document.querySelectorAll('.turn, .message, .chat-message, flow-chat-turn, flow-message');
            return {
                editorCleared: !editor.innerText.trim(),
                turnsCount: turns.length
            };
        })()
        """
        await ws.send(json.dumps({'id': 5, 'method': 'Runtime.evaluate', 'params': {'expression': js_check_submitted, 'returnByValue': True}}))
        raw5 = await ws.recv()
        sub1 = json.loads(raw5).get('result', {}).get('result', {}).get('value')
        print("After Enter:", sub1)
        
        if not sub1.get('editorCleared'):
            print("Enter didn't submit. Dispatching native mouse click to button center...")
            cx = info['center']['x']
            cy = info['center']['y']
            await ws.send(json.dumps({'id': 6, 'method': 'Input.dispatchMouseEvent', 'params': {
                'type': 'mouseMoved', 'x': cx, 'y': cy
            }}))
            await ws.recv()
            await ws.send(json.dumps({'id': 7, 'method': 'Input.dispatchMouseEvent', 'params': {
                'type': 'mousePressed', 'x': cx, 'y': cy, 'button': 'left', 'clickCount': 1
            }}))
            await ws.recv()
            await asyncio.sleep(0.05)
            await ws.send(json.dumps({'id': 8, 'method': 'Input.dispatchMouseEvent', 'params': {
                'type': 'mouseReleased', 'x': cx, 'y': cy, 'button': 'left', 'clickCount': 1
            }}))
            await ws.recv()
            
            await asyncio.sleep(2)
            
            # Check again
            await ws.send(json.dumps({'id': 9, 'method': 'Runtime.evaluate', 'params': {'expression': js_check_submitted, 'returnByValue': True}}))
            raw9 = await ws.recv()
            sub2 = json.loads(raw9).get('result', {}).get('result', {}).get('value')
            print("After native click:", sub2)

if __name__ == "__main__":
    asyncio.run(test_dispatch())
