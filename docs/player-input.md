# Player input

Players pick pieces up and drop them on a square; they never type moves. The view forwards the drop to the logical board as a from/to pair, which the board matches against the legal move list by comparing from and to squares. No notation parsing is involved. Two cases need handling:

- **Promotion**: one from/to pair matches up to four moves (queen, rook, bishop, knight), so the UI has to ask which piece.
- **Castling**: castle moves store the *rook's* square in *to*, so the board translates the drop. The king can castle by being dropped:
  - on its castling destination (g1/c1), when that's unambiguous, or
  - on its own rook (the usual Chess960 convention).
- **Ambiguous castling in Chess960.** If castling moves the king only one square, a normal king move can end on the same square (e.g. king f1, rook h1: castling and `Kg1` both put the king on g1). Then dropping on the destination means the normal move, and castling uses a **castle drop space**: an extra drop target outside the board, the size of a board square, adjacent to the king's castling destination on the outer side of the back rank. The view shows it only while that king is held and the castle is legal, and forwards a drop there as the castle.
  - The same applies when castling doesn't move the king at all (it already stands on g1/c1): dropping on its own square would just put it back, so the castle drop space is used there too.
  - The castle drop space is purely a view concept; the board receives it as "castle on that side".

## Desktop mode

Players not in VR (desktop and mobile, `IsUserInVR()` false) move by clicking instead of carrying. Their pieces aren't pickups (`pickupable` stays off for them), so a click is an interact.

- **Select:** clicking one of their own pieces lifts it a little above its square to show it's selected.
- **Place:** clicking a square plays the move to it; clicking an opponent's piece captures it (a move to its square). Clicking their own rook with the king selected castles, as a drop on it would, and the castle drop space appears while the king is selected, as it does while the king is held.
- **Unselect:** clicking the selected piece again, or the square it came from, puts it back down where it was.
- **Changing selection:** clicking another of their own pieces (other than a rook the selected king can castle with) selects that one instead, the first going back down.
- **Rejected:** a click on a square the piece can't move to puts it back down, unselected, as a VR drop snaps back.
- **Board changes** (a reset, a resignation, a flag fall, entering replay, the piece no longer being selectable) put a selected piece back down, unselected.
- **Others see it:** selecting takes the piece's ownership, like a grab, so its `VRCObjectSync` shows the lift to everyone. If the pieces lose `VRCObjectSync` (step 9.6), the lift is seen only by the player who selected it.

This is purely a view concept: selecting is `PiecePickedUp`, placing is `PieceDropped` (or `KingDroppedOnCastleSpace`), so the board treats both input styles alike, including promotion (the picker appears after the placing click) and replay's free moves.

## Implementation

On `ChessBoard` (`Assets/Siggi/ChessBoard/ChessBoard.cs`). Each method has an `*As(name, ...)` form taking any display name, for tests and the debug view; the plain one is for the local player. They return `InputAccepted`, `InputRejected` or `InputPromotionNeeded`.

