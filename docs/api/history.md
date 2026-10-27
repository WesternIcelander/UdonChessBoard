# History

What a world's own scripts call on the archives of played games: finding them, reading their games, deleting from the player's own, loading an archived game onto a board, noticing a change, and what the boards log and when. The package's **Game History Wall** and **Personal History Wall** (`GameHistoryWall`) are built on exactly this and are the reference for it. Read the [API index](README.md) first; the design behind the archives is in [archive](../archive.md), the IDs in [game IDs](../game-ids.md).

## The two archives

Both are a `GameArchive`, an abstract behaviour whose methods each kind overrides (Udon has no interfaces, so this stands in for one, as `ChessBoardListener` does). A field typed `GameArchive` reaches either kind.

| Kind | Prefab | Holds | Synced |
|---|---|---|---|
| `InstanceGameArchive` | **Game Archive** | Every game logged in this instance, by any board on any client: the last `Capacity()` of them (50 in the prefab, 10 holders of `GameArchiveHolder.Capacity` (5)), the newest overwriting the oldest once full. Nobody may delete from it. | Yes: every client reads the same games. |
| `PersonalGameArchive` | **Personal Game Archive** | The local player's own games, those with their display name as White or Black, kept in their VRChat PlayerData across visits and instances: the last `capacity` of them (Inspector, `PersonalGameArchive.DefaultCapacity`, 20). The player may delete them. | No: each client reads its own player's. |

**Finding them:** a script holds one in a field set in the Inspector (`public GameArchive archive;`), or reads the arbiter's `archives` (Inspector, the list every board hands its games to; the [wiring script](../prefabs.md#wiring) fills it with the scene's two). To use one kind's own calls (`IsArchived`, `restored`), hold it in a field of that type.

Readable fields (read, never write): on every archive `changes` ([below](#noticing-a-change)) and `arbiter` (the arbiter listing it, set on the arbiter's `Start`; `null` before then or without one); on the personal one the Inspector's `capacity` and `keyPrefix` (the start of its PlayerData keys, `PersonalGameArchive.DefaultKeyPrefix`, `siggi.chess.personal.`) and `restored` ([below](#when-the-games-are-there)). Anything else on them is internal, the holders and their arrays included.

## Reading an archive

Games are read by **index, oldest first**: 0 is the oldest, `GameCount()` − 1 the newest (a wall shows them the other way round, newest at the top). The indices move whenever a game is added or deleted, so keep a game's **ID** across frames, not its index, and look it up again with `IndexOf(gameId)` (−1 when the archive doesn't hold it). An index outside 0 to `GameCount()` − 1 isn't checked by the readers below (`HasMovesAt` excepted): check it first.

| Call | Gives |
|---|---|
| `Capacity()` | How many games it can hold. |
| `GameCount()` | How many it holds now. |
| `IndexOf(gameId)` | The game's index, −1 when not held (or for `""` / `null`). |
| `GameIdAt(i)` | Its Game ID (`yyyyMMddHHmmss-xxxxxx`, the start time in UTC; [game IDs](../game-ids.md)). |
| `ContinuedFromAt(i)` | `<GameID>/<ply>` of the game it was continued from (a takeback, "Continue from here", play on after a flag fall), `""` for none. |
| `WhiteAt(i)`, `BlackAt(i)` | The players' display names, `""` for an empty seat. |
| `ModeAt(i)` | `ChessGame.ModeStandard` or `ModeChess960`. |
| `EndingAt(i)`, `WinnerAt(i)` | How it ended (`ChessGame.Ending…`) and the winner (`ChessRules.White`, `Black`, or `NoColor` for a draw or none). An unfinished game is `EndingUnterminated` with `NoColor`. `ChessGame.EndingKey(ending, winner)` is its text key ([text](text.md)), `ChessPgn.ResultText(ending, winner)` its result (`1-0`, `0-1`, `1/2-1/2`, `*`). |
| `StartFenAt(i)` | The starting position's FEN; treat `""` as the standard start (`ChessFen.StartFen`), as the wall does. |
| `BaseTimeAt(i)`, `IncrementAt(i)` | The time control in seconds: base `ChessArbiter.NoClock` (−1) for an untimed game, `ChessBoard.UnknownClock` (−2) when not known (a personal game whose PGN had no `TimeControl`). |
| `EventAt(i)`, `SiteAt(i)` | The PGN Event and Site tags it was played with, `""` for none. |
| `MoveCountAt(i)` | Its number of plies. |
| `HasMovesAt(i)` | Whether its moves and clocks can be copied (`false` for an index out of range). |
| `CopyMovesAt(i, dest)`, `CopyClocksAt(i, dest)` | Copy its packed moves ([move encoding](../move-encoding.md)) or its clocks (milliseconds left after each move, −1 unknown) into `dest`, which needs room for `MoveCountAt(i)`. Only when `HasMovesAt(i)`; otherwise `dest` is left as it was. |
| `PgnAt(i)` | Its PGN as the board logged it, when the archive keeps one; `""` otherwise. |

The two kinds differ in what they keep:

- **The instance's** keeps the moves and clocks of every game (`HasMovesAt` always `true`) but **no notation**: `PgnAt` is `""`. To show its moves as SAN, or its PGN, work the SAN out from the moves, as its wall's Export PGN does (below).
- **The personal one** keeps each game's PGN (`PgnAt`). Its moves and clocks are in memory only for the games handed in this session (`HasMovesAt` `true`); a game saved in an earlier session has only its PGN until a board imports it. Its readers come from that PGN's tags: a game without an `Ending` (impossible from these boards) reads as `EndingUnknown` with the result's winner, or `EndingUnterminated` for `*`.

`InstanceGameArchive.IsArchived(gameId)` says whether a game is in the instance's archive yet (the same as `IndexOf(gameId) >= 0`).

**Other players can read a player's PlayerData** (VRChat's rule), so the personal history isn't secret.

