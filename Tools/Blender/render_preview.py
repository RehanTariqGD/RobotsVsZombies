"""Flat-shaded preview renders of an FBX, from three angles.

    blender.exe -b -P Tools/Blender/render_preview.py -- <fbx> <out_prefix> [palette.png]

Workbench engine, so it is instant and needs no lights. The palette, if given,
is applied so the swatch colours can be judged along with the shape.
"""

import math
import os
import sys

import bpy
import mathutils

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
fbx, prefix = argv[0], argv[1]
palette = argv[2] if len(argv) > 2 else None

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]

if palette:
    img = bpy.data.images.load(palette)
    img.alpha_mode = "NONE"
    mat = bpy.data.materials.new("preview")
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    for o in meshes:
        o.data.materials.clear()
        o.data.materials.append(mat)

# Frame from the combined bounds.
lo = mathutils.Vector((1e9,) * 3)
hi = mathutils.Vector((-1e9,) * 3)
for o in meshes:
    for c in o.bound_box:
        w = o.matrix_world @ mathutils.Vector(c)
        lo = mathutils.Vector(map(min, lo, w))
        hi = mathutils.Vector(map(max, hi, w))
centre = (lo + hi) / 2
radius = max((hi - lo).length / 2, 0.5)

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = scene.render.resolution_y = 512
scene.render.film_transparent = False
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE" if palette else "MATERIAL"
scene.world = bpy.data.worlds.new("w")
scene.world.color = (0.12, 0.13, 0.15)

cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = radius * 2.3
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

views = {  # name: (azimuth degrees, elevation degrees)
    "front": (0, 8),
    "side": (90, 8),
    "three_quarter": (40, 22),
}
for name, (az, el) in views.items():
    a, e = math.radians(az), math.radians(el)
    cam.location = centre + mathutils.Vector((
        math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * radius * 4
    direction = centre - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = f"{prefix}_{name}.png"
    bpy.ops.render.render(write_still=True)
    print(f"[ZS] rendered {scene.render.filepath}")
