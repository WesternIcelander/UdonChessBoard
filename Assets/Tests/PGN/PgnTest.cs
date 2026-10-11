using SiggiChessBoard;
using UdonSharp;
using UnityEngine;

// Step 5 test behaviour (PLAN.md section 5): PGN export and import, with no world objects.
// Logs "[PgnTest] PASS: <check>" or "[PgnTest] FAIL: <check>: <details>" per check, then a summary line.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PgnTest : UdonSharpBehaviour {
    int passed;
    int failed;

    void Start() {
        TestTags();
        LogSummary();
    }

    void LogSummary() {
        Debug.Log("[PgnTest] done: " + passed + " passed, " + failed + " failed"
            + (failed == 0 ? " ALL PASS" : " SOME FAIL"));
    }

    void Check(bool ok, string name, string details) {
        if (ok) {
            passed++;
            Debug.Log("[PgnTest] PASS: " + name);
        } else {
            failed++;
            Debug.Log("[PgnTest] FAIL: " + name + ": " + details);
        }
    }

    void CheckText(string actual, string expected, string name) {
        Check(actual == expected, name, "expected\n" + expected + "\ngot\n" + actual);
    }

    // ---- 5.1a ----

    void TestTags() {
        string gameId = "20261005143027-048213";

        // A standard game from the standard start, with a clock without increment, and lookalike names (Cyrillic
        // i, U+01C3 in place of "!"). The standard start's FEN gives no SetUp/FEN tags.
        CheckText(ChessPgn.WriteTags("Casual game", "VRChat", "2026.10.05", "Siggі", "Nukaǃ",
                ChessGame.EndingCheckmate, ChessRules.Black, ChessGame.ModeStandard, ChessFen.StartFen, 600, 0,
                gameId, ""),
            "[Event \"Casual game\"]\n"
            + "[Site \"VRChat\"]\n"
            + "[Date \"2026.10.05\"]\n"
            + "[Round \"-\"]\n"
            + "[White \"Siggі\"]\n"
            + "[Black \"Nukaǃ\"]\n"
            + "[Result \"0-1\"]\n"
            + "[TimeControl \"600\"]\n"
            + "[GameID \"20261005143027-048213\"]\n"
            + "[Termination \"checkmate\"]\n",
            "tags: standard game");

        // Chess960 with Shredder castling letters, a clock with increment, drawn by stalemate.
        CheckText(ChessPgn.WriteTags("Casual game", "VRChat", "2026.10.05", "White", "Black",
                ChessGame.EndingStalemate, ChessRules.NoColor, ChessGame.ModeChess960,
                "qrkbbnrn/pppppppp/8/8/8/8/PPPPPPPP/QRKBBNRN w GBgb - 0 1", 300, 3, gameId, ""),
            "[Event \"Casual game\"]\n"
            + "[Site \"VRChat\"]\n"
            + "[Date \"2026.10.05\"]\n"
            + "[Round \"-\"]\n"
            + "[White \"White\"]\n"
            + "[Black \"Black\"]\n"
            + "[Result \"1/2-1/2\"]\n"
            + "[Variant \"Chess960\"]\n"
            + "[SetUp \"1\"]\n"
            + "[FEN \"qrkbbnrn/pppppppp/8/8/8/8/PPPPPPPP/QRKBBNRN w GBgb - 0 1\"]\n"
            + "[TimeControl \"300+3\"]\n"
            + "[GameID \"20261005143027-048213\"]\n"
            + "[Termination \"stalemate\"]\n",
            "tags: Chess960 game");

        // Chess960 position 518 is the standard start, and still gets its FEN, which Chess960 readers need.
        CheckText(ChessPgn.WriteTags("Casual game", "VRChat", "2026.10.05", "White", "Black",
                ChessGame.EndingResignation, ChessRules.White, ChessGame.ModeChess960, ChessFen.StartFen, 60, 1,
                gameId, ""),
            "[Event \"Casual game\"]\n"
            + "[Site \"VRChat\"]\n"
            + "[Date \"2026.10.05\"]\n"
            + "[Round \"-\"]\n"
            + "[White \"White\"]\n"
            + "[Black \"Black\"]\n"
            + "[Result \"1-0\"]\n"
            + "[Variant \"Chess960\"]\n"
            + "[SetUp \"1\"]\n"
            + "[FEN \"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1\"]\n"
            + "[TimeControl \"60+1\"]\n"
            + "[GameID \"20261005143027-048213\"]\n"
            + "[Termination \"resignation\"]\n",
            "tags: Chess960 game from position 518 (the standard start)");

        // No clock, won by resignation.
        CheckText(ChessPgn.WriteTags("Casual game", "VRChat", "2026.10.05", "Ana", "Siggi",
                ChessGame.EndingResignation, ChessRules.White, ChessGame.ModeStandard, "", 0, 0, gameId, ""),
            "[Event \"Casual game\"]\n"
            + "[Site \"VRChat\"]\n"
            + "[Date \"2026.10.05\"]\n"
            + "[Round \"-\"]\n"
            + "[White \"Ana\"]\n"
            + "[Black \"Siggi\"]\n"
            + "[Result \"1-0\"]\n"
            + "[TimeControl \"-\"]\n"
            + "[GameID \"20261005143027-048213\"]\n"
            + "[Termination \"resignation\"]\n",
            "tags: game with no clock");

        // A game continued from ply 3 of another (a takeback), still in progress: Result "*", no Termination. It
        // keeps the original game's starting FEN.
        CheckText(ChessPgn.WriteTags("Casual game", "VRChat", "2026.10.06", "Ana", "Siggi",
                ChessGame.EndingNone, ChessRules.NoColor, ChessGame.ModeStandard,
                "r3k3/1P4P1/8/8/8/8/1p4p1/R3K2R b KQq - 3 40", 600, 5, "20261006090000-000007", gameId + "/3"),
            "[Event \"Casual game\"]\n"
            + "[Site \"VRChat\"]\n"
            + "[Date \"2026.10.06\"]\n"
            + "[Round \"-\"]\n"
            + "[White \"Ana\"]\n"
            + "[Black \"Siggi\"]\n"
            + "[Result \"*\"]\n"
            + "[SetUp \"1\"]\n"
            + "[FEN \"r3k3/1P4P1/8/8/8/8/1p4p1/R3K2R b KQq - 3 40\"]\n"
            + "[TimeControl \"600+5\"]\n"
            + "[GameID \"20261006090000-000007\"]\n"
            + "[ContinuedFrom \"20261005143027-048213/3\"]\n",
            "tags: continued game in progress");

        // The same continued game logged unfinished, then a continued game that ended by the fifty-move rule.
        CheckText(ChessPgn.WriteTags("Casual game", "VRChat", "2026.10.06", "Ana", "Siggi",
                ChessGame.EndingUnterminated, ChessRules.NoColor, ChessGame.ModeStandard, "", 600, 5,
                "20261006090000-000007", gameId + "/0"),
            "[Event \"Casual game\"]\n"
            + "[Site \"VRChat\"]\n"
            + "[Date \"2026.10.06\"]\n"
            + "[Round \"-\"]\n"
            + "[White \"Ana\"]\n"
            + "[Black \"Siggi\"]\n"
            + "[Result \"*\"]\n"
            + "[TimeControl \"600+5\"]\n"
            + "[GameID \"20261006090000-000007\"]\n"
            + "[ContinuedFrom \"20261005143027-048213/0\"]\n"
            + "[Termination \"unterminated\"]\n",
            "tags: continued game, unterminated");

        // Names needing escapes: " and \, an emoji (a surrogate pair), ] and [, a tab and a newline (control
        // characters become spaces), U+0085 (a C1 control) and U+00A0 (not a control, kept). Nothing else given.
        CheckText(ChessPgn.WriteTags(null, "", "", "Ana \"The Rook\" \\ Ñandú 🐴",
                "Siggі] [x\t\r\ny\u0085z ", ChessGame.EndingThreefoldRepetition, ChessRules.NoColor,
                ChessGame.ModeStandard, null, -1, 0, null, null),
            "[Event \"?\"]\n"
            + "[Site \"?\"]\n"
            + "[Date \"????.??.??\"]\n"
            + "[Round \"-\"]\n"
            + "[White \"Ana \\\"The Rook\\\" \\\\ Ñandú 🐴\"]\n"
            + "[Black \"Siggі] [x   y z \"]\n"
            + "[Result \"1/2-1/2\"]\n"
            + "[TimeControl \"-\"]\n"
            + "[Termination \"threefold repetition\"]\n",
            "tags: escaped names, unknown values");

        // Empty names, and a time forfeit drawn because the opponent can't mate.
        CheckText(ChessPgn.WriteTags("", "", "2026.10.05", "", null, ChessGame.EndingTimeForfeit,
                ChessRules.NoColor, ChessGame.ModeStandard, "", 180, 2, gameId, ""),
            "[Event \"?\"]\n"
            + "[Site \"?\"]\n"
            + "[Date \"2026.10.05\"]\n"
            + "[Round \"-\"]\n"
            + "[White \"?\"]\n"
            + "[Black \"?\"]\n"
            + "[Result \"1/2-1/2\"]\n"
            + "[TimeControl \"180+2\"]\n"
            + "[GameID \"20261005143027-048213\"]\n"
            + "[Termination \"time forfeit\"]\n",
            "tags: empty names, drawn time forfeit");

        // Every ending's Termination text and Result, with each winner.
        string[] terminations = new string[] {
            "", "checkmate", "resignation", "time forfeit", "stalemate", "draw agreement", "threefold repetition",
            "fivefold repetition", "fifty-move rule", "seventy-five-move rule", "insufficient material",
            "unterminated", "king captured"
        };
        string errors = "";
        if (terminations.Length != ChessGame.EndingCount) errors += " count=" + ChessGame.EndingCount;
        for (int ending = 0; ending < terminations.Length; ending++) {
            string text = ChessPgn.TerminationText(ending);
            if (text != terminations[ending]) errors += " " + ending + ":\"" + text + "\"";
            bool noResult = ending == ChessGame.EndingNone || ending == ChessGame.EndingUnterminated;
            string white = ChessPgn.ResultText(ending, ChessRules.White);
            string black = ChessPgn.ResultText(ending, ChessRules.Black);
            string draw = ChessPgn.ResultText(ending, ChessRules.NoColor);
            if (white != (noResult ? "*" : "1-0") || black != (noResult ? "*" : "0-1")
                || draw != (noResult ? "*" : "1/2-1/2")) {
                errors += " " + ending + ":" + white + "/" + black + "/" + draw;
            }
        }
        if (ChessPgn.TerminationText(-1) != "" || ChessPgn.TerminationText(ChessGame.EndingCount) != "") {
            errors += " unknown endings";
        }
        Check(errors == "", "Termination and Result for every ending", errors);

        Check(ChessPgn.TimeControlText(0, 0) == "-" && ChessPgn.TimeControlText(0, 5) == "-"
            && ChessPgn.TimeControlText(-1, 0) == "-" && ChessPgn.TimeControlText(600, 0) == "600"
            && ChessPgn.TimeControlText(300, 3) == "300+3" && ChessPgn.TimeControlText(5400, 30) == "5400+30",
            "TimeControl values",
            ChessPgn.TimeControlText(0, 5) + " " + ChessPgn.TimeControlText(600, 0) + " "
            + ChessPgn.TimeControlText(300, 3));

        Check(ChessPgn.EscapeTagValue(null) == "" && ChessPgn.EscapeTagValue("") == ""
            && ChessPgn.EscapeTagValue("\\\"") == "\\\\\\\"" && ChessPgn.EscapeTagValue("a\u0000b\u007f\u009f ")
            == "a b   ",
            "EscapeTagValue edge cases", ChessPgn.EscapeTagValue("\\\""));
    }
}
