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
}
