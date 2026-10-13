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
