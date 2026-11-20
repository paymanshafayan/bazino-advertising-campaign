import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

for k in ["ALL_PROXY", "all_proxy", "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy"]:
    os.environ.pop(k, None)

import asyncio
import json
import urllib.request
import websockets
import time

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

class CdpClient:
    def __init__(self, ws):
        self.ws = ws
        self.msg_id = 0
        self.futures = {}
        self.graphql_req_ids = []
        self.reader_task = asyncio.create_task(self._reader())
        
    async def _reader(self):
        try:
            async for raw in self.ws:
                data = json.loads(raw)
                req_id = data.get('id')
                if req_id is not None and req_id in self.futures:
                    if not self.futures[req_id].done():
                        self.futures[req_id].set_result(data)
                method = data.get('method')
                if method == 'Network.responseReceived':
                    url = data.get('params', {}).get('response', {}).get('url', '')
                    if 'graphql' in url:
                        rid = data.get('params', {}).get('requestId')
                        if rid:
                            self.graphql_req_ids.append(rid)
        except asyncio.CancelledError:
            pass
        except Exception as e:
            # print("Reader error:", e)
            pass
            
    async def call(self, method, params=None):
        self.msg_id += 1
        mid = self.msg_id
        payload = {"id": mid, "method": method}
        if params is not None:
            payload["params"] = params
        fut = asyncio.get_event_loop().create_future()
        self.futures[mid] = fut
        await self.ws.send(json.dumps(payload))
        res = await asyncio.wait_for(fut, timeout=15)
        self.futures.pop(mid, None)
        return res
        
    async def close(self):
        self.reader_task.cancel()

async def harvest_collection(client, collection_url, collection_name):
    print(f"\n==================================================")
    print(f"HARVESTING: {collection_name}")
    print(f"URL: {collection_url}")
    print(f"==================================================")
    
    # 1. Navigate to collection
    await client.call("Page.navigate", {"url": collection_url})
    await asyncio.sleep(4)
    
    all_edges = {}
    processed_req_ids = set()
    has_next_page = True
    consecutive_no_new = 0
    max_scrolls = 60 # up to 60 pages (~700 posts)
    scroll_count = 0

    async def drain_graphql():
        nonlocal has_next_page
        unprocessed = [r for r in client.graphql_req_ids if r not in processed_req_ids]
        for rid in unprocessed:
            processed_req_ids.add(rid)
            try:
                res = await client.call("Network.getResponseBody", {"requestId": rid})
                body = res.get('result', {}).get('body', '')
                if body:
                    parsed = json.loads(body)
                    mc = parsed.get('data', {}).get('fetch__MediaCollection', {})
                    if mc and 'media' in mc:
                        media = mc['media']
                        edges = media.get('edges', [])
                        for e in edges:
                            node = e.get('node', {})
                            code = node.get('shortcode') or node.get('code') or node.get('id')
                            if code and code not in all_edges:
                                all_edges[code] = node
                        pi = media.get('page_info', {})
                        has_next_page = pi.get('has_next_page', False)
                        print(f"  [GraphQL Page] Added {len(edges)} items. Total unique so far: {len(all_edges)}. has_next_page={has_next_page}")
            except Exception as ex:
                pass

    async def get_dom_links():
        js = """
        (() => {
            const anchors = Array.from(document.querySelectorAll('a'));
            return anchors.map(a => a.href).filter(h => h.includes('/p/') || h.includes('/reel/'));
        })()
        """
        res = await client.call("Runtime.evaluate", {"expression": js, "returnByValue": True})
        return res.get("result", {}).get("result", {}).get("value", [])

    await drain_graphql()
    dom_links = set(await get_dom_links())
    print(f"Initial: GraphQL nodes={len(all_edges)}, DOM links={len(dom_links)}")

    while scroll_count < max_scrolls:
        scroll_count += 1
        prev_count = max(len(all_edges), len(dom_links))
        
        # Scroll to bottom
        await client.call("Runtime.evaluate", {
            "expression": "window.scrollTo(0, document.documentElement.scrollHeight);",
            "returnByValue": True
        })
        
        await asyncio.sleep(2.5)
        await drain_graphql()
        
        # Supplement with DOM links
        for l in await get_dom_links():
            dom_links.add(l)
            
        current_count = max(len(all_edges), len(dom_links))
        print(f"Scroll #{scroll_count}: GraphQL items={len(all_edges)}, DOM items={len(dom_links)}")
        
        if not has_next_page and current_count == prev_count:
            print("GraphQL reports has_next_page=False and no new items.")
            break
            
        if current_count == prev_count:
            consecutive_no_new += 1
            if consecutive_no_new >= 4:
                print(f"Reached end of collection after {scroll_count} scrolls.")
                break
        else:
            consecutive_no_new = 0

    print(f"\n>>> FINISHED {collection_name}: {len(all_edges)} GraphQL nodes, {len(dom_links)} DOM links.\n")
    return {
        "collection_name": collection_name,
        "collection_url": collection_url,
        "graphql_nodes": all_edges,
        "dom_links": list(dom_links)
    }

async def main():
    tab = get_instagram_tab()
    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=40_000_000) as ws:
        client = CdpClient(ws)
        await client.call("Network.enable")
        
        # 1. آموزش گیم
        amoozesh_url = "https://www.instagram.com/fartash_v/saved/_/3535547046633112/"
        res_amoozesh = await harvest_collection(client, amoozesh_url, "آموزش گیم")
        
        # 2. game
        game_url = "https://www.instagram.com/fartash_v/saved/game/1050777357950348/"
        res_game = await harvest_collection(client, game_url, "game")
        
        output = {
            "amoozesh_game": {
                "name": "آموزش گیم",
                "url": amoozesh_url,
                "posts_count": len(res_amoozesh["graphql_nodes"]),
                "nodes": res_amoozesh["graphql_nodes"],
                "dom_links": res_amoozesh["dom_links"]
            },
            "game": {
                "name": "game",
                "url": game_url,
                "posts_count": len(res_game["graphql_nodes"]),
                "nodes": res_game["graphql_nodes"],
                "dom_links": res_game["dom_links"]
            }
        }
        
        with open(r"C:\projects\Antigravity Bazino\harvested_collections.json", "w", encoding="utf-8") as f:
            json.dump(output, f, ensure_ascii=False, indent=2)
            
        print("\nSUCCESS! Harvested data saved to C:\\projects\\Antigravity Bazino\\harvested_collections.json")
        await client.close()

if __name__ == '__main__':
    asyncio.run(main())
