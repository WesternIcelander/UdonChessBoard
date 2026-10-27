using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Step 14.11's Play Mode check (PLAN.md, docs/testing.md): in a scene of its own made by Tools > Chess Board > Open
// Wiring Play Test (Editor/WiringTest.cs), with one of each prefab and two Chess Tables and none of their references
// set (the scene saved unwired), entering Play Mode has wired them. It checks the wiring, plays a fool's mate on each
// table, and checks that each game is archived and shown on the history wall, and that the 2D view shows the boards.
// The fields below are the test's own, set by the scene's builder; nothing on the chess objects is. Logs
// "[WiringPlayTest] PASS: <check>" or "[WiringPlayTest] FAIL: <check>: <details>" per check, then a summary line.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WiringPlayTest : ChessTest {
    protected override string TestName() {
        return "WiringPlayTest";
    }

    public ChessArbiter arbiter;
    // The two tables' boards, in the order they were placed.
    public ChessBoard[] boards = new ChessBoard[0];
    public InstanceGameArchive archive;
    public PersonalGameArchive personal;
    public GameHistoryWall wall;
    public GameHistoryWall personalWall;
    public ChessSettingsMenu menu;
    // The fixed 2D view and the movable one's.
    public ChessBoard2DView[] views = new ChessBoard2DView[0];

    bool joined;
    bool ran;

    void Start() {
        SendCustomEventDelayedFrames(nameof(_Run), 5);
    }

    public override void OnPlayerJoined(VRCPlayerApi player) {
        if (player.isLocal) joined = true;
    }

    public void _Run() {
        if (ran) return;
        if (!ReadyAfter(joined, null, null, null, "the local player")) {
            SendCustomEventDelayedFrames(nameof(_Run), 1);
            return;
        }
        ran = true;
        if (boards.Length != 2 || !Utilities.IsValid(boards[0]) || !Utilities.IsValid(boards[1])) {
            Check(false, "two boards assigned", "run Open Wiring Play Test again");
            LogSummary();
            return;
        }
        RunWiringChecks();
        for (int i = 0; i < 2; i++) PlayFoolsMate(i);
        SendCustomEventDelayedFrames(nameof(_CheckShown), 5);
    }

    void RunWiringChecks() {
        Check(boards[0].arbiter == arbiter && boards[1].arbiter == arbiter, "wiring: both boards on the arbiter",
            boards[0].arbiter + ", " + boards[1].arbiter);
        Check(boards[0].boardNumber == 0 && boards[1].boardNumber == 1, "wiring: boards numbered 0 and 1",
            boards[0].boardNumber + ", " + boards[1].boardNumber);
        CheckInt(arbiter.BoardCount(), 2, "wiring: the arbiter's board count");
        Check(arbiter.archives.Length == 2 && System.Array.IndexOf(arbiter.archives, archive) >= 0
                && System.Array.IndexOf(arbiter.archives, personal) >= 0, "wiring: the arbiter lists both archives",
            arbiter.archives.Length + " listed");
        Check(wall.archive == archive, "wiring: the history wall on the Game Archive", "on " + wall.archive);
        Check(personalWall.archive == personal, "wiring: the personal wall on the Personal Game Archive",
            "on " + personalWall.archive);
        Check(menu.arbiter == arbiter && menu.boards.Length == 2 && menu.boards[0] == boards[0]
                && menu.boards[1] == boards[1], "wiring: the settings menu on the arbiter with both boards in order",
            menu.boards.Length + " boards");
        for (int i = 0; i < views.Length; i++) {
            ChessBoard2DView view = views[i];
            Check(view.boards.Length == 2 && view.boards[0] == boards[0] && view.boards[1] == boards[1],
                "wiring: 2D view " + i + " on both boards in order", view.boards.Length + " boards");
        }
    }

    // Plays a fool's mate on boards[index], which ends the game by checkmate and logs it to the archives.
    void PlayFoolsMate(int index) {
        ChessBoard board = boards[index];
        string white = "Wiring White " + index;
        string black = "Wiring Black " + index;
        string name = "board " + index + ": ";
        board.JoinAs(ChessRules.White, white);
        board.JoinAs(ChessRules.Black, black);
        CheckInt(board.StartGameAs(white), ChessBoard.InputAccepted, name + "game started");
        CheckInt(board.PieceDroppedAs(white, Sq("f2"), Sq("f3")), ChessBoard.InputAccepted, name + "f3");
        CheckInt(board.PieceDroppedAs(black, Sq("e7"), Sq("e5")), ChessBoard.InputAccepted, name + "e5");
        CheckInt(board.PieceDroppedAs(white, Sq("g2"), Sq("g4")), ChessBoard.InputAccepted, name + "g4");
        CheckInt(board.PieceDroppedAs(black, Sq("d8"), Sq("h4")), ChessBoard.InputAccepted, name + "Qh4#");
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, name + "game ended");
        CheckInt(board.ending, ChessGame.EndingCheckmate, name + "by checkmate");
        CheckInt(archive.GameCount(), index + 1, name + "game archived");
        if (archive.GameCount() == index + 1) {
            CheckString(archive.WhiteAt(index), white, name + "archived White");
            CheckString(archive.BlackAt(index), black, name + "archived Black");
        }
    }

    // A few frames later, once the wall and the 2D views have redrawn.
    public void _CheckShown() {
        Check(wall.rows.Length >= 2 && wall.rows[0].text.Contains("Wiring White 1")
                && wall.rows[1].text.Contains("Wiring White 0"), "wall: both games shown, newest first",
            wall.rows.Length >= 2 ? "\"" + wall.rows[0].text + "\", \"" + wall.rows[1].text + "\"" : "no rows");
        for (int i = 0; i < views.Length; i++) {
            ChessBoard2DView view = views[i];
            Check(view.ShownBoard() == boards[0], "2D view " + i + ": shows the first board", "" + view.ShownBoard());
            CheckPosition(view, boards[0], "2D view " + i + ": the first board's mate");
            view.PressRight();
            Check(view.ShownBoard() == boards[1], "2D view " + i + ": Right shows the second board",
                "" + view.ShownBoard());
            CheckPosition(view, boards[1], "2D view " + i + ": the second board's mate");
            view.PressLeft();
        }
        LogSummary();
    }

    void CheckPosition(ChessBoard2DView view, ChessBoard board, string name) {
        string differs = "";
        for (int s = 0; s < 64 && differs == ""; s++) {
            if (view._ShownPieceAt(s) != board.PieceAt(s)) differs = "square " + s + " differs";
        }
        Check(differs == "" && board.PieceAt(Sq("h4")) != ChessRules.Empty, name, differs);
    }

    int Sq(string name) {
        return ChessRules.ParseSquare(name);
    }
}
