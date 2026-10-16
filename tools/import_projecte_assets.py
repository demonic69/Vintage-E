#!/usr/bin/env python3
"""
Copies ProjectE's original textures and sounds into Vintage-E for the ProjectETextures / ProjectESounds settings.

    python3 tools/import_projecte_assets.py [path to a ProjectE checkout]

ProjectE (https://github.com/sinkillerj/ProjectE) is MIT licensed; see THIRD-PARTY-NOTICES.md. Files are copied
unchanged into assets/vintagee/textures/projecte/ and assets/vintagee/sounds/projecte/. A few faces are cut out of
ProjectE's chest-model textures (condensers, alchemical chest) or its torch sprite, because Vintage-E draws those
blocks as plain cubes or its own models; those go to textures/projecte/derived/.

It also writes assets/vintagee/config/vintagee-projecte-look.json, which says which ProjectE texture replaces which
Vintage-E one. The mod applies it on the server at start-up when ProjectETextures is on (see ProjectELook.cs).
"""
import json, os, shutil, sys
from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
A = os.path.join(ROOT, "assets", "vintagee")
PE_REPO = sys.argv[1] if len(sys.argv) > 1 else "/home/claude/sinkillerj/projecte"
PE = os.path.join(PE_REPO, "src", "main", "resources", "assets", "projecte")
OUT_TEX = os.path.join(A, "textures", "projecte")
OUT_SND = os.path.join(A, "sounds", "projecte")

