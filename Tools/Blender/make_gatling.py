"""Synthetic gatling turret, LOD0 (hard budget: 400 triangles).

    blender.exe -b -P Tools/Blender/make_gatling.py

One flat-shaded mesh, facing +Y, Z up, origin at the ground. Reuses the
railgun's Synthetic palette strip without writing any new textures.
"""

import math
import os
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import bmesh
from mathutils import Vector
import zs_common as zs
from make_turret import (
    STEEL_D, STEEL_M, CHROME, NAVY, CYAN, AMBER, SLATE,
    SYNTH_PALETTE, SYNTH_EMISSION,
)


def build_gatling():
    zs.reset_scene()
    bm = bmesh.new()

    def face(verts, swatch):
        zs.paint(bm, [bm.faces.new(verts)], swatch)

    def box(centre, size, swatch, taper=1.0):
        zs.paint(bm, zs.tapered_box(bm, centre, size, taper), swatch)

    def tube(a, b, radius, swatch, sides=6, bore=False, cap=STEEL_D):
        """Faceted cylinder; barrels have a dark, genuinely recessed muzzle."""
        a, b = Vector(a), Vector(b)
        axis = (b - a).normalized()
        right = axis.cross(Vector((0, 0, 1))).normalized()
        up = right.cross(axis).normalized()

        def ring(centre, r):
            return [bm.verts.new(centre + r * (
                right * math.cos(j * math.tau / sides) +
                up * math.sin(j * math.tau / sides))) for j in range(sides)]

        back, front = ring(a, radius), ring(b, radius)
        face(back[::-1], STEEL_D)
        for j in range(sides):
            k = (j + 1) % sides
            face([back[j], back[k], front[k], front[j]], swatch)
        if bore:
            inset = ring(b - axis * 0.055, radius * 0.68)
            for j in range(sides):
                k = (j + 1) % sides
                face([front[j], front[k], inset[k], inset[j]], SLATE)
            face(inset, NAVY)
        else:
            face(front, cap)

    # Broad, low plinth; all feet terminate exactly at z=0.
    box((0, 0, 0.12), (1.50, 1.40, 0.24), STEEL_D, taper=0.85)
    box((0, -0.10, 0.30), (0.94, 0.90, 0.16), SLATE, taper=0.90)
    box((0, -0.10, 0.46), (0.65, 0.62, 0.22), STEEL_M)

    # Squat rotating receiver, not the railgun's slender elevated head.
    box((0, -0.17, 0.83), (0.96, 0.80, 0.58), STEEL_M, taper=0.90)
    box((0, -0.17, 1.12), (0.91, 0.77, 0.16), STEEL_D, taper=0.80)
    tube((0, 0.16, 0.87), (0, 0.36, 0.87), 0.37, STEEL_D,
         sides=8, cap=SLATE)

    # Six thick, short chrome barrels in a hexagon around an EMPTY centre.
    # No forward binding plate: all six bores remain legible from gameplay.
    for j in range(6):
        angle = j * math.tau / 6
        x = 0.235 * math.cos(angle)
        z = 0.87 + 0.235 * math.sin(angle)
        tube((x, 0.30, z), (x, 0.94, z), 0.115, CHROME, bore=True)

    # Side-mounted ammunition drum intersects the receiver at its axle.
    tube((0.38, -0.20, 0.83), (0.88, -0.20, 0.83), 0.31,
         SLATE, sides=8, cap=STEEL_M)
    tube((0.875, -0.20, 0.83), (0.92, -0.20, 0.83), 0.18,
         CHROME, cap=NAVY)

    # Thin solid strips, embedded in the armour, avoid open decal normals.
    box((0, -0.17, 1.201), (0.11, 0.54, 0.014), AMBER)
    box((0, 0.612, 0.205), (0.80, 0.025, 0.035), CYAN)

    obj = zs.finish_mesh(bm, "SM_Turret_Gatling_LOD0")
    bpy.context.view_layer.update()
    assert zs.triangle_count(obj) <= 400, "Gatling exceeded triangle budget"
    assert len([o for o in bpy.context.scene.objects if o.type == "MESH"]) == 1
    assert abs(min(v.co.z for v in obj.data.vertices)) < 1e-6
    assert obj.dimensions.x <= 2.0 and obj.dimensions.y <= 2.0
    assert 1.0 <= obj.dimensions.z <= 1.5
    swatches = [zs.swatch_uv(i) for i in range(len(SYNTH_PALETTE))]
    for loop in obj.data.uv_layers.active.data:
        assert any(abs(loop.uv.x - u) < 1e-6 and abs(loop.uv.y - v) < 1e-6
                   for u, v in swatches)
    return obj


def main():
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    path = os.path.join(root, "Assets", "ZeroSynth", "Art", "Models", "SM_Turret_Gatling_LOD0.fbx")
    obj = build_gatling()
    dims = obj.dimensions
    print(f"[ZS] Gatling LOD0: {zs.triangle_count(obj)} tris, {len(obj.data.vertices)} verts, "
          f"{dims.x:.3f} x {dims.y:.3f} x {dims.z:.3f} m (Blender XYZ)")
    zs.export_fbx(obj, path)
    print(f"[ZS] wrote {path}; reuse T_Palette_Synth_BaseColor / _Emission")


if __name__ == "__main__":
    main()
