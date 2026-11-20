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

COLLECTIONS = {
    "amoozesh_game": {
        "id": "3535547046633112",
        "name": "آموزش گیم"
    },
    "game": {
        "id": "1050777357950348",
        "name": "game"
    }
}

def get_instagram_tab():
    req = urllib.request.Request(f"http://127.0.0.1:{CDP_PORT}/json")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open(req, timeout=5) as resp:
        tabs = json.loads(resp.read().decode())
        for t in tabs:
            if t.get('type') == 'page' and 'instagram.com' in t.get('url', ''):
                return t
    return None

async def harvest():
    tab = get_instagram_tab()
    if not tab:
        print("خطا: تب فعال اینستاگرام در پورت CDP 9335 یافت نشد.")
        return

    print(f"اتصال به تب اینستاگرام: {tab.get('url')}")
    
    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=50_000_000) as ws:
        # Template for Relay GraphQL query doc_id: 28540310098976890
        js = """
        (async () => {
            const results = {};
            const collections = {
                amoozesh_game: { id: "3535547046633112", name: "آموزش گیم" },
                game: { id: "1050777357950348", name: "game" }
            };

            const csrf = document.cookie.match(/csrftoken=([^;]+)/)?.[1] || '';
            const appId = '936619743392459';
            const docId = '28540310098976890';

            for (const [key, col] of Object.entries(collections)) {
                let allItems = [];
                let afterCursor = null;
                let pageNum = 0;
                let hasNextPage = true;

                while (hasNextPage && pageNum < 40) {
                    pageNum++;
                    const vars = {
                        after: afterCursor,
                        collection_id: col.id,
                        first: 24,
                        __relay_internal__pv__PolarisShortDramaEnabledrelayprovider: true,
                        __relay_internal__pv__PolarisSEOIsCrawlerrelayprovider: false
                    };

                    const body = new URLSearchParams({
                        variables: JSON.stringify(vars),
                        doc_id: docId
                    });

                    const res = await fetch('/api/graphql', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/x-www-form-urlencoded',
                            'X-CSRFToken': csrf,
                            'X-IG-App-ID': appId,
                            'X-FB-Friendly-Name': 'PolarisSavedCollectionPageWWWQuery'
                        },
                        body: body.toString()
                    });

                    if (!res.ok) break;
                    const json = await res.json();
                    const collectionData = json?.data?.xdt_api__v1__collections__collection_id__media_connection;
                    const edges = collectionData?.edges || [];

                    for (const edge of edges) {
                        const node = edge?.node;
                        if (!node) continue;
                        allItems.push({
                            id: node.id,
                            code: node.code,
                            media_type: node.media_type,
                            like_count: node.like_count,
                            comment_count: node.comment_count,
                            view_count: node.view_count,
                            user: {
                                id: node.user?.id,
                                username: node.user?.username,
                                full_name: node.user?.full_name,
                                is_verified: node.user?.is_verified
                            }
                        });
                    }

                    const pageInfo = collectionData?.page_info;
                    hasNextPage = pageInfo?.has_next_page || false;
                    afterCursor = pageInfo?.end_cursor;
                    if (!hasNextPage || !afterCursor) break;
                    await new Promise(r => setTimeout(r, 400));
                }

                results[key] = {
                    collectionId: col.id,
                    collectionName: col.name,
                    totalPages: pageNum,
                    totalItems: allItems.length,
                    items: allItems
                };
            }
            return results;
        })()
        """
        msg = {
            "id": 1,
            "method": "Runtime.evaluate",
            "params": {"expression": js, "awaitPromise": True, "returnByValue": True}
        }
        await ws.send(json.dumps(msg))
        print("در حال دریافت تمام صفحات کالکشن‌ها از طریق Relay GraphQL...")
        resp = await ws.recv()
        data = json.loads(resp).get('result', {}).get('result', {}).get('value', {})
        
        output_file = r"C:\projects\Antigravity Bazino\complete_collections_data.json"
        with open(output_file, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        
        for k, v in data.items():
            print(f"کالکشن «{v.get('collectionName')}»: {v.get('totalItems')} پست در {v.get('totalPages')} صفحه دریافت شد.")
        print(f"داده‌های خام در {output_file} با موفقیت ذخیره شد.")

if __name__ == "__main__":
    asyncio.run(harvest())
