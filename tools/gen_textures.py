#!/usr/bin/env python3
"""Generates every Vintage-E texture as 32x32 pixel art (original artwork, not copied from ProjectE).

    python3 tools/gen_textures.py            # writes into assets/vintagee/textures
    python3 tools/gen_textures.py --preview  # also writes preview.png with everything side by side
"""
import math
import os
import random
import sys

from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "vintagee", "textures")
S = 32


def rgba(hexstr, a=255):
    hexstr = hexstr.lstrip("#")
    return (int(hexstr[0:2], 16), int(hexstr[2:4], 16), int(hexstr[4:6], 16), a)


def mix(c1, c2, t):
    return tuple(int(round(c1[i] + (c2[i] - c1[i]) * t)) for i in range(4))


def shade(c, f):
    return (max(0, min(255, int(c[0] * f))), max(0, min(255, int(c[1] * f))), max(0, min(255, int(c[2] * f))), c[3])


def new():
    return Image.new("RGBA", (S, S), (0, 0, 0, 0))


def outline(img, color=(20, 14, 18, 255)):
    """Adds a 1px dark outline around opaque pixels (classic item look)."""
    src = img.copy()
    px = src.load()
    out = img.load()
    for y in range(S):
        for x in range(S):
            if px[x, y][3] > 0:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < S and 0 <= ny < S and px[nx, ny][3] > 128:
                    out[x, y] = color
                    break
    return img


def save(img, rel):
    path = os.path.join(ROOT, rel + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)
    return path


# ---------------------------------------------------------------- items

def sphere(base, glow, seed, swirl=True):
    rnd = random.Random(seed)
    img = new()
    px = img.load()
    cx, cy, r = 15.5, 15.5, 12.5
    for y in range(S):
        for x in range(S):
            d = math.hypot(x - cx, y - cy)
            if d > r:
                continue
            # light from top-left
            lx, ly = (x - cx) / r, (y - cy) / r
            lz = math.sqrt(max(0.0, 1 - lx * lx - ly * ly))
            light = max(0.0, (-lx * 0.5 - ly * 0.6 + lz * 0.6))
            c = mix(shade(base, 0.45), base, min(1, light * 1.1))
            if swirl:
                ang = math.atan2(y - cy, x - cx)
                band = math.sin(ang * 3 + d * 0.55)
                if band > 0.72:
                    c = mix(c, glow, 0.55)
            c = shade(c, 1 + rnd.uniform(-0.05, 0.05))
            px[x, y] = c
    # rim glow + specular highlight
    for y in range(S):
        for x in range(S):
            d = math.hypot(x - cx, y - cy)
            if r - 1.3 < d <= r and px[x, y][3]:
                px[x, y] = mix(px[x, y], glow, 0.45)
    for (x, y, t) in ((10, 9, 0.85), (11, 9, 0.7), (10, 10, 0.7), (12, 10, 0.4), (9, 11, 0.35)):
        px[x, y] = mix(px[x, y], (255, 255, 255, 255), t)
    return outline(img)


def gem(base, seed):
    rnd = random.Random(seed)
    img = new()
    d = ImageDraw.Draw(img)
    top, mid, bot = 5, 12, 27
    pts = [(9, top), (22, top), (28, mid), (15.5, bot), (3, mid)]
    d.polygon(pts, fill=base)
    # facets
    light = shade(base, 1.45)
    dark = shade(base, 0.55)
    midc = shade(base, 1.1)
    d.polygon([(9, top), (22, top), (19, mid), (12, mid)], fill=light)
    d.polygon([(3, mid), (9, top), (12, mid)], fill=midc)
    d.polygon([(22, top), (28, mid), (19, mid)], fill=shade(base, 0.85))
    d.polygon([(3, mid), (12, mid), (15.5, bot)], fill=shade(base, 0.9))
    d.polygon([(12, mid), (19, mid), (15.5, bot)], fill=base)
    d.polygon([(19, mid), (28, mid), (15.5, bot)], fill=dark)
    px = img.load()
    for _ in range(14):
        x, y = rnd.randint(6, 25), rnd.randint(6, 22)
        if px[x, y][3]:
            px[x, y] = shade(px[x, y], rnd.uniform(0.9, 1.15))
    for (x, y) in ((12, 7), (13, 7), (12, 8), (16, 6)):
        px[x, y] = (255, 240, 240, 255)
    return outline(img)


