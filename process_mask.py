import os
from PIL import Image, ImageDraw, ImageFilter
import colorsys
from collections import deque

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "mask_src.jpg")
OUT = os.path.join(HERE, "mask_processed.png")

im = Image.open(SRC).convert("RGB")
W, H = im.size
px = im.load()

# ---------- 1. фон: заливка от краёв с допуском по градиенту ----------
visited = bytearray(W * H)
q = deque()
def push(x, y):
    if 0 <= x < W and 0 <= y < H and not visited[y * W + x]:
        r, g, b = px[x, y]
        if min(r, g, b) > 232 and (max(r, g, b) - min(r, g, b)) < 18:
            visited[y * W + x] = 1
            q.append((x, y))

for x in range(W):
    push(x, 0); push(x, H - 1)
for y in range(H):
    push(0, y); push(W - 1, y)

TOL = 16
while q:
    x, y = q.popleft()
    r0, g0, b0 = px[x, y]
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = x + dx, y + dy
        if 0 <= nx < W and 0 <= ny < H and not visited[ny * W + nx]:
            r, g, b = px[nx, ny]
            if abs(r - r0) <= TOL and abs(g - g0) <= TOL and abs(b - b0) <= TOL and min(r, g, b) > 150:
                visited[ny * W + nx] = 1
                q.append((nx, ny))

alpha = Image.new("L", (W, H), 255)
aw = alpha.load()
for y in range(H):
    base = y * W
    for x in range(W):
        if visited[base + x]:
            aw[x, y] = 0
        else:
            r, g, b = px[x, y]
            # проёмы и просветы внутри маски
            if min(r, g, b) > 242 and (max(r, g, b) - min(r, g, b)) < 12:
                aw[x, y] = 0

# добить кайму тени: несколько проходов «съедания» светлых пикселей у прозрачных
def erode_light(alpha_img, passes=4, limit=214):
    a = alpha_img.load()
    for _ in range(passes):
        to_clear = []
        for y in range(1, H - 1):
            for x in range(1, W - 1):
                if a[x, y] == 0:
                    continue
                if a[x - 1, y] == 0 or a[x + 1, y] == 0 or a[x, y - 1] == 0 or a[x, y + 1] == 0:
                    r, g, b = px[x, y]
                    if min(r, g, b) > limit and (max(r, g, b) - min(r, g, b)) < 22:
                        to_clear.append((x, y))
        for (x, y) in to_clear:
            a[x, y] = 0
    return alpha_img

alpha = erode_light(alpha)
alpha = alpha.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.GaussianBlur(1.2))

base = im.convert("RGBA")
base.putalpha(alpha)

# ---------- 2. подсветка фильтров ----------
def rainbow_ring(size, center, radius, thickness):
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    steps = 240
    for i in range(steps):
        a0 = 360.0 * i / steps
        a1 = 360.0 * (i + 1) / steps + 0.6
        hue = (i / steps)
        r, g, b = colorsys.hsv_to_rgb(hue, 1.0, 1.0)
        col = (int(r * 255), int(g * 255), int(b * 255), 255)
        d.arc([center[0] - radius, center[1] - radius, center[0] + radius, center[1] + radius],
              a0, a1, fill=col, width=thickness)
    return layer

def filter_light(size, center, radius, tint, mask_alpha):
    out = Image.new("RGBA", size, (0, 0, 0, 0))

    # внешнее свечение
    bloom = Image.new("RGBA", size, (0, 0, 0, 0))
    bd = ImageDraw.Draw(bloom)
    bd.ellipse([center[0] - radius * 1.25, center[1] - radius * 1.25,
                center[0] + radius * 1.25, center[1] + radius * 1.25],
               fill=(tint[0], tint[1], tint[2], 150))
    bloom = bloom.filter(ImageFilter.GaussianBlur(radius * 0.45))

    # лёгкая подсветка самой поверхности
    fill = Image.new("RGBA", size, (0, 0, 0, 0))
    fd = ImageDraw.Draw(fill)
    fd.ellipse([center[0] - radius * 0.92, center[1] - radius * 0.92,
                center[0] + radius * 0.92, center[1] + radius * 0.92],
               fill=(tint[0], tint[1], tint[2], 30))
    fill = fill.filter(ImageFilter.GaussianBlur(radius * 0.3))

    # радужное кольцо
    ring = rainbow_ring(size, center, radius, max(5, int(radius * 0.17)))
    inner_glow = ring.filter(ImageFilter.GaussianBlur(radius * 0.16))
    ring = Image.alpha_composite(inner_glow, ring)

    out = Image.alpha_composite(out, bloom)
    out = Image.alpha_composite(out, fill)
    out = Image.alpha_composite(out, ring)

    # не даём свету выйти далеко за контур
    a = out.getchannel("A")
    soft = mask_alpha.filter(ImageFilter.GaussianBlur(14))
    out.putalpha(Image.composite(a, soft, mask_alpha))
    return out

left = filter_light((W, H), (97, 395), 64, (255, 40, 150), alpha)
right = filter_light((W, H), (452, 395), 64, (40, 215, 255), alpha)
middle = filter_light((W, H), (277, 372), 40, (120, 255, 190), alpha)

out = base.copy()
for lay in (left, right, middle):
    out = Image.alpha_composite(out, lay)

out.save(OUT)

# ---------- 3. квадрат для иконки ----------
bbox = out.getbbox()
crop = out.crop(bbox)
side = max(crop.size)
pad = int(side * 0.06)
canvas = Image.new("RGBA", (side + pad * 2, side + pad * 2), (0, 0, 0, 0))
canvas.alpha_composite(crop, ((canvas.size[0] - crop.size[0]) // 2, (canvas.size[1] - crop.size[1]) // 2))
canvas.save(os.path.join(HERE, "mask_icon_src.png"))

dark = Image.new("RGBA", out.size, (18, 20, 26, 255))
dark.alpha_composite(out)
dark.convert("RGB").save(os.path.join(HERE, "mask_on_dark.png"))
print("saved", OUT, "icon canvas", canvas.size)
