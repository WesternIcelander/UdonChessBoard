# Architecture

- **Moves are packed `int`s**, not objects. A move list is a preallocated `int[]` plus a count. No allocation, no pooling, and every field read is a shift and mask.
- **Pure logic as static methods.** Move encoding, square maths, move generation and attack checks take the board's arrays as parameters. Static methods can be called across classes, so the rules live in one place and every board calls them directly, with no cross-behaviour calls. With no static fields, they hold no state of their own: everything comes in through parameters.
- **Behaviours only for long-lived things**: each board's game state, UI, networking, the PGN importer. There are few of them and they live for the whole session, so the cross-behaviour cost doesn't matter.
- **A position is two arrays**: the `byte[64]` board and an `int[ChessPosition.StateLength]` state array holding the rest of the Java `Board`'s fields (side to move, castling rights, en passant file, king and rook starting files) plus each king's square, which `ChessPosition.MakeMove`/`UnmakeMove` keep up to date for the attack check. The halfmove clock and move number aren't in it (see [Move encoding](move-encoding.md#undo)).
- **Scratch boards** (what `ScratchBoard` does in Java) are a small fixed set of extra piece arrays inside the board script, since legality checking only nests a few levels deep.

Pooled `UdonSharpBehaviour` move objects were considered and rejected: each field read is a cross-behaviour call, and there's no `finally` to guarantee objects are returned. Revisit only if packed ints turn out to be too much of a hassle in practice.

## Logical board and physical board

Each board is split into behaviours that never reach into each other's domain.

- **`ChessBoard` (logical):** game state, rules, synced fields, ownership, logging. It never touches the world: no transforms, no pickups, no GameObjects.
- **`ChessBoardView` (physical):** the table, the board, the pieces, highlights, animations, the promotion ghosts (the promotion picker before step 12.11) and the player blocker, and the events from them (pickups, drops, clicks). It holds no game state and no synced fields of its own.
- **`ChessBoardUI`** (step 12.1): the button panels, the status display, the PGN box (copying out and Load PGN) and Load it here for the history wall's pick, and their button presses. Like the view, a listener holding no game state and no synced fields. The view and the UI don't know each other: each listens to the board and acts through it, so either can be replaced or left out.
- **`ChessBoard2DView`** (step 12.13, [2D board view](board-2d.md)): a flat board showing one of several boards at a time. It listens only to the board it shows, moving its listener (`RemoveListener`, `AddListener`) as its selector steps, and acts on no board. Its holder, `ChessBoard2DViewHolder` (step 12.16), moves and resizes it from two pickups; the view knows nothing of it.

