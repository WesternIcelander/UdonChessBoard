using System.Text;
using UdonSharp;

namespace SiggiChessBoard {
    // FEN parsing and writing (Java util/FEN), on the board and state arrays of ChessPosition. The halfmove clock and
    // move number aren't part of the position (docs/architecture.md), so they come and go as separate values.
    // Holds no state; everything comes in through parameters (see docs/architecture.md).
    // Abstract because it is never placed as a component, only used for its constants and static methods.
    public abstract class ChessFen : UdonSharpBehaviour {
        public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        // Parses a FEN into board (byte[64]) and state (int[ChessPosition.StateLength]). On failure returns false and
        // leaves both arrays untouched.
        //
        // Fields are separated by spaces; the halfmove clock and move number may be left out (0 and 1). Castling
        // takes KQkq, Shredder file letters (HAha) or a mix: K/Q are the outermost rook of that colour right/left of
        // the king on its back rank, a letter names the rook's file. This sets the king and rook starting files, which
        // both colours share, so the rights must agree on them. Sides without a right keep the standard files.
        //
        // Rejected as malformed, besides bad syntax: not exactly one king per colour, a pawn on the first or last rank,
        // the side not to move in check, a castling right without its king and rook on the back rank, a repeated
        // right, an en passant square not on the side to move's rank 6 (3 for Black) or without the pawn that just
        // moved two squares past it, a negative halfmove clock or a move number below 1.
        public static bool ParseFen(string fen, byte[] board, int[] state, out int halfmoveClock, out int fullmoveNumber) {
            halfmoveClock = 0;
            fullmoveNumber = 1;
            if (fen == null) return false;
            string[] parts = fen.Split(new char[] { ' ' });
            string[] fields = new string[6];
            int fieldCount = 0;
            for (int i = 0; i < parts.Length; i++) {
                if (parts[i].Length == 0) continue;
                if (fieldCount == 6) return false;
                fields[fieldCount++] = parts[i];
            }
            if (fieldCount != 4 && fieldCount != 6) return false;

            // Placement, into a scratch board so the caller's arrays only change on success.
            byte[] b = new byte[64];
            string placement = fields[0];
            int rank = 7;
            int file = 0;
            for (int i = 0; i < placement.Length; i++) {
                char c = placement[i];
                if (c == '/') {
                    if (file != 8 || rank == 0) return false;
                    rank--;
                    file = 0;
                } else if (c >= '1' && c <= '8') {
                    file += c - '0';
                    if (file > 8) return false;
                } else {
                    byte piece = _PieceFromFenChar(c);
                    if (piece == ChessRules.Empty || file > 7) return false;
                    b[(rank << 3) | file] = piece;
                    file++;
                }
            }
            if (rank != 0 || file != 8) return false;

            int whiteKing = ChessRules.NoSquare;
            int blackKing = ChessRules.NoSquare;
            for (int sq = 0; sq < 64; sq++) {
                int piece = b[sq];
                if (piece == ChessRules.WhiteKing) {
                    if (whiteKing != ChessRules.NoSquare) return false;
                    whiteKing = sq;
                } else if (piece == ChessRules.BlackKing) {
                    if (blackKing != ChessRules.NoSquare) return false;
                    blackKing = sq;
                } else if ((piece & 7) == ChessRules.Pawn && (sq < 8 || sq >= 56)) {
                    return false;
                }
            }
            if (whiteKing == ChessRules.NoSquare || blackKing == ChessRules.NoSquare) return false;

            int sideToMove;
            if (fields[1] == "w") sideToMove = ChessRules.White;
            else if (fields[1] == "b") sideToMove = ChessRules.Black;
            else return false;
            int opponent = sideToMove ^ 1;
            if (ChessMoves.IsSquareAttacked(b, opponent == ChessRules.White ? whiteKing : blackKing, sideToMove)) {
                return false;
            }

            // Castling.
            int rights = 0;
            int kingFile = ChessRules.FileE;
            int leftRookFile = ChessRules.FileA;
            int rightRookFile = ChessRules.FileH;
            bool kingFileSet = false;
            bool leftSet = false;
            bool rightSet = false;
            string castling = fields[2];
            if (castling != "-") {
                for (int i = 0; i < castling.Length; i++) {
                    char c = castling[i];
                    int color;
                    char letter;
                    if (c >= 'A' && c <= 'Z') {
                        color = ChessRules.White;
                        letter = c;
                    } else if (c >= 'a' && c <= 'z') {
                        color = ChessRules.Black;
                        letter = (char)(c - 'a' + 'A');
                    } else {
                        return false;
                    }
                    int backRank = color == ChessRules.White ? 0 : 56;
                    int king = color == ChessRules.White ? whiteKing : blackKing;
                    if ((king & 56) != backRank) return false;
                    int kf = king & 7;
                    byte rook = ChessRules.PieceOf(color, ChessRules.Rook);
                    int rookFile = -1;
                    if (letter == 'K') {
                        for (int f = 7; f > kf && rookFile < 0; f--) {
                            if (b[backRank | f] == rook) rookFile = f;
                        }
                    } else if (letter == 'Q') {
                        for (int f = 0; f < kf && rookFile < 0; f++) {
                            if (b[backRank | f] == rook) rookFile = f;
                        }
                    } else if (letter >= 'A' && letter <= 'H') {
                        int f = letter - 'A';
                        if (f != kf && b[backRank | f] == rook) rookFile = f;
                    }
                    if (rookFile < 0) return false;

                    bool kingSide = rookFile > kf;
                    int bit = (kingSide ? ChessRules.CastleWhiteKingSide : ChessRules.CastleWhiteQueenSide) << (color * 2);
                    if ((rights & bit) != 0) return false;
                    rights |= bit;
                    if (kingFileSet && kingFile != kf) return false;
                    kingFile = kf;
                    kingFileSet = true;
                    if (kingSide) {
                        if (rightSet && rightRookFile != rookFile) return false;
                        rightRookFile = rookFile;
                        rightSet = true;
                    } else {
                        if (leftSet && leftRookFile != rookFile) return false;
                        leftRookFile = rookFile;
                        leftSet = true;
                    }
                }
            }

            // En passant: on rank 6 when White is to move (rank 3 for Black), with the pawn that moved two squares
            // in front of it and the square it came from empty.
            int enPassantFile = -1;
            if (fields[3] != "-") {
                int square = ChessRules.ParseSquare(fields[3]);
                if (square == ChessRules.NoSquare) return false;
                if ((square >> 3) != (sideToMove == ChessRules.White ? ChessRules.Rank6 : ChessRules.Rank3)) return false;
                int pawnSquare = sideToMove == ChessRules.White ? square - 8 : square + 8;
                int originSquare = sideToMove == ChessRules.White ? square + 8 : square - 8;
                if (b[square] != ChessRules.Empty || b[originSquare] != ChessRules.Empty
                    || b[pawnSquare] != ChessRules.PieceOf(opponent, ChessRules.Pawn)) return false;
                enPassantFile = square & 7;
            }

            int halfmoves = 0;
            int fullmoves = 1;
            if (fieldCount == 6) {
                halfmoves = _ParseCount(fields[4]);
                fullmoves = _ParseCount(fields[5]);
                if (halfmoves < 0 || fullmoves < 1) return false;
            }

            for (int sq = 0; sq < 64; sq++) board[sq] = b[sq];
            state[ChessPosition.StateSideToMove] = sideToMove;
            state[ChessPosition.StateCastlingRights] = rights;
            state[ChessPosition.StateEnPassantFile] = enPassantFile;
            state[ChessPosition.StateKingFile] = kingFile;
            state[ChessPosition.StateLeftRookFile] = leftRookFile;
            state[ChessPosition.StateRightRookFile] = rightRookFile;
            state[ChessPosition.StateWhiteKingSquare] = whiteKing;
            state[ChessPosition.StateBlackKingSquare] = blackKing;
            halfmoveClock = halfmoves;
            fullmoveNumber = fullmoves;
            return true;
        }

