import os
import sys
import json

sys.stdout.reconfigure(encoding='utf-8')

# 1. Load data
complete_file = r"C:\projects\Antigravity Bazino\complete_collections_data.json"
cache_file = r"C:\projects\Antigravity Bazino\scraped_profiles.json"

with open(complete_file, encoding="utf-8") as f:
    collections_data = json.load(f)

with open(cache_file, encoding="utf-8") as f:
    profiles_cache = json.load(f)

c1 = collections_data['amoozesh_game']
c2 = collections_data['game']

# Process creators in c1 (آموزش گیم)
c1_creators = {}
for it in c1['items']:
    u = it.get('user')
    if u and u.get('username'):
        un = u['username']
        if un not in c1_creators:
            c1_creators[un] = {"user": u, "posts": []}
        c1_creators[un]["posts"].append(it.get('code'))

# Process creators in c2 (game)
c2_creators = {}
for it in c2['items']:
    u = it.get('user')
    if u and u.get('username'):
        un = u['username']
        if un not in c2_creators:
            c2_creators[un] = {"user": u, "posts": []}
        c2_creators[un]["posts"].append(it.get('code'))

all_unames = set(list(c1_creators.keys()) + list(c2_creators.keys()))
print(f"Total unique creators across both: {len(all_unames)}")

all_creators_list = []
for un in all_unames:
    prof = profiles_cache.get(un, {})
    user_meta = None
    if un in c1_creators:
        user_meta = c1_creators[un]['user']
    elif un in c2_creators:
        user_meta = c2_creators[un]['user']
        
    full_name = user_meta.get('full_name', '') if user_meta else ''
    
    in_c1 = un in c1_creators
    in_c2 = un in c2_creators
    c1_posts = len(c1_creators[un]['posts']) if in_c1 else 0
    c2_posts = len(c2_creators[un]['posts']) if in_c2 else 0
    total_posts = c1_posts + c2_posts
    
    presence = "هردو (مشترک)" if (in_c1 and in_c2) else ("آموزش گیم" if in_c1 else "game")
    
    followers = prof.get('followersCount', 0)
    
    all_creators_list.append({
        "username": un,
        "full_name": full_name,
        "followersCount": followers,
        "followersRaw": prof.get('followersRaw', ''),
        "presence": presence,
        "amoozesh_game_posts": c1_posts,
        "game_posts": c2_posts,
        "total_saved_posts": total_posts,
        "url": f"https://www.instagram.com/{un}/"
    })

# Sort descending by followersCount
all_creators_list.sort(key=lambda x: (x['followersCount'], x['total_saved_posts']), reverse=True)

# Assign ranks
for i, item in enumerate(all_creators_list, 1):
    item['rank'] = i

# ----------------- Save All Creators JSON & MD in Publish-Studio -----------------
json_path = r"C:\projects\Antigravity Bazino\Publish-Studio\all_collections_creators_ranked.json"
with open(json_path, "w", encoding="utf-8") as f:
    json.dump(all_creators_list, f, ensure_ascii=False, indent=2)

md_path = r"C:\projects\Antigravity Bazino\Publish-Studio\all_collections_creators_ranked.md"
with open(md_path, "w", encoding="utf-8") as f:
    f.write("# لیست کامل و رتبه‌بندی تمامی تولیدکنندگان محتوای کالکشن‌های اینستاگرام بازینو\n\n")
    f.write(f"این سند شامل فهرست کامل و ۱۰۰٪ واقعی **{len(all_creators_list)} تولیدکنندهٔ محتوا** از هر دو کالکشن «آموزش گیم» (۷۴ پست) و «game» (۱۷۰ پست) است که مستقیماً بر اساس بیشترین تعداد فالوور مرتب شده‌اند.\n\n")
    f.write("---\n\n")
    f.write("| رتبه | نام کاربری (Handle) | نام کامل / عنوان | تعداد فالوور | کالکشن منبع | تعداد پست آموزش گیم | تعداد پست game | مجموع پست‌های ذخیره | لینک پیج |\n")
    f.write("|:---:|:---|:---|:---:|:---:|:---:|:---:|:---:|:---:|\n")
    for x in all_creators_list:
        f_str = f"{x['followersCount']:,}" if x['followersCount'] > 0 else "نامشخص / خصوصی"
        f.write(f"| {x['rank']} | `@{x['username']}` | {x['full_name']} | {f_str} | {x['presence']} | {x['amoozesh_game_posts']} | {x['game_posts']} | {x['total_saved_posts']} | [مشاهده پیج]({x['url']}) |\n")

print(f"Saved all {len(all_creators_list)} creators to {md_path}")

# ----------------- Save Top 10 Ranked Lists in Publish-Studio -----------------
# 1. Top 10 آموزش گیم
c1_ranked = [x for x in all_creators_list if x['amoozesh_game_posts'] > 0][:10]
top10_c1_usernames = set(x['username'] for x in c1_ranked)

# 2. Top 10 game excluding top10 of آموزش گیم
c2_ranked = [x for x in all_creators_list if x['game_posts'] > 0 and x['username'] not in top10_c1_usernames][:10]

top10_dict = {
    "amoozesh_game_top10": c1_ranked,
    "game_top10": c2_ranked
}

top10_json_path = r"C:\projects\Antigravity Bazino\Publish-Studio\top10_collections_ranked.json"
with open(top10_json_path, "w", encoding="utf-8") as f:
    json.dump(top10_dict, f, ensure_ascii=False, indent=2)

top10_md_path = r"C:\projects\Antigravity Bazino\Publish-Studio\top10_collections_ranked.md"
with open(top10_md_path, "w", encoding="utf-8") as f:
    f.write("# ۱۰ پیج برتر هر کالکشن با بیشترین فالوور (بدون همپوشانی)\n\n")
    f.write("## ۱. کالکشن «آموزش گیم» (URL: `https://www.instagram.com/fartash_v/saved/_/3535547046633112/`)\n\n")
    f.write("| رتبه | نام کاربری | نام کامل | تعداد فالوور | پست‌های ذخیره | لینک پیج |\n")
    f.write("|:---:|:---|:---|:---:|:---:|:---:|\n")
    for i, x in enumerate(c1_ranked, 1):
        f.write(f"| {i} | `@{x['username']}` | {x['full_name']} | {x['followersCount']:,} | {x['amoozesh_game_posts']} | [مشاهده پیج]({x['url']}) |\n")
        
    f.write("\n---\n\n")
    f.write("## ۲. کالکشن «game» (URL: `https://www.instagram.com/fartash_v/saved/game/1050777357950348/`)\n\n")
    f.write("*توجه: هیچ‌کدام از پیج‌های این لیست با لیست آموزش گیم تکراری نیستند.*\n\n")
    f.write("| رتبه | نام کاربری | نام کامل | تعداد فالوور | پست‌های ذخیره | لینک پیج |\n")
    f.write("|:---:|:---|:---|:---:|:---:|:---:|\n")
    for i, x in enumerate(c2_ranked, 1):
        f.write(f"| {i} | `@{x['username']}` | {x['full_name']} | {x['followersCount']:,} | {x['game_posts']} | [مشاهده پیج]({x['url']}) |\n")

print(f"Saved Top 10 lists to {top10_md_path}")
