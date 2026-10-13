#!/usr/bin/env python3
"""Writes the JSON assets for the ProjectE-style items, blocks and recipes added in 1.4.0:
covalence dust, Klein stars, the iron band, rings/stones/amulets/talisman/watch ("trinket"), gadgets,
alchemical bags, matter armour, red matter katar/morning star, fuel blocks, the alchemical chest, matter
furnaces, anti-matter relays, the interdiction torch, nova explosives and the pedestal.
Also writes their models and English names. Run from anywhere: python3 tools/gen_projecte.py
"""
import json
import os
from collections import OrderedDict as O

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "vintagee")


def write(rel, data):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")


def xyz(x=0, y=0, z=0):
    return O([("x", x), ("y", y), ("z", z)])


def xf(trans=(0, 0, 0), rot=(0, 0, 0), origin=None, scale=1.0, rotate=None):
    d = O()
    if rotate is not None:
        d["rotate"] = rotate
    d["translation"] = xyz(*trans)
    d["rotation"] = xyz(*rot)
    if origin is not None:
        d["origin"] = xyz(*origin)
    d["scale"] = scale
    return d


CREATIVE = O([("general", ["*"]), ("vintagee", ["*"])])

DUSTS = ["low", "medium", "high"]
STARS = ["ein", "zwei", "drei", "vier", "sphere", "omega"]
STAR_CAP = {"ein": 50000, "zwei": 200000, "drei": 800000, "vier": 3200000, "sphere": 12800000, "omega": 51200000}
TRINKETS = ["blackholeband", "swiftwolfsrendinggale", "harvestgoddessband", "ignitionring", "zeroring", "ringofarcana",
            "bodystone", "soulstone", "lifestone", "repairtalisman", "gemofeternaldensity", "evertideamulet",
            "volcaniteamulet", "watchofflowingtime"]
GADGETS = ["destructioncatalyst", "hyperkineticlens", "catalyticlens", "mercurialeye", "archangelssmite",
           "tomeofknowledge", "diviningrodlow", "diviningrodmedium", "diviningrodhigh"]
BAGS = ["white", "plain", "gray", "black", "brown", "red", "orange", "yellow", "green", "blue", "purple", "pink"]
ARMOR_MATS = ["dark", "red", "gem"]
PARTS = ["head", "body", "legs"]
FUELS = ["alchemicalcoal", "mobiusfuel", "aeternalisfuel"]


def items():
    write("itemtypes/covalencedust.json", O([
        ("code", "covalencedust"),
        ("variantgroups", [O([("code", "tier"), ("states", DUSTS)])]),
        ("maxstacksize", 64),
        ("creativeinventory", CREATIVE),
        ("texture", O([("base", "item/covalencedust-{tier}")])),
        ("materialDensity", 400),
    ]))
    write("itemtypes/kleinstar.json", O([
        ("code", "kleinstar"),
        ("class", "ItemKleinStar"),
        ("variantgroups", [O([("code", "tier"), ("states", STARS)])]),
        ("maxstacksize", 1),
        ("creativeinventory", CREATIVE),
        ("texture", O([("base", "item/kleinstar-{tier}")])),
        ("attributesByType", O((f"*-{s}", O([("capacity", STAR_CAP[s])])) for s in STARS)),
        ("materialDensity", 3000),
    ]))
    write("itemtypes/ironband.json", O([
        ("code", "ironband"),
        ("maxstacksize", 16),
        ("creativeinventory", CREATIVE),
        ("texture", O([("base", "item/ironband")])),
        ("materialDensity", 7800),
    ]))
    write("itemtypes/trinket.json", O([
        ("code", "trinket"),
        ("class", "ItemEmcTrinket"),
        ("variantgroups", [O([("code", "type"), ("states", TRINKETS)])]),
        ("maxstacksize", 1),
        ("creativeinventory", CREATIVE),
        ("textureByType", O([("*", O([("base", "item/trinket/{type}")]))])),
        ("attributesByType", O([("*-gemofeternaldensity", O([("maxInputValue", 8)]))])),
        ("materialDensity", 5000),
    ]))
    write("itemtypes/gadget.json", O([
        ("code", "gadget"),
        ("class", "ItemEmcGadget"),
        ("variantgroups", [O([("code", "type"), ("states", GADGETS)])]),
        ("maxstacksize", 1),
        ("creativeinventory", CREATIVE),
        ("texture", O([("base", "item/gadget/{type}")])),
        ("materialDensity", 5000),
    ]))
    # Alchemical bags use the game's sack model and bag behaviours (worn in a backpack slot), with 32 slots.
    write("itemtypes/alchemicalbag.json", O([
        ("code", "alchemicalbag"),
        ("variantgroups", [O([("code", "color"), ("states", BAGS)])]),
        ("maxstacksize", 1),
        ("shape", O([("base", "game:item/bag/linensack")])),
        ("textures", O([("linen", O([("base", "item/bag/alchemicalbag-{color}")])), ("top", O([("base", "item/bag/alchemicalbag-top")]))])),
        ("behaviors", [
            O([("name", "GroundStorable"), ("properties", O([("layout", "SingleCenter"), ("collisionBox", O([("x1", 0.25), ("y1", 0), ("z1", 0.25), ("x2", 0.75), ("y2", 0.375), ("z2", 0.75)]))]))]),
            O([("name", "HeldBag")]),
            O([("name", "GroundStoredHeldBag"), ("properties", O([("dialogTitleLangCode", "vintagee:alchemicalbagcontents")]))]),
        ]),
        ("attributes", O([("backpack", O([("quantitySlots", 32)]))])),
        ("storageFlags", 2),
        ("creativeinventory", CREATIVE),
        ("guiTransform", xf(rot=(-25, 23, 180), origin=(0.5299, 0.23, 0.5), scale=2.7, rotate=True)),
        ("tpHandTransform", xf(trans=(-0.7, -0.5, -0.6), rot=(-4, 47, -17), scale=0.81)),
        ("groundTransform", xf(rot=(180, 33, -180), origin=(0.5, 0, 0.5), scale=2.5)),
    ]))
    armor()


