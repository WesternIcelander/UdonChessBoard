# UI

What a world's own buttons and displays call on a `ChessBoard`: the method behind each of the board's buttons, the `Can…` check that decides whether it shows, the status getters, the import of a pasted PGN, the UI text, and being told of changes. The package's **Chess Board UI** (`ChessBoardUI`) is built on exactly this and is the reference for it: a world can replace it, add to it or leave it out. Read the [API index](README.md) first: it explains the numbers (colours, squares, pieces), the `Input*` return codes, when a call takes effect on a client that doesn't own the board, and the `…As(name)` forms, which a world's UI doesn't use.

Every method here acts for the **local player**: their display name, and whether the arbiter's `SimpleAuth` makes them an organizer. Each input method answers `ChessBoard.InputAccepted` (0) or `InputRejected` (1), and refuses exactly when its `Can…` check is false; nothing here answers `InputPromotionNeeded` (that's a piece drop, [board input](board-input.md)).

## The lifecycle

A board is in one of five states, its readable field `lifecycle` ([board lifecycle](../board-lifecycle.md)):

| `lifecycle` | Value | What it is | Leaves it |
|---|---|---|---|
| `ChessBoard.LifecycleReset` | 0 | No game, empty seats, the standard position. | A join (to the lobby), a load (to loaded). |
| `LifecycleLoaded` | 1 | A loaded game (PGN import or an archived game), always in replay. | Continue from here (to in game), reset. |
| `LifecycleLobby` | 2 | Someone has joined; the seated players choose the mode and clock. | Start (to in game), reset, the last seat left (back to reset). |
| `LifecycleInGame` | 3 | A game in progress. | Its end (to ended), reset. |
| `LifecycleEnded` | 4 | The game is over; its result is shown. | A takeback or Continue from here (to in game), reset. |

The settings the board plays by (whether draws by agreement, takebacks and each draw claim are allowed, a forced mode or clock) are a snapshot of the arbiter's taken when the board leaves the reset state; several checks below depend on it. Read it with `IsGameSettingOn(ChessArbiter.Setting…)` and `GameNumber(ChessArbiter.Number…)`, as [settings](settings.md) describes.

## The buttons

