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

**Using this checklist.** Each item is sized to finish and verify in one session. Tick it (`- [x]`) only after its **Verify** has passed, and before starting the next item. If the work deviates from the plan, update the plan (and the docs) to match.

- Unless an item says otherwise, verification runs in Unity Play Mode (ClientSim): the step's test behaviour logs `PASS`/`FAIL` per check, and the results are read from the Unity Editor log (`%LOCALAPPDATA%\Unity\Editor\Editor.log`). Each item adds its checks to that test behaviour, so the step's final **Test** item reruns everything.
- "Several clients" means VRChat's Build & Test with multiple client instances (see [Testing](docs/testing.md)).
- Comparing against the Java means building and running it in `JavaChessBoard/` with Maven, without changing its code (see CLAUDE.md).

### 1. Project setup and early check

**Read:** [overview](docs/overview.md), [udon-constraints](docs/udon-constraints.md), [organizers](docs/organizers.md) (`SimpleAuth`), [java-bugs](docs/java-bugs.md), [testing](docs/testing.md)

- [x] **1.1** Create the Unity project with the VRChat Creator Companion, Worlds SDK 3.7.1 or later.
  - Verify: `Packages/vpm-manifest.json` lists `com.vrchat.worlds` at 3.7.1 or later. (Done: 3.10.5.)
- [x] **1.2** Make the Java library available as `JavaChessBoard/`; move this plan to the project root.
  - Verify: `JavaChessBoard/pom.xml` and `JavaChessBoard/src/main/java/io/siggi/chessboard/` exist; `PLAN.md` is at the project root.
- [ ] **1.3** Copy `JavaChessBoard/Siggi-vs-Nuka.pgn` into `Assets/` (leaving the original in place), so test behaviours can read it through a `TextAsset` field, since Udon has no file I/O.
  - Verify: the copy is byte-identical to the original (`cmp`), Unity has imported it (a `.meta` file exists), and a behaviour with a `TextAsset` field set to it logs its first line in Play Mode.
- [x] **1.4** Add `SimpleAuth` (`SiggiUSTools`).
  - Verify: `Assets/Siggi/USTools/SimpleAuth.cs` declares the class as in [Organizers](docs/organizers.md), and the Editor log shows it imported with no compile errors.
- [ ] **1.5** **Early check: Udon speed.** A throwaway behaviour that generates moves in a loop, to get a feel for real costs before the design relies on them.
  - Verify: in Play Mode it logs moves generated per second (and ms per position); record the numbers next to this item.

Leave the Java unchanged. Its two castling bugs are fixed only in the port, so later comparisons against the Java must avoid or allow for them (see [Bugs found in the Java](docs/java-bugs.md)).