def armor():
    material_tex = {"dark": "vintagee:item/tool/material/darkmatter", "red": "vintagee:item/tool/material/redmatter", "gem": "vintagee:item/tool/material/gem"}
    shape = {"head": "game:entity/humanoid/seraph/armor/plate/head-steel", "body": "game:entity/humanoid/seraph/armor/plate/body", "legs": "game:entity/humanoid/seraph/armor/plate/legs"}
    prot = {
        "dark": O([("relativeProtection", 0.98), ("flatDamageReduction", 2.4), ("highDamageTierResistant", True), ("protectionTier", 5)]),
        "red": O([("relativeProtection", 0.985), ("flatDamageReduction", 3.0), ("highDamageTierResistant", True), ("protectionTier", 6)]),
        "gem": O([("relativeProtection", 0.99), ("flatDamageReduction", 3.6), ("highDamageTierResistant", True), ("protectionTier", 7)]),
    }
    stats = {"dark": O(), "red": O([("walkSpeed", 0.03)]), "gem": O([("walkSpeed", 0.06), ("healingeffectivness", 0.15)])}
    hide_head = ["hidehat", "hoodshoulder", "hoodupperover", "Hair tile upper part", "ponytailhigh", "ponytaillow", "Hair",
                 "hair-uncovered", "hidefacialhair", "sidehigh", "sidelow", "bangs"]
    attrs = O()
    for m in ARMOR_MATS:
        for p in PARTS:
            ate = O([("texturePrefixCode", f"matterarmor-{p}-{m}"), ("categoryCode", "armor" + p)])
            if p == "head":
                ate["disableElements"] = hide_head
            elif p == "legs":
                ate["disableElements"] = ["shoeL", "shoeR"]
            attrs[f"*-{m}-{p}"] = O([
                ("clothescategory", "armor" + p),
                ("attachableToEntity", ate),
                ("protectionModifiers", prot[m]),
                ("statModifiers", stats[m]),
                ("defaultProtLoss", O([("perTierRelativeProtectionLoss", [0.03, 0.15])])),
                ("footStepSound", "game:wearable/plate*"),
            ])
    write("itemtypes/matterarmor.json", O([
        ("code", "matterarmor"),
        ("variantgroups", [O([("code", "material"), ("states", ARMOR_MATS)]), O([("code", "bodypart"), ("states", PARTS)])]),
        ("maxstacksize", 1),
        ("storageFlags", 128),
        ("heldTpIdleAnimation", "holdunderarm"),
        ("behaviors", [O([("name", "Wearable")]), O([("name", "VintageEMatterTool"), ("properties", O([("maxAreaRadius", 0)]))])]),
        ("durability", 10000),
        ("shapeByType", O((f"*-{p}", O([("base", shape[p])])) for p in PARTS)),
        ("texturesByType", O((f"*-{m}-*", O([("seraph", O([("base", "game:block/transparent")])), ("material", O([("base", material_tex[m])]))])) for m in ARMOR_MATS)),
        ("creativeinventory", O([("general", ["*"]), ("clothing", ["*"]), ("vintagee", ["*"])])),
        ("attributesByType", attrs),
        ("guiTransformByType", O([
            ("*-head", xf(rot=(-26, -46, -180), origin=(0.6, 1.72, 0.5), scale=2)),
            ("*-body", xf(trans=(3, 0, 0), rot=(-26, -46, -180), origin=(0.5, 1.11, 0.5), scale=1.37)),
            ("*-legs", xf(trans=(1, 0, 0), rot=(-26, -46, -180), origin=(0.5, 0.6, 0.5), scale=1.25)),
        ])),
        ("tpHandTransformByType", O([
            ("*-head", xf(trans=(-0.82, -2.2, -0.5), rot=(18, 169, 30), origin=(0.5, 1.7, 0.5), scale=0.64)),
            ("*-body", xf(trans=(-1, -1.9, -0.5), rot=(97, -168, 94), origin=(0.5, 1.5, 0.5), scale=0.67)),
            ("*-legs", xf(trans=(-0.7, -0.6, -0.4), rot=(95, -20, -91), origin=(0.5, 0.7, 0.5), scale=0.74)),
        ])),
        ("groundTransformByType", O([
            ("*-head", xf(origin=(0.34, 1.71, 0.5), scale=3.1)),
            ("*-body", xf(rot=(0, 0, 90), origin=(0.34, 1.12, 0.5), scale=3.1)),
            ("*-legs", xf(rot=(0, 0, 90), origin=(0.34, 1.12, 0.5), scale=3.1)),
        ])),
        ("materialDensity", 9000),
    ]))


def cube(top, side, bottom=None):
    bottom = bottom or top
    return O([("up", O([("base", top)])), ("down", O([("base", bottom)])),
              ("north", O([("base", side)])), ("east", O([("base", side)])), ("south", O([("base", side)])), ("west", O([("base", side)]))])


SOUNDS_METAL = O([("place", "game:block/ingot"), ("break", "game:block/ingot"), ("hit", "game:block/ingot")])


