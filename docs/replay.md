# Replay mode

Players can enter replay mode at any time to review the game.

- **Replay is always shared.** The board is one physical object: when it's in replay mode, everyone sees the replay. During a live game this pauses play, since no game moves can be made until the board leaves replay, but the clock keeps running (see [Clocks](clocks.md)).
- Entering replay changes synced state, so it needs ownership. While both players are active, only they (or an organizer) can do that (see [Ownership](ownership.md)), so a bystander can't hijack a live game's board.
- **Free manipulation:** in replay mode players can step through the game, play moves, or move pieces anywhere, including illegal moves.
- **Separate state:** replay never touches the game state. It works on a separate replay board (its own 64-square array) plus the ply it started from, so leaving replay always returns to the game exactly as it was.
- **Stepping** through the game uses the move history: undoing and redoing packed moves gives any earlier position, without the starting FEN.
- **Free moves** are plain piece placement on the replay board: no move generation, no packed moves, no legality checks. The replay board is just an array.
- **Custom flag:** any free move or deviation from the game's moves marks the replay position as *custom*. Stepping back onto a position from the game history clears the flag.
- **The 2D view's replay is separate** (step 12.14): the 2D board view steps through a board's game by itself, on its own scratch position, without touching the board, and the board's replay doesn't move it. See [2D board view](board-2d.md#its-own-replay-step-1214).
- **Captured pieces** (step 12.4): the physical board shows the captured pieces of the position shown: stepping back returns the pieces taken since to the board, and free moves add the pieces they take after the places of the ply they started from, in a fixed order (queen, rooks, bishops, knights, pawns, king). See [Physical pieces](physical-pieces.md#captured-piece-places-step-124).

## Continuing from an earlier position (takeback)

- From a replay position that is part of the game history, players can continue the game from there. The move history is truncated to that ply, and the game resumes with that side to move.
- Not allowed from a custom position.
- **Both players must agree.** One player requests continuing from the shown position; the other accepts or declines. The pending request (requesting player and ply) is synced state. A request is cancelled if the replay position changes before it's accepted.
- **Works whether or not the game has ended.**
  - If the game had not ended, it is first logged as an incomplete game (PGN result `*`), with its full move history before truncation.
  - If the game had ended, it was already logged at its end; nothing more is logged at this point.
  - Either way, the continued game is logged in full when it ends.
- **Linking games:** every game gets a unique ID when it starts (see [Game IDs](game-ids.md)). A game continued from replay gets a new ID, and its PGN references the game it came from in custom tags.
- **Instance setting:** whether takeback is allowed at all is configurable by organizers, so it can be turned off for tournaments or other events. It lives on the settings object (see [Settings](settings.md)).

## Continuing a loaded game

A loaded game is one imported from a PGN or loaded from the archive. The board is then in its *loaded* state (see [Board lifecycle](board-lifecycle.md)), showing the game in replay mode. There's no live game behind the replay, so the board can't leave replay; resetting it clears the loaded game.

- Continuing is allowed from any position in the loaded game's history, as with any other game; not from a custom position.
- **Seats:** players join with Join White / Join Black while the game is shown. The players seated at the board play on, whatever the loaded game's `White`/`Black` say. Names change, and someone may want to play on from another person's game.
- **Clock:** chosen as in the lobby (or forced by the settings). The continued game starts with fresh clocks for both sides; the loaded game's clock history isn't carried over.
- **Mode:** comes from the loaded game, not from the lobby setting or a forced mode. For an imported PGN it's read from the `Variant` tag (`Chess960`, otherwise Standard).
- **"Continue from here"** takes the place of Start: once both seats are filled, any seated player can press it on a game-history position. Unlike a takeback, no request and accept is needed, since nobody is playing the loaded game. It truncates the history at the shown ply, gives the game a new ID with `ContinuedFrom` (when the loaded game has a Game ID), starts the clocks and enters the in-game state with that side to move.
- **What import keeps:** the game history (starting FEN, if one was given, the moves, and the clock history from `[%clk]` comments), the mode from `Variant`, the time control from `TimeControl`, plus the `White`, `Black` and `Result` tags, which are shown in the UI during replay. Nothing else from the PGN is stored (see [PGN import](pgn.md#import-local-rare)).
- **On continuing**, the game history up to that point (including the starting FEN) is kept, and the loaded `White`, `Black` and `Result` are dropped.
- Every PGN the board writes is built from scratch from the board's own state: current players, date, result, and the `FEN` tag when the game didn't start from the standard position.

## Implementation

On `ChessBoard` (`Assets/Siggi/ChessBoard/ChessBoard.cs`), each method refusing when its `Can…` check doesn't hold ([Buttons](player-input.md#buttons)), with a local-player form and an `*As(name, organizer, …)` form:

- `EnterReplay`/`EnterReplayAs`: replay on, showing the current position (`replayIndex` = `moveCount`).
- `StepTo(ply)`/`StepToAs`: shows the position before move `ply` (0 is the start, `moveCount` the current position). The replay board is rebuilt each time by copying the game position and undoing the moves after `ply` (`ChessPosition.UnmakeMove`), on a scratch state, since the synced replay board is only the 64 squares. Stepping clears the custom flag, and stepping to the shown ply is allowed only from a custom position, which brings the game's position back. There are no separate back and forward methods: the view's step buttons (step 9.4b) call `StepTo(0)`, `StepTo(replayIndex - 1)`, `StepTo(replayIndex + 1)` and `StepTo(moveCount)`.
- `LeaveReplay`/`LeaveReplayAs`: replay off. The game fields were never touched, and the replay fields are put back on the current position, as on entering.
- **Free moves:** in replay, `PiecePickedUp`/`PieceDropped` (and their `*As` forms, which don't count the name as an organizer) go to `ReplayPiecePickedUpAs(name, organizer, square)`/`ReplayPieceDroppedAs(name, organizer, from, to)`. They're open to whoever may work the replay controls, for a piece of either colour. A drop puts the piece on the target square and replaces whatever stands there (a king too). There's no promotion, castling or legality, and every free move marks the position custom, even one that matches the game's next move. A drop back on its own square, or from an empty square, is refused. `PromotionChosen` and `KingDroppedOnCastleSpace` are refused in replay. Taking a piece off the board isn't a free move: the view puts a piece dropped off the board back.
- The clock keeps running and the lifecycle is unchanged. A game that ends while in replay stays in replay.
- **Takebacks:** `RequestTakeback(color)`/`RequestTakebackAs(color, name)` records the request (`takebackColor`, and `takebackPly` = `replayIndex`). `DeclineTakeback`/`DeclineTakebackAs` clears it. Any change of the replay position cancels it: stepping and leaving (both through `ShowPly`) and a free move.
- `AcceptTakeback(color)`/`AcceptTakebackAs(color, name)` continues from `takebackPly`, in this order:
  1. A game still in progress is logged as unfinished (`LogGame(EndingUnterminated, …)`, `*`) with its whole history and its own ID. An ended game was logged when it ended, so nothing is logged.
  2. `ContinueGameIdAt(ply, …)` gives the new ID and `ContinuedFrom`.
  3. The history is cut: the moves after the ply are undone on the game position (move number and halfmove clock too), and their entries are emptied (move 0, notation `""`, clock `-1`).
  4. The clocks are restored: each side's time is the clock history entry of its own last move before the ply, or the base time if it hadn't moved (also for an entry of `-1`). The turn starts now. A flag that had fallen is up again. A game playing on untimed after a flag fall (12.9) stays untimed, even back before the flag fell, and keeps the timed game's times.
  5. The game is in progress again with no result. The draw offer and the request are cleared, and the board leaves replay with the replay fields on the new current position.
- **Loading a game:** `LoadGame(fen, moves, sans, clocks, count, mode, white, black, result, gameId, baseTime, increment)` takes a game's data from any source (the archive in step 10). `LoadImport(importer)` takes a finished import (`ChessPgnImporter.result` = `ImportDone`) and passes on its starting position, history, `Variant` mode, `White`/`Black`/`Result`/`GameID` tags and `TimeControl`. Both are refused unless `CanLoadGame()` (a reset board), and for invalid data (a bad FEN, a count beyond the arrays, an unknown mode, a negative increment or a base time that isn't `NoClock`, `UnknownClock` or at least 1 s), which leaves the board reset.
  - The moves are played through on the game position (they must be the game's legal moves, as an import or the archive has them) to rebuild the halfmove clocks and the move number. The SAN and clocks are copied (`""` and `-1` where unknown).
  - The board enters `LifecycleLoaded` in replay, showing the last position (`replayIndex` = `moveCount`), with the tags in `replaySourceWhite`/`Black`/`Result` and the ID in `gameId` (no `ContinuedFrom`).
  - **Result:** a checkmate on the board is `EndingCheckmate` (the side not to move wins) and a stalemate `EndingStalemate`, whatever the tag says. Otherwise a `Result` of `1-0`, `0-1` or `1/2-1/2` gives `ChessGame.EndingUnknown` (no `Termination`) with that winner, and `*` or no tag gives no result.
  - **Time control:** the game's own is kept in `replaySourceBaseTime`/`replaySourceIncrement`: seconds, `NoClock` for `"-"`, `ChessBoard.UnknownClock` (-2) for a missing or unreadable `TimeControl`. The clock for continuing (`baseTime`/`increment`) starts as the game's own when a player could choose it (`IsValidTimeControl`), no clock otherwise. The players can change it (`SetClockAs`), and a forced clock replaces it.
  - The settings snapshot is taken last, once the board is in the loaded state, so it applies a forced clock but not a forced mode.
  - `GamePgn()` on a loaded board writes the loaded game: its own White, Black and time control (`?` when unknown), not the seats or the clock chosen for continuing.
- **Continuing a loaded game:** players join with `JoinAs` (the board stays loaded) and choose the clock with `SetClockAs`. `ContinueGame()`/`ContinueGameAs(name)` is "Continue from here", refused unless `CanContinueAs(name)`: a loaded board in replay on a game position (not custom; the last one too), both seats filled, `name` seated, and the position not over already. A position is over when it has no legal moves (a loaded game won by checkmate, or a stalemate, ends on one) or one of the automatic draws the snapshot has on applies to it (fivefold repetition counted over the history before it, the seventy-five-move rule on its halfmove clock, insufficient material), since the game would be over before anyone moved. Continuing from the shown ply, in this order:
  1. Nothing is logged: the loaded game wasn't played on this board.
  2. `ContinueGameIdAt(ply, …)` gives the new ID, with `ContinuedFrom` `""` when the loaded game had no ID.
  3. The history is cut at the ply as for a takeback, and the clock history entries before the ply are emptied (`-1`): the loaded game's clocks aren't this game's, so they're neither exported with it nor restored by a takeback. A takeback may go back past the continuation point, like any other ply of the game's history; there both sides get the base time, as they had when the game was continued.
  4. Both clocks get the base time (0 with no clock) and the turn starts now. The clock is the one chosen; a forced one was applied by the snapshot when the game was loaded (or a re-snapshot) and can't be changed. The mode and the starting position stay the loaded game's.
  5. The game is in progress with no result; the loaded `White`/`Black`/`Result` and time control are cleared (`replaySourceBaseTime` back to `NoClock`), and the board leaves replay with the replay fields on the new current position. The players are the seated ones from here on, in the export too.
