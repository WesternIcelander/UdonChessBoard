using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

// Step 12.13 test behaviour (PLAN.md 12.13, docs/board-2d.md): the 2D board view, ChessBoard2DView. Stepping is
// checked on its own (StepTarget, with five boards) and on the scene's boards through the view: which board it
// shows, that it listens to that one only, its pieces, highlights, board label and texts against the board and the
// board's UI's texts, as moves are played and in replay. The boards are synced, so like BoardTest it runs only with
// no one else in the instance, once BoardTest, ViewTest and ArchiveTest have finished with the boards. The view's
// Synced is turned off for the checks (nothing is sent) and put back. Logs "[Board2DTest] PASS: <check>" or
// "[Board2DTest] FAIL: <check>: <details>" per check, then a summary line. Built in the scene by Tools > Chess Board >
// Create 2D View Test (Editor/Board2DTestBuilder.cs).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class Board2DTest : UdonSharpBehaviour {
    public ChessBoard2DView view;
    // Waited for, since they change the boards; none to start at once.
    public BoardTest boardTest;
    public ViewTest viewTest;
    public ArchiveTest archiveTest;

    const string Ana = "2D Ana";
    const string Bo = "2D Bo";

    int passed;
    int failed;
    bool joined;
    bool ran;
    int waitedFrames;
    bool savedSynced;
    ChessBoard first;
    ChessBoard second;

    void Start() {
        SendCustomEventDelayedFrames(nameof(WaitForTests), 1);
    }

    public override void OnPlayerJoined(VRCPlayerApi player) {
        if (player.isLocal) joined = true;
    }

    public void WaitForTests() {
        if (ran) return;
        if (!joined || (Utilities.IsValid(boardTest) && !boardTest.finished)
            || (Utilities.IsValid(viewTest) && !viewTest.finished)
            || (Utilities.IsValid(archiveTest) && !archiveTest.finished)) {
            waitedFrames++;
            if (waitedFrames % 3000 == 0) Debug.Log("[Board2DTest] still waiting for the other tests to finish");
            SendCustomEventDelayedFrames(nameof(WaitForTests), 1);
            return;
        }
        ran = true;
        if (VRCPlayerApi.GetPlayerCount() > 1) {
            Debug.Log("[Board2DTest] skipped: other players are in the instance");
            return;
        }
        RunStepTargetChecks();
        if (!Utilities.IsValid(view)) {
            Check(false, "view assigned", "assign Board2DTest's View (Tools > Chess Board > Create 2D View Test)");
            LogSummary();
            return;
        }
        RunWiringChecks();
        if (view.boards == null || view.boards.Length < 2 || !Utilities.IsValid(view.boards[0])
            || !Utilities.IsValid(view.boards[1])) {
            Check(false, "view: two boards", "the stepping and drawing checks need the view's first two boards");
            LogSummary();
            return;
        }
        first = view.boards[0];
        second = view.boards[1];
        savedSynced = view.synced;
        view.synced = false;
        ResetBoth();
        RunSteppingChecks();
        RunDrawingChecks();
        // A move left for the view to draw by itself, checked two frames on.
        view.Show(0);
        CheckInt(first.PieceDroppedAs(Bo, Sq("g7"), Sq("g6")), ChessBoard.InputAccepted, "frames: g6 played");
        SendCustomEventDelayedFrames(nameof(CheckDrawnLater), 2);
    }

    // ---- Stepping on its own (five boards) ----

    void RunStepTargetChecks() {
        // Boards 1 to 5 with only 2 and 4 in use (indices 1 and 3).
        bool[] inUse = { false, true, false, true, false };
        CheckInt(ChessBoard2DView.StepTarget(inUse, 1, 1), 3, "step: right from 2 to 4");
        CheckInt(ChessBoard2DView.StepTarget(inUse, 3, 1), 1, "step: right from 4 wraps to 2");
        CheckInt(ChessBoard2DView.StepTarget(inUse, 3, -1), 1, "step: left from 4 to 2");
        CheckInt(ChessBoard2DView.StepTarget(inUse, 1, -1), 3, "step: left from 2 wraps to 4");
        CheckInt(ChessBoard2DView.StepTarget(inUse, 0, 1), 1, "step: right from reset 1 to 2");
        CheckInt(ChessBoard2DView.StepTarget(inUse, 0, -1), 3, "step: left from reset 1 wraps to 4");
        CheckInt(ChessBoard2DView.StepTarget(inUse, 4, 1), 1, "step: right from reset 5 wraps to 2");
        CheckInt(ChessBoard2DView.StepTarget(inUse, 2, -1), 1, "step: left from reset 3 to 2");
        CheckInt(ChessBoard2DView.StepTarget(inUse, -1, 1), 1, "step: right from none to 2");
        CheckInt(ChessBoard2DView.StepTarget(inUse, -1, -1), 3, "step: left from none to 4");
        bool[] none = { false, false, false, false, false };
        CheckInt(ChessBoard2DView.StepTarget(none, 3, 1), -1, "step: all reset, right does nothing");
        CheckInt(ChessBoard2DView.StepTarget(none, 3, -1), -1, "step: all reset, left does nothing");
        bool[] onlyFour = { false, false, false, true, false };
        CheckInt(ChessBoard2DView.StepTarget(onlyFour, 3, 1), -1, "step: only the board shown in use, right stays");
        CheckInt(ChessBoard2DView.StepTarget(onlyFour, 3, -1), -1, "step: only the board shown in use, left stays");
        CheckInt(ChessBoard2DView.StepTarget(onlyFour, 1, 1), 3, "step: from reset 2 to the only one in use");
        CheckInt(ChessBoard2DView.StepTarget(new bool[0], 0, 1), -1, "step: no boards");
    }

    // ---- Wiring ----

    void RunWiringChecks() {
        Check(view.squareImages != null && view.squareImages.Length == 64, "wiring: 64 squares", "wrong count");
        Check(view.pieceImages != null && view.pieceImages.Length == 64, "wiring: 64 piece images", "wrong count");
        bool squaresOk = view.squareImages != null && view.pieceImages != null && view.squareImages.Length == 64
            && view.pieceImages.Length == 64;
        for (int square = 0; squaresOk && square < 64; square++) {
            if (!Utilities.IsValid(view.squareImages[square]) || !Utilities.IsValid(view.pieceImages[square])) {
                squaresOk = false;
            }
        }
        Check(squaresOk, "wiring: every square's images assigned", "one missing");
        bool spritesOk = view.whiteSprites != null && view.blackSprites != null && view.whiteSprites.Length == 6
            && view.blackSprites.Length == 6;
        for (int i = 0; spritesOk && i < 6; i++) {
            if (view.whiteSprites[i] == null || view.blackSprites[i] == null) spritesOk = false;
        }
        Check(spritesOk, "wiring: 6 sprites per colour", "one missing");
        if (spritesOk) {
            bool distinct = true;
            for (int i = 0; i < 6; i++) {
                for (int j = 0; j < 6; j++) {
                    if (view.whiteSprites[i] == view.blackSprites[j]) distinct = false;
                    if (i != j && view.whiteSprites[i] == view.whiteSprites[j]) distinct = false;
                }
            }
            Check(distinct, "wiring: the 12 sprites all different", "a sprite used twice");
        }
        Check(Utilities.IsValid(view.boardLabel) && Utilities.IsValid(view.playersLabel)
            && Utilities.IsValid(view.statusLabel), "wiring: board label, players and status texts", "one missing");
    }

    // ---- Stepping through the view ----

    void RunSteppingChecks() {
        string s = "stepping";
        view.Show(0);
        CheckShown(0, s + ": shows the first board");
        view.PressRight();
        CheckShown(0, s + ": all reset, right does nothing");
        view.PressLeft();
        CheckShown(0, s + ": all reset, left does nothing");

        CheckInt(second.JoinAs(ChessRules.White, Ana), ChessBoard.InputAccepted, s + ": Ana joins the second board");
        view.PressRight();
        CheckShown(1, s + ": right to the second board, in use");
        view.PressRight();
        CheckShown(1, s + ": the only board in use, right stays");
        view.PressLeft();
        CheckShown(1, s + ": the only board in use, left stays");

        CheckInt(first.JoinAs(ChessRules.White, Ana), ChessBoard.InputAccepted, s + ": Ana joins the first board");
        view.PressRight();
        CheckShown(0, s + ": right from the second wraps to the first");
        view.PressLeft();
        CheckShown(1, s + ": left from the first wraps to the second");

        CheckInt(second.ResetBoardAs(Ana, true), ChessBoard.InputAccepted, s + ": the second board reset while shown");
        view.Refresh();
        CheckShown(1, s + ": the second board still shown once reset");
        CheckString(view.statusLabel.text, "Join White or Black to play", s + ": the reset board's status");
        view.PressRight();
        CheckShown(0, s + ": right to the first board, the only one in use");
        view.PressLeft();
        CheckShown(0, s + ": left stays on the first, the second reset");
    }

    // The view shows boards[index], listening to it and not to the other board.
    void CheckShown(int index, string name) {
        CheckInt(view.ShownIndex(), index, name);
        ChessBoard board = view.boards[index];
        ChessBoard other = view.boards[1 - index];
        Check(IsListening(board) && !IsListening(other), name + ": listening to it alone",
            "listening to the shown: " + IsListening(board) + ", to the other: " + IsListening(other));
        CheckString(view.boardLabel.text, "Board " + (board.boardNumber + 1), name + ": board label");
    }

    bool IsListening(ChessBoard board) {
        int count = 0;
        for (int i = 0; i < board.listeners.Length; i++) {
            if (board.listeners[i] == view) count++;
        }
        return count == 1;
    }

    // ---- Drawing ----

    void RunDrawingChecks() {
        string s = "drawing";
        view.Show(0);
        CheckView(s + ": the first board's lobby");
        CheckInt(first.JoinAs(ChessRules.Black, Bo), ChessBoard.InputAccepted, s + ": Bo joins");
        // Standard and no clock where the arbiter doesn't force them, so the texts don't tick between drawing and checking.
        first.SetModeAs(Ana, ChessGame.ModeStandard);
        first.SetClockAs(Ana, ChessArbiter.NoClock, 0);
        CheckInt(first.StartGameAs(Ana), ChessBoard.InputAccepted, s + ": started");
        view.Refresh();
        CheckView(s + ": the game started");
        CheckInt(first.PieceDroppedAs(Ana, Sq("e2"), Sq("e4")), ChessBoard.InputAccepted, s + ": e4");
        view.Refresh();
        CheckView(s + ": e4");
        CheckSquareColor("e2", true, false, s + ": e4, e2 tinted");
        CheckSquareColor("e4", true, false, s + ": e4, e4 tinted");
        CheckSquareColor("d2", false, false, s + ": e4, d2 not tinted");
        CheckInt(first.PieceDroppedAs(Bo, Sq("f7"), Sq("f6")), ChessBoard.InputAccepted, s + ": f6");
        CheckInt(first.PieceDroppedAs(Ana, Sq("d1"), Sq("h5")), ChessBoard.InputAccepted, s + ": Qh5+");
        view.Refresh();
        CheckView(s + ": Qh5+");
        CheckSquareColor("e8", false, true, s + ": Qh5+, Black's king red");
        CheckString(view.statusLabel.text, "Black to move, in check", s + ": Qh5+, status");
        // Replay at ply 1.
        CheckInt(first.EnterReplayAs(Ana, false), ChessBoard.InputAccepted, s + ": replay entered");
        CheckInt(first.StepToAs(Ana, false, 1), ChessBoard.InputAccepted, s + ": replay at ply 1");
        view.Refresh();
        CheckView(s + ": replay at ply 1");
        Check(view.statusLabel.text.EndsWith("Replay: 1. e4 (1 of 3)"), s + ": replay at ply 1, status",
            "got \"" + view.statusLabel.text + "\"");
        CheckSquareColor("e8", false, false, s + ": replay at ply 1, no check");
        CheckInt(first.LeaveReplayAs(Ana, false), ChessBoard.InputAccepted, s + ": replay left");
        view.Refresh();
        CheckView(s + ": back to the game");
        // A change on the board not shown leaves the view as it is.
        CheckInt(second.JoinAs(ChessRules.White, Bo), ChessBoard.InputAccepted, s + ": Bo joins the second board");
        view.Refresh();
        CheckView(s + ": the second board changed, the first still shown");
    }

    public void CheckDrawnLater() {
        CheckView("frames: g6 drawn by the view itself");
        CheckSquareColor("g7", true, false, "frames: g6, g7 tinted");
        ResetBoth();
        view.synced = savedSynced;
        view.Show(0);
        CheckView("end: both boards reset, the first shown");
        LogSummary();
    }

    // Every square of the view against the board shown: its piece's sprite (hidden when empty), and the texts as the
    // board's UI writes them.
    void CheckView(string name) {
        ChessBoard board = view.ShownBoard();
        bool ok = true;
        string details = "";
        for (int square = 0; square < 64 && ok; square++) {
            int piece = board.PieceAt(square);
            Image image = view.pieceImages[square];
            Sprite expected = view.PieceSprite(piece);
            if (piece != ChessRules.Empty && expected == null) {
                ok = false;
                details = "no sprite for the piece on " + ChessRules.SquareName(square);
            } else if (image.enabled != (piece != ChessRules.Empty) || (image.enabled && image.sprite != expected)) {
                ok = false;
                details = ChessRules.SquareName(square) + " shows "
                    + (image.enabled && image.sprite != null ? image.sprite.name : "nothing");
            }
        }
        Check(ok, name + ": pieces", details);
        double now = board.ServerTime();
        CheckString(view.playersLabel.text, ChessBoardUI.PlayersTextFor(board, now), name + ": players");
        CheckString(view.statusLabel.text, ChessBoardUI.StatusTextFor(board, now), name + ": status");
        CheckString(view.boardLabel.text, "Board " + (board.boardNumber + 1), name + ": board label");
    }

    // A square's colour: its own, tinted for the last move, or the check colour.
    void CheckSquareColor(string squareName, bool lastMove, bool check, string name) {
        int square = Sq(squareName);
        bool light = (ChessRules.FileOf(square) + ChessRules.RankOf(square)) % 2 == 1;
        Color expected = light ? view.lightSquare : view.darkSquare;
        if (lastMove) expected = Color.Lerp(expected, view.lastMoveTint, 0.6f);
        if (check) expected = view.checkTint;
        Color actual = view.squareImages[square].color;
        bool same = Mathf.Abs(actual.r - expected.r) < 0.01f && Mathf.Abs(actual.g - expected.g) < 0.01f
            && Mathf.Abs(actual.b - expected.b) < 0.01f;
        Check(same, name, "got " + actual + ", expected " + expected);
    }

    void ResetBoth() {
        if (first.lifecycle != ChessBoard.LifecycleReset) first.ResetBoardAs(Ana, true);
        if (second.lifecycle != ChessBoard.LifecycleReset) second.ResetBoardAs(Ana, true);
        Check(first.lifecycle == ChessBoard.LifecycleReset && second.lifecycle == ChessBoard.LifecycleReset,
            "boards reset", "lifecycles " + first.lifecycle + " and " + second.lifecycle);
    }

    int Sq(string name) {
        return ChessRules.ParseSquare(name);
    }

    void LogSummary() {
        Debug.Log("[Board2DTest] done: " + passed + " passed, " + failed + " failed"
            + (failed == 0 ? " ALL PASS" : " SOME FAIL"));
    }

    void CheckInt(int actual, int expected, string name) {
        Check(actual == expected, name, "got " + actual + ", expected " + expected);
    }

    void CheckString(string actual, string expected, string name) {
        Check(actual == expected, name, "got \"" + actual + "\", expected \"" + expected + "\"");
    }

    void Check(bool ok, string name, string details) {
        if (ok) {
            passed++;
            Debug.Log("[Board2DTest] PASS: " + name);
        } else {
            failed++;
            Debug.Log("[Board2DTest] FAIL: " + name + ": " + details);
        }
    }
}
