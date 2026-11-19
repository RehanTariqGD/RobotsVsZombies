"""5 m player walker. +Y cannons, mirrored stance, open bores and leg gap."""
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh
import zs_common as zs
from make_turret import STEEL_D, STEEL_M, CHROME, NAVY, CYAN, AMBER, HAZARD, SLATE
from zs_synth_parts import box, segment, face, prism, tube, chamfer_outline, finish, export


def build_goliath():
    zs.reset_scene()
    bm = bmesh.new()
    for s in (-1, 1):
        x = s * .75
        prism(bm, chamfer_outline(.90, 1.70, .18), 0, .38, STEEL_D,
              .88, centre=(x, .40))
        box(bm, (x, .84, .40), (.68, .62, .16), STEEL_M, .86)
        segment(bm, (x, .02, .37), (s * .81, .15, 1.45), .46, .62, SLATE, .48, .62)
        knee = box(bm, (s * .81, .28, 1.49), (.66, .56, .46), STEEL_M, .85)
        zs.paint(bm, [knee[4]], CHROME)
        segment(bm, (s * .81, .02, 1.60), (s * .57, -.16, 2.65), .52, .66, STEEL_D, .58, .72)
    box(bm, (0, -.12, 2.60), (1.42, .85, .42), SLATE, .82)
    # Broad upper torso and sloped chest, kept clear of the inner leg silhouette.
    prism(bm, chamfer_outline(1.64, 1.22, .24), 2.79, 4.10, STEEL_M, 1.12)
    segment(bm, (0, .64, 3.18), (0, .69, 3.94), 1.06, 1.58, STEEL_D, .18, .20)
    prism(bm, chamfer_outline(1.02, .91, .18), 4.10, 5.0, STEEL_D, .74,
          centre=(0, .05))
    # Cockpit glass is one wide cyan strip, sloping with the helmet's front.
    def visor_y(z):
        return .05 + .455 * (1 - .26 * (z - 4.10) / .90) + .002
    face(bm, [(-.29, visor_y(4.45), 4.45), (.29, visor_y(4.45), 4.45),
              (.25, visor_y(4.72), 4.72), (-.25, visor_y(4.72), 4.72)], CYAN, (0, 1, 0))
    for s in (-1, 1):
        # Missile boxes rest on the torso shoulders and project above the arms.
        pod = box(bm, (s * 1.14, -.06, 4.20), (.86, 1.04, .68), SLATE, .88)
        zs.paint(bm, [pod[4]], NAVY)
        # Four large dark missile ports, with chrome rims, on each forward face.
        for dx in (-.19, .19):
            for z in (4.04, 4.32):
                x = s * 1.14 + dx
                for radius, offset, colour in ((.105, .002, CHROME), (.067, .004, NAVY)):
                    points = [(x - radius, z), (x, z - radius),
                              (x + radius, z), (x, z + radius)]
                    face(bm, [(px, .46 - .12 * .52 * (pz - 3.86) / .68 + offset, pz)
                              for px, pz in points], colour, (0, 1, 0))
        segment(bm, (s * 1.03, -.26, 3.94), (s * 1.35, -.35, 3.18),
                .48, .42, STEEL_D, .54, .48)
        box(bm, (s * 1.35, -.23, 3.15), (.70, .94, .69), STEEL_M, .90)
        # Square-section heavy cannons with thick metal walls and truly open bores.
        groups = tube(bm, [(-.26, -.26), (.26, -.26), (.26, .26), (-.26, .26)],
                      .60, .20, 1.40, CHROME, axis='Y', centre=(s * 1.35, 3.15))
        zs.paint(bm, groups[1], NAVY)
        zs.paint(bm, groups[0], STEEL_D)
        tube(bm, [(-.30, -.30), (.30, -.30), (.30, .30), (-.30, .30)],
             .87, .34, .52, SLATE, axis='Y', centre=(s * 1.35, 3.15))
        # Rear closure is behind the barrel, not across the open muzzle.
        face(bm, [(s * 1.35 - .156, .201, 2.994), (s * 1.35 + .156, .201, 2.994),
                  (s * 1.35 + .156, .201, 3.306), (s * 1.35 - .156, .201, 3.306)], NAVY, (0, 1, 0))
    box(bm, (0, -.66, 3.65), (.68, .32, .74), SLATE)
    box(bm, (0, -1.04, 3.54), (1.02, .72, 1.24), STEEL_D, .90)
    box(bm, (0, -1.04, 4.20), (.86, .60, .16), SLATE, .82)
    for x in (-.23, .23):
        face(bm, [(x - .075, -1.24, 4.281), (x + .075, -1.24, 4.281),
                  (x + .075, -.86, 4.281), (x - .075, -.86, 4.281)], AMBER)
    return finish(bm, 'SM_Goliath_LOD0', 560)


if __name__ == '__main__':
    export(build_goliath())
