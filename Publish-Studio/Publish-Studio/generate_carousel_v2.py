import os
import sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter

# Force UTF-8 output
sys.stdout.reconfigure(encoding='utf-8')

OUT_DIR = r"C:\projects\Antigravity Bazino\Publish-Studio\ig-carousel"
FONTS_DIR = r"C:\projects\Antigravity Bazino\Assets\fonts"
LOCKUP_PATH = os.path.join(OUT_DIR, "bazino-lockup-top.png")
COVER_PREVIEW_PATH = r"C:\projects\Antigravity Bazino\Publish-Studio\carousel-template-v2\chosen-cover-preview.jpg"

os.makedirs(OUT_DIR, exist_ok=True)

# Canvas Dimensions (4:5 ratio)
W, H = 1080, 1351

# Palette
NAVY_BG = (5, 8, 20)          # #050814
YELLOW = (245, 208, 0)        # #F5D000
CYAN = (0, 229, 255)          # #00E5FF
WHITE = (245, 248, 255)       # #F5F8FF
MAGENTA = (177, 74, 255)      # #B14AFF
DARK_CARD = (10, 18, 38)      # #0A1226
CARD_BORDER = (0, 180, 220)   # Cyan border
TEXT_MUTED = (140, 170, 205)

# Fonts
FONT_TITLE = os.path.join(FONTS_DIR, "ArchivoBlack-Regular.ttf")
FONT_TITLE_ALT = os.path.join(FONTS_DIR, "RussoOne-Regular.ttf")
FONT_SUBTITLE = os.path.join(FONTS_DIR, "Poppins-Bold.ttf")
FONT_BODY = os.path.join(FONTS_DIR, "Montserrat-Variable.ttf")
FONT_NUM = os.path.join(FONTS_DIR, "BebasNeue-Regular.ttf")

lockup_img = Image.open(LOCKUP_PATH).convert("RGBA")
LOCKUP_X = (W - lockup_img.width) // 2
LOCKUP_Y = 55

def create_base_canvas():
    """High-end studio background in Navy #050814 with soft ambient radial glow."""
    img = Image.new("RGBA", (W, H), NAVY_BG)
    draw = ImageDraw.Draw(img)
    
    for y in range(H):
        ratio = y / H
        r = int(5 + ratio * 8)
        g = int(8 + ratio * 14)
        b = int(20 + ratio * 28)
        draw.line([(0, y), (W, y)], fill=(r, g, b, 255))
        
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gdraw = ImageDraw.Draw(glow)
    gdraw.ellipse([LOCKUP_X - 80, LOCKUP_Y - 40, LOCKUP_X + lockup_img.width + 80, LOCKUP_Y + 130], fill=(0, 229, 255, 22))
    # Ambient soft circle at bottom
    gdraw.ellipse([100, 750, W - 100, H - 50], fill=(0, 229, 255, 15))
    glow = glow.filter(ImageFilter.GaussianBlur(50))
    img = Image.alpha_composite(img, glow)
    
    return img

def add_lockup_and_footer(img, slide_num, total_slides=7):
    """Adds the official BAZINO top lockup and slide indicators."""
    img.paste(lockup_img, (LOCKUP_X, LOCKUP_Y), lockup_img)
    draw = ImageDraw.Draw(img)
    
    font_footer = ImageFont.truetype(FONT_SUBTITLE, 40)
    
    page_text = f"{slide_num}/{total_slides}"
    bbox_p = font_footer.getbbox(page_text)
    pw = bbox_p[2] - bbox_p[0]
    draw.text((W - 80 - pw, H - 75), page_text, font=font_footer, fill=CYAN)
    
    brand_tag = "BAZINO · KKTC"
    draw.text((80, H - 75), brand_tag, font=font_footer, fill=TEXT_MUTED)
    draw.line([(80, H - 95), (W - 80, H - 95)], fill=(20, 45, 80), width=2)
    
    return img

def render_pill(draw, x, y, text, font, bg_color, text_color, px=25, py=10):
    bbox = font.getbbox(text)
    tw = bbox[2] - bbox[0]
    th = bbox[3] - bbox[1]
    box = [x, y, x + tw + px * 2, y + th + py * 2]
    r = (box[3] - box[1]) // 2
    draw.rounded_rectangle(box, radius=r, fill=bg_color)
    draw.text((x + px - bbox[0], y + py - bbox[1]), text, font=font, fill=text_color)
    return box

