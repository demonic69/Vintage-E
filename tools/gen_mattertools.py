#!/usr/bin/env python3
"""Writes assets/vintagee/itemtypes/mattertool.json: the dark/red matter tools as 3D items.

The pickaxe, shovel, axe, hoe and sword reuse the game's own 3D tool models (so they are held, swung and
displayed exactly like vanilla metal tools) with matter textures swapped in. Transforms and animations are
the vanilla values for each tool. The sledgehammer has its own model (shapes/item/tool/sledgehammer.json).
"""
import json
import os
from collections import OrderedDict as O

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "vintagee", "itemtypes", "mattertool.json")
TOOLS = ["pickaxe", "shovel", "axe", "sword", "hoe", "hammer"]
HEAD = {"dark": "vintagee:item/tool/material/darkmatter", "red": "vintagee:item/tool/material/redmatter"}
HANDLE = {"dark": "game:block/wood/debarked/ebony", "red": "vintagee:item/tool/material/darkmatter"}
BAND = {"dark": "vintagee:item/tool/material/redmatter", "red": "vintagee:item/tool/material/darkmatter"}
STRING = "game:item/resource/string"
CLEAR = "game:block/transparent"


def t(x=0, y=0, z=0):
    return O([("x", x), ("y", y), ("z", z)])


def xf(rot=None, origin=None, scale=None, trans=None, rotate=None):
    d = O()
    if rotate is not None:
        d["rotate"] = rotate
    if trans is not None:
        d["translation"] = t(*trans)
    if rot is not None:
        d["rotation"] = t(*rot)
    if origin is not None:
        d["origin"] = t(*origin)
    if scale is not None:
        d["scale"] = scale
    return d


SHAPE = {
    "pickaxe": "game:item/tool/pickaxe-iron",
    "shovel": "game:item/tool/shovel-copper",
    "axe": "game:item/tool/axe/iron",
    "hoe": "game:item/tool/hoe-copper",
    "sword": "game:item/tool/blade/falx",
    "hammer": "vintagee:item/tool/sledgehammer",
}


def textures(tool, mat):
    head, handle = HEAD[mat], HANDLE[mat]
    if tool == "pickaxe":
        return O([("metal", head), ("handle", handle), ("string", STRING)])
    if tool in ("shovel", "axe", "hoe"):
        return O([("material", head), ("handle", handle), ("string", STRING)])
    if tool == "sword":
        return O([("falxblade", head), ("handguard", head), ("spinerivet", head), ("handle", handle),
                  ("falxstring", CLEAR), ("thongs", CLEAR), ("woodguard", CLEAR), ("cloth", CLEAR), ("pips", CLEAR)])
    if tool == "hammer":
        return O([("head", head), ("handle", handle), ("band", BAND[mat])])


