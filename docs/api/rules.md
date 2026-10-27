# Rules

The rules without a board: static methods a world calls on arrays of its own, for a puzzle, a FEN on a sign, an opening trainer, or anything else that needs chess but not a **Chess Board**'s seats, clocks and sync. The board itself plays by exactly these methods. Read the [API index](README.md) first: it explains the numbers (colours, squares a1 = 0, pieces as type + 8 for Black, packed moves).

Every class here (`ChessRules`, `ChessPosition`, `ChessFen`, `ChessMoves`, `ChessDraws`, `ChessNotation`, `ChessPgn`, `ChessGame`, in `Assets/Siggi/ChessBoard/Rules/`) is abstract and never placed: call its methods on the class (`ChessFen.ParseFen(…)`). They hold no state: everything goes in through the parameters, and the arrays passed in are the only thing they change.

## A position: the board and state arrays

A position is two arrays of the caller's own:

- **`board`**, a `byte[64]`: the piece on each square (a1 = 0 … h8 = 63), `ChessRules.Empty` for none.
- **`state`**, an `int[ChessPosition.StateLength]` (8), the rest of the position, by these indices on `ChessPosition`:

| Index | Holds |
|---|---|
| `StateSideToMove` (0) | the colour to move, `ChessRules.White` or `Black` |
| `StateCastlingRights` (1) | the castling rights still held, a set of `ChessRules.CastleWhiteKingSide` (1), `CastleWhiteQueenSide` (2), `CastleBlackKingSide` (4), `CastleBlackQueenSide` (8); `CastleAll` (15) is every one. King side is the rook right of the king, also in Chess960. |
| `StateEnPassantFile` (2) | the file (0 to 7) a pawn just moved two squares on, `-1` for none; set after every double push, whether or not a capture is possible |
| `StateKingFile` (3), `StateLeftRookFile` (4), `StateRightRookFile` (5) | the files the king and the castling rooks start on (4, 0, 7 in standard chess), set when the position is loaded and never changed by moves; castling uses them |
| `StateWhiteKingSquare` (6), `StateBlackKingSquare` (7) | each king's square, kept up to date by the moves |

The **halfmove clock** (plies since the last capture or pawn move) and the **fullmove number** aren't part of the position: they come and go as separate `int`s ([FEN](#fen) gives them, and the caller updates them after each move, [below](#making-a-move)).

Fill the arrays only through `ChessFen.ParseFen` or `ChessGame.Chess960Position`: the state's castling files and king squares must agree with the board, which those two see to. After that, change them only with `ChessPosition.MakeMove` and `UnmakeMove`. Copy them with `System.Array.Copy` to keep a position.

## FEN

