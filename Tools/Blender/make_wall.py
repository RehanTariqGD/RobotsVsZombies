"""Joinable 3 x 1 x 1.6 m synthetic barricade; run with Blender -b -P."""
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh
import zs_common as zs
from make_turret import STEEL_D, STEEL_M, CHROME, NAVY, CYAN, AMBER, HAZARD, SLATE
from zs_synth_parts import box, face, prism, chamfer_outline, finish, export


def build_wall():
    zs.reset_scene()
    bm = bmesh.new()
    prism(bm, chamfer_outline(3, 1, .12), 0, .32, STEEL_D, .94)
    box(bm, (0, 0, .91), (2.36, .52, 1.18), STEEL_M)
    # Flush, vertical X ends let neighbouring segments meet cleanly.
    for s in (-1, 1):
        box(bm, (s * 1.32, 0, .90), (.36, .8, 1.16), STEEL_D)
        box(bm, (s * 1.32, 0, 1.51), (.36, .8, .06), CHROME)
        box(bm, (s * 1.32, 0, 1.57), (.18, .32, .06), AMBER)
    # Two roof panels, with a broad dark structural joint visible from above.
    box(bm, (0, 0, 1.43), (2.30, .64, .14), NAVY)
    for s in (-1, 1):
        box(bm, (s * .59, 0, 1.51), (1.06, .60, .10), SLATE, .94)
    # Two genuine chevrons per face, not lots of tiny hazard stripes.
    for side in (-1, 1):
        y = side * .261
        for x in (-.55, .55):
            for s in (-1, 1):
                face(bm, [(x, y, .56), (x + s * .46, y, .92),
                          (x + s * .46, y, 1.22), (x, y, .86)], HAZARD, (0, side, 0))
        face(bm, [(-1.13, y, .39), (1.13, y, .39), (1.13, y, .46), (-1.13, y, .46)],
             CHROME, (0, side, 0))
    return finish(bm, 'SM_Wall_LOD0', 220)


if __name__ == '__main__':
    export(build_wall())
