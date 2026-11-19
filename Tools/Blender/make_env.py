"""All twelve environment meshes. Blender -b -P Tools/Blender/make_env.py.

Metres; +Y forward, +Z up; footprint-centred, grounded origins. No LODs.
Exports to out/; deliberately does not modify Unity metadata or scenes.
Each mesh has one shared environment material and collapsed swatch UVs.
"""

import json
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import bmesh
from mathutils import Vector
import zs_common as zs
from zs_env import BASALT, ROCK_MID, ROCK_LIGHT, DUST, RUST, CONCRETE, METAL, PAINT


def box(bm, centre, size, colour, taper=1.0, tilt=(0, 0, 0)):
    faces = zs.tapered_box(bm, centre, size, taper, tilt)
    zs.paint(bm, faces, colour)
    return faces


def bar(bm, a, b, width, colour, end_width=None):
    zs.paint(bm, zs.limb(bm, a, b, width, width if end_width is None else end_width), colour)


def tip(bm, a, b, width, colour):
    direction = Vector(b) - Vector(a)
    zs.paint(bm, zs.spike(bm, a, direction, direction.length, width), colour)


def face(bm, points, colour):
    layer = bm.faces.layers.int.get("env_overlay") or bm.faces.layers.int.new("env_overlay")
    f = bm.faces.new([bm.verts.new(p) for p in points])
    f[layer] = 1
    zs.paint(bm, [f], colour)


def extrude_profile(bm, profile, depth, colour, axis="Y", offset=0):
    """Extrude an ordered 2D profile (horizontal coordinate, Z), including caps."""
    def point(u, v, z):
        return (u, v, z) if axis == "Y" else (v, u, z)
    rings = [[bm.verts.new(point(u, offset + d, z)) for u, z in profile]
             for d in (-depth / 2, depth / 2)]
    faces = [bm.faces.new(rings[0][::-1]), bm.faces.new(rings[1])]
    for i in range(len(profile)):
        j = (i + 1) % len(profile)
        faces.append(bm.faces.new([rings[0][i], rings[0][j], rings[1][j], rings[1][i]]))
    zs.paint(bm, faces, colour)
    return faces


def ground_and_size(bm, size=None):
    lo = [min(v.co[i] for v in bm.verts) for i in range(3)]
    hi = [max(v.co[i] for v in bm.verts) for i in range(3)]
    for v in bm.verts:
        for i in range(3):
            v.co[i] -= (lo[i] + hi[i]) / 2 if i < 2 else lo[i]
            if size is not None:
                v.co[i] *= size[i] / (hi[i] - lo[i])


def rock(variant):
    bm = bmesh.new()
    rng = random.Random(120 + variant)
    n = 7 if variant == 0 else 8
    rings = []
    # Low buried skirt, broad shoulder, asymmetric crown: not a round sphere.
    for level, (radius, z) in enumerate(((0.85, 0), (1, .36), (.75, .78), (.30, 1.0))):
        ring = []
        for i in range(n):
            a = 2 * math.pi * i / n + .11 * level
            r = radius * rng.uniform(.87, 1.12)
            ring.append(bm.verts.new((math.cos(a) * r + .12 * level,
                                      math.sin(a) * r, z + rng.uniform(-.07, .07))))
        rings.append(ring)
    zs.paint(bm, [bm.faces.new(rings[0][::-1])], BASALT)
    for level in range(3):
        for i in range(n):
            j = (i + 1) % n
            f = bm.faces.new([rings[level][i], rings[level][j], rings[level + 1][j], rings[level + 1][i]])
            choices = [(BASALT, 2), (ROCK_MID, 7), (ROCK_LIGHT, 2)] if level < 2 else [(ROCK_MID, 5), (ROCK_LIGHT, 5), (DUST, 1)]
            zs.paint_mix(bm, [f], choices, 500 + variant * 80 + level * n + i)
    zs.paint(bm, [bm.faces.new(rings[-1])], ROCK_LIGHT)
    zs.jitter(bm, .035, 75 + variant)
    ground_and_size(bm, ((2.2, 1.8, 1.25), (3.0, 2.25, 1.75), (4.0, 2.9, 2.1))[variant])
    return bm


