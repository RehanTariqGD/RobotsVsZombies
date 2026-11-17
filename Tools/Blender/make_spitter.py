"""Spitter, LOD0 (budget ~400 tris). Ranged acid zombie, ~1.7 m.

    blender.exe -b -P Tools/Blender/make_spitter.py

Silhouette from the concept: stilt legs, a huge faceted acid sac for a belly,
a hunched shoulder hump, and the head thrown back with a flared funnel mouth.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import bmesh
import zs_common as zs
from zs_swarm import (GREEN, BROWN, DARK, BONE, CRIMSON, DEEP_RED, PUS, BLACK,
                      export_unit)


def build():
    zs.reset_scene()
    bm = bmesh.new()

    def seg(a, b, wa, wb, swatch, da=None, db=None):
        zs.paint(bm, zs.limb(bm, a, b, wa, wb, da, db), swatch)

    def thorn(base, direction, length, width, swatch=BONE):
        zs.paint(bm, zs.spike(bm, base, direction, length, width), swatch)

    # Stilt legs: long and thin, so the belly reads as the whole unit.
    for s in (-1, 1):
        hip = (s * 0.12, 0.00, 0.80)
        knee = (s * 0.14, 0.10, 0.44)
        ankle = (s * 0.13, -0.03, 0.08)
        seg(hip, knee, 0.11, 0.09, BROWN)
        seg(knee, ankle, 0.09, 0.07, GREEN if s > 0 else BROWN)
        seg(ankle, (s * 0.13, 0.20, 0.04), 0.09, 0.08, DARK, 0.08, 0.06)

    seg((0, 0.00, 0.78), (0, 0.04, 0.90), 0.30, 0.34, DARK, 0.22, 0.26)      # pelvis

    # The acid sac. Mostly green with pus-yellow patches, like the concept.
    zs.blob(bm, (0, 0.10, 1.06), (0.44, 0.40, 0.40),
            [(GREEN, 58), (PUS, 36), (DEEP_RED, 6)], seed=7)

    # Narrow chest above the sac, then a heavy hump behind the head.
    seg((0, 0.02, 1.30), (0, -0.04, 1.50), 0.34, 0.40, BROWN, 0.26, 0.28)
    seg((0, -0.14, 1.40), (0, -0.20, 1.58), 0.32, 0.24, DARK, 0.26, 0.20)    # hump
    seg((0.18, -0.08, 1.50), (0.22, -0.10, 1.58), 0.12, 0.09, CRIMSON, 0.16, 0.12)

    # Head thrown back, funnel mouth flaring forward and up.
    seg((0, 0.00, 1.50), (0, -0.04, 1.64), 0.18, 0.20, GREEN, 0.18, 0.20)    # neck
    seg((0, -0.04, 1.62), (0, 0.04, 1.74), 0.24, 0.26, BONE, 0.24, 0.26)     # skull
    seg((0, 0.04, 1.66), (0, 0.26, 1.80), 0.16, 0.32, CRIMSON, 0.16, 0.32)   # funnel rim
    seg((0, 0.10, 1.68), (0, 0.285, 1.795), 0.09, 0.22, BLACK, 0.09, 0.22)   # throat, proud of the rim
    seg((0.13, 0.00, 1.70), (0.13, 0.05, 1.73), 0.05, 0.05, PUS, 0.05, 0.05) # eye

    # Long thin arms, hanging forward.
    for s in (-1, 1):
        shoulder = (s * 0.25, 0.00, 1.46)
        elbow = (s * 0.36, 0.06, 1.06)
        hand = (s * 0.32, 0.22, 0.70)
        seg(shoulder, elbow, 0.10, 0.08, GREEN)
        seg(elbow, hand, 0.08, 0.07, BONE if s > 0 else BROWN)
        for k in (-1, 0, 1):
            thorn(hand, (s * 0.10 * k, 0.45, -0.7), 0.16, 0.02)

    zs.jitter(bm, 0.008, seed=2024)
    obj = zs.finish_mesh(bm, "ZS_Spitter_LOD0")
    bpy.context.view_layer.update()
    return obj


export_unit(build(), "ZS_Spitter_LOD0", budget=400)
