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
| [replay.md](docs/replay.md) | Replay mode, takeback, continuing loaded (imported or archived) games |
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

Ordered so that each step can be tested before the next builds on it: rules first with no world objects, then settings and the logical board, then networking, then the world. Open decisions that block a step are noted on it.

### 1. Project setup and early check

**Read:** [overview](docs/overview.md), [udon-constraints](docs/udon-constraints.md), [organizers](docs/organizers.md) (`SimpleAuth`), [java-bugs](docs/java-bugs.md), [testing](docs/testing.md)

1. Create the Unity project with the VRChat Creator Companion, Worlds SDK 3.7.1 or later.
2. Make the Java library available as `JavaChessBoard/`; move this plan to the project root. Copy `JavaChessBoard/Siggi-vs-Nuka.pgn` into `Assets/` (leaving the original in place), so test behaviours can read it through a `TextAsset` field, since Udon has no file I/O.
3. Add `SimpleAuth` (`SiggiUSTools`).
4. Leave the Java unchanged. Its two castling bugs are fixed only in the port, so later comparisons against the Java must avoid or allow for them (see [Bugs found in the Java](docs/java-bugs.md)).
5. **Early check: Udon speed.** A throwaway behaviour that generates moves in a loop, to get a feel for real costs before the design relies on them.

