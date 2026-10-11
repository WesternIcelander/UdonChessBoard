# Testing

Neither codebase has tests yet, and UdonSharp code only runs inside Unity, so tests can't run under a plain .NET test runner.

- A test behaviour, run in Unity Play Mode (with ClientSim), that executes checks and logs pass/fail.
- Perft counts from known positions (standard start, Kiwipete, Chess960 positions) against published values, which avoids depending on the Java. Perft also gives the speed numbers needed to judge the Udon performance concerns (see [Udon constraints](udon-constraints.md)). The deep perft in `RulesTest` (3.3a, 3.3b) takes a couple of minutes, so it sits behind the `deepPerft` Inspector tick. It only exercises move generation and make/unmake (`ChessMoves`, `ChessPosition`, `ChessRules`, and `ChessFen` parsing), so untick it for steps that don't touch those files and tick it again when they change. The shallow perft and move-count checks always run and still catch most move-generation regressions.
- **Networking needs real clients.** ClientSim runs a single local player, so ownership, sync, late joiners and the `VRCObjectSync` behaviour have to be tested with several clients, e.g. VRChat's Build & Test with multiple instances of the client.
- PGN import and export are tested with the PGN test set below, comparing against the Java (on `port-reference`) where the Java can read the file.

## PGN test set

`Assets/Tests/PGN/`, read by test behaviours through `TextAsset` fields (Udon has no file I/O). The files are named `*.pgn.txt` because Unity only imports a fixed list of extensions as `TextAsset`, and `.pgn` isn't one of them. Every expected FEN below comes from the Java on `port-reference`, with its bugs fixed (see [Bugs found in the Java](java-bugs.md)).

Clock history is in milliseconds per ply, `-1` = no `[%clk]` for that move. Files whose row lists no clocks have none (every entry `-1`).

| File | Covers | Plies | Final position |
|---|---|---|---|
| `Siggi-vs-Nuka.pgn.txt` | A real game (copied byte for byte from `JavaChessBoard/`): VRChess quirks `0-0`, no `+`/`#` and no result token at the end of the move text, lookalike names (`Siggі` with Cyrillic `і`, `Nukaǃ` with U+01C3), `TimeControl "600"`. Clocks with tenths on every ply: first two `600000`, `597600`; last `480500` | 54 | `6k1/1b4pp/p3p3/2p1P1b1/NPPn4/7P/P5q1/RN2Kr2 w - - 0 28`, checkmate |
| `lichess-long-draw.pgn.txt` | A long real game (Lichess `h0rrawli`, CC0): Lichess formatting (`1. e4`, `1... g6`, `{ [%clk …] }`), many extra tags, `TimeControl "60+0"`, ends in K v K. Clocks without tenths on every ply: first two `60000`, `60000`; last `8000` | 142 | `8/8/8/3k4/8/4K3/8/8 w - - 0 72`, insufficient material |
| `en-passant.pgn.txt` | En passant by both colours | 10 | `rnbqkbnr/2p1pppp/p2P4/8/8/P1p5/1P1P1PPP/RNBQKBNR w KQkq - 0 6` |
| `promotions.pgn.txt` | Starting `FEN` with Black to move and non-zero counters (move text starts `40...`); promotion with capture and check, underpromotion to a knight, promotion with check; disambiguation by file (`Qgf8+`) | 9 | `Q4Q2/8/8/4k3/8/8/3K4/n6q w - - 3 45` |
| `disambiguation.pgn.txt` | Disambiguation by both file and rank (`Nb5d4`), by rank (`R1a3`), by file (`Nbd2`) | 6 | `8/7k/8/R4N2/3N4/R4N2/3N4/6RK w - - 6 4` |
| `chess960.pgn.txt` | `Variant`/`SetUp`/`FEN` with Shredder castling letters (`GBgb`); `O-O-O` for both colours where the king doesn't move | 6 | `q1krbnrn/pppp1ppp/5b2/4p3/4P3/5B2/PPPP1PPP/Q1KRBNRN w - - 4 4` |
| `stalemate.pgn.txt` | Sam Loyd's ten-move stalemate; `1/2-1/2`; Arabic name | 19 | `5bnr/4p1pq/4Qpkr/7p/7P/4P3/PPPP1PP1/RNB1KBNR b KQ - 2 10`, stalemate |
| `repetition-names.pgn.txt` | Threefold repetition; names with escaped `"` and `\`, an emoji (outside the BMP), `]` and `[` inside the value | 8 | `rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 8 5` |
| `checkmate.pgn.txt` | Fool's mate with `#`; escaped name inside `Termination` | 4 | `rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3`, checkmate |
| `formatting.pgn.txt` | Port-only (the Java can't read it): `%` line, NAGs, `!?`/`?!`, a comment over two lines, nested variations, `;` comment, `5...` after a line break, `+`. Clocks: a `[%clk]` before the first move and one inside a variation (both ignored), one sharing a comment with `[%eval]`; history `300000`, `299500`, then `-1` for the other 22 plies | 24 | `3k1bnr/ppp2ppp/4p3/5b2/4pB2/P7/1PP2PPP/2K2BNR w - - 0 13` (from the Java on the same moves without the extras) |

Malformed, in `malformed/`. Each must fail at the stated move, having played the moves before it:

| File | Fails at | Why |
|---|---|---|
| `illegal-move.pgn.txt` | `2...Nf6` | doesn't answer the check from `2.Qh5+` |
| `ambiguous-move.pgn.txt` | `5.Rc3` | rooks on a3 and h3 can both go there |
| `garbage.pgn.txt` | `2...` (`hello`) | not a PGN token (the Java skips it) |
| `unterminated-comment.pgn.txt` | `2...` (the `{`) | the comment never closes (the Java skips the `{` and plays on) |

- **Java comparison:** for every file except `formatting.pgn.txt` and the malformed ones, the port's import must reach the same final FEN as the Java, and the moves (SAN) in the port's export must match the moves in the Java's export, with each move's `[%clk]` value the same. Other tags and comments differ: an import keeps only what [Replay](replay.md#continuing-a-loaded-game) lists, the port adds some tags on purpose, and the port writes clocks in its own format (`0:10:00` where the source has `0:10:00.0`). The Java's export adds the `+`/`#` that VRChess leaves out, and always writes `O-O`.
- The checker used to produce these values (a small Java class that prints each file's tags, ply count, final FEN and the Java's export) isn't kept in either repository. It's easy to rewrite against `PGN.deserialize`/`PGN.serialize` and `FEN.serialize`.