def blocks():
    write("blocktypes/alchemicalfuelblock.json", O([
        ("code", "alchemicalfuelblock"),
        ("variantgroups", [O([("code", "type"), ("states", FUELS)])]),
        ("creativeinventory", CREATIVE),
        ("blockmaterial", "Stone"),
        ("drawtype", "cube"),
        ("textures", cube("block/alchemicalfuelblock-{type}", "block/alchemicalfuelblock-{type}")),
        ("combustiblePropsByType", O([
            ("*-alchemicalcoal", O([("burnTemperature", 1400), ("burnDuration", 1440)])),
            ("*-mobiusfuel", O([("burnTemperature", 1500), ("burnDuration", 5760)])),
            ("*-aeternalisfuel", O([("burnTemperature", 1600), ("burnDuration", 23040)])),
        ])),
        ("lightHsvByType", O([("*-alchemicalcoal", [4, 5, 3]), ("*-mobiusfuel", [8, 6, 5]), ("*-aeternalisfuel", [10, 3, 8])])),
        ("resistance", 3),
        ("requiredMiningTier", 1),
        ("sounds", O([("place", "game:block/rock-hit-pickaxe"), ("break", "game:block/rock-break-pickaxe"), ("hit", "game:block/rock-hit-pickaxe")])),
    ]))
    write("blocktypes/alchemicalchest.json", O([
        ("code", "alchemicalchest"),
        ("class", "BlockEmcMachine"),
        ("entityClass", "VintageEAlchemicalChest"),
        ("creativeinventory", CREATIVE),
        ("blockmaterial", "Wood"),
        ("drawtype", "cube"),
        ("textures", cube("block/alchemicalchest-top", "block/alchemicalchest-side", "block/alchemicalchest-bottom")),
        ("attributes", O([("quantitySlots", 104), ("interactionHelp", "vintagee:blockhelp-openchest")])),
        ("resistance", 3),
        ("sounds", O([("place", "game:block/planks"), ("break", "game:block/planks"), ("hit", "game:block/planks")])),
    ]))
    write("blocktypes/matterfurnace.json", O([
        ("code", "matterfurnace"),
        ("class", "BlockEmcMachine"),
        ("entityClass", "VintageEMatterFurnace"),
        ("variantgroups", [O([("code", "type"), ("states", ["dark", "red"])])]),
        ("creativeinventory", O([("general", ["*"]), ("mechanics", ["*"]), ("vintagee", ["*"])])),
        ("blockmaterial", "Metal"),
        ("drawtype", "cube"),
        ("textures", cube("block/matterfurnace-{type}-top", "block/matterfurnace-{type}-side")),
        ("attributesByType", O([
            ("*-dark", O([("secondsPerItem", 1.0), ("maxTemperature", 1600), ("interactionHelp", "vintagee:blockhelp-furnace")])),
            ("*-red", O([("secondsPerItem", 0.25), ("maxTemperature", 3000), ("interactionHelp", "vintagee:blockhelp-furnace")])),
        ])),
        ("lightHsvByType", O([("*-dark", [40, 4, 6]), ("*-red", [0, 5, 8])])),
        ("resistance", 10),
        ("requiredMiningTier", 4),
        ("sounds", SOUNDS_METAL),
    ]))
    write("blocktypes/antimatterrelay.json", O([
        ("code", "antimatterrelay"),
        ("class", "BlockEmcMachine"),
        ("entityClass", "VintageEAntimatterRelay"),
        ("variantgroups", [O([("code", "tier"), ("states", ["mk1", "mk2", "mk3"])])]),
        ("creativeinventory", O([("general", ["*"]), ("mechanics", ["*"]), ("vintagee", ["*"])])),
        ("blockmaterial", "Metal"),
        ("drawtype", "cube"),
        ("textures", cube("block/antimatterrelay-{tier}-top", "block/antimatterrelay-{tier}-side", "block/transmutationtable-bottom")),
        ("attributesByType", O([
            ("*-mk1", O([("capacity", 100000), ("bonusPerSecond", 1), ("transferPerSecond", 64), ("sneakInteractionHelp", "vintagee:blockhelp-relay-withdraw")])),
            ("*-mk2", O([("capacity", 1000000), ("bonusPerSecond", 3), ("transferPerSecond", 192), ("sneakInteractionHelp", "vintagee:blockhelp-relay-withdraw")])),
            ("*-mk3", O([("capacity", 10000000), ("bonusPerSecond", 10), ("transferPerSecond", 640), ("sneakInteractionHelp", "vintagee:blockhelp-relay-withdraw")])),
        ])),
        ("lightHsvByType", O([("*-mk1", [46, 3, 4]), ("*-mk2", [46, 4, 6]), ("*-mk3", [0, 5, 8])])),
        ("resistance", 6),
        ("requiredMiningTier", 3),
        ("sounds", SOUNDS_METAL),
    ]))
    write("blocktypes/novaexplosive.json", O([
        ("code", "novaexplosive"),
        ("class", "BlockEmcMachine"),
        ("entityClass", "VintageENovaExplosive"),
        ("variantgroups", [O([("code", "type"), ("states", ["catalyst", "cataclysm"])])]),
        ("creativeinventory", CREATIVE),
        ("blockmaterial", "Stone"),
        ("drawtype", "cube"),
        ("textures", cube("block/novaexplosive-{type}-top", "block/novaexplosive-{type}-side")),
        ("attributesByType", O([
            ("*-catalyst", O([("radius", 6), ("fuseSeconds", 4), ("sneakInteractionHelp", "vintagee:blockhelp-nova")])),
            ("*-cataclysm", O([("radius", 11), ("fuseSeconds", 6), ("sneakInteractionHelp", "vintagee:blockhelp-nova")])),
        ])),
        ("resistance", 1),
        ("sounds", O([("place", "game:block/sand"), ("break", "game:block/sand"), ("hit", "game:block/sand")])),
    ]))
    write("blocktypes/interdictiontorch.json", O([
        ("code", "interdictiontorch"),
        ("entityClass", "VintageEInterdictionTorch"),
        ("creativeinventory", CREATIVE),
        ("blockmaterial", "Metal"),
        ("drawtype", "json"),
        ("shape", O([("base", "block/interdictiontorch")])),
        ("textures", O([("rod", O([("base", "item/tool/material/darkmatter")])), ("glow", O([("base", "block/interdictiontorch-glow")]))])),
        ("attributes", O([("radius", 8)])),
        ("lightHsv", [36, 3, 14]),
        ("sidesolid", O([("all", False)])),
        ("sideopaque", O([("all", False)])),
        ("emitSideAo", O([("all", False)])),
        ("collisionbox", O([("x1", 0.375), ("y1", 0), ("z1", 0.375), ("x2", 0.625), ("y2", 0.75), ("z2", 0.625)])),
        ("selectionbox", O([("x1", 0.375), ("y1", 0), ("z1", 0.375), ("x2", 0.625), ("y2", 0.75), ("z2", 0.625)])),
        ("replaceable", 100),
        ("resistance", 2),
        ("sounds", SOUNDS_METAL),
    ]))
    write("blocktypes/pedestal.json", O([
        ("code", "pedestal"),
        ("class", "BlockEmcMachine"),
        ("entityClass", "VintageEPedestal"),
        ("variantgroups", [O([("code", "state"), ("states", ["empty", "active"])])]),
        ("creativeinventoryByType", O([("*-empty", CREATIVE)])),
        ("blockmaterial", "Metal"),
        ("drawtype", "json"),
        ("shape", O([("base", "block/pedestal")])),
        ("textures", O([("body", O([("base", "item/tool/material/darkmatter")])), ("top", O([("base", "block/matterblock-red")]))])),
        ("drops", [O([("type", "block"), ("code", "pedestal-empty")])]),
        ("attributes", O([("interactionHelp", "vintagee:blockhelp-pedestal")])),
        ("lightHsvByType", O([("*-active", [48, 4, 10])])),
        ("sidesolid", O([("all", False), ("down", True)])),
        ("sideopaque", O([("all", False)])),
        ("collisionbox", O([("x1", 0.125), ("y1", 0), ("z1", 0.125), ("x2", 0.875), ("y2", 0.75), ("z2", 0.875)])),
        ("selectionbox", O([("x1", 0.125), ("y1", 0), ("z1", 0.125), ("x2", 0.875), ("y2", 0.75), ("z2", 0.875)])),
        ("resistance", 12),
        ("requiredMiningTier", 4),
        ("sounds", SOUNDS_METAL),
    ]))
    shapes()


