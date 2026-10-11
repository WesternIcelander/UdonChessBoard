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
        TestMoveText();
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

    // ---- 5.1b ----

    // Siggi-vs-Nuka.pgn.txt's moves (with O-O for its 0-0) and clock history in milliseconds.
    string[] SiggiVsNukaSan() {
        return ("e4 c5 Nf3 Nc6 d3 e6 Bf4 a6 c3 b5 c4 Bb7 Nc3 bxc4 dxc4 Qb6 b3 Nd4 Nd2 Bd6 e5 Be7 Na4 Qc6 h3 f6 "
            + "f3 fxe5 Bxe5 Nf6 f4 d6 b4 dxe5 fxe5 Ne4 Nb1 O-O Bd3 Nf2 Rf1 Nxd1 Kxd1 Rxf1 Kd2 Rf2 Ke3 Bg5 Kxf2 Qxg2 "
            + "Ke1 Rf8 Bf1 Rxf1").Split(new char[] { ' ' });
    }

    int[] SiggiVsNukaClocks() {
        return new int[] {
            600000, 597600, 598300, 597000, 587100, 595200, 576900, 592200, 553600, 590400, 549200, 588300, 545500,
            586600, 543200, 577500, 523200, 574300, 514600, 563900, 493100, 548900, 460300, 547100, 426200, 545100,
            416600, 537500, 410300, 536200, 382700, 529700, 338000, 527100, 333000, 521800, 324200, 510100, 316200,
            501900, 302100, 498400, 300200, 495300, 296600, 491900, 292300, 487200, 289800, 484500, 283600, 482600,
            279300, 480500
        };
    }

    void TestMoveText() {
        string[] siggiSan = SiggiVsNukaSan();
        int[] siggiClocks = SiggiVsNukaClocks();

        CheckText(ChessPgn.ClockText(0) + " " + ChessPgn.ClockText(99) + " " + ChessPgn.ClockText(5999) + " "
                + ChessPgn.ClockText(60000) + " " + ChessPgn.ClockText(597600) + " " + ChessPgn.ClockText(597650) + " "
                + ChessPgn.ClockText(600000) + " " + ChessPgn.ClockText(3599900) + " " + ChessPgn.ClockText(3600000)
                + " " + ChessPgn.ClockText(360000000),
            "0:00:00 0:00:00 0:00:05.9 0:01:00 0:09:57.6 0:09:57.6 0:10:00 0:59:59.9 1:00:00 100:00:00",
            "move text: clock values");

        // The first six plies of Siggi-vs-Nuka (the arrays hold the whole game, as the board's arrays are longer
        // than the moves played): every move has a clock, so every Black move is numbered. Wraps at 80.
        CheckText(ChessPgn.WriteMoveText(siggiSan, siggiClocks, 6, 1, ChessRules.White, ChessGame.EndingCheckmate,
                ChessRules.Black, ChessPgn.LineWidth),
            "1. e4 { [%clk 0:10:00] } 1... c5 { [%clk 0:09:57.6] } 2. Nf3\n"
            + "{ [%clk 0:09:58.3] } 2... Nc6 { [%clk 0:09:57] } 3. d3 { [%clk 0:09:47.1] }\n"
            + "3... e6 { [%clk 0:09:55.2] } 0-1\n",
            "move text: clocks on every move, wrapped at 80");

        // The same at width 60: the first line is exactly 60 characters long and still fits.
        CheckText(ChessPgn.WriteMoveText(siggiSan, siggiClocks, 6, 1, ChessRules.White, ChessGame.EndingCheckmate,
                ChessRules.Black, 60),
            "1. e4 { [%clk 0:10:00] } 1... c5 { [%clk 0:09:57.6] } 2. Nf3\n"
            + "{ [%clk 0:09:58.3] } 2... Nc6 { [%clk 0:09:57] } 3. d3\n"
            + "{ [%clk 0:09:47.1] } 3... e6 { [%clk 0:09:55.2] } 0-1\n",
            "move text: a line exactly the wrap width");

        // Some clocks unknown (-1): Black's move is numbered only after a clock comment.
        string[] ruyLopez = new string[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6" };
        CheckText(ChessPgn.WriteMoveText(ruyLopez, new int[] { 300000, -1, 297500, 296000, -1, -1 }, 6, 1,
                ChessRules.White, ChessGame.EndingResignation, ChessRules.White, ChessPgn.LineWidth),
            "1. e4 { [%clk 0:05:00] } 1... e5 2. Nf3 { [%clk 0:04:57.5] } 2... Nc6\n"
            + "{ [%clk 0:04:56] } 3. Bb5 a6 1-0\n",
            "move text: some clocks unknown");

        // No clock history, starting with Black to move at move 40 (promotions.pgn.txt), in progress.
        string[] promotions = new string[] {
            "gxh1=Q+", "Kd2", "bxa1=N", "bxa8=Q+", "Kf7", "g8=Q+", "Kf6", "Qgf8+", "Ke5"
        };
        CheckText(ChessPgn.WriteMoveText(promotions, null, 9, 40, ChessRules.Black, ChessGame.EndingNone,
                ChessRules.NoColor, ChessPgn.LineWidth),
            "40... gxh1=Q+ 41. Kd2 bxa1=N 42. bxa8=Q+ Kf7 43. g8=Q+ Kf6 44. Qgf8+ Ke5 *\n",
            "move text: Black to move first, no clocks");
        CheckText(ChessPgn.WriteMoveText(promotions, null, 9, 40, ChessRules.Black, ChessGame.EndingNone,
                ChessRules.NoColor, 30),
            "40... gxh1=Q+ 41. Kd2 bxa1=N\n"
            + "42. bxa8=Q+ Kf7 43. g8=Q+ Kf6\n"
            + "44. Qgf8+ Ke5 *\n",
            "move text: Black to move first, wrapped at 30");

        // White to move at a later move number, castling, a draw.
        CheckText(ChessPgn.WriteMoveText(new string[] { "O-O", "O-O-O", "Rxd8+", "Kxd8" },
                new int[] { -1, -1, -1, -1 }, 4, 7, ChessRules.White, ChessGame.EndingDrawAgreement,
                ChessRules.NoColor, ChessPgn.LineWidth),
            "7. O-O O-O-O 8. Rxd8+ Kxd8 1/2-1/2\n",
            "move text: White to move at move 7, castling, a draw");

        // Units wider than the line get a line each, and the first never gets an empty line before it.
        CheckText(ChessPgn.WriteMoveText(siggiSan, siggiClocks, 1, 1, ChessRules.White, ChessGame.EndingUnterminated,
                ChessRules.NoColor, 3),
            "1. e4\n{ [%clk 0:10:00] }\n*\n",
            "move text: units wider than the line");

        // No moves.
        CheckText(ChessPgn.WriteMoveText(new string[0], null, 0, 1, ChessRules.White, ChessGame.EndingNone,
                ChessRules.NoColor, ChessPgn.LineWidth), "*\n", "move text: no moves, in progress");
        CheckText(ChessPgn.WriteMoveText(new string[0], new int[0], 0, 1, ChessRules.White,
                ChessGame.EndingResignation, ChessRules.Black, ChessPgn.LineWidth), "0-1\n",
            "move text: no moves, resigned");

        // The whole game: unwrapped, it is every move numbered with its clock; wrapped at 80, joining the lines with
        // spaces gives the unwrapped text back (lines only break at spaces), no line is longer than 80 or starts or
        // ends with a space, and no line break falls inside a clock comment.
        string expected = "";
        for (int i = 0; i < 54; i++) {
            expected += (i / 2 + 1).ToString() + (i % 2 == 0 ? ". " : "... ") + siggiSan[i] + " { [%clk "
                + ChessPgn.ClockText(siggiClocks[i]) + "] } ";
        }
        expected += "0-1\n";
        string unwrapped = ChessPgn.WriteMoveText(siggiSan, siggiClocks, 54, 1, ChessRules.White,
            ChessGame.EndingCheckmate, ChessRules.Black, int.MaxValue);
        CheckText(unwrapped, expected, "move text: whole game unwrapped");

        string wrapped = ChessPgn.WriteMoveText(siggiSan, siggiClocks, 54, 1, ChessRules.White,
            ChessGame.EndingCheckmate, ChessRules.Black, ChessPgn.LineWidth);
        string errors = "";
        if (wrapped[wrapped.Length - 1] != '\n') errors += " no final newline";
        string[] lines = wrapped.Substring(0, wrapped.Length - 1).Split(new char[] { '\n' });
        if (lines.Length < 10) errors += " only " + lines.Length + " lines";
        string joined = "";
        for (int i = 0; i < lines.Length; i++) {
            string line = lines[i];
            if (line.Length > ChessPgn.LineWidth) errors += " line " + i + " is " + line.Length + " long";
            if (line.Length == 0 || line[0] == ' ' || line[line.Length - 1] == ' ') errors += " line " + i + " spaces";
            if (line.Split(new char[] { '{' }).Length != line.Split(new char[] { '}' }).Length) {
                errors += " line " + i + " splits a comment";
            }
            joined += (i == 0 ? "" : " ") + line;
        }
        if (joined + "\n" != unwrapped) errors += " joined lines differ from the unwrapped text";
        Check(errors == "", "move text: whole game wrapped at 80 (" + lines.Length + " lines)",
            errors + "\n" + wrapped);
    }
}