def cliff(variant):
    bm = bmesh.new()
    # Both ends use EXACTLY this cross-section, in the same world coordinates.
    # Their planar X caps mate at 8m spacing, including A/B mixed chains.
    end = [(-1.5, 0), (-1.35, 2.35), (-.82, 3.15),
           (.65, 3.05), (1.35, 2.2), (1.5, 0)]
    peaks = ([3.35, 3.65, 3.12, 3.40, 2.95, 3.50],
             [3.55, 4.15, 3.90, 3.45, 3.83, 3.25])[variant]
    # Broad irregular column crowns, separated by narrow fissures rather than
    # evenly spaced triangular teeth. End stations remain untouched.
    stations = [-4, -3.94, -2.72, -2.57, -1.38, -1.24, -.28,
                -.12, 1.03, 1.17, 2.36, 2.50, 3.94, 4]
    rings = []
    for station, x in enumerate(stations):
        if station in (0, len(stations) - 1):
            section = end
        else:
            column = (station - 1) // 2
            left = station % 2 == 1
            h = peaks[column] - (0 if left else (.12, .25, .09)[column % 3])
            bulge = (1.43, 1.28, 1.47, 1.37, 1.45, 1.32)[column]
            front = -bulge + (0 if left else .12)
            section = [(front, 0), (front + .035, h * .76),
                       (-.82 + column % 3 * .055, h),
                       (.65 - column % 2 * .10, h - .12 - column % 3 * .06),
                       (bulge - .12, h * .73), (bulge, 0)]
        rings.append([bm.verts.new((x, y, z)) for y, z in section])
    zs.paint(bm, [bm.faces.new(rings[0][::-1]), bm.faces.new(rings[-1])], ROCK_MID)
    for s in range(len(rings) - 1):
        for i in range(6):
            j = (i + 1) % 6
            f = bm.faces.new([rings[s][i], rings[s][j], rings[s + 1][j], rings[s + 1][i]])
            # Broad column planes, NOT noisy triangle-by-triangle confetti.
            colour = ROCK_LIGHT if i == 2 else ROCK_MID
            if i in (0, 4) and s % 2 == 0 and s not in (0, len(rings) - 2):
                colour = BASALT
            if i == 5:
                colour = BASALT
            zs.paint(bm, [f], colour)
    return bm


def wall(window=False):
    bm = bmesh.new()
    if not window:
        profile = [(-2, 0), (2, 0), (2, 1.8), (1.68, 2.12), (1.33, 2.04),
                   (.92, 2.60), (.40, 2.42), (.10, 2.86), (-.48, 2.68),
                   (-.80, 2.98), (-1.45, 2.73), (-2, 2.83)]
        faces = extrude_profile(bm, profile, .6, CONCRETE)
        zs.paint(bm, faces[4:7], ROCK_MID)
        tip(bm, (1.28, .10, 2.04), (1.42, .10, 2.58), .025, RUST)
        tip(bm, (-.70, 0, 2.75), (-.57, 0, 3.13), .025, RUST)
    else:
        # Four solids leave a real see-through rectangular window, not a decal.
        extrude_profile(bm, [(-2, 0), (2, 0), (2, .82), (-2, .82)], .6, CONCRETE)
        extrude_profile(bm, [(-2, .82), (-.92, .82), (-.92, 2.22), (-2, 2.22)], .6, CONCRETE)
        extrude_profile(bm, [(.92, .82), (2, .82), (2, 2.22), (.92, 2.22)], .6, CONCRETE)
        faces = extrude_profile(bm, [(-2, 2.22), (2, 2.22), (2, 2.61),
                                    (1.55, 2.87), (1.18, 2.67), (.56, 3.05),
                                    (.18, 2.90), (-.35, 3.18), (-.9, 2.89),
                                    (-1.32, 3.04), (-2, 2.72)], .6, CONCRETE)
        zs.paint(bm, faces[4:7], ROCK_MID)
        tip(bm, (1.6, 0, 2.78), (1.76, 0, 3.14), .025, RUST)
    ground_and_size(bm)
    return bm


