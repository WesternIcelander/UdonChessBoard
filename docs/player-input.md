# Player input

Players pick pieces up and drop them on a square; they never type moves. The view forwards the drop to the logical board as a from/to pair, which the board matches against the legal move list by comparing from and to squares. No notation parsing is involved. Two cases need handling:

- **Promotion**: one from/to pair matches up to four moves (queen, rook, bishop, knight), so the UI has to ask which piece.
- **Castling**: castle moves store the *rook's* square in *to*, so the board translates the drop. The king can castle by being dropped:
  - on its castling destination (g1/c1), when that's unambiguous, or
  - on its own rook (the usual Chess960 convention).
- **Ambiguous castling in Chess960.** If castling moves the king only one square, a normal king move can end on the same square (e.g. king f1, rook h1: castling and `Kg1` both put the king on g1). Then dropping on the destination means the normal move, and the king castles by being dropped on its rook. The same goes when castling doesn't move the king at all (it already stands on g1/c1): dropping on its own square just puts it back.
  - A drop on the rook is never ambiguous: no normal king move ends on its own piece. So every castle can be made that way, and nothing else is needed.
  - Decided in 9.6: there is no **castle drop space**. Steps 7.4b–9.3c built one (an extra drop target beyond the back rank, shown while a king whose castle was ambiguous was held), but in the first several-client test it didn't appear in VR for a castle that was already unambiguous (king f1, rook g1), and the king dropped on its rook castled. Since the rook drop always works, the space was removed with its board methods.

## Desktop mode

Players not in VR (desktop and mobile, `IsUserInVR()` false) move by clicking instead of carrying. Their pieces aren't pickups (`pickupable` stays off for them), so a click is an interact.

- **Select:** clicking one of their own pieces lifts it a little above its square to show it's selected.
- **Place:** clicking a square plays the move to it; clicking an opponent's piece captures it (a move to its square). Clicking their own rook with the king selected castles, as a drop on it would.
- **Unselect:** clicking the selected piece again, or the square it came from, puts it back down where it was.
- **Changing selection:** clicking another of their own pieces (other than a rook the selected king can castle with) selects that one instead, the first going back down.
- **Rejected:** a click on a square the piece can't move to puts it back down, unselected, as a VR drop snaps back.
- **Board changes** (a reset, a resignation, a flag fall, entering replay, the piece no longer being selectable) put a selected piece back down, unselected.
- **Only the selecting player sees the lift**: the pieces have no `VRCObjectSync` (step 9.6), so others see the piece move once it's placed. Selecting still takes the piece's ownership, like a grab.

This is purely a view concept: selecting is `PiecePickedUp`, placing is `PieceDropped`, so the board treats both input styles alike, including promotion (the picker appears after the placing click) and replay's free moves.

## Implementation

On `ChessBoard` (`Assets/Siggi/ChessBoard/ChessBoard.cs`). Each method has an `*As(name, ...)` form taking any display name, for tests and the debug view; the plain one is for the local player. They return `InputAccepted`, `InputRejected` or `InputPromotionNeeded`.