- **Who may move:** a game in progress (`LifecycleInGame`), not in replay, the side to move's flag not fallen, and the name seated as the side to move (a player in both seats moves both sides). Anyone else is rejected, and the view puts the piece back from the board state.
- `PiecePickedUp(square)`: accepted when the name may move and the piece is the side to move's.
- `PieceDropped(from, to)`: matches from and to against the position's legal moves. One match is played with `PlayMove` (see [Clocks](clocks.md#implementation)), which records it and runs the clocks. A pawn move to the last rank matches its four promotions and answers `InputPromotionNeeded` without playing anything. No match, or a drop back on its own square, is rejected.
- `PromotionChosen(from, to, pieceType)`: the view's follow-up with `ChessRules.Queen`, `Rook`, `Bishop` or `Knight`; plays that promotion. Any other type, or a from-to that isn't a promotion, is rejected. The board keeps nothing between the two calls, so there's no pending promotion to sync or clear.
- **Castling by drop** (in `PieceDropped`): a king dropped on the square of a rook it can castle with castles on that side. Dropped on its castling destination (g or c on its back rank), it castles only when no normal king move ends there; otherwise (Chess960 castles that move the king one square) that king move is played. A king that castles without moving can't be dropped on its own square (a drop back is rejected), so it castles on the rook or the castle drop space.
- `KingDroppedOnCastleSpace(side)`: the view's drop on the castle drop space, `CastleKingSide` (rook right of the king) or `CastleQueenSide`; plays the side to move's legal castle on that side, otherwise rejected. It castles in unambiguous positions too, since the view only offers the space when needed.
- `IsCastleDropSpaceNeeded(side)`: tells the view (step 9.3b) whether to show the space while the king is held: a game in progress and not in replay, the castle on that side legal, and a drop on its destination not a castle (the king already stands there, or a normal king move ends there).
- In replay, pickups and drops are free moves on the replay board instead (see [Replay](replay.md#implementation)). Every piece input is also a touch that keeps the board from going idle, sent at most every few seconds (see [Inactivity](inactivity.md#implementation)).

### Desktop mode (step 9.3c)

- **Click targets:** each piece's behaviour (already there for 9.2's pickup events and `OnOwnershipRequest`) gets `Interact`, and the board gets 64 square targets, one collider each with a small behaviour that forwards its square index to the view (`Interact` doesn't say where the collider was hit, so one collider for the whole board can't tell the squares apart). The interact text says what a click does ("Select", "Place", "Capture", "Put back").
- **What's clickable:** pieces with `DisableInteractive` off only where a click does something for the local player: their own pieces while `PiecePickedUp` would accept them, and while one is selected, every square it could move to plus the selected piece and its square. The square targets are switched off while nothing is selected, so the board doesn't outline on hover.
- **VR players** never get interacts on pieces or squares; desktop players never get pickups. To check in 9.3c: `Interact` works on an object whose `VRCPickup` has `pickupable` off (if not, desktop clicks go through a separate collider on the piece).

## Buttons

The board's buttons are one method each on `ChessBoard` (see [Architecture](architecture.md#logical-board-and-physical-board)), and each has a `Can…` check that says whether it applies. The view shows a button only while its check holds (steps 9.4a/b), and the button's method refuses whenever its check doesn't, so the two can never disagree. The checks for Join, mode, clock and Start come with their methods (step 7.1); the others come before their methods, which arrive with resign (7.5a), draws (7.5b), replay (7.8a), takebacks (7.8b), loading (7.9a), continuing (7.9b) and reset (7.10).

Like the input methods, each check has a plain form for the local player and an `*As` form taking any name; where organizers count, the `*As` form also takes an `organizer` flag (the plain one asks `arbiter.IsOrganizer` for the local player). A button that acts for one colour takes it, so a player in both seats chooses which side resigns, offers or accepts.

| Check | Applies when |
|---|---|
| `CanJoinAs(color, name)` | a reset, lobby or loaded board, and that seat is empty |
| `CanSetModeAs(name)` | a seated player, in the lobby, the mode not forced |
| `CanSetClockAs(name)` | a seated player, in the lobby or on a loaded board, the clock not forced |
| `CanStartGameAs(name)` | a seated player, in the lobby, both seats filled |
| `CanResignAs(color, name)` | the game running, `name` seated as `color` |
| `CanOfferDrawAs(color, name)` | draws by agreement on, the game running, `name` seated as `color`, no offer pending |
| `CanAcceptDrawAs` / `CanDeclineDrawAs(color, name)` | the other colour's offer pending, draws by agreement still on, the game running, `name` seated as `color` |
| `CanClaimDrawAs(name)` | the game running, `name` seated as the side to move, and `ClaimableDraw()` has a draw |
| `CanEnterReplayAs(name, organizer)` | a game in progress or ended, not in replay, `name` may work the replay controls |
| `CanLeaveReplayAs(name, organizer)` | in replay of a game in progress or ended (not a loaded board), `name` may work the replay controls |
| `CanStepToAs(name, organizer, ply)` | in replay, `name` may work the replay controls, `ply` from 0 to `moveCount` and not the position shown (unless that's custom) |
| `CanRequestTakebackAs(color, name)` | takebacks on, no request pending, `name` seated as `color`, and the game can continue from the replay position |
| `CanAcceptTakebackAs` / `CanDeclineTakebackAs(color, name)` | the other colour's request pending for the ply still shown, takebacks still on, `name` seated as `color`, and the game can continue from the replay position |
| `CanContinueAs(name)` | a loaded board in replay on a game position (not custom, the last one included) that isn't over already (it has legal moves, and no automatic draw the snapshot has on applies to it), both seats filled, `name` seated |
| `CanResetAs(name, organizer)` | the board isn't reset, and `name` is seated or an organizer, or the board has been idle for the reset-available time |
| `CanLoadGame()` | the board is reset |

- **The game running** means in progress (`LifecycleInGame`) with the side to move's flag not fallen. Resigning, draw offers and claims aren't moves, so they work in replay too.
- **`ClaimableDraw()`** is the draw the side to move could claim: `EndingThreefoldRepetition` when the position has occurred three times and that claim is on in the snapshot, otherwise `EndingFiftyMoveRule` when the halfmove clock has reached 100 and that claim is on, otherwise `EndingNone` (always under "No draws"). `ClaimDrawAs` ends the game with it ([Game endings](game-endings.md#implementation)).
- **Who may work the replay controls:** a seated player or an organizer, or anyone on a board where nobody is seated (a loaded game nobody has joined), since the ownership rule only limits who isn't seated while someone is (see [Ownership](ownership.md)). A bystander gets no way in once the players have gone idle: they get the reset button instead (below), and their touches would make the board active again.
- **The game can continue from the replay position** when it's a position of the game history (not custom) with at least one move after it (a takeback takes back at least one move), and the game is running or ended. A loaded game continues with "Continue from here" instead, which needs no agreement and can also start from the last position.
- Anyone else may reset the board once it has been idle for the reset-available time ([Inactivity](inactivity.md#implementation)).
