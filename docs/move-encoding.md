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

## Derived, not stored

```csharp
static bool IsEnPassantCapture(int move) =>
    PieceType(move) == PAWN && PrevEnPassantValid(move) && To(move) == PrevEnPassantSquare(move);

static bool IsDoublePush(int move) =>
    PieceType(move) == PAWN && Math.Abs((To(move) >> 3) - (From(move) >> 3)) == 2;
```

- **En passant capture**: the en passant square is always empty, so a pawn landing on it must be capturing en passant. The captured pawn is on the *from* rank, *to* file.
- **Double push**: sets the new en passant square to the square between from and to.

## Castling

- Castle bit set; *from* = king's square, *to* = rook's starting square ("king takes own rook"). The side follows from whether the rook is right or left of the king. This supports Chess960 and configurable rook files, including castles where the king doesn't move.
- Destinations are fixed: king to g/c, rook to f/d on the back rank.
- Keeping the king in *from* means "the piece on *from* matches the piece-type field" holds for every move, so generic code (highlighting, notation matching) needs no castling special case.
- Undo doesn't need from/to: castling is only legal while the king and rook are unmoved, so their squares are the board's stored starting files. The Java board already stores the rook files (`leftRookFile`/`rightRookFile`, set by `FEN.java`); the port adds a `kingFile`, set by the FEN loader.

## Undo

- Restore the previous castling rights and en passant (bits 22–29) wholesale, instead of the Java's "removes right to castle" and "cleared en passant" fields. This removes the special cases.
- The halfmove clock (fifty-move rule, ~7 bits) isn't in the move, and doesn't need its own history: at any ply it's the number of plies since the last capture or pawn move, which can be read off the move history. If there's none since the start, add the starting position's halfmove clock. So the board syncs the starting halfmove clock and move number as plain ints, and receivers never have to parse the starting FEN.
