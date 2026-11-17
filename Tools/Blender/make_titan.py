"""Meat Titan. Boss, ~5 m. Detail levels 0-2.

    blender.exe -b -P Tools/Blender/make_titan.py         (LOD0 only)
    blender.exe -b -P Tools/Blender/make_lods.py          (all tiers)

Silhouette from the concept: a huge trapezoid body, a tiny head sunk between
massive shoulders, giant fists dragging near the ground, and bone horns swept
up across the back. One or two of these exist at a time, so it gets a slightly
bigger budget than the horde types.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import bmesh
import zs_common as zs
from zs_swarm import (GREEN, BROWN, DARK, BONE, CRIMSON, DEEP_RED, PUS, BLACK,
                      export_unit)


def build_titan(lod=0):
    zs.reset_scene()
    bm = bmesh.new()
    full = lod == 0
    mid = lod <= 1

    def seg(a, b, wa, wb, swatch, da=None, db=None):
        zs.paint(bm, zs.limb(bm, a, b, wa, wb, da, db), swatch)

    def seg_mix(a, b, wa, wb, choices, seed, da=None, db=None):
        faces = zs.limb(bm, a, b, wa, wb, da, db)
        zs.paint_mix(bm, faces, choices, seed)

    def thorn(base, direction, length, width, swatch=BONE):
        zs.paint(bm, zs.spike(bm, base, direction, length, width), swatch)

    # Legs: thick pillars ending in broad feet.
    for s in (-1, 1):
        hip = (s * 0.55, 0.00, 2.10)
        knee = (s * 0.66, 0.16, 1.10)
        ankle = (s * 0.62, 0.00, 0.28)
        if mid:
            seg_mix(hip, knee, 0.86, 0.70, [(BROWN, 60), (GREEN, 30), (CRIMSON, 10)], 11 + s)
            seg(knee, ankle, 0.70, 0.62, DARK)
            seg(ankle, (s * 0.62, 0.62, 0.10), 0.70, 0.78, BROWN, 0.60, 0.26)
        else:
            seg(hip, ankle, 0.80, 0.66, BROWN)
        if full:
            for k in (-1, 0, 1):
                thorn((s * 0.62 + k * 0.22, 0.64, 0.10), (0, 1.0, -0.2), 0.20, 0.07, BONE)

    # Belly: a dark-red mass with a pus patch, as in the concept.
    if mid:
        zs.blob(bm, (0, 0.12, 2.42), (1.00, 0.84, 0.80),
                [(DEEP_RED, 55), (CRIMSON, 20), (PUS, 10), (BROWN, 15)], seed=5)
    else:
        seg((0, 0.12, 2.10), (0, 0.12, 2.80), 1.80, 1.70, DEEP_RED, 1.50, 1.40)

    # Torso: a wide trapezoid, broad across the shoulders.
    seg_mix((0, 0.00, 2.80), (0, 0.18, 3.95), 1.70, 2.60,
            [(BROWN, 50), (GREEN, 36), (CRIMSON, 14)], 21, 1.10, 1.30)

    # Tiny head, sunk low between the shoulders.
    seg((0, 0.52, 3.78), (0, 0.62, 4.24), 0.56, 0.52, GREEN, 0.52, 0.48)
    if full:
        seg((0, 0.84, 3.92), (0, 0.90, 4.10), 0.30, 0.28, BLACK, 0.06, 0.06)  # mouth
        seg((0, 0.80, 4.10), (0, 0.84, 4.16), 0.38, 0.36, BONE, 0.05, 0.05)   # teeth
    if mid:
        # Shoulder mass wrapping the neck.
        seg((0, 0.40, 3.70), (0, 0.46, 4.02), 1.60, 1.10, DEEP_RED, 0.56, 0.50)

    # Bone horns swept across the back. Two survive at the lowest tier: they
    # are what separates the Titan from a plain block at distance.
    horns = (
        ((-0.9, -0.32, 3.80), (-0.7, -0.3, 1.0), 1.10),
        ((-0.3, -0.40, 3.95), (-0.2, -0.3, 1.0), 1.25),
        ((0.35, -0.40, 3.95), (0.2, -0.3, 1.0), 1.15),
        ((0.95, -0.32, 3.80), (0.7, -0.3, 1.0), 0.95),
    )
    for i, (base, direction, length) in enumerate(horns):
        if mid or i in (1, 2):
            thorn(base, direction, length, 0.17)

    # Arms: enormous, ending in block fists that hang near the ground.
    for s in (-1, 1):
        shoulder = (s * 1.40, 0.10, 3.60)
        elbow = (s * 1.62, 0.50, 2.40)
        fist_top = (s * 1.50, 0.80, 1.45)
        fist_bottom = (s * 1.50, 0.86, 0.34)
        if mid:
            seg_mix(shoulder, elbow, 1.00, 0.86, [(GREEN, 50), (BROWN, 35), (CRIMSON, 15)], 31 + s)
            seg_mix(elbow, fist_top, 0.86, 0.96, [(GREEN, 40), (CRIMSON, 25), (BROWN, 35)], 41 + s)
        else:
            seg(shoulder, fist_top, 0.96, 0.96, GREEN)
        seg(fist_top, fist_bottom, 1.00, 1.20, BROWN, 0.96, 1.10)             # fist
        if full:
            for k in (-1, 0, 1):                                              # knuckle gaps
                seg((s * 1.50 + k * 0.30, 1.40, 0.62), (s * 1.50 + k * 0.30, 1.43, 0.36),
                    0.05, 0.05, BLACK, 0.05, 0.05)

    if full:
        zs.jitter(bm, 0.025, seed=4242)

    obj = zs.finish_mesh(bm, f"SM_Titan_LOD{lod}")
    bpy.context.view_layer.update()
    return obj


if __name__ == "__main__":
    export_unit(build_titan(), "SM_Titan_LOD0", budget=450)
