# Game IDs

- Each game gets a unique ID when it starts, written to its PGN as the custom tag `[GameID "..."]`.
- **Format:** `yyyyMMddHHmmss-xxxxxx`: the time the game started, in UTC, then a random 6-digit number (zero-padded), e.g. `20261005143027-048213`. The suffix makes collisions unlikely between boards starting in the same second, and between instances. The format may be made configurable later for tournaments.
- **Continued games:** a game continued from a replay position gets a new ID and references the original with the custom tag `[ContinuedFrom "<GameID>/<ply>"]`, e.g. `[ContinuedFrom "20261005143027-048213/3"]`. A logging bot can use this to link the two games. So does a game played on untimed after a flag fall (step 12.9), referencing the game that ended on time at its last ply.
- **Ply numbering:** the number of half-moves played from the original game's starting position. 0 is before any move, 1 is after White's first move, 2 after Black's first, 3 after White's second, and so on. For a game whose starting FEN has Black to move, 1 is after Black's first move; it's always a count of half-moves.
- Custom tags are allowed by the PGN standard; other tools ignore tags they don't know.
- **Imported games:** import also reads `GameID`, if present (e.g. a PGN exported from one of these boards), so a game continued from an import can reference it. If the PGN has none, the continued game has no `ContinuedFrom` tag.

## Implementation

- `ChessGame.GameId(utc, suffix)` formats an ID (`""` for a suffix outside 0 to `GameIdSuffixCount` − 1), and `ChessGame.ContinuedFromText(gameId, ply)` gives `<GameID>/<ply>`, or `""` when the game has no ID. An imported `GameID` that isn't in this format (another site's) is referenced as it is.
- The time is VRChat's network time, `Networking.GetNetworkDateTime()` (UTC, the same on every client to within a moment), through `ChessBoard.NetworkDateTime()`; the suffix is `UnityEngine.Random.Range(0, 1000000)` (`ChessBoard.NewGameIdAt(utc)`).
- The board's `gameId` and `continuedFrom` fields. `StartGameAs` gives the game a new ID and clears `continuedFrom`. `ContinueGameIdAt(ply, utc)` is for a game continued from an earlier position (takeback, 7.8b; "Continue from here", 7.9b): it sets `continuedFrom` from the current ID and `ply`, then gives the game a new ID. Its callers pass the replay's ply; truncating the history and the clocks is theirs. A chain of continuations references the game each one came from, not the first.