def lump(base, glow, seed, glow_amount):
    rnd = random.Random(seed)
    img = new()
    px = img.load()
    blobs = [(15, 17, 8.5), (10, 13, 5), (20, 11, 5.5), (10, 21, 4.5), (21, 21, 5.5), (16, 24, 4)]
    for y in range(S):
        for x in range(S):
            inside = any(math.hypot(x - bx, y - by) < br + rnd.uniform(-0.6, 0.6) for bx, by, br in blobs)
            if not inside:
                continue
            t = (x + y) / (2 * S)
            c = mix(shade(base, 1.35), shade(base, 0.6), t)
            c = shade(c, rnd.uniform(0.85, 1.15))
            px[x, y] = c
    # glowing cracks
    for _ in range(int(9 * glow_amount) + 3):
        x, y = rnd.randint(6, 25), rnd.randint(6, 26)
        length = rnd.randint(2, 5)
        dx, dy = rnd.choice(((1, 0), (0, 1), (1, 1), (1, -1)))
        for i in range(length):
            xx, yy = x + dx * i, y + dy * i
            if 0 <= xx < S and 0 <= yy < S and px[xx, yy][3]:
                px[xx, yy] = mix(px[xx, yy], glow, 0.85)
    return outline(img)


def tablet():
    img = new()
    d = ImageDraw.Draw(img)
    d.rectangle((5, 3, 26, 28), fill=rgba("2b2633"))
    d.rectangle((6, 4, 25, 27), fill=rgba("3b3446"))
    d.rectangle((6, 4, 25, 5), fill=rgba("514862"))
    # sigil
    d.ellipse((9, 9, 22, 22), outline=rgba("e04a3a"), width=1)
    d.polygon([(15.5, 10), (21, 19), (10, 19)], outline=rgba("ff8a5c"))
    d.ellipse((14, 14, 17, 17), fill=rgba("ffcf5c"))
    d.line((8, 25, 23, 25), fill=rgba("6a5f80"))
    return outline(img)


# Tool heads in local coordinates: a runs along the handle (towards the tip), b across it.
# The handle runs from the bottom-left corner up to HANDLE_END.
HANDLE_START = (4.5, 27.5)
HANDLE_END = (20.5, 11.5)
TOOL_SHAPES = {
    "pickaxe": [(-1.5, -12), (1.5, -12.5), (3.5, -6), (4, 0), (3.5, 6), (1.5, 12.5), (-1.5, 12), (0.5, 6), (1, 0), (0.5, -6)],
    "shovel": [(-1, -3.2), (5, -4.8), (9.5, -3.5), (11.5, 0), (9.5, 3.5), (5, 4.8), (-1, 3.2)],
    "axe": [(-3, -1.5), (3, -1.5), (5.5, -8), (2.5, -11.5), (-3.5, -11.5), (-5.5, -8), (-3, -1.5), (-1.5, 1.5), (1.5, 1.5)],
    "hoe": [(-1.5, -1.5), (1.5, -1.5), (1.5, -6), (-3.5, -10.5), (-5.5, -10.5), (-1.5, -6), (-1.5, -1.5)],
    "hammer": [(-3, -7.5), (3, -7.5), (3, 7.5), (-3, 7.5)],
}


def to_world(a, b, center):
    ux, uy = 0.7071, -0.7071   # along the handle
    vx, vy = -0.7071, -0.7071  # across it (towards top-left)
    return (center[0] + a * ux + b * vx, center[1] + a * uy + b * vy)


def tool(kind, head, edge, handle):
    img = new()
    d = ImageDraw.Draw(img)
    if kind == "sword":
        d.polygon([(25.5, 2.5), (29.5, 2.5), (29.5, 6.5), (14, 21.5), (10.5, 18)], fill=head)
        d.line((5, 27, 11, 21), fill=handle, width=3)
        d.line((7, 17, 15, 25), fill=shade(edge, 0.9), width=2)  # crossguard
    else:
        d.line((HANDLE_START, HANDLE_END), fill=handle, width=3)
        d.line(((HANDLE_START[0] + 1, HANDLE_START[1]), (HANDLE_END[0] + 1, HANDLE_END[1])), fill=shade(handle, 1.35), width=1)
        d.polygon([to_world(a, b, HANDLE_END) for a, b in TOOL_SHAPES[kind]], fill=head)
    px = img.load()
    # bevel: lighten top/left edges of the head, darken bottom/right
    head_pixels = [(x, y) for y in range(S) for x in range(S) if px[x, y] == head]
    hp = set(head_pixels)
    for (x, y) in head_pixels:
        if (x, y - 1) not in hp or (x - 1, y) not in hp:
            px[x, y] = edge
        elif (x, y + 1) not in hp or (x + 1, y) not in hp:
            px[x, y] = shade(head, 0.6)
    rnd = random.Random(kind)
    for (x, y) in head_pixels:
        if px[x, y] == head and rnd.random() < 0.18:
            px[x, y] = shade(head, rnd.uniform(0.8, 1.25))
    return outline(img)


