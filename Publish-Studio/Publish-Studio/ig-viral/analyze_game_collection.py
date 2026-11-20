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

game_items = col_data.get("game", {}).get("items", [])
print(f"Total items in 'game' collection: {len(game_items)}")

analyzed_game_posts = []
for item in game_items:
    u = item.get("user") or {}
    uname = u.get("username")
    creator_info = creators.get(uname, {})
    followers = creator_info.get("followersCount", 0)
    likes = item.get("like_count") or 0
    comments = item.get("comment_count") or 0
    total_eng = likes + comments
    
    # Expected engagement: 2.5% of followers (min 100)
    expected_eng = max(followers * 0.025, 100) if followers > 0 else 500
    outlier_mult = total_eng / expected_eng
    
    shortcode = item.get("code")
    analyzed_game_posts.append({
        "code": shortcode,
        "username": uname,
        "full_name": u.get("full_name") or creator_info.get("full_name", ""),
        "followers": followers,
        "followersRaw": creator_info.get("followersRaw", "0"),
        "likes": likes,
        "comments": comments,
        "total_eng": total_eng,
        "outlier_mult": round(outlier_mult, 2),
        "url": f"https://www.instagram.com/reel/{shortcode}/"
    })

# Rank by outlier multiple
ranked_by_outlier = sorted(analyzed_game_posts, key=lambda x: (x["outlier_mult"], x["likes"]), reverse=True)

print("\n--- TOP 20 IN 'game' BY OUTLIER MULTIPLE (NO FILTER) ---")
for i, p in enumerate(ranked_by_outlier[:20], 1):
    print(f"{i}. @{p['username']} ({p['followersRaw']}) | Likes: {p['likes']:,} | Com: {p['comments']:,} | Outlier: {p['outlier_mult']}x")
    print(f"   URL: {p['url']}")

ranked_by_likes = sorted(analyzed_game_posts, key=lambda x: x["likes"], reverse=True)
print("\n--- TOP 10 IN 'game' BY RAW LIKES ---")
for i, p in enumerate(ranked_by_likes[:10], 1):
    print(f"{i}. @{p['username']} ({p['followersRaw']}) | Likes: {p['likes']:,} | Com: {p['comments']:,} | Outlier: {p['outlier_mult']}x")
    print(f"   URL: {p['url']}")
