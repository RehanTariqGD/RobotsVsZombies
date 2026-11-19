"""Deterministic low-contrast ground/deck, no normal maps and no dependencies.

Run with plain Python: python Tools/make_ground_textures.py
Pillow is used if installed; otherwise a stdlib/zlib RGB PNG writer is used.
All noise and details wrap on a torus. The duplicated final row/column make
opposite edges byte-identical, and 3x3 previews are written to _preview/.
"""

import json
import math
from pathlib import Path
import random
import struct
import zlib

try:
    from PIL import Image
except ImportError:
    Image = None
try:
    import numpy  # Optional dependency check only; output is backend-independent.
except ImportError:
    numpy = None

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "ZeroSynth" / "Art" / "Textures"
PREVIEW = ROOT / "_preview" / "environment"
SIZE = 256
PERIOD = SIZE - 1
SEED = 7192026


def png(path, pixels, width, height):
    path.parent.mkdir(parents=True, exist_ok=True)
    raw = bytes(v for p in pixels for v in p)
    if Image is not None:
        Image.frombytes("RGB", (width, height), raw).save(path)
        return

    def chunk(tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xffffffff))

    scanlines = b"".join(b"\0" + raw[y * width * 3:(y + 1) * width * 3] for y in range(height))
    path.write_bytes(b"\x89PNG\r\n\x1a\n"
                     + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(scanlines, 9)) + chunk(b"IEND", b""))


def noise_grid(n, seed):
    rng = random.Random(seed)
    return [[rng.uniform(-1, 1) for _ in range(n)] for _ in range(n)]


def noise(grid, x, y):
    n = len(grid)
    u, v = x * n / PERIOD, y * n / PERIOD
    ix, iy = math.floor(u), math.floor(v)
    a, b = u - ix, v - iy
    a, b = a * a * (3 - 2 * a), b * b * (3 - 2 * b)
    p = grid[iy % n][ix % n] * (1 - a) + grid[iy % n][(ix + 1) % n] * a
    q = grid[(iy + 1) % n][ix % n] * (1 - a) + grid[(iy + 1) % n][(ix + 1) % n] * a
    return p * (1 - b) + q * b


def dab(field, x, y, radius, delta):
    """Subpixel antialiased detail, wrapping even when a mark crosses an edge."""
    for iy in range(math.floor(y - radius - 1), math.ceil(y + radius + 1)):
        for ix in range(math.floor(x - radius - 1), math.ceil(x + radius + 1)):
            d = math.hypot(ix - x, iy - y)
            alpha = max(0, min(1, radius + .5 - d))
            field[iy % PERIOD][ix % PERIOD] += delta * alpha


def line(field, a, b, width, delta):
    # Max coverage within a segment avoids over-darkening oversampled pixels.
    ax, ay = a
    bx, by = b
    dx, dy = bx - ax, by - ay
    denom = dx * dx + dy * dy
    for y in range(math.floor(min(ay, by) - width - 1), math.ceil(max(ay, by) + width + 1)):
        for x in range(math.floor(min(ax, bx) - width - 1), math.ceil(max(ax, bx) + width + 1)):
            t = max(0, min(1, ((x - ax) * dx + (y - ay) * dy) / max(denom, 1e-6)))
            distance = math.hypot(x - ax - t * dx, y - ay - t * dy)
            alpha = max(0, min(1, width / 2 + .5 - distance))
            field[y % PERIOD][x % PERIOD] += delta * alpha


def ground():
    layers = [(noise_grid(n, SEED + n), amplitude) for n, amplitude in
              ((3, 3.0), (7, 1.8), (19, .85), (63, .65), (255, 1.0))]
    tint = noise_grid(5, SEED + 18)
    detail = [[0.0] * PERIOD for _ in range(PERIOD)]
    rng = random.Random(SEED)
    # A handful of short, hairline fractures, not bold dried-mud cells.
    for start in ((249, 57), (102, 172), (32, 235)):
        a = start
        angle = rng.uniform(-math.pi, math.pi)
        for step in range(6):
            angle += rng.uniform(-.50, .50)
            length = rng.uniform(3, 6)
            b = (a[0] + math.cos(angle) * length, a[1] + math.sin(angle) * length)
            line(detail, a, b, .52, -3.5)
            if step == 3:
                c = (a[0] + math.cos(angle + 1.1) * 7, a[1] + math.sin(angle + 1.1) * 7)
                line(detail, a, c, .38, -2.5)
            a = b
    for _ in range(45):
        x, y = rng.uniform(0, PERIOD), rng.uniform(0, PERIOD)
        r = rng.uniform(.45, 1.05)
        dab(detail, x + .5, y + .6, r, -2.5)
        dab(detail, x, y, r, rng.uniform(2.2, 4))
    pixels = []
    for y in range(SIZE):
        for x in range(SIZE):
            px, py = x % PERIOD, y % PERIOD
            value = sum(noise(g, px, py) * amp for g, amp in layers) + detail[py][px]
            warm = noise(tint, px, py) * .7
            pixels.append(tuple(round(c + value + t * warm) for c, t in zip((74, 69, 64), (1, .15, -.5))))
    return pixels


