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
        # Run in-page fetch for PolarisSavedCollectionPageWWWQuery with after cursor
        js = """
        (async () => {
            try {
                // Get CSRF and App ID from page / cookies
                const csrf = document.cookie.match(/csrftoken=([^;]+)/)?.[1] || '';
                const appId = '936619743392459';
                
                async function fetchCollectionPage(collectionId, afterCursor = null, count = 12) {
                    const variables = {
                        after: afterCursor,
                        collection_id: collectionId,
                        first: count,
                        __relay_internal__pv__PolarisShortDramaEnabledrelayprovider: true,
                        __relay_internal__pv__PolarisSEOIsCrawlerrelayprovider: false
                    };
                    
                    const body = new URLSearchParams();
                    body.append('fb_api_caller_class', 'RelayModern');
                    body.append('fb_api_req_friendly_name', 'PolarisSavedCollectionPageWWWQuery');
                    body.append('variables', JSON.stringify(variables));
                    body.append('doc_id', '28540310098976890');
                    
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
                    
                    const text = await res.text();
                    try {
                        return JSON.parse(text);
                    } catch (err) {
                        return { rawError: text.substring(0, 300) };
                    }
                }
                
                const p1 = await fetchCollectionPage('3535547046633112', null, 12);
                return {
                    csrf: csrf,
                    p1_keys: Object.keys(p1),
                    p1_data_keys: p1.data ? Object.keys(p1.data) : null,
                    p1_raw_error: p1.rawError || null,
                    p1: p1
                };
            } catch (err) {
                return { error: err.toString(), stack: err.stack };
            }
        })()
        """
        
        await ws.send(json.dumps({'id': 1, 'method': 'Runtime.evaluate', 'params': {
            'expression': js,
            'returnByValue': True,
            'awaitPromise': True
        }}))
        
        while True:
            raw = await ws.recv()
            data = json.loads(raw)
            if data.get('id') == 1:
                print(json.dumps(data.get('result', {}).get('result', {}).get('value', {}), ensure_ascii=False, indent=2))
                break

if __name__ == '__main__':
    asyncio.run(main())