Already settled, so no early check is needed:
- `VRCObjectSync` is assumed not to fight a non-owner snapping a piece into place. If step 9 shows it does, pieces stop using `VRCObjectSync` (see [Physical pieces](docs/physical-pieces.md)).
- Synced `string[]` works, but a single `null` element makes the whole serialization fail, so every element must be initialised to an empty string (see [Synced fields](docs/networking.md#synced-fields-per-board)).

### 2. Core rules (static methods, no world)

**Read:** [udon-constraints](docs/udon-constraints.md), [architecture](docs/architecture.md), [move-encoding](docs/move-encoding.md), [legality-checking](docs/legality-checking.md), [modes](docs/modes.md) (`prohibitSelfCheck`, Chess960 castling), [java-bugs](docs/java-bugs.md), [testing](docs/testing.md)

1. Constants and helpers: piece types and colours, square ↔ file/rank, piece encoding in the `byte[64]` board.
2. Move encoding: pack/unpack helpers, and the derived en passant / double-push checks.
3. Make and unmake a move on the board arrays, including castling (Chess960-aware), en passant, promotion, castling rights and en passant restore.
4. Move generation per piece type, and the attack check.
5. Legal move list (play, attack check, undo), with the `prohibitSelfCheck` option; setting it to `false` skips the check filtering (for Fog of War later).
6. **Test:** a test behaviour that builds a few positions by hand with the constants (the standard start, plus positions with castling, en passant and promotion available). It checks that make followed by unmake restores every array exactly, and that the legal move counts match counts worked out by hand or with the Java. Include the two Java bug cases: capturing a rook on its starting square removes that side's castling right, and castling is refused when the rook is gone (the Java gets both wrong, so don't compare these with it). Full perft from published positions needs FEN, so it comes in step 3.

### 3. FEN

**Read:** [udon-constraints](docs/udon-constraints.md) (regex, no I/O), [move-encoding](docs/move-encoding.md) (castling files, undo and halfmove clock), [modes](docs/modes.md) (Chess960), [java-bugs](docs/java-bugs.md) (comparing against the Java), [testing](docs/testing.md)

1. FEN parsing and writing, including Chess960 castling letters and the king/rook starting files.
2. **Test:** round-trip a set of FENs; compare against the Java's output for the same positions.
3. **Test:** a test behaviour running perft from published positions (standard start, Kiwipete, the other standard perft positions, some Chess960 positions), loaded from their FENs, and checking the counts. Record the timings. Published counts only cover normal rules, so also run some positions with `prohibitSelfCheck = false` and compare with the Java's counts for the same setting.

### 4. Notation, endings and draw detection

**Read:** [legality-checking](docs/legality-checking.md), [move-encoding](docs/move-encoding.md) (undo, halfmove clock), [game-endings](docs/game-endings.md), [pgn](docs/pgn.md) (Export: castling notation), [testing](docs/testing.md)

1. SAN generation with direct disambiguation and `+`/`#` suffix; `O-O`/`O-O-O`.
2. Checkmate and stalemate detection.
3. Halfmove clock (derived from history), repetition detection, the fifty/seventy-five-move rules, insufficient material.
4. **Test:** known positions for each ending and draw rule; SAN for ambiguous moves (two knights, two rooks, promotions, captures).

### 5. PGN

**Read:** [pgn](docs/pgn.md), [udon-constraints](docs/udon-constraints.md) (regex changes for .NET), [game-ids](docs/game-ids.md), [game-endings](docs/game-endings.md) (results, `Termination`), [clocks](docs/clocks.md) (`TimeControl`, `%clk`), [modes](docs/modes.md) (`Variant` tags, also read on import), [replay](docs/replay.md) (Continuing a loaded game: what import keeps), [java-bugs](docs/java-bugs.md) (comparing against the Java), [testing](docs/testing.md)

1. Export: tags (seven-tag roster, `FEN`/`SetUp`, `Variant`, `TimeControl`, `GameID`, `ContinuedFrom`, `Termination`), move text with `[%clk]` comments and line wrapping, via `StringBuilder`. The board doesn't exist yet, so export takes the game's data (players, result, history, clocks, Game ID...) as parameters.
2. Import: regex tokenizer (adapted for .NET), reading `FEN`/`Variant`/`White`/`Black`/`Result`/`GameID`, matching each SAN against legal moves; failure reporting.
3. Time-budgeted state machine for import.
4. **Test:** round-trip `Siggi-vs-Nuka.pgn` and compare with the Java's export; malformed PGNs fail with the right move number.

### 6. Settings (`ChessArbiter`)

**Read:** [settings](docs/settings.md), [organizers](docs/organizers.md), [inactivity](docs/inactivity.md) (the two times, "never"), [game-endings](docs/game-endings.md) (Draw rules), [clocks](docs/clocks.md) (forced time control), [board-lifecycle](docs/board-lifecycle.md) (Starting: forced lobby settings), [testing](docs/testing.md)

1. Setting bits (including "mode forced" and "clock forced") and the five numeric settings (two inactivity times, forced mode, forced base time and increment), instance defaults from the editor, per-board overrides, effective-settings calculation.
2. `FirstJoinerAuth`, `InstanceMasterAuth`, `NoOneAuth`; default wiring; empty-field build check (editor script) and in-game fallback (log the error and behave like `NoOneAuth`; the message in the settings UI comes with the settings menu in step 11).
3. **Test:** effective settings for combinations of defaults and overrides; the build check blocks an empty field.

Taking a board's snapshot of these settings depends on the board's lifecycle, so that's in step 7.

### 7. Logical board, single player (`ChessBoard`, no networking yet)

**Read:** [architecture](docs/architecture.md), [board-lifecycle](docs/board-lifecycle.md), [clocks](docs/clocks.md), [modes](docs/modes.md), [player-input](docs/player-input.md), [game-endings](docs/game-endings.md), [replay](docs/replay.md), [game-ids](docs/game-ids.md), [settings](docs/settings.md) (Snapshot, Re-snapshot), [organizers](docs/organizers.md) (who counts as an organizer), [pgn](docs/pgn.md) (Import, Logging), [networking](docs/networking.md) (Synced fields: the board's state fields), [move-encoding](docs/move-encoding.md), [testing](docs/testing.md)

1. Board state fields; lifecycle (reset, loaded, lobby, in game, ended); Join White / Join Black; mode and clock selection, respecting forced settings; Start; Chess960 position generation.
2. Settings snapshot when the board leaves the reset state; re-snapshot on request.
3. Clocks: running time from the turn-start timestamp, increment, clock history, flag fall.
4. Input API: `PiecePickedUp`, `PieceDropped`, promotion choice, castling by drop, the buttons.
5. Game endings and result; resign; draw offers and claims per the snapshot's draw rules; time forfeit.
6. Game IDs and `ContinuedFrom`.
7. PGN logging on the "in progress → ended" transition, skipping games with no moves.
8. Replay mode: replay board, stepping, free moves, custom flag; takeback request/accept (if the snapshot allows takebacks) and truncation (restoring clocks), logging an unfinished game first and giving the continued game a new ID.
9. Loading a game: take a loaded game's data (a finished PGN import from step 5 now; the archive reuses this in step 10) and enter the loaded state in replay mode, with the mode from the game and its `White`/`Black`/`Result` shown; refused unless the board is in its reset state. Joining seats while loaded, choosing the clock, and "Continue from here": truncate at the shown ply, new Game ID with `ContinuedFrom`, fresh clocks, in game.
10. Reset: clears the board and both seats, logging an unfinished game that had moves. Allowed for seated players and organizers here; the idle-time rule for everyone else comes with inactivity in step 8.
11. Listener notification (`ChessBoardListener`, `OnBoardChanged`, last-change value) and the getters the view needs.
12. **Test:** a test behaviour (also a `ChessBoardListener`) that drives the board through scripted games, replays, takebacks, imported games (including joining and continuing them), resets and every ending, with different settings (including forced lobby settings, "No draws" and takebacks off), checking state, snapshot timing and log output.

### 8. Networking (`ChessBoard`, `ChessArbiter`)

**Read:** [networking](docs/networking.md), [ownership](docs/ownership.md), [inactivity](docs/inactivity.md), [organizers](docs/organizers.md), [settings](docs/settings.md) (the arbiter's synced settings, re-snapshot), [board-lifecycle](docs/board-lifecycle.md) (Reset), [legality-checking](docs/legality-checking.md) (notation fill-in), [pgn](docs/pgn.md) (Import, Logging), [game-endings](docs/game-endings.md) (log once per client), [architecture](docs/architecture.md) (listeners, for the debug view), [testing](docs/testing.md)

1. Debug view: a minimal `ChessBoardListener` that shows the board state as text, with buttons that call the board's input methods (join, start, a few scripted moves, resign, replay, reset) and a few that change settings on the arbiter and re-snapshot. It lets each client act before `ChessBoardView` and the settings menu exist, and stays as a testing tool.
2. `ChessArbiter`: synced settings (instance defaults, per-board overrides, inactivity times) changed by an organizer taking ownership; `OnOwnershipRequest` allowing only organizers; `FirstJoinerAuth`'s synced first-joiner name. The board's ownership rules below rely on organizers being recognised on every client.
3. Board synced fields, manual sync, `RequestSerialization`, `OnDeserialization` → local state → listeners.
4. Ownership rules in `OnOwnershipRequest`: players, organizers, idle-reset exception.
5. Inactivity: shared server-time timestamp, throttled activity updates, reset-available and auto-reset (including "never").
6. Notation sync and fill-in for missing entries.
7. Imported games written in one update at the end; abandon if the board changed.
8. **Test with several clients**, using the debug view: moves both ways, late joiner, owner leaving mid-game, a player rejoining and reclaiming their seat, bystander denied, organizer reset, idle timers, log written once per client, clocks agreeing on every client and a flag fall ending the game once, notation filled in when entries are missing; settings changed by an organizer reach every client, a non-organizer can't take the arbiter, the first joiner keeps organizer rights after rejoining.

### 9. World presentation (`ChessBoardView`)

**Read:** [architecture](docs/architecture.md), [physical-pieces](docs/physical-pieces.md), [player-input](docs/player-input.md), [board-lifecycle](docs/board-lifecycle.md) (lobby controls), [clocks](docs/clocks.md) (clock display), [replay](docs/replay.md) (replay controls, takeback requests, replay source names), [game-endings](docs/game-endings.md) (draw offers and claims, status display), [testing](docs/testing.md)

1. 32 piece objects with mesh swapping; square ↔ object mapping; placing pieces from board state.
2. `VRCPickup` + `VRCObjectSync`; per-player `pickupable` from the board; pieces' ownership check.
3. Forwarding pickups and drops to the board, including the castle drop space (shown only while that king is held and the castle is legal); snapping back on rejection.
4. Board buttons, each shown only when it applies: Join White / Join Black, mode and clock selection, Start, resign, offer / accept / claim draw, enter / leave replay and step, request / accept / decline takeback, "Continue from here" on a loaded game, reset. ("Load PGN" opens the PGN text box, so it comes with that in step 11.)
5. Promotion picker, highlights (last move, legal targets), move animations, status display (turn, clocks, check, result, stalemate, pending offers and requests, replay source names).
6. **Test with several clients:** carrying is visible to others, dropped pieces stay where the board puts them on every client (if `VRCObjectSync` fights the snap, remove it from the pieces), rejected moves snap back everywhere, bystanders can't grab during a game, castling by drop (including the castle drop space in Chess960).

### 10. Archive and history wall (`GameArchive`)

**Read:** [archive](docs/archive.md), [networking](docs/networking.md) (Limits), [game-ids](docs/game-ids.md), [replay](docs/replay.md) (continuing a loaded game), [board-lifecycle](docs/board-lifecycle.md) (Reset), [game-endings](docs/game-endings.md) (what gets logged/archived), [testing](docs/testing.md)

1. Paged storage (flattened arrays), capacity 200 with oldest dropped, appending whenever a game is logged (finished, or unfinished on reset or takeback), the retry check for simultaneous additions.
2. History wall view; "Load it here" buttons on reset boards; loading through the board's load method from step 7, which puts the board in its loaded state.
3. **Test:** fill past 200 games; two boards ending at once; late joiner sees the full archive; loaded games continue with `ContinuedFrom`.

### 11. Remaining UI

**Read:** [settings](docs/settings.md), [organizers](docs/organizers.md), [inactivity](docs/inactivity.md), [clocks](docs/clocks.md), [game-endings](docs/game-endings.md) (Draw rules), [pgn](docs/pgn.md) (Import, Input / output), [architecture](docs/architecture.md) (views of instance-wide objects)

1. Settings menu (all boards / per board, on/off/default, timers with "never", forced time control, re-snapshot one board or all boards), shown only to organizers; the error message when the arbiter's `SimpleAuth` field is empty.
2. PGN text box: copy current PGN; the "Load PGN" button on reset boards, which opens the box to paste into and starts the import; import progress, and the reason when it fails ("failed at move N") or is abandoned because the board changed.

### 12. Final pass

**Read:** [testing](docs/testing.md), [pgn](docs/pgn.md) (Logging), [game-endings](docs/game-endings.md) (`Termination` values), [udon-constraints](docs/udon-constraints.md) (performance)

1. Full multi-client session covering every feature, with a few boards at once.
2. Check the output log with a logging bot (or a script reading the log): one PGN per completed game, correct tags.
3. Performance: frame times during play, imports and archive loads, on lower-end hardware and in VR.
