"""Shared Swarm-faction palette and export, so every zombie uses the same strip.

One material for the whole faction means one palette; keeping the swatch table
in a single file stops the units drifting apart.
"""

import os

import zs_common as zs

# Swatch indices.
GREEN, BROWN, DARK, BONE, CRIMSON, DEEP_RED, PUS, BLACK = range(8)

SWARM_PALETTE = [
    (0.36, 0.45, 0.22),  # GREEN    sickly skin
    (0.33, 0.25, 0.18),  # BROWN    dull flesh
    (0.18, 0.13, 0.10),  # DARK     shadowed limb
    (0.72, 0.68, 0.55),  # BONE     spikes, claws
    (0.80, 0.08, 0.12),  # CRIMSON  bright blood accent
    (0.40, 0.05, 0.08),  # DEEP_RED wound
    (0.68, 0.62, 0.20),  # PUS      eyes, acid
    (0.05, 0.04, 0.04),  # BLACK
]


def export_unit(obj, name, budget):
    """Print stats, warn if over budget, export the FBX and the palette."""
    out = zs.out_dir()
    os.makedirs(out, exist_ok=True)

    tris = zs.triangle_count(obj)
    d = obj.dimensions
    flag = "" if tris <= budget else f"  ** OVER BUDGET ({budget}) **"
    print(f"[ZS] {name}: {tris} tris, {len(obj.data.vertices)} verts, "
          f"{d.x:.2f} x {d.y:.2f} x {d.z:.2f} m{flag}")

    zs.export_fbx(obj, os.path.join(out, name + ".fbx"))
    zs.write_palette(os.path.join(out, "ZS_Palette_Swarm_BaseColor.png"), SWARM_PALETTE)