Each row is one of the Chess Board UI's buttons: the board method it calls, and the check it's shown by (the UI's `_ButtonApplies`). A button acting for one colour comes in a pair, one per colour; the Chess Board UI has a panel at each end of the table, each with its own colour's. A player who holds both seats sees both.

| Button | Call | Shown while |
|---|---|---|
| Join White / Join Black | `JoinWhite()` / `JoinBlack()` (or `Join(color)`) | `CanJoin(color)` |
| Leave (each seat) | `LeaveSeat(color)` | `CanLeaveSeat(color)` |
| Mode | `SetMode(mode)`: the UI switches `ChessGame.ModeStandard` ↔ `ModeChess960` | `CanSetMode()` |
| Clock | `SetClock(baseSeconds, incrementSeconds)`: the UI steps to the next of its list | `CanSetClock()` |
| Start | `StartGame()` | `CanStartGame()` |
| Continue from here | `ContinueGame()` | `CanContinue()` |
| Reset | `ResetBoard()` | `CanReset()` |
| Resign (each colour) | `Resign(color)` | `CanResign(color)` |
| Offer draw (each colour) | `OfferDraw(color)` | `CanOfferDraw(color)` |
| Accept draw (each colour) | `AcceptDraw(color)` | `CanAcceptDraw(color)` |
| Decline draw (each colour) | `DeclineDraw(color)` | `CanDeclineDraw(color)` |
| Claim draw | `ClaimDraw()` | `CanClaimDraw()` |
| Replay | `EnterReplay()` | `CanEnterReplay()` |
| Back to game | `LeaveReplay()` | `CanLeaveReplay()` |
| `<<` `<` `>` `>>` | `StepTo(0)`, `StepTo(replayIndex - 1)`, `StepTo(replayIndex + 1)`, `StepTo(moveCount)` | `CanStepTo(` the same ply `)` |
| Take back to here (each colour) | `RequestTakeback(color)` | `CanRequestTakeback(color)` |
| Accept takeback (each colour) | `AcceptTakeback(color)` | `CanAcceptTakeback(color)` |
| Decline takeback (each colour) | `DeclineTakeback(color)` | `CanDeclineTakeback(color)` |
| Load it here | `LoadSelectedGame()` | `CanLoadGame() && board.arbiter.HasSelection()` |
| PGN | none: opens the UI's own box with `GamePgn()` | `moveCount > 0` |
| Load PGN | none: opens the UI's own paste box; its Import button calls `StartImport(text)` | `CanImport()` |

Showing a button only while its check holds is what the Chess Board UI does; a world's UI may instead show a button disabled, or always, since the method refuses anyway. The checks change without the board changing: the time a board has been idle opens Reset to everyone, an organizer can be added, and a clock can run out. The Chess Board UI checks its buttons again once a second as well as on every change.

### Seats

- **`JoinWhite()`, `JoinBlack()`, `Join(color)`** seat the local player. `CanJoin(color)` holds while the seats are open (a reset board, the lobby, a loaded board) and that seat is empty; a board locked to the player who loaded it is theirs to join first (below). The first join on a reset board takes the settings snapshot and enters the lobby; joining a loaded board leaves it loaded. A player may hold both seats. A join also puts the player's preferred piece set (the arbiter's, [piece sets](piece-sets.md)) on their side, or on both while the other seat is empty. Told as `ChangeJoined`.
- **`LeaveSeat(color)`** empties the local player's seat before a game starts (the lobby or a loaded board). A lobby whose last seat is left goes back to reset; a loaded board stays loaded. Nobody can empty someone else's seat, organizers included: they reset. Once a game has started, a reset is the only way out. Told as `ChangeLeft`.
- **`IsSeated(color, name)`** and **`IsPlayer(name)`** say whether a display name holds a seat (names are compared exactly). For the local player, pass `Networking.LocalPlayer.displayName`.

### The lobby: mode, clock and Start

- **`SetMode(mode)`** chooses `ChessGame.ModeStandard` (0) or `ModeChess960` (1). `CanSetMode()`: a seated player in the lobby, with the mode not forced (`IsModeForced()`). A loaded game keeps its own mode.
- **`SetClock(baseSeconds, incrementSeconds)`** chooses the time control: `ChessArbiter.NoClock` (−1) with increment 0 for none, or a base of 1 s to `ChessBoard.MaxBaseTime` (86400) with an increment of 0 to `MaxIncrement` (3600); anything else is rejected (`ChessBoard.IsValidTimeControl(base, increment)` checks it). `CanSetClock()`: a seated player in the lobby or on a loaded board, with the clock not forced (`IsClockForced()`), and not refused by the loader's lock. The Chess Board UI's Clock button steps through its own `clockBases` / `clockIncrements` lists (Inspector fields); a world's UI offers whatever choices it likes. Both told as `ChangeSettings`.
- **`StartGame()`** starts the game. `CanStartGame()`: the lobby, both seats filled, and the local player seated. A forced mode and clock are applied first; Chess960 gets a random starting position. Told as `ChangeStarted`.

### Resigning and draws

These aren't moves, so they work in replay too. They need the game **running**: in progress, with the side to move's time not run out.

- **`Resign(color)`**: the local player resigns for `color`, whose seat they hold (a player in both seats chooses which side). The other side wins. Told as `ChangeEnded`.
- **`OfferDraw(color)`**: `CanOfferDraw(color)` needs the snapshot's `ChessArbiter.SettingDrawAgreement`, the seat, and no offer pending. The offer is `drawOfferColor` until the other side accepts or declines it, or makes a move (which declines it); the game ending drops it. Told as `ChangeRequest`.
- **`AcceptDraw(color)`**, **`DeclineDraw(color)`**: for the side the offer was made to (`drawOfferColor` is the other colour). Accepting draws the game (`ChangeEnded`); declining clears the offer (`ChangeRequest`).
- **`ClaimDraw()`**: the side to move's player claims the draw `ClaimableDraw()` returns: `ChessGame.EndingThreefoldRepetition`, else `EndingFiftyMoveRule`, each only when the snapshot allows that claim, else `EndingNone` (nothing to claim, and `CanClaimDraw()` is false). The Chess Board UI labels the button by it. Told as `ChangeEnded`.

