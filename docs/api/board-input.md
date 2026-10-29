# Board input

The board and its pieces outside the UI: reading the position and the moves, telling the board a player picked up, dropped or promoted a piece, who may touch the pieces, and telling the board's changes apart, for a world's own pieces, its own move entry or its own animations. The package's **Chess Board View** (`ChessBoardView`) is built on exactly this and is the reference for it: its pieces forward their pickups and drops to the board and are placed again from the board after every change. Read the [API index](README.md) first: it explains the numbers (squares a1 = 0, pieces as type + 8 for Black, packed moves), the `Input*` return codes, when a call takes effect on a client that doesn't own the board, and the `…As(name)` forms, which a world doesn't use.

Every input method here acts for the **local player**. Unlike the buttons ([UI](ui.md)), a refused piece input **is** told: the board calls every listener's `OnBoardChanged()` with `lastChange` `ChangeRejected`, on this client only, so a view puts the piece back.

## Reading the position

Two positions can be on a board: the **game's** (the moves played) and, in replay, the **one replay shows** (a ply of the game, or one free moves have changed, [replay](../replay.md)). The getters that show pieces answer for the position shown; the game's turn and moves are the game's.

- **`PieceAt(square)`**: the piece on a square of the position shown (the replay's in replay, the game's otherwise), `ChessRules.Empty` for none and for a square off the board. A display draws from this.
- **`SideToMove()`**: the colour to move in the game, whatever replay shows. **`IsInCheck()`**: whether it's in check in the game.
- **`LastMove()`**: the packed move that led to the position shown (in replay, to the ply shown), `0` for none: the starting position, or a position free moves have changed (`replayCustom`). For the last-move highlight and for animating a move (below).
- **`LegalMovesFrom(square, moves)`**: the legal moves of the piece on `square` in the game, into `moves` (an `int[ChessMoves.MaxMoves]` holds any), and how many. None (0) unless a game is in progress and not in replay with the side to move's time not run out, and none for an empty square or one of the side not to move. A pawn's promotion is four moves to the same square (one per piece type); a castle's `MoveTo` is its **rook's** square (below).
- **`Checks(checkers, kings)`**: the checks in the position shown, for drawing check lines: entry `i` of `checkers` is the square of a piece giving check, entry `i` of `kings` the king it attacks, filled while both arrays have room; returns how many. In the game only the side to move can be in check; a position changed by free moves can have both kings in check. The view keeps 16 (`ChessBoardView.CheckLineCount`).
- **`FallenKingSquare()`**: the square of the king to show fallen, `ChessRules.NoSquare` for none: the checkmated king, or the resigning side's, in a game that ended that way, at its last ply (in the game or in replay). The view tips it over.
- **`CapturedPieces(captured, counts)`**: the pieces standing beside the board in each colour's captured places for the position shown: `counts[color]` of them, their types at `captured[color * ChessCaptures.MaxCaptured + place]` in place order (arrays of `2 * ChessCaptures.MaxCaptured` and 2). Worked out from the board and its moves alone, so every client, a late joiner too, gets the same places; a captured promoted pawn is a pawn. In replay the places follow the ply shown, and pieces free moves take off the board join them.