def pillar():
    bm = bmesh.new()
    xy = [(-.35, -.5), (.35, -.5), (.5, -.35), (.5, .35),
          (.35, .5), (-.35, .5), (-.5, .35), (-.5, -.35)]
    heights = [2.82, 2.75, 2.96, 3.08, 2.87, 2.96, 2.78, 2.91]
    lower = [bm.verts.new((x, y, 0)) for x, y in xy]
    upper = [bm.verts.new((x, y, z)) for (x, y), z in zip(xy, heights)]
    zs.paint(bm, [bm.faces.new(lower[::-1]), bm.faces.new(upper)], ROCK_MID)
    for i in range(8):
        j = (i + 1) % 8
        zs.paint(bm, [bm.faces.new([lower[i], lower[j], upper[j], upper[i]])], CONCRETE)
    bar(bm, (-.2, 0, 2.78), (-.22, 0, 3.35), .045, RUST)
    tip(bm, (-.22, 0, 3.35), (-.02, .02, 3.50), .022, RUST)
    tip(bm, (.22, .12, 2.85), (.31, .10, 3.42), .023, RUST)
    return bm


def wheel(bm, x, y, z, radius, width):
    profile = [(y + math.cos(i * math.tau / 6) * radius,
                z + math.sin(i * math.tau / 6) * radius) for i in range(6)]
    extrude_profile(bm, profile, width, METAL, axis="X", offset=x)


def truck():
    bm = bmesh.new()
    # +Y is the nose. Both front wheels are gone; a bare axle remains.
    box(bm, (0, 0, .50), (1.85, 5.10, .33), METAL)
    box(bm, (0, -.08, .86), (2.22, 4.85, .42), RUST, taper=.93)
    # Hollow armoured cargo bay: the forward roof has been stripped away,
    # exposing the dark floor; the remaining sheet is visibly buckled.
    for sx in (-1, 1):
        box(bm, (sx * 1.01, -1.175, 1.56), (.14, 2.45, 1.04), PAINT)
    box(bm, (0, -2.33, 1.56), (1.88, .14, 1.04), RUST)
    roof = [(-2.4, 2.10), (-1.45, 1.86), (-.90, 1.96),
            (-.90, 2.04), (-1.45, 1.94), (-2.4, 2.18)]
    extrude_profile(bm, roof, 2.16, PAINT, axis="X")
    cabin = [(.12, 1.0), (1.65, .95), (1.56, 1.36),
             (1.06, 2.12), (.16, 2.12)]
    extrude_profile(bm, cabin, 2.07, RUST, axis="X")
    box(bm, (0, 1.94, 1.04), (2.08, 1.15, .40), PAINT, taper=.86)
    box(bm, (0, 2.62, .65), (2.4, .26, .25), METAL)
    box(bm, (0, -2.65, .64), (2.30, .24, .25), RUST)
    # Two dark windscreen halves follow the slanted cab front.
    for x0, x1 in ((-.88, -.08), (.08, .88)):
        face(bm, [(x0, 1.475, 1.5), (x1, 1.475, 1.5),
                  (x1, 1.14, 2.0), (x0, 1.14, 2.0)], METAL)
    for sx in (-1, 1):
        x = sx * 1.038
        face(bm, [(x, .29, 1.46), (x, 1.34, 1.46),
                  (x, .98, 1.97), (x, .29, 1.97)], METAL)
        wheel(bm, sx * 1.04, -1.62, .40, .46, .32)
        # Subdued rust straps on the cargo body, two triangles each.
        for y in (-1.9, -.48):
            face(bm, [(sx * 1.083, y - .055, 1.10), (sx * 1.083, y + .055, 1.10),
                      (sx * 1.083, y + .055, 1.92), (sx * 1.083, y - .055, 1.92)], RUST)
    bar(bm, (-1.08, 1.65, .43), (1.08, 1.65, .43), .15, METAL)
    # Torn rear hatch, leaning out from the cargo body.
    box(bm, (.35, -2.50, 1.33), (1.12, .10, 1.26), RUST, tilt=(.22, -.13, 0))
    face(bm, [(-.82, 1.48, 1.245), (.74, 1.48, 1.245),
              (.52, 2.25, 1.245), (-.65, 2.25, 1.245)], DUST)
    for sx in (-1, 1):
        face(bm, [(sx * 1.083, -.70, 1.09), (sx * 1.083, -.12, 1.09),
                  (sx * 1.083, -.21, 1.38), (sx * 1.083, -.48, 1.27)], RUST)
    ground_and_size(bm, (2.4, 5.5, 2.2))
    return bm