def box(name, frm, to, tex):
    faces = O()
    sizes = {"north": (to[0] - frm[0], to[1] - frm[1]), "south": (to[0] - frm[0], to[1] - frm[1]),
             "east": (to[2] - frm[2], to[1] - frm[1]), "west": (to[2] - frm[2], to[1] - frm[1]),
             "up": (to[0] - frm[0], to[2] - frm[2]), "down": (to[0] - frm[0], to[2] - frm[2])}
    for face, (w, h) in sizes.items():
        faces[face] = O([("texture", tex), ("uv", [0, 0, round(min(16, w), 3), round(min(16, h), 3)])])
    return O([("name", name), ("from", frm), ("to", to), ("faces", faces)])


def shapes():
    write("shapes/block/interdictiontorch.json", O([
        ("textureWidth", 16), ("textureHeight", 16),
        ("textures", O([("rod", "vintagee:item/tool/material/darkmatter"), ("glow", "vintagee:block/interdictiontorch-glow")])),
        ("elements", [
            box("base", [6, 0, 6], [10, 1, 10], "#rod"),
            box("rod", [7, 1, 7], [9, 9, 9], "#rod"),
            box("head", [6.5, 9, 6.5], [9.5, 12, 9.5], "#glow"),
        ]),
    ]))
    write("shapes/block/pedestal.json", O([
        ("textureWidth", 16), ("textureHeight", 16),
        ("textures", O([("body", "vintagee:item/tool/material/darkmatter"), ("top", "vintagee:block/matterblock-red")])),
        ("elements", [
            box("foot", [2, 0, 2], [14, 2, 14], "#body"),
            box("column", [5, 2, 5], [11, 10, 11], "#body"),
            box("plate", [3, 10, 3], [13, 12, 13], "#top"),
        ]),
    ]))


def I(code, typ="item", consume=True):
    d = O([("type", typ), ("code", code)])
    if not consume:
        d["consume"] = False
    return d


def R(name, pattern, ingredients, out, qty=1, out_type="item", shapeless=False):
    rows = pattern.split(",")
    output = O([("type", out_type), ("code", out)])
    if qty != 1:
        output["quantity"] = qty
    r = O([("ingredientPattern", pattern), ("ingredients", ingredients), ("width", max(len(x) for x in rows)), ("height", len(rows)),
           ("output", output), ("name", "vintagee:" + name)])
    if shapeless:
        r["shapeless"] = True
    return r


DM, RM = I("vintagee:matter-dark"), I("vintagee:matter-red")
PS = I("vintagee:philosophersstone", consume=False)
DIAMOND = I("game:gem-diamond-rough")
OBSIDIAN = I("game:rock-obsidian", "block")
BAND = I("vintagee:ironband")


def dust(t):
    return I(f"vintagee:covalencedust-{t}")


