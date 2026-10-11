# Move encoding (32-bit int)

| Bits | Width | Field |
|---|---|---|
| 0–5 | 6 | from square (king's square when castling) |
| 6–11 | 6 | to square (rook's starting square when castling) |
| 12–14 | 3 | moving piece type (1–6 = pawn…king) |
| 15–17 | 3 | promotion type (0 = none) |
| 18–20 | 3 | captured type (0 = no capture) |
| 21 | 1 | castle |
| 22–25 | 4 | castling rights before the move |
| 26–29 | 4 | en passant before the move: 1 valid bit + 3-bit file |
| 30–31 | 2 | spare |

- Only piece *types* are stored. The mover is the side to move; a captured piece is always the other colour.
- Always mask after shifting (`(move >> shift) & mask`). Then `int` and `uint` behave the same: `>>` on a negative `int` copies the sign bit, but the mask discards it. Bits 30–31 are currently unused anyway.
- The en passant rank isn't stored: it is rank 6 if White is moving, rank 3 if Black is.
- Castling rights (bits 22–25) are in FEN order: bit 22 = White king side (`K`), 23 = White queen side (`Q`), 24 = Black king side (`k`), 25 = Black queen side (`q`). King side means the rook right of the king, queen side the rook left of it, so the names hold for Chess960 too (`ChessRules.CastleWhiteKingSide` etc.).
- En passant (bits 26–29): bit 26 is the valid bit, bits 27–29 the file. `PackMove` takes the file, `-1` for none.
- Helpers in `ChessRules`: `PackMove`, `MoveFrom`, `MoveTo`, `MovePieceType`, `MovePromotion`, `MoveCaptured`, `MoveIsCastle`, `MovePrevCastlingRights`, `MovePrevEnPassantValid`, `MovePrevEnPassantFile`, `MovePrevEnPassantSquare(move, moverColor)`, `IsEnPassantCapture`, `IsDoublePush`.

## Derived, not stored

```csharp
static bool IsEnPassantCapture(int move) =>
    PieceType(move) == PAWN && PrevEnPassantValid(move) && To(move) == PrevEnPassantSquare(move);

static bool IsDoublePush(int move) =>
    PieceType(move) == PAWN && Math.Abs((To(move) >> 3) - (From(move) >> 3)) == 2;
```

- **En passant capture**: the en passant square is always empty, so a pawn landing on it must be capturing en passant. The captured pawn is on the *from* rank, *to* file. Since the move doesn't store the mover's colour, `IsEnPassantCapture` takes it from the pawn's direction (up the board = White) to find the en passant rank.
- **Double push**: sets the new en passant square to the square between from and to.

## Castling

- Castle bit set; *from* = king's square, *to* = rook's starting square ("king takes own rook"). The side follows from whether the rook is right or left of the king. This supports Chess960 and configurable rook files, including castles where the king doesn't move.
- Destinations are fixed: king to g/c, rook to f/d on the back rank.
- Keeping the king in *from* means "the piece on *from* matches the piece-type field" holds for every move, so generic code (highlighting, notation matching) needs no castling special case.
- Undo doesn't need from/to: castling is only legal while the king and rook are unmoved, so their squares are the board's stored starting files. The Java board already stores the rook files (`leftRookFile`/`rightRookFile`, set by `FEN.java`); the port adds a `kingFile`, set by the FEN loader.

## Undo

- Restore the previous castling rights and en passant (bits 22–29) wholesale, instead of the Java's "removes right to castle" and "cleared en passant" fields. This removes the special cases.
- The halfmove clock (fifty-move rule) doesn't fit in the move, so the board keeps it in a separate `int[]` alongside the move history: entry *i* is the clock before ply *i*, like the before-the-move state in bits 22–29. Undo restores the clock from that entry. (The Java does the same per move, with `Move.clearedHalfMoves()`.)
- That array is **local, not synced**, to keep network traffic down. Each client computes it from the synced move history and the synced starting halfmove clock: the clock goes to 0 after a capture or pawn move, and up by 1 after any other move. The current clock is a local field computed the same way. A client rebuilds the array whenever it receives a new move history (a few hundred ints at most, so a full rebuild is cheap) and extends it when it applies a move itself. Receivers never have to parse the starting FEN.
