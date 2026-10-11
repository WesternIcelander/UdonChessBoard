using UdonSharp;

namespace SiggiChessBoard {
    // Draw rules (docs/game-endings.md#draw-rules): the halfmove clock and the fifty/seventy-five-move rules.
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
    }
}
