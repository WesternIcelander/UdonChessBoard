# Bugs found in the Java

Fix these in the port. In the Java they're fixed on the `port-reference` branch of `JavaChessBoard/`, never on `main`, so comparisons against the Java (FEN, PGN or move-list output) are made on that branch and agree with the port.

- **Capturing a rook doesn't remove the opponent's castling right.** `MoveFinderPiece.findMove` only sets the removal flags for the *moving* piece, and `MoveFinderPawn` always passes `false`.
- **Castling doesn't check the rook is there.** `MoveFinderKing.findCastleMove` checks the squares in between but never that the rook is still on its starting square, and `CastleMove` creates a rook of the castling colour on the destination. Combined with the bug above: after `Bxh8` capturing the rook, Black can still `O-O` and a rook appears on f8.
