"""Run with Blender -b -P; write the environment strip to out/ and Assets/."""

import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import zs_common as zs
from zs_env import COLOURS


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    filename = "T_Palette_Env_BaseColor.png"
    os.makedirs(zs.out_dir(), exist_ok=True)
    target = os.path.join(root, "Assets", "ZeroSynth", "Art", "Textures")
    os.makedirs(target, exist_ok=True)
    zs.write_palette(os.path.join(zs.out_dir(), filename), COLOURS)
    shutil.copyfile(os.path.join(zs.out_dir(), filename), os.path.join(target, filename))
    print("[ZS] Environment palette: 64 x 8, eight sRGB swatches")
