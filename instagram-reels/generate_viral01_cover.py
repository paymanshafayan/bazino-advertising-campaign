import os
import sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter

sys.stdout.reconfigure(encoding='utf-8')

OUT_DIR = r"C:\projects\Antigravity Bazino\Publish-Studio\ig-reel"
FONTS_DIR = r"C:\projects\Antigravity Bazino\Assets\fonts"
LOCKUP_PATH = os.path.join(r"C:\projects\Antigravity Bazino\Publish-Studio\carousel-template-v2", "bazino-lockup-top.png")
MONA_2K_PATH = r"C:\Jinus-Media\mona_2k_frame_mid.jpg"

os.makedirs(OUT_DIR, exist_ok=True)

# 9:16 Reels Canvas
W, H = 1080, 1920

# Palette
NAVY_BG = (5, 8, 20)          # #050814
YELLOW = (245, 208, 0)        # #F5D000
CYAN = (0, 229, 255)          # #00E5FF
WHITE = (245, 248, 255)       # #F5F8FF
MAGENTA = (177, 74, 255)      # #B14AFF

# Fonts
FONT_TITLE = os.path.join(FONTS_DIR, "ArchivoBlack-Regular.ttf")
FONT_TITLE_ALT = os.path.join(FONTS_DIR, "RussoOne-Regular.ttf")
FONT_SUBTITLE = os.path.join(FONTS_DIR, "Poppins-Bold.ttf")
FONT_BODY = os.path.join(FONTS_DIR, "Montserrat-Variable.ttf")

def render_pill(draw, x, y, text, font, bg_color, text_color, px=25, py=10):
    bbox = font.getbbox(text)
    tw = bbox[2] - bbox[0]
    th = bbox[3] - bbox[1]
    box = [x, y, x + tw + px * 2, y + th + py * 2]
    r = (box[3] - box[1]) // 2
    draw.rounded_rectangle(box, radius=r, fill=bg_color)
    draw.text((x + px - bbox[0], y + py - bbox[1]), text, font=font, fill=text_color)
    return box

def generate_cover():
    print("Loading 2K Mona frame...")
    im = Image.open(MONA_2K_PATH).convert("RGBA")
    
    # 9:16 vertical crop centered on Mona
    target_w = int(im.height * 9 / 16)
    left = (im.width - target_w) // 2
    base = im.crop((left, 0, left + target_w, im.height))
    base = base.resize((W, H), Image.Resampling.LANCZOS)
    
    # Cinematic dark gradient overlays for crystal-clear readability
    overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    odraw = ImageDraw.Draw(overlay)
    
    # Top header gradient (dark navy to transparent)
    for y in range(0, 600):
        alpha = int(220 * (1.0 - y / 600)**1.2)
        odraw.line([(0, y), (W, y)], fill=(5, 8, 20, alpha))
        
    # Bottom footer gradient (dark navy to transparent)
    for y in range(H - 550, H):
        alpha = int(230 * ((y - (H - 550)) / 550)**1.3)
        odraw.line([(0, y), (W, y)], fill=(5, 8, 20, alpha))
        
    slide = Image.alpha_composite(base, overlay)
    draw = ImageDraw.Draw(slide)
    
    # Top Brand Lockup
    if os.path.exists(LOCKUP_PATH):
        lockup = Image.open(LOCKUP_PATH).convert("RGBA")
        lx = (W - lockup.width) // 2
        slide.paste(lockup, (lx, 90), lockup)
        
    # Category Pill
    font_pill = ImageFont.truetype(FONT_SUBTITLE, 40)
    pill_text = "PS5 OYUN KRİZİ 🤦‍♀️"
    bbox_pill = font_pill.getbbox(pill_text)
    pw = (bbox_pill[2] - bbox_pill[0]) + 50
    px = (W - pw) // 2
    render_pill(draw, px, 210, pill_text, font_pill, MAGENTA, WHITE, px=25, py=12)
    
    # Main Hook Title (Yellow #F5D000, Archivo Black)
    font_title = ImageFont.truetype(FONT_TITLE, 64)
    t1 = "KONSOLUN OYUN DOLU AMA"
    t2 = "HİÇBİR ŞEY OYNAYAMIYOR"
    t3 = "MUSUN? 🎮❌"
    
    y_start = 310
    for line in [t1, t2, t3]:
        bb = font_title.getbbox(line)
        lw = bb[2] - bb[0]
        # Shadow
        draw.text(((W - lw)//2 + 3, y_start + 3), line, font=font_title, fill=(0, 0, 0, 200))
        draw.text(((W - lw)//2, y_start), line, font=font_title, fill=YELLOW)
        y_start += 82
        
    # Subtitle / Benefit Pill (Cyan #00E5FF)
    font_sub = ImageFont.truetype(FONT_SUBTITLE, 42)
    sub = "Gamer Seçim Felcini Kırmanın 2 Altın Yolu"
    bbs = font_sub.getbbox(sub)
    ws = bbs[2] - bbs[0]
    draw.text(((W - ws)//2, y_start + 15), sub, font=font_sub, fill=CYAN)
    
    # Bottom Action Card
    action_box = [80, H - 320, W - 80, H - 150]
    draw.rounded_rectangle(action_box, radius=22, fill=(8, 14, 30, 220), outline=CYAN, width=3)
    
    font_act = ImageFont.truetype(FONT_TITLE_ALT, 46)
    act_text = "KAYDET & YORUMDA BELİRT! 👇"
    bb_act = font_act.getbbox(act_text)
    w_act = bb_act[2] - bb_act[0]
    draw.text(((W - w_act)//2, H - 290), act_text, font=font_act, fill=YELLOW)
    
    font_loc = ImageFont.truetype(FONT_BODY, 40)
    loc_text = "BAZINO · Gazimağusa / KKTC · @bazinopro"
    bb_loc = font_loc.getbbox(loc_text)
    w_loc = bb_loc[2] - bb_loc[0]
    draw.text(((W - w_loc)//2, H - 220), loc_text, font=font_loc, fill=WHITE)
    
    out_path = os.path.join(OUT_DIR, "viral-01-paralysis-cover.jpg")
    # Save as high-quality RGB JPEG
    slide.convert("RGB").save(out_path, "JPEG", quality=95)
    print("Cover generated successfully:", out_path)

if __name__ == "__main__":
    generate_cover()
