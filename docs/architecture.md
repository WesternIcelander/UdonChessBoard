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
- The view then reads what it needs from the board through public getters: piece on each square, last move, mode (game/replay), result, whose turn, pending requests, which buttons apply (the `Can…` checks under [Buttons](player-input.md#buttons)).
- A "last change" value (move played, reset, entered replay, loaded game...) lets the view animate a move rather than snapping every piece.
- The same path serves remote updates: `OnDeserialization` updates the board's state, and the board notifies its listeners exactly as for a local change.

**Physical → logical: input forwarding.**
- The view turns world events into calls on the board: `PiecePickedUp(square)`, `PieceDropped(from, to)`, `KingDroppedOnCastleSpace(side)`, and one method per button (resign, offer draw, claim draw, enter/leave replay, step, request takeback, accept/decline, reset, load PGN).
- The board decides everything: whether the local player may act, ownership, legality, castling and promotion handling, replay free moves, activity timestamps. It returns a result, e.g. accepted, rejected, or "promotion choice needed".
- On rejection, the board notifies listeners as usual and the view puts the piece back from the board state. The view never decides a piece's position itself.
- Promotion: when the board answers "promotion choice needed", the view shows the piece picker and calls again with the chosen piece.

### Implementation

`ChessBoardListener` (`Assets/Siggi/ChessBoard/ChessBoardListener.cs`) is an abstract behaviour with three virtual methods, `OnBoardChanged()`, `OnNotationFilled()` (8.6) and `OnImportChanged()` (8.7). On `ChessBoard`:

- **Listeners:** the `listeners` array, set in the Inspector or changed at run time with `AddListener`/`RemoveListener` (adding one twice changes nothing). Empty entries are skipped.
- **One notification per change:** every public method that changes the board calls each listener's `OnBoardChanged()` once, after the change, having set `lastChange`. A change made of steps (a reset that ends the game on time and logs it first, a move that checkmates) is still one notification. Answers (`Can…`, getters), refusals and accepted pickups tell nobody. `ClearState` and `TakeSnapshot` are steps of other changes, public for tests, and tell nobody either. `Start` tells the listeners once (`ChangeReset`) after clearing, so a view can draw the starting board.
- **`lastChange`** is a `Change*` constant: `ChangeReset`, `ChangeJoined`, `ChangeSettings` (mode, clock, re-snapshot), `ChangeStarted`, `ChangeMove` (also a move that ends the game), `ChangeEnded` (resignation, a draw agreed or claimed, flag fall), `ChangeRequest` (a draw offer or takeback request made or declined), `ChangeEnteredReplay`, `ChangeLeftReplay`, `ChangeReplayStep`, `ChangeFreeMove`, `ChangeTakeback`, `ChangeLoaded`, `ChangeContinued`, `ChangeActivity` (a touch that changed nothing, received from another client only: see [Inactivity](inactivity.md#implementation)); `ChangeNone` before the first.
- **Rejections:** a rejected piece input (`PiecePickedUp`, `PieceDropped`, `PromotionChosen`, `KingDroppedOnCastleSpace`, and their replay forms) tells the listeners once with `ChangeRejected`, so the view puts the piece back. `InputPromotionNeeded` tells nobody: the piece waits while the picker shows, and a cancelled picker calls `PromotionChosen` with `ChessRules.NoPieceType`, which is rejected and puts the pawn back. `ChangeRejected` is local and never sent (step 8.3): a rejection changes nothing.
- **Received changes:** a change arriving from another client tells the listeners the same way, with `lastChangeReceived` set (`false` for a change made on this client), so a view animates a remote move but not one the local player has just dropped. See [Networking](networking.md#implementation).
- **Notation filled in** (8.6): when the board has filled in missing notation by itself, it calls each listener's `OnNotationFilled()` once, from `Update` on the frame after, and only once nothing is missing: a walk over several frames is told at its end, never with its entries half done. Only the move list's text has changed, so `lastChange` stays as it was and `OnBoardChanged()` isn't called. See [Filling in missing notation](networking.md#filling-in-missing-notation).
- **Imports** (8.7): on the importing client only, the board calls each listener's `OnImportChanged()` when a PGN import starts, is loaded, fails or is abandoned (`importStatus`, `importMessage`). A loaded import is also a change (`ChangeLoaded`), told through `OnBoardChanged()` first. See [Imported games](networking.md#imported-games).
- **Getters:** the public state fields (lifecycle, `replayMode`/`replayIndex`, seats, `ending`/`winner`, pending requests, `RemainingTime`) and the `Can…` checks, plus:
  - `PieceAt(square)`: the piece shown, from the replay board in replay and the game's board otherwise.
  - `LastMove()`: the packed move that led to the position shown, for the last-move highlight and animation; 0 for the starting position or a custom replay position.
  - `SideToMove()` and `IsInCheck()`: the game's, whatever replay shows.
  - `LegalMovesFrom(square, moves)`: the game's legal moves from a square (for the legal-target highlights), none unless a game is in progress, not in replay and the side to move's flag hasn't fallen.
  - `ResultText()`: `1-0`, `0-1`, `1/2-1/2` or `*`.
  - Which pieces the local player may grab (`pickupable`, [Physical pieces](physical-pieces.md)) depends on ownership and inactivity, so it comes with steps 8.4/8.5 and 9.2.

**Benefits:** the logical board can be driven entirely by test behaviours with no world objects (see [Testing](testing.md)), and the world presentation can be redesigned without touching the rules.

The same split applies to the instance-wide objects: the archive's history wall and the settings menu are views of `GameArchive` and `ChessArbiter`.

## Instance-wide objects

Besides the boards, the world has two shared objects, each one behaviour with its own synced fields: the settings object [`ChessArbiter`](settings.md) and the game archive [`GameArchive`](archive.md).