**Logical → physical: change notification.**
- Udon has no C# events or delegates, so the board keeps a reference to its listener(s) and calls a method on them when its state changes: `OnBoardChanged()`.
- So the board doesn't depend on the concrete view class, listeners derive from an abstract `ChessBoardListener` base behaviour with virtual methods (UdonSharp supports inheritance with virtual methods; interfaces aren't available). The board holds an array of them, so a move-list display or clock can listen too.
- The view then reads what it needs from the board through public getters: piece on each square, last move, mode (game/replay), result, whose turn, pending requests, which buttons apply (the `Can…` checks under [Buttons](player-input.md#buttons)).
- A "last change" value (move played, reset, entered replay, loaded game...) lets the view animate a move rather than snapping every piece.
- The same path serves remote updates: `OnDeserialization` updates the board's state, and the board notifies its listeners exactly as for a local change.

**Physical → logical: input forwarding.**
- The view turns world events into calls on the board: `PiecePickedUp(square)`, `PieceDropped(from, to)`, and one method per button (resign, offer draw, claim draw, enter/leave replay, step, request takeback, accept/decline, reset, load PGN).
- The board decides everything: whether the local player may act, ownership, legality, castling and promotion handling, replay free moves, activity timestamps. It returns a result, e.g. accepted, rejected, or "promotion choice needed".
- On rejection, the board notifies listeners as usual and the view puts the piece back from the board state. The view never decides a piece's position itself.
- Promotion: when the board answers "promotion choice needed", the view shows the pieces to choose from (ghost pieces over the pawn since step 12.11) and calls again with the chosen piece.

### Implementation

**The UI's place** (decided in 12.1): `ChessBoardUI` has no table to measure, so it is placed relative to its own origin. **Create Board View** builds it on an object of its own beside the view (`ChessBoardUI (<board>)`, the view's next sibling), standing where the view does: at the board's centre with the view's axes (files along +x, White's end at −z). It places the panels and the PGN box around that origin as before, and the UI reads only its own transform (`LocalEnd`, `PlacePgnBox`), so it keeps working wherever it's put as long as its origin stays at the board's centre. Rebuilding replaces the UI where it stands. Reference transforms set by the builder were the other option; they'd add fields for what the origin already gives.

`ChessBoardListener` (`Assets/Siggi/ChessBoard/ChessBoardListener.cs`) is an abstract behaviour with three virtual methods, `OnBoardChanged()`, `OnNotationFilled()` (8.6) and `OnImportChanged()` (8.7). On `ChessBoard`:

- **Listeners:** the `listeners` array, set in the Inspector or changed at run time with `AddListener`/`RemoveListener` (adding one twice changes nothing). Empty entries are skipped.
- **One notification per change:** every public method that changes the board calls each listener's `OnBoardChanged()` once, after the change, having set `lastChange`. A change made of steps (a reset that ends the game on time and logs it first, a move that checkmates) is still one notification. Answers (`Can…`, getters), refusals and accepted pickups tell nobody. `ClearState` and `TakeSnapshot` are steps of other changes, public for tests, and tell nobody either. `Start` tells the listeners once (`ChangeReset`) after clearing, so a view can draw the starting board.
- **`lastChange`** is a `Change*` constant: `ChangeReset`, `ChangeJoined`, `ChangeSettings` (mode, clock, re-snapshot), `ChangeStarted`, `ChangeMove` (also a move that ends the game), `ChangeEnded` (resignation, a draw agreed or claimed, flag fall), `ChangeRequest` (a draw offer or takeback request made or declined), `ChangeEnteredReplay`, `ChangeLeftReplay`, `ChangeReplayStep`, `ChangeFreeMove`, `ChangeTakeback`, `ChangeLoaded`, `ChangeContinued`, `ChangeActivity` (a touch that changed nothing, received from another client only: see [Inactivity](inactivity.md#implementation)), `ChangeLeft` (a seat left before a game started, step 13.1, also when that resets an emptied lobby); `ChangeNone` before the first.
- **Rejections:** a rejected piece input (`PiecePickedUp`, `PieceDropped`, `PromotionChosen`, and their replay forms) tells the listeners once with `ChangeRejected`, so the view puts the piece back. `InputPromotionNeeded` tells nobody: the pawn waits while the promotion ghosts show (step 12.11). There's no cancel since then: the view refuses a choice that isn't a promotion piece without asking the board (`PromotionChosen` with any other type is still rejected, putting the pawn back). `ChangeRejected` is local and never sent (step 8.3): a rejection changes nothing.
- **Received changes:** a change arriving from another client tells the listeners the same way, with `lastChangeReceived` set (`false` for a change made on this client), so a view animates a remote move but not one the local player has just dropped. See [Networking](networking.md#implementation).
- **Notation filled in** (8.6): when the board has filled in missing notation by itself, it calls each listener's `OnNotationFilled()` once, from `Update` on the frame after, and only once nothing is missing: a walk over several frames is told at its end, never with its entries half done. Only the move list's text has changed, so `lastChange` stays as it was and `OnBoardChanged()` isn't called. See [Filling in missing notation](networking.md#filling-in-missing-notation).
- **Imports** (8.7): on the importing client only, the board calls each listener's `OnImportChanged()` when a PGN import starts, is loaded, fails or is abandoned (`importStatus`, `importMessage`). A loaded import is also a change (`ChangeLoaded`), told through `OnBoardChanged()` first. See [Imported games](networking.md#imported-games).
- **Getters:** the public state fields (lifecycle, `replayMode`/`replayIndex`, seats, `ending`/`winner`, pending requests, `RemainingTime`) and the `Can…` checks, plus:
  - `PieceAt(square)`: the piece shown, from the replay board in replay and the game's board otherwise.
  - `LastMove()`: the packed move that led to the position shown, for the last-move highlight and animation; 0 for the starting position or a custom replay position.
  - `SideToMove()` and `IsInCheck()`: the game's, whatever replay shows.
  - `ClocksFromPreviousGame()`: the game plays on untimed after a flag fall (step 12.9), so `RemainingTime` gives the timed game's times, for the UI's `GG`.
  - `Checks(checkers, kings)`: the checks in the position shown, for the check lines (step 12.7); `FallenKingSquare()`: the king checkmated, or the resigning side's, in the position shown, for the falling king (step 12.8).
  - `LegalMovesFrom(square, moves)`: the game's legal moves from a square (for the legal-target highlights), none unless a game is in progress, not in replay and the side to move's flag hasn't fallen.
  - `ResultText()`: `1-0`, `0-1`, `1/2-1/2` or `*`.
  - `MayTouchPieces()` (and `MayTouchPiecesAs`, `PlayerMayTouchPieces`): whether the local player may touch the pieces, for `pickupable` and the pieces' ownership check (step 9.2, [Physical pieces](physical-pieces.md#pickups-step-92)).
  - `CanPickUp(square)` (and `CanPickUpAs(name, organizer, square)`): whether `PiecePickedUp` would accept the pickup, without touching the board or telling anyone, for desktop mode's clickable pieces (step 9.3c, [Desktop mode](player-input.md#desktop-mode-step-93c)).

**Benefits:** the logical board can be driven entirely by test behaviours with no world objects (see [Testing](testing.md)), and the world presentation can be redesigned without touching the rules.

The same split applies to the instance-wide objects: the archive's history wall and the settings menu are views of `GameArchive` and `ChessArbiter`.

## Shared helpers

Code that several behaviours, builders or tests need is written once (steps 12.18 and 12.19):

- **`ChessUtil`** (`Assets/Siggi/ChessBoard/ChessUtil.cs`): static methods for the behaviours, on an abstract behaviour like `ChessPgn` (never placed, so no program asset). `SetText` (set a label only when its text changed), `TwoDigits`, and the sound effects' shared part: `SoundAllowed(board, soundsFrom, caughtUp, out caughtUp)`, the behaviour handing in its own state and taking back its `caughtUp` flag through the `out` parameter, and `PlaySound(clip, audioSource, muted)`, which plays the clip and says whether there was one, the view and the UI each keeping a two-line `PlaySound` that counts it (`lastSound`, `soundsPlayed`). `NoteSound(change, makesSound, board, soundsFrom, ref caughtUp, ref soundChanges, ref soundChange)` (12.19) notes a change's sound for the next `Refresh`, each behaviour keeping its own list of the changes that make one (`MakesSound`). Also `AddLine` (a status line added under the others, none for an empty line), `LocalName(fallback)` and `OwnerName(target, fallback)` (a display name, the fallback when there's no player: `""`, `"?"` or `"nobody"`, as each caller had it). The view and the UI still don't know each other: each calls the static methods with its own fields.
- **`ChessBuilderUtil`** (`Assets/Siggi/ChessBoard/Editor/ChessBuilderUtil.cs`): plain editor C# for the **Tools > Chess Board** builders and the debug view's builder, imported with `using static ChessBuilderUtil` by them and the test builders: `AddTriggerCollider` (a canvas's trigger box), `EnsureProgramAssets(menuItem, script, program, …)` (creates the missing program assets and asks for the menu item to be run again), `FindBoard(menuItem, what)` (the selected board or the scene's only one), `ExistingFor<T>(board, boardOf)` (the object of a view built earlier for a board), `NewChild` (a top-left-anchored rect), `AddStretched` and `AddAnchored` (a rect filling its parent or between anchors), `AddText` (every text: font, size, alignment, colour, wrapping, cut off at its rect, not taking the pointer; the PGN fields' text sets overflow and the pointer back for its field), `NewText` (a white left-aligned text at a top-left position) and `DefaultFont` (TMP's default font, step 12.17).
- **`ChessTest`** (`Assets/Tests/ChessTest.cs`, 12.19): the test behaviours' abstract base (a `ChessBoardListener`, as `BoardTest` listens), with each test's `passed` and `failed` counts, `Check`, `CheckInt`, `CheckString`, `LogSummary` (which sets `finished`, for the tests that wait), `ReadyAfter` (the waiting tests' check that the local player has joined and the tests before have finished) and `NotationText`. Each test names itself for its log lines (`TestName`). See [Testing](testing.md#test-behaviours).

## Instance-wide objects

Besides the boards, the world has two shared objects, the settings object [`ChessArbiter`](settings.md), one behaviour with its own synced fields, and the game archive [`GameArchive`](archive.md), a controller with 10 synced holders of games.

## More boards

**Tools > Chess Board > Create Board** (`Editor/ChessBoardBuilder.cs`, step 10.1b) adds another `ChessBoard` 2.5 m along x from the last one: the next free `boardNumber` (warning when it's beyond the arbiter's `boardCount`), the scene's arbiter and archive, and a `ChessPgnImporter` of its own as a child. Its view and debug view are then built with the new board selected (**Create Board View**, **Create Debug View**). The test builders that want the scene's only board view (**Create View Test**) refuse once there are two, so the tests stay on the first board.
