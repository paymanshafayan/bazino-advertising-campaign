import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

for k in ["ALL_PROXY", "all_proxy", "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy"]:
    os.environ.pop(k, None)

import asyncio
import json
import urllib.request
import websockets
import urllib.parse

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
    async with websockets.connect(tab['webSocketDebuggerUrl'], max_size=30_000_000) as ws:
        # Full captured postData template
        raw_post_data = "av=17841475456864781&__d=www&__user=0&__a=1&__req=1&__hs=20754.HYP%3Ainstagram_web_pkg.2.1...0&dpr=1&__ccg=EXCELLENT&__rev=1052007341&__s=a0bjtf%3Adtut8c%3A6ddogj&__hsi=7701628412113072327&__dyn=7xe5WwlEnwn8K6EjBAg5S1Dxu13w8CewSwAyUco38w5ux609vCwjE1EE2Cw8G11w6zx62G3i1ywOwa90Fwcy1yw9O0H8jwae4UaEW2G0AEco5G0zEnwhE2Lw6OyES1Twoob82ZwrUdUbGxK0PEaU6O1fws86PhA6bwg8rAwHxW1oxe0hibBK4o1lUGq2K18whE981eUdoW1jwiE&__csr=hk8FE4fGxTdjCiSL9nEmSG_BAKX8qum9xebCCFpozlKbFCHKF8kDWKHBhbKWCgB3By8oxCi-2e6pEG26X9BykEkLChGVpAmnam644Q4V9rK6ohzVuez88EfbxO3W2iUG5UiwOyFUC7XwmSbyo9ojyQq6E8oox-cwLwLz84210wLw0wcw06hwwt9Rg08yEK8w2Sozg1nQ480pNweJ28y3S12w1cdw&__hsdp=q8xn7b21yidBWBrqJ88qa-HCh9djbhRG5Z2kaoJ29UowKw6swfu18U8VrwgU06dyfw&__sjsp=q8xn7b21yid6Ux5Di26yBGah9dj4hRG5UN2Cbggxy0b3wbO&__comet_req=7&fb_dtsg=NAfzsvgIu-cX5Vl9uGoLcMznPp-ypbF-8lEvFDe0zcx5yWyppbuw6vA%3A17855905060073950%3A1793173019&jazoest=26574&lsd=GMSqMudaFRDFdaPjpnDkQv&__spin_r=1052007341&__spin_b=trunk&__spin_t=1793175100&fb_api_caller_class=RelayModern&fb_api_req_friendly_name=PolarisSavedCollectionPageWWWQuery&server_timestamps=true&doc_id=28540310098976890"
        
        # Test fetching page 1 and page 2 using in-page fetch with exact params
        js = f"""
        (async () => {{
            const baseParams = new URLSearchParams({json.dumps(raw_post_data)});
            
            async function fetchPage(afterCursor = null) {{
                const params = new URLSearchParams(baseParams);
                const vars = {{
                    after: afterCursor,
                    collection_id: "3535547046633112",
                    first: 24,
                    __relay_internal__pv__PolarisShortDramaEnabledrelayprovider: true,
                    __relay_internal__pv__PolarisSEOIsCrawlerrelayprovider: false
                }};
                params.set('variables', JSON.stringify(vars));
                
                const csrf = document.cookie.match(/csrftoken=([^;]+)/)?.[1] || '';
                const res = await fetch('/api/graphql', {{
                    method: 'POST',
                    headers: {{
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'X-CSRFToken': csrf,
                        'X-IG-App-ID': '936619743392459',
                        'X-FB-Friendly-Name': 'PolarisSavedCollectionPageWWWQuery'
                    }},
                    body: params.toString()
                }});
                return await res.json();
            }}
            
            const r1 = await fetchPage(null);
            const media1 = r1.data?.fetch__MediaCollection?.media;
            const cursor1 = media1?.page_info?.end_cursor;
            const hasNext = media1?.page_info?.has_next_page;
            
            let r2 = null;
            if (cursor1) {{
                r2 = await fetchPage(cursor1);
            }}
            const media2 = r2?.data?.fetch__MediaCollection?.media;
            
            return {{
                r1_edges: media1?.edges?.length || 0,
                r1_sample: media1?.edges?.slice(0, 3).map(e => e.node.shortcode),
                hasNext: hasNext,
                cursor1: cursor1,
                r2_edges: media2?.edges?.length || 0,
                r2_sample: media2?.edges?.slice(0, 3).map(e => e.node.shortcode),
                hasNext2: media2?.page_info?.has_next_page,
                cursor2: media2?.page_info?.end_cursor
            }};
        }})()
        """
        
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {
            'expression': js,
            'returnByValue': True,
            'awaitPromise': True
        }}))
        
        while True:
            raw = await ws.recv()
            d = json.loads(raw)
            if d.get('id') == 1:
                print(json.dumps(d.get('result', {}).get('result', {}).get('value', {}), ensure_ascii=False, indent=2))
                break

if __name__ == '__main__':
    asyncio.run(main())
