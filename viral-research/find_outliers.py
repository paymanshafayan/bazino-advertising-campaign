import json
import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

collections_path = r"C:\projects\Antigravity Bazino\complete_collections_data.json"
creators_path = r"C:\projects\Antigravity Bazino\Publish-Studio\all_collections_creators_ranked.json"

with open(collections_path, "r", encoding="utf-8") as f:
    col_data = json.load(f)

with open(creators_path, "r", encoding="utf-8") as f:
    creators_list = json.load(f)
    creators = {c["username"]: c for c in creators_list}

all_posts = []
for col_name, cinfo in col_data.items():
    for item in cinfo["items"]:
        u = item.get("user") or {}
        uname = u.get("username")
        creator_info = creators.get(uname, {})
        followers = creator_info.get("followersCount", 0)
        likes = item.get("like_count") or 0
        comments = item.get("comment_count") or 0
        total_eng = likes + comments
        
        # Benchmark expected engagement: 2.5% of followers (min 100)
        expected_eng = max(followers * 0.025, 100) if followers > 0 else 500
        outlier_mult = total_eng / expected_eng
        
        caption_node = item.get("caption") or {}
        caption_text = caption_node.get("text", "") if isinstance(caption_node, dict) else ""
        
        shortcode = item.get("code")
        all_posts.append({
            "code": shortcode,
            "collection": col_name,
            "username": uname,
            "full_name": u.get("full_name") or creator_info.get("full_name", ""),
            "followers": followers,
            "followersRaw": creator_info.get("followersRaw", "0"),
            "likes": likes,
            "comments": comments,
            "total_eng": total_eng,
            "outlier_mult": round(outlier_mult, 2),
            "url": f"https://www.instagram.com/reel/{shortcode}/",
            "caption": caption_text
        })

print(f"Loaded {len(all_posts)} posts.")

# Separate Iranian/Gaming Net/Tutorial vs Foreign/General
# Notice that Iranian gaming net / tutorial creators are the core target for Bazino recreation!
amoozesh_posts = [p for p in all_posts if p["collection"] == "amoozesh_game"]
game_posts = [p for p in all_posts if p["collection"] == "game"]

print(f"Amoozesh Game posts: {len(amoozesh_posts)}")
print(f"Game posts: {len(game_posts)}")

# Sort amoozesh posts by outlier_mult and likes
amoozesh_top = sorted(amoozesh_posts, key=lambda x: (x["outlier_mult"], x["likes"]), reverse=True)

print("\n--- TOP 15 IN 'آموزش گیم' (BY OUTLIER MULTIPLE) ---")
for i, p in enumerate(amoozesh_top[:15], 1):
    cap_preview = p["caption"].replace("\n", " ")[:70]
    print(f"{i}. @{p['username']} ({p['followersRaw']}) | Likes: {p['likes']:,} | Com: {p['comments']:,} | Outlier: {p['outlier_mult']}x")
    print(f"   URL: {p['url']}")
    print(f"   Cap: {cap_preview}")

# Sort game posts by outlier_mult
game_top = sorted(game_posts, key=lambda x: (x["outlier_mult"], x["likes"]), reverse=True)
print("\n--- TOP 15 IN 'game' (BY OUTLIER MULTIPLE) ---")
for i, p in enumerate(game_top[:15], 1):
    cap_preview = p["caption"].replace("\n", " ")[:70]
    print(f"{i}. @{p['username']} ({p['followersRaw']}) | Likes: {p['likes']:,} | Com: {p['comments']:,} | Outlier: {p['outlier_mult']}x")
    print(f"   URL: {p['url']}")
    print(f"   Cap: {cap_preview}")