# Vintage-E texture → ProjectE texture (paths inside each mod's textures/ folder, no .png).
FILES = {
    # Rings, amulets and stones (the "off" look; the "on" look is picked while rendering, see ProjectELook.cs)
    "item/trinket/blackholeband": "item/rings/black_hole_off",
    "item/trinket/bodystone": "item/rings/body_stone_off",
    "item/trinket/evertideamulet": "item/rings/evertide_amulet",
    "item/trinket/gemofeternaldensity": "item/dense_gem_off",
    "item/trinket/harvestgoddessband": "item/rings/harvest_god_off",
    "item/trinket/ignitionring": "item/rings/ignition_off",
    "item/trinket/lifestone": "item/rings/life_stone_off",
    "item/trinket/repairtalisman": "item/repair_talisman",
    "item/trinket/ringofarcana": "item/rings/arcana_0",
    "item/trinket/soulstone": "item/rings/soul_stone_off",
    "item/trinket/swiftwolfsrendinggale": "item/rings/swrg_off",
    "item/trinket/voidring": "item/rings/void_off",
    "item/trinket/volcaniteamulet": "item/rings/volcanite_amulet",
    "item/trinket/watchofflowingtime": "item/rings/time_watch_off",
    "item/trinket/zeroring": "item/rings/zero_off",
    # Gadgets
    "item/gadget/archangelssmite": "item/rings/archangel_smite",
    "item/gadget/catalyticlens": "item/catalytic_lens",
    "item/gadget/destructioncatalyst": "item/destruction_catalyst",
    "item/gadget/diviningrodlow": "item/divining_rod_1",
    "item/gadget/diviningrodmedium": "item/divining_rod_2",
    "item/gadget/diviningrodhigh": "item/divining_rod_3",
    "item/gadget/hyperkineticlens": "item/hyperkinetic_lens",
    "item/gadget/mercurialeye": "item/mercurial_eye",
    "item/gadget/tomeofknowledge": "item/tome",
    # Other items
    "item/ironband": "item/rings/iron_band",
    "item/philosophersstone": "item/philosophers_stone",
    "item/transmutationtablet": "item/transmutation_tablet",
    "item/darkmatter": "item/matter/dark",
    "item/redmatter": "item/matter/red",
    "item/covalencedust-low": "item/covalence_dust/low",
    "item/covalencedust-medium": "item/covalence_dust/medium",
    "item/covalencedust-high": "item/covalence_dust/high",
    "item/alchemicalfuel-alchemicalcoal": "item/fuels/alchemical_coal",
    "item/alchemicalfuel-mobiusfuel": "item/fuels/mobius",
    "item/alchemicalfuel-aeternalisfuel": "item/fuels/aeternalis",
    # Matter as a material: tool heads, armour, the pedestal and the matter furnace use the matter blocks' look
    "item/tool/material/darkmatter": "block/dark_matter_block",
    "item/tool/material/redmatter": "block/red_matter_block",
    # Blocks
    "block/matterblock-dark": "block/dark_matter_block",
    "block/matterblock-red": "block/red_matter_block",
    "block/alchemicalfuelblock-alchemicalcoal": "block/alchemical_coal_block",
    "block/alchemicalfuelblock-mobiusfuel": "block/mobius_fuel_block",
    "block/alchemicalfuelblock-aeternalisfuel": "block/aeternalis_fuel_block",
    "block/transmutationtable-top": "block/transmutation_stone/top",
    "block/transmutationtable-side": "block/transmutation_stone/side",
    "block/transmutationtable-bottom": "block/transmutation_stone/bottom",
    "block/energycollector-mk1-top": "block/collectors/top_1",
    "block/energycollector-mk2-top": "block/collectors/top_2",
    "block/energycollector-mk3-top": "block/collectors/top_3",
    "block/energycollector-mk1-side": "block/collectors/other",
    "block/energycollector-mk2-side": "block/collectors/other",
    "block/energycollector-mk3-side": "block/collectors/other",
    "block/antimatterrelay-mk1-top": "block/relays/top_1",
    "block/antimatterrelay-mk2-top": "block/relays/top_2",
    "block/antimatterrelay-mk3-top": "block/relays/top_3",
    "block/antimatterrelay-mk1-side": "block/relays/other",
    "block/antimatterrelay-mk2-side": "block/relays/other",
    "block/antimatterrelay-mk3-side": "block/relays/other",
    "block/energycondenser-mk1-top": "derived/condenser_mk1-top",
    "block/energycondenser-mk1-side": "derived/condenser_mk1-side",
    "block/energycondenser-mk2-top": "derived/condenser_mk2-top",
    "block/energycondenser-mk2-side": "derived/condenser_mk2-side",
    "block/alchemicalchest-top": "derived/alchemical_chest-top",
    "block/alchemicalchest-side": "derived/alchemical_chest-side",
    "block/alchemicalchest-bottom": "derived/alchemical_chest-bottom",
    "block/novaexplosive-catalyst-side": "block/explosives/nova_side",
    "block/novaexplosive-catalyst-top": "block/explosives/top",
    "block/novaexplosive-cataclysm-side": "block/explosives/nova1_side",
    "block/novaexplosive-cataclysm-top": "block/explosives/top",
    # Projectiles (drawn with a shape that shows the whole sprite, see "overrides")
    "entity/projectile-fire": "entity/fireball",
    "entity/projectile-lava": "entity/lava_orb",
    "entity/projectile-water": "entity/water_orb",
    "entity/projectile-lens": "entity/lens_explosive",
}

# Shown while rendering, not referenced by any block or item: the rings' "on" looks and the Ring of Arcana's modes.
EXTRA = [
    "item/rings/black_hole_on", "item/rings/body_stone_on", "item/dense_gem_on", "item/rings/harvest_god_on",
    "item/rings/ignition_on", "item/rings/life_stone_on", "item/rings/soul_stone_on", "item/rings/void_on",
    "item/rings/time_watch_on", "item/rings/zero_on", "item/rings/swrg_on1", "item/rings/swrg_on2", "item/rings/swrg_on3",
    "item/rings/arcana_0_on", "item/rings/arcana_1", "item/rings/arcana_1_on", "item/rings/arcana_2",
    "item/rings/arcana_2_on", "item/rings/arcana_3", "item/rings/arcana_3_on",
]

# Single faces set by "overrides" below (not swapped file for file).
FACES = ["block/collectors/front", "block/relays/front", "block/explosives/bottom"]

