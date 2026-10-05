# Game IDs

- Each game gets a unique ID when it starts, written to its PGN as the custom tag `[GameID "..."]`.
- **Format:** `yyyyMMddHHmmss-xxxxxx`: the time the game started, in UTC, then a random 6-digit number (zero-padded), e.g. `20261005143027-048213`. The suffix makes collisions unlikely between boards starting in the same second, and between instances. The format may be made configurable later for tournaments.
- **Continued games:** a game continued from a replay position gets a new ID and references the original with the custom tag `[ContinuedFrom "<GameID>/<ply>"]`, e.g. `[ContinuedFrom "20261005143027-048213/3"]`. A logging bot can use this to link the two games.
- **Ply numbering:** the number of half-moves played from the original game's starting position. 0 is before any move, 1 is after White's first move, 2 after Black's first, 3 after White's second, and so on. For a game whose starting FEN has Black to move, 1 is after Black's first move; it's always a count of half-moves.
- Custom tags are allowed by the PGN standard; other tools ignore tags they don't know.
- **Imported games:** import also reads `GameID`, if present (e.g. a PGN exported from one of these boards), so a game continued from an import can reference it. If the PGN has none, the continued game has no `ContinuedFrom` tag.
