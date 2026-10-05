# Physical pieces

- **Other players see a piece while it's being carried.**
- **32 piece objects per board**, one per piece. A side never has more than 16 pieces, since promotion turns a pawn into another piece rather than adding one. Each object swaps its mesh to match the piece the board says it is, so promotion needs no spare queens. Captured pieces are hidden or set beside the board.
- **Carrying is synced with `VRCObjectSync`** on each piece. It handles ownership on grab, interpolation and late joiners. This is presentation-only networking, owned by the view; the board's synced fields stay the single source of truth for the game.
- **At rest, positions come from the board.** After every `OnBoardChanged()`, every client puts each piece on the square the board says. The player who dropped the piece (its sync owner) computes the same position, so all clients agree. To check in testing: that `VRCObjectSync` doesn't fight a non-owner's local snap.
- **Grabbing is gated by the board.** Grabbing a piece takes ownership of the *piece*, which the board's ownership rules don't cover. So the view sets each piece's `VRCPickup.pickupable` for the local player from a board getter ("may the local player touch pieces?"), updated on every change. Bystanders can't grab pieces during a game, but anyone can on a reset board. The pieces' `OnOwnershipRequest` asks the board too, as a second check.
- **Mapping:** the view keeps an array from square to piece object, rebuilt from board state. A move updates it; a reset or load rebuilds it.
