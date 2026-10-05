# Bugs found in the Java

Fix these in the port only. The Java in `JavaChessBoard/` is never modified, so it keeps both bugs. Any comparison against it (FEN, PGN or move-list output) will disagree in positions where a rook has been captured on its starting square while its side could still castle. Choose test positions and games that avoid this, or treat a difference there as expected.

- **Capturing a rook doesn't remove the opponent's castling right.** `MoveFinderPiece.findMove` only sets the removal flags for the *moving* piece, and `MoveFinderPawn` always passes `false`.
- **Castling doesn't check the rook is there.** `MoveFinderKing.findCastleMove` checks the squares in between but never that the rook is still on its starting square, and `CastleMove` creates a rook of the castling colour on the destination. Combined with the bug above: after `Bxh8` capturing the rook, Black can still `O-O` and a rook appears on f8.