# ---------------------------------------------------------------- blocks

def noise_fill(base, var, seed):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S), base)
    px = img.load()
    for y in range(S):
        for x in range(S):
            px[x, y] = shade(base, 1 + rnd.uniform(-var, var))
    return img


def bevel(img, light, dark, width=1):
    px = img.load()
    for i in range(width):
        for k in range(S):
            px[k, i] = mix(px[k, i], light, 0.6)
            px[i, k] = mix(px[i, k], light, 0.6)
            px[k, S - 1 - i] = mix(px[k, S - 1 - i], dark, 0.6)
            px[S - 1 - i, k] = mix(px[S - 1 - i, k], dark, 0.6)
    return img


def table_top():
    img = noise_fill(rgba("2f2a36"), 0.08, "tt")
    d = ImageDraw.Draw(img)
    d.ellipse((3, 3, 28, 28), outline=rgba("b8322a"), width=1)
    d.ellipse((6, 6, 25, 25), outline=rgba("e0563c"), width=1)
    tri = [(15.5, 6), (24.5, 21.5), (6.5, 21.5)]
    d.polygon(tri, outline=rgba("ff9a5c"))
    tri2 = [(15.5, 25), (24.5, 9.5), (6.5, 9.5)]
    d.polygon(tri2, outline=rgba("ff9a5c"))
    d.ellipse((13, 13, 18, 18), fill=rgba("ffd36a"))
    return bevel(img, rgba("5a5068"), rgba("161219"))


def table_side():
    img = noise_fill(rgba("3a3243"), 0.07, "ts")
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, 31, 2), fill=rgba("5a5068"))
    d.rectangle((0, 29, 31, 31), fill=rgba("1d1822"))
    for x in range(3, 31, 7):
        d.rectangle((x, 12, x + 2, 18), fill=rgba("b8322a"))
        d.point((x + 1, 13), fill=rgba("ff9a5c"))
    return img


def table_bottom():
    return bevel(noise_fill(rgba("26212c"), 0.06, "tb"), rgba("3a3243"), rgba("100c12"))


def collector_top(core, seed):
    img = noise_fill(rgba("c9d6dc"), 0.04, seed)
    d = ImageDraw.Draw(img)
    # glass panes
    for (x0, y0) in ((2, 2), (17, 2), (2, 17), (17, 17)):
        d.rectangle((x0, y0, x0 + 12, y0 + 12), fill=rgba("9ec4d6"))
        d.line((x0 + 1, y0 + 11, x0 + 11, y0 + 1), fill=rgba("e4f4fb"))
    d.rectangle((11, 11, 20, 20), fill=shade(core, 0.7))
    d.rectangle((12, 12, 19, 19), fill=core)
    d.rectangle((14, 14, 17, 17), fill=mix(core, (255, 255, 255, 255), 0.6))
    return bevel(img, rgba("eef4f6"), rgba("6d7a80"))


def collector_side(core, seed):
    img = noise_fill(rgba("55606a"), 0.06, seed)
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, 31, 3), fill=rgba("c9d6dc"))
    d.rectangle((4, 8, 27, 27), fill=rgba("3a434b"))
    d.rectangle((6, 10, 25, 25), fill=shade(core, 0.5))
    for i in range(6, 26, 4):
        d.line((6, i, 25, i), fill=shade(core, 0.85))
    d.rectangle((13, 15, 18, 20), fill=core)
    return bevel(img, rgba("7d8892"), rgba("2a3036"))


def condenser_side(accent, seed):
    img = noise_fill(rgba("23202a"), 0.07, seed)
    d = ImageDraw.Draw(img)
    d.rectangle((2, 2, 29, 29), outline=shade(accent, 0.8))
    d.rectangle((5, 5, 26, 26), outline=rgba("3d3848"))
    for x in (8, 15, 22):
        d.line((x, 8, x, 23), fill=shade(accent, 0.55))
    d.rectangle((13, 13, 18, 18), fill=accent)
    return bevel(img, rgba("4a4456"), rgba("0c0a0f"))


