using UdonSharp;

namespace SiggiChessBoard {
    // The captured pieces of a position (docs/physical-pieces.md, Captured-piece places): which piece types stand in
    // each colour's captured places, in place order, worked out from the game's board and history, which every client
    // has. The view stands its pieces in the places from it (step 12.4), and the board checks a piece put back in
    // replay against it (step 13.4).
    // Holds no state; everything comes in through parameters (see docs/architecture.md).
    // Abstract because it is never placed as a component, only used for its constants and static methods.
    public abstract class ChessCaptures : UdonSharpBehaviour {
        // The room per colour in a captured array (colour * MaxCaptured + place).
        public const int MaxCaptured = 64;

        // Fills captured (colour * MaxCaptured + place) with the piece types standing in each colour's captured-piece
        // places, in place order, and counts with how many, for the position shown: the game's after ply of its
        // moves, or custom (null for none), a replay position the free moves made from it. Everything comes from the
        // game's board and history, which every client has, so every client, a late joiner too, shows the same
        // places; no history is made up for a position without one. In order:
        // - The pieces the starting position lacks (from a FEN), short of the standard set, in the fixed order
        //   (PlaceOrderType); each piece beyond a type's standard count (a promotion the FEN doesn't record) counts
        //   against the missing pawns. The starting counts are the game's board's with its moves undone.
        // - The captures of the moves before ply. A promoted pawn is captured as a pawn; a piece whose promotion the
        //   history can't show (an extra queen in the starting FEN) keeps its type.
        // - For a custom position, the pieces of the ply's position missing from it (kings too), in the fixed order:
        //   the free moves took them, whatever they were. A type it has more of than the ply's position had pieces
        //   put back from the places (step 13.4), each taken out of the last place of that type.
        // gameBoard and sideToMove are the game's after its moveCount moves (whatever replay shows).
        public static void CaptureOrder(byte[] gameBoard, int sideToMove, int[] moves, int moveCount, int ply,
            byte[] custom, int[] captured, int[] counts) {
            counts[ChessRules.White] = 0;
            counts[ChessRules.Black] = 0;
            if (gameBoard == null || moves == null) return;
            if (moveCount > moves.Length) moveCount = moves.Length;
            if (ply < 0) ply = 0;
            if (ply > moveCount) ply = moveCount;
            // Pieces per colour * 8 + type: the game's, then the starting position's (its moves undone).
            int[] pieceCounts = new int[16];
            for (int square = 0; square < 64; square++) {
                int piece = gameBoard[square];
                if (piece != ChessRules.Empty) pieceCounts[ChessRules.PieceColorOf(piece) * 8 + (piece & 7)]++;
            }
            for (int i = moveCount - 1; i >= 0; i--) {
                int move = moves[i];
                int mover = MoverAt(sideToMove, moveCount, i);
                int capturedType = ChessRules.MoveIsCastle(move) ? ChessRules.NoPieceType : ChessRules.MoveCaptured(move);
                if (capturedType != ChessRules.NoPieceType) pieceCounts[ChessRules.OtherColor(mover) * 8 + capturedType]++;
                int promotion = ChessRules.MovePromotion(move);
                if (promotion != ChessRules.NoPieceType) {
                    pieceCounts[mover * 8 + promotion]--;
                    pieceCounts[mover * 8 + ChessRules.Pawn]++;
                }
            }
            for (int color = ChessRules.White; color <= ChessRules.Black; color++) {
                int extra = 0;
                for (int type = ChessRules.Knight; type <= ChessRules.Queen; type++) {
                    int over = pieceCounts[color * 8 + type] - StandardCount(type);
                    if (over > 0) extra += over;
                }
                for (int i = 0; i < 5; i++) {
                    int type = PlaceOrderType(i);
                    int missing = StandardCount(type) - pieceCounts[color * 8 + type];
                    if (type == ChessRules.Pawn) missing -= extra;
                    for (int n = 0; n < missing; n++) AddCaptured(captured, counts, color, type);
                }
            }
            // The game's captures up to ply, following which pieces are promoted pawns, and the counts brought to ply.
            bool[] promoted = new bool[64];
            for (int i = 0; i < ply; i++) {
                int move = moves[i];
                int mover = MoverAt(sideToMove, moveCount, i);
                int from = ChessRules.MoveFrom(move);
                int to = ChessRules.MoveTo(move);
                if (ChessRules.MoveIsCastle(move)) {
                    promoted[from] = false;
                    promoted[to] = false;
                    continue;
                }
                int capturedType = ChessRules.MoveCaptured(move);
                if (capturedType != ChessRules.NoPieceType) {
                    int square = ChessRules.IsEnPassantCapture(move)
                        ? ChessRules.SquareOf(ChessRules.FileOf(to), ChessRules.RankOf(from)) : to;
                    int other = ChessRules.OtherColor(mover);
                    AddCaptured(captured, counts, other, promoted[square] ? ChessRules.Pawn : capturedType);
                    pieceCounts[other * 8 + capturedType]--;
                    promoted[square] = false;
                }
                int promotion = ChessRules.MovePromotion(move);
                if (promotion != ChessRules.NoPieceType) {
                    pieceCounts[mover * 8 + ChessRules.Pawn]--;
                    pieceCounts[mover * 8 + promotion]++;
                }
                promoted[to] = promoted[from] || promotion != ChessRules.NoPieceType;
                promoted[from] = false;
            }
            if (custom == null) return;
            // The free moves only move pieces and take them off; a type with more than the ply's count had pieces
            // put back from the places.
            int[] customCounts = new int[16];
            for (int square = 0; square < 64; square++) {
                int piece = custom[square];
                if (piece != ChessRules.Empty) customCounts[ChessRules.PieceColorOf(piece) * 8 + (piece & 7)]++;
            }
            for (int color = ChessRules.White; color <= ChessRules.Black; color++) {
                for (int i = 0; i < 6; i++) {
                    int type = PlaceOrderType(i);
                    int missing = pieceCounts[color * 8 + type] - customCounts[color * 8 + type];
                    for (int n = 0; n < missing; n++) AddCaptured(captured, counts, color, type);
                    for (int n = 0; n < -missing; n++) RemoveCaptured(captured, counts, color, type);
                }
            }
        }

