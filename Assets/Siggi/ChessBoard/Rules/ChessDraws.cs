using UdonSharp;

namespace SiggiChessBoard {
    // Draw rules (docs/game-endings.md#draw-rules): the halfmove clock and the fifty/seventy-five-move rules, and
    // threefold/fivefold repetition, and dead positions by insufficient material.
    // Whether a rule is switched on, and checkmate or stalemate taking precedence, are up to the board.
    // Holds no state; everything comes in through parameters (see docs/architecture.md).
    // Abstract because it is never placed as a component, only used for its constants and static methods.
    public abstract class ChessDraws : UdonSharpBehaviour {
        // Plies without a capture or pawn move after which a draw can be claimed (50 moves each) or is automatic
        // (75 moves each).
        public const int FiftyMoveRulePlies = 100;
        public const int SeventyFiveMoveRulePlies = 150;

        // Whether move sets the halfmove clock to 0: any pawn move (en passant and promotions included) or capture.
        // Castling captures nothing, since its to-square is the mover's own rook and the captured field is empty.
        public static bool ResetsHalfmoveClock(int move) {
            return ((move >> ChessRules.MovePieceTypeShift) & ChessRules.MoveTypeMask) == ChessRules.Pawn
                || ((move >> ChessRules.MoveCapturedShift) & ChessRules.MoveTypeMask) != ChessRules.NoPieceType;
        }

        // The halfmove clock after playing move with the clock at clock. (The Java's halfMoves never reset, see
        // docs/java-bugs.md.)
        public static int HalfmoveClockAfter(int clock, int move) {
            return ResetsHalfmoveClock(move) ? 0 : clock + 1;
        }

        // Works out the halfmove clock before each ply of moves[0..moveCount) into clocks[0..moveCount), starting
        // from the starting position's clock, and returns the clock after the last move (the current clock).
        // Undoing ply i restores clocks[i]. The array is local, not synced: every client rebuilds it from the synced
        // move history and starting clock (docs/move-encoding.md#undo).
        public static int BuildHalfmoveClocks(int[] moves, int moveCount, int startClock, int[] clocks) {
            int clock = startClock;
            for (int i = 0; i < moveCount; i++) {
                clocks[i] = clock;
                clock = HalfmoveClockAfter(clock, moves[i]);
            }
            return clock;
        }

        // Whether the fifty-move rule lets the side to move claim a draw at this halfmove clock.
        public static bool IsFiftyMoveRule(int clock) {
            return clock >= FiftyMoveRulePlies;
        }

        // Whether the seventy-five-move rule draws the game at this halfmove clock.
        public static bool IsSeventyFiveMoveRule(int clock) {
            return clock >= SeventyFiveMoveRulePlies;
        }

        // ---- Repetition ----

        // How many times the current position must have occurred for a draw to be claimable (threefold) or
        // automatic (fivefold).
        public const int ThreefoldRepetitionCount = 3;
        public const int FivefoldRepetitionCount = 5;

        // How many times the current position has occurred in the game, counting this time (1 if it is new). Positions
        // are the same when the pieces, side to move, castling rights and en passant are (FIDE 9.2.3), where en
        // passant only counts if the capture can actually be made.
        // board and state are the position after moves[0..moveCount), and halfmoveClock its halfmove clock. A position
        // can only repeat one since the last capture or pawn move, i.e. within the last halfmoveClock plies (and not
        // before the first move: earlier positions aren't known), and only while the castling rights are unchanged.
        // So this walks back through those plies with UnmakeMove on a copy in scratchBoard and scratchState, comparing
        // every second one (same side to move); no hashing (docs/game-endings.md#draw-rules). scratchMoves (at least
        // ChessMoves.MaxMoves) is for checking en passant captures. board and state are left as they were.
        public static int CountRepetitions(byte[] board, int[] state, int[] moves, int moveCount, int halfmoveClock,
            byte[] scratchBoard, int[] scratchState, int[] scratchMoves, bool prohibitSelfCheck) {
            int plies = halfmoveClock < moveCount ? halfmoveClock : moveCount;
            // Every move changes the board, so a repeat takes at least two moves each.
            if (plies < 4) return 1;
            int castlingRights = state[ChessPosition.StateCastlingRights];
            int enPassantFile = EffectiveEnPassantFile(board, state, scratchMoves, prohibitSelfCheck);
            System.Array.Copy(board, scratchBoard, 64);
            System.Array.Copy(state, scratchState, ChessPosition.StateLength);
            int count = 1;
            for (int k = 1; k <= plies; k++) {
                int move = moves[moveCount - k];
                // Castling rights are only ever lost, so once they differ every earlier position differs too.
                if (((move >> ChessRules.MoveCastlingRightsShift) & ChessRules.MoveCastlingRightsMask) != castlingRights) break;
                ChessPosition.UnmakeMove(scratchBoard, scratchState, move);
                if ((k & 1) != 0 || !SameSquares(board, scratchBoard)) continue;
                if (EffectiveEnPassantFile(scratchBoard, scratchState, scratchMoves, prohibitSelfCheck) == enPassantFile) count++;
            }
            return count;
        }