def crates():
    bm = bmesh.new()
    specs = [((-.64, 0, .575), (1.22, 2.5, 1.15), PAINT),
             ((.64, 0, .575), (1.22, 2.5, 1.15), ROCK_MID),
             ((-.10, .15, 1.775), (1.75, 1.90, 1.25), PAINT)]
    for centre, size, colour in specs:
        box(bm, centre, size, colour)
        x, y, z = centre
        w, d, h = size
        # Flat inset reinforcing straps have no costly hidden box surfaces.
        for fraction in (-.31, .31):
            u = x + w * fraction
            face(bm, [(u - .045, y - d / 2, z + h / 2 + .002),
                      (u + .045, y - d / 2, z + h / 2 + .002),
                      (u + .045, y + d / 2, z + h / 2 + .002),
                      (u - .045, y + d / 2, z + h / 2 + .002)], METAL)
            for sy in (-1, 1):
                v = y + sy * (d / 2 + .002)
                face(bm, [(u - .045, v, z - h / 2), (u + .045, v, z - h / 2),
                          (u + .045, v, z + h / 2), (u - .045, v, z + h / 2)], METAL)
        for sy in (-1, 1):
            v = y + sy * (d / 2 + .003)
            face(bm, [(x - .13, v, z + .01), (x + .13, v, z + .01),
                      (x + .13, v, z + .11), (x - .13, v, z + .11)], RUST)
    ground_and_size(bm, (2.5, 2.5, 2.4))
    return bm


def tree():
    bm = bmesh.new()
    bar(bm, (0, 0, 0), (-.26, .05, 1.6), .52, ROCK_MID, .34)
    bar(bm, (-.26, .05, 1.6), (.13, -.04, 2.75), .34, ROCK_MID, .24)
    bar(bm, (.13, -.04, 2.75), (-.23, .14, 3.94), .24, BASALT, .12)
    tip(bm, (-.23, .14, 3.94), (.03, .12, 4.50), .055, ROCK_MID)
    tip(bm, (-.18, .03, 1.95), (-1.48, .20, 3.0), .115, BASALT)
    tip(bm, (.06, -.03, 2.60), (1.16, .34, 3.53), .10, ROCK_MID)
    tip(bm, (-.14, .1, 3.45), (-.65, -.86, 4.04), .072, DUST)
    ground_and_size(bm)
    height = max(v.co.z for v in bm.verts)
    for v in bm.verts:
        v.co.z *= 4.5 / height
    return bm


def debris():
    bm = bmesh.new()
    box(bm, (-.71, .15, .14), (1.25, .82, .25), ROCK_MID, .88, (0, .09, .18))
    box(bm, (.54, .36, .23), (1.20, .90, .30), ROCK_LIGHT, .76, (.11, -.08, -.24))
    box(bm, (-.07, -.59, .12), (.85, .60, .22), ROCK_MID, .85, (.08, .10, .32))
    box(bm, (1.1, -.4, .10), (.55, .45, .17), BASALT, .70, (0, .06, -.17))
    tip(bm, (-.72, -.3, .17), (.51, -.41, .23), .022, RUST)
    tip(bm, (.1, .77, .22), (.9, .77, .32), .021, RUST)
    # Chip the slabs before adding no further geometry; rebar stays slender.
    for v in list(bm.verts)[:32]:
        v.co.x += math.sin(v.co.y * 17 + v.co.z * 11) * .055
        v.co.y += math.cos(v.co.x * 13 + v.co.z * 7) * .045
    ground_and_size(bm, (3.0, 1.95, .50))
    return bm


