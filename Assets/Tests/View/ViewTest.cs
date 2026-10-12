using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Step 9 test behaviour (PLAN.md section 9): the physical board (ChessBoardView) checked against the logical one it
// shows. It changes the board, so it runs once BoardTest has finished with it, and only with no one else in the
// instance. Logs "[ViewTest] PASS: <check>" or "[ViewTest] FAIL: <check>: <details>" per check, then a summary line.
// Built in the scene by Tools > Chess Board > Create View Test (Editor/ViewTestBuilder.cs).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ViewTest : UdonSharpBehaviour {
    // The board and its view under test.
    public ChessBoard board;
    public ChessBoardView view;
    // Waited for, since both change the board; none to start at once.
    public BoardTest boardTest;
    // For the loaded games: an importer and Assets/Tests/PGN/chess960.pgn.txt and promotions.pgn.txt.
    public ChessPgnImporter importer;
    public TextAsset chess960Pgn;
    public TextAsset promotionsPgn;

    const string Files = "abcdefgh";
    const string Ranks = "12345678";

    string localName;
    int passed;
    int failed;
    bool joined;
    bool ran;
    int waitedFrames;

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
            return;
        }
        if (!Utilities.IsValid(board) || !Utilities.IsValid(view)) {
            Check(false, "board and view assigned", "assign ViewTest's Board and View in the Inspector");
            LogSummary();
            return;
        }
        localName = Networking.LocalPlayer.displayName;
        RunGameChecks();
    }

    // ---- 9.1: pieces placed from the board ----

    // The first frame: a cleared board, a game with an en passant capture and both castles, and a move left for the
    // view's own redraw on the next frame (RunResetChecks).
    void RunGameChecks() {
        board.ClearState();
        view.Refresh();
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
        view.Refresh();
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
        view.Refresh();
        CheckView("bxa8: nothing played yet");
        CheckInt(board.PromotionChosenAs(localName, Sq("b7"), Sq("a8"), ChessRules.Queen), ChessBoard.InputAccepted,
            "bxa8=Q: played");
        view.Refresh();
        CheckView("bxa8=Q");
        CheckInt(view.squareObject[Sq("a8")], aPawn, "bxa8=Q: the a2 pawn's object is the queen");
        CheckInt(view.objectType[aPawn], ChessRules.Queen, "bxa8=Q: the object stands for a queen");

        board.ClearState();
        StartGame(ChessGame.ModeChess960, "Chess960 game");
        view.Refresh();
        CheckView("a Chess960 game started (" + ChessFen.WriteFen(board.board, board.state, 0, 1) + ")");
        board.ClearState();
        SendCustomEventDelayedFrames(nameof(RunLoadChecks), 1);
    }

    // The third frame: loaded games, stepped through in replay.
    public void RunLoadChecks() {
        CheckReplay(chess960Pgn, "chess960.pgn", 6);
        CheckReplay(promotionsPgn, "promotions.pgn", 9);
        board.ClearState();
        view.Refresh();
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
        view.Refresh();
        CheckView(name + ": loaded, the last position");
        CheckInt(board.StepToAs(localName, true, 0), ChessBoard.InputAccepted, name + ": step to the start");
        view.Refresh();
        CheckView(name + ": the start");
        for (int ply = 1; ply <= board.moveCount; ply++) {
            int move = board.moveHistory[ply - 1];
            int mover = view.squareObject[ChessRules.MoveFrom(move)];
            string label = name + ": ply " + ply + " (" + board.notation[ply - 1] + ")";
            CheckInt(board.StepToAs(localName, true, ply), ChessBoard.InputAccepted, label + ": stepped");
            view.Refresh();
            CheckView(label);
            CheckInt(view.squareObject[Landing(move)], mover, label + ": the mover's object travelled");
        }
        CheckInt(board.StepToAs(localName, true, 0), ChessBoard.InputAccepted, name + ": back to the start");
        view.Refresh();
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
        view.Refresh();
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
        view.Refresh();
        CheckPickupable(0, "Pickups: the lobby of others");
        CheckOwnership(false, "the lobby of others");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, "Ana and Bob started");
        view.Refresh();
        CheckPickupable(0, "Pickups: others' game");
        CheckOwnership(false, "others' game");
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "Ana resigned");
        view.Refresh();
        CheckPickupable(0, "Pickups: others' ended game");

        board.ClearState();
        CheckInt(board.JoinAs(ChessRules.White, localName), ChessBoard.InputAccepted, "pickups game: joined White");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "pickups game: Bob joined Black");
        CheckInt(board.StartGameAs(localName), ChessBoard.InputAccepted, "pickups game: started");
        view.Refresh();
        CheckPickupable(32, "Pickups: own game");
        CheckOwnership(true, "own game");
        CheckInt(board.PieceDroppedAs(localName, Sq("e2"), Sq("e4")), ChessBoard.InputAccepted, "e4: played");
        view.Refresh();
        CheckPickupable(32, "Pickups: own game, the other side to move");
        CheckInt(board.PieceDroppedAs("Bob", Sq("d7"), Sq("d5")), ChessBoard.InputAccepted, "d5: played");
        CheckInt(board.PieceDroppedAs(localName, Sq("e4"), Sq("d5")), ChessBoard.InputAccepted, "exd5: played");
        view.Refresh();
        CheckPickupable(31, "Pickups: the captured pawn isn't a pickup");
        CheckInt(board.ResignAs(ChessRules.White, localName), ChessBoard.InputAccepted, "pickups game: resigned");
        view.Refresh();
        CheckPickupable(31, "Pickups: own ended game");
        CheckOwnership(true, "own ended game");
        view.SetInputStyle(ChessBoardView.InputClicks);
        CheckPickupable(0, "Clicks: own ended game");
        view.SetInputStyle(ChessBoardView.InputPickups);
        CheckPickupable(31, "Pickups again: own ended game");

        board.ClearState();
        view.SetInputStyle(savedStyle);
        view.Refresh();
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
        view.Refresh();
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
        view.Refresh();
        CheckInt(view.heldObject, d2, "d2 still held after the game ended, its square unchanged");
        view.pieces[d2].localPosition = Over("d4");
        view.OnPieceDropped(d2);
        CheckDropped(2, ChessBoard.ChangeRejected, "d2 dropped over d4 after the game ended: rejected, put back");

        // In replay: free moves, and a held piece whose square another free move takes.
        CheckInt(board.EnterReplayAs(localName, true), ChessBoard.InputAccepted, "entered replay");
        view.Refresh();
        CheckInt(ViewDrop("a1", Over("a5")), ChessBoard.InputAccepted, "replay: a1 picked up");
        CheckDropped(2, ChessBoard.ChangeFreeMove, "replay: a1 dropped over a5, a free move");
        CheckInt(board.replayBoard[Sq("a5")], ChessRules.PieceOf(ChessRules.White, ChessRules.Rook),
            "replay: the rook on a5");
        int e4 = view.squareObject[Sq("e4")];
        view.OnPiecePickedUp(e4);
        CheckInt(view.heldObject, e4, "replay: e4 picked up");
        CheckInt(board.PieceDroppedAs(localName, Sq("d8"), Sq("e4")), ChessBoard.InputAccepted,
            "replay: the queen freely to e4 while e4 is held");
        view.Refresh();
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
        view.Refresh();
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
        view.Refresh();
        CheckView("cleared after the drop checks");
        SendCustomEventDelayedFrames(nameof(RunCastleSpaceChecks), 1);
    }

    // ---- 9.3b: the castle drop space ----

    // King f1, rook h1 (and the same for Black): castling and Kg1 both put the king on g1, so the king side castle
    // needs the space; the queen side one (the king three squares to c1) doesn't. The f pawns keep a castled rook
    // from giving check.
    const string OneSquareFen = "1r3k1r/5p2/8/8/8/8/5P2/1R3K1R w KQkq - 0 1";
    // King d1, rook a1: the queen side castle moves the king one square, to c1.
    const string QueenOneSquareFen = "r2k3r/3p4/8/8/8/8/3P4/R2K3R w KQkq - 0 1";
    const string StandardCastlingFen = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";

    // The sixth frame: the castle drop space shown only while a king whose castle needs it is held (where the view
    // puts it), hidden on the drop, for other pieces, standard castles, once the game ended and in replay; a drop on
    // it castling, and a drop where no space is shown, or on the destination, not.
    public void RunCastleSpaceChecks() {
        int savedStyle = view.inputStyle;
        view.SetInputStyle(ChessBoardView.InputPickups);
        Check(Utilities.IsValid(view.castleSpaces[ChessBoard.CastleKingSide])
            && Utilities.IsValid(view.castleSpaces[ChessBoard.CastleQueenSide]), "both castle spaces assigned",
            "run Tools > Chess Board > Create Board View again");

        CastleSpaceGame(OneSquareFen, "f1 game");
        CheckSpaces(false, false, "f1 game: nothing held");
        int king = view.squareObject[Sq("f1")];
        int rook = view.squareObject[Sq("h1")];
        view.OnPiecePickedUp(king);
        CheckInt(view.heldObject, king, "f1 picked up");
        CheckSpaces(true, false, "f1 held");
        CheckSpacePlace(ChessRules.White, ChessBoard.CastleKingSide, "f1 held");
        view.Refresh();
        CheckSpaces(true, false, "f1 held, after Refresh");
        view.pieces[king].localPosition = OverSpace(ChessRules.White, ChessBoard.CastleKingSide);
        view.OnPieceDropped(king);
        CheckSpaces(false, false, "f1 dropped on the space");
        CheckDropped(1, ChessBoard.ChangeMove, "f1 dropped on the space: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "f1 dropped on the space: the move is a castle", "it isn't");
        CheckInt(view.squareObject[Sq("g1")], king, "f1 dropped on the space: the king's object on g1");
        CheckInt(view.squareObject[Sq("f1")], rook, "f1 dropped on the space: the rook's object on f1");

        // Black: shown beyond rank 8; a drop on the destination is Kg8, not a castle.
        int blackKing = view.squareObject[Sq("f8")];
        view.OnPiecePickedUp(blackKing);
        CheckSpaces(true, false, "f8 held");
        CheckSpacePlace(ChessRules.Black, ChessBoard.CastleKingSide, "f8 held");
        view.pieces[blackKing].localPosition = Over("g8");
        view.OnPieceDropped(blackKing);
        CheckSpaces(false, false, "f8 dropped over g8");
        CheckDropped(2, ChessBoard.ChangeMove, "f8 dropped over g8: played");
        Check(!ChessRules.MoveIsCastle(board.LastMove()), "f8 dropped over g8: Kg8, not a castle", "it castled");
        CheckInt(board.PieceAt(Sq("h8")), ChessRules.PieceOf(ChessRules.Black, ChessRules.Rook),
            "f8 dropped over g8: the rook still on h8");

        // Other pieces show nothing; a king dropped where no space is shown is put back.
        CastleSpaceGame(OneSquareFen, "f1 game again");
        int whiteRook = view.squareObject[Sq("h1")];
        view.OnPiecePickedUp(whiteRook);
        CheckSpaces(false, false, "h1 rook held");
        view.OnPieceDropped(whiteRook);
        CheckDropped(0, ChessBoard.ChangeRejected, "h1 rook dropped back: put back");
        king = view.squareObject[Sq("f1")];
        view.OnPiecePickedUp(king);
        view.pieces[king].localPosition = OverSpace(ChessRules.White, ChessBoard.CastleQueenSide);
        view.OnPieceDropped(king);
        CheckDropped(0, ChessBoard.ChangeRejected, "f1 dropped where the queen side space would be: put back");
        view.OnPiecePickedUp(king);
        view.pieces[king].localPosition = Over("g1");
        view.OnPieceDropped(king);
        CheckDropped(1, ChessBoard.ChangeMove, "f1 dropped over g1: played");
        Check(!ChessRules.MoveIsCastle(board.LastMove()), "f1 dropped over g1: Kg1, not a castle", "it castled");

        // King d1, rook a1: the queen side space, beside c1.
        CastleSpaceGame(QueenOneSquareFen, "d1 game");
        king = view.squareObject[Sq("d1")];
        rook = view.squareObject[Sq("a1")];
        view.OnPiecePickedUp(king);
        CheckSpaces(false, true, "d1 held");
        CheckSpacePlace(ChessRules.White, ChessBoard.CastleQueenSide, "d1 held");
        view.pieces[king].localPosition = OverSpace(ChessRules.White, ChessBoard.CastleQueenSide);
        view.OnPieceDropped(king);
        CheckSpaces(false, false, "d1 dropped on the space");
        CheckDropped(1, ChessBoard.ChangeMove, "d1 dropped on the space: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "d1 dropped on the space: the move is a castle", "it isn't");
        CheckInt(view.squareObject[Sq("c1")], king, "d1 dropped on the space: the king's object on c1");
        CheckInt(view.squareObject[Sq("d1")], rook, "d1 dropped on the space: the rook's object on d1");

        // A standard position needs no space.
        CastleSpaceGame(StandardCastlingFen, "standard game");
        king = view.squareObject[Sq("e1")];
        view.OnPiecePickedUp(king);
        CheckSpaces(false, false, "e1 held in a standard position");
        view.OnPieceDropped(king);

        // Hidden once the game ends while the king is held, and in replay.
        CastleSpaceGame(OneSquareFen, "f1 game, ended");
        king = view.squareObject[Sq("f1")];
        view.OnPiecePickedUp(king);
        CheckSpaces(true, false, "f1 held before resigning");
        CheckInt(board.ResignAs(ChessRules.White, localName), ChessBoard.InputAccepted, "resigned while holding f1");
        view.Refresh();
        CheckInt(view.heldObject, king, "f1 still held after the game ended");
        CheckSpaces(false, false, "f1 held after the game ended");
        view.pieces[king].localPosition = OverSpace(ChessRules.White, ChessBoard.CastleKingSide);
        view.OnPieceDropped(king);
        CheckDropped(0, ChessBoard.ChangeRejected, "f1 dropped on the hidden space after the game ended: put back");
        // Replay shows the game's start, wherever its king stands.
        CheckInt(board.EnterReplayAs(localName, true), ChessBoard.InputAccepted, "entered replay");
        view.Refresh();
        king = NoObjectOr(KingSquare(board.replayBoard, ChessRules.White));
        if (king != ChessBoardView.NoObject) view.OnPiecePickedUp(king);
        Check(king != ChessBoardView.NoObject && view.heldObject == king, "replay: the white king picked up",
            "held " + view.heldObject);
        CheckSpaces(false, false, "replay: the white king held");
        if (king != ChessBoardView.NoObject) view.OnPieceDropped(king);

        board.ClearState();
        view.SetInputStyle(savedStyle);
        view.Refresh();
        CheckSpaces(false, false, "cleared at the end");
        CheckView("cleared at the end");
        LogSummary();
    }

    // A game of the local player against themselves, from fen.
    void CastleSpaceGame(string fen, string name) {
        board.ClearState();
        StartGame(ChessGame.ModeStandard, name);
        int clock;
        int number;
        ChessFen.ParseFen(fen, board.board, board.state, out clock, out number);
        board.halfmoveClock = clock;
        board.moveNumber = number;
        view.Refresh();
        CheckView(name + ": set up");
    }

    // Whether exactly the king side and queen side spaces wanted are shown, as the view says and in the scene.
    void CheckSpaces(bool kingSide, bool queenSide, string name) {
        string errors = "";
        for (int side = 0; side < 2; side++) {
            bool wanted = side == ChessBoard.CastleKingSide ? kingSide : queenSide;
            GameObject space = view.castleSpaces[side];
            bool active = Utilities.IsValid(space) && space.activeSelf;
            if (view.castleSpaceShown[side] != wanted || active != wanted) {
                errors += (side == ChessBoard.CastleKingSide ? "king side" : "queen side") + " shown "
                    + view.castleSpaceShown[side] + ", active " + active + "; ";
            }
        }
        Check(errors.Length == 0, name + ": castle space " + (kingSide ? "king side" : queenSide ? "queen side" : "none"),
            errors);
    }

    // Whether color's space for side stands off the board, beside the castling destination on the outer side of the
    // back rank.
    void CheckSpacePlace(int color, int side, string name) {
        GameObject space = view.castleSpaces[side];
        if (!Utilities.IsValid(space)) return;
        string destination = (side == ChessBoard.CastleKingSide ? "g" : "c") + (color == ChessRules.White ? "1" : "8");
        Vector3 expected = view.SquarePosition(Sq(destination))
            + new Vector3(0, 0, color == ChessRules.White ? -view.squareSize : view.squareSize);
        Check((space.transform.localPosition - expected).sqrMagnitude < 1e-8f,
            name + ": the space beyond " + destination, "at " + space.transform.localPosition.ToString("F3"));
        CheckInt(view.DropSquare(space.transform.position), ChessRules.NoSquare, name + ": the space is off the board");
    }

    // The square of color's king on pieces, NoSquare for none.
    int KingSquare(byte[] pieces, int color) {
        int king = ChessRules.PieceOf(color, ChessRules.King);
        for (int square = 0; square < 64; square++) {
            if (pieces[square] == king) return square;
        }
        return ChessRules.NoSquare;
    }

    // The object on square, NoObject for none or no square.
    int NoObjectOr(int square) {
        return square == ChessRules.NoSquare ? ChessBoardView.NoObject : view.squareObject[square];
    }

    // Over color's castle drop space for side, off its centre and above it, as a hand lets go of a piece.
    Vector3 OverSpace(int color, int side) {
        return view.CastleSpacePosition(color, side) + new Vector3(-0.3f, 0.8f, 0.4f) * view.squareSize;
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
        view.Refresh();
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
        view.Refresh();
    }

    // Drops the piece on from on to, which must be played, then checks the view, and that the object that was on
    // from now stands on landing (the king's square when castling).
    void Play(string from, string to, string landing, string name) {
        int mover = view.squareObject[Sq(from)];
        CheckInt(board.PieceDroppedAs(localName, Sq(from), Sq(to)), ChessBoard.InputAccepted, name + ": played");
        view.Refresh();
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
    // piece's mesh, on that square; no object on an empty square; every object off the board placed beside it.
    void CheckView(string name) {
        string errors = "";
        int count = 0;
        float half = 4 * view.squareSize;
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
            } else if (view.pieceFilters[obj].sharedMesh != view.meshes[ChessRules.PieceTypeOf(piece)]) {
                error = "object " + obj + " has the wrong mesh";
            } else if ((view.pieces[obj].localPosition - view.SquarePosition(square)).sqrMagnitude > 1e-8f) {
                error = "object " + obj + " is at " + view.pieces[obj].localPosition.ToString("F3");
            }
            if (error.Length > 0 && count++ < 3) errors += SquareText(square) + ": " + error + "; ";
        }
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (view.objectSquare[obj] != ChessRules.NoSquare) continue;
            Vector3 position = view.pieces[obj].localPosition;
            if (Mathf.Abs(position.x) < half && Mathf.Abs(position.z) < half && count++ < 3) {
                errors += "object " + obj + " is off the board but at " + position.ToString("F3") + "; ";
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
        Debug.Log("[ViewTest] done: " + passed + " passed, " + failed + " failed"
            + (failed == 0 ? " ALL PASS" : " SOME FAIL"));
    }

    void CheckInt(int actual, int expected, string name) {
        Check(actual == expected, name, "got " + actual + ", expected " + expected);
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
