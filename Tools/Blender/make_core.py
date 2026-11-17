"""The Core: the objective the swarm is headed for. Synthetic faction.

    blender.exe -b -P Tools/Blender/make_core.py

A reactor column on a wide plinth, with a cyan crystal that glows through the
emission strip. It has to read from the tactical camera as the one thing worth
defending, so it is taller than any turret and the brightest object in a scene.
Uses the Synthetic palette, so it shares M_Turret's material.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import bmesh
import zs_common as zs
from make_turret import (STEEL_D, STEEL_M, CHROME, NAVY, CYAN, AMBER, HAZARD, SLATE)


def build_core():
    zs.reset_scene()
    bm = bmesh.new()

    def seg(a, b, wa, wb, swatch, da=None, db=None):
        zs.paint(bm, zs.limb(bm, a, b, wa, wb, da, db), swatch)

    # Plinth: wide and low, so it looks planted.
    seg((0, 0, 0.00), (0, 0, 0.35), 3.20, 2.60, STEEL_D)
    seg((0, 0, 0.35), (0, 0, 0.45), 2.70, 2.70, SLATE)

    # Column.
    seg((0, 0, 0.45), (0, 0, 1.70), 1.90, 1.60, STEEL_M)

    # Four buttress fins at the corners.
    for sx in (-1, 1):
        for sy in (-1, 1):
            seg((sx * 1.00, sy * 1.00, 0.45), (sx * 0.80, sy * 0.80, 1.95), 0.26, 0.16, STEEL_D)

    # The crystal.
    seg((0, 0, 1.30), (0, 0, 2.95), 1.10, 0.55, CYAN, 1.10, 0.55)

    # Cap and antenna.
    seg((0, 0, 2.95), (0, 0, 3.20), 1.30, 0.90, CHROME)
    seg((0, 0, 3.20), (0, 0, 4.20), 0.14, 0.07, STEEL_D)
    seg((0, 0, 4.20), (0, 0, 4.36), 0.16, 0.16, CYAN)

    # Warning blocks on the plinth edge, one per side.
    for sx, sy in ((1.45, 0), (-1.45, 0), (0, 1.45), (0, -1.45)):
        seg((sx, sy, 0.30), (sx, sy, 0.50), 0.34, 0.30, AMBER, 0.34, 0.30)

    obj = zs.finish_mesh(bm, "SM_Core_LOD0")
    bpy.context.view_layer.update()
    return obj


if __name__ == "__main__":
    obj = build_core()
    d = obj.dimensions
    print(f"[ZS] SM_Core_LOD0: {zs.triangle_count(obj)} tris, {d.x:.2f} x {d.y:.2f} x {d.z:.2f} m")
    zs.export_fbx(obj, os.path.join(zs.out_dir(), "SM_Core_LOD0.fbx"))
