"""Runner zombie, LOD0 (budget ~400 tris), plus the Swarm palette strip.

    blender.exe -b -P Tools/Blender/make_runner.py

Output goes to Tools/Blender/out/. Nothing is written into Assets/ from here;
exported files are reviewed and moved in by hand.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import bmesh
import zs_common as zs

# Swarm palette swatch indices. Organic, matches the GDD: dull browns, sickly
# greens, bright crimson accents. Same 8-swatch strip as the Synthetic one.
GREEN, BROWN, DARK, BONE, CRIMSON, DEEP_RED, PUS, BLACK = range(8)

SWARM_PALETTE = [
    (0.36, 0.45, 0.22),  # GREEN    sickly skin
    (0.33, 0.25, 0.18),  # BROWN    dull flesh
    (0.18, 0.13, 0.10),  # DARK     shadowed limb
    (0.72, 0.68, 0.55),  # BONE     spikes, claws
    (0.80, 0.08, 0.12),  # CRIMSON  bright blood accent
    (0.40, 0.05, 0.08),  # DEEP_RED wound
    (0.68, 0.62, 0.20),  # PUS      eyes
    (0.05, 0.04, 0.04),  # BLACK
]


def build_runner():
    zs.reset_scene()
    bm = bmesh.new()

    def seg(a, b, wa, wb, swatch, da=None, db=None):
        zs.paint(bm, zs.limb(bm, a, b, wa, wb, da, db), swatch)

    def thorn(base, direction, length, width, swatch=BONE):
        zs.paint(bm, zs.spike(bm, base, direction, length, width), swatch)

    # Forward is +Y in Blender. The forward lean sells "runner" in silhouette.
    seg((0, -0.02, 0.84), (0, 0.00, 1.00), 0.34, 0.38, DARK, 0.22, 0.24)      # pelvis
    seg((0, 0.00, 0.98), (0, 0.22, 1.52), 0.40, 0.54, GREEN, 0.24, 0.30)      # torso
    seg((0, 0.26, 1.50), (0, 0.40, 1.76), 0.22, 0.24, GREEN, 0.24, 0.26)      # head
    seg((0, 0.34, 1.52), (0, 0.56, 1.46), 0.18, 0.12, DEEP_RED, 0.20, 0.10)   # jaw

    for s in (-1, 1):
        # Eyes
        seg((s * 0.07, 0.50, 1.68), (s * 0.07, 0.53, 1.68), 0.05, 0.05, PUS, 0.04, 0.04)

        # Arms hang long and forward: shoulder -> elbow -> hand share joints.
        shoulder = (s * 0.29, 0.20, 1.46)
        elbow = (s * 0.38, 0.42, 1.06)
        hand = (s * 0.40, 0.72, 0.70)
        seg(shoulder, elbow, 0.15, 0.11, BROWN)
        seg(elbow, hand, 0.11, 0.08, GREEN)
        for k in (-1, 0, 1):                                              # claws
            thorn(hand, (s * 0.12 * k, 0.55, -0.55), 0.24, 0.025)

        # Legs: hip -> knee -> ankle -> toe.
        hip = (s * 0.13, 0.00, 0.90)
        knee = (s * 0.15, 0.10, 0.48)
        ankle = (s * 0.14, -0.04, 0.10)
        toe = (s * 0.14, 0.22, 0.04)
        seg(hip, knee, 0.18, 0.13, BROWN)
        seg(knee, ankle, 0.13, 0.10, DARK)
        seg(ankle, toe, 0.12, 0.10, DARK, 0.10, 0.07)

    # Back spines and an open wound, so the silhouette is not a clean human.
    for i in range(5):
        t = i / 4.0
        base = (0, 0.00 + 0.22 * t - 0.13, 1.00 + 0.52 * t)
        thorn(base, (0, -0.8, 0.5), 0.22 - 0.03 * i, 0.035)
    seg((0.10, 0.20, 1.30), (0.10, 0.22, 1.42), 0.12, 0.10, CRIMSON, 0.30, 0.30)
    thorn((-0.24, 0.20, 1.52), (-0.9, 0.0, 0.5), 0.18, 0.03)              # shoulder

    # A little deterministic noise so no two limbs are machined-straight.
    zs.jitter(bm, 0.012, seed=1337)

    obj = zs.finish_mesh(bm, "ZS_Runner_LOD0")
    bpy.context.view_layer.update()
    return obj


def main():
    out = zs.out_dir()
    os.makedirs(out, exist_ok=True)

    obj = build_runner()
    tris = zs.triangle_count(obj)

    dims = obj.dimensions
    print(f"[ZS] Runner LOD0: {tris} tris, {len(obj.data.vertices)} verts, "
          f"{dims.x:.2f} x {dims.y:.2f} x {dims.z:.2f} m")

    zs.export_fbx(obj, os.path.join(out, "ZS_Runner_LOD0.fbx"))
    zs.write_palette(os.path.join(out, "ZS_Palette_Swarm_BaseColor.png"), SWARM_PALETTE)
    print(f"[ZS] wrote {out}")


main()
