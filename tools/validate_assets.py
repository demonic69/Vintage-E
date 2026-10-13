#!/usr/bin/env python3
"""Sanity checks for Vintage-E assets: JSON parses, variant codes expand, every texture/shape exists,
every vintagee: code used in recipes exists, every lang key the code uses is translated, every
class/behaviour/entity named in JSON is registered in C#."""
import itertools, json, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
A = os.path.join(ROOT, "assets", "vintagee")
SRC = os.path.join(ROOT, "src")
errors = []

def load(path):
    try:
        return json.load(open(path, encoding="utf-8"))
    except Exception as e:
        errors.append(f"{path}: {e}"); return None

def expand(obj):
    groups = obj.get("variantgroups") or []
    states = [[(g["code"], s) for s in g["states"]] for g in groups]
    for combo in itertools.product(*states) if states else [()]:
        code = "-".join([obj["code"]] + [s for _, s in combo])
        yield code, dict(combo)

def wild(pattern, code):
    return re.fullmatch(re.escape(pattern).replace(r"\*", ".*"), code) is not None

def by_type(obj, key, code):
    if key in obj: return obj[key]
    bt = obj.get(key + "ByType") or {}
    for pat, val in bt.items():
        if wild(pat, code): return val
    return None

def subst(s, variant):
    return re.sub(r"\{(\w+)\}", lambda m: variant.get(m.group(1), m.group(0)), s)

codes = {}
for kind in ("itemtypes", "blocktypes"):
    for f in sorted(os.listdir(os.path.join(A, kind))):
        obj = load(os.path.join(A, kind, f))
        if not obj: continue
        for code, variant in expand(obj):
            codes[code] = kind
            texs = []
            t = by_type(obj, "texture", code)
            if t: texs.append(t["base"])
            ts = by_type(obj, "textures", code) or {}
            texs += [v["base"] for v in ts.values()]
            for tex in texs:
                p = os.path.join(A, "textures", subst(tex, variant) + ".png")
                if not os.path.exists(p): errors.append(f"{code}: missing texture {p}")
            sh = by_type(obj, "shape", code)
            if sh:
                p = os.path.join(A, "shapes", subst(sh["base"], variant) + ".json")
                if not os.path.exists(p): errors.append(f"{code}: missing shape {p}")
                else:
                    shape = load(p)
                    for k, v in (shape or {}).get("textures", {}).items():
                        dom, path = v.split(":", 1) if ":" in v else ("vintagee", v)
                        if dom == "vintagee" and not os.path.exists(os.path.join(A, "textures", path + ".png")):
                            errors.append(f"shape {p}: missing texture {v}")

cs = ""
for dp, _, fs in os.walk(SRC):
    for f in fs:
        if f.endswith(".cs"): cs += open(os.path.join(dp, f), encoding="utf-8").read()
registered = set(re.findall(r'Register(?:Item|Block|BlockEntity|CollectibleBehavior)Class\("(\w+)"', cs))
vanilla_classes = {"ItemAxe", "ItemHoe", "ItemHammer", "Item", "Block"}
for kind in ("itemtypes", "blocktypes"):
    for f in os.listdir(os.path.join(A, kind)):
        obj = load(os.path.join(A, kind, f)) or {}
        names = [obj.get("class"), obj.get("entityClass")] + list((obj.get("classByType") or {}).values())
        for bl in [obj.get("behaviors") or []] + list((obj.get("behaviorsByType") or {}).values()):
            names += [b["name"] for b in bl]
        for n in filter(None, names):
            if n not in registered and n not in vanilla_classes:
                errors.append(f"{f}: class/behaviour '{n}' is not registered")

nrecipes = 0
for f in sorted(os.listdir(os.path.join(A, "recipes", "grid"))):
    for r in load(os.path.join(A, "recipes", "grid", f)) or []:
        nrecipes += 1
        pat = r["ingredientPattern"].replace(",", "")
        if len(pat) != r["width"] * r["height"]: errors.append(f"{f} {r.get('name')}: pattern length")
        for ch in pat:
            if ch not in " _" and ch not in r["ingredients"]: errors.append(f"{f} {r.get('name')}: no ingredient for '{ch}'")
        for ing in list(r["ingredients"].values()) + [r["output"]]:
            dom, code = ing["code"].split(":", 1)
            want = "itemtypes" if ing.get("type", "block") == "item" else "blocktypes"
            if dom == "vintagee":
                if code not in codes: errors.append(f"{f} {r.get('name')}: unknown code {ing['code']}")
                elif codes[code] != want: errors.append(f"{f} {r.get('name')}: {ing['code']} is a {codes[code]} entry but type says {ing.get('type', 'block')}")

lang = load(os.path.join(A, "lang", "en.json")) or {}
for key in set(re.findall(r'"vintagee:([a-z0-9-]+)"', cs)):
    if key.endswith("-"): continue
    if key not in lang: errors.append(f"lang: missing key {key}")
for code in re.findall(r'Fail\(player, "(\w+)"', cs):
    if "error-" + code not in lang: errors.append(f"lang: missing error-{code}")
for f in os.listdir(os.path.join(A, "blocktypes")):
    for v in re.findall(r'"vintagee:([a-z0-9-]+)"', open(os.path.join(A, "blocktypes", f)).read()):
        if v not in lang: errors.append(f"lang: missing key {v} (from {f})")
for code, kind in codes.items():
    key = ("item-" if kind == "itemtypes" else "block-") + code
    if key not in lang: errors.append(f"lang: missing name {key}")

load(os.path.join(ROOT, "modinfo.json"))
load(os.path.join(A, "config", "vintagee-emc.json"))

print(f"{len(codes)} item/block codes, {nrecipes} recipes, {len(lang)} lang keys, {len(registered)} registered classes")
if errors:
    print("\n".join("ERROR " + e for e in errors)); sys.exit(1)
print("assets OK")
