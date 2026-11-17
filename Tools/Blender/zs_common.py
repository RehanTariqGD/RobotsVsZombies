"""Shared helpers for ZERO-SYNTH Blender scripts.

Run headless:
    blender.exe -b -P Tools/Blender/<script>.py

Palette convention (matches ZS_Sentry_01): an 8-swatch strip, 64x8 pixels,
each swatch 8px wide. Every face is UV-collapsed to the centre of one swatch,
so a whole unit family shares one tiny texture and one material.
"""

import math
import os
import random

import bpy
import bmesh
import mathutils

SWATCHES = 8
PALETTE_W = 64
PALETTE_H = 8


def swatch_uv(index):
    """Centre of swatch `index` on the strip. V is the middle of the row."""
    return ((index + 0.5) / SWATCHES, 0.5)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def tapered_box(bm, centre, size, taper=1.0, tilt=(0.0, 0.0, 0.0)):
    """Add a box whose top face is scaled by `taper`. Returns the new faces.

    Boxes taper because organic limbs and torsos read better as wedges than
    as crates, and it costs no extra triangles.
    """
    sx, sy, sz = size
    hx, hy, hz = sx / 2, sy / 2, sz / 2
    tx, ty = hx * taper, hy * taper
    verts = [
        (-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
        (-tx, -ty, hz), (tx, -ty, hz), (tx, ty, hz), (-tx, ty, hz),
    ]
    rot = mathutils.Euler(tilt, "XYZ").to_matrix().to_4x4()
    new = [bm.verts.new(rot @ mathutils.Vector(v) + mathutils.Vector(centre))
           for v in verts]
    idx = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
           (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return [bm.faces.new([new[i] for i in f]) for f in idx]


def limb(bm, a, b, wa, wb, da=None, db=None):
    """Tapered box from joint `a` to joint `b`.

    Built from endpoints rather than centre-plus-tilt so that chained
    segments (shoulder -> elbow -> hand) share a joint and cannot drift apart.
    `wa`/`wb` are widths at each end; depths default to the widths.
    """
    pa, pb = mathutils.Vector(a), mathutils.Vector(b)
    axis = (pb - pa).normalized()
    up = mathutils.Vector((0, 0, 1)) if abs(axis.z) < 0.9 else mathutils.Vector((0, 1, 0))
    right = axis.cross(up).normalized()
    fwd = right.cross(axis).normalized()
    da = wa if da is None else da
    db = wb if db is None else db

    ring = []
    for p, w, d in ((pa, wa, da), (pb, wb, db)):
        for sr, sf in ((1, 1), (-1, 1), (-1, -1), (1, -1)):
            ring.append(bm.verts.new(p + right * (sr * w / 2) + fwd * (sf * d / 2)))

    idx = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1),
           (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    return [bm.faces.new([ring[i] for i in f]) for f in idx]


def spike(bm, base_centre, direction, length, width):
    """Four-sided pyramid. 6 triangles once triangulated."""
    d = mathutils.Vector(direction).normalized()
    up = mathutils.Vector((0, 0, 1)) if abs(d.z) < 0.9 else mathutils.Vector((1, 0, 0))
    a = d.cross(up).normalized() * width
    b = d.cross(a).normalized() * width
    c = mathutils.Vector(base_centre)
    base = [bm.verts.new(c + a + b), bm.verts.new(c - a + b),
            bm.verts.new(c - a - b), bm.verts.new(c + a - b)]
    tip = bm.verts.new(c + d * length)
    faces = [bm.faces.new(base[::-1])]
    for i in range(4):
        faces.append(bm.faces.new([base[i], base[(i + 1) % 4], tip]))
    return faces


def paint(bm, faces, swatch):
    """Collapse each face's UVs onto one swatch centre."""
    uv_layer = bm.loops.layers.uv.verify()
    u, v = swatch_uv(swatch)
    for f in faces:
        for loop in f.loops:
            loop[uv_layer].uv = (u, v)


def jitter(bm, amount, seed):
    """Deterministic vertex noise so shapes read as irregular, not machined."""
    rng = random.Random(seed)
    for v in bm.verts:
        v.co.x += rng.uniform(-amount, amount)
        v.co.y += rng.uniform(-amount, amount)
        v.co.z += rng.uniform(-amount, amount)


def finish_mesh(bm, name):
    """Triangulate, flip to outward normals, and return a linked object."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def triangle_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def write_palette(path, colours):
    """Write the 64x8 swatch strip. `colours` is 8 sRGB (r, g, b) tuples, 0-1.

    The image is a byte image, so values are written as-is with no colour
    conversion: pass the sRGB colour you want to see.
    """
    img = bpy.data.images.new("palette", PALETTE_W, PALETTE_H, alpha=False)
    px = []
    sw = PALETTE_W // SWATCHES
    for _y in range(PALETTE_H):
        for x in range(PALETTE_W):
            r, g, b = colours[x // sw]
            px.extend((r, g, b, 1.0))
    img.pixels = px
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()


def export_fbx(obj, path):
    """FBX in Unity's convention: metres, Y up, no baked animation."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_scale_options="FBX_SCALE_ALL",
        # Scripts model facing Blender +Y. Forward "Z" lands that on Unity +Z,
        # which is where LookRotation points agents at their heading.
        axis_forward="Z",
        axis_up="Y",
        # Without this the axis settings are only written as metadata, and
        # Unity imports the mesh lying on its side (Blender Z up -> Unity Z).
        bake_space_transform=True,
        bake_anim=False,
        add_leaf_bones=False,
    )


def out_dir():
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(here, "out")
