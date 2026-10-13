#!/usr/bin/env python3
"""
Used by .github/workflows/analyze.yml. Pulls the handful of facts the mod's development needs out of a real
Vintage Story install (tool/armour item settings, the models and animations they use, board recipes) into
small JSON summaries, so nothing large is copied from the game.

usage: ci_extract.py <game assets dir> <output dir>
"""
import json
import os
import re
import sys

import json5

assets, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
errors = []


def load(path):
    try:
        with open(path, encoding="utf-8-sig") as f:
            return json5.load(f)
    except Exception as e:  # noqa: BLE001
        errors.append(f"{path}: {e}")
        return None


def walk(sub):
    root = os.path.join(assets, sub)
    for dirpath, _, files in os.walk(root):
        for name in sorted(files):
            if name.endswith(".json"):
                yield os.path.join(dirpath, name)


def rel(path):
    return os.path.relpath(path, assets)


KEEP = re.compile(r"(transform|animation|^shape|^texture|^code$|^class$|^tool$|variantgroups|^behaviors|^heldsound|"
                  r"^attackRange|^attackpower|^tooltier|^durability|^maxstacksize|^storageflags|^creativeinventory)", re.I)
ATTR_KEEP = re.compile(r"(transform|anim|clothescategory|protection|statmodifiers|wearable|footStep|attachableToEntity|"
                       r"handbook|groundStorage|rack|displaycase|shelvable|toolrack)", re.I)


def summarize_item(path, data):
    s = {"file": rel(path)}
    for k, v in data.items():
        if KEEP.search(k):
            s[k] = v
    attrs = data.get("attributes") or data.get("attributesByType")
    if isinstance(attrs, dict):
        kept = {}
        for k, v in attrs.items():
            if ATTR_KEEP.search(k):
                kept[k] = v
            elif isinstance(v, dict):  # attributesByType: one level down
                inner = {ik: iv for ik, iv in v.items() if ATTR_KEEP.search(ik)}
                if inner:
                    kept[k] = inner
        s["attributes(selected)"] = kept
    return s


# 1. Tools, weapons and armour: everything about how they're shown and held.
items = []
for path in walk("survival/itemtypes"):
    p = rel(path).replace("\\", "/")
    if not re.search(r"/(tool|weapon|wearable)/|armor|pickaxe|shovel|axe|hoe|hammer|blade|sword|knife|scythe|shears|saw|chisel|prospecting|spear|falx|mace|club", p):
        continue
    data = load(path)
    if isinstance(data, dict):
        items.append(summarize_item(path, data))
    elif isinstance(data, list):
        for d in data:
            if isinstance(d, dict):
                items.append(summarize_item(path, d))
with open(os.path.join(out, "vanilla-tools.json"), "w") as f:
    json.dump(items, f, indent=1)

# 2. The models those tools use: size, texture slots and element names (not the geometry).
shape_refs = set()
for it in items:
    for key in ("shape", "shapeByType", "shapeInventory", "shapeInventoryByType"):
        v = it.get(key)
        if isinstance(v, dict):
            if "base" in v:
                shape_refs.add(v["base"])
            for sub in v.values():
                if isinstance(sub, dict) and "base" in sub:
                    shape_refs.add(sub["base"])
shapes = {}
for path in walk("survival/shapes/item"):
    p = rel(path).replace("\\", "/")
    if not re.search(r"/(tool|weapon|wearable|armor|clothes)/", p):
        continue
    data = load(path)
    if not isinstance(data, dict):
        continue
    names = []
    lo, hi = [1e9] * 3, [-1e9] * 3

    def collect(elements, offset, depth=0):
        for e in elements or []:
            fr, to = e.get("from") or [0, 0, 0], e.get("to") or [0, 0, 0]
            a = [offset[i] + fr[i] for i in range(3)]
            b = [offset[i] + to[i] for i in range(3)]
            for i in range(3):
                lo[i] = min(lo[i], a[i], b[i])
                hi[i] = max(hi[i], a[i], b[i])
            names.append({"name": e.get("name"), "depth": depth,
                          "textures": sorted({(fv or {}).get("texture") for fv in (e.get("faces") or {}).values() if isinstance(fv, dict)} - {None})})
            collect(e.get("children"), a, depth + 1)

    collect(data.get("elements"), [0, 0, 0])
    shapes[p] = {"textureWidth": data.get("textureWidth"), "textureHeight": data.get("textureHeight"),
                 "textures": data.get("textures"), "textureSizes": data.get("textureSizes"),
                 "bounds(approx, unrotated)": [lo, hi], "elements": names}
    if p.endswith(("tool/hammer.json", "tool/pickaxe.json")):
        # Which end the head is on, for orienting this mod's own models the same way (boxes only).
        shapes[p]["boxes"] = [{"name": e.get("name"), "from": e.get("from"), "to": e.get("to"),
                               "rot": [e.get("rotationX", 0), e.get("rotationY", 0), e.get("rotationZ", 0)],
                               "origin": e.get("rotationOrigin")} for e in data.get("elements") or []]
with open(os.path.join(out, "vanilla-tool-shapes.json"), "w") as f:
    json.dump({"referenced": sorted(shape_refs), "shapes": shapes}, f, indent=1)

# 3. Player animation codes available to held items.
anims = {}
for path in walk("game/shapes/entity/humanoid"):
    data = load(path)
    if isinstance(data, dict) and data.get("animations"):
        anims[rel(path)] = sorted(a.get("code") for a in data["animations"] if isinstance(a, dict) and a.get("code"))
with open(os.path.join(out, "player-animations.json"), "w") as f:
    json.dump(anims, f, indent=1)

# 4. Every recipe that makes or uses boards/planks, and the board item itself.
boards = {"recipes": [], "items": [], "blocks": []}
for path in walk("survival/recipes"):
    with open(path, encoding="utf-8-sig") as f:
        text = f.read()
    if "plank" not in text:
        continue
    data = load(path)
    for r in (data if isinstance(data, list) else [data]):
        if isinstance(r, dict) and "plank" in json.dumps(r):
            boards["recipes"].append({"file": rel(path), "recipe": r})
for path in walk("survival/itemtypes"):
    if "plank" in os.path.basename(path):
        boards["items"].append({"file": rel(path), "data": load(path)})
for path in walk("survival/blocktypes"):
    if "plank" in os.path.basename(path):
        d = load(path)
        if isinstance(d, dict):
            boards["blocks"].append({"file": rel(path), "code": d.get("code"), "class": d.get("class"),
                                     "variantgroups": d.get("variantgroups"), "drops": d.get("drops"), "dropsByType": d.get("dropsByType"),
                                     "behaviors": d.get("behaviors")})
with open(os.path.join(out, "boards.json"), "w") as f:
    json.dump(boards, f, indent=1)

with open(os.path.join(out, "extract-errors.txt"), "w") as f:
    f.write("\n".join(errors))
print(f"tools: {len(items)}, shapes: {len(shapes)}, animation files: {len(anims)}, board recipes: {len(boards['recipes'])}, parse errors: {len(errors)}")
