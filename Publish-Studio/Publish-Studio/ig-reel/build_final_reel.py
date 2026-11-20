import os
import sys
import subprocess
from PIL import Image, ImageDraw, ImageFont, ImageFilter

sys.stdout.reconfigure(encoding='utf-8')

# Output paths
OUT_DIR = r"C:\projects\Antigravity Bazino\Publish-Studio\ig-reel"
JINUS_MEDIA_DIR = r"C:\Jinus-Media"
FONTS_DIR = r"C:\projects\Antigravity Bazino\Assets\fonts"
LOCKUP_PATH = r"C:\projects\Antigravity Bazino\Publish-Studio\carousel-template-v2\bazino-lockup-top.png"

VOICEOVER_PATH = r"C:\Jinus-Media\viral01_mona_voiceover_final.mp3"
MONA_2K_PATH = r"C:\Jinus-Media\mona_2k_frame_mid.jpg"
CLUB_TALL_PATH = r"C:\projects\Antigravity Bazino\Assets\club\club-tall-01.jpeg"
GPU_VIDEO_PATH = r"C:\Jinus-Media\gpu5090_task_1792321822_7d2688b0_20261018_144215.mp4"

FINAL_VIDEO_MEDIA = os.path.join(JINUS_MEDIA_DIR, "viral01_paralysis_reel_final.mp4")
FINAL_VIDEO_STUDIO = os.path.join(OUT_DIR, "viral01_paralysis_reel_final.mp4")

# Specifications
W, H = 1080, 1920
FPS = 30
DURATION = 16.92
TOTAL_FRAMES = int(FPS * DURATION) # ~507 frames

# Colors
NAVY_BG = (5, 8, 20)
YELLOW = (245, 208, 0)
CYAN = (0, 229, 255)
WHITE = (245, 248, 255)
MAGENTA = (177, 74, 255)
DARK_CARD = (8, 14, 30)

# Fonts
FONT_TITLE = os.path.join(FONTS_DIR, "ArchivoBlack-Regular.ttf")
FONT_TITLE_ALT = os.path.join(FONTS_DIR, "RussoOne-Regular.ttf")
FONT_SUBTITLE = os.path.join(FONTS_DIR, "Poppins-Bold.ttf")
FONT_BODY = os.path.join(FONTS_DIR, "Montserrat-Variable.ttf")

lockup_img = Image.open(LOCKUP_PATH).convert("RGBA")
LOCKUP_X = (W - lockup_img.width) // 2
LOCKUP_Y = 85

def render_pill(draw, x, y, text, font, bg_color, text_color, px=25, py=10):
    bbox = font.getbbox(text)
    tw = bbox[2] - bbox[0]
    th = bbox[3] - bbox[1]
    box = [x, y, x + tw + px * 2, y + th + py * 2]
    r = (box[3] - box[1]) // 2
    draw.rounded_rectangle(box, radius=r, fill=bg_color)
    draw.text((x + px - bbox[0], y + py - bbox[1]), text, font=font, fill=text_color)
    return box

def load_base_images():
    print("Loading source graphics...")
    # Mona base
    im_mona = Image.open(MONA_2K_PATH).convert("RGBA")
    tw = int(im_mona.height * 9 / 16)
    left = (im_mona.width - tw) // 2
    mona_crop = im_mona.crop((left, 0, left + tw, im_mona.height))
    mona_crop = mona_crop.resize((W, H), Image.Resampling.LANCZOS)
    
    # Club base
    im_club = Image.open(CLUB_TALL_PATH).convert("RGBA")
    club_crop = im_club.resize((W, H), Image.Resampling.LANCZOS)
    
    return mona_crop, club_crop

