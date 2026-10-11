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
| [java-bugs.md](docs/java-bugs.md) | Bugs in the Java reference (castling, pawn captures) |
| [testing.md](docs/testing.md) | Test approach: Play Mode tests, perft, multi-client |
| [open-decisions.md](docs/open-decisions.md) | Open questions (Fog of War only) |

[overview.md](docs/overview.md) and [udon-constraints.md](docs/udon-constraints.md) apply to every step; they're listed below only where a step depends on them directly.

## Implementation steps

Ordered so that each step can be tested before the next builds on it: rules first with no world objects, then settings and the logical board, then networking, then the world. Open decisions that block a step are noted on it.

**Using this checklist.** Each item is sized to finish and verify in one session. Tick it (`- [x]`) only after its **Verify** has passed, and before starting the next item. If the work deviates from the plan, update the plan (and the docs) to match.

- Unless an item says otherwise, verification runs in Unity Play Mode (ClientSim): the step's test behaviour logs `PASS`/`FAIL` per check, and the results are read from the Unity Editor log (`%LOCALAPPDATA%\Unity\Editor\Editor.log`). Each item adds its checks to that test behaviour, so the step's final **Test** item reruns everything.
- "Several clients" means VRChat's Build & Test with multiple client instances (see [Testing](docs/testing.md)).
- Comparing against the Java means building and running it in `JavaChessBoard/` with Maven, on its `port-reference` branch, which has its known bugs fixed (item 1.6; see CLAUDE.md).

### 1. Project setup and early check

**Read:** [overview](docs/overview.md), [udon-constraints](docs/udon-constraints.md), [organizers](docs/organizers.md) (`SimpleAuth`), [java-bugs](docs/java-bugs.md), [testing](docs/testing.md)

- [x] **1.1** Create the Unity project with the VRChat Creator Companion, Worlds SDK 3.7.1 or later.
  - Verify: `Packages/vpm-manifest.json` lists `com.vrchat.worlds` at 3.7.1 or later. (Done: 3.10.5.)
- [x] **1.2** Make the Java library available as `JavaChessBoard/`; move this plan to the project root.
  - Verify: `JavaChessBoard/pom.xml` and `JavaChessBoard/src/main/java/io/siggi/chessboard/` exist; `PLAN.md` is at the project root.
