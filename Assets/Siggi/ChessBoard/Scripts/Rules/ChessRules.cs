using UdonSharp;

namespace SiggiChessBoard {
    // Constants and static helpers for the rules: piece types and colours, squares, and the piece encoding used in
    // the byte[64] board. Holds no state; everything comes in through parameters (see docs/architecture.md).
    // Abstract because it is never placed as a component, only used for its constants and static methods.
    public abstract class ChessRules : UdonSharpBehaviour {
        // Piece types (Java PieceType). 0 = none, which packed moves also use for "no promotion" / "no capture".
        public const int NoPieceType = 0;
        public const int Pawn = 1;
        public const int Knight = 2;
        public const int Bishop = 3;
        public const int Rook = 4;
        public const int Queen = 5;
        public const int King = 6;

        // Piece colours (Java PieceColor). -1 in place of null.
        public const int NoColor = -1;
        public const int White = 0;
        public const int Black = 1;

        // Pieces on the board (Java Piece): type | colour << 3, so white pieces are 1-6, black 9-14, empty 0.
        public const byte Empty = 0;
        public const byte WhitePawn = 1;
        public const byte WhiteKnight = 2;
        public const byte WhiteBishop = 3;
        public const byte WhiteRook = 4;
        public const byte WhiteQueen = 5;
        public const byte WhiteKing = 6;
        public const byte BlackPawn = 9;
        public const byte BlackKnight = 10;
        public const byte BlackBishop = 11;
        public const byte BlackRook = 12;
        public const byte BlackQueen = 13;
        public const byte BlackKing = 14;

        // Files and ranks (Java File, Rank), 0-7.
        public const int FileA = 0, FileB = 1, FileC = 2, FileD = 3, FileE = 4, FileF = 5, FileG = 6, FileH = 7;
        public const int Rank1 = 0, Rank2 = 1, Rank3 = 2, Rank4 = 3, Rank5 = 4, Rank6 = 5, Rank7 = 6, Rank8 = 7;

        // Squares (Java Square): rank * 8 + file, a1 = 0 ... h8 = 63, the same as Java's Square.index. -1 in place of null.
        public const int NoSquare = -1;
        public const int A1 = 0, B1 = 1, C1 = 2, D1 = 3, E1 = 4, F1 = 5, G1 = 6, H1 = 7;
        public const int A2 = 8, B2 = 9, C2 = 10, D2 = 11, E2 = 12, F2 = 13, G2 = 14, H2 = 15;
        public const int A3 = 16, B3 = 17, C3 = 18, D3 = 19, E3 = 20, F3 = 21, G3 = 22, H3 = 23;
        public const int A4 = 24, B4 = 25, C4 = 26, D4 = 27, E4 = 28, F4 = 29, G4 = 30, H4 = 31;
        public const int A5 = 32, B5 = 33, C5 = 34, D5 = 35, E5 = 36, F5 = 37, G5 = 38, H5 = 39;
        public const int A6 = 40, B6 = 41, C6 = 42, D6 = 43, E6 = 44, F6 = 45, G6 = 46, H6 = 47;
        public const int A7 = 48, B7 = 49, C7 = 50, D7 = 51, E7 = 52, F7 = 53, G7 = 54, H7 = 55;
        public const int A8 = 56, B8 = 57, C8 = 58, D8 = 59, E8 = 60, F8 = 61, G8 = 62, H8 = 63;

        // Castling rights, as a 4-bit set in FEN order (KQkq). King side is the rook right of the king (Java
        // rightRookFile), queen side the rook left of it, so the names also hold for Chess960.
        public const int CastleWhiteKingSide = 1;
        public const int CastleWhiteQueenSide = 2;
        public const int CastleBlackKingSide = 4;
        public const int CastleBlackQueenSide = 8;
        public const int CastleAll = 15;

        // Packed move layout (docs/move-encoding.md). Read a field with (move >> shift) & mask.
        public const int MoveFromShift = 0;
        public const int MoveToShift = 6;
        public const int MovePieceTypeShift = 12;
        public const int MovePromotionShift = 15;
        public const int MoveCapturedShift = 18;
        public const int MoveCastleShift = 21;
        public const int MoveCastlingRightsShift = 22;
        public const int MoveEnPassantValidShift = 26;
        public const int MoveEnPassantFileShift = 27;
        public const int MoveSquareMask = 63;
        public const int MoveTypeMask = 7;
        public const int MoveCastlingRightsMask = 15;
        public const int MoveEnPassantFileMask = 7;

        // ---- Squares ----

        // Java Square.of. No range check: file and rank must be 0-7.
        public static int SquareOf(int file, int rank) {
            return (rank << 3) | file;
        }

        // Java Square.fromCoordinate, with NoSquare instead of an exception when off the board.
        public static int SquareFromCoordinate(int file, int rank) {
            if (file < 0 || file > 7 || rank < 0 || rank > 7) return NoSquare;
            return (rank << 3) | file;
        }

        // Java Square.getRelative: NoSquare when the result is off the board.
        public static int SquareRelative(int square, int fileOffset, int rankOffset) {
            return SquareFromCoordinate((square & 7) + fileOffset, ((square >> 3) & 7) + rankOffset);
        }

        public static bool IsSquare(int square) {
            return square >= 0 && square <= 63;
        }

        // Java Square.file / Square.x.
        public static int FileOf(int square) {
            return square & 7;
        }

        // Java Square.rank / Square.y.
        public static int RankOf(int square) {
            return (square >> 3) & 7;
        }

        // "e4". "-" for NoSquare or anything else off the board.
        public static string SquareName(int square) {
            if (square < 0 || square > 63) return "-";
            return "" + (char)('a' + (square & 7)) + (char)('1' + ((square >> 3) & 7));
        }

