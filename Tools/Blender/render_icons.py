"""Render all build-bar icons, plus a dark-background inspection sheet.

    blender.exe -b -P Tools/Blender/render_icons.py

No external Python packages: Blender's bundled NumPy handles supersampling
and a two-pixel silhouette rim. Existing FBXs and their palette are read-only.
"""

import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, str(Path(__file__).resolve().parent))
from zs_common import reset_scene


ROOT = Path(__file__).resolve().parents[2]
MODELS = ROOT / "Assets/ZeroSynth/Art/Models"
PALETTE = ROOT / "Assets/ZeroSynth/Art/Textures/T_Palette_Synth_BaseColor.png"
OUTPUT = ROOT / "Assets/ZeroSynth/Art/Textures/Icons"
PREVIEW = ROOT / "_preview/icons"
SIZE = 128
SS = 4
MODELS_TO_RENDER = (
    ("Wall", "SM_Wall_LOD0.fbx"),
    ("Railgun", "SM_Turret_Rail_LOD0.fbx"),
    ("Gatling", "SM_Turret_Gatling_LOD0.fbx"),
    ("Mortar", "SM_Turret_Mortar_LOD0.fbx"),
    ("Reactor", "SM_Reactor_LOD0.fbx"),
    ("Pylon", "SM_Pylon_LOD0.fbx"),
    ("Foundry", "SM_Foundry_LOD0.fbx"),
)
# Cartesian azimuth 45 degrees, elevation 35 degrees, identical for every icon.
AZ, EL = math.radians(45), math.radians(35)
VIEW = Vector((math.cos(AZ) * math.cos(EL),
               math.sin(AZ) * math.cos(EL), math.sin(EL)))
CAM_ROT = (-VIEW).to_track_quat("-Z", "Y")


def palette_material(meshes):
    image = bpy.data.images.load(str(PALETTE))
    image.alpha_mode = "NONE"
    material = bpy.data.materials.new("IconPalette")
    material.use_nodes = True
    texture = material.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = image
    texture.interpolation = "Closest"
    material.node_tree.nodes.active = texture
    material.node_tree.links.new(texture.outputs["Color"],
                                 material.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    for obj in meshes:
        obj.data.materials.clear()
        obj.data.materials.append(material)


def amber_material():
    material = bpy.data.materials.new("Amber_f29e1f")
    srgb = (242 / 255, 158 / 255, 31 / 255)
    linear = tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
                   for c in srgb)
    material.diffuse_color = (*linear, 1)
    return material


def repair_glyph():
    material = amber_material()
    # Open-ended wrench: shaft, heel, head bridge and two square jaws.
    parts = (
        ((0, -0.40, 0), (0.34, 1.70, 0.20)),
        ((0, -1.17, 0), (0.43, 0.32, 0.20)),
        ((0, 0.49, 0), (1.06, 0.34, 0.24)),
        ((-0.40, 0.89, 0), (0.28, 0.64, 0.24)),
        ((0.40, 0.89, 0), (0.28, 0.64, 0.24)),
    )
    # Stand the glyph toward the common camera, with enough tilt to show depth.
    rotation = (CAM_ROT.to_matrix().to_4x4()
                @ Matrix.Rotation(math.radians(-35), 4, "Z")
                @ Matrix.Rotation(math.radians(18), 4, "X"))
    for centre, size in parts:
        bpy.ops.mesh.primitive_cube_add(size=1)
        obj = bpy.context.object
        obj.name = "WrenchBox"
        obj.matrix_world = (rotation @ Matrix.Translation(Vector(centre))
                            @ Matrix.Diagonal((*size, 1)))
        obj.data.materials.append(material)


def sell_glyph():
    material = amber_material()
    for index in range(3):
        bpy.ops.mesh.primitive_cylinder_add(
            vertices=48, radius=0.85, depth=0.20,
            location=(0.13 * index, -0.10 * index, 0.10 + 0.24 * index))
        obj = bpy.context.object
        obj.name = "Coin"
        obj.data.materials.append(material)
        bevel = obj.modifiers.new("CoinEdge", "BEVEL")
        bevel.width = 0.035
        bevel.segments = 2


def setup_scene(meshes, textured):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = scene.render.resolution_y = SIZE * SS
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    scene.render.dither_intensity = 0
    scene.display.render_aa = "32"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.studiolight_rotate_z = math.radians(20)
    shading.color_type = "TEXTURE" if textured else "MATERIAL"
    shading.show_shadows = False
    shading.show_specular_highlight = True
    shading.show_cavity = True
    shading.cavity_type = "BOTH"
    shading.curvature_ridge_factor = 1.5
    shading.curvature_valley_factor = 0.6
    shading.cavity_ridge_factor = 1.2
    shading.cavity_valley_factor = 0.5
    shading.show_object_outline = False  # External silhouette only, below.
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.65
    scene.view_settings.gamma = 1.0

    bpy.context.view_layer.update()
    # Project actual mesh bounds into the common camera basis. Recentring in
    # screen space avoids asymmetric barrels shifting the subject off-centre.
    inverse = CAM_ROT.inverted()
    points = [inverse @ (obj.matrix_world @ vertex.co)
              for obj in meshes for vertex in obj.data.vertices]
    lo = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    hi = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    centre = CAM_ROT @ ((lo + hi) / 2)
    span = max(hi.x - lo.x, hi.y - lo.y, 0.001)
    camera_data = bpy.data.cameras.new("IconCamera")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = span / 0.85
    camera_data.clip_end = max(1000, (hi - lo).length * 20)
    camera = bpy.data.objects.new("IconCamera", camera_data)
    scene.collection.objects.link(camera)
    camera.location = centre + VIEW * max(10, (hi - lo).length * 4)
    camera.rotation_euler = CAM_ROT.to_euler()
    scene.camera = camera
    return scene


