using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

// Step 9 test behaviour (PLAN.md section 9): the physical board (ChessBoardView) checked against the logical one it
// shows, and since step 12.1 the board's UI (ChessBoardUI: buttons, status display, PGN box), each checked on its own
// behaviour. It changes the board, so it runs once BoardTest has finished with it, and only with no one else in the
// instance. Logs "[ViewTest] PASS: <check>" or "[ViewTest] FAIL: <check>: <details>" per check, then a summary line.
// Built in the scene by Tools > Chess Board > Create View Test (Editor/ViewTestBuilder.cs).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ViewTest : UdonSharpBehaviour {
    // The board, its view and its UI under test.
    public ChessBoard board;
    public ChessBoardView view;
    public ChessBoardUI ui;
    // Waited for, since both change the board; none to start at once.
    public BoardTest boardTest;
    // For the loaded games: an importer and Assets/Tests/PGN/chess960.pgn.txt and promotions.pgn.txt.
    public ChessPgnImporter importer;
    public TextAsset chess960Pgn;
    public TextAsset promotionsPgn;
    // A second chess piece set (step 12.3): models turned 90°, twice the size, origin off centre, two material slots.
    public ChessPieceSet testPieceSet;
    // Set once the checks are over (or skipped), so ArchiveTest (step 10) can use the board after them.
    [System.NonSerialized] public bool finished;

    const string Files = "abcdefgh";
    const string Ranks = "12345678";

    string localName;
    int passed;
    int failed;
    bool joined;
    bool ran;
    int waitedFrames;
    bool savedAnimate;
    int savedAnimationStyle;
    Vector3[] before = new Vector3[ChessBoardView.PieceCount];

    void Start() {
        SendCustomEventDelayedFrames(nameof(WaitForBoardTest), 1);
    }

    public override void OnPlayerJoined(VRCPlayerApi player) {
        if (player.isLocal) joined = true;
    }

    public void WaitForBoardTest() {
        if (ran) return;
        if (!joined || (Utilities.IsValid(boardTest) && !boardTest.finished)) {
            waitedFrames++;
            if (waitedFrames % 3000 == 0) Debug.Log("[ViewTest] still waiting for BoardTest to finish");
            SendCustomEventDelayedFrames(nameof(WaitForBoardTest), 1);
            return;
        }
        ran = true;
        // As for BoardTest: the board is synced, so its changes would reach everyone else here.
        if (VRCPlayerApi.GetPlayerCount() > 1) {
            Debug.Log("[ViewTest] skipped: other players are in the instance");
            finished = true;
            return;
        }
        if (!Utilities.IsValid(board) || !Utilities.IsValid(view) || !Utilities.IsValid(ui)) {
            Check(false, "board, view and UI assigned", "assign ViewTest's Board, View and UI in the Inspector "
                + "(Tools > Chess Board > Create View Test)");
            LogSummary();
            return;
        }
        Check(view.board == board && ui.board == board, "the view and the UI on the board", "they aren't");
        CheckString(view.LocalSpaceProblem(), "", "the view's objects in its local space (step 12.2)");
        localName = Networking.LocalPlayer.displayName;
        // Every check but the animation checks (9.5b) looks at pieces in place straight after a change.
        savedAnimate = view.animateMoves;
        view.animateMoves = false;
        RunGameChecks();
    }

    // ---- 9.1: pieces placed from the board ----

    // The first frame: a cleared board, a game with an en passant capture and both castles, and a move left for the
    // view's own redraw on the next frame (RunResetChecks).
    void RunGameChecks() {
        board.ClearState();
        Redraw();
        CheckView("a cleared board");
        CheckInt(OnBoardCount(), 32, "a cleared board: all 32 objects on the board");

        StartGame(ChessGame.ModeStandard, "game");
        Play("e2", "e4", "e4", "e4");
        Play("g8", "f6", "f6", "Nf6");
        Play("e4", "e5", "e5", "e5");
        Play("d7", "d5", "d5", "d5");
        int capturedPawn = view.squareObject[Sq("d5")];
        Play("e5", "d6", "d6", "exd6 e.p.");
        Check(capturedPawn != ChessBoardView.NoObject
            && view.objectSquare[capturedPawn] == ChessRules.NoSquare, "exd6 e.p.: the d5 pawn's object is off the "
            + "board", "object " + capturedPawn + " on " + SquareText(SquareOrNone(capturedPawn)));
        Play("e7", "d6", "d6", "exd6");
        Play("g1", "f3", "f3", "Nf3");
        Play("f8", "e7", "e7", "Be7");
        Play("f1", "c4", "c4", "Bc4");
        // Castling by the king's destination, then by its own rook.
        int blackRook = view.squareObject[Sq("h8")];
        Play("e8", "g8", "g8", "O-O (king on g8)");
        CheckInt(view.squareObject[Sq("f8")], blackRook, "O-O: the h8 rook's object moved to f8");
        int whiteRook = view.squareObject[Sq("h1")];
        Play("e1", "h1", "g1", "O-O (king on h1)");
        CheckInt(view.squareObject[Sq("f1")], whiteRook, "O-O: the h1 rook's object moved to f1");

        // A move the view draws by itself, on the next frame (checked two frames on, whichever of the view and this
        // test runs first in a frame).
        CheckInt(board.PieceDroppedAs(localName, Sq("b8"), Sq("c6")), ChessBoard.InputAccepted, "Nc6: played");
        SendCustomEventDelayedFrames(nameof(RunResetChecks), 2);
    }

    // The second frame: the move drawn on its own, a reset, a promotion, a Chess960 start.
    public void RunResetChecks() {
        CheckView("Nc6 drawn on the next frame, without Refresh");

        CheckInt(board.ResetBoardAs(localName, true), ChessBoard.InputAccepted, "reset: accepted");
        Redraw();
        CheckView("after a reset");
        CheckInt(OnBoardCount(), 32, "after a reset: all 32 objects back on the board");

        board.ClearState();
        StartGame(ChessGame.ModeStandard, "promotion game");
        int aPawn = view.squareObject[Sq("a2")];
        Play("a2", "a4", "a4", "a4");
        Play("b7", "b5", "b5", "b5");
        Play("a4", "b5", "b5", "axb5");
        Play("a7", "a6", "a6", "a6");
        Play("b5", "a6", "a6", "bxa6");
        Play("c8", "b7", "b7", "Bb7");
        Play("a6", "b7", "b7", "axb7");
        Play("b8", "c6", "c6", "Nc6");
        CheckInt(board.PieceDroppedAs(localName, Sq("b7"), Sq("a8")), ChessBoard.InputPromotionNeeded,
            "bxa8: asks for a piece");
        Redraw();
        CheckView("bxa8: nothing played yet");
        CheckInt(board.PromotionChosenAs(localName, Sq("b7"), Sq("a8"), ChessRules.Queen), ChessBoard.InputAccepted,
            "bxa8=Q: played");
        Redraw();
        CheckView("bxa8=Q");
        CheckInt(view.squareObject[Sq("a8")], aPawn, "bxa8=Q: the a2 pawn's object is the queen");
        CheckInt(view.objectType[aPawn], ChessRules.Queen, "bxa8=Q: the object stands for a queen");

        board.ClearState();
        StartGame(ChessGame.ModeChess960, "Chess960 game");
        Redraw();
        CheckView("a Chess960 game started (" + ChessFen.WriteFen(board.board, board.state, 0, 1) + ")");
        board.ClearState();
        SendCustomEventDelayedFrames(nameof(RunLoadChecks), 1);
    }

    // The third frame: loaded games, stepped through in replay.
    public void RunLoadChecks() {
        CheckReplay(chess960Pgn, "chess960.pgn", 6);
        CheckReplay(promotionsPgn, "promotions.pgn", 9);
        board.ClearState();
        Redraw();
        CheckView("cleared after the loaded games");
        SendCustomEventDelayedFrames(nameof(RunPickupChecks), 1);
    }

    // Loads pgn (plies moves) and checks the view on its last position, then at the start, each ply stepping forward
    // (the mover's object travelling), and back at the start in one step.
    void CheckReplay(TextAsset pgn, string name, int plies) {
        Check(Utilities.IsValid(importer) && pgn != null, name + ": importer and PGN assigned",
            "assign ViewTest's Importer and PGNs in the Inspector");
        if (!Utilities.IsValid(importer) || pgn == null) return;
        board.ClearState();
        importer.StartImport(pgn.text, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
        CheckInt(importer.ImportFor(float.MaxValue), ChessPgn.ImportDone, name + ": imported");
        CheckInt(board.LoadImport(importer), ChessBoard.InputAccepted, name + ": loaded");
        CheckInt(board.moveCount, plies, name + ": " + plies + " plies");
        Redraw();
        CheckView(name + ": loaded, the last position");
        CheckInt(board.StepToAs(localName, true, 0), ChessBoard.InputAccepted, name + ": step to the start");
        Redraw();
        CheckView(name + ": the start");
        for (int ply = 1; ply <= board.moveCount; ply++) {
            int move = board.moveHistory[ply - 1];
            int mover = view.squareObject[ChessRules.MoveFrom(move)];
            string label = name + ": ply " + ply + " (" + board.notation[ply - 1] + ")";
            CheckInt(board.StepToAs(localName, true, ply), ChessBoard.InputAccepted, label + ": stepped");
            Redraw();
            CheckView(label);
            CheckInt(view.squareObject[Landing(move)], mover, label + ": the mover's object travelled");
        }
        CheckInt(board.StepToAs(localName, true, 0), ChessBoard.InputAccepted, name + ": back to the start");
        Redraw();
        CheckView(name + ": back to the start");
    }

    // ---- 9.2: pickups ----

    // The fourth frame: each piece wired to the view, then who gets pickups with each input style, and the pieces'
    // OnOwnershipRequest agreeing with the board. The view's input style is put back as it was.
    public void RunPickupChecks() {
        int savedStyle = view.inputStyle;
        VRCPlayerApi local = Networking.LocalPlayer;
        int wired = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            ChessPiece piece = PieceBehaviour(obj);
            if (Utilities.IsValid(piece) && piece.view == view && piece.index == obj
                && Utilities.IsValid(view.piecePickups[obj]) && Utilities.IsValid(view.pieceColliders[obj])
                && view.piecePickups[obj].gameObject == view.pieces[obj].gameObject) {
                wired++;
            }
        }
        CheckInt(wired, ChessBoardView.PieceCount, "every piece has its pickup, collider and ChessPiece");

        board.ClearState();
        view.SetInputStyle(ChessBoardView.InputClicks);
        Redraw();
        Check(!view.UsesPickups(), "Clicks: no pickups", "uses pickups");
        CheckPickupable(0, "Clicks: a reset board");
        view.SetInputStyle(ChessBoardView.InputAuto);
        bool inVR = local.IsUserInVR();
        Check(view.UsesPickups() == inVR, "Auto: pickups exactly in VR (in VR: " + inVR + ")",
            "uses pickups: " + view.UsesPickups());
        CheckPickupable(inVR ? 32 : 0, "Auto: a reset board");

        view.SetInputStyle(ChessBoardView.InputPickups);
        Check(view.UsesPickups(), "Pickups: pickups", "no pickups");
        CheckPickupable(32, "Pickups: a reset board, anyone");
        CheckOwnership(true, "a reset board");

        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "Ana joined White");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "Bob joined Black");
        Redraw();
        CheckPickupable(0, "Pickups: the lobby of others");
        CheckOwnership(false, "the lobby of others");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, "Ana and Bob started");
        Redraw();
        CheckPickupable(0, "Pickups: others' game");
        CheckOwnership(false, "others' game");
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "Ana resigned");
        Redraw();
        CheckPickupable(0, "Pickups: others' ended game");

        board.ClearState();
        CheckInt(board.JoinAs(ChessRules.White, localName), ChessBoard.InputAccepted, "pickups game: joined White");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "pickups game: Bob joined Black");
        CheckInt(board.StartGameAs(localName), ChessBoard.InputAccepted, "pickups game: started");
        Redraw();
        CheckPickupable(32, "Pickups: own game");
        CheckOwnership(true, "own game");
        CheckInt(board.PieceDroppedAs(localName, Sq("e2"), Sq("e4")), ChessBoard.InputAccepted, "e4: played");
        Redraw();
        CheckPickupable(32, "Pickups: own game, the other side to move");
        CheckInt(board.PieceDroppedAs("Bob", Sq("d7"), Sq("d5")), ChessBoard.InputAccepted, "d5: played");
        CheckInt(board.PieceDroppedAs(localName, Sq("e4"), Sq("d5")), ChessBoard.InputAccepted, "exd5: played");
        Redraw();
        CheckPickupable(31, "Pickups: the captured pawn isn't a pickup");
        CheckInt(board.ResignAs(ChessRules.White, localName), ChessBoard.InputAccepted, "pickups game: resigned");
        Redraw();
        CheckPickupable(31, "Pickups: own ended game");
        CheckOwnership(true, "own ended game");
        view.SetInputStyle(ChessBoardView.InputClicks);
        CheckPickupable(0, "Clicks: own ended game");
        view.SetInputStyle(ChessBoardView.InputPickups);
        CheckPickupable(31, "Pickups again: own ended game");

        board.ClearState();
        view.SetInputStyle(savedStyle);
        Redraw();
        CheckView("cleared after the pickup checks");
        SendCustomEventDelayedFrames(nameof(RunDropChecks), 1);
    }

    // ---- 9.3a: pickups and drops forwarded to the board ----

    // The fifth frame: pickups and drops through the view, as the pieces' OnPickup and OnDrop make them, each piece
    // put where it's dropped first: legal drops played, rejected ones (an illegal move, off the board, back on its
    // own square, after the game ended, a pickup out of turn) put back, a held piece left alone until the board
    // takes its square, free moves in replay, and a promotion waiting for its piece.
    public void RunDropChecks() {
        int savedStyle = view.inputStyle;
        view.SetInputStyle(ChessBoardView.InputPickups);
        board.ClearState();
        StartGame(ChessGame.ModeStandard, "drop game");

        CheckInt(view.DropSquare(view.transform.TransformPoint(Over("e4"))), Sq("e4"), "drop square: over e4");
        CheckInt(view.DropSquare(view.transform.TransformPoint(Over("a1"))), Sq("a1"), "drop square: over a1");
        CheckInt(view.DropSquare(view.transform.TransformPoint(Over("h8"))), Sq("h8"), "drop square: over h8");
        CheckInt(view.DropSquare(view.transform.TransformPoint(OffBoard())), ChessRules.NoSquare,
            "drop square: beside the board");

        int pawn = view.squareObject[Sq("e2")];
        view.OnPiecePickedUp(pawn);
        CheckInt(view.heldObject, pawn, "e2 picked up: held");
        view.pieces[pawn].localPosition = Over("e4");
        Redraw();
        Check((view.pieces[pawn].localPosition - Over("e4")).sqrMagnitude < 1e-8f, "e2 held: not placed by Refresh",
            "at " + view.pieces[pawn].localPosition.ToString("F3"));
        view.OnPieceDropped(pawn);
        CheckInt(view.heldObject, ChessBoardView.NoObject, "e2 dropped: no longer held");
        CheckDropped(1, ChessBoard.ChangeMove, "e2 dropped over e4: e4 played");
        CheckInt(ChessRules.MoveTo(board.LastMove()), Sq("e4"), "e2 dropped over e4: the move goes to e4");
        CheckInt(view.squareObject[Sq("e4")], pawn, "e2 dropped over e4: the pawn's object on e4");

        CheckInt(ViewDrop("e7", Over("e3")), ChessBoard.InputAccepted, "e7 picked up");
        CheckDropped(1, ChessBoard.ChangeRejected, "e7 dropped over e3: rejected, put back");
        CheckInt(ViewDrop("d7", OffBoard()), ChessBoard.InputAccepted, "d7 picked up");
        CheckDropped(1, ChessBoard.ChangeRejected, "d7 dropped beside the board: rejected, put back");
        CheckInt(ViewDrop("g8", Over("g8")), ChessBoard.InputAccepted, "g8 picked up");
        CheckDropped(1, ChessBoard.ChangeRejected, "g8 dropped back on g8: rejected, put back");
        // Over f6, where a drop forwarded from g8 (the last pickup) would play Nf6.
        CheckInt(ViewDrop("d2", Over("f6")), ChessBoard.InputRejected, "d2 picked up with Black to move: refused");
        CheckDropped(1, ChessBoard.ChangeRejected, "d2 dropped over f6 after a refused pickup: not forwarded");
        CheckInt(ViewDrop("g8", Over("f6")), ChessBoard.InputAccepted, "g8 picked up again");
        CheckDropped(2, ChessBoard.ChangeMove, "g8 dropped over f6: Nf6 played");

        // Picked up, then the game ends before the drop.
        int d2 = view.squareObject[Sq("d2")];
        view.OnPiecePickedUp(d2);
        CheckInt(view.heldObject, d2, "d2 picked up");
        CheckInt(board.ResignAs(ChessRules.White, localName), ChessBoard.InputAccepted, "White resigned while holding d2");
        Redraw();
        CheckInt(view.heldObject, d2, "d2 still held after the game ended, its square unchanged");
        view.pieces[d2].localPosition = Over("d4");
        view.OnPieceDropped(d2);
        CheckDropped(2, ChessBoard.ChangeRejected, "d2 dropped over d4 after the game ended: rejected, put back");

        // In replay: free moves, and a held piece whose square another free move takes.
        CheckInt(board.EnterReplayAs(localName, true), ChessBoard.InputAccepted, "entered replay");
        Redraw();
        CheckInt(ViewDrop("a1", Over("a5")), ChessBoard.InputAccepted, "replay: a1 picked up");
        CheckDropped(2, ChessBoard.ChangeFreeMove, "replay: a1 dropped over a5, a free move");
        CheckInt(board.replayBoard[Sq("a5")], ChessRules.PieceOf(ChessRules.White, ChessRules.Rook),
            "replay: the rook on a5");
        int e4 = view.squareObject[Sq("e4")];
        view.OnPiecePickedUp(e4);
        CheckInt(view.heldObject, e4, "replay: e4 picked up");
        CheckInt(board.PieceDroppedAs(localName, Sq("d8"), Sq("e4")), ChessBoard.InputAccepted,
            "replay: the queen freely to e4 while e4 is held");
        Redraw();
        CheckInt(view.heldObject, ChessBoardView.NoObject, "replay: e4 let go once the queen took its square");
        CheckView("replay: e4 let go");

        // A promotion waits for its piece, then is cancelled, then played.
        board.ClearState();
        StartGame(ChessGame.ModeStandard, "promotion drop game");
        Play("a2", "a4", "a4", "a4");
        Play("b7", "b5", "b5", "b5");
        Play("a4", "b5", "b5", "axb5");
        Play("a7", "a6", "a6", "a6");
        Play("b5", "a6", "a6", "bxa6");
        Play("c8", "b7", "b7", "Bb7");
        Play("a6", "b7", "b7", "axb7");
        Play("b8", "c6", "c6", "Nc6");
        int promoting = view.squareObject[Sq("b7")];
        CheckInt(ViewDrop("b7", Over("a8")), ChessBoard.InputAccepted, "b7 picked up");
        CheckInt(view.promotionObject, promoting, "b7 dropped over a8: waits for a piece");
        CheckInt(board.moveCount, 8, "b7 dropped over a8: nothing played yet");
        Redraw();
        Check((view.pieces[promoting].localPosition - view.SquarePosition(Sq("a8"))).sqrMagnitude < 1e-8f,
            "b7 dropped over a8: the pawn stands on a8 meanwhile",
            "at " + view.pieces[promoting].localPosition.ToString("F3"));
        view.CancelPromotion();
        CheckInt(view.promotionObject, ChessBoardView.NoObject, "promotion cancelled: nothing waits");
        CheckDropped(8, ChessBoard.ChangeRejected, "promotion cancelled: the pawn back on b7");
        CheckInt(view.ChoosePromotion(ChessRules.Queen), ChessBoard.InputRejected,
            "a choice with no promotion waiting: refused");
        CheckInt(ViewDrop("b7", Over("a8")), ChessBoard.InputAccepted, "b7 picked up again");
        CheckInt(view.promotionObject, promoting, "b7 dropped over a8 again: waits for a piece");
        CheckInt(view.ChoosePromotion(ChessRules.Knight), ChessBoard.InputAccepted, "bxa8=N chosen: played");
        CheckDropped(9, ChessBoard.ChangeMove, "bxa8=N");
        CheckInt(view.squareObject[Sq("a8")], promoting, "bxa8=N: the pawn's object on a8");
        CheckInt(view.objectType[promoting], ChessRules.Knight, "bxa8=N: the object stands for a knight");

        board.ClearState();
        view.SetInputStyle(savedStyle);
        Redraw();
        CheckView("cleared after the drop checks");
        SendCustomEventDelayedFrames(nameof(RunCastleDropChecks), 1);
    }

    // ---- 9.3b: castling by drop ----

    // King f1, rook h1 (and the same for Black): castling and Kg1 both put the king on g1, so a drop on g1 is Kg1 and
    // the castle is a drop on the rook. The f pawns keep a castled rook from giving check.
    const string OneSquareFen = "1r3k1r/5p2/8/8/8/8/5P2/1R3K1R w KQkq - 0 1";
    // King d1, rook a1: the queen side castle moves the king one square, to c1.
    const string QueenOneSquareFen = "r2k3r/3p4/8/8/8/8/3P4/R2K3R w KQkq - 0 1";
    const string StandardCastlingFen = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";

    // The sixth frame (the castle drop space was removed in step 9.6): in Chess960 positions where castling is
    // ambiguous, a king dropped on its rook castles and one dropped on the destination makes the normal king move;
    // a king dropped beyond the back rank, where the space stood, is put back.
    public void RunCastleDropChecks() {
        int savedStyle = view.inputStyle;
        view.SetInputStyle(ChessBoardView.InputPickups);

        CastleGame(OneSquareFen, "f1 game");
        int king = view.squareObject[Sq("f1")];
        int rook = view.squareObject[Sq("h1")];
        view.OnPiecePickedUp(king);
        CheckInt(view.heldObject, king, "f1 picked up");
        view.pieces[king].localPosition = Over("h1");
        view.OnPieceDropped(king);
        CheckDropped(1, ChessBoard.ChangeMove, "f1 dropped on h1: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "f1 dropped on h1: the move is a castle", "it isn't");
        CheckInt(view.squareObject[Sq("g1")], king, "f1 dropped on h1: the king's object on g1");
        CheckInt(view.squareObject[Sq("f1")], rook, "f1 dropped on h1: the rook's object on f1");

        // Black: a drop on the destination is Kg8, not a castle.
        int blackKing = view.squareObject[Sq("f8")];
        view.OnPiecePickedUp(blackKing);
        view.pieces[blackKing].localPosition = Over("g8");
        view.OnPieceDropped(blackKing);
        CheckDropped(2, ChessBoard.ChangeMove, "f8 dropped over g8: played");
        Check(!ChessRules.MoveIsCastle(board.LastMove()), "f8 dropped over g8: Kg8, not a castle", "it castled");
        CheckInt(board.PieceAt(Sq("h8")), ChessRules.PieceOf(ChessRules.Black, ChessRules.Rook),
            "f8 dropped over g8: the rook still on h8");

        // A king dropped beyond the back rank, where the space stood, is put back; over g1 it plays Kg1.
        CastleGame(OneSquareFen, "f1 game again");
        king = view.squareObject[Sq("f1")];
        view.OnPiecePickedUp(king);
        view.pieces[king].localPosition = Over("g1") + new Vector3(0, 0, -view.squareSize);
        view.OnPieceDropped(king);
        CheckDropped(0, ChessBoard.ChangeRejected, "f1 dropped beyond g1: put back");
        view.OnPiecePickedUp(king);
        view.pieces[king].localPosition = Over("g1");
        view.OnPieceDropped(king);
        CheckDropped(1, ChessBoard.ChangeMove, "f1 dropped over g1: played");
        Check(!ChessRules.MoveIsCastle(board.LastMove()), "f1 dropped over g1: Kg1, not a castle", "it castled");

        // King d1, rook a1: dropped on a1, it castles queen side.
        CastleGame(QueenOneSquareFen, "d1 game");
        king = view.squareObject[Sq("d1")];
        rook = view.squareObject[Sq("a1")];
        view.OnPiecePickedUp(king);
        view.pieces[king].localPosition = Over("a1");
        view.OnPieceDropped(king);
        CheckDropped(1, ChessBoard.ChangeMove, "d1 dropped on a1: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "d1 dropped on a1: the move is a castle", "it isn't");
        CheckInt(view.squareObject[Sq("c1")], king, "d1 dropped on a1: the king's object on c1");
        CheckInt(view.squareObject[Sq("d1")], rook, "d1 dropped on a1: the rook's object on d1");

        // King already on g1: castling doesn't move it; dropped on h1 it castles, the rook's object going to f1.
        CastleGame(KingStaysFen, "g1 game");
        king = view.squareObject[Sq("g1")];
        rook = view.squareObject[Sq("h1")];
        view.OnPiecePickedUp(king);
        view.pieces[king].localPosition = Over("h1");
        view.OnPieceDropped(king);
        CheckDropped(1, ChessBoard.ChangeMove, "g1 dropped on h1: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "g1 dropped on h1: the move is a castle", "it isn't");
        CheckInt(view.squareObject[Sq("g1")], king, "g1 dropped on h1: the king's object on g1");
        CheckInt(view.squareObject[Sq("f1")], rook, "g1 dropped on h1: the rook's object on f1");

        board.ClearState();
        view.SetInputStyle(savedStyle);
        Redraw();
        CheckView("cleared at the end");
        SendCustomEventDelayedFrames(nameof(RunClickChecks), 1);
    }

    // ---- 9.3c: desktop mode ----

    // A white pawn about to promote on e8, the kings out of its way.
    const string PromotionFen = "k7/4P3/8/8/8/8/8/K7 w - - 0 1";

    // The seventh frame, with Clicks: selecting by clicking a piece (lifted, its ownership taken), putting it back by
    // clicking it or its square, switching to another piece, placing on a square, capturing by clicking a piece,
    // castling on the rook (in Chess960 too) and on the destination, promoting, illegal placings put back
    // unselected, a selection put back when the game ends or replay is left, free moves in replay, and exactly the
    // pieces and squares where a click does something interactive, with the right text. Then with Pickups: no
    // interacts at all.
    public void RunClickChecks() {
        int savedStyle = view.inputStyle;
        view.SetInputStyle(ChessBoardView.InputClicks);
        int wired = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (Utilities.IsValid(view.pieceBehaviours[obj]) && view.pieceBehaviours[obj] == PieceBehaviour(obj)) wired++;
        }
        CheckInt(wired, ChessBoardView.PieceCount, "clicks: every piece's ChessPiece assigned to the view");
        int squares = 0;
        for (int square = 0; square < 64; square++) {
            ChessSquare target = view.squareTargets[square];
            if (Utilities.IsValid(target) && target.view == view && target.square == square
                && Utilities.IsValid(target.GetComponent<Collider>())
                && view.DropSquare(target.transform.position) == square) {
                squares++;
            }
        }
        CheckInt(squares, 64, "clicks: every square has its target, over that square");

        // Nobody's game: nothing to click.
        board.ClearState();
        Redraw();
        CheckClickables("", "", "clicks: a reset board");

        string c = "click game";
        board.ClearState();
        StartGame(ChessGame.ModeStandard, c);
        CheckClickables(WhitePieces, "", c + ": White to move");
        CheckText(Sq("e2"), "Select", c + ": e2");

        // Selected and put back, by clicking the piece and by clicking its square.
        int pawn = view.squareObject[Sq("e2")];
        ClickPiece("e2");
        CheckInt(view.selectedObject, pawn, c + ": e2 clicked: selected");
        Check(Networking.IsOwner(Networking.LocalPlayer, view.pieces[pawn].gameObject),
            c + ": e2 clicked: its ownership taken", "not the owner");
        Redraw();
        CheckLifted(pawn, "e2", c + ": e2 selected");
        CheckClickables(WhitePieces, "e2 e3 e4", c + ": e2 selected");
        CheckText(Sq("e2"), "Put back", c + ": e2 selected");
        CheckSquareText("e2", "Put back", c + ": e2 selected");
        CheckSquareText("e4", "Place", c + ": e2 selected");
        CheckText(Sq("d2"), "Select", c + ": e2 selected, d2");
        ClickPiece("e2");
        CheckInt(board.lastChange, ChessBoard.ChangeStarted, c + ": e2 clicked again: the board not asked");
        CheckUnselected(0, c + ": e2 clicked again: put back");
        ClickPiece("e2");
        ClickSquare("e2");
        CheckInt(board.lastChange, ChessBoard.ChangeStarted, c + ": its square clicked: the board not asked");
        CheckUnselected(0, c + ": e2 selected, its square clicked: put back");

        // Switching to another piece, then placing it.
        int knight = view.squareObject[Sq("g1")];
        ClickPiece("e2");
        ClickPiece("g1");
        CheckInt(view.selectedObject, knight, c + ": g1 clicked with e2 selected: g1 selected");
        Redraw();
        CheckLifted(knight, "g1", c + ": g1 selected");
        Check((view.pieces[pawn].localPosition - view.SquarePosition(Sq("e2"))).sqrMagnitude < 1e-8f,
            c + ": g1 selected: e2 back down", "at " + view.pieces[pawn].localPosition.ToString("F3"));
        CheckClickables(WhitePieces, "g1 f3 h3", c + ": g1 selected");
        ClickSquare("f3");
        CheckUnselected(1, c + ": f3 clicked: Nf3 played");
        CheckInt(view.squareObject[Sq("f3")], knight, c + ": Nf3: the g1 object on f3");
        CheckClickables(BlackPieces, "", c + ": Black to move");

        ClickPiece("e7");
        ClickSquare("e5");
        CheckUnselected(2, c + ": e7 then e5 clicked: e5 played");

        // Capturing by clicking the opponent's piece.
        ClickPiece("f3");
        Redraw();
        CheckText(Sq("e5"), "Capture", c + ": f3 selected, e5");
        Check(view.IsPieceClickable(view.squareObject[Sq("e5")]), c + ": f3 selected: e5 clickable", "it isn't");
        Check(!view.IsPieceClickable(view.squareObject[Sq("d7")]), c + ": f3 selected: d7 not clickable", "it is");
        ClickPiece("e5");
        CheckUnselected(3, c + ": e5 clicked with f3 selected: Nxe5 played");
        CheckInt(ChessRules.MoveTo(board.LastMove()), Sq("e5"), c + ": Nxe5 goes to e5");
        CheckInt(view.squareObject[Sq("e5")], knight, c + ": Nxe5: the knight's object on e5");

        // Illegal placings: put back, unselected.
        ClickPiece("a7");
        view.OnSquareClicked(Sq("a4"));
        CheckInt(board.lastChange, ChessBoard.ChangeRejected, c + ": a7 to a4 rejected");
        CheckUnselected(3, c + ": a7 selected, a4 clicked: put back");
        ClickPiece("d8");
        view.OnPieceClicked(view.squareObject[Sq("e5")]);
        CheckInt(board.lastChange, ChessBoard.ChangeRejected, c + ": d8 to e5 rejected");
        CheckUnselected(3, c + ": d8 selected, the e5 knight clicked: put back");
        CheckInt(view.squareObject[Sq("e5")], knight, c + ": the e5 knight still there");

        // The game ending while a piece is selected puts it back.
        ClickPiece("d7");
        CheckInt(view.selectedObject, view.squareObject[Sq("d7")], c + ": d7 selected");
        CheckInt(board.ResignAs(ChessRules.Black, localName), ChessBoard.InputAccepted, c + ": resigned with d7 selected");
        Redraw();
        CheckView(c + ": resigned: d7 down on that Refresh");
        CheckUnselected(3, c + ": resigned: d7 put back");
        CheckClickables("", "", c + ": ended");

        // In replay: free moves of any piece; another piece of the selected one's colour is selected instead, one of
        // the other colour captured; leaving replay puts a selection back.
        CheckInt(board.EnterReplayAs(localName, true), ChessBoard.InputAccepted, c + ": entered replay");
        Redraw();
        CheckInt(ClickableCount(), OnBoardCount(), c + ": replay: every piece on the board clickable");
        ClickPiece("a1");
        CheckInt(view.selectedObject, view.squareObject[Sq("a1")], c + ": replay: a1 selected");
        Redraw();
        CheckInt(SquareTargetCount(), 64, c + ": replay: a1 selected, every square clickable");
        CheckText(Sq("a8"), "Capture", c + ": replay: a1 selected, a8");
        CheckText(Sq("b1"), "Select", c + ": replay: a1 selected, b1");
        ClickPiece("b1");
        CheckInt(view.selectedObject, view.squareObject[Sq("b1")], c + ": replay: b1 clicked: b1 selected instead");
        ClickPiece("b8");
        CheckInt(board.lastChange, ChessBoard.ChangeFreeMove, c + ": replay: b8 clicked with b1 selected: a free move");
        CheckUnselected(3, c + ": replay: b1 to b8");
        CheckInt(board.replayBoard[Sq("b8")], ChessRules.PieceOf(ChessRules.White, ChessRules.Knight),
            c + ": replay: the white knight on b8");
        ClickPiece("h2");
        ClickSquare("h5");
        CheckInt(board.lastChange, ChessBoard.ChangeFreeMove, c + ": replay: h2 then h5 clicked: a free move");
        CheckUnselected(3, c + ": replay: h2 to h5");
        CheckInt(board.replayBoard[Sq("h5")], ChessRules.PieceOf(ChessRules.White, ChessRules.Pawn),
            c + ": replay: the white pawn on h5");
        ClickPiece("c1");
        CheckInt(view.selectedObject, view.squareObject[Sq("c1")], c + ": replay: c1 selected");
        CheckInt(board.LeaveReplayAs(localName, true), ChessBoard.InputAccepted, c + ": left replay with c1 selected");
        Redraw();
        CheckUnselected(3, c + ": left replay: c1 put back");

        // Castling on the rook (labelled Castle) and on the destination.
        CastleGame(StandardCastlingFen, "click castling");
        int king = view.squareObject[Sq("e1")];
        int rook = view.squareObject[Sq("h1")];
        ClickPiece("e1");
        Redraw();
        CheckClickables("a1 e1 h1", "a1 c1 d1 e1 f1 g1 h1 d2 e2 f2", "click castling: e1 selected");
        CheckText(Sq("h1"), "Castle", "click castling: e1 selected, h1");
        ClickPiece("h1");
        CheckUnselected(1, "click castling: h1 clicked with e1 selected: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "click castling: on the rook, a castle", "it isn't");
        CheckInt(view.squareObject[Sq("g1")], king, "click castling: the king's object on g1");
        CheckInt(view.squareObject[Sq("f1")], rook, "click castling: the rook's object on f1");
        ClickPiece("e8");
        ClickSquare("c8");
        CheckUnselected(2, "click castling: e8 then c8 clicked: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "click castling: on the destination, a castle", "it isn't");

        // Chess960, king f1 and rook h1: a click on the rook castles, a click on g1 is Kg1.
        CastleGame(OneSquareFen, "click chess960");
        king = view.squareObject[Sq("f1")];
        rook = view.squareObject[Sq("h1")];
        ClickPiece("f1");
        CheckText(Sq("h1"), "Castle", "click chess960: f1 selected, h1");
        CheckSquareText("g1", "Place", "click chess960: f1 selected, g1");
        ClickPiece("h1");
        CheckUnselected(1, "click chess960: h1 clicked with f1 selected: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "click chess960: on the rook, a castle", "it isn't");
        CheckInt(view.squareObject[Sq("g1")], king, "click chess960: the king's object on g1");
        CheckInt(view.squareObject[Sq("f1")], rook, "click chess960: the rook's object on f1");
        ClickPiece("f8");
        ClickSquare("g8");
        CheckUnselected(2, "click chess960: f8 then g8 clicked: played");
        Check(!ChessRules.MoveIsCastle(board.LastMove()), "click chess960: g8 clicked: Kg8, not a castle",
            "it castled");

        // Promoting by clicking: the pawn waits on e8 for its piece.
        CastleGame(PromotionFen, "click promotion");
        int promoting = view.squareObject[Sq("e7")];
        ClickPiece("e7");
        ClickSquare("e8");
        CheckInt(view.promotionObject, promoting, "click promotion: e8 clicked: waits for a piece");
        CheckInt(view.selectedObject, ChessBoardView.NoObject, "click promotion: nothing selected");
        CheckInt(board.moveCount, 0, "click promotion: nothing played yet");
        Redraw();
        Check((view.pieces[promoting].localPosition - view.SquarePosition(Sq("e8"))).sqrMagnitude < 1e-8f,
            "click promotion: the pawn stands on e8 meanwhile", "at " + view.pieces[promoting].localPosition.ToString("F3"));
        CheckInt(view.ChoosePromotion(ChessRules.Rook), ChessBoard.InputAccepted, "click promotion: e8=R chosen");
        CheckDropped(1, ChessBoard.ChangeMove, "click promotion: e8=R");
        CheckInt(view.objectType[promoting], ChessRules.Rook, "click promotion: the pawn's object is a rook");

        // A VR player (Pickups): pickups and no interacts; switching to it puts a selection back.
        board.ClearState();
        StartGame(ChessGame.ModeStandard, "pickups, no clicks");
        ClickPiece("e2");
        CheckInt(view.selectedObject, view.squareObject[Sq("e2")], "pickups, no clicks: e2 selected with Clicks");
        view.SetInputStyle(ChessBoardView.InputPickups);
        Redraw();
        CheckUnselected(0, "pickups, no clicks: switched to Pickups: e2 put back");
        CheckClickables("", "", "pickups, no clicks: Pickups");
        CheckPickupable(32, "pickups, no clicks: Pickups");
        view.OnPieceClicked(view.squareObject[Sq("e2")]);
        CheckInt(view.selectedObject, ChessBoardView.NoObject, "pickups, no clicks: a click selects nothing");

        board.ClearState();
        view.SetInputStyle(savedStyle);
        Redraw();
        CheckView("cleared after the click checks");
        SendCustomEventDelayedFrames(nameof(RunButtonChecks), 1);
    }

    // ---- 9.4a: board buttons ----

    // The Button* actions' names, as CheckButtons lists them.
    string[] buttonNames = {
        "JoinWhite", "JoinBlack", "Mode", "Clock", "Start", "Continue", "Reset", "ResignWhite", "ResignBlack",
        "OfferDrawWhite", "OfferDrawBlack", "AcceptDrawWhite", "AcceptDrawBlack", "DeclineDrawWhite", "DeclineDrawBlack",
        "ClaimDraw", "Replay", "Leave", "First", "Back", "Forward", "Last", "TakebackWhite", "TakebackBlack",
        "AcceptTakebackWhite", "AcceptTakebackBlack", "DeclineTakebackWhite", "DeclineTakebackBlack", "LoadHere",
        "Pgn", "LoadPgn",
    };

    // The eighth frame: every action has its buttons, each with a label; in each state (a reset board, the local
    // player's lobby and game, someone else's lobby and game, an ended game, a loaded game joined and continued) the
    // buttons shown are exactly the ones that apply, and pressing one does what the board's method does, a hidden
    // one being refused.
    public void RunButtonChecks() {
        int count = ui.buttons.Length;
        Check(count > 0 && ui.buttonActions.Length == count && ui.buttonLabels.Length == count,
            "buttons: wired, each with its action and label", count + " buttons, " + ui.buttonActions.Length
            + " actions, " + ui.buttonLabels.Length + " labels");
        string missing = "";
        for (int action = 0; action < ChessBoardUI.ButtonCount; action++) {
            bool found = false;
            for (int i = 0; i < count && i < ui.buttonActions.Length; i++) {
                if (ui.buttonActions[i] == action && Utilities.IsValid(ui.buttons[i])
                    && i < ui.buttonLabels.Length && Utilities.IsValid(ui.buttonLabels[i])) {
                    found = true;
                }
            }
            if (!found) missing += buttonNames[action] + " ";
        }
        Check(missing.Length == 0, "buttons: every action has a button", "none for " + missing);
        CheckString(ChessBoardUI.ClockText(ChessArbiter.NoClock, 0), "No clock", "clock text: none");
        CheckString(ChessBoardUI.ClockText(300, 3), "5+3", "clock text: 5+3");
        CheckString(ChessBoardUI.ClockText(45, 0), "45s+0", "clock text: 45 s");

        board.ClearState();
        CheckButtons("JoinWhite JoinBlack", "reset board");
        CheckInt(ui.PressButton(ChessBoardUI.ButtonStart), ChessBoard.InputRejected, "reset board: Start refused");
        CheckInt(ui.PressButton(ChessBoardUI.ButtonReset), ChessBoard.InputRejected, "reset board: Reset refused");

        // The local player's lobby, White first.
        ui.PressJoinWhite();
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "Join White: in the lobby");
        CheckString(board.whitePlayer, localName, "Join White: seated as White");
        CheckButtons("JoinBlack Mode Clock Reset", "lobby, White joined");
        CheckInt(ui.PressButton(ChessBoardUI.ButtonStart), ChessBoard.InputRejected,
            "lobby, White joined: Start refused");
        if (!board.IsModeForced()) {
            CheckInt(ui.PressButton(ChessBoardUI.ButtonMode), ChessBoard.InputAccepted, "Mode pressed");
            CheckInt(board.mode, ChessGame.ModeChess960, "Mode pressed: Chess960");
            CheckLabel(ChessBoardUI.ButtonMode, "Mode: Chess960", "Mode pressed");
            ui.PressMode();
            CheckInt(board.mode, ChessGame.ModeStandard, "Mode pressed again: Standard");
            CheckLabel(ChessBoardUI.ButtonMode, "Mode: Standard", "Mode pressed again");
        }
        if (!board.IsClockForced()) {
            CheckLabel(ChessBoardUI.ButtonClock, "Clock: No clock", "lobby, no clock");
            CheckInt(ui.PressButton(ChessBoardUI.ButtonClock), ChessBoard.InputAccepted, "Clock pressed");
            CheckInt(board.baseTime * 1000 + board.increment, 60000, "Clock pressed: 1+0");
            CheckLabel(ChessBoardUI.ButtonClock, "Clock: 1+0", "Clock pressed");
            ui.PressClock();
            CheckInt(board.baseTime * 1000 + board.increment, 180002, "Clock pressed again: 3+2");
            CheckLabel(ChessBoardUI.ButtonClock, "Clock: 3+2", "Clock pressed again");
        }
        ui.PressJoinBlack();
        CheckString(board.blackPlayer, localName, "Join Black: seated as Black too");
        CheckButtons("Mode Clock Start Reset", "lobby, both seats");
        int baseTime = board.baseTime;
        ui.PressStart();
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "Start: in game");
        CheckInt(board.baseTime, baseTime, "Start: with the clock chosen");
        CheckButtons(OwnGame, "own game");
        CheckInt(ui.PressButton(ChessBoardUI.ButtonJoinWhite), ChessBoard.InputRejected,
            "own game: Join White refused");
        CheckInt(board.ResignAs(ChessRules.White, localName), ChessBoard.InputAccepted, "own game: resigned");
        CheckButtons("Replay Reset", "own game ended");
        ui.PressReset();
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, "Reset: the board is reset");
        CheckButtons("JoinWhite JoinBlack", "after Reset");

        // Someone else's lobby and game: only Reset, for an organizer or once the board is idle.
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "others: Ana joined White");
        CheckButtons("JoinBlack Reset?", "others' lobby, Black open");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "others: Bob joined Black");
        CheckButtons("Reset?", "others' lobby");
        CheckInt(ui.PressButton(ChessBoardUI.ButtonMode), ChessBoard.InputRejected, "others' lobby: Mode refused");
        CheckInt(ui.PressButton(ChessBoardUI.ButtonClock), ChessBoard.InputRejected,
            "others' lobby: Clock refused");
        CheckInt(ui.PressButton(ChessBoardUI.ButtonStart), ChessBoard.InputRejected,
            "others' lobby: Start refused");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, "others: Ana started");
        CheckButtons("Replay? Reset?", "others' game");

        // A loaded game: both seats joined, the clock chosen, continued from ply 3.
        board.ClearState();
        Check(Utilities.IsValid(importer) && chess960Pgn != null, "buttons: importer and PGN assigned",
            "assign ViewTest's Importer and PGNs in the Inspector");
        if (Utilities.IsValid(importer) && chess960Pgn != null) {
            importer.StartImport(chess960Pgn.text, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
            CheckInt(importer.ImportFor(float.MaxValue), ChessPgn.ImportDone, "loaded game: imported");
            CheckInt(board.LoadImport(importer), ChessBoard.InputAccepted, "loaded game: loaded");
            CheckButtons("JoinWhite JoinBlack First Back Reset?", "loaded game");
            ui.PressJoinWhite();
            CheckButtons("JoinBlack Clock First Back Reset", "loaded game, White joined");
            CheckInt(ui.PressButton(ChessBoardUI.ButtonContinue), ChessBoard.InputRejected,
                "loaded game, White joined: Continue refused");
            ui.PressJoinBlack();
            CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "loaded game, both seats: still loaded");
            CheckButtons("Clock Continue First Back Reset", "loaded game, both seats");
            CheckInt(ui.PressButton(ChessBoardUI.ButtonMode), ChessBoard.InputRejected,
                "loaded game: Mode refused");
            if (!board.IsClockForced()) {
                CheckInt(ui.PressButton(ChessBoardUI.ButtonClock), ChessBoard.InputAccepted,
                    "loaded game: Clock pressed");
                CheckLabel(ChessBoardUI.ButtonClock, "Clock: " + ChessBoardUI.ClockText(board.baseTime,
                    board.increment), "loaded game: Clock pressed");
            }
            CheckInt(board.StepToAs(localName, true, 3), ChessBoard.InputAccepted, "loaded game: stepped to ply 3");
            CheckButtons("Clock Continue First Back Forward Last Reset", "loaded game at ply 3");
            ui.PressContinue();
            CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "Continue: in game");
            CheckInt(board.moveCount, 3, "Continue: from ply 3");
            CheckButtons(OwnGame, "continued game");
        }

        board.ClearState();
        CheckButtons("JoinWhite JoinBlack", "cleared after the button checks");
        CheckView("cleared after the button checks");
        SendCustomEventDelayedFrames(nameof(RunGameButtonChecks), 1);
    }

    // ---- 9.4b: game and replay buttons ----

    // The buttons a seated player in both seats sees during their game, outside replay with nothing pending.
    const string OwnGame = "ResignWhite ResignBlack OfferDrawWhite? OfferDrawBlack? Replay Reset";
    // The same in replay, before the step buttons and takeback requests that apply.
    const string OwnReplay = "ResignWhite ResignBlack OfferDrawWhite? OfferDrawBlack? Leave Reset";

    // The ninth frame: on games of the local player against themselves, with takebacks, draws by agreement and
    // threefold claims on in the snapshot (SetRequestRules), resigning, draw offers (offered, declined, accepted), a
    // threefold claim, replay with each step button, and takeback requests (requested, declined, accepted), each button
    // shown exactly when it applies and pressing it doing what the board's method does, a hidden one refused; then the
    // same offers, requests and claim with those rules off, hidden and refused; then someone else's game.
    public void RunGameButtonChecks() {
        // Resigning.
        board.ClearState();
        StartGame(ChessGame.ModeStandard, "resign button");
        SetRequestRules(true);
        CheckButtons(OwnGame, "resign button: own game");
        CheckRefused(ChessBoardUI.ButtonAcceptDrawBlack, "no offer");
        CheckRefused(ChessBoardUI.ButtonDeclineDrawWhite, "no offer");
        CheckRefused(ChessBoardUI.ButtonClaimDraw, "nothing to claim");
        CheckRefused(ChessBoardUI.ButtonLeaveReplay, "not in replay");
        CheckRefused(ChessBoardUI.ButtonStepBack, "not in replay");
        CheckRefused(ChessBoardUI.ButtonRequestTakebackWhite, "not in replay");
        CheckRefused(ChessBoardUI.ButtonAcceptTakebackBlack, "no request");
        ui.PressResignBlack();
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, "Resign (Black): the game ended");
        CheckInt(board.ending, ChessGame.EndingResignation, "Resign (Black): by resignation");
        CheckInt(board.winner, ChessRules.White, "Resign (Black): White won");
        CheckButtons("Replay Reset", "resigned");
        CheckRefused(ChessBoardUI.ButtonResignWhite, "resigned");
        CheckRefused(ChessBoardUI.ButtonOfferDrawWhite, "resigned");

        // Draw offers.
        board.ClearState();
        StartGame(ChessGame.ModeStandard, "draw buttons");
        SetRequestRules(true);
        ui.PressOfferDrawWhite();
        CheckInt(board.drawOfferColor, ChessRules.White, "Offer draw (White): offered");
        CheckButtons("ResignWhite ResignBlack AcceptDrawBlack DeclineDrawBlack Replay Reset", "White's offer");
        CheckRefused(ChessBoardUI.ButtonOfferDrawBlack, "White's offer");
        CheckRefused(ChessBoardUI.ButtonAcceptDrawWhite, "White's offer");
        ui.PressDeclineDrawBlack();
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "Decline draw (Black): no offer");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "Decline draw (Black): still in game");
        CheckButtons(OwnGame, "offer declined");
        ui.PressOfferDrawBlack();
        CheckButtons("ResignWhite ResignBlack AcceptDrawWhite DeclineDrawWhite Replay Reset", "Black's offer");
        CheckLabel(ChessBoardUI.ButtonAcceptDrawWhite, "Accept draw", "Black's offer");
        ui.PressAcceptDrawWhite();
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, "Accept draw (White): the game ended");
        CheckInt(board.ending, ChessGame.EndingDrawAgreement, "Accept draw (White): drawn by agreement");
        CheckButtons("Replay Reset", "draw accepted");

        // Replay and its steps, on 1. e4 e5 2. Nf3 Nc6.
        board.ClearState();
        StartGame(ChessGame.ModeStandard, "replay buttons");
        SetRequestRules(true);
        PlayMoves("e2e4 e7e5 g1f3 b8c6", "replay buttons");
        ui.PressEnterReplay();
        Check(board.replayMode, "Replay: in replay", "not in replay");
        CheckInt(board.replayIndex, 4, "Replay: at the current position");
        CheckButtons(OwnReplay + " First Back", "replay at ply 4");
        CheckRefused(ChessBoardUI.ButtonStepForward, "replay at ply 4");
        CheckRefused(ChessBoardUI.ButtonStepLast, "replay at ply 4");
        CheckRefused(ChessBoardUI.ButtonEnterReplay, "in replay");
        ui.PressStepBack();
        CheckInt(board.replayIndex, 3, "< : ply 3");
        Redraw();
        CheckView("< : ply 3");
        CheckButtons(OwnReplay + " First Back Forward Last TakebackWhite TakebackBlack", "replay at ply 3");
        ui.PressStepFirst();
        CheckInt(board.replayIndex, 0, "<< : ply 0");
        Redraw();
        CheckView("<< : ply 0");
        CheckButtons(OwnReplay + " Forward Last TakebackWhite TakebackBlack", "replay at ply 0");
        CheckRefused(ChessBoardUI.ButtonStepBack, "replay at ply 0");
        ui.PressStepForward();
        CheckInt(board.replayIndex, 1, "> : ply 1");
        ui.PressStepLast();
        CheckInt(board.replayIndex, 4, ">> : ply 4");
        Redraw();
        CheckView(">> : ply 4");
        ui.PressStepBack();
        ui.PressStepBack();
        CheckInt(board.replayIndex, 2, "< twice: ply 2");

        // Takeback requests, from ply 2.
        CheckLabel(ChessBoardUI.ButtonRequestTakebackWhite, "Take back to here", "replay at ply 2");
        ui.PressRequestTakebackWhite();
        CheckInt(board.takebackColor, ChessRules.White, "Take back (White): requested");
        CheckInt(board.takebackPly, 2, "Take back (White): for ply 2");
        CheckButtons(OwnReplay + " First Back Forward Last AcceptTakebackBlack DeclineTakebackBlack",
            "White's request");
        CheckRefused(ChessBoardUI.ButtonRequestTakebackBlack, "White's request");
        CheckRefused(ChessBoardUI.ButtonAcceptTakebackWhite, "White's request");
        ui.PressDeclineTakebackBlack();
        CheckInt(board.takebackColor, ChessRules.NoColor, "Decline takeback (Black): no request");
        CheckInt(board.moveCount, 4, "Decline takeback (Black): the game unchanged");
        CheckButtons(OwnReplay + " First Back Forward Last TakebackWhite TakebackBlack", "request declined");
        ui.PressRequestTakebackBlack();
        CheckButtons(OwnReplay + " First Back Forward Last AcceptTakebackWhite DeclineTakebackWhite",
            "Black's request");
        ui.PressAcceptTakebackWhite();
        CheckInt(board.moveCount, 2, "Accept takeback (White): back to ply 2");
        Check(!board.replayMode, "Accept takeback (White): out of replay", "in replay");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "Accept takeback (White): in game");
        Redraw();
        CheckView("takeback accepted");
        CheckButtons(OwnGame, "takeback accepted");
        ui.PressEnterReplay();
        ui.PressStepFirst();
        ui.PressLeaveReplay();
        Check(!board.replayMode, "Back to game: out of replay", "in replay");
        CheckInt(board.moveCount, 2, "Back to game: the game unchanged");
        Redraw();
        CheckView("back to game");
        CheckButtons(OwnGame, "back to game");

        // A threefold repetition claim: the knights out and back twice.
        board.ClearState();
        StartGame(ChessGame.ModeStandard, "claim button");
        SetRequestRules(true);
        PlayMoves("g1f3 g8f6 f3g1 f6g8 g1f3 g8f6 f3g1", "claim button");
        CheckButtons(OwnGame, "claim button: twice repeated");
        PlayMoves("f6g8", "claim button");
        CheckButtons("ResignWhite ResignBlack OfferDrawWhite OfferDrawBlack ClaimDraw Replay Reset",
            "claim button: three times");
        CheckLabel(ChessBoardUI.ButtonClaimDraw, "Claim draw (threefold)", "claim button: three times");
        ui.PressClaimDraw();
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, "Claim draw: the game ended");
        CheckInt(board.ending, ChessGame.EndingThreefoldRepetition, "Claim draw: by threefold repetition");
        CheckButtons("Replay Reset", "draw claimed");

        // The same with takebacks, draws by agreement and threefold claims off.
        board.ClearState();
        StartGame(ChessGame.ModeStandard, "rules off");
        SetRequestRules(false);
        CheckButtons("ResignWhite ResignBlack Replay Reset", "rules off: own game");
        CheckRefused(ChessBoardUI.ButtonOfferDrawWhite, "rules off");
        PlayMoves("g1f3 g8f6 f3g1 f6g8 g1f3 g8f6 f3g1 f6g8", "rules off");
        CheckButtons("ResignWhite ResignBlack Replay Reset", "rules off: three times");
        CheckRefused(ChessBoardUI.ButtonClaimDraw, "rules off");
        ui.PressEnterReplay();
        ui.PressStepBack();
        CheckButtons("ResignWhite ResignBlack Leave First Back Forward Last Reset", "rules off: replay at ply 7");
        CheckRefused(ChessBoardUI.ButtonRequestTakebackWhite, "rules off");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "rules off: still in game");

        // Someone else's game: none of the players' buttons.
        board.ClearState();
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "others: Ana joined White");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "others: Bob joined Black");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, "others: Ana started");
        SetRequestRules(true);
        CheckButtons("Replay? Reset?", "others' game");
        CheckRefused(ChessBoardUI.ButtonResignWhite, "others' game");
        CheckRefused(ChessBoardUI.ButtonOfferDrawBlack, "others' game");
        CheckInt(board.OfferDrawAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "others: Ana offered");
        CheckButtons("Replay? Reset?", "others' game, Ana's offer");
        CheckRefused(ChessBoardUI.ButtonAcceptDrawBlack, "others' game, Ana's offer");

        board.ClearState();
        CheckButtons("JoinWhite JoinBlack", "cleared after the game button checks");
        CheckView("cleared after the game button checks");
        SendCustomEventDelayedFrames(nameof(RunHighlightChecks), 1);
    }

    // ---- 9.5a: the promotion picker and highlights ----

    // A black pawn about to promote on e1, and a white one on a7 (the picker can't stand over the a file).
    const string BlackPromotionFen = "k7/8/8/8/8/8/4p3/K7 b - - 0 1";
    const string APawnPromotionFen = "4k3/P7/8/8/8/8/8/4K3 w - - 0 1";
    // The king already on g1 with its rook on h1: castling moves only the rook.
    const string KingStaysFen = "6k1/8/8/8/8/8/8/6KR w K - 0 1";
    // A knight on e4 in front of its rook: Nd6 gives a double check (the knight, and the rook it uncovers).
    const string DoubleCheckFen = "4k3/8/8/8/4N3/8/8/4RK2 w - - 0 1";
    // A pawn on e5 that can take a knight on f6 or the pawn on d5 en passant, or move to e6.
    const string CaptureMarksFen = "4k3/8/5n2/3pP3/8/8/8/4K3 w - d6 0 1";
    // The king on f1 with its rook on g1, the king's destination: castling swaps them.
    const string RookOnDestinationFen = "k7/8/8/8/8/8/8/5KR1 w K - 0 1";

    // The tenth frame: the picker, highlights and marks wired; the last-move line and mark after moves (castling on
    // the rook showing the king's path, or the rook's when the king stays), in replay and on a cleared board; the
    // check lines (step 12.7) of a check, a double check and a checkmate, in replay and a custom position, and gone
    // once answered; the legal-target marks of a held piece (pickups) and of a selected one (clicks), each of its kind
    // (a capture, a castle's rook square, an empty square), none in replay, after the drop or once the game ends; the
    // picker shown over the promotion square for a drop and for a placing click, facing the promoting side, each of
    // its buttons playing its piece or putting the pawn back, and hidden when a piece is picked up or the board
    // changes. The input style is put back afterwards.
    public void RunHighlightChecks() {
        int savedStyle = view.inputStyle;
        Check(Utilities.IsValid(view.promotionPicker) && !view.promotionPicker.activeSelf,
            "highlights: the promotion picker assigned, hidden", "missing or shown");
        Check(Utilities.IsValid(view.lastMoveLine) && Utilities.IsValid(view.lastMoveMark),
            "highlights: the last-move line and mark assigned", "missing");
        Material lastMoveMaterial = MaterialOf(view.lastMoveLine);
        Check(Utilities.IsValid(lastMoveMaterial) && lastMoveMaterial == MaterialOf(view.lastMoveMark)
            && lastMoveMaterial != view.targetMaterial && lastMoveMaterial != view.captureMaterial,
            "highlights: the last-move line and mark share a material, not the target marks'", "they don't");
        int lineCount = 0;
        for (int i = 0; i < view.checkLines.Length; i++) {
            if (Utilities.IsValid(view.checkLines[i])) lineCount++;
        }
        CheckInt(lineCount, ChessBoardView.CheckLineCount, "highlights: the check lines assigned");
        Material checkMaterial = view.checkLines.Length > 0 ? MaterialOf(view.checkLines[0]) : null;
        Check(Utilities.IsValid(checkMaterial) && checkMaterial != lastMoveMaterial
            && checkMaterial != view.targetMaterial && checkMaterial != view.captureMaterial,
            "highlights: the check lines' material, not the last move's or the target marks'", "missing or shared");
        int marks = 0;
        for (int square = 0; square < 64 && square < view.targetHighlights.Length; square++) {
            GameObject mark = view.targetHighlights[square];
            if (Utilities.IsValid(mark) && view.DropSquare(mark.transform.position) == square) marks++;
        }
        CheckInt(marks, 64, "highlights: every square has its target mark, over that square");
        Check(Utilities.IsValid(view.targetMaterial) && Utilities.IsValid(view.captureMaterial)
            && view.targetMaterial != view.captureMaterial,
            "highlights: the target and capture materials assigned, different", "missing or the same");

        // The last move.
        string g = "last move";
        board.ClearState();
        Redraw();
        CheckLastMove("", "", g + ": a cleared board");
        StartGame(ChessGame.ModeStandard, g);
        CheckLastMove("", "", g + ": started");
        PlayMoves("e2e4", g);
        CheckLastMove("e2", "e4", g + ": e4");
        PlayMoves("e7e5 g1f3 b8c6 f1c4 g8f6", g);
        CheckLastMove("g8", "f6", g + ": Nf6");
        PlayMoves("e1h1", g);
        CheckLastMove("e1", "g1", g + ": O-O on the rook");
        CheckInt(board.EnterReplayAs(localName, false), ChessBoard.InputAccepted, g + ": replay entered");
        Redraw();
        CheckLastMove("e1", "g1", g + ": replay entered");
        CheckInt(board.StepToAs(localName, false, 1), ChessBoard.InputAccepted, g + ": stepped to ply 1");
        Redraw();
        CheckLastMove("e2", "e4", g + ": replay at ply 1");
        CheckInt(board.StepToAs(localName, false, 0), ChessBoard.InputAccepted, g + ": stepped to the start");
        Redraw();
        CheckLastMove("", "", g + ": replay at the start");

        // No target marks in replay, where every square is a target.
        view.SetInputStyle(ChessBoardView.InputPickups);
        int replayPawn = view.squareObject[Sq("e2")];
        view.OnPiecePickedUp(replayPawn);
        CheckInt(view.heldObject, replayPawn, g + ": e2 picked up in replay");
        CheckTargets("", g + ": e2 held in replay");
        view.OnPieceDropped(replayPawn);
        view.SetInputStyle(ChessBoardView.InputClicks);
        Redraw();
        ClickPiece("e2");
        CheckInt(view.selectedObject, replayPawn, g + ": e2 selected in replay");
        CheckTargets("", g + ": e2 selected in replay");
        ClickPiece("e2");

        CastleGame(KingStaysFen, g + " (king stays)");
        CheckInt(board.PieceDroppedAs(localName, Sq("g1"), Sq("h1")), ChessBoard.InputAccepted,
            g + " (king stays): O-O on the rook");
        Redraw();
        CheckLastMove("h1", "f1", g + " (king stays): O-O, the rook's path");

        // Check lines: one from each piece giving check, in the game and in replay, and none once it's answered.
        string k = "check lines";
        board.ClearState();
        Redraw();
        CheckCheckLines("", k + ": a cleared board");
        StartGame(ChessGame.ModeStandard, k);
        PlayMoves("e2e4 d7d5", k);
        CheckCheckLines("", k + ": no check");
        PlayMoves("f1b5", k);
        CheckCheckLines("b5e8", k + ": Bb5+");
        PlayMoves("c7c6", k);
        CheckCheckLines("", k + ": c6, answered");
        CheckInt(board.EnterReplayAs(localName, false), ChessBoard.InputAccepted, k + ": replay entered");
        CheckInt(board.StepToAs(localName, false, 3), ChessBoard.InputAccepted, k + ": stepped to ply 3");
        Redraw();
        CheckCheckLines("b5e8", k + ": replay at Bb5+");
        CheckInt(board.StepToAs(localName, false, 2), ChessBoard.InputAccepted, k + ": stepped to ply 2");
        Redraw();
        CheckCheckLines("", k + ": replay before Bb5+");
        CheckInt(board.PieceDroppedAs(localName, Sq("d8"), Sq("d2")), ChessBoard.InputAccepted,
            k + ": a free move, the queen to d2");
        Redraw();
        CheckCheckLines("d2e1", k + ": replay, custom position: White in check");
        CheckInt(board.PieceDroppedAs(localName, Sq("f1"), Sq("b5")), ChessBoard.InputAccepted,
            k + ": a free move, the bishop to b5");
        Redraw();
        CheckCheckLines("d2e1 b5e8", k + ": replay, custom position: both kings in check");

        CastleGame(DoubleCheckFen, k + " (double check)");
        CheckCheckLines("", k + " (double check): no check");
        PlayMoves("e4d6", k + " (double check)");
        CheckCheckLines("d6e8 e1e8", k + " (double check): Nd6, a double check");
        PlayMoves("e8d8", k + " (double check)");
        CheckCheckLines("", k + " (double check): Kd8, answered");

        board.ClearState();
        StartGame(ChessGame.ModeStandard, k + " (mate)");
        PlayMoves("e2e4 e7e5 d1h5 b8c6 f1c4 g8f6 h5f7", k + " (mate)");
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, k + " (mate): Qxf7# ends the game");
        CheckCheckLines("f7e8", k + " (mate): the checkmate's line stays");

        // Target marks of a held piece.
        string t = "held targets";
        view.SetInputStyle(ChessBoardView.InputPickups);
        board.ClearState();
        StartGame(ChessGame.ModeStandard, t);
        CheckTargets("", t + ": nothing held");
        int pawn = view.squareObject[Sq("e2")];
        view.OnPiecePickedUp(pawn);
        CheckTargets("e3 e4", t + ": e2 held");
        Redraw();
        CheckTargets("e3 e4", t + ": e2 held, refreshed");
        view.pieces[pawn].localPosition = Over("e4");
        view.OnPieceDropped(pawn);
        CheckTargets("", t + ": e2 dropped over e4");
        Redraw();
        CheckLastMove("e2", "e4", t + ": e4 by a drop");
        int knight = view.squareObject[Sq("g8")];
        view.OnPiecePickedUp(knight);
        CheckTargets("f6 h6", t + ": g8 held");
        view.OnPiecePickedUp(view.squareObject[Sq("d2")]);
        CheckTargets("", t + ": d2 picked up out of turn instead");
        Redraw();
        view.OnPiecePickedUp(knight);
        CheckTargets("f6 h6", t + ": g8 held again");
        CheckInt(board.ResignAs(ChessRules.Black, localName), ChessBoard.InputAccepted, t + ": Black resigned");
        Redraw();
        CheckTargets("", t + ": the game ended while g8 was held");
        if (view.heldObject == knight) view.OnPieceDropped(knight);

        CastleGame(StandardCastlingFen, t + " (castling)");
        int king = view.squareObject[Sq("e1")];
        view.OnPiecePickedUp(king);
        CheckTargets("d1 f1 d2 e2 f2 oa1 c1 g1 oh1", t + " (castling): e1 held");
        view.pieces[king].localPosition = Over("h1");
        view.OnPieceDropped(king);
        CheckTargets("", t + " (castling): dropped over h1");
        Redraw();
        CheckLastMove("e1", "g1", t + " (castling): O-O by a drop");

        // Captures (en passant marking the square the pawn moves to), and a castle whose rook stands on the king's
        // destination: one full-size yellow mark there.
        CastleGame(CaptureMarksFen, t + " (captures)");
        int capturing = view.squareObject[Sq("e5")];
        view.OnPiecePickedUp(capturing);
        CheckTargets("e6 xd6 xf6", t + " (captures): e5 held");
        view.OnPieceDropped(capturing);
        Redraw();
        CastleGame(RookOnDestinationFen, t + " (rook on g1)");
        int castlingKing = view.squareObject[Sq("f1")];
        view.OnPiecePickedUp(castlingKing);
        CheckTargets("e1 e2 f2 g2 og1", t + " (rook on g1): f1 held");
        view.OnPieceDropped(castlingKing);
        Redraw();

        // Target marks of a selected piece.
        string s = "selected targets";
        view.SetInputStyle(ChessBoardView.InputClicks);
        board.ClearState();
        StartGame(ChessGame.ModeStandard, s);
        ClickPiece("g1");
        CheckTargets("f3 h3", s + ": g1 selected");
        ClickPiece("e2");
        CheckTargets("e3 e4", s + ": e2 selected instead");
        ClickPiece("e2");
        CheckTargets("", s + ": e2 put back");
        ClickPiece("b1");
        ClickSquare("c3");
        CheckTargets("", s + ": Nc3 placed");
        Redraw();
        CheckLastMove("b1", "c3", s + ": Nc3 by clicks");
        CastleGame(CaptureMarksFen, s + " (captures)");
        ClickPiece("e5");
        CheckTargets("e6 xd6 xf6", s + " (captures): e5 selected");
        ClickPiece("e5");
        CheckTargets("", s + " (captures): e5 put back");

        // The promotion picker, for a drop.
        string p = "picker";
        view.SetInputStyle(ChessBoardView.InputPickups);
        CastleGame(PromotionFen, p);
        CheckPicker(false, "", p + ": set up");
        int promoting = view.squareObject[Sq("e7")];
        CheckInt(ViewDrop("e7", Over("e8")), ChessBoard.InputAccepted, p + ": e7 picked up");
        CheckPicker(true, "e8", p + ": e7 dropped over e8");
        CheckPickupable(0, p + ": e7 dropped over e8, no pickups");
        Redraw();
        CheckPicker(true, "e8", p + ": e7 dropped over e8, refreshed");
        CheckPickupable(0, p + ": the picker shown");
        view.PressPromoteQueen();
        CheckPicker(false, "", p + ": Queen pressed");
        CheckPickupable(3, p + ": the picker gone");
        CheckDropped(1, ChessBoard.ChangeMove, p + ": e8=Q");
        CheckInt(view.objectType[promoting], ChessRules.Queen, p + ": e8=Q: the pawn's object is a queen");
        CheckLastMove("e7", "e8", p + ": e8=Q");

        CastleGame(PromotionFen, p + " (cancel)");
        ViewDrop("e7", Over("e8"));
        CheckPicker(true, "e8", p + " (cancel): e7 dropped over e8");
        view.PressPromoteCancel();
        CheckPicker(false, "", p + " (cancel): Cancel pressed");
        CheckDropped(0, ChessBoard.ChangeRejected, p + " (cancel): the pawn back on e7");
        CheckPickupable(3, p + " (cancel): the picker gone");

        ViewDrop("e7", Over("e8"));
        CheckPicker(true, "e8", p + " (pickup): e7 dropped over e8");
        int whiteKing = view.squareObject[Sq("a1")];
        view.OnPiecePickedUp(whiteKing);
        CheckPicker(false, "", p + " (pickup): the king picked up meanwhile");
        CheckInt(view.promotionObject, ChessBoardView.NoObject, p + " (pickup): nothing waits");
        view.OnPieceDropped(whiteKing);
        CheckDropped(0, ChessBoard.ChangeRejected, p + " (pickup): the king dropped back, the pawn back on e7");

        ViewDrop("e7", Over("e8"));
        CheckPicker(true, "e8", p + " (change): e7 dropped over e8");
        CheckInt(board.ResignAs(ChessRules.Black, localName), ChessBoard.InputAccepted, p + " (change): Black resigned");
        CheckPicker(false, "", p + " (change): the game ended meanwhile");
        CheckInt(view.ChoosePromotion(ChessRules.Queen), ChessBoard.InputRejected,
            p + " (change): a choice afterwards refused");

        CastleGame(BlackPromotionFen, p + " (Black)");
        int blackPawn = view.squareObject[Sq("e2")];
        ViewDrop("e2", Over("e1"));
        CheckPicker(true, "e1", p + " (Black): e2 dropped over e1");
        view.PressPromoteKnight();
        CheckDropped(1, ChessBoard.ChangeMove, p + " (Black): e1=N");
        CheckInt(view.objectType[blackPawn], ChessRules.Knight, p + " (Black): e1=N: the pawn's object is a knight");

        // The promotion picker, for a placing click.
        view.SetInputStyle(ChessBoardView.InputClicks);
        CastleGame(PromotionFen, p + " (clicks)");
        int clickPawn = view.squareObject[Sq("e7")];
        ClickPiece("e7");
        ClickSquare("e8");
        CheckPicker(true, "e8", p + " (clicks): e8 clicked");
        CheckClickables("", "", p + " (clicks): the picker shown");
        view.PressPromoteRook();
        CheckDropped(1, ChessBoard.ChangeMove, p + " (clicks): e8=R");
        CheckClickables("a8", "", p + " (clicks): e8=R, the picker gone");
        CheckInt(view.objectType[clickPawn], ChessRules.Rook, p + " (clicks): e8=R: the pawn's object is a rook");

        CastleGame(APawnPromotionFen, p + " (a file)");
        int aPawn = view.squareObject[Sq("a7")];
        ClickPiece("a7");
        ClickSquare("a8");
        CheckPicker(true, "a8", p + " (a file): a8 clicked");
        view.PressPromoteBishop();
        CheckDropped(1, ChessBoard.ChangeMove, p + " (a file): a8=B");
        CheckInt(view.objectType[aPawn], ChessRules.Bishop, p + " (a file): a8=B: the pawn's object is a bishop");

        board.ClearState();
        view.SetInputStyle(savedStyle);
        Redraw();
        CheckLastMove("", "", "cleared after the highlight checks");
        CheckTargets("", "cleared after the highlight checks");
        CheckPicker(false, "", "cleared after the highlight checks");
        CheckView("cleared after the highlight checks");
        RunBlockerChecks();
        SendCustomEventDelayedFrames(nameof(RunAnimationChecks), 1);
    }

    // ---- 9.5a2: walking through the board ----

    // Run straight after the highlight checks: nothing a player walks into is solid but the blocker (the click
    // targets and the UI canvases are triggers), which is enabled exactly while the local player has no seat.
    void RunBlockerChecks() {
        Collider blocker = view.blocker;
        Check(Utilities.IsValid(blocker) && !blocker.isTrigger, "the blocker assigned and solid",
            Utilities.IsValid(blocker) ? "it's a trigger" : "none");
        int solid = 0;
        for (int square = 0; square < 64; square++) {
            ChessSquare target = view.squareTargets[square];
            if (!Utilities.IsValid(target) || !IsTrigger(target.transform)) solid++;
        }
        CheckInt(solid, 0, "the square targets are triggers: solid ones");
        solid = 0;
        for (int i = 0; i < ui.buttons.Length; i++) {
            if (Utilities.IsValid(ui.buttons[i]) && !IsTrigger(ui.buttons[i].transform.parent)) solid++;
        }
        if (!Utilities.IsValid(view.promotionPicker) || !IsTrigger(view.promotionPicker.transform)) solid++;
        CheckInt(solid, 0, "the button panels and the picker have trigger colliders: solid or missing");
        if (!Utilities.IsValid(blocker)) return;

        board.ClearState();
        Redraw();
        Check(blocker.enabled, "blocker: enabled on a reset board", "disabled");
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "blocker: Ana joined White");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "blocker: Bob joined Black");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, "blocker: Ana and Bob started");
        Redraw();
        Check(blocker.enabled, "blocker: enabled in others' game", "disabled");

        board.ClearState();
        CheckInt(board.JoinAs(ChessRules.Black, localName), ChessBoard.InputAccepted, "blocker: joined Black");
        Redraw();
        Check(!blocker.enabled, "blocker: disabled once seated (Black, lobby)", "enabled");
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "blocker: Ana joined White");
        CheckInt(board.StartGameAs(localName), ChessBoard.InputAccepted, "blocker: started");
        Redraw();
        Check(!blocker.enabled, "blocker: disabled in own game", "enabled");
        CheckInt(board.ResignAs(ChessRules.Black, localName), ChessBoard.InputAccepted, "blocker: resigned");
        Redraw();
        Check(!blocker.enabled, "blocker: disabled in own ended game", "enabled");

        board.ClearState();
        Redraw();
        Check(blocker.enabled, "blocker: enabled again once cleared", "disabled");
        CheckView("cleared after the blocker checks");
    }

    // ---- 9.5b: move animations ----

    // The eleventh frame, with animations on and Pickups. No time passes within a frame, so a travelling object
    // still stands where it started; each check starts with everything in place (Settle). A move made on the board
    // (as one arriving from another client is) sets its piece travelling, and a Refresh meanwhile doesn't cut it
    // short; a captured piece (en passant too) travels off the board once the mover has landed; castling moves the
    // king and the rook together; a piece dropped through the view is put in place at once, and the piece it
    // captures leaves without waiting; a rejected drop goes back at once; stepping to the start of a longer game in
    // replay puts everything in place at once, one ply on travels; a reset and a load put everything in place at
    // once, as does any move with animations off. Then a last move, checked once it has had time to land.
    public void RunAnimationChecks() {
        view.animateMoves = true;
        savedAnimationStyle = view.inputStyle;
        view.SetInputStyle(ChessBoardView.InputPickups);
        string a = "animation";
        board.ClearState();
        Redraw();
        StartGame(ChessGame.ModeStandard, a + " game");
        CheckAtRest(a + " game started");

        int mover = BeforeMove("e2");
        PlayOnBoard("e2", "e4", a);
        CheckTravel(mover, "e4", false, a + ": e4");
        CheckInt(TravellingFromBoard(), 1, a + ": e4: one object travelling from the board");
        Redraw();
        CheckTravel(mover, "e4", false, a + ": e4, after another Refresh");
        Settle();
        PlayOnBoard("d7", "d5", a);
        Settle();

        mover = BeforeMove("e4");
        int captured = view.squareObject[Sq("d5")];
        PlayOnBoard("e4", "d5", a);
        CheckTravel(mover, "d5", false, a + ": exd5");
        CheckTravelOff(captured, true, a + ": exd5: the d5 pawn");
        CheckInt(TravellingFromBoard(), 2, a + ": exd5: two objects travelling from the board");
        Settle();
        PlayOnBoard("c7", "c5", a);
        Settle();

        mover = BeforeMove("d5");
        captured = view.squareObject[Sq("c5")];
        PlayOnBoard("d5", "c6", a);
        CheckTravel(mover, "c6", false, a + ": dxc6 e.p.");
        CheckTravelOff(captured, true, a + ": dxc6 e.p.: the c5 pawn");
        CheckInt(TravellingFromBoard(), 2, a + ": dxc6 e.p.: two objects travelling from the board");
        Settle();
        PlayOnBoard("b7", "c6", a);
        PlayOnBoard("g1", "f3", a);
        PlayOnBoard("g8", "f6", a);
        PlayOnBoard("f1", "c4", a);
        PlayOnBoard("e7", "e6", a);
        Settle();

        mover = BeforeMove("e1");
        int rook = view.squareObject[Sq("h1")];
        PlayOnBoard("e1", "g1", a);
        CheckTravel(mover, "g1", false, a + ": O-O: the king");
        CheckTravel(rook, "f1", false, a + ": O-O: the rook");
        CheckInt(TravellingFromBoard(), 2, a + ": O-O: two objects travelling from the board");
        Settle();

        // Dropped through the view: the queen lands where it's let go, then stands on d2 at once.
        RecordPositions();
        int queen = view.squareObject[Sq("d8")];
        captured = view.squareObject[Sq("d2")];
        CheckInt(ViewDrop("d8", Over("d2")), ChessBoard.InputAccepted, a + ": d8 picked up");
        CheckInt(board.lastChange, ChessBoard.ChangeMove, a + ": Qxd2 dropped: played");
        Redraw();
        Check(!view.animating[queen] && (view.pieces[queen].localPosition - view.SquarePosition(Sq("d2"))).sqrMagnitude
            < 1e-10f, a + ": Qxd2 dropped: the queen in place at once",
            "at " + view.pieces[queen].localPosition.ToString("F3"));
        CheckTravelOff(captured, false, a + ": Qxd2 dropped: the d2 pawn");
        CheckInt(TravellingFromBoard(), 1, a + ": Qxd2 dropped: one object travelling from the board");
        Settle();

        CheckInt(ViewDrop("b1", Over("b5")), ChessBoard.InputAccepted, a + ": b1 picked up");
        CheckInt(board.lastChange, ChessBoard.ChangeRejected, a + ": b1 dropped over b5: rejected");
        Redraw();
        CheckAtRest(a + ": b1 dropped over b5, put back at once");

        // Replay: to the start of 12 plies at once, then one ply on travelling.
        CheckInt(board.EnterReplayAs(localName, true), ChessBoard.InputAccepted, a + ": entered replay");
        Redraw();
        CheckAtRest(a + ": entered replay");
        CheckInt(board.StepToAs(localName, true, 0), ChessBoard.InputAccepted, a + ": stepped to the start");
        Redraw();
        CheckAtRest(a + ": stepped to the start");
        mover = BeforeMove("e2");
        CheckInt(board.StepToAs(localName, true, 1), ChessBoard.InputAccepted, a + ": stepped to ply 1");
        Redraw();
        CheckTravel(mover, "e4", false, a + ": stepped to ply 1");
        CheckInt(TravellingFromBoard(), 1, a + ": stepped to ply 1: one object travelling from the board");
        Settle();

        // A reset from ply 1 in replay moves one piece back, but a reset never travels.
        CheckInt(board.ResetBoardAs(localName, true), ChessBoard.InputAccepted, a + ": reset");
        Redraw();
        CheckAtRest(a + ": reset");

        if (Utilities.IsValid(importer) && chess960Pgn != null) {
            board.ClearState();
            importer.StartImport(chess960Pgn.text, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
            CheckInt(importer.ImportFor(float.MaxValue), ChessPgn.ImportDone, a + ": chess960.pgn imported");
            CheckInt(board.LoadImport(importer), ChessBoard.InputAccepted, a + ": chess960.pgn loaded");
            Redraw();
            CheckAtRest(a + ": chess960.pgn loaded");
        }

        board.ClearState();
        StartGame(ChessGame.ModeStandard, a + " game 2");
        CheckAtRest(a + " game 2 started");
        view.animateMoves = false;
        PlayOnBoard("e2", "e4", a + " off");
        Redraw();
        CheckAtRest(a + " off: e4");
        view.animateMoves = true;

        mover = BeforeMove("d7");
        PlayOnBoard("d7", "d5", a);
        CheckTravel(mover, "d5", false, a + ": d5");
        SendCustomEventDelayedSeconds(nameof(RunAnimationEndChecks), view.moveSeconds + 0.5f);
    }

    // Once the last move has had time to land: everything in place, nothing travelling.
    public void RunAnimationEndChecks() {
        CheckAtRest("animation: d5 landed");
        board.ClearState();
        view.SetInputStyle(savedAnimationStyle);
        view.animateMoves = savedAnimate;
        Redraw();
        CheckView("cleared after the animation checks");
        SendCustomEventDelayedFrames(nameof(RunStatusChecks), 1);
    }

    // ---- 9.5c: the status display ----

    // Sam Loyd's ten-move stalemate (docs/testing.md, stalemate.pgn.txt) and the fool's mate.
    const string LoydStalemate = "e2e3 a7a5 d1h5 a8a6 h5a5 h7h5 h2h4 a6h6 a5c7 f7f6 c7d7 e8f7 d7b7 d8d3 b7b8 d3h7 "
        + "b8c8 f7g6 c8e6";
    const string FoolsMate = "f2f3 e7e5 g2g4 d8h4";

    // The twelfth frame: the status panels wired; ClockDisplay; the players' and status texts (and the panels showing
    // them) on a reset board, in a lobby, in a game of two others with a clock (at the turn start, a minute on, the
    // flag fallen), in check, with a draw offer, in replay with a takeback request and after a free move, resigned;
    // every ending's text; a stalemate and a checkmate played, the mate in replay; a loaded game at its last ply, its
    // start and with seats taken, and one starting with Black to move.
    public void RunStatusChecks() {
        string s = "status";
        int panels = ui.statusLabels.Length;
        Check(panels > 0 && ui.statusPlayerLabels.Length == panels, s + ": panels assigned",
            panels + " status texts, " + ui.statusPlayerLabels.Length + " players texts");
        int missing = 0;
        for (int i = 0; i < panels; i++) {
            if (!Utilities.IsValid(ui.statusLabels[i])) missing++;
            if (i < ui.statusPlayerLabels.Length && !Utilities.IsValid(ui.statusPlayerLabels[i])) missing++;
        }
        CheckInt(missing, 0, s + ": every panel's texts assigned: missing");
        CheckString(ChessBoardUI.ClockDisplay(300000), "5:00", s + ": clock 5:00");
        CheckString(ChessBoardUI.ClockDisplay(299999), "4:59", s + ": clock 4:59.999");
        CheckString(ChessBoardUI.ClockDisplay(61000), "1:01", s + ": clock 1:01");
        CheckString(ChessBoardUI.ClockDisplay(10000), "0:10", s + ": clock 0:10");
        CheckString(ChessBoardUI.ClockDisplay(9876), "0:09.8", s + ": clock 0:09.876");
        CheckString(ChessBoardUI.ClockDisplay(0), "0:00.0", s + ": clock 0");
        CheckString(ChessBoardUI.ClockDisplay(3600000), "1:00:00", s + ": clock an hour");
        CheckString(ChessBoardUI.ClockDisplay(3725000), "1:02:05", s + ": clock 1:02:05");

        // Two others' lobby and game, with a clock.
        board.ClearState();
        CheckStatus("White: (open)\nBlack: (open)", "Join White or Black to play", s + ": reset board");
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": Ana joined White");
        Unforce();
        CheckStatus("White: Ana\nBlack: (open)", "Lobby: Standard, No clock", s + ": lobby");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob joined Black");
        CheckInt(board.SetClockAs("Ana", 300, 3), ChessBoard.InputAccepted, s + ": 5+3 chosen");
        CheckInt(board.SetModeAs("Bob", ChessGame.ModeChess960), ChessBoard.InputAccepted, s + ": Chess960 chosen");
        CheckStatus("White: Ana\nBlack: Bob", "Lobby: Chess960, 5+3", s + ": lobby, both seats");
        CheckInt(board.SetModeAs("Bob", ChessGame.ModeStandard), ChessBoard.InputAccepted, s + ": Standard chosen");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, s + ": started");
        CheckStatus("White: Ana  5:00\nBlack: Bob  5:00", "White to move", s + ": game started");
        double start = board.turnStart;
        CheckString(ui.PlayersTextAt(start + 61.5), "White: Ana  3:58\nBlack: Bob  5:00", s + ": a minute on");
        CheckString(ui.StatusTextAt(start + 295.1), "White to move", s + ": 4.9 s left");
        CheckString(ui.PlayersTextAt(start + 295.1), "White: Ana  0:04.9\nBlack: Bob  5:00", s + ": 4.9 s left");
        CheckString(ui.StatusTextAt(start + 301), "White's time is up", s + ": flag fallen");
        CheckString(ui.PlayersTextAt(start + 301), "White: Ana  0:00.0\nBlack: Bob  5:00", s + ": flag fallen");
        CheckInt(board.PieceDroppedAs("Ana", Sq("e2"), Sq("e4")), ChessBoard.InputAccepted, s + ": e4");
        CheckInt(board.PieceDroppedAs("Bob", Sq("f7"), Sq("f6")), ChessBoard.InputAccepted, s + ": f6");
        CheckInt(board.PieceDroppedAs("Ana", Sq("d1"), Sq("h5")), ChessBoard.InputAccepted, s + ": Qh5+");
        CheckStatus(null, "Black to move, in check", s + ": check");
        SetRequestRules(true);
        CheckInt(board.OfferDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob offered a draw");
        CheckStatus(null, "Black to move, in check\nBlack offers a draw", s + ": draw offer");
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, s + ": replay");
        CheckStatus(null, "Black to move, in check\nBlack offers a draw\nReplay: 2. Qh5+ (3 of 3)", s + ": replay");
        CheckInt(board.StepToAs("Ana", false, 1), ChessBoard.InputAccepted, s + ": stepped to ply 1");
        CheckInt(board.RequestTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": Ana asked");
        CheckStatus(null, "Black to move, in check\nBlack offers a draw\nWhite asks to take back to here\n"
            + "Replay: 1. e4 (1 of 3)", s + ": takeback request");
        CheckInt(board.StepToAs("Ana", false, 0), ChessBoard.InputAccepted, s + ": stepped to the start");
        CheckStatus(null, "Black to move, in check\nBlack offers a draw\nReplay: start (0 of 3)",
            s + ": request cancelled by stepping");
        CheckInt(board.PieceDroppedAs("Ana", Sq("a2"), Sq("a4")), ChessBoard.InputAccepted, s + ": a free move");
        CheckStatus(null, "Black to move, in check\nBlack offers a draw\nReplay: custom position", s + ": free move");
        CheckInt(board.LeaveReplayAs("Ana", false), ChessBoard.InputAccepted, s + ": back to the game");
        CheckInt(board.ResignAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob resigned");
        CheckStatus(null, "White wins by resignation (1-0)", s + ": resigned");
        CheckString(ui.PlayerTextAt(ChessRules.Black, board.ServerTime() + 60),
            ui.PlayerTextAt(ChessRules.Black, board.ServerTime()), s + ": the clocks stopped");

        // Every ending's text, set on the ended game (Black was to move).
        CheckEnding(ChessGame.EndingCheckmate, ChessRules.White, "White wins by checkmate (1-0)");
        CheckEnding(ChessGame.EndingCheckmate, ChessRules.Black, "Black wins by checkmate (0-1)");
        CheckEnding(ChessGame.EndingResignation, ChessRules.Black, "Black wins by resignation (0-1)");
        CheckEnding(ChessGame.EndingTimeForfeit, ChessRules.White, "White wins on time (1-0)");
        CheckEnding(ChessGame.EndingTimeForfeit, ChessRules.NoColor,
            "Draw: Black ran out of time, White can't checkmate (1/2-1/2)");
        CheckEnding(ChessGame.EndingStalemate, ChessRules.NoColor, "Draw by stalemate (1/2-1/2)");
        CheckEnding(ChessGame.EndingDrawAgreement, ChessRules.NoColor, "Draw by agreement (1/2-1/2)");
        CheckEnding(ChessGame.EndingThreefoldRepetition, ChessRules.NoColor, "Draw by threefold repetition (1/2-1/2)");
        CheckEnding(ChessGame.EndingFivefoldRepetition, ChessRules.NoColor, "Draw by fivefold repetition (1/2-1/2)");
        CheckEnding(ChessGame.EndingFiftyMoveRule, ChessRules.NoColor, "Draw by the fifty-move rule (1/2-1/2)");
        CheckEnding(ChessGame.EndingSeventyFiveMoveRule, ChessRules.NoColor,
            "Draw by the seventy-five-move rule (1/2-1/2)");
        CheckEnding(ChessGame.EndingInsufficientMaterial, ChessRules.NoColor, "Draw: insufficient material (1/2-1/2)");
        CheckEnding(ChessGame.EndingKingCaptured, ChessRules.Black, "Black wins by capturing the king (0-1)");
        CheckEnding(ChessGame.EndingUnknown, ChessRules.White, "White wins (1-0)");
        CheckEnding(ChessGame.EndingUnknown, ChessRules.NoColor, "Draw (1/2-1/2)");
        CheckEnding(ChessGame.EndingNone, ChessRules.NoColor, "");

        // A stalemate and a checkmate played on the local player's games, with no clock.
        board.ClearState();
        StartUnforced(s + ": stalemate game");
        PlayMoves(LoydStalemate, s + ": stalemate game");
        CheckInt(board.ending, ChessGame.EndingStalemate, s + ": stalemated");
        CheckStatus("White: " + localName + "\nBlack: " + localName, "Draw by stalemate (1/2-1/2)",
            s + ": stalemate");
        board.ClearState();
        StartUnforced(s + ": checkmate game");
        PlayMoves(FoolsMate, s + ": checkmate game");
        CheckStatus("White: " + localName + "\nBlack: " + localName, "Black wins by checkmate (0-1)",
            s + ": checkmate");
        CheckInt(board.EnterReplayAs(localName, false), ChessBoard.InputAccepted, s + ": the mate in replay");
        CheckInt(board.StepToAs(localName, false, 2), ChessBoard.InputAccepted, s + ": stepped to ply 2");
        CheckStatus(null, "Black wins by checkmate (0-1)\nReplay: 1... e5 (2 of 4)", s + ": the mate in replay");

        // Loaded games.
        if (LoadPgn(chess960Pgn, s + ": chess960.pgn")) {
            string loaded = "Loaded game: White vs Black (Chess960)\nUnfinished game (*)\n";
            CheckStatus("White: (open)\nBlack: (open)", loaded + "Replay: 3... O-O-O (6 of 6)",
                s + ": chess960.pgn loaded");
            CheckInt(board.StepToAs(localName, true, 0), ChessBoard.InputAccepted, s + ": chess960.pgn at the start");
            CheckStatus(null, loaded + "Replay: start (0 of 6)", s + ": chess960.pgn at the start");
            CheckInt(board.JoinAs(ChessRules.White, localName), ChessBoard.InputAccepted, s + ": joined the loaded game");
            CheckStatus("White: " + localName + "\nBlack: (open)", loaded + "Replay: start (0 of 6)",
                s + ": the loaded game joined");
        }
        if (LoadPgn(promotionsPgn, s + ": promotions.pgn")) {
            CheckInt(board.StepToAs(localName, true, 1), ChessBoard.InputAccepted, s + ": promotions.pgn at ply 1");
            CheckStatus(null, "Loaded game: White vs Black\nUnfinished game (*)\nReplay: 40... gxh1=Q+ (1 of 9)",
                s + ": promotions.pgn at ply 1");
        }

        board.ClearState();
        CheckStatus("White: (open)\nBlack: (open)", "Join White or Black to play", s + ": cleared");
        Redraw();
        CheckView("cleared after the status checks");
        SendCustomEventDelayedFrames(nameof(RunPgnBoxChecks), 1);
    }

    // ---- 11.2a: PGN box ----

    // The thirteenth frame: the PGN button only for a game with moves, the box opened and closed by it, showing the
    // game's PGN as it stands and kept up to date while open, the PGN logged when the game ends, the whole game in
    // replay, turned to face either end, closed by a reset, and a loaded game's PGN.
    public void RunPgnBoxChecks() {
        string s = "PGN box";
        bool wired = Utilities.IsValid(ui.pgnBox) && Utilities.IsValid(ui.pgnField);
        Check(wired, s + ": box and field assigned", "rebuild the view (Tools > Chess Board > Create Board View)");
        if (!wired) {
            LogSummary();
            return;
        }
        Check(ui.pgnField.transform.IsChildOf(ui.pgnBox.transform), s + ": the field in the box", "it isn't");
        board.ClearState();
        Redraw();
        CheckPgnBox(false, s + ": reset board");
        CheckInt(ui.PressButton(ChessBoardUI.ButtonPgn), ChessBoard.InputRejected, s + ": reset board: refused");
        CheckPgnBox(false, s + ": reset board, refused");

        StartUnforced(s);
        CheckButtons(OwnGame, s + ": no moves yet");
        CheckInt(ui.PressButton(ChessBoardUI.ButtonPgn), ChessBoard.InputRejected, s + ": no moves yet: refused");
        PlayOnBoard("e2", "e4", s);
        PlayOnBoard("e7", "e5", s);
        CheckButtons(OwnGame + " Pgn", s + ": 1. e4 e5");
        CheckLabel(ChessBoardUI.ButtonPgn, "PGN", s + ": closed");
        ui.PressPgn();
        CheckPgnBox(true, s + ": opened");
        CheckLabel(ChessBoardUI.ButtonPgn, "Hide PGN", s + ": opened");
        string text = ui.pgnField.text;
        Check(text.Contains("[White \"" + localName + "\"]") && text.EndsWith("\n\n1. e4 e5 *\n"),
            s + ": opened: the tags and 1. e4 e5 *", text);
        bool white = ui.LocalEnd() == ChessRules.White;
        Check(ui.pgnBox.transform.localPosition.z > 0 == white, s + ": opened: beyond the board from the local end ("
            + (white ? "White" : "Black") + ")", "at " + ui.pgnBox.transform.localPosition.ToString("F3"));
        PlayOnBoard("g1", "f3", s);
        CheckPgnBox(true, s + ": after Nf3");
        text = ui.pgnField.text;
        Check(text.EndsWith("1. e4 e5 2. Nf3 *\n"), s + ": after Nf3: the move added", text);

        // Facing either end, over the far edge of the board from it.
        Vector3 placed = ui.pgnBox.transform.localPosition;
        ui.PlacePgnBox(ChessRules.Black);
        Transform box = ui.pgnBox.transform;
        Check(box.localPosition.z < 0 && Mathf.Abs(box.localPosition.z) == Mathf.Abs(placed.z)
            && box.localPosition.y == placed.y
            && Quaternion.Angle(box.localRotation, Quaternion.Euler(0, 180, 0)) < 0.01f,
            s + ": for Black: on White's side, facing Black", "at " + box.localPosition.ToString("F3") + ", turned "
            + box.localEulerAngles.ToString("F1"));
        ui.PlacePgnBox(ChessRules.White);
        Check(box.localPosition.z > 0 && box.localPosition.y == placed.y
            && Quaternion.Angle(box.localRotation, Quaternion.identity) < 0.01f,
            s + ": for White: on Black's side, facing White", "at " + box.localPosition.ToString("F3") + ", turned "
            + box.localEulerAngles.ToString("F1"));

        // Ended: the PGN logged, also in replay.
        int logged = board.loggedGames;
        CheckInt(board.ResignAs(ChessRules.Black, localName), ChessBoard.InputAccepted, s + ": Black resigned");
        Redraw();
        CheckInt(board.loggedGames, logged + 1, s + ": resigned: logged once");
        CheckPgnBox(true, s + ": resigned");
        CheckString(ui.pgnField.text, board.lastLoggedPgn, s + ": resigned: the PGN logged");
        text = ui.pgnField.text;
        Check(text.Contains("[Result \"1-0\"]") && text.Contains("[Termination \"resignation\"]")
            && text.EndsWith("2. Nf3 1-0\n"), s + ": resigned: 1-0 by resignation", text);
        CheckInt(board.EnterReplayAs(localName, true), ChessBoard.InputAccepted, s + ": entered replay");
        CheckInt(board.StepToAs(localName, true, 1), ChessBoard.InputAccepted, s + ": replay at ply 1");
        Redraw();
        CheckPgnBox(true, s + ": replay at ply 1");
        CheckString(ui.pgnField.text, board.lastLoggedPgn, s + ": replay at ply 1: the whole game");

        // Closed and opened again; a reset closes it, and the next game doesn't open it.
        ui.PressPgn();
        CheckPgnBox(false, s + ": closed");
        CheckLabel(ChessBoardUI.ButtonPgn, "PGN", s + ": closed again");
        ui.PressPgn();
        CheckPgnBox(true, s + ": opened again");
        ui.PressReset();
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, s + ": reset");
        Redraw();
        CheckPgnBox(false, s + ": reset");
        StartUnforced(s + ": next game");
        PlayOnBoard("d2", "d4", s + ": next game");
        CheckPgnBox(false, s + ": next game, 1. d4");

        // A loaded game: its own tags.
        if (LoadPgn(chess960Pgn, s + ": chess960.pgn")) {
            Redraw();
            ui.PressPgn();
            CheckPgnBox(true, s + ": chess960.pgn loaded");
            text = ui.pgnField.text;
            Check(text.Contains("[Variant \"Chess960\"]") && text.Contains("[SetUp \"1\"]"),
                s + ": chess960.pgn loaded: a Chess960 PGN", text);
        }

        board.ClearState();
        Redraw();
        CheckPgnBox(false, s + ": cleared");
        CheckView("cleared after the PGN box checks");
        SendCustomEventDelayedFrames(nameof(RunLoadPgnChecks), 1);
    }

    // ---- 11.2b: Load PGN ----

    ChessPgnImporter savedImporter;
    int loadPgnStart;
    bool progressSeen;

    // The fourteenth frame: the box's paste part wired; Load PGN only on a reset board, opening and closing the box to
    // paste into; an empty paste refused with a note; Siggi-vs-Nuka pasted and imported over the next frames
    // (PollLoadPgn) on the test's own importer, the board's put back afterwards.
    public void RunLoadPgnChecks() {
        string s = "Load PGN";
        bool wired = Utilities.IsValid(ui.pgnCopyPart) && Utilities.IsValid(ui.pgnPastePart)
            && Utilities.IsValid(ui.pgnPasteField) && Utilities.IsValid(ui.pgnImportLabel)
            && Utilities.IsValid(ui.pgnImportButton);
        Check(wired, s + ": the box's copy and paste parts assigned",
            "rebuild the view (Tools > Chess Board > Create Board View)");
        TextAsset nuka = Utilities.IsValid(boardTest) ? boardTest.siggiVsNuka : null;
        Check(Utilities.IsValid(importer) && nuka != null, s + ": importer and Siggi-vs-Nuka assigned",
            "assign ViewTest's Importer and BoardTest's Siggi Vs Nuka in the Inspector");
        if (!wired || !Utilities.IsValid(importer) || nuka == null) {
            LogSummary();
            return;
        }
        Transform box = ui.pgnBox.transform;
        Check(ui.pgnField.transform.IsChildOf(ui.pgnCopyPart.transform)
            && ui.pgnPasteField.transform.IsChildOf(ui.pgnPastePart.transform)
            && ui.pgnImportButton.transform.IsChildOf(ui.pgnPastePart.transform)
            && ui.pgnCopyPart.transform.IsChildOf(box) && ui.pgnPastePart.transform.IsChildOf(box),
            s + ": each field in its part, both parts in the box", "they aren't");
        savedImporter = board.importer;
        board.importer = importer;

        board.ClearState();
        Redraw();
        CheckButtons("JoinWhite JoinBlack LoadPgn", s + ": reset board");
        CheckLabel(ChessBoardUI.ButtonLoadPgn, "Load PGN", s + ": closed");
        ui.PressLoadPgn();
        CheckPasteBox(true, true, "", s + ": opened");
        CheckLabel(ChessBoardUI.ButtonLoadPgn, "Hide PGN", s + ": opened");
        bool white = ui.LocalEnd() == ChessRules.White;
        Check(box.localPosition.z > 0 == white, s + ": opened: beyond the board from the local end",
            "at " + box.localPosition.ToString("F3"));
        ui.PressLoadPgn();
        CheckPasteBox(false, false, "", s + ": closed by the button");
        CheckLabel(ChessBoardUI.ButtonLoadPgn, "Load PGN", s + ": closed again");
        ui.PressLoadPgn();
        ui.PressPgnClose();
        CheckPasteBox(false, false, "", s + ": closed by Close");
        CheckInt(ui.ImportPasted(), ChessBoard.InputRejected, s + ": closed: Import refused");

        // Nothing pasted: refused, and the box says so.
        ui.PressLoadPgn();
        ui.pgnPasteField.text = "  \n";
        int status = board.importStatus;
        CheckInt(ui.ImportPasted(), ChessBoard.InputRejected, s + ": nothing pasted: refused");
        CheckPasteBox(true, true, "Paste a PGN into the box first.", s + ": nothing pasted");
        CheckInt(board.importStatus, status, s + ": nothing pasted: no import");

        // Siggi-vs-Nuka over the next frames, run by the importer's Update.
        ui.pgnPasteField.text = nuka.text;
        CheckInt(ui.ImportPasted(), ChessBoard.InputAccepted, s + ": Siggi-vs-Nuka: started");
        CheckInt(board.importStatus, ChessBoard.ImportRunning, s + ": Siggi-vs-Nuka: under way");
        CheckPasteBox(true, false, "Importing: 0 moves read", s + ": Siggi-vs-Nuka: started");
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, s + ": Siggi-vs-Nuka: the board still reset");
        progressSeen = false;
        loadPgnStart = Time.frameCount;
        SendCustomEventDelayedFrames(nameof(PollLoadPgn), 1);
    }

    // Each frame while the import runs: the box's line counting the moves read. Then: loaded, the box closed and
    // emptied, the loaded game's buttons, its PGN in the copy part; then a failed import (the move and the reason, the
    // box opened again to say so when it was closed meanwhile) and an abandoned one (a join), by hand.
    public void PollLoadPgn() {
        string s = "Load PGN";
        int frames = Time.frameCount - loadPgnStart;
        if (board.importStatus == ChessBoard.ImportRunning && frames < 600) {
            string label = ui.pgnImportLabel.text;
            if (label.StartsWith("Importing: ") && label != "Importing: 0 moves read") progressSeen = true;
            SendCustomEventDelayedFrames(nameof(PollLoadPgn), 1);
            return;
        }
        string n = s + ": Siggi-vs-Nuka";
        Check(progressSeen, n + ": progress shown while importing", "never more than 0 moves read in "
            + frames + " frames");
        CheckInt(board.importStatus, ChessBoard.ImportLoaded, n + ": loaded");
        CheckInt(board.lifecycle * 1000 + board.moveCount, ChessBoard.LifecycleLoaded * 1000 + 54,
            n + ": the board loaded with 54 plies");
        CheckString(board.gameId, "20250710001921", n + ": its GameID");
        Redraw();
        CheckPasteBox(false, false, "", n + ": loaded: the box closed");
        CheckString(ui.pgnPasteField.text, "", n + ": loaded: the paste emptied");
        CheckButtons("JoinWhite JoinBlack First Back Reset?", n + ": loaded");
        ui.PressPgn();
        CheckPgnBox(true, n + ": PGN opens the copy part");
        Check(ui.pgnCopyPart.activeSelf && !ui.pgnPastePart.activeSelf, n + ": PGN: the copy part shown",
            "copy " + ui.pgnCopyPart.activeSelf + ", paste " + ui.pgnPastePart.activeSelf);
        string text = ui.pgnField.text;
        Check(text.Contains("[GameID \"20250710001921\"]") && text.Contains("[Result \"0-1\"]"),
            n + ": PGN: the imported game's", text);

        // Failed: the move it failed at and why; the board unchanged.
        string f = s + ": failed";
        board.ClearState();
        Redraw();
        CheckPgnBox(false, f + ": reset closed the copy part");
        ui.PressLoadPgn();
        ui.pgnPasteField.text = "1. e4 e5 2. Ke3 *";
        CheckInt(ui.ImportPasted(), ChessBoard.InputAccepted, f + ": started");
        importer.ImportFor(float.MaxValue);
        board._ImportFinished();
        CheckPasteBox(true, true, "Import failed at move 2.: illegal move", f);
        CheckInt(board.lifecycle * 1000 + board.moveCount, ChessBoard.LifecycleReset * 1000, f + ": the board reset");
        CheckString(ui.pgnPasteField.text, "1. e4 e5 2. Ke3 *", f + ": the paste kept");
        // Closed while it runs: opened again to say why.
        ui.pgnPasteField.text = "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ke3 *";
        CheckInt(ui.ImportPasted(), ChessBoard.InputAccepted, f + " while closed: started");
        ui.PressLoadPgn();
        CheckPasteBox(false, false, "", f + " while closed: closed");
        importer.ImportFor(float.MaxValue);
        board._ImportFinished();
        CheckPasteBox(true, true, "Import failed at move 4.: illegal move", f + " while closed: opened again");
        // Opened afresh, the old outcome is gone.
        ui.PressLoadPgn();
        ui.PressLoadPgn();
        CheckPasteBox(true, true, "", f + ": opened afresh");

        // Abandoned: someone joins meanwhile.
        string a = s + ": abandoned";
        ui.pgnPasteField.text = boardTest.siggiVsNuka.text;
        CheckInt(ui.ImportPasted(), ChessBoard.InputAccepted, a + ": started");
        importer.ImportFor(0f);
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, a + ": Ana joins");
        CheckInt(board.importStatus, ChessBoard.ImportAbandoned, a);
        Redraw();
        CheckPasteBox(true, false, "Import abandoned: the board changed before the import finished", a);
        CheckButtons("JoinBlack Reset?", a + ": Ana's lobby");
        ui.PressPgnClose();
        CheckPasteBox(false, false, "", a + ": closed");
        CheckInt(ui.PressButton(ChessBoardUI.ButtonLoadPgn), ChessBoard.InputRejected, a + ": Load PGN refused");

        board.importer = savedImporter;
        board.ClearState();
        Redraw();
        CheckPgnBox(false, s + ": cleared");
        CheckView("cleared after the Load PGN checks");
        SendCustomEventDelayedFrames(nameof(RunPieceSetChecks), 1);
    }

    // ---- 12.3: chess piece sets ----

    ChessPieceSet savedViewSet;
    ChessPieceSet savedDefaultSet;
    // Each object's world bounds under the starting set on a cleared board: the centre from its square's centre, and
    // the size.
    Vector3[] boundsCentre = new Vector3[ChessBoardView.PieceCount];
    Vector3[] boundsSize = new Vector3[ChessBoardView.PieceCount];

    // A fifteenth frame: the test set put on the view at once (meshes, both material slots, turn, size, collider, the
    // pieces standing exactly as the starting set's do), a game played with it by drops and clicks with captures and
    // a promotion; back to the arbiter's set with SetChessPieces(null); the view one of the arbiter's piece set
    // listeners, once; SetDefaultChessPieces(null) refused, and each change of the arbiter's default worn at once,
    // unless the view has a set of its own or isn't a listener; everything put back.
    public void RunPieceSetChecks() {
        string s = "piece sets";
        ChessArbiter arbiter = board.arbiter;
        bool ready = Utilities.IsValid(testPieceSet) && Utilities.IsValid(arbiter)
            && Utilities.IsValid(arbiter.defaultPieceSet);
        Check(ready, s + ": the test set and the arbiter's default set assigned", "rebuild with Tools > Chess Board > "
            + "Create Board View, then Create View Test");
        if (!ready) {
            LogSummary();
            return;
        }
        savedViewSet = view.pieceSet;
        savedDefaultSet = arbiter.defaultPieceSet;
        view.animateMoves = false;
        Check(view.AppliedPieceSet() == view.CurrentPieceSet(), s + ": the current set applied", "it isn't");
        CheckString(testPieceSet.Problem(), "", s + ": the test set complete");
        CheckString(savedDefaultSet.Problem(), "", s + ": the default set complete");
        Check(!testPieceSet.whitePawn.gameObject.activeInHierarchy && !savedDefaultSet.whiteKing.gameObject
            .activeInHierarchy, s + ": the sets' templates hidden in play", "shown");

        view.SetChessPieces(null);
        board.ClearState();
        Redraw();
        CheckPieceSet(savedDefaultSet, s + ": the arbiter's set");
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            Bounds bounds = view.pieceRenderers[obj].bounds;
            boundsCentre[obj] = bounds.center - SquareWorld(view.objectSquare[obj]);
            boundsSize[obj] = bounds.size;
        }

        // Applied at once, nothing moving off its square.
        string t = s + ": the test set";
        view.SetChessPieces(testPieceSet);
        CheckPieceSet(testPieceSet, t + ", at once");
        CheckView(t + ", at once");
        int wrong = 0;
        string details = "";
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            Bounds bounds = view.pieceRenderers[obj].bounds;
            Vector3 centre = bounds.center - SquareWorld(view.objectSquare[obj]);
            if ((centre - boundsCentre[obj]).magnitude > 1e-4f || (bounds.size - boundsSize[obj]).magnitude > 1e-4f) {
                if (wrong++ < 2) {
                    details += "object " + obj + " bounds " + centre.ToString("F4") + " size " + bounds.size.ToString("F4")
                        + ", the default's " + boundsCentre[obj].ToString("F4") + " size "
                        + boundsSize[obj].ToString("F4") + "; ";
                }
            }
        }
        Check(wrong == 0, t + ": every piece upright on its square at the default's size", wrong + " differ: "
            + details);

        // A game: drops (pickups) and clicks, captures and a promotion.
        int savedStyle = view.inputStyle;
        StartGame(ChessGame.ModeStandard, t + " game");
        view.SetInputStyle(ChessBoardView.InputPickups);
        CheckInt(view.piecePickups[view.squareObject[Sq("e2")]].pickupable ? 1 : 0, 1, t + ": e2 a pickup");
        DropOver("e2", "e4", t + ": e4 dropped");
        CheckDropped(1, ChessBoard.ChangeMove, t + ": e4");
        view.SetInputStyle(ChessBoardView.InputClicks);
        ClickPiece("d7");
        Check(view.selectedObject != ChessBoardView.NoObject, t + ": d7 clicked: selected", "nothing selected");
        Redraw();
        CheckLifted(view.selectedObject, "d7", t + ": d7 selected");
        ClickSquare("d5");
        CheckUnselected(2, t + ": d5 placed by a click");
        view.SetInputStyle(ChessBoardView.InputPickups);
        int blackPawn = view.squareObject[Sq("d5")];
        DropOver("e4", "d5", t + ": exd5 dropped");
        CheckDropped(3, ChessBoard.ChangeMove, t + ": exd5");
        Check(view.objectSquare[blackPawn] == ChessRules.NoSquare && (view.pieces[blackPawn].localPosition
            - view.CapturedPlacePosition(ChessRules.Black, 0) - view.PieceOffset(blackPawn)).sqrMagnitude < 1e-8f,
            t + ": the captured pawn in its place, offset as on a square", "at "
            + view.pieces[blackPawn].localPosition.ToString("F3"));
        Play("c7", "c6", "c6", t + ": c6");
        view.SetInputStyle(ChessBoardView.InputClicks);
        ClickPiece("d5");
        ClickPiece("c6");
        CheckUnselected(5, t + ": dxc6 by clicking the pawn");
        Play("g8", "f6", "f6", t + ": Nf6");
        Play("c6", "b7", "b7", t + ": cxb7");
        Play("b8", "d7", "d7", t + ": Nbd7");
        view.SetInputStyle(ChessBoardView.InputPickups);
        int promoting = view.squareObject[Sq("b7")];
        DropOver("b7", "a8", t + ": bxa8 dropped");
        CheckInt(view.promotionObject, promoting, t + ": bxa8 waits for its piece");
        CheckInt(view.ChoosePromotion(ChessRules.Queen), ChessBoard.InputAccepted, t + ": bxa8=Q chosen");
        CheckDropped(9, ChessBoard.ChangeMove, t + ": bxa8=Q");
        CheckInt(view.objectType[promoting], ChessRules.Queen, t + ": bxa8=Q: the pawn's object a queen");
        CheckPieceSet(testPieceSet, t + ": after the game");
        view.SetInputStyle(savedStyle);

        // Back to the arbiter's set, at once, every piece on its square.
        string b = s + ": SetChessPieces(null)";
        view.SetChessPieces(null);
        CheckPieceSet(savedDefaultSet, b + ": the arbiter's set, at once");
        CheckView(b);

        // The arbiter's default: the view a listener of the arbiter's, once; null refused; each change worn at once.
        int listeners = arbiter.PieceSetListenerCount();
        int found = 0;
        for (int i = 0; i < arbiter.pieceSetListeners.Length; i++) {
            if (arbiter.pieceSetListeners[i] == view) found++;
        }
        CheckInt(found, 1, s + ": the view among the arbiter's piece set listeners");
        arbiter.AddPieceSetListener(view);
        CheckInt(arbiter.PieceSetListenerCount(), listeners, s + ": the view added again: no change");
        Check(!arbiter.SetDefaultChessPieces(null), s + ": SetDefaultChessPieces(null) refused", "accepted");
        Check(arbiter.defaultPieceSet == savedDefaultSet, s + ": SetDefaultChessPieces(null): the default kept",
            "changed");
        string d = s + ": SetDefaultChessPieces";
        Check(arbiter.SetDefaultChessPieces(testPieceSet), d + "(test set) accepted", "refused");
        CheckPieceSet(testPieceSet, d + "(test set): worn at once");
        CheckView(d + "(test set)");
        Check(arbiter.SetDefaultChessPieces(savedDefaultSet), d + "(default) accepted", "refused");
        CheckPieceSet(savedDefaultSet, d + "(default): worn at once");

        // A set of the view's own wins over the arbiter's changes.
        view.SetChessPieces(testPieceSet);
        arbiter.SetDefaultChessPieces(testPieceSet);
        arbiter.SetDefaultChessPieces(savedDefaultSet);
        CheckPieceSet(testPieceSet, s + ": the view's own set kept through the arbiter's changes");
        view.SetChessPieces(null);
        CheckPieceSet(savedDefaultSet, s + ": the view's own set dropped: the arbiter's");

        // A listener removed isn't told.
        arbiter.RemovePieceSetListener(view);
        CheckInt(arbiter.PieceSetListenerCount(), listeners - 1, s + ": the view removed as a listener");
        arbiter.SetDefaultChessPieces(testPieceSet);
        CheckPieceSet(savedDefaultSet, s + ": the view removed: not told of the arbiter's change");
        arbiter.AddPieceSetListener(view);
        CheckInt(arbiter.PieceSetListenerCount(), listeners, s + ": the view added back");

        // Put back as it was.
        arbiter.SetDefaultChessPieces(savedDefaultSet);
        view.SetChessPieces(savedViewSet);
        Check(view.AppliedPieceSet() == view.CurrentPieceSet() && arbiter.defaultPieceSet == savedDefaultSet,
            s + ": put back", "they aren't");
        board.ClearState();
        Redraw();
        CheckView(s + ": cleared");
        SendCustomEventDelayedFrames(nameof(RunCapturedPlaceChecks), 1);
    }

    // ---- 12.4: captured-piece places ----

    // The last frame: captured pieces stand in their colour's places in capture order, from the game's history (in
    // replay the shown ply's, free moves' captures appended in the fixed order), a takeback empties the last place,
    // pieces beyond the last target are hidden, a promoted pawn is captured as a pawn, and a FEN's missing pieces come
    // first. Animations: a captured piece travels between the board and its place, whose height differs.
    public void RunCapturedPlaceChecks() {
        string c = "captured places";
        view.animateMoves = false;
        Check(view.whiteCapturedPlaces.Length == ChessBoardView.PiecesPerColor
            && view.blackCapturedPlaces.Length == ChessBoardView.PiecesPerColor, c + ": 16 places per colour built",
            view.whiteCapturedPlaces.Length + " and " + view.blackCapturedPlaces.Length);

        board.ClearState();
        StartGame(ChessGame.ModeStandard, c);
        int blackD = view.squareObject[Sq("d7")];
        int whiteE = view.squareObject[Sq("e2")];
        int whiteA = view.squareObject[Sq("a2")];
        int blackQ = view.squareObject[Sq("d8")];
        Play("e2", "e4", "e4", c + ": e4");
        Play("d7", "d5", "d5", c + ": d5");
        Play("e4", "d5", "d5", c + ": exd5");
        CheckPlaces("", "P", c + ": exd5");
        Check(Mathf.Abs(view.CapturedPlacePosition(ChessRules.Black, 0).y) > 0.001f, c + ": the places at another "
            + "height than the board", "at " + view.CapturedPlacePosition(ChessRules.Black, 0).ToString("F3"));
        Play("d8", "d5", "d5", c + ": Qxd5");
        Play("b1", "c3", "c3", c + ": Nc3");
        Play("d5", "a2", "a2", c + ": Qxa2");
        Play("a1", "a2", "a2", c + ": Rxa2");
        CheckPlaces("PP", "PQ", c + ": Rxa2");
        CheckInt(view.objectPlace[blackD], 0, c + ": the d pawn in Black's first place");
        CheckInt(view.objectPlace[whiteE], 0, c + ": the e pawn in White's first place");
        CheckInt(view.objectPlace[whiteA], 1, c + ": the a pawn in White's second place");
        CheckInt(view.objectPlace[blackQ], 1, c + ": the queen in Black's second place");

        // Replay: the places of the ply shown, the same objects in the same places stepping back and forth.
        string r = c + ": replay";
        CheckInt(board.EnterReplayAs(localName, true), ChessBoard.InputAccepted, r + ": entered");
        CheckInt(board.StepToAs(localName, true, 5), ChessBoard.InputAccepted, r + ": stepped to Nc3");
        Redraw();
        CheckView(r + ": Nc3");
        CheckPlaces("P", "P", r + ": Nc3");
        CheckInt(view.squareObject[Sq("a2")], whiteA, r + ": Nc3: the a pawn back on a2");
        CheckInt(view.squareObject[Sq("d5")], blackQ, r + ": Nc3: the queen back on d5");
        CheckInt(view.objectPlace[blackD], 0, r + ": Nc3: the d pawn kept its place");
        CheckInt(board.StepToAs(localName, true, 7), ChessBoard.InputAccepted, r + ": stepped to Rxa2");
        Redraw();
        CheckView(r + ": Rxa2");
        CheckPlaces("PP", "PQ", r + ": Rxa2");
        CheckInt(view.objectPlace[whiteA], 1, r + ": Rxa2: the a pawn in its place again");
        CheckInt(view.objectPlace[blackQ], 1, r + ": Rxa2: the queen in its place again");
        // Free moves: their captures after the ply's places, in the fixed order (the king last, though taken first).
        CheckInt(board.PieceDroppedAs(localName, Sq("d1"), Sq("e8")), ChessBoard.InputAccepted, r + ": Qd1 takes the "
            + "king");
        CheckInt(board.PieceDroppedAs(localName, Sq("a2"), Sq("a7")), ChessBoard.InputAccepted, r + ": Ra2 takes a7");
        Redraw();
        CheckView(r + ": free moves");
        CheckPlaces("PP", "PQPK", r + ": free moves");
        CheckInt(board.StepToAs(localName, true, 7), ChessBoard.InputAccepted, r + ": back to Rxa2");
        Redraw();
        CheckView(r + ": back to Rxa2");
        CheckPlaces("PP", "PQ", r + ": back to Rxa2");

        // A takeback after a capture empties the last place.
        SetRequestRules(true);
        CheckInt(board.StepToAs(localName, true, 6), ChessBoard.InputAccepted, c + ": takeback: stepped to Qxa2");
        CheckInt(board.RequestTakebackAs(ChessRules.White, localName), ChessBoard.InputAccepted,
            c + ": takeback: asked");
        CheckInt(board.AcceptTakebackAs(ChessRules.Black, localName), ChessBoard.InputAccepted,
            c + ": takeback: accepted");
        Redraw();
        CheckView(c + ": takeback");
        CheckPlaces("PP", "P", c + ": takeback");
        CheckInt(view.squareObject[Sq("a2")], blackQ, c + ": takeback: the queen back on a2");

        // Fewer targets than captured pieces: the ones beyond are hidden, and come back when a place frees.
        string f = c + ": one Black target";
        Transform[] savedBlack = view.blackCapturedPlaces;
        Transform[] oneTarget = new Transform[1];
        oneTarget[0] = savedBlack[0];
        view.blackCapturedPlaces = oneTarget;
        Play("a1", "a2", "a2", f + ": Rxa2");
        CheckPlaces("PP", "PQ", f + ": Rxa2");
        Check(!view.IsObjectShown(blackQ) && !view.pieces[blackQ].gameObject.activeSelf, f + ": the queen hidden",
            "shown");
        CheckInt(board.EnterReplayAs(localName, true), ChessBoard.InputAccepted, f + ": replay");
        CheckInt(board.StepToAs(localName, true, 6), ChessBoard.InputAccepted, f + ": stepped to Qxa2");
        Redraw();
        CheckView(f + ": Qxa2");
        Check(view.squareObject[Sq("a2")] == blackQ && view.pieces[blackQ].gameObject.activeSelf,
            f + ": the queen back on a2, shown", "object " + view.squareObject[Sq("a2")]);
        CheckInt(board.StepToAs(localName, true, 7), ChessBoard.InputAccepted, f + ": stepped to Rxa2");
        Redraw();
        CheckView(f + ": Rxa2 again");
        view.blackCapturedPlaces = savedBlack;
        Redraw();
        CheckView(c + ": all targets again");
        Check(view.pieces[blackQ].gameObject.activeSelf, c + ": all targets again: the queen shown", "hidden");

        // The trip between the board and a place, at another height: back to the board, then off it again.
        string a = c + ": animation";
        Settle();
        RecordPositions();
        CheckInt(board.StepToAs(localName, true, 6), ChessBoard.InputAccepted, a + ": stepped to Qxa2");
        Redraw();
        CheckTravel(blackQ, "a2", false, a + ": the queen from its place");
        Settle();
        RecordPositions();
        CheckInt(board.StepToAs(localName, true, 7), ChessBoard.InputAccepted, a + ": stepped to Rxa2");
        Redraw();
        CheckTravelOff(blackQ, true, a + ": the queen to its place");
        Check(Mathf.Abs(view.animationTo[blackQ].y - view.animationFrom[blackQ].y) > 0.001f,
            a + ": the queen's trip changes height", "from " + view.animationFrom[blackQ].ToString("F3") + " to "
            + view.animationTo[blackQ].ToString("F3"));
        Settle();
        view.animateMoves = false;
        CheckInt(board.LeaveReplayAs(localName, true), ChessBoard.InputAccepted, c + ": left replay");
        SetRequestRules(false);

        // A promoted pawn captured goes to its place as a pawn.
        string p = c + ": promotion";
        board.ClearState();
        StartGame(ChessGame.ModeStandard, p);
        int aPawn = view.squareObject[Sq("a2")];
        PlayMoves("a2a4 b7b5 a4b5 a7a6 b5a6 c8b7 a6b7 b8c6", p);
        CheckInt(board.PieceDroppedAs(localName, Sq("b7"), Sq("a8")), ChessBoard.InputPromotionNeeded,
            p + ": bxa8 asks for a piece");
        CheckInt(board.PromotionChosenAs(localName, Sq("b7"), Sq("a8"), ChessRules.Queen), ChessBoard.InputAccepted,
            p + ": bxa8=Q");
        PlayMoves("d8a8", p);
        CheckView(p + ": Qxa8");
        CheckPlaces("P", "PPBR", p + ": Qxa8");
        Check(view.objectPlace[aPawn] == 0 && view.objectType[aPawn] == ChessRules.Pawn
            && view.pieceFilters[aPawn].sharedMesh == SetMesh(ChessRules.PieceOf(ChessRules.White, ChessRules.Pawn)),
            p + ": the promoted pawn in its place as a pawn", "place " + view.objectPlace[aPawn] + ", type "
            + view.objectType[aPawn]);

        // A FEN's missing pieces fill the first places in the fixed order, the game's captures after them.
        string m = c + ": FEN missing pieces";
        LoadFen("4k3/pppppppp/8/8/8/8/PPPPPPPP/RN2K1NR w - - 0 1", m);
        CheckView(m);
        CheckPlaces("QBB", "QRRBBNN", m);
        ContinueLoaded(m);
        PlayMoves("e2e4 d7d5 e4d5", m);
        CheckView(m + ": exd5");
        CheckPlaces("QBB", "QRRBBNNP", m + ": exd5");

        // An extra queen counts against the missing pawn, and is captured as a queen.
        string q = c + ": FEN extra queen";
        LoadFen("rnbqkbnr/pppppppp/7Q/8/8/8/PPPPPPP1/RNBQKBNR b KQkq - 0 1", q);
        CheckView(q);
        CheckPlaces("", "", q + ": no pawn captured");
        ContinueLoaded(q);
        PlayMoves("g7h6", q);
        CheckView(q + ": gxh6");
        CheckPlaces("Q", "", q + ": gxh6");

        board.ClearState();
        Redraw();
        CheckView(c + ": cleared");
        CheckPlaces("", "", c + ": cleared");
        SendCustomEventDelayedFrames(nameof(RunFallChecks), 1);
    }

    // ---- 12.8: the checkmated king falls over ----

    const string MatePgn = "[White \"Fool\"]\n[Black \"Mate\"]\n[Result \"0-1\"]\n\n1. f3 e5 2. g4 Qh4# 0-1\n";
    // Black's king h5 mated by the queen on g5, guarded by the king on f4: a king on the h file falls along the files.
    const string SideMateFen = "8/8/8/6Qk/5K2/8/8/8 b - - 0 1";
    // White's king on e1, standing: its lowest point (view space).
    float fallStanding;
    int fallKing;

    // The seventeenth frame (and timed frames after it): the fool's mate laid down at once (animations off) in the
    // game and on entering replay; then with animations, stepping back to ply 3 starts it standing up at once.
    public void RunFallChecks() {
        string f = "fall";
        view.animateMoves = false;
        board.ClearState();
        Redraw();
        CheckFalls(f + ": a cleared board");
        fallKing = view.squareObject[Sq("e1")];
        fallStanding = LowestPoint(fallKing);
        StartGame(ChessGame.ModeStandard, f);
        PlayMoves("f2f3 e7e5 g2g4", f);
        CheckFalls(f + ": before the mate");
        PlayMoves("d8h4", f);
        CheckInt(board.MatedKingSquare(), Sq("e1"), f + ": Qh4#: White's king mated");
        CheckView(f + ": Qh4#");
        CheckFallen("e1", Vector3.forward, f + ": Qh4#");
        CheckInt(board.EnterReplayAs(localName, false), ChessBoard.InputAccepted, f + ": replay entered");
        Redraw();
        CheckFallen("e1", Vector3.forward, f + ": replay entered");

        view.animateMoves = true;
        CheckInt(board.StepToAs(localName, false, 3), ChessBoard.InputAccepted, f + ": stepped to ply 3");
        Redraw();
        CheckInt(board.MatedKingSquare(), ChessRules.NoSquare, f + ": ply 3: no king mated");
        Check(view.fallTarget[fallKing] == 0 && view.fallAmount[fallKing] == 1 && view.IsFalling(fallKing)
            && view.fallStart[fallKing] <= Time.time, f + ": ply 3: the king starts standing up at once",
            FallText(fallKing));
        SendCustomEventDelayedSeconds(nameof(RunFallStoodChecks), view.fallSeconds + 0.5f);
    }

    // Stood up; stepping forward to the mate again: the king waits for the queen to land, then falls.
    public void RunFallStoodChecks() {
        string f = "fall";
        CheckView(f + ": ply 3: stood up");
        CheckFalls(f + ": ply 3: stood up");
        CheckInt(board.StepToAs(localName, false, 4), ChessBoard.InputAccepted, f + ": stepped to the mate");
        Redraw();
        Check(view.fallTarget[fallKing] == 1 && view.fallAmount[fallKing] == 0
            && view.fallStart[fallKing] > Time.time + 0.001f, f + ": stepped to the mate: the king waits for the queen",
            FallText(fallKing));
        Check(view.animating[view.squareObject[Sq("h4")]], f + ": stepped to the mate: the queen travels", "it doesn't");
        SendCustomEventDelayedSeconds(nameof(RunFallMidChecks), view.moveSeconds + view.fallSeconds / 2);
    }

    // Halfway down: partly over, in its place, its base over e1 and its lowest point no lower than standing.
    public void RunFallMidChecks() {
        string f = "fall: halfway";
        float amount = view.fallAmount[fallKing];
        Check(amount > 0 && amount < 1 && view.IsFalling(fallKing), f + ": partly over", FallText(fallKing));
        Check((view.pieces[fallKing].localPosition - view.ObjectPlace(fallKing)).sqrMagnitude < 1e-8f,
            f + ": in its place", "at " + view.pieces[fallKing].localPosition.ToString("F3") + ", its place "
            + view.ObjectPlace(fallKing).ToString("F3"));
        CheckInt(view.DropSquare(view.PieceBase(fallKing)), Sq("e1"), f + ": its base over e1");
        float lowest = LowestPoint(fallKing);
        Check(lowest > fallStanding - 1e-5f, f + ": not through the board",
            "lowest " + lowest.ToString("F5") + ", standing " + fallStanding.ToString("F5"));
        SendCustomEventDelayedSeconds(nameof(RunFallEndChecks), view.fallSeconds / 2 + 0.5f);
    }

    // Down again; then (animations off) a free move, a takeback, a reset, a loaded game ending in mate, a mate of
    // Black's king (falling towards White) and one on the h file (falling along the files).
    public void RunFallEndChecks() {
        string f = "fall";
        CheckView(f + ": stepped to the mate: fallen again");
        CheckFallen("e1", Vector3.forward, f + ": stepped to the mate: fallen again");

        view.animateMoves = false;
        CheckInt(board.PieceDroppedAs(localName, Sq("a7"), Sq("a6")), ChessBoard.InputAccepted, f + ": a free move");
        Redraw();
        CheckFalls(f + ": a free move (custom position)");
        CheckInt(board.StepToAs(localName, false, 4), ChessBoard.InputAccepted, f + ": back to the mate");
        Redraw();
        CheckFallen("e1", Vector3.forward, f + ": back to the mate");

        SetRequestRules(true);
        CheckInt(board.StepToAs(localName, false, 3), ChessBoard.InputAccepted, f + ": takeback: stepped to ply 3");
        CheckInt(board.RequestTakebackAs(ChessRules.Black, localName), ChessBoard.InputAccepted, f + ": takeback: asked");
        CheckInt(board.AcceptTakebackAs(ChessRules.White, localName), ChessBoard.InputAccepted,
            f + ": takeback: accepted");
        Redraw();
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, f + ": takeback: the game goes on");
        CheckFalls(f + ": takeback");

        PlayMoves("d8h4", f + ": mated again");
        CheckFallen("e1", Vector3.forward, f + ": mated again");
        view.animateMoves = true;
        CheckInt(board.ResetBoardAs(localName, true), ChessBoard.InputAccepted, f + ": reset");
        Redraw();
        CheckFalls(f + ": reset (animations on): stood up at once");
        view.animateMoves = false;

        if (Utilities.IsValid(importer)) {
            board.ClearState();
            importer.StartImport(MatePgn, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
            CheckInt(importer.ImportFor(float.MaxValue), ChessPgn.ImportDone, f + ": loaded mate: imported");
            CheckInt(board.LoadImport(importer), ChessBoard.InputAccepted, f + ": loaded mate: loaded");
            view.animateMoves = true;
            Redraw();
            view.animateMoves = false;
            CheckInt(board.replayIndex, 4, f + ": loaded mate: shown at its end");
            CheckFallen("e1", Vector3.forward, f + ": loaded mate (animations on): fallen at once");
            CheckInt(board.StepToAs(localName, true, 3), ChessBoard.InputAccepted, f + ": loaded mate: ply 3");
            Redraw();
            CheckFalls(f + ": loaded mate: ply 3");
        } else {
            Check(false, f + ": importer assigned", "assign ViewTest's Importer in the Inspector");
        }

        board.ClearState();
        StartGame(ChessGame.ModeStandard, f + " (Black)");
        PlayMoves("e2e4 e7e5 d1h5 b8c6 f1c4 g8f6 h5f7", f + " (Black)");
        CheckInt(board.MatedKingSquare(), Sq("e8"), f + " (Black): Qxf7#: Black's king mated");
        CheckFallen("e8", Vector3.back, f + " (Black): Qxf7#");

        LoadFen(SideMateFen, f + " (h file)");
        CheckInt(board.MatedKingSquare(), Sq("h5"), f + " (h file): Black's king on h5 mated");
        CheckFallen("h5", Vector3.left, f + " (h file)");

        board.ClearState();
        Redraw();
        CheckFalls(f + ": cleared");
        CheckView(f + ": cleared");
        view.animateMoves = savedAnimate;
        LogSummary();
    }

    // No king mated in the position shown, and every object standing, none falling.
    void CheckFalls(string name) {
        CheckInt(board.MatedKingSquare(), ChessRules.NoSquare, name + ": no king mated");
        int wrong = 0;
        string details = "";
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (view.fallTarget[obj] == 0 && view.fallAmount[obj] == 0) continue;
            if (wrong++ < 3) details += "object " + obj + ": " + FallText(obj) + "; ";
        }
        Check(wrong == 0, name + ": every piece standing", wrong + " not: " + details);
    }

    // The king on square lying: fallen all the way, its top towards direction (local space), its base over its square
    // and its lowest point as high as a standing king's, in its place.
    void CheckFallen(string square, Vector3 direction, string name) {
        int obj = view.squareObject[Sq(square)];
        if (obj == ChessBoardView.NoObject) {
            Check(false, name + ": the king on " + square + " fallen", "no object there");
            return;
        }
        Check(view.fallTarget[obj] == 1 && view.fallAmount[obj] == 1 && !view.IsFalling(obj),
            name + ": the king on " + square + " fallen", FallText(obj));
        Vector3 up = PieceUp(obj);
        Check((up - direction).sqrMagnitude < 1e-6f, name + ": lying towards " + direction.ToString("F0"),
            "its top towards " + up.ToString("F3"));
        CheckInt(view.DropSquare(view.PieceBase(obj)), Sq(square), name + ": its base over " + square);
        float lowest = LowestPoint(obj);
        Check(Mathf.Abs(lowest - fallStanding) < 1e-5f, name + ": lying on the board",
            "lowest " + lowest.ToString("F5") + ", standing " + fallStanding.ToString("F5"));
        Check((view.pieces[obj].localPosition - view.ObjectPlace(obj)).sqrMagnitude < 1e-8f, name + ": in its place",
            "at " + view.pieces[obj].localPosition.ToString("F3"));
    }

    string FallText(int obj) {
        return "fallen " + view.fallAmount[obj].ToString("F3") + ", going to " + view.fallTarget[obj]
            + ", starting in " + (view.fallStart[obj] - Time.time).ToString("F3") + " s";
    }

    // Which way obj's top points (view space), its template's turn taken out: up when it stands.
    Vector3 PieceUp(int obj) {
        Quaternion template = Quaternion.identity;
        ChessPieceSet set = view.AppliedPieceSet();
        int type = view.objectType[obj];
        if (Utilities.IsValid(set) && type != ChessRules.NoPieceType) {
            Transform t = set.Template(view.ObjectColor(obj), type);
            if (Utilities.IsValid(t)) template = t.localRotation;
        }
        return view.pieces[obj].localRotation * Quaternion.Inverse(template) * Vector3.up;
    }

    // The lowest point of obj's mesh bounds (view space).
    float LowestPoint(int obj) {
        Mesh mesh = view.pieceFilters[obj].sharedMesh;
        Transform piece = view.pieces[obj];
        if (mesh == null) return 0;
        Bounds bounds = mesh.bounds;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        float lowest = float.MaxValue;
        for (int corner = 0; corner < 8; corner++) {
            Vector3 local = center + new Vector3((corner & 1) == 0 ? -extents.x : extents.x,
                (corner & 2) == 0 ? -extents.y : extents.y, (corner & 4) == 0 ? -extents.z : extents.z);
            float y = view.transform.InverseTransformPoint(piece.TransformPoint(local)).y;
            if (y < lowest) lowest = y;
        }
        return lowest;
    }

    // Loads fen as a game with no moves (the board then in replay, loaded).
    void LoadFen(string fen, string name) {
        board.ClearState();
        CheckInt(board.LoadGame(fen, new int[0], new string[0], new int[0], 0, ChessGame.ModeStandard, "", "", "*",
            "", ChessArbiter.NoClock, 0), ChessBoard.InputAccepted, name + ": loaded");
        Redraw();
    }

    // Joins both seats of a loaded game and continues it from the position shown.
    void ContinueLoaded(string name) {
        CheckInt(board.JoinAs(ChessRules.White, localName), ChessBoard.InputAccepted, name + ": joined White");
        CheckInt(board.JoinAs(ChessRules.Black, localName), ChessBoard.InputAccepted, name + ": joined Black");
        CheckInt(board.ContinueGameAs(localName), ChessBoard.InputAccepted, name + ": continued");
        Redraw();
    }

    // Whether each colour's captured places are expected (piece letters in place order, "PQ"): the view's capture
    // order, and the objects standing in the places.
    void CheckPlaces(string white, string black, string name) {
        CheckString(CapturedText(ChessRules.White), white, name + ": White's captured pieces");
        CheckString(CapturedText(ChessRules.Black), black, name + ": Black's captured pieces");
        CheckString(PlacesText(ChessRules.White), white, name + ": White's places");
        CheckString(PlacesText(ChessRules.Black), black, name + ": Black's places");
    }

    // The view's capture order for color, as piece letters.
    string CapturedText(int color) {
        string text = "";
        for (int place = 0; place < view.capturedCounts[color]; place++) {
            text += ChessRules.PieceTypeLetter(view.capturedTypes[color * ChessBoardView.MaxCaptured + place]);
        }
        return text;
    }

    // What stands in color's captured places, as piece letters ("-" for a place no object stands in).
    string PlacesText(int color) {
        string text = "";
        int first = color == ChessRules.White ? 0 : ChessBoardView.PiecesPerColor;
        for (int place = 0; place < view.capturedCounts[color]; place++) {
            string letter = "-";
            for (int obj = first; obj < first + ChessBoardView.PiecesPerColor; obj++) {
                if (view.objectSquare[obj] == ChessRules.NoSquare && view.objectPlace[obj] == place) {
                    letter = ChessRules.PieceTypeLetter(view.objectType[obj]);
                }
            }
            text += letter;
        }
        return text;
    }

    // Picks the piece on from up as the local player, moves it so its base is over to (off the square's centre and
    // above the surface, as a hand lets go) and drops it.
    void DropOver(string from, string to, string name) {
        int obj = view.squareObject[Sq(from)];
        if (obj == ChessBoardView.NoObject) {
            Check(false, name, "no piece on " + from);
            return;
        }
        view.OnPiecePickedUp(obj);
        Check(view.heldObject == obj, name + ": picked up", "not held");
        view.pieces[obj].localPosition = Over(to) + view.PieceOffset(obj);
        view.OnPieceDropped(obj);
    }

    // The centre of a square on the playing surface, in the world.
    Vector3 SquareWorld(int square) {
        return view.transform.TransformPoint(view.SquarePosition(square));
    }

    // The mesh the current set gives piece (null for none).
    Mesh SetMesh(int piece) {
        ChessPieceSet set = view.CurrentPieceSet();
        if (!Utilities.IsValid(set)) return null;
        Transform template = set.Template(ChessRules.PieceColorOf(piece), ChessRules.PieceTypeOf(piece));
        if (!Utilities.IsValid(template)) return null;
        MeshFilter filter = template.GetComponent<MeshFilter>();
        return Utilities.IsValid(filter) ? filter.sharedMesh : null;
    }

    // Whether set is applied and every object standing for a piece wears its template: the mesh, every material
    // slot, the turn and the size, with its collider fitted to the mesh.
    void CheckPieceSet(ChessPieceSet set, string name) {
        Check(view.AppliedPieceSet() == set, name + ": applied", "another set is");
        int wrong = 0;
        string details = "";
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            int type = view.objectType[obj];
            if (type == ChessRules.NoPieceType) continue;
            Transform template = set.Template(view.ObjectColor(obj), type);
            Transform piece = view.pieces[obj];
            Mesh mesh = template.GetComponent<MeshFilter>().sharedMesh;
            Material[] want = template.GetComponent<MeshRenderer>().sharedMaterials;
            Material[] have = view.pieceRenderers[obj].sharedMaterials;
            BoxCollider box = view.pieceColliders[obj];
            string error = "";
            if (view.pieceFilters[obj].sharedMesh != mesh) {
                error = "mesh";
            } else if (have.Length != want.Length) {
                error = have.Length + " materials, not " + want.Length;
            } else if (obj != view.heldObject && Quaternion.Angle(piece.localRotation, template.localRotation) > 0.1f) {
                error = "turned " + piece.localRotation.eulerAngles.ToString("F1");
            } else if ((piece.localScale - template.localScale).sqrMagnitude > 1e-8f) {
                error = "scale " + piece.localScale.ToString("F3");
            } else if ((box.center - mesh.bounds.center).sqrMagnitude > 1e-10f
                || (box.size - mesh.bounds.size).sqrMagnitude > 1e-10f) {
                error = "collider " + box.size.ToString("F4") + ", the mesh " + mesh.bounds.size.ToString("F4");
            }
            for (int i = 0; error.Length == 0 && i < want.Length; i++) {
                if (have[i] != want[i]) error = "material " + i;
            }
            if (error.Length > 0 && wrong++ < 3) details += "object " + obj + ": " + error + "; ";
        }
        Check(wrong == 0, name + ": every piece wears its template", wrong + " wrong: " + details);
    }

    // Whether the PGN box shows its paste part (shown) or is hidden; shown, with the Import button as importShown
    // says and the status line reading note.
    void CheckPasteBox(bool shown, bool importShown, string note, string name) {
        Redraw();
        string errors = "";
        if (ui.pgnShown != shown || ui.pgnPasting != shown) {
            errors += "pgnShown " + ui.pgnShown + ", pgnPasting " + ui.pgnPasting + "; ";
        }
        if (ui.pgnBox.activeSelf != shown) errors += "box " + (shown ? "hidden" : "shown") + "; ";
        if (shown) {
            if (!ui.pgnPastePart.activeSelf || ui.pgnCopyPart.activeSelf) {
                errors += "paste part " + ui.pgnPastePart.activeSelf + ", copy part " + ui.pgnCopyPart.activeSelf
                    + "; ";
            }
            if (ui.pgnImportButton.activeSelf != importShown) {
                errors += "Import " + (importShown ? "hidden" : "shown") + "; ";
            }
            if (ui.pgnImportLabel.text != note) errors += "line \"" + ui.pgnImportLabel.text + "\"; ";
        }
        Check(errors.Length == 0, name + ": " + (shown ? "the paste box shown" + (note.Length > 0 ? ", \"" + note
            + "\"" : "") : "the box hidden"), errors);
    }

    // Whether the PGN box is shown, as the view says, and when shown holds the game's PGN as it stands.
    void CheckPgnBox(bool shown, string name) {
        string errors = "";
        if (ui.pgnShown != shown) errors += "pgnShown " + ui.pgnShown + "; ";
        if (ui.pgnBox.activeSelf != shown) errors += "box " + (shown ? "hidden" : "shown") + "; ";
        if (shown && ui.pgnField.text != board.GamePgn()) errors += "text \"" + ui.pgnField.text + "\"; ";
        Check(errors.Length == 0, name + ": " + (shown ? "shown with the game's PGN" : "hidden"), errors);
    }

    // After a Refresh, the view's players text (unless null) and status text at the turn start are as expected, and
    // every panel shows the view's texts for the moment it drew them. The server time can tick within a frame, so a
    // panel may show the texts from just before or just after the Refresh (a clock at 5:00 there and 4:59 here).
    void CheckStatus(string players, string status, string name) {
        double before = board.ServerTime();
        Redraw();
        double after = board.ServerTime();
        double now = board.turnStart;
        if (players != null) CheckString(ui.PlayersTextAt(now), players, name + ": players");
        CheckString(ui.StatusTextAt(now), status, name + ": status");
        string playersBefore = ui.PlayersTextAt(before);
        string playersAfter = ui.PlayersTextAt(after);
        string statusBefore = ui.StatusTextAt(before);
        string statusAfter = ui.StatusTextAt(after);
        string errors = "";
        for (int i = 0; i < ui.statusLabels.Length; i++) {
            Text label = ui.statusLabels[i];
            if (Utilities.IsValid(label) && label.text != statusBefore && label.text != statusAfter) {
                errors += "status " + i + ": " + label.text + "; ";
            }
            if (i >= ui.statusPlayerLabels.Length) continue;
            label = ui.statusPlayerLabels[i];
            if (Utilities.IsValid(label) && label.text != playersBefore && label.text != playersAfter) {
                errors += "players " + i + ": " + label.text + "; ";
            }
        }
        Check(errors.Length == 0, name + ": the panels show it", errors);
    }

    // The ended game's text with ending and winner set on it.
    void CheckEnding(int ending, int winner, string expected) {
        board.ending = ending;
        board.winner = winner;
        CheckString(ui.EndingText(), expected, "status: ending " + ending + ", winner " + winner);
    }

    // No mode or clock forced in the snapshot, and no clock chosen, whatever the arbiter's defaults.
    void Unforce() {
        board.gameSettings &= ~(ChessArbiter.SettingModeForced | ChessArbiter.SettingClockForced);
        board.baseTime = ChessArbiter.NoClock;
        board.increment = 0;
    }

    // Seats the local player on both sides and starts a Standard game with no clock.
    void StartUnforced(string name) {
        CheckInt(board.JoinAs(ChessRules.White, localName), ChessBoard.InputAccepted, name + ": joined White");
        CheckInt(board.JoinAs(ChessRules.Black, localName), ChessBoard.InputAccepted, name + ": joined Black");
        Unforce();
        board.mode = ChessGame.ModeStandard;
        CheckInt(board.StartGameAs(localName), ChessBoard.InputAccepted, name + ": started");
        Redraw();
    }

    // Clears the board and loads pgn; whether it did.
    bool LoadPgn(TextAsset pgn, string name) {
        Check(Utilities.IsValid(importer) && pgn != null, name + ": importer and PGN assigned",
            "assign ViewTest's Importer and PGNs in the Inspector");
        if (!Utilities.IsValid(importer) || pgn == null) return false;
        board.ClearState();
        importer.StartImport(pgn.text, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
        CheckInt(importer.ImportFor(float.MaxValue), ChessPgn.ImportDone, name + ": imported");
        return board.LoadImport(importer) == ChessBoard.InputAccepted;
    }

    // Records where every object stands and returns the object on square, about to move.
    int BeforeMove(string square) {
        RecordPositions();
        return view.squareObject[Sq(square)];
    }

    void RecordPositions() {
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) before[obj] = view.pieces[obj].localPosition;
    }

    // Plays from to on the board directly (as a move arriving from another client is), then refreshes the view.
    void PlayOnBoard(string from, string to, string name) {
        CheckInt(board.PieceDroppedAs(localName, Sq(from), Sq(to)), ChessBoard.InputAccepted,
            name + ": " + from + "-" + to + " played");
        Redraw();
    }

    // Puts everything in place at once (a Refresh with animations off), then turns them back on.
    void Settle() {
        view.animateMoves = false;
        Redraw();
        view.animateMoves = true;
        CheckAtRest("settled");
    }

    // Whether obj travels from where it stood (RecordPositions) to square, still standing there, starting now or
    // (waits) once the others have landed.
    void CheckTravel(int obj, string square, bool waits, string name) {
        CheckTravelTo(obj, view.SquarePosition(Sq(square)), waits, name + ": travels to " + square);
    }

    // The same for a captured piece, to its captured place (step 12.4): once the mover has landed (waits), or at once
    // when the mover was dropped in place.
    void CheckTravelOff(int obj, bool waits, string name) {
        if (obj == ChessBoardView.NoObject) {
            Check(false, name + ": travels off the board", "no object");
            return;
        }
        CheckTravelTo(obj, view.ObjectPlace(obj), waits,
            name + ": travels off the board, " + (waits ? "once the mover has landed" : "without waiting"));
    }

    void CheckTravelTo(int obj, Vector3 to, bool waits, string name) {
        if (obj == ChessBoardView.NoObject) {
            Check(false, name, "no object");
            return;
        }
        Vector3 from = before[obj];
        bool waiting = view.animationStart[obj] > Time.time + 0.001f;
        Check(view.animating[obj] && (view.animationFrom[obj] - from).sqrMagnitude < 1e-10f
            && (view.animationTo[obj] - to).sqrMagnitude < 1e-10f && waiting == waits
            && (view.pieces[obj].localPosition - from).sqrMagnitude < 1e-10f, name,
            "animating " + view.animating[obj] + " from " + view.animationFrom[obj].ToString("F3") + " (expected "
            + from.ToString("F3") + ") to " + view.animationTo[obj].ToString("F3") + " (expected " + to.ToString("F3")
            + "), waiting " + waiting + ", now at " + view.pieces[obj].localPosition.ToString("F3"));
    }

    int TravellingCount() {
        int count = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (view.animating[obj]) count++;
        }
        return count;
    }

    // The objects travelling that stood on the board (RecordPositions): not the ones beside it, which shift along
    // their rows when a captured piece takes a place before them.
    int TravellingFromBoard() {
        int count = 0;
        float half = 4 * view.squareSize;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (view.animating[obj] && Mathf.Abs(before[obj].x) < half && Mathf.Abs(before[obj].z) < half) count++;
        }
        return count;
    }

    // The view shows the board with every object in place and none travelling.
    void CheckAtRest(string name) {
        CheckView(name);
        CheckInt(TravellingCount(), 0, name + ": nothing travelling");
    }

    // Brings the view and the UI up to the board now, as each does by itself on the frame after a change.
    void Redraw() {
        view.Refresh();
        ui.Refresh();
    }

    // Whether the object has a collider and it's a trigger.
    bool IsTrigger(Transform obj) {
        if (!Utilities.IsValid(obj)) return false;
        Collider collider = obj.GetComponent<Collider>();
        return Utilities.IsValid(collider) && collider.isTrigger;
    }

    // Whether exactly the squares in expected (names separated by spaces, in any order) have their target mark shown,
    // each with the look of its kind: a name alone for a small yellow diamond (an empty square), "x" before it for a
    // full-size red one (a capture), "o" for a full-size yellow one (a castle's rook square).
    void CheckTargets(string expected, string name) {
        string wrong = "";
        string padded = " " + expected + " ";
        for (int square = 0; square < 64; square++) {
            string text = SquareText(square);
            int want = ChessBoardView.MarkNone;
            if (padded.Contains(" " + text + " ")) want = ChessBoardView.MarkMove;
            else if (padded.Contains(" x" + text + " ")) want = ChessBoardView.MarkCapture;
            else if (padded.Contains(" o" + text + " ")) want = ChessBoardView.MarkCastle;
            int shown = view.TargetMarkShown(square);
            if (shown != want) wrong += text + " (" + MarkText(shown) + ") ";
        }
        Check(wrong.Length == 0, name + ": target marks [" + expected + "]", "wrong on " + wrong);
    }

    string MarkText(int mark) {
        if (mark == ChessBoardView.MarkNone) return "none";
        if (mark == ChessBoardView.MarkMove) return "small yellow";
        if (mark == ChessBoardView.MarkCastle) return "full yellow";
        if (mark == ChessBoardView.MarkCapture) return "full red";
        return "another look";
    }

    // Whether the last move is shown from from to to (both "" for none): the mark (a small diamond) on from, and the
    // line starting there and ending on to.
    void CheckLastMove(string from, string to, string name) {
        string errors = "";
        bool want = from.Length > 0;
        GameObject mark = view.lastMoveMark;
        GameObject line = view.lastMoveLine;
        if (Utilities.IsValid(mark)) {
            Transform t = mark.transform;
            if (mark.activeSelf != want) {
                errors += "mark " + (want ? "hidden; " : "shown; ");
            } else if (want && (t.localPosition - view.SquarePosition(Sq(from))).sqrMagnitude > 1e-8f) {
                errors += "mark at " + t.localPosition.ToString("F3") + "; ";
            } else if (want && Mathf.Abs(t.localScale.x - ChessBoardView.SmallMarkScale) > 0.001f) {
                errors += "mark scaled " + t.localScale.ToString("F2") + "; ";
            }
        }
        if (Utilities.IsValid(line)) {
            Transform t = line.transform;
            Vector3 end = t.localPosition + t.localRotation * new Vector3(0, 0, t.localScale.z);
            if (line.activeSelf != want) {
                errors += "line " + (want ? "hidden; " : "shown; ");
            } else if (want && (t.localPosition - view.SquarePosition(Sq(from))).sqrMagnitude > 1e-8f) {
                errors += "line starting at " + t.localPosition.ToString("F3") + "; ";
            } else if (want && (end - view.SquarePosition(Sq(to))).sqrMagnitude > 1e-8f) {
                errors += "line ending at " + end.ToString("F3") + "; ";
            } else if (want && Mathf.Abs(t.localScale.x - 1) > 0.001f) {
                errors += "line widened " + t.localScale.ToString("F2") + "; ";
            }
        }
        Check(errors.Length == 0, name + ": last move " + (want ? from + " " + to : "not") + " shown", errors);
    }

    // Whether exactly the check lines in expected (each a from and a to square, "b5e8", separated by spaces, in any
    // order; "" for none) are shown: each a line starting on its from square and ending on its to square.
    void CheckCheckLines(string expected, string name) {
        string shown = "";
        string padded = " " + expected + " ";
        int matched = 0;
        int count = 0;
        for (int i = 0; i < view.checkLines.Length; i++) {
            GameObject line = view.checkLines[i];
            if (!Utilities.IsValid(line) || !line.activeSelf) continue;
            count++;
            Transform t = line.transform;
            Vector3 end = t.localPosition + t.localRotation * new Vector3(0, 0, t.localScale.z);
            string text = SquareAt(t.localPosition) + SquareAt(end);
            shown += " " + text;
            if (padded.Contains(" " + text + " ") && Mathf.Abs(t.localScale.x - 1) < 0.001f) matched++;
        }
        int wanted = expected.Length == 0 ? 0 : expected.Split(new char[] { ' ' }).Length;
        Check(matched == wanted && count == wanted, name + ": check lines [" + expected + "]",
            "shown [" + shown.Trim() + "]");
    }

    // The name of the square whose centre is at position (the view's local space), "?" for none.
    string SquareAt(Vector3 position) {
        for (int square = 0; square < 64; square++) {
            if ((position - view.SquarePosition(square)).sqrMagnitude < 1e-8f) return SquareText(square);
        }
        return "?";
    }

    // The material of obj's renderer (null for none).
    Material MaterialOf(GameObject obj) {
        if (!Utilities.IsValid(obj)) return null;
        MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
        return Utilities.IsValid(renderer) ? renderer.sharedMaterial : null;
    }

    // Whether the picker is shown (with a promotion waiting) or hidden (with none); shown, whether it stands over
    // square (no further along the files than the view allows), PickerHeight squares up, facing the promoting side.
    void CheckPicker(bool shown, string square, string name) {
        GameObject picker = view.promotionPicker;
        if (!Utilities.IsValid(picker)) return;
        Check(picker.activeSelf == shown && (view.promotionObject != ChessBoardView.NoObject) == shown,
            name + ": the picker " + (shown ? "shown" : "hidden"),
            "active " + picker.activeSelf + ", waiting object " + view.promotionObject);
        if (!shown || !picker.activeSelf) return;
        Vector3 expected = view.SquarePosition(Sq(square));
        float limit = ChessBoardView.PickerMaxOffset * view.squareSize;
        expected.x = Mathf.Clamp(expected.x, -limit, limit);
        expected.y = ChessBoardView.PickerHeight * view.squareSize;
        Check((picker.transform.localPosition - expected).sqrMagnitude < 1e-8f, name + ": the picker over " + square,
            "at " + picker.transform.localPosition.ToString("F3"));
        bool white = square.Substring(1, 1) == "8";
        Quaternion facing = white ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
        Check(Quaternion.Angle(picker.transform.localRotation, facing) < 1, name + ": the picker facing "
            + (white ? "White" : "Black"), "turned " + Quaternion.Angle(picker.transform.localRotation, facing));
    }

    // Turns takebacks, draws by agreement and threefold claims on or off in the board's snapshot (directly, as
    // BoardTest does), whatever the arbiter's defaults, with the other draw rules off. A reset clears the snapshot.
    void SetRequestRules(bool on) {
        int rules = ChessArbiter.SettingTakebacks | ChessArbiter.SettingDrawAgreement
            | ChessArbiter.SettingThreefoldRepetition;
        board.gameSettings &= ~(ChessArbiter.SettingsDrawRules | ChessArbiter.SettingTakebacks);
        if (on) board.gameSettings |= rules;
    }

    // Plays moves (from and to squares, "e2e4 e7e5"), each of which must be played, by the local player.
    void PlayMoves(string moves, string name) {
        for (int i = 0; i + 4 <= moves.Length; i += 5) {
            string move = moves.Substring(i, 4);
            CheckInt(board.PieceDroppedAs(localName, Sq(move.Substring(0, 2)), Sq(move.Substring(2, 2))),
                ChessBoard.InputAccepted, name + ": " + move + " played");
        }
        Redraw();
    }

    // Whether a button listed with "?" (CheckButtons) should be shown: Reset and Replay when the board lets the local
    // player reset or enter replay (an organizer, or an idle board for Reset), the draw offers, takeback requests and
    // the claim when the snapshot allows them.
    bool OptionalShown(int action) {
        if (action == ChessBoardUI.ButtonReset) return board.CanReset();
        if (action == ChessBoardUI.ButtonEnterReplay) return board.CanEnterReplay();
        if (action == ChessBoardUI.ButtonOfferDrawWhite || action == ChessBoardUI.ButtonOfferDrawBlack) {
            return board.IsGameSettingOn(ChessArbiter.SettingDrawAgreement);
        }
        if (action == ChessBoardUI.ButtonRequestTakebackWhite
            || action == ChessBoardUI.ButtonRequestTakebackBlack) {
            return board.IsGameSettingOn(ChessArbiter.SettingTakebacks);
        }
        if (action == ChessBoardUI.ButtonClaimDraw) {
            return board.IsGameSettingOn(ChessArbiter.SettingThreefoldRepetition);
        }
        return false;
    }

    // Pressing the button for action is refused.
    void CheckRefused(int action, string name) {
        CheckInt(ui.PressButton(action), ChessBoard.InputRejected, name + ": " + buttonNames[action] + " refused");
    }

    // After a Refresh, the buttons shown are exactly the actions in expected (names from buttonNames, separated by
    // spaces), each shown one labelled as the view says; a name followed by "?" is shown as OptionalShown says, and
    // Mode and Clock are hidden when the snapshot forces them. The view's ButtonApplies agrees.
    void CheckButtons(string expected, string name) {
        Redraw();
        string padded = " " + expected + " ";
        string errors = "";
        for (int i = 0; i < ui.buttons.Length && i < ui.buttonActions.Length; i++) {
            GameObject button = ui.buttons[i];
            if (!Utilities.IsValid(button)) continue;
            int action = ui.buttonActions[i];
            if (action < 0 || action >= ChessBoardUI.ButtonCount) continue;
            string actionName = buttonNames[action];
            bool want = padded.Contains(" " + actionName + " ");
            if (padded.Contains(" " + actionName + "? ")) want = OptionalShown(action);
            // PGN (11.2a), unless named, is checked in every state: shown exactly when the game has moves.
            if (action == ChessBoardUI.ButtonPgn && !padded.Contains(" Pgn")) want = board.moveCount > 0;
            // Load PGN (11.2b), unless named, likewise: shown exactly when the board can take an import.
            if (action == ChessBoardUI.ButtonLoadPgn && !padded.Contains(" LoadPgn")) want = board.CanImport();
            if (action == ChessBoardUI.ButtonMode && board.IsModeForced()) want = false;
            if (action == ChessBoardUI.ButtonClock && board.IsClockForced()) want = false;
            if (button.activeSelf != want) {
                errors += actionName + (want ? " hidden; " : " shown; ");
            } else if (ui.ButtonApplies(action) != want) {
                errors += actionName + " applies " + ui.ButtonApplies(action) + "; ";
            } else if (want && i < ui.buttonLabels.Length && Utilities.IsValid(ui.buttonLabels[i])
                && ui.buttonLabels[i].text != ui.ButtonText(action)) {
                errors += actionName + " labelled " + ui.buttonLabels[i].text + "; ";
            }
        }
        Check(errors.Length == 0, name + ": buttons shown " + expected, errors);
    }

    // Every button for action is shown and labelled text.
    void CheckLabel(int action, string text, string name) {
        Redraw();
        string errors = "";
        for (int i = 0; i < ui.buttons.Length && i < ui.buttonActions.Length; i++) {
            if (ui.buttonActions[i] != action || !Utilities.IsValid(ui.buttons[i])) continue;
            if (!ui.buttons[i].activeSelf) {
                errors += "hidden; ";
            } else if (i < ui.buttonLabels.Length && Utilities.IsValid(ui.buttonLabels[i])
                && ui.buttonLabels[i].text != text) {
                errors += "labelled " + ui.buttonLabels[i].text + "; ";
            }
        }
        Check(errors.Length == 0, name + ": " + buttonNames[action] + " labelled " + text, errors);
    }

    const string WhitePieces = "a1 b1 c1 d1 e1 f1 g1 h1 a2 b2 c2 d2 e2 f2 g2 h2";
    const string BlackPieces = "a8 b8 c8 d8 e8 f8 g8 h8 a7 b7 c7 d7 e7 f7 g7 h7";

    // Clicks the piece on square through its ChessPiece, as VRChat's Interact would.
    void ClickPiece(string square) {
        int obj = view.squareObject[Sq(square)];
        ChessPiece piece = obj == ChessBoardView.NoObject ? null : PieceBehaviour(obj);
        if (Utilities.IsValid(piece)) piece.Interact();
        else Check(false, "click on " + square, "no piece there");
    }

    // Clicks square's target through its ChessSquare (whether it's active or not).
    void ClickSquare(string square) {
        ChessSquare target = view.squareTargets[Sq(square)];
        if (Utilities.IsValid(target)) target.Interact();
        else Check(false, "click on the square " + square, "no target");
    }

    // Nothing selected and the board at moveCount moves; then the view, refreshed, matches the board (nothing
    // lifted) with no square clickable.
    void CheckUnselected(int moveCount, string name) {
        CheckInt(view.selectedObject, ChessBoardView.NoObject, name + ": nothing selected");
        CheckInt(board.moveCount, moveCount, name + ": move count");
        Redraw();
        CheckView(name);
        CheckInt(SquareTargetCount(), 0, name + ": no square clickable");
    }

    // Whether obj stands lifted over square.
    void CheckLifted(int obj, string square, string name) {
        Vector3 expected = view.SquarePosition(Sq(square)) + view.PieceOffset(obj)
            + new Vector3(0, ChessBoardView.SelectLift * view.squareSize, 0);
        Check((view.pieces[obj].localPosition - expected).sqrMagnitude < 1e-8f, name + ": lifted over " + square,
            "at " + view.pieces[obj].localPosition.ToString("F3"));
    }

    // Whether exactly the pieces on pieceSquares and the square targets on targetSquares (square names separated by
    // spaces, in any order) are clickable, and no piece off the board.
    void CheckClickables(string pieceSquares, string targetSquares, string name) {
        string wrongPieces = "";
        string wrongSquares = "";
        for (int square = 0; square < 64; square++) {
            string text = " " + SquareText(square) + " ";
            int obj = view.squareObject[square];
            bool piece = obj != ChessBoardView.NoObject && view.IsPieceClickable(obj);
            if (piece != (" " + pieceSquares + " ").Contains(text)) wrongPieces += SquareText(square) + " ";
            if (view.IsSquareClickable(square) != (" " + targetSquares + " ").Contains(text)) {
                wrongSquares += SquareText(square) + " ";
            }
        }
        int offBoard = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (view.objectSquare[obj] == ChessRules.NoSquare && view.IsPieceClickable(obj)) offBoard++;
        }
        Check(wrongPieces.Length == 0 && wrongSquares.Length == 0 && offBoard == 0, name + ": clickable pieces ["
            + pieceSquares + "], squares [" + targetSquares + "]", "pieces wrong on " + wrongPieces
            + "; squares wrong on " + wrongSquares + "; " + offBoard + " off the board");
    }

    // Whether the piece on square has the interact text.
    void CheckText(int square, string text, string name) {
        int obj = view.squareObject[square];
        ChessPiece piece = obj == ChessBoardView.NoObject ? null : PieceBehaviour(obj);
        string actual = Utilities.IsValid(piece) ? piece.InteractionText : "(no piece)";
        Check(actual == text, name + ": labelled " + text, "labelled " + actual);
    }

    // Whether square's target is clickable with the interact text.
    void CheckSquareText(string square, string text, string name) {
        ChessSquare target = view.squareTargets[Sq(square)];
        string actual = Utilities.IsValid(target) ? target.InteractionText : "(no target)";
        Check(view.IsSquareClickable(Sq(square)) && actual == text, name + ": the square " + square + " labelled "
            + text, "clickable " + view.IsSquareClickable(Sq(square)) + ", labelled " + actual);
    }

    int ClickableCount() {
        int count = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (view.IsPieceClickable(obj)) count++;
        }
        return count;
    }

    int SquareTargetCount() {
        int count = 0;
        for (int square = 0; square < 64; square++) {
            if (view.IsSquareClickable(square)) count++;
        }
        return count;
    }

    // A game of the local player against themselves, from fen.
    void CastleGame(string fen, string name) {
        board.ClearState();
        StartGame(ChessGame.ModeStandard, name);
        int clock;
        int number;
        ChessFen.ParseFen(fen, board.board, board.state, out clock, out number);
        board.halfmoveClock = clock;
        board.moveNumber = number;
        Redraw();
        CheckView(name + ": set up");
    }

    // Picks up the object on from through the view and drops it at position (the view's local space), as the
    // piece's OnPickup and OnDrop would, with it moved there in between. Answers whether the pickup was accepted.
    int ViewDrop(string from, Vector3 position) {
        int obj = view.squareObject[Sq(from)];
        if (obj == ChessBoardView.NoObject) return -1;
        view.OnPiecePickedUp(obj);
        int result = view.heldObject == obj ? ChessBoard.InputAccepted : ChessBoard.InputRejected;
        view.pieces[obj].localPosition = position;
        view.OnPieceDropped(obj);
        return result;
    }

    // Checks the board after a drop (its move count and last change), then the view once refreshed.
    void CheckDropped(int moveCount, int change, string name) {
        CheckInt(board.moveCount, moveCount, name + ": move count");
        CheckInt(board.lastChange, change, name + ": the board's last change");
        Redraw();
        CheckView(name);
    }

    // Over a square, off its centre and above the surface, as a hand lets go of a piece.
    Vector3 Over(string square) {
        return view.SquarePosition(Sq(square)) + new Vector3(0.3f, 0.8f, -0.4f) * view.squareSize;
    }

    Vector3 OffBoard() {
        return new Vector3(6 * view.squareSize, 0.8f * view.squareSize, 0);
    }

    // Whether exactly count of the pieces are pickups for the local player, all of them on the board.
    void CheckPickupable(int count, string name) {
        int pickupable = 0;
        int offBoard = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (!Utilities.IsValid(view.piecePickups[obj]) || !view.piecePickups[obj].pickupable) continue;
            pickupable++;
            if (view.objectSquare[obj] == ChessRules.NoSquare) offBoard++;
        }
        Check(pickupable == count && offBoard == 0, name + ": " + count + " pickupable",
            pickupable + " pickupable, " + offBoard + " of them off the board");
    }

    // Whether a piece's OnOwnershipRequest gives the local player ownership exactly when the board lets them touch
    // the pieces, and that's allowed.
    void CheckOwnership(bool allowed, string name) {
        VRCPlayerApi local = Networking.LocalPlayer;
        ChessPiece piece = PieceBehaviour(0);
        if (!Utilities.IsValid(piece)) return;
        bool answer = piece.OnOwnershipRequest(local, local);
        Check(answer == allowed && board.PlayerMayTouchPieces(local) == allowed,
            name + ": a piece's ownership " + (allowed ? "given" : "refused") + ", as the board says",
            "piece " + answer + ", board " + board.PlayerMayTouchPieces(local));
    }

    ChessPiece PieceBehaviour(int obj) {
        if (!Utilities.IsValid(view.pieces[obj])) return null;
        return view.pieces[obj].GetComponent<ChessPiece>();
    }

    // ---- Helpers ----

    // Seats the local player on both sides and starts a game in mode.
    void StartGame(int mode, string name) {
        CheckInt(board.JoinAs(ChessRules.White, localName), ChessBoard.InputAccepted, name + ": joined White");
        CheckInt(board.JoinAs(ChessRules.Black, localName), ChessBoard.InputAccepted, name + ": joined Black");
        if (mode != board.mode) {
            CheckInt(board.SetModeAs(localName, mode), ChessBoard.InputAccepted, name + ": mode set");
        }
        CheckInt(board.StartGameAs(localName), ChessBoard.InputAccepted, name + ": started");
        Redraw();
    }

    // Drops the piece on from on to, which must be played, then checks the view, and that the object that was on
    // from now stands on landing (the king's square when castling).
    void Play(string from, string to, string landing, string name) {
        int mover = view.squareObject[Sq(from)];
        CheckInt(board.PieceDroppedAs(localName, Sq(from), Sq(to)), ChessBoard.InputAccepted, name + ": played");
        Redraw();
        CheckView(name);
        CheckInt(view.squareObject[Sq(landing)], mover, name + ": the " + from + " object moved to " + landing);
    }

    // Where a move's piece ends up: its to square, or the king's destination when castling.
    int Landing(int move) {
        int from = ChessRules.MoveFrom(move);
        int to = ChessRules.MoveTo(move);
        if (!ChessRules.MoveIsCastle(move)) return to;
        bool kingSide = ChessRules.FileOf(to) > ChessRules.FileOf(from);
        return ChessRules.SquareOf(kingSide ? ChessRules.FileG : ChessRules.FileC, ChessRules.RankOf(from));
    }

    // Whether the view shows the position the board shows: each square's object standing for its piece, with that
    // piece's mesh, on that square; no object on an empty square; every object off the board on its captured place,
    // or hidden when it has none with a target, and every captured place filled (step 12.4).
    void CheckView(string name) {
        string errors = "";
        int count = 0;
        int mated = board.MatedKingSquare();
        for (int square = 0; square < 64; square++) {
            int piece = board.PieceAt(square);
            int obj = view.squareObject[square];
            string error = "";
            if (piece == ChessRules.Empty) {
                if (obj != ChessBoardView.NoObject) error = "object " + obj + " on the empty square";
            } else if (obj == ChessBoardView.NoObject) {
                error = "no object for " + PieceText(piece);
            } else if (view.ObjectPiece(obj) != piece) {
                error = "object " + obj + " stands for " + PieceText(view.ObjectPiece(obj)) + ", not " + PieceText(piece);
            } else if (view.objectSquare[obj] != square) {
                error = "object " + obj + " thinks it's on " + SquareText(view.objectSquare[obj]);
            } else if (view.pieceFilters[obj].sharedMesh != SetMesh(piece)) {
                error = "object " + obj + " has the wrong mesh";
            } else if (view.fallTarget[obj] != (square == mated ? 1 : 0)) {
                // Step 12.8: only the king mated in the position shown lies (or is on its way down).
                error = "object " + obj + (square == mated ? " isn't falling, mated" : " is falling, not mated");
            } else if (square != mated && (view.fallAmount[obj] != 0 || (PieceUp(obj) - Vector3.up).sqrMagnitude > 1e-6f)) {
                error = "object " + obj + " isn't upright (fallen " + view.fallAmount[obj] + ")";
            } else if (square == mated && (view.pieces[obj].localPosition - view.ObjectPlace(obj)).sqrMagnitude > 1e-8f) {
                error = "the mated king's object " + obj + " is at " + view.pieces[obj].localPosition.ToString("F3");
            } else if (square != mated && (view.pieces[obj].localPosition - view.SquarePosition(square)
                - view.PieceOffset(obj)).sqrMagnitude > 1e-8f) {
                error = "object " + obj + " is at " + view.pieces[obj].localPosition.ToString("F3");
            } else if (view.DropSquare(view.PieceBase(obj)) != square) {
                // In the world too, whatever the view's scale (step 12.2), from its base (step 12.3).
                error = "object " + obj + " stands over " + SquareText(view.DropSquare(view.PieceBase(obj)))
                    + " in the world";
            }
            if (error.Length > 0 && count++ < 3) errors += SquareText(square) + ": " + error + "; ";
        }
        // Off the board (step 12.4): shown in a captured place with a target, standing on it; otherwise hidden.
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (view.objectSquare[obj] != ChessRules.NoSquare || obj == view.promotionObject) continue;
            int color = view.ObjectColor(obj);
            int place = view.objectPlace[obj];
            bool active = view.pieces[obj].gameObject.activeSelf;
            string error = "";
            if (!view.IsObjectShown(obj)) {
                if (active) error = "is shown, with no place to stand in";
            } else if (!active) {
                error = "is hidden in place " + place;
            } else if (place >= view.capturedCounts[color]
                || view.capturedTypes[color * ChessBoardView.MaxCaptured + place] != view.objectType[obj]) {
                error = "stands in place " + place + ", which isn't its";
            } else if ((view.pieces[obj].localPosition - view.CapturedPlacePosition(color, place)
                - view.PieceOffset(obj)).sqrMagnitude > 1e-8f) {
                error = "isn't on place " + place + " but at " + view.pieces[obj].localPosition.ToString("F3");
            }
            if (error.Length > 0 && count++ < 3) errors += "object " + obj + " " + error + "; ";
        }
        for (int color = ChessRules.White; color <= ChessRules.Black; color++) {
            string text = PlacesText(color);
            if (text.IndexOf('-') >= 0 && count++ < 3) {
                errors += (color == ChessRules.White ? "White" : "Black") + "'s places " + text + "; ";
            }
        }
        Check(count == 0, name + ": the pieces match the board", count + " wrong: " + errors);
    }

    int OnBoardCount() {
        int count = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (view.objectSquare[obj] != ChessRules.NoSquare) count++;
        }
        return count;
    }

    int SquareOrNone(int obj) {
        return obj == ChessBoardView.NoObject ? ChessRules.NoSquare : view.objectSquare[obj];
    }

    // "e2" to its square.
    int Sq(string name) {
        return ChessRules.SquareOf(Files.IndexOf(name.Substring(0, 1)), Ranks.IndexOf(name.Substring(1, 1)));
    }

    string SquareText(int square) {
        if (square == ChessRules.NoSquare) return "no square";
        return Files.Substring(ChessRules.FileOf(square), 1) + Ranks.Substring(ChessRules.RankOf(square), 1);
    }

    string PieceText(int piece) {
        if (piece == ChessRules.Empty) return "nothing";
        string letters = "?PNBRQK";
        string letter = letters.Substring(ChessRules.PieceTypeOf(piece), 1);
        return ChessRules.PieceColorOf(piece) == ChessRules.White ? letter : letter.ToLower();
    }

    void LogSummary() {
        finished = true;
        Debug.Log("[ViewTest] done: " + passed + " passed, " + failed + " failed"
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
            Debug.Log("[ViewTest] PASS: " + name);
        } else {
            failed++;
            Debug.Log("[ViewTest] FAIL: " + name + ": " + details);
        }
    }
}
