# Legality checking

The Java checks each candidate by copying the board and generating all of the opponent's replies as `Move` objects; one legal-move list costs roughly a thousand or more moves. Replace with a direct attack check:

1. Play the candidate on the piece array.
2. Check whether the king's square is attacked (look outward for sliders, knights, pawns, the enemy king).
3. Undo; keep the move only if legal.

This also removes the recursion (`getAllLegalMoves` → `isInCheck` → `getAllLegalMoves`).

Notation generation (`RegularMove.notation`) should reuse the already-computed legal move list and disambiguate directly, with no parsing. Find the other legal moves with the same piece type and *to* square. If there are none, no prefix is needed. Otherwise add the from-file if that makes it unique, else the from-rank, else both. Pawn captures always take the from-file. (`ChessNotation.MoveToSan`; the other moves must also match on castling, which never collides since a castle's *to* is the mover's own rook.)

Import goes the other way, from SAN to a legal move, also without regex (`ChessNotation.MoveFromSan`, see [Import steps](pgn.md#import-steps)).