- **`ChessFen.ParseFen(fen, board, state, out halfmoveClock, out fullmoveNumber)`** → `bool`: reads a FEN into `board` and `state`. The halfmove clock and move number may be left out (0 and 1). Castling takes `KQkq`, Shredder file letters (`HAha`, for Chess960) or a mix. Returns `false`, leaving both arrays untouched, for a FEN that's malformed or not a possible position: bad syntax, not exactly one king a side, a pawn on the first or last rank, the side not to move in check, a castling right without its king and rook on the back rank, an en passant square without the pawn that just moved past it, a negative halfmove clock or a move number below 1.
- **`ChessFen.WriteFen(board, state, halfmoveClock, fullmoveNumber)`** → `string`: the position as FEN. Castling is written `KQkq` when the rooks start on a and h, otherwise as Shredder letters.
- **`ChessFen.StartFen`**: the standard starting position, `rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1`.
- **`ChessGame.Chess960Position(index, board, state)`** → `bool`: sets up Chess960 starting position `index` (0 to `ChessGame.Chess960Count` − 1, numbered as Scharnagl's scheme; `Chess960StandardIndex`, 518, is the standard one), White to move, every right. `false`, changing nothing, for an index out of range. **`ChessGame.Chess960Fen(index)`**: the same as a FEN, `""` out of range.

A board's own position is in the same arrays: `ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber)` is its FEN ([board input](board-input.md#reading-the-position)).

```csharp
using SiggiChessBoard;
using UdonSharp;

// A sign showing a board's position as FEN, for players to copy into an analysis site.
public class FenSign : ChessBoardListener {
    public ChessBoard board;
    public TMPro.TextMeshPro label;

    void Start() {
        board.AddListener(this);
        OnBoardChanged();
    }

    public override void OnBoardChanged() {
        label.text = ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber);
    }
}
```

Before the first game, a board holds the starting position: the sign shows `ChessFen.StartFen`.

## Legal moves

- **`ChessMoves.GenerateLegalMoves(board, state, moves, prohibitSelfCheck)`** → count: every legal move of the side to move, written into `moves` from index 0. An `int[ChessMoves.MaxMoves]` (320) holds any position's.
- **`ChessMoves.GenerateLegalPieceMoves(board, state, from, moves, count, prohibitSelfCheck)`** → new count: the legal moves of the piece on `from`, **appended** at index `count` (pass 0 for a fresh list). Only for a piece of the side to move (check `ChessRules.PieceColorOf(board[from]) == state[ChessPosition.StateSideToMove]` first): the moves carry the side to move's castling and en passant state. None for an empty square.

Pass **`prohibitSelfCheck` `true`**: a move is then legal only if it doesn't leave the mover's king in check, and a castle only if the king isn't in check and doesn't pass through or land on an attacked square, as in chess. `false` drops those checks (the board's Fog of War, where kings can be captured); a world playing chess has no use for it, and every method taking the flag must get the same value for one position.

What the list holds:

- A pawn reaching the last rank has **four moves to the same square**, one per `MovePromotion` (`Queen`, `Rook`, `Bishop`, `Knight`): a world asking "from e7 to e8" must also pick the piece.
- A castle goes from the king's square **to its own rook's square** (`MoveIsCastle`), not to where the king lands (g or c). A world letting players drag the king two squares, or onto g1, maps that to the castle move itself.
- An en passant capture's `MoveTo` is the empty square the pawn lands on; the captured pawn is beside it ([board input](board-input.md#moves) has the decoding).

Both methods leave the position as it was (they play each move on the arrays and take it back).

## Making a move

- **`ChessPosition.MakeMove(board, state, move)`**: plays `move` on the arrays: moves the piece (and the rook of a castle), takes the captured piece off (the pawn beside it for en passant), promotes, updates castling rights, en passant, the king squares and the side to move. **Nothing is checked**: `move` must come from the legal moves of exactly this position, since its packed fields carry the state before it. A move made up by hand, or kept from another position, breaks the arrays.
- **`ChessPosition.UnmakeMove(board, state, move)`**: takes back the move just made with `MakeMove`, restoring both arrays exactly. To go back several moves, unmake them last first, which needs the moves kept (below).

The clock and the move number are the caller's:

```csharp
halfmoveClock = ChessDraws.HalfmoveClockAfter(halfmoveClock, move); // 0 after a capture or pawn move, else + 1
if (state[ChessPosition.StateSideToMove] == ChessRules.White) moveNumber++; // after MakeMove: Black has just moved
```

## Check, mate and stalemate

- **`ChessMoves.PositionStatus(board, state, legalCount, prohibitSelfCheck)`** → `ChessMoves.StatusNormal` (0), `StatusCheck` (1), `StatusCheckmate` (2) or `StatusStalemate` (3), for the side to move. `legalCount` is the position's number of legal moves (`GenerateLegalMoves` with the same flag), which a caller has already.
- **`ChessMoves.IsInCheck(board, state, color)`** → `bool`: whether `color`'s king is attacked (either side, not only the one to move).
- **`ChessMoves.IsSquareAttacked(board, square, byColor)`** → `bool`: whether a piece of `byColor` attacks `square`, whatever stands on it (en passant isn't counted).

## Draws

The draw rules are checks on numbers the caller keeps; whether one draws the game, can be claimed, or is ignored is the world's choice (the board's is in [game endings](../game-endings.md)).

| Method | `true` when |
|---|---|
| `ChessDraws.IsFiftyMoveRule(halfmoveClock)` | 100 plies or more without a capture or pawn move: a draw can be claimed |
| `ChessDraws.IsSeventyFiveMoveRule(halfmoveClock)` | 150 or more: the game is drawn |
| `ChessDraws.IsThreefoldRepetition(count)` | the position has occurred 3 times or more: a draw can be claimed |
| `ChessDraws.IsFivefoldRepetition(count)` | 5 times or more: the game is drawn |
| `ChessDraws.IsInsufficientMaterial(board)` | neither side can mate (kings alone, one knight, or bishops all on one colour of squares besides the kings): the game is drawn |
| `ChessDraws.HasNoMatingMaterial(board, color)` | `color` couldn't mate by any series of legal moves, so its opponent running out of time is a draw rather than a loss |

The thresholds are the constants `FiftyMoveRulePlies` (100), `SeventyFiveMoveRulePlies` (150), `ThreefoldRepetitionCount` (3) and `FivefoldRepetitionCount` (5).

**`ChessDraws.CountRepetitions(board, state, moves, moveCount, halfmoveClock, scratchBoard, scratchState, scratchMoves, prohibitSelfCheck)`** → how many times the current position has occurred, this time included (1 for a new one). `board` and `state` are the position after `moves[0 … moveCount)`, the game's moves from its start (the caller keeps them), and `halfmoveClock` its clock. Positions count as the same when the pieces, side to move, castling rights and the en passant capture actually possible are. It walks back with `UnmakeMove` over the plies since the last capture or pawn move, on copies in the scratch arrays (a `byte[64]`, an `int[ChessPosition.StateLength]`, an `int[ChessMoves.MaxMoves]`), so `board` and `state` are left as they were. Positions before `moves[0]` aren't known to it.

## SAN

- **`ChessNotation.MoveToSan(board, state, move, legalMoves, legalCount, scratch, prohibitSelfCheck)`** → `string`: the move's standard algebraic notation (`"Nf3"`, `"exd5"`, `"e8=Q+"`, `"O-O"`, `"Rd8#"`), with the file or rank added when another piece of the type could go there too. `move` must be one of `legalMoves[0 … legalCount)`, the position's legal moves; `scratch` is an `int[ChessMoves.MaxMoves]` used to work out `+` and `#`. Write it **before** `MakeMove`: it describes the move in the position it's played from.
- **`ChessNotation.MoveFromSan(san, legalMoves, legalCount)`** → the legal move that `san` names, or `ChessNotation.SanNoMove` (−1) when none does or it isn't SAN, `SanAmbiguous` (−2) when it fits more than one. A trailing `+` or `#` is ignored, castling may be written with zeros (`0-0`), a promotion must be given (`"e8=Q"`, not `"e8"`), and over-disambiguated moves (`"Ng1f3"`) are accepted. Packed moves are never negative, so `move < 0` is the failure test.

## PGN

Writing PGN is plain string building from what the caller has kept: the SAN of each move, written as it was played (above).

- **`ChessPgn.WriteTags(eventName, site, date, white, black, ending, winner, mode, startFen, baseSeconds, incrementSeconds, gameId, continuedFrom)`** → the tag section, one `[Name "value"]` line each: the seven-tag roster (`Event`, `Site`, `Date` as `yyyy.MM.dd`, `Round` `-`, `White`, `Black`, `Result`; an empty value is written `?`, an empty date `????.??.??`), `Variant` for Chess960 (`mode` `ChessGame.ModeChess960`, else `ModeStandard`), `SetUp` and `FEN` when `startFen` isn't the standard start (or the game is Chess960; pass `""` for the standard start too), `TimeControl` (`baseSeconds` 0 for no clock), `GameID` and `ContinuedFrom` when not empty, and `Termination` unless the game is still in progress. Values are escaped. The blank line after the tags isn't included.
- **`ChessPgn.WriteMoveText(sans, clocks, moveCount, firstMoveNumber, firstSideToMove, ending, winner, lineWidth)`** → the move text: `moveCount` moves from `sans`, numbered from the starting position's move number and side to move, each with a `{ [%clk H:MM:SS] }` comment when `clocks` (milliseconds left after the move; `null` for none, −1 for one unknown) has its time, then the result, wrapped to `lineWidth` (`ChessPgn.LineWidth`, 80).
- **`ChessPgn.WriteGame(eventName, site, white, black, ending, winner, mode, startFen, baseSeconds, incrementSeconds, gameId, continuedFrom, sans, clocks, moveCount, firstMoveNumber, firstSideToMove)`** → a whole game, tags, blank line and move text, as the board's `GamePgn` writes it. The `Date` comes from `gameId` (the board's Game IDs, [game IDs](../game-ids.md)); with any other ID, or `""`, it's unknown: use `WriteTags` and `WriteMoveText` to give a date.
- The values: **`ChessPgn.ResultText(ending, winner)`** (`"1-0"`, `"0-1"`, `"1/2-1/2"`, `"*"` for a game in progress), **`TerminationText(ending)`** (`"checkmate"`, `"draw agreement"`, …, `""` for none), **`TimeControlText(baseSeconds, incrementSeconds)`** (`"300+2"`, `"-"` for no clock), **`ClockText(milliseconds)`** (`"0:04:59.5"`).

`ending` is one of `ChessGame.Ending…` (`EndingNone` for a game in progress, `EndingCheckmate`, `EndingResignation`, `EndingTimeForfeit`, `EndingStalemate`, `EndingDrawAgreement`, `EndingThreefoldRepetition`, `EndingFivefoldRepetition`, `EndingFiftyMoveRule`, `EndingSeventyFiveMoveRule`, `EndingInsufficientMaterial`, `EndingUnterminated`, `EndingUnknown`; [game endings](../game-endings.md)) and `winner` the winning colour, `ChessRules.NoColor` for a draw. For players:

- **`ChessGame.EndingKey(ending, winner)`**: the language-pack key of the sentence saying how it ended (`"ending_checkmate_white"`, "White wins by checkmate"), shown through Chess Language ([text](text.md)).
- **`ChessGame.EndingName(ending)`** and **`EndingFromName(name)`**: a stable name for each ending (`"checkmate"`, `"agreement"`, …, never translated or changed) and back (−1 for an unknown name), for saving an ending as text.

Reading PGN isn't part of the rules API: a board imports PGN itself (`CanImport`, `StartImport`, [UI](ui.md)).

## Examples

### A puzzle checking the player's move

A mate-in-one: the player picks a piece and a square with the world's own buttons; the script says whether that was the solution.

```csharp
using SiggiChessBoard;
using UdonSharp;
using UnityEngine;

public class MateInOne : UdonSharpBehaviour {
    public string puzzleFen = "6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1";

    byte[] board = new byte[64];
    int[] state = new int[ChessPosition.StateLength];
    int[] moves = new int[ChessMoves.MaxMoves];
    int[] scratch = new int[ChessMoves.MaxMoves];

    void Start() {
        int halfmoveClock, moveNumber;
        if (!ChessFen.ParseFen(puzzleFen, board, state, out halfmoveClock, out moveNumber)) {
            Debug.LogError("[MateInOne] bad FEN: " + puzzleFen);
        }
    }

    // The move from one square to another (a promotion as a queen) as SAN if it mates, "" if it's legal but doesn't
    // mate, null if it isn't legal. The puzzle's position stays as it was.
    public string TryMove(int from, int to) {
        int count = ChessMoves.GenerateLegalMoves(board, state, moves, true);
        int move = -1;
        for (int i = 0; i < count; i++) {
            int promotion = ChessRules.MovePromotion(moves[i]);
            if (ChessRules.MoveFrom(moves[i]) == from && ChessRules.MoveTo(moves[i]) == to
                && (promotion == ChessRules.NoPieceType || promotion == ChessRules.Queen)) {
                move = moves[i];
                break;
            }
        }
        if (move < 0) return null;
        string san = ChessNotation.MoveToSan(board, state, move, moves, count, scratch, true);

        ChessPosition.MakeMove(board, state, move);
        int replies = ChessMoves.GenerateLegalMoves(board, state, moves, true);
        int status = ChessMoves.PositionStatus(board, state, replies, true);
        ChessPosition.UnmakeMove(board, state, move);
        return status == ChessMoves.StatusCheckmate ? san : "";
    }
}
```

`TryMove(ChessRules.D1, ChessRules.D8)` gives `"Rd8#"`, `TryMove(ChessRules.D1, ChessRules.D7)` gives `""` (legal, no mate), `TryMove(ChessRules.D1, ChessRules.E3)` gives `null`. A puzzle whose solution is written as SAN compares `MoveFromSan(solution, moves, count)` with the move found instead, which takes care of promotions and castling.

### A game of the world's own: moves, draws and PGN

The world keeps the game: the position, the moves played and their SAN, the clock and the move number.

```csharp
using SiggiChessBoard;
using UdonSharp;

public class LocalGame : UdonSharpBehaviour {
    byte[] board = new byte[64];
    int[] state = new int[ChessPosition.StateLength];
    int[] legal = new int[ChessMoves.MaxMoves];
    int[] scratchMoves = new int[ChessMoves.MaxMoves];
    byte[] scratchBoard = new byte[64];
    int[] scratchState = new int[ChessPosition.StateLength];
    int[] history = new int[1000];
    string[] sans = new string[1000];
    int count;
    int halfmoveClock;
    int moveNumber;

    public void NewGame() {
        ChessFen.ParseFen(ChessFen.StartFen, board, state, out halfmoveClock, out moveNumber);
        count = 0;
    }

    // Plays a move given as SAN; false if it isn't one legal move here.
    public bool Play(string san) {
        int legalCount = ChessMoves.GenerateLegalMoves(board, state, legal, true);
        int move = ChessNotation.MoveFromSan(san, legal, legalCount);
        if (move < 0) return false;
        sans[count] = ChessNotation.MoveToSan(board, state, move, legal, legalCount, scratchMoves, true);
        ChessPosition.MakeMove(board, state, move);
        halfmoveClock = ChessDraws.HalfmoveClockAfter(halfmoveClock, move);
        if (state[ChessPosition.StateSideToMove] == ChessRules.White) moveNumber++;
        history[count++] = move;
        return true;
    }

    // Takes back the last move.
    public void TakeBack() {
        if (count == 0) return;
        ChessPosition.UnmakeMove(board, state, history[--count]);
        if (state[ChessPosition.StateSideToMove] == ChessRules.Black) moveNumber--;
        // The clock before a move isn't in the move: recount it from the start (the start's clock was 0).
        halfmoveClock = 0;
        for (int i = 0; i < count; i++) halfmoveClock = ChessDraws.HalfmoveClockAfter(halfmoveClock, history[i]);
    }

    // The game's ending as a ChessGame.Ending… value: EndingNone while it goes on. Claimable draws aren't endings
    // until a player claims them (ClaimableDraw).
    public int Ending() {
        int legalCount = ChessMoves.GenerateLegalMoves(board, state, legal, true);
        int status = ChessMoves.PositionStatus(board, state, legalCount, true);
        if (status == ChessMoves.StatusCheckmate) return ChessGame.EndingCheckmate;
        if (status == ChessMoves.StatusStalemate) return ChessGame.EndingStalemate;
        if (ChessDraws.IsInsufficientMaterial(board)) return ChessGame.EndingInsufficientMaterial;
        if (ChessDraws.IsSeventyFiveMoveRule(halfmoveClock)) return ChessGame.EndingSeventyFiveMoveRule;
        if (ChessDraws.IsFivefoldRepetition(Repetitions())) return ChessGame.EndingFivefoldRepetition;
        return ChessGame.EndingNone;
    }

    // A draw the side to move may claim now, or EndingNone.
    public int ClaimableDraw() {
        if (ChessDraws.IsThreefoldRepetition(Repetitions())) return ChessGame.EndingThreefoldRepetition;
        if (ChessDraws.IsFiftyMoveRule(halfmoveClock)) return ChessGame.EndingFiftyMoveRule;
        return ChessGame.EndingNone;
    }

    int Repetitions() {
        return ChessDraws.CountRepetitions(board, state, history, count, halfmoveClock,
            scratchBoard, scratchState, scratchMoves, true);
    }

    public string Fen() {
        return ChessFen.WriteFen(board, state, halfmoveClock, moveNumber);
    }

    public string Pgn(string white, string black, int ending, int winner) {
        return ChessPgn.WriteTags("Casual game", "My World", "", white, black, ending, winner,
                ChessGame.ModeStandard, "", 0, 0, "", "")
            + "\n" + ChessPgn.WriteMoveText(sans, null, count, 1, ChessRules.White, ending, winner, ChessPgn.LineWidth);
    }
}
```

After `NewGame()` and `e4 e5 Nf3 Nf6 Ng1 Ng8 Nf3 Nf6 Ng1 Ng8` (each `Play` returning `true`), the position after `e4 e5` has occurred three times: `ClaimableDraw()` gives `EndingThreefoldRepetition`, `Ending()` `EndingNone`, and `Fen()` gives `rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 8 6`. There, `Play("Ng1")` gives `false` (White's knight is already on g1) and changes nothing; `Play("Nf3")` would be played. `Pgn("Alice", "Bob", ChessGame.EndingThreefoldRepetition, ChessRules.NoColor)` gives:

```
[Event "Casual game"]
[Site "My World"]
[Date "????.??.??"]
[Round "-"]
[White "Alice"]
[Black "Bob"]
[Result "1/2-1/2"]
[TimeControl "-"]
[Termination "threefold repetition"]

1. e4 e5 2. Nf3 Nf6 3. Ng1 Ng8 4. Nf3 Nf6 5. Ng1 Ng8 1/2-1/2
```

A fool's mate (`f3 e5 g4 Qh4#`) ends with `Ending()` giving `EndingCheckmate`, the last SAN `"Qh4#"`; `ChessGame.EndingKey(ChessGame.EndingCheckmate, ChessRules.Black)` is `"ending_checkmate_black"`.

## What the rules don't do

- **No clocks.** Time is the world's: `ChessPgn.ClockText` only formats it.
- **No networking.** The arrays are local to the client running the script. A world sharing its own game syncs what it needs itself, for instance the starting FEN and the packed moves as an `int[]` (every client replays them with `MakeMove`, as the board does), or the FEN as a `string`; the board's own sync is internal.
- **No history.** A move list, the SAN of past moves, the halfmove clocks before each move: whatever the caller keeps. `UnmakeMove` needs the move taken back, and `CountRepetitions` only sees the moves it's given.
- **No player text** besides `ChessGame.EndingKey`'s keys: the rules write FEN, SAN and PGN, which aren't translated.
- **No checks in `MakeMove`.** Only moves from the position's own legal list may be played; the arrays aren't checked for sense after `ParseFen`.
- **No notification.** Nothing tells anyone that a world's arrays changed; its own script redraws after its own calls.

## Costs

Udon runs these far slower than C# would, so call them when something happens, not every frame:

- **`GenerateLegalMoves`** plays and takes back every candidate move with an attack check: the dearest call here. Generate once per position and keep the list and count while the position stands, passing them on (`PositionStatus`, `MoveToSan` and `MoveFromSan` take them for that reason). `GenerateLegalPieceMoves` is the cheap one for a single square's highlights.
- **`MoveToSan`** generates the opponent's replies once more when the move gives check, to tell `+` from `#`.
- **`CountRepetitions`** steps back over every ply since the last capture or pawn move (up to 150 under the seventy-five-move rule), with an en passant check where one is possible.
- **`ParseFen`**, **`Chess960Position`** and **`Chess960Fen`** create small arrays each call; the others create nothing besides the strings they return.
- **Keep the arrays local**, as fields of the calling behaviour, and create them once (`new byte[64]` as a field initializer, as above), not per call: indexing an array is cheap, calling into another behaviour isn't ([udon-constraints](../udon-constraints.md)). A board's own arrays (`board.board`, `board.state`) can be read in place, since reading the field once hands over the array itself; but never pass them to `MakeMove`, `UnmakeMove` or `ParseFen`: copy them first (`System.Array.Copy`), as [board input](board-input.md#reading-the-position) says.
