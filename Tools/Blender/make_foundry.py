"""3 x 2 m biomass processor: open intake, furnace mouth, discharge ramp."""
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh
import zs_common as zs
from make_turret import STEEL_D, STEEL_M, CHROME, NAVY, CYAN, AMBER, HAZARD, SLATE
from zs_synth_parts import box, segment, face, prism, chamfer_outline, finish, export


def build_foundry():
    zs.reset_scene()
    bm = bmesh.new()
    prism(bm, chamfer_outline(3, 2, .16), 0, .25, STEEL_D, .95)
    prism(bm, chamfer_outline(1.35, 1.30, .18), .25, 1.91, STEEL_M,
          .94, centre=(.10, 0))
    prism(bm, chamfer_outline(1.48, 1.46, .20), 1.91, 2.12, STEEL_D,
          .89, centre=(.10, 0))
    # Hopper: tapered outer wall, thick lip, sloping inner wall and dark throat.
    rings = []
    for w, d, z in ((.50, .66, .73), (1.0, 1.76, 1.75),
                    (.84, 1.60, 1.75), (.34, .50, .83)):
        rings.append([(-1 + x * w / 2, y * d / 2, z)
                      for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    for a, b, colour in ((0, 1, SLATE), (1, 2, CHROME), (2, 3, STEEL_D), (3, 0, STEEL_D)):
        for i in range(4):
            j = (i + 1) % 4
            face(bm, [rings[a][i], rings[a][j], rings[b][j], rings[b][i]], colour)
    face(bm, rings[3], NAVY)
    box(bm, (-1, 0, .49), (.50, .66, .48), STEEL_D)
    segment(bm, (-.95, 0, .62), (-.35, 0, .62), .44, .44, STEEL_M, .36, .36)
    # Recessed-looking furnace mouth; large dark surround and a smaller hot slit.
    # Front body tapers: plates follow its actual face, rather than float above it.
    def mouth_y(z):
        return .65 * (1 - .06 * (z - .25) / 1.66) + .002
    face(bm, [(-.39, mouth_y(.70), .70), (.59, mouth_y(.70), .70),
              (.59, mouth_y(1.48), 1.48), (-.39, mouth_y(1.48), 1.48)], NAVY, (0, 1, 0))
    face(bm, [(-.28, mouth_y(.88) + .002, .88), (.48, mouth_y(.88) + .002, .88),
              (.48, mouth_y(1.28) + .002, 1.28), (-.28, mouth_y(1.28) + .002, 1.28)],
         AMBER, (0, 1, 0))
    for x in (-.43, .63):
        box(bm, (x, .655, 1.08), (.09, .14, .87), CHROME)
    segment(bm, (.10, .62, .80), (.10, .96, .34), .86, .96, STEEL_D, .09, .09)
    for x in (-.38, .58):
        segment(bm, (x, .62, .83), (x, .96, .39), .055, .055, HAZARD)
    # High chimney at +X; a flared socket makes its connection unambiguous.
    box(bm, (1.06, -.18, .77), (.74, .94, 1.04), SLATE, .80)
    box(bm, (1.06, -.18, 1.915), (.43, .52, 1.67), STEEL_D, .78)
    box(bm, (1.06, -.18, 2.66), (.51, .60, .18), CHROME, .92)
    cap = box(bm, (1.06, -.18, 2.775), (.32, .40, .05), NAVY)
    zs.paint(bm, [cap[1]], AMBER)
    # Roof vent: two broad louvres, deliberately not tiny greebles.
    for x in (-.16, .32):
        box(bm, (x, -.12, 2.16), (.24, .84, .10), SLATE, .85)
    return finish(bm, 'SM_Foundry_LOD0', 400)


if __name__ == '__main__':
    export(build_foundry())
