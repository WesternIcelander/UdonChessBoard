# UdonSharp Port Plan

Plan for porting the Java ChessBoard library to UdonSharp, to run in VRChat worlds. Nothing has been written yet; this records the design agreed so far.

This file holds the implementation steps. The design is split into topic files under `docs/`, and each step lists the ones to read before starting it.

## Design documents

| File | Contents |
|---|---|
| [overview.md](docs/overview.md) | Project layout, goals, environment |
| [udon-constraints.md](docs/udon-constraints.md) | What UdonSharp can't do, and the consequences for the design |
| [architecture.md](docs/architecture.md) | Packed moves, static rules, logical board (`ChessBoard`) vs physical board (`ChessBoardView`), listeners |
| [physical-pieces.md](docs/physical-pieces.md) | 32 piece objects, `VRCObjectSync`, grab gating |
| [move-encoding.md](docs/move-encoding.md) | 32-bit move layout, derived flags, castling encoding, undo |
| [legality-checking.md](docs/legality-checking.md) | Attack-check legality, direct SAN disambiguation |
| [player-input.md](docs/player-input.md) | Drops, promotion, castling by drop, castle drop space |
| [board-lifecycle.md](docs/board-lifecycle.md) | Lifecycle states, seats, starting, reset |
| [clocks.md](docs/clocks.md) | Time control, synced clock state, flag fall |
| [modes.md](docs/modes.md) | Standard, Chess960, Fog of War (later) and `prohibitSelfCheck` |
| [replay.md](docs/replay.md) | Replay mode, takeback, continuing imported games |
| [game-ids.md](docs/game-ids.md) | Game ID format, `ContinuedFrom`, ply numbering |
| [settings.md](docs/settings.md) | `ChessArbiter`, per-board overrides, snapshots |
| [organizers.md](docs/organizers.md) | `SimpleAuth` and its built-in implementations, empty-field checks |
| [archive.md](docs/archive.md) | `GameArchive`: paged storage, history wall, loading games |
| [networking.md](docs/networking.md) | Limits, synced fields, notation fill-in, imported games |
| [ownership.md](docs/ownership.md) | Ownership rules, players by display name, organizers |
| [inactivity.md](docs/inactivity.md) | Reset-available and auto-reset timers, activity timestamp |
| [pgn.md](docs/pgn.md) | Import, export, text box, logging |
| [game-endings.md](docs/game-endings.md) | Endings, results, `Termination` values, draw rules |
| [java-bugs.md](docs/java-bugs.md) | Castling bugs in the Java reference |
| [testing.md](docs/testing.md) | Test approach: Play Mode tests, perft, multi-client |
| [open-decisions.md](docs/open-decisions.md) | Open questions (Fog of War only) |

[overview.md](docs/overview.md) and [udon-constraints.md](docs/udon-constraints.md) apply to every step; they're listed below only where a step depends on them directly.

## Implementation steps

Ordered so that each step can be tested before the next builds on it: rules first with no world objects, then the logical board, then networking, then the world. Open decisions that block a step are noted on it.

### 1. Project setup and early checks

**Read:** [overview](docs/overview.md), [udon-constraints](docs/udon-constraints.md), [organizers](docs/organizers.md) (`SimpleAuth`), [java-bugs](docs/java-bugs.md), [physical-pieces](docs/physical-pieces.md) (`VRCObjectSync` check), [networking](docs/networking.md) (synced `string[]` check), [testing](docs/testing.md)

1. Create the Unity project with the VRChat Creator Companion, Worlds SDK 3.7.1 or later.
2. Copy the Java library into `JavaChessBoard/` and `Siggi-vs-Nuka.pgn` alongside; move this plan to the project root.
3. Add `SimpleAuth` (`SiggiUSTools`).
4. Leave the Java unchanged. Its two castling bugs are fixed only in the port, so later comparisons against the Java must avoid or allow for them (see [Bugs found in the Java](docs/java-bugs.md)).
5. **Early checks.** These are risky assumptions that could change the design, so test them before building on them:
   - Udon speed: a throwaway behaviour that generates moves in a loop, to get a feel for real costs.
   - `VRCObjectSync`: does it fight a non-owner snapping a piece into place? Test with two clients.
   - Whether synced `string[]` works (affects how notation is stored).

### 2. Core rules (static methods, no world)

**Read:** [udon-constraints](docs/udon-constraints.md), [architecture](docs/architecture.md), [move-encoding](docs/move-encoding.md), [legality-checking](docs/legality-checking.md), [modes](docs/modes.md) (`prohibitSelfCheck`, Chess960 castling), [java-bugs](docs/java-bugs.md), [testing](docs/testing.md)

