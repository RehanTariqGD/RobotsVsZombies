# ZERO-SYNTH

Low-poly sci-fi tower defence for Android. A rogue AI defends a power grid
against zombie hordes of thousands, built on Unity DOTS (Entities, Burst,
Entities Graphics) so the swarm stays cheap on low-end phones.

Unity 6000.5.4f1 · URP 17.5 (Forward+) · Android, IL2CPP, ARM64, Vulkan only ·
landscape only. Working rules for contributors, human or AI, are in
[CLAUDE.md](CLAUDE.md).

## Running it

Open the project and **press Play from `SC_Boot`**, not from `SC_Game`.
`SC_Boot` creates `GameFlow`, which owns the menus, pause, win and lose.
Pressing Play in `SC_Game` runs the simulation without any of that.

Scene order: `SC_Boot` → `SC_Loading` → `SC_MainMenu` → `SC_Game`.

## Layout

Everything game-owned is under `Assets/ZeroSynth/`:

| Folder | What |
|---|---|
| `Runtime/` | ECS components and systems (assembly `ZeroSynth.Runtime`) |
| `Authoring/` | MonoBehaviours that create entities, bootstraps (`ZeroSynth.Authoring`) |
| `Game/` | Game flow, build controller, UI controllers (`ZeroSynth.Game`) |
| `Art/{Models,Textures,Materials}` | Generated meshes, palettes, materials |
| `UI/` | UXML, USS, panel settings, UI textures |
| `Scenes/`, `Settings/` | Scenes; render pipeline and input assets |

`Tools/Blender/` holds the scripts that generate every mesh. Source `.blend`
files are never committed.

## Art pipeline

Meshes are generated, not hand-modelled, so each is reproducible and reviewable
as text. Blender 5.2 runs headless:

```
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b -P Tools/Blender/make_lods.py
```

Outputs land in `Tools/Blender/out/` (ignored). Copy the reviewed FBX into
`Assets/ZeroSynth/Art/Models/`. `render_preview.py` renders a mesh from four
angles so it can be judged by eye. Every Swarm unit shares one 64x8 palette
strip and one material; the Synthetic faction has its own with an emission
strip for neon, since Forward+ makes many real lights expensive.

## Architecture in one paragraph

The horde is entities: pooled once at load, released by a wave director, never
created or destroyed at runtime. Pathing is one shared flow field, with walls
as high-cost cells so the swarm funnels at the weakest point. Damage flows
through one queue and one system. UI never touches the ECS world: it reads a
plain `GameSnapshot` from `GameFlow` and calls `GameFlow` / `BuildController`.

## Status

Playable sandbox: waves of four unit types, turrets, walls, power grid, a
possessable turret and mech, a build bar with tactical pause, and a win/lose
loop. Not yet play-tested on a device. Package name and company are still the
Unity template's. No audio, no save.