# Vintage-E bag colours → ProjectE alchemical bag colours (ProjectE has no undyed bag; light grey is closest).
BAGS = {"white": "white", "plain": "light_gray", "gray": "gray", "black": "black", "brown": "brown", "red": "red",
        "orange": "orange", "yellow": "yellow", "green": "green", "blue": "blue", "purple": "purple", "pink": "pink"}

# ProjectE sound events (sounds.json) → the file each plays. Only the eight ProjectE actually uses.
SOUNDS = {"charge": "pecharge", "uncharge": "peuncharge", "heal": "peheal", "destruct": "pedestruct",
          "power": "pepower", "transmute": "petransmute", "watermagic": "pewatermagic", "windmagic": "pewindmagic"}


def pe_path(rel):
    return os.path.join(PE, "textures", rel + ".png")


def copy_texture(rel):
    src = pe_path(rel)
    dst = os.path.join(OUT_TEX, rel + ".png")
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copyfile(src, dst)


def pad16(im):
    """Grows a 14×14 / 14×15 chest face to a full 16×16 block face by repeating its edge pixels (keeps the pixel art sharp)."""
    w, h = im.size
    out = Image.new("RGBA", (16, 16))
    left, top = (16 - w) // 2, (16 - h) // 2
    for y in range(16):
        for x in range(16):
            sx = min(max(x - left, 0), w - 1)
            sy = min(max(y - top, 0), h - 1)
            out.putpixel((x, y), im.getpixel((sx, sy)))
    return out


def chest_faces(rel, name):
    """
    Minecraft's chest model texture (64×64): the lid is a 14×5×14 box at texture offset (0,0), the base a 14×10×14 box
    at (0,19). The lid's top is the 14×14 square at (28,0) and the base's underside the one at (14,19); the front (with
    the latch) is the fourth side strip, x 42-56, rows 14-19 for the lid and 33-43 for the base. Chests are drawn
    without the entity model's y flip, so the top and side strips are stored upside down.
    """
    im = Image.open(pe_path(rel)).convert("RGBA")
    flip = Image.Transpose.FLIP_TOP_BOTTOM
    top = im.crop((28, 0, 42, 14)).transpose(flip)
    bottom = im.crop((14, 19, 28, 33))
    lid = im.crop((42, 14, 56, 19)).transpose(flip)
    base = im.crop((42, 33, 56, 43)).transpose(flip)
    side = Image.new("RGBA", (14, 15))
    side.paste(lid, (0, 0))
    side.paste(base, (0, 5))
    out = os.path.join(OUT_TEX, "derived")
    os.makedirs(out, exist_ok=True)
    pad16(top).save(os.path.join(out, name + "-top.png"))
    pad16(side).save(os.path.join(out, name + "-side.png"))
    pad16(bottom).save(os.path.join(out, name + "-bottom.png"))


def opaque(im):
    """Fills transparent pixels with the nearest opaque pixel's colour (cube faces can't have holes)."""
    w, h = im.size
    px = [(x, y) for y in range(h) for x in range(w) if im.getpixel((x, y))[3] > 0]
    out = im.copy()
    for y in range(h):
        for x in range(w):
            if im.getpixel((x, y))[3] == 0:
                nx, ny = min(px, key=lambda p: (p[0] - x) ** 2 + (p[1] - y) ** 2)
                out.putpixel((x, y), im.getpixel((nx, ny))[:3] + (255,))
    return out


def torch_parts():
    """
    ProjectE's interdiction torch is a Minecraft torch sprite: a 2-pixel stick (rows 8-15) under a 4×4 green flame
    (rows 5-8). Vintage-E's torch model samples a 2×8 rod, a 4×4 base and a 3×3 head, so those are rebuilt from it.
    """
    im = Image.open(pe_path("block/interdiction_torch")).convert("RGBA")
    stick = im.crop((7, 8, 9, 16))  # 2×8
    rod = Image.new("RGBA", (16, 16))
    for y in range(16):
        for x in range(16):
            rod.putpixel((x, y), stick.getpixel((x % 2, y % 8)))
    flame = opaque(im.crop((6, 5, 10, 9)))  # 4×4
    head = flame.resize((3, 3), Image.NEAREST)
    glow = Image.new("RGBA", (16, 16))
    for y in range(16):
        for x in range(16):
            glow.putpixel((x, y), head.getpixel((x % 3, y % 3)))
    out = os.path.join(OUT_TEX, "derived")
    os.makedirs(out, exist_ok=True)
    rod.save(os.path.join(out, "interdiction_torch-rod.png"))
    glow.save(os.path.join(out, "interdiction_torch-glow.png"))