1. Constants and helpers: piece types and colours, square ↔ file/rank, piece encoding in the `byte[64]` board.
2. Move encoding: pack/unpack helpers, and the derived en passant / double-push checks.
3. Make and unmake a move on the board arrays, including castling (Chess960-aware), en passant, promotion, castling rights and en passant restore.
4. Move generation per piece type, and the attack check.
5. Legal move list (play, attack check, undo), with the `prohibitSelfCheck` option; setting it to `false` skips the check filtering (for Fog of War later).
6. **Test:** a test behaviour running perft from published positions (standard start, Kiwipete, the other standard perft positions, some Chess960 positions) and checking the counts. Record the timings.

### 3. FEN

**Read:** [udon-constraints](docs/udon-constraints.md) (regex, no I/O), [move-encoding](docs/move-encoding.md) (castling files, undo and halfmove clock), [modes](docs/modes.md) (Chess960), [java-bugs](docs/java-bugs.md) (comparing against the Java), [testing](docs/testing.md)

1. FEN parsing and writing, including Chess960 castling letters and the king/rook starting files.
2. **Test:** round-trip a set of FENs; compare against the Java's output for the same positions.

### 4. Notation, endings and draw detection

**Read:** [legality-checking](docs/legality-checking.md), [move-encoding](docs/move-encoding.md) (undo, halfmove clock), [game-endings](docs/game-endings.md), [pgn](docs/pgn.md) (Export: castling notation), [testing](docs/testing.md)

1. SAN generation with direct disambiguation and `+`/`#` suffix; `O-O`/`O-O-O`.
2. Checkmate and stalemate detection.
3. Halfmove clock (derived from history), repetition detection, the fifty/seventy-five-move rules, insufficient material.
4. **Test:** known positions for each ending and draw rule; SAN for ambiguous moves (two knights, two rooks, promotions, captures).

### 5. PGN

**Read:** [pgn](docs/pgn.md), [udon-constraints](docs/udon-constraints.md) (regex changes for .NET), [game-ids](docs/game-ids.md), [game-endings](docs/game-endings.md) (results, `Termination`), [clocks](docs/clocks.md) (`TimeControl`, `%clk`), [modes](docs/modes.md) (`Variant` tags), [replay](docs/replay.md) (Continuing from an imported game: what import keeps), [java-bugs](docs/java-bugs.md) (comparing against the Java), [testing](docs/testing.md)

1. Export: tags (seven-tag roster, `FEN`, `GameID`, `ContinuedFrom`, how the game ended), move text with line wrapping, via `StringBuilder`.
2. Import: regex tokenizer (adapted for .NET), reading `FEN`/`White`/`Black`/`Result`/`GameID`, matching each SAN against legal moves; failure reporting.
3. Time-budgeted state machine for import.
4. **Test:** round-trip `Siggi-vs-Nuka.pgn` and compare with the Java's export; malformed PGNs fail with the right move number.

### 6. Logical board, single player (`ChessBoard`, no networking yet)

