# PGN

## Import (local, rare)

- Started with a "Load PGN" button on a board, which opens the text box to paste into.
- Runs only on the importing player. **The player doesn't take ownership of the board until the import has finished**; only then are the results written to the board's synced fields.
- Starts from the standard position, or from the PGN's `FEN` tag if present (e.g. an imported Chess960 game), then plays through every move in the PGN.
- Tags read: `FEN` (starting position), `Variant` (the mode: `Chess960`, otherwise Standard), `White`, `Black`, `Result` for display in replay, `GameID` so a continued game can reference it, and `TimeControl` (`base` or `base+increment` in seconds, or `-`; any other value counts as unknown). All others are ignored.
- **Clock comments are read; other comments are skipped.** A `[%clk H:MM:SS]` command (seconds may have a fraction, e.g. `0:09:57.6`) in a comment after a move becomes that move's entry in the clock history, as milliseconds: the mover's remaining time after the move, the same value the board records during play (see [Clocks](clocks.md)). The command may share the comment with other text or commands (Lichess writes `{ [%eval 0.17] [%clk 0:04:59] }`). A move without one gets `-1` (unknown), and export writes no `[%clk]` for unknown entries. A clock comment before the first move is ignored.
- Written as a state machine with its state in fields (text position, working board, moves so far). Each frame it processes moves until a time budget runs out, then pauses until the next frame, and stops once the PGN is done.
- Starting budget is 10 ms, but that may cost frames: at 90 Hz in VR a whole frame is ~11 ms. Benchmark it; the budget may need to be smaller, or the whole import may be fast enough to run in one go.
  - **Benchmark (5.3, Unity Editor, 2026-10-11):** about 1.2 ms per ply. One import step (a move: regex, legal moves, SAN with its check test) costs about 1–4 ms. A 142-ply game takes 172 ms in one go, far too long for one frame, so the import must be spread over frames. With the 10 ms budget it took 17 frames, and the longest frame was 12.4 ms: the budget plus one step's overshoot, more than a 90 Hz frame. The budget should therefore be about 11 ms minus the slowest step, roughly 5 ms. Editor timings aren't the VRChat client's, so the default stays 10 ms until it's measured in the client (PLAN 12.3); lowering `DefaultBudgetMs` is the only change needed.
