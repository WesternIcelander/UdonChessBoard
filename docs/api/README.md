# API

How a world's own UdonSharp scripts use the chess package: its own buttons and displays, its own piece sets, the rules without a board. These documents define the package's public API (step 15); from the first release on it keeps working across updates, as [compatibility](../compatibility.md) (rule 6) says. This page holds what every document would otherwise repeat; read it first.

## The documents

- [UI](ui.md): what to call when a player presses each of the board's buttons, the `Can…` / `Is…` checks that decide whether a button shows, the status getters, and being told of changes (`ChessBoardListener`). For a world replacing or adding to the **Chess Board UI**.
- [Board input](board-input.md): the board and its pieces outside the UI: reading the position and the moves, telling the board a piece was picked up, dropped or promoted, and which change was which, for a world's own pieces or animations.
- [Settings](settings.md): the arbiter's instance defaults, per-board overrides and a board's snapshot, changing them from a script, who may (organizers, `SimpleAuth`), and noticing a change.
- [History](history.md): the archives (`GameArchive`) and their games, deleting, the pick of an archived game and loading it onto a board, and what's logged when.
- [Piece sets](piece-sets.md): a step-by-step guide to a chess piece set of the world's own, from importing the models to players choosing it.
- [2D view](board-2d.md): the 2D board view from a world's scripts (which board, its own replay, restyling, the movable holder), and as a pattern for a read-only display of the world's own.
- [Rules](rules.md): the static rules on arrays of the world's own, without a board: FEN, legal moves, making moves, check and mate, draws, SAN and PGN.
- [Text](text.md): every text key of the language pack, where it shows, and how a world's own UI shows the chess texts.

## The objects and who references whom

A world places the [prefabs](../prefabs.md); each behaviour below is the one a script talks to. Every class is in the namespace `SiggiChessBoard`.

| Object (prefab) | Behaviour | References (Inspector field) | Referenced by |
|---|---|---|---|
| **Chess Arbiter** | `ChessArbiter`: the instance's settings, organizers, piece sets and the pick of an archived game | its `SimpleAuth` (on the same object), `pieceSets` (the nested **Chess Pieces** first), `archives` | every board's and the settings menu's `arbiter` |
| **Chess Board** (in each **Chess Table**) | `ChessBoard`: one board's game, rules, seats and synced state | `arbiter`, `importer` (its child `ChessPgnImporter`), `listeners` | the view's and UI's `board`, the settings menu's and 2D views' `boards`, a 2D view's `lockedBoard` |
| **Chess Board View** | `ChessBoardView`: the 3D board, pieces, highlights and sounds | `board`, `i18n` | (none) |
| **Chess Board UI** | `ChessBoardUI`: the buttons, status panels and PGN box | `board`, `i18n` | (none) |
| **Game Archive**, **Personal Game Archive** | `InstanceGameArchive` (synced, the instance's last 50 games), `PersonalGameArchive` (local, the player's own games across visits), both a `GameArchive` | (its holders) | the arbiter's `archives`, a history wall's `archive` |
| **Game History Wall**, **Personal History Wall** | `GameHistoryWall`: an archive's list of games, export and pick | `archive`, `i18n` | (none) |
| **Chess Settings Menu** | `ChessSettingsMenu`: the organizers' in-world settings | `arbiter`, `boards`, `i18n` | (none) |
| **2D Chess Board View** (also inside the **Movable 2D Chess Board View**) | `ChessBoard2DView`: a flat view of one board at a time, with its own replay and move list | `boards` or `lockedBoard`, `i18n` | the holder's `view` |
| **Movable 2D Chess Board View** | `ChessBoard2DViewHolder`: moves and resizes its view | `view`, `i18n` | (none) |
| **Chess Language** | `SiggiUSTools.I18n`: the language packs every chess text comes from | its packs | every `i18n` field above |

- **Listeners.** The view, the UI and the 2D view are `ChessBoardListener`s: each adds itself to its board's listeners when it starts (`board.AddListener(this)`) and draws from the board's getters whenever the board tells it of a change. They don't know each other, so any of them can be left out or replaced by a world's own listener. A world's listener is added the same way, or listed in the board's **Listeners** in the Inspector.
- **Who tells whom.** A board tells its listeners of its own changes (`OnBoardChanged`, `OnNotationFilled`, `OnImportChanged`). The arbiter tells every board of a change to its settings or to the pick, and each board passes it on to its listeners (`OnArbiterChanged(board, change)`). The arbiter and the archives have no listeners of a world's own: a world's script that watches them polls their `changes` counter, as the settings menu does ([settings](settings.md), [history](history.md)).
- **Filled in for you.** The wiring script fills in the empty references between them (each board's and the menu's `arbiter`, the arbiter's `archives`, the walls' `archive`, the menu's and 2D views' `boards`) when the scene is saved, when Play Mode starts and before every build; the `i18n` fields are the `I18n` sanity check's ([prefabs](../prefabs.md#wiring)). A world's own script's references to them are its own to set.

