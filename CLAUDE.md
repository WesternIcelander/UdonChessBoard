# CLAUDE.md

UdonSharp port of the Java ChessBoard library, for VRChat worlds. `PLAN.md` holds the implementation steps; the design is in `docs/`.

## Rules

- **Do not modify anything inside `JavaChessBoard/`.** It is the reference implementation; read it, but never edit, add, or delete files there. Its known bugs are fixed only in the port (see [docs/java-bugs.md](docs/java-bugs.md)).
- **Before working on an implementation step, read the docs listed on that step's "Read:" line in `PLAN.md`.**

## UdonSharp limits

UdonSharp compiles only part of C#. Details and reasons: [docs/udon-constraints.md](docs/udon-constraints.md).

- No custom classes except `UdonSharpBehaviour` subclasses, and no `new` on them (they're components: instantiate or pre-place).
- No interfaces, no generic collections (`List<T>`, `Dictionary<K,V>`): only arrays and `DataList`/`DataDictionary`.
- No static fields, no delegates or lambdas. Static methods are fine and take all state through parameters.
- No `try`/`catch`/`finally`/`throw`: report errors with return codes or `bool` + `out` values.
- Recursion requires `[RecursiveMethod]`.
- No file or stream I/O: FEN and PGN are string in, string out.
- Minimum VRChat SDK is 3.7.1 (needed for `System.Text.StringBuilder`).
- Calls into another behaviour are expensive; keep hot paths on local arrays.

## Code conventions

- Where the port has an equivalent of a Java name (field, option, concept), keep the Java name, e.g. `prohibitSelfCheck`.
- Moves are packed `int`s, not objects (layout: [docs/move-encoding.md](docs/move-encoding.md)). Always mask after shifting: `(move >> shift) & mask`.
- No regex in normal play. Regex is only for parsing PGN and FEN, and the Java patterns need adapting for .NET (atomic groups instead of possessive quantifiers, `^...$` anchors).
- `Square`, `Piece`, `PieceType` etc. are enums or ints, with `-1`/`None` in place of `null`.

## Layers

See [docs/architecture.md](docs/architecture.md).

- **Rules are static methods** that take the board's arrays as parameters and hold no state.
- **`ChessBoard` (logical)** holds game state, rules, synced fields and ownership. It never touches the world: no transforms, pickups or GameObjects.
- **`ChessBoardView` (physical)** owns the world objects and their events, holds no game state or synced fields, and forwards input to the board. It never decides a piece's position itself.
- The board notifies views through `ChessBoardListener` (abstract base behaviour with virtual methods), not events.

## Environment and testing

- Unity only imports `Assets/` and `Packages/`; `JavaChessBoard/` sits alongside without being imported. Java sources are under `JavaChessBoard/src/main/java/io/siggi/chessboard/`.
- Development uses Unity with the VRChat Worlds SDK (which includes UdonSharp). There is no standalone .NET SDK.
- UdonSharp code only runs inside Unity, so tests can't run from the shell under a .NET test runner. Tests are test behaviours run in Unity Play Mode (ClientSim), and networking needs several real clients. See [docs/testing.md](docs/testing.md).