# Vanilla values (survival/itemtypes/tool/*.json, 1.22.7) for the matching metal tools.
GUI = {
    "pickaxe": xf(rot=(-89, 47, 33), origin=(0.65, 0, 0.47), scale=1.49),
    "shovel": xf(rot=(-115, -28, 139), origin=(0.7, 0, 0.55)),
    "axe": xf(trans=(0, 0, 0), rot=(-63, -123, -180), origin=(0.61, 0, 0.47), scale=1.58),
    "hoe": xf(rotate=False, trans=(0, 0, 0), rot=(-56, -127, -180), origin=(0.35, 0, 0.55), scale=1.43),
    "sword": xf(trans=(0, 0, 0), rot=(-53, 118, -5), origin=(0.65, 0.1, 0.37), scale=1.65),
}
TP = {
    "pickaxe": xf(trans=(-0.87, -0.01, -0.56), rot=(-90, 0, 0), origin=(0.5, 0, 0.5), scale=0.8),
    "shovel": xf(trans=(-1.69, -0.29, -0.6), rot=(0, 0, -10), scale=0.8),
    "axe": xf(trans=(-0.43, -0.52, -0.07), rot=(90, 0, 0), scale=0.95),
    "hoe": xf(trans=(-1, -0.5, -0.08), rot=(90, 0, -5), scale=1),
    "sword": xf(trans=(-1.03, -0.005, -0.66), rot=(85, 0, 2), origin=(0.5, 0, 0.5), scale=0.8),
}
GROUND = {
    "pickaxe": xf(trans=(0, 0, 0), rot=(0, 0, 0), origin=(0.5, 0, 0.5), scale=3.4),
    "shovel": xf(trans=(0, 0, 0), rot=(0, 0, 0), origin=(0.5, 0, 0.5), scale=3.6),
    "axe": xf(trans=(0, 0, 0), rot=(0, 0, 0), origin=(0.5, 0, 0.5), scale=3.6),
    "hoe": xf(trans=(0, 0, 0), rot=(0, 0, 0), origin=(0.5, 0, 0.5), scale=3.6),
    "sword": xf(trans=(0, 0, 0), rot=(0, 0, 0), origin=(0.5, 0, 0.5), scale=3.4),
}
TOOLRACK = {
    "pickaxe": O([("rotation", O([("y", 2.5), ("z", -1)])), ("translation", O([("x", -0.1), ("y", 0.59)])), ("scale", 1.25)]),
    "shovel": O([("rotation", O([("y", 1), ("z", -1)])), ("translation", O([("x", -0.47), ("y", 0.6)])), ("scale", 1.25)]),
    "axe": O([("rotation", O([("y", 1), ("z", -1)])), ("translation", O([("x", -0.3), ("y", 0.6)])), ("scale", 1.25)]),
    "hoe": O([("rotation", O([("y", 1), ("z", -1)])), ("translation", O([("x", -0.25), ("y", 0.6)])), ("scale", 1.25)]),
    "sword": O([("rotation", O([("y", 180), ("z", 1)])), ("translation", O([("x", -0.1), ("y", 0.6)])), ("scale", 1.25)]),
}
GROUNDSTORAGE = {
    "pickaxe": xf(trans=(0, 0.1, -0.105), rot=(20, 88, -87), scale=0.88),
    "shovel": xf(trans=(0, 0.59, -0.15), rot=(13, 88, -87), scale=0.88),
    "axe": xf(trans=(0, 0.1, -0.105), rot=(20, 88, -87), scale=0.88),
    "hoe": xf(trans=(0, 0.04, -0.25), rot=(23, 87, -90), scale=0.88),
    "sword": xf(trans=(0.24, 0.195, -0.06), rot=(24, 61, -87), scale=0.88),
}
WALL_OFF_Y = {"pickaxe": 1, "shovel": 2, "axe": 1, "hoe": 1, "sword": 1, "hammer": 1}
HIT = {"pickaxe": "pickaxe", "shovel": "shoveldig", "axe": "axechop", "hoe": "breaktool", "sword": "falx", "hammer": "pickaxe"}

# The sledgehammer model (tools/gen_sledgehammer.py) is built in the vanilla hammer's frame, so these are the
# vanilla hammer values, re-centred and scaled for a model about twice as long. It swings like a pickaxe.
GUI["hammer"] = xf(trans=(0, 0, 0), rot=(-77, 46, 8), origin=(0.76, 0.5, 0.5), scale=1.55)
TP["hammer"] = xf(trans=(-0.65, -0.48, -0.52), rot=(90, 1, 0), scale=1)
GROUND["hammer"] = xf(trans=(0, 0, 0), rot=(0, 0, 0), origin=(0.73, 0.45, 0.5), scale=2.7)
TOOLRACK["hammer"] = O([("rotation", O([("y", 1), ("z", -1)])), ("translation", O([("x", -0.2), ("y", 0.02)])), ("scale", 0.9)])
GROUNDSTORAGE["hammer"] = xf(trans=(0, -0.16, 0.33), rot=(26, 88, -87), scale=0.6)

AREA = {  # (max radius, default radius)
    ("dark", "pickaxe"): (1, 0), ("dark", "shovel"): (1, 0), ("dark", "axe"): (1, 0), ("dark", "hammer"): (1, 1),
    ("red", "pickaxe"): (2, 0), ("red", "shovel"): (2, 0), ("red", "axe"): (2, 0), ("red", "hammer"): (2, 1),
}


def ground_storable(tool):
    return O([("name", "GroundStorable"), ("properties", O([
        ("layout", "WallHalves"), ("wallOffY", WALL_OFF_Y[tool]), ("ctrlKey", True),
        ("selectionBox", O([("x1", 0), ("y1", 0), ("z1", 0), ("x2", 1), ("y2", 0.1), ("z2", 1)])),
        ("collisionBox", O([("x1", 0), ("y1", 0), ("z1", 0), ("x2", 0), ("y2", 0), ("z2", 0)]))]))])


def behaviors(mat, tool):
    mx, df = AREA.get((mat, tool), (0, 0))
    props = O([("maxAreaRadius", mx)])
    if df:
        props["defaultAreaRadius"] = df
    list_ = [ground_storable(tool), O([("name", "VintageEMatterTool"), ("properties", props)])]
    if tool == "sword":
        list_.append(O([("name", "AnimationAuthoritative")]))
    if tool == "axe":
        list_.append(O([("name", "AnimationAuthoritative"), ("properties", O([("onlyOnEntity", True)]))]))
    return list_


