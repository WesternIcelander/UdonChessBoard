# Overview

## Project layout

`PLAN.md` lives at the root of the Unity project, with the detailed design split into `docs/`:

```
<Unity project>/
  Assets/            UdonSharp scripts, prefabs, scenes
  Packages/
  ProjectSettings/
  JavaChessBoard/    copy of the Java library (reference implementation)
    pom.xml
    src/main/java/io/siggi/chessboard/...
  PLAN.md            implementation steps and index
  docs/              design documents
```

Unity only imports what's under `Assets/` and `Packages/`, so `JavaChessBoard/` sits alongside without being imported. Java paths in this plan (e.g. `MoveFinderPiece.java`) are under `JavaChessBoard/src/main/java/io/siggi/chessboard/`.

## Goals

- A chess library written entirely in UdonSharp, for use in VRChat worlds. Running in VRChat is the primary target, not a later adaptation; there is no separate plain-.NET version.
- The Java library stays as the reference implementation for behaviour (move generation, notation, FEN/PGN), not as code to mirror structurally.
- Where the port has an equivalent of a Java name (fields, options, concepts), keep the Java name, e.g. `prohibitSelfCheck`.
- Main use is live play. PGN import is rare: loading a saved game into replay mode.

## Environment

- Development needs Unity with the VRChat Worlds SDK (installed via the VRChat Creator Companion), which includes UdonSharp. A standalone .NET SDK isn't needed.
- VRChat's planned successor to Udon, **Soba**, would remove most of the Udon constraints. As of August 2026 it had not entered closed beta and has no date, so this plan doesn't rely on it.
