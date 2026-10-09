# Replay mode

Players can enter replay mode at any time to review the game.

- **Replay is always shared.** The board is one physical object: when it's in replay mode, everyone sees the replay. During a live game this pauses play, since no game moves can be made until the board leaves replay, but the clock keeps running (see [Clocks](clocks.md)).
- Entering replay changes synced state, so it needs ownership. While both players are active, only they (or an organizer) can do that (see [Ownership](ownership.md)), so a bystander can't hijack a live game's board.
- **Free manipulation:** in replay mode players can step through the game, play moves, or move pieces anywhere, including illegal moves.
- **Separate state:** replay never touches the game state. It works on a separate replay board (its own 64-square array) plus the ply it started from, so leaving replay always returns to the game exactly as it was.
- **Stepping** through the game uses the move history: undoing and redoing packed moves gives any earlier position, without the starting FEN.
- **Free moves** are plain piece placement on the replay board: no move generation, no packed moves, no legality checks. The replay board is just an array.
- **Custom flag:** any free move or deviation from the game's moves marks the replay position as *custom*. Stepping back onto a position from the game history clears the flag.

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
- **What import keeps:** the game history (starting FEN, if one was given, and the moves), the mode from `Variant`, plus the `White`, `Black` and `Result` tags, which are shown in the UI during replay. Nothing else from the PGN is stored.
- **On continuing**, the game history up to that point (including the starting FEN) is kept, and the loaded `White`, `Black` and `Result` are dropped.
- Every PGN the board writes is built from scratch from the board's own state: current players, date, result, and the `FEN` tag when the game didn't start from the standard position.