MINING = {
    ("dark", "pickaxe"): dict(stone=9, ore=9, metal=9, ice=9, brick=9, ceramic=9, glass=9),
    ("red", "pickaxe"): dict(stone=14, ore=14, metal=14, ice=14, brick=14, ceramic=14, glass=14),
    ("dark", "shovel"): dict(soil=9, sand=9, gravel=9, snow=9),
    ("red", "shovel"): dict(soil=14, sand=14, gravel=14, snow=14),
    ("dark", "axe"): dict(wood=9, plant=6, leaves=6),
    ("red", "axe"): dict(wood=14, plant=9, leaves=9),
    ("dark", "sword"): dict(plant=4, leaves=4),
    ("red", "sword"): dict(plant=6, leaves=6),
    ("dark", "hoe"): dict(soil=5, plant=5),
    ("red", "hoe"): dict(soil=8, plant=8),
    ("dark", "hammer"): dict(stone=8, ore=8, metal=8, ice=8, brick=8, ceramic=8, glass=8),
    ("red", "hammer"): dict(stone=12, ore=12, metal=12, ice=12, brick=12, ceramic=12, glass=12),
}


def main():
    d = O()
    d["code"] = "mattertool"
    d["variantgroups"] = [O([("code", "material"), ("states", ["dark", "red"])]), O([("code", "tool"), ("states", TOOLS)])]
    d["classByType"] = O([("*-axe", "ItemAxe"), ("*-hoe", "ItemHoe")])
    d["toolByType"] = O([("*-pickaxe", "pickaxe"), ("*-shovel", "shovel"), ("*-axe", "axe"), ("*-sword", "sword"), ("*-hoe", "hoe"), ("*-hammer", "pickaxe")])
    d["behaviorsByType"] = O((f"*-{m}-{tl}", behaviors(m, tl)) for m in ("dark", "red") for tl in TOOLS)
    d["maxstacksize"] = 1
    d["creativeinventory"] = O([("general", ["*"]), ("tools", ["*"]), ("vintagee", ["*"])])
    d["shapeByType"] = O((f"*-{tl}", O([("base", SHAPE[tl])])) for tl in TOOLS)
    d["texturesByType"] = O((f"*-{m}-{tl}", O((k, O([("base", v)])) for k, v in textures(tl, m).items())) for m in ("dark", "red") for tl in TOOLS)
    d["heldTpHitAnimationByType"] = O((f"*-{tl}", HIT[tl]) for tl in TOOLS)
    d["heldTpUseAnimationByType"] = O([("*-shovel", "interactStaticLong"), ("*-hoe", "hoe"), ("*", "interactstatic")])
    d["heldTpIdleAnimationByType"] = O([("*-shovel", "shovelidle")])
    d["heldRightReadyAnimationByType"] = O([("*-shovel", "shovelready"), ("*", "helditemready")])
    d["guiTransformByType"] = O((f"*-{tl}", GUI[tl]) for tl in TOOLS)
    d["tpHandTransformByType"] = O((f"*-{tl}", TP[tl]) for tl in TOOLS)
    d["groundTransformByType"] = O((f"*-{tl}", GROUND[tl]) for tl in TOOLS)
    d["attributesByType"] = O((f"*-{tl}", O([
        ("handbook", O([("groupBy", ["mattertool-*-" + tl])])),
        ("attachableToEntity", O([("categoryCode", "toolholding")])),
        ("toolrackTransform", TOOLRACK[tl]),
        ("groundStorageTransform", GROUNDSTORAGE[tl]),
    ])) for tl in TOOLS)
    d["tooltierByType"] = O([("*-dark-*", 5), ("*-red-*", 6)])
    d["miningspeedByType"] = O((f"*-{m}-{tl}", MINING[(m, tl)]) for m in ("dark", "red") for tl in TOOLS)
    d["attackpowerByType"] = O([("*-dark-sword", 8), ("*-red-sword", 11), ("*-dark-axe", 5), ("*-red-axe", 7),
                                ("*-dark-hammer", 5), ("*-red-hammer", 7), ("*-dark-*", 3.5), ("*-red-*", 4.5)])
    d["attackRangeByType"] = O([("*-sword", 2.75), ("*", 2.25)])
    d["materialDensity"] = 9000
    with open(OUT, "w") as f:
        json.dump(d, f, indent=2)
        f.write("\n")
    print("wrote", OUT)


if __name__ == "__main__":
    main()