def build_frame(frame_idx, mona_crop, club_crop):
    sec = frame_idx / FPS
    
    # ----------------------------------------------------
    # SCENE 1: HOOK (0.0s to 3.5s)
    # ----------------------------------------------------
    if sec < 3.5:
        # Subtle slow zoom
        scale = 1.0 + (sec / 3.5) * 0.04
        cur_w, cur_h = int(W * scale), int(H * scale)
        dx = (cur_w - W) // 2
        dy = (cur_h - H) // 2
        frame = mona_crop.resize((cur_w, cur_h), Image.Resampling.BILINEAR).crop((dx, dy, dx + W, dy + H))
        
        # Vignette & Gradient
        overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        odraw = ImageDraw.Draw(overlay)
        for y in range(0, 520):
            alpha = int(220 * (1.0 - y / 520)**1.2)
            odraw.line([(0, y), (W, y)], fill=(5, 8, 20, alpha))
        frame = Image.alpha_composite(frame, overlay)
        
        draw = ImageDraw.Draw(frame)
        frame.paste(lockup_img, (LOCKUP_X, LOCKUP_Y), lockup_img)
        
        font_pill = ImageFont.truetype(FONT_SUBTITLE, 38)
        render_pill(draw, 80, 200, "OYUN SEÇME KRİZİ! 🤦‍♀️", font_pill, MAGENTA, WHITE, px=25, py=10)
        
        font_title = ImageFont.truetype(FONT_TITLE, 62)
        draw.text((80, 290), "KONSOLUN OYUNLA DOLU", font=font_title, fill=YELLOW)
        draw.text((80, 365), "AMA OYNAYAMIYOR MUSUN?", font=font_title, fill=YELLOW)
        
        font_sub = ImageFont.truetype(FONT_SUBTITLE, 40)
        draw.text((80, 455), "Saatlerce menüde gezmeye son! 👇", font=font_sub, fill=CYAN)
        
    # ----------------------------------------------------
    # SCENE 2: TACTIC 1 - FOCUS (3.5s to 8.5s)
    # ----------------------------------------------------
    elif sec < 8.5:
        # Mona presenter angle with club lighting
        progress = (sec - 3.5) / 5.0
        frame = mona_crop.copy()
        
        overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        odraw = ImageDraw.Draw(overlay)
        for y in range(0, 500):
            alpha = int(210 * (1.0 - y / 500)**1.2)
            odraw.line([(0, y), (W, y)], fill=(5, 8, 20, alpha))
        frame = Image.alpha_composite(frame, overlay)
        
        draw = ImageDraw.Draw(frame)
        frame.paste(lockup_img, (LOCKUP_X, LOCKUP_Y), lockup_img)
        
        font_pill = ImageFont.truetype(FONT_SUBTITLE, 38)
        render_pill(draw, 80, 200, "1. ALTIN KURAL · SEÇİM FELCİ", font_pill, YELLOW, NAVY_BG, px=25, py=10)
        
        font_title = ImageFont.truetype(FONT_TITLE, 60)
        draw.text((80, 290), "AYNI ANDA 5 OYUNA", font=font_title, fill=YELLOW)
        draw.text((80, 365), "BİRDEN BAŞLAMA! 🎯", font=font_title, fill=YELLOW)
        
        font_sub = ImageFont.truetype(FONT_SUBTITLE, 42)
        draw.text((80, 455), "Sadece tek bir oyunu seç ve bitir.", font=font_sub, fill=CYAN)
        
    # ----------------------------------------------------
    # SCENE 3: TACTIC 2 - SQUAD & CLUB (8.5s to 12.5s)
    # ----------------------------------------------------
    elif sec < 12.5:
        progress = (sec - 8.5) / 4.0
        # Pan effect on club VIP lounge
        shift_y = int(progress * 40)
        frame = club_crop.crop((0, shift_y, W, H)).resize((W, H), Image.Resampling.BILINEAR)
        
        overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        odraw = ImageDraw.Draw(overlay)
        for y in range(0, 520):
            alpha = int(220 * (1.0 - y / 520)**1.2)
            odraw.line([(0, y), (W, y)], fill=(5, 8, 20, alpha))
        frame = Image.alpha_composite(frame, overlay)
        
        draw = ImageDraw.Draw(frame)
        frame.paste(lockup_img, (LOCKUP_X, LOCKUP_Y), lockup_img)
        
        font_pill = ImageFont.truetype(FONT_SUBTITLE, 38)
        render_pill(draw, 80, 200, "2. ALTIN KURAL · REKABET", font_pill, CYAN, NAVY_BG, px=25, py=10)
        
        font_title = ImageFont.truetype(FONT_TITLE, 60)
        draw.text((80, 290), "ARKADAŞINI AL,", font=font_title, fill=YELLOW)
        draw.text((80, 365), "REKABETE KATIL! 👥🔥", font=font_title, fill=YELLOW)
        
        font_sub = ImageFont.truetype(FONT_SUBTITLE, 40)
        draw.text((80, 455), "Bazino VIP Lounge Gazimağusa", font=font_sub, fill=CYAN)
        
    # ----------------------------------------------------
    # SCENE 4: CTA & CLOSING (12.5s to 16.92s)
    # ----------------------------------------------------
    else:
        frame = mona_crop.copy()
        
        overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        odraw = ImageDraw.Draw(overlay)
        for y in range(0, 460):
            alpha = int(210 * (1.0 - y / 460)**1.2)
            odraw.line([(0, y), (W, y)], fill=(5, 8, 20, alpha))
            
        for y in range(H - 480, H):
            alpha = int(220 * ((y - (H - 480)) / 480)**1.2)
            odraw.line([(0, y), (W, y)], fill=(5, 8, 20, alpha))
            
        frame = Image.alpha_composite(frame, overlay)
        draw = ImageDraw.Draw(frame)
        frame.paste(lockup_img, (LOCKUP_X, LOCKUP_Y), lockup_img)
        
        font_pill = ImageFont.truetype(FONT_SUBTITLE, 38)
        render_pill(draw, 80, 200, "BAZINO TOPLULUĞU", font_pill, MAGENTA, WHITE, px=25, py=10)
        
        font_title = ImageFont.truetype(FONT_TITLE, 60)
        draw.text((80, 290), "SEN HANGİ OYUNDASIN?", font=font_title, fill=YELLOW)
        
        font_sub = ImageFont.truetype(FONT_SUBTITLE, 42)
        draw.text((80, 370), "Yorumlarda yaz, BAZINO'yu takip et!", font=font_sub, fill=CYAN)
        
        # Bottom L22 CTA Banner
        action_box = [80, H - 280, W - 80, H - 140]
        draw.rounded_rectangle(action_box, radius=20, fill=(15, 30, 60), outline=YELLOW, width=3)
        font_act = ImageFont.truetype(FONT_TITLE_ALT, 46)
        act_text = "Kaydet ve arkadaşına gönder!"
        bb_act = font_act.getbbox(act_text)
        w_act = bb_act[2] - bb_act[0]
        draw.text(((W - w_act)//2, H - 235), act_text, font=font_act, fill=YELLOW)
        
    return frame.convert("RGB")

def render_video():
    print(f"Rendering Reel Video: {TOTAL_FRAMES} frames ({DURATION}s at {FPS}fps)...")
    mona_crop, club_crop = load_base_images()
    
    # FFmpeg pipe
    ffmpeg_cmd = [
        "ffmpeg", "-y",
        "-f", "rawvideo",
        "-vcodec", "rawvideo",
        "-s", f"{W}x{H}",
        "-pix_fmt", "rgb24",
        "-r", str(FPS),
        "-i", "-",  # stdin pipe
        "-i", VOICEOVER_PATH,  # audio track
        "-c:v", "libx264",
        "-preset", "veryfast",
        "-crf", "19",
        "-pix_fmt", "yuv420p",
        "-c:a", "aac",
        "-b:a", "192k",
        "-shortest",
        FINAL_VIDEO_MEDIA
    ]
    
    proc = subprocess.Popen(ffmpeg_cmd, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    
    for i in range(TOTAL_FRAMES):
        f = build_frame(i, mona_crop, club_crop)
        proc.stdin.write(f.tobytes())
        if i % 60 == 0 or i == TOTAL_FRAMES - 1:
            print(f"Rendered frame {i+1}/{TOTAL_FRAMES} ({((i+1)/TOTAL_FRAMES)*100:.1f}%)")
            
    proc.stdin.close()
    proc.wait()
    
    if proc.returncode == 0 and os.path.exists(FINAL_VIDEO_MEDIA):
        print(f"Video encoded successfully in Jinus-Media: {FINAL_VIDEO_MEDIA} ({os.path.getsize(FINAL_VIDEO_MEDIA):,} bytes)")
        # Copy to Publish-Studio
        import shutil
        shutil.copy2(FINAL_VIDEO_MEDIA, FINAL_VIDEO_STUDIO)
        print(f"Video synced to studio folder: {FINAL_VIDEO_STUDIO}")
    else:
        print("FFmpeg encoding failed with return code:", proc.returncode)

if __name__ == "__main__":
    render_video()
