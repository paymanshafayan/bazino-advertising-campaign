import json
import sys

sys.stdout.reconfigure(encoding='utf-8')

for fname in [r'C:\projects\Antigravity Bazino\graphql_resp_6884.13301.json', r'C:\projects\Antigravity Bazino\graphql_resp_6884.13430.json']:
    print(f"\n================ File: {fname} ================")
    with open(fname, encoding='utf-8') as f:
        d = json.load(f)
    mc = d.get('data', {}).get('fetch__MediaCollection', {})
    print("Collection name:", mc.get('collection_name') or mc.get('name'))
    for k, v in mc.items():
        if isinstance(v, dict):
            print(f"Key '{k}': {list(v.keys())}")
            if 'edges' in v:
                print(f"  Edges count in {k}: {len(v['edges'])}")
                for edge in v['edges'][:3]:
                    node = edge.get('node', {})
                    user = node.get('owner', {}) or node.get('user', {})
                    code = node.get('shortcode') or node.get('code')
                    print(f"    Post code: {code}, author: {user.get('username')}")
            if 'page_info' in v:
                print(f"  page_info: {v['page_info']}")
        elif isinstance(v, list):
            print(f"Key '{k}': list len={len(v)}")
        else:
            print(f"Key '{k}': {v}")