def condenser_top(accent, seed):
    img = noise_fill(rgba("23202a"), 0.07, seed)
    d = ImageDraw.Draw(img)
    d.ellipse((4, 4, 27, 27), outline=accent)
    d.ellipse((9, 9, 22, 22), fill=shade(accent, 0.45), outline=shade(accent, 0.9))
    d.ellipse((13, 13, 18, 18), fill=mix(accent, (255, 255, 255, 255), 0.4))
    return bevel(img, rgba("4a4456"), rgba("0c0a0f"))


def matter_block(base, glow, seed):
    img = noise_fill(base, 0.08, seed)
    px = img.load()
    rnd = random.Random(seed)
    for _ in range(40):
        x, y = rnd.randint(2, 29), rnd.randint(2, 29)
        px[x, y] = mix(px[x, y], glow, rnd.uniform(0.3, 0.7))
    d = ImageDraw.Draw(img)
    d.rectangle((3, 3, 28, 28), outline=mix(base, glow, 0.5))
    return bevel(img, mix(base, glow, 0.6), shade(base, 0.4), 2)


def matter_material(base, glow, seed):
    """Seamless material texture for the heads of the 3D matter tools (no border, tiles cleanly)."""
    img = noise_fill(base, 0.10, seed)
    px = img.load()
    rnd = random.Random(seed + "s")
    for _ in range(26):
        x, y = rnd.randrange(S), rnd.randrange(S)
        px[x, y] = mix(px[x, y], glow, rnd.uniform(0.35, 0.8))
        px[(x + 1) % S, y] = mix(px[(x + 1) % S, y], glow, 0.25)
    for _ in range(5):  # faint diagonal sheen, wrapped so the texture still tiles
        x0, y0 = rnd.randrange(S), rnd.randrange(S)
        for k in range(6):
            x, y = (x0 + k) % S, (y0 + k) % S
            px[x, y] = mix(px[x, y], glow, 0.18)
    return img


# ---------------------------------------------------------------- 1.4.0 items and blocks

def dust_pile(base, glow, seed):
    """A small heap of glittering dust."""
    rnd = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(S):
        for x in range(S):
            # mound: wider at the bottom
            top = 26 - 14 * math.exp(-((x - 15.5) / 8.5) ** 2)
            if y < top + rnd.uniform(-0.8, 0.8) or y > 27 or x < 3 or x > 28:
                continue
            t = (y - top) / max(1.0, 27 - top)
            c = mix(shade(base, 1.3), shade(base, 0.65), t)
            px[x, y] = shade(c, rnd.uniform(0.85, 1.15))
    for _ in range(22):
        x, y = rnd.randint(5, 26), rnd.randint(12, 26)
        if px[x, y][3]:
            px[x, y] = mix(px[x, y], glow, rnd.uniform(0.5, 0.95))
    return outline(img)


def ring(band, gem_color, seed, gem_size=3.2):
    """A ring seen from the front with a gem on top."""
    rnd = random.Random(seed)
    img = new()
    px = img.load()
    cx, cy = 15.5, 17.5
    for y in range(S):
        for x in range(S):
            d = math.hypot((x - cx) / 1.0, (y - cy) / 0.85)
            if 7.2 <= d <= 11.2:
                light = 0.75 + 0.5 * max(0.0, -(x - cx) / 11 - (y - cy) / 11)
                px[x, y] = shade(band, light * rnd.uniform(0.94, 1.06))
    if gem_color is not None:
        gx, gy = 15.5, 6.5
        for y in range(S):
            for x in range(S):
                d = math.hypot(x - gx, y - gy)
                if d <= gem_size:
                    px[x, y] = mix(shade(gem_color, 1.4), shade(gem_color, 0.7), (x - gx + y - gy + gem_size * 2) / (gem_size * 4))
        px[int(gx) - 1, int(gy) - 1] = (255, 255, 255, 255)
    return outline(img)


