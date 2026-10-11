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
}
