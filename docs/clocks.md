# Clocks

- **Time control:** base time plus an increment per move (e.g. 5+3), or no clock. Shown in the lobby; recorded in the PGN as the standard `[TimeControl "300+3"]` tag (seconds), or `"-"` for no clock.
- **Who chooses:** the players, in the lobby, unless organizers have forced a time control in the settings UI. Forcing is a setting like any other: a "clock forced" bit plus the forced base and increment, as instance defaults with per-board overrides, included in the snapshot. On a loaded game, the clock is chosen the same way, and continuing starts both sides with fresh clocks (see [Continuing a loaded game](replay.md#continuing-a-loaded-game)).
- **Synced state:** each side's remaining time (milliseconds, `int`) and the server time when the current turn began (`double`, `Networking.GetServerTimeInSeconds()`). Every client computes the running clock from those, so nothing needs syncing while a player thinks.
- **On each move:** the mover's remaining time is reduced by the time since their turn began, the increment is added, and the turn start resets. This is part of the same serialization as the move.
- **Per-move clock history:** the remaining time after each move is stored alongside the move history (`int[]`). This lets a takeback restore the clocks at that point, and lets the PGN include standard `[%clk 0:04:58]` comments.
  - Export writes `H:MM:SS`, adding tenths (`0:09:57.6`) when they aren't zero, so an imported VRChess or Lichess clock comes back out with the same value.
  - PGN import fills the history from the PGN's `[%clk]` comments, with `-1` for moves that have none (see [PGN import](pgn.md#import-local-rare)), so a loaded game keeps its clocks for replay and re-export. A game loaded from the archive has its stored history.
- **Flag fall:** when a side's time reaches zero, every client can see it. The board's current owner ends the game (once, as with the automatic reset). FIDE rules: the side out of time loses, unless the opponent has no way to checkmate, in which case it's a draw. That exception is a draw rule, so under "No draws" a timeout is always a loss.
- **A player leaving doesn't stop their clock**: their time runs out as normal.
- **The clock doesn't pause in replay.** Replay pauses moves, not time: the player to move keeps using their time while the board is in replay.
- Time forfeit is a new row in Game endings.
