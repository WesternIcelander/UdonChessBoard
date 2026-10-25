# Board lifecycle, seats and reset

## Board lifecycle

| State | How it's entered | Notes |
|---|---|---|
| Reset | initial; reset (button, timers, organizer) | empty board; "Load PGN" and "Load it here" available |
| Loaded | a game is imported (PGN) or loaded from the archive | the loaded game is shown in replay mode; players can join seats and continue it (see [Continuing a loaded game](replay.md#continuing-a-loaded-game)) |
| Lobby | someone presses Join White or Join Black on a reset board | seats being filled, mode chosen |
| In game | Start pressed | moves allowed |
| Ended | checkmate, king capture, stalemate, resignation, time forfeit, draw | replay and continuing still available |

Replay mode is entered from a game (in progress or ended) or by loading/importing a game, as described under [Replay mode](replay.md).

- **Leaving the reset state happens on the first join, or when a game is loaded.** That's when the settings snapshot is taken and the inactivity timers start applying.
- **The game ID is created when Start is pressed** (or "Continue from here" on a loaded game), since that's when the game itself begins.

## Seats

- Two buttons on each board: **Join White** and **Join Black**. Pressing one records the player's display name in that seat (synced).
- Seats can be joined on a reset board (which enters the lobby) and on a loaded board (which stays loaded, in replay). They can't be joined once a game has started. On a loaded board, the player who loaded it joins first: until they hold a seat, nobody else may join (step 13.2, [Ownership](ownership.md)).
- **The same player may join both seats**, e.g. to practise or analyse alone. Everything that's limited to "the player of that colour" then works for both colours. Takebacks are the exception: instead of asking themselves and accepting, they continue from an earlier position at once with "Continue from here" (step 13.3, see [Replay](replay.md#continuing-from-an-earlier-position-takeback)).
- The ownership rule ("while both players are active...") refers to the seated players. With only one seat filled, the rule is about that player alone.
- **Leaving a seat before the game starts** (step 13.1): a seated player may leave their seat while no game has started, in the lobby and on a loaded board, with the **Leave** button at that seat's end (a player in both seats leaves one at a time). The seat is emptied, nothing is logged, and anyone may join it again. A lobby whose last seat is emptied goes back to reset (its snapshot dropped, as if nobody had joined); a loaded board stays loaded however many seats are empty, until a game starts ("Continue from here") or it's reset. Once a game has started, resetting the board (which clears both seats) is the only way out. An organizer can't empty someone else's seat, only reset the board.

## Starting

- A **Start** button starts the game once both seats are filled.
- **Mode selection** happens in the lobby, before Start: Standard Chess or Chess960 (Fog of War later).
- **Clock settings** are also chosen in the lobby (see [Clocks](clocks.md)).
- **Any seated player can change any lobby setting** (mode, clock) and press Start, unless an organizer has forced that setting in the settings UI. Forcing works the same way for every lobby setting: a "forced" bit plus the forced value, as instance defaults with per-board overrides, included in the snapshot (see [Per-board overrides](settings.md#per-board-overrides)).
- **On a loaded board**, the clock is chosen the same way, but the mode isn't: it comes from the loaded game. "Continue from here" takes the place of Start (see [Continuing a loaded game](replay.md#continuing-a-loaded-game)).
## Reset

- Resetting a board clears it completely, including both seats. The previous game remains available from the archive, so the board doesn't need to keep it.
- **Who can reset:**
  - a seated player, at any time, including mid-game;
  - on a loaded board whose loader holds no seat (step 13.2), the loader instead of the seated players;
  - an organizer, at any time;
  - anyone else, once the board has been idle for the reset-available time (see [Inactivity](inactivity.md)).
- A game in progress that's reset is logged and archived as unfinished (`*`), if it had any moves. So a player who resets instead of resigning doesn't record a loss; that's accepted.

## Implementation

`ChessBoard` in `Assets/Siggi/ChessBoard/ChessBoard.cs` (namespace `SiggiChessBoard`).

- The lifecycle is the `lifecycle` field, one of `LifecycleReset`, `LifecycleLoaded`, `LifecycleLobby`, `LifecycleInGame`, `LifecycleEnded`.
- The state fields are the ones under [Synced fields](networking.md#synced-fields-per-board), as plain public fields, packed into synced fields when sent (step 8.3, see [Networking](networking.md#implementation)). `_ClearState()` puts them all back to the reset state: the standard starting position (so a reset board shows pieces ready to play), no moves, empty seats, no game ID, no result, Standard mode, no clock, no snapshot. The board clears itself in its Unity `Start` (the Start button is `StartGame`), unless an update has already arrived.
- Input methods return `InputAccepted` or `InputRejected`.
- Each button below has a `Can…` check saying whether it applies (`CanJoinAs`, `CanSetModeAs`, `CanSetClockAs`, `CanStartGameAs`), which its method uses too; see [Buttons](player-input.md#buttons).
- **Seats:** `JoinWhite()` / `JoinBlack()` (the buttons) seat the local player by display name, through `JoinAs(color, name)`, which tests and the debug view call with any name. A join is accepted on a reset board (which enters the lobby and takes the settings snapshot, see [Snapshot](settings.md#per-board-overrides)), in the lobby and on a loaded board (which stays loaded), and rejected in game and once ended. **A seat held by someone else can't be taken**, since only its player can leave it; names are compared exactly (C# `==` on strings is ordinal), so a lookalike name doesn't get the seat. Joining a seat you already hold is accepted and changes nothing. `IsSeated(color, name)` says whether a name holds a seat.
- **Leaving:** `LeaveSeat(color)` (the Leave buttons, for the local player) goes through `LeaveSeatAs(color, name)`, checked by `CanLeaveSeat(color)` / `CanLeaveSeatAs(color, name)`: `name` holds that seat (compared exactly, as for joining) and the board is in the lobby or loaded. It empties the seat and is told as `ChangeLeft`; in the lobby, with both seats then empty, it `_ClearState()`s first, so the board is reset (still told as `ChangeLeft`, so it plays no reset sound). Refused in game, once ended, on a reset board, for a seat someone else holds and for one nobody holds.
- **Lobby settings:** `SetMode(mode)` and `SetClock(baseTime, increment)` (the lobby buttons, for the local player) go through `SetModeAs(name, ...)` / `SetClockAs(name, ...)`. The name must hold a seat (`IsPlayer(name)`), and the setting mustn't be forced by the snapshot (`IsModeForced()` / `IsClockForced()`, read from `gameSettings`); choosing the forced value is refused too. The snapshot applies the forced values as it's taken, so the lobby shows them. The mode can be changed only in the lobby (a loaded game keeps its own), and the clock in the lobby and on a loaded board. The mode is `ChessGame.ModeStandard` or `ModeChess960`. A time control is no clock (`ChessArbiter.NoClock`, increment 0) or a base of 1 s to `MaxBaseTime` (24 h) with an increment of 0 to `MaxIncrement` (1 h); anything else is refused and changes nothing.
- **Loading:** `_LoadGame(...)` and `_LoadImport(importer)` put a game on a reset board, which enters `LifecycleLoaded` in replay and takes the settings snapshot, recording the loader in `loadedBy` (step 13.2); see [Replay](replay.md#implementation).
- **Start:** `StartGame()` / `StartGameAs(name)`, by a seated player, in the lobby, with both seats filled; refused otherwise (a loaded board uses "Continue from here" instead, `ContinueGameAs`, see [Replay](replay.md#implementation)). It copies any forced mode and clock from the snapshot over the players' choices (a forced "no clock" has increment 0), sets the starting position (the standard one, or a random Chess960 one, see [Modes](modes.md#chess960)) with counters 0 and 1, gives both clocks the base time (0 with no clock), sets the turn start to `Networking.GetServerTimeInSeconds()`, gives the game a new ID (see [Game IDs](game-ids.md#implementation)) and enters `LifecycleInGame`.
- **Reset:** `ResetBoard()` / `ResetBoardAs(name, organizer)` (named so because `Reset` is a Unity message), checked by `CanReset()` / `CanResetAs(name, organizer)`: a board that isn't reset, by a seated player or an organizer, or by anyone once the board has been idle for the reset-available time ([Inactivity](inactivity.md#implementation)). The board also resets itself, the same way, after the auto-reset time. A game in progress is logged as unfinished (`*`, `Termination "unterminated"`) if it has moves, unless the side to move's flag has already fallen: then `_CheckFlagFall` first ends it on time and it's logged with that result, since it's over, not unfinished. An ended game was logged when it ended, and a loaded game wasn't played here, so neither is logged. Then `_ClearState()`. Like every change, it takes ownership and is sent (8.3).
