# Player input

Players pick pieces up and drop them on a square; they never type moves. The view forwards the drop to the logical board as a from/to pair, which the board matches against the legal move list by comparing from and to squares. No notation parsing is involved. Two cases need handling:

- **Promotion**: one from/to pair matches up to four moves (queen, rook, bishop, knight), so the UI has to ask which piece.
- **Castling**: castle moves store the *rook's* square in *to*, so the board translates the drop. The king can castle by being dropped:
  - on its castling destination (g1/c1), when that's unambiguous, or
  - on its own rook (the usual Chess960 convention).
- **Ambiguous castling in Chess960.** If castling moves the king only one square, a normal king move can end on the same square (e.g. king f1, rook h1: castling and `Kg1` both put the king on g1). Then dropping on the destination means the normal move, and castling uses a **castle drop space**: an extra drop target outside the board, the size of a board square, adjacent to the king's castling destination on the outer side of the back rank. The view shows it only while that king is held and the castle is legal, and forwards a drop there as the castle.
  - The same applies when castling doesn't move the king at all (it already stands on g1/c1): dropping on its own square would just put it back, so the castle drop space is used there too.
  - The castle drop space is purely a view concept; the board receives it as "castle on that side".

## Implementation

On `ChessBoard` (`Assets/Siggi/ChessBoard/ChessBoard.cs`). Each method has an `*As(name, ...)` form taking any display name, for tests and the debug view; the plain one is for the local player. They return `InputAccepted`, `InputRejected` or `InputPromotionNeeded`.

- **Who may move:** a game in progress (`LifecycleInGame`), not in replay, the side to move's flag not fallen, and the name seated as the side to move (a player in both seats moves both sides). Anyone else is rejected, and the view puts the piece back from the board state.
- `PiecePickedUp(square)`: accepted when the name may move and the piece is the side to move's.
- `PieceDropped(from, to)`: matches from and to against the position's legal moves (castles left out until castling by drop, step 7.4b). One match is played with `PlayMove` (see [Clocks](clocks.md#implementation)), which records it and runs the clocks. A pawn move to the last rank matches its four promotions and answers `InputPromotionNeeded` without playing anything. No match, or a drop back on its own square, is rejected.
- `PromotionChosen(from, to, pieceType)`: the view's follow-up with `ChessRules.Queen`, `Rook`, `Bishop` or `Knight`; plays that promotion. Any other type, or a from-to that isn't a promotion, is rejected. The board keeps nothing between the two calls, so there's no pending promotion to sync or clear.
- Free moves in replay are step 7.8a; activity timestamps on touches come with inactivity (step 8.5).
