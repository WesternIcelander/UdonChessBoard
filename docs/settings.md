# Settings: `ChessArbiter`

An arbiter is the official who enforces the rules at a chess event, which matches what this object does.

- Holds the rules settings: whether takebacks are allowed, each draw rule (agreement, threefold, fifty-move, fivefold, seventy-five-move, dead position; "No draws" is all draw rules off), the inactivity times, and an optional forced time control.
- Defaults come from the world's configuration in the editor. Only organizers can change settings in-world.

## Per-board overrides

- Each setting has an instance-wide default, and any board can override any setting, e.g. a tournament board with no takebacks next to casual boards.
- **Encoding:** each setting is one bit, so a full set of settings is one `int`. The arbiter holds:
  - the instance defaults (`int`),
  - per board: an override mask and override values (`int[]` each, indexed by board number).
  - A board's effective settings are `(defaults & ~mask) | (values & mask)`: overridden bits come from the board, the rest from the defaults.
  - The two inactivity times are numbers, not on/off, so they're stored as separate values: instance defaults plus per-board `int[]` overrides (in seconds), with a bit in the override mask saying whether the board overrides each one.
- **Stored on the arbiter, not the board.** Overrides must survive a board reset, which clears all of the board's state, and changing them only needs ownership of the arbiter. Boards are given their board number in the editor.
- The settings menu (a view of the arbiter) lets an organizer pick "all boards" or a specific board, and set each setting to on, off, or "use default" for a board.
- **Snapshot:** a board copies its effective settings into its own synced state when it leaves the reset state (someone joins a seat, or a game is loaded or imported), and uses that copy until it's reset. Changing settings doesn't alter the rules of a board already in use.
- **Re-snapshot on request:** if a mistake was made, an organizer can tell a board, or all boards, to take a new snapshot. The organizer takes ownership of the board (see [Ownership](ownership.md)), the board re-copies its effective settings, and serializes.
