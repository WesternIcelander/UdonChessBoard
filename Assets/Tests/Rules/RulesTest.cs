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
}