Already settled, so no early check is needed:
- `VRCObjectSync` is assumed not to fight a non-owner snapping a piece into place. If step 9 shows it does, pieces stop using `VRCObjectSync` (see [Physical pieces](docs/physical-pieces.md)).
- Synced `string[]` works, but a single `null` element makes the whole serialization fail, so every element must be initialised to an empty string (see [Synced fields](docs/networking.md#synced-fields-per-board)).

### 2. Core rules (static methods, no world)

**Read:** [udon-constraints](docs/udon-constraints.md), [architecture](docs/architecture.md), [move-encoding](docs/move-encoding.md), [legality-checking](docs/legality-checking.md), [modes](docs/modes.md) (`prohibitSelfCheck`, Chess960 castling), [java-bugs](docs/java-bugs.md), [testing](docs/testing.md)

- [ ] **2.1** Constants and helpers: piece types and colours, square ↔ file/rank, piece encoding in the `byte[64]` board.
  - Verify: the step 2 test behaviour checks square → file/rank → square for all 64 squares, and piece encode → decode for every colour and type, plus empty.
- [ ] **2.2** Move encoding: pack/unpack helpers, and the derived en passant / double-push checks.
  - Verify: pack → unpack returns every field unchanged, at each field's minimum and maximum; the derived checks are right for hand-built moves (en passant capture, double push, ordinary pawn move, non-pawn move).
- [ ] **2.3a** Make and unmake a move on the board arrays: ordinary moves, captures, en passant, promotion, and en passant restore.
  - Verify: for each kind of move on a hand-built position, the position after make is as expected, and make followed by unmake restores every array exactly.
- [ ] **2.3b** Make and unmake for castling (Chess960-aware), and castling rights and their restore.
  - Verify: as 2.3a, for both sides' O-O and O-O-O in the standard position and in Chess960 positions (including one where the king doesn't move); castling rights are lost after king and rook moves and when a rook is captured on its starting square, and are restored by unmake.
- [ ] **2.4a** Move generation per piece type.
  - Verify: for hand-built positions with a single piece of each type (plus blockers and capturable pieces), the generated moves match a hand-written list.
- [ ] **2.4b** The attack check.
  - Verify: for hand-built positions, attacked / not attacked is right for squares hit by each attacker type (pawn of each colour, knight, bishop, rook, queen, king), including blocked sliders.
- [ ] **2.5** Legal move list (play, attack check, undo), with the `prohibitSelfCheck` option; setting it to `false` skips the check filtering (for Fog of War later).
  - Verify: 20 legal moves from the standard start; pinned pieces and moves into check are excluded; castling out of, through or into check is excluded; with `prohibitSelfCheck = false` those moves are included.
- [ ] **2.6** **Test:** a test behaviour that builds a few positions by hand with the constants (the standard start, plus positions with castling, en passant and promotion available). It checks that make followed by unmake restores every array exactly, and that the legal move counts match counts worked out by hand or with the Java. Include the two Java bug cases: capturing a rook on its starting square removes that side's castling right, and castling is refused when the rook is gone (the Java gets both wrong, so don't compare these with it). Full perft from published positions needs FEN, so it comes in step 3.
  - Verify: the whole step 2 test behaviour logs only `PASS`.

### 3. FEN

**Read:** [udon-constraints](docs/udon-constraints.md) (regex, no I/O), [move-encoding](docs/move-encoding.md) (castling files, undo and halfmove clock), [modes](docs/modes.md) (Chess960), [java-bugs](docs/java-bugs.md) (comparing against the Java), [testing](docs/testing.md)

- [ ] **3.1** FEN parsing and writing, including Chess960 castling letters and the king/rook starting files.
  - Verify: parsing then writing returns the same string for the standard start, Kiwipete, a position with en passant, and Chess960 positions; the king and rook starting files are set; malformed FENs are rejected with a `false` result rather than a broken board.
- [ ] **3.2** **Test:** round-trip a set of FENs; compare against the Java's output for the same positions.
  - Verify: every FEN in the set round-trips, and matches the Java's output except where [Bugs found in the Java](docs/java-bugs.md) explains the difference.
- [ ] **3.3a** **Test:** a test behaviour running perft from published positions (standard start, Kiwipete, the other standard perft positions, some Chess960 positions), loaded from their FENs, and checking the counts. Record the timings.
  - Verify: every count matches the published values, to the depth the Udon speed allows; record the depths and timings next to this item.
- [ ] **3.3b** Published counts only cover normal rules, so also run some positions with `prohibitSelfCheck = false` and compare with the Java's counts for the same setting.
  - Verify: the counts match the Java's, except where [Bugs found in the Java](docs/java-bugs.md) explains the difference.

### 4. Notation, endings and draw detection

**Read:** [legality-checking](docs/legality-checking.md), [move-encoding](docs/move-encoding.md) (undo, halfmove clock), [game-endings](docs/game-endings.md), [pgn](docs/pgn.md) (Export: castling notation), [testing](docs/testing.md)

- [ ] **4.1** SAN generation with direct disambiguation and `+`/`#` suffix; `O-O`/`O-O-O`.
  - Verify: the expected SAN for hand-built positions with two knights or two rooks (disambiguated by file, by rank, and by both), pawn captures, promotions (with and without capture), castling on both sides, check and checkmate.
- [ ] **4.2** Checkmate and stalemate detection.
  - Verify: fool's mate is checkmate; known stalemate positions are stalemate; a position in check with an escape is neither.
