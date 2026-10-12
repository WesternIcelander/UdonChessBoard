# Inactivity

- A board is inactive when nobody has touched anything on it. After the **reset-available time** (default 2 minutes) the reset button becomes available to everyone; after the **auto-reset time** (default 10 minutes) the board resets itself.
- Both times are settings on `ChessArbiter`, configurable in the in-world settings UI, with per-board overrides like the other settings. 
- **Either time can be "never"** (stored as `-1`; `0` would mean immediately), e.g. for tournaments where a crashed player may need longer to get back.
  - Reset-available "never": nobody else can reset the board, however long it's idle.
  - Auto-reset "never": the board never resets itself.
  - When both are set to times, the reset-available time must not exceed the auto-reset time.
- With both set to "never", a board whose player has left for good stays as it is until an organizer resets it. So organizers can always reset a board regardless of the timers. They take ownership of the board and reset it directly (see [Ownership](ownership.md)).
- **Shared clock:** the last activity time is a synced field holding VRChat server time (`Networking.GetServerTimeInSeconds()`), so every client agrees on how long the board has been idle without syncing a timer. The `double` seconds version avoids the wraparound of the `int` milliseconds one. Every change the board sends stamps it (8.4); piece touches that change nothing are added in 8.5.
- **Touching anything on the board counts as activity**: picking up a piece (even dropping it back where it came from, or an illegal drop), stepping through a replay, pressing a board button. Each touch updates the synced timestamp: the player takes ownership and serializes. Throttle it if players fidget with pieces.
- **Exception to the ownership rule:** pressing reset means a non-player takes ownership while both players may still be active. `OnOwnershipRequest` allows the transfer once the board has been idle for at least the reset-available time, measured against the synced timestamp.
- **The automatic reset is done by the current owner only**, so it happens once. If the owner has left, VRChat has already passed ownership to someone else, who then does it.
- Applies whenever the board isn't in its reset state: a game in progress, an ended game, or a replay. A reset board has nothing to reset.
- A game ended by reset is logged as an unfinished game (PGN result `*`); see [Game endings](game-endings.md).
