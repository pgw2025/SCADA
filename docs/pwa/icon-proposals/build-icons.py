"""从 AI 生成的 F1 提案中提取白色主体图形，配合程序生成的干净渐变背景，
合成全套 PWA 图标（彻底消除生成水印与圆角外杂色）。"""
from PIL import Image, ImageDraw, ImageFilter
from collections import deque
import os

SRC = r"D:\CSharp\SCADA\doc\pwa\icon-proposals\Redraw_this_app_icon_in_the_mo_2026-09-05T12-44-18.png"
OUT_PUBLIC = r"D:\CSharp\SCADA\Client\public"
OUT_DOC = r"D:\CSharp\SCADA\doc\pwa\icon-proposals"

TOP = (56, 189, 248)     # #38bdf8
BOTTOM = (2, 132, 199)   # #0284c7

im = Image.open(SRC).convert("RGB")
W, H = im.size
px = im.load()

# ---------- 1. 二值化 + 连通域，分离主体与圆角外空白 ----------
def whiteish(p):
    m = min(p[0], p[1], p[2])
    return m > 200 and (max(p[0], p[1], p[2]) - m) < 30

grid = bytearray(W * H)
for y in range(H):
    for x in range(W):
        if whiteish(px[x, y]):
            grid[y * W + x] = 1

seen = bytearray(W * H)
keep = bytearray(W * H)
comps = 0
for start in range(W * H):
    if grid[start] and not seen[start]:
        q = deque([start]); seen[start] = 1
        cells = []
        while q:
            i = q.popleft(); cells.append(i)
            x, y = i % W, i // W
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if 0 <= nx < W and 0 <= ny < H:
                    j = ny * W + nx
                    if grid[j] and not seen[j]:
                        seen[j] = 1; q.append(j)
        # 质心：剔除位于四角（圆角外空白）的域
        cx = sum(c % W for c in cells) / len(cells) / W
        cy = sum(c // W for c in cells) / len(cells) / H
        in_corner = (cx < 0.22 or cx > 0.78) and (cy < 0.22 or cy > 0.78)
        if not in_corner and len(cells) > 200:
            comps += 1
            for i in cells:
                keep[i] = 1
            print("  keep component #%d: %6d px  center (%.2f, %.2f)"
                  % (comps, len(cells), cx, cy))
        else:
            print("  drop component:      %6d px  center (%.2f, %.2f)" % (len(cells), cx, cy))

# ---------- 2. 软 alpha 映射（保留抗锯齿边缘） ----------
alpha = Image.new("L", (W, H), 0)
ap = alpha.load()
for y in range(H):
    for x in range(W):
        if keep[y * W + x]:
            p = px[x, y]
            m = min(p[0], p[1], p[2])
            ap[x, y] = int(255 * min(1.0, max(0.0, (m - 150) / 70.0)))
        else:
            ap[x, y] = 0

# 膨胀 3px 把抗锯齿外圈纳入
mask_bin = Image.new("L", (W, H), 0)
mask_bin.putdata([255 if v else 0 for v in keep])
mask_bin = mask_bin.filter(ImageFilter.MaxFilter(7))
mp = mask_bin.load()
ap2 = alpha.load()
for y in range(H):
    for x in range(W):
        if mp[x, y] and not keep[y * W + x]:
            p = px[x, y]
            m = min(p[0], p[1], p[2])
            v = int(255 * min(1.0, max(0.0, (m - 150) / 70.0)))
            if v > ap2[x, y]:
                ap2[x, y] = v

glyph = Image.new("RGBA", (W, H), (255, 255, 255, 0))
glyph.putalpha(alpha)
bbox = alpha.getbbox()
print("glyph bbox:", bbox)
glyph = glyph.crop(bbox)
alpha_ch = glyph.getchannel("A")            # 先取原 alpha 通道，再重建纯白扁平图形
glyph = Image.new("RGBA", glyph.size, (255, 255, 255, 0))
glyph.putalpha(alpha_ch)
print("glyph size:", glyph.size)

# ---------- 3. 背景渐变 ----------
def gradient(size):
    img = Image.new("RGB", (size, size))
    d = ImageDraw.Draw(img)
    for y in range(size):
        t = y / max(1, size - 1)
        c = tuple(int(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3))
        d.line([(0, y), (size, y)], fill=c)
    return img

def rounded_mask(size, ratio):
    m = Image.new("L", (size * 4, size * 4), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, size * 4 - 1, size * 4 - 1],
                                        radius=int(size * ratio * 4), fill=255)
    return m.resize((size, size), Image.LANCZOS)

def build(size, glyph_ratio, rounded, radius_ratio=0.22):
    base = gradient(size).convert("RGBA")
    gw = int(size * glyph_ratio)
    gh = int(gw * glyph.height / glyph.width)
    if gh > int(size * glyph_ratio * 1.15):      # 极端比例保护
        gh = int(size * glyph_ratio)
        gw = int(gh * glyph.width / glyph.height)
    g = glyph.resize((gw, gh), Image.LANCZOS)
    base.paste(g, ((size - gw) // 2, (size - gh) // 2), g)
    if rounded:
        base.putalpha(rounded_mask(size, radius_ratio))
    return base

# ---------- 4. 输出 ----------
os.makedirs(os.path.join(OUT_PUBLIC, "pwa"), exist_ok=True)

# 主图留档（1024，方形铺满，供后续派生）
master = build(1024, 0.66, False)
master.save(os.path.join(OUT_DOC, "F1-master-1024.png"))

targets = [
    (os.path.join(OUT_PUBLIC, "pwa", "icon-512.png"), 512, 0.66, True, 0.22),
    (os.path.join(OUT_PUBLIC, "pwa", "icon-192.png"), 192, 0.66, True, 0.22),
    (os.path.join(OUT_PUBLIC, "pwa", "icon-maskable-512.png"), 512, 0.54, False, 0.0),
    (os.path.join(OUT_PUBLIC, "apple-touch-icon.png"), 180, 0.62, False, 0.0),
]
for path, size, ratio, rounded, rr in targets:
    img = build(size, ratio, rounded, rr)
    img.save(path)
    print("saved %-58s %4dx%-4d  glyph=%.0f%%  rounded=%s  %.1fKB"
          % (os.path.relpath(path, OUT_PUBLIC), size, size, ratio * 100, rounded,
             os.path.getsize(path) / 1024))

# favicon.ico（多尺寸；小尺寸用更大图形占比保证可辨识）
ico = build(64, 0.80, False)
ico.save(os.path.join(OUT_PUBLIC, "favicon.ico"), sizes=[(16, 16), (32, 32), (48, 48)])
print("saved %-58s 16/32/48  %.1fKB"
      % ("favicon.ico", os.path.getsize(os.path.join(OUT_PUBLIC, "favicon.ico")) / 1024))
