# Architecture

- **Moves are packed `int`s**, not objects. A move list is a preallocated `int[]` plus a count. No allocation, no pooling, and every field read is a shift and mask.
- **Pure logic as static methods.** Move encoding, square maths, move generation and attack checks take the board's arrays as parameters. Static methods can be called across classes, so the rules live in one place and every board calls them directly, with no cross-behaviour calls. With no static fields, they hold no state of their own: everything comes in through parameters.
- **Behaviours only for long-lived things**: each board's game state, UI, networking, the PGN importer. There are few of them and they live for the whole session, so the cross-behaviour cost doesn't matter.
- **A position is two arrays**: the `byte[64]` board and an `int[ChessPosition.StateLength]` state array holding the rest of the Java `Board`'s fields (side to move, castling rights, en passant file, king and rook starting files) plus each king's square, which `ChessPosition.MakeMove`/`UnmakeMove` keep up to date for the attack check. The halfmove clock and move number aren't in it (see [Move encoding](move-encoding.md#undo)).
- **Scratch boards** (what `ScratchBoard` does in Java) are a small fixed set of extra piece arrays inside the board script, since legality checking only nests a few levels deep.

Pooled `UdonSharpBehaviour` move objects were considered and rejected: each field read is a cross-behaviour call, and there's no `finally` to guarantee objects are returned. Revisit only if packed ints turn out to be too much of a hassle in practice.

## Logical board and physical board

Each board is split into two behaviours that never reach into each other's domain.

- **`ChessBoard` (logical):** game state, rules, synced fields, ownership, logging. It never touches the world: no transforms, no pickups, no GameObjects.
- **`ChessBoardView` (physical):** the pieces, buttons, highlights and other objects in the world, and the events from them (pickups, drops, button presses). It holds no game state and no synced fields of its own.

**Logical → physical: change notification.**
- Udon has no C# events or delegates, so the board keeps a reference to its listener(s) and calls a method on them when its state changes: `OnBoardChanged()`.
- So the board doesn't depend on the concrete view class, listeners derive from an abstract `ChessBoardListener` base behaviour with virtual methods (UdonSharp supports inheritance with virtual methods; interfaces aren't available). The board holds an array of them, so a move-list display or clock can listen too.
- The view then reads what it needs from the board through public getters: piece on each square, last move, mode (game/replay), result, whose turn, pending requests, which buttons apply.
- A "last change" value (move played, reset, entered replay, loaded game...) lets the view animate a move rather than snapping every piece.
- The same path serves remote updates: `OnDeserialization` updates the board's state, and the board notifies its listeners exactly as for a local change.

**Physical → logical: input forwarding.**
- The view turns world events into calls on the board: `PiecePickedUp(square)`, `PieceDropped(from, to)`, `KingDroppedOnCastleSpace(side)`, and one method per button (resign, offer draw, claim draw, enter/leave replay, step, request takeback, accept/decline, reset, load PGN).
- The board decides everything: whether the local player may act, ownership, legality, castling and promotion handling, replay free moves, activity timestamps. It returns a result, e.g. accepted, rejected, or "promotion choice needed".
- On rejection, the board notifies listeners as usual and the view puts the piece back from the board state. The view never decides a piece's position itself.
- Promotion: when the board answers "promotion choice needed", the view shows the piece picker and calls again with the chosen piece.

**Benefits:** the logical board can be driven entirely by test behaviours with no world objects (see [Testing](testing.md)), and the world presentation can be redesigned without touching the rules.

The same split applies to the instance-wide objects: the archive's history wall and the settings menu are views of `GameArchive` and `ChessArbiter`.

## Instance-wide objects

Besides the boards, the world has two shared objects, each one behaviour with its own synced fields: the settings object [`ChessArbiter`](settings.md) and the game archive [`GameArchive`](archive.md).