        // "e4" -> square. NoSquare if the text isn't exactly a lowercase file letter and a rank digit.
        public static int ParseSquare(string name) {
            if (name == null || name.Length != 2) return NoSquare;
            int file = name[0] - 'a';
            int rank = name[1] - '1';
            return SquareFromCoordinate(file, rank);
        }

        // ---- Pieces ----

        // Java Piece.of. No range check: color must be White or Black, type Pawn-King.
        public static byte PieceOf(int color, int type) {
            return (byte)(type | (color << 3));
        }

        // Java Piece.type. NoPieceType for Empty.
        public static int PieceTypeOf(int piece) {
            return piece & 7;
        }

        // Java Piece.color. NoColor for Empty.
        public static int PieceColorOf(int piece) {
            if (piece == Empty) return NoColor;
            return (piece >> 3) & 1;
        }

        // Java PieceColor.otherColor.
        public static int OtherColor(int color) {
            return color ^ 1;
        }

        // Java PieceType.singleLetterNotation: "P", "N", "B", "R", "Q", "K"; "" for anything else.
        public static string PieceTypeLetter(int type) {
            switch (type) {
                case Pawn: return "P";
                case Knight: return "N";
                case Bishop: return "B";
                case Rook: return "R";
                case Queen: return "Q";
                case King: return "K";
                default: return "";
            }
        }

        // Java PieceType.fromSingleLetterNotation: uppercase letters only, NoPieceType for anything else.
        public static int PieceTypeFromLetter(char letter) {
            switch (letter) {
                case 'P': return Pawn;
                case 'N': return Knight;
                case 'B': return Bishop;
                case 'R': return Rook;
                case 'Q': return Queen;
                case 'K': return King;
                default: return NoPieceType;
            }
        }

        // ---- Moves ----

        // Packs a move. When castling, from is the king's square and to the rook's starting square. castlingRights
        // and enPassantFile are the board's state before the move; enPassantFile is -1 when there is none.
        // No range checks: every field must fit its width.
        public static int PackMove(int from, int to, int pieceType, int promotion, int captured, bool castle,
            int castlingRights, int enPassantFile) {
            int move = from
                | (to << MoveToShift)
                | (pieceType << MovePieceTypeShift)
                | (promotion << MovePromotionShift)
                | (captured << MoveCapturedShift)
                | (castlingRights << MoveCastlingRightsShift);
            if (castle) move |= 1 << MoveCastleShift;
            if (enPassantFile >= 0) move |= (1 << MoveEnPassantValidShift) | (enPassantFile << MoveEnPassantFileShift);
            return move;
        }

        public static int MoveFrom(int move) {
            return (move >> MoveFromShift) & MoveSquareMask;
        }

        public static int MoveTo(int move) {
            return (move >> MoveToShift) & MoveSquareMask;
        }

        public static int MovePieceType(int move) {
            return (move >> MovePieceTypeShift) & MoveTypeMask;
        }

        // NoPieceType if the move isn't a promotion.
        public static int MovePromotion(int move) {
            return (move >> MovePromotionShift) & MoveTypeMask;
        }

        // NoPieceType if the move isn't a capture. Always the other colour from the mover.
        public static int MoveCaptured(int move) {
            return (move >> MoveCapturedShift) & MoveTypeMask;
        }

        public static bool MoveIsCastle(int move) {
            return ((move >> MoveCastleShift) & 1) != 0;
        }

        // Castling rights before the move.
        public static int MovePrevCastlingRights(int move) {
            return (move >> MoveCastlingRightsShift) & MoveCastlingRightsMask;
        }

        // Whether there was an en passant square before the move.
        public static bool MovePrevEnPassantValid(int move) {
            return ((move >> MoveEnPassantValidShift) & 1) != 0;
        }

        // The en passant file before the move, -1 if there was none.
        public static int MovePrevEnPassantFile(int move) {
            if (((move >> MoveEnPassantValidShift) & 1) == 0) return -1;
            return (move >> MoveEnPassantFileShift) & MoveEnPassantFileMask;
        }

        // The en passant square before the move, for the given mover: rank 6 if White is moving, rank 3 if Black is.
        // NoSquare if there was none.
        public static int MovePrevEnPassantSquare(int move, int moverColor) {
            if (((move >> MoveEnPassantValidShift) & 1) == 0) return NoSquare;
            int file = (move >> MoveEnPassantFileShift) & MoveEnPassantFileMask;
            return (moverColor == White ? Rank6 << 3 : Rank3 << 3) | file;
        }

        // A pawn landing on the en passant square must be capturing en passant, since that square is always empty.
        // The mover's colour follows from the direction the pawn moves. The captured pawn is on the from rank, to file.
        public static bool IsEnPassantCapture(int move) {
            if (((move >> MovePieceTypeShift) & MoveTypeMask) != Pawn) return false;
            if (((move >> MoveEnPassantValidShift) & 1) == 0) return false;
            int from = (move >> MoveFromShift) & MoveSquareMask;
            int to = (move >> MoveToShift) & MoveSquareMask;
            int moverColor = (to >> 3) > (from >> 3) ? White : Black;
            return to == MovePrevEnPassantSquare(move, moverColor);
        }

        // A pawn moving two ranks. The new en passant square is the one between from and to.
        public static bool IsDoublePush(int move) {
            if (((move >> MovePieceTypeShift) & MoveTypeMask) != Pawn) return false;
            int fromRank = (move >> (MoveFromShift + 3)) & 7;
            int toRank = (move >> (MoveToShift + 3)) & 7;
            return fromRank - toRank == 2 || toRank - fromRank == 2;
        }
    }
}
