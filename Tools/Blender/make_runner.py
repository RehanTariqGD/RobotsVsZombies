"""Runner zombie. Detail levels 0-2, plus the Swarm palette strip.

    blender.exe -b -P Tools/Blender/make_runner.py        (LOD0 only)
    blender.exe -b -P Tools/Blender/make_lods.py          (all tiers)

lod 0: full model (~280 tris). lod 1: no spikes, eyes, jaw or feet (~130).
lod 2: one block per limb and a single torso (~70). Each tier is built from the
same parts as LOD0, merged, rather than decimated: collapse-decimating a model
made of separate boxes shatters it into floating triangles.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import bmesh
import zs_common as zs
from zs_swarm import (GREEN, BROWN, DARK, BONE, CRIMSON, DEEP_RED, PUS, BLACK,
                      export_unit)


def build_runner(lod=0):
    zs.reset_scene()
    bm = bmesh.new()
    full = lod == 0
    mid = lod <= 1

    def seg(a, b, wa, wb, swatch, da=None, db=None):
        zs.paint(bm, zs.limb(bm, a, b, wa, wb, da, db), swatch)

    def thorn(base, direction, length, width, swatch=BONE):
        zs.paint(bm, zs.spike(bm, base, direction, length, width), swatch)

    # Forward is +Y in Blender. The forward lean sells "runner" in silhouette.
    if mid:
        seg((0, -0.02, 0.84), (0, 0.00, 1.00), 0.34, 0.38, DARK, 0.22, 0.24)  # pelvis
        seg((0, 0.00, 0.98), (0, 0.22, 1.52), 0.40, 0.54, GREEN, 0.24, 0.30)  # torso
    else:
        seg((0, -0.02, 0.84), (0, 0.22, 1.52), 0.40, 0.54, GREEN, 0.24, 0.30)  # torso + pelvis
    seg((0, 0.26, 1.50), (0, 0.40, 1.76), 0.22, 0.24, GREEN, 0.24, 0.26)       # head

    if full:
        seg((0, 0.34, 1.52), (0, 0.56, 1.46), 0.18, 0.12, DEEP_RED, 0.20, 0.10)  # jaw

    for s in (-1, 1):
        if full:
            seg((s * 0.07, 0.50, 1.68), (s * 0.07, 0.53, 1.68), 0.05, 0.05, PUS, 0.04, 0.04)

        # Arms hang long and forward.
        shoulder = (s * 0.29, 0.20, 1.46)
        elbow = (s * 0.38, 0.42, 1.06)
        hand = (s * 0.40, 0.72, 0.70)
        if mid:
            seg(shoulder, elbow, 0.15, 0.11, BROWN)
            seg(elbow, hand, 0.11, 0.08, GREEN)
        else:
            seg(shoulder, hand, 0.13, 0.08, GREEN)
        if full:
            for k in (-1, 0, 1):
                thorn(hand, (s * 0.12 * k, 0.55, -0.55), 0.24, 0.025)

        # Legs.
        hip = (s * 0.13, 0.00, 0.90)
        knee = (s * 0.15, 0.10, 0.48)
        ankle = (s * 0.14, -0.04, 0.10)
        if mid:
            seg(hip, knee, 0.18, 0.13, BROWN)
            seg(knee, ankle, 0.13, 0.10, DARK)
        else:
            seg(hip, ankle, 0.17, 0.10, BROWN)
        if full:
            seg(ankle, (s * 0.14, 0.22, 0.04), 0.12, 0.10, DARK, 0.10, 0.07)

    if full:
        for i in range(5):
            t = i / 4.0
            base = (0, 0.00 + 0.22 * t - 0.13, 1.00 + 0.52 * t)
            thorn(base, (0, -0.8, 0.5), 0.22 - 0.03 * i, 0.035)
        seg((0.10, 0.20, 1.30), (0.10, 0.22, 1.42), 0.12, 0.10, CRIMSON, 0.30, 0.30)
        thorn((-0.24, 0.20, 1.52), (-0.9, 0.0, 0.5), 0.18, 0.03)

        # A little deterministic noise so no two limbs are machined-straight.
        zs.jitter(bm, 0.012, seed=1337)

    obj = zs.finish_mesh(bm, f"SM_Runner_LOD{lod}")
    bpy.context.view_layer.update()
    return obj


if __name__ == "__main__":
    export_unit(build_runner(), "SM_Runner_LOD0", budget=400)
