"""Horde LOD ladder for every Swarm unit.

    blender.exe -b -P Tools/Blender/make_lods.py

Every tier comes from the unit's own builder at a lower detail level: spikes,
eyes and toes drop first, then limbs merge into single blocks. LOD3 is a
hand-built two-block silhouette. Budgets follow the technical review's ladder:
~400 / ~150 / ~60 / ~24 triangles. Decimation was tried and rejected: the
models are separate boxes, and collapse-decimating them shatters them into
floating triangles.

Every tier keeps the same palette UVs, so all four share one material.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import bmesh
import zs_common as zs
from zs_swarm import (GREEN, BROWN, DARK, BONE, CRIMSON, DEEP_RED, PUS, BLACK,
                      export_unit)
from make_runner import build_runner
from make_spitter import build_spitter
from make_burrower import build_burrower
from make_titan import build_titan

# name, builder, LOD0 budget, body swatch, head swatch for the LOD3 silhouette
UNITS = (
    ("Runner", build_runner, 400, GREEN, GREEN),
    ("Spitter", build_spitter, 400, GREEN, CRIMSON),
    ("Burrower", build_burrower, 400, BROWN, GREEN),
    ("Titan", build_titan, 450, BROWN, GREEN),
)

def impostor(src, name, body_swatch, head_swatch):
    """Two blocks sized from the unit's bounds: ~24 triangles.

    This is only ever seen small and far away, so it needs the right height,
    footprint and a head-shaped bump, not detail.
    """
    xs = [v.co.x for v in src.data.vertices]
    ys = [v.co.y for v in src.data.vertices]
    zs_ = [v.co.z for v in src.data.vertices]
    lo = (min(xs), min(ys), min(zs_))
    hi = (max(xs), max(ys), max(zs_))
    w, d, h = hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]
    cx, cy = (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2

    bm = bmesh.new()

    body = zs.limb(bm, (cx, cy, lo[2]), (cx, cy, lo[2] + h * 0.74),
                   w * 0.62, w * 0.46, d * 0.50, d * 0.40)
    zs.paint(bm, body, body_swatch)

    # Head sits toward the front (+Y) and the top, like the full models.
    hy = cy + d * 0.18
    head = zs.limb(bm, (cx, hy, lo[2] + h * 0.66), (cx, hy, lo[2] + h * 0.96),
                   w * 0.26, w * 0.22, w * 0.26, w * 0.22)
    zs.paint(bm, head, head_swatch)

    return zs.finish_mesh(bm, name)


def main():
    for name, builder, budget, body, head in UNITS:
        lod0 = builder(0)
        export_unit(lod0, f"SM_{name}_LOD0", budget=budget)
        # The impostor is sized from LOD0's bounds, so it must be built before
        # the next builder call resets the scene.
        lod3 = impostor(lod0, f"SM_{name}_LOD3", body, head)

        export_unit(lod3, f"SM_{name}_LOD3", budget=24)
        export_unit(builder(1), f"SM_{name}_LOD1", budget=230)
        export_unit(builder(2), f"SM_{name}_LOD2", budget=125)


if __name__ == "__main__":
    main()