def recipes():
    rs = []
    rs.append(R("covalencedust-low", "SSS,SCS,SSS", O([("S", I("game:stone-*")), ("C", I("game:charcoal"))]), "vintagee:covalencedust-low", 40, shapeless=True))
    rs.append(R("covalencedust-medium", "IS", O([("I", I("game:ingot-iron")), ("S", I("game:saltpeter"))]), "vintagee:covalencedust-medium", 40, shapeless=True))
    rs.append(R("covalencedust-high", "DC", O([("D", DIAMOND), ("C", I("game:coke"))]), "vintagee:covalencedust-high", 40, shapeless=True))
    rs.append(R("ironband", "III,IAI,III", O([("I", I("game:ingot-iron")), ("A", I("vintagee:alchemicalfuel-alchemicalcoal"))]), "vintagee:ironband"))

    rs.append(R("kleinstar-ein", "_M_,MDM,_M_", O([("M", I("vintagee:alchemicalfuel-mobiusfuel")), ("D", DIAMOND)]), "vintagee:kleinstar-ein"))
    for prev, nxt in zip(STARS, STARS[1:]):
        rs.append(R(f"kleinstar-{nxt}", "SS,SS", O([("S", I(f"vintagee:kleinstar-{prev}"))]), f"vintagee:kleinstar-{nxt}", shapeless=True))

    for f in FUELS:
        rs.append(R(f"alchemicalfuelblock-{f}", "FFF,FFF,FFF", O([("F", I(f"vintagee:alchemicalfuel-{f}"))]), f"vintagee:alchemicalfuelblock-{f}", out_type="block"))
        rs.append(R(f"alchemicalfuel-{f}-from-block", "B", O([("B", I(f"vintagee:alchemicalfuelblock-{f}", "block"))]), f"vintagee:alchemicalfuel-{f}", 9))

    t = lambda k: f"vintagee:trinket-{k}"
    rs.append(R("blackholeband", "RRR,DID,RRR", O([("R", I("game:rope")), ("D", DM), ("I", BAND)]), t("blackholeband")))
    rs.append(R("swiftwolfsrendinggale", "DFD,FIF,DFD", O([("D", DM), ("F", I("game:feather")), ("I", BAND)]), t("swiftwolfsrendinggale")))
    rs.append(R("harvestgoddessband", "SFS,DID,SFS", O([("S", I("game:sapling-*", "block")), ("F", I("game:flower-*", "block")), ("D", DM), ("I", BAND)]), t("harvestgoddessband")))
    rs.append(R("ignitionring", "AAA,AIA,ADA", O([("A", I("vintagee:alchemicalfuel-alchemicalcoal")), ("I", BAND), ("D", DM)]), t("ignitionring")))
    rs.append(R("zeroring", "SSS,SIS,SDS", O([("S", I("game:snowball-snow")), ("I", BAND), ("D", DM)]), t("zeroring")))
    rs.append(R("ringofarcana", "ZRI,R_R,HRS", O([("Z", I(t("zeroring"))), ("I", I(t("ignitionring"))), ("H", I(t("harvestgoddessband"))),
                                                  ("S", I(t("swiftwolfsrendinggale"))), ("R", RM)]), t("ringofarcana")))
    rs.append(R("bodystone", "HHH,RPR,HHH", O([("H", I("game:honeycomb")), ("R", RM), ("P", PS)]), t("bodystone")))
    rs.append(R("soulstone", "QQQ,RPR,QQQ", O([("Q", I("game:clearquartz")), ("R", RM), ("P", PS)]), t("soulstone")))
    rs.append(R("lifestone", "BS", O([("B", I(t("bodystone"))), ("S", I(t("soulstone")))]), t("lifestone"), shapeless=True))
    rs.append(R("repairtalisman", "LMH,SPS,HML", O([("L", dust("low")), ("M", dust("medium")), ("H", dust("high")), ("S", I("game:flaxtwine")), ("P", PS)]), t("repairtalisman")))
    rs.append(R("gemofeternaldensity", "DOD,OMO,DOD", O([("D", DIAMOND), ("O", OBSIDIAN), ("M", DM)]), t("gemofeternaldensity")))
    rs.append(R("evertideamulet", "SSS,DDD,SSS", O([("S", I("game:seashell-*", "block")), ("D", DM)]), t("evertideamulet")))
    rs.append(R("volcaniteamulet", "OOO,DDD,OOO", O([("O", OBSIDIAN), ("D", DM)]), t("volcaniteamulet")))
    rs.append(R("watchofflowingtime", "GGG,DTD,GGG", O([("G", I("game:glass-*", "block")), ("D", DM), ("T", I("game:gear-temporal"))]), t("watchofflowingtime")))

    g = lambda k: f"vintagee:gadget-{k}"
    NOVA = I("vintagee:novaexplosive-catalyst", "block")
    rs.append(R("destructioncatalyst", "NMN,MDM,NMN", O([("N", NOVA), ("M", I("vintagee:alchemicalfuel-mobiusfuel")), ("D", DM)]), g("destructioncatalyst")))
    rs.append(R("hyperkineticlens", "DDD,GNG,DDD", O([("D", DM), ("G", DIAMOND), ("N", NOVA)]), g("hyperkineticlens")))
    rs.append(R("catalyticlens", "DDD,LHC,DDD", O([("D", DM), ("L", I(g("hyperkineticlens"))), ("H", dust("high")), ("C", I(g("destructioncatalyst")))]), g("catalyticlens")))
    rs.append(R("mercurialeye", "_R_,DKD,_R_", O([("R", RM), ("D", DM), ("K", I("vintagee:kleinstar-ein"))]), g("mercurialeye")))
    rs.append(R("archangelssmite", "FFF,DBD,FFF", O([("F", I("game:feather")), ("D", DM), ("B", I("game:bow-*"))]), g("archangelssmite")))
    rs.append(R("tomeofknowledge", "PKP,RBR,PKP", O([("P", I("game:paper-parchment")), ("K", I("vintagee:kleinstar-vier")), ("R", RM), ("B", I("game:book-normal-*"))]), g("tomeofknowledge")))
    rs.append(R("diviningrodlow", "__L,_S_,S__", O([("L", dust("low")), ("S", I("game:stick"))]), g("diviningrodlow")))
    rs.append(R("diviningrodmedium", "__M,_R_,M__", O([("M", dust("medium")), ("R", I(g("diviningrodlow")))]), g("diviningrodmedium")))
    rs.append(R("diviningrodhigh", "__H,_R_,H__", O([("H", dust("high")), ("R", I(g("diviningrodmedium")))]), g("diviningrodhigh")))

    for c in BAGS:
        cloth = "white" if c == "white" else c
        rs.append(R(f"alchemicalbag-{c}", "HHH,CXC,CCC", O([("H", dust("high")), ("X", I("vintagee:alchemicalchest", "block")), ("C", I(f"game:cloth-{cloth}"))]), f"vintagee:alchemicalbag-{c}"))

    a = lambda m, p: f"vintagee:matterarmor-{m}-{p}"
    rs.append(R("matterarmor-dark-head", "DDD,D_D", O([("D", DM)]), a("dark", "head")))
    rs.append(R("matterarmor-dark-body", "D_D,DDD,DDD", O([("D", DM)]), a("dark", "body")))
    rs.append(R("matterarmor-dark-legs", "DDD,D_D,D_D", O([("D", DM)]), a("dark", "legs")))
    rs.append(R("matterarmor-red-head", "RRR,RAR", O([("R", RM), ("A", I(a("dark", "head")))]), a("red", "head")))
    rs.append(R("matterarmor-red-body", "RAR,RRR,RRR", O([("R", RM), ("A", I(a("dark", "body")))]), a("red", "body")))
    rs.append(R("matterarmor-red-legs", "RRR,RAR,R_R", O([("R", RM), ("A", I(a("dark", "legs")))]), a("red", "legs")))
    K = I("vintagee:kleinstar-vier")
    rs.append(R("matterarmor-gem-head", "GKG,GAG", O([("G", DIAMOND), ("K", K), ("A", I(a("red", "head")))]), a("gem", "head")))
    rs.append(R("matterarmor-gem-body", "GAG,GKG,GGG", O([("G", DIAMOND), ("K", K), ("A", I(a("red", "body")))]), a("gem", "body")))
    rs.append(R("matterarmor-gem-legs", "GGG,GAG,GKG", O([("G", DIAMOND), ("K", K), ("A", I(a("red", "legs")))]), a("gem", "legs")))

    mt = lambda m, k: f"vintagee:mattertool-{m}-{k}"
    rs.append(R("darkmatter-shears", "_D,D_", O([("D", DM)]), mt("dark", "shears")))
    rs.append(R("redmatter-shears", "_R,RT", O([("R", RM), ("T", I(mt("dark", "shears")))]), mt("red", "shears")))
    rs.append(R("redmatter-katar", "WAS,HRR", O([("W", I(mt("red", "sword"))), ("A", I(mt("red", "axe"))), ("S", I(mt("red", "shears"))),
                                               ("H", I(mt("red", "hoe"))), ("R", RM)]), "vintagee:redmattertool-katar", shapeless=True))
    rs.append(R("redmatter-morningstar", "PSH,RR_", O([("P", I(mt("red", "pickaxe"))), ("S", I(mt("red", "shovel"))), ("H", I(mt("red", "hammer"))),
                                                     ("R", RM)]), "vintagee:redmattertool-morningstar", shapeless=True))

    rs.append(R("alchemicalchest", "LMH,SDS,III", O([("L", dust("low")), ("M", dust("medium")), ("H", dust("high")), ("S", I("game:rock-*", "block")),
                                                     ("D", DIAMOND), ("I", I("game:ingot-iron"))]), "vintagee:alchemicalchest", out_type="block"))
    rs.append(R("interdictiontorch", "_D_,QTQ,_A_", O([("D", DIAMOND), ("Q", I("game:clearquartz")), ("T", I("game:torch-basic-lit-up", "block")),
                                                       ("A", I("vintagee:alchemicalfuel-alchemicalcoal"))]), "vintagee:interdictiontorch", out_type="block"))
    rs.append(R("novacatalyst", "BM", O([("B", I("game:bomb-ore", "block")), ("M", I("vintagee:alchemicalfuel-mobiusfuel"))]), "vintagee:novaexplosive-catalyst", out_type="block", shapeless=True))
    rs.append(R("novacataclysm", "NA", O([("N", NOVA), ("A", I("vintagee:alchemicalfuel-aeternalisfuel"))]), "vintagee:novaexplosive-cataclysm", out_type="block", shapeless=True))
    rs.append(R("antimatterrelay-mk1", "OOO,ODO,OGO", O([("O", OBSIDIAN), ("D", DM), ("G", I("game:glass-*", "block"))]), "vintagee:antimatterrelay-mk1", out_type="block"))
    rs.append(R("antimatterrelay-mk2", "ODO,DRD,ODO", O([("O", OBSIDIAN), ("D", DM), ("R", I("vintagee:antimatterrelay-mk1", "block"))]), "vintagee:antimatterrelay-mk2", out_type="block"))
    rs.append(R("antimatterrelay-mk3", "ORO,RXR,ORO", O([("O", OBSIDIAN), ("R", RM), ("X", I("vintagee:antimatterrelay-mk2", "block"))]), "vintagee:antimatterrelay-mk3", out_type="block"))
    rs.append(R("matterfurnace-dark", "DDD,DFD,DDD", O([("D", DM), ("F", I("game:forge", "block"))]), "vintagee:matterfurnace-dark", out_type="block"))
    rs.append(R("matterfurnace-red", "_R_,RFR,_R_", O([("R", RM), ("F", I("vintagee:matterfurnace-dark", "block"))]), "vintagee:matterfurnace-red", out_type="block"))
    rs.append(R("pedestal", "R_R,RDR,DDD", O([("R", RM), ("D", I("vintagee:matterblock-dark", "block"))]), "vintagee:pedestal-empty", out_type="block"))
    write("recipes/grid/projecte.json", rs)