- Reports a failure state ("failed at move N", with the reason) instead of throwing (see [Import steps](#import-steps)).
- The board runs it (8.7): `ChessBoard.StartImport(text)` on the board's own `ChessPgnImporter`, loading and sending the game when it finishes, and abandoning it on any change to the board meanwhile; see [Imported games](networking.md#imported-games).
- **While importing**, the moves are played on a local working board as they are parsed. Nothing is displayed or synced until the import finishes; then the result is written to the board's synced fields in one update, and the board enters replay mode.
- The board isn't locked during an import. If its state changes before the import finishes (e.g. someone starts a game on it), the import is abandoned and the player told why. This avoids a lock that could get stuck if the importing player leaves mid-import.
- "Load PGN" is only offered on boards in the reset state, like loading from the archive.
- The Java's `BufferedReader.mark()/reset()` loop for reading multiple games goes away: input is a single pasted string.

### Import syntax

The Java's tokenizer uses `find()`, so it silently skips anything it doesn't recognise, and it plays the moves inside variations as if they were the main line. The port is stricter: it accepts what real PGN producers write, and fails on anything else.

- **Accepted:**
  - move numbers as `12.` or `12...`, with or without a space before the move;
  - castling as `O-O`/`O-O-O` or `0-0`/`0-0-0`;
  - `+`/`#` present or missing (VRChess writes neither);
  - comments in `{…}` (which may span lines) and `;` to the end of the line;
  - lines starting with `%`;
  - NAGs (`$1`) and move annotations (`!`, `?`, `!!`, `??`, `!?`, `?!`), which are skipped;
  - variations in `(…)`, nested to any depth, which are skipped along with any clock comments inside them;
  - the result token (`1-0`, `0-1`, `1/2-1/2`, `*`), which ends the move text.
- **Failure, reported as "failed at move N"** (the move where it happened, e.g. `2...` for Black's second move): any other token, an unclosed `{` or `(`, and an illegal or ambiguous move.

Details settled while writing the reader (5.2a):

- The move text ends at the result token, at the end of the text (VRChess writes no result token), or at a tag after the move text has started (the next game's tags). Nothing after that is read, so a second game is ignored.
- A move is only read as one when whitespace, a comment, a variation, a NAG or the end follows it, so `e4x` or `hello` fail instead of being read in part. Promotions need the `=` (`e8=Q`; `e8Q` fails). The SAN keeps any `+`/`#` (matching ignores them, 5.2b); castling written with zeros comes back as `O-O`/`O-O-O`.
- A `%` line must start in the first column; ` %` fails.
- Tag names are case-sensitive, as in the PGN standard: Lichess's `[GameId "h0rrawli"]` is not a `GameID` and is ignored. A tag given twice keeps its last value. A tag value must close on its line with an unescaped `"`, or the import fails.
- Move numbers aren't checked against the moves.
- `[%clk]`: hours `H…` (up to 9 digits), minutes and seconds one or two digits, a fraction of any length (digits after the thousandths dropped). The first `[%clk]` in a comment counts; a second clock comment after the same move replaces the first. A clock inside a variation is ignored; one after a variation's `)` belongs to the main-line move before the variation. A value over the `int` milliseconds range (596:31:23.647) counts as no clock.
- `TimeControl`: `-` is no clock (base 0); `base` or `base+increment` with a base above 0 is read; anything else, or no tag, is unknown (base and increment -1).
- A leading byte order mark and `\r\n` line ends are accepted.

### Import reader

`ChessPgn` holds the reader as static methods. The two regexes are created by the behaviour that imports (`new Regex(ChessPgn.TokenPattern)`, `new Regex(ChessPgn.ClockPattern)`, kept in its fields, see [Udon constraints](udon-constraints.md)) and passed in.

- `TokenPattern` matches one token at a position (`\G`), with named groups for each kind (`tag`/`name`/`value`, `result`, `number`, `move`, `comment`, `nag`, `open`, `close`, `end`). Adapted from the Java's `TOKEN_PATTERN` and `TAG_PATTERN`: atomic groups `(?>…)` replace the possessive quantifiers, and since the port fails on unknown text rather than skipping it, each accepted token is an alternative and a position none matches is an error.
- `ReadNext(tokenRegex, clockRegex, pgn, cursor, tags, out san, out clock)` reads up to the next thing the import needs and returns `ReadMove` (a main-line move's SAN), `ReadClock` (a `[%clk]` for the move read last, in milliseconds), `ReadEnd` or `ReadError`. Its state is a small `int[]` cursor (text position, variation depth, main-line moves read, whether the move text has started), set up by `StartReading`, so the import can stop after any move and go on in the next frame (5.3). On `ReadError` the cursor's move count is the failing ply.
- Tags are stored into a `string[TagCount]` (`TagFen`, `TagVariant`, `TagWhite`, `TagBlack`, `TagResult`, `TagGameId`, `TagTimeControl`; `""` when absent), unescaped. They all come before the first move, so the starting FEN is known before the first `ReadMove`.
- Helpers: `ClockFromComment`, `UnescapeTagValue`, `ParseTimeControl`, `ModeFromVariant`.

### Import steps

`ChessPgn.ImportNext(tokenRegex, clockRegex, pgn, cursor, tags, board, state, moves, sans, clocks, legalMoves, scratch, prohibitSelfCheck)` does one step of an import: one `ReadNext`, and for a move, matching it and playing it on the working board. It returns `ImportMore` (call again), `ImportDone`, or a failure: `ImportBadSyntax` (`ReadError`), `ImportIllegalMove`, `ImportAmbiguousMove`, `ImportBadFen` (the `FEN` tag isn't a valid position) or `ImportTooLong` (more moves than the history arrays hold). The time-budgeted state machine (5.3) calls it until the budget runs out.

- **Setting up.** Before the first move (or the end, for a game with no moves), the position is set up from the `FEN` tag, or the standard start without one. Its fullmove number, side to move and halfmove clock are kept in the cursor (`CursorSetUp`, `CursorFirstMoveNumber`, `CursorFirstSideToMove`, `CursorFirstHalfmoveClock`; `StartReading` sets them to "not yet", 1, White, 0). A syntax error before the first move is reported as such even if the `FEN` tag is bad too, since the error may have cut the tags short.
- **Matching.** Each SAN is matched against the position's legal moves by `ChessNotation.MoveFromSan(san, legalMoves, legalCount)`, which returns the move, `SanNoMove` (illegal) or `SanAmbiguous`. It follows the Java's `MovePredicates`, with no regex: `+`/`#` are ignored, castling is `O-O`/`O-O-O` (zeros too), and otherwise the SAN is `[piece][from file][from rank][x]square[=promotion]`:
  - no piece letter is a pawn (`P` is accepted); a given from-file or from-rank must match, so an over-disambiguated move (`Ng1f3`, also `e2e4`) is accepted and a move missing its disambiguation is ambiguous;
  - a pawn's `x` must match whether it captures (`ed5` for `exd5` is illegal); a piece's `x` needs a capture, but a piece capture without `x` (`Qf7` for `Qxf7`) is accepted, as in the Java;
  - the promotion must be given exactly when the move promotes. `e8` alone is illegal (the Java reads it as all four promotions, which fails too);
  - `O-O` is the castling with the rook right of the king, `O-O-O` left (Chess960 too). A king move written to the castling square (`Kg1`) never matches a castling.
- **History.** Ply *i* gets the packed move, the SAN as the board records it (`ChessNotation.MoveToSan`: `+`/`#` added where VRChess leaves them out, extra disambiguation dropped, `O-O`), and its `[%clk]` (`-1` without one). The three arrays must be the same length, which caps the game's length.
- **Over frames.** `ChessPgnImporter` (`Assets/Siggi/ChessBoard/ChessPgnImporter.cs`) is the long-lived behaviour that runs an import as a state machine (see [Architecture](architecture.md)). It holds the regexes, the cursor, the working board and the history in fields:
  - `StartImport(text, capacity, budgetMs, target, eventName)` starts an import into a history of `capacity` moves, and starting again abandons any import under way. From the next `Update` on, each frame runs `ImportNext` until `budgetMs` (`DefaultBudgetMs` = 10) is used up, always at least one step. When the import finishes (done or failed), `eventName` is sent to `target` with `SendCustomEvent`.
  - `Abandon()` stops it with no event, for when the board changes during the import.
  - `paused` (8.7) holds an import under way: `Update` runs no steps while it's set, and starting an import clears it. Only for testing (the debug view holds an import to change the board meanwhile).
  - `ImportFor(budget)` runs one frame's worth by hand. With a huge budget it imports in one go, and sends no event.
  - The result is in its fields (`result`, `cursor`, `tags`, `board`, `state`, `moves`, `sans`, `clocks`) and `MoveCount()`, `FailedAt()` and `StartFen()`. The frames used and the longest frame's import time (`frames`, `longestFrameMs`, `totalMs`) are kept for benchmarking the budget.
  - Imports are Standard or Chess960, so `prohibitSelfCheck` is always on.
  - The board takes a finished import with `ChessBoard.LoadImport(importer)` (7.9a; see [Replay](replay.md#implementation)).
- **Failure reporting.** `cursor[CursorMoves]` is always the number of moves played, also after a failure, so the moves before the failure stay in the history and on the working board. `ImportFailedAt(cursor)` gives the move it failed at as `MoveLabel(firstMoveNumber, firstSideToMove, ply)` text, numbered from the starting position (`2...` for Black's second move, `41.`): the UI shows "failed at move 2...". `ImportErrorText(result)` gives a short reason ("illegal move", "ambiguous move", "not a PGN", "invalid FEN", "game too long").

## Export

- In the Java, `PGN.write` uses no regex itself, but it replays the whole game from the start and calls `move.notation(board)` for every move. That runs `MovePredicates.parse` (regex) on candidate spellings, and also regenerates legal moves and tests for check and checkmate. Export therefore costs about as much as import.
- The port instead records each move's notation (SAN) when the move is played, using the direct disambiguation described under [Legality checking](legality-checking.md), and syncs it with the board state (see [Networking](networking.md)). Export and logging are then plain string building with `StringBuilder`, with no replay.
- Computing notation needs legal-move generation (to disambiguate, and for the `+`/`#` suffix). Since it is synced, only the player making the move (or the importer) pays that cost; other clients compute it only to fill in missing entries.
- Castling is written as `O-O`/`O-O-O` directly. The Java produces `0-0` and has `PGN.write` swap the zeros for letters afterwards.
- Line wrapping (the Java's `LineWriter`) only breaks at spaces. The port wraps as it builds the text, so it needs no splitting (see [Export move text](#export-move-text)).

### Export tags

`ChessPgn.WriteTags` writes the tag section in this order. The Java writes the seven-tag roster first and then whatever tags the `Game` holds; the port builds every tag from the game's own data (see [Continuing a loaded game](replay.md#continuing-a-loaded-game)).

| Tag | Value |
|---|---|
| `Event`, `Site` | given by the caller; the board passes its Inspector fields `pgnEvent` (default empty) and `pgnSite` (default `VRChat`); `?` when empty |
| `Date` | the game's start date, UTC, `yyyy.MM.dd`; `????.??.??` when unknown. The board takes it from the Game ID's first eight digits (`ChessGame.DateFromGameId`), so it's unknown only for an ID in another format |
| `Round` | always `-` |
| `White`, `Black` | the players' names, escaped; `?` when empty |
| `Result` | `1-0`, `0-1`, `1/2-1/2`, or `*` for a game in progress or unterminated (from the ending and winner, `ChessGame.Ending*`) |
| `Variant` | `Chess960`, only for Chess960 games |
| `SetUp`, `FEN` | `1` and the starting FEN, when the game didn't start from the standard position, and always for Chess960 (even position 518, the standard one, since Chess960 readers need the FEN) |
| `TimeControl` | `base` or `base+increment` in seconds (`600`, `300+3`), `-` for no clock, `?` when unknown (a game imported without a readable `TimeControl`, so the unknown time control survives a round trip) |
| `GameID`, `ContinuedFrom` | see [Game IDs](game-ids.md); each left out when empty |
| `Termination` | see [Game endings](game-endings.md); left out for a game still in progress |

### Export move text

`ChessPgn.WriteMoveText` writes the moves' recorded SAN with their clocks, in the PGN standard's export format (as Lichess writes it), e.g.:

```
1. e4 { [%clk 0:10:00] } 1... c5 { [%clk 0:09:57.6] } 2. Nf3
```

- **Numbering** starts from the starting position's fullmove number and side to move. White's moves are written `12. e4`. Black's are numbered `12... e5` when they come first in the game or follow a clock comment, and written alone otherwise (`7. O-O O-O-O`). The Java writes `1.e4` with no space and never repeats the number after a comment.
- **Clocks:** each move with a known clock (`≥ 0`; `-1` is unknown) is followed by `{ [%clk H:MM:SS] }`, with tenths (`H:MM:SS.t`) when they aren't zero (`ChessPgn.ClockText`, see [Clocks](clocks.md)). Hundredths and below are dropped, hours have as many digits as they need. No other comments are written.
- **Result:** the move text ends with the same result as the `Result` tag.
- **Wrapping:** lines are at most `ChessPgn.LineWidth` (80, as in the Java) characters, filled greedily. Lines only break at the space between two units: a move with its number, a clock comment, or the result. A clock comment is therefore never split across lines, and a number never ends a line. A unit wider than the line (only possible with a tiny width) gets a line of its own. Every line ends with `\n`.

## Player names in tags

- `White` and `Black` hold VRChat display names, which can contain almost any character (e.g. `Siggі` with a Cyrillic `і`, `Nukaǃ` with U+01C3 instead of `!`). `Termination` contains them too (e.g. `Nukaǃ won by checkmate`).
- **Export** escapes tag values as the PGN standard requires: `\` becomes `\\` and `"` becomes `\"`. The Java writes values raw, so a name containing `"` produces a broken tag. Newlines and other control characters are replaced with a space, since a tag must stay on one line (and the log entry is found line by line).
- **Import** reads each tag value up to its closing unescaped `"` and undoes the escapes. The Java's greedy `\[(.*)\w*"(.*)"\]` doesn't handle escapes.
- Names are kept exactly as given, never normalized (see [Ownership](ownership.md)). Anything that shortens a name for display must not split a UTF-16 surrogate pair (emoji and other characters outside the BMP).

## Input / output

- A text box players can open to copy the current PGN out, or paste one in.
- **Copying out (step 11.2a):** each board view has a PGN box (`ChessBoardView.pgnBox`, a world-space canvas with a read-only multi-line `InputField`, `pgnField`), opened and closed for the local player with the PGN button ([Player input](player-input.md#pgn-step-112a)). It holds `ChessBoard.GamePgn()`: the game as it stands, so a game in progress shows `*` and no `Termination`, and once the game ends it's the PGN that was logged (without the `[ChessPGN]` line). Replay doesn't change it: it's always the whole game. A loaded game shows its own PGN (players, result and time control from the import or the archive).
  - **Kept up to date** on every `Refresh` while open and when notation is filled in (`OnNotationFilled`), and written only when the text changed, so a selection survives a redraw that changed nothing. The box closes when the game has no moves left (a reset), so it doesn't reopen by itself on the next game.
  - **Where:** upright over the far edge of the board from the end the player stands at (`LocalEnd`, from the local player's position), facing them (`PlacePgnBox`), its bottom edge 10 cm above the board, as wide as the board (440 by 560 mm, 10 mm text, 80 characters a line, `ChessPgn.LineWidth`). A long game scrolls within the field. Its hint line says how to copy on desktop: click the text, then Ctrl+A and Ctrl+C. In VR there's no clipboard to copy to; a finished game's PGN is in the output log either way.
  - Decided in 11.2a: one box per board, local to the player who opened it, rather than one per end or a shared one, since copying is one player's business; the same box takes a pasted PGN in 11.2b. An `InputField` rather than a plain `Text`, since only an input field lets a player select and copy.

## Logging

- Every completed game's PGN is written to the VRChat output log, so players can recover it later and a bot can capture every game in the instance.
- One log call per PGN with a distinctive prefix (e.g. `[ChessPGN]`), so a bot can find it among other log lines.
- Every client needs the full move list to produce the PGN — another reason to sync moves, not just positions.
- **Implementation (7.7):** `ChessBoard.LogGame(ending, winner)` writes one `Debug.Log` entry: a `[ChessPGN]` line, then the PGN (tags, a blank line, the move text, ending with `\n`). A bot takes everything after the prefix line up to the next log entry. `EndGameAt` calls it when the game goes from in game to ended, and it does nothing for a game with no moves. It takes the ending and winner as parameters so that an unfinished game can be logged as `EndingUnterminated` (`*`) without ending it (takeback 7.8b, reset 7.10). `GamePgn()` gives the game's PGN as it stands (for the text box, 11.2a). The board counts the games it has logged in `loggedGames` and keeps the last PGN in `lastLoggedPgn`. Both are local and not synced, for tests. `ReceiveState` (from `OnDeserialization`) logs on the same transition, and logs a game in progress that an update replaces before it was seen to end (see [Networking](networking.md#implementation)).
- The PGN's first move number is the starting position's (`startMoveNumber`), and its side to move is worked out from the current side to move and the number of moves.
- New work, not porting: the Java has check and checkmate but no game results. The port adds a synced game-result field (see [Game endings](game-endings.md)).