def deck():
    broad, grain = noise_grid(6, SEED + 40), noise_grid(255, SEED + 41)
    detail = [[0.0] * PERIOD for _ in range(PERIOD)]
    w, h = PERIOD / 3, PERIOD / 4
    rng = random.Random(SEED + 42)
    variations = [[rng.uniform(-1.8, 1.8) for _ in range(3)] for _ in range(4)]
    # Staggered, clipped-corner armour plates, in a repeatable 3-by-4 layout.
    for row in range(4):
        for col in range(3):
            x = col * w + (w / 2 if row % 2 else 0)
            y = row * h
            bevel, inset = 4, .65
            outline = [(x + bevel, y + inset), (x + w - bevel, y + inset),
                       (x + w - inset, y + bevel), (x + w - inset, y + h - bevel),
                       (x + w - bevel, y + h - inset), (x + bevel, y + h - inset),
                       (x + inset, y + h - bevel), (x + inset, y + bevel)]
            for a, b in zip(outline, outline[1:] + outline[:1]):
                line(detail, a, b, .65, -3.0)
            # Small recessed bolts, kept below a one-pixel-radius at source size.
            for u, v in ((6.5, 6.5), (w - 6.5, 6.5), (6.5, h - 6.5), (w - 6.5, h - 6.5)):
                dab(detail, x + u, y + v, .62, -4.0)
                dab(detail, x + u - .35, y + v - .35, .27, 1.2)
            # Faint inset service score on alternating panels, no emissive marks.
            if (row + col) % 3 == 0:
                line(detail, (x + 17, y + h - 9), (x + w - 17, y + h - 9), .45, -1.2)
    pixels = []
    for y in range(SIZE):
        for x in range(SIZE):
            px, py = x % PERIOD, y % PERIOD
            row = min(int(py / h), 3)
            col = int(((px - (w / 2 if row % 2 else 0)) % PERIOD) / w)
            value = (variations[row][col] + noise(broad, px, py) * .6
                     + noise(grain, px, py) * .65 + detail[py][px])
            pixels.append(tuple(round(c + value) for c in (58, 63, 72)))
    return pixels


def save_and_verify(name, pixels):
    lr = all(pixels[y * SIZE] == pixels[y * SIZE + SIZE - 1] for y in range(SIZE))
    tb = pixels[:SIZE] == pixels[-SIZE:]
    assert lr and tb, name + " is not edge-identical"
    png(OUT / (name + ".png"), pixels, SIZE, SIZE)
    tiled = [pixels[(y % SIZE) * SIZE + x % SIZE] for y in range(SIZE * 3) for x in range(SIZE * 3)]
    png(PREVIEW / (name + "_tiled_3x3.png"), tiled, SIZE * 3, SIZE * 3)
    mean = [round(sum(p[c] for p in pixels) / len(pixels), 3) for c in range(3)]
    mean_hex = "#" + "".join(f"{round(c):02x}" for c in mean)
    ranges = [[min(p[c] for p in pixels), max(p[c] for p in pixels)] for c in range(3)]
    report = dict(name=name, size=[SIZE, SIZE], mean_rgb=mean, mean_hex=mean_hex,
                  channel_ranges=ranges, opposite_edges_equal=lr and tb)
    print(json.dumps(report))
    return report


if __name__ == "__main__":
    print("Pillow:", "available" if Image is not None else "unavailable; using zlib PNG writer",
          "| NumPy:", "available" if numpy is not None else "unavailable (not required)")
    reports = [save_and_verify("T_Ground_BaseColor", ground()),
               save_and_verify("T_BasePad_BaseColor", deck())]
    (PREVIEW / "texture_report.json").write_text(json.dumps(reports, indent=2), encoding="utf-8")