## Numbers shared by every document

The constants are on `ChessRules` (`Rules/ChessRules.cs`) unless named otherwise.

- **Colours:** `ChessRules.White` (0), `ChessRules.Black` (1), `ChessRules.NoColor` (−1) for none (no winner, no draw offer, no colour). `ChessRules.OtherColor(color)` gives the other.
- **Squares:** 0 to 63, a1 first, along the rank and then up: square = rank × 8 + file, so a1 = 0, h1 = 7, a2 = 8, e4 = 28, h8 = 63. Files and ranks are 0 to 7 (`FileA` … `FileH`, `Rank1` … `Rank8`), every square has its constant (`ChessRules.E4`), and `ChessRules.NoSquare` (−1) is none. `SquareOf(file, rank)`, `FileOf(square)`, `RankOf(square)`, `SquareName(square)` (`"e4"`) and `ParseSquare("e4")` convert.
- **Piece types:** `Pawn` (1), `Knight` (2), `Bishop` (3), `Rook` (4), `Queen` (5), `King` (6); `NoPieceType` (0) for none.
- **Pieces:** a piece on a square is its type plus 8 for Black: `WhitePawn` (1) … `WhiteKing` (6), `BlackPawn` (9) … `BlackKing` (14), `Empty` (0) for an empty square. `PieceOf(color, type)` makes one, `PieceTypeOf(piece)` and `PieceColorOf(piece)` (`NoColor` for `Empty`) take it apart. The board's getters return a piece as an `int` (`PieceAt`); the rules' arrays hold them as `byte`s.
- **None is −1** wherever a colour, square or index can be missing (`NoColor`, `NoSquare`, a piece set override cleared with −1), and `0` where the value is a type or piece (`NoPieceType`, `Empty`) or a packed move (no move).
- **Moves are packed `int`s:** from square, to square, the moving type, the promotion and captured types, a castling bit, and the castling rights and en passant square before the move, each in its own bits ([move encoding](../move-encoding.md)). Read them with `ChessRules.MoveFrom(move)`, `MoveTo`, `MovePieceType`, `MovePromotion`, `MoveCaptured`, `MoveIsCastle`, `IsEnPassantCapture`, …, or shift and always mask: `(move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask`. A castling move goes from the king's square **to the rook's** (king takes own rook, so Chess960 castles too); the king lands on g or c, the rook on f or d. A list of moves is an `int[]` and a count: the methods that fill one take the array and return how many they wrote, and an array of `ChessMoves.MaxMoves` (320) holds any position's moves.

## Calls and when they take effect

The board's methods that change something (join, start, a move, resign, reset, …) are **input methods**: each checks whether the player may do it, and if so changes the board and sends the change. They return `ChessBoard.InputAccepted` (0), `InputRejected` (1) or, for a pawn reaching the last rank, `InputPromotionNeeded` (2); each has a `Can…` check that answers the same question without changing anything, for deciding whether a button shows. The documents say which method returns what.

On whichever client a script calls them, owner of the board or not:

- **Accepted, it's applied here at once.** The board changes on the caller's client before the method returns: its getters already show the change. No waiting for ownership or the network first.
- **The caller's listeners are told during the call.** The board sets `lastChange` (a `ChessBoard.Change*` constant saying what happened) and calls every listener's `OnBoardChanged()` once, before returning. A refusal tells nobody, except a rejected piece input (picking up, dropping, promoting), which tells the listeners with `ChangeRejected` so a view puts the piece back.
- **Then it's sent.** The caller's client takes ownership of the board (`Networking.SetOwner`) if it doesn't hold it, and asks VRChat to send the board's state (manual sync, `RequestSerialization`); several changes in one frame go out as one update. Other clients receive it a moment later, usually within a fraction of a second, depending on the network: each applies the whole state and tells its listeners with the same `lastChange`, `lastChangeReceived` set (it's `false` for a change made on that client). A player joining later gets the last state sent and is told of it the same way. Nothing is sent for a refusal or a rejection.
- **So `OnBoardChanged` is how a change shows up everywhere**, the caller's client included. A world's display redraws from the board in `OnBoardChanged`, not after its own call: that one place covers its own players' changes, everyone else's and late joiners alike. Use the return value only for what the caller does next (a sound on rejection, asking for the promotion piece).
- **Races.** Two clients acting at once each apply their own change; the update arriving last wins everywhere. The board also refuses its ownership to bystanders while both players are in the instance ([ownership](../ownership.md)), which only matters in a race (a seat changing in between): the refused client's change stays local, isn't sent, and the owner's next update replaces it. The `Can…` checks already refuse a bystander, so this doesn't happen in normal use.
- **Not every change comes from a call.** The board's owner ends a game on time, resets an idle board by itself, and the board fills in missing notation (`OnNotationFilled`): another reason to draw in the listener.

The other synced objects differ. The arbiter's setters only change the calling client's copy: an organizer's change is wrapped in `BeginChange()` (taking ownership of the arbiter, refused for anyone else) and `EndChange()` (sending it), as [settings](settings.md) shows. The instance archive is written by its owner alone, as games are logged ([history](history.md)). The personal archive is local and sends nothing.

### The `…As(name)` forms

Most input methods and `Can…` checks have a form ending in `As`, taking the player to act for by display name, and for some an `organizer` flag (`JoinAs(color, name)`, `ResetBoardAs(name, organizer)`, `CanResignAs(color, name)`, …). The plain form calls it with the local player's display name and whether the arbiter's `SimpleAuth` makes them an organizer: `ResetBoard()` is `ResetBoardAs(<local player's name>, <local player is an organizer>)`.

A world's own UI uses the **plain forms**. The `As` forms decide as if that player had acted, but the change is still made and sent by the local client, and nothing checks that the name is the local player's: a script calling them could seat anyone or reset as an organizer. They're there for the package's tests and debug view, which act for several players on one client. A world calling one with another player's name also runs into the ownership rule above: the local player, not the named one, takes the board.

## Calling from UdonSharp

- **Hold a reference set in the Inspector.** Give your behaviour a public field of the type (`public ChessBoard board;`) and drag the placed object into it; there's no `new` and no finding by name. A behaviour of yours isn't wired by the package's wiring script.
- **Static methods are called on their class:** the rules (`ChessFen.ParseFen(…)`, `ChessRules.SquareName(…)`) take everything through parameters and need no object.
- **Return codes, not exceptions.** UdonSharp has no `try` / `catch`, so nothing here throws: a refused call returns `InputRejected` or `false`, and a method that fills arrays returns a count or a `bool` with `out` values ([udon-constraints](../udon-constraints.md)).
- **Listen by deriving from `ChessBoardListener`** and overriding the methods you need; the others do nothing.
- **Read the board, don't write it.** The documents name some of the board's public fields as readable state (such as `lifecycle`, `ending`, `winner`, `replayMode`, as [UI](ui.md#readable-state) lists them, and the position's arrays in [board input](board-input.md#reading-the-position)). Writing one bypasses the board's checks and sync, and the next update overwrites it.
- **Calls into another behaviour are slow** in Udon. Read what a redraw needs once in `OnBoardChanged` rather than every frame, and keep a rules search on local arrays.

```csharp
using SiggiChessBoard;
using UdonSharp;
using UnityEngine;

// A sign showing whose turn it is on one board.
public class TurnSign : ChessBoardListener {
    public ChessBoard board;
    public TMPro.TextMeshPro label;

    void Start() {
        board.AddListener(this);
        OnBoardChanged();
    }

    public override void OnBoardChanged() {
        if (board.lifecycle != ChessBoard.LifecycleInGame) {
            label.text = "";
            return;
        }
        label.text = board.SideToMove() == ChessRules.White ? "White to move" : "Black to move";
    }

    // A button of the world's own: join White as the local player.
    public void PressJoinWhite() {
        if (board.CanJoin(ChessRules.White)) board.JoinWhite();
    }
}
```

## What's supported

The API is exactly what these documents name: the classes, public methods, Inspector fields, readable fields, constants and return values they describe, which [compatibility](../compatibility.md) (rule 6) keeps across releases from the first one on. Everything else is internal and may change or go in any release, in particular:

- **public methods starting with `_`** (`_PackState`, `_LoadImport`, `_Press…`, …): public only because another of the package's behaviours, a test, a UI event or `SendCustomEvent` calls them (step 13.7);
- **public fields not shown in the Inspector** (`[NonSerialized]` ones such as the board's synced fields, its clock history, the arbiter's `boards`), unless a document names one as readable;
- the editor scripts, the test behaviours and debug views under `Assets/Tests/`, and the inner objects of the prefabs.

Unity's and Udon's events (`Start`, `OnDeserialization`, …) aren't calls for a world to make. A world's own text uses the keys [Text](text.md) lists, which are kept as the API is.
