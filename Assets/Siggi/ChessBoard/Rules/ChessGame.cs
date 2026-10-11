using System;
using UdonSharp;

namespace SiggiChessBoard {
    // Constants describing a game as a whole rather than a position: its mode (docs/modes.md) and how it ended
    // (docs/game-endings.md), the Chess960 starting positions, and Game IDs (docs/game-ids.md). The board's synced mode and result fields hold
    // these; PGN export turns them into tags. Abstract because it is never placed as a component, only used for its
    // constants and static methods.
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

        // ---- Chess960 starting positions (docs/modes.md) ----

        // The 960 positions are numbered 0 to 959 as in Scharnagl's scheme, where 518 is the standard position.
        public const int Chess960Count = 960;
        public const int Chess960StandardIndex = 518;

        // Where the two knights go among the five squares left after the bishops and the queen, for n = 0 to 9:
        // the first knight's slot, then the second's.
        const string Chess960Knights = "01020304121314232434";

        // Sets up Chess960 position number index (0 to Chess960Count - 1) with White to move and every castling
        // right: the light-squared bishop on file 2 * (index % 4) + 1, the dark-squared one on file 2 * (index / 4 % 4),
        // the queen on empty square index / 16 % 6, the knights from index / 96 by Chess960Knights, and then rook,
        // king and rook on the three squares left. Black mirrors White. Returns false, changing nothing, for an index
        // out of range.
        public static bool Chess960Position(int index, byte[] board, int[] state) {
            if (index < 0 || index >= Chess960Count) return false;
            int[] types = new int[8];
            int n = index;
            types[(n % 4) * 2 + 1] = ChessRules.Bishop;
            n /= 4;
            types[(n % 4) * 2] = ChessRules.Bishop;
            n /= 4;
            PlaceOnEmpty(types, n % 6, ChessRules.Queen);
            n /= 6;
            // Placing the first knight shifts the empty squares after it by one.
            PlaceOnEmpty(types, Chess960Knights[n * 2] - '0', ChessRules.Knight);
            PlaceOnEmpty(types, Chess960Knights[n * 2 + 1] - '0' - 1, ChessRules.Knight);
            PlaceOnEmpty(types, 0, ChessRules.Rook);
            PlaceOnEmpty(types, 0, ChessRules.King);
            PlaceOnEmpty(types, 0, ChessRules.Rook);

            int kingFile = 0;
            int leftRookFile = -1;
            int rightRookFile = 0;
            for (int file = 0; file < 8; file++) {
                int type = types[file];
                if (type == ChessRules.King) kingFile = file;
                if (type == ChessRules.Rook) {
                    if (leftRookFile < 0) leftRookFile = file;
                    else rightRookFile = file;
                }
            }

            for (int sq = 0; sq < 64; sq++) board[sq] = ChessRules.Empty;
            for (int file = 0; file < 8; file++) {
                board[file] = ChessRules.PieceOf(ChessRules.White, types[file]);
                board[ChessRules.A2 + file] = ChessRules.WhitePawn;
                board[ChessRules.A7 + file] = ChessRules.BlackPawn;
                board[ChessRules.A8 + file] = ChessRules.PieceOf(ChessRules.Black, types[file]);
            }
            state[ChessPosition.StateSideToMove] = ChessRules.White;
            state[ChessPosition.StateCastlingRights] = ChessRules.CastleAll;
            state[ChessPosition.StateEnPassantFile] = -1;
            state[ChessPosition.StateKingFile] = kingFile;
            state[ChessPosition.StateLeftRookFile] = leftRookFile;
            state[ChessPosition.StateRightRookFile] = rightRookFile;
            state[ChessPosition.StateWhiteKingSquare] = kingFile;
            state[ChessPosition.StateBlackKingSquare] = ChessRules.A8 + kingFile;
            return true;
        }

        // Chess960 position number index as a FEN (as ChessFen.WriteFen writes it, so Shredder castling letters
        // unless the rooks are on a and h), or "" for an index out of range.
        public static string Chess960Fen(int index) {
            byte[] board = new byte[64];
            int[] state = new int[ChessPosition.StateLength];
            if (!Chess960Position(index, board, state)) return "";
            return ChessFen.WriteFen(board, state, 0, 1);
        }

        // ---- Game IDs (docs/game-ids.md) ----

        // A Game ID is yyyyMMddHHmmss-xxxxxx: the UTC time the game started, then a random suffix below
        // GameIdSuffixCount, zero-padded to six digits.
        public const int GameIdSuffixCount = 1000000;
        public const int GameIdLength = 21;

        // The Game ID of a game started at utc (a UTC time; its Kind isn't looked at) with this suffix, or "" for a
        // suffix out of range.
        public static string GameId(DateTime utc, int suffix) {
            if (suffix < 0 || suffix >= GameIdSuffixCount) return "";
            return Digits(utc.Year, 4) + Digits(utc.Month, 2) + Digits(utc.Day, 2) + Digits(utc.Hour, 2)
                + Digits(utc.Minute, 2) + Digits(utc.Second, 2) + "-" + Digits(suffix, 6);
        }

        // The PGN Date (yyyy.MM.dd) of the game whose ID is gameId: the UTC start date its first eight digits give, or
        // "" (an unknown date) when gameId isn't in the yyyyMMddHHmmss-xxxxxx form, e.g. an imported game's own ID.
        public static string DateFromGameId(string gameId) {
            if (gameId == null || gameId.Length != GameIdLength) return "";
            for (int i = 0; i < GameIdLength; i++) {
                char c = gameId[i];
                if (i == 14 ? c != '-' : (c < '0' || c > '9')) return "";
            }
            return gameId.Substring(0, 4) + "." + gameId.Substring(4, 2) + "." + gameId.Substring(6, 2);
        }

        // The ContinuedFrom value of a game continued from ply (half-moves from the original game's starting
        // position) of the game with ID gameId: "<gameId>/<ply>", or "" when that game has no ID (a PGN import without
        // a GameID tag), so the continued game gets no ContinuedFrom tag.
        public static string ContinuedFromText(string gameId, int ply) {
            if (gameId == null || gameId.Length == 0) return "";
            return gameId + "/" + ply.ToString();
        }

        // value (not negative) in decimal, zero-padded to at least count digits.
        static string Digits(int value, int count) {
            string text = value.ToString();
            while (text.Length < count) text = "0" + text;
            return text;
        }

        // Puts type on the slot-th empty file (counting from 0 on the a-file) of a back rank being filled.
        static void PlaceOnEmpty(int[] types, int slot, int type) {
            for (int file = 0; file < 8; file++) {
                if (types[file] != ChessRules.NoPieceType) continue;
                if (slot == 0) {
                    types[file] = type;
                    return;
                }
                slot--;
            }
        }
    }
}
