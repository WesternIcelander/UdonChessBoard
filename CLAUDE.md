# CLAUDE.md

UdonSharp port of the Java ChessBoard library, for VRChat worlds. `PLAN.md` holds the implementation steps; the design is in `docs/`.

## Rules

- **Never commit to `main` in `JavaChessBoard/`.** It is the reference implementation and its own git repository (a symlink to the original Java project). Its code may be changed when that makes the port easier (e.g. a helper that prints perft counts or FENs for comparisons), but only on the `port-reference` branch (checked out in place, so the user's original Java project is on that branch too), never committed to `main`, and never pushed unless the user asks. Building and running it with Maven is fine; build artifacts (`target/`) don't matter. Its known bugs are fixed in the port (see [docs/java-bugs.md](docs/java-bugs.md)).
- **Before working on an implementation step, read the docs listed on that step's "Read:" line in `PLAN.md`.**
- **`PLAN.md`'s implementation steps are a checklist.** Work through the items in order. After completing an item and passing its **Verify**, tick it (`- [x]`) in `PLAN.md` before moving on. Never tick an item whose verification hasn't actually been seen to pass.
- **Commit whenever a step is complete**: once its Verify has passed and it's ticked, commit the step's changes (code, tests, `PLAN.md`, docs) in one commit named after it, e.g. "Add game IDs and ContinuedFrom (PLAN 7.6)". Unity-generated asset changes go in their own commit.
- **If the work deviates from the plan, update `PLAN.md` (and the affected `docs/` files) to match** in the same change.
- **Running tests:** when `TestingAutomation.md` exists in the project root, Play Mode tests, uploads and multi-client checks run through the `vrchat-world-unity-automation` Claude Code skill: load it and read that file before running any. Without them, the developer runs Unity and the clients. Test results are read from the Unity Editor log (`%LOCALAPPDATA%\Unity\Editor\Editor.log`), or the skill's log.

## Compatibility with existing worlds

From the first release (PLAN 16.1) on, worlds use the package, and updating must need nothing but importing the new version. Every change follows [docs/compatibility.md](docs/compatibility.md); in short:

- **Edit prefabs in place, never rebuild them:** keep every existing object, add only what's new. Recreating an object loses every world's overrides on it.
- **Never rename, retype or remove a saved field in a way that loses a world's value**: a plain rename uses `[FormerlySerializedAs]` (kept forever); anything more keeps the old field hidden with a run-time fallback to it, or is avoided. There is no editor migrator: updated prefabs carry the rest. A field whose meaning changes is a new field.
- **Keep asset GUIDs:** move or rename assets only inside Unity (or with their `.meta`), never delete and recreate one worlds can reference.
- **New required parts come with a default** (in the prefab, found at run time, or filled in by the wiring script); a world shouldn't have to set anything.
- **Keep the public API:** names, parameters, return codes and constants stay; a changed signature gets a new method and the old one forwards to it.
- **Changed defaults and behaviour that reach every world are deliberate**, and every release has a `CHANGELOG.md` entry saying what changed, what reaches existing worlds, and anything a world must do by hand.
- **Test every release against the previous one** (the compatibility fixture, PLAN 16.1).

## UdonSharp limits

UdonSharp compiles only part of C#. Details and reasons: [docs/udon-constraints.md](docs/udon-constraints.md).

- No custom classes except `UdonSharpBehaviour` subclasses, and no `new` on them (they're components: instantiate or pre-place).
- No interfaces, no generic collections (`List<T>`, `Dictionary<K,V>`): only arrays and `DataList`/`DataDictionary`.
- No static fields, no delegates or lambdas. Static methods are fine and take all state through parameters.
- No `try`/`catch`/`finally`/`throw`: report errors with return codes or `bool` + `out` values.
- Recursion requires `[RecursiveMethod]`.
- No file or stream I/O: FEN and PGN are string in, string out.
- Minimum VRChat SDK is 3.10.4 (needed by the `io.siggi.ustools` dependency; the code itself needs 3.7.1 for `System.Text.StringBuilder`).
- Calls into another behaviour are expensive; keep hot paths on local arrays.
- A synced `string[]` must never contain `null`: one `null` element makes the whole serialization fail. Initialise every element to `""`.

## Code conventions

- Where the port has an equivalent of a Java name (field, option, concept), keep the Java name, e.g. `prohibitSelfCheck`.
- Moves are packed `int`s, not objects (layout: [docs/move-encoding.md](docs/move-encoding.md)). Always mask after shifting: `(move >> shift) & mask`.
- **Public methods that aren't public API start with `_`** (`_UpdateClickables`, `_PressReset`), since UdonSharp needs `public` for any call from another behaviour, a test, a UI event or `SendCustomEvent`. The API is the public methods without one (what step 15 documents); a Java-named internal method keeps its Java name after the underscore. Unity's and Udon's events, `ChessBoardListener`'s virtual methods and `OnLanguageChange` keep their names. A UI event or `SendCustomEvent` naming a renamed method uses the new name (`nameof` where the code already does). Fields and constants keep plain names.
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
- `SimpleAuth` and its built-ins (`FirstJoinerAuth`, `InstanceMasterAuth`, `NoOneAuth`, namespace `SiggiUSTools`) come from the `io.siggi.ustools` package in `Packages/`, fetched by VCC from the VPM listing `https://vpm.siggi.io/index.json`. Don't change anything in it from this project, and it isn't committed here (see [docs/organizers.md](docs/organizers.md)).
- Python is not installed. Don't try to use it for scripts; use PowerShell, Bash, or Java instead.
- UdonSharp code only runs inside Unity, so tests can't run from the shell under a .NET test runner. Tests are test behaviours run in Unity Play Mode (ClientSim), and networking needs several real clients. See [docs/testing.md](docs/testing.md).
