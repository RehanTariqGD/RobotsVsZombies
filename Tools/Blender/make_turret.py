"""Railgun turret, LOD0 (budget ~400 tris), plus the Synthetic palette strips.

    blender.exe -b -P Tools/Blender/make_turret.py

Faction look per the GDD: hard geometric angles, dark steel and chrome, neon
blue LEDs. Neon is an emissive swatch rather than a real light — Entities
Graphics is Forward+ only, and many small lights are the expensive thing there.

Pivot is at the feet so the mesh sits on the ground at y = 0. Faces +Y.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import bmesh
import zs_common as zs

STEEL_D, STEEL_M, CHROME, NAVY, CYAN, AMBER, HAZARD, SLATE = range(8)

SYNTH_PALETTE = [
    (0.18, 0.20, 0.24),  # STEEL_D  dark steel
    (0.32, 0.36, 0.42),  # STEEL_M  mid steel
    (0.70, 0.74, 0.80),  # CHROME   barrels, trim
    (0.06, 0.07, 0.10),  # NAVY     recesses
    (0.20, 0.85, 1.00),  # CYAN     neon LED
    (0.95, 0.62, 0.12),  # AMBER    warning
    (0.85, 0.28, 0.10),  # HAZARD   damaged / understaffed
    (0.28, 0.31, 0.36),  # SLATE
]

# Only the LEDs glow. Amber is lit dimly so the damaged state can reuse it.
SYNTH_EMISSION = [
    (0, 0, 0), (0, 0, 0), (0, 0, 0), (0, 0, 0),
    (0.20, 0.85, 1.00), (0.35, 0.21, 0.04), (0, 0, 0), (0, 0, 0),
]


def build_turret():
    zs.reset_scene()
    bm = bmesh.new()

    def seg(a, b, wa, wb, swatch, da=None, db=None):
        zs.paint(bm, zs.limb(bm, a, b, wa, wb, da, db), swatch)

    # Plinth and pedestal.
    seg((0, 0, 0.00), (0, 0, 0.22), 1.30, 1.00, STEEL_D)
    seg((0, 0, 0.22), (0, 0, 0.28), 0.96, 0.96, SLATE)                      # trim ring
    seg((0, 0, 0.28), (0, 0, 0.78), 0.62, 0.52, STEEL_M)                    # pedestal

    # Rotating head: a wedge, narrower at the front.
    seg((0, -0.18, 0.82), (0, 0.30, 0.82), 0.86, 0.70, STEEL_D, 0.20, 0.20)
    seg((0, -0.20, 0.80), (0, 0.32, 0.80), 0.80, 0.64, STEEL_M, 0.48, 0.38) # body
    seg((0, -0.12, 1.08), (0, 0.22, 1.08), 0.62, 0.50, STEEL_D, 0.20, 0.16) # roof

    # Twin rails, chrome, running past the front of the head.
    for s in (-1, 1):
        seg((s * 0.17, 0.20, 0.82), (s * 0.17, 1.35, 0.82), 0.09, 0.07, CHROME, 0.09, 0.07)
        seg((s * 0.17, 0.30, 0.82), (s * 0.17, 0.70, 0.82), 0.15, 0.13, STEEL_D, 0.15, 0.13)  # shroud

    # Neon: head stripes, muzzle ring, base edge lights.
    for s in (-1, 1):
        seg((s * 0.42, -0.14, 0.86), (s * 0.40, 0.26, 0.86), 0.03, 0.03, CYAN, 0.05, 0.05)
        seg((s * 0.55, 0.50, 0.04), (s * 0.55, -0.50, 0.04), 0.04, 0.04, CYAN, 0.06, 0.06)
        seg((s * 0.17, 1.10, 0.82), (s * 0.17, 1.14, 0.82), 0.12, 0.12, CYAN, 0.12, 0.12)
    seg((0, 0.00, 1.14), (0, 0.10, 1.14), 0.16, 0.12, CYAN, 0.12, 0.10)     # sensor

    # Warning chevron on the front of the plinth.
    seg((0, 0.60, 0.04), (0, 0.66, 0.16), 0.40, 0.34, AMBER, 0.02, 0.02)

    # Rear heat vent, recessed.
    seg((0, -0.30, 0.86), (0, -0.18, 0.86), 0.34, 0.34, NAVY, 0.22, 0.22)

    obj = zs.finish_mesh(bm, "SM_Turret_Rail_LOD0")
    bpy.context.view_layer.update()
    return obj


def main():
    out = zs.out_dir()
    os.makedirs(out, exist_ok=True)

    obj = build_turret()
    dims = obj.dimensions
    print(f"[ZS] Turret LOD0: {zs.triangle_count(obj)} tris, {len(obj.data.vertices)} verts, "
          f"{dims.x:.2f} x {dims.y:.2f} x {dims.z:.2f} m")

    zs.export_fbx(obj, os.path.join(out, "SM_Turret_Rail_LOD0.fbx"))
    zs.write_palette(os.path.join(out, "T_Palette_Synth_BaseColor.png"), SYNTH_PALETTE)
    zs.write_palette(os.path.join(out, "T_Palette_Synth_Emission.png"), SYNTH_EMISSION)
    print(f"[ZS] wrote {out}")


if __name__ == "__main__":
    main()