        // Java FEN.serialize. Castling is written KQkq when the rook files are a and h, otherwise as Shredder file
        // letters (HAha), in the order K, Q, k, q as in the Java.
        public static string WriteFen(byte[] board, int[] state, int halfmoveClock, int fullmoveNumber) {
            StringBuilder sb = new StringBuilder();
            for (int rank = 7; rank >= 0; rank--) {
                if (rank != 7) sb.Append('/');
                int blanks = 0;
                for (int file = 0; file < 8; file++) {
                    int piece = board[(rank << 3) | file];
                    if (piece == ChessRules.Empty) {
                        blanks++;
                    } else {
                        if (blanks > 0) sb.Append((char)('0' + blanks));
                        blanks = 0;
                        sb.Append(_FenChar(piece));
                    }
                }
                if (blanks > 0) sb.Append((char)('0' + blanks));
            }

            sb.Append(state[ChessPosition.StateSideToMove] == ChessRules.White ? " w " : " b ");

            int rights = state[ChessPosition.StateCastlingRights];
            if (rights == 0) {
                sb.Append('-');
            } else {
                int left = state[ChessPosition.StateLeftRookFile];
                int right = state[ChessPosition.StateRightRookFile];
                bool standard = left == ChessRules.FileA && right == ChessRules.FileH;
                char kingSide = standard ? 'K' : (char)('A' + right);
                char queenSide = standard ? 'Q' : (char)('A' + left);
                if ((rights & ChessRules.CastleWhiteKingSide) != 0) sb.Append(kingSide);
                if ((rights & ChessRules.CastleWhiteQueenSide) != 0) sb.Append(queenSide);
                if ((rights & ChessRules.CastleBlackKingSide) != 0) sb.Append((char)(kingSide - 'A' + 'a'));
                if ((rights & ChessRules.CastleBlackQueenSide) != 0) sb.Append((char)(queenSide - 'A' + 'a'));
            }

            sb.Append(' ');
            int enPassantFile = state[ChessPosition.StateEnPassantFile];
            if (enPassantFile < 0) {
                sb.Append('-');
            } else {
                int enPassantRank = state[ChessPosition.StateSideToMove] == ChessRules.White ? ChessRules.Rank6 : ChessRules.Rank3;
                sb.Append(ChessRules.SquareName((enPassantRank << 3) | enPassantFile));
            }

            sb.Append(' ');
            sb.Append(halfmoveClock.ToString());
            sb.Append(' ');
            sb.Append(fullmoveNumber.ToString());
            return sb.ToString();
        }

