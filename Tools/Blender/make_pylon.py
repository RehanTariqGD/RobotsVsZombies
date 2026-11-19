"""Slender 3.6 m relay with a triangular stance and two open coil rings."""
import math
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh
import zs_common as zs
from make_turret import STEEL_D, STEEL_M, CHROME, NAVY, CYAN, AMBER, HAZARD, SLATE
from zs_synth_parts import box, segment, prism, tube, polygon_outline, finish, export


def build_pylon():
    zs.reset_scene()
    bm = bmesh.new()
    # Centre the triangular footprint's bounds on the ground origin.
    outline = [(x, y - .215) for x, y in polygon_outline(.86, 3, math.pi / 2)]
    prism(bm, outline, 0, .18, STEEL_D, .85)
    for x, y in outline:
        segment(bm, (x * .72, y * .72, .16), (x * .16, y * .16, 1.00), .22, .16, STEEL_M)
    box(bm, (0, 0, 1.91), (.23, .23, 2.66), STEEL_D, .72)
    for z in (1.65, 2.28):
        box(bm, (0, 0, z), (1.02, .12, .12), CHROME)
        tube(bm, polygon_outline(.53, 6), .76, z - .07, z + .07, CYAN)
    box(bm, (0, 0, 2.96), (1.24, .18, .16), STEEL_M, .85)
    for s in (-1, 1):
        box(bm, (s * .50, 0, 3.10), (.14, .14, .22), CHROME)
    box(bm, (0, 0, 3.35), (.08, .08, .26), CHROME)
    prism(bm, polygon_outline(.12, 4, math.pi / 4), 3.48, 3.60, CYAN, .50)
    return finish(bm, 'SM_Pylon_LOD0', 240)


if __name__ == '__main__':
    export(build_pylon())
