using UdonSharp;

namespace SiggiChessBoard {
    // A position is a byte[64] board (pieces as in ChessRules) plus an int[StateLength] state array laid out by the
    // State* indices below (Java Board's other fields). Making and unmaking moves works on both arrays in place.
    // Holds no state; everything comes in through parameters (see docs/architecture.md).
    // Abstract because it is never placed as a component, only used for its constants and static methods.
    public abstract class ChessPosition : UdonSharpBehaviour {
        // Java Board.nextMove: White or Black.
        public const int StateSideToMove = 0;
        // Java Board.white/blackCastleKing/QueenAllowed, as ChessRules.Castle* bits.
        public const int StateCastlingRights = 1;
        // Java Board.enPassant, as a file; -1 for none. The rank follows from the side to move.
        public const int StateEnPassantFile = 2;
        // The king's and rooks' starting files (Java Board.leftRookFile/rightRookFile, plus kingFile), set when the
        // position is loaded and never changed by moves. Castling uses them (docs/move-encoding.md#castling).
        public const int StateKingFile = 3;
        public const int StateLeftRookFile = 4;
        public const int StateRightRookFile = 5;
        // Where each king is, kept up to date by MakeMove/UnmakeMove so the attack check doesn't have to search for
        // it. NoSquare if that king has been captured (only possible with prohibitSelfCheck off).
        public const int StateWhiteKingSquare = 6;
        public const int StateBlackKingSquare = 7;
        public const int StateLength = 8;

        // Plays a move on the board and state. The move must have been packed for this position (its castling rights
        // and en passant fields are the state before it); nothing is checked.
        public static void MakeMove(byte[] board, int[] state, int move) {
            int mover = state[StateSideToMove];
            int opponent = mover ^ 1;
            int from = (move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask;
            int to = (move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask;
            int type = (move >> ChessRules.MovePieceTypeShift) & ChessRules.MoveTypeMask;
            int promotion = (move >> ChessRules.MovePromotionShift) & ChessRules.MoveTypeMask;
            int captured = (move >> ChessRules.MoveCapturedShift) & ChessRules.MoveTypeMask;

            // The captured pawn of an en passant capture is on the from rank, to file.
            if (ChessRules.IsEnPassantCapture(move)) board[(from & 56) | (to & 7)] = ChessRules.Empty;
            board[to] = (byte)((promotion != ChessRules.NoPieceType ? promotion : type) | (mover << 3));
            board[from] = ChessRules.Empty;

            if (type == ChessRules.King) state[StateWhiteKingSquare + mover] = to;
            if (captured == ChessRules.King) state[StateWhiteKingSquare + opponent] = ChessRules.NoSquare;
            state[StateEnPassantFile] = ChessRules.IsDoublePush(move) ? from & 7 : -1;
            state[StateSideToMove] = opponent;
        }

        // Takes back the last move played with MakeMove, restoring both arrays exactly.
        public static void UnmakeMove(byte[] board, int[] state, int move) {
            int opponent = state[StateSideToMove];
            int mover = opponent ^ 1;
            int from = (move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask;
            int to = (move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask;
            int type = (move >> ChessRules.MovePieceTypeShift) & ChessRules.MoveTypeMask;
            int captured = (move >> ChessRules.MoveCapturedShift) & ChessRules.MoveTypeMask;

            // The piece-type field is the piece before any promotion.
            board[from] = (byte)(type | (mover << 3));
            if (ChessRules.IsEnPassantCapture(move)) {
                board[to] = ChessRules.Empty;
                board[(from & 56) | (to & 7)] = (byte)(ChessRules.Pawn | (opponent << 3));
            } else if (captured != ChessRules.NoPieceType) {
                board[to] = (byte)(captured | (opponent << 3));
            } else {
                board[to] = ChessRules.Empty;
            }

            if (type == ChessRules.King) state[StateWhiteKingSquare + mover] = from;
            if (captured == ChessRules.King) state[StateWhiteKingSquare + opponent] = to;
            // The state before the move is in the move itself (docs/move-encoding.md#undo).
            state[StateCastlingRights] = (move >> ChessRules.MoveCastlingRightsShift) & ChessRules.MoveCastlingRightsMask;
            state[StateEnPassantFile] = ChessRules.MovePrevEnPassantFile(move);
            state[StateSideToMove] = mover;
        }
    }
}
