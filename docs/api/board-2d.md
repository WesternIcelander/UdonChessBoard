# 2D view

The 2D board view from a world's own scripts: choosing which board it shows, stepping its own replay, its move list, restyling it, and the movable holder. It's also the package's clearest example of a presentation built as a `ChessBoardListener`, and the last section takes it apart as a pattern for a read-only display of the world's own. Read the [index](README.md) first; how the view looks and behaves for players is in [2D board view](../board-2d.md).

Two behaviours, both in the namespace `SiggiChessBoard`:

- **`ChessBoard2DView`**, on the canvas of the **2D Chess Board View** prefab ([prefabs](../prefabs.md#2d-chess-board-view)): a flat board showing one board's game at a time, with the board's players and status lines, a selector (`<`, **Board N**, `>`), step buttons (`<<` `<` `>` `>>`) and a move list. Nothing on it acts on a board: every input changes only what the view shows.
- **`ChessBoard2DViewHolder`**, the root of the **Movable 2D Chess Board View** prefab ([prefabs](../prefabs.md#movable-2d-chess-board-view)), whose nested view is a **2D Chess Board View**: lets players move and resize the view. The view knows nothing of it.

Hold a reference to either in an Inspector field of your behaviour (`public ChessBoard2DView view;`). The view has no listeners of its own and tells nobody when it switches board or steps: a script that follows it reads its getters (below), on a timer or each frame.

## Choosing the board

Set in the Inspector:

| Field | What it does |
|---|---|
| **Boards** (`boards`) | The boards the view may show, in the order the selector steps through them. Left empty (and without a **Locked Board**), the [wiring script](../prefabs.md#wiring) fills it with every board in the scene by board number; a list that's set stays as it is. |
| **Locked Board** (`lockedBoard`) | Set: the view shows only this board (a view beside its table), the selector's `<` and `>` are hidden, and stepping between boards is refused. `boards` isn't read then. |
| **Synced** (`synced`) | On: the board chosen and the ply shown are synced, so everyone sees the same, late joiners included (below). Off: each player chooses for themselves and nothing is sent. Set it in the Inspector only: the movable holder's `VRCObjectSync` is matched to it before the build. |
| **I18n** (`i18n`) | The Chess Language object, for the view's texts. Left empty, the `I18n` sanity check fills it in. |

**Where it starts:** a locked view on its locked board; an unsynced one on the first board in `boards` (whether in use or not); a synced one on the board last chosen by anyone, which a late joiner switches to once the view's synced state arrives (it shows the first board until then). A view whose `boards` is empty and that isn't locked logs `[ChessBoard2DView] <view>: the Boards list is empty.` on `Start` and shows nothing.

Calls on `ChessBoard2DView`:

| Call | What it does |
|---|---|
| `PressLeft()`, `PressRight()` | The selector's buttons: step to the previous or next board in `boards` that isn't reset (`lifecycle != ChessBoard.LifecycleReset`), wrapping round. Nothing when no other board is in use (the board shown isn't left for itself, in use or not) and on a locked view. The new board starts on its latest position. Synced, the press takes ownership of the view (anyone may) and sends the choice to everyone. |
| `Show(index)` | Shows the board at `index` in `boards`, on this client only: not sent even when synced (the next choice received replaces it), and not skipping a reset board. An index outside `boards` keeps the board shown. On a locked view it shows the locked board whatever the index. A board not shown before starts on its latest position; showing the board already shown keeps the ply and redraws. |
| `ShownIndex()` | The index in `boards` of the board shown; −1 for none and on a locked view. |
| `ShownBoard()` | The board shown (the locked board on a locked view); `null` for none. |
| `IsLocked()` | Whether `lockedBoard` is set. |
| `SetLockedBoard(board)` | Locks the view to `board` at run time, as the Inspector's field does; `null` unlocks it, showing the board the synced choice names (synced) or the first in `boards` (unsynced), with the selector back. Nothing is sent: lock every client's view alike, e.g. from an event every client runs. |

A world's own selector calls `PressLeft()` / `PressRight()`, so a synced view stays synced. To show whether a press would do anything, as the view's own buttons don't, check whether another board in `boards` has `lifecycle != ChessBoard.LifecycleReset`. A synced jump straight to a given board isn't part of the API: `Show(index)` is local, and only the selector and the steps send. (Adding a synced form would be a new method, decided with the user first.)

## Its own replay

The view shows a ply of the shown board's game: the position before move `ply`, 0 the start and the board's `moveCount` the latest. It builds that position itself from the board's game, so the board's own replay doesn't move it, and a custom replay position (free moves) is never shown.

| Call | What it does |
|---|---|
| `ViewPly()` | The ply shown. |
| `IsOnLatest()` | Whether it's on the latest position, following the game. |
| `CanShowPly(ply)` | Whether `ShowPly(ply)` would show something else: a board is shown, `0 <= ply <= moveCount`, and `ply` isn't the one shown. |
| `ShowPly(ply)` | Shows the position before move `ply`; `false` (refused, nothing changed) unless `CanShowPly(ply)`. `moveCount` is the latest, which follows the game from then on. Synced, it takes ownership of the view and sends the board and ply to everyone. |
| `PressFirst()`, `PressBack()`, `PressForward()`, `PressLast()` | The step buttons: `ShowPly(0)`, `ShowPly(ViewPly() - 1)`, `ShowPly(ViewPly() + 1)`, `ShowPly(moveCount)`. Refused as `ShowPly` is. |

**Following the game.** On the latest position, a move moves the view on to the new latest. Anywhere earlier it stays, unless the history no longer reaches its ply or has another move there (a takeback or a continuation cutting below it), the ply is the latest again (a takeback to exactly it), or the board was reset or loaded (`ChangeReset`, `ChangeLoaded`): then it goes back to the latest. The view catches up on the frame after a board change, so a world's listener reading `ViewPly()` in its own `OnBoardChanged` may still see the ply from before; read it a frame later.

**Synced.** The ply is sent with the board as one more synced value, `LatestPly` (−1) standing for "the latest", so clients following the game stay on it whichever update reaches them first. `ViewPly()` never returns `LatestPly`: it's always a ply of the game. A ply received before this client's game reaches it (the view's update arriving before the board's) shows the latest until the game's moves go past it. When the view's owner goes back to the latest by itself (a takeback below it, a reset), it sends that.

**The step buttons' constants:** `StepFirst` (0), `StepBack` (1), `StepForward` (2), `StepLast` (3), the indices of the four buttons in the view's `stepButtons` and their labels in `stepLabels`, left to right. The view makes each button pressable only where it steps somewhere: `<<` and `<` when `ViewPly() > 0`, `>` and `>>` when `ViewPly() < moveCount`, which is `CanShowPly` of the ply each would show.

A world's own step buttons for a view, enabled as the view's are (polled, since the view tells nobody):

```csharp
using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// Back and forward buttons of the world's own for a 2D view, each pressable only where it steps somewhere.
public class MyReplayButtons : UdonSharpBehaviour {
    public ChessBoard2DView view;
    public Button backButton;
    public Button forwardButton;

    void Update() {
        int ply = view.ViewPly();
        bool back = view.CanShowPly(ply - 1);
        bool forward = view.CanShowPly(ply + 1);
        if (backButton.interactable != back) backButton.interactable = back;
        if (forwardButton.interactable != forward) forwardButton.interactable = forward;
    }

    // The buttons' OnClick, through SendCustomEvent.
    public void PressBack() {
        view.PressBack();
    }

    public void PressForward() {
        view.PressForward();
    }
}
```

## The move list

Right of the board: a row per move number (from the board's `startMoveNumber`), White's move left and Black's right (a game starting with Black to move has an empty White cell first), each move the board's SAN (`notation[i]`, empty until the board has worked it out and told `OnNotationFilled`). The move into the position shown, move `ViewPly() - 1`, is drawn as the current move (none at the start).

- **A cell pressed** shows the position after its move: move `i` pressed is `ShowPly(i + 1)`, so synced as the step buttons are. Empty cells can't be pressed.
- **Scrolling:** whenever the board or ply shown changes, the list scrolls just enough to bring the current move into view (the top at the start); otherwise a player's own scrolling stays.

The cells' and the scroll rect's handlers (`ChessMoveListCell._Press`, `_PressMoveCell`, `_OnMoveListScrolled`) and the methods for tests (`_MoveCellPly`, `_ScrollMoveList`, …) are internal. A world's own move list reads the board directly: `moveCount`, `notation`, `startMoveNumber` and the side to move ([UI](ui.md#status)), redrawn in `OnBoardChanged` and `OnNotationFilled`, and calls the view's `ShowPly(i + 1)` for a move pressed.

## Restyling

The view's look is in Inspector fields of `ChessBoard2DView`, read on every redraw. Set them as overrides on the placed prefab; a change at run time shows at the view's next redraw (the board changing, a step, or `Show(ShownIndex())`, which redraws at once).

| Field | Default | What it colours |
|---|---|---|
| `lightSquare`, `darkSquare` | light and dark brown | The squares. |
| `lastMoveTint` | yellow | The last move's two squares: blended 60 % over the square's colour. |
| `checkTint` | red | A king in check's square, replacing its colour. |
| `stepLabelColor`, `stepLabelDisabledColor` | black, grey | The step buttons' labels, pressable or not (the buttons' own tint greys their images). |
| `moveCellColor`, `currentMoveColor` | clear, yellow | The move list's cells, and the current move's. |
| `moveTextColor`, `currentMoveTextColor` | white, black | Their text. |

- **Piece sprites:** `whiteSprites` and `blackSprites`, six each, in the order pawn, knight, bishop, rook, queen, king (`ChessRules.Pawn` … `King`, index type − 1). The defaults are the package's PNGs in `Sprites2D/`. An empty entry leaves that piece's squares empty. Each piece image keeps its sprite's aspect.
- **The labels:** the board label (`boardLabel`), players (`playersLabel`), status (`statusLabel`), the step buttons' (`stepLabels`), the move numbers (`moveNumbers`) and the moves (`moveCellLabels`) are TextMeshPro objects of the prefab: their font, size and colour are set on those objects as overrides, except the colours the view sets itself (the step labels' and the moves' above). What they say comes from the language pack ([text](text.md)).
- **The move list's rows:** `moveRowHeight` (50, canvas pixels) is the height of each row as the prefab lays them out (**Row 1** to **Row 28** under the list's **Rows**, each 50 high and placed 50 below the last, enough to fill the list and one more) and the scroll rect's **Scroll Sensitivity** (one row a wheel notch). Changing it means laying the rows out again to match and setting the sensitivity to it; the rows' count and layout are the prefab's, not something the view adjusts.

The other array fields (`squareImages`, `pieceImages`, `stepButtons`, `moveCells`, `moveCellImages`, …) and `moveList`, `moveListViewport`, `moveListContent`, `moveListRows`, `leftButton`, `rightButton` are the prefab's wiring of its own objects: leave them as they are.

## The movable holder

A view in a **Movable 2D Chess Board View** can be moved and resized by players ([moving and resizing](../board-2d.md#moving-and-resizing-step-1216)):

- **Moving:** grabbing the grey bar under the view (the **Grip**, a `VRCPickup`); the view follows it and stays where it's let go. In VR the view turns and tilts with the hand; after a desktop player picks it up it stands upright, turning only with the bar's heading, until a VR player picks it up.
- **Resizing:** holding the small cube at the view's bottom right (the **Corner**, a second pickup) sizes the view by how far along its bottom edge it's held, keeping its aspect, between `minSize` and `maxSize`.
- **In VR** a hand must be within `vrReach` of the bar or the corner to grab it, so reaching for the view's buttons doesn't grab the view.

Inspector fields of `ChessBoard2DViewHolder`:

| Field | Default | What it does |
|---|---|---|
| **Movable** (`movable`) | on | Off: neither pickup can be picked up, and the bar and the corner are hidden. |
| **Min Size**, **Max Size** (`minSize`, `maxSize`) | 0.25, 3 | The size's limits, as shares of the size the view has in the scene. The prefab's bar is fitted to 0.25, so after lowering **Min Size** check that the bar doesn't reach the corner. |
| **Vr Reach** (`vrReach`) | 0.05 | How near (m) a VR hand must be to grab the bar or the corner. Desktop players keep the pickups' own reach. |
| **I18n** (`i18n`) | empty | The Chess Language object, for the bar's and corner's hover texts; filled in by the `I18n` sanity check. |

`view`, `grip`, `gripPickup`, `corner`, `cornerPickup`, `handleRenderers` and `gripModel` are the prefab's wiring.

Calls on `ChessBoard2DViewHolder` (each does nothing before the holder's `Start`):

| Call | What it does |
|---|---|
| `SetMovable(value)` | Turns moving and resizing on or off, as **Movable** does (letting go of a pickup held here). Local: call it on every client to change it for everyone. |
| `SetSize(value)` | Sets the size at once, clamped to `minSize` … `maxSize` (1 is the size in the scene). Synced, it takes ownership of the holder and sends it. |
| `Size()` | The size drawn now (easing towards a size received from another player). |
| `PutBack()` | Puts the view back where it was placed in the scene, at size 1, letting go of the bar if it's held here. Synced, for everyone (taking ownership of the bar's `VRCObjectSync`). Nothing calls it by itself: give it a button of the world's own. |
| `SetUpright(value)` | Whether the view stands upright (on) or turns and tilts with the bar (off). Picking up the bar sets it (on for a desktop player, off for a VR one); synced, it's sent with the size. |
| `IsUpright()` | Whether it stands upright. |

**Synced** is the view's tick, not the holder's: on, the bar's `VRCObjectSync` carries the place to everyone (picking it up takes ownership, as any pickup) and the size and upright flag are the holder's synced fields; off, each player's moves and sizes stay their own. The prefab's bar has no `VRCObjectSync`; before every VRChat build, `ChessBoard2DViewSyncCheck` (an editor script) adds one to each holder's bar whose view is synced and removes it from the others, logging each change (`[ChessBoard2DViewSyncCheck] <holder>: added a VRCObjectSync to the grip, …`). Nothing is told when the view is moved or resized; each let-go and received size is logged (`[ChessBoard2DViewHolder] <holder>: moved to …`, `resized to 1.5 …`, `put back …`).

```csharp
using SiggiChessBoard;
using UdonSharp;

// A "put it back" button of the world's own for a movable 2D view.
public class MyPutBackButton : UdonSharpBehaviour {
    public ChessBoard2DViewHolder holder;

    public override void Interact() {
        holder.PutBack();
    }
}
```

## A read-only display of your own

The 2D view is a `ChessBoardListener` that only reads the board, and the pattern carries over to any display of the world's own (a scoreboard, a spectator screen, a sign over a table):

- **Listen to the board you show, and only to it.** The view calls `board.AddListener(this)` on the board it shows and `RemoveListener(this)` on the one it leaves when it switches, so boards nobody looks at don't call it. It reads another board's `lifecycle` only when a selector button is pressed.
- **Mark, then draw once a frame.** `OnBoardChanged()` only notes that a redraw is due (`dirty`) and what the change calls for (`lastChange` `ChangeReset` or `ChangeLoaded`: back to the latest); `Update` redraws on the next frame. Several changes can arrive between two frames, and this draws them once.
- **Draw everything from the board.** A redraw reads the board's getters and readable fields ([UI](ui.md#readable-state), [board input](board-input.md#reading-the-position)) and sets every text and image from them, with no state of its own about the game, so a change made here, one received and a late joiner's first update all come out the same.
- **`OnNotationFilled()`** redraws what shows moves (the move list and the status's replay line), since SAN can arrive after the change that brought the moves.
- **Time passes without changes.** The clocks run without the board telling anyone, so the view redraws its texts on a timer too: every 0.1 s while `IsClockRunning()` (as the Chess Board UI does), once a second otherwise.
- **A position other than the board's** is built on arrays of its own: the view copies the board's `board` and `state` into a `byte[64]` and an `int[ChessPosition.StateLength]` and undoes the moves after its ply with `ChessPosition.UnmakeMove(position, state, moveHistory[i])`, from the last move down. It never reads `PieceAt`, which follows the board's replay. Rebuilding from the latest each redraw costs a few milliseconds on a 300-move game and can't fall out of step with a takeback or a load ([rules](rules.md)).
- **Texts from the language pack,** written again on `OnLanguageChange` ([text](text.md)).

A scoreboard over one board, as a pattern:

```csharp
using SiggiChessBoard;
using SiggiUSTools;
using UdonSharp;
using UnityEngine;

// The players, the move count and the result of one board, redrawn once a frame after a change.
public class MyScoreboard : ChessBoardListener {
    public ChessBoard board;
    public I18n i18n;
    public TMPro.TextMeshProUGUI playersText;
    public TMPro.TextMeshProUGUI resultText;
    bool dirty = true;

    void Start() {
        board.AddListener(this);
        i18n.AddLanguageChangeListener(this);
    }

    public override void OnBoardChanged() {
        dirty = true;
    }

    public void OnLanguageChange() {
        dirty = true;
    }

    void Update() {
        if (!dirty) return;
        dirty = false;
        if (board.lifecycle == ChessBoard.LifecycleReset) {
            playersText.text = "";
            resultText.text = "";
            return;
        }
        playersText.text = board.whitePlayer + " – " + board.blackPlayer;
        if (board.ending == ChessGame.EndingNone) {
            resultText.text = board.moveCount + " moves";
        } else {
            resultText.text = board.ResultText() + "\n"
                + ChessUtil.Text(i18n, ChessGame.EndingKey(board.ending, board.winner));
        }
    }
}
```

For one display over several boards, keep the board shown in a field and move the listener as the view's `Listen` does: `RemoveListener(this)` on the old board, `AddListener(this)` on the new, then redraw.
