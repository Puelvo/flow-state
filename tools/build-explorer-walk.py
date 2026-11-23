# Gezginin yürüme şeridini üretir: bacaklar prosedürel bir yürüyüş döngüsüyle piksel piksel çizilir.
#
# Kaynak görsellerdeki yürüyüş karelerinde bacaklar neredeyse aynı pozdaydı; adım belli olmuyordu.
# Bu araç tek bir gövde karesini (kalçadan yukarısı) alır, altına her karede yeni bacaklar çizer:
# basan ayak geriye kayar, salınan ayak dizden bükülüp yerden kalkarak öne gelir, gövde adım
# anında hafifçe çöker. Bacak çizimi kaynaktaki piksel boyuna (≈6 px) oturan bir ızgarada yapılır.
#
# Girdi:  Assets/explorer/src/explorer_idle.png  (192x192 duruş karesi)
#         Assets/explorer/src/explorer_body.png  (192x192 yürüyüş karesi; kalçadan yukarısı kullanılır)
# Çıktı:  Assets/explorer/explorer_walk.png      (kare 0 duruş, 1..N yürüyüş)
# Çalıştır: python tools/build-explorer-walk.py [önizleme.png]   (Pillow gerekir)
import math
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent / "Assets" / "explorer"
CELL = 192          # şerit kare boyu
PX = 6              # bir "sanat pikseli" (kaynaktaki piksel bloğu ≈ 6.4 px)
FRAMES = 8          # yürüyüş karesi sayısı (8 fps ile bir döngü 1 sn)
CUT_Y = 150         # gövde karesinde bacakların başladığı satır (şort paçasının üstü)
GROUND = 186        # ayak tabanının alt satırı
HIPS = (82, 108)    # sol (arka) ve sağ (ön) bacağın kalça merkezi, x
STRIDE = 3.2        # ayağın kalçaya göre en fazla ileri/geri kayması (sanat pikseli)
LIFT = 1.8          # salınan ayağın en fazla yerden kalkışı (sanat pikseli)
BOB = 4             # adım anında gövdenin çökmesi (px)

OUTLINE = (12, 8, 6)
SHORTS = (167, 129, 84)
SHORTS_DARK = (135, 94, 64)
SKIN = (248, 160, 116)
SKIN_DARK = (214, 128, 92)
BOOT = (97, 61, 44)
BOOT_DARK = (62, 36, 28)
SOLE = (40, 24, 18)

LEG_ROWS = (GROUND - CUT_Y) // PX   # ayakta dururken bacak kaç sanat pikseli boyunda (6)


def shade(c, k):
    return tuple(max(0, min(255, round(v * k))) for v in c)


def leg_cells(foot_x, lift, bend):
    """Bir bacağın sanat pikseli ızgarasındaki hücreleri: {(x, y): renk}. Kalça (0, 0)'da, y aşağı doğru.

    foot_x: ayağın kalçaya göre yatay konumu; lift: yerden kalkış; bend: dizin öne çıkışı.
    Her satırın merkezi kalça → diz → bilek çizgisi üzerinden bulunur; satırlar o merkeze
    göre boyanır (üstte şort paçası, sonra diz/bacak teni, en altta öne bakan bot)."""
    bottom = LEG_ROWS - lift              # tabanın satırı (kesirli)
    rows = max(4, round(bottom))
    knee_y = rows * 0.45
    knee_x = foot_x * 0.5 + bend

    def center(y):
        if y <= knee_y:
            return knee_x * (y / knee_y)
        t = (y - knee_y) / max(0.001, rows - knee_y)
        return knee_x + (foot_x - knee_x) * t

    cells = {}
    for r in range(rows):
        cx = center(r + 0.5)
        from_bottom = rows - 1 - r
        if from_bottom <= 2:                     # bot: 3 satır, alttaki iki satırda burun öne uzar
            left, right = -1.5, 1.5 + (1 if from_bottom <= 1 else 0)
            color = SOLE if from_bottom == 0 else (BOOT_DARK if from_bottom == 2 else BOOT)
        elif r <= 1:                             # şort paçası
            left, right = -2, 2
            color = SHORTS if r == 0 else SHORTS_DARK
        else:                                    # diz / bacak
            left, right = -1.5, 1.5
            color = SKIN
        for x in range(math.floor(cx + left + 0.5), math.floor(cx + right + 0.5)):
            cells[(x, r)] = color
    # 1 sanat piksellik koyu kontur (üst kenar hariç: gövdenin altına girer).
    outline = {}
    for (x, y) in cells:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (1, 1), (-1, 1)):
            n = (x + dx, y + dy)
            if n not in cells and n[1] >= 0:
                outline[n] = OUTLINE
    return {**outline, **cells}


