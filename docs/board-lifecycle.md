# Board lifecycle, seats and reset

## Board lifecycle

| State | How it's entered | Notes |
|---|---|---|
| Reset | initial; reset (button, timers, organizer) | empty board; "Load PGN" and "Load it here" available |
| Loaded | a game is imported (PGN) or loaded from the archive | the loaded game is shown in replay mode; players can join seats and continue it (see [Continuing a loaded game](replay.md#continuing-a-loaded-game)) |
| Lobby | someone presses Join White or Join Black on a reset board | seats being filled, mode chosen |
| In game | Start pressed | moves allowed |
| Ended | checkmate, king capture, stalemate, resignation, draw | replay and continuing still available |

Replay mode is entered from a game (in progress or ended) or by loading/importing a game, as described under [Replay mode](replay.md).

- **Leaving the reset state happens on the first join, or when a game is loaded.** That's when the settings snapshot is taken and the inactivity timers start applying.
- **The game ID is created when Start is pressed** (or "Continue from here" on a loaded game), since that's when the game itself begins.

## Seats

- Two buttons on each board: **Join White** and **Join Black**. Pressing one records the player's display name in that seat (synced).
- Seats can be joined on a reset board (which enters the lobby) and on a loaded board (which stays loaded, in replay). They can't be joined once a game has started.
- **The same player may join both seats**, e.g. to practise or analyse alone. Everything that's limited to "the player of that colour" then works for both colours.
- The ownership rule ("while both players are active...") refers to the seated players. With only one seat filled, the rule is about that player alone.
- **There's no leaving a seat.** Once a player has joined, the only way to leave is resetting the board, which clears both seats.

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
  - an organizer, at any time;
  - anyone else, once the board has been idle for the reset-available time (see [Inactivity](inactivity.md)).
- A game in progress that's reset is logged and archived as unfinished (`*`), if it had any moves. So a player who resets instead of resigning doesn't record a loss; that's accepted.