        // The colour that played move i of a game of moveCount moves with sideToMove to move after them.
        static int MoverAt(int sideToMove, int moveCount, int i) {
            return ((moveCount - i) & 1) == 1 ? ChessRules.OtherColor(sideToMove) : sideToMove;
        }

        // The fixed order of pieces put in places without a capture to order them: queen, rook, bishop, knight, pawn,
        // then king (only a free move takes one).
        public static int PlaceOrderType(int i) {
            if (i < 4) return ChessRules.Queen - i;
            return i == 4 ? ChessRules.Pawn : ChessRules.King;
        }

        // How many of a piece type a side starts a standard game with.
        public static int StandardCount(int type) {
            if (type == ChessRules.Pawn) return 8;
            if (type == ChessRules.Queen || type == ChessRules.King) return 1;
            return 2;
        }

        // Whether color has a piece of type in its captured places (CaptureOrder's captured and counts).
        public static bool HasCaptured(int[] captured, int[] counts, int color, int type) {
            if (color != ChessRules.White && color != ChessRules.Black) return false;
            for (int place = 0; place < counts[color]; place++) {
                if (captured[color * MaxCaptured + place] == type) return true;
            }
            return false;
        }

        static void AddCaptured(int[] captured, int[] counts, int color, int type) {
            if (counts[color] >= MaxCaptured) return;
            captured[color * MaxCaptured + counts[color]] = type;
            counts[color]++;
        }

        // Takes the last of color's places holding type out, the places after it moving up one.
        static void RemoveCaptured(int[] captured, int[] counts, int color, int type) {
            int first = color * MaxCaptured;
            for (int place = counts[color] - 1; place >= 0; place--) {
                if (captured[first + place] != type) continue;
                for (int i = place; i < counts[color] - 1; i++) captured[first + i] = captured[first + i + 1];
                counts[color]--;
                return;
            }
        }
    }
}
