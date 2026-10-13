#!/usr/bin/env python3
"""Writes assets/vintagee/shapes/item/tool/sledgehammer.json (original model).

Built in the same frame as the game's hammer model so the vanilla hammer transforms line up: the handle runs
along +X (centred on y=8, z=8, grip end at low X) and the head sits at the far +X end, spanning Z.
"""
import json
import os
from collections import OrderedDict as O

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "vintagee", "shapes", "item", "tool", "sledgehammer.json")


def box(name, frm, to, tex, uvscale=1.0):
    """A cuboid with every face textured; UVs follow the face size (clamped to the 16x16 texture)."""
    sx, sy, sz = (to[i] - frm[i] for i in range(3))

    def uv(w, h):
        w, h = min(16, w * uvscale), min(16, h * uvscale)
        return [0, 0, round(w, 3), round(h, 3)]

    faces = O([
        ("north", O([("texture", tex), ("uv", uv(sx, sy))])),
        ("east", O([("texture", tex), ("uv", uv(sz, sy))])),
        ("south", O([("texture", tex), ("uv", uv(sx, sy))])),
        ("west", O([("texture", tex), ("uv", uv(sz, sy))])),
        ("up", O([("texture", tex), ("uv", uv(sx, sz))])),
        ("down", O([("texture", tex), ("uv", uv(sx, sz))])),
    ])
    return O([("name", name), ("from", frm), ("to", to), ("faces", faces)])


elements = [
    box("handle", [2, 7.4, 7.4], [21.5, 8.6, 8.6], "#handle"),
    box("grip", [2.5, 7.2, 7.2], [7.5, 8.8, 8.8], "#band"),
    box("pommel", [1.6, 7.1, 7.1], [2.6, 8.9, 8.9], "#band"),
    box("collar", [16, 7.15, 7.15], [17.5, 8.85, 8.85], "#band"),
    box("head", [17.5, 5.75, 3.5], [21.5, 10.25, 12.5], "#head"),
    box("face north", [17.75, 6.0, 2.75], [21.25, 10.0, 3.5], "#band"),
    box("face south", [17.75, 6.0, 12.5], [21.25, 10.0, 13.25], "#band"),
    box("cap", [21.5, 7.1, 7.1], [21.9, 8.9, 8.9], "#band"),
]

shape = O([
    ("textureWidth", 16),
    ("textureHeight", 16),
    ("textures", O([("head", "vintagee:item/tool/material/darkmatter"),
                    ("handle", "game:block/wood/debarked/ebony"),
                    ("band", "vintagee:item/tool/material/redmatter")])),
    ("elements", elements),
])
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w") as f:
    json.dump(shape, f, indent=1)
    f.write("\n")
print("wrote", OUT)
