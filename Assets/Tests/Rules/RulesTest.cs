using SiggiChessBoard;
using UdonSharp;
using UnityEngine;

// Step 2 test behaviour (PLAN.md section 2): checks the static rules with no world objects.
// Logs "[RulesTest] PASS: <check>" or "[RulesTest] FAIL: <check>: <details>" per check, then a summary line.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RulesTest : UdonSharpBehaviour {
    int passed;
    int failed;

    void Start() {
        TestSquares();
        TestPieces();
        TestMovePacking();
        TestMoveDerived();
        TestMakeUnmake();
        TestCastling();
        TestMoveGeneration();
        TestAttacks();
        TestLegalMoves();
        TestPositions();
        Debug.Log("[RulesTest] done: " + passed + " passed, " + failed + " failed"
            + (failed == 0 ? " ALL PASS" : " SOME FAIL"));
    }

    void Check(bool ok, string name, string details) {
        if (ok) {
            passed++;
            Debug.Log("[RulesTest] PASS: " + name);
        } else {
            failed++;
            Debug.Log("[RulesTest] FAIL: " + name + ": " + details);
        }
    }

    // ---- 2.1 ----

    void TestSquares() {
        // square -> file/rank -> square for all 64 squares, plus names.
        string errors = "";
        string files = "abcdefgh";
        for (int sq = 0; sq < 64; sq++) {
            int file = ChessRules.FileOf(sq);
            int rank = ChessRules.RankOf(sq);
            if (file != sq % 8 || rank != sq / 8) errors += " " + sq + ":file/rank=" + file + "/" + rank;
            if (ChessRules.SquareOf(file, rank) != sq) errors += " " + sq + ":SquareOf";
            if (ChessRules.SquareFromCoordinate(file, rank) != sq) errors += " " + sq + ":SquareFromCoordinate";
            if (!ChessRules.IsSquare(sq)) errors += " " + sq + ":IsSquare";
            string name = ChessRules.SquareName(sq);
            string expectedName = "" + files[sq % 8] + (sq / 8 + 1);
            if (name != expectedName) errors += " " + sq + ":name=" + name;
            if (ChessRules.ParseSquare(expectedName) != sq) errors += " " + sq + ":ParseSquare";
        }
        Check(errors == "", "square -> file/rank -> square, all 64", errors);

        Check(ChessRules.A1 == 0 && ChessRules.H1 == 7 && ChessRules.E4 == 28 && ChessRules.A8 == 56
            && ChessRules.H8 == 63 && ChessRules.SquareOf(ChessRules.FileE, ChessRules.Rank4) == ChessRules.E4,
            "square constants", "A1 " + ChessRules.A1 + " E4 " + ChessRules.E4 + " H8 " + ChessRules.H8);

        Check(ChessRules.SquareFromCoordinate(-1, 0) == ChessRules.NoSquare
            && ChessRules.SquareFromCoordinate(8, 0) == ChessRules.NoSquare
            && ChessRules.SquareFromCoordinate(0, -1) == ChessRules.NoSquare
            && ChessRules.SquareFromCoordinate(0, 8) == ChessRules.NoSquare
            && !ChessRules.IsSquare(-1) && !ChessRules.IsSquare(64),
            "off-board coordinates give NoSquare", "");

        Check(ChessRules.SquareRelative(ChessRules.E4, 1, 2) == ChessRules.F6
            && ChessRules.SquareRelative(ChessRules.E4, -4, -3) == ChessRules.A1
            && ChessRules.SquareRelative(ChessRules.H4, 1, 0) == ChessRules.NoSquare
            && ChessRules.SquareRelative(ChessRules.A4, -1, 0) == ChessRules.NoSquare
            && ChessRules.SquareRelative(ChessRules.E8, 0, 1) == ChessRules.NoSquare
            && ChessRules.SquareRelative(ChessRules.E1, 0, -1) == ChessRules.NoSquare
            && ChessRules.SquareRelative(ChessRules.H1, 1, 1) == ChessRules.NoSquare,
            "SquareRelative, including off every edge", "");

        Check(ChessRules.ParseSquare("i1") == ChessRules.NoSquare && ChessRules.ParseSquare("a9") == ChessRules.NoSquare
            && ChessRules.ParseSquare("a0") == ChessRules.NoSquare && ChessRules.ParseSquare("E4") == ChessRules.NoSquare
            && ChessRules.ParseSquare("e") == ChessRules.NoSquare && ChessRules.ParseSquare("e44") == ChessRules.NoSquare
            && ChessRules.ParseSquare("") == ChessRules.NoSquare && ChessRules.ParseSquare(null) == ChessRules.NoSquare
            && ChessRules.SquareName(ChessRules.NoSquare) == "-" && ChessRules.SquareName(64) == "-",
            "bad square names", "");
    }

    void TestPieces() {
        // encode -> decode for every colour and type.
        string errors = "";
        int[] seen = new int[16];
        for (int color = ChessRules.White; color <= ChessRules.Black; color++) {
            for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) {
                byte piece = ChessRules.PieceOf(color, type);
                if (piece == ChessRules.Empty) errors += " " + color + "/" + type + ":empty";
                if (piece >= 16) errors += " " + color + "/" + type + ":range=" + piece;
                else seen[piece]++;
                if (ChessRules.PieceTypeOf(piece) != type) errors += " " + color + "/" + type + ":type";
                if (ChessRules.PieceColorOf(piece) != color) errors += " " + color + "/" + type + ":color";
                if (ChessRules.PieceTypeFromLetter(ChessRules.PieceTypeLetter(type)[0]) != type)
                    errors += " " + color + "/" + type + ":letter";
            }
        }
        for (int i = 0; i < 16; i++) {
            if (seen[i] > 1) errors += " duplicate:" + i;
        }
        Check(errors == "", "piece encode -> decode, every colour and type", errors);

        Check(ChessRules.PieceTypeOf(ChessRules.Empty) == ChessRules.NoPieceType
            && ChessRules.PieceColorOf(ChessRules.Empty) == ChessRules.NoColor,
            "empty decodes to no type and no colour", "type " + ChessRules.PieceTypeOf(ChessRules.Empty)
            + " color " + ChessRules.PieceColorOf(ChessRules.Empty));

        Check(ChessRules.PieceOf(ChessRules.White, ChessRules.Pawn) == ChessRules.WhitePawn
            && ChessRules.PieceOf(ChessRules.White, ChessRules.Knight) == ChessRules.WhiteKnight
            && ChessRules.PieceOf(ChessRules.White, ChessRules.Bishop) == ChessRules.WhiteBishop
            && ChessRules.PieceOf(ChessRules.White, ChessRules.Rook) == ChessRules.WhiteRook
            && ChessRules.PieceOf(ChessRules.White, ChessRules.Queen) == ChessRules.WhiteQueen
            && ChessRules.PieceOf(ChessRules.White, ChessRules.King) == ChessRules.WhiteKing
            && ChessRules.PieceOf(ChessRules.Black, ChessRules.Pawn) == ChessRules.BlackPawn
            && ChessRules.PieceOf(ChessRules.Black, ChessRules.Knight) == ChessRules.BlackKnight
            && ChessRules.PieceOf(ChessRules.Black, ChessRules.Bishop) == ChessRules.BlackBishop
            && ChessRules.PieceOf(ChessRules.Black, ChessRules.Rook) == ChessRules.BlackRook
            && ChessRules.PieceOf(ChessRules.Black, ChessRules.Queen) == ChessRules.BlackQueen
            && ChessRules.PieceOf(ChessRules.Black, ChessRules.King) == ChessRules.BlackKing,
            "piece constants match PieceOf", "");

        Check(ChessRules.OtherColor(ChessRules.White) == ChessRules.Black
            && ChessRules.OtherColor(ChessRules.Black) == ChessRules.White,
            "OtherColor", "");

        Check(ChessRules.PieceTypeLetter(ChessRules.NoPieceType) == "" && ChessRules.PieceTypeLetter(7) == ""
            && ChessRules.PieceTypeFromLetter('p') == ChessRules.NoPieceType
            && ChessRules.PieceTypeFromLetter('X') == ChessRules.NoPieceType,
            "bad piece letters", "");
    }

    // ---- 2.2 ----

    // Field order: from, to, piece type, promotion, captured, castle (0/1), castling rights, en passant file (-1 = none).
    int[] UnpackFields(int move) {
        int[] f = new int[8];
        f[0] = ChessRules.MoveFrom(move);
        f[1] = ChessRules.MoveTo(move);
        f[2] = ChessRules.MovePieceType(move);
        f[3] = ChessRules.MovePromotion(move);
        f[4] = ChessRules.MoveCaptured(move);
        f[5] = ChessRules.MoveIsCastle(move) ? 1 : 0;
        f[6] = ChessRules.MovePrevCastlingRights(move);
        f[7] = ChessRules.MovePrevEnPassantFile(move);
        return f;
    }

    int PackFields(int[] f) {
        return ChessRules.PackMove(f[0], f[1], f[2], f[3], f[4], f[5] != 0, f[6], f[7]);
    }

    string FieldsText(int[] f) {
        string text = "";
        for (int i = 0; i < f.Length; i++) text += (i == 0 ? "" : ",") + f[i];
        return text;
    }

    // Packs the fields, unpacks them again and returns any difference ("" if none).
    string RoundTrip(int[] fields, string label) {
        int move = PackFields(fields);
        int[] back = UnpackFields(move);
        string errors = "";
        for (int i = 0; i < 8; i++) {
            if (back[i] != fields[i]) {
                errors += " " + label + ": in " + FieldsText(fields) + " out " + FieldsText(back);
                break;
            }
        }
        if (ChessRules.MovePrevEnPassantValid(move) != (fields[7] >= 0)) errors += " " + label + ":valid";
        if (((move >> 30) & 3) != 0) errors += " " + label + ":spare bits set";
        return errors;
    }

    void TestMovePacking() {
        int[] min = new int[] { 0, 0, 0, 0, 0, 0, 0, -1 };
        int[] max = new int[] { 63, 63, 7, 7, 7, 1, 15, 7 };
        string errors = RoundTrip(min, "all min") + RoundTrip(max, "all max");
        Check(errors == "" && PackFields(min) == 0 && PackFields(max) == 0x3FFFFFFF,
            "pack -> unpack, every field at min and at max",
            errors + " min=" + PackFields(min) + " max=" + PackFields(max));

        // One field at max with the rest at min, and one at min with the rest at max, so no field bleeds into another.
        errors = "";
        for (int field = 0; field < 8; field++) {
            int[] oneMax = new int[8];
            int[] oneMin = new int[8];
            for (int i = 0; i < 8; i++) {
                oneMax[i] = i == field ? max[i] : min[i];
                oneMin[i] = i == field ? min[i] : max[i];
            }
            errors += RoundTrip(oneMax, "field " + field + " max") + RoundTrip(oneMin, "field " + field + " min");
        }
        Check(errors == "", "pack -> unpack, each field alone at min and at max", errors);

        // En passant on the a-file (file 0) is valid, not "none".
        int[] epA = new int[] { ChessRules.E5, ChessRules.F6, ChessRules.Pawn, 0, 0, 0, ChessRules.CastleAll, ChessRules.FileA };
        int epAMove = PackFields(epA);
        Check(RoundTrip(epA, "ep a-file") == "" && ChessRules.MovePrevEnPassantValid(epAMove)
            && ChessRules.MovePrevEnPassantFile(epAMove) == ChessRules.FileA,
            "en passant on the a-file is kept as valid", FieldsText(UnpackFields(epAMove)));

        int noEp = ChessRules.PackMove(ChessRules.E2, ChessRules.E4, ChessRules.Pawn, 0, 0, false, ChessRules.CastleAll, -1);
        Check(!ChessRules.MovePrevEnPassantValid(noEp) && ChessRules.MovePrevEnPassantFile(noEp) == -1
            && ChessRules.MovePrevEnPassantSquare(noEp, ChessRules.White) == ChessRules.NoSquare,
            "no en passant unpacks as none", FieldsText(UnpackFields(noEp)));

        Check((ChessRules.CastleWhiteKingSide | ChessRules.CastleWhiteQueenSide | ChessRules.CastleBlackKingSide
                | ChessRules.CastleBlackQueenSide) == ChessRules.CastleAll
            && ChessRules.CastleWhiteKingSide + ChessRules.CastleWhiteQueenSide + ChessRules.CastleBlackKingSide
                + ChessRules.CastleBlackQueenSide == ChessRules.CastleAll,
            "castling right bits are distinct and fill the field", "");
    }

    void TestMoveDerived() {
        int all = ChessRules.CastleAll;
        int pawn = ChessRules.Pawn;

        // White exd6 e.p. after ...d7-d5, and Black dxe3 e.p. after e2-e4.
        int whiteEp = ChessRules.PackMove(ChessRules.E5, ChessRules.D6, pawn, 0, pawn, false, all, ChessRules.FileD);
        int blackEp = ChessRules.PackMove(ChessRules.D4, ChessRules.E3, pawn, 0, pawn, false, all, ChessRules.FileE);
        Check(ChessRules.IsEnPassantCapture(whiteEp) && !ChessRules.IsDoublePush(whiteEp)
            && ChessRules.MovePrevEnPassantSquare(whiteEp, ChessRules.White) == ChessRules.D6,
            "white en passant capture", "");
        Check(ChessRules.IsEnPassantCapture(blackEp) && !ChessRules.IsDoublePush(blackEp)
            && ChessRules.MovePrevEnPassantSquare(blackEp, ChessRules.Black) == ChessRules.E3,
            "black en passant capture", "");

        // An ordinary pawn capture on another file while en passant is available.
        int otherCapture = ChessRules.PackMove(ChessRules.E5, ChessRules.F6, pawn, 0, ChessRules.Knight, false, all,
            ChessRules.FileD);
        Check(!ChessRules.IsEnPassantCapture(otherCapture) && !ChessRules.IsDoublePush(otherCapture),
            "pawn capture beside the en passant square", "");

        // Double pushes, both colours.
        int whiteDouble = ChessRules.PackMove(ChessRules.E2, ChessRules.E4, pawn, 0, 0, false, all, -1);
        int blackDouble = ChessRules.PackMove(ChessRules.D7, ChessRules.D5, pawn, 0, 0, false, all, ChessRules.FileE);
        Check(ChessRules.IsDoublePush(whiteDouble) && !ChessRules.IsEnPassantCapture(whiteDouble)
            && ChessRules.IsDoublePush(blackDouble) && !ChessRules.IsEnPassantCapture(blackDouble),
            "double push, both colours", "");

        // Ordinary pawn moves: single pushes of both colours (with en passant available), and a promotion.
        int whiteSingle = ChessRules.PackMove(ChessRules.E2, ChessRules.E3, pawn, 0, 0, false, all, ChessRules.FileD);
        int blackSingle = ChessRules.PackMove(ChessRules.E7, ChessRules.E6, pawn, 0, 0, false, all, ChessRules.FileD);
        int promotion = ChessRules.PackMove(ChessRules.A7, ChessRules.A8, pawn, ChessRules.Queen, 0, false, 0, -1);
        Check(!ChessRules.IsDoublePush(whiteSingle) && !ChessRules.IsEnPassantCapture(whiteSingle)
            && !ChessRules.IsDoublePush(blackSingle) && !ChessRules.IsEnPassantCapture(blackSingle)
            && !ChessRules.IsDoublePush(promotion) && !ChessRules.IsEnPassantCapture(promotion),
            "ordinary pawn moves", "");

        // Non-pawn moves: a rook moving two ranks, a bishop landing on the en passant square, a knight.
        int rook = ChessRules.PackMove(ChessRules.A1, ChessRules.A3, ChessRules.Rook, 0, 0, false, all, -1);
        int bishop = ChessRules.PackMove(ChessRules.F4, ChessRules.D6, ChessRules.Bishop, 0, 0, false, all,
            ChessRules.FileD);
        int knight = ChessRules.PackMove(ChessRules.G1, ChessRules.F3, ChessRules.Knight, 0, 0, false, all, -1);
        Check(!ChessRules.IsDoublePush(rook) && !ChessRules.IsEnPassantCapture(rook)
            && !ChessRules.IsDoublePush(bishop) && !ChessRules.IsEnPassantCapture(bishop)
            && !ChessRules.IsDoublePush(knight) && !ChessRules.IsEnPassantCapture(knight),
            "non-pawn moves", "");
    }

    // ---- 2.3 ----

    byte[] EmptyBoard() {
        return new byte[64];
    }

    // The standard starting position.
    byte[] StartBoard() {
        byte[] board = new byte[64];
        int[] backRank = new int[] {
            ChessRules.Rook, ChessRules.Knight, ChessRules.Bishop, ChessRules.Queen,
            ChessRules.King, ChessRules.Bishop, ChessRules.Knight, ChessRules.Rook
        };
        for (int file = 0; file < 8; file++) {
            board[ChessRules.SquareOf(file, ChessRules.Rank1)] = ChessRules.PieceOf(ChessRules.White, backRank[file]);
            board[ChessRules.SquareOf(file, ChessRules.Rank2)] = ChessRules.WhitePawn;
            board[ChessRules.SquareOf(file, ChessRules.Rank7)] = ChessRules.BlackPawn;
            board[ChessRules.SquareOf(file, ChessRules.Rank8)] = ChessRules.PieceOf(ChessRules.Black, backRank[file]);
        }
        return board;
    }

    // State with the given king and rook starting files.
    int[] NewState960(int sideToMove, int castlingRights, int kingFile, int leftRookFile, int rightRookFile,
        int whiteKing, int blackKing) {
        int[] state = NewState(sideToMove, castlingRights, -1, whiteKing, blackKing);
        state[ChessPosition.StateKingFile] = kingFile;
        state[ChessPosition.StateLeftRookFile] = leftRookFile;
        state[ChessPosition.StateRightRookFile] = rightRookFile;
        return state;
    }

    // State with the standard king and rook starting files.
    int[] NewState(int sideToMove, int castlingRights, int enPassantFile, int whiteKing, int blackKing) {
        int[] state = new int[ChessPosition.StateLength];
        state[ChessPosition.StateSideToMove] = sideToMove;
        state[ChessPosition.StateCastlingRights] = castlingRights;
        state[ChessPosition.StateEnPassantFile] = enPassantFile;
        state[ChessPosition.StateKingFile] = ChessRules.FileE;
        state[ChessPosition.StateLeftRookFile] = ChessRules.FileA;
        state[ChessPosition.StateRightRookFile] = ChessRules.FileH;
        state[ChessPosition.StateWhiteKingSquare] = whiteKing;
        state[ChessPosition.StateBlackKingSquare] = blackKing;
        return state;
    }

    byte[] CopyBoard(byte[] board) {
        byte[] copy = new byte[board.Length];
        for (int i = 0; i < board.Length; i++) copy[i] = board[i];
        return copy;
    }

    int[] CopyState(int[] state) {
        int[] copy = new int[state.Length];
        for (int i = 0; i < state.Length; i++) copy[i] = state[i];
        return copy;
    }

    // "" if equal, else the squares that differ.
    string BoardDiff(byte[] actual, byte[] expected) {
        string diff = "";
        for (int sq = 0; sq < 64; sq++) {
            if (actual[sq] != expected[sq]) {
                diff += " " + ChessRules.SquareName(sq) + "=" + actual[sq] + " (expected " + expected[sq] + ")";
            }
        }
        return diff;
    }

    // "" if equal, else the state entries that differ.
    string StateDiff(int[] actual, int[] expected) {
        string diff = "";
        for (int i = 0; i < ChessPosition.StateLength; i++) {
            if (actual[i] != expected[i]) diff += " state[" + i + "]=" + actual[i] + " (expected " + expected[i] + ")";
        }
        return diff;
    }

    // Packs a non-castling move for the position, taking the piece, capture and previous state from the arrays.
    // A pawn moving diagonally onto an empty square is taken to be capturing en passant.
    int MoveOn(byte[] board, int[] state, int from, int to, int promotion) {
        int type = ChessRules.PieceTypeOf(board[from]);
        int captured = ChessRules.PieceTypeOf(board[to]);
        if (type == ChessRules.Pawn && captured == ChessRules.NoPieceType
            && ChessRules.FileOf(from) != ChessRules.FileOf(to)) captured = ChessRules.Pawn;
        return ChessRules.PackMove(from, to, type, promotion, captured, false,
            state[ChessPosition.StateCastlingRights], state[ChessPosition.StateEnPassantFile]);
    }

    // Makes the move on copies of board/state, compares with the expected position, then unmakes it and compares
    // with the original.
    void CheckMakeUnmake(string name, byte[] board, int[] state, int move, byte[] expectedBoard, int[] expectedState) {
        byte[] b = CopyBoard(board);
        int[] s = CopyState(state);
        ChessPosition.MakeMove(b, s, move);
        string errors = BoardDiff(b, expectedBoard) + StateDiff(s, expectedState);
        if (errors != "") errors = " after make:" + errors;
        ChessPosition.UnmakeMove(b, s, move);
        string undoErrors = BoardDiff(b, board) + StateDiff(s, state);
        if (undoErrors != "") errors += " after unmake:" + undoErrors;
        Check(errors == "", "make/unmake: " + name, errors);
    }

    void TestMakeUnmake() {
        int white = ChessRules.White;
        int black = ChessRules.Black;
        int all = ChessRules.CastleAll;

        // Ordinary moves: double pushes set en passant, any other move clears it.
        byte[] start = StartBoard();
        int[] startState = NewState(white, all, -1, ChessRules.E1, ChessRules.E8);
        byte[] eb = CopyBoard(start);
        eb[ChessRules.E2] = ChessRules.Empty;
        eb[ChessRules.E4] = ChessRules.WhitePawn;
        int[] es = CopyState(startState);
        es[ChessPosition.StateSideToMove] = black;
        es[ChessPosition.StateEnPassantFile] = ChessRules.FileE;
        CheckMakeUnmake("white double push sets en passant", start, startState,
            MoveOn(start, startState, ChessRules.E2, ChessRules.E4, 0), eb, es);

        int[] blackState = NewState(black, all, ChessRules.FileE, ChessRules.E1, ChessRules.E8);
        eb = CopyBoard(start);
        eb[ChessRules.D7] = ChessRules.Empty;
        eb[ChessRules.D5] = ChessRules.BlackPawn;
        es = CopyState(blackState);
        es[ChessPosition.StateSideToMove] = white;
        es[ChessPosition.StateEnPassantFile] = ChessRules.FileD;
        CheckMakeUnmake("black double push replaces en passant", start, blackState,
            MoveOn(start, blackState, ChessRules.D7, ChessRules.D5, 0), eb, es);

        int[] epState = NewState(white, all, ChessRules.FileD, ChessRules.E1, ChessRules.E8);
        eb = CopyBoard(start);
        eb[ChessRules.G1] = ChessRules.Empty;
        eb[ChessRules.F3] = ChessRules.WhiteKnight;
        es = CopyState(epState);
        es[ChessPosition.StateSideToMove] = black;
        es[ChessPosition.StateEnPassantFile] = -1;
        CheckMakeUnmake("knight move clears en passant", start, epState,
            MoveOn(start, epState, ChessRules.G1, ChessRules.F3, 0), eb, es);

        eb = CopyBoard(start);
        eb[ChessRules.E2] = ChessRules.Empty;
        eb[ChessRules.E3] = ChessRules.WhitePawn;
        CheckMakeUnmake("single push clears en passant", start, epState,
            MoveOn(start, epState, ChessRules.E2, ChessRules.E3, 0), eb, es);

        // Captures by both colours.
        byte[] cap = EmptyBoard();
        cap[ChessRules.E1] = ChessRules.WhiteKing;
        cap[ChessRules.E8] = ChessRules.BlackKing;
        cap[ChessRules.C4] = ChessRules.WhiteBishop;
        cap[ChessRules.F7] = ChessRules.BlackPawn;
        cap[ChessRules.D8] = ChessRules.BlackQueen;
        cap[ChessRules.D1] = ChessRules.WhiteQueen;
        int[] capState = NewState(white, 0, -1, ChessRules.E1, ChessRules.E8);
        int bxf7 = MoveOn(cap, capState, ChessRules.C4, ChessRules.F7, 0);
        eb = CopyBoard(cap);
        eb[ChessRules.C4] = ChessRules.Empty;
        eb[ChessRules.F7] = ChessRules.WhiteBishop;
        es = CopyState(capState);
        es[ChessPosition.StateSideToMove] = black;
        CheckMakeUnmake("white bishop captures pawn", cap, capState, bxf7, eb, es);
        Check(ChessRules.MoveCaptured(bxf7) == ChessRules.Pawn, "capture move records the captured type",
            "captured " + ChessRules.MoveCaptured(bxf7));

        int[] capStateBlack = NewState(black, 0, -1, ChessRules.E1, ChessRules.E8);
        eb = CopyBoard(cap);
        eb[ChessRules.D8] = ChessRules.Empty;
        eb[ChessRules.D1] = ChessRules.BlackQueen;
        es = CopyState(capStateBlack);
        es[ChessPosition.StateSideToMove] = white;
        CheckMakeUnmake("black queen captures queen", cap, capStateBlack,
            MoveOn(cap, capStateBlack, ChessRules.D8, ChessRules.D1, 0), eb, es);

        // En passant by both colours: the captured pawn is beside the from square, not on the to square.
        byte[] ep = EmptyBoard();
        ep[ChessRules.E1] = ChessRules.WhiteKing;
        ep[ChessRules.E8] = ChessRules.BlackKing;
        ep[ChessRules.E5] = ChessRules.WhitePawn;
        ep[ChessRules.D5] = ChessRules.BlackPawn;
        ep[ChessRules.D4] = ChessRules.BlackPawn;
        ep[ChessRules.E4] = ChessRules.WhitePawn;
        int[] epWhite = NewState(white, 0, ChessRules.FileD, ChessRules.E1, ChessRules.E8);
        int exd6 = MoveOn(ep, epWhite, ChessRules.E5, ChessRules.D6, 0);
        eb = CopyBoard(ep);
        eb[ChessRules.E5] = ChessRules.Empty;
        eb[ChessRules.D5] = ChessRules.Empty;
        eb[ChessRules.D6] = ChessRules.WhitePawn;
        es = CopyState(epWhite);
        es[ChessPosition.StateSideToMove] = black;
        es[ChessPosition.StateEnPassantFile] = -1;
        CheckMakeUnmake("white en passant capture", ep, epWhite, exd6, eb, es);

        int[] epBlack = NewState(black, 0, ChessRules.FileE, ChessRules.E1, ChessRules.E8);
        eb = CopyBoard(ep);
        eb[ChessRules.D4] = ChessRules.Empty;
        eb[ChessRules.E4] = ChessRules.Empty;
        eb[ChessRules.E3] = ChessRules.BlackPawn;
        es = CopyState(epBlack);
        es[ChessPosition.StateSideToMove] = white;
        es[ChessPosition.StateEnPassantFile] = -1;
        CheckMakeUnmake("black en passant capture", ep, epBlack,
            MoveOn(ep, epBlack, ChessRules.D4, ChessRules.E3, 0), eb, es);

        // Promotions: push and capture, both colours, including underpromotion.
        byte[] promo = EmptyBoard();
        promo[ChessRules.H1] = ChessRules.WhiteKing;
        promo[ChessRules.H8] = ChessRules.BlackKing;
        promo[ChessRules.A7] = ChessRules.WhitePawn;
        promo[ChessRules.C7] = ChessRules.WhitePawn;
        promo[ChessRules.B8] = ChessRules.BlackKnight;
        promo[ChessRules.E2] = ChessRules.BlackPawn;
        promo[ChessRules.G2] = ChessRules.BlackPawn;
        promo[ChessRules.F1] = ChessRules.WhiteBishop;
        int[] promoWhite = NewState(white, 0, -1, ChessRules.H1, ChessRules.H8);
        int[] promoBlack = NewState(black, 0, -1, ChessRules.H1, ChessRules.H8);

        eb = CopyBoard(promo);
        eb[ChessRules.A7] = ChessRules.Empty;
        eb[ChessRules.A8] = ChessRules.WhiteQueen;
        es = CopyState(promoWhite);
        es[ChessPosition.StateSideToMove] = black;
        CheckMakeUnmake("white promotion to queen", promo, promoWhite,
            MoveOn(promo, promoWhite, ChessRules.A7, ChessRules.A8, ChessRules.Queen), eb, es);

        eb = CopyBoard(promo);
        eb[ChessRules.C7] = ChessRules.Empty;
        eb[ChessRules.B8] = ChessRules.WhiteKnight;
        CheckMakeUnmake("white capture-promotion to knight", promo, promoWhite,
            MoveOn(promo, promoWhite, ChessRules.C7, ChessRules.B8, ChessRules.Knight), eb, es);

        eb = CopyBoard(promo);
        eb[ChessRules.E2] = ChessRules.Empty;
        eb[ChessRules.E1] = ChessRules.BlackRook;
        es = CopyState(promoBlack);
        es[ChessPosition.StateSideToMove] = white;
        CheckMakeUnmake("black promotion to rook", promo, promoBlack,
            MoveOn(promo, promoBlack, ChessRules.E2, ChessRules.E1, ChessRules.Rook), eb, es);

        eb = CopyBoard(promo);
        eb[ChessRules.G2] = ChessRules.Empty;
        eb[ChessRules.F1] = ChessRules.BlackQueen;
        CheckMakeUnmake("black capture-promotion to queen", promo, promoBlack,
            MoveOn(promo, promoBlack, ChessRules.G2, ChessRules.F1, ChessRules.Queen), eb, es);

        // King moves update the king square; capturing a king (only with prohibitSelfCheck off) clears it.
        byte[] kings = EmptyBoard();
        kings[ChessRules.E1] = ChessRules.WhiteKing;
        kings[ChessRules.E8] = ChessRules.BlackKing;
        kings[ChessRules.E4] = ChessRules.WhiteRook;
        int[] kingsWhite = NewState(white, 0, -1, ChessRules.E1, ChessRules.E8);
        eb = CopyBoard(kings);
        eb[ChessRules.E1] = ChessRules.Empty;
        eb[ChessRules.D2] = ChessRules.WhiteKing;
        es = CopyState(kingsWhite);
        es[ChessPosition.StateSideToMove] = black;
        es[ChessPosition.StateWhiteKingSquare] = ChessRules.D2;
        CheckMakeUnmake("white king move updates its square", kings, kingsWhite,
            MoveOn(kings, kingsWhite, ChessRules.E1, ChessRules.D2, 0), eb, es);

        int[] kingsBlack = NewState(black, 0, -1, ChessRules.E1, ChessRules.E8);
        eb = CopyBoard(kings);
        eb[ChessRules.E8] = ChessRules.Empty;
        eb[ChessRules.F7] = ChessRules.BlackKing;
        es = CopyState(kingsBlack);
        es[ChessPosition.StateSideToMove] = white;
        es[ChessPosition.StateBlackKingSquare] = ChessRules.F7;
        CheckMakeUnmake("black king move updates its square", kings, kingsBlack,
            MoveOn(kings, kingsBlack, ChessRules.E8, ChessRules.F7, 0), eb, es);

        eb = CopyBoard(kings);
        eb[ChessRules.E4] = ChessRules.Empty;
        eb[ChessRules.E8] = ChessRules.WhiteRook;
        es = CopyState(kingsWhite);
        es[ChessPosition.StateSideToMove] = black;
        es[ChessPosition.StateBlackKingSquare] = ChessRules.NoSquare;
        CheckMakeUnmake("capturing a king clears its square", kings, kingsWhite,
            MoveOn(kings, kingsWhite, ChessRules.E4, ChessRules.E8, 0), eb, es);

        // A sequence made and then unmade in reverse, checking each step's undo restores the position before it:
        // 1.e4 d5 2.exd5 c5 3.dxc6 e.p. bxc6.
        byte[] b = StartBoard();
        int[] s = NewState(white, all, -1, ChessRules.E1, ChessRules.E8);
        int[] froms = new int[] { ChessRules.E2, ChessRules.D7, ChessRules.E4, ChessRules.C7, ChessRules.D5, ChessRules.B7 };
        int[] tos = new int[] { ChessRules.E4, ChessRules.D5, ChessRules.D5, ChessRules.C5, ChessRules.C6, ChessRules.C6 };
        int count = froms.Length;
        int[] moves = new int[count];
        byte[][] boardsBefore = new byte[count][];
        int[][] statesBefore = new int[count][];
        for (int i = 0; i < count; i++) {
            boardsBefore[i] = CopyBoard(b);
            statesBefore[i] = CopyState(s);
            moves[i] = MoveOn(b, s, froms[i], tos[i], 0);
            ChessPosition.MakeMove(b, s, moves[i]);
        }
        eb = StartBoard();
        eb[ChessRules.E2] = ChessRules.Empty;
        eb[ChessRules.D7] = ChessRules.Empty;
        eb[ChessRules.C7] = ChessRules.Empty;
        eb[ChessRules.B7] = ChessRules.Empty;
        eb[ChessRules.C6] = ChessRules.BlackPawn;
        string errors = BoardDiff(b, eb);
        if (!ChessRules.IsEnPassantCapture(moves[4])) errors += " dxc6 not en passant";
        if (s[ChessPosition.StateSideToMove] != white || s[ChessPosition.StateEnPassantFile] != -1) {
            errors += " state after:" + StateDiff(s, NewState(white, all, -1, ChessRules.E1, ChessRules.E8));
        }
        for (int i = count - 1; i >= 0; i--) {
            ChessPosition.UnmakeMove(b, s, moves[i]);
            string diff = BoardDiff(b, boardsBefore[i]) + StateDiff(s, statesBefore[i]);
            if (diff != "") errors += " undo ply " + (i + 1) + ":" + diff;
        }
        Check(errors == "", "make/unmake: 1.e4 d5 2.exd5 c5 3.dxc6 e.p. bxc6, then undo all", errors);
    }

    // Packs a castling move: from = the king's square, to = its rook's starting square.
    int CastleOn(int[] state, int kingSquare, int rookSquare) {
        return ChessRules.PackMove(kingSquare, rookSquare, ChessRules.King, 0, 0, true,
            state[ChessPosition.StateCastlingRights], state[ChessPosition.StateEnPassantFile]);
    }

    // Checks one castling move: the king and rook end on the given squares and nothing else changes on the board;
    // the mover loses both rights, en passant is cleared, the king square follows the king. Unmake restores both
    // arrays.
    void CheckCastle(string name, byte[] board, int[] state, int kingSquare, int rookSquare, int kingTo, int rookTo) {
        int mover = state[ChessPosition.StateSideToMove];
        byte[] eb = CopyBoard(board);
        eb[kingSquare] = ChessRules.Empty;
        eb[rookSquare] = ChessRules.Empty;
        eb[kingTo] = ChessRules.PieceOf(mover, ChessRules.King);
        eb[rookTo] = ChessRules.PieceOf(mover, ChessRules.Rook);
        int[] es = CopyState(state);
        es[ChessPosition.StateSideToMove] = mover ^ 1;
        es[ChessPosition.StateEnPassantFile] = -1;
        int moverRights = mover == ChessRules.White
            ? ChessRules.CastleWhiteKingSide | ChessRules.CastleWhiteQueenSide
            : ChessRules.CastleBlackKingSide | ChessRules.CastleBlackQueenSide;
        es[ChessPosition.StateCastlingRights] = state[ChessPosition.StateCastlingRights] & ~moverRights;
        es[ChessPosition.StateWhiteKingSquare + mover] = kingTo;
        CheckMakeUnmake("castling: " + name, board, state, CastleOn(state, kingSquare, rookSquare), eb, es);
    }

    // Makes a non-castling move and checks the castling rights after it (the 2.3a checks cover the board), and that
    // unmake restores both arrays.
    void CheckRightsAfter(string name, byte[] board, int[] state, int from, int to, int promotion, int expectedRights) {
        byte[] b = CopyBoard(board);
        int[] s = CopyState(state);
        int move = MoveOn(b, s, from, to, promotion);
        ChessPosition.MakeMove(b, s, move);
        string errors = "";
        int rights = s[ChessPosition.StateCastlingRights];
        if (rights != expectedRights) errors += " rights after make " + rights + " (expected " + expectedRights + ")";
        ChessPosition.UnmakeMove(b, s, move);
        string undoErrors = BoardDiff(b, board) + StateDiff(s, state);
        if (undoErrors != "") errors += " after unmake:" + undoErrors;
        Check(errors == "", "castling rights: " + name, errors);
    }

    void TestCastling() {
        int white = ChessRules.White;
        int black = ChessRules.Black;
        int all = ChessRules.CastleAll;
        int wk = ChessRules.CastleWhiteKingSide;
        int wq = ChessRules.CastleWhiteQueenSide;
        int bk = ChessRules.CastleBlackKingSide;
        int bq = ChessRules.CastleBlackQueenSide;

        // Standard: r3k2r/8/8/8/8/8/8/R3K2R, both sides' O-O and O-O-O. White moves with en passant set, to check
        // that castling clears it.
        byte[] std = EmptyBoard();
        std[ChessRules.E1] = ChessRules.WhiteKing;
        std[ChessRules.A1] = ChessRules.WhiteRook;
        std[ChessRules.H1] = ChessRules.WhiteRook;
        std[ChessRules.E8] = ChessRules.BlackKing;
        std[ChessRules.A8] = ChessRules.BlackRook;
        std[ChessRules.H8] = ChessRules.BlackRook;
        int[] stdWhite = NewState(white, all, ChessRules.FileD, ChessRules.E1, ChessRules.E8);
        int[] stdBlack = NewState(black, all, -1, ChessRules.E1, ChessRules.E8);
        CheckCastle("white O-O", std, stdWhite, ChessRules.E1, ChessRules.H1, ChessRules.G1, ChessRules.F1);
        CheckCastle("white O-O-O", std, stdWhite, ChessRules.E1, ChessRules.A1, ChessRules.C1, ChessRules.D1);
        CheckCastle("black O-O", std, stdBlack, ChessRules.E8, ChessRules.H8, ChessRules.G8, ChessRules.F8);
        CheckCastle("black O-O-O", std, stdBlack, ChessRules.E8, ChessRules.A8, ChessRules.C8, ChessRules.D8);

        // The other side's rights are kept.
        int[] partial = NewState(white, wk | bq, -1, ChessRules.E1, ChessRules.E8);
        CheckCastle("white O-O keeps black's rights", std, partial, ChessRules.E1, ChessRules.H1, ChessRules.G1,
            ChessRules.F1);

        // Chess960, king on c between rooks on b and g (the chess960.pgn.txt start, "GBgb"): O-O-O where the king
        // doesn't move, and O-O where it does.
        byte[] c960 = EmptyBoard();
        c960[ChessRules.C1] = ChessRules.WhiteKing;
        c960[ChessRules.B1] = ChessRules.WhiteRook;
        c960[ChessRules.G1] = ChessRules.WhiteRook;
        c960[ChessRules.C8] = ChessRules.BlackKing;
        c960[ChessRules.B8] = ChessRules.BlackRook;
        c960[ChessRules.G8] = ChessRules.BlackRook;
        c960[ChessRules.A1] = ChessRules.WhiteQueen;
        c960[ChessRules.A8] = ChessRules.BlackQueen;
        int[] c960White = NewState960(white, all, ChessRules.FileC, ChessRules.FileB, ChessRules.FileG,
            ChessRules.C1, ChessRules.C8);
        int[] c960Black = NewState960(black, all, ChessRules.FileC, ChessRules.FileB, ChessRules.FileG,
            ChessRules.C1, ChessRules.C8);
        CheckCastle("chess960 white O-O-O, king doesn't move", c960, c960White, ChessRules.C1, ChessRules.B1,
            ChessRules.C1, ChessRules.D1);
        CheckCastle("chess960 black O-O-O, king doesn't move", c960, c960Black, ChessRules.C8, ChessRules.B8,
            ChessRules.C8, ChessRules.D8);
        CheckCastle("chess960 white O-O", c960, c960White, ChessRules.C1, ChessRules.G1, ChessRules.G1, ChessRules.F1);
        CheckCastle("chess960 black O-O", c960, c960Black, ChessRules.C8, ChessRules.G8, ChessRules.G8, ChessRules.F8);

        // Chess960, king on f between rooks on d and g: O-O swaps the king and rook (f <-> g); on O-O-O the rook
        // doesn't move.
        byte[] swap = EmptyBoard();
        swap[ChessRules.F1] = ChessRules.WhiteKing;
        swap[ChessRules.D1] = ChessRules.WhiteRook;
        swap[ChessRules.G1] = ChessRules.WhiteRook;
        swap[ChessRules.F8] = ChessRules.BlackKing;
        swap[ChessRules.D8] = ChessRules.BlackRook;
        swap[ChessRules.G8] = ChessRules.BlackRook;
        int[] swapWhite = NewState960(white, all, ChessRules.FileF, ChessRules.FileD, ChessRules.FileG,
            ChessRules.F1, ChessRules.F8);
        int[] swapBlack = NewState960(black, all, ChessRules.FileF, ChessRules.FileD, ChessRules.FileG,
            ChessRules.F1, ChessRules.F8);
        CheckCastle("chess960 white O-O, king and rook swap", swap, swapWhite, ChessRules.F1, ChessRules.G1,
            ChessRules.G1, ChessRules.F1);
        CheckCastle("chess960 black O-O, king and rook swap", swap, swapBlack, ChessRules.F8, ChessRules.G8,
            ChessRules.G8, ChessRules.F8);
        CheckCastle("chess960 white O-O-O, rook doesn't move", swap, swapWhite, ChessRules.F1, ChessRules.D1,
            ChessRules.C1, ChessRules.D1);

        // Chess960, king on d between rooks on c and h: O-O-O swaps the king and rook (d <-> c).
        byte[] qswap = EmptyBoard();
        qswap[ChessRules.D1] = ChessRules.WhiteKing;
        qswap[ChessRules.C1] = ChessRules.WhiteRook;
        qswap[ChessRules.H1] = ChessRules.WhiteRook;
        qswap[ChessRules.D8] = ChessRules.BlackKing;
        qswap[ChessRules.C8] = ChessRules.BlackRook;
        qswap[ChessRules.H8] = ChessRules.BlackRook;
        int[] qswapBlack = NewState960(black, all, ChessRules.FileD, ChessRules.FileC, ChessRules.FileH,
            ChessRules.D1, ChessRules.D8);
        CheckCastle("chess960 black O-O-O, king and rook swap", qswap, qswapBlack, ChessRules.D8, ChessRules.C8,
            ChessRules.C8, ChessRules.D8);

        // Chess960, king on g between rooks on b and h: O-O where the king doesn't move, and a long O-O-O.
        byte[] kg = EmptyBoard();
        kg[ChessRules.G1] = ChessRules.WhiteKing;
        kg[ChessRules.B1] = ChessRules.WhiteRook;
        kg[ChessRules.H1] = ChessRules.WhiteRook;
        kg[ChessRules.G8] = ChessRules.BlackKing;
        int[] kgWhite = NewState960(white, wk | wq, ChessRules.FileG, ChessRules.FileB, ChessRules.FileH,
            ChessRules.G1, ChessRules.G8);
        CheckCastle("chess960 white O-O, king doesn't move", kg, kgWhite, ChessRules.G1, ChessRules.H1,
            ChessRules.G1, ChessRules.F1);
        CheckCastle("chess960 white O-O-O from g", kg, kgWhite, ChessRules.G1, ChessRules.B1,
            ChessRules.C1, ChessRules.D1);

        // Losing rights by moving, standard position.
        CheckRightsAfter("white king move loses both white rights", std, stdWhite, ChessRules.E1, ChessRules.E2, 0,
            bk | bq);
        CheckRightsAfter("black king move loses both black rights", std, stdBlack, ChessRules.E8, ChessRules.D7, 0,
            wk | wq);
        CheckRightsAfter("white h-rook move loses K", std, stdWhite, ChessRules.H1, ChessRules.H5, 0, wq | bk | bq);
        CheckRightsAfter("white a-rook move loses Q", std, stdWhite, ChessRules.A1, ChessRules.B1, 0, wk | bk | bq);
        CheckRightsAfter("black h-rook move loses k", std, stdBlack, ChessRules.H8, ChessRules.G8, 0, wk | wq | bq);
        CheckRightsAfter("black a-rook move loses q", std, stdBlack, ChessRules.A8, ChessRules.A2, 0, wk | wq | bk);

        // Capturing a rook on its starting square loses the opponent's right (Java bug, docs/java-bugs.md).
        CheckRightsAfter("white Rxh8 loses K and k", std, stdWhite, ChessRules.H1, ChessRules.H8, 0, wq | bq);
        CheckRightsAfter("black Rxa1 loses q and Q", std, stdBlack, ChessRules.A8, ChessRules.A1, 0, wk | bk);

        byte[] bxh8 = CopyBoard(std);
        bxh8[ChessRules.C3] = ChessRules.WhiteBishop;
        bxh8[ChessRules.H1] = ChessRules.Empty;
        int[] bxh8White = NewState(white, wq | bk | bq, -1, ChessRules.E1, ChessRules.E8);
        CheckRightsAfter("white Bxh8 loses k only", bxh8, bxh8White, ChessRules.C3, ChessRules.H8, 0, wq | bq);
        byte[] bxa1 = CopyBoard(std);
        bxa1[ChessRules.D4] = ChessRules.BlackBishop;
        CheckRightsAfter("black Bxa1 loses Q only", bxa1, stdBlack, ChessRules.D4, ChessRules.A1, 0, wk | bk | bq);

        // Pawn capture-promotions onto a rook's starting square.
        byte[] pxr = CopyBoard(std);
        pxr[ChessRules.B7] = ChessRules.WhitePawn;
        pxr[ChessRules.G2] = ChessRules.BlackPawn;
        CheckRightsAfter("white bxa8=Q loses q", pxr, stdWhite, ChessRules.B7, ChessRules.A8, ChessRules.Queen,
            wk | wq | bk);
        CheckRightsAfter("black gxh1=N loses K", pxr, stdBlack, ChessRules.G2, ChessRules.H1, ChessRules.Knight,
            wq | bk | bq);

        // Moves that touch no starting square keep the rights: a capture elsewhere, a rook returning to its square.
        byte[] other = CopyBoard(std);
        other[ChessRules.D4] = ChessRules.WhiteKnight;
        other[ChessRules.E6] = ChessRules.BlackPawn;
        CheckRightsAfter("unrelated capture keeps all rights", other, stdWhite, ChessRules.D4, ChessRules.E6, 0, all);
        byte[] movedRook = CopyBoard(std);
        movedRook[ChessRules.H1] = ChessRules.Empty;
        movedRook[ChessRules.H3] = ChessRules.WhiteRook;
        int[] movedRookState = NewState(white, wq | bk | bq, -1, ChessRules.E1, ChessRules.E8);
        CheckRightsAfter("rook returning to its start square changes nothing", movedRook, movedRookState,
            ChessRules.H3, ChessRules.H1, 0, wq | bk | bq);

        // Chess960 starting files: the b-rook is queen side, the g-rook king side, the c-king both.
        CheckRightsAfter("chess960 white b-rook move loses Q", c960, c960White, ChessRules.B1, ChessRules.B5, 0,
            wk | bk | bq);
        CheckRightsAfter("chess960 white g-rook move loses K", c960, c960White, ChessRules.G1, ChessRules.G4, 0,
            wq | bk | bq);
        CheckRightsAfter("chess960 black king move loses k and q", c960, c960Black, ChessRules.C8, ChessRules.D7, 0,
            wk | wq);
        CheckRightsAfter("chess960 Rxg8 loses K and k", c960, c960White, ChessRules.G1, ChessRules.G8, 0, wq | bq);
        // The queen on a1 stands where a standard rook would: moving it keeps the rights.
        CheckRightsAfter("chess960 queen on a1 moving keeps rights", c960, c960White, ChessRules.A1, ChessRules.A4, 0,
            all);

        // A sequence with castling, undone in reverse: 1.O-O (loses K and Q) Rxa1 (loses q) 2.Rxa1 (nothing left to
        // lose but k, which stays). Checks the rights after each ply and that each undo restores the step before.
        byte[] b = CopyBoard(std);
        int[] s = NewState(white, all, -1, ChessRules.E1, ChessRules.E8);
        int count = 3;
        int[] moves = new int[count];
        byte[][] boardsBefore = new byte[count][];
        int[][] statesBefore = new int[count][];
        int[] expectedRights = new int[] { bk | bq, bk, bk };
        string errors = "";
        for (int i = 0; i < count; i++) {
            boardsBefore[i] = CopyBoard(b);
            statesBefore[i] = CopyState(s);
            if (i == 0) moves[i] = CastleOn(s, ChessRules.E1, ChessRules.H1);
            else if (i == 1) moves[i] = MoveOn(b, s, ChessRules.A8, ChessRules.A1, 0);
            else moves[i] = MoveOn(b, s, ChessRules.F1, ChessRules.A1, 0);
            ChessPosition.MakeMove(b, s, moves[i]);
            if (s[ChessPosition.StateCastlingRights] != expectedRights[i]) {
                errors += " ply " + (i + 1) + " rights " + s[ChessPosition.StateCastlingRights];
            }
        }
        for (int i = count - 1; i >= 0; i--) {
            ChessPosition.UnmakeMove(b, s, moves[i]);
            string diff = BoardDiff(b, boardsBefore[i]) + StateDiff(s, statesBefore[i]);
            if (diff != "") errors += " undo ply " + (i + 1) + ":" + diff;
        }
        Check(errors == "", "castling: 1.O-O Rxa1 2.Rxa1, rights per ply, then undo all", errors);
    }

    // ---- 2.4 ----

    // A move as text for comparing with hand-written lists: from and to squares, then "x" and the captured type,
    // "=" and the promotion type, "c" for castling. E.g. "e2e4", "b7a8xR=Q", "e1h1c".
    string MoveText(int move) {
        string text = ChessRules.SquareName(ChessRules.MoveFrom(move)) + ChessRules.SquareName(ChessRules.MoveTo(move));
        int captured = ChessRules.MoveCaptured(move);
        if (captured != ChessRules.NoPieceType) text += "x" + ChessRules.PieceTypeLetter(captured);
        int promotion = ChessRules.MovePromotion(move);
        if (promotion != ChessRules.NoPieceType) text += "=" + ChessRules.PieceTypeLetter(promotion);
        if (ChessRules.MoveIsCastle(move)) text += "c";
        return text;
    }

    // Errors in the fields every generated move should share: the piece-type field is the piece on from, and the
    // castling rights and en passant before the move are the state's.
    string MoveFieldErrors(byte[] board, int[] state, int move) {
        string errors = "";
        if (ChessRules.MovePieceType(move) != ChessRules.PieceTypeOf(board[ChessRules.MoveFrom(move)])) {
            errors += " " + MoveText(move) + ":type " + ChessRules.MovePieceType(move);
        }
        if (ChessRules.MovePrevCastlingRights(move) != state[ChessPosition.StateCastlingRights]) {
            errors += " " + MoveText(move) + ":rights " + ChessRules.MovePrevCastlingRights(move);
        }
        if (ChessRules.MovePrevEnPassantFile(move) != state[ChessPosition.StateEnPassantFile]) {
            errors += " " + MoveText(move) + ":en passant " + ChessRules.MovePrevEnPassantFile(move);
        }
        return errors;
    }

    // Generates the moves of the piece on from and compares them, in any order, with expected: MoveText strings
    // separated by spaces, "" for none.
    void CheckMoves(string name, byte[] board, int[] state, int from, string expected) {
        int[] moves = new int[ChessMoves.MaxMoves];
        byte[] b = CopyBoard(board);
        int[] s = CopyState(state);
        int count = ChessMoves.GeneratePieceMoves(b, s, from, moves, 0);
        string errors = BoardDiff(b, board) + StateDiff(s, state);
        if (errors != "") errors = " changed the position:" + errors;
        errors += CompareMoves(board, state, moves, count, expected);
        Check(errors == "", "move generation: " + name, errors);
    }

    // Errors comparing moves[0..count), in any order, with expected: MoveText strings separated by spaces, "" for
    // none. Also checks each move's shared fields.
    string CompareMoves(byte[] board, int[] state, int[] moves, int count, string expected) {
        string errors = "";
        string[] wanted = expected == "" ? new string[0] : expected.Split(new char[] { ' ' });
        bool[] found = new bool[wanted.Length];
        string extra = "";
        for (int i = 0; i < count; i++) {
            string text = MoveText(moves[i]);
            bool match = false;
            for (int j = 0; j < wanted.Length; j++) {
                if (!found[j] && wanted[j] == text) {
                    found[j] = true;
                    match = true;
                    break;
                }
            }
            if (!match) extra += " " + text;
            errors += MoveFieldErrors(board, state, moves[i]);
        }
        string missing = "";
        for (int j = 0; j < wanted.Length; j++) {
            if (!found[j]) missing += " " + wanted[j];
        }
        if (extra != "") errors += " unexpected:" + extra;
        if (missing != "") errors += " missing:" + missing;
        return errors;
    }

    // A board from the piece placement part of a FEN (test helper only; FEN parsing proper is step 3).
    byte[] Placement(string placement) {
        byte[] board = new byte[64];
        int rank = 7;
        int file = 0;
        for (int i = 0; i < placement.Length; i++) {
            char c = placement[i];
            if (c == '/') {
                rank--;
                file = 0;
            } else if (c >= '1' && c <= '8') {
                file += c - '0';
            } else {
                int color = char.IsUpper(c) ? ChessRules.White : ChessRules.Black;
                board[ChessRules.SquareOf(file, rank)] = ChessRules.PieceOf(color, ChessRules.PieceTypeFromLetter(char.ToUpper(c)));
                file++;
            }
        }
        return board;
    }

    int FindPiece(byte[] board, byte piece) {
        for (int sq = 0; sq < 64; sq++) {
            if (board[sq] == piece) return sq;
        }
        return ChessRules.NoSquare;
    }

    // Generates all moves for the side to move and compares the count with the Java's (prohibitSelfCheck = false,
    // on port-reference). Also checks each move's shared fields, and that make followed by unmake restores the
    // position for every move.
    void CheckMoveCount(string name, string placement, int sideToMove, int castlingRights, int enPassantFile,
        int expected) {
        byte[] board = Placement(placement);
        int[] state = NewState(sideToMove, castlingRights, enPassantFile,
            FindPiece(board, ChessRules.WhiteKing), FindPiece(board, ChessRules.BlackKing));
        int[] moves = new int[ChessMoves.MaxMoves];
        int count = ChessMoves.GenerateMoves(board, state, moves);
        string errors = count == expected ? "" : " count " + count + " (expected " + expected + ")";
        byte[] b = CopyBoard(board);
        int[] s = CopyState(state);
        for (int i = 0; i < count; i++) {
            errors += MoveFieldErrors(board, state, moves[i]);
            ChessPosition.MakeMove(b, s, moves[i]);
            ChessPosition.UnmakeMove(b, s, moves[i]);
            string diff = BoardDiff(b, board) + StateDiff(s, state);
            if (diff != "") {
                errors += " " + MoveText(moves[i]) + " make/unmake:" + diff;
                b = CopyBoard(board);
                s = CopyState(state);
            }
        }
        Check(errors == "", "move count: " + name, errors);
    }

    void TestMoveGeneration() {
        int white = ChessRules.White;
        int black = ChessRules.Black;
        int none = ChessRules.NoSquare;
        int all = ChessRules.CastleAll;
        int wk = ChessRules.CastleWhiteKingSide;
        int wq = ChessRules.CastleWhiteQueenSide;
        int bk = ChessRules.CastleBlackKingSide;
        int bq = ChessRules.CastleBlackQueenSide;
        int[] whiteToMove = NewState(white, 0, -1, none, none);
        int[] blackToMove = NewState(black, 0, -1, none, none);

        // Knights: an own piece blocks, an enemy piece is captured; no wrapping round the board's edges.
        byte[] b = EmptyBoard();
        b[ChessRules.D4] = ChessRules.WhiteKnight;
        b[ChessRules.E6] = ChessRules.WhitePawn;
        b[ChessRules.C6] = ChessRules.BlackRook;
        b[ChessRules.H4] = ChessRules.WhiteKnight;
        b[ChessRules.H8] = ChessRules.BlackKnight;
        CheckMoves("knight with blocker and capture", b, whiteToMove, ChessRules.D4,
            "d4c6xR d4b5 d4f5 d4b3 d4f3 d4c2 d4e2");
        CheckMoves("knight on the h-file", b, whiteToMove, ChessRules.H4, "h4g6 h4f5 h4f3 h4g2");
        CheckMoves("black knight in the corner", b, blackToMove, ChessRules.H8, "h8g6 h8f7");

        // Sliders: each ray stops before an own piece and at an enemy piece.
        b = EmptyBoard();
        b[ChessRules.D4] = ChessRules.WhiteBishop;
        b[ChessRules.F6] = ChessRules.WhitePawn;
        b[ChessRules.B6] = ChessRules.BlackKnight;
        CheckMoves("bishop", b, whiteToMove, ChessRules.D4, "d4e5 d4c5 d4b6xN d4e3 d4f2 d4g1 d4c3 d4b2 d4a1");

        b = EmptyBoard();
        b[ChessRules.E5] = ChessRules.BlackRook;
        b[ChessRules.E7] = ChessRules.BlackPawn;
        b[ChessRules.E2] = ChessRules.WhitePawn;
        b[ChessRules.G5] = ChessRules.WhiteQueen;
        CheckMoves("black rook", b, blackToMove, ChessRules.E5,
            "e5e6 e5e4 e5e3 e5e2xP e5d5 e5c5 e5b5 e5a5 e5f5 e5g5xQ");

        b = EmptyBoard();
        b[ChessRules.A1] = ChessRules.WhiteQueen;
        b[ChessRules.A3] = ChessRules.WhitePawn;
        b[ChessRules.E1] = ChessRules.BlackBishop;
        b[ChessRules.G7] = ChessRules.BlackPawn;
        CheckMoves("queen in the corner", b, whiteToMove, ChessRules.A1,
            "a1a2 a1b1 a1c1 a1d1 a1e1xB a1b2 a1c3 a1d4 a1e5 a1f6 a1g7xP");

        // Kings, without castling. Moving next to danger is allowed here; the legal move list filters it.
        b = EmptyBoard();
        b[ChessRules.E4] = ChessRules.WhiteKing;
        b[ChessRules.D5] = ChessRules.WhitePawn;
        b[ChessRules.F5] = ChessRules.BlackPawn;
        b[ChessRules.H8] = ChessRules.BlackKing;
        CheckMoves("king", b, whiteToMove, ChessRules.E4, "e4d3 e4e3 e4f3 e4d4 e4f4 e4e5 e4f5xP");
        CheckMoves("black king in the corner", b, blackToMove, ChessRules.H8, "h8g8 h8g7 h8h7");

        // White pawns: pushes, blocked pushes, captures (never of its own pieces, a Java bug), no wrapping round the
        // board's edges.
        b = EmptyBoard();
        b[ChessRules.E2] = ChessRules.WhitePawn;
        b[ChessRules.D2] = ChessRules.WhitePawn;
        b[ChessRules.D4] = ChessRules.BlackKnight;
        b[ChessRules.C2] = ChessRules.WhitePawn;
        b[ChessRules.C3] = ChessRules.WhiteKnight;
        b[ChessRules.F2] = ChessRules.WhitePawn;
        b[ChessRules.G3] = ChessRules.BlackKnight;
        b[ChessRules.H4] = ChessRules.WhitePawn;
        b[ChessRules.A6] = ChessRules.BlackPawn;
        b[ChessRules.A3] = ChessRules.WhitePawn;
        b[ChessRules.H3] = ChessRules.BlackKnight;
        b[ChessRules.A1] = ChessRules.WhitePawn;
        CheckMoves("white pawn single and double push", b, whiteToMove, ChessRules.E2, "e2e3 e2e4");
        CheckMoves("white pawn double push blocked, own piece not captured", b, whiteToMove, ChessRules.D2, "d2d3");
        CheckMoves("white pawn blocked", b, whiteToMove, ChessRules.C2, "");
        CheckMoves("white pawn captures", b, whiteToMove, ChessRules.F2, "f2f3 f2f4 f2g3xN");
        CheckMoves("white pawn on the h-file doesn't wrap", b, whiteToMove, ChessRules.H4, "h4h5");
        CheckMoves("white pawn on the a-file doesn't wrap", b, whiteToMove, ChessRules.A3, "a3a4");
        CheckMoves("white pawn on rank 1 has no moves", b, whiteToMove, ChessRules.A1, "");

        // Black pawns.
        b = EmptyBoard();
        b[ChessRules.C7] = ChessRules.BlackPawn;
        b[ChessRules.E7] = ChessRules.BlackPawn;
        b[ChessRules.E6] = ChessRules.WhitePawn;
        b[ChessRules.D6] = ChessRules.WhiteBishop;
        b[ChessRules.F6] = ChessRules.BlackKnight;
        b[ChessRules.A5] = ChessRules.BlackPawn;
        b[ChessRules.H3] = ChessRules.WhiteKnight;
        b[ChessRules.H6] = ChessRules.BlackPawn;
        b[ChessRules.A6] = ChessRules.WhiteKnight;
        CheckMoves("black pawn pushes and capture", b, blackToMove, ChessRules.C7, "c7c6 c7c5 c7d6xB");
        CheckMoves("black pawn blocked, captures, own piece not captured", b, blackToMove, ChessRules.E7, "e7d6xB");
        CheckMoves("black pawn on the a-file doesn't wrap", b, blackToMove, ChessRules.A5, "a5a4");
        CheckMoves("black pawn on the h-file doesn't wrap", b, blackToMove, ChessRules.H6, "h6h5");

        // En passant, from both sides of the pawn that double-pushed.
        b = EmptyBoard();
        b[ChessRules.E5] = ChessRules.WhitePawn;
        b[ChessRules.C5] = ChessRules.WhitePawn;
        b[ChessRules.B5] = ChessRules.WhitePawn;
        b[ChessRules.D5] = ChessRules.BlackPawn;
        b[ChessRules.D4] = ChessRules.BlackPawn;
        b[ChessRules.F4] = ChessRules.BlackPawn;
        b[ChessRules.E4] = ChessRules.WhitePawn;
        int[] epWhite = NewState(white, 0, ChessRules.FileD, none, none);
        int[] epBlack = NewState(black, 0, ChessRules.FileE, none, none);
        CheckMoves("white en passant from the right", b, epWhite, ChessRules.E5, "e5e6 e5d6xP");
        CheckMoves("white en passant from the left", b, epWhite, ChessRules.C5, "c5c6 c5d6xP");
        CheckMoves("white pawn not beside the en passant pawn", b, epWhite, ChessRules.B5, "b5b6");
        CheckMoves("black en passant from the left", b, epBlack, ChessRules.D4, "d4d3 d4e3xP");
        CheckMoves("black en passant from the right", b, epBlack, ChessRules.F4, "f4f3 f4e3xP");
        // Without the en passant state the same pawns can only push.
        CheckMoves("no en passant without the state", b, whiteToMove, ChessRules.E5, "e5e6");

        // Promotions, pushing and capturing, both colours.
        b = EmptyBoard();
        b[ChessRules.B7] = ChessRules.WhitePawn;
        b[ChessRules.A8] = ChessRules.BlackRook;
        b[ChessRules.C8] = ChessRules.WhiteKnight;
        b[ChessRules.G7] = ChessRules.WhitePawn;
        b[ChessRules.G8] = ChessRules.BlackBishop;
        b[ChessRules.G2] = ChessRules.BlackPawn;
        b[ChessRules.F1] = ChessRules.WhiteBishop;
        CheckMoves("white promotions", b, whiteToMove, ChessRules.B7,
            "b7b8=Q b7b8=R b7b8=B b7b8=N b7a8xR=Q b7a8xR=R b7a8xR=B b7a8xR=N");
        CheckMoves("white pawn blocked on rank 7", b, whiteToMove, ChessRules.G7, "");
        CheckMoves("black promotions", b, blackToMove, ChessRules.G2,
            "g2g1=Q g2g1=R g2g1=B g2g1=N g2f1xB=Q g2f1xB=R g2f1xB=B g2f1xB=N");

        // Castling, standard: r3k2r/8/8/8/8/8/8/R3K2R. Only the pieces are checked here, not check.
        byte[] std = EmptyBoard();
        std[ChessRules.E1] = ChessRules.WhiteKing;
        std[ChessRules.A1] = ChessRules.WhiteRook;
        std[ChessRules.H1] = ChessRules.WhiteRook;
        std[ChessRules.E8] = ChessRules.BlackKing;
        std[ChessRules.A8] = ChessRules.BlackRook;
        std[ChessRules.H8] = ChessRules.BlackRook;
        int[] stdWhite = NewState(white, all, -1, ChessRules.E1, ChessRules.E8);
        int[] stdBlack = NewState(black, all, -1, ChessRules.E1, ChessRules.E8);
        string whiteKingSteps = "e1d1 e1d2 e1e2 e1f2 e1f1";
        CheckMoves("white castles both ways", std, stdWhite, ChessRules.E1, whiteKingSteps + " e1h1c e1a1c");
        CheckMoves("black castles both ways", std, stdBlack, ChessRules.E8, "e8d8 e8d7 e8e7 e8f7 e8f8 e8h8c e8a8c");
        CheckMoves("no castling without rights", std, NewState(white, bk | bq, -1, ChessRules.E1, ChessRules.E8),
            ChessRules.E1, whiteKingSteps);
        CheckMoves("castling only on the side with the right", std, NewState(white, wq, -1, ChessRules.E1, ChessRules.E8),
            ChessRules.E1, whiteKingSteps + " e1a1c");

        b = CopyBoard(std);
        b[ChessRules.B1] = ChessRules.BlackKnight;
        CheckMoves("piece on the rook's path blocks O-O-O", b, stdWhite, ChessRules.E1, whiteKingSteps + " e1h1c");
        b = CopyBoard(std);
        b[ChessRules.G1] = ChessRules.WhiteBishop;
        CheckMoves("piece on the king's path blocks O-O", b, stdWhite, ChessRules.E1, whiteKingSteps + " e1a1c");
        // Java bugs (docs/java-bugs.md): castling with the rook gone, or past another rook or king.
        b = CopyBoard(std);
        b[ChessRules.H1] = ChessRules.Empty;
        CheckMoves("no castling with the rook gone", b, stdWhite, ChessRules.E1, whiteKingSteps + " e1a1c");
        b = CopyBoard(std);
        b[ChessRules.H1] = ChessRules.BlackRook;
        CheckMoves("no castling with an enemy rook on the rook's square", b, stdWhite, ChessRules.E1,
            whiteKingSteps + " e1a1c");
        b = CopyBoard(std);
        b[ChessRules.D1] = ChessRules.WhiteRook;
        CheckMoves("another own rook blocks O-O-O", b, stdWhite, ChessRules.E1, "e1d2 e1e2 e1f2 e1f1 e1h1c");
        b = CopyBoard(std);
        b[ChessRules.F1] = ChessRules.BlackRook;
        CheckMoves("an enemy rook blocks O-O", b, stdWhite, ChessRules.E1, "e1d1 e1d2 e1e2 e1f2 e1f1xR e1a1c");
        b = CopyBoard(std);
        b[ChessRules.C8] = ChessRules.WhiteKing;
        b[ChessRules.E1] = ChessRules.Empty;
        b[ChessRules.D8] = ChessRules.Empty;
        CheckMoves("an enemy king blocks O-O-O", b, stdBlack, ChessRules.E8, "e8d8 e8d7 e8e7 e8f7 e8f8 e8h8c");
        // A king off its starting square doesn't castle, even with the rights still set.
        b = CopyBoard(std);
        b[ChessRules.E1] = ChessRules.Empty;
        b[ChessRules.F1] = ChessRules.WhiteKing;
        CheckMoves("no castling with the king off its square", b, stdWhite, ChessRules.F1,
            "f1e1 f1e2 f1f2 f1g2 f1g1");

        // Castling, Chess960. King on c between rooks on b and g (as in 2.3b): O-O-O where the king doesn't move.
        byte[] c960 = EmptyBoard();
        c960[ChessRules.A1] = ChessRules.WhiteQueen;
        c960[ChessRules.B1] = ChessRules.WhiteRook;
        c960[ChessRules.C1] = ChessRules.WhiteKing;
        c960[ChessRules.G1] = ChessRules.WhiteRook;
        int[] c960White = NewState960(white, wk | wq, ChessRules.FileC, ChessRules.FileB, ChessRules.FileG,
            ChessRules.C1, none);
        CheckMoves("chess960 king on c", c960, c960White, ChessRules.C1, "c1d1 c1b2 c1c2 c1d2 c1g1c c1b1c");
        b = CopyBoard(c960);
        b[ChessRules.D1] = ChessRules.WhiteKnight;
        CheckMoves("chess960 piece on d1 blocks both castles", b, c960White, ChessRules.C1, "c1b2 c1c2 c1d2");

        // King on f between rooks on d and g: O-O swaps the king and rook; on O-O-O the rook doesn't move.
        byte[] swap = EmptyBoard();
        swap[ChessRules.D1] = ChessRules.WhiteRook;
        swap[ChessRules.F1] = ChessRules.WhiteKing;
        swap[ChessRules.G1] = ChessRules.WhiteRook;
        int[] swapWhite = NewState960(white, wk | wq, ChessRules.FileF, ChessRules.FileD, ChessRules.FileG,
            ChessRules.F1, none);
        CheckMoves("chess960 king on f", swap, swapWhite, ChessRules.F1, "f1e1 f1e2 f1f2 f1g2 f1g1c f1d1c");
        b = CopyBoard(swap);
        b[ChessRules.E1] = ChessRules.WhiteBishop;
        CheckMoves("chess960 piece between king and rook blocks O-O-O", b, swapWhite, ChessRules.F1,
            "f1e2 f1f2 f1g2 f1g1c");

        // King on g between rooks on b and h: O-O where the king doesn't move, and a long O-O-O.
        byte[] kg = EmptyBoard();
        kg[ChessRules.B1] = ChessRules.WhiteRook;
        kg[ChessRules.G1] = ChessRules.WhiteKing;
        kg[ChessRules.H1] = ChessRules.WhiteRook;
        int[] kgWhite = NewState960(white, wk | wq, ChessRules.FileG, ChessRules.FileB, ChessRules.FileH,
            ChessRules.G1, none);
        CheckMoves("chess960 king on g", kg, kgWhite, ChessRules.G1, "g1f1 g1f2 g1g2 g1h2 g1h1c g1b1c");
        b = CopyBoard(kg);
        b[ChessRules.F1] = ChessRules.BlackKnight;
        CheckMoves("chess960 enemy piece on f1 blocks both castles", b, kgWhite, ChessRules.G1,
            "g1f1xN g1f2 g1g2 g1h2");

        // Every move of the side to move, against the Java's counts with prohibitSelfCheck = false.
        CheckMoveCount("standard start", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR", white, all, -1, 20);
        CheckMoveCount("Kiwipete, white", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R", white, all, -1, 48);
        CheckMoveCount("Kiwipete, black", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R", black, all, -1, 43);
        CheckMoveCount("perft position 3", "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8", white, 0, -1, 16);
        CheckMoveCount("perft position 4", "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1", white, bk | bq, -1, 38);
        CheckMoveCount("perft position 5", "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R", white, wk | wq, -1, 44);
    }

    // ---- 2.4b ----

    // Compares the squares byColor attacks, over all 64, with expected: square names separated by spaces, "" for
    // none.
    void CheckAttacked(string name, byte[] board, int byColor, string expected) {
        bool[] wanted = new bool[64];
        if (expected != "") {
            string[] names = expected.Split(new char[] { ' ' });
            for (int i = 0; i < names.Length; i++) wanted[ChessRules.ParseSquare(names[i])] = true;
        }
        byte[] b = CopyBoard(board);
        string extra = "";
        string missing = "";
        for (int sq = 0; sq < 64; sq++) {
            bool attacked = ChessMoves.IsSquareAttacked(b, sq, byColor);
            if (attacked && !wanted[sq]) extra += " " + ChessRules.SquareName(sq);
            if (!attacked && wanted[sq]) missing += " " + ChessRules.SquareName(sq);
        }
        string errors = BoardDiff(b, board);
        if (errors != "") errors = " changed the board:" + errors;
        if (extra != "") errors += " unexpected:" + extra;
        if (missing != "") errors += " missing:" + missing;
        Check(errors == "", "attack check: " + name, errors);
    }

    void TestAttacks() {
        int white = ChessRules.White;
        int black = ChessRules.Black;

        // Pawns attack diagonally forward only, never straight ahead, and not round the board's edges.
        byte[] b = EmptyBoard();
        b[ChessRules.E4] = ChessRules.WhitePawn;
        b[ChessRules.A2] = ChessRules.WhitePawn;
        b[ChessRules.H7] = ChessRules.WhitePawn;
        CheckAttacked("white pawns", b, white, "d5 f5 b3 g8");
        CheckAttacked("white pawns don't attack for black", b, black, "");

        b = EmptyBoard();
        b[ChessRules.E5] = ChessRules.BlackPawn;
        b[ChessRules.H7] = ChessRules.BlackPawn;
        b[ChessRules.A2] = ChessRules.BlackPawn;
        CheckAttacked("black pawns", b, black, "d4 f4 g6 b1");
        CheckAttacked("black pawns don't attack for white", b, white, "");

        // Knights jump over pieces in the way, and don't wrap round the board's edges.
        b = EmptyBoard();
        b[ChessRules.D4] = ChessRules.WhiteKnight;
        b[ChessRules.D5] = ChessRules.BlackPawn;
        b[ChessRules.E4] = ChessRules.BlackPawn;
        b[ChessRules.C4] = ChessRules.BlackPawn;
        b[ChessRules.D3] = ChessRules.BlackPawn;
        b[ChessRules.H1] = ChessRules.WhiteKnight;
        CheckAttacked("white knights", b, white, "c6 e6 b5 f5 b3 f3 c2 e2 g3 f2");
        b = EmptyBoard();
        b[ChessRules.A8] = ChessRules.BlackKnight;
        CheckAttacked("black knight in the corner", b, black, "b6 c7");

        // Sliders: each line stops at the first piece of either colour, which is itself attacked.
        b = EmptyBoard();
        b[ChessRules.D4] = ChessRules.WhiteBishop;
        b[ChessRules.F6] = ChessRules.WhitePawn;
        b[ChessRules.B2] = ChessRules.BlackKnight;
        CheckAttacked("bishop blocked by both colours", b, white, "e5 f6 c5 b6 a7 e3 f2 g1 c3 b2 e7 g7");

        b = EmptyBoard();
        b[ChessRules.E5] = ChessRules.BlackRook;
        b[ChessRules.E7] = ChessRules.BlackPawn;
        b[ChessRules.E2] = ChessRules.WhitePawn;
        CheckAttacked("black rook blocked by both colours", b, black,
            "e6 e7 e4 e3 e2 a5 b5 c5 d5 f5 g5 h5 d6 f6");

        b = EmptyBoard();
        b[ChessRules.A1] = ChessRules.WhiteQueen;
        b[ChessRules.A5] = ChessRules.BlackPawn;
        b[ChessRules.D4] = ChessRules.BlackBishop;
        CheckAttacked("queen in the corner, blocked", b, white, "a2 a3 a4 a5 b1 c1 d1 e1 f1 g1 h1 b2 c3 d4");

        b = EmptyBoard();
        b[ChessRules.D4] = ChessRules.BlackQueen;
        b[ChessRules.D6] = ChessRules.WhiteKing;
        b[ChessRules.F2] = ChessRules.WhiteKnight;
        CheckAttacked("black queen in the middle, blocked", b, black,
            "d5 d6 d3 d2 d1 a4 b4 c4 e4 f4 g4 h4 e5 f6 g7 h8 c5 b6 a7 e3 f2 c3 b2 a1");

        // Kings: one step each way.
        b = EmptyBoard();
        b[ChessRules.E4] = ChessRules.WhiteKing;
        CheckAttacked("white king", b, white, "d3 e3 f3 d4 f4 d5 e5 f5");
        b = EmptyBoard();
        b[ChessRules.H8] = ChessRules.BlackKing;
        CheckAttacked("black king in the corner", b, black, "g8 g7 h7");

        // A piece of the attacking colour standing in the way blocks its own slider too: rook a1, own knight a3
        // (whose own attacks are b5 c4 c2 b1), and bishop b1 behind its own pawn c2 (which attacks b3 d3), so e4
        // and beyond aren't attacked.
        b = EmptyBoard();
        b[ChessRules.A1] = ChessRules.WhiteRook;
        b[ChessRules.A3] = ChessRules.WhiteKnight;
        b[ChessRules.B1] = ChessRules.WhiteBishop;
        b[ChessRules.C2] = ChessRules.WhitePawn;
        CheckAttacked("sliders blocked by their own pieces", b, white, "a2 a3 b1 b5 c4 c2 b3 d3");
    }

    // ---- 2.5 ----

    // State for a placement, with the kings' squares found on the board.
    int[] PlacementState(byte[] board, int sideToMove, int castlingRights, int enPassantFile) {
        return NewState(sideToMove, castlingRights, enPassantFile,
            FindPiece(board, ChessRules.WhiteKing), FindPiece(board, ChessRules.BlackKing));
    }

    // Compares the legal moves of the piece on from with expectedLegal (prohibitSelfCheck on) and expectedAll
    // (prohibitSelfCheck off), in any order, as in CheckMoves.
    void CheckLegalMoves(string name, byte[] board, int[] state, int from, string expectedLegal, string expectedAll) {
        int[] moves = new int[ChessMoves.MaxMoves];
        byte[] b = CopyBoard(board);
        int[] s = CopyState(state);
        int count = ChessMoves.GenerateLegalPieceMoves(b, s, from, moves, 0, true);
        string errors = BoardDiff(b, board) + StateDiff(s, state);
        if (errors != "") errors = " changed the position:" + errors;
        errors += CompareMoves(board, state, moves, count, expectedLegal);
        Check(errors == "", "legal moves: " + name, errors);

        count = ChessMoves.GenerateLegalPieceMoves(b, s, from, moves, 0, false);
        errors = BoardDiff(b, board) + StateDiff(s, state);
        if (errors != "") errors = " changed the position:" + errors;
        errors += CompareMoves(board, state, moves, count, expectedAll);
        Check(errors == "", "legal moves, prohibitSelfCheck off: " + name, errors);
    }

    // Compares all legal moves of the side to move with expected, and checks that with prohibitSelfCheck off the
    // list is the pseudo-legal one.
    void CheckLegalSide(string name, string placement, int sideToMove, int castlingRights, int enPassantFile,
        string expected) {
        byte[] board = Placement(placement);
        int[] state = PlacementState(board, sideToMove, castlingRights, enPassantFile);
        int[] moves = new int[ChessMoves.MaxMoves];
        byte[] b = CopyBoard(board);
        int[] s = CopyState(state);
        int count = ChessMoves.GenerateLegalMoves(b, s, moves, true);
        string errors = BoardDiff(b, board) + StateDiff(s, state);
        if (errors != "") errors = " changed the position:" + errors;
        errors += CompareMoves(board, state, moves, count, expected);
        int all = ChessMoves.GenerateLegalMoves(b, s, moves, false);
        int pseudo = ChessMoves.GenerateMoves(b, s, moves);
        if (all != pseudo) errors += " prohibitSelfCheck off gave " + all + " moves, pseudo-legal " + pseudo;
        Check(errors == "", "legal moves: " + name, errors);
    }

    // Compares the number of legal moves for the side to move with expectedLegal (prohibitSelfCheck on) and
    // expectedAll (off).
    void CheckLegalCount(string name, string placement, int sideToMove, int castlingRights, int enPassantFile,
        int expectedLegal, int expectedAll) {
        byte[] board = Placement(placement);
        int[] state = PlacementState(board, sideToMove, castlingRights, enPassantFile);
        int[] moves = new int[ChessMoves.MaxMoves];
        byte[] b = CopyBoard(board);
        int[] s = CopyState(state);
        int legal = ChessMoves.GenerateLegalMoves(b, s, moves, true);
        int all = ChessMoves.GenerateLegalMoves(b, s, moves, false);
        string errors = BoardDiff(b, board) + StateDiff(s, state);
        if (errors != "") errors = " changed the position:" + errors;
        if (legal != expectedLegal) errors += " legal " + legal + " (expected " + expectedLegal + ")";
        if (all != expectedAll) errors += " prohibitSelfCheck off " + all + " (expected " + expectedAll + ")";
        Check(errors == "", "legal move count: " + name, errors);
    }

    void CheckInCheck(string name, string placement, int color, bool expected) {
        byte[] board = Placement(placement);
        int[] state = PlacementState(board, ChessRules.White, 0, -1);
        bool inCheck = ChessMoves.IsInCheck(board, state, color);
        Check(inCheck == expected, "in check: " + name, "got " + inCheck);
    }

    void TestLegalMoves() {
        int white = ChessRules.White;
        int black = ChessRules.Black;
        int all = ChessRules.CastleAll;
        int wk = ChessRules.CastleWhiteKingSide;
        int wq = ChessRules.CastleWhiteQueenSide;
        int bk = ChessRules.CastleBlackKingSide;
        int bq = ChessRules.CastleBlackQueenSide;

        // Whole-side counts: published perft depth 1 with prohibitSelfCheck on, the pseudo-legal counts (2.4a) off.
        CheckLegalCount("standard start", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR", white, all, -1, 20, 20);
        CheckLegalCount("standard start, black", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR", black, all, -1, 20, 20);
        CheckLegalCount("Kiwipete", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R", white, all, -1, 48, 48);
        CheckLegalCount("perft position 3", "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8", white, 0, -1, 14, 16);
        CheckLegalCount("perft position 4", "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1", white, bk | bq, -1,
            6, 38);
        CheckLegalCount("perft position 5", "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R", white, wk | wq, -1, 44, 44);

        CheckInCheck("rook on the file", "4r2k/8/8/8/8/8/8/4K3", white, true);
        CheckInCheck("rook blocked", "4r2k/8/8/8/4n3/8/8/4K3", white, false);
        CheckInCheck("black king by a pawn", "8/8/8/8/8/3k4/4P3/7K", black, true);
        CheckInCheck("no king", "8/8/8/8/8/8/8/4r3", white, false);

        // Pinned pieces: a knight pinned on the file has no moves; a bishop pinned on the diagonal can still move
        // along it, up to capturing the pinning piece.
        byte[] b = Placement("k3r3/8/8/8/8/8/4N3/4K3");
        int[] s = PlacementState(b, white, 0, -1);
        CheckLegalMoves("pinned knight", b, s, ChessRules.E2, "", "e2c1 e2c3 e2d4 e2f4 e2g3 e2g1");
        b = Placement("8/6b1/8/8/8/2B5/8/K6k");
        s = PlacementState(b, white, 0, -1);
        CheckLegalMoves("pinned bishop moves along the pin", b, s, ChessRules.C3, "c3b2 c3d4 c3e5 c3f6 c3g7xB",
            "c3b2 c3d4 c3e5 c3f6 c3g7xB c3b4 c3a5 c3d2 c3e1");

        // The king can't move into check, or capture a defended piece. Here it is in check from the pawn on f2
        // (defended by the knight), and the rook covers the d-file.
        b = Placement("k2r4/8/8/8/6n1/8/5p2/4K3");
        s = PlacementState(b, white, 0, -1);
        CheckLegalMoves("king doesn't move into check", b, s, ChessRules.E1, "e1e2 e1f1", "e1d1 e1d2 e1e2 e1f2xP e1f1");

        // In check from a rook: the king can't step back along the rook's line (e2), and the other pieces may only
        // block (Ra2-e2, Bb5-e2) or capture the rook (Bxe8).
        CheckLegalSide("answering a check", "4r2k/8/8/1B6/8/8/R7/4K3", white, 0, -1,
            "e1d1 e1d2 e1f1 e1f2 a2e2 b5e2 b5e8xR");

        // En passant that would take both pawns off the king's rank, exposing the king to the rook.
        b = Placement("7k/8/8/KPp4r/8/8/8/8");
        s = PlacementState(b, white, 0, ChessRules.FileC);
        CheckLegalMoves("en passant exposing the king", b, s, ChessRules.B5, "b5b6", "b5b6 b5c6xP");

        // Castling out of, through and into check, with prohibitSelfCheck on and off. b1 being attacked doesn't stop
        // O-O-O: only the king's squares (e1, d1, c1) count, not the rook's.
        int castleRights = wk | wq;
        b = Placement("k7/8/8/4r3/8/8/8/R3K2R");
        s = PlacementState(b, white, castleRights, -1);
        CheckLegalMoves("no castling out of check", b, s, ChessRules.E1, "e1d1 e1d2 e1f2 e1f1",
            "e1d1 e1d2 e1e2 e1f2 e1f1 e1h1c e1a1c");
        b = Placement("k7/8/8/5r2/8/8/8/R3K2R");
        s = PlacementState(b, white, castleRights, -1);
        CheckLegalMoves("no O-O through check", b, s, ChessRules.E1, "e1d1 e1d2 e1e2 e1a1c",
            "e1d1 e1d2 e1e2 e1f2 e1f1 e1h1c e1a1c");
        b = Placement("k7/8/8/6r1/8/8/8/R3K2R");
        s = PlacementState(b, white, castleRights, -1);
        CheckLegalMoves("no O-O into check", b, s, ChessRules.E1, "e1d1 e1d2 e1e2 e1f2 e1f1 e1a1c",
            "e1d1 e1d2 e1e2 e1f2 e1f1 e1h1c e1a1c");
        b = Placement("k7/8/8/3r4/8/8/8/R3K2R");
        s = PlacementState(b, white, castleRights, -1);
        CheckLegalMoves("no O-O-O through check", b, s, ChessRules.E1, "e1e2 e1f2 e1f1 e1h1c",
            "e1d1 e1d2 e1e2 e1f2 e1f1 e1h1c e1a1c");
        b = Placement("k7/8/8/1r6/8/8/8/R3K2R");
        s = PlacementState(b, white, castleRights, -1);
        CheckLegalMoves("O-O-O with b1 attacked", b, s, ChessRules.E1, "e1d1 e1d2 e1e2 e1f2 e1f1 e1h1c e1a1c",
            "e1d1 e1d2 e1e2 e1f2 e1f1 e1h1c e1a1c");
        b = Placement("r3k2r/8/8/8/5R2/8/8/K7");
        s = PlacementState(b, black, bk | bq, -1);
        CheckLegalMoves("black: no O-O through check", b, s, ChessRules.E8, "e8d8 e8d7 e8e7 e8a8c",
            "e8d8 e8d7 e8e7 e8f7 e8f8 e8h8c e8a8c");

        // Chess960, king on c1 between rooks on b1 and g1, d-file attacked: O-O-O leaves the king on c1 and puts the
        // rook on the attacked d1, which is allowed; O-O takes the king through d1, which isn't.
        b = Placement("k2r4/8/8/8/8/8/8/1RK3R1");
        s = NewState960(white, castleRights, ChessRules.FileC, ChessRules.FileB, ChessRules.FileG,
            ChessRules.C1, ChessRules.A8);
        CheckLegalMoves("chess960 castling with the d-file attacked", b, s, ChessRules.C1, "c1b2 c1c2 c1b1c",
            "c1b2 c1c2 c1d2 c1d1 c1b1c c1g1c");
    }

    // ---- 2.6 ----

    // Counts the legal move paths of the given depth (perft). buffers[d] is the move list for depth d, so each level
    // keeps its own. The position is left as it was.
    [RecursiveMethod]
    int Perft(byte[] board, int[] state, int depth, bool prohibitSelfCheck, int[][] buffers) {
        int[] moves = buffers[depth];
        int count = ChessMoves.GenerateLegalMoves(board, state, moves, prohibitSelfCheck);
        if (depth == 1) return count;
        int nodes = 0;
        for (int i = 0; i < count; i++) {
            ChessPosition.MakeMove(board, state, moves[i]);
            nodes += Perft(board, state, depth - 1, prohibitSelfCheck, buffers);
            ChessPosition.UnmakeMove(board, state, moves[i]);
        }
        return nodes;
    }

    // Perft to each depth up to expected.Length, against expected (from the Java on port-reference, or published).
    // Below the root, every root move is made, its subtree searched and the move unmade, and both arrays must then
    // be exactly as they were.
    void CheckPerft(string name, byte[] board, int[] state, bool prohibitSelfCheck, int[] expected) {
        int maxDepth = expected.Length;
        int[][] buffers = new int[maxDepth + 1][];
        for (int d = 1; d <= maxDepth; d++) buffers[d] = new int[ChessMoves.MaxMoves];
        byte[] b = CopyBoard(board);
        int[] s = CopyState(state);
        string errors = "";
        string counts = "";
        float startTime = Time.realtimeSinceStartup;
        for (int depth = 1; depth <= maxDepth; depth++) {
            int nodes;
            if (depth == 1) {
                nodes = ChessMoves.GenerateLegalMoves(b, s, buffers[1], prohibitSelfCheck);
            } else {
                // The root level here rather than in Perft, so each root move's undo can be checked.
                int[] rootMoves = new int[ChessMoves.MaxMoves];
                int count = ChessMoves.GenerateLegalMoves(b, s, rootMoves, prohibitSelfCheck);
                nodes = 0;
                for (int i = 0; i < count; i++) {
                    ChessPosition.MakeMove(b, s, rootMoves[i]);
                    nodes += Perft(b, s, depth - 1, prohibitSelfCheck, buffers);
                    ChessPosition.UnmakeMove(b, s, rootMoves[i]);
                    string diff = BoardDiff(b, board) + StateDiff(s, state);
                    if (diff != "") {
                        errors += " depth " + depth + " " + MoveText(rootMoves[i]) + " make/unmake:" + diff;
                        b = CopyBoard(board);
                        s = CopyState(state);
                    }
                }
            }
            counts += " " + nodes;
            if (nodes != expected[depth - 1]) {
                errors += " depth " + depth + ": " + nodes + " (expected " + expected[depth - 1] + ")";
            }
        }
        string diffEnd = BoardDiff(b, board) + StateDiff(s, state);
        if (diffEnd != "") errors += " position changed:" + diffEnd;
        int ms = (int)((Time.realtimeSinceStartup - startTime) * 1000f);
        Check(errors == "", "perft: " + name + (prohibitSelfCheck ? "" : ", prohibitSelfCheck off") + " (" + counts.Trim()
            + " in " + ms + " ms)", errors);
    }

    // The board seen from the other side: ranks flipped and colours swapped.
    byte[] MirrorBoard(byte[] board) {
        byte[] mirror = new byte[64];
        for (int sq = 0; sq < 64; sq++) {
            if (board[sq] != ChessRules.Empty) mirror[sq ^ 56] = (byte)(board[sq] ^ 8);
        }
        return mirror;
    }

    // The legal move whose MoveText is text, or -1 if there is none.
    int FindLegal(byte[] board, int[] state, string text) {
        int[] moves = new int[ChessMoves.MaxMoves];
        int count = ChessMoves.GenerateLegalMoves(board, state, moves, true);
        for (int i = 0; i < count; i++) {
            if (MoveText(moves[i]) == text) return moves[i];
        }
        return -1;
    }

    // Errors if the side to move's legal moves don't include each of present, or do include any of absent (MoveText
    // strings separated by spaces, "" for none).
    string LegalListErrors(byte[] board, int[] state, string present, string absent) {
        string errors = "";
        if (present != "") {
            string[] names = present.Split(new char[] { ' ' });
            for (int i = 0; i < names.Length; i++) {
                if (FindLegal(board, state, names[i]) == -1) errors += " missing " + names[i];
            }
        }
        if (absent != "") {
            string[] names = absent.Split(new char[] { ' ' });
            for (int i = 0; i < names.Length; i++) {
                if (FindLegal(board, state, names[i]) != -1) errors += " unexpected " + names[i];
            }
        }
        return errors;
    }

    // Plays moves (MoveText strings separated by spaces) from the position, each taken from the legal move list.
    // After each ply the castling rights must be expectedRights[ply], and the next side's legal moves must include
    // present[ply] and leave out absent[ply]. Then undoes every ply in reverse, checking each restores the position
    // before it exactly.
    void CheckLine(string name, byte[] board, int[] state, string line, int[] expectedRights, string[] present,
        string[] absent) {
        string[] texts = line.Split(new char[] { ' ' });
        int count = texts.Length;
        int[] moves = new int[count];
        byte[][] boardsBefore = new byte[count][];
        int[][] statesBefore = new int[count][];
        byte[] b = CopyBoard(board);
        int[] s = CopyState(state);
        string errors = "";
        int played = 0;
        for (int i = 0; i < count; i++) {
            int move = FindLegal(b, s, texts[i]);
            if (move == -1) {
                errors += " ply " + (i + 1) + " " + texts[i] + " not legal";
                break;
            }
            boardsBefore[i] = CopyBoard(b);
            statesBefore[i] = CopyState(s);
            moves[i] = move;
            ChessPosition.MakeMove(b, s, move);
            played++;
            int rights = s[ChessPosition.StateCastlingRights];
            if (rights != expectedRights[i]) {
                errors += " ply " + (i + 1) + " rights " + rights + " (expected " + expectedRights[i] + ")";
            }
            string listErrors = LegalListErrors(b, s, present[i], absent[i]);
            if (listErrors != "") errors += " after ply " + (i + 1) + ":" + listErrors;
        }
        for (int i = played - 1; i >= 0; i--) {
            ChessPosition.UnmakeMove(b, s, moves[i]);
            string diff = BoardDiff(b, boardsBefore[i]) + StateDiff(s, statesBefore[i]);
            if (diff != "") errors += " undo ply " + (i + 1) + ":" + diff;
        }
        Check(errors == "", "line: " + name, errors);
    }

    void TestPositions() {
        int white = ChessRules.White;
        int black = ChessRules.Black;
        int all = ChessRules.CastleAll;
        int wk = ChessRules.CastleWhiteKingSide;
        int wq = ChessRules.CastleWhiteQueenSide;
        int bk = ChessRules.CastleBlackKingSide;
        int bq = ChessRules.CastleBlackQueenSide;

        // The standard start, built with the constants. Published perft 20, 400, 8902.
        byte[] start = StartBoard();
        int[] startState = NewState(white, all, -1, ChessRules.E1, ChessRules.E8);
        CheckPerft("standard start", start, startState, true, new int[] { 20, 400, 8902 });

        // Castling both ways for both sides, en passant and promotion (push and capture of a rook on its starting
        // square) all available: r3k2r/1P6/8/3pP3/8/8/8/R3K2R w KQkq d6. Counts from the Java on port-reference.
        byte[] mixed = EmptyBoard();
        mixed[ChessRules.E1] = ChessRules.WhiteKing;
        mixed[ChessRules.A1] = ChessRules.WhiteRook;
        mixed[ChessRules.H1] = ChessRules.WhiteRook;
        mixed[ChessRules.E5] = ChessRules.WhitePawn;
        mixed[ChessRules.B7] = ChessRules.WhitePawn;
        mixed[ChessRules.D5] = ChessRules.BlackPawn;
        mixed[ChessRules.E8] = ChessRules.BlackKing;
        mixed[ChessRules.A8] = ChessRules.BlackRook;
        mixed[ChessRules.H8] = ChessRules.BlackRook;
        int[] mixedState = NewState(white, all, ChessRules.FileD, ChessRules.E1, ChessRules.E8);
        CheckPerft("castling, en passant and promotion", mixed, mixedState, true, new int[] { 36, 719, 21611 });
        CheckPerft("castling, en passant and promotion", mixed, mixedState, false, new int[] { 36, 852 });
        // The same position from Black's side (r3k2r/8/8/8/3Pp3/8/1p6/R3K2R b KQkq d3) gives the same counts.
        byte[] mirrored = MirrorBoard(mixed);
        int[] mirroredState = NewState(black, all, ChessRules.FileD, ChessRules.E1, ChessRules.E8);
        CheckPerft("castling, en passant and promotion, mirrored", mirrored, mirroredState, true,
            new int[] { 36, 719, 21611 });
        CheckPerft("castling, en passant and promotion, mirrored", mirrored, mirroredState, false, new int[] { 36, 852 });

        // Published perft positions, shallow (the deep runs need FEN and come in step 3).
        byte[] b = Placement("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R");
        CheckPerft("Kiwipete", b, PlacementState(b, white, all, -1), true, new int[] { 48, 2039 });
        b = Placement("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8");
        CheckPerft("perft position 3", b, PlacementState(b, white, 0, -1), true, new int[] { 14, 191, 2812 });
        b = Placement("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1");
        CheckPerft("perft position 4", b, PlacementState(b, white, bk | bq, -1), true, new int[] { 6, 264, 9467 });
        b = Placement("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R");
        CheckPerft("perft position 5", b, PlacementState(b, white, wk | wq, -1), true, new int[] { 44, 1486 });
        b = Placement("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1");
        CheckPerft("perft position 6", b, PlacementState(b, white, 0, -1), true, new int[] { 46, 2079 });

        // Java bugs (docs/java-bugs.md), through the legal move list.

        // Capturing a rook on its starting square removes that side's castling right: after bxa8=Q Black can't
        // O-O-O (and must answer the check), and after Bxh8 Black can't O-O, but can still O-O-O.
        CheckLine("bxa8=Q+ removes black's O-O-O", mixed, mixedState, "b7a8xR=Q",
            new int[] { wk | wq | bk }, new string[] { "e8e7" }, new string[] { "e8a8c e8h8c" });
        b = Placement("r3k2r/8/8/8/8/2B5/8/4K3");
        CheckLine("Bxh8 removes black's O-O", b, PlacementState(b, white, bk | bq, -1), "c3h8xR",
            new int[] { bq }, new string[] { "e8a8c" }, new string[] { "e8h8c" });

        // Castling is refused when the rook is gone (even with the right still set), or another piece is in the way.
        b = Placement("4k3/8/8/8/8/8/8/R3K3");
        int[] s = PlacementState(b, white, wk | wq, -1);
        CheckLegalMoves("no O-O with the rook gone", b, s, ChessRules.E1, "e1d1 e1d2 e1e2 e1f2 e1f1 e1a1c",
            "e1d1 e1d2 e1e2 e1f2 e1f1 e1a1c");
        b = Placement("4k3/8/8/8/8/8/8/R2RK2R");
        s = PlacementState(b, white, wk | wq, -1);
        CheckLegalMoves("no O-O-O with another own rook in the way", b, s, ChessRules.E1, "e1d2 e1e2 e1f2 e1f1 e1h1c",
            "e1d2 e1e2 e1f2 e1f1 e1h1c");
        b = Placement("4k3/8/8/8/8/8/8/R3KN1R");
        s = PlacementState(b, white, wk | wq, -1);
        CheckLegalMoves("no O-O with a knight in the way", b, s, ChessRules.E1, "e1d1 e1d2 e1e2 e1f2 e1a1c",
            "e1d1 e1d2 e1e2 e1f2 e1a1c");

        // Castling removes both of that side's rights. A line from the mixed position: 1.exd6 e.p. O-O (Black loses
        // k and q; O-O-O was refused anyway, the b7 pawn covering c8) 2.bxa8=Q Rxa8 (nothing more to lose; O-O was
        // refused while the f8 rook covered f1) 3.O-O-O (White loses K and Q).
        CheckLine("1.exd6 e.p. O-O 2.bxa8=Q Rxa8 3.O-O-O", mixed, mixedState,
            "e5d6xP e8h8c b7a8xR=Q f8a8xQ e1a1c",
            new int[] { all, wk | wq, wk | wq, wk | wq, 0 },
            new string[] { "e8h8c", "e1a1c", "", "e1h1c e1a1c", "" },
            new string[] { "e8a8c", "e1h1c", "", "", "" });

        // Pawns can't capture their own pieces (the Java's d2xc3 and d2xe3 here).
        b = Placement("4k3/8/8/8/8/2N1B3/3P4/4K3");
        s = PlacementState(b, white, 0, -1);
        CheckLegalMoves("pawn doesn't capture its own pieces", b, s, ChessRules.D2, "d2d3 d2d4", "d2d3 d2d4");
        b = Placement("4k3/3p4/2n1b3/8/8/8/8/4K3");
        s = PlacementState(b, black, 0, -1);
        CheckLegalMoves("black pawn doesn't capture its own pieces", b, s, ChessRules.D7, "d7d6 d7d5", "d7d6 d7d5");
    }
}