- [ ] **4.3a** Halfmove clock (derived from history), and the fifty/seventy-five-move rules.
  - Verify: the halfmove clock resets on captures and pawn moves, includes the starting position's halfmove clock, and the fifty- and seventy-five-move conditions trigger at exactly 100 and 150 plies.
- [ ] **4.3b** Repetition detection.
  - Verify: threefold and fivefold are detected for a knight shuffle; positions that differ only in castling rights or en passant don't count as repeats.
- [ ] **4.3c** Insufficient material.
  - Verify: K v K, K+B v K, K+N v K and K+B v K+B with same-coloured bishops are detected; K+B v K+B with opposite-coloured bishops, and anything with a pawn, rook or queen, are not.
- [ ] **4.4** **Test:** known positions for each ending and draw rule; SAN for ambiguous moves (two knights, two rooks, promotions, captures).
  - Verify: the whole step 4 test behaviour logs only `PASS`.

### 5. PGN

**Read:** [pgn](docs/pgn.md), [udon-constraints](docs/udon-constraints.md) (regex changes for .NET), [game-ids](docs/game-ids.md), [game-endings](docs/game-endings.md) (results, `Termination`), [clocks](docs/clocks.md) (`TimeControl`, `%clk`), [modes](docs/modes.md) (`Variant` tags, also read on import), [replay](docs/replay.md) (Continuing a loaded game: what import keeps), [java-bugs](docs/java-bugs.md) (comparing against the Java), [testing](docs/testing.md)

- [ ] **5.1a** Export: tags (seven-tag roster, `FEN`/`SetUp`, `Variant`, `TimeControl`, `GameID`, `ContinuedFrom`, `Termination`), via `StringBuilder`. The board doesn't exist yet, so export takes the game's data (players, result, history, clocks, Game ID...) as parameters.
  - Verify: for hand-built game data, the tag section matches hand-written expected text, including a standard game, a Chess960 game, a game with no clock, and a continued game.
- [ ] **5.1b** Export: move text with `[%clk]` comments and line wrapping.
  - Verify: the move text matches hand-written expected text; no line exceeds the wrap width, and lines only break at spaces.
- [ ] **5.2a** Import: regex tokenizer (adapted for .NET), reading `FEN`/`Variant`/`White`/`Black`/`Result`/`GameID`.
  - Verify: `Siggi-vs-Nuka.pgn` tokenizes into the expected tags and the right number of move tokens; comments are skipped; other tags are ignored.
- [ ] **5.2b** Import: matching each SAN against legal moves; failure reporting.
  - Verify: `Siggi-vs-Nuka.pgn` imports to the expected final position (FEN); PGNs with an illegal move, an ambiguous move and garbage text fail with the right move number.
- [ ] **5.3** Time-budgeted state machine for import.
  - Verify: importing `Siggi-vs-Nuka.pgn` with a small budget takes several frames and gives the same result as importing in one go; log the frames used and the longest frame's import time.
- [ ] **5.4** **Test:** round-trip `Siggi-vs-Nuka.pgn` and compare with the Java's export; malformed PGNs fail with the right move number.
  - Verify: the whole step 5 test behaviour logs only `PASS`; the move text matches the Java's export, and any tag differences are ones the port adds on purpose.

### 6. Settings (`ChessArbiter`)

**Read:** [settings](docs/settings.md), [organizers](docs/organizers.md), [inactivity](docs/inactivity.md) (the two times, "never"), [game-endings](docs/game-endings.md) (Draw rules), [clocks](docs/clocks.md) (forced time control), [board-lifecycle](docs/board-lifecycle.md) (Starting: forced lobby settings), [testing](docs/testing.md)

- [ ] **6.1a** Setting bits (including "mode forced" and "clock forced") and the five numeric settings (two inactivity times, forced mode, forced base time and increment), instance defaults from the editor.
  - Verify: values set in the inspector are what the arbiter reports in Play Mode.
- [ ] **6.1b** Per-board overrides, effective-settings calculation.
  - Verify: effective settings are right for no overrides, a board overriding some bits on and some off, and a board overriding each numeric setting, with other boards unaffected.