def save_png(path, pixels):
    height, width, _ = pixels.shape
    image = bpy.data.images.new(path.stem, width, height, alpha=True)
    image.alpha_mode = "STRAIGHT"
    image.pixels.foreach_set(np.ascontiguousarray(pixels, dtype=np.float32).ravel())
    image.filepath_raw = str(path)
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)
    print(f"[ZS] wrote {path}", flush=True)


def finish_icon(raw_path, output_path):
    image = bpy.data.images.load(str(raw_path), check_existing=False)
    size = SIZE * SS
    pixels = np.empty(size * size * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(size, size, 4)
    bpy.data.images.remove(image)
    alpha = pixels[:, :, 3]
    # Circular dilation creates an exact two-output-pixel, light-grey rim.
    radius = 2 * SS
    padded = np.pad(alpha, radius)
    dilated = alpha.copy()
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            if dx * dx + dy * dy <= radius * radius:
                np.maximum(dilated, padded[radius + dy:radius + dy + size,
                                          radius + dx:radius + dx + size], out=dilated)
    rim_alpha = dilated * (1 - alpha)
    combined_alpha = alpha + rim_alpha
    premultiplied = pixels[:, :, :3] * alpha[:, :, None]
    premultiplied += np.array((0.69, 0.74, 0.79)) * rim_alpha[:, :, None]
    # Downsample premultiplied colour to avoid dark fringes on transparent UI.
    rgba = np.dstack((premultiplied, combined_alpha))
    rgba = rgba.reshape(SIZE, SS, SIZE, SS, 4).mean(axis=(1, 3))
    rgba[:, :, :3] /= np.maximum(rgba[:, :, 3:4], 1e-8)
    save_png(output_path, rgba)
    occupied = np.argwhere(rgba[:, :, 3] > 0.05)
    if not len(occupied):
        raise RuntimeError(f"Blank icon: {output_path}")
    low, high = occupied.min(axis=0), occupied.max(axis=0)
    if np.any(low < 2) or np.any(high >= SIZE - 2):
        raise RuntimeError(f"Cropped icon: {output_path}")
    print(f"[ZS] {output_path.stem}: alpha bounds {low.tolist()}..{high.tolist()}")
    return rgba


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    PREVIEW.mkdir(parents=True, exist_ok=True)
    icons = []
    missing = []
    for name, filename in (*MODELS_TO_RENDER, ("Repair", None), ("Sell", None)):
        if filename and not (MODELS / filename).is_file():
            print(f"[ZS] MISSING model: {filename}", flush=True)
            missing.append(filename)
            icons.append(None)
            continue
        reset_scene()
        if filename:
            bpy.ops.import_scene.fbx(filepath=str(MODELS / filename))
        elif name == "Repair":
            repair_glyph()
        else:
            sell_glyph()
        meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
        if not meshes:
            raise RuntimeError(f"No meshes found for {name}")
        if filename:
            palette_material(meshes)
        scene = setup_scene(meshes, bool(filename))
        raw_path = PREVIEW / f"{name}_render.png"
        scene.render.filepath = str(raw_path)
        bpy.ops.render.render(write_still=True)
        print(f"[ZS] wrote {raw_path}", flush=True)
        icons.append(finish_icon(raw_path, OUTPUT / f"T_Icon_{name}.png"))

    # Row-major order: Wall/Railgun/Gatling, Mortar/Reactor/Pylon,
    # Foundry/Repair/Sell. No labels or text are baked into the icons or sheet.
    cell = 160
    sheet = np.ones((cell * 3, cell * 3, 4), dtype=np.float32)
    sheet[:, :, :3] = np.array((20, 24, 31)) / 255
    for index, icon in enumerate(icons):
        if icon is None:
            continue
        x = (index % 3) * cell + 16
        y = (2 - index // 3) * cell + 16  # Blender pixels are bottom-up.
        tile = sheet[y:y + SIZE, x:x + SIZE]
        alpha = icon[:, :, 3:4]
        tile[:, :, :3] = icon[:, :, :3] * alpha + tile[:, :, :3] * (1 - alpha)
    save_png(PREVIEW / "contact.png", sheet)
    if missing:
        raise RuntimeError("Could not render missing models: " + ", ".join(missing))


if __name__ == "__main__":
    main()