def amulet(chain, pendant, seed):
    rnd = random.Random(seed)
    img = new()
    d = ImageDraw.Draw(img)
    d.arc((6, 1, 25, 20), 200, 340, fill=chain, width=2)
    d.line((8, 8, 13, 17), fill=chain, width=1)
    d.line((23, 8, 18, 17), fill=chain, width=1)
    d.ellipse((9, 15, 22, 28), fill=shade(pendant, 0.75))
    d.ellipse((10, 16, 21, 27), fill=pendant)
    d.ellipse((12, 18, 16, 22), fill=shade(pendant, 1.6))
    px = img.load()
    for _ in range(10):
        x, y = rnd.randint(11, 20), rnd.randint(17, 26)
        if px[x, y][3]:
            px[x, y] = shade(px[x, y], rnd.uniform(0.85, 1.2))
    return outline(img)


def watch():
    img = new()
    d = ImageDraw.Draw(img)
    gold = rgba("d9b440")
    d.ellipse((6, 7, 25, 26), fill=shade(gold, 0.7))
    d.ellipse((7, 8, 24, 25), fill=gold)
    d.ellipse((9, 10, 22, 23), fill=rgba("efe6cf"))
    d.rectangle((14, 3, 17, 7), fill=gold)
    d.line((15.5, 16.5, 15.5, 11), fill=rgba("302010"), width=1)
    d.line((15.5, 16.5, 19.5, 18.5), fill=rgba("302010"), width=1)
    for a in range(12):
        x = 15.5 + 6 * math.cos(a * math.pi / 6)
        y = 16.5 + 6 * math.sin(a * math.pi / 6)
        img.putpixel((int(round(x)), int(round(y))), rgba("6b5530"))
    return outline(img)


def book(cover, accent):
    img = new()
    d = ImageDraw.Draw(img)
    d.rectangle((7, 4, 25, 28), fill=shade(cover, 0.7))
    d.rectangle((8, 4, 25, 27), fill=cover)
    d.rectangle((22, 5, 24, 27), fill=rgba("efe8d8"))
    d.ellipse((11, 10, 20, 19), outline=accent, width=1)
    d.polygon([(15.5, 11), (19, 17), (12, 17)], outline=shade(accent, 1.3))
    d.line((9, 23, 20, 23), fill=accent)
    return outline(img)


def rod(stick, tip, seed):
    img = new()
    d = ImageDraw.Draw(img)
    d.line((5, 27, 22, 10), fill=stick, width=3)
    d.line((6, 27, 23, 10), fill=shade(stick, 1.3), width=1)
    d.ellipse((19, 4, 28, 13), fill=tip)
    d.ellipse((21, 6, 24, 9), fill=shade(tip, 1.6))
    return outline(img)


def lens(rim, glass):
    img = new()
    d = ImageDraw.Draw(img)
    d.ellipse((4, 4, 27, 27), fill=rim)
    d.ellipse((7, 7, 24, 24), fill=glass)
    d.ellipse((9, 9, 14, 14), fill=shade(glass, 1.6))
    d.line((24, 24, 29, 29), fill=rim, width=3)
    return outline(img)


def eye(iris):
    img = new()
    d = ImageDraw.Draw(img)
    d.ellipse((3, 9, 28, 23), fill=rgba("e8e4f0"))
    d.ellipse((10, 9, 21, 23), fill=iris)
    d.ellipse((13, 12, 18, 20), fill=rgba("120818"))
    d.point((14, 13), fill=(255, 255, 255, 255))
    return outline(img)


def wings(feather, core):
    img = new()
    d = ImageDraw.Draw(img)
    for i in range(5):
        d.line((15, 18, 2 + i * 2, 6 + i * 3), fill=shade(feather, 1 - i * 0.07), width=2)
        d.line((16, 18, 29 - i * 2, 6 + i * 3), fill=shade(feather, 1 - i * 0.07), width=2)
    d.line((15.5, 8, 15.5, 29), fill=core, width=2)
    d.ellipse((13, 15, 18, 20), fill=shade(core, 1.4))
    return outline(img)