        // Whether a draw by threefold repetition can be claimed with the position occurring count times.
        public static bool IsThreefoldRepetition(int count) {
            return count >= ThreefoldRepetitionCount;
        }

        // Whether fivefold repetition draws the game with the position occurring count times.
        public static bool IsFivefoldRepetition(int count) {
            return count >= FivefoldRepetitionCount;
        }

        static bool SameSquares(byte[] a, byte[] b) {
            for (int sq = 0; sq < 64; sq++) {
                if (a[sq] != b[sq]) return false;
            }
            return true;
        }

        // The en passant file as far as repetition is concerned: the state's en passant file if the side to move has
        // a legal en passant capture (any pseudo-legal one with prohibitSelfCheck off), otherwise -1. A double push
        // sets the en passant file even with no pawn beside it, and that position is the same as one without it.
        // The position is left as it was.
        public static int EffectiveEnPassantFile(byte[] board, int[] state, int[] scratchMoves, bool prohibitSelfCheck) {
            int file = state[ChessPosition.StateEnPassantFile];
            if (file < 0) return -1;
            int mover = state[ChessPosition.StateSideToMove];
            // The capturing pawns stand beside the double-pushed pawn: on rank 5 when White is to move, rank 4 when Black is.
            int rank = mover == ChessRules.White ? ChessRules.Rank5 : ChessRules.Rank4;
            int target = ((rank + (mover == ChessRules.White ? 1 : -1)) << 3) | file;
            byte pawn = (byte)(ChessRules.Pawn | (mover << 3));
            for (int side = -1; side <= 1; side += 2) {
                int captureFile = file + side;
                if (captureFile < 0 || captureFile > 7) continue;
                int from = (rank << 3) | captureFile;
                if (board[from] != pawn) continue;
                int count = ChessMoves.GenerateLegalPieceMoves(board, state, from, scratchMoves, 0, prohibitSelfCheck);
                for (int i = 0; i < count; i++) {
                    if (((scratchMoves[i] >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask) == target) return file;
                }
            }
            return -1;
        }

        // ---- Insufficient material ----

        // Whether neither side has the material to checkmate, so the position is dead and drawn
        // (docs/game-endings.md#draw-rules). Only the standard cases, not every dead position: kings alone, a single
        // knight besides the kings, or bishops besides the kings that all stand on squares of one colour (K v K,
        // K+N v K, K+B v K, K+B v K+B with same-coloured bishops, and more same-coloured bishops on either side,
        // which can't mate either). Any pawn, rook or queen, two knights, a knight and a bishop, or bishops on both
        // colours are enough, even if mate can't be forced.
        public static bool IsInsufficientMaterial(byte[] board) {
            int knights = 0;
            bool lightBishop = false;
            bool darkBishop = false;
            for (int sq = 0; sq < 64; sq++) {
                int type = board[sq] & 7;
                if (type == ChessRules.NoPieceType || type == ChessRules.King) continue;
                if (type == ChessRules.Knight) {
                    knights++;
                } else if (type == ChessRules.Bishop) {
                    // a1 (square 0) is dark: a square is light when its file plus rank is odd.
                    if ((((sq >> 3) + (sq & 7)) & 1) != 0) lightBishop = true;
                    else darkBishop = true;
                } else {
                    return false;
                }
            }
            if (knights == 0) return !(lightBishop && darkBishop);
            return knights == 1 && !lightBishop && !darkBishop;
        }

        // Whether color can't checkmate by any series of legal moves, which turns its opponent's time forfeit into a
        // draw (FIDE 6.9): color has only its king, or the whole board is insufficient material
        // (IsInsufficientMaterial). Otherwise the opponent's own pieces can hem their king in, so even a lone knight or
        // bishop can mate (K+N v K+P, K+B v K+N), and the flag loses.
        public static bool HasNoMatingMaterial(byte[] board, int color) {
            bool onlyKing = true;
            for (int sq = 0; sq < 64; sq++) {
                int piece = board[sq];
                if (piece == ChessRules.Empty || ChessRules.PieceTypeOf(piece) == ChessRules.King) continue;
                if (ChessRules.PieceColorOf(piece) == color) {
                    onlyKing = false;
                    break;
                }
            }
            return onlyKing || IsInsufficientMaterial(board);
        }
    }
}
