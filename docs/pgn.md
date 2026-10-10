# PGN

## Import (local, rare)

- Started with a "Load PGN" button on a board, which opens the text box to paste into.
- Runs only on the importing player. **The player doesn't take ownership of the board until the import has finished**; only then are the results written to the board's synced fields.
- Starts from the standard position, or from the PGN's `FEN` tag if present (e.g. an imported Chess960 game), then plays through every move in the PGN.
- Tags read: `FEN` (starting position), `Variant` (the mode: `Chess960`, otherwise Standard), `White`, `Black`, `Result` for display in replay, `GameID` so a continued game can reference it, and `TimeControl` (`base` or `base+increment` in seconds, or `-`; any other value counts as unknown). All others are ignored.
- **Clock comments are read; other comments are skipped.** A `[%clk H:MM:SS]` command (seconds may have a fraction, e.g. `0:09:57.6`) in a comment after a move becomes that move's entry in the clock history, as milliseconds: the mover's remaining time after the move, the same value the board records during play (see [Clocks](clocks.md)). The command may share the comment with other text or commands (Lichess writes `{ [%eval 0.17] [%clk 0:04:59] }`). A move without one gets `-1` (unknown), and export writes no `[%clk]` for unknown entries. A clock comment before the first move is ignored.
- Written as a state machine with its state in fields (text position, working board, moves so far). Each frame it processes moves until a time budget runs out, then pauses until the next frame, and stops once the PGN is done.
- Starting budget is 10 ms, but that may cost frames: at 90 Hz in VR a whole frame is ~11 ms. Benchmark it; the budget may need to be smaller, or the whole import may be fast enough to run in one go.
- Reports a failure state ("failed at move N") instead of throwing.
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

## Export

- In the Java, `PGN.write` uses no regex itself, but it replays the whole game from the start and calls `move.notation(board)` for every move. That runs `MovePredicates.parse` (regex) on candidate spellings, and also regenerates legal moves and tests for check and checkmate. Export therefore costs about as much as import.
- The port instead records each move's notation (SAN) when the move is played, using the direct disambiguation described under [Legality checking](legality-checking.md), and syncs it with the board state (see [Networking](networking.md)). Export and logging are then plain string building with `StringBuilder`, with no replay.
- Computing notation needs legal-move generation (to disambiguate, and for the `+`/`#` suffix). Since it is synced, only the player making the move (or the importer) pays that cost; other clients compute it only to fill in missing entries.
- Castling is written as `O-O`/`O-O-O` directly. The Java produces `0-0` and has `PGN.write` swap the zeros for letters afterwards.
- Line wrapping (the Java's `LineWriter`) only splits on spaces; `string.Split(' ')` in C# is not regex-based.

## Player names in tags

- `White` and `Black` hold VRChat display names, which can contain almost any character (e.g. `Siggі` with a Cyrillic `і`, `Nukaǃ` with U+01C3 instead of `!`). `Termination` contains them too (e.g. `Nukaǃ won by checkmate`).
- **Export** escapes tag values as the PGN standard requires: `\` becomes `\\` and `"` becomes `\"`. The Java writes values raw, so a name containing `"` produces a broken tag. Newlines and other control characters are replaced with a space, since a tag must stay on one line (and the log entry is found line by line).
- **Import** reads each tag value up to its closing unescaped `"` and undoes the escapes. The Java's greedy `\[(.*)\w*"(.*)"\]` doesn't handle escapes.
- Names are kept exactly as given, never normalized (see [Ownership](ownership.md)). Anything that shortens a name for display must not split a UTF-16 surrogate pair (emoji and other characters outside the BMP).

## Input / output

- A text box players can open to copy the current PGN out, or paste one in.

## Logging

- Every completed game's PGN is written to the VRChat output log, so players can recover it later and a bot can capture every game in the instance.
- One log call per PGN with a distinctive prefix (e.g. `[ChessPGN]`), so a bot can find it among other log lines.
- Every client needs the full move list to produce the PGN — another reason to sync moves, not just positions.
- New work, not porting: the Java has check and checkmate but no game results. The port adds a synced game-result field (see [Game endings](game-endings.md)).