def env_material():
    material = bpy.data.materials.new("M_Environment")
    material.use_nodes = True
    texture = material.node_tree.nodes.new("ShaderNodeTexImage")
    path = os.path.join(zs.out_dir(), "T_Palette_Env_BaseColor.png")
    texture.image = bpy.data.images.load(path, check_existing=True)
    texture.interpolation = "Closest"
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    material.node_tree.links.new(texture.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = .94
    return material


if __name__ == "__main__":
    zs.reset_scene()
    material = env_material()
    jobs = [("SM_Rock_A", lambda: rock(0), 70), ("SM_Rock_B", lambda: rock(1), 70),
            ("SM_Rock_C", lambda: rock(2), 70), ("SM_Cliff_A", lambda: cliff(0), 180),
            ("SM_Cliff_B", lambda: cliff(1), 180), ("SM_Ruin_Wall_A", wall, 150),
            ("SM_Ruin_Wall_B", lambda: wall(True), 150), ("SM_Ruin_Pillar", pillar, 60),
            ("SM_Wreck_Truck", truck, 220), ("SM_Crate_Stack", crates, 100),
            ("SM_Tree_Dead", tree, 60), ("SM_Debris_Pile", debris, 60)]
    report = []
    for name, builder, limit in jobs:
        obj = zs.finish_mesh(builder(), name)
        # Recalc cannot infer which side of a disconnected decal is outside.
        # These panels are all on the outward hull/box surfaces; orient them
        # explicitly so Unity's backface culling cannot hide half the details.
        overlays = obj.data.attributes.get("env_overlay")
        if overlays is not None:
            for p in obj.data.polygons:
                if overlays.data[p.index].value and p.normal.dot(p.center) < 0:
                    p.flip()
            obj.data.attributes.remove(overlays)
            obj.data.update()
        obj.data.materials.append(material)
        bpy.context.view_layer.update()
        count = zs.triangle_count(obj)
        assert count <= limit, (name, count, limit)
        assert all(not p.use_smooth for p in obj.data.polygons)
        uv = obj.data.uv_layers.active.data
        for p in obj.data.polygons:
            coords = [uv[i].uv for i in p.loop_indices]
            assert all((c - coords[0]).length < 1e-6 for c in coords)
            assert abs(coords[0].y - .5) < 1e-6
            assert abs(coords[0].x * 8 - .5 - round(coords[0].x * 8 - .5)) < 1e-6
        dims = tuple(round(d, 4) for d in obj.dimensions)
        minimum = tuple(min(v.co[i] for v in obj.data.vertices) for i in range(3))
        maximum = tuple(max(v.co[i] for v in obj.data.vertices) for i in range(3))
        assert abs(minimum[2]) < 1e-5, (name, "not grounded", minimum)
        assert all(abs(minimum[i] + maximum[i]) < 1e-5 for i in (0, 1)), (name, "not centred")
        if name.startswith("SM_Cliff"):
            assert abs(dims[0] - 8) < 1e-5
        print(f"[ZS] {name}: {count}/{limit} tris; {dims[0]:.3f} x {dims[1]:.3f} x {dims[2]:.3f} m (X/Y/Z)")
        zs.export_fbx(obj, os.path.join(zs.out_dir(), name + ".fbx"))
        report.append(dict(name=name, triangles=count, ceiling=limit, dimensions_xyz_m=dims))
    with open(os.path.join(zs.out_dir(), "environment_mesh_report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)