The status around the position (`replayMode`, `replayIndex`, `replayCustom`, `moveCount`, `notation`, `lifecycle`, …) is the readable state [UI](ui.md#readable-state) lists. The position itself can also be read as the board's arrays, readable (never written) like those fields:

| Field | What it holds |
|---|---|
| `board` | The game's position: `byte[64]`, a piece per square (whatever replay shows). |
| `state` | The game's position's state: `int[ChessPosition.StateLength]`; `state[ChessPosition.StateSideToMove]` is the side to move, the other slots the castling rights, en passant file, castling files and king squares ([rules](rules.md)). |
| `replayBoard` | The position replay shows, `byte[64]`; only meaningful while `replayMode`. |
| `moveHistory` | The game's moves, packed: entry `i` (below `moveCount`) is move `i`, the one `notation[i]` writes. The array has room for more. |
| `halfmoveClock`, `moveNumber` | The game's position's halfmove clock and fullmove number, as FEN writes them. |

`board` and `state` are the arrays the [rules](rules.md) take, so a world can pass them straight to a method that only reads them (`ChessMoves.GenerateLegalMoves(board.board, board.state, moves, true)`, `ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber)`), and copies them (`System.Array.Copy`) before anything that changes them, such as `ChessPosition.MakeMove`. Like every readable field they're replaced by the next update; read them in `OnBoardChanged`, not across frames.

### Moves

A move is a packed `int` ([move encoding](../move-encoding.md)); decode it with `ChessRules`:

- `MoveFrom(move)`, `MoveTo(move)`: its squares. `MovePieceType(move)`: the type that moves (`Pawn` for a promotion).
- `MovePromotion(move)`: the type a pawn becomes, `NoPieceType` if it isn't a promotion.
- `MoveCaptured(move)`: the type taken, `NoPieceType` if none. `IsEnPassantCapture(move)`: an en passant capture, whose pawn stands on the move's from rank and to file (`SquareOf(FileOf(to), RankOf(from))`), not on `to`.
- `MoveIsCastle(move)`: a castle. It goes from the king's square **to its rook's**; the king lands on g (the rook to f) when the rook is right of the king, on c (the rook to d) when left, on its own back rank, in Chess960 too, where either can already stand there ([modes](../modes.md)).
- `IsDoublePush(move)`: a pawn moving two squares.

The SAN of a played move is `notation[i]` (`""` until it's been worked out, which `OnNotationFilled` says). For a move not played yet, `ChessNotation.MoveToSan(board, state, move, legalMoves, legalCount, scratch, true)` writes it from the position's legal moves ([rules](rules.md)).

## Telling the board about pieces

The board remembers nothing between a pickup and a drop: each call is judged on its own, so nothing needs clearing when a player walks off with a piece, and a world's own input needn't pick up before it drops.

| Call | When | Accepted | Rejected |
|---|---|---|---|
| `PiecePickedUp(square)` | A piece on `square` is picked up (the view: its `VRCPickup`'s `OnPickup`, or a click selecting it). | Nothing changes and nobody is told. | Told as `ChangeRejected`: let go of the piece. |
| `PieceDropped(from, to)` | The piece picked up on `from` is let go over `to` (`ChessRules.NoSquare` for beside the board). | The move is played (`ChangeMove`), or, in replay, the free move made (`ChangeFreeMove`). `InputPromotionNeeded`: nothing played yet, see below. | Told as `ChangeRejected`: put the piece back. |
| `PromotionChosen(from, to, pieceType)` | The player chose the piece for a promotion. | The promotion is played (`ChangeMove`). | Told as `ChangeRejected`. |
| `CapturedPickedUp(color, type)` | In replay, a captured piece of `color` and `type` is picked up from its place. | Nothing changes and nobody is told. | Told as `ChangeRejected`. |
| `PlaceCaptured(color, type, square)` | That captured piece is put down on `square`. | The free move is made (`ChangeFreeMove`). | Told as `ChangeRejected`: back to its place. |

Each also counts as a touch, keeping the board from going idle ([inactivity](../inactivity.md)): a touch by the player who'd act next (the side to move's, or in replay whoever may work it) is sent to the other clients now and then as `ChangeActivity`, which they're told of and which changes nothing. The toucher's own listeners aren't told of it. A world's own control that changes nothing on the board can touch it the same way with `Touch()` ([UI](ui.md#idle-times-and-touching-the-board)).

### In a game

- **Who may move:** a game in progress, not in replay, the side to move's time not run out, and the local player seated on the side to move (a player in both seats moves both). `PiecePickedUp(square)` also needs a piece of the side to move on `square`. **`CanPickUp(square)`** answers the same without touching anything or telling anyone; the view makes only those pieces pickups and click targets.
- **A drop is matched** against the legal moves by its from and to squares. One match is played. A drop back on its own square, off the board, or on a square no legal move reaches is rejected.
- **Castling:** a king dropped **on its own rook** castles on that side. That is always unambiguous, so every castle can be made that way, and `PieceDropped(MoveFrom(move), MoveTo(move))` plays any castle as it's encoded. A king dropped on its castling destination (g or c) castles too, unless a normal king move ends there, which is then played instead (Chess960, where castling can move the king one square). A king that castles without moving castles only on its rook, since a drop back is rejected.
- **Promotion:** a pawn dropped on the last rank matches four moves, so `PieceDropped` answers **`InputPromotionNeeded`** and plays nothing. Ask the player for the piece, then call `PromotionChosen(from, to, pieceType)` with the same squares and `ChessRules.Queen`, `Rook`, `Bishop` or `Knight`; any other type is rejected, so a picker that can be cancelled passes `ChessRules.NoPieceType` to put the pawn back. The board keeps nothing meanwhile: `PromotionChosen` can be called without a drop before it, and a promotion whose position has gone (the board changed) is rejected like any move no longer legal. The view's promotion ghosts have no cancel: the pawn waits on its square, and no piece is a pickup, until a ghost is chosen or the board changes (other than a `ChangeActivity` or `ChangePieceSet`, which change no position).
- A move's other effects follow from it: a move that checkmates or stalemates, or makes an automatic draw, ends the game with it (still `ChangeMove`); a move by the side a draw was offered to declines the offer ([UI](ui.md)).

### In replay: free moves

In replay the pieces move freely on the replay board, for whoever may work the replay controls (an organizer, a seated player, or anyone on a board nobody has joined; the loader and organizers on a board locked to its loader). `PiecePickedUp(square)` needs any piece on `square` of the position shown; `PieceDropped(from, to)` puts it on `to`, replacing whatever stood there (kings too), with no legality, castling or promotion, and is told as `ChangeFreeMove`. The position becomes custom (`replayCustom`, `LastMove()` 0), which cancels a pending takeback request; stepping shows the game's positions again. `LegalMovesFrom` gives no moves in replay: every other square is a target. `PromotionChosen` is rejected in replay.

**Captured pieces back** (13.4): in replay a piece standing in a captured place can be put back on the board. `CanPlaceCaptured(color, type)` says whether the local player may (they may touch the pieces, below, and the position shown has that type in that colour's places); `CapturedPickedUp(color, type)` and `PlaceCaptured(color, type, square)` are the pickup and the drop. The piece replaces whatever stood on `square` (which goes to its own colour's places), and the place that empties is the **last** of its type, not necessarily the one the piece was picked up from: the places come from the position alone. Dropped anywhere else (`NoSquare`), it's rejected and goes back. Outside replay captured pieces stay where they are.

### Desktop select-and-place

The view's clicking mode (for desktop players, or chosen per view with `inputStyle`) is the same calls: selecting a piece is `PiecePickedUp(square)`, placing it is `PieceDropped(from, to)`, so the board treats both alike. As the view does it:

- With nothing selected, a click on a piece selects it if `PiecePickedUp` accepts. Its targets are the `MoveTo` squares of `LegalMovesFrom(square, …)`, plus a castle's king destination (g or c) when the king moves; in replay every other square.
- With one selected: a click on it or its square puts it back down (the board isn't asked); on a target, a square or a piece on it (an opponent's to capture, an own rook to castle with; in replay any piece of the other colour), places it there; on another piece `CanPickUp` allows, selects that one instead; anything else is a placing the board rejects, which puts the piece back.
- A placing answered with `InputPromotionNeeded` waits for the piece as a drop does.
- A selection is dropped when the board changes so that `CanPickUp(from)` no longer holds there (the turn passing, the game ending, replay entered or left, the square taken).

## Who may touch the pieces

Touching isn't moving: these decide who may hold a piece at all, and the input methods still decide what a pickup or drop does.

- **`MayTouchPieces()`**: whether the local player may touch the pieces: anyone on a reset board; in replay whoever may make free moves; otherwise (the lobby, a game in progress or ended) the seated players, whichever side is to move. **`PlayerMayTouchPieces(player)`** answers it for any `VRCPlayerApi`: each piece's `ChessPiece.OnOwnershipRequest` answers it, so nobody else takes a piece's ownership.
- **`CanPickUp(square)`**: whether `PiecePickedUp(square)` would be accepted now, without touching or telling anything.
- **`pickupable` per player.** A `VRCPickup`'s `pickupable` is local, so each client sets it for its own player. The view makes a piece a pickup only where the local player could pick it up (VRChat highlights every pickup a hand comes near): on a square where `CanPickUp` holds (in a game the side to move's pieces for its player; in replay every piece for whoever may make free moves), in a captured place only in replay while `MayTouchPieces()` holds, and none while a promotion waits. A world's own pieces do the same, after every `OnBoardChanged` **and on a timer** (the view once a second): whether the local player is an organizer, and whether the side to move's time has run out, change without the board telling anyone. A piece held when it stops being allowed is let go (`VRCPickup.Drop()`).

The pieces aren't synced objects: a carried piece is seen only by its carrier, and every client places the pieces from the board after each change. A world's own pieces should be the same (no `VRCObjectSync`): the board's state is what every client agrees on.

## Being told of changes

Derive from `ChessBoardListener` and add it with `board.AddListener(this)` ([UI](ui.md#being-told-of-changes) has the four methods and every `Change*` value). On `OnBoardChanged()`, `board.lastChange` tells what happened to the pieces:

| `lastChange` | The pieces |
|---|---|
| `ChangeMove` | A move: `LastMove()` is it (moves are only made outside replay). A capture, castle or promotion is in its bits. |
| `ChangeReplayStep` | Replay shows another ply; `LastMove()` is the move that led to it. One ply on is one move forwards; any other step is a jump. |
| `ChangeFreeMove` | A free move in replay, or a captured piece put back: one or two squares changed; `LastMove()` is 0. |
| `ChangeRejected` | Nothing changed (local only): put back the piece the player was holding. |
| `ChangeTakeback`, `ChangeEnteredReplay`, `ChangeLeftReplay` | Another position is shown: the game's from an earlier ply, or replay's. |
| `ChangeReset`, `ChangeLoaded`, `ChangeContinued`, `ChangeStarted` | A new position (the standard start, a loaded game, a Chess960 start). |
| `ChangePieceSet` | A side's piece set changed; **nothing moved**, and a promotion waiting for its piece still waits. |
| `ChangeActivity` | Only a touch from another client; nothing changed. |
| any other | Seats, settings, requests or the result; the pieces are as they were. |

**Place from the board, every time.** After any change, put every piece where `PieceAt` says, rather than applying `lastChange` to what you showed: an update from another client carries the whole state, and several changes can arrive in one update (two moves, or a move and a takeback) with only the last one's `lastChange`. The view rebuilds its piece objects from the position after every change other than a single move, and checks that one too.

**Animating moves.** Keep the ply you last showed (`moveCount`, or `replayIndex` in replay) and whether it was replay. A move to animate is `LastMove()` when the change is `ChangeMove`, not in replay, and `moveCount` is exactly one more than the ply shown; or `ChangeReplayStep` with `replayIndex` one more and no free moves on either side (`replayCustom` false). Then the mover travels from `MoveFrom` to `MoveTo` (taking the promotion's type), the captured piece leaves (from the en passant square for an en passant capture), and a castle moves both the king and its rook to their g/f or c/d squares; all of them leave their squares before either lands, since in Chess960 one's destination can be the other's start. Anything else jumps to the new position. The view also lets other small changes travel (a rejected piece going back, a free move, a step back, a takeback of a few pieces) and puts pieces at once for a reset, a load, a continue, entering or leaving replay, and anything moving more than four of them. `ChangePieceSet` and `ChangeActivity` move nothing: don't clear a waiting promotion or restart an animation for them.

## Example

A world's own move entry: a text field where the player types a move in SAN, played as a drop would be.

```csharp
using SiggiChessBoard;
using UdonSharp;
using UnityEngine;

public class SanMoveEntry : UdonSharpBehaviour {
    public ChessBoard board;
    public TMPro.TMP_InputField input;
    int[] moves = new int[ChessMoves.MaxMoves];

    // The input field's or a Submit button's event.
    public void Submit() {
        // LegalMovesFrom's conditions: a game in progress, not in replay.
        if (board.lifecycle != ChessBoard.LifecycleInGame || board.replayMode) return;
        int count = ChessMoves.GenerateLegalMoves(board.board, board.state, moves, true);
        int move = ChessNotation.MoveFromSan(input.text, moves, count);
        if (move < 0) return; // ChessNotation.SanNoMove or SanAmbiguous
        int from = ChessRules.MoveFrom(move);
        int to = ChessRules.MoveTo(move);
        int promotion = ChessRules.MovePromotion(move);
        // A castle's to is its rook's square, which PieceDropped castles on; a promotion goes straight to its piece.
        int result = promotion != ChessRules.NoPieceType
            ? board.PromotionChosen(from, to, promotion)
            : board.PieceDropped(from, to);
        if (result == ChessBoard.InputAccepted) input.text = "";
        // Rejected: not the local player's turn, or their time has run out.
    }
}
```

The board's own checks decide whether the local player may move: the example doesn't check the seat, and a rejection only tells the listeners `ChangeRejected`, on which the view places its pieces from the board again, changing nothing.