The automatic draws (fivefold repetition, the seventy-five-move rule, a dead position), checkmate and stalemate end the game with the move that makes them (`ChangeMove`); a flag fall ends it when the board's owner sees the time run out (`ChangeEnded`). [Game endings](../game-endings.md) has the rules.

### Replay and takebacks

Replay shows a position of the game on the board without changing the game, and the clock keeps running ([replay](../replay.md)). Who may work the replay controls: an organizer, a seated player, or anyone on a board nobody has joined; on a board locked to its loader, the loader and organizers.

- **`EnterReplay()`** (a game in progress or ended, not in replay) shows the current position; **`LeaveReplay()`** goes back to the game. A loaded board is always in replay and can't leave it. Told as `ChangeEnteredReplay` / `ChangeLeftReplay`.
- **`StepTo(ply)`** shows the position before move `ply`: 0 the starting position, `moveCount` the current one. `CanStepTo(ply)` is false outside replay, for a ply outside 0 … `moveCount`, and for the position already shown, except after free moves (stepping to the same ply then shows the game's position again). Told as `ChangeReplayStep`.
- **`RequestTakeback(color)`** asks to continue the game from the position replay shows. `CanRequestTakeback(color)`: the snapshot's `SettingTakebacks`, no request pending, the local player holds that seat and not both, and the position is one of the game's (no free moves since a step) with a move after it, in a game running or ended. The request is `takebackColor` and `takebackPly`; stepping or a free move cancels it. Told as `ChangeRequest`.
- **`AcceptTakeback(color)`**, **`DeclineTakeback(color)`**: for the other side, while the replay still shows the requested ply. Accepting continues the game from there as a new game (a game in progress is logged as unfinished first), back in progress and out of replay, told as `ChangeTakeback`; declining clears the request (`ChangeRequest`).

### Continue from here

**`ContinueGame()`** plays on from the position replay shows, as a new game. `CanContinue()` holds in two cases:

- **A loaded game**: both seats filled, the local player seated, and the position shown is one of the game's that isn't over already (it can be the last one). The clock is the one chosen in `SetClock` (or the forced one), the mode stays the loaded game's, both clocks start from the base time. Told as `ChangeContinued`.
- **A game in progress or ended, for one player in both seats** (who has nobody to ask for a takeback): the snapshot allows takebacks, and the position shown is one a takeback could be asked for (one of the game's, with a move after it, the game running or ended). It continues as an accepted takeback does, and is told as `ChangeTakeback`.

### Reset

**`ResetBoard()`** clears the board and both seats (a game in progress is logged as unfinished, or on time if its flag has fallen). `CanReset()`: the board isn't reset already, and the local player is seated, an organizer, or anyone once the board has been idle for the snapshot's reset-available time ([inactivity](../inactivity.md)). On a board locked to its loader, the loader takes the seated players' place. Told as `ChangeReset`. The board's owner also resets an idle board by itself after the auto-reset time.

### Loading a game, and who holds a loaded board

A game can only be loaded onto a reset board (`CanLoadGame()`), by anyone, two ways:

- **From a history wall: `LoadSelectedGame()`** loads the game the local player picked on a history wall (the arbiter's pick, `board.arbiter.HasSelection()`; [history](history.md)), then clears the pick. A game whose archive holds its moves loads at once (`ChangeLoaded`); a personal game saved in an earlier visit is imported from its PGN instead, as below. The pick changes without the board changing: the board passes it on as `OnArbiterChanged(board, ChessArbiter.ChangeSelection)`, so a Load it here button can show at once.
- **From pasted PGN: `StartImport(text)`**, below.

The player who loads a game holds the board: their name is the readable `loadedBy`, and while they hold no seat the board is **locked to them** (`IsLockedToLoader()`, `IsLoader(name)`): only they may join, choose the clock, step, make free moves and reset (organizers may step, make free moves and reset too, but don't join for them), and everyone else may only leave a seat they hold, or reset once the board is idle. Once the loader takes a seat the lock is gone and anyone may join the other seat. `loadedBy` is cleared when the game is continued or the board reset.

### PGN out and in

- **Copy: `GamePgn()`** returns the game's PGN as it stands (tags, a blank line, the moves; a loaded game with its own players and time control). Missing notation is worked out first, so it's complete. Nothing changes on the board: showing it is the UI's own business, for the local player only. The Chess Board UI shows it in a text field to copy from while `moveCount > 0`, and writes it again on each change and on `OnNotationFilled`.
- **Paste: `StartImport(text)`** reads the PGN text over the next frames on this client and loads it onto the board when it's done. `CanImport()`: `CanLoadGame()` and the board has its `importer`. Accepted means the import started, not that it loaded; starting another replaces it. Nothing is sent and nothing on the board changes until it loads, when the board is loaded by the local player and sent as one change (`ChangeLoaded`), held by them as above. Only the importing client knows of an import.

## Import progress

The import's state on this client is in readable fields, and the board calls every listener's `OnImportChanged()` when it changes:

| `importStatus` | Value | Meaning |
|---|---|---|
| `ChessBoard.ImportNone` | 0 | No import yet on this client. |
| `ImportRunning` | 1 | Under way: `board.importer.MoveCount()` is the moves read so far (the Chess Board UI updates it every frame, since progress isn't told). |
| `ImportLoaded` | 2 | Loaded onto the board. Its `OnBoardChanged` (`ChangeLoaded`) comes first. |
| `ImportFailed` | 3 | Failed: `importResult` says why, `importFailedAt` at which move (`"2..."`, `""` when not at a move). |
| `ImportAbandoned` | 4 | The board changed before it finished. |

`importResult` is a `ChessPgn.Import*` code: `ImportBadSyntax` (2), `ImportIllegalMove` (3), `ImportAmbiguousMove` (4), `ImportBadFen` (5), `ImportTooLong` (6, more than `ChessBoard.ImportCapacity`, 1024, plies), or `ImportDone` (1) for a game read whole that the board then refused to load. It's `ImportMore` (0) while nothing has failed. The Chess Board UI turns these into the `import_…` texts.

**What abandons an import.** An import can only be loaded onto the reset board it started on, so every change to the board before it finishes abandons it, whether made on this client or received from another, **except** three that leave the position as it was:

- a rejection (`ChangeRejected`: a piece input refused, which changes nothing);
- a touch (`ChangeActivity`: someone picked up a piece and put it back, received from another client);
- a piece set change (`ChangePieceSet`).

So a UI showing import progress keeps showing it through a piece set change, and learns of an abandon through `OnImportChanged`, not by guessing from `lastChange`.

## Status

Read in `OnBoardChanged` (and on a timer for the clocks):

- **Whose turn:** `SideToMove()` (the game's, whatever replay shows); `IsInCheck()` (the side to move, in the game).
- **Seats and names:** `whitePlayer`, `blackPlayer` (`""` for an empty seat); a loaded game's own players and result tag: `replaySourceWhite`, `replaySourceBlack`, `replaySourceResult` (`""` for none).
- **Mode and clock:** `mode`; `baseTime` and `increment` (seconds, `baseTime` `ChessArbiter.NoClock` for none); `HasClock()`; `IsClockRunning()` (a game with a clock in progress; replay doesn't stop it).
- **Clocks:** `RemainingTime(color)` in milliseconds, never below 0, counting down for the side to move while the clock runs: work it out again on a timer (the Chess Board UI every 0.1 s while a clock runs), since nothing is told as time passes. `FlaggedColor()` is the side whose time has run out (`NoColor` for none): the moment it isn't `NoColor` the game is over on time for everyone, moves and the buttons that need a running game are refused, and the board's owner ends it a moment later (`ChangeEnded`). `ClocksFromPreviousGame()`: the game plays on untimed after a flag fall (a setting); the remaining times are then the timed game's, the side that ran out at 0.
- **Result:** `ending` (a `ChessGame.Ending*` value, `EndingNone` while the game is in progress) and `winner` (`ChessRules.White`, `Black`, or `NoColor` for a draw or no result); `ResultText()` gives `"1-0"`, `"0-1"`, `"1/2-1/2"` or `"*"`. An ended game and a loaded one have them; a loaded game without a result has `EndingNone` (or `EndingUnknown` with the result tag's winner when its ending isn't known). The text for players is `ChessGame.EndingKey(ending, winner)`, a key of the language pack (below).
- **Pending:** `drawOfferColor` (the colour offering a draw), `takebackColor` and `takebackPly` (the colour asking and the ply it asks to continue from); `ChessRules.NoColor` for none. `ClaimableDraw()` for a draw the side to move could claim.
- **Replay:** `replayMode`; `replayIndex`, the ply shown (0 … `moveCount`); `replayCustom`, whether free moves have changed it since the last step. `moveCount` is the number of moves played; `notation[i]` is the SAN of move `i` (0-based, below `moveCount`), `""` while it's still being worked out (`OnNotationFilled` says when it has been). `startMoveNumber` is the move number of the first move.
- **Game IDs:** `gameId`, and `continuedFrom` (`"<game ID>/<ply>"` for a game continued from another, `""` otherwise) ([game IDs](../game-ids.md)).

### Readable state

These public fields of `ChessBoard` are API to **read**, never to write (writing one bypasses the board's checks and sync, and the next update overwrites it); so are the position's arrays [board input](board-input.md#reading-the-position) names (`board`, `state`, `replayBoard`, `moveHistory`, `halfmoveClock`, `moveNumber`). Any other `[NonSerialized]` field isn't API ([index](README.md#whats-supported)):

`lifecycle`, `mode`, `baseTime`, `increment`, `whitePlayer`, `blackPlayer`, `loadedBy`, `ending`, `winner`, `drawOfferColor`, `takebackColor`, `takebackPly`, `replayMode`, `replayIndex`, `replayCustom`, `moveCount`, `notation` (entries below `moveCount`), `startMoveNumber`, `replaySourceWhite`, `replaySourceBlack`, `replaySourceResult`, `gameId`, `continuedFrom`, `lastChange`, `lastChangeReceived`, `importStatus`, `importResult`, `importFailedAt`.

Of the Inspector fields, a UI reads `arbiter` (for `HasSelection()`) and `importer` (for `MoveCount()`).

## Being told of changes

Derive from `ChessBoardListener` and add the listener to the board: `board.AddListener(this)` in `Start` (adding twice changes nothing), or list it in the board's **Listeners** in the Inspector. `board.RemoveListener(this)` stops it. Override the methods you need:

- **`OnBoardChanged()`**: once per change, after it, on every client: the caller's during the call, the others' when the update arrives, a late joiner's on its first update. `board.lastChange` says what it was, and `board.lastChangeReceived` whether it came from another client. Redraw from the getters here.
- **`OnNotationFilled()`**: the board worked out missing SAN by itself (after a received update or a load, over a few frames); only `notation` changed, and `lastChange` stays as it was. Redraw a move list or a PGN shown.
- **`OnImportChanged()`**: on the importing client only, when an import starts, loads, fails or is abandoned (above).
- **`OnArbiterChanged(board, change)`**: the board's arbiter changed: `ChessArbiter.ChangeSettings` (its settings, which a running game's PGN Event and Site follow) or `ChangeSelection` (the local player picked or unpicked a game on a history wall). Nothing on the board changed.

`lastChange` is one of these `ChessBoard.Change*` constants:

| Constant | Value | Told for |
|---|---|---|
| `ChangeNone` | 0 | Nothing since the board started. |
| `ChangeRejected` | 1 | A piece input refused (local only): a view puts the piece back. |
| `ChangeReset` | 2 | A reset, by a player or by itself. |
| `ChangeJoined` | 3 | A join. |
| `ChangeSettings` | 4 | Mode or clock chosen, or a re-snapshot. |
| `ChangeStarted` | 5 | Start. |
| `ChangeMove` | 6 | A move, also one that ends the game. |
| `ChangeEnded` | 7 | The game ended other than by a move: resignation, a draw agreed or claimed, a flag fall. |
| `ChangeRequest` | 8 | A draw offer or takeback request made or declined. |
| `ChangeEnteredReplay` | 9 | Replay entered. |
| `ChangeLeftReplay` | 10 | Replay left. |
| `ChangeReplayStep` | 11 | A replay step. |
| `ChangeFreeMove` | 12 | A free move in replay, or a captured piece put back. |
| `ChangeTakeback` | 13 | A takeback accepted, or one player in both seats continuing. |
| `ChangeLoaded` | 14 | A game loaded. |
| `ChangeContinued` | 15 | A loaded game continued. |
| `ChangeActivity` | 16 | Only a touch: received from another client, never told on the toucher's own. Nothing to redraw. |
| `ChangeLeft` | 17 | A seat left (also when it resets the lobby). |
| `ChangePieceSet` | 18 | A side's piece set changed; the position is the same. |

A UI usually redraws everything on any change and uses `lastChange` only for effects: the Chess Board UI plays its sounds by it (a draw offer's notification only when `drawOfferColor` has just appeared, since `ChangeRequest` is also a decline). Several changes can arrive between two frames; the Chess Board UI redraws once, on the next frame.

## Text

The package's UI keeps no text in code: every string comes from the **Chess Language** object (`SiggiUSTools.I18n`) by key, from the language packs under `Assets/Siggi/ChessBoard/Languages/` (`en.json` the English one). A world's own UI shows the same texts by giving its behaviour an `I18n i18n` field set to the Chess Language object, reading each string with `ChessUtil.Text(i18n, key)` (the key itself if the field is empty), filling placeholders with `I18n.Format(text, new string[] { "name", value, … })`, and reading them again when the player changes language: register with `i18n.AddLanguageChangeListener(this)` and give the behaviour a `public void OnLanguageChange()`. The Chess Board UI's button labels are `button_…` keys (`button_join_white`, `button_resign`, …), its status lines `status_…`, `player_…` and `replay_…`, its PGN box's `pgn_…` and `import_…`, and how a game ended is `ChessGame.EndingKey(ending, winner)`. [Text](text.md) lists every key with its placeholders.

```csharp
using SiggiChessBoard;
using SiggiUSTools;
using UdonSharp;
using UnityEngine;

// A Resign button of the world's own for one colour, shown only while the local player may press it.
public class MyResignButton : ChessBoardListener {
    public ChessBoard board;
    public int color = ChessRules.White;
    public I18n i18n;
    public GameObject button;
    public TMPro.TextMeshProUGUI label;

    void Start() {
        board.AddListener(this);
        i18n.AddLanguageChangeListener(this);
        OnLanguageChange();
        OnBoardChanged();
    }

    public void OnLanguageChange() {
        label.text = ChessUtil.Text(i18n, "button_resign");
    }

    public override void OnBoardChanged() {
        button.SetActive(board.CanResign(color));
    }

    // The UI Button's OnClick, through SendCustomEvent.
    public void Press() {
        board.Resign(color);
    }
}
```

A button whose check can change without a change to the board (Reset, which opens to everyone once the board is idle; anything a running-out clock ends) is checked again on a timer too, as the Chess Board UI does once a second.
