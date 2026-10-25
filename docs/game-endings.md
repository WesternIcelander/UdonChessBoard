# Game endings

| Ending | How | PGN `Result` | Logged |
|---|---|---|---|
| Checkmate | detected after a move (not in Fog of War) | `1-0` / `0-1` | yes |
| King capture | Fog of War only (later) | `1-0` / `0-1` | yes |
| Stalemate | detected after a move: no legal moves, not in check; always, regardless of draw settings | `1/2-1/2`, with `[Termination "stalemate"]` | yes |
| Other draws | per the enabled draw rules | `1/2-1/2` | yes |
| Resignation | resign button, pressed by a player for their own colour | `1-0` / `0-1` | yes |
| Time forfeit | a side's clock reaches zero (see [Clocks](clocks.md)) | `1-0` / `0-1`, or `1/2-1/2` if the opponent can't checkmate and draw rules allow | yes |
| Reset of an unfinished game | by a seated player or organizer at any time, anyone after the reset-available time, or automatic after the auto-reset time | `*` | yes, as unfinished |
| Continuing an unfinished game from an earlier position | takeback agreed by both players (see [Replay mode](replay.md)) | `*` | yes, as unfinished, before the history is cut |

- The result is a synced field (in progress, or which ending and who won), set by whoever ends the game, like any other state change.
- Only the player assigned to a colour can resign for it. Pressing resign takes ownership, which players are always allowed to do.
- **Log once per client, on the transition.** Each client logs when it sees the result change from "in progress" to ended, either locally or in `OnDeserialization`. A late joiner whose first sync already shows an ended game doesn't log it. Each client in the instance writes its own log, so a logging bot gets every game.
- A reset after the game has already ended doesn't log it a second time.
- **Games with no moves aren't logged** (or archived), e.g. a board reset from the lobby or right after Start.
- **How the game ended is recorded in the `Termination` tag.** The PGN standard's suggested values (`normal`, `time forfeit`, `abandoned`, `unterminated`, ...) can't tell checkmate from resignation or one draw rule from another, so the board uses descriptive values. `Termination` is free text, so other tools accept them. Standard wording is used where it exists:

  | Ending | `Termination` |
  |---|---|
  | Checkmate | `checkmate` |
  | Resignation | `resignation` |
  | Time forfeit | `time forfeit` |
  | Stalemate | `stalemate` |
  | Draw by agreement | `draw agreement` |
  | Threefold / fivefold repetition | `threefold repetition` / `fivefold repetition` |
  | Fifty / seventy-five-move rule | `fifty-move rule` / `seventy-five-move rule` |
  | Dead position | `insufficient material` |
  | Reset of an unfinished game | `unterminated` |
  | Continued from an earlier position (unfinished) | `unterminated` |
  | King capture (Fog of War, later) | `king captured` |

## Draw rules

Each FIDE draw rule can be switched on or off in the instance settings menu (on the settings object). A **"No draws"** option, selected by default, prohibits draws entirely.

| Rule | FIDE behaviour | Needs |
|---|---|---|
| Agreement | one player offers, the other accepts | draw-offer button and synced pending offer, like takeback requests |
| Threefold repetition | drawn when a player claims it | repetition detection, claim button |
| Fifty-move rule | drawn when a player claims it (50 moves each without a capture or pawn move) | halfmove clock, claim button |
| Fivefold repetition | drawn automatically | repetition detection |
| Seventy-five-move rule | drawn automatically | halfmove clock |
| Dead position | drawn automatically when neither side can checkmate (e.g. king vs king, king and bishop vs king) | insufficient-material check |