NAMES = O([
    ("covalencedust-low", "Low Covalence Dust"), ("covalencedust-medium", "Medium Covalence Dust"), ("covalencedust-high", "High Covalence Dust"),
    ("kleinstar-ein", "Klein Star Ein"), ("kleinstar-zwei", "Klein Star Zwei"), ("kleinstar-drei", "Klein Star Drei"),
    ("kleinstar-vier", "Klein Star Vier"), ("kleinstar-sphere", "Klein Star Sphere"), ("kleinstar-omega", "Klein Star Omega"),
    ("ironband", "Iron Band"),
    ("trinket-blackholeband", "Black Hole Band"), ("trinket-swiftwolfsrendinggale", "Swiftwolf's Rending Gale"),
    ("trinket-harvestgoddessband", "Harvest Goddess Band"), ("trinket-ignitionring", "Ignition Ring"), ("trinket-zeroring", "Zero Ring"),
    ("trinket-ringofarcana", "Ring of Arcana"), ("trinket-bodystone", "Body Stone"), ("trinket-soulstone", "Soul Stone"),
    ("trinket-lifestone", "Life Stone"), ("trinket-repairtalisman", "Repair Talisman"), ("trinket-gemofeternaldensity", "Gem of Eternal Density"),
    ("trinket-evertideamulet", "Evertide Amulet"), ("trinket-volcaniteamulet", "Volcanite Amulet"), ("trinket-watchofflowingtime", "Watch of Flowing Time"),
    ("gadget-destructioncatalyst", "Destruction Catalyst"), ("gadget-hyperkineticlens", "Hyperkinetic Lens"), ("gadget-catalyticlens", "Catalytic Lens"),
    ("gadget-mercurialeye", "Mercurial Eye"), ("gadget-archangelssmite", "Archangel's Smite"), ("gadget-tomeofknowledge", "Tome of Knowledge"),
    ("gadget-diviningrodlow", "Divining Rod (low)"), ("gadget-diviningrodmedium", "Divining Rod (medium)"), ("gadget-diviningrodhigh", "Divining Rod (high)"),
    ("redmattertool-katar", "Red Matter Katar"), ("redmattertool-morningstar", "Red Matter Morning Star"),
    ("mattertool-dark-shears", "Dark Matter Shears"), ("mattertool-red-shears", "Red Matter Shears"),
])
DESCS = O([
    ("covalencedust-low", "Crafting dust for alchemical gear. Made from stone and charcoal."),
    ("covalencedust-medium", "Crafting dust for alchemical gear. Made from iron and saltpeter."),
    ("covalencedust-high", "Crafting dust for alchemical gear. Made from diamond and coke."),
    ("kleinstar-ein", "Stores EMC. Right-click to empty it into your EMC, sneak + right-click to fill it. Rings and gadgets draw from stars you carry. Combine four into the next size (their EMC is kept)."),
    ("ironband", "The base of every ring."),
    ("trinket-blackholeband", "While on: pulls loose items around you to you. Works anywhere in your hotbar or backpack; right-click to switch on/off."),
    ("trinket-swiftwolfsrendinggale", "While on: you run 40% faster and take no fall damage. Costs a little EMC while on."),
    ("trinket-harvestgoddessband", "While on: crops around you grow faster (costs EMC per growth stage)."),
    ("trinket-ignitionring", "While on: hostile creatures near you catch fire, and fire can't hurt you. Costs EMC per burn."),
    ("trinket-zeroring", "While on: still water around and below you freezes into ice."),
    ("trinket-ringofarcana", "Zero Ring, Ignition Ring, Harvest Goddess Band and Swiftwolf's Rending Gale in one. Pick the mode with the tool-mode key."),
    ("trinket-bodystone", "While on: keeps you fed using EMC."),
    ("trinket-soulstone", "While on: heals you using EMC."),
    ("trinket-lifestone", "While on: keeps you fed and heals you using EMC."),
    ("trinket-repairtalisman", "While on: slowly repairs every damaged item and piece of armour you carry. Free."),
    ("trinket-gemofeternaldensity", "While on: turns cheap junk you carry (stone, soil, sand, gravel, sticks... anything worth 8 EMC or less) into the target material. Pick the target with the tool-mode key."),
    ("trinket-evertideamulet", "Right-click a block to place a water source next to it. Free."),
    ("trinket-volcaniteamulet", "Right-click a block to place a lava source next to it. Costs EMC."),
    ("trinket-watchofflowingtime", "While on: time runs 10x faster for the whole world - crops, cooking, curing, drying and day and night (or 3x slower, which also slows hostile creatures near you; pick with the tool-mode key). Costs EMC every second it is on."),
    ("gadget-destructioncatalyst", "Right-click a block to break a 3x3 tunnel into it (3, 6 or 9 deep with the tool-mode key). Costs EMC per block."),
    ("gadget-hyperkineticlens", "Right-click to blow up the spot you're looking at, up to 64 blocks away. Costs EMC."),
    ("gadget-catalyticlens", "A bigger explosion, or the destruction catalyst's tunnel (tool-mode key). Costs EMC."),
    ("gadget-mercurialeye", "Sneak + right-click a block to remember it, then right-click blocks to turn them into it (1x1 or 3x3). You pay the difference in EMC."),
    ("gadget-archangelssmite", "Right-click to strike the creature you're looking at, up to 48 blocks away. Costs EMC."),
    ("gadget-tomeofknowledge", "Right-click to learn every item that has an EMC value."),
    ("gadget-diviningrodlow", "Right-click a block to see how much EMC lies in the ground below it (3 blocks deep)."),
    ("gadget-diviningrodmedium", "Right-click a block to see how much EMC lies in the ground below it (16 blocks deep)."),
    ("gadget-diviningrodhigh", "Right-click a block to see how much EMC lies in the ground below it (64 blocks deep)."),
    ("redmattertool-katar", "Sword, axe and shears in one: fells trees, cuts plants 3x3 or 5x5 and hits very hard. Never wears out."),
    ("redmattertool-morningstar", "Pickaxe, shovel and sledgehammer in one: digs stone, ore and soil 3x3 or 5x5. Never wears out."),
    ("mattertool-dark-shears", "Never wear out."), ("mattertool-red-shears", "Never wear out."),
])
BLOCK_NAMES = O([
    ("alchemicalfuelblock-alchemicalcoal", "Alchemical Coal Block"), ("alchemicalfuelblock-mobiusfuel", "Mobius Fuel Block"),
    ("alchemicalfuelblock-aeternalisfuel", "Aeternalis Fuel Block"), ("alchemicalchest", "Alchemical Chest"),
    ("matterfurnace-dark", "Dark Matter Furnace"), ("matterfurnace-red", "Red Matter Furnace"),
    ("antimatterrelay-mk1", "Anti-Matter Relay MK1"), ("antimatterrelay-mk2", "Anti-Matter Relay MK2"), ("antimatterrelay-mk3", "Anti-Matter Relay MK3"),
    ("novaexplosive-catalyst", "Nova Catalyst"), ("novaexplosive-cataclysm", "Nova Cataclysm"), ("interdictiontorch", "Interdiction Torch"),
    ("pedestal-empty", "Dark Matter Pedestal"), ("pedestal-active", "Dark Matter Pedestal"),
])
BLOCK_DESCS = O([
    ("alchemicalfuelblock-alchemicalcoal", "Nine alchemical coal. Burns nine times as long."),
    ("alchemicalfuelblock-mobiusfuel", "Nine mobius fuel. Burns nine times as long."),
    ("alchemicalfuelblock-aeternalisfuel", "Nine aeternalis fuel. Burns nine times as long."),
    ("alchemicalchest", "104 slots of storage."),
    ("matterfurnace-dark", "Smelts, fires and cooks anything with smelting data - ore straight to ingots, no crucible needed. Runs on fuel or on EMC from adjacent collectors and relays."),
    ("matterfurnace-red", "Like the dark matter furnace, but four times as fast and hot enough for anything."),
    ("antimatterrelay-mk1", "Stores EMC from collectors, adds a bonus and passes it on to condensers, furnaces and other relays."),
    ("antimatterrelay-mk2", "Stores EMC from collectors, adds a bonus and passes it on to condensers, furnaces and other relays."),
    ("antimatterrelay-mk3", "Stores EMC from collectors, adds a bonus and passes it on to condensers, furnaces and other relays."),
    ("novaexplosive-catalyst", "Sneak + right-click with an empty hand to light it. Explodes after a few seconds, dropping what it breaks."),
    ("novaexplosive-cataclysm", "A much bigger nova catalyst. Sneak + right-click with an empty hand to light it."),
    ("interdictiontorch", "Keeps hostile creatures away from it."),
    ("pedestal-empty", "Right-click with a ring, stone, amulet or talisman to set it here; it then works on the area around the pedestal for free."),
    ("pedestal-active", "Right-click with an empty hand to take back what's on it."),
])
LANG = O([
    ("kleinstar-stored", "Stored EMC: {0} / {1}"),
    ("heldhelp-kleinstar-empty", "Empty into your EMC"),
    ("heldhelp-kleinstar-fill", "Fill from your EMC"),
    ("trinket-on", "On"), ("trinket-off", "Off (right-click to switch on)"), ("trinket-mode", "Mode: {0}"),
    ("watch-faster", "Speed up time"), ("watch-slower", "Slow down time"),
    ("arcana-zero", "Zero"), ("arcana-ignition", "Ignition"), ("arcana-harvest", "Harvest"), ("arcana-swiftwolf", "Swiftwolf"),
    ("density-target", "Makes: {0} ({1} EMC collected)"),
    ("heldhelp-trinket-toggle", "Switch on/off"), ("heldhelp-trinket-place", "Place liquid"), ("heldhelp-trinket-mode", "Change mode"),
    ("gadget-depth", "{0} deep"), ("gadget-explode", "Explode"), ("gadget-tunnel", "Tunnel"),
    ("tome-learned", "Learned {0} new items."),
    ("eye-target", "Mercurial Eye set to {0}."), ("eye-notarget", "No block chosen - sneak + right-click a block to choose one."), ("eye-current", "Turns blocks into: {0}"),
    ("divining-result", "{0} blocks deep: {1} EMC in total, {2} per block on average. Most valuable: {3} ({4} EMC)."),
    ("heldhelp-gadget-destructioncatalyst", "Break tunnel"), ("heldhelp-gadget-hyperkineticlens", "Explode"), ("heldhelp-gadget-catalyticlens", "Use"),
    ("heldhelp-gadget-mercurialeye", "Turn block into chosen block"), ("heldhelp-gadget-eyepick", "Choose block"),
    ("heldhelp-gadget-archangelssmite", "Smite"), ("heldhelp-gadget-tomeofknowledge", "Learn everything"),
    ("heldhelp-gadget-diviningrodlow", "Divine"), ("heldhelp-gadget-diviningrodmedium", "Divine"), ("heldhelp-gadget-diviningrodhigh", "Divine"),
    ("furnace-slots", "Slots: input, fuel, output"),
    ("furnace-burning", "Burning: {0}s of heat left, {1} EMC stored"), ("furnace-cold", "Cold - add fuel or feed it EMC ({1} EMC stored)"),
    ("furnace-progress", "Smelting: {0}%"),
    ("relay-bonus", "Bonus: {0} EMC/s, passes on up to {1} EMC/s"),
    ("nova-lit", "Exploding in {0}s!"),
    ("pedestal-empty", "Empty"), ("pedestal-holding", "Holding: {0}"),
    ("blockhelp-openchest", "Open"), ("blockhelp-furnace", "Open"), ("blockhelp-relay-withdraw", "Take stored EMC (empty hand or Klein star)"),
    ("blockhelp-nova", "Light the fuse (empty hand)"), ("blockhelp-pedestal", "Place or take a ring/stone/amulet"),
    ("alchemicalbagcontents", "Alchemical Bag"),
])