### When the games are there

- **The instance's archive** has one writer, its object's owner. A game logged on another client shows up there once the owner has archived it and the holder's update has arrived, a moment later. Until then each client keeps the last 5 games it logged as its **recent games** (local, never shown, not readable), and whoever becomes the owner adds those still missing, so a game ending as the owner leaves isn't lost. A late joiner's archive fills in as the holders arrive, each a change.
- **The personal archive** reads nothing until VRChat has restored the local player's PlayerData (`OnPlayerRestored`): until then `restored` is `false` and `GameCount()` 0, and a game handed in waits, saved once restored. Its games are only what this player's own client logged: a game finished after they left isn't theirs.

### Exporting a stored game

- **From the personal archive:** `PgnAt(i)`, at once.
- **From the instance's:** write it with `ChessPgn.WriteGame(event, site, white, black, ending, winner, mode, startFen, baseSeconds, incrementSeconds, gameId, continuedFrom, sans, clocks, count, firstMoveNumber, firstSideToMove)` from the readers, after working out the SAN of each move by walking the game from its start: parse `StartFenAt(i)` (`ChessFen.ParseFen`, which also gives the first move number, and the side to move in the state), then per ply `ChessMoves.GenerateLegalMoves`, `ChessNotation.MoveToSan` and `ChessPosition.MakeMove` ([rules](rules.md)). `baseSeconds` is 0 for `NoClock` and −1 for `UnknownClock`, the increment 0 without a clock. That's a legal-move search per ply, so spread a long game over frames, as the wall does (`ExportBudgetMs`, 10 ms a frame).
- A board that loads the game writes the same PGN with `GamePgn()` ([UI](ui.md#pgn-out-and-in)), once its notation is filled in (`OnNotationFilled`).

## Deleting

Only from the local player's own archive:

- **`CanDelete()`**: `true` on the personal archive, `false` on the instance's.
- **`DeleteGame(gameId)`**: removes the game and saves the slot empty; returns whether it did. `false` for a game not held, before the PlayerData is restored, and always on the instance's archive. Deleting is a change ([below](#noticing-a-change)), and clears the pick if it was that game.

The wall asks for a second press within 3 seconds (`GameHistoryWall.DeleteArmTime`) before it deletes; a world's own delete button should ask too, since nothing brings a deleted game back.

## The pick and loading a game

### The pick

The game the local player picked on a history wall, held on the arbiter, local to each player (nothing synced). While there is one, every reset board's UI shows **Load it here** to that player.

- **Reading it:** `arbiter.HasSelection()`, and the arbiter's readable `selectedArchive` (the archive it's in, `null` for none) and `selectedGameId` (`""` for none). Its index is `selectedArchive.IndexOf(selectedGameId)`.
- **Making it** is the walls' (`_SelectGame`, `_ClearSelection` are internal): pressing an entry picks its game, or unpicks it when it's the one picked; a pick on the other wall replaces it. The pick is cleared once it's loaded, and when its game leaves its archive (deleted, or overwritten as the oldest), with no message. A world's own list of games doesn't set the pick: give it its own load button (below).
- **Told** as `OnArbiterChanged(board, ChessArbiter.ChangeSelection)` to every listener of every board ([below](#noticing-a-change)), as the board's UI shows and hides Load it here: `board.CanLoadGame() && board.arbiter.HasSelection()`.

### Loading onto a board

A game loads **only onto a reset board** (`board.CanLoadGame()`), by anyone, as a PGN import does. All return `ChessBoard.InputAccepted` or `InputRejected`.

| Call on `ChessBoard` | Loads | Rejected when |
|---|---|---|
| `LoadSelectedGame()` | The pick (Load it here). With its moves (`HasMovesAt`), copied at once, as `LoadArchivedGameFrom`; otherwise (a personal game from an earlier session) an import of its saved PGN starts, told through `OnImportChanged` like any import ([UI](ui.md#import-progress)). Clears the pick once accepted. | The board isn't reset, there's no pick or its game is gone, or (for an import) `CanImport()` is false (the board has no importer). |
| `LoadArchivedGameFrom(archive, gameId)` | That game of that archive, its moves copied at once. | The board isn't reset, `archive` is empty, or it doesn't hold the game with its moves. |
| `LoadArchivedGame(gameId)` | That game from the first of the arbiter's `archives` holding it with its moves. | As above, or the board has no arbiter. |

- **What loads:** the moves and clocks, starting position, mode, players, ID, time control, Event and Site, and the ending it was logged with (a resignation loads as one; an unfinished game with none). No notation: every entry starts `""` and fills in over the next frames on every client (`OnNotationFilled`).
- **Then** the board is in the loaded state, in replay at the last position, held by the local player (`loadedBy`) until they take a seat ([UI](ui.md#loading-a-game-and-who-holds-a-loaded-board)); told everywhere as `OnBoardChanged` with `ChangeLoaded`, the board sent as any change is ([index](README.md#calls-and-when-they-take-effect)). Other clients need nothing from the loader's archive.
- **Continuing it** ("Continue from here") makes a new game whose `ContinuedFrom` names the loaded one at the ply shown.
- **A personal game without its moves** can't be loaded with the two `LoadArchived…` calls. `board.StartImport(archive.PgnAt(i))` imports it, as Load it here does, except that the PGN as logged has no `Ending` tag, so a resignation or other ending not on the board loads as the result alone (`EndingUnknown`); prefer `LoadSelectedGame` where the player has picked it.

## Noticing a change

The archives have no listeners of a world's own (the walls register with theirs through an internal call). Two ways:

- **Polling `changes`:** each archive's readable `changes` counts every change to the games it holds: a game archived or saved here, a holder update arriving, a game deleted, the personal archive restored. Keep the value last drawn and redraw when it differs, as the wall's `_RefreshIfChanged` does (and its first drawing). The count only says that something changed, not how much.
- **Through a board:** a game is logged on the change that ends or replaces it, so a `ChessBoardListener` on the boards hears of it as `OnBoardChanged` ([below](#whats-logged-and-when)). The archive may change a moment later (a game logged elsewhere is archived by the owner), so redraw a display of the archive on its `changes`, not on the board.

**The pick** is told through the boards: `OnArbiterChanged(board, ChessArbiter.ChangeSelection)`, once per board the listener is added to, with nothing changed on the board. `ChangeSettings` is the settings ([settings](settings.md#noticing-a-change)).

```csharp
using SiggiChessBoard;
using TMPro;
using UdonSharp;

// A sign showing the instance's last game, with a button loading it onto a board.
public class LastGameSign : UdonSharpBehaviour {
    public GameArchive archive;   // the scene's Game Archive
    public ChessBoard board;
    public TextMeshProUGUI label;

    int drawnChanges = -1;
    string shownId = "";

    void Update() {
        if (archive.changes == drawnChanges) return;
        drawnChanges = archive.changes;
        int count = archive.GameCount();
        if (count == 0) {
            shownId = "";
            label.text = "No games yet";
            return;
        }
        int i = count - 1;   // the newest
        shownId = archive.GameIdAt(i);
        label.text = archive.WhiteAt(i) + " vs " + archive.BlackAt(i) + "  "
            + ChessPgn.ResultText(archive.EndingAt(i), archive.WinnerAt(i));
    }

    public void PressLoad() {
        if (shownId.Length == 0 || !board.CanLoadGame()) return;
        board.LoadArchivedGameFrom(archive, shownId);   // InputRejected if it's gone meanwhile
    }
}
```

## What's logged and when

Every client logs each game once, on the change that ends or replaces it, and hands it to each of the arbiter's archives (a board without an arbiter logs to the output log alone):

| When | Logged as | `OnBoardChanged` |
|---|---|---|
| A game ends (mate, resignation, agreed or automatic draw, flag fall, …) | Its ending and winner | `ChangeEnded` |
| A game in progress is reset | Unfinished (`*`), or on time if its flag had fallen | `ChangeReset` |
| An accepted takeback, or "Continue from here" by a player in both seats, in a game in progress | The game so far as unfinished; play goes on as a new game continued from it | `ChangeTakeback` |
| A flag falls with play on after a flag fall on | On time; play goes on untimed as a new game continued from it | `ChangeEnded` |
| An update replaces a game this client hadn't seen end | As the sender logged it: on time after a fallen flag, otherwise unfinished | the update's own |

- **Not logged:** a game with no moves; a game played on untimed after a flag fall with no move since; an ended game when it's reset (logged when it ended); a loaded or imported game (not played here), though a game continued from it is logged once it ends or is replaced; a late joiner's first update.
- **The output log:** one `Debug.Log` entry per game, a `[ChessPGN]` line then the game's PGN (tags, a blank line, the move text ending in `\n`), so a bot reading the VRChat output log takes everything after that line up to the next entry ([PGN](../pgn.md#logging)). Each client writes its own.
- **The instance's archive** keeps every logged game (with its ID, one copy however many clients log it), without its PGN.
- **The personal archive** keeps those with the local player in a seat, saving the PGN as logged after two lines only the saved copy has: `[PlyCount "…"]` (its plies) and `[Ending "…"]` (`ChessGame.EndingName(ending)`: `resignation`, `checkmate`, …). `PgnAt` gives the PGN without them, as logged; a game held already (by ID) is replaced in its slot, and once full the oldest by ID makes room.
