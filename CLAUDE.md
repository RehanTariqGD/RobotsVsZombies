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

---

## Project facts

- Unity 6000.5.4f1, URP 17.5.0, Android, IL2CPP, ARM64, Vulkan only
- DOTS core packages are editor-versioned **6.5.0**, not the old 1.x line
- Entities Graphics requires URP **Forward+** and Vulkan on Android
- No Git LFS. Keep large binaries out of the repo.
- Assemblies: `ZeroSynth.Runtime`, `ZeroSynth.Authoring`, `ZeroSynth.Editor`
