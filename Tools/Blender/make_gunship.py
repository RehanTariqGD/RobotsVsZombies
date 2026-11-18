"""Synthetic VTOL attack gunship, LOD0 (hard budget: 450 triangles).

    blender.exe -b -P Tools/Blender/make_gunship.py

Faces +Y, Z up, with its pivot at the fuselage centre. All parts are built
into one flat-shaded mesh. UVs use the existing Synthetic palette only;
this script never writes textures. No rotor or animated parts.
"""

import math
import os
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import bmesh
import zs_common as zs
from make_turret import (
    STEEL_D, STEEL_M, CHROME, NAVY, CYAN, AMBER, SLATE,
    SYNTH_PALETTE, SYNTH_EMISSION,
)


def build_gunship():
    zs.reset_scene()
    bm = bmesh.new()

    def face(points, swatch):
        f = bm.faces.new(points)
        zs.paint(bm, [f], swatch)
        return f

    def patch(coords, swatch):
        # Flush overlays are lifted slightly to avoid coplanar flicker.
        return face([bm.verts.new(p) for p in coords], swatch)

    def seg(a, b, wa, wb, swatch, da=None, db=None):
        zs.paint(bm, zs.limb(bm, a, b, wa, wb, da, db), swatch)

    def prism(outline, offset, swatch):
        rings = [[bm.verts.new(tuple(p[i] + offset[i] * t for i in range(3)))
                  for p in outline] for t in (-0.5, 0.5)]
        face(rings[0][::-1], STEEL_D)
        face(rings[1], swatch)
        for j in range(len(outline)):
            k = (j + 1) % len(outline)
            face([rings[0][j], rings[0][k], rings[1][k], rings[1][j]], STEEL_D)

    def tube(a, b, rx, rz, swatch, sides=6, inset=False, start_swatch=NAVY):
        """Y-axis faceted barrel/nacelle; inset adds a recessed cyan end disc."""
        rings = []
        for x, y, z in (a, b):
            rings.append([bm.verts.new((x + rx * math.cos(2 * math.pi * j / sides),
                                       y, z + rz * math.sin(2 * math.pi * j / sides)))
                          for j in range(sides)])
        face(rings[0], start_swatch)
        for j in range(sides):
            k = (j + 1) % sides
            face([rings[0][j], rings[1][j], rings[1][k], rings[0][k]], swatch)
        if inset:
            x, y, z = b
            inner = [bm.verts.new((x + rx * 0.72 * math.cos(2 * math.pi * j / sides),
                                  y + (0.055 if b[1] < a[1] else -0.055),
                                  z + rz * 0.72 * math.sin(2 * math.pi * j / sides)))
                     for j in range(sides)]
            for j in range(sides):
                k = (j + 1) % sides
                face([rings[1][j], inner[j], inner[k], rings[1][k]], NAVY)
            face(inner[::-1], CYAN)
        else:
            face(rings[1][::-1], NAVY)

    # Three bevelled octagonal stations make an angular, low wedge hull.
    stations = [(-2.4, 0.65, 0.30, -0.45),
                (0.2, 1.05, 0.65, -0.60),
                (2.4, 0.33, 0.15, -0.35)]

    def hull_section(y, w, top, bottom):
        return [(-w * 0.65, y, top), (w * 0.65, y, top),
                (w, y, top - 0.18), (w, y, bottom + 0.14),
                (w * 0.65, y, bottom), (-w * 0.65, y, bottom),
                (-w, y, bottom + 0.14), (-w, y, top - 0.18)]

    rings = [[bm.verts.new(p) for p in hull_section(*s)] for s in stations]
    face(rings[0][::-1], STEEL_D)
    face(rings[-1], STEEL_M)
    for a, b in zip(rings, rings[1:]):
        for j, swatch in enumerate((STEEL_D, STEEL_M, SLATE, STEEL_D,
                                    NAVY, STEEL_D, SLATE, STEEL_M)):
            k = (j + 1) % 8
            face([a[j], a[k], b[k], b[j]], swatch)

    # A single cyan glass slit wraps the roof and both bevels of the nose.
    canopy = []
    for y in (0.95, 1.35):
        t = (y - 0.2) / 2.2
        canopy.append(hull_section(y, 1.05 - 0.72 * t, 0.655 - 0.5 * t, -0.6))
    for j, k in ((0, 1), (1, 2), (7, 0)):
        patch([canopy[0][j], canopy[0][k], canopy[1][k], canopy[1][j]], CYAN)

    # Chin cradle is embedded in the belly; two three-barrel gatling clusters
    # project beyond the nose, making the forward direction unambiguous.
    seg((0, 1.05, -0.46), (0, 1.80, -0.65), 0.85, 0.95, STEEL_D, 0.38, 0.40)
    for s in (-1, 1):
        tube((s * 0.32, 1.38, -0.65), (s * 0.32, 1.88, -0.65),
             0.29, 0.29, STEEL_M)
        for j in range(3):
            angle = 2 * math.pi * j / 3
            x = s * (0.32 + 0.145 * math.cos(angle))
            z = -0.65 + 0.145 * math.sin(angle)
            # Mirrored hexagonal barrels keep exact X symmetry.
            tube((x, 1.78, z), (x, 3.0, z), s * 0.085, 0.085, CHROME)
        tube((s * 0.32, 2.70, -0.65), (s * 0.32, 2.80, -0.65),
             0.27, 0.27, STEEL_D)

        # Swept, thick stub wing intersects both hull and tip pod.
        prism([(s * 0.72, 0.35, 0.12), (s * 2.55, -0.60, 0.12),
               (s * 2.55, -1.25, 0.12), (s * 0.72, -0.80, 0.12)],
              (0, 0, 0.24), STEEL_M)

        # Tip VTOL pod: hexagonal housing, recessed upper cyan lift disc,
        # lower cyan exhaust. The wing enters its middle, not its glowing cap.
        pod_rings = []
        for z, r in ((-0.38, 0.50), (0.40, 0.50), (0.35, 0.36)):
            pod_rings.append([bm.verts.new((s * (2.50 + r * math.cos(j * math.pi / 3)),
                                           -0.87 + r * 1.25 * math.sin(j * math.pi / 3), z))
                              for j in range(6)])
        face(pod_rings[0][::-1], CYAN)
        for a, b, colour in ((pod_rings[0], pod_rings[1], STEEL_D),
                              (pod_rings[1], pod_rings[2], CHROME)):
            for j in range(6):
                k = (j + 1) % 6
                face([a[j], a[k], b[k], b[j]], colour)
        face(pod_rings[2], CYAN)

        # Paired rear thrust nacelles, bright inset exhausts facing -Y.
        tube((s * 0.95, -1.12, 0.02), (s * 0.95, -3.0, 0.02),
             0.47, 0.49, SLATE, inset=True, start_swatch=STEEL_D)

        # Swept tail fins penetrate the nacelle roof along their whole base.
        prism([(s * 0.95, -1.45, 0.35), (s * 0.95, -2.67, 1.05),
               (s * 0.95, -2.62, 0.35)], (0.12, 0, 0), STEEL_M)

        # Two amber warning bars per wing; no unique texture required.
        for x in (1.30, 1.62):
            y = -0.30 - (x - 1.30) * 0.40
            patch([(s * x, y + 0.23, 0.245), (s * (x + 0.13), y + 0.16, 0.245),
                   (s * (x + 0.13), y - 0.21, 0.245), (s * x, y - 0.14, 0.245)], AMBER)

        # Navy dorsal heat vents follow the rear roof slope.
        patch([(s * 0.18, -0.65, 0.541), (s * 0.45, -0.65, 0.541),
               (s * 0.39, -1.60, 0.413), (s * 0.18, -1.60, 0.413)], NAVY)

    obj = zs.finish_mesh(bm, "SM_Gunship_LOD0")
    # Open decal faces have no enclosed volume, so the shared normal-recalc
    # helper cannot infer their outside. Explicitly keep these facing upward.
    uv_data = obj.data.uv_layers.active.data
    for poly in obj.data.polygons:
        swatch = round(uv_data[poly.loop_start].uv.x * 8 - 0.5)
        is_patch = (swatch == AMBER or
                    (swatch == CYAN and poly.center.y > 0.9) or
                    (swatch == NAVY and poly.center.z > 0.3))
        if is_patch and poly.normal.z < 0:
            poly.flip()
    obj.data.update()
    bpy.context.view_layer.update()
    assert zs.triangle_count(obj) <= 450, "Gunship exceeded the hard triangle budget"
    assert len([o for o in bpy.context.scene.objects if o.type == "MESH"]) == 1
    # Every exported loop must land at a centre of the original palette strip.
    swatches = [zs.swatch_uv(i) for i in range(len(SYNTH_PALETTE))]
    for loop in obj.data.uv_layers.active.data:
        assert any(abs(loop.uv.x - u) < 1e-6 and abs(loop.uv.y - v) < 1e-6
                   for u, v in swatches)
    return obj


def main():
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    path = os.path.join(root, "Assets", "ZeroSynth", "Art", "Models", "SM_Gunship_LOD0.fbx")
    obj = build_gunship()
    dims = obj.dimensions
    print(f"[ZS] Gunship LOD0: {zs.triangle_count(obj)} tris, {len(obj.data.vertices)} verts, "
          f"{dims.x:.3f} x {dims.y:.3f} x {dims.z:.3f} m (Blender XYZ)")
    zs.export_fbx(obj, path)
    print(f"[ZS] wrote {path}; reuse T_Palette_Synth_BaseColor / _Emission")


if __name__ == "__main__":
    main()