- [ ] **6.2a** `FirstJoinerAuth`, `InstanceMasterAuth`, `NoOneAuth`; default wiring.
  - Verify: in Play Mode the local player is authorized by `FirstJoinerAuth` and `InstanceMasterAuth` and not by `NoOneAuth`; the arbiter prefab has a `FirstJoinerAuth` component and its `SimpleAuth` field points to it.
- [ ] **6.2b** Empty-field build check (editor script).
  - Verify: with the arbiter's `SimpleAuth` field empty, a build/upload is cancelled and the error names the arbiter object; with it set, the build proceeds.
- [ ] **6.2c** In-game fallback (log the error and behave like `NoOneAuth`; the message in the settings UI comes with the settings menu in step 11).
  - Verify: entering Play Mode with the field empty logs the error, and nobody is authorized.
- [ ] **6.3** **Test:** effective settings for combinations of defaults and overrides; the build check blocks an empty field.
  - Verify: the whole step 6 test behaviour logs only `PASS`, and 6.2b's check still blocks.

Taking a board's snapshot of these settings depends on the board's lifecycle, so that's in step 7.

### 7. Logical board, single player (`ChessBoard`, no networking yet)

**Read:** [architecture](docs/architecture.md), [board-lifecycle](docs/board-lifecycle.md), [clocks](docs/clocks.md), [modes](docs/modes.md), [player-input](docs/player-input.md), [game-endings](docs/game-endings.md), [replay](docs/replay.md), [game-ids](docs/game-ids.md), [settings](docs/settings.md) (Snapshot, Re-snapshot), [organizers](docs/organizers.md) (who counts as an organizer), [pgn](docs/pgn.md) (Import, Logging), [networking](docs/networking.md) (Synced fields: the board's state fields), [move-encoding](docs/move-encoding.md), [testing](docs/testing.md)

- [ ] **7.1a** Board state fields; lifecycle (reset, loaded, lobby, in game, ended); Join White / Join Black.
  - Verify: a reset board enters the lobby on the first join; the same player can join both seats; seats can't be joined once a game has started.
- [ ] **7.1b** Mode and clock selection, respecting forced settings; Start; Chess960 position generation.
  - Verify: seated players can change mode and clock unless forced; Start works only with both seats filled; 1000 generated Chess960 positions are all valid (bishops on opposite colours, king between the rooks, same for both sides) and many are distinct.
- [ ] **7.2** Settings snapshot when the board leaves the reset state; re-snapshot on request.
  - Verify: the snapshot is taken on the first join (and on loading a game); changing the arbiter afterwards doesn't affect the board; a re-snapshot picks up the change.
- [ ] **7.3** Clocks: running time from the turn-start timestamp, increment, clock history, flag fall.
  - Verify: with a short time control, a move reduces the mover's time by the time taken and adds the increment; the clock history has one entry per move; a side running out of time is detected.
- [ ] **7.4a** Input API: `PiecePickedUp`, `PieceDropped`, promotion choice.
  - Verify: legal drops are played, illegal ones rejected; a pawn reaching the last rank answers "promotion choice needed", and the follow-up call with each piece plays the right promotion.
- [ ] **7.4b** Input API: castling by drop.
  - Verify: castling by dropping the king on its destination and on its own rook works; in the ambiguous Chess960 cases, the destination drop is the normal move and `KingDroppedOnCastleSpace` castles.
- [ ] **7.4c** Input API: the buttons.
  - Verify: each button method acts only when it applies, and is rejected otherwise (e.g. resign by a non-player).
- [ ] **7.5a** Game endings and result; resign; time forfeit.
  - Verify: checkmate, stalemate, resignation and time forfeit each set the right result; time forfeit is a draw when the opponent can't checkmate and draw rules allow it, and a loss under "No draws".
- [ ] **7.5b** Draw offers and claims per the snapshot's draw rules.
  - Verify: offer/accept/decline works when agreement is on; claims are offered only when the condition holds and the rule is on; automatic draws (fivefold, seventy-five-move, dead position) end the game; under "No draws" none of them do.
- [ ] **7.6** Game IDs and `ContinuedFrom`.
  - Verify: IDs match `yyyyMMddHHmmss-xxxxxx` with the UTC start time; `ContinuedFrom` is `<GameID>/<ply>` with the right ply.
- [ ] **7.7** PGN logging on the "in progress → ended" transition, skipping games with no moves.
  - Verify: the Editor log has exactly one `[ChessPGN]` entry per ended game, with the right result and `Termination`, and none for a game with no moves.
- [ ] **7.8a** Replay mode: replay board, stepping, free moves, custom flag.
  - Verify: stepping reaches every position of the game; free moves change only the replay board and set the custom flag; stepping back onto the game history clears it; leaving replay restores the game exactly.
- [ ] **7.8b** Takeback request/accept (if the snapshot allows takebacks) and truncation (restoring clocks), logging an unfinished game first and giving the continued game a new ID.
  - Verify: an accepted takeback truncates the history and restores the clocks at that ply; a request is cancelled when the replay position changes; it's refused from a custom position or when takebacks are off; an unfinished game is logged once with `*`; the continued game has a new ID and `ContinuedFrom`.
- [ ] **7.9a** Loading a game: take a loaded game's data (a finished PGN import from step 5 now; the archive reuses this in step 10) and enter the loaded state in replay mode, with the mode from the game and its `White`/`Black`/`Result` shown; refused unless the board is in its reset state.
  - Verify: importing `Siggi-vs-Nuka.pgn` puts the board in the loaded state showing the game, with the PGN's names and result; loading onto a board that isn't reset is refused.
- [ ] **7.9b** Joining seats while loaded, choosing the clock, and "Continue from here": truncate at the shown ply, new Game ID with `ContinuedFrom`, fresh clocks, in game.
  - Verify: after joining both seats and continuing from a mid-game ply, the board is in game at that position with the right side to move, fresh clocks, the seated players' names, and `ContinuedFrom` set (or empty when the loaded game had no Game ID).
- [ ] **7.10** Reset: clears the board and both seats, logging an unfinished game that had moves. Allowed for seated players and organizers here; the idle-time rule for everyone else comes with inactivity in step 8.
  - Verify: reset clears everything; an unfinished game with moves is logged once with `*`; an ended game isn't logged again; a non-seated, non-organizer reset is refused.
- [ ] **7.11** Listener notification (`ChessBoardListener`, `OnBoardChanged`, last-change value) and the getters the view needs.
  - Verify: a test listener gets one `OnBoardChanged` per change, with the right last-change value for a move, reset, entering replay and loading a game; the getters agree with the board state.
- [ ] **7.12** **Test:** a test behaviour (also a `ChessBoardListener`) that drives the board through scripted games, replays, takebacks, imported games (including joining and continuing them), resets and every ending, with different settings (including forced lobby settings, "No draws" and takebacks off), checking state, snapshot timing and log output.
  - Verify: the whole step 7 test behaviour logs only `PASS`.

### 8. Networking (`ChessBoard`, `ChessArbiter`)

**Read:** [networking](docs/networking.md), [ownership](docs/ownership.md), [inactivity](docs/inactivity.md), [organizers](docs/organizers.md), [settings](docs/settings.md) (the arbiter's synced settings, re-snapshot), [board-lifecycle](docs/board-lifecycle.md) (Reset), [legality-checking](docs/legality-checking.md) (notation fill-in), [pgn](docs/pgn.md) (Import, Logging), [game-endings](docs/game-endings.md) (log once per client), [architecture](docs/architecture.md) (listeners, for the debug view), [testing](docs/testing.md)

- [ ] **8.1** Debug view: a minimal `ChessBoardListener` that shows the board state as text, with buttons that call the board's input methods (join, start, a few scripted moves, resign, replay, reset) and a few that change settings on the arbiter and re-snapshot. It lets each client act before `ChessBoardView` and the settings menu exist, and stays as a testing tool.
  - Verify: in Play Mode, the buttons play a game through to an ending, and the text matches the board state after each change.
- [ ] **8.2** `ChessArbiter`: synced settings (instance defaults, per-board overrides, inactivity times) changed by an organizer taking ownership; `OnOwnershipRequest` allowing only organizers; `FirstJoinerAuth`'s synced first-joiner name. The board's ownership rules below rely on organizers being recognised on every client.
  - Verify (several clients): a setting changed by the organizer reaches every client; a non-organizer can't take the arbiter; every client agrees who the first joiner is, and they keep organizer rights after rejoining.
- [ ] **8.3** Board synced fields, manual sync, `RequestSerialization`, `OnDeserialization` → local state → listeners.
  - Verify (several clients): moves made on one client appear on the other; a late joiner gets the full state; no synced `string[]` element is ever `null`.
- [ ] **8.4** Ownership rules in `OnOwnershipRequest`: players, organizers, idle-reset exception.
  - Verify (several clients): both players and organizers can take ownership; a bystander is denied while both players are active; the idle-reset exception is checked once 8.5 is in.
- [ ] **8.5** Inactivity: shared server-time timestamp, throttled activity updates, reset-available and auto-reset (including "never").
  - Verify (several clients, short times): reset becomes available to a bystander after the reset-available time; the board resets itself once after the auto-reset time; "never" disables each; handling pieces doesn't serialize more often than the throttle allows.
- [ ] **8.6** Notation sync and fill-in for missing entries.
  - Verify: notation arrives with each move; with entries removed on one client (via the debug view), fill-in recomputes them to match.
- [ ] **8.7** Imported games written in one update at the end; abandon if the board changed.
  - Verify (several clients): an import appears on every client in one update when it finishes; changing the board during an import abandons it and tells the importing player.
- [ ] **8.8** **Test with several clients**, using the debug view: moves both ways, late joiner, owner leaving mid-game, a player rejoining and reclaiming their seat, bystander denied, organizer reset, idle timers, log written once per client, clocks agreeing on every client and a flag fall ending the game once, notation filled in when entries are missing; settings changed by an organizer reach every client, a non-organizer can't take the arbiter, the first joiner keeps organizer rights after rejoining.
  - Verify: every scenario listed behaves as described, and each client's log has exactly one `[ChessPGN]` entry per ended game.

### 9. World presentation (`ChessBoardView`)

**Read:** [architecture](docs/architecture.md), [physical-pieces](docs/physical-pieces.md), [player-input](docs/player-input.md), [board-lifecycle](docs/board-lifecycle.md) (lobby controls), [clocks](docs/clocks.md) (clock display), [replay](docs/replay.md) (replay controls, takeback requests, replay source names), [game-endings](docs/game-endings.md) (draw offers and claims, status display), [testing](docs/testing.md)

- [ ] **9.1** 32 piece objects with mesh swapping; square ↔ object mapping; placing pieces from board state.
  - Verify: in Play Mode the pieces match the board after a reset, a sequence of moves, a promotion, and loading a Chess960 game.
- [ ] **9.2** `VRCPickup` + `VRCObjectSync`; per-player `pickupable` from the board; pieces' ownership check.
  - Verify (several clients): players can grab pieces during their game and bystanders can't; anyone can on a reset board; the pieces' `OnOwnershipRequest` agrees with the board.
- [ ] **9.3a** Forwarding pickups and drops to the board; snapping back on rejection.
  - Verify: legal drops are played; illegal drops snap the piece back; promotion drops ask for a piece.
- [ ] **9.3b** The castle drop space (shown only while that king is held and the castle is legal).
  - Verify: in a Chess960 position where castling is ambiguous, the space appears only while that king is held and the castle is legal, and dropping on it castles.
- [ ] **9.4a** Board buttons, each shown only when it applies: Join White / Join Black, mode and clock selection, Start, "Continue from here" on a loaded game, reset. ("Load PGN" opens the PGN text box, so it comes with that in step 11.)
  - Verify: each button appears only in the states where it applies, and pressing it does what the board's method does.
- [ ] **9.4b** Board buttons, each shown only when it applies: resign, offer / accept / claim draw, enter / leave replay and step, request / accept / decline takeback.
  - Verify: as 9.4a.
- [ ] **9.5a** Promotion picker, highlights (last move, legal targets).
  - Verify: the picker appears on a promotion drop and plays the chosen piece; highlights show the last move and the held piece's legal targets.
- [ ] **9.5b** Move animations.
  - Verify: a move, a capture, castling and en passant animate rather than snap; a reset or load places pieces without odd animations.
- [ ] **9.5c** Status display (turn, clocks, check, result, stalemate, pending offers and requests, replay source names).
  - Verify: each item shows correctly in a game and in replay of a loaded game.
- [ ] **9.6** **Test with several clients:** carrying is visible to others, dropped pieces stay where the board puts them on every client (if `VRCObjectSync` fights the snap, remove it from the pieces), rejected moves snap back everywhere, bystanders can't grab during a game, castling by drop (including the castle drop space in Chess960).
  - Verify: every scenario listed behaves as described on every client.

### 10. Archive and history wall (`GameArchive`)

**Read:** [archive](docs/archive.md), [networking](docs/networking.md) (Limits), [game-ids](docs/game-ids.md), [replay](docs/replay.md) (continuing a loaded game), [board-lifecycle](docs/board-lifecycle.md) (Reset), [game-endings](docs/game-endings.md) (what gets logged/archived), [testing](docs/testing.md)

- [ ] **10.1a** Paged storage (flattened arrays), capacity 200 with oldest dropped, appending whenever a game is logged (finished, or unfinished on reset or takeback).
  - Verify: adding games fills the pages in order; the 201st game drops the oldest; each game reads back with the same data; adding a game reserializes only its page.
- [ ] **10.1b** The retry check for simultaneous additions.
  - Verify (several clients): two boards ending at the same moment both end up in the archive.
- [ ] **10.2a** History wall view.
  - Verify: the wall lists every archived game across all pages, with players and result.
- [ ] **10.2b** "Load it here" buttons on reset boards; loading through the board's load method from step 7, which puts the board in its loaded state.
  - Verify: picking a game shows "Load it here" only on reset boards and only to that player; loading puts the board in its loaded state with the game's notation filled in.
- [ ] **10.3** **Test:** fill past 200 games; two boards ending at once; late joiner sees the full archive; loaded games continue with `ContinuedFrom`.
  - Verify: every scenario listed behaves as described.

### 11. Remaining UI

**Read:** [settings](docs/settings.md), [organizers](docs/organizers.md), [inactivity](docs/inactivity.md), [clocks](docs/clocks.md), [game-endings](docs/game-endings.md) (Draw rules), [pgn](docs/pgn.md) (Import, Input / output), [architecture](docs/architecture.md) (views of instance-wide objects)

- [ ] **11.1a** Settings menu (all boards / per board, on/off/default, timers with "never", forced time control), shown only to organizers.
  - Verify (several clients): only organizers see the controls; each control changes the right default or per-board override, and the change reaches every client.
- [ ] **11.1b** Settings menu: re-snapshot one board or all boards; the error message when the arbiter's `SimpleAuth` field is empty.
  - Verify: re-snapshot updates the chosen boards only; with the field empty, the menu shows the error.
- [ ] **11.2a** PGN text box: copy current PGN.
  - Verify: the box shows the current game's PGN, matching what would be logged.
- [ ] **11.2b** The "Load PGN" button on reset boards, which opens the box to paste into and starts the import; import progress, and the reason when it fails ("failed at move N") or is abandoned because the board changed.
  - Verify: pasting `Siggi-vs-Nuka.pgn` loads it; a malformed PGN shows the failing move; changing the board mid-import shows that it was abandoned.

### 12. Final pass

**Read:** [testing](docs/testing.md), [pgn](docs/pgn.md) (Logging), [game-endings](docs/game-endings.md) (`Termination` values), [udon-constraints](docs/udon-constraints.md) (performance)

- [ ] **12.1** Full multi-client session covering every feature, with a few boards at once.
  - Verify: every feature works with several clients and boards at once; note any problems found as new items.
- [ ] **12.2** Check the output log with a logging bot (or a script reading the log): one PGN per completed game, correct tags.
  - Verify: the script finds exactly one PGN per completed game in each client's log, and every one parses with the right tags.
- [ ] **12.3** Performance: frame times during play, imports and archive loads, on lower-end hardware and in VR.
  - Verify: no noticeable frame drops in any of these; record the measurements next to this item.
