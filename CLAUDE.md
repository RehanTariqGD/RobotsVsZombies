# ZERO-SYNTH — Working Rules

Rules Claude must follow in this project. Any instruction the user gives about
how work should be done gets added here as a numbered rule, then followed.

## R1 — Do not run or test anything automatically

The user does all testing. Claude does **not**:

- enter or exit play mode
- run builds or deploy to device
- run test suites
- trigger play-mode verification of its own changes

Claude writes the code, compiles it, and reports what changed. Verification is
the user's step. If something needs testing, say what to test and stop.

## R2 — Keep responses minimal

Short answers. No progress narration, no restating what was just done, no
recaps the user did not ask for. State the change and stop. Expand only when
the user asks or when a real risk needs flagging.

Talk like a colleague, not a report. Default to a few sentences. No status
tables, no checklists, no headings unless the user asks for a document. If a
risk needs flagging, one line is usually enough.

## R3 — Record instructions as rules

When the user says what should or should not be done, add it here as a new
numbered rule before acting on it. Rules persist across sessions.

## R4 — Compiling is allowed

Claude may refresh the asset database and trigger compilation to confirm code
builds, and may read the console for compile errors. That is not "testing" —
it is checking the code is valid. Play mode is still off limits (R1).

## R5 — Landscape only, fixed isometric camera

The game ships landscape. Portrait is not supported and no UI or camera work
should assume it. The tactical camera angle is fixed — a free/rotatable camera
breaks the culling and impostor strategy the frame budget depends on.

Player settings to match: `defaultScreenOrientation` auto-rotate with
**landscape left and right only**, both portrait options off.

## R6 — Claude leads; Codex is a worker

Claude owns design, mapping, architecture and integration decisions. Codex is
reached through `mcp__codex_bridge__ask_codex` and is delegated bounded
tasks: art and concept images, Blender scripts, bulk calculation, data and
tooling scripts. Claude reviews everything Codex returns before it goes into
the project. Codex output is never committed unread.

Codex is read-only by default. Writing needs `write=true` inside an approved
git worktree, so anything it produces is saved to a scratch folder and moved
in by Claude.

## R7 — Art follows the GDD budget

Low-poly, faceted, one shared 256x256 palette atlas, one material per unit
family so instancing holds. Triangle targets per the LOD ladder in the
technical review: LOD0 ~400, LOD1 ~150, LOD2 ~60, LOD3 impostor ~12.
Every delegated art task states these numbers. Source `.blend` files stay out
of git (no LFS); commit exported FBX and textures only.

## R8 — Blender is run headless

Blender 5.2 lives at `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`
and is not on PATH. Scripted work uses `blender.exe -b -P script.py`. Scripts
go in `Tools/Blender/` and are committed; they are the reproducible source of
every mesh.

## R9 — Naming conventions

- C#: PascalCase types and methods, `m_Camel` private serialized fields,
  one type per file, file name = type name. Namespace `ZeroSynth.<Area>`.
- Assets are prefixed by type: `SM_` mesh, `T_` texture, `M_` material,
  `PF_` prefab, `SC_` scene, `SO_` ScriptableObject, `UI_` UXML/USS,
  `A_` audio. Meshes carry their LOD: `SM_Runner_LOD0`.
- Scenes: `SC_Boot`, `SC_Loading`, `SC_MainMenu`, `SC_Game`.
- Hierarchy groups are root GameObjects named `--- GROUP ---` at the origin,
  with identity transforms, so children keep their world positions.

## R10 — Project layout

Everything lives under `Assets/ZeroSynth/`: `Art/{Models,Textures,Materials}`,
`Audio`, `Prefabs`, `Scenes`, `UI`, `Settings`, plus the code folders
`Runtime`, `Authoring`, `Game`, `Editor`. Nothing game-owned sits loose in
`Assets/`. Template leftovers are removed, not kept "just in case".

## R11 — Codex runs in parallel, in a worktree

Independent work goes to Codex while Claude works on something else: issue the
`ask_codex` call in the same message as Claude's own tool calls. Code Codex
writes goes in a git worktree (`D:/cw/RobotVsZombies_codex`, the only folder the Codex bridge may write to) and is merged by
Claude after review. Claude defines the API contract first so Codex's code
compiles against real types. Art (images) is saved to a scratch folder and
imported by Claude.

---

## Project facts

- Unity 6000.5.4f1, URP 17.5.0, Android, IL2CPP, ARM64, Vulkan only
- DOTS core packages are editor-versioned **6.5.0**, not the old 1.x line
- Entities Graphics requires URP **Forward+** and Vulkan on Android
- No Git LFS. Keep large binaries out of the repo.
- Assemblies: `ZeroSynth.Runtime`, `ZeroSynth.Authoring`, `ZeroSynth.Editor`
