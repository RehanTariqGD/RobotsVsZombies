"""Octagonal 2 x 2 m generator, exposed core and protected cooling crown."""
import math
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh
import zs_common as zs
from make_turret import STEEL_D, STEEL_M, CHROME, NAVY, CYAN, AMBER, HAZARD, SLATE
from zs_synth_parts import box, face, prism, chamfer_outline, polygon_outline, finish, export


def build_reactor():
    zs.reset_scene()
    bm = bmesh.new()
    outline = chamfer_outline(2, 2, .48)
    prism(bm, outline, 0, .30, STEEL_D, .91)
    prism(bm, chamfer_outline(1.78, 1.78, .40), .30, .76, STEEL_M, .92)
    # Broad lower hazard belt subdivides the side faces rather than adding a box.
    belt = chamfer_outline(1.78, 1.78, .40)
    s0 = 1 - .08 * (.54 - .30) / .46 + .002
    s1 = 1 - .08 * (.68 - .30) / .46 + .002
    for i, (x, y) in enumerate(belt):
        nx, ny = belt[(i + 1) % 8]
        face(bm, [(x * s0, y * s0, .54), (nx * s0, ny * s0, .54),
                  (nx * s1, ny * s1, .68), (x * s1, y * s1, .68)],
             HAZARD if i % 2 == 0 else STEEL_D)
    # Dark pressure vessel with a narrow luminous core belt (not a glowing building).
    prism(bm, chamfer_outline(1.24, 1.24, .30), .76, 1.94, NAVY)
    for s in (-1, 1):
        face(bm, [(-.32, s * .621, 1.02), (.32, s * .621, 1.02),
                  (.32, s * .621, 1.65), (-.32, s * .621, 1.65)], CYAN, (0, s, 0))
        face(bm, [(s * .621, -.32, 1.02), (s * .621, .32, 1.02),
                  (s * .621, .32, 1.65), (s * .621, -.32, 1.65)], CYAN, (s, 0, 0))
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(bm, (sx * .60, sy * .60, 1.35), (.26, .26, 1.30), STEEL_M, .85)
    prism(bm, chamfer_outline(1.76, 1.76, .42), 1.94, 2.16, STEEL_M, .86)
    # Small core inspection window keeps the power identity visible from overhead.
    for radius, z, colour in ((.35, 2.161, NAVY), (.25, 2.163, CYAN)):
        face(bm, [(x, y + .22, z) for x, y in polygon_outline(radius, 8)], colour, (0, 0, 1))
    # Paired broad cooling fins at each flank, with visible dark panel gaps.
    for s in (-1, 1):
        for y in (-.29, .29):
            box(bm, (s * .88, y, 1.32), (.16, .30, 1.36), SLATE, .70)
    for x in (-.46, .46):
        stack = box(bm, (x, -.18, 2.38), (.34, .42, .44), STEEL_D, .86)
        zs.paint(bm, [stack[1]], AMBER)
    return finish(bm, 'SM_Reactor_LOD0', 340)


if __name__ == '__main__':
    export(build_reactor())
