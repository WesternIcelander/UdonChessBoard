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

This is purely a view concept: selecting is `PiecePickedUp`, placing is `PieceDropped`, so the board treats both input styles alike, including promotion (the promotion ghosts appear after the placing click) and replay's free moves.

## Implementation

On `ChessBoard` (`Packages/io.siggi.chessboard/Scripts/ChessBoard.cs`). Each method has an `*As(name, ...)` form taking any display name, for tests and the debug view; the plain one is for the local player. They return `InputAccepted`, `InputRejected` or `InputPromotionNeeded`.

- **Who may move:** a game in progress (`LifecycleInGame`), not in replay, the side to move's flag not fallen, and the name seated as the side to move (a player in both seats moves both sides). Anyone else is rejected, and the view puts the piece back from the board state.
- `PiecePickedUp(square)`: accepted when the name may move and the piece is the side to move's.
- `PieceDropped(from, to)`: matches from and to against the position's legal moves. One match is played with `_PlayMove` (see [Clocks](clocks.md#implementation)), which records it and runs the clocks. A pawn move to the last rank matches its four promotions and answers `InputPromotionNeeded` without playing anything. No match, or a drop back on its own square, is rejected.
- `PromotionChosen(from, to, pieceType)`: the view's follow-up with `ChessRules.Queen`, `Rook`, `Bishop` or `Knight`; plays that promotion. Any other type, or a from-to that isn't a promotion, is rejected. The board keeps nothing between the two calls, so there's no pending promotion to sync or clear.
- **Castling by drop** (in `PieceDropped`): a king dropped on the square of a rook it can castle with castles on that side. Dropped on its castling destination (g or c on its back rank), it castles only when no normal king move ends there; otherwise (Chess960 castles that move the king one square) that king move is played. A king that castles without moving can't be dropped on its own square (a drop back is rejected), so it castles on the rook.
- In replay, pickups and drops are free moves on the replay board instead (see [Replay](replay.md#implementation)). Every piece input is also a touch that keeps the board from going idle, sent at most every few seconds (see [Inactivity](inactivity.md#implementation)).

### Desktop mode (step 9.3c)

- **Click targets:** each piece's `ChessPiece` (already there for 9.2's pickup events and `OnOwnershipRequest`) gets `Interact`, forwarded as `ChessBoardView._OnPieceClicked(index)`. The board gets 64 square targets, children of **Square Targets**, each a thin box collider over its square with a `ChessSquare` behaviour that forwards its square as `_OnSquareClicked(square)` (`Interact` doesn't say where the collider was hit, so one collider for the whole board can't tell the squares apart). The view holds them in `pieceBehaviours` and `squareTargets`.
- **Selecting:** `PiecePickedUp(square)` accepted makes the piece `selectedObject` (with `selectedFrom`), lifted `SelectLift` (half a square) over its square, and the local player takes its ownership. Its targets (`selectedTargets`) are the to squares of its legal moves (`LegalMovesFrom`), plus a castle's king destination when the king moves; in replay every other square. The board says whether a pickup would be accepted, without touching anything, with `CanPickUp(square)` (`CanPickUpAs(name, organizer, square)`).
- **A click on a piece:** with nothing selected, selects it; on the selected piece, puts it back; on a target square's piece that isn't the selected piece's colour (outside replay, a target holding an own piece is a castling rook), places there; on another piece the board would let them pick up, selects that one; anything else is a placing the board rejects. **A click on a square** puts the piece back on its own square, and places it anywhere else. Putting back doesn't ask the board.
- **Placing** is `PieceDropped(selectedFrom, square)`, unselecting first, so a rejection puts the piece back unselected as for a drop; a promotion waits for `_ChoosePromotion` with the pawn on its square, as for a drop.
- **Losing the selection:** each `_Refresh` and the once-a-second check put the piece back, unselected, once the board no longer shows it on `selectedFrom`, replay has been entered or left, `CanPickUp(selectedFrom)` no longer holds (a reset, the game ending, a flag fall, the turn passing), or the local player uses pickups.
- **What's clickable** (`_UpdateClickables`, after every change, every second and every click): pieces with `DisableInteractive` off only where a click does something for the local player: with nothing selected, the pieces `CanPickUp` allows ("Select"; asked once per side through `_CanPickUpColor`, step 14.14); with one selected, that piece ("Put back"), the pieces on its targets ("Capture", or "Castle" for a rook), and the other pieces they could select instead ("Select"). Square targets are active only while a piece is selected, and only its square ("Put back") and its targets ("Place"), so the board doesn't outline on hover.
- **VR players** never get interacts on pieces or squares; desktop players never get pickups. The view's `inputStyle` (Auto, Pickups or Clicks) overrides this for testing with one VR headset (see [Testing](testing.md#one-vr-client)). To check in 9.3c: `Interact` works on an object whose `VRCPickup` has `pickupable` off (if not, desktop clicks go through a separate collider on the piece).
- **Captured pieces** (step 13.4): in replay, a click on a piece in a captured place selects it there (lifted over its place, "Select"; a second click puts it back, "Put back"), and a click on a square, or on a piece of the other colour, puts it back on the board there (`PlaceCaptured`). With pickups it's picked up from its place and dropped on a square; dropped beside the board it goes back. See [Physical pieces](physical-pieces.md#captured-piece-places-step-124) and [Replay](replay.md#implementation).
- Decided in 9.3c: in replay, where every piece can be picked up, a click on a piece of the selected piece's colour selects it and one of the other colour is a free move capturing it (so a free move onto an own piece takes a click on the square); putting back is local, so it sends no touch; selecting takes the piece's ownership (the pieces lost `VRCObjectSync` in 9.6, so nothing shows it).

## Buttons

The board's buttons are one method each on `ChessBoard` (see [Architecture](architecture.md#logical-board-and-physical-board)), and each has a `Can…` check that says whether it applies. The view shows a button only while its check holds (steps 9.4a/b), and the button's method refuses whenever its check doesn't, so the two can never disagree. The checks for Join, mode, clock and Start come with their methods (step 7.1); the others come before their methods, which arrive with resign (7.5a), draws (7.5b), replay (7.8a), takebacks (7.8b), loading (7.9a), continuing (7.9b) and reset (7.10).

Like the input methods, each check has a plain form for the local player and an `*As` form taking any name; where organizers count, the `*As` form also takes an `organizer` flag (the plain one asks `arbiter.IsOrganizer` for the local player). A button that acts for one colour takes it, so a player in both seats chooses which side resigns, offers or accepts.

| Check | Applies when |
|---|---|
| `CanJoinAs(color, name)` | a reset, lobby or loaded board, and that seat is empty |
| `CanLeaveSeatAs(color, name)` | the lobby or a loaded board, `name` seated as `color` (step 13.1) |
| `CanSetModeAs(name)` | a seated player, in the lobby, the mode not forced |
| `CanSetClockAs(name)` | a seated player, in the lobby or on a loaded board, the clock not forced |
| `CanStartGameAs(name)` | a seated player, in the lobby, both seats filled |
| `CanResignAs(color, name)` | the game running, `name` seated as `color` |
| `CanOfferDrawAs(color, name)` | draws by agreement on, the game running, `name` seated as `color`, no offer pending |
| `CanAcceptDrawAs` / `CanDeclineDrawAs(color, name)` | the other colour's offer pending, draws by agreement still on, the game running, `name` seated as `color` |
| `CanClaimDrawAs(name)` | the game running, `name` seated as the side to move, and `ClaimableDraw()` has a draw |
| `CanEnterReplayAs(name, organizer)` | a game in progress or ended, not in replay, `name` may work the replay controls |
| `CanLeaveReplayAs(name, organizer)` | in replay of a game in progress or ended (not a loaded board), `name` may work the replay controls |
| `CanControlReplayAs(name, organizer)` | in replay, `name` may work the replay controls |
| `CanStepToAs(name, organizer, ply)` | `CanControlReplayAs(name, organizer)`, `ply` from 0 to `moveCount` and not the position shown (unless that's custom) |
| `CanRequestTakebackAs(color, name)` | takebacks on, no request pending, `name` seated as `color` but not in both seats, and the game can continue from the replay position |
| `CanAcceptTakebackAs` / `CanDeclineTakebackAs(color, name)` | the other colour's request pending for the ply still shown, takebacks still on, `name` seated as `color` but not in both seats, and the game can continue from the replay position |
| `CanContinueAs(name)` | a loaded board in replay on a game position (not custom, the last one included) that isn't over already (it has legal moves, and no automatic draw the snapshot has on applies to it), both seats filled, `name` seated; or, in a game in progress or ended (step 13.3), `name` in both seats, takebacks on, and the game can continue from the replay position |
| `CanResetAs(name, organizer)` | the board isn't reset, and `name` is seated or an organizer, or the board has been idle for the reset-available time |
| `CanLoadGame()` | the board is reset |

- **The game running** means in progress (`LifecycleInGame`) with the side to move's flag not fallen. Resigning, draw offers and claims aren't moves, so they work in replay too.
- **`ClaimableDraw()`** is the draw the side to move could claim: `EndingThreefoldRepetition` when the position has occurred three times and that claim is on in the snapshot, otherwise `EndingFiftyMoveRule` when the halfmove clock has reached 100 and that claim is on, otherwise `EndingNone` (always under "No draws"). `ClaimDrawAs` ends the game with it ([Game endings](game-endings.md#implementation)).
- **Who may work the replay controls:** a seated player or an organizer, or anyone on a board where nobody is seated (a loaded game nobody has joined), since the ownership rule only limits who isn't seated while someone is (see [Ownership](ownership.md)). A bystander gets no way in once the players have gone idle: they get the reset button instead (below), and their touches would make the board active again.
- **The game can continue from the replay position** when it's a position of the game history (not custom) with at least one move after it (a takeback takes back at least one move), and the game is running or ended. A loaded game continues with "Continue from here" instead, which needs no agreement and can also start from the last position. So does a player in both seats (step 13.3), from the same positions as a takeback: asking themselves and accepting would be pointless, so they get "Continue from here" in place of the takeback buttons.
- Anyone else may reset the board once it has been idle for the reset-available time ([Inactivity](inactivity.md#implementation)).

### The board's UI (step 12.1)

The buttons below (their `Button*` actions, `_Press…` events, `_PressButton`, `_ButtonApplies`, `_ButtonText`, `_ButtonColor`, `_ClockText`, `clockBaseSteps`/`clockIncrementSteps`, and until 13.10 `historyWall`) and the PGN box were built on `ChessBoardView`; since 12.1 they are `ChessBoardUI`'s (see [Architecture](architecture.md#logical-board-and-physical-board)), and "the view" in the sections below means the UI for them. The UI brings its buttons, the PGN box and the status display up to date on the frame after each change (`_Refresh`, public for tests), its buttons once a second too, and on its own `OnNotationFilled` and `OnImportChanged`; the view no longer does any of it.

### Board buttons

Replaced the two panels of 20-odd buttons (steps 9.4a, 9.4b, 10.2b, 11.2a, 11.2b, 13.1) with panels shown by the board's state and buttons that merge what the board has as separate actions. The board's methods and `Can…` checks are unchanged.

- **Objects:** the **Chess Board UI** prefab ([Prefabs](prefabs.md#chess-board-ui)) holds the panels in `modeSetupPanels`, `resetPanels`, `preGamePanels`, `inGamePanels` and `replayPanels` (Pre-game, In-game and Replay one at each end of the table, in the same place, as only one of them shows; Mode Setup and Reset one each, beside the board), and each button in `buttons`, with its `Button*` action in `buttonActions` and its label in `buttonLabels`. Each button's UI Button calls the UI's `_Press…` event, which goes through `_PressButton(action)`. Each panel is a world-space canvas with `VRCUiShape` and a trigger `BoxCollider` of its size.
- **Colours:** a panel acts for its own end's colour (`_ButtonColor(action)` gives an action's colour, from the action, not the panel), so a player answers and resigns for the side whose end they're at.
- **Shown:** `_UpdateButtons` (on every `_Refresh`, after every press, once a second and when a press stops holding) shows the panels, and in them each button while `_ButtonApplies(action)` holds. A shown button is pressable only where `_ButtonEnabled(action)` says, and greyed out otherwise (its UI Button not `interactable`, its label faded). The once-a-second update covers what changes without the board changing: being an organizer, the board's idle time.

| Panel | Shown while |
|---|---|
| **Mode Setup** | `CanSetMode() \|\| CanSetClock()`; its clock part only while `CanSetClock()` |
| **Reset** | `CanReset()` |
| **Pre-game** | the board is reset or in the lobby (a loaded board is always in replay) |
| **In-game** | a game in progress or ended, outside replay |
| **Replay Controls** | in replay |

Ended games use the In-game panel, whose Resign and Draw then hide, since Replay and Export PGN are the only ways on from there.

| Button | Does | Shown while |
|---|---|---|
| **Mode** | `SetMode`, switching Standard and Chess960; labelled with the mode | `CanSetMode()` |
| **+ / -** (clock) | `SetClock` with the next or previous of `clockBaseSteps`, keeping the increment; below the shortest is no clock; a time between two steps (a loaded game's) goes to the next one that way. Greyed out with no step left | `CanSetClock()` |
| **++ / --** (increment) | `SetClock` with the next or previous of `clockIncrementSteps`. Greyed out with no step left, and with no clock | `CanSetClock()` |
| **Reset** | `ResetBoard()`, on a second press within `ConfirmSeconds` (3 s); the first says `Sure?` | `CanReset()` |
| **Join/Start** (each colour) | `Join(color)` labelled Join where `CanJoin(color)`; otherwise `StartGame()` labelled Start, greyed out until both seats are filled | `CanJoin(color)` (not on a loaded board), or a seated player in the lobby |
| **Leave** (each colour) | `LeaveSeat(color)` | in the lobby, `CanLeaveSeat(color)` |
| **Import PGN** | opens the PGN box to paste into; `Hide PGN` while it shows that | `CanImport()` |
| **Load Here** | `LoadSelectedGame()`, the game picked on a history wall ([Archive](archive.md#loading-a-game-step-102b)) | `CanLoadGame()` and `board.arbiter.HasSelection()` |
| **Resign** (each colour) | `Resign(color)`, on a second press as Reset | `CanResign(color)` |
| **Draw** (each colour) | `ClaimDraw()` where `CanClaimDraw()`; otherwise `AcceptDraw(color)` where the other side has offered; otherwise `OfferDraw(color)` where `CanOfferDraw(color)`; otherwise it says `No!` for `DrawNoSeconds` (1 s) and does nothing | `CanResign(color)`, and draws by agreement, threefold or fifty-move claims on in the snapshot |
| **Decline Draw** (each colour) | `DeclineDraw(color)` | `CanDeclineDraw(color)` |
| **Replay** | `EnterReplay()` | `CanEnterReplay()` |
| **Export PGN** | opens the PGN box with the game's PGN; `Hide PGN` while it shows that. Nothing is sent ([PGN](pgn.md#input--output)) | `moveCount > 0` |
| **`<<` `<` `>` `>>`** | `StepTo` the start, one back, one on, the end. Each greyed out where `CanStepTo` its ply doesn't hold, so the four keep their places | `CanControlReplay()` |
| **Takeback** (each colour) | `ContinueGame()` labelled `Continue from here` where `CanContinue()`; otherwise `AcceptTakeback(color)` labelled `Accept takeback`; otherwise `RequestTakeback(color)` labelled `Take back to here` | any of those checks |
| **Back** (each colour) | `DeclineTakeback(color)` labelled `Decline takeback` where `CanDeclineTakeback(color)`; otherwise `LeaveReplay()` labelled `Back to game`; on a loaded board, which has no game to go back to, `Join(color)` or `LeaveSeat(color)` labelled Join or Leave | any of those checks |
| **I'm here!** | `ConfirmPresence()` ([Inactivity](inactivity.md)) | while the reset warning shows |

- **Replay Controls' status text** (`replayStatusLabels`) is `_ReplayText()`, as the status panel's replay line.
- **Pressed:** a press the board refuses changes nothing. A first press of Reset or Resign returns `InputRejected`, the board not having been asked. `armedUntil[action]` holds until when a press holds, for both `Sure?` and `No!`.
- **Who can reach what:** a player in both seats has each end's colour buttons at that end, and Takeback continues from the replay position in place of a request (step 13.3). Resign, Draw and Decline Draw are on the In-game panel, so a player in replay goes back to the game first; the board still allows them in replay ([above](#buttons)).
- A loaded board's seats are on the Back button because Pre-game doesn't show in replay, and without them nobody could join to continue it.

### Sound effects (step 12.12)

`ChessBoardUI` plays every sound but the view's move sounds ([Physical pieces](physical-pieces.md#sound-effects-step-1212)), from its `audioSource` (a spatial `AudioSource` on **Sounds**, at the UI's origin, the board's centre, set up as the view's):

- **`clickSound`** for every button press (`_PressButton`, and the PGN box's Import and Close), refused or not, a first press of Reset or Resign too, on the presser's client only, at once; the change's own sound follows on the next `_Refresh`. Not for the replay step buttons (`<<`, `<`, `>`, `>>`): their step plays the shown position's move sound, and a click with it would double it.
- **For changes**, on every client, chosen on the `_Refresh` after a change that was alone since the last one and heard (the same rules as the view's: `StartupSilence`, a late joiner's first update silent): `gameStartSound` for Start and Continue from here (`ChangeStarted`, `ChangeContinued`); `notificationSound` for a draw offer or a takeback request made (a `ChangeRequest` that adds one: a decline plays nothing); `gameEndSound` for a game ended by anything but a move (`ChangeEnded`), or `timeOutSound` instead when it's a flag fall (`_IsFlagFall`: ended by a time forfeit, or playing on untimed after the flag fell, step 12.9); `resetSound` for a reset.
- **Game over after a move:** a move that ends the game, or a replay step onto the end of such a game (`_ShowsEndByMove`: checkmate, stalemate, an automatic draw, the king captured; not a resignation, agreement, claim or time forfeit after the last move), waits `checkSound`'s length (`gameEndAt`; the view's check clip, which the UI holds for its length only) and then plays `gameEndSound`, so the view's move or check sound is heard first. Any later sound-bearing change drops a game over still waiting.
- **The time warning** (`_CheckTimeWarningAt`, on every status update): see [Clocks](clocks.md#implementation).
- `muted`, `lastSound` and `soundsPlayed` as the view's, for tests. The **Chess Board UI** prefab (step 14.4) holds `ui-click`, `game-start`, `notification-pop`, `game-end`, `table-reset`, `time-warning`, `time-out` and `move-check` from `Sounds/`.
- Decided in 12.12: one source at the UI's origin rather than one per panel, as the panels stand at both ends and a sound from the middle is as near to both players; game over waits after every game-ending move, not only checkmate, since the view plays a move sound for each (a stalemate's move, then game over), and the plan's "the check sound's length" is used for all of them; the click plays whatever the press does, as the press is heard before its answer.

### TextMeshPro text (step 12.17)

Every text on the board's UI is TextMeshPro, so it stays sharp up close: the button labels, the status panel's texts (`buttonLabels`, `whiteClockLabels`, `whiteNameLabels`, `blackClockLabels`, `blackNameLabels`, `statusLabels`: `TextMeshProUGUI`) and the PGN box's hints, import line (`pgnImportLabel`) and two fields (`pgnField`, `pgnPasteField`: `TMP_InputField`, each with its text in a masked **Text Area**, its viewport, which a long PGN scrolls in). They were UI `Text` and `InputField` before; the fields kept their names. The fields are multi-line (`MultiLineNewline`), the copy field read-only, and rich text is off in both, so a name's `<` shows as typed. Sizes, places, alignment, colours and wrapping are as before (TMP's sizes match the old ones, Liberation Sans having Arial's metrics); the status texts shrink to fit with TMP's auto size, as they did with best fit. The font is TMP's default ([Customizing](customizing.md#text-and-font-step-1217)).