def cloth(base, seed):
    """Woven cloth for bags (tiles)."""
    rnd = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(S):
        for x in range(S):
            weave = 1.08 if (x // 2 + y // 2) % 2 == 0 else 0.92
            px[x, y] = shade(base, weave * rnd.uniform(0.95, 1.05))
    return img


def fuel_block(base, glow, seed):
    img = noise_fill(base, 0.12, seed)
    px = img.load()
    rnd = random.Random(seed + "g")
    for _ in range(18):
        x, y = rnd.randint(1, 30), rnd.randint(1, 30)
        for k in range(rnd.randint(2, 5)):
            xx, yy = x + k, y + (k % 2)
            if 0 <= xx < S and 0 <= yy < S:
                px[xx, yy] = mix(px[xx, yy], glow, 0.7)
    return bevel(img, mix(base, glow, 0.35), shade(base, 0.45), 1)


def chest_face(kind):
    wood = rgba("3a2a3f")
    trim = rgba("b38cd9")
    img = noise_fill(wood, 0.08, "chest" + kind)
    d = ImageDraw.Draw(img)
    for y in range(0, S, 8):
        d.line((0, y, S - 1, y), fill=shade(wood, 0.7))
    d.rectangle((0, 0, S - 1, S - 1), outline=trim)
    if kind == "side":
        d.rectangle((13, 10, 18, 17), fill=rgba("e0c060"))
        d.rectangle((14, 13, 17, 16), fill=rgba("6b5020"))
    if kind == "top":
        d.ellipse((9, 9, 22, 22), outline=trim)
        d.polygon([(15.5, 10), (21, 19), (10, 19)], outline=shade(trim, 1.2))
    return img


def furnace_face(base, glow, side, seed):
    img = matter_block(base, glow, seed)
    if side:
        d = ImageDraw.Draw(img)
        d.rectangle((8, 14, 23, 27), fill=rgba("120a0a"))
        d.rectangle((9, 22, 22, 26), fill=rgba("ff7a20"))
        d.rectangle((10, 20, 21, 21), fill=rgba("ffb040"))
    return img


def relay_face(core, top, seed):
    img = noise_fill(rgba("1b1b22"), 0.06, seed)
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, S - 1, S - 1), outline=rgba("3a3a48"))
    if top:
        d.ellipse((8, 8, 23, 23), fill=shade(core, 0.6))
        d.ellipse((11, 11, 20, 20), fill=core)
    else:
        for x in (6, 15, 24):
            d.rectangle((x, 6, x + 2, 25), fill=core)
    return bevel(img, rgba("4a4a5a"), rgba("0e0e12"), 1)


def nova_face(band, top, seed):
    img = noise_fill(rgba("c8c0b0") if not top else rgba("8a8070"), 0.08, seed)
    d = ImageDraw.Draw(img)
    if top:
        d.ellipse((10, 10, 21, 21), fill=band)
        d.ellipse((13, 13, 18, 18), fill=rgba("ffe070"))
    else:
        d.rectangle((0, 11, S - 1, 20), fill=band)
        d.rectangle((0, 14, S - 1, 17), fill=shade(band, 1.4))
    return img


def glow_tex(color, seed):
    img = noise_fill(color, 0.06, seed)
    px = img.load()
    for y in range(S):
        for x in range(S):
            d = math.hypot(x - 15.5, y - 15.5) / 22
            px[x, y] = mix(px[x, y], (255, 255, 255, 255), max(0.0, 0.6 - d))
    return img


def gem_material(seed):
    """Iridescent, seamless material for the gem armour."""
    rnd = random.Random(seed)
    img = new()
    px = img.load()
    for y in range(S):
        for x in range(S):
            t = (math.sin((x + y) * 2 * math.pi / S) + 1) / 2
            u = (math.sin((x - y) * 2 * math.pi / S * 2) + 1) / 2
            c = mix(mix(rgba("9fe8ff"), rgba("e6a8ff"), t), rgba("fff6b0"), u * 0.35)
            px[x, y] = shade(c, rnd.uniform(0.92, 1.05))
    return img