def lang():
    path = os.path.join(ROOT, "lang", "en.json")
    d = json.load(open(path, encoding="utf-8"), object_pairs_hook=O)
    for k, v in NAMES.items():
        d["item-" + k] = v
    for k, v in DESCS.items():
        d["itemdesc-" + k] = v
    for s in STARS[1:]:
        d["itemdesc-kleinstar-" + s] = DESCS["kleinstar-ein"]
    for c in BAGS:
        d[f"item-alchemicalbag-{c}"] = f"Alchemical Bag ({c})" if c not in ("white", "plain") else ("Alchemical Bag" if c == "white" else "Alchemical Bag (plain)")
        d[f"itemdesc-alchemicalbag-{c}"] = "A 32-slot bag. Wear it in a backpack slot."
    for m, mn in (("dark", "Dark Matter"), ("red", "Red Matter"), ("gem", "Gem")):
        for p, pn in (("head", "Helmet"), ("body", "Chestplate"), ("legs", "Leggings")):
            d[f"item-matterarmor-{m}-{p}"] = f"{mn} {pn}"
            d[f"itemdesc-matterarmor-{m}-{p}"] = {
                "dark": "Never wears out.",
                "red": "Never wears out. A little lighter on your feet.",
                "gem": "Never wears out. A full set makes you immune to fall and fire damage."}[m]
    for k, v in BLOCK_NAMES.items():
        d["block-" + k] = v
    for k, v in BLOCK_DESCS.items():
        d["blockdesc-" + k] = v
    for k, v in LANG.items():
        d[k] = v
    with open(path, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2, ensure_ascii=False)
        f.write("\n")


