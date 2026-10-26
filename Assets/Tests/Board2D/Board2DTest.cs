using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;

// Step 12.13 to 12.16 test behaviour (PLAN.md 12.13 to 12.16, docs/board-2d.md): the 2D board view, ChessBoard2DView.
// Stepping is checked on its own (_StepTarget, with five boards) and on the scene's boards through the view: which
// board it shows, that it listens to that one only, its pieces, highlights, board label and texts against the board
// and the board's UI's texts, as moves are played. Then its own replay (12.14): the step buttons, following the game,
// the board's replay and takebacks, switching board, locking to one board, the move list (12.15) and a long game's
// timing, and the holder moving and resizing it (12.16). The boards are synced, so like BoardTest it runs only with no
// one else in the instance, once BoardTest, ViewTest and ArchiveTest have finished with the boards. The view's Synced
// is turned off for the checks (nothing is sent) and put back. Logs "[Board2DTest] PASS: <check>" or
// "[Board2DTest] FAIL: <check>: <details>" per check, then a summary line. Built in the scene by Tools > Chess Board >
// Create 2D View Test (Editor/Board2DTestBuilder.cs).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class Board2DTest : ChessTest {
    protected override string TestName() {
        return "Board2DTest";
    }

    public ChessBoard2DView view;
    // The view's holder (12.16), when it's movable; its checks are skipped without one.
    public ChessBoard2DViewHolder holder;
    // Waited for, since they change the boards; none to start at once.
    public BoardTest boardTest;
    public ViewTest viewTest;
    public ArchiveTest archiveTest;

    const string Ana = "2D Ana";
    const string Bo = "2D Bo";

    bool joined;
    bool ran;
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
        if (!ReadyAfter(joined, boardTest, viewTest, archiveTest, "the other tests to finish")) {
            SendCustomEventDelayedFrames(nameof(WaitForTests), 1);
            return;
        }
        ran = true;
        if (VRCPlayerApi.GetPlayerCount() > 1) {
            Debug.Log("[Board2DTest] skipped: other players are in the instance");
            return;
        }
        if (Utilities.IsValid(view)) UseEnglish(view.i18n);
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
        CheckInt(ChessBoard2DView._StepTarget(inUse, 1, 1), 3, "step: right from 2 to 4");
        CheckInt(ChessBoard2DView._StepTarget(inUse, 3, 1), 1, "step: right from 4 wraps to 2");
        CheckInt(ChessBoard2DView._StepTarget(inUse, 3, -1), 1, "step: left from 4 to 2");
        CheckInt(ChessBoard2DView._StepTarget(inUse, 1, -1), 3, "step: left from 2 wraps to 4");
        CheckInt(ChessBoard2DView._StepTarget(inUse, 0, 1), 1, "step: right from reset 1 to 2");
        CheckInt(ChessBoard2DView._StepTarget(inUse, 0, -1), 3, "step: left from reset 1 wraps to 4");
        CheckInt(ChessBoard2DView._StepTarget(inUse, 4, 1), 1, "step: right from reset 5 wraps to 2");
        CheckInt(ChessBoard2DView._StepTarget(inUse, 2, -1), 1, "step: left from reset 3 to 2");
        CheckInt(ChessBoard2DView._StepTarget(inUse, -1, 1), 1, "step: right from none to 2");
        CheckInt(ChessBoard2DView._StepTarget(inUse, -1, -1), 3, "step: left from none to 4");
        bool[] none = { false, false, false, false, false };
        CheckInt(ChessBoard2DView._StepTarget(none, 3, 1), -1, "step: all reset, right does nothing");
        CheckInt(ChessBoard2DView._StepTarget(none, 3, -1), -1, "step: all reset, left does nothing");
        bool[] onlyFour = { false, false, false, true, false };
        CheckInt(ChessBoard2DView._StepTarget(onlyFour, 3, 1), -1, "step: only the board shown in use, right stays");
        CheckInt(ChessBoard2DView._StepTarget(onlyFour, 3, -1), -1, "step: only the board shown in use, left stays");
        CheckInt(ChessBoard2DView._StepTarget(onlyFour, 1, 1), 3, "step: from reset 2 to the only one in use");
        CheckInt(ChessBoard2DView._StepTarget(new bool[0], 0, 1), -1, "step: no boards");
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
        Check(Utilities.IsValid(view.leftButton) && Utilities.IsValid(view.rightButton), "wiring: selector buttons",
            "one missing");
        bool stepsOk = view.stepButtons != null && view.stepLabels != null && view.stepButtons.Length == 4
            && view.stepLabels.Length == 4;
        for (int i = 0; stepsOk && i < 4; i++) {
            if (!Utilities.IsValid(view.stepButtons[i]) || !Utilities.IsValid(view.stepLabels[i])) stepsOk = false;
        }
        Check(stepsOk, "wiring: 4 step buttons with labels", "one missing");
        if (stepsOk) {
            CheckString(view.stepLabels[0].text + " " + view.stepLabels[1].text + " " + view.stepLabels[2].text + " "
                + view.stepLabels[3].text, "<< < > >>", "wiring: step labels");
        }
        Check(!view.IsLocked(), "wiring: not locked", "locked to " + view.lockedBoard);
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
        view._Refresh();
        CheckShown(1, s + ": the second board still shown once reset");
        CheckString(view.statusLabel.text, Text("status_reset"), s + ": the reset board's status");
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
        CheckString(view.boardLabel.text, BoardLabel(board), name + ": board label");
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
        view._Refresh();
        CheckView(s + ": the game started");
        CheckInt(first.PieceDroppedAs(Ana, Sq("e2"), Sq("e4")), ChessBoard.InputAccepted, s + ": e4");
        view._Refresh();
        CheckView(s + ": e4");
        CheckSquareColor("e2", true, false, s + ": e4, e2 tinted");
        CheckSquareColor("e4", true, false, s + ": e4, e4 tinted");
        CheckSquareColor("d2", false, false, s + ": e4, d2 not tinted");
        CheckInt(first.PieceDroppedAs(Bo, Sq("f7"), Sq("f6")), ChessBoard.InputAccepted, s + ": f6");
        CheckInt(first.PieceDroppedAs(Ana, Sq("d1"), Sq("h5")), ChessBoard.InputAccepted, s + ": Qh5+");
        view._Refresh();
        CheckView(s + ": Qh5+");
        CheckSquareColor("e8", false, true, s + ": Qh5+, Black's king red");
        CheckString(view.statusLabel.text, ColorLine("status_to_move_in_check", ChessRules.Black),
            s + ": Qh5+, status");
        // The board's replay at ply 1 leaves the view on the latest (12.14).
        CheckInt(first.EnterReplayAs(Ana, false), ChessBoard.InputAccepted, s + ": replay entered");
        CheckInt(first.StepToAs(Ana, false, 1), ChessBoard.InputAccepted, s + ": replay at ply 1");
        view._Refresh();
        CheckView(s + ": the board's replay at ply 1, the view on the latest");
        Check(view.statusLabel.text == ColorLine("status_to_move_in_check", ChessRules.Black),
            s + ": the board's replay at ply 1, no replay line", "got \"" + view.statusLabel.text + "\"");
        CheckSquareColor("e8", false, true, s + ": the board's replay at ply 1, Black's king still red");
        CheckInt(first.LeaveReplayAs(Ana, false), ChessBoard.InputAccepted, s + ": replay left");
        view._Refresh();
        CheckView(s + ": back to the game");
        // A change on the board not shown leaves the view as it is.
        CheckInt(second.JoinAs(ChessRules.White, Bo), ChessBoard.InputAccepted, s + ": Bo joins the second board");
        view._Refresh();
        CheckView(s + ": the second board changed, the first still shown");
    }

    public void CheckDrawnLater() {
        CheckView("frames: g6 drawn by the view itself");
        CheckSquareColor("g7", true, false, "frames: g6, g7 tinted");
        ResetBoth();
        RunReplayChecks();
        RunLockChecks();
        RunMoveListChecks();
        // Ends on a loaded game from a FEN without notation, which the board fills in over the next frames.
        SendCustomEventDelayedFrames(nameof(CheckNotationLater), 3);
    }

    public void CheckNotationLater() {
        RunNotationLaterChecks();
        RunLongGameCheck();
        RunHolderChecks();
        ResetBoth();
        view.synced = savedSynced;
        view.Show(0);
        CheckView("end: both boards reset, the first shown");
        LogSummary();
    }

    // ---- Its own replay (12.14) ----

    const string FenAfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
    const string FenAfterNf3 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2";
    const string FenAfterNc6 = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3";

    void RunReplayChecks() {
        string s = "replay";
        StartGame(first);
        first.gameSettings |= ChessArbiter.SettingTakebacks;
        Play(first, "e2", "e4", s);
        Play(first, "e7", "e5", s);
        Play(first, "g1", "f3", s);
        Play(first, "b8", "c6", s);
        view.Show(0);
        CheckView(s + ": on the latest after 4 plies");
        CheckSteps(true, true, false, false, s + ": on the latest");

        // Each button.
        view.PressBack();
        CheckViewAt(FenAfterNf3, 3, s + ": < to ply 3");
        CheckStatusEnds(ReplayLine("2.", "Nf3", 3, 4), s + ": < to ply 3");
        CheckSquareColor("g1", true, false, s + ": ply 3, g1 tinted");
        CheckSquareColor("f3", true, false, s + ": ply 3, f3 tinted");
        CheckSquareColor("c6", false, false, s + ": ply 3, c6 not tinted");
        CheckSteps(true, true, true, true, s + ": ply 3");
        view.PressFirst();
        CheckViewAt(ChessFen.StartFen, 0, s + ": << to the start");
        CheckStatusEnds(TextWith("replay_start", new string[] { "ply", "0", "count", "4" }), s + ": << to the start");
        CheckSquareColor("b8", false, false, s + ": the start, b8 not tinted");
        CheckSteps(false, false, true, true, s + ": the start");
        view.PressBack();
        CheckInt(view.ViewPly(), 0, s + ": < at the start does nothing");
        view.PressFirst();
        CheckInt(view.ViewPly(), 0, s + ": << at the start does nothing");
        Check(!view.CanShowPly(-1) && !view.CanShowPly(0) && !view.CanShowPly(5), s + ": plies off the game refused",
            "a ply allowed");
        view.PressForward();
        CheckViewAt(FenAfterE4, 1, s + ": > to ply 1");
        CheckStatusEnds(ReplayLine("1.", "e4", 1, 4), s + ": > to ply 1");
        CheckSquareColor("e2", true, false, s + ": ply 1, e2 tinted");
        view.PressLast();
        CheckViewAt(FenAfterNc6, 4, s + ": >> to the latest");
        Check(view.IsOnLatest(), s + ": >> on the latest", "not on the latest");
        CheckString(view.statusLabel.text, ChessBoardUI._StatusTextFor(first, first._ServerTime(), view._GameTexts()),
            s + ": >> the board's status, no replay line");
        CheckSteps(true, true, false, false, s + ": >> the latest");
        view.PressForward();
        CheckInt(view.ViewPly(), 4, s + ": > on the latest does nothing");
        view.PressLast();
        CheckInt(view.ViewPly(), 4, s + ": >> on the latest does nothing");
        view.PressForward();
        view.PressBack();
        view.PressForward();
        CheckInt(view.ViewPly(), 4, s + ": < then > back on the latest");
        Check(view.IsOnLatest(), s + ": > to the latest follows again", "not on the latest");

        // Following the game.
        Play(first, "f1", "b5", s);
        view._Refresh();
        CheckView(s + ": on the latest, Bb5 moves it on to ply 5");
        view.PressBack();
        CheckInt(view.ViewPly(), 4, s + ": one back, ply 4");
        Play(first, "a7", "a6", s);
        view._Refresh();
        CheckViewAt(FenAfterNc6, 4, s + ": one back, a6 leaves it at ply 4");
        CheckStatusEnds(ReplayLine("2...", "Nc6", 4, 6), s + ": a6, still ply 4");
        CheckSteps(true, true, true, true, s + ": ply 4 of 6");

        // The board's replay leaves the view where it is, and the view steps through the game meanwhile.
        CheckInt(first.EnterReplayAs(Ana, false), ChessBoard.InputAccepted, s + ": the board enters replay");
        view._Refresh();
        CheckViewAt(FenAfterNc6, 4, s + ": the board entering replay, the view at ply 4");
        CheckInt(first.StepToAs(Ana, false, 1), ChessBoard.InputAccepted, s + ": the board steps to ply 1");
        view._Refresh();
        CheckViewAt(FenAfterNc6, 4, s + ": the board at ply 1, the view at ply 4");
        CheckStatusEnds(ReplayLine("2...", "Nc6", 4, 6), s + ": the board at ply 1, the view's replay line");
        view.PressFirst();
        CheckViewAt(ChessFen.StartFen, 0, s + ": the board in replay, << to the start");
        view.PressLast();
        CheckInt(view.ViewPly(), 6, s + ": the board in replay, >> to the latest");
        CheckGamePosition(s + ": the board in replay, >> shows the game's position");
        CheckInt(first._ReplayPieceDroppedAs(Ana, false, Sq("d2"), Sq("d4")), ChessBoard.InputAccepted,
            s + ": a free move on the board");
        view._Refresh();
        CheckGamePosition(s + ": a free move, the view still on the game's position");
        view.PressBack();
        CheckInt(view.ViewPly(), 5, s + ": < from the latest during the board's replay");
        CheckInt(first.StepToAs(Ana, false, 2), ChessBoard.InputAccepted, s + ": the board steps to ply 2");
        CheckInt(first.LeaveReplayAs(Ana, false), ChessBoard.InputAccepted, s + ": the board leaves replay");
        view._Refresh();
        CheckInt(view.ViewPly(), 5, s + ": the board left replay, the view at ply 5");

        // A takeback above the ply shown leaves it; one below it brings the view to the new latest.
        view.ShowPly(2);
        TakeBack(first, 4, s);
        view._Refresh();
        CheckInt(view.ViewPly(), 2, s + ": a takeback to ply 4 above the view's 2 leaves it");
        Check(!view.IsOnLatest(), s + ": the takeback above, not on the latest", "on the latest");
        Play(first, "f1", "b5", s);
        view.ShowPly(4);
        TakeBack(first, 2, s);
        view._Refresh();
        CheckInt(view.ViewPly(), 2, s + ": a takeback to ply 2 below the view's 4 brings it to the latest");
        Check(view.IsOnLatest(), s + ": the takeback below, on the latest", "not on the latest");
        CheckGamePosition(s + ": the takeback below, the game's position");
        Play(first, "g1", "f3", s);
        view._Refresh();
        CheckInt(view.ViewPly(), 3, s + ": after the takeback, following the game");

        // A synced ply arriving before the board's moves (a late joiner): the latest meanwhile, then that ply once the
        // game reaches beyond it.
        view._ReceivePly(5);
        CheckInt(view.ViewPly(), 3, s + ": ply 5 received with 3 moves here, the latest meanwhile");
        Play(first, "b8", "c6", s);
        view._Refresh();
        CheckInt(view.ViewPly(), 4, s + ": ply 5 waiting, 4 moves here, still the latest");
        Play(first, "f1", "b5", s);
        Play(first, "a7", "a6", s);
        view._Refresh();
        CheckInt(view.ViewPly(), 5, s + ": the game reaches beyond ply 5, shown");
        Check(!view.IsOnLatest(), s + ": the received ply 5, not on the latest", "on the latest");
        view._ReceivePly(ChessBoard2DView.LatestPly);
        CheckInt(view.ViewPly(), 6, s + ": the latest received");
        view._ReceivePly(9);
        view.PressFirst();
        Play(first, "b5", "a4", s);
        Play(first, "g8", "f6", s);
        Play(first, "e1", "g1", s);
        Play(first, "f8", "e7", s);
        view._Refresh();
        CheckInt(view.ViewPly(), 0, s + ": a step here drops a received ply still waiting");
        view._ReceivePly(1);
        CheckInt(view.ViewPly(), 1, s + ": a received ply the game reaches, shown at once");

        // A reset brings it to the latest.
        view.ShowPly(1);
        CheckInt(first.ResetBoardAs(Ana, true), ChessBoard.InputAccepted, s + ": reset");
        view._Refresh();
        CheckInt(view.ViewPly(), 0, s + ": the reset, ply 0");
        Check(view.IsOnLatest(), s + ": the reset, on the latest", "not on the latest");
        CheckView(s + ": the reset board");
        CheckSteps(false, false, false, false, s + ": a reset board");

        // Switching board starts on the latest.
        StartGame(first);
        Play(first, "e2", "e4", s);
        Play(first, "e7", "e5", s);
        CheckInt(second.JoinAs(ChessRules.White, Bo), ChessBoard.InputAccepted, s + ": Bo joins the second board");
        CheckInt(second.JoinAs(ChessRules.Black, Ana), ChessBoard.InputAccepted, s + ": Ana joins the second board");
        second.SetModeAs(Bo, ChessGame.ModeStandard);
        second.SetClockAs(Bo, ChessArbiter.NoClock, 0);
        CheckInt(second.StartGameAs(Bo), ChessBoard.InputAccepted, s + ": the second board started");
        Play(second, "d2", "d4", s);
        view.Show(0);
        view.ShowPly(0);
        view.PressRight();
        CheckShown(1, s + ": right to the second board");
        CheckInt(view.ViewPly(), 1, s + ": the second board on its latest");
        CheckView(s + ": the second board's latest");
        view.PressFirst();
        view.PressLeft();
        CheckShown(0, s + ": left back to the first board");
        CheckInt(view.ViewPly(), 2, s + ": the first board on its latest");
        CheckView(s + ": the first board's latest");
    }

    // ---- Locked to one board (12.14) ----

    void RunLockChecks() {
        string s = "locked";
        view.Show(0);
        view.SetLockedBoard(second);
        Check(view.IsLocked(), s + ": locked", "not locked");
        Check(view.ShownBoard() == second, s + ": shows the second board", "shows " + view.ShownBoard());
        CheckInt(view.ShownIndex(), -1, s + ": no index in boards");
        Check(IsListening(second) && !IsListening(first), s + ": listening to the second board alone",
            "the second: " + IsListening(second) + ", the first: " + IsListening(first));
        CheckString(view.boardLabel.text, BoardLabel(second), s + ": board label");
        Check(!view.leftButton.gameObject.activeSelf && !view.rightButton.gameObject.activeSelf,
            s + ": selector hidden", "shown");
        Check(!view.leftButton.interactable && !view.rightButton.interactable, s + ": selector disabled", "enabled");
        CheckView(s + ": the second board's latest");
        view.PressRight();
        Check(view.ShownBoard() == second, s + ": right refused", "shows " + view.ShownBoard());
        view.PressLeft();
        view._Step(1);
        Check(view.ShownBoard() == second, s + ": left and Step refused", "shows " + view.ShownBoard());
        view.Show(0);
        Check(view.ShownBoard() == second && IsListening(second) && !IsListening(first), s + ": Show(0) still the second",
            "shows " + view.ShownBoard());
        view.PressFirst();
        CheckViewAt(ChessFen.StartFen, 0, s + ": its own replay, << to the start");
        view.SetLockedBoard(null);
        Check(!view.IsLocked(), s + ": unlocked", "still locked");
        CheckShown(0, s + ": unlocked, the first board");
        Check(view.leftButton.gameObject.activeSelf && view.rightButton.gameObject.activeSelf
            && view.leftButton.interactable && view.rightButton.interactable, s + ": unlocked, selector back",
            "hidden or disabled");
    }

    // ---- The move list (12.15) ----

    // From the standard start with Black to move after e4, for a game whose first move is Black's.
    const string FenBlackFirst = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";

    void RunMoveListChecks() {
        string s = "move list";
        RunMoveListWiringChecks(s);
        RunMoveListLayoutChecks(s);
        ResetBoth();
        view.Show(0);
        CheckMoveCells(new string[0], -1, s + ": a reset board, no moves");
        StartGame(first);
        Play(first, "e2", "e4", s);
        Play(first, "e7", "e5", s);
        Play(first, "g1", "f3", s);
        Play(first, "b8", "c6", s);
        view._Refresh();
        string[] moves = { "e4", "e5", "Nf3", "Nc6" };
        CheckMoveCells(moves, 3, s + ": 4 plies, Nc6 current");
        CheckString(view.moveNumbers[0].text + " " + view.moveNumbers[1].text + " [" + view.moveNumbers[2].text + "]",
            "1. 2. []", s + ": move numbers");
        view.PressBack();
        CheckMoveCells(moves, 2, s + ": < to ply 3, Nf3 current");
        view.PressFirst();
        CheckMoveCells(moves, -1, s + ": << to the start, none current");
        view._PressMoveCell(1);
        CheckInt(view.ViewPly(), 2, s + ": e5 clicked, the position after it");
        CheckViewAt("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", 2, s + ": e5 clicked");
        CheckMoveCells(moves, 1, s + ": e5 clicked, e5 current");
        view._PressMoveCell(3);
        CheckInt(view.ViewPly(), 4, s + ": Nc6 clicked, the latest");
        Check(view.IsOnLatest(), s + ": Nc6 clicked, on the latest", "not on the latest");
        view._PressMoveCell(0);
        CheckInt(view.ViewPly(), 1, s + ": e4 clicked, ply 1");
        view._PressMoveCell(4);
        CheckInt(view.ViewPly(), 1, s + ": an empty cell clicked does nothing");
        view._PressMoveCell(-1);
        view._PressMoveCell(view.moveCells.Length);
        CheckInt(view.ViewPly(), 1, s + ": cells off the pool do nothing");
        view.PressLast();
        Play(first, "f1", "b5", s);
        view._Refresh();
        CheckMoveCells(new string[] { "e4", "e5", "Nf3", "Nc6", "Bb5" }, 4, s + ": on the latest, Bb5 current");

        // Scrolling: moves past the list's height, the view on the latest.
        float rowsShown = view.moveListViewport.rect.height / view.moveRowHeight;
        int wanted = Mathf.CeilToInt(rowsShown) * 2 + 8;
        bool inView = true;
        string lost = "";
        bool played = true;
        while (first.moveCount < wanted && played) {
            played = PlayAny(first);
            view._Refresh();
            if (!view._IsMoveInView(first.moveCount - 1) && inView) {
                inView = false;
                lost = "ply " + first.moveCount + " out of view at scroll " + view._MoveListScroll();
            }
        }
        CheckInt(first.moveCount, wanted, s + ": " + wanted + " plies played");
        Check(inView, s + ": on the latest, each new move in view", lost);
        Check(view._MoveListMaxScroll() > 0, s + ": the moves don't fit, it scrolls", "max scroll 0");
        Check(Mathf.Abs(view._MoveListScroll() - view._MoveListMaxScroll()) < 0.5f, s + ": scrolled to the bottom",
            view._MoveListScroll() + " of " + view._MoveListMaxScroll());
        Check(view._IsMoveCellCurrent(view._MoveCellOf(first.moveCount - 1)), s + ": the latest move current",
            "cell " + view._MoveCellOf(first.moveCount - 1));
        Check(view.moveList.verticalScrollbar != null, s + ": a scrollbar", "none");
        view.PressFirst();
        CheckFloat(view._MoveListScroll(), 0, s + ": << scrolls to the top");
        view.PressForward();
        Check(view._IsMoveInView(0) && view._IsMoveCellCurrent(view._MoveCellOf(0)), s + ": > to ply 1, e4 in view",
            "scroll " + view._MoveListScroll());
        // A player's own scrolling stays while the ply shown doesn't change.
        view._ScrollMoveList(view.moveRowHeight * 3);
        view._Refresh();
        CheckFloat(view._MoveListScroll(), view.moveRowHeight * 3, s + ": scrolled by hand, kept on a redraw");
        // The scroll rect's onValueChanged after the view's own scroll keeps it. The mouse wheel's step can't be
        // simulated (Input.GetAxisRaw), so this checks that a move without it (a drag) stays where it went.
        view._OnMoveListScrolled();
        CheckFloat(view._MoveListScroll(), view.moveRowHeight * 3, s + ": the view's own scroll kept");
        Vector2 at = view.moveListContent.anchoredPosition;
        view.moveListContent.anchoredPosition = new Vector2(at.x, view.moveRowHeight * 5.5f);
        view._OnMoveListScrolled();
        CheckFloat(view._MoveListScroll(), view.moveRowHeight * 5.5f, s + ": a drag's scroll kept");
        view._ScrollMoveList(view.moveRowHeight * 3);
        // A step brings the move back into view, scrolling just enough: e5's row (the first) at the top.
        view.PressForward();
        CheckFloat(view._MoveListScroll(), 0, s + ": > to ply 2, scrolled up just to e5's row");
        // Down: a ply below the list, its row at the bottom.
        int row = Mathf.CeilToInt(rowsShown) + 2;
        view.ShowPly(2 * row + 1);
        CheckFloat(view._MoveListScroll(), (row + 1) * view.moveRowHeight - view.moveListViewport.rect.height,
            s + ": a ply below the list, its row scrolled just to the bottom");
        Check(view._IsMoveInView(2 * row), s + ": that move in view", "scroll " + view._MoveListScroll());
        // Scrolled part of a row: the pool starts on the row at the top, shifted up by the rest.
        view._ScrollMoveList(view.moveRowHeight * 3 + view.moveRowHeight / 2);
        CheckInt(view._MoveCellPly(0), 6, s + ": scrolled 3.5 rows, the pool's first cell is ply 7's");
        CheckFloat(view.moveListRows.anchoredPosition.y, view.moveRowHeight / 2,
            s + ": scrolled 3.5 rows, rows shifted");
        CheckString(view.moveNumbers[0].text, "4.", s + ": scrolled 3.5 rows, the first number");
        int ply = view._MoveCellPly(3);
        view._PressMoveCell(3);
        CheckInt(view.ViewPly(), ply + 1, s + ": a scrolled cell clicked, the position after its move");
        Check(view._IsMoveCellCurrent(3), s + ": the clicked cell current", "not yellow");
        // Switching board scrolls to the new board's latest, and back.
        CheckInt(second.JoinAs(ChessRules.White, Bo), ChessBoard.InputAccepted, s + ": Bo joins the second board");
        view.PressRight();
        CheckMoveCells(new string[0], -1, s + ": the second board, no moves");
        CheckFloat(view._MoveListScroll(), 0, s + ": the second board, at the top");
        view.PressLeft();
        Check(view._IsMoveInView(first.moveCount - 1) && view.IsOnLatest(), s + ": back on the first, its latest in view",
            "scroll " + view._MoveListScroll());

        // A game from a FEN with Black to move, its notation left for the board to fill in.
        ResetBoth();
        int[] loaded = new int[2];
        string[] sans = { "", "" };
        int[] clocks = { -1, -1 };
        byte[] position = new byte[64];
        int[] state = new int[ChessPosition.StateLength];
        int clock;
        int number;
        ChessFen.ParseFen(FenBlackFirst, position, state, out clock, out number);
        loaded[0] = FindMove(position, state, "e7", "e5");
        ChessPosition.MakeMove(position, state, loaded[0]);
        loaded[1] = FindMove(position, state, "g1", "f3");
        CheckInt(first._LoadGame(FenBlackFirst, loaded, sans, clocks, 2, ChessGame.ModeStandard, "Fen A", "Fen B", "*",
            "", ChessArbiter.NoClock, 0), ChessBoard.InputAccepted, s + ": a game from Black to move loaded");
        view.Show(0);
        view._Refresh();
        CheckString(view.moveCellLabels[1].text + "|" + view.moveCellLabels[2].text, "|",
            s + ": the notation not filled in yet, empty cells");
        Check(!view.moveCells[0].interactable && view.moveCells[1].interactable && view.moveCells[2].interactable,
            s + ": Black first, White's first cell empty and not clickable", "clickable: " + view.moveCells[0].interactable
            + " " + view.moveCells[1].interactable + " " + view.moveCells[2].interactable);
    }

    // The FEN game loaded at the end of RunMoveListChecks, frames later: its notation filled in by the board.
    void RunNotationLaterChecks() {
        string s = "move list";
        CheckMoveCells(new string[] { "", "e5", "Nf3" }, 2, s + ": Black first, the notation filled in");
        CheckString(view.moveNumbers[0].text + " " + view.moveNumbers[1].text, "1. 2.", s + ": Black first, numbers");
        view._PressMoveCell(1);
        CheckInt(view.ViewPly(), 1, s + ": Black first, e5 clicked, ply 1");
        CheckMoveCells(new string[] { "", "e5", "Nf3" }, 1, s + ": Black first, e5 current");
        CheckInt(first.ResetBoardAs(Ana, true), ChessBoard.InputAccepted, s + ": reset");
    }

    void RunMoveListWiringChecks(string s) {
        Check(Utilities.IsValid(view.moveList) && Utilities.IsValid(view.moveListViewport)
            && Utilities.IsValid(view.moveListContent) && Utilities.IsValid(view.moveListRows),
            s + ": wiring, scroll rect, viewport, content and rows", "one missing");
        int poolRows = view.moveNumbers == null ? 0 : view.moveNumbers.Length;
        Check(poolRows >= Mathf.CeilToInt(view.moveListViewport.rect.height / view.moveRowHeight) + 1,
            s + ": wiring, enough pooled rows to fill the list", poolRows + " rows");
        bool ok = view.moveCells != null && view.moveCellImages != null && view.moveCellLabels != null
            && view.moveCells.Length == 2 * poolRows && view.moveCellImages.Length == 2 * poolRows
            && view.moveCellLabels.Length == 2 * poolRows;
        string details = "wrong counts";
        for (int i = 0; ok && i < 2 * poolRows; i++) {
            ChessMoveListCell cell = Utilities.IsValid(view.moveCells[i])
                ? view.moveCells[i].GetComponent<ChessMoveListCell>()
                : null;
            if (!Utilities.IsValid(view.moveCells[i]) || !Utilities.IsValid(view.moveCellImages[i])
                || !Utilities.IsValid(view.moveCellLabels[i]) || !Utilities.IsValid(cell) || cell.view != view
                || cell.cell != i) {
                ok = false;
                details = "cell " + i;
            }
        }
        for (int r = 0; ok && r < poolRows; r++) {
            if (!Utilities.IsValid(view.moveNumbers[r])) {
                ok = false;
                details = "number " + r;
            }
        }
        Check(ok, s + ": wiring, each pooled cell with its button, image, label and ChessMoveListCell", details);
        Check(view.moveList.content == view.moveListContent && view.moveList.viewport == view.moveListViewport
            && !view.moveList.horizontal && view.moveList.vertical, s + ": wiring, the scroll rect's parts",
            "content, viewport or direction");
    }

    // The board, the selector, the texts and the step buttons all left of the move list, which runs from above the
    // selector to below the step buttons.
    void RunMoveListLayoutChecks(string s) {
        RectTransform listRect = view.moveList.GetComponent<RectTransform>();
        Vector3[] list = WorldCorners(listRect);
        Transform canvas = view.transform;
        float listLeft = canvas.InverseTransformPoint(list[0]).x;
        float listBottom = canvas.InverseTransformPoint(list[0]).y;
        float listTop = canvas.InverseTransformPoint(list[1]).y;
        RectTransform[] others = {
            view.squareImages[0].transform.parent.GetComponent<RectTransform>(),
            view.boardLabel.GetComponent<RectTransform>(),
            view.leftButton.GetComponent<RectTransform>(), view.rightButton.GetComponent<RectTransform>(),
            view.playersLabel.GetComponent<RectTransform>(), view.statusLabel.GetComponent<RectTransform>(),
            view.stepButtons[0].GetComponent<RectTransform>(), view.stepButtons[3].GetComponent<RectTransform>()
        };
        bool ok = true;
        string details = "";
        float othersTop = float.MinValue;
        float othersBottom = float.MaxValue;
        for (int i = 0; i < others.Length; i++) {
            Vector3[] corners = WorldCorners(others[i]);
            float right = canvas.InverseTransformPoint(corners[2]).x;
            othersTop = Mathf.Max(othersTop, canvas.InverseTransformPoint(corners[1]).y);
            othersBottom = Mathf.Min(othersBottom, canvas.InverseTransformPoint(corners[0]).y);
            if (right > listLeft) {
                ok = false;
                details = others[i].gameObject.name + " reaches " + right + ", the list starts at " + listLeft;
            }
        }
        Check(ok, s + ": layout, nothing reaching into the list", details);
        Check(listTop >= othersTop - 0.5f && listBottom <= othersBottom + 0.5f,
            s + ": layout, the list the full height",
            "list " + listBottom + " to " + listTop + ", the rest " + othersBottom + " to " + othersTop);
        RectTransform canvasRect = view.GetComponent<RectTransform>();
        Check(listLeft + listRect.rect.width <= canvasRect.rect.width / 2 + 0.5f, s + ": layout, the list on the canvas",
            "right edge " + (listLeft + listRect.rect.width) + " of " + canvasRect.rect.width / 2);
    }

    Vector3[] WorldCorners(RectTransform rect) {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return corners;
    }

    // The list's cells from the first row against moves (SAN by cell, "" for an empty one; the rest empty), with the
    // cell at current (the cell's index in moves, -1 for none) yellow and only cells with a move clickable.
    void CheckMoveCells(string[] moves, int current, string name) {
        bool ok = true;
        string details = "";
        for (int i = 0; i < view.moveCells.Length && ok; i++) {
            string expected = i < moves.Length ? moves[i] : "";
            bool clickable = i < moves.Length && view._MoveCellPly(i) >= 0;
            if (view.moveCellLabels[i].text != expected) {
                ok = false;
                details = "cell " + i + " reads \"" + view.moveCellLabels[i].text + "\", expected \"" + expected + "\"";
            } else if (view.moveCells[i].interactable != clickable) {
                ok = false;
                details = "cell " + i + (clickable ? " not clickable" : " clickable");
            } else if (view._IsMoveCellCurrent(i) != (i == current)) {
                ok = false;
                details = "cell " + i + (i == current ? " not yellow" : " yellow");
            } else if (i == current && view.moveCellLabels[i].color != view.currentMoveTextColor) {
                ok = false;
                details = "cell " + i + "'s text not in the current move's colour";
            }
        }
        Check(ok, name + ": cells", details);
    }

    // Plays a legal move for the side to move (the first one that isn't a promotion, from an index varying with the
    // game's length), false when there's none or it's refused.
    bool PlayAny(ChessBoard board) {
        int[] legal = new int[ChessMoves.MaxMoves];
        byte[] position = new byte[64];
        int[] state = new int[ChessPosition.StateLength];
        System.Array.Copy(board.board, position, 64);
        System.Array.Copy(board.state, state, ChessPosition.StateLength);
        int legalCount = ChessMoves.GenerateLegalMoves(position, state, legal, true);
        for (int i = 0; i < legalCount; i++) {
            int move = legal[(board.moveCount * 7 + i) % legalCount];
            if (((move >> ChessRules.MovePromotionShift) & ChessRules.MoveTypeMask) != 0) continue;
            int side = board.SideToMove();
            string player = side == ChessRules.White ? board.whitePlayer : board.blackPlayer;
            int result = board.PieceDroppedAs(player, (move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask,
                (move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask);
            return result == ChessBoard.InputAccepted;
        }
        return false;
    }

    // The legal move from-to in position, 0 for none.
    int FindMove(byte[] position, int[] state, string from, string to) {
        int[] legal = new int[ChessMoves.MaxMoves];
        int legalCount = ChessMoves.GenerateLegalMoves(position, state, legal, true);
        for (int i = 0; i < legalCount; i++) {
            int move = legal[i];
            if (((move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask) == Sq(from)
                && ((move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask) == Sq(to)) {
                return move;
            }
        }
        return 0;
    }

    void CheckFloat(float actual, float expected, string name) {
        Check(Mathf.Abs(actual - expected) < 0.5f, name, "got " + actual + ", expected " + expected);
    }

    // ---- A long game (12.14: rebuilding the position from the latest each time) ----

    void RunLongGameCheck() {
        string s = "long game";
        ResetBoth();
        // 300 plies of the first legal move each time, from the standard start (stopping early on a game's end).
        int wanted = 300;
        int[] moves = new int[wanted];
        string[] sans = new string[wanted];
        int[] clocks = new int[wanted];
        byte[] position = new byte[64];
        int[] state = new int[ChessPosition.StateLength];
        int[] legal = new int[ChessMoves.MaxMoves];
        int clock;
        int number;
        ChessFen.ParseFen(ChessFen.StartFen, position, state, out clock, out number);
        int count = 0;
        while (count < wanted) {
            int legalCount = ChessMoves.GenerateLegalMoves(position, state, legal, true);
            if (legalCount == 0) break;
            int move = legal[(count * 7) % legalCount];
            ChessPosition.MakeMove(position, state, move);
            moves[count] = move;
            sans[count] = "";
            clocks[count] = -1;
            count++;
        }
        CheckInt(first._LoadGame("", moves, sans, clocks, count, ChessGame.ModeStandard, "Long A", "Long B", "*", "",
            ChessArbiter.NoClock, 0), ChessBoard.InputAccepted, s + ": " + count + " plies loaded");
        view.Show(0);
        view._Refresh();
        CheckInt(view.ViewPly(), count, s + ": on the latest");
        CheckGamePosition(s + ": the latest");
        System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        watch.Start();
        view.PressFirst();
        watch.Stop();
        double toStart = watch.Elapsed.TotalMilliseconds;
        CheckViewAt(ChessFen.StartFen, 0, s + ": << to the start");
        watch.Reset();
        watch.Start();
        view.PressForward();
        watch.Stop();
        double toFirst = watch.Elapsed.TotalMilliseconds;
        CheckInt(view.ViewPly(), 1, s + ": > to ply 1");
        Debug.Log("[Board2DTest] long game timing: " + count + " plies, << to the start (" + count
            + " moves undone and drawn) " + Mathf.Round((float)toStart * 10) / 10 + " ms, then > to ply 1 "
            + Mathf.Round((float)toFirst * 10) / 10 + " ms");
        Check(toStart < 100, s + ": << within 100 ms", Mathf.Round((float)toStart * 10) / 10 + " ms");
        CheckInt(first.ResetBoardAs(Ana, true), ChessBoard.InputAccepted, s + ": reset");
    }

    // ---- Moving and resizing (12.16) ----

    void RunHolderChecks() {
        string s = "holder";
        // The sums on their own: the view's bottom right corner along +x at size 1.
        Vector3 baseRight = new Vector3(0.7f, 0, 0);
        CheckNear(ChessBoard2DViewHolder._SizeFor(baseRight, baseRight, 0.25f, 3), 1, s + ": size 1 at the corner");
        CheckNear(ChessBoard2DViewHolder._SizeFor(baseRight * 2, baseRight, 0.25f, 3), 2, s + ": size 2 twice as far");
        CheckNear(ChessBoard2DViewHolder._SizeFor(baseRight * 2 + new Vector3(0, -0.2f, 0.3f), baseRight, 0.25f, 3), 2,
            s + ": size 2, off the bottom edge");
        CheckNear(ChessBoard2DViewHolder._SizeFor(baseRight * 5, baseRight, 0.25f, 3), 3, s + ": size clamped to 3");
        CheckNear(ChessBoard2DViewHolder._SizeFor(-baseRight, baseRight, 0.25f, 3), 0.25f,
            s + ": size clamped to 0.25");
        Quaternion tilted = Quaternion.Euler(30, 90, 25);
        CheckNearVector(ChessBoard2DViewHolder._Upright(tilted, Quaternion.identity) * Vector3.forward, Vector3.right,
            s + ": upright keeps the heading");
        CheckNearVector(ChessBoard2DViewHolder._Upright(tilted, Quaternion.identity) * Vector3.up, Vector3.up,
            s + ": upright stands up");
        Quaternion fallback = Quaternion.Euler(0, 45, 0);
        CheckNearVector(ChessBoard2DViewHolder._Upright(Quaternion.Euler(90, 0, 0), fallback) * Vector3.forward,
            fallback * Vector3.forward, s + ": facing straight down keeps the heading before");

        if (!Utilities.IsValid(holder)) {
            Debug.Log("[Board2DTest] holder checks skipped: the view has no holder (Create Movable 2D Board View)");
            return;
        }
        Check(holder.view == view, s + ": its view is the view tested", "another view");
        Check(Utilities.IsValid(holder.grip) && Utilities.IsValid(holder.gripPickup) && Utilities.IsValid(holder.corner)
            && Utilities.IsValid(holder.cornerPickup), s + ": grip and corner assigned", "one missing");
        if (!Utilities.IsValid(holder.grip) || !Utilities.IsValid(holder.corner)) return;
        Check(holder.handleRenderers != null && holder.handleRenderers.Length == 2, s + ": two handle renderers",
            "wrong count");
        CheckNear(holder.minSize, ChessBoard2DViewHolder.DefaultMinSize, s + ": smallest size 0.25");
        // From the language pack (step 13.5), set by the holder at run time.
        CheckString(holder.gripPickup.InteractionText + "|" + holder.gripPickup.UseText + "|"
            + holder.cornerPickup.InteractionText + "|" + holder.cornerPickup.UseText,
            Text("hover_move") + "|" + Text("hover_move") + "|" + Text("hover_resize") + "|" + Text("hover_resize"),
            s + ": the grip's and the corner's hover texts");
        Check(holder.i18n == view.i18n && Utilities.IsValid(view.i18n), s + ": the holder and the view share one I18n",
            "they don't");
        bool gripSync = holder.grip.GetComponent<VRCObjectSync>() != null;
        Check(gripSync == savedSynced, s + ": the grip has a VRCObjectSync exactly when the view is synced",
            "VRCObjectSync " + (gripSync ? "there" : "missing") + ", Synced " + (savedSynced ? "on" : "off"));
        Check(view.GetComponent<VRCObjectSync>() == null && holder.GetComponent<VRCObjectSync>() == null,
            s + ": no VRCObjectSync on the view or the holder (manual sync)", "one has one");
        Check(holder.movable && holder.gripPickup.pickupable && holder.cornerPickup.pickupable,
            s + ": movable, both pickupable", "movable " + holder.movable);
        // ClientSim is desktop, so the pickups keep their own reach.
        Check(holder.gripPickup.proximity > holder.vrReach, s + ": on desktop, the pickups' own reach",
            "proximity " + holder.gripPickup.proximity);

        Transform v = view.transform;
        Transform grip = holder.grip;
        Vector3 startGrip = grip.position;
        Quaternion startGripTurn = grip.rotation;
        Vector3 startView = v.position;
        Quaternion startViewTurn = v.rotation;
        Vector3 startScale = v.localScale;
        Vector3 startCorner = holder.corner.position;
        Vector3 startBottom = ViewBottom(0);
        Vector3 startRight = ViewBottom(1);
        CheckNear(holder.Size(), 1, s + ": size 1 to start");

        // Moved, turned a quarter round and tilted: the view follows, turned and tilted with it.
        Vector3 moved = startGrip + new Vector3(1, 0.2f, -0.5f);
        Quaternion turn = Quaternion.Euler(20, 90, 15);
        grip.SetPositionAndRotation(moved, turn * startGripTurn);
        holder._Follow();
        CheckNearVector(v.position, moved + turn * (startView - startGrip), s + ": moved, the view follows the grip");
        CheckNearVector(v.up, turn * (startViewTurn * Vector3.up), s + ": moved, tilted with the grip");
        CheckNearVector(v.forward, turn * (startViewTurn * Vector3.forward), s + ": moved, turned with the grip");
        CheckNearVector(holder.corner.position, moved + turn * (startCorner - startGrip),
            s + ": moved, the corner follows");
        CheckNearVector(v.localScale, startScale, s + ": moved, the size kept");
        // As after a desktop player picked it up: the view upright, turned with the grip's heading alone, the bar
        // turned upright on the grip, which isn't turned itself (VRChat places a held pickup).
        Check(!holder.IsUpright(), s + ": not upright before anyone picks it up", "upright");
        holder.SetUpright(true);
        holder._Follow();
        Quaternion quarter = Quaternion.Euler(0, 90, 0);
        CheckNearVector(grip.up, turn * (startGripTurn * Vector3.up), s + ": upright, the grip left as held");
        CheckNearVector(v.position, moved + quarter * (startView - startGrip), s + ": upright, the view above it");
        CheckNearVector(v.up, quarter * (startViewTurn * Vector3.up), s + ": upright, the view upright");
        CheckNearVector(v.forward, quarter * (startViewTurn * Vector3.forward),
            s + ": upright, the view turned with the heading");
        Check(Utilities.IsValid(holder.gripModel), s + ": the bar assigned", "missing");
        if (Utilities.IsValid(holder.gripModel)) {
            CheckNearVector(holder.gripModel.up, Vector3.up, s + ": upright, the bar upright");
        }
        // As after a VR player picked it up: tilted with the grip again.
        holder.SetUpright(false);
        holder._Follow();
        CheckNearVector(v.up, turn * (startViewTurn * Vector3.up), s + ": not upright, the view tilted with the grip");
        if (Utilities.IsValid(holder.gripModel)) {
            CheckNearVector(holder.gripModel.up, grip.up, s + ": not upright, the bar with the grip");
        }
        // Upright for the size checks below.
        holder.SetUpright(true);
        holder._Follow();
        turn = quarter;

        // Resized about the view's bottom edge, which stays where it was from the grip, the corner beside its corner.
        holder.SetSize(2);
        CheckNear(holder.Size(), 2, s + ": size 2");
        CheckNearVector(v.localScale, startScale * 2, s + ": size 2, twice the scale");
        CheckNearVector(ViewBottom(0), moved + turn * (startBottom - startGrip),
            s + ": size 2, the bottom edge kept above the grip");
        CheckNearVector(holder.corner.position - ViewBottom(1), turn * (startCorner - startRight),
            s + ": size 2, the corner beside the view's corner");
        holder.SetSize(100);
        CheckNear(holder.Size(), holder.maxSize, s + ": size clamped to the largest");
        holder.SetSize(0);
        CheckNear(holder.Size(), holder.minSize, s + ": size clamped to the smallest");
        CheckNearVector(ViewBottom(0), moved + turn * (startBottom - startGrip),
            s + ": smallest, the bottom edge kept above the grip");

        // Not movable: nothing to pick up, the handles hidden; and back.
        holder.SetMovable(false);
        bool hidden = true;
        for (int i = 0; i < holder.handleRenderers.Length; i++) {
            if (Utilities.IsValid(holder.handleRenderers[i]) && holder.handleRenderers[i].enabled) hidden = false;
        }
        Check(!holder.gripPickup.pickupable && !holder.cornerPickup.pickupable && hidden,
            s + ": not movable, nothing pickupable and the handles hidden", "grip " + holder.gripPickup.pickupable
                + ", corner " + holder.cornerPickup.pickupable + ", hidden " + hidden);
        holder.SetMovable(true);
        Check(holder.gripPickup.pickupable && holder.cornerPickup.pickupable
            && holder.handleRenderers[0].enabled, s + ": movable again", "still off");

        // The view still draws where it's been moved (real clicks on it are left to a client run).
        view.Show(0);
        view._Refresh();
        CheckView(s + ": the view drawn after moving");

        // Put back where it was placed, at its size there.
        holder.PutBack();
        CheckNear(holder.Size(), 1, s + ": put back, size 1");
        CheckNearVector(grip.position, startGrip, s + ": put back, the grip where it was");
        CheckNearVector(v.position, startView, s + ": put back, the view where it was");
        CheckNearVector(v.forward, startViewTurn * Vector3.forward, s + ": put back, turned as it was");
        CheckNearVector(v.localScale, startScale, s + ": put back, its scale as it was");

        // At the smallest size, upright where it was placed, the bar and the corner don't meet.
        holder.SetSize(0);
        BoxCollider gripBox = grip.GetComponent<BoxCollider>();
        BoxCollider cornerBox = holder.corner.GetComponent<BoxCollider>();
        Check(Utilities.IsValid(gripBox) && Utilities.IsValid(cornerBox)
            && !gripBox.bounds.Intersects(cornerBox.bounds), s + ": smallest, the bar clear of the corner",
            Utilities.IsValid(gripBox) && Utilities.IsValid(cornerBox)
                ? "bar " + gripBox.bounds + ", corner " + cornerBox.bounds : "a collider missing");
        holder.SetSize(1);
        holder.SetUpright(false);
    }

    // The view's bottom edge in the world: its middle (0) or its right end (1).
    Vector3 ViewBottom(int right) {
        Rect rect = view.GetComponent<RectTransform>().rect;
        return view.transform.TransformPoint(new Vector3(right == 1 ? rect.xMax : rect.center.x, rect.yMin, 0));
    }

    void CheckNear(float actual, float expected, string name) {
        Check(Mathf.Abs(actual - expected) < 0.001f, name, "got " + actual + ", expected " + expected);
    }

    void CheckNearVector(Vector3 actual, Vector3 expected, string name) {
        Check(Vector3.Distance(actual, expected) < 0.001f, name, "got " + actual + ", expected " + expected);
    }

    void StartGame(ChessBoard board) {
        string s = "game on " + board.gameObject.name;
        CheckInt(board.JoinAs(ChessRules.White, Ana), ChessBoard.InputAccepted, s + ": Ana joins");
        CheckInt(board.JoinAs(ChessRules.Black, Bo), ChessBoard.InputAccepted, s + ": Bo joins");
        board.SetModeAs(Ana, ChessGame.ModeStandard);
        board.SetClockAs(Ana, ChessArbiter.NoClock, 0);
        CheckInt(board.StartGameAs(Ana), ChessBoard.InputAccepted, s + ": started");
    }

    // Plays from-to for the side to move's player.
    void Play(ChessBoard board, string from, string to, string name) {
        int side = board.SideToMove();
        string player = side == ChessRules.White ? board.whitePlayer : board.blackPlayer;
        CheckInt(board.PieceDroppedAs(player, Sq(from), Sq(to)), ChessBoard.InputAccepted,
            name + ": " + from + "-" + to + " played");
    }

    // Takes the first board's game back to ply: White asks, Black accepts.
    void TakeBack(ChessBoard board, int ply, string name) {
        CheckInt(board.EnterReplayAs(Ana, false), ChessBoard.InputAccepted, name + ": replay for a takeback");
        CheckInt(board.StepToAs(Ana, false, ply), ChessBoard.InputAccepted, name + ": the board at ply " + ply);
        CheckInt(board.RequestTakebackAs(ChessRules.White, Ana), ChessBoard.InputAccepted,
            name + ": takeback to ply " + ply + " asked");
        if (view.ShownBoard() == board) {
            view._Refresh();
            Check(view.statusLabel.text.IndexOf(ColorLine("status_takeback", ChessRules.White)) >= 0
                && view.statusLabel.text.IndexOf(ColorLine("status_takeback_to_here", ChessRules.White)) < 0,
                name + ": the request in the view's status", "got \"" + view.statusLabel.text + "\"");
        }
        CheckInt(board.AcceptTakebackAs(ChessRules.Black, Bo), ChessBoard.InputAccepted,
            name + ": takeback to ply " + ply + " accepted");
        CheckInt(board.moveCount, ply, name + ": the game back to ply " + ply);
    }

    // The step buttons' states, <<, <, >, >>, and their labels' colours.
    void CheckSteps(bool firstOn, bool backOn, bool forwardOn, bool lastOn, string name) {
        bool[] expected = { firstOn, backOn, forwardOn, lastOn };
        string got = "";
        bool ok = true;
        for (int i = 0; i < 4; i++) {
            bool on = view._IsStepEnabled(i);
            Color color = view.stepLabels[i].color;
            if (on != expected[i] || color != (expected[i] ? view.stepLabelColor : view.stepLabelDisabledColor)) {
                ok = false;
            }
            got += on ? "on " : "off ";
        }
        Check(ok, name + ": step buttons", "got " + got);
    }

    void CheckStatusEnds(string line, string name) {
        Check(view.statusLabel.text.EndsWith(line), name + ": status ends \"" + line + "\"",
            "got \"" + view.statusLabel.text + "\"");
    }

    // The view's texts by key (PLAN.md 13.5a): board's label ("Board 1"), a status line naming a side ("Black to
    // move, in check") and the replay line of a move ("Replay: 2. Nf3 (3 of 4)").
    string BoardLabel(ChessBoard board) {
        return TextWith("board_number", new string[] { "number", (board.boardNumber + 1).ToString() });
    }

    string ColorLine(string key, int color) {
        return TextWith(key, new string[] { "color", Text(color == ChessRules.White ? "color_white" : "color_black") });
    }

    string ReplayLine(string move, string san, int ply, int count) {
        return TextWith("replay_move", new string[] {
            "move", move, "san", san, "ply", ply.ToString(), "count", count.ToString() });
    }

    // The view at ply showing the position of fen, with the texts for that ply.
    void CheckViewAt(string fen, int ply, string name) {
        CheckInt(view.ViewPly(), ply, name + ": ply");
        byte[] expected = new byte[64];
        int[] state = new int[ChessPosition.StateLength];
        int clock;
        int number;
        ChessFen.ParseFen(fen, expected, state, out clock, out number);
        CheckPieces(expected, name);
        CheckTexts(name);
    }

    // The view showing the game's own position (not the board's replay).
    void CheckGamePosition(string name) {
        ChessBoard board = view.ShownBoard();
        CheckInt(view.ViewPly(), board.moveCount, name + ": ply");
        CheckPieces(board.board, name);
    }

    // Every square of the view against expected: its piece's sprite (hidden when empty) and _ShownPieceAt.
    void CheckPieces(byte[] expected, string name) {
        bool ok = true;
        string details = "";
        for (int square = 0; square < 64 && ok; square++) {
            int piece = expected[square];
            Image image = view.pieceImages[square];
            Sprite sprite = view._PieceSprite(piece);
            if (view._ShownPieceAt(square) != piece) {
                ok = false;
                details = ChessRules.SquareName(square) + " holds " + view._ShownPieceAt(square) + ", expected "
                    + piece;
            } else if (image.enabled != (piece != ChessRules.Empty) || (image.enabled && image.sprite != sprite)) {
                ok = false;
                details = ChessRules.SquareName(square) + " shows "
                    + (image.enabled && image.sprite != null ? image.sprite.name : "nothing");
            }
        }
        Check(ok, name + ": pieces", details);
    }

    void CheckTexts(string name) {
        ChessBoard board = view.ShownBoard();
        double now = board._ServerTime();
        string[] texts = view._GameTexts();
        CheckString(view.playersLabel.text, ChessBoardUI._PlayersTextFor(board, now, texts), name + ": players");
        CheckString(view.statusLabel.text, ChessBoard2DView._StatusTextFor(board, now, view.ViewPly(), texts),
            name + ": status");
        CheckString(view.boardLabel.text, BoardLabel(board), name + ": board label");
    }

    // The view on the latest position of the board shown: the game's position (not the board's replay), and the
    // texts as the board's UI writes them, without the board's replay lines.
    void CheckView(string name) {
        ChessBoard board = view.ShownBoard();
        CheckInt(view.ViewPly(), board.moveCount, name + ": on the latest");
        bool ok = true;
        string details = "";
        for (int square = 0; square < 64 && ok; square++) {
            int piece = board.board[square];
            Image image = view.pieceImages[square];
            Sprite expected = view._PieceSprite(piece);
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
        double now = board._ServerTime();
        string[] texts = view._GameTexts();
        CheckString(view.playersLabel.text, ChessBoardUI._PlayersTextFor(board, now, texts), name + ": players");
        // No takeback request is pending where this is called, so the view's lines are the UI's game lines.
        CheckString(view.statusLabel.text, ChessBoardUI._GameStatusTextFor(board, now, texts), name + ": status");
        CheckString(view.boardLabel.text, BoardLabel(board), name + ": board label");
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
}