def foot(phase):
    """Döngü evresine (0..1) göre ayağın (x, kalkış, diz bükümü)."""
    if phase < 0.5:                       # basma: ayak yerde, önden arkaya kayar
        t = phase / 0.5
        return STRIDE * (1 - 2 * t), 0.0, 0.0
    t = (phase - 0.5) / 0.5               # salınım: kalkar, öne gelir, iner
    e = 0.5 - 0.5 * math.cos(math.pi * t)
    lift = LIFT * math.sin(math.pi * t)
    return STRIDE * (2 * e - 1), lift, 1.2 * math.sin(math.pi * t)


def draw_leg(img, hip_x, top_y, phase, dim):
    fx, lift, bend = foot(phase)
    for (x, y), c in leg_cells(fx, lift, bend).items():
        if dim and c != OUTLINE:
            c = shade(c, 0.82)
        x0 = hip_x + x * PX
        y0 = top_y + y * PX
        for yy in range(y0, y0 + PX):
            for xx in range(x0, x0 + PX):
                if 0 <= xx < CELL and 0 <= yy < CELL:
                    img.putpixel((xx, yy), (*c, 255))


def walk_frame(body, i):
    phase = i / FRAMES
    # Ayaklar en açıkken (basma anı) gövde çöker, ayaklar yan yana geçerken kalkar.
    spread = abs(math.cos(2 * math.pi * phase))
    bob = round(BOB * spread)
    img = Image.new("RGBA", (CELL, CELL))
    top = CUT_Y - PX                      # bacak bir satır yukarıdan başlar; gövde bu satırı örter
    # Arka bacak (sol) önce ve hafif koyu; ön bacak (sağ) üstte.
    draw_leg(img, HIPS[0], top, (phase + 0.5) % 1, dim=True)
    draw_leg(img, HIPS[1], top, phase, dim=False)
    upper = body.crop((0, 0, CELL, CUT_Y))
    img.alpha_composite(upper, (0, bob))
    return img


def main():
    idle = Image.open(ROOT / "src" / "explorer_idle.png").convert("RGBA")
    body = Image.open(ROOT / "src" / "explorer_body.png").convert("RGBA")
    strip = Image.new("RGBA", (CELL * (FRAMES + 1), CELL))
    strip.alpha_composite(idle, (0, 0))
    for i in range(FRAMES):
        strip.alpha_composite(walk_frame(body, i), (CELL * (i + 1), 0))
    strip.save(ROOT / "explorer_walk.png")
    print(f"explorer: {FRAMES + 1} kare (1 duruş + {FRAMES} yürüyüş), {strip.width}x{strip.height}")

    if len(sys.argv) > 1:
        prev = Image.new("RGBA", (strip.width, strip.height * 2), (18, 22, 40, 255))
        light = Image.new("RGBA", strip.size, (235, 235, 240, 255))
        prev.paste(light, (0, strip.height))
        prev.alpha_composite(strip, (0, 0))
        prev.alpha_composite(strip, (0, strip.height))
        prev.save(sys.argv[1])


if __name__ == "__main__":
    main()