def main():
    if not os.path.isdir(PE):
        sys.exit(f"ProjectE assets not found at {PE}")
    if os.path.isdir(OUT_TEX): shutil.rmtree(OUT_TEX)
    if os.path.isdir(OUT_SND): shutil.rmtree(OUT_SND)

    for rel in sorted(set(v for v in FILES.values() if not v.startswith("derived/")) | set(EXTRA) | set(FACES)):
        copy_texture(rel)
    for color in sorted(set(BAGS.values())):
        copy_texture("item/alchemy_bags/" + color)
    chest_faces("block/condenser_mk1", "condenser_mk1")
    chest_faces("block/condenser_mk2", "condenser_mk2")
    chest_faces("block/alchemical_chest", "alchemical_chest")
    torch_parts()

    os.makedirs(OUT_SND, exist_ok=True)
    for event, f in SOUNDS.items():
        shutil.copyfile(os.path.join(PE, "sounds", "item", f + ".ogg"), os.path.join(OUT_SND, event + ".ogg"))

    def ours(rel): return "vintagee:" + rel
    def theirs(rel): return "vintagee:projecte/" + rel

    for k in FILES:
        if not os.path.exists(os.path.join(A, "textures", k + ".png")):
            sys.exit(f"Vintage-E texture {k} doesn't exist")

    overrides = []
    for tier in ("mk1", "mk2", "mk3"):
        # ProjectE collectors and relays have one front face; the rest, underneath included, are plain.
        overrides.append({"code": f"vintagee:energycollector-{tier}", "textures": {"north": theirs("block/collectors/front"), "down": theirs("block/collectors/other")}})
        overrides.append({"code": f"vintagee:antimatterrelay-{tier}", "textures": {"north": theirs("block/relays/front"), "down": theirs("block/relays/other")}})
    for tier in ("mk1", "mk2"):
        overrides.append({"code": f"vintagee:energycondenser-{tier}", "textures": {"down": theirs(f"derived/condenser_{tier}-bottom")}})
    overrides.append({"code": "vintagee:novaexplosive-*", "textures": {"down": theirs("block/explosives/bottom")}})
    overrides.append({"code": "vintagee:interdictiontorch*", "textures": {"rod": theirs("derived/interdiction_torch-rod"), "glow": theirs("derived/interdiction_torch-glow")}})
    for kind in ("fire", "lava", "water", "lens"):
        overrides.append({"code": f"vintagee:projectile-{kind}", "shape": "vintagee:entity/projectile-sprite"})
    for ve, pe in BAGS.items():
        # ProjectE's bags are flat sprites, so they're drawn like other flat items instead of as a sack.
        overrides.append({"code": f"vintagee:alchemicalbag-{ve}", "sprite": theirs("item/alchemy_bags/" + pe)})

    look = {
        "//": "Written by tools/import_projecte_assets.py. Used when the server setting ProjectETextures is on: each Vintage-E texture on the left is "
              "swapped for the ProjectE texture on the right; 'overrides' set single faces/texture codes, swap shapes, or draw an item as a flat sprite.",
        "textures": {ours(k): theirs(v) for k, v in FILES.items()},
        "overrides": overrides,
    }
    with open(os.path.join(A, "config", "vintagee-projecte-look.json"), "w", encoding="utf-8") as f:
        json.dump(look, f, indent=2)
        f.write("\n")

    n_tex = sum(len(fs) for _, _, fs in os.walk(OUT_TEX))
    print(f"{n_tex} textures, {len(SOUNDS)} sounds, {len(FILES)} texture swaps, {len(overrides)} overrides")


if __name__ == "__main__":
    main()
