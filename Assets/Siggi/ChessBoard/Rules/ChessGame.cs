using UdonSharp;

namespace SiggiChessBoard {
    // Constants describing a game as a whole rather than a position: its mode (docs/modes.md) and how it ended
    // (docs/game-endings.md). The board's synced mode and result fields hold these; PGN export turns them into tags.
    // Abstract because it is never placed as a component, only used for its constants.
    public abstract class ChessGame : UdonSharpBehaviour {
        // Modes. Fog of War comes later.
        public const int ModeStandard = 0;
        public const int ModeChess960 = 1;

        // How a game ended, one per row of the Termination table in docs/game-endings.md. EndingNone is a game still
        // in progress. The winner is a separate value: ChessRules.White or Black, or ChessRules.NoColor for a draw
        // (or a game without a result).
        public const int EndingNone = 0;
        public const int EndingCheckmate = 1;
        public const int EndingResignation = 2;
        public const int EndingTimeForfeit = 3;
        public const int EndingStalemate = 4;
        public const int EndingDrawAgreement = 5;
        public const int EndingThreefoldRepetition = 6;
        public const int EndingFivefoldRepetition = 7;
        public const int EndingFiftyMoveRule = 8;
        public const int EndingSeventyFiveMoveRule = 9;
        public const int EndingInsufficientMaterial = 10;
        // A reset of an unfinished game, or continuing an unfinished game from an earlier position (result "*").
        public const int EndingUnterminated = 11;
        // Fog of War only (later).
        public const int EndingKingCaptured = 12;
        public const int EndingCount = 13;
    }
}