def projecte_textures():
    w = []
    for tier, base, glow in (("low", "8f9a8c", "e0f0d8"), ("medium", "3e9e5c", "b8ffcc"), ("high", "3b8fc8", "c8f0ff")):
        w.append(save(dust_pile(rgba(base), rgba(glow), "cd" + tier), f"item/covalencedust-{tier}"))
    # Klein stars use ProjectE's own artwork (MIT licence, see THIRD-PARTY-NOTICES.md), vendored in tools/vendor/projecte.
    vendor = os.path.join(os.path.dirname(os.path.abspath(__file__)), "vendor", "projecte")
    for tier in ("ein", "zwei", "drei", "vier", "sphere", "omega"):
        w.append(save(Image.open(os.path.join(vendor, f"kleinstar-{tier}.png")).convert("RGBA"), f"item/kleinstar-{tier}"))
    w.append(save(ring(rgba("9a9aa4"), None, "ib"), "item/ironband"))
    rings = {
        "blackholeband": (rgba("2a2433"), rgba("05030a")),
        "swiftwolfsrendinggale": (rgba("c8ccd8"), rgba("6fd0ff")),
        "harvestgoddessband": (rgba("5aa040"), rgba("f0e050")),
        "ignitionring": (rgba("b04020"), rgba("ffb030")),
        "zeroring": (rgba("80c8e8"), rgba("e8fbff")),
        "ringofarcana": (rgba("6a3aa0"), rgba("ff60ff")),
    }
    for k, (band, g) in rings.items():
        w.append(save(ring(band, g, "r" + k), f"item/trinket/{k}"))
    for k, base, glow in (("bodystone", "d07a2a", "ffd080"), ("soulstone", "7a2ad0", "e0a0ff"), ("lifestone", "d02a7a", "ffb0d8")):
        w.append(save(sphere(rgba(base), rgba(glow), "s" + k), f"item/trinket/{k}"))
    w.append(save(amulet(rgba("d9b440"), rgba("e0c040"), "rt"), "item/trinket/repairtalisman"))
    w.append(save(gem(rgba("4a1a6a"), "ged"), "item/trinket/gemofeternaldensity"))
    w.append(save(ring(rgba("1a1020"), rgba("b040ff"), "rvoid"), "item/trinket/voidring"))
    w.append(save(amulet(rgba("c0c8d0"), rgba("2a70d0"), "ea"), "item/trinket/evertideamulet"))
    w.append(save(amulet(rgba("403030"), rgba("e04010"), "va"), "item/trinket/volcaniteamulet"))
    w.append(save(watch(), "item/trinket/watchofflowingtime"))
    for kind, base, glow in (("lens", "ff7a1a", "fff0a0"), ("water", "2a6fe0", "bfe8ff"), ("lava", "c83010", "ffb040"),
                             ("fire", "ff9a10", "fff6c0"), ("arrow", "e8e8f0", "ffffff")):
        w.append(save(sphere(rgba(base), rgba(glow), "proj" + kind), f"entity/projectile-{kind}"))
    w.append(save(sphere(rgba("2a2a30"), rgba("ff3020"), "dc"), "item/gadget/destructioncatalyst"))
    w.append(save(lens(rgba("2a2433"), rgba("70c0ff")), "item/gadget/hyperkineticlens"))
    w.append(save(lens(rgba("6a1018"), rgba("ff9060")), "item/gadget/catalyticlens"))
    w.append(save(eye(rgba("c02040")), "item/gadget/mercurialeye"))
    w.append(save(wings(rgba("f0f0f8"), rgba("d9b440")), "item/gadget/archangelssmite"))
    w.append(save(book(rgba("4a1a6a"), rgba("e0c040")), "item/gadget/tomeofknowledge"))
    for tier, tip in (("low", "8f9a8c"), ("medium", "3e9e5c"), ("high", "3b8fc8")):
        w.append(save(rod(rgba("6b4a2a"), rgba(tip), "dr" + tier), f"item/gadget/diviningrod{tier}"))
    bag_colors = {"white": "f0f0f0", "plain": "d8cbb0", "gray": "8a8a8a", "black": "2a2a2e", "brown": "6b4a2a", "red": "b02828",
                  "orange": "e07a20", "yellow": "e0c030", "green": "3a9040", "blue": "2a5ab0", "purple": "7030a0", "pink": "e080b0"}
    for k, c in bag_colors.items():
        w.append(save(cloth(rgba(c), "bag" + k), f"item/bag/alchemicalbag-{k}"))
    w.append(save(cloth(rgba("3a2a3f"), "bagtop"), "item/bag/alchemicalbag-top"))
    w.append(save(gem_material("gemmat"), "item/tool/material/gem"))
    for k, base, glow in (("alchemicalcoal", "4a1b1b", "ff3b2b"), ("mobiusfuel", "6b3a12", "ffb02b"), ("aeternalisfuel", "b9a77a", "fffbe0")):
        w.append(save(fuel_block(rgba(base), rgba(glow), "fb" + k), f"block/alchemicalfuelblock-{k}"))
    for kind in ("top", "side", "bottom"):
        w.append(save(chest_face(kind), f"block/alchemicalchest-{kind}"))
    for k, base, glow in (("dark", "1e1229", "8a52d6"), ("red", "5c0d12", "ff5a48")):
        w.append(save(furnace_face(rgba(base), rgba(glow), False, "ft" + k), f"block/matterfurnace-{k}-top"))
        w.append(save(furnace_face(rgba(base), rgba(glow), True, "fs" + k), f"block/matterfurnace-{k}-side"))
    for tier, core in (("mk1", "e0c040"), ("mk2", "8a52d6"), ("mk3", "ff5a48")):
        w.append(save(relay_face(rgba(core), True, "rt" + tier), f"block/antimatterrelay-{tier}-top"))
        w.append(save(relay_face(rgba(core), False, "rs" + tier), f"block/antimatterrelay-{tier}-side"))
    for k, band in (("catalyst", "c03020"), ("cataclysm", "6a1a9a")):
        w.append(save(nova_face(rgba(band), True, "nt" + k), f"block/novaexplosive-{k}-top"))
        w.append(save(nova_face(rgba(band), False, "ns" + k), f"block/novaexplosive-{k}-side"))
    w.append(save(glow_tex(rgba("ffd860"), "itg"), "block/interdictiontorch-glow"))
    return w


