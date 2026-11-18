"""Synthetic artillery mortar, LOD0 (hard budget: 400 triangles).

    blender.exe -b -P Tools/Blender/make_mortar.py

One flat-shaded mesh, facing +Y, Z up, origin at the feet. The short tube
elevates 65 degrees. Uses the unchanged Synthetic palette; no new textures.
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


def build_mortar():
    zs.reset_scene()
    bm = bmesh.new()

    def face(verts, swatch):
        zs.paint(bm, [bm.faces.new(verts)], swatch)

    def box(centre, size, swatch, taper=1.0):
        zs.paint(bm, zs.tapered_box(bm, centre, size, taper), swatch)

    def tube(a, b, radius, swatch, sides=8, bore=False, cap=STEEL_D):
        """Arbitrary-axis cylinder, optionally with a thick lip and deep bore."""
        a, b = Vector(a), Vector(b)
        axis = (b - a).normalized()
        right = Vector((1, 0, 0))
        up = axis.cross(right).normalized()

        def ring(centre, r):
            return [bm.verts.new(centre + r * (
                right * math.cos(j * math.tau / sides) +
                up * math.sin(j * math.tau / sides))) for j in range(sides)]

        def join(first, second, colour):
            for j in range(sides):
                k = (j + 1) % sides
                face([first[j], first[k], second[k], second[j]], colour)

        back, front = ring(a, radius), ring(b, radius)
        face(back[::-1], STEEL_D)
        join(back, front, swatch)
        if bore:
            lip = ring(b, radius * 0.68)
            inset = ring(b - axis * 0.19, radius * 0.68)
            join(front, lip, CHROME)
            join(lip, inset, NAVY)
            face(inset, NAVY)
        else:
            face(front, cap)

    # A wide deck with four splayed stabiliser pads, all touching the ground.
    box((0, 0, 0.16), (1.42, 1.40, 0.24), STEEL_D, taper=0.92)
    for x in (-0.69, 0.69):
        for y in (-0.63, 0.63):
            box((x, y, 0.10), (0.52, 0.56, 0.20), SLATE, taper=0.72)

    # Symmetric cradle cheeks overlap deck, breech and recoil collar.
    for x in (-0.29, 0.29):
        box((x, -0.10, 0.47), (0.18, 0.64, 0.44), STEEL_M, taper=0.85)

    axis = Vector((0, math.cos(math.radians(65)), math.sin(math.radians(65))))
    breech = Vector((0, -0.20, 0.49))
    tube(breech - axis * 0.10, breech + axis * 0.20, 0.29, STEEL_D)
    tube(breech, breech + axis * 0.94, 0.255, STEEL_M, bore=True)
    tube(breech + axis * 0.18, breech + axis * 0.43, 0.35,
         SLATE, cap=CHROME)

    # Rear ready-ammunition rack: three separate shells with amber noses.
    # Keeping the rack behind the breech leaves the forward LED unobstructed.
    box((0, -0.535, 0.33), (0.90, 0.27, 0.16), NAVY)
    for x in (-0.29, 0, 0.29):
        tube((x, -0.535, 0.35), (x, -0.535, 0.67), 0.095,
             CHROME, sides=6, cap=AMBER)
        ring = [bm.verts.new((x + 0.095 * math.cos(j * math.tau / 6),
                             -0.535 + 0.095 * math.sin(j * math.tau / 6),
                             0.67)) for j in range(6)]
        tip = bm.verts.new((x, -0.535, 0.83))
        face(ring[::-1], AMBER)
        for j in range(6):
            face([ring[j], ring[(j + 1) % 6], tip], AMBER)

    # Forward identification strip embedded in the sloping deck edge.
    box((0, 0.661, 0.245), (0.76, 0.025, 0.035), CYAN)

    obj = zs.finish_mesh(bm, "SM_Turret_Mortar_LOD0")
    bpy.context.view_layer.update()
    assert zs.triangle_count(obj) <= 400, "Mortar exceeded triangle budget"
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
    path = os.path.join(root, "Assets", "ZeroSynth", "Art", "Models", "SM_Turret_Mortar_LOD0.fbx")
    obj = build_mortar()
    dims = obj.dimensions
    print(f"[ZS] Mortar LOD0: {zs.triangle_count(obj)} tris, {len(obj.data.vertices)} verts, "
          f"{dims.x:.3f} x {dims.y:.3f} x {dims.z:.3f} m (Blender XYZ)")
    zs.export_fbx(obj, path)
    print(f"[ZS] wrote {path}; reuse T_Palette_Synth_BaseColor / _Emission")


if __name__ == "__main__":
    main()