- [x] **1.3** Add the PGN test set in `Assets/Tests/PGN/` (see [PGN test set](docs/testing.md#pgn-test-set)), so test behaviours can read the files through `TextAsset` fields, since Udon has no file I/O. It includes a copy of `JavaChessBoard/Siggi-vs-Nuka.pgn` (original left in place), a long Lichess game, hand-written PGNs for the features Siggi-vs-Nuka doesn't cover, and malformed PGNs. Files are named `*.pgn.txt`, since Unity doesn't import `.pgn` as a `TextAsset`. (Done: Play Mode check with `Assets/Tests/Checks/TextAssetCheck.cs`.)
  - Verify: the Siggi-vs-Nuka copy is byte-identical to the original (`cmp`); every file except `formatting.pgn.txt` and the malformed ones imports in the Java on `port-reference` with the ply count and final FEN listed in the test set, and the malformed ones fail where listed (done); Unity has imported every file (a `.meta` file exists), and a behaviour with a `TextAsset` field set to `Siggi-vs-Nuka.pgn` logs its first line in Play Mode.
- [x] **1.4** Add `SimpleAuth` (`SiggiUSTools`).
  - Verify: `Assets/Siggi/USTools/SimpleAuth.cs` declares the class as in [Organizers](docs/organizers.md), and the Editor log shows it imported with no compile errors.
- [x] **1.5** **Early check: Udon speed.** A throwaway behaviour that generates moves in a loop, to get a feel for real costs before the design relies on them.
  - Verify: in Play Mode it logs moves generated per second (and ms per position); record the numbers next to this item.
  - Done (2026-10-10, `Assets/Tests/Checks/UdonSpeedCheck.cs`): a stand-in generator in the planned style (`byte[64]`, packed moves, static methods, legality by make → attack check → unmake); its sanity check passed (20 legal moves from the start, 48 in Kiwipete). Results in ms per position (moves/s):

    | Phase | Editor (ClientSim) | VRChat client |
    |---|---|---|
    | Start, pseudo-legal (20 moves) | 0.083–0.088 (227k–240k) | 0.127 (158k) |
    | Kiwipete, pseudo-legal (48 moves) | 0.105 (457k) | 0.161 (298k) |
    | Start, legal (20 moves) | 0.49 (41k) | 0.74 (27k) |
    | Kiwipete, legal (48 moves) | 1.29 (37k) | 1.93 (25k) |

    The client is about 1.5× slower than the Editor. A legal move list costs roughly 0.7–2 ms in the client, almost all of it in the legality check (each pseudo-legal move costs one attack check). For a single move in live play that's fine. Anything that needs a legal list for every ply (PGN import, notation fill-in) gets through about 5–10 plies per 10 ms budget, so a long game takes tens of frames. The perft checks in 3.3a need to be spread across frames as well: start depth 4 is about 9,300 legal lists (≈5 s in the Editor), and Kiwipete depth 3 is about 2,100 (≈3 s).
- [x] **1.6** In `JavaChessBoard/`, create a `port-reference` branch from `main` and check it out in place; fix the two castling bugs there (see [Bugs found in the Java](docs/java-bugs.md)) and commit them to that branch only. (Done. Three more bugs turned up and were fixed on the branch too: castling paths accepting any king or rook, castling not removing the rights, and pawns capturing their own pieces. The branch now matches published perft counts.)
  - Verify: `mvn package` succeeds on the branch; in the Java, after a rook is captured on its starting square that side has lost the castling right, and after `Bxh8` capturing the rook Black can't `O-O`; `main` still points at `726e76d`, the commit it was on when this item was written.

The Java's `main` branch is never committed to. Its known bugs are fixed on the `port-reference` branch (1.6), so comparisons against the Java need no exceptions (see [Bugs found in the Java](docs/java-bugs.md)).

Already settled, so no early check is needed:
- `VRCObjectSync` is assumed not to fight a non-owner snapping a piece into place. If step 9 shows it does, pieces stop using `VRCObjectSync` (see [Physical pieces](docs/physical-pieces.md)).
- Synced `string[]` works, but a single `null` element makes the whole serialization fail, so every element must be initialised to an empty string (see [Synced fields](docs/networking.md#synced-fields-per-board)).

### 2. Core rules (static methods, no world)

**Read:** [udon-constraints](docs/udon-constraints.md), [architecture](docs/architecture.md), [move-encoding](docs/move-encoding.md), [legality-checking](docs/legality-checking.md), [modes](docs/modes.md) (`prohibitSelfCheck`, Chess960 castling), [java-bugs](docs/java-bugs.md), [testing](docs/testing.md)

The rules live in `Assets/Siggi/ChessBoard/Rules/` (namespace `SiggiChessBoard`), as static methods and constants on abstract `UdonSharpBehaviour` classes, starting with `ChessRules`. The step 2 test behaviour is `Assets/Tests/Rules/RulesTest.cs`.

- [x] **2.1** Constants and helpers: piece types and colours, square ↔ file/rank, piece encoding in the `byte[64]` board.
  - Verify: the step 2 test behaviour checks square → file/rank → square for all 64 squares, and piece encode → decode for every colour and type, plus empty.
- [x] **2.2** Move encoding: pack/unpack helpers, and the derived en passant / double-push checks.
  - Verify: pack → unpack returns every field unchanged, at each field's minimum and maximum; the derived checks are right for hand-built moves (en passant capture, double push, ordinary pawn move, non-pawn move).
- [x] **2.3a** Make and unmake a move on the board arrays: ordinary moves, captures, en passant, promotion, and en passant restore.
  - Verify: for each kind of move on a hand-built position, the position after make is as expected, and make followed by unmake restores every array exactly.
  - `ChessPosition.MakeMove`/`UnmakeMove` in `Assets/Siggi/ChessBoard/Rules/ChessPosition.cs`, on the board and a state array (see [Architecture](docs/architecture.md)). En passant is set on every double push, as in the Java.
- [x] **2.3b** Make and unmake for castling (Chess960-aware), and castling rights and their restore.
  - Verify: as 2.3a, for both sides' O-O and O-O-O in the standard position and in Chess960 positions (including one where the king doesn't move); castling rights are lost after king and rook moves and when a rook is captured on its starting square, and are restored by unmake.
  - In `ChessPosition.MakeMove`/`UnmakeMove`; rights are lost by any move from or to a king's or rook's starting square (`ChessPosition.CastlingRightsAt`, see [Move encoding](docs/move-encoding.md#castling)).
- [x] **2.4a** Move generation per piece type.
  - Verify: for hand-built positions with a single piece of each type (plus blockers and capturable pieces), the generated moves match a hand-written list.
  - `ChessMoves.GenerateMoves`/`GeneratePieceMoves` in `Assets/Siggi/ChessBoard/Rules/ChessMoves.cs`: pseudo-legal moves into a caller's `int[ChessMoves.MaxMoves]`. The king's moves include castling, checked for the rights, the rook on its starting square and a clear path (only the castling king and rook may stand on it), but not for check, which 2.5 adds. The test also compares whole-side counts with the Java at `prohibitSelfCheck = false` (start 20, Kiwipete 48 / 43 for Black, perft positions 3–5: 16, 38, 44) and makes and unmakes every generated move.
- [x] **2.4b** The attack check.
  - Verify: for hand-built positions, attacked / not attacked is right for squares hit by each attacker type (pawn of each colour, knight, bishop, rook, queen, king), including blocked sliders.
  - `ChessMoves.IsSquareAttacked(board, square, byColor)`, looking outward from the square (see [Legality checking](docs/legality-checking.md)). The test compares the whole attacked set, all 64 squares, with a hand-written list for each position.
- [x] **2.5** Legal move list (play, attack check, undo), with the `prohibitSelfCheck` option; setting it to `false` skips the check filtering (for Fog of War later).
  - Verify: 20 legal moves from the standard start; pinned pieces and moves into check are excluded; castling out of, through or into check is excluded; with `prohibitSelfCheck = false` those moves are included.
  - `ChessMoves.GenerateLegalMoves`/`GenerateLegalPieceMoves(..., prohibitSelfCheck)` filter the pseudo-legal list in place (`FilterLegalMoves`: make, `IsSquareAttacked` on the mover's king, unmake), plus `ChessMoves.IsInCheck`. Castling also needs every square from the king's square to its destination unattacked, checked as in the Java with the king lifted and the rook still on its square; the rook's squares don't count (in Chess960, O-O-O with the king staying on c1 is allowed with d1 attacked). The test also checks whole-side counts against published perft depth 1 (start 20, Kiwipete 48, positions 3–5: 14, 6, 44), and en passant that would expose the king along the rank.
- [x] **2.6** **Test:** a test behaviour that builds a few positions by hand with the constants (the standard start, plus positions with castling, en passant and promotion available). It checks that make followed by unmake restores every array exactly, and that the legal move counts match counts worked out by hand or with the Java. Include the Java bug cases (see [Bugs found in the Java](docs/java-bugs.md)): capturing a rook on its starting square removes that side's castling right, castling is refused when the rook is gone or another piece is in the way, castling removes both of that side's rights, and pawns can't capture their own pieces. Full perft from published positions needs FEN, so it comes in step 3.
  - Verify: the whole step 2 test behaviour logs only `PASS`.
  - `TestPositions` in `RulesTest.cs`: shallow perft (`CheckPerft`, a `[RecursiveMethod]` perft that also checks every root move's make/unmake restores both arrays exactly) for the standard start built with the constants (to depth 3), a hand-built position with castling both ways for both sides, en passant and promotion all available (`r3k2r/1P6/8/3pP3/8/8/8/R3K2R w KQkq d6`: 36, 719, 21611; 36, 852 with `prohibitSelfCheck = false`) and its mirror for Black (same counts), and the published perft positions 2–6 to depth 2 or 3. The counts for the hand-built position come from the Java on `port-reference` (a throwaway perft class run against its jar, which also reproduced every published count used here). The Java bug cases are checked through the legal move list: lines played move by move (`CheckLine`) with the castling rights and the castling moves on offer checked after each ply and every ply undone exactly, plus single-piece lists for a missing rook, pieces in the castling path and pawns next to their own pieces.

### 3. FEN

**Read:** [udon-constraints](docs/udon-constraints.md) (regex, no I/O), [move-encoding](docs/move-encoding.md) (castling files, undo and halfmove clock), [modes](docs/modes.md) (Chess960), [java-bugs](docs/java-bugs.md) (comparing against the Java), [testing](docs/testing.md)

- [x] **3.1** FEN parsing and writing, including Chess960 castling letters and the king/rook starting files.
  - Verify: parsing then writing returns the same string for the standard start, Kiwipete, a position with en passant, and Chess960 positions; the king and rook starting files are set; malformed FENs are rejected with a `false` result rather than a broken board.
  - `ChessFen.ParseFen(fen, board, state, out halfmoveClock, out fullmoveNumber)`/`WriteFen` in `Assets/Siggi/ChessBoard/Rules/ChessFen.cs`, parsed by hand (no regex). On failure the arrays are left untouched. Castling accepts `KQkq` (X-FEN: the outermost rook on that side), Shredder letters, or a mix; writing uses `KQkq` when the rook files are a and h, Shredder letters otherwise, as the Java. Stricter than the Java on purpose (see the end of [Bugs found in the Java](docs/java-bugs.md)). The checks are `TestFen` in `RulesTest.cs`: round trips (the positions above, partial rights, Shredder and X-FEN on Chess960 positions, an inner rook named by its letter, counters left out), the state's files, and 36 malformed FENs that must leave the arrays as they were, plus `null`.
- [x] **3.2** **Test:** round-trip a set of FENs; compare against the Java's output for the same positions.
  - Verify: every FEN in the set round-trips, and matches the Java's output.
  - `TestFenJava` in `RulesTest.cs`: 29 FENs (perft positions 1–6 and position 4 mirrored, en passant including one the pawn can't take, promotion, high counters, the PGN test set's final positions including mate and stalemate, Chess960 in Shredder letters). Each must write back as the Java's `FEN.serialize(FEN.deserialize(fen))`, which is the FEN itself for all but `HAha` with the rooks on a and h (written `KQkq` by both). It also compares the FEN after every legal move with the Java's, as a move count and the int sum of the FENs' Java `String.hashCode` (order-independent, so no move matching is needed); the test works out the halfmove clock and move number after each move as the board will. The expected values come from a throwaway Java class run against `port-reference`'s classes (not kept).
- [x] **3.3a** **Test:** a test behaviour running perft from published positions (standard start, Kiwipete, the other standard perft positions, some Chess960 positions), loaded from their FENs, and checking the counts. Record the timings.
  - Verify: every count matches the published values, to the depth the Udon speed allows; record the depths and timings next to this item.
  - Done (2026-10-11): the deep perft section of `RulesTest.cs` (`StartDeepPerft`), run after the other checks and spread across frames in `Update` (`perftMsPerFrame`, default 20 ms; `deepPerft` off skips it). It walks the tree with an explicit stack instead of recursion, and one walk gives every shallower depth's count too. After each root move the position must be restored exactly. The counts are the published ones (chessprogramming.org Perft Results and Chess960 Perft Results), and the Java on `port-reference` gives the same. All 17 pass; Editor (ClientSim) work time, about 103 s in all:

    | Position | Depth | Final count | Legal lists | Time | ms per list |
    |---|---|---|---|---|---|
    | Standard start | 4 | 197,281 | 9,323 | 8.1 s | 0.87 |
    | Kiwipete | 3 | 97,862 | 2,088 | 4.0 s | 1.90 |
    | Position 3 | 5 | 674,624 | 46,256 | 29.4 s | 0.64 |
    | Position 4 | 4 | 422,333 | 9,738 | 18.4 s | 1.89 |
    | Position 4 mirrored | 4 | 422,333 | 9,738 | 18.2 s | 1.87 |
    | Position 5 | 3 | 62,379 | 1,531 | 2.7 s | 1.73 |
    | Position 6 | 3 | 89,890 | 2,126 | 3.1 s | 1.45 |
    | Chess960 1 | 4 | 326,672 | 12,739 | 12.5 s | 0.98 |
    | Chess960 2–10 (the published list's next nine) | 3 | 10,471–31,058 | 500–1,149 | 0.4–1.3 s each | 0.88–1.11 |

    A legal list costs about 0.9–1.9 ms in the Editor, so about 1.3–2.9 ms in the client at 1.5× slower. That's roughly 1.8× the 1.5 stand-in for the same positions (start 0.87 vs 0.49 ms, Kiwipete 1.90 vs 1.29 ms). The PGN import and notation estimates in 1.5 should be read with that in mind: about 3–7 plies per 10 ms budget rather than 5–10.
- [x] **3.3b** Published counts only cover normal rules, so also run some positions with `prohibitSelfCheck = false` and compare with the Java's counts for the same setting.
  - Verify: the counts match the Java's.
  - Added to the same deep perft section of `RulesTest.cs` (`AddPerftSelfCheckOff`, logged as `..., prohibitSelfCheck off`): standard start (depth 4: 197,742 against the published 197,281), Kiwipete (3), perft position 3 (4), position 4 and its mirror (3; White starts in check, so Black can capture the king at ply 2 and ply 3 is White moving with no king), position 5 (3), the 2.6 castling/en passant/promotion position (3), Chess960 1 (3) and Chess960 3 (4, O-O swaps king and rook). The expected counts come from a throwaway Java perft run against `port-reference`'s classes with `Board.prohibitSelfCheck = false` (not kept); the same class reproduced the 2.6 counts with it on. All 9 pass (2026-10-11), about 4.4 s of Editor work in all: the start at depth 4 in 1.3 s, Chess960 3 at depth 4 in 1.5 s, the rest 0.07–0.33 s each. A move list without the check filter costs 0.07–0.16 ms against 0.9–1.9 ms with it, so the filter (make, attack check, unmake per move) is about 85–90% of a legal list's cost.

### 4. Notation, endings and draw detection

**Read:** [legality-checking](docs/legality-checking.md), [move-encoding](docs/move-encoding.md) (undo, halfmove clock), [game-endings](docs/game-endings.md), [pgn](docs/pgn.md) (Export: castling notation), [testing](docs/testing.md)

- [x] **4.1** SAN generation with direct disambiguation and `+`/`#` suffix; `O-O`/`O-O-O`.
  - Verify: the expected SAN for hand-built positions with two knights or two rooks (disambiguated by file, by rank, and by both), pawn captures, promotions (with and without capture), castling on both sides, check and checkmate.
  - `ChessNotation.MoveToSan(board, state, move, legalMoves, legalCount, scratch, prohibitSelfCheck)` in `Assets/Siggi/ChessBoard/Rules/ChessNotation.cs`, taking the position's legal list from the caller (see [Legality checking](docs/legality-checking.md)). The `+`/`#` suffix makes and unmakes the move; only a check pays for the opponent's legal list (in `scratch`). With `prohibitSelfCheck` off there is no suffix (Fog of War, see [Modes](docs/modes.md)). The step 4 checks go in `RulesTest.cs` (`TestSan`), which keeps one test behaviour for the rules: 42 hand-picked moves (two and three knights, two rooks, a pinned knight that doesn't count unless `prohibitSelfCheck` is off, pawn captures, en passant, promotions and underpromotions with and without capture or check, castling both sides for both colours, with check and in Chess960, fool's mate, a back-rank mate, check with an escape), then every legal move's SAN in 22 positions (those plus perft positions 1–5) against the Java on `port-reference` (a throwaway class printing `notation` for each legal move, not kept). All 64 pass in Play Mode (2026-10-11).
- [x] **4.2** Checkmate and stalemate detection.
  - Verify: fool's mate is checkmate; known stalemate positions are stalemate; a position in check with an escape is neither.
  - `ChessMoves.PositionStatus(board, state, legalCount, prohibitSelfCheck)` returns `StatusNormal`, `StatusCheck`, `StatusCheckmate` or `StatusStalemate` for the side to move (Java `Board.getCheckType`, plus stalemate, which the Java doesn't have). It takes the position's legal move count from the caller, who has the legal list already, so it only adds one attack check. With `prohibitSelfCheck` off there is no check or checkmate, and no move at all reports stalemate for now (see [Open decisions](docs/open-decisions.md)). `MoveToSan` keeps its own check for the suffix, since it only needs the opponent's legal list when the move gives check. Checks in `RulesTest.cs` (`TestStatus`): 20 positions (start; fool's, scholar's, back-rank and smothered mate, Siggi-vs-Nuka's final position; five stalemates for both colours including a pinned knight and Sam Loyd's; check escaped by king move, block, capture and en passant; `prohibitSelfCheck` off). All 20 pass in Play Mode (2026-10-11).
- [x] **4.3a** Halfmove clock (a local per-ply array computed from the move history, not synced), and the fifty/seventy-five-move rules.
  - Verify: the halfmove clock resets on captures and pawn moves, includes the starting position's halfmove clock, undo restores the previous value, rebuilding the array from a move history matches the values recorded while playing it, and the fifty- and seventy-five-move conditions trigger at exactly 100 and 150 plies.
  - `ChessDraws` in `Assets/Siggi/ChessBoard/Rules/ChessDraws.cs` (4.3b and 4.3c add repetition and insufficient material to it): `ResetsHalfmoveClock(move)`, `HalfmoveClockAfter(clock, move)`, `BuildHalfmoveClocks(moves, moveCount, startClock, clocks)` (entry *i* = the clock before ply *i*; returns the current clock), `IsFiftyMoveRule(clock)` (≥ 100) and `IsSeventyFiveMoveRule(clock)` (≥ 150). The board doesn't exist yet, so the array is held by the test, which plays as the board will. Checks in `RulesTest.cs` (`TestHalfmoveClock`): which moves reset (pawn pushes, double pushes, en passant, promotions with and without capture, piece captures; not castling either side, king, knight or rook moves); a 17-ply game from a starting clock of 5 against hand-worked clocks; rebuilding from its history; undo back to the start; undoing five plies, playing on and rebuilding; the thresholds; a knight shuffle first reaching the rules after ply 100 and 150; starting clocks of 95 and 149 counting towards them. All 10 pass in Play Mode (2026-10-11).
- [x] **4.3b** Repetition detection.
  - Verify: threefold and fivefold are detected for a knight shuffle; positions that differ only in castling rights or en passant don't count as repeats.
  - `ChessDraws.CountRepetitions(board, state, moves, moveCount, halfmoveClock, scratchBoard, scratchState, scratchMoves, prohibitSelfCheck)` returns how many times the current position has occurred, counting now, and `IsThreefoldRepetition(count)` (≥ 3) and `IsFivefoldRepetition(count)` (≥ 5) test it. It walks back up to the last `min(halfmoveClock, moveCount)` plies with `UnmakeMove` on a scratch copy, stopping once the castling rights differ, and compares every second position's 64 squares and en passant. En passant only counts when the capture can actually be made (`EffectiveEnPassantFile`: legal with `prohibitSelfCheck` on, pseudo-legal with it off), as FIDE 9.2.3 says, so a double push with no pawn beside it doesn't stop a repeat. Checks in `RulesTest.cs` (`TestRepetition`): a knight shuffle from the start (counts after each ply, threefold first after ply 8, fivefold after ply 16) and from a FEN whose halfmove clock is above the plies played; kings stepping off and back losing castling rights (not counted) and a shuffle keeping one side's rights (counted); en passant after d4 with a capture available (not counted), with no pawn beside it (counted), with the capture illegal because it opens a rank to the king (counted), and the same with `prohibitSelfCheck` off (not counted); the thresholds; `EffectiveEnPassantFile` for eight positions. Every check also confirms `CountRepetitions` leaves the position unchanged. All 10 pass in Play Mode (2026-10-11).
- [x] **4.3c** Insufficient material.
  - Verify: K v K, K+B v K, K+N v K and K+B v K+B with same-coloured bishops are detected; K+B v K+B with opposite-coloured bishops, and anything with a pawn, rook or queen, are not.
  - `ChessDraws.IsInsufficientMaterial(board)`: no pawn, rook or queen, and either a single knight or bishops that all stand on one colour besides the kings (the standard cases, generalised to any number of same-coloured bishops, which can't mate either; see [Draw rules](docs/game-endings.md#draw-rules)). It only looks at the material; whether it applies with `prohibitSelfCheck` off, where kings can be taken, is in [Open decisions](docs/open-decisions.md). Checks in `RulesTest.cs` (`TestInsufficientMaterial`): 22 positions. Dead: K v K (also the `lichess-long-draw.pgn` final position), K+B v K and K+N v K for both colours, K+B v K+B with both bishops dark and both light, K+B+B v K on one colour. Not dead: K+B v K+B and K+B+B v K on opposite colours, a pawn for either side, K+R v K, K v K+Q, K+B v K+P, same-coloured bishops plus a pawn, K+N+N v K, K+N v K+N, K+N v K+B, K+B+N v K, the start. Every check also confirms the board is unchanged. All 22 pass in Play Mode (2026-10-11).
- [x] **4.4** **Test:** known positions for each ending and draw rule; SAN for ambiguous moves (two knights, two rooks, promotions, captures).
  - Verify: the whole step 4 test behaviour logs only `PASS`.
  - The single positions are already covered by 4.1–4.3c (`TestSan`, `TestStatus`, `TestHalfmoveClock`, `TestRepetition`, `TestInsufficientMaterial`). 4.4 adds `TestGames` to `RulesTest.cs`: whole games played as SAN (each move found by its `MoveToSan` among the legal moves, ignoring `+`/`#` and reading `0-0` as `O-O`; not the PGN import, which is step 5) with every ending check run after each ply, as the board will run them. Each game checks the SAN of every move played, the final FEN (halfmove clock and move number included), and the first ply at which each ending (checkmate, stalemate, threefold, fivefold, fifty, seventy-five, insufficient material) holds, so an ending showing up early or in the wrong game fails too. 14 games: the nine PGN test set games with a fixed move list (fool's mate and Siggi-vs-Nuka checkmate at the last ply, Sam Loyd's stalemate, threefold in `repetition-names`, `lichess-long-draw` dead at ply 142 and nothing earlier; en passant, promotions, disambiguation and Chess960 with no ending), against the [PGN test set](docs/testing.md#pgn-test-set)'s final FENs and the files' own SAN (Siggi-vs-Nuka's against the Java's export of it, which adds the `+`/`#` VRChess leaves out); a 16-ply knight shuffle (threefold at 8, fivefold at 16); the fifty-move rule at ply 4 from a halfmove clock of 96, the seventy-five-move rule at ply 4 from 146, a capture restarting the count from 97; and a capture leaving K+N v K. Pre-checked outside Unity (making two expectations wrong fails both). All 415 checks pass in Play Mode (2026-10-11).

### 5. PGN

**Read:** [pgn](docs/pgn.md), [udon-constraints](docs/udon-constraints.md) (regex changes for .NET), [game-ids](docs/game-ids.md), [game-endings](docs/game-endings.md) (results, `Termination`), [clocks](docs/clocks.md) (`TimeControl`, `%clk`), [modes](docs/modes.md) (`Variant` tags, also read on import), [replay](docs/replay.md) (Continuing a loaded game: what import keeps), [java-bugs](docs/java-bugs.md) (comparing against the Java), [testing](docs/testing.md)

- [x] **5.1a** Export: tags (seven-tag roster, `FEN`/`SetUp`, `Variant`, `TimeControl`, `GameID`, `ContinuedFrom`, `Termination`), via `StringBuilder`. The board doesn't exist yet, so export takes the game's data (players, result, history, clocks, Game ID...) as parameters.
  - Verify: for hand-built game data, the tag section matches hand-written expected text, including a standard game, a Chess960 game, a game with no clock, and a continued game.
  - `ChessPgn.WriteTags(eventName, site, date, white, black, ending, winner, mode, startFen, baseSeconds, incrementSeconds, gameId, continuedFrom)` in `Assets/Siggi/ChessBoard/Rules/ChessPgn.cs`, with `ResultText`, `TerminationText`, `TimeControlText` and `EscapeTagValue`; tag order and values in [Export tags](docs/pgn.md#export-tags). The mode and ending constants (`ChessGame.Mode*`, `ChessGame.Ending*`, one per row of the `Termination` table; the winner is `ChessRules.White`/`Black`/`NoColor`) are in `Assets/Siggi/ChessBoard/Rules/ChessGame.cs`, for the board's synced mode and result fields in step 7. Checks in the new step 5 test behaviour `Assets/Tests/PGN/PgnTest.cs` (`TestTags`): a standard game (lookalike names, the standard start's FEN giving no `FEN` tag), a Chess960 game, a Chess960 game from position 518 (still gets its `FEN`), a game with no clock, a continued game in progress (`*`, no `Termination`) and unterminated, escaped names (`"`, `\`, an emoji, `]`/`[`, control characters) with every unknown value, a drawn time forfeit with empty names; every ending's `Termination` and `Result` for each winner; `TimeControl` values; escaping edge cases. Pre-checked outside Unity. All 11 pass in Play Mode (2026-10-11).
- [x] **5.1b** Export: move text with `[%clk]` comments and line wrapping.
  - Verify: the move text matches hand-written expected text; no line exceeds the wrap width, and lines only break at spaces.
  - `ChessPgn.WriteMoveText(sans, clocks, moveCount, firstMoveNumber, firstSideToMove, ending, winner, lineWidth)` and `ClockText(milliseconds)` in `ChessPgn.cs`, taking the recorded SAN and clock history arrays (which may be longer than `moveCount`; `clocks` may be null) and the starting position's fullmove number and side to move; format in [Export move text](docs/pgn.md#export-move-text) (`1. e4 { [%clk 0:10:00] } 1... c5`, Black numbered again after a comment, wrapped at `LineWidth` = 80 between units so a clock comment is never split). Checks in `PgnTest.cs` (`TestMoveText`): clock values (tenths, truncation, hours); Siggi-vs-Nuka's first six plies wrapped at 80 and at 60 (a line exactly the width); some clocks unknown; Black to move first at move 40 with no clocks, unwrapped and at 30; White first at move 7 with castling; units wider than the line; no moves; the whole Siggi-vs-Nuka game unwrapped against a text built move by move, and wrapped at 80 (lines ≤ 80, no edge spaces, no split comment, rejoining gives the unwrapped text). Pre-checked outside Unity (a wrong wrap rule or numbering fails 5 checks). All 23 pass in Play Mode (2026-10-11).
- [x] **5.2a** Import: regex tokenizer (adapted for .NET), reading `FEN`/`Variant`/`White`/`Black`/`Result`/`GameID`/`TimeControl`, and `[%clk]` comments into the clock history.
  - Verify: `Siggi-vs-Nuka.pgn` and `lichess-long-draw.pgn` tokenize into the expected tags and the right number of move tokens; their clock histories match the [PGN test set](docs/testing.md#pgn-test-set); other comments are skipped; other tags are ignored; `formatting.pgn` gives only its 24 main-line moves (see [Import syntax](docs/pgn.md#import-syntax)) and its listed clock history; escaped names in `repetition-names.pgn` and `checkmate.pgn` come out unescaped.
  - `ChessPgn.ReadNext(tokenRegex, clockRegex, pgn, cursor, tags, out san, out clock)` in `ChessPgn.cs`, with `TokenPattern`/`ClockPattern` (the regexes are created and held by the importing behaviour), `StartReading`, the `Tag*`, `Cursor*` and `Read*` constants, `ClockFromComment`, `UnescapeTagValue`, `ParseTimeControl` and `ModeFromVariant`; see [Import reader](docs/pgn.md#import-reader). Its state is an `int[]` cursor, so 5.3 can stop after any move. Details settled on the way (the move text ends at a tag when there's no result token, as in `Siggi-vs-Nuka.pgn`; case-sensitive tag names, so Lichess's `GameId` isn't read; `=` needed in promotions...) are under [Import syntax](docs/pgn.md#import-syntax). `ChessFen.ParseCount` is now public for it. Checks in `PgnTest.cs` (`TestImportValues`, `TestImportFiles`, `TestImportSyntax`), reading the PGN test set through the new `pgnFiles` `TextAsset[]` field (found by name): `[%clk]`, `TimeControl`, unescaping and `Variant` values; every test set file's tags, SAN and clocks (`formatting.pgn` also with `\r\n` line ends), the malformed files' failing ply (`garbage` and `unterminated-comment` fail at ply 3, i.e. `2...`; `illegal-move` and `ambiguous-move` read in full, failing only in 5.2b); and 31 short texts for each accepted and rejected form. Pre-checked outside Unity (74 pass; mutating the castling rename, the clock-before-first-move rule, the move lookahead, the end-at-next-tag rule, unescaping or clock fractions each fails checks). All 74 pass in Play Mode (2026-10-11).
- [x] **5.2b** Import: matching each SAN against legal moves; failure reporting.
  - Verify: every file in the [PGN test set](docs/testing.md#pgn-test-set) imports to its listed ply count and final FEN, and each file in `malformed/` fails at its listed move.
  - `ChessNotation.MoveFromSan(san, legalMoves, legalCount)` (the move, `SanNoMove` or `SanAmbiguous`; the Java's `MovePredicates` rules without regex) and `ChessPgn.ImportNext(...)`, one import step: a `ReadNext`, then for a move matching it, recording its SAN as the board writes it (`MoveToSan`) and playing it. Returns `ImportMore`, `ImportDone` or a failure (`ImportBadSyntax`, `ImportIllegalMove`, `ImportAmbiguousMove`, `ImportBadFen`, `ImportTooLong`). The starting position's fullmove number, side and halfmove clock went into the cursor (`CursorSetUp`, `CursorFirst*`, so `CursorLength` is now 8), and `ImportFailedAt(cursor)`/`MoveLabel` give the failing move as `2...`; see [Import steps](docs/pgn.md#import-steps). Checks in `PgnTest.cs` (`TestSanMatching`, `TestImportGames`, `TestImportFailures`): every legal move's SAN read back (with and without `+`/`#`, the from-square in full, a piece capture without `x`, castling with zeros) in 12 positions (perft 1–5, Chess960 castling with the king already on g1, en passant, promotions, three queens...); 100 hand-written SANs in 7 positions (over-disambiguated, ambiguous, wrong `x`, missing or wrong promotion, `Kg1` for castling, castling over an attacked square); every test set file's ply count, final FEN, recorded SAN (Siggi-vs-Nuka's as the Java exports it) and clocks; a history with room for exactly the game and for one move less; the four malformed files' result, failing move and position before it; and 15 short texts (no moves, a FEN with Black to move at move 40 failing at `40...` and `41.`, a bad FEN, castling through check, `+`/`#` added to the recorded SAN...). Pre-checked outside Unity (124 pass; 12 mutations of the matching rules, the failure ply, the recorded SAN, the error order, the first side and the capacity check each fail checks). All 124 pass in Play Mode (2026-10-11).
- [x] **5.3** Time-budgeted state machine for import.
  - Verify: importing `lichess-long-draw.pgn` (142 plies) with the normal budget, and `Siggi-vs-Nuka.pgn` with a small one, each takes several frames and gives the same result as importing in one go; log the frames used and the longest frame's import time.
  - `ChessPgnImporter` in `Assets/Siggi/ChessBoard/ChessPgnImporter.cs`, the importer behaviour from [Architecture](docs/architecture.md): `StartImport(text, capacity, budgetMs, target, eventName)`, then `ImportNext` in `Update` until the budget (`DefaultBudgetMs` = 10) is used, with at least one step per frame; `eventName` is sent to `target` when it finishes; `Abandon()`; `ImportFor(budget)` for a frame by hand or (huge budget) an import in one go; frame count, longest frame and total time kept. See [Import steps](docs/pgn.md#import-steps). Checks in `PgnTest.cs` (`StartFrameImports`, through its new `importer` field; the summary line now comes after them, a few frames after Start): `lichess-long-draw` with 10 ms, `Siggi-vs-Nuka` with 1 ms, and `formatting` and `malformed/illegal-move` at one step per frame (0 ms). Each is imported in one go (result and ply count checked) and then over frames, and must give the same result, failing move, plies, position, moves, SAN, clocks and tags, in at least 2, 2, 24 and 4 frames. An abandoned import stops after its one frame and sends no event. Each import logs an `importer timing:` line. Pre-checked outside Unity: everything passes except the two "at least 2 frames" checks, since plain .NET imports each of those files in about 1 ms. An importer that doesn't reset its cursor fails the checks. All 133 pass in Play Mode (2026-10-11). Timings in the Editor: `lichess-long-draw` takes 17 frames at 10 ms (longest 12.4 ms, 172 ms in all, the same in one go); `Siggi-vs-Nuka` takes 50 frames at 1 ms (longest 3.8 ms, 89 ms); one step costs about 1–4 ms. The budget question is recorded in [PGN import](docs/pgn.md#import-local-rare).
- [x] **5.4** **Test:** round-trip the [PGN test set](docs/testing.md#pgn-test-set) and compare with the Java's export; malformed PGNs fail with the right move number.
  - Verify: the whole step 5 test behaviour logs only `PASS`; for every file the Java can read, the moves in the port's export match the Java's, and the tag and comment differences are the ones listed in the test set; re-importing the port's own export gives the same game, including names with `"` and `\` and the clock history (tenths kept, `-1` entries staying `-1`).
  - `TestRoundTrip` in `PgnTest.cs`, against the Java's exports of the nine files it can read, now kept in the test set as `Assets/Tests/PGN/java-export/<name>.java.pgn.txt` (added to `pgnFiles`); see [Java exports and round trip](docs/testing.md#java-exports-and-round-trip), which lists the expected tag differences per file. Each file is imported, exported as the board will export a loaded game, and compared: the SAN read back from both exports is identical (`+`/`#` included), the port's clocks are the file's and the Java's the same except the last ply (the Java drops the comment after the last move), the comment counts match, the tag lines differ by exactly the listed lines, the Java's export imports to the same moves and position, and re-importing the port's export gives the same game (moves, SAN, clocks, start and final position, names, `Result`, `GameID`, mode, time control). Also for `formatting.pgn.txt` (port only, `-1` clocks) and a hand-written Chess960 game with `"`/`\` names and clocks with hours, tenths and unknown entries. Malformed files failing at the right move are already checked by 5.2b's `TestImportFailures`. Changed on the way: an unknown time control (no readable `TimeControl` on import, base -1) is now exported as `TimeControl "?"` instead of `"-"` (no clock), so it survives a round trip (`TimeControlText`, 5.1a's checks updated, [Clocks](docs/clocks.md), [Export tags](docs/pgn.md#export-tags)). Pre-checked outside Unity: everything passes except 5.3's two "at least 2 frames" checks, as before; mutating the escaping, the tenths, `-1` clocks, the unknown time control or a Java export each fails round-trip checks. All 144 pass in Play Mode (2026-10-11).

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
  - Verify: importing `Siggi-vs-Nuka.pgn` puts the board in the loaded state showing the game, with the PGN's names and result, its time control and clock history kept, and the board's export giving the same `[%clk]` values; loading onto a board that isn't reset is refused.
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
  - Verify: no noticeable frame drops in any of these; record the measurements next to this item. Set `ChessPgnImporter.DefaultBudgetMs` from the import measurements (the Editor suggests about 5 ms, see [PGN import](docs/pgn.md#import-local-rare)).