# ==========================================
# SLIDE 1: COVER
# ==========================================
def render_slide_1():
    base = Image.open(COVER_PREVIEW_PATH).convert("RGBA")
    base = base.resize((W, H), Image.Resampling.LANCZOS)
    
    # Overwrite top text area with studio dark navy gradient for crystal-clear readability
    overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    odraw = ImageDraw.Draw(overlay)
    
    for y in range(0, 580):
        if y < 450:
            alpha = 255
        else:
            alpha = int(255 * (1.0 - (y - 450) / 130))
        odraw.line([(0, y), (W, y)], fill=(5, 8, 20, alpha))
        
    slide = Image.alpha_composite(base, overlay)
    draw = ImageDraw.Draw(slide)
    
    # Lockup on top
    slide.paste(lockup_img, (LOCKUP_X, LOCKUP_Y), lockup_img)
    
    # Category Pill
    font_pill = ImageFont.truetype(FONT_SUBTITLE, 40)
    pill_text = "PS5 PERFORMANS REHBERİ"
    bbox_pill = font_pill.getbbox(pill_text)
    pw = (bbox_pill[2] - bbox_pill[0]) + 50
    px = (W - pw) // 2
    render_pill(draw, px, 180, pill_text, font_pill, MAGENTA, WHITE, px=25, py=12)
    
    # Yellow Title (2 lines)
    font_title = ImageFont.truetype(FONT_TITLE, 66)
    t1 = "PS5'TE OYUN DENEYİMİNİ"
    t2 = "UÇURACAK 5 GİZLİ AYAR"
    
    bb1 = font_title.getbbox(t1)
    w1 = bb1[2] - bb1[0]
    draw.text(((W - w1)//2, 275), t1, font=font_title, fill=YELLOW)
    
    bb2 = font_title.getbbox(t2)
    w2 = bb2[2] - bb2[0]
    draw.text(((W - w2)//2, 360), t2, font=font_title, fill=YELLOW)
    
    # Cyan Subtitle
    font_sub = ImageFont.truetype(FONT_SUBTITLE, 42)
    sub = "FPS Artır · Gecikmeyi Düşür · Sesi Netleştir"
    bbs = font_sub.getbbox(sub)
    ws = bbs[2] - bbs[0]
    draw.text(((W - ws)//2, 465), sub, font=font_sub, fill=CYAN)
    
    # Footer
    slide = add_lockup_and_footer(slide, 1)
    
    # Swipe prompt on right
    font_swipe = ImageFont.truetype(FONT_SUBTITLE, 40)
    draw_final = ImageDraw.Draw(slide)
    sw_text = "KAYDIR »"
    draw_final.text((W - 320, H - 75), sw_text, font=font_swipe, fill=YELLOW)
    
    out_path = os.path.join(OUT_DIR, "slide-01.png")
    slide.save(out_path, "PNG")
    print("Slide 1 generated:", out_path)

# ==========================================
# SLIDE 2: PROMISE
# ==========================================
def render_slide_2():
    slide = create_base_canvas()
    draw = ImageDraw.Draw(slide)
    
    # Pill
    font_pill = ImageFont.truetype(FONT_SUBTITLE, 40)
    render_pill(draw, 80, 180, "BAZINO OYUN REHBERİ", font_pill, CYAN, NAVY_BG, px=28, py=12)
    
    # Title
    font_title = ImageFont.truetype(FONT_TITLE, 62)
    t1 = "KONSOLUNUN TÜM GÜCÜNÜ"
    t2 = "KULLANIYOR MUSUN?"
    draw.text((80, 275), t1, font=font_title, fill=YELLOW)
    draw.text((80, 360), t2, font=font_title, fill=YELLOW)
    
    # Main Card
    card_box = [80, 480, W - 80, 960]
    draw.rounded_rectangle(card_box, radius=24, fill=DARK_CARD, outline=CARD_BORDER, width=3)
    
    # Card Header Tag
    font_tag = ImageFont.truetype(FONT_SUBTITLE, 40)
    render_pill(draw, 120, 520, "ESPOR STANDARTLARI", font_tag, MAGENTA, WHITE, px=22, py=8)
    
    # Body Text (Montserrat, size 44, Turkish)
    font_body = ImageFont.truetype(FONT_BODY, 44)
    lines = [
        "Bu rehberde PS5 menülerinde gizlenen,",
        "profesyonel esporcuların tercih ettiği",
        "en kritik 5 ayarı adım adım topladık."
    ]
    cur_y = 610
    for l in lines:
        draw.text((120, cur_y), l, font=font_body, fill=WHITE)
        cur_y += 62
        
    cur_y += 30
    sub_lines = [
        "Akıcı 120 Hz, sıfır gecikme ve kristal netlikte ses",
        "için konsolunu hemen şimdi optimize et!"
    ]
    for sl in sub_lines:
        draw.text((120, cur_y), sl, font=font_body, fill=CYAN)
        cur_y += 58

    # L22 Rule Action Banner: "Kaydet ve arkadaşına gönder"
    action_box = [80, 1020, W - 80, 1160]
    draw.rounded_rectangle(action_box, radius=20, fill=(15, 30, 60), outline=YELLOW, width=3)
    
    font_action = ImageFont.truetype(FONT_TITLE_ALT, 46)
    act_text = "Kaydet ve arkadaşına gönder!"
    bb_act = font_action.getbbox(act_text)
    w_act = bb_act[2] - bb_act[0]
    draw.text(((W - w_act)//2, 1060), act_text, font=font_action, fill=YELLOW)
    
    slide = add_lockup_and_footer(slide, 2)
    out_path = os.path.join(OUT_DIR, "slide-02.png")
    slide.save(out_path, "PNG")
    print("Slide 2 generated:", out_path)

# ==========================================
# SLIDE 3: STEP 1 (120 Hz & Performance Mode)
# ==========================================
def render_slide_3():
    slide = create_base_canvas()
    draw = ImageDraw.Draw(slide)
    
    # Pill
    font_pill = ImageFont.truetype(FONT_SUBTITLE, 40)
    render_pill(draw, 80, 180, "ADIM 1", font_pill, YELLOW, NAVY_BG, px=30, py=12)
    
    # Title
    font_title = ImageFont.truetype(FONT_TITLE, 58)
    draw.text((80, 270), "120 Hz & PERFORMANS MODU", font=font_title, fill=YELLOW)
    
    # Menu Path
    font_path = ImageFont.truetype(FONT_SUBTITLE, 40)
    path_text = "Ayarlar > Kayıtlı Veriler ve Oyun Ayarları > Oyun Ön Ayarları"
    draw.text((80, 360), path_text, font=font_path, fill=CYAN)
    
    # Simulated PS5 UI Screen Frame (L24 compliant representation)
    ui_box = [80, 440, W - 80, 940]
    draw.rounded_rectangle(ui_box, radius=22, fill=(8, 14, 30), outline=CYAN, width=3)
    
    # Top frame bar
    draw.rounded_rectangle([80, 440, W - 80, 520], radius=22, fill=(15, 32, 65))
    draw.rectangle([80, 490, W - 80, 520], fill=(15, 32, 65)) # flatten bottom corners
    font_ui_title = ImageFont.truetype(FONT_SUBTITLE, 40)
    draw.text((120, 460), "PS5 MENÜ AYARI: OYUN ÖN AYARLARI", font=font_ui_title, fill=WHITE)
    
    # Items inside UI
    font_ui = ImageFont.truetype(FONT_BODY, 42)
    
    # Item 1
    draw.rounded_rectangle([120, 560, W - 120, 680], radius=14, fill=(18, 38, 75), outline=YELLOW, width=2)
    draw.text((150, 580), "Performans veya Çözünürlük Modu", font=font_ui, fill=WHITE)
    draw.text((150, 630), "[ x ] Performans Modu (Öncelikli)", font=font_ui, fill=YELLOW)
    
    # Item 2
    draw.rounded_rectangle([120, 710, W - 120, 830], radius=14, fill=(18, 38, 75), outline=CYAN, width=2)
    draw.text((150, 730), "120 Hz Çıkış Desteği (Ekran ve Video)", font=font_ui, fill=WHITE)
    draw.text((150, 780), "[ x ] Otomatik (120 Hz Ekranlarda Etkin)", font=font_ui, fill=CYAN)
    
    # Highlight Tag
    render_pill(draw, 120, 865, "SONUÇ: 60 FPS → 120 FPS AKICILIK", font_path, MAGENTA, WHITE, px=25, py=10)
    
    # Key Benefit
    font_benefit = ImageFont.truetype(FONT_BODY, 44)
    b_lines = [
        "Desteklenen oyunlarda kare hızını ikiye katlar,",
        "giriş gecikmesini (input lag) yarıya indirir."
    ]
    cur_y = 990
    for bl in b_lines:
        draw.text((80, cur_y), bl, font=font_benefit, fill=WHITE)
        cur_y += 62
        
    slide = add_lockup_and_footer(slide, 3)
    out_path = os.path.join(OUT_DIR, "slide-03.png")
    slide.save(out_path, "PNG")
    print("Slide 3 generated:", out_path)

# ==========================================
# SLIDE 4: STEP 2 (Controller Mic Mute)
# ==========================================
def render_slide_4():
    slide = create_base_canvas()
    draw = ImageDraw.Draw(slide)
    
    font_pill = ImageFont.truetype(FONT_SUBTITLE, 40)
    render_pill(draw, 80, 180, "ADIM 2", font_pill, YELLOW, NAVY_BG, px=30, py=12)
    
    font_title = ImageFont.truetype(FONT_TITLE, 56)
    draw.text((80, 270), "DUALSENSE MİKROFONUNU SUSTUR", font=font_title, fill=YELLOW)
    
    font_path = ImageFont.truetype(FONT_SUBTITLE, 40)
    draw.text((80, 360), "Ayarlar > Ses > Mikrofon > Açılışta Mikrofon Durumu", font=font_path, fill=CYAN)
    
    # Simulated PS5 UI Screen Frame
    ui_box = [80, 440, W - 80, 940]
    draw.rounded_rectangle(ui_box, radius=22, fill=(8, 14, 30), outline=CYAN, width=3)
    
    draw.rounded_rectangle([80, 440, W - 80, 520], radius=22, fill=(15, 32, 65))
    draw.rectangle([80, 490, W - 80, 520], fill=(15, 32, 65))
    font_ui_title = ImageFont.truetype(FONT_SUBTITLE, 40)
    draw.text((120, 460), "PS5 MENÜ AYARI: MİKROFON DURUMU", font=font_ui_title, fill=WHITE)
    
    font_ui = ImageFont.truetype(FONT_BODY, 42)
    
    draw.rounded_rectangle([120, 560, W - 120, 680], radius=14, fill=(18, 38, 75), outline=YELLOW, width=2)
    draw.text((150, 580), "Giriş Yapıldığında Mikrofon Durumu", font=font_ui, fill=WHITE)
    draw.text((150, 630), "[ x ] Sessiz (Mute)", font=font_ui, fill=YELLOW)
    
    draw.rounded_rectangle([120, 710, W - 120, 830], radius=14, fill=(18, 38, 75), outline=CYAN, width=2)
    draw.text((150, 730), "Tetik Efekti ve Titreşim Yoğunluğu", font=font_ui, fill=WHITE)
    draw.text((150, 780), "[ x ] Standart (%100 Tam Haptik Güç)", font=font_ui, fill=CYAN)
    
    render_pill(draw, 120, 865, "GİZLİ BİLGİ: MİKROFON AÇIKKEN TİTREŞİM KISILIR", font_path, MAGENTA, WHITE, px=25, py=10)
    
    font_benefit = ImageFont.truetype(FONT_BODY, 44)
    b_lines = [
        "Dahili mikrofon açıkken PS5 motor gücünü %20 kısar.",
        "Susturarak tam titreşim ve tetik sertliğini geri alırsın."
    ]
    cur_y = 990
    for bl in b_lines:
        draw.text((80, cur_y), bl, font=font_benefit, fill=WHITE)
        cur_y += 62
        
    slide = add_lockup_and_footer(slide, 4)
    out_path = os.path.join(OUT_DIR, "slide-04.png")
    slide.save(out_path, "PNG")
    print("Slide 4 generated:", out_path)

# ==========================================
# SLIDE 5: STEP 3 (Tempest 3D Audio)
# ==========================================
def render_slide_5():
    slide = create_base_canvas()
    draw = ImageDraw.Draw(slide)
    
    font_pill = ImageFont.truetype(FONT_SUBTITLE, 40)
    render_pill(draw, 80, 180, "ADIM 3", font_pill, YELLOW, NAVY_BG, px=30, py=12)
    
    font_title = ImageFont.truetype(FONT_TITLE, 56)
    draw.text((80, 270), "TEMPEST 3D SES KALİBRASYONU", font=font_title, fill=YELLOW)
    
    font_path = ImageFont.truetype(FONT_SUBTITLE, 40)
    draw.text((80, 360), "Ayarlar > Ses > 3D Ses (Kulaklıklar) > 3D Ses Profilini Ayarla", font=font_path, fill=CYAN)
    
    ui_box = [80, 440, W - 80, 940]
    draw.rounded_rectangle(ui_box, radius=22, fill=(8, 14, 30), outline=CYAN, width=3)
    
    draw.rounded_rectangle([80, 440, W - 80, 520], radius=22, fill=(15, 32, 65))
    draw.rectangle([80, 490, W - 80, 520], fill=(15, 32, 65))
    font_ui_title = ImageFont.truetype(FONT_SUBTITLE, 40)
    draw.text((120, 460), "PS5 MENÜ AYARI: 3D SES PROFİLİ", font=font_ui_title, fill=WHITE)
    
    font_ui = ImageFont.truetype(FONT_BODY, 42)
    
    draw.rounded_rectangle([120, 560, W - 120, 680], radius=14, fill=(18, 38, 75), outline=YELLOW, width=2)
    draw.text((150, 580), "Kulaklıklar İçin 3D Ses Etkinleştir", font=font_ui, fill=WHITE)
    draw.text((150, 630), "[ x ] Açık (Tempest 3D Audio)", font=font_ui, fill=YELLOW)
    
    draw.rounded_rectangle([120, 710, W - 120, 830], radius=14, fill=(18, 38, 75), outline=CYAN, width=2)
    draw.text((150, 730), "Özel Ses Yüksekliği Seviyesi", font=font_ui, fill=WHITE)
    draw.text((150, 780), "[ Tip 1 - Tip 5 ] (Kulak hizası ile eşleştir)", font=font_ui, fill=CYAN)
    
    render_pill(draw, 120, 865, "AVANTAJ: 360 DERECE AYAK SESİ KONUMLANDIRMA", font_path, MAGENTA, WHITE, px=25, py=10)
    
    font_benefit = ImageFont.truetype(FONT_BODY, 44)
    b_lines = [
        "Warzone ve FC25'te rakiplerin konumunu ve",
        "top yönünü milimetrik olarak duymanı sağlar."
    ]
    cur_y = 990
    for bl in b_lines:
        draw.text((80, cur_y), bl, font=font_benefit, fill=WHITE)
        cur_y += 62
        
    slide = add_lockup_and_footer(slide, 5)
    out_path = os.path.join(OUT_DIR, "slide-05.png")
    slide.save(out_path, "PNG")
    print("Slide 5 generated:", out_path)

# ==========================================
# SLIDE 6: STEP 4 & 5 (Two frames: step2)
# ==========================================
def render_slide_6():
    slide = create_base_canvas()
    draw = ImageDraw.Draw(slide)
    
    font_pill = ImageFont.truetype(FONT_SUBTITLE, 40)
    render_pill(draw, 80, 180, "ADIM 4 & 5", font_pill, YELLOW, NAVY_BG, px=30, py=12)
    
    font_title = ImageFont.truetype(FONT_TITLE, 56)
    draw.text((80, 270), "HDR VE İNDİRME İNCE AYARLARI", font=font_title, fill=YELLOW)
    
    font_header = ImageFont.truetype(FONT_TITLE_ALT, 44)
    font_text = ImageFont.truetype(FONT_BODY, 42)
    
    # Frame 1: HDR
    f1_box = [80, 360, W - 80, 710]
    draw.rounded_rectangle(f1_box, radius=20, fill=DARK_CARD, outline=CARD_BORDER, width=3)
    render_pill(draw, 120, 390, "ADIM 4 · HDR AYARI", font_pill, MAGENTA, WHITE, px=20, py=8)
    draw.text((120, 460), "Ekran ve Video > HDR Ayarla", font=font_header, fill=YELLOW)
    lines_f1 = [
        "Siyah seviyesini ayarlarken logoyu tamamen yok",
        "etmeyin, 1 tık görünür bırakın. Karanlık",
        "köşelerdeki düşmanları anında fark edersiniz."
    ]
    y_f1 = 525
    for l in lines_f1:
        draw.text((120, y_f1), l, font=font_text, fill=WHITE)
        y_f1 += 54
        
    # Frame 2: Background download
    f2_box = [80, 750, W - 80, 1100]
    draw.rounded_rectangle(f2_box, radius=20, fill=DARK_CARD, outline=CARD_BORDER, width=3)
    render_pill(draw, 120, 780, "ADIM 5 · DİNLENME MODU", font_pill, CYAN, NAVY_BG, px=20, py=8)
    draw.text((120, 850), "Sistem > Güç Tasarrufu > Dinlenme Modu", font=font_header, fill=YELLOW)
    lines_f2 = [
        "'İnternete Bağlı Kal' seçeneğini etkinleştirin.",
        "Devasa oyun güncellemeleri gece indirilir,",
        "oyun zevkiniz beklemeden asla bölünmez."
    ]
    y_f2 = 915
    for l in lines_f2:
        draw.text((120, y_f2), l, font=font_text, fill=WHITE)
        y_f2 += 54
        
    slide = add_lockup_and_footer(slide, 6)
    out_path = os.path.join(OUT_DIR, "slide-06.png")
    slide.save(out_path, "PNG")
    print("Slide 6 generated:", out_path)

# ==========================================
# SLIDE 7: CLOSING (Interactive L21 + L22)
# ==========================================
def render_slide_7():
    slide = create_base_canvas()
    draw = ImageDraw.Draw(slide)
    
    font_pill = ImageFont.truetype(FONT_SUBTITLE, 40)
    render_pill(draw, 80, 180, "BAZINO TOPLULUĞU", font_pill, MAGENTA, WHITE, px=30, py=12)
    
    # Big Question Title
    font_title = ImageFont.truetype(FONT_TITLE, 60)
    draw.text((80, 270), "SEN EN ÇOK HANGİ", font=font_title, fill=YELLOW)
    draw.text((80, 355), "AYARI KULLANIYORSUN?", font=font_title, fill=YELLOW)
    
    # Options Card
    opt_box = [80, 460, W - 80, 730]
    draw.rounded_rectangle(opt_box, radius=20, fill=DARK_CARD, outline=CARD_BORDER, width=3)
    
    font_opt = ImageFont.truetype(FONT_TITLE_ALT, 44)
    font_opt_sub = ImageFont.truetype(FONT_BODY, 40)
    
    # Option 1
    draw.rounded_rectangle([120, 490, W - 120, 580], radius=12, fill=(18, 38, 75))
    draw.text((150, 510), "A) 120 FPS & Ultra Akıcılık", font=font_opt, fill=CYAN)
    
    # Option 2
    draw.rounded_rectangle([120, 610, W - 120, 700], radius=12, fill=(18, 38, 75))
    draw.text((150, 630), "B) Tempest 3D Ses & Taktik Üstünlük", font=font_opt, fill=YELLOW)
    
    # L21 Requirement: Ask for both comment and follow
    l21_box = [80, 765, W - 80, 960]
    draw.rounded_rectangle(l21_box, radius=20, fill=(28, 15, 55), outline=MAGENTA, width=3)
    
    font_l21_header = ImageFont.truetype(FONT_TITLE_ALT, 46)
    font_l21_body = ImageFont.truetype(FONT_BODY, 42)
    
    draw.text((120, 795), "Görüşünü yorumlarda paylaş!", font=font_l21_header, fill=YELLOW)
    draw.text((120, 860), "KKTC'nin en iyi espor dünyası için", font=font_l21_body, fill=WHITE)
    draw.text((120, 910), "BAZINO'yu takip etmeyi unutma!", font=font_l21_body, fill=CYAN)
    
    # L22 Requirement: "Kaydet ve arkadaşına gönder"
    l22_box = [80, 995, W - 80, 1120]
    draw.rounded_rectangle(l22_box, radius=18, fill=(15, 30, 60), outline=YELLOW, width=3)
    
    font_l22 = ImageFont.truetype(FONT_TITLE, 46)
    l22_text = "Kaydet ve arkadaşına gönder!"
    b_l22 = font_l22.getbbox(l22_text)
    w_l22 = b_l22[2] - b_l22[0]
    draw.text(((W - w_l22)//2, 1030), l22_text, font=font_l22, fill=YELLOW)
    
    slide = add_lockup_and_footer(slide, 7)
    out_path = os.path.join(OUT_DIR, "slide-07.png")
    slide.save(out_path, "PNG")
    print("Slide 7 generated:", out_path)

if __name__ == "__main__":
    print("Rendering Carousel Template v2 Slides...")
    render_slide_1()
    render_slide_2()
    render_slide_3()
    render_slide_4()
    render_slide_5()
    render_slide_6()
    render_slide_7()
    print("All 7 slides rendered successfully!")
