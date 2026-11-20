import json
import os
import sys

sys.stdout.reconfigure(encoding='utf-8')

DAILY_SLOTS = [
    {
        "slot": 1,
        "time": "12:30",
        "type": "Quick Hack (Reels)",
        "theme": "Konsol & Oyun Ayarları",
        "day1_title_tr": "PS5'te Düşman Sesini Net Duymak İçin Bu Ayarı Aç! 🎧",
        "status": "Ready for Pipeline"
    },
    {
        "slot": 2,
        "time": "15:30",
        "type": "Listicle / Carousel",
        "theme": "Faydalı Siteler & Araçlar",
        "day1_title_tr": "Her Oyuncunun Kaydetmesi Gereken 3 Hayati Site 💻",
        "status": "Ready for Pipeline"
    },
    {
        "slot": 3,
        "time": "18:30",
        "type": "Main Viral Reel (Mona)",
        "theme": "FC25 & Rekabetçi Oyun Taktikleri",
        "day1_title_tr": "FC25'te Arkadaşını Delirtecek Gizli Defans Taktiği! 🛡️🔥",
        "status": "Priority #1 (ig-reel)"
    },
    {
        "slot": 4,
        "time": "21:00",
        "type": "Game Net Humor & Banter",
        "theme": "Mizah, Kol Kırma & Hesap Ödeme",
        "day1_title_tr": "Mağusa'da 90+3 Golü Sonrası Kolların Hali 😂💀",
        "status": "Ready for Pipeline"
    },
    {
        "slot": 5,
        "time": "23:30",
        "type": "Late Night Debate",
        "theme": "Gece Tartışması & Hype",
        "day1_title_tr": "Gece Yarısı Tartışması: Ronaldo mu Messi mi? 🏆⚽",
        "status": "Ready for Pipeline"
    }
]

def main():
    print("=" * 70)
    print(" BAZINO GAME NET - GÜNLÜK 5 İÇERİK MOTORU (GAZİMAĞUSA / KKTC)")
    print(" Toplam Haftalık Hedef: 35 Gönderi (Günde 5 Yayın)")
    print("=" * 70)
    for s in DAILY_SLOTS:
        print(f"[{s['slot']}] Saat {s['time']} (UTC+3) -> {s['type']}")
        print(f"    Tema   : {s['theme']}")
        print(f"    Örnek  : {s['day1_title_tr']}")
        print(f"    Durum  : {s['status']}")
        print("-" * 70)

    out_file = r"C:\projects\Antigravity Bazino\Publish-Studio\ig-plan\schedule.json"
    with open(out_file, "w", encoding="utf-8") as f:
        json.dump({
            "daily_target": 5,
            "weekly_total": 35,
            "timezone": "UTC+03:00 (Famagusta)",
            "slots": DAILY_SLOTS
        }, f, ensure_ascii=False, indent=2)
    print(f"\n5 پست روزانه با موفقیت در {out_file} ثبت شد.")

if __name__ == "__main__":
    main()
