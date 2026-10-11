using System.Text.RegularExpressions;
using SiggiChessBoard;
using UdonSharp;
using UnityEngine;

// Step 5 test behaviour (PLAN.md section 5): PGN export and import, with no world objects.
// Logs "[PgnTest] PASS: <check>" or "[PgnTest] FAIL: <check>: <details>" per check, then a summary line.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PgnTest : UdonSharpBehaviour {
    // The PGN test set (docs/testing.md), found by name (the file name without .txt).
    public TextAsset[] pgnFiles;

    Regex tokenRegex;
    Regex clockRegex;
    int passed;
    int failed;

    void Start() {
        TestTags();
        TestMoveText();
        tokenRegex = new Regex(ChessPgn.TokenPattern);
        clockRegex = new Regex(ChessPgn.ClockPattern);
        TestImportValues();
        TestImportFiles();
        TestImportSyntax();
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

    // ---- 5.2a ----

    // The text of a PGN test set file, by its asset name (e.g. "Siggi-vs-Nuka.pgn"); null if it isn't assigned.
    string PgnText(string name) {
        if (pgnFiles == null) return null;
        for (int i = 0; i < pgnFiles.Length; i++) {
            if (pgnFiles[i] != null && pgnFiles[i].name == name) return pgnFiles[i].text;
        }
        return null;
    }

    // Reads a whole PGN with ChessPgn.ReadNext, as an import does (5.2b also plays each move as it comes). Returns
    // ReadEnd or ReadError; sans and clocks get one entry per main-line move (clock -1 when it has none), moveCount
    // the number of moves (the failing ply for ReadError).
    int ReadAll(string pgn, string[] tags, string[] sans, int[] clocks, out int moveCount) {
        int[] cursor = new int[ChessPgn.CursorLength];
        ChessPgn.StartReading(cursor, tags);
        moveCount = 0;
        while (true) {
            string san;
            int clock;
            int read = ChessPgn.ReadNext(tokenRegex, clockRegex, pgn, cursor, tags, out san, out clock);
            if (read == ChessPgn.ReadMove) {
                if (moveCount == sans.Length) return ChessPgn.ReadError;
                sans[moveCount] = san;
                clocks[moveCount] = -1;
                moveCount++;
                if (cursor[ChessPgn.CursorMoves] != moveCount) return ChessPgn.ReadError;
            } else if (read == ChessPgn.ReadClock) {
                if (moveCount == 0) return ChessPgn.ReadError;
                clocks[moveCount - 1] = clock;
            } else {
                return read;
            }
        }
    }

    // Reads pgn and checks the outcome (ChessPgn.ReadEnd, or ReadError with expectedSans the moves before the
    // failure), the tags (string[TagCount], null to skip), the main-line moves (space-separated SAN) and their clocks
    // (null for all -1).
    void CheckRead(string name, string pgn, int expectedRead, string[] expectedTags, string expectedSans,
        int[] expectedClocks) {
        if (pgn == null) {
            Check(false, name, "PGN file not assigned in PgnTest's pgnFiles");
            return;
        }
        string[] tags = new string[ChessPgn.TagCount];
        string[] sans = new string[600];
        int[] clocks = new int[600];
        int moveCount;
        int read = ReadAll(pgn, tags, sans, clocks, out moveCount);
        string[] expected = expectedSans == "" ? new string[0] : expectedSans.Split(new char[] { ' ' });
        string errors = "";
        if (read != expectedRead) errors += " read " + read + " at ply " + moveCount;
        if (moveCount != expected.Length) errors += " " + moveCount + " moves, expected " + expected.Length;
        for (int i = 0; i < moveCount && i < expected.Length; i++) {
            if (sans[i] != expected[i]) errors += " ply " + (i + 1) + ": " + sans[i] + " not " + expected[i];
            int expectedClock = expectedClocks == null ? -1 : expectedClocks[i];
            if (clocks[i] != expectedClock) errors += " ply " + (i + 1) + " clock " + clocks[i] + " not " + expectedClock;
        }
        if (expectedClocks != null && expectedClocks.Length != expected.Length) errors += " clock list length";
        if (expectedTags != null) {
            for (int tag = 0; tag < ChessPgn.TagCount; tag++) {
                if (tags[tag] != expectedTags[tag]) {
                    errors += " " + ChessPgn.TagName(tag) + " \"" + tags[tag] + "\" not \"" + expectedTags[tag] + "\"";
                }
            }
        }
        Check(errors == "", name + " (" + moveCount + " moves)", errors);
    }

    string[] Tags(string fen, string variant, string white, string black, string result, string gameId,
        string timeControl) {
        return new string[] { fen, variant, white, black, result, gameId, timeControl };
    }

    string JoinSans(string[] sans) {
        string joined = "";
        for (int i = 0; i < sans.Length; i++) joined += (i == 0 ? "" : " ") + sans[i];
        return joined;
    }

    void TestImportValues() {
        string errors = "";
        string[] comments = new string[] {
            " [%clk 0:09:57.6] ", "[%eval 0.17] [%clk 0:04:59]", "[%clk 1:02:03.456]", "[%clk 0:00:05.07]",
            "[%clk 0:10:00.0]", "[%clk 0:00:00]", "[%clk 0:00:01.23456]", "[%clk  0:1:2 ]", "[%clk 10:00]",
            "no clock", "", "[%clk 0:05:00] [%clk 0:04:00]", "[%clk 596:31:23.647]", "[%clk 596:31:23.648]",
            "[%clk 999999999:00:00]", "[%clk 0:05:00", "[%clkx 0:05:00]"
        };
        int[] milliseconds = new int[] {
            597600, 299000, 3723456, 5070, 600000, 0, 1234, 62000, -1, -1, -1, 300000, int.MaxValue, -1, -1, -1, -1
        };
        for (int i = 0; i < comments.Length; i++) {
            int clock = ChessPgn.ClockFromComment(clockRegex, comments[i]);
            if (clock != milliseconds[i]) errors += " \"" + comments[i] + "\"=" + clock;
        }
        Check(errors == "", "import: [%clk] values", errors);

        Check(ChessPgn.UnescapeTagValue("") == "" && ChessPgn.UnescapeTagValue("plain") == "plain"
            && ChessPgn.UnescapeTagValue("a\\\\b\\\"c\\]") == "a\\b\"c]"
            && ChessPgn.UnescapeTagValue("end\\") == "end\\"
            && ChessPgn.UnescapeTagValue(ChessPgn.EscapeTagValue("Ana \"The Rook\" \\ Ñandú 🐴"))
                == "Ana \"The Rook\" \\ Ñandú 🐴",
            "import: UnescapeTagValue", ChessPgn.UnescapeTagValue("a\\\\b\\\"c\\]"));

        errors = "";
        string[] timeControls = new string[] {
            "-", "600", "300+3", "60+0", "5400+30", "", "?", "40/9000:300", "0", "0+5", "+3", "600+", "600+-1",
            "1234567890", "600 ", " 600", "10+2+1", "1.5"
        };
        string[] parsed = new string[] {
            "0 0", "600 0", "300 3", "60 0", "5400 30", "", "", "", "", "", "", "", "", "", "", "", "", ""
        };
        for (int i = 0; i < timeControls.Length; i++) {
            int baseSeconds;
            int incrementSeconds;
            bool known = ChessPgn.ParseTimeControl(timeControls[i], out baseSeconds, out incrementSeconds);
            string text = known ? baseSeconds + " " + incrementSeconds : "";
            if (text != parsed[i] || (!known && (baseSeconds != -1 || incrementSeconds != -1))) {
                errors += " \"" + timeControls[i] + "\"=" + known + " " + baseSeconds + " " + incrementSeconds;
            }
        }
        Check(errors == "", "import: TimeControl values", errors);

        Check(ChessPgn.ModeFromVariant("Chess960") == ChessGame.ModeChess960
            && ChessPgn.ModeFromVariant("Standard") == ChessGame.ModeStandard
            && ChessPgn.ModeFromVariant("") == ChessGame.ModeStandard
            && ChessPgn.ModeFromVariant("From Position") == ChessGame.ModeStandard,
            "import: mode from Variant", "");

        errors = "";
        for (int tag = 0; tag < ChessPgn.TagCount; tag++) {
            if (ChessPgn.TagIndex(ChessPgn.TagName(tag)) != tag) errors += " " + tag;
        }
        if (ChessPgn.TagIndex("GameId") != -1 || ChessPgn.TagIndex("white") != -1 || ChessPgn.TagIndex("Event") != -1
            || ChessPgn.TagIndex("") != -1) {
            errors += " other names";
        }
        Check(errors == "" && ChessPgn.TagName(ChessPgn.TagFen) == "FEN" && ChessPgn.TagName(ChessPgn.TagGameId)
            == "GameID", "import: tag names", errors);
    }

    // formatting.pgn.txt's clock history: 0:05:00 and 0:04:59.5 on the first two plies, none on the other 22.
    int[] FormattingClocks() {
        int[] clocks = new int[24];
        for (int i = 0; i < clocks.Length; i++) clocks[i] = -1;
        clocks[0] = 300000;
        clocks[1] = 299500;
        return clocks;
    }

    void TestImportFiles() {
        int end = ChessPgn.ReadEnd;
        int error = ChessPgn.ReadError;
        string siggi = "Siggі";
        string nuka = "Nukaǃ";
        string ana = "Ana \"The Rook\" \\ Ñandú 🐴";

        // The full SAN lists are joined from SiggiVsNukaSan (which has O-O for the file's 0-0).
        CheckRead("import: Siggi-vs-Nuka (VRChess: 0-0, no result token)", PgnText("Siggi-vs-Nuka.pgn"), end,
            Tags("", "", siggi, nuka, "0-1", "20250710001921", "600"), JoinSans(SiggiVsNukaSan()),
            SiggiVsNukaClocks());

        CheckRead("import: lichess-long-draw (Lichess formatting)", PgnText("lichess-long-draw.pgn"), end,
            Tags("", "Standard", "iazz1", "Mohamed_AL_Shaqiri", "1/2-1/2", "", "60+0"),
            "e4 g6 d4 c6 Nc3 d5 f3 dxe4 fxe4 Bg7 Nf3 Bg4 Be3 Nd7 Bc4 e5 O-O Ngf6 h3 Bxf3 Qxf3 exd4 Bxd4 O-O Rad1 Qe7 e5 "
            + "Nxe5 Bxe5 Qxe5 Bb3 Rae8 Kh1 Re7 Nb1 Nh5 Rf2 Ng3+ Kg1 Ne2+ Kh1 Nd4 Qd3 Nf5 Nd2 Qxb2 Ne4 Qe5 Re1 Kh8 Rfe2 "
            + "Qc7 Kg1 h6 Nf2 Rxe2 Rxe2 Kg8 Ng4 h5 Nf2 Bd4 c3 Bxf2+ Rxf2 Kg7 g4 hxg4 hxg4 Qg3+ Qxg3 Nxg3 c4 Ne4 Re2 "
            + "Nc5 Bc2 Rd8 Kf2 Rd6 Kf3 Rd4 Re7 Rxc4 Bb3 Nxb3 axb3 Rc3+ Re3 Rxb3 Rxb3 b5 Ra3 b4 Rxa7 c5 Rc7 b3 Rxc5 f5 "
            + "Rb5 fxg4+ Kxg4 Kf6 Rxb3 g5 Rb6+ Ke5 Rb5+ Kd4 Rxg5 Kc4 Rf5 Kd3 Rf4 Kc3 Rf5 Kd3 Rf4 Kc3 Kf3 Kd2 Re4 Kc3 "
            + "Kf4 Kd3 Re3+ Kd2 Re4 Kc2 Re5 Kd2 Re6 Kc2 Rd6 Kc3 Ke4 Kc4 Ke3 Kc5 Rd5+ Kxd5",
            new int[] {
                60000, 60000, 59000, 60000, 59000, 59000, 55000, 59000, 54000, 58000, 54000, 58000, 52000, 56000,
                51000, 54000, 47000, 53000, 45000, 52000, 45000, 48000, 45000, 48000, 42000, 46000, 41000, 46000,
                39000, 46000, 38000, 44000, 36000, 43000, 30000, 42000, 27000, 41000, 27000, 40000, 26000, 39000,
                24000, 36000, 24000, 35000, 22000, 34000, 21000, 32000, 20000, 27000, 18000, 26000, 17000, 25000,
                17000, 24000, 16000, 23000, 14000, 21000, 12000, 20000, 12000, 20000, 11000, 19000, 11000, 18000,
                10000, 18000, 9000, 18000, 8000, 18000, 8000, 17000, 8000, 16000, 7000, 15000, 7000, 15000, 6000,
                14000, 6000, 14000, 5000, 14000, 4000, 13000, 3000, 13000, 3000, 13000, 3000, 13000, 2000, 13000, 2000,
                13000, 1000, 13000, 1000, 13000, 1000, 12000, 1000, 12000, 1000, 12000, 1000, 12000, 1000, 12000, 1000,
                12000, 1000, 12000, 1000, 11000, 1000, 11000, 0, 11000, 0, 10000, 0, 10000, 0, 10000, 0, 10000, 0,
                9000, 0, 9000, 0, 9000, 0, 8000
            });

        string formattingSans = "d4 d5 Nc3 Nc6 Bf4 Bf5 Qd2 Qd7 O-O-O O-O-O e4 dxe4 d5 Nb4 a3 Nxd5 Nxd5 Qxd5 Qxd5 Rxd5 "
            + "Rxd5 e6 Rd8+ Kxd8";
        string[] formattingTags = Tags("", "", "White", "Black", "*", "", "");
        string formatting = PgnText("formatting.pgn");
        CheckRead("import: formatting (main line only, clocks)", formatting, end, formattingTags, formattingSans,
            FormattingClocks());
        CheckRead("import: formatting with CRLF line ends", formatting == null ? null : formatting.Replace("\n", "\r\n"),
            end, formattingTags, formattingSans, FormattingClocks());

        CheckRead("import: repetition-names (escaped names)", PgnText("repetition-names.pgn"), end,
            Tags("", "", ana, "Siggі] [x", "1/2-1/2", "", ""), "Nf3 Nf6 Ng1 Ng8 Nf3 Nf6 Ng1 Ng8", null);
        CheckRead("import: checkmate (escaped name, #)", PgnText("checkmate.pgn"), end,
            Tags("", "", nuka, ana, "0-1", "", ""), "f3 e5 g4 Qh4#", null);
        CheckRead("import: chess960", PgnText("chess960.pgn"), end,
            Tags("qrkbbnrn/pppppppp/8/8/8/8/PPPPPPPP/QRKBBNRN w GBgb - 0 1", "Chess960", "White", "Black", "*", "", ""),
            "e4 e5 Bf3 Bf6 O-O-O O-O-O", null);
        CheckRead("import: promotions (starts at 40...)", PgnText("promotions.pgn"), end,
            Tags("r3k3/1P4P1/8/8/8/8/1p4p1/R3K2R b KQq - 3 40", "", "White", "Black", "*", "", ""),
            "gxh1=Q+ Kd2 bxa1=N bxa8=Q+ Kf7 g8=Q+ Kf6 Qgf8+ Ke5", null);
        CheckRead("import: en-passant", PgnText("en-passant.pgn"), end, Tags("", "", "White", "Black", "*", "", ""),
            "e4 a6 e5 d5 exd6 b5 a3 b4 c4 bxc3", null);
        CheckRead("import: disambiguation", PgnText("disambiguation.pgn"), end,
            Tags("7k/8/8/RN3N2/8/1N3N2/8/R5RK w - - 0 1", "", "White", "Black", "*", "", ""),
            "Nb5d4 Kh7 R1a3 Kh8 Nbd2 Kh7", null);
        CheckRead("import: stalemate", PgnText("stalemate.pgn"), end,
            Tags("", "", "Sam Loyd", "سيجي", "1/2-1/2", "", ""),
            "e3 a5 Qh5 Ra6 Qxa5 h5 h4 Rah6 Qxc7 f6 Qxd7+ Kf7 Qxb7 Qd3 Qxb8 Qh7 Qxc8 Kg6 Qe6", null);

        // The malformed files whose fault is the text itself fail at the bad token; the others (an illegal and an
        // ambiguous move) only fail when the moves are matched (5.2b), so they read in full.
        CheckRead("import: malformed/garbage fails at 2...", PgnText("garbage.pgn"), error, null, "e4 e5 Nf3", null);
        CheckRead("import: malformed/unterminated-comment fails at 2...", PgnText("unterminated-comment.pgn"), error,
            null, "e4 e5 Nf3", null);
        CheckRead("import: malformed/illegal-move reads", PgnText("illegal-move.pgn"), end, null,
            "e4 f5 Qh5+ Nf6 Qxf5", null);
        CheckRead("import: malformed/ambiguous-move reads", PgnText("ambiguous-move.pgn"), end, null,
            "a4 a5 h4 h5 Ra3 Ra6 Rhh3 Rhh6 Rc3 Rc6", null);
    }

    void TestImportSyntax() {
        int end = ChessPgn.ReadEnd;
        int error = ChessPgn.ReadError;
        string[] noTags = Tags("", "", "", "", "", "", "");

        CheckRead("import: empty text", "", end, noTags, "", null);
        CheckRead("import: only whitespace", " \n\t\r\n ", end, noTags, "", null);
        CheckRead("import: no tags, no result", "e4 e5", end, noTags, "e4 e5", null);
        CheckRead("import: byte order mark", "﻿[White \"A\"]\n\n1. e4 *", end, Tags("", "", "A", "", "", "", ""),
            "e4", null);
        CheckRead("import: move numbers with and without spaces, 12... first, 0-0-0",
            "[FEN \"x\"]\n\n12...e5 13. Nf3 13...0-0-0 14.O-O 14... O-O-O *", end, Tags("x", "", "", "", "", "", ""),
            "e5 Nf3 O-O-O O-O O-O-O", null);
        CheckRead("import: annotations and NAGs skipped",
            "1. e4!! e5?? 2. Nf3!? $14 Nc6?! 3. Bb5 ! d6 ? 4. c3$1 $2 a6! 5. O-O!? 1-0", end, null,
            "e4 e5 Nf3 Nc6 Bb5 d6 c3 a6 O-O", null);
        CheckRead("import: +, # and promotions kept", "1. f3 e5 2. g4 Qh4# 3. exd8=N+ bxa1=Q 4. Raxb1 R8h7+ *", end,
            null, "f3 e5 g4 Qh4# exd8=N+ bxa1=Q Raxb1 R8h7+", null);
        CheckRead("import: 1-0 ends the text", "1. e4 1-0 hello ( {", end, null, "e4", null);
        CheckRead("import: 0-1 ends the text", "1. e4 0-1 2. d4", end, null, "e4", null);
        CheckRead("import: 1/2-1/2 ends the text", "1. e4 1/2-1/2 )", end, null, "e4", null);
        CheckRead("import: * ends the text", "1. e4 e5 * }", end, null, "e4 e5", null);
        CheckRead("import: a tag after the move text ends it",
            "[White \"A\"]\n\n1. e4\n\n[White \"B\"]\n\n1. d4 *", end, Tags("", "", "A", "", "", "", ""), "e4", null);
        CheckRead("import: only the first of two games",
            "[White \"A\"]\n\n1. e4 *\n\n[White \"B\"]\n\n1. d4 *", end, Tags("", "", "A", "", "", "", ""), "e4", null);
        CheckRead("import: tags kept, repeated, ignored, case-sensitive",
            "[White \"A\"]\n[white \"x\"]\n[GameId \"y\"]\n[Annotator \"z\"]\n[ Black   \"C\" ]\n[White \"B\"]\n"
            + "[GameID \"g\"][TimeControl \"300+3\"]\n[Variant \"Chess960\"]\n[Result \"*\"]\n\n*", end,
            Tags("", "Chess960", "B", "C", "*", "g", "300+3"), "", null);
        CheckRead("import: escapes in a tag", "[White \"a\\\\b\\\"c\\]\"]\n[Black \"\"]\n\n*", end,
            Tags("", "", "a\\b\"c]", "", "", "", ""), "", null);
        CheckRead("import: unclosed tag value fails", "[White \"A]\n\n1. e4 *", error, null, "", null);
        CheckRead("import: unescaped quote in a tag fails", "[White \"A\"B\"]\n\n1. e4 *", error, null, "", null);
        CheckRead("import: % line in the first column", "% first\n[White \"A\"]\n%x\n1. e4\n% e5\nd5 *", end,
            Tags("", "", "A", "", "", "", ""), "e4 d5", null);
        CheckRead("import: % not in the first column fails", "1. e4\n % x\ne5 *", error, null, "e4", null);
        CheckRead("import: ; comment to the end of the line", "1. e4 ; e5 ( {\n1... c5 *", end, null, "e4 c5", null);
        CheckRead("import: nested variations, ) inside a comment",
            "1. e4 (1. d4 {a ) in a comment} (1. c4 (1. Nf3)) 1... d5) 1... e5 (1... c5) *", end, null, "e4 e5",
            null);
        CheckRead("import: ) without ( fails", "1. e4 e5 ) 2. Nf3 *", error, null, "e4 e5", null);
        CheckRead("import: unclosed ( fails", "1. e4 (1. d4 d5", error, null, "e4", null);
        CheckRead("import: result inside a variation fails", "1. e4 (1. d4 *) e5 *", error, null, "e4", null);
        CheckRead("import: unclosed { fails", "1. e4 {e5 *", error, null, "e4", null);
        CheckRead("import: } without { fails", "1. e4 } e5", error, null, "e4", null);
        CheckRead("import: a word fails", "1. e4 e5 2. Nf3 hello", error, null, "e4 e5 Nf3", null);
        CheckRead("import: text after a move fails", "1. e4x e5", error, null, "", null);
        CheckRead("import: promotion without = fails", "1. e8Q", error, null, "", null);
        CheckRead("import: lowercase piece fails", "1. e4 e5 2. nf3", error, null, "e4 e5", null);
        CheckRead("import: clocks: before the first move, in a variation, two after a move, after a variation",
            "{[%clk 0:09:00]} 1. e4 {[%clk 0:05:00]} {[%clk 0:04:59]} e5 (1... c5 {[%clk 0:01:00]}) {[%clk 0:04:58]} "
            + "2. Nf3 {no clock} Nc6 {[%eval 0.3]} *", end, null, "e4 e5 Nf3 Nc6",
            new int[] { 299000, 298000, -1, -1 });
    }
}