- **Who may move:** a game in progress (`LifecycleInGame`), not in replay, the side to move's flag not fallen, and the name seated as the side to move (a player in both seats moves both sides). Anyone else is rejected, and the view puts the piece back from the board state.
- `PiecePickedUp(square)`: accepted when the name may move and the piece is the side to move's.
- `PieceDropped(from, to)`: matches from and to against the position's legal moves. One match is played with `PlayMove` (see [Clocks](clocks.md#implementation)), which records it and runs the clocks. A pawn move to the last rank matches its four promotions and answers `InputPromotionNeeded` without playing anything. No match, or a drop back on its own square, is rejected.
- `PromotionChosen(from, to, pieceType)`: the view's follow-up with `ChessRules.Queen`, `Rook`, `Bishop` or `Knight`; plays that promotion. Any other type, or a from-to that isn't a promotion, is rejected. The board keeps nothing between the two calls, so there's no pending promotion to sync or clear.
- **Castling by drop** (in `PieceDropped`): a king dropped on the square of a rook it can castle with castles on that side. Dropped on its castling destination (g or c on its back rank), it castles only when no normal king move ends there; otherwise (Chess960 castles that move the king one square) that king move is played. A king that castles without moving can't be dropped on its own square (a drop back is rejected), so it castles on the rook.
- In replay, pickups and drops are free moves on the replay board instead (see [Replay](replay.md#implementation)). Every piece input is also a touch that keeps the board from going idle, sent at most every few seconds (see [Inactivity](inactivity.md#implementation)).

### Desktop mode (step 9.3c)

- **Click targets:** each piece's `ChessPiece` (already there for 9.2's pickup events and `OnOwnershipRequest`) gets `Interact`, forwarded as `ChessBoardView.OnPieceClicked(index)`. The board gets 64 square targets, children of **Square Targets**, each a thin box collider over its square with a `ChessSquare` behaviour that forwards its square as `OnSquareClicked(square)` (`Interact` doesn't say where the collider was hit, so one collider for the whole board can't tell the squares apart). The view holds them in `pieceBehaviours` and `squareTargets`.
- **Selecting:** `PiecePickedUp(square)` accepted makes the piece `selectedObject` (with `selectedFrom`), lifted `SelectLift` (half a square) over its square, and the local player takes its ownership. Its targets (`selectedTargets`) are the to squares of its legal moves (`LegalMovesFrom`), plus a castle's king destination when the king moves; in replay every other square. The board says whether a pickup would be accepted, without touching anything, with `CanPickUp(square)` (`CanPickUpAs(name, organizer, square)`).
- **A click on a piece:** with nothing selected, selects it; on the selected piece, puts it back; on a target square's piece that isn't the selected piece's colour (outside replay, a target holding an own piece is a castling rook), places there; on another piece the board would let them pick up, selects that one; anything else is a placing the board rejects. **A click on a square** puts the piece back on its own square, and places it anywhere else. Putting back doesn't ask the board.
- **Placing** is `PieceDropped(selectedFrom, square)`, unselecting first, so a rejection puts the piece back unselected as for a drop; a promotion waits for `ChoosePromotion` with the pawn on its square, as for a drop.
- **Losing the selection:** each `Refresh` and the once-a-second check put the piece back, unselected, once the board no longer shows it on `selectedFrom`, replay has been entered or left, `CanPickUp(selectedFrom)` no longer holds (a reset, the game ending, a flag fall, the turn passing), or the local player uses pickups.
- **What's clickable** (`UpdateClickables`, after every change, every second and every click): pieces with `DisableInteractive` off only where a click does something for the local player: with nothing selected, the pieces `CanPickUp` allows ("Select"); with one selected, that piece ("Put back"), the pieces on its targets ("Capture", or "Castle" for a rook), and the other pieces they could select instead ("Select"). Square targets are active only while a piece is selected, and only its square ("Put back") and its targets ("Place"), so the board doesn't outline on hover.
- **VR players** never get interacts on pieces or squares; desktop players never get pickups. The view's `inputStyle` (Auto, Pickups or Clicks) overrides this for testing with one VR headset (see [Testing](testing.md#one-vr-client)). To check in 9.3c: `Interact` works on an object whose `VRCPickup` has `pickupable` off (if not, desktop clicks go through a separate collider on the piece).
- Decided in 9.3c: in replay, where every piece can be picked up, a click on a piece of the selected piece's colour selects it and one of the other colour is a free move capturing it (so a free move onto an own piece takes a click on the square); putting back is local, so it sends no touch; selecting takes the piece's ownership (the pieces lost `VRCObjectSync` in 9.6, so nothing shows it).

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

### Board buttons (step 9.4a)

- **Objects:** **Create Board View** puts a world-space canvas (with `VRCUiShape`, like the debug view's) at each end of the table, leaning back towards that side's player, each with one UI button per action: its own colour's Join (Join White at White's end, Join Black at Black's), Mode, Clock, Start, Continue from here, Reset. A grid packs the buttons shown. The view holds them in `buttons`, with each one's action (`ButtonJoinWhite` … `ButtonReset`) in `buttonActions` and its label in `buttonLabels`; any number of buttons per action works. The table is deeper to make room for the panels.
- **Shown:** `UpdateButtons` (on every `Refresh`, once a second, and after every press) shows each button while `ButtonApplies(action)` holds, which is the board's check for the local player: `CanJoin(color)`, `CanSetMode`, `CanSetClock`, `CanStartGame`, `CanContinue`, `CanReset`. The once-a-second update covers what changes without the board changing: being an organizer, and the board's idle time for Reset.
- **Pressed:** each UI button calls the view's `Press…` event (`PressJoinWhite`, `PressMode`, …), which goes through `PressButton(action)` to the board's method for the local player (`JoinWhite`, `SetMode`, `SetClock`, `StartGame`, `ContinueGame`, `ResetBoard`) and returns its answer. A press the board refuses changes nothing.
- **Mode and Clock** each step through choices, since a button can't ask for a value: Mode switches between Standard and Chess960; Clock picks the next of the view's `clockBases`/`clockIncrements` (no clock, 1+0, 3+2, 5+3, 10+0, 15+10, 30+0 by default) after the board's time control, or the first when it isn't one of them (a loaded game's). Their labels show the board's current choice (`Mode: Chess960`, `Clock: 3+2`, `ClockText`).
- Decided in 9.4a: the buttons are UI canvases rather than `Interact` colliders, so labels are plain UI text and the shown ones pack together; each end has its own colour's Join and every other button, so someone playing both sides joins at both ends but can play from either. "Load PGN" comes with the PGN text box (step 11).

### Board buttons (step 9.4b)

- **Actions:** `ButtonResign…`, `ButtonOfferDraw…`, `ButtonAcceptDraw…`, `ButtonDeclineDraw…`, `ButtonRequestTakeback…`, `ButtonAcceptTakeback…` and `ButtonDeclineTakeback…`, each in a White and a Black form like Join, and `ButtonClaimDraw`, `ButtonEnterReplay`, `ButtonLeaveReplay` and four step buttons, `ButtonStepFirst`, `ButtonStepBack`, `ButtonStepForward`, `ButtonStepLast`. `ButtonColor(action)` gives the colour an action is for (`NoColor` for the rest). Each has its `Press…` event and goes through `PressButton` and `ButtonApplies` like 9.4a's, to the board's method and check for the local player with that colour: `Resign`/`CanResign`, `OfferDraw`, `AcceptDraw`, `DeclineDraw`, `ClaimDraw`, `EnterReplay`, `LeaveReplay`, `RequestTakeback`, `AcceptTakeback`, `DeclineTakeback`. The step buttons call `StepTo` (and are shown by `CanStepTo`) with ply 0, `replayIndex - 1`, `replayIndex + 1` and `moveCount`, so each is shown only where it steps somewhere.
- **Labels:** Resign, Offer draw, Accept draw, Decline draw, `Claim draw (threefold)` or `Claim draw (50 moves)` (from `ClaimableDraw`), Replay, Back to game, `<<`, `<`, `>`, `>>`, Take back to here, Accept takeback, Decline takeback.
- **Objects:** each end's panel has every action that isn't the other colour's (20 buttons), so a player answers and resigns for the side whose end they're at, and Claim draw and the replay controls are at both ends. The panel's grid is five columns by three rows, enough for the most one player sees at once (12: in replay of their own game against themselves, with an offer and a takeback request pending), in the order lobby, replay, takebacks, game, Reset last. The table is deeper again to keep the bigger panels clear of the castle drop spaces.
- Decided in 9.4b: the colour of a button comes from its action, not its panel, so the view needs nothing per panel and any number of panels works; the step buttons include first and last as well as one back and one on, since stepping through a long loaded game one ply at a time is slow.
