using System.Text.RegularExpressions;
using SiggiChessBoard;
using UdonSharp;
using UnityEngine;

// Step 5 test behaviour (PLAN.md section 5): PGN export and import, with no world objects.
// Logs "[PgnTest] PASS: <check>" or "[PgnTest] FAIL: <check>: <details>" per check, then a summary line.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PgnTest : ChessTest {
    protected override string TestName() {
        return "PgnTest";
    }

    // The PGN test set (docs/testing.md), found by name (the file name without .txt).
    public TextAsset[] pgnFiles;
    // The importer the multi-frame import checks (5.3) run on; they fail if it isn't assigned.
    public ChessPgnImporter importer;

    Regex tokenRegex;
    Regex clockRegex;

    void Start() {
        TestTags();
        TestMoveText();
        tokenRegex = new Regex(ChessPgn.TokenPattern);
        clockRegex = new Regex(ChessPgn.ClockPattern);
        TestImportValues();
        TestImportFiles();
        TestImportSyntax();
        TestSanMatching();
        TestImportGames();
        TestImportFailures();
        TestRoundTrip();
        // The importer checks take frames; the summary is logged once they finish.
        StartFrameImports();
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
            + "[TimeControl \"?\"]\n"
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
            "unterminated", "king captured", ""
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
            && ChessPgn.TimeControlText(-1, 0) == "?" && ChessPgn.TimeControlText(-1, -1) == "?"
            && ChessPgn.TimeControlText(600, 0) == "600" && ChessPgn.TimeControlText(300, 3) == "300+3"
            && ChessPgn.TimeControlText(5400, 30) == "5400+30",
            "TimeControl values",
            ChessPgn.TimeControlText(0, 5) + " " + ChessPgn.TimeControlText(600, 0) + " "
            + ChessPgn.TimeControlText(300, 3) + " " + ChessPgn.TimeControlText(-1, -1));

        Check(ChessPgn._EscapeTagValue(null) == "" && ChessPgn._EscapeTagValue("") == ""
            && ChessPgn._EscapeTagValue("\\\"") == "\\\\\\\"" && ChessPgn._EscapeTagValue("a\u0000b\u007f\u009f ")
            == "a b   ",
            "EscapeTagValue edge cases", ChessPgn._EscapeTagValue("\\\""));
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

    // Reads a whole PGN with ChessPgn._ReadNext, as an import does (5.2b also plays each move as it comes). Returns
    // ReadEnd or ReadError; sans and clocks get one entry per main-line move (clock -1 when it has none), moveCount
    // the number of moves (the failing ply for ReadError).
    int ReadAll(string pgn, string[] tags, string[] sans, int[] clocks, out int moveCount) {
        int[] cursor = new int[ChessPgn.CursorLength];
        ChessPgn._StartReading(cursor, tags);
        moveCount = 0;
        while (true) {
            string san;
            int clock;
            int read = ChessPgn._ReadNext(tokenRegex, clockRegex, pgn, cursor, tags, out san, out clock);
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
                    errors += " " + ChessPgn._TagName(tag) + " \"" + tags[tag] + "\" not \"" + expectedTags[tag] + "\"";
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
            int clock = ChessPgn._ClockFromComment(clockRegex, comments[i]);
            if (clock != milliseconds[i]) errors += " \"" + comments[i] + "\"=" + clock;
        }
        Check(errors == "", "import: [%clk] values", errors);

        Check(ChessPgn._UnescapeTagValue("") == "" && ChessPgn._UnescapeTagValue("plain") == "plain"
            && ChessPgn._UnescapeTagValue("a\\\\b\\\"c\\]") == "a\\b\"c]"
            && ChessPgn._UnescapeTagValue("end\\") == "end\\"
            && ChessPgn._UnescapeTagValue(ChessPgn._EscapeTagValue("Ana \"The Rook\" \\ Ñandú 🐴"))
                == "Ana \"The Rook\" \\ Ñandú 🐴",
            "import: UnescapeTagValue", ChessPgn._UnescapeTagValue("a\\\\b\\\"c\\]"));

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
            bool known = ChessPgn._ParseTimeControl(timeControls[i], out baseSeconds, out incrementSeconds);
            string text = known ? baseSeconds + " " + incrementSeconds : "";
            if (text != parsed[i] || (!known && (baseSeconds != -1 || incrementSeconds != -1))) {
                errors += " \"" + timeControls[i] + "\"=" + known + " " + baseSeconds + " " + incrementSeconds;
            }
        }
        Check(errors == "", "import: TimeControl values", errors);

        Check(ChessPgn._ModeFromVariant("Chess960") == ChessGame.ModeChess960
            && ChessPgn._ModeFromVariant("Standard") == ChessGame.ModeStandard
            && ChessPgn._ModeFromVariant("") == ChessGame.ModeStandard
            && ChessPgn._ModeFromVariant("From Position") == ChessGame.ModeStandard,
            "import: mode from Variant", "");

        errors = "";
        for (int tag = 0; tag < ChessPgn.TagCount; tag++) {
            if (ChessPgn._TagIndex(ChessPgn._TagName(tag)) != tag) errors += " " + tag;
        }
        if (ChessPgn._TagIndex("GameId") != -1 || ChessPgn._TagIndex("white") != -1 || ChessPgn._TagIndex("Event") != -1
            || ChessPgn._TagIndex("") != -1) {
            errors += " other names";
        }
        Check(errors == "" && ChessPgn._TagName(ChessPgn.TagFen) == "FEN" && ChessPgn._TagName(ChessPgn.TagGameId)
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

    // ---- 5.2b ----

    // The SAN of the legal move MoveFromSan finds for san in fen's position (ChessNotation.MoveToSan), or "none" and
    // "ambiguous" for SanNoMove and SanAmbiguous.
    string SanMatch(string fen, string san) {
        byte[] board = new byte[64];
        int[] state = new int[ChessPosition.StateLength];
        int halfmoveClock;
        int fullmoveNumber;
        if (!ChessFen.ParseFen(fen, board, state, out halfmoveClock, out fullmoveNumber)) return "bad FEN";
        int[] legalMoves = new int[ChessMoves.MaxMoves];
        int legalCount = ChessMoves.GenerateLegalMoves(board, state, legalMoves, true);
        int move = ChessNotation.MoveFromSan(san, legalMoves, legalCount);
        if (move == ChessNotation.SanNoMove) return "none";
        if (move == ChessNotation.SanAmbiguous) return "ambiguous";
        return ChessNotation.MoveToSan(board, state, move, legalMoves, legalCount, new int[ChessMoves.MaxMoves], true);
    }

    // Checks SanMatch for each "san>expected" pair (separated by spaces) in fen's position.
    void CheckSanMatches(string name, string fen, string pairs) {
        string errors = "";
        string[] items = pairs.Split(new char[] { ' ' });
        for (int i = 0; i < items.Length; i++) {
            int separator = items[i].IndexOf('>');
            string san = items[i].Substring(0, separator);
            string expected = items[i].Substring(separator + 1);
            string actual = SanMatch(fen, san);
            if (actual != expected) errors += " " + san + "=" + actual + " (expected " + expected + ")";
        }
        Check(errors == "", "SAN matching: " + name + " (" + items.Length + " SANs)", errors);
    }

    // For every legal move in fen's position, MoveFromSan finds it again from its own SAN, with and without "+"/"#",
    // from its SAN with the from-square in full (and, for a piece capture, without the "x"), and castling with zeros.
    void CheckSanRoundTrip(string name, string fen) {
        byte[] board = new byte[64];
        int[] state = new int[ChessPosition.StateLength];
        int halfmoveClock;
        int fullmoveNumber;
        if (!ChessFen.ParseFen(fen, board, state, out halfmoveClock, out fullmoveNumber)) {
            Check(false, "SAN round trip: " + name, "FEN rejected");
            return;
        }
        int[] legalMoves = new int[ChessMoves.MaxMoves];
        int[] scratch = new int[ChessMoves.MaxMoves];
        int legalCount = ChessMoves.GenerateLegalMoves(board, state, legalMoves, true);
        string errors = "";
        int spellings = 0;
        string[] texts = new string[4];
        for (int i = 0; i < legalCount; i++) {
            int move = legalMoves[i];
            string san = ChessNotation.MoveToSan(board, state, move, legalMoves, legalCount, scratch, true);
            string bare = san.Replace("+", "").Replace("#", "");
            int count = 0;
            texts[count++] = san;
            texts[count++] = bare;
            int type = ChessRules.MovePieceType(move);
            bool capture = ChessRules.MoveCaptured(move) != ChessRules.NoPieceType;
            if (ChessRules.MoveIsCastle(move)) {
                texts[count++] = bare.Replace("O", "0");
            } else {
                string promotion = ChessRules.MovePromotion(move) == ChessRules.NoPieceType ? ""
                    : "=" + ChessRules.PieceTypeLetter(ChessRules.MovePromotion(move));
                string letter = type == ChessRules.Pawn ? "" : ChessRules.PieceTypeLetter(type);
                texts[count++] = letter + ChessRules.SquareName(ChessRules.MoveFrom(move)) + (capture ? "x" : "")
                    + ChessRules.SquareName(ChessRules.MoveTo(move)) + promotion;
                if (type != ChessRules.Pawn && capture) texts[count++] = bare.Replace("x", "");
            }
            for (int t = 0; t < count; t++) {
                spellings++;
                int found = ChessNotation.MoveFromSan(texts[t], legalMoves, legalCount);
                if (found != move) errors += " " + texts[t] + " (" + san + ") gave " + found;
            }
        }
        if (legalCount == 0) errors += " no legal moves";
        Check(errors == "", "SAN round trip: " + name + " (" + legalCount + " moves, " + spellings + " spellings)",
            errors);
    }

    void TestSanMatching() {
        CheckSanRoundTrip("start", ChessFen.StartFen);
        CheckSanRoundTrip("Kiwipete", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
        CheckSanRoundTrip("Kiwipete, Black", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R b KQkq - 0 1");
        CheckSanRoundTrip("perft 3", "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1");
        CheckSanRoundTrip("perft 4", "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1");
        CheckSanRoundTrip("perft 4 mirrored", "r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1");
        CheckSanRoundTrip("perft 5", "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8");
        CheckSanRoundTrip("promotions", "r3k3/1P4P1/8/8/8/8/1p4p1/R3K2R b KQq - 3 40");
        CheckSanRoundTrip("disambiguation", "7k/8/8/RN3N2/8/1N3N2/8/R5RK w - - 0 1");
        CheckSanRoundTrip("three queens", "8/7k/8/2Q1Q3/8/2Q5/8/K7 w - - 0 1");
        CheckSanRoundTrip("en passant", "rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3");
        CheckSanRoundTrip("Chess960 castling", "1r4kr/8/8/8/8/8/8/1R4KR w HBhb - 0 1");

        CheckSanMatches("start", ChessFen.StartFen,
            "Nf3>Nf3 Ng1f3>Nf3 Ngf3>Nf3 N1f3>Nf3 Nf3+>Nf3 Nf3#>Nf3 Nxf3>none Nhf3>none N2f3>none Nf4>none e4>e4 Pe4>e4 "
            + "e2e4>e4 e5>none exd3>none ed3>none dxe3>none Ke2>none O-O>none 0-0-0>none Nf>none Nf9>none nf3>none "
            + "e4x>none e>none f3>f3 f3=Q>none Kf3>none Bb5>none");
        CheckSanMatches("disambiguation", "7k/8/8/RN3N2/8/1N3N2/8/R5RK w - - 0 1",
            "Nd4>ambiguous Nbd4>ambiguous N5d4>ambiguous Nb5d4>Nb5d4 Nf3d4>Nf3d4 Nd6>ambiguous Nbd6>Nbd6 Nfd6>Nfd6 "
            + "Ra3>ambiguous R1a3>R1a3 R5a3>R5a3 Raa3>ambiguous Rb1>ambiguous Rab1>Rab1 Rgb1>Rgb1 Rg8>Rg8+ "
            + "Rxg8>none Nd2>ambiguous Nbd2>Nbd2 Nb3d2>Nbd2");
        CheckSanMatches("promotions", "r3k3/1P4P1/8/8/8/8/1p4p1/R3K2R b KQq - 3 40",
            "gxh1=Q>gxh1=Q+ gxh1=Q+>gxh1=Q+ gxh1>none gxh1=K>none gxh1=P>none g1=N>g1=N g1>none g1Q>none "
            + "bxa1=R>bxa1=R+ b1=Q>b1=Q+ bxa1=N>bxa1=N hxg1=Q>none Kd7>Kd7 O-O-O>none Rd8>Rd8 Rxa1>Rxa1+ Ra1>Rxa1+");
        CheckSanMatches("en passant", "rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3",
            "exf6>exf6 ef6>none exd6>none e6>e6 f6>none xf6>exf6 Pexf6>exf6 e5f6>none e5xf6>exf6");
        CheckSanMatches("Chess960 castling, king already on g1", "1r4kr/8/8/8/8/8/8/1R4KR w HBhb - 0 1",
            "O-O>O-O 0-0>O-O O-O+>O-O O-O-O>O-O-O 0-0-0>O-O-O Kg1>none Kh1>none Kb1>none Kf1>Kf1 Rf1>Rf1 O-O-O-O>none");
        CheckSanMatches("standard castling", "r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1",
            "O-O>O-O O-O-O>O-O-O Kg8>none Kc8>none Kxh8>none Ke7>Ke7");
        CheckSanMatches("three queens", "8/7k/8/2Q1Q3/8/2Q5/8/K7 w - - 0 1",
            "Qd4>ambiguous Qcd4>ambiguous Q5d4>ambiguous Qc5d4>Qc5d4 Qed4>Qed4 Q3d4>Q3d4 Qe3>ambiguous Qc5e3>Qc5e3");
    }

    // Imports pgn with ChessPgn._ImportNext into the given history arrays, as the importing behaviour will (one step
    // at a time, all in one go here). Returns the last _ImportNext result; cursor, tags and the arrays are the
    // import's. fen is the position reached (halfmove clock and move number worked out from the history), "" if the
    // position was never set up.
    int ImportAll(string pgn, int[] cursor, string[] tags, int[] moves, string[] sans, int[] clocks, out string fen) {
        byte[] board = new byte[64];
        int[] state = new int[ChessPosition.StateLength];
        int[] legalMoves = new int[ChessMoves.MaxMoves];
        int[] scratch = new int[ChessMoves.MaxMoves];
        ChessPgn._StartReading(cursor, tags);
        int result = ChessPgn.ImportMore;
        int steps = 0;
        while (result == ChessPgn.ImportMore && steps < 10000) {
            result = ChessPgn._ImportNext(tokenRegex, clockRegex, pgn, cursor, tags, board, state, moves, sans, clocks,
                legalMoves, scratch, true);
            steps++;
        }
        int moveCount = cursor[ChessPgn.CursorMoves];
        int halfmoveClock = ChessDraws._BuildHalfmoveClocks(moves, moveCount,
            cursor[ChessPgn.CursorFirstHalfmoveClock], new int[moveCount]);
        int fullmoveNumber = cursor[ChessPgn.CursorFirstMoveNumber]
            + (cursor[ChessPgn.CursorFirstSideToMove] + moveCount) / 2;
        fen = cursor[ChessPgn.CursorSetUp] == 0 ? "" : ChessFen.WriteFen(board, state, halfmoveClock, fullmoveNumber);
        return result;
    }

    // Imports pgn into a history of capacity moves and checks the result (expectedResult, and for a failure the
    // move it failed at), the number of plies played and the position reached. The SAN, clock and tags must be
    // what _ReadNext reads from the text, except that expectedSans (space-separated, null for none) replaces the SAN
    // where the board records it differently (+/# added, no extra disambiguation).
    void CheckImport(string name, string pgn, int expectedResult, string expectedFailedAt, int expectedPlies,
        string expectedFen, string expectedSans, int capacity) {
        if (pgn == null) {
            Check(false, name, "PGN file not assigned in PgnTest's pgnFiles");
            return;
        }
        int[] cursor = new int[ChessPgn.CursorLength];
        string[] tags = new string[ChessPgn.TagCount];
        int[] moves = new int[capacity];
        string[] sans = new string[capacity];
        int[] clocks = new int[capacity];
        string fen;
        int result = ImportAll(pgn, cursor, tags, moves, sans, clocks, out fen);
        int plies = cursor[ChessPgn.CursorMoves];
        string failedAt = ChessPgn._ImportFailedAt(cursor);
        string errors = "";
        if (result != expectedResult) {
            errors += " result " + result + " (" + ChessPgn._ImportErrorText(result) + ") at " + failedAt;
        }
        if (expectedResult != ChessPgn.ImportDone && failedAt != expectedFailedAt) {
            errors += " failed at " + failedAt + " (expected " + expectedFailedAt + ")";
        }
        if (plies != expectedPlies) errors += " " + plies + " plies (expected " + expectedPlies + ")";
        if (fen != expectedFen) errors += " FEN " + fen + " (expected " + expectedFen + ")";

        string[] readTags = new string[ChessPgn.TagCount];
        string[] readSans = new string[600];
        int[] readClocks = new int[600];
        int readCount;
        ReadAll(pgn, readTags, readSans, readClocks, out readCount);
        string[] expected = expectedSans == null ? readSans : expectedSans.Split(new char[] { ' ' });
        for (int i = 0; i < plies; i++) {
            if (i >= expected.Length || sans[i] != expected[i]) errors += " ply " + (i + 1) + " SAN " + sans[i];
            if (i >= readCount || clocks[i] != readClocks[i]) errors += " ply " + (i + 1) + " clock " + clocks[i];
        }
        for (int tag = 0; tag < ChessPgn.TagCount; tag++) {
            if (tags[tag] != readTags[tag]) errors += " tag " + ChessPgn._TagName(tag) + " \"" + tags[tag] + "\"";
        }
        Check(errors == "", name + " (" + plies + " plies"
            + (expectedResult == ChessPgn.ImportDone ? "" : ", failed at " + expectedFailedAt) + ")", errors);
    }

    void TestImportGames() {
        int done = ChessPgn.ImportDone;
        string siggiSan = "e4 c5 Nf3 Nc6 d3 e6 Bf4 a6 c3 b5 c4 Bb7 Nc3 bxc4 dxc4 Qb6 b3 Nd4 Nd2 Bd6 e5 Be7 Na4 Qc6 h3 f6 "
            + "f3 fxe5 Bxe5 Nf6 f4 d6 b4 dxe5 fxe5 Ne4 Nb1 O-O Bd3 Nf2 Rf1 Nxd1 Kxd1 Rxf1+ Kd2 Rf2+ Ke3 Bg5+ Kxf2 "
            + "Qxg2+ Ke1 Rf8 Bf1 Rxf1#";
        CheckImport("import game: Siggi-vs-Nuka (SAN as the Java exports it)", PgnText("Siggi-vs-Nuka.pgn"), done, "",
            54, "6k1/1b4pp/p3p3/2p1P1b1/NPPn4/7P/P5q1/RN2Kr2 w - - 0 28", siggiSan, 600);
        CheckImport("import game: lichess-long-draw", PgnText("lichess-long-draw.pgn"), done, "", 142,
            "8/8/8/3k4/8/4K3/8/8 w - - 0 72", null, 600);
        CheckImport("import game: en-passant", PgnText("en-passant.pgn"), done, "", 10,
            "rnbqkbnr/2p1pppp/p2P4/8/8/P1p5/1P1P1PPP/RNBQKBNR w KQkq - 0 6", null, 600);
        CheckImport("import game: promotions", PgnText("promotions.pgn"), done, "", 9,
            "Q4Q2/8/8/4k3/8/8/3K4/n6q w - - 3 45", null, 600);
        CheckImport("import game: disambiguation", PgnText("disambiguation.pgn"), done, "", 6,
            "8/7k/8/R4N2/3N4/R4N2/3N4/6RK w - - 6 4", null, 600);
        CheckImport("import game: chess960", PgnText("chess960.pgn"), done, "", 6,
            "q1krbnrn/pppp1ppp/5b2/4p3/4P3/5B2/PPPP1PPP/Q1KRBNRN w - - 4 4", null, 600);
        CheckImport("import game: stalemate", PgnText("stalemate.pgn"), done, "", 19,
            "5bnr/4p1pq/4Qpkr/7p/7P/4P3/PPPP1PP1/RNB1KBNR b KQ - 2 10", null, 600);
        CheckImport("import game: repetition-names", PgnText("repetition-names.pgn"), done, "", 8,
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 8 5", null, 600);
        CheckImport("import game: checkmate", PgnText("checkmate.pgn"), done, "", 4,
            "rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3", null, 600);
        CheckImport("import game: formatting", PgnText("formatting.pgn"), done, "", 24,
            "3k1bnr/ppp2ppp/4p3/5b2/4pB2/P7/1PP2PPP/2K2BNR w - - 0 13", null, 600);

        // A history with room for exactly the game, and for one move less.
        CheckImport("import game: checkmate with room for 4 moves", PgnText("checkmate.pgn"), done, "", 4,
            "rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3", null, 4);
        CheckImport("import game: checkmate with room for 3 moves fails", PgnText("checkmate.pgn"),
            ChessPgn.ImportTooLong, "2...", 3, "rnbqkbnr/pppp1ppp/8/4p3/6P1/5P2/PPPPP2P/RNBQKBNR b KQkq g3 0 2", null,
            3);
    }

    void TestImportFailures() {
        int done = ChessPgn.ImportDone;
        string afterNf3 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2";

        // The malformed test set files, each having played the moves before its failure.
        CheckImport("import failure: malformed/illegal-move", PgnText("illegal-move.pgn"), ChessPgn.ImportIllegalMove,
            "2...", 3, "rnbqkbnr/ppppp1pp/8/5p1Q/4P3/8/PPPP1PPP/RNB1KBNR b KQkq - 1 2", null, 600);
        CheckImport("import failure: malformed/ambiguous-move", PgnText("ambiguous-move.pgn"),
            ChessPgn.ImportAmbiguousMove, "5.", 8, "1nbqkbn1/1pppppp1/r6r/p6p/P6P/R6R/1PPPPPP1/1NBQKBN1 w - - 4 5",
            null, 600);
        CheckImport("import failure: malformed/garbage", PgnText("garbage.pgn"), ChessPgn.ImportBadSyntax, "2...", 3,
            afterNf3, null, 600);
        CheckImport("import failure: malformed/unterminated-comment", PgnText("unterminated-comment.pgn"),
            ChessPgn.ImportBadSyntax, "2...", 3, afterNf3, null, 600);

        // Games with no moves still set up their position.
        CheckImport("import: no moves", "[White \"A\"]\n\n*", done, "", 0, ChessFen.StartFen, null, 600);
        CheckImport("import: empty text", "", done, "", 0, ChessFen.StartFen, null, 600);
        CheckImport("import: no moves from a FEN", "[FEN \"4k3/8/8/8/8/8/8/4K3 b - - 7 31\"]\n\n*", done, "", 0,
            "4k3/8/8/8/8/8/8/4K3 b - - 7 31", null, 600);

        // Failures are numbered from the starting position.
        CheckImport("import failure: illegal first move", "1. e5 *", ChessPgn.ImportIllegalMove, "1.", 0,
            ChessFen.StartFen, null, 600);
        CheckImport("import failure: illegal first move, Black to move at 40",
            "[FEN \"4k3/8/8/8/8/8/8/4K3 b - - 7 40\"]\n\n40... Ke2 *", ChessPgn.ImportIllegalMove, "40...", 0,
            "4k3/8/8/8/8/8/8/4K3 b - - 7 40", null, 600);
        CheckImport("import failure: Black to move at 40, fails at 41.",
            "[FEN \"4k3/8/8/8/8/8/8/4K3 b - - 7 40\"]\n\n40... Kd7 41. Ke9 *", ChessPgn.ImportBadSyntax, "41.", 1,
            "8/3k4/8/8/8/8/8/4K3 w - - 8 41", null, 600);
        CheckImport("import failure: syntax error before the first move, from a FEN",
            "[FEN \"4k3/8/8/8/8/8/8/4K3 b - - 7 40\"]\n\nhello *", ChessPgn.ImportBadSyntax, "40...", 0,
            "4k3/8/8/8/8/8/8/4K3 b - - 7 40", null, 600);

        // An invalid FEN fails before the first move, unless the text fails first.
        CheckImport("import failure: invalid FEN", "[FEN \"4k3/8/8/8/8/8/8/8 w - - 0 1\"]\n\n1. Kd2 *",
            ChessPgn.ImportBadFen, "1.", 0, "", null, 600);
        CheckImport("import failure: invalid FEN, no moves", "[FEN \"x\"]\n\n*", ChessPgn.ImportBadFen, "1.", 0, "",
            null, 600);
        CheckImport("import failure: syntax error in the tags after an invalid FEN",
            "[FEN \"x\"]\n[White \"A]\n\n1. e4 *", ChessPgn.ImportBadSyntax, "1.", 0, "", null, 600);

        // Moves that read but don't match.
        CheckImport("import failure: missing promotion", "[FEN \"4k3/P7/8/8/8/8/8/4K3 w - - 0 1\"]\n\n1. a8 *",
            ChessPgn.ImportIllegalMove, "1.", 0, "4k3/P7/8/8/8/8/8/4K3 w - - 0 1", null, 600);
        CheckImport("import failure: castling through check",
            "[FEN \"4k3/8/8/8/8/8/5r2/4K2R w K - 0 1\"]\n\n1. O-O *", ChessPgn.ImportIllegalMove, "1.", 0,
            "4k3/8/8/8/8/8/5r2/4K2R w K - 0 1", null, 600);
        CheckImport("import failure: pawn capture without x", "1. e4 d5 2. ed5 *", ChessPgn.ImportIllegalMove, "2.",
            2, "rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2", null, 600);

        // The SAN is recorded as the board writes it.
        CheckImport("import: SAN recorded with + and #, piece capture without x",
            "1. e4 e5 2. Bc4 Nc6 3. Qh5 Nf6 4. Qf7 *", done, "", 7,
            "r1bqkb1r/pppp1Qpp/2n2n2/4p3/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 0 4", "e4 e5 Bc4 Nc6 Qh5 Nf6 Qxf7#", 600);
        CheckImport("import: over-disambiguated SAN recorded as the board writes it",
            "1. Ng1f3 Nb8c6 2. e2e4 e7xe5 *", ChessPgn.ImportIllegalMove, "2...", 3,
            "r1bqkbnr/pppppppp/2n5/8/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq e3 0 2", "Nf3 Nc6 e4", 600);
    }

    // ---- 5.4 ----

    // The tag lines the port writes for every imported game, which the import doesn't keep (docs/testing.md).
    const string PortRosterLines = "+[Event \"?\"]\n+[Site \"?\"]\n+[Date \"????.??.??\"]\n+[Round \"-\"]\n";

    void TestRoundTrip() {
        string standardOnly = PortRosterLines + "+[TimeControl \"?\"]\n";
        string ana = "-[Termination \"Ana \\\"The Rook\\\" \\\\ Ñandú 🐴 won by checkmate\"]\n";
        CheckRoundTrip("Siggi-vs-Nuka", ChessGame.EndingCheckmate, ChessRules.Black,
            "-[Site \"VRChess\"]\n-[Date \"2025.7.10\"]\n-[Termination \"Nukaǃ won by checkmate\"]\n"
            + PortRosterLines + "+[Termination \"checkmate\"]\n");
        CheckRoundTrip("lichess-long-draw", ChessGame.EndingInsufficientMaterial, ChessRules.NoColor,
            "-[Event \"rated bullet game\"]\n-[Site \"https://lichess.org/h0rrawli\"]\n-[Date \"2026.10.10\"]\n"
            + "-[GameId \"h0rrawli\"]\n-[UTCDate \"2026.10.10\"]\n-[UTCTime \"18:22:10\"]\n-[WhiteElo \"3043\"]\n"
            + "-[BlackElo \"2988\"]\n-[WhiteRatingDiff \"-1\"]\n-[BlackRatingDiff \"+1\"]\n-[WhiteTitle \"FM\"]\n"
            + "-[Variant \"Standard\"]\n-[TimeControl \"60+0\"]\n-[ECO \"B15\"]\n"
            + "-[Opening \"Caro-Kann Defense: Gurgenidze System\"]\n-[Termination \"Normal\"]\n"
            + "+[Event \"?\"]\n+[Site \"?\"]\n+[Date \"????.??.??\"]\n+[TimeControl \"60\"]\n"
            + "+[Termination \"insufficient material\"]\n");
        CheckRoundTrip("en-passant", ChessGame.EndingNone, ChessRules.NoColor, standardOnly);
        CheckRoundTrip("promotions", ChessGame.EndingNone, ChessRules.NoColor, standardOnly);
        CheckRoundTrip("disambiguation", ChessGame.EndingNone, ChessRules.NoColor, standardOnly);
        CheckRoundTrip("chess960", ChessGame.EndingNone, ChessRules.NoColor, standardOnly);
        CheckRoundTrip("stalemate", ChessGame.EndingStalemate, ChessRules.NoColor,
            "-[Termination \"Draw by stalemate\"]\n" + standardOnly + "+[Termination \"stalemate\"]\n");
        CheckRoundTrip("repetition-names", ChessGame.EndingThreefoldRepetition, ChessRules.NoColor,
            "-[Termination \"Draw by threefold repetition\"]\n" + standardOnly
            + "+[Termination \"threefold repetition\"]\n");
        CheckRoundTrip("checkmate", ChessGame.EndingCheckmate, ChessRules.Black,
            ana + standardOnly + "+[Termination \"checkmate\"]\n");
        // The Java can't read formatting.pgn.txt; its export is only re-imported. Its clock history has -1 entries.
        CheckRoundTrip("formatting", ChessGame.EndingNone, ChessRules.NoColor, null);

        // A hand-written game exported and re-imported: names with " and \ and an emoji, a Chess960 start, a clock
        // history with tenths, hours and unknown entries, a Game ID and a continued game's ContinuedFrom (which the
        // import doesn't read).
        string pgn = ChessPgn.WriteTags("Casual game", "VRChat", "2026.10.11", "Ana \"The Rook\" \\ Ñandú 🐴",
                "\\\"\\", ChessGame.EndingResignation, ChessRules.White, ChessGame.ModeChess960,
                "qrkbbnrn/pppppppp/8/8/8/8/PPPPPPPP/QRKBBNRN w GBgb - 0 1", 5400, 30, "20261011120000-000001",
                "20261010120000-000002/4")
            + "\n" + ChessPgn.WriteMoveText(new string[] { "e4", "e5", "Bf3", "Bf6", "O-O-O", "O-O-O" },
                new int[] { 5400000, -1, 5399900, 3723400, -1, 0 }, 6, 1, ChessRules.White, ChessGame.EndingResignation,
                ChessRules.White, ChessPgn.LineWidth);
        int[] cursor = new int[ChessPgn.CursorLength];
        string[] tags = new string[ChessPgn.TagCount];
        int[] moves = new int[600];
        string[] sans = new string[600];
        int[] clocks = new int[600];
        string fen;
        int result = ImportAll(pgn, cursor, tags, moves, sans, clocks, out fen);
        string errors = "";
        if (result != ChessPgn.ImportDone || cursor[ChessPgn.CursorMoves] != 6) {
            errors += " result " + result + ", " + cursor[ChessPgn.CursorMoves] + " plies";
        }
        if (fen != "q1krbnrn/pppp1ppp/5b2/4p3/4P3/5B2/PPPP1PPP/Q1KRBNRN w - - 4 4") errors += " FEN " + fen;
        string[] expectedSans = new string[] { "e4", "e5", "Bf3", "Bf6", "O-O-O", "O-O-O" };
        int[] expectedClocks = new int[] { 5400000, -1, 5399900, 3723400, -1, 0 };
        for (int i = 0; i < 6; i++) {
            if (sans[i] != expectedSans[i] || clocks[i] != expectedClocks[i]) {
                errors += " ply " + (i + 1) + ": " + sans[i] + " " + clocks[i];
            }
        }
        string[] expectedTags = Tags("qrkbbnrn/pppppppp/8/8/8/8/PPPPPPPP/QRKBBNRN w GBgb - 0 1", "Chess960",
            "Ana \"The Rook\" \\ Ñandú 🐴", "\\\"\\", "1-0", "20261011120000-000001", "5400+30");
        for (int tag = 0; tag < ChessPgn.TagCount; tag++) {
            if (tags[tag] != expectedTags[tag]) errors += " " + ChessPgn._TagName(tag) + " \"" + tags[tag] + "\"";
        }
        Check(errors == "", "round trip: hand-written Chess960 game with clocks and escaped names", errors + "\n" + pgn);
    }

    // The port's export of an imported game, as the board will write it for a loaded game: the tags the import kept
    // (Event, Site and Date aren't kept, so they're unknown), with ending and winner given by the caller, and the
    // recorded SAN and clocks.
    string ExportImported(int[] cursor, string[] tags, string[] sans, int[] clocks, int ending, int winner) {
        int baseSeconds;
        int incrementSeconds;
        ChessPgn._ParseTimeControl(tags[ChessPgn.TagTimeControl], out baseSeconds, out incrementSeconds);
        return ChessPgn.WriteTags("", "", "", tags[ChessPgn.TagWhite], tags[ChessPgn.TagBlack], ending, winner,
                ChessPgn._ModeFromVariant(tags[ChessPgn.TagVariant]), tags[ChessPgn.TagFen], baseSeconds,
                incrementSeconds, tags[ChessPgn.TagGameId], "")
            + "\n" + ChessPgn.WriteMoveText(sans, clocks, cursor[ChessPgn.CursorMoves],
                cursor[ChessPgn.CursorFirstMoveNumber], cursor[ChessPgn.CursorFirstSideToMove], ending, winner,
                ChessPgn.LineWidth);
    }

    // The lines of a PGN's tag section (up to the first empty line), without line ends.
    string[] TagLines(string pgn) {
        string[] lines = pgn.Replace("\r", "").Split(new char[] { '\n' });
        int count = 0;
        while (count < lines.Length && lines[count] != "") count++;
        string[] tagLines = new string[count];
        for (int i = 0; i < count; i++) tagLines[i] = lines[i];
        return tagLines;
    }

    bool HasLine(string[] lines, string line) {
        for (int i = 0; i < lines.Length; i++) {
            if (lines[i] == line) return true;
        }
        return false;
    }

    // The tag lines only the Java's export has ("-" and the line), in its order, then those only the port's has
    // ("+"), in its order; one per line. Lines both have, in any order, aren't listed.
    string TagDiff(string javaPgn, string portPgn) {
        string[] java = TagLines(javaPgn);
        string[] port = TagLines(portPgn);
        string diff = "";
        for (int i = 0; i < java.Length; i++) {
            if (!HasLine(port, java[i])) diff += "-" + java[i] + "\n";
        }
        for (int i = 0; i < port.Length; i++) {
            if (!HasLine(java, port[i])) diff += "+" + port[i] + "\n";
        }
        return diff;
    }

    int CountChar(string text, char c) {
        int count = 0;
        for (int i = 0; i < text.Length; i++) {
            if (text[i] == c) count++;
        }
        return count;
    }

    // Imports test set file name, exports it as the board will (ExportImported, with the game's ending and winner),
    // and checks:
    // - against the Java's export of the same file (java-export/<name>.java.pgn.txt; javaTagDiff null for a file
    //   the Java can't read): the moves read from both exports are the same SAN, +/# included; the clocks read
    //   from the port's export are the file's, and from the Java's the file's except the last ply, whose comment
    //   the Java drops; the Java's export has one comment per clock but that one (the test set's files have no
    //   other comments); the tag lines differ by exactly javaTagDiff (TagDiff); and the Java's export imports to
    //   the same game;
    // - that re-importing the port's export gives the same game: result, moves, recorded SAN, clocks (tenths kept,
    //   -1 staying -1), starting position and its numbering, final position, and the White, Black, Result, FEN,
    //   GameID tags, the mode and the time control.
    void CheckRoundTrip(string name, int ending, int winner, string javaTagDiff) {
        string source = PgnText(name + ".pgn");
        string javaExport = javaTagDiff == null ? "" : PgnText(name + ".java.pgn");
        if (source == null || javaExport == null) {
            Check(false, "round trip: " + name, "PGN file not assigned in PgnTest's pgnFiles");
            return;
        }
        int[] cursor = new int[ChessPgn.CursorLength];
        string[] tags = new string[ChessPgn.TagCount];
        int[] moves = new int[600];
        string[] sans = new string[600];
        int[] clocks = new int[600];
        string fen;
        int result = ImportAll(source, cursor, tags, moves, sans, clocks, out fen);
        int plies = cursor[ChessPgn.CursorMoves];
        string export = ExportImported(cursor, tags, sans, clocks, ending, winner);
        string errors = "";
        if (result != ChessPgn.ImportDone) errors += " source import " + ChessPgn._ImportErrorText(result);
        if (ChessPgn.ResultText(ending, winner) != tags[ChessPgn.TagResult]) {
            errors += " Result " + tags[ChessPgn.TagResult];
        }

        string[] readTags = new string[ChessPgn.TagCount];
        string[] portSans = new string[600];
        int[] portClocks = new int[600];
        int portCount;
        if (ReadAll(export, readTags, portSans, portClocks, out portCount) != ChessPgn.ReadEnd || portCount != plies) {
            errors += " port export reads " + portCount + " moves";
        }
        int knownClocks = 0;
        for (int i = 0; i < plies; i++) {
            if (clocks[i] >= 0) knownClocks++;
            if (portClocks[i] != clocks[i]) errors += " port export ply " + (i + 1) + " clock " + portClocks[i];
        }
        if (CountChar(export, '{') != knownClocks) errors += " port export has other comments";

        if (javaTagDiff != null) {
            string[] javaSans = new string[600];
            int[] javaClocks = new int[600];
            int javaCount;
            if (ReadAll(javaExport, readTags, javaSans, javaClocks, out javaCount) != ChessPgn.ReadEnd
                || javaCount != plies) {
                errors += " Java export reads " + javaCount + " moves";
            }
            for (int i = 0; i < plies && i < javaCount; i++) {
                if (javaSans[i] != portSans[i]) {
                    errors += " ply " + (i + 1) + " Java " + javaSans[i] + " port " + portSans[i];
                }
                int expectedClock = i == plies - 1 ? -1 : clocks[i];
                if (javaClocks[i] != expectedClock) errors += " Java export ply " + (i + 1) + " clock " + javaClocks[i];
            }
            int javaComments = plies > 0 && clocks[plies - 1] >= 0 ? knownClocks - 1 : knownClocks;
            if (CountChar(javaExport, '{') != javaComments) {
                errors += " Java export has " + CountChar(javaExport, '{') + " comments, expected " + javaComments;
            }
            string diff = TagDiff(javaExport, export);
            if (diff != javaTagDiff) errors += " tag differences:\n" + diff + "expected:\n" + javaTagDiff;

            int[] javaCursor = new int[ChessPgn.CursorLength];
            string[] javaTags = new string[ChessPgn.TagCount];
            int[] javaMoves = new int[600];
            string javaFen;
            int javaResult = ImportAll(javaExport, javaCursor, javaTags, javaMoves, new string[600], new int[600],
                out javaFen);
            if (javaResult != ChessPgn.ImportDone || javaCursor[ChessPgn.CursorMoves] != plies || javaFen != fen) {
                errors += " Java export imports to " + javaCursor[ChessPgn.CursorMoves] + " plies, " + javaFen;
            }
            for (int i = 0; i < plies; i++) {
                if (javaMoves[i] != moves[i]) errors += " Java export ply " + (i + 1) + " move " + javaMoves[i];
            }
        }

        int[] cursor2 = new int[ChessPgn.CursorLength];
        string[] tags2 = new string[ChessPgn.TagCount];
        int[] moves2 = new int[600];
        string[] sans2 = new string[600];
        int[] clocks2 = new int[600];
        string fen2;
        int result2 = ImportAll(export, cursor2, tags2, moves2, sans2, clocks2, out fen2);
        if (result2 != ChessPgn.ImportDone) errors += " re-import " + ChessPgn._ImportErrorText(result2);
        for (int i = ChessPgn.CursorMoves; i < ChessPgn.CursorLength; i++) {
            if (i != ChessPgn.CursorInMoveText && cursor2[i] != cursor[i]) errors += " re-import cursor " + i;
        }
        if (fen2 != fen) errors += " re-import FEN " + fen2;
        for (int i = 0; i < plies; i++) {
            if (moves2[i] != moves[i] || sans2[i] != sans[i] || clocks2[i] != clocks[i]) {
                errors += " re-import ply " + (i + 1) + ": " + sans2[i] + " " + clocks2[i];
            }
        }
        int[] sameTags = new int[] {
            ChessPgn.TagFen, ChessPgn.TagWhite, ChessPgn.TagBlack, ChessPgn.TagResult, ChessPgn.TagGameId
        };
        for (int i = 0; i < sameTags.Length; i++) {
            if (tags2[sameTags[i]] != tags[sameTags[i]]) {
                errors += " re-import " + ChessPgn._TagName(sameTags[i]) + " \"" + tags2[sameTags[i]] + "\"";
            }
        }
        if (ChessPgn._ModeFromVariant(tags2[ChessPgn.TagVariant])
            != ChessPgn._ModeFromVariant(tags[ChessPgn.TagVariant])) {
            errors += " re-import mode";
        }
        int base1;
        int increment1;
        int base2;
        int increment2;
        ChessPgn._ParseTimeControl(tags[ChessPgn.TagTimeControl], out base1, out increment1);
        ChessPgn._ParseTimeControl(tags2[ChessPgn.TagTimeControl], out base2, out increment2);
        if (base1 != base2 || increment1 != increment2) {
            errors += " re-import TimeControl " + tags2[ChessPgn.TagTimeControl];
        }

        Check(errors == "", "round trip: " + name + " (" + plies + " plies" + (javaTagDiff == null ? ", port only" : "")
            + ")", errors + "\n" + export);
    }

    // ---- 5.3 ----

    // The multi-frame imports, run one after another: each file is imported in one go and then over frames with
    // the given budget, and the two must give the same result. minFrames is the fewest frames the budgeted import
    // must take.
    string[] frameFiles;
    float[] frameBudgets;
    int[] frameResults;
    int[] framePlies;
    int[] frameMinFrames;
    int frameIndex;
    string frameOneGo;
    double frameOneGoMs;
    bool abandonedEventSent;

    void StartFrameImports() {
        if (importer == null) {
            Check(false, "importer: multi-frame imports", "no ChessPgnImporter assigned to PgnTest's importer");
            LogSummary();
            return;
        }
        // lichess-long-draw with the normal budget, Siggi-vs-Nuka with a small one, and two at one step per frame:
        // formatting (clocks, comments and variations between moves) and a failure.
        frameFiles = new string[] { "lichess-long-draw.pgn", "Siggi-vs-Nuka.pgn", "formatting.pgn", "illegal-move.pgn" };
        frameBudgets = new float[] { ChessPgnImporter.DefaultBudgetMs, 1f, 0f, 0f };
        frameResults = new int[] {
            ChessPgn.ImportDone, ChessPgn.ImportDone, ChessPgn.ImportDone, ChessPgn.ImportIllegalMove
        };
        framePlies = new int[] { 142, 54, 24, 3 };
        frameMinFrames = new int[] { 2, 2, 24, 4 };
        frameIndex = -1;
        NextFrameImport();
    }

    // Starts the next multi-frame import, or the abandon check after the last.
    void NextFrameImport() {
        while (true) {
            frameIndex++;
            if (frameIndex >= frameFiles.Length) {
                StartAbandonCheck();
                return;
            }
            string pgn = PgnText(frameFiles[frameIndex]);
            if (pgn == null) {
                Check(false, "importer: " + frameFiles[frameIndex], "PGN file not assigned in PgnTest's pgnFiles");
                continue;
            }
            importer._StartImport(pgn, 600, frameBudgets[frameIndex], null, "");
            importer._ImportFor(float.MaxValue);
            frameOneGo = ImporterSummary();
            frameOneGoMs = importer.totalMs;
            Check(importer.result == frameResults[frameIndex] && importer.MoveCount() == framePlies[frameIndex]
                && importer.frames == 1 && !importer.importing,
                "importer: " + frameFiles[frameIndex] + " in one go",
                "result " + importer.result + ", " + importer.MoveCount() + " plies, " + importer.frames
                + " frames, importing " + importer.importing);
            importer._StartImport(pgn, 600, frameBudgets[frameIndex], this, nameof(OnFrameImportDone));
            return;
        }
    }

    public void OnFrameImportDone() {
        string name = frameFiles[frameIndex];
        string summary = ImporterSummary();
        string errors = "";
        if (summary != frameOneGo) errors += " differs from the import in one go:\n" + summary + "\n" + frameOneGo;
        if (importer.frames < frameMinFrames[frameIndex]) {
            errors += " only " + importer.frames + " frames (expected at least " + frameMinFrames[frameIndex] + ")";
        }
        if (importer.importing) errors += " still importing";
        Check(errors == "", "importer: " + name + " over frames with a " + frameBudgets[frameIndex]
            + " ms budget gives the same result", errors);
        Debug.Log("[PgnTest] importer timing: " + name + ": " + importer.MoveCount() + " plies, budget "
            + frameBudgets[frameIndex] + " ms: " + importer.frames + " frames, longest frame "
            + importer.longestFrameMs.ToString("F2") + " ms, total " + importer.totalMs.ToString("F2")
            + " ms; in one go " + frameOneGoMs.ToString("F2") + " ms");
        NextFrameImport();
    }

    // The importer's result as text, for comparing two imports of the same PGN: the result, the failing move, the
    // plies played, the position reached, every ply's move, SAN and clock, and the tags.
    string ImporterSummary() {
        int moveCount = importer.MoveCount();
        int[] cursor = importer.cursor;
        int halfmoveClock = ChessDraws._BuildHalfmoveClocks(importer.moves, moveCount,
            cursor[ChessPgn.CursorFirstHalfmoveClock], new int[moveCount]);
        int fullmoveNumber = cursor[ChessPgn.CursorFirstMoveNumber]
            + (cursor[ChessPgn.CursorFirstSideToMove] + moveCount) / 2;
        string summary = importer.result + " " + (importer.result == ChessPgn.ImportDone ? "" : importer._FailedAt())
            + " " + moveCount + " " + ChessFen.WriteFen(importer.board, importer.state, halfmoveClock, fullmoveNumber)
            + " " + importer._StartFen();
        for (int i = 0; i < moveCount; i++) {
            summary += " " + importer.moves[i] + "/" + importer.sans[i] + "/" + importer.clocks[i];
        }
        for (int tag = 0; tag < ChessPgn.TagCount; tag++) summary += " [" + importer.tags[tag] + "]";
        return summary;
    }

    // An abandoned import stops where it is and sends no event.
    void StartAbandonCheck() {
        abandonedEventSent = false;
        importer._StartImport(PgnText("lichess-long-draw.pgn"), 600, 0f, this, nameof(OnAbandonedImportDone));
        importer._ImportFor(0f);
        importer._Abandon();
        SendCustomEventDelayedFrames(nameof(FinishAbandonCheck), 3);
    }

    public void OnAbandonedImportDone() {
        abandonedEventSent = true;
    }

    public void FinishAbandonCheck() {
        Check(!abandonedEventSent && !importer.importing && importer.frames == 1 && importer.MoveCount() == 1
                && importer.result == ChessPgn.ImportMore,
            "importer: an abandoned import stops and sends no event",
            "event " + abandonedEventSent + ", importing " + importer.importing + ", " + importer.frames + " frames, "
            + importer.MoveCount() + " moves, result " + importer.result);
        LogSummary();
    }
}
