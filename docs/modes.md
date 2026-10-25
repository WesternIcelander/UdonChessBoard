# Modes

## Standard Chess

The standard starting position.

## Chess960

- When Start is pressed, the starting position is generated randomly from the 960 legal Chess960 positions (bishops on opposite colours, king between the rooks; same setup for both sides). The player pressing Start generates it and syncs the result like any other state.
- The king and rook starting files are set from the generated position; castling uses the Chess960 rules already supported by the move encoding.
- PGN: `[Variant "Chess960"]`, `[SetUp "1"]` and the starting `FEN`.
- **Implementation:** `ChessGame.Chess960Position(index, board, state)` sets up position `index` (0 to `Chess960Count` − 1) in Scharnagl's numbering, where 518 (`Chess960StandardIndex`) is the standard position: the light-squared bishop on file `2 × (index mod 4) + 1`, the dark-squared one on `2 × (index / 4 mod 4)`, the queen on empty square `index / 16 mod 6`, the knights by `index / 96` (0 to 9) from the usual table, then rook, king, rook. It sets White to move, every castling right, the king and rook files and the king squares, and returns `false` for an index out of range. `Chess960Fen(index)` gives the same position as `ChessFen.WriteFen` writes it (Shredder castling letters unless the rooks are on a and h). The board picks the index with `_RandomChess960Index()` (`UnityEngine.Random.Range`) when Start is pressed.

## Fog of War (later)

**Deferred: not part of the first version.** The first version offers Standard Chess and Chess960. The rules core still includes the `prohibitSelfCheck` option described below, so Fog of War can be added later without reworking it. Everything else in this subsection is a sketch for that later work.

- **`prohibitSelfCheck` (in the first version):** a flag passed to the rules code, with the same name and meaning as the Java's `Board.prohibitSelfCheck` (default `true`). When `false`, legal-move generation skips the attack-check filter and the castling check conditions, and check/checkmate detection reports nothing. Published perft values only cover normal rules, so test the `false` setting against the Java with `prohibitSelfCheck = false`.

Each player sees only their own pieces, plus the opponent's pieces on squares their own pieces could move to (i.e. could capture).

- **Rules:** there is no check. Moving into check, staying in check, and castling out of or through check are allowed, and **the game is won by capturing the king**. This is what the Java's `Board.prohibitSelfCheck` field is for: set to `false`, it skips all of the check-related filtering, including in `MoveFinderKing`'s castling checks.
- In the port: the legal move list skips the attack-check filter, check/checkmate detection is off, and a king capture ends the game. The move encoding needs nothing new (captured type = king).
- **Notation:** no `+`/`#` suffixes; the final move is the king capture (e.g. `Bxe8`).
- **Visibility is per viewer**, computed locally from the board state: the view hides (doesn't render) pieces the local player can't see, including while they're being carried. A player seated as both colours sees everything.
- Hidden information is only hidden on screen: the board's synced state still contains every piece, so a modified client could reveal it. That's an accepted limitation in VRChat.
- PGN: a custom tag, e.g. `[Variant "Fog of War"]`.
