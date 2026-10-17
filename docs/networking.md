# Networking

Board state is synced directly as `[UdonSynced]` fields on the board behaviour (manual sync mode), sent with `RequestSerialization()`. Only manual sync is used; no continuous sync.

## Limits

From VRChat's [network details](https://creators.vrchat.com/worlds/udon/networking/network-details/):

- Udon scripts can send about **11 KB per second**.
- Manual sync: about **280,496 bytes per serialization**.
- Continuous sync: about 200 bytes per serialization (not used here).

Implications:

- A board's synced state is a few KB at most (a long game's move history is ~1–2 KB, plus notation), far under the per-serialization limit.
- Every serialization sends all of a behaviour's synced fields, not just the changed ones. So the board resends its whole state on every move *and* on every activity touch (pickups). That's still small, but it's why activity updates are throttled, e.g. at most one every few seconds while pieces are being handled.
- The archive is the one large object, so it's split into holders of 5 games, written only by the archive's owner (see [`GameArchive`](archive.md)).

No FEN or PGN goes over the network, so no client parses anything to stay in sync. Late joiners receive the last serialized state automatically.

## Synced fields (per board)

| Field | Type | Notes |
|---|---|---|
| pieces | `byte[64]` | piece per square, 0 = empty |
| side to move | `byte` | |
| castling rights | `byte` | 4 bits |
| en passant | `sbyte` | square, or -1 |
| move number | `int` | current value |
| starting halfmove clock, move number | `int` | from the starting position; see [Undo](move-encoding.md#undo) |
| king / rook starting files | `byte` ×3 | for castling and undo (Chess960) |
| move history | `int[]` | full packed moves, 4 bytes each |
| notation | `string[]` | each move's SAN, `""` if not known yet |
| starting position | `string` | FEN, copied into the PGN `FEN` tag when not the standard start; never parsed by receivers |
| replay mode | `bool` | whether the board is showing replay or the live game |
| replay index | `int` | ply of the game history the replay started from or is showing |
| replay board | `byte[64]` | the position shown in replay mode |
| replay custom | `bool` | replay position has left the game history; blocks continuing |
| takeback request | `byte` + `int` | requesting colour and ply, or none |
| draw offer | `byte` | offering colour, or none |
| game ID | `string` | see [Game IDs](game-ids.md) |
| continued from | `string` | `<GameID>/<ply>`, or empty; see [Game IDs](game-ids.md) |
| replay source White / Black / Result | `string` ×3 | from an imported PGN or an archived game, for display in replay; cleared when play continues |
| replay source time control | `int` ×2 | the loaded game's own base and increment (base `-1` no clock, `-2` unknown), for its export; the clock for continuing is the time control field |
| white / black player | `string` ×2 | display names, so a rejoining player keeps their seat; see [Ownership](ownership.md) |
| last activity | `double` | server time of the last piece touch, see [Inactivity](inactivity.md) |
| result | `byte` ×2 | ending (in progress, or how it ended) and winner; see [Game endings](game-endings.md) |
| lifecycle state | `byte` | reset, loaded, lobby, in game, ended; see [Board lifecycle](board-lifecycle.md) |
| mode | `byte` | Standard, Chess960 (Fog of War later) |
| time control | `int` ×2 | base and increment in seconds; base `-1` = no clock |
| clocks | `int` ×2 + `double` | each side's remaining ms, and server time the current turn began |
| clock history | `int[]` | mover's remaining ms after each move |
| play-on after a flag fall | `bool` + `int` | whether the game is untimed because a flag fell (the clocks are the timed game's), and the flag fall's ply while it's that flag fall's own continuation (−1 otherwise); step 12.9, see [Clocks](clocks.md#implementation) |
| last change | `byte` | what the last change was (`ChessBoard.Change*`), so listeners on every client can animate a move; `ChangeRejected` is local and never sent; `ChangeActivity` is a touch that changed nothing but the last activity. See [Architecture](architecture.md#implementation) |
| game settings | `int` + `int` ×5 | settings snapshot (setting bits, plus the five numeric settings: two inactivity times, forced mode, forced base time and increment) taken when the board left the reset state; see [Per-board overrides](settings.md#per-board-overrides) |

- The move history is the full packed ints, not a 2-byte compact form. A long game is a couple of KB, well within manual sync limits, and receivers get the undo info with no reconstruction. This replaces the earlier 2-byte format, which was designed for receivers rebuilding moves themselves.
- Because every packed move carries its own undo info, a client can walk back from the current position to any earlier one by undoing moves. Replay and filling in notation therefore don't need the starting FEN to be parsed.
- The board keeps notation as a `string[]`, one SAN per ply with `""` where not known yet, which PGN export takes as it is (`ChessPgn.WriteMoveText`). It's synced as that array, cut to the game's moves, rather than joined into one string: nothing to split on arrival, and `PackState` writes `""` for any `null` entry (see below).
- The table lists what is sent, not how: the board's own fields are plain fields (with `int` where this table has `byte`, and the side to move, castling rights, en passant and starting files in its `ChessPosition` state array), packed into a few synced fields when sent. See [Implementation](#implementation).
- **Synced `string[]` must never contain `null`.** A single `null` element makes the whole serialization fail, so every element is initialised to an empty string (this applies to any synced `string[]`, not just notation).

## Implementation

`ChessBoard` (8.3), manual sync.

- **Sending:** every public method that changes the board ends in `NotifyChanged(change)`, which, unless the change is `ChangeRejected`, takes ownership for the local player (`Networking.SetOwner`, if not the owner already), calls `RequestSerialization()`, then tells the listeners. Several changes in one frame go out as one update. It also stamps the board's last activity time. A piece touch that changes nothing is sent the same way as `ChangeActivity`, throttled ([Inactivity](inactivity.md#implementation)). Who may take ownership is in [Ownership](ownership.md#implementation).
- **Packing:** `OnPreSerialization` calls `PackState()`, which copies the state into the synced fields: `syncedBoard` and `syncedReplayBoard` (`byte[64]`), `syncedInts` (every `int` and `bool` of the table, the settings snapshot, the change sent and the `ChessPosition` state array, laid out by the `ChessBoard.Sync*` indices), `syncedMoves`, `syncedClocks` and `syncedNotation` (the game's first `moveCount` entries only, so the history arrays' spare room isn't sent), `syncedTurnStart` and `syncedLastActivity` (`double`), and one `string` each for the starting FEN, game ID, continued-from, the three replay source tags and the two seats. `null` strings are sent as `""`.
  - Packing into separate fields, rather than syncing the state fields themselves, lets a receiver compare its old state with the new one (for logging, below), and leaves its state untouched by an update that isn't valid.
  - The change sent is `lastSentChange`: the last change that wasn't a rejection, so `ChangeRejected` is never sent.
  - `Start` packs the cleared state too, since a late joiner gets the owner's synced fields even if the owner has never sent anything.
- **Receiving:** `OnDeserialization` calls `ReceiveState()`. An update that isn't one `PackState` could have written (an array missing or the wrong length, a string missing) is ignored. Otherwise the fields are unpacked into the state, the halfmove clocks are worked out from the moves (they aren't sent), the history arrays grow if needed and their entries past the game are emptied, and the listeners are told of the change sent, with `lastChangeReceived` set (it's `false` for a change made locally), so a view can animate a remote move but not one the local player dropped. `Start` doesn't clear a board that has already received an update.
- **Logging on arrival** ([Game endings](game-endings.md)): a client that had the game in progress logs it when an update ends it (same game ID, now ended), with the result sent. If the update replaces it instead (a different game ID: a reset, or a takeback continuing it as a new game) without the client having seen it end, the client logs its own copy of the game before taking the update: on time if the side to move's flag has fallen by then (as the reset that replaced it did), otherwise as unfinished (`*`). A client that didn't have it in progress (a late joiner, or one that already saw it end) logs nothing. If a reset arrives within moments of a flag fall, a receiver can call it a time forfeit where the resetting client logged it unfinished, or the other way round; that's accepted.
- **Flag fall and auto-reset:** only the owner calls `CheckFlagFall` and `CheckAutoResetAt` from `Update`, so the game ends on time once, the board resets itself once, and the other clients receive it.
- **Playing on after a flag fall** (12.9): the owner sends the timed game's end and the untimed game in one update, so receivers never see the ended game. A receiver that had the timed game in progress recognises the update as its continuation (the update's flag-fall ply is the game's ply count and its `ContinuedFrom` is `<this game's ID>/<that ply>`) and logs the timed game on time, with the winner its snapshot gives, whatever its own clock says.
- **Tests:** `PackState()` and `ReceiveState()` are public, so `BoardTest` (8.3) checks the round trip, the logging on arrival and invalid updates on one client. `BoardTest` runs only when the local player is alone in the instance: its changes are sent like any other, and would replace the board for everyone else.

## Filling in missing notation

- Notation is synced, but every client also has code to compute it locally if any is missing (the string has fewer entries than the move history, or an entry is empty). It walks back to the position before each missing move and computes it as described under [Legality checking](legality-checking.md), using the time-budgeted processing for long gaps.
- This keeps open the option of not syncing notation at all and having every client compute its own.

### Implementation

`ChessBoard` (8.6). Notation arrives with the moves as the synced `string[]` (8.3); the fill-in is local and sends nothing.

- **Gaps:** an entry is missing when it's `""` (or `null`). `FirstMissingNotation()` gives the first, `-1` for none.
- **The walk:** `FillNotationFor(budgetMs)` copies the game position, undoes the moves back to the first gap, then walks forward through the history: a known entry is just played, a missing one gets its SAN from `ChessNotation.MoveToSan` (a legal-move list on the walk's position, its own scratch arrays) and is played. It runs for about `budgetMs` (at least one entry, `float.MaxValue` for all of it), keeps the walk's position for the next call, and returns whether nothing is missing any more. `filledNotation` counts the entries this client has filled in and `lastFillFrames` the calls the last walk took, for tests and the debug view.
- **Each frame:** `Update` calls `FillNotationFrame()` on every client (not only the owner): while gaps may exist it walks for `fillBudgetMs` (`DefaultFillBudgetMs`, the PGN import's 10 ms, see [PGN import](pgn.md#import-local-rare)), so a long gap takes several frames. Once there are no gaps it stops looking until the history changes.
- **Changes:** every change to the history (unpacking an update, clearing, cutting it for a takeback or continuing, loading a game) calls `RecheckNotation()`, which drops a walk under way (its position may no longer be the game's) and has the next frame look for gaps again. Code that blanks entries itself (tests, the debug view) calls it too. A move played locally doesn't: it appends its own SAN, and a walk under way is still on the game's history.
- **PGN:** `GamePgn` and logging fill every gap first, in one go, so a logged or exported game never has an empty move.
- **Listeners:** entries filled in (by walks or by a PGN) are told through `OnNotationFilled()` on the first `FillNotationFrame` that finds nothing missing, so a walk over several frames is told once, at its end ([Architecture](architecture.md#implementation)); `lastChange` is untouched and nothing is sent. The owner sends whatever its notation holds, gaps included; each receiver fills them in on its own.

## Imported games

The importing player parses the PGN locally, then writes the resulting state (pieces, move history, notation, starting FEN, replay index) to the synced fields like any other update. Everyone else, including late joiners, can step through the replay. (Syncing only the final FEN was the original idea; it shows the end position but loses the game.)

### Implementation

`ChessBoard` (8.7), on the importing client. See also [PGN import](pgn.md#import-local-rare).

- **Starting:** `StartImport(text)` (the import button; `CanImport()`: a reset board, by anyone, and the board's `importer` field set) starts the board's `ChessPgnImporter` with room for `ImportCapacity` (1024) plies and `importBudgetMs` per frame (the importer's `DefaultBudgetMs`). Starting again replaces an import under way. Nothing changes on the board, nothing is sent and nobody takes ownership while it runs.
- **Finishing:** the importer sends `_ImportFinished` to the board when it's done. A finished import is loaded with `LoadImport` (`ChangeLoaded`), which takes ownership and sends the whole game in one update like any other change; every client, and every late joiner, gets it from that update. A failed import changes nothing and sends nothing; `importMessage` says why (`failed at move 2...: illegal move`).
- **Abandoning:** every change after it started abandons the import: one made here (`NotifyChanged`, anything but a rejection) or one arriving from another client (`ReceiveState`, anything but a touch, `ChangeActivity`, which changes nothing). The importer is stopped and a later done event is ignored, so an abandoned import is never loaded. Abandoning on the change itself, rather than checking when the import finishes, tells the importing player at once.
- **Telling:** `importStatus` is `ImportNone`, `ImportRunning`, `ImportLoaded`, `ImportFailed` or `ImportAbandoned`, and each change to it calls the listeners' `OnImportChanged()` ([Architecture](architecture.md#implementation)). Only the importing client knows of the import.
- **One importer per board:** the board's `importer` (Inspector) runs only its imports, since starting an import on an importer abandons the one under way there. The debug view's builder adds one to a board that has none.

## The 2D view's synced choice

The 2D board view (step 12.13, [2D board view](board-2d.md)) has one synced field of its own, `syncedIndex` (an `int`, manual sync): the index in its `boards` of the board shown, sent only when its **Synced** tick is on. A press takes ownership of the view and sends it; every client shows that board on `OnDeserialization`, a late joiner included (VRChat sends it the last value on joining). The view sends nothing about the boards themselves: each board's own sync brings its position, and the view listens to it as any listener does. With **Synced** off, nothing is sent and arriving data is ignored, so each player chooses for themselves.
