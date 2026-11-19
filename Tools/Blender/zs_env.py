"""Environment palette: reserved, desaturated colours, never gameplay accents."""

BASALT, ROCK_MID, ROCK_LIGHT, DUST, RUST, CONCRETE, METAL, PAINT = range(8)

PALETTE_HEX = (
    "2b2a2e", "4a4640", "6d675c", "8c7a5c",
    "7a4a2e", "8d9096", "23262b", "4e5f63",
)
COLOURS = tuple(tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
                for h in PALETTE_HEX)