PROJECTILE_KINDS = ["lens", "water", "lava", "fire", "arrow"]


def entities():
    """ProjectE's projectiles (lens charges, amulet water/lava, ignition fire, smite arrows): see EntityVintageEProjectile."""
    write("entities/projectile.json", O([
        ("code", "projectile"),
        ("class", "VintageEProjectile"),
        ("variantgroups", [O([("code", "kind"), ("states", PROJECTILE_KINDS)])]),
        ("hitboxSize", O([("x", 0.25), ("y", 0.25)])),
        ("client", O([
            ("renderer", "Shape"),
            ("shape", O([("base", "vintagee:entity/projectile")])),
            ("texturesByType", O([("*-" + k, O([("orb", O([("base", "vintagee:entity/projectile-" + k)]))])) for k in PROJECTILE_KINDS])),
            ("size", 1),
            ("behaviors", [O([("code", "interpolateposition")])]),
        ])),
        ("server", O([("behaviors", [])])),
        ("sounds", O()),
    ]))
    orb = box("orb", [6, 0, 6], [10, 4, 10], "#orb")
    orb["glow"] = 255
    write("shapes/entity/projectile.json", O([
        ("textureWidth", 16), ("textureHeight", 16),
        ("textures", O([("orb", "vintagee:entity/projectile-lens")])),
        ("elements", [orb]),
    ]))


if __name__ == "__main__":
    items()
    blocks()
    entities()
    recipes()
    lang()
    print("ProjectE assets written")
