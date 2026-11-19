"""Small hard-surface primitives for the synthetic infrastructure and walker.

All surfaces use zs_common's collapsed palette UVs. No modifiers or smoothing.
"""
import math
import os

import bpy
import bmesh
from mathutils import Vector
import zs_common as zs
from make_turret import STEEL_D, STEEL_M, CHROME, NAVY, CYAN, AMBER, HAZARD, SLATE


def box(bm, centre, size, colour, taper=1.0):
    faces = zs.tapered_box(bm, centre, size, taper)
    zs.paint(bm, faces, colour)
    return faces


def segment(bm, a, b, wa, wb, colour, da=None, db=None):
    faces = zs.limb(bm, a, b, wa, wb, da, db)
    zs.paint(bm, faces, colour)
    return faces


def face(bm, points, colour, normal=None):
    layer = bm.faces.layers.int.get('zs_facing') or bm.faces.layers.int.new('zs_facing')
    f = bm.faces.new([bm.verts.new(p) for p in points])
    # Open decals have no closed volume from which recalc can infer an outside.
    # Explicit facing is essential for Unity's single-sided palette material.
    f.normal_update()
    if normal is None:
        normal = f.normal.copy()
    elif f.normal.dot(Vector(normal)) < 0:
        f.normal_flip()
    axis = max(range(3), key=lambda i: abs(normal[i]))
    f[layer] = (axis + 1) * (1 if normal[axis] > 0 else -1)
    zs.paint(bm, [f], colour)
    return f


def chamfer_outline(width, depth, cut):
    x, y = width / 2, depth / 2
    return [(-x + cut, -y), (x - cut, -y), (x, -y + cut),
            (x, y - cut), (x - cut, y), (-x + cut, y),
            (-x, y - cut), (-x, -y + cut)]


def polygon_outline(radius, sides, angle=0):
    return [(radius * math.cos(angle + i * math.tau / sides),
             radius * math.sin(angle + i * math.tau / sides)) for i in range(sides)]


def prism(bm, outline, z0, z1, colour, top_scale=1.0, centre=(0, 0)):
    rings = [[bm.verts.new((centre[0] + x * scale, centre[1] + y * scale, z))
              for x, y in outline] for z, scale in ((z0, 1), (z1, top_scale))]
    n = len(outline)
    faces = [bm.faces.new(rings[0][::-1]), bm.faces.new(rings[1])]
    faces += [bm.faces.new([rings[0][i], rings[0][(i + 1) % n],
                           rings[1][(i + 1) % n], rings[1][i]]) for i in range(n)]
    zs.paint(bm, faces, colour)
    return faces


def tube(bm, outline, inner_scale, start, end, colour, axis='Z', centre=(0, 0)):
    """Open tube, with proper inner walls and annular ends; also makes coil rings."""
    def point(x, y, z):
        if axis == 'Y':
            return (centre[0] + x, z, centre[1] + y)
        return (centre[0] + x, centre[1] + y, z)
    rings = [[bm.verts.new(point(x * scale, y * scale, z)) for x, y in outline]
             for z, scale in ((start, 1), (end, 1), (start, inner_scale), (end, inner_scale))]
    groups = []
    for a, b in ((0, 1), (2, 3), (0, 2), (1, 3)):
        fs = [bm.faces.new([rings[a][i], rings[a][(i + 1) % len(outline)],
                           rings[b][(i + 1) % len(outline)], rings[b][i]])
              for i in range(len(outline))]
        zs.paint(bm, fs, colour)
        groups.append(fs)
    return groups


def finish(bm, name, budget):
    obj = zs.finish_mesh(bm, name)
    # zs.finish_mesh recalculates closed solids correctly, but may reverse an
    # isolated sloped decal. Restore its explicit outward direction afterwards.
    facing = obj.data.attributes.get('zs_facing')
    if facing:
        for poly, value in zip(obj.data.polygons, facing.data):
            direction = value.value
            if direction and poly.normal[abs(direction) - 1] * direction < 0:
                poly.flip()
        obj.data.attributes.remove(facing)
        obj.data.update()
    mat = bpy.data.materials.new('Synthetic_Palette')
    mat.use_nodes = True
    palette = os.path.abspath(os.path.join(os.path.dirname(__file__), '../../Assets/ZeroSynth/Art/Textures/T_Palette_Synth_BaseColor.png'))
    tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(palette, check_existing=True)
    tex.interpolation = 'Closest'
    mat.node_tree.links.new(tex.outputs['Color'], mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    obj.data.materials.append(mat)
    bpy.context.view_layer.update()
    tris = zs.triangle_count(obj)
    assert tris <= budget, (name, tris, budget)
    assert abs(min(v.co.z for v in obj.data.vertices)) < 0.00001
    for axis in (0, 1):
        lo = min(v.co[axis] for v in obj.data.vertices)
        hi = max(v.co[axis] for v in obj.data.vertices)
        assert abs(lo + hi) < 0.00001, (name, 'off-centre footprint', axis, lo, hi)
    assert len(obj.data.materials) == 1
    assert all(not p.use_smooth for p in obj.data.polygons)
    uv = obj.data.uv_layers.active.data
    for poly in obj.data.polygons:
        coords = [uv[i].uv for i in poly.loop_indices]
        assert all((c - coords[0]).length < 0.00001 for c in coords)
        assert abs(coords[0].y - 0.5) < 0.00001
        assert abs(coords[0].x * 8 - 0.5 - round(coords[0].x * 8 - 0.5)) < 0.00001
    d = obj.dimensions
    print(f'[ZS] {name}: {tris}/{budget} tris, {d.x:.3f} x {d.y:.3f} x {d.z:.3f} m; one mesh/material, flat, palette UVs verified')
    return obj


def export(obj):
    zs.export_fbx(obj, os.path.join(zs.out_dir(), obj.name + '.fbx'))
