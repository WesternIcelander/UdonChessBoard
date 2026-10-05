# Ownership and sending

- Only the owner of a behaviour can change its synced fields. The player making a move takes ownership (`Networking.SetOwner`), applies the move, updates the fields, then calls `RequestSerialization()`.
- Receivers handle `OnDeserialization` by copying the fields into their local state and refreshing the board visuals. No move generation is needed to stay in sync.
- The two players are recorded in synced fields (white and black).
- **While both players are active, the board refuses to transfer ownership to anyone who isn't one of them**, except to organizers (below), or once the board has been idle long enough for reset (see [Inactivity](inactivity.md)). A player is active if they are in the instance. The check goes in `OnOwnershipRequest`, which returns `false` to deny a transfer.
- **Either player may take ownership at any time**, not only the player to move, since either player can enter replay mode or touch pieces. Ownership flips between them as they act; whoever acts last owns the board and sends the update.
- **When the owner leaves, VRChat gives ownership to a random remaining player without calling `OnOwnershipRequest`** (confirmed). This is consistent with the rule: if the owner was one of the two players, they are leaving, so both players are no longer active.
- The owner isn't always a player: the instance master owns the board by default, and someone pressing reset takes ownership. If such a non-player owner leaves while both players are active, the automatic transfer can again land on a non-player. That's harmless as long as game logic never treats "is the owner" as "may move": a move is valid only if made by the player assigned to that colour, and players can always request ownership back.
- **A player leaving doesn't end or reset the game.** People leave because of VRChat or SteamVR crashes and other technical trouble, and should be able to come back to their game. The board stops being touched, so the [inactivity rules](inactivity.md) apply: other players can reset it after the reset-available time, and it resets itself after the auto-reset time. No leave detection is needed.
- **Players are identified by display name**, not `playerId`, so a player who rejoins after a crash gets their seat back. `playerId` changes on rejoin; the display name doesn't.
- **Organizers** (see [Organizers](organizers.md)) may always take ownership of any board, whatever its state. They then act on it directly, through the same code paths players use: reset, re-snapshot settings, and whatever dispute handling needs.
  - This was chosen over routing organizer actions through per-board request counters on `ChessArbiter`. Counters would need the board's owner to watch the arbiter, track which requests it had handled, and act on them, which is more moving parts for no gain.
  - Taking ownership mid-game is harmless: ownership never grants the right to move, and players take it back on their next action.
  - As between the two players, an organizer acting at the same moment as a player can overwrite that player's update (e.g. a reset landing just after a move). That's acceptable: the organizer's intent wins.