        // FEN letter for a piece: uppercase for White, lowercase for Black. ' ' for Empty.
        public static char _FenChar(int piece) {
            if (piece == ChessRules.Empty) return ' ';
            char letter = ChessRules.PieceTypeLetter(piece & 7)[0];
            return ((piece >> 3) & 1) == ChessRules.White ? letter : (char)(letter - 'A' + 'a');
        }

        // The piece for a FEN letter, Empty for anything else.
        public static byte _PieceFromFenChar(char c) {
            if (c >= 'a' && c <= 'z') {
                int type = ChessRules.PieceTypeFromLetter((char)(c - 'a' + 'A'));
                if (type == ChessRules.NoPieceType) return ChessRules.Empty;
                return ChessRules.PieceOf(ChessRules.Black, type);
            }
            int whiteType = ChessRules.PieceTypeFromLetter(c);
            if (whiteType == ChessRules.NoPieceType) return ChessRules.Empty;
            return ChessRules.PieceOf(ChessRules.White, whiteType);
        }

        // A non-negative decimal number of at most 9 digits; -1 for anything else.
        public static int _ParseCount(string text) {
            if (text.Length == 0 || text.Length > 9) return -1;
            int value = 0;
            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (c < '0' || c > '9') return -1;
                value = value * 10 + (c - '0');
            }
            return value;
        }
    }
}