**Read:** [architecture](docs/architecture.md), [board-lifecycle](docs/board-lifecycle.md), [clocks](docs/clocks.md), [modes](docs/modes.md), [player-input](docs/player-input.md), [game-endings](docs/game-endings.md), [replay](docs/replay.md), [game-ids](docs/game-ids.md), [pgn](docs/pgn.md) (Logging), [networking](docs/networking.md) (Synced fields: the board's state fields), [move-encoding](docs/move-encoding.md), [testing](docs/testing.md)

1. Board state fields; lifecycle (reset, lobby, in game, ended); Join White / Join Black; mode and clock selection; Start; Chess960 position generation.
2. Clocks: running time from the turn-start timestamp, increment, clock history, flag fall.
3. Input API: `PiecePickedUp`, `PieceDropped`, promotion choice, castling by drop, the buttons.
4. Game endings and result; resign; draw offers and claims per settings; time forfeit.
5. Replay mode: replay board, stepping, free moves, custom flag; takeback request/accept and truncation (restoring clocks).
6. Listener notification (`ChessBoardListener`, `OnBoardChanged`, last-change value) and the getters the view needs.
7. Game IDs and `ContinuedFrom`.
8. PGN logging on the "in progress → ended" transition, skipping games with no moves.
9. **Test:** a test behaviour (also a `ChessBoardListener`) that drives the board through scripted games, replays, takebacks and every ending, checking state and log output.

### 7. Settings (`ChessArbiter`)

**Read:** [settings](docs/settings.md), [organizers](docs/organizers.md), [inactivity](docs/inactivity.md) (the two times, "never"), [game-endings](docs/game-endings.md) (Draw rules), [clocks](docs/clocks.md) (forced time control), [board-lifecycle](docs/board-lifecycle.md) (Starting: forced lobby settings), [testing](docs/testing.md)

1. Settings bits and inactivity times, instance defaults from the editor, per-board overrides, effective-settings calculation.
2. Board snapshot on leaving the reset state; re-snapshot.
3. `FirstJoinerAuth`, `InstanceMasterAuth`, `NoOneAuth`; default wiring; empty-field build check (editor script) and in-game fallback.
4. **Test:** effective settings for combinations of defaults and overrides; snapshot timing; the build check blocks an empty field.

### 8. Networking (`ChessBoard`, `ChessArbiter`)

**Read:** [networking](docs/networking.md), [ownership](docs/ownership.md), [inactivity](docs/inactivity.md), [organizers](docs/organizers.md), [settings](docs/settings.md) (re-snapshot), [board-lifecycle](docs/board-lifecycle.md) (Reset), [legality-checking](docs/legality-checking.md) (notation fill-in), [pgn](docs/pgn.md) (Import, Logging), [game-endings](docs/game-endings.md) (log once per client), [testing](docs/testing.md)

1. Synced fields, manual sync, `RequestSerialization`, `OnDeserialization` → local state → listeners.
2. Ownership rules in `OnOwnershipRequest`: players, organizers, idle-reset exception.
3. Inactivity: shared server-time timestamp, throttled activity updates, reset-available and auto-reset (including "never").
4. Notation sync and fill-in for missing entries.
5. Imported games written in one update at the end; abandon if the board changed.
6. **Test with several clients:** moves both ways, late joiner, owner leaving mid-game, a player rejoining and reclaiming their seat, bystander denied, organizer reset, idle timers, log written once per client.

### 9. World presentation (`ChessBoardView`)

**Read:** [architecture](docs/architecture.md), [physical-pieces](docs/physical-pieces.md), [player-input](docs/player-input.md), [replay](docs/replay.md) (replay source names), [game-endings](docs/game-endings.md) (status display), [testing](docs/testing.md)

1. 32 piece objects with mesh swapping; square ↔ object mapping; placing pieces from board state.
2. `VRCPickup` + `VRCObjectSync`; per-player `pickupable` from the board; pieces' ownership check.
3. Forwarding pickups, drops and buttons to the board; snapping back on rejection.
4. Promotion picker, highlights (last move, legal targets), move animations, status display (turn, check, result, stalemate, replay source names).
5. **Test with several clients:** carrying is visible to others, rejected moves snap back everywhere, bystanders can't grab during a game.

### 10. Archive and history wall (`GameArchive`)

**Read:** [archive](docs/archive.md), [networking](docs/networking.md) (Limits), [game-ids](docs/game-ids.md), [replay](docs/replay.md) (continuing a loaded game), [board-lifecycle](docs/board-lifecycle.md) (Reset), [game-endings](docs/game-endings.md) (what gets logged/archived), [testing](docs/testing.md)

1. Paged storage (flattened arrays), capacity 200 with oldest dropped, appending on game end, the retry check for simultaneous additions.
2. History wall view; "Load it here" buttons on reset boards; loading into replay mode.
3. **Test:** fill past 200 games; two boards ending at once; late joiner sees the full archive; loaded games continue with `ContinuedFrom`.

### 11. Remaining UI

**Read:** [settings](docs/settings.md), [organizers](docs/organizers.md), [inactivity](docs/inactivity.md), [clocks](docs/clocks.md), [game-endings](docs/game-endings.md) (Draw rules), [pgn](docs/pgn.md) (Import, Input / output), [architecture](docs/architecture.md) (views of instance-wide objects)

1. Settings menu (all boards / per board, on/off/default, timers with "never", forced time control), shown only to organizers.
2. PGN text box: copy current PGN, "Load PGN" on reset boards.

### 12. Final pass

**Read:** [testing](docs/testing.md), [pgn](docs/pgn.md) (Logging), [game-endings](docs/game-endings.md) (`Termination` values), [udon-constraints](docs/udon-constraints.md) (performance)

1. Full multi-client session covering every feature, with a few boards at once.
2. Check the output log with a logging bot (or a script reading the log): one PGN per completed game, correct tags.
3. Performance: frame times during play, imports and archive loads, on lower-end hardware and in VR.
