# Open decisions

None for the first version.

- **Deferred until Fog of War is implemented** (not needed for the first version):
  - Spectators: see everything, or nothing? Seeing everything is more fun to watch, but a spectator could tell a player what's hidden.
  - Replay during a live Fog of War game: replay is shared, so it would show every piece to everyone. Options: replay shows the fog from each viewer's side, or replay is disabled until the game ends. After the game ends, presumably everything is revealed.
  - With no check, a player can't be checkmated, and is almost never left without a move; if they are, is that stalemate (draw) or a loss? Do draw rules apply? (For now `ChessMoves.PositionStatus` reports it as stalemate.) Insufficient material in particular assumes kings can't be taken: with king capture, K+N v K isn't dead. `ChessDraws.IsInsufficientMaterial` only looks at the material, so the board would have to skip it.
  - Can Chess960 and Fog of War be combined?

Items marked "to check" in the other docs (e.g. benchmarking the PGN import's time budget) are verification work for implementation, not design decisions.
