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
- The archive is the one large object, so it's split into pages (see [`GameArchive`](archive.md)).

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
| white / black player | `string` ×2 | display names, so a rejoining player keeps their seat; see [Ownership](ownership.md) |
| last activity | `double` | server time of the last piece touch, see [Inactivity](inactivity.md) |
| result | `byte` ×2 | ending (in progress, or how it ended) and winner; see [Game endings](game-endings.md) |
| lifecycle state | `byte` | reset, loaded, lobby, in game, ended; see [Board lifecycle](board-lifecycle.md) |
| mode | `byte` | Standard, Chess960 (Fog of War later) |
| time control | `int` ×2 | base and increment in seconds; base `-1` = no clock |
| clocks | `int` ×2 + `double` | each side's remaining ms, and server time the current turn began |
| clock history | `int[]` | mover's remaining ms after each move |
| game settings | `int` + `int` ×5 | settings snapshot (setting bits, plus the five numeric settings: two inactivity times, forced mode, forced base time and increment) taken when the board left the reset state; see [Per-board overrides](settings.md#per-board-overrides) |

- The move history is the full packed ints, not a 2-byte compact form. A long game is a couple of KB, well within manual sync limits, and receivers get the undo info with no reconstruction. This replaces the earlier 2-byte format, which was designed for receivers rebuilding moves themselves.
- Because every packed move carries its own undo info, a client can walk back from the current position to any earlier one by undoing moves. Replay and filling in notation therefore don't need the starting FEN to be parsed.
- The board keeps notation as a `string[]`, one SAN per ply with `""` where not known yet, which PGN export takes as it is (`ChessPgn.WriteMoveText`). Whether it's synced as that array (never `null` elements, see below) or joined into one space-separated string (SAN never contains spaces) is decided in step 8.3.
- Until step 8.3 the board's fields are plain fields, with `int` where this table has `byte`, and the side to move, castling rights, en passant and starting files are its `ChessPosition` state array; 8.3 picks the synced types. The history arrays (moves, notation, clock history) have spare room, with the move count saying how many entries are the game.
- **Synced `string[]` must never contain `null`.** A single `null` element makes the whole serialization fail, so every element is initialised to an empty string (this applies to any synced `string[]`, not just notation).

## Filling in missing notation

- Notation is synced, but every client also has code to compute it locally if any is missing (the string has fewer entries than the move history, or an entry is empty). It walks back to the position before each missing move and computes it as described under [Legality checking](legality-checking.md), using the time-budgeted processing for long gaps.
- This keeps open the option of not syncing notation at all and having every client compute its own.

## Imported games

The importing player parses the PGN locally, then writes the resulting state (pieces, move history, notation, starting FEN, replay index) to the synced fields like any other update. Everyone else, including late joiners, can step through the replay. (Syncing only the final FEN was the original idea; it shows the end position but loses the game.)
