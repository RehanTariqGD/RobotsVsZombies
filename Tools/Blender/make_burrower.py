"""Burrower, LOD0 (budget ~400 tris). Tunnelling zombie, long and low.

    blender.exe -b -P Tools/Blender/make_burrower.py

Silhouette from the concept: a drill-shaped wedge head, oversized bone shovel
claws for forearms, a humped ridged back, and short heavy back legs. Brown
dominant so it reads as dirt-coloured against the other types.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import bmesh
import zs_common as zs
from zs_swarm import (GREEN, BROWN, DARK, BONE, CRIMSON, DEEP_RED, PUS, BLACK,
                      export_unit)


def build_burrower():
    zs.reset_scene()
    bm = bmesh.new()

    def seg(a, b, wa, wb, swatch, da=None, db=None):
        zs.paint(bm, zs.limb(bm, a, b, wa, wb, da, db), swatch)

    def thorn(base, direction, length, width, swatch=BONE):
        zs.paint(bm, zs.spike(bm, base, direction, length, width), swatch)

    # Body: a long low wedge, thickest over the shoulders.
    seg((0, -0.55, 0.58), (0, 0.38, 0.70), 0.70, 0.84, BROWN, 0.56, 0.62)
    seg((0, -0.35, 0.92), (0, 0.22, 1.02), 0.52, 0.62, DARK, 0.34, 0.40)     # hump
    for i in range(4):                                                        # ridge
        y = -0.30 + i * 0.16
        thorn((0, y, 1.04), (0, -0.2, 1.0), 0.20 - i * 0.02, 0.05, DARK)

    # Drill head: a wedge that narrows to a point, jaw underneath.
    seg((0, 0.34, 0.60), (0, 0.96, 0.40), 0.46, 0.04, GREEN, 0.40, 0.05)
    seg((0, 0.40, 0.44), (0, 0.88, 0.34), 0.24, 0.03, DEEP_RED, 0.05, 0.02)  # jaw
    for s in (-1, 1):
        seg((s * 0.14, 0.50, 0.66), (s * 0.12, 0.62, 0.64), 0.07, 0.05, BLACK, 0.04, 0.04)
    seg((0.18, 0.20, 0.82), (0.22, 0.34, 0.76), 0.14, 0.10, CRIMSON, 0.14, 0.10)

    # The shovel claws. Oversized on purpose: these ARE the unit's silhouette.
    for s in (-1, 1):
        shoulder = (s * 0.40, 0.28, 0.74)
        elbow = (s * 0.58, 0.52, 0.44)
        wrist = (s * 0.52, 0.80, 0.24)
        seg(shoulder, elbow, 0.30, 0.26, BROWN, 0.28, 0.24)
        seg(elbow, wrist, 0.26, 0.34, GREEN, 0.24, 0.30)
        seg(wrist, (s * 0.50, 1.18, 0.10), 0.52, 0.46, BONE, 0.16, 0.10)      # blade
        for k in (-1, 0, 1):
            thorn((s * (0.50 + 0.16 * k), 1.14, 0.10), (0, 1.0, -0.15), 0.26, 0.06)

    # Short, heavy back legs.
    for s in (-1, 1):
        hip = (s * 0.36, -0.40, 0.58)
        knee = (s * 0.46, -0.62, 0.32)
        foot = (s * 0.46, -0.46, 0.05)
        seg(hip, knee, 0.30, 0.22, DARK)
        seg(knee, foot, 0.22, 0.20, BROWN, 0.22, 0.20)
        seg(foot, (s * 0.46, -0.28, 0.03), 0.22, 0.20, DARK, 0.10, 0.06)

    zs.jitter(bm, 0.010, seed=909)
    obj = zs.finish_mesh(bm, "SM_Burrower_LOD0")
    bpy.context.view_layer.update()
    return obj


if __name__ == "__main__":
    export_unit(build_burrower(), "SM_Burrower_LOD0", budget=400)