def main():
    written = []
    # items
    written.append(save(gem(rgba("c8202a"), "ps"), "item/philosophersstone"))
    written.append(save(tablet(), "item/transmutationtablet"))
    written.append(save(lump(rgba("4a1b1b"), rgba("ff3b2b"), "ac", 0.5), "item/alchemicalfuel-alchemicalcoal"))
    written.append(save(lump(rgba("6b3a12"), rgba("ffb02b"), "mf", 0.9), "item/alchemicalfuel-mobiusfuel"))
    written.append(save(lump(rgba("b9a77a"), rgba("fffbe0"), "af", 1.4), "item/alchemicalfuel-aeternalisfuel"))
    written.append(save(sphere(rgba("2a1638"), rgba("9b4dff"), "dm"), "item/darkmatter"))
    written.append(save(sphere(rgba("6e0f14"), rgba("ff4038"), "rm"), "item/redmatter"))
    mats = {
        "dark": (rgba("2c1a3a"), rgba("8a52d6"), rgba("1d1424")),
        "red": (rgba("7a1418"), rgba("ff5a48"), rgba("3a0c10")),
    }
    for mat, (head, edge, handle) in mats.items():
        for kind in list(TOOL_SHAPES) + ["sword"]:
            written.append(save(tool(kind, head, edge, handle), f"item/tool/{mat}matter-{kind}"))
    # blocks
    written.append(save(table_top(), "block/transmutationtable-top"))
    written.append(save(table_side(), "block/transmutationtable-side"))
    written.append(save(table_bottom(), "block/transmutationtable-bottom"))
    cores = {"mk1": rgba("ffd24a"), "mk2": rgba("9b5cff"), "mk3": rgba("ff4a3c")}
    for tier, core in cores.items():
        written.append(save(collector_top(core, "ct" + tier), f"block/energycollector-{tier}-top"))
        written.append(save(collector_side(core, "cs" + tier), f"block/energycollector-{tier}-side"))
    accents = {"mk1": rgba("9b5cff"), "mk2": rgba("ff4a3c")}
    for tier, accent in accents.items():
        written.append(save(condenser_top(accent, "kt" + tier), f"block/energycondenser-{tier}-top"))
        written.append(save(condenser_side(accent, "ks" + tier), f"block/energycondenser-{tier}-side"))
    written.append(save(matter_block(rgba("1e1229"), rgba("8a52d6"), "mbd"), "block/matterblock-dark"))
    written.append(save(matter_block(rgba("5c0d12"), rgba("ff5a48"), "mbr"), "block/matterblock-red"))
    written.append(save(matter_material(rgba("22132f"), rgba("8a52d6"), "mmd"), "item/tool/material/darkmatter"))
    written.append(save(matter_material(rgba("62101a"), rgba("ff5a48"), "mmr"), "item/tool/material/redmatter"))
    written += projecte_textures()

    if "--preview" in sys.argv:
        scale = 4
        cols = 8
        rows = (len(written) + cols - 1) // cols
        sheet = Image.new("RGBA", (cols * (S * scale + 8) + 8, rows * (S * scale + 8) + 8), (60, 64, 72, 255))
        for i, path in enumerate(written):
            tex = Image.open(path).resize((S * scale, S * scale), Image.NEAREST)
            x = 8 + (i % cols) * (S * scale + 8)
            y = 8 + (i // cols) * (S * scale + 8)
            sheet.alpha_composite(tex, (x, y))
        sheet.save(os.path.join(ROOT, "..", "..", "..", "preview.png"))
    print(f"wrote {len(written)} textures")


if __name__ == "__main__":
    main()
