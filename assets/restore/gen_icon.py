import os
from PIL import Image, ImageDraw

out = r"E:\Lavoro\biemme projects\DumpSQL\assets\restore"

def make_icon_512():
    s = 512
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    def p(v): return int(v * s / 64)

    # ── Background rounded rect ───────────────────────────────────────────────
    bg = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    bg_draw = ImageDraw.Draw(bg)
    bg_draw.rounded_rectangle([0, 0, s-1, s-1], radius=p(12), fill=(13, 17, 23, 255))
    img.alpha_composite(bg)
    draw = ImageDraw.Draw(img)

    # ── Database body ─────────────────────────────────────────────────────────
    db_x1, db_x2 = p(14), p(50)
    db_cy_top    = p(18)
    db_cy_bot    = p(40)
    db_ry        = p(5.5)
    draw.rectangle([db_x1, db_cy_top, db_x2, db_cy_bot], fill=(12, 51, 32, 255))

    # Bottom ellipse
    draw.ellipse([db_x1, db_cy_bot - db_ry, db_x2, db_cy_bot + db_ry],
                 fill=(20, 83, 45, 255))

    # Middle ring
    mid_cy = p(29)
    draw.ellipse([db_x1, mid_cy - db_ry, db_x2, mid_cy + db_ry],
                 outline=(34, 197, 94, 90), width=max(1, p(1.2)))

    # Top ellipse
    draw.ellipse([db_x1, db_cy_top - db_ry, db_x2, db_cy_top + db_ry],
                 fill=(34, 197, 94, 255))
    # Highlight
    hl_cx, hl_cy = p(32), p(17)
    draw.ellipse([hl_cx - p(12), hl_cy - p(3), hl_cx + p(12), hl_cy + p(3)],
                 fill=(134, 239, 172, 90))

    # ── Arrow UP: poligono solido ─────────────────────────────────────────────
    arrow_color = (0, 229, 176, 255)
    glow_color  = (0, 229, 176, 35)

    pts_arrow = [
        (p(29), p(57)),  # bottom-left shaft
        (p(29), p(52)),  # shoulder-left
        (p(23), p(52)),  # left wing
        (p(32), p(44)),  # tip
        (p(41), p(52)),  # right wing
        (p(35), p(52)),  # shoulder-right
        (p(35), p(57)),  # bottom-right shaft
    ]
    draw.polygon(pts_arrow, fill=glow_color)
    draw.polygon(pts_arrow, fill=arrow_color)

    return img

# Genera il master a 512px
master = make_icon_512()
master.save(os.path.join(out, "icon_512.png"))
print("  512x512 (master)")

# Tutte le altre dimensioni: ridimensiona il master
sizes = [16, 32, 48, 64, 128, 256]
resized = {}
for s in sizes:
    img = master.resize((s, s), Image.LANCZOS)
    path = os.path.join(out, f"icon_{s}.png")
    img.save(path)
    resized[s] = img
    print(f"  {s}x{s} OK")

# ICO con tutte le taglie
ico_sizes = [16, 32, 48, 64, 128, 256]
ico_imgs  = [resized[s] for s in ico_sizes]
ico_path  = os.path.join(out, "icon.ico")
ico_imgs[0].save(ico_path, format="ICO",
                 sizes=[(s, s) for s in ico_sizes],
                 append_images=ico_imgs[1:])
print(f"ICO creato: {ico_path}")
