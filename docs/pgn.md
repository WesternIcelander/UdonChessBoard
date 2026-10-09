# PGN

## Import (local, rare)

- Started with a "Load PGN" button on a board, which opens the text box to paste into.
- Runs only on the importing player. **The player doesn't take ownership of the board until the import has finished**; only then are the results written to the board's synced fields.
- Starts from the standard position, or from the PGN's `FEN` tag if present (e.g. an imported Chess960 game), then plays through every move in the PGN.
- Tags read: `FEN` (starting position), `Variant` (the mode: `Chess960`, otherwise Standard), `White`, `Black`, `Result` for display in replay, and `GameID` so a continued game can reference it. All others are ignored. Comments in the move text are skipped too.
- Written as a state machine with its state in fields (text position, working board, moves so far). Each frame it processes moves until a time budget runs out, then pauses until the next frame, and stops once the PGN is done.
- Starting budget is 10 ms, but that may cost frames: at 90 Hz in VR a whole frame is ~11 ms. Benchmark it; the budget may need to be smaller, or the whole import may be fast enough to run in one go.
- Reports a failure state ("failed at move N") instead of throwing.
- **While importing**, the moves are played on a local working board as they are parsed. Nothing is displayed or synced until the import finishes; then the result is written to the board's synced fields in one update, and the board enters replay mode.
- The board isn't locked during an import. If its state changes before the import finishes (e.g. someone starts a game on it), the import is abandoned and the player told why. This avoids a lock that could get stuck if the importing player leaves mid-import.
- "Load PGN" is only offered on boards in the reset state, like loading from the archive.
- The Java's `BufferedReader.mark()/reset()` loop for reading multiple games goes away: input is a single pasted string.

## Export

- In the Java, `PGN.write` uses no regex itself, but it replays the whole game from the start and calls `move.notation(board)` for every move. That runs `MovePredicates.parse` (regex) on candidate spellings, and also regenerates legal moves and tests for check and checkmate. Export therefore costs about as much as import.
- The port instead records each move's notation (SAN) when the move is played, using the direct disambiguation described under [Legality checking](legality-checking.md), and syncs it with the board state (see [Networking](networking.md)). Export and logging are then plain string building with `StringBuilder`, with no replay.
- Computing notation needs legal-move generation (to disambiguate, and for the `+`/`#` suffix). Since it is synced, only the player making the move (or the importer) pays that cost; other clients compute it only to fill in missing entries.
- Castling is written as `O-O`/`O-O-O` directly. The Java produces `0-0` and has `PGN.write` swap the zeros for letters afterwards.
- Line wrapping (the Java's `LineWriter`) only splits on spaces; `string.Split(' ')` in C# is not regex-based.

## Input / output

- A text box players can open to copy the current PGN out, or paste one in.

## Logging

- Every completed game's PGN is written to the VRChat output log, so players can recover it later and a bot can capture every game in the instance.
- One log call per PGN with a distinctive prefix (e.g. `[ChessPGN]`), so a bot can find it among other log lines.
- Every client needs the full move list to produce the PGN — another reason to sync moves, not just positions.
- New work, not porting: the Java has check and checkmate but no game results. The port adds a synced game-result field (see [Game endings](game-endings.md)).