- **Halfmove clock:** the Java's `halfMoves` just counts every move, which is not the fifty-move clock. The port resets it on captures and pawn moves, and keeps each ply's value in a local, unsynced array that every client computes from the move history (see [Undo](move-encoding.md#undo)).
- **Repetition detection without hashing:** a repeat can only occur since the last capture, pawn move or castling-rights change, so at most the last halfmove-clock's worth of plies. Walk back through those by undoing moves on a scratch array and compare the 64 squares, side to move, castling rights and en passant. That's at most ~100 comparisons of 64 bytes, once per move, and needs no precomputed hash tables (which would need static fields). Castling rights are only ever lost, so the walk stops at the first move whose before-the-move rights differ, and it can't go back past the first move (positions before a starting FEN aren't known). En passant only makes positions differ when the capture can actually be made (FIDE 9.2.3): a double push sets the en passant file even with no pawn beside it, or with the capture illegal, and that position counts as the same as one without it. (`ChessDraws.CountRepetitions`, `_EffectiveEnPassantFile`.)
- **Dead position:** check only the standard insufficient-material cases (K v K, K+B v K, K+N v K, K+B v K+B with same-coloured bishops), generalised to any number of bishops on either side as long as they all stand on one colour, which can never mate either. Detecting every dead position is impractical. Pawns, rooks, queens, two knights, knight and bishop, or bishops on both colours always count as enough, even when mate can't be forced (`ChessDraws.IsInsufficientMaterial`).
- **Claims:** for threefold and fifty-move, show a "Claim draw" button to the player to move when the condition holds. FIDE also lets a player claim with a move they're about to make; leave that out.
- **Draw result:** PGN `1/2-1/2`, logged like any other ending, with the rule recorded in `Termination`.
- **Stalemate is not one of these switches.** It always ends the game as a stalemate, whatever the draw settings, including "No draws".
- **Under "No draws"**, none of the rules above end a game. Apart from checkmate and stalemate, the only ways to end it are resignation, time forfeit (always a loss) or a reset. In dead positions and repetition or move-count situations, play simply continues.

## Time forfeit

- FIDE 6.9: the side out of time loses, unless its opponent can't checkmate by any series of legal moves, which makes it a draw. That exception is the **dead-position rule's**: it applies only while the snapshot has the dead-position draw rule on, so under "No draws" (or with only the other draw rules on) a timeout is always a loss.
- **"Can't checkmate"** (`ChessDraws.HasNoMatingMaterial(board, color)`): the opponent has only its king, or the whole board is insufficient material (`IsInsufficientMaterial`, the standard cases above). Anything else counts as able to mate, since the flagged side's own pieces can hem its king in: K+N or K+B against a king with a pawn or a minor piece can mate, so the flag loses there.
- The result is the time forfeit ending either way (`Termination "time forfeit"`), with `1/2-1/2` for the draw.
- **Then, by default, play goes on untimed** (step 12.9): the ended game is logged, and a new untimed game continues from its last position (see [Clocks](clocks.md#implementation), Playing on). Organizers can turn it off per board or for all.

## Implementation

On `ChessBoard` (`Assets/Siggi/ChessBoard/ChessBoard.cs`). `EndGameAt(ending, winner, now)` sets `ending` and `winner` (`ChessRules.NoColor` for a draw), enters `LifecycleEnded`, stops a running clock with the side to move's remaining time stored (see [Clocks](clocks.md#implementation)), and lets a pending draw offer lapse. A pending takeback request stays, since an ended game can still be continued. Replay mode is left as it is. The game's PGN is logged on this transition, unless it has no moves (see [PGN](pgn.md#logging)).

- **Checkmate and stalemate:** `_PlayMoveAt` checks the position after every move (`ChessMoves.PositionStatus`): checkmate ends the game with the mover as winner, stalemate as a draw, whatever the draw settings. The automatic draws are checked after them (below).
- **Resign:** `Resign(color)` / `ResignAs(color, name)`, accepted when `CanResignAs` holds (the game running, `name` seated as `color`, in replay too); the other colour wins. A player in both seats chooses the side that resigns.
- **Time forfeit:** `_CheckFlagFall()` / `_CheckFlagFallAt(now)` ends the game when the side to move's flag has fallen (`_FlaggedColorAt`), and returns whether it did; the winner is the opponent, or nobody under the exception above. It works in replay too, since replay doesn't stop the clock. The board calls it from `Update` while a game is in progress; only the owner does, so it happens once (8.3).
- **Automatic draws:** after a move that neither checkmates nor stalemates, `_PlayMoveAt` asks `_AutomaticDraw()`: `EndingFivefoldRepetition` when the position has occurred five times, `EndingSeventyFiveMoveRule` when the halfmove clock has reached 150, `EndingInsufficientMaterial` when `ChessDraws.IsInsufficientMaterial` holds, each only while the snapshot has that rule on (so never under "No draws"). Any of them ends the game as a draw. Checkmate and stalemate are checked first, so a mate on the 150th ply is still a mate.
- **Draw by agreement:** `OfferDraw(color)` / `OfferDrawAs(color, name)`, `AcceptDraw`/`AcceptDrawAs` and `DeclineDraw`/`DeclineDrawAs`, each accepted when its `Can…` check holds ([Buttons](player-input.md#buttons)). An offer sets the synced draw offer to the offering colour; accepting ends the game with `EndingDrawAgreement`; declining clears it. An offer also lapses when the side it was made to plays a move (FIDE 9.1.2.3: making a move rejects it), while the offerer's own move keeps it (FIDE has the offer made after one's move), and when the game ends any other way.
- **Loaded games:** a loaded game's result is a checkmate or stalemate on its last position, otherwise `ChessGame.EndingUnknown` with the winner its `Result` tag gives (no `Termination` written), or none for `*` (see [Replay](replay.md#implementation)). It's never logged: a loaded board doesn't go through the "in progress → ended" transition.
- **Claims:** `ClaimDraw()` / `ClaimDrawAs(name)`, accepted when `CanClaimDrawAs` holds, ends the game with the draw `ClaimableDraw()` names (threefold before fifty-move).
