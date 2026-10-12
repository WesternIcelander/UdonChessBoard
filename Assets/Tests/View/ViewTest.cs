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
        SendCustomEventDelayedFrames(nameof(RunClickChecks), 1);
    }

    // ---- 9.3c: desktop mode ----

    // A white pawn about to promote on e8, the kings out of its way.
    const string PromotionFen = "k7/4P3/8/8/8/8/8/K7 w - - 0 1";

    // The seventh frame, with Clicks: selecting by clicking a piece (lifted, its ownership taken), putting it back by
    // clicking it or its square, switching to another piece, placing on a square, capturing by clicking a piece,
    // castling on the rook, on the destination and on the castle drop space, promoting, illegal placings put back
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
                && target.castleSide == ChessBoardView.NoCastleSide && Utilities.IsValid(target.GetComponent<Collider>())
                && view.DropSquare(target.transform.position) == square) {
                squares++;
            }
        }
        CheckInt(squares, 64, "clicks: every square has its target, over that square");
        int spaces = 0;
        for (int side = 0; side < 2; side++) {
            ChessSquare target = view.castleSpaceTargets[side];
            if (Utilities.IsValid(target) && target.view == view && target.castleSide == side
                && Utilities.IsValid(view.castleSpaces[side]) && target.gameObject == view.castleSpaces[side]
                && Utilities.IsValid(target.GetComponent<Collider>())) {
                spaces++;
            }
        }
        CheckInt(spaces, 2, "clicks: both castle spaces have their target");

        // Nobody's game: nothing to click.
        board.ClearState();
        view.Refresh();
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
        view.Refresh();
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
        view.Refresh();
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
        view.Refresh();
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
        view.Refresh();
        CheckView(c + ": resigned: d7 down on that Refresh");
        CheckUnselected(3, c + ": resigned: d7 put back");
        CheckClickables("", "", c + ": ended");

        // In replay: free moves of any piece; another piece of the selected one's colour is selected instead, one of
        // the other colour captured; leaving replay puts a selection back.
        CheckInt(board.EnterReplayAs(localName, true), ChessBoard.InputAccepted, c + ": entered replay");
        view.Refresh();
        CheckInt(ClickableCount(), OnBoardCount(), c + ": replay: every piece on the board clickable");
        ClickPiece("a1");
        CheckInt(view.selectedObject, view.squareObject[Sq("a1")], c + ": replay: a1 selected");
        view.Refresh();
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
        view.Refresh();
        CheckUnselected(3, c + ": left replay: c1 put back");

        // Castling on the rook (labelled Castle), on the destination, and on the castle drop space (Chess960).
        CastleSpaceGame(StandardCastlingFen, "click castling");
        int king = view.squareObject[Sq("e1")];
        int rook = view.squareObject[Sq("h1")];
        ClickPiece("e1");
        view.Refresh();
        CheckClickables("a1 e1 h1", "a1 c1 d1 e1 f1 g1 h1 d2 e2 f2", "click castling: e1 selected");
        CheckText(Sq("h1"), "Castle", "click castling: e1 selected, h1");
        CheckSpaces(false, false, "click castling: e1 selected");
        ClickPiece("h1");
        CheckUnselected(1, "click castling: h1 clicked with e1 selected: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "click castling: on the rook, a castle", "it isn't");
        CheckInt(view.squareObject[Sq("g1")], king, "click castling: the king's object on g1");
        CheckInt(view.squareObject[Sq("f1")], rook, "click castling: the rook's object on f1");
        ClickPiece("e8");
        ClickSquare("c8");
        CheckUnselected(2, "click castling: e8 then c8 clicked: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "click castling: on the destination, a castle", "it isn't");

        CastleSpaceGame(OneSquareFen, "click space");
        king = view.squareObject[Sq("f1")];
        rook = view.squareObject[Sq("h1")];
        ChessSquare space = view.castleSpaceTargets[ChessBoard.CastleKingSide];
        Check(Utilities.IsValid(space) && space.DisableInteractive, "click space: nothing selected, the space not "
            + "clickable", "clickable");
        ClickPiece("f1");
        CheckSpaces(true, false, "click space: f1 selected");
        Check(Utilities.IsValid(space) && !space.DisableInteractive && space.InteractionText == "Castle",
            "click space: the space clickable, labelled Castle", "not clickable or wrong text");
        CheckSquareText("g1", "Place", "click space: f1 selected, g1");
        ClickPiece("f2");
        CheckSpaces(false, false, "click space: f2 selected instead");
        ClickPiece("f1");
        if (Utilities.IsValid(space)) space.Interact();
        CheckSpaces(false, false, "click space: the space clicked");
        CheckUnselected(1, "click space: the space clicked: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "click space: a castle", "it isn't");
        CheckInt(view.squareObject[Sq("g1")], king, "click space: the king's object on g1");
        CheckInt(view.squareObject[Sq("f1")], rook, "click space: the rook's object on f1");
        ClickPiece("f8");
        ClickSquare("g8");
        CheckUnselected(2, "click space: f8 then g8 clicked: played");
        Check(!ChessRules.MoveIsCastle(board.LastMove()), "click space: g8 clicked: Kg8, not a castle", "it castled");

        // Promoting by clicking: the pawn waits on e8 for its piece.
        CastleSpaceGame(PromotionFen, "click promotion");
        int promoting = view.squareObject[Sq("e7")];
        ClickPiece("e7");
        ClickSquare("e8");
        CheckInt(view.promotionObject, promoting, "click promotion: e8 clicked: waits for a piece");
        CheckInt(view.selectedObject, ChessBoardView.NoObject, "click promotion: nothing selected");
        CheckInt(board.moveCount, 0, "click promotion: nothing played yet");
        view.Refresh();
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
        view.Refresh();
        CheckUnselected(0, "pickups, no clicks: switched to Pickups: e2 put back");
        CheckClickables("", "", "pickups, no clicks: Pickups");
        CheckPickupable(32, "pickups, no clicks: Pickups");
        view.OnPieceClicked(view.squareObject[Sq("e2")]);
        CheckInt(view.selectedObject, ChessBoardView.NoObject, "pickups, no clicks: a click selects nothing");
        CastleSpaceGame(OneSquareFen, "pickups, no clicks: f1 game");
        int heldKing = view.squareObject[Sq("f1")];
        view.OnPiecePickedUp(heldKing);
        CheckSpaces(true, false, "pickups, no clicks: f1 held");
        Check(Utilities.IsValid(space) && space.DisableInteractive, "pickups, no clicks: the space shown, not clickable",
            "clickable");
        view.OnPieceDropped(heldKing);

        board.ClearState();
        view.SetInputStyle(savedStyle);
        view.Refresh();
        CheckView("cleared after the click checks");
        SendCustomEventDelayedFrames(nameof(RunButtonChecks), 1);
    }

    // ---- 9.4a: board buttons ----

    // The Button* actions' names, as CheckButtons lists them.
    string[] buttonNames = { "JoinWhite", "JoinBlack", "Mode", "Clock", "Start", "Continue", "Reset" };

    // The eighth frame: every action has its buttons, each with a label; in each state (a reset board, the local
    // player's lobby and game, someone else's lobby and game, an ended game, a loaded game joined and continued) the
    // buttons shown are exactly the ones that apply, and pressing one does what the board's method does, a hidden
    // one being refused.
    public void RunButtonChecks() {
        int count = view.buttons.Length;
        Check(count > 0 && view.buttonActions.Length == count && view.buttonLabels.Length == count,
            "buttons: wired, each with its action and label", count + " buttons, " + view.buttonActions.Length
            + " actions, " + view.buttonLabels.Length + " labels");
        string missing = "";
        for (int action = 0; action < ChessBoardView.ButtonCount; action++) {
            bool found = false;
            for (int i = 0; i < count && i < view.buttonActions.Length; i++) {
                if (view.buttonActions[i] == action && Utilities.IsValid(view.buttons[i])
                    && i < view.buttonLabels.Length && Utilities.IsValid(view.buttonLabels[i])) {
                    found = true;
                }
            }
            if (!found) missing += buttonNames[action] + " ";
        }
        Check(missing.Length == 0, "buttons: every action has a button", "none for " + missing);
        CheckString(ChessBoardView.ClockText(ChessArbiter.NoClock, 0), "No clock", "clock text: none");
        CheckString(ChessBoardView.ClockText(300, 3), "5+3", "clock text: 5+3");
        CheckString(ChessBoardView.ClockText(45, 0), "45s+0", "clock text: 45 s");

        board.ClearState();
        CheckButtons("JoinWhite JoinBlack", "reset board");
        CheckInt(view.PressButton(ChessBoardView.ButtonStart), ChessBoard.InputRejected, "reset board: Start refused");
        CheckInt(view.PressButton(ChessBoardView.ButtonReset), ChessBoard.InputRejected, "reset board: Reset refused");

        // The local player's lobby, White first.
        view.PressJoinWhite();
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "Join White: in the lobby");
        CheckString(board.whitePlayer, localName, "Join White: seated as White");
        CheckButtons("JoinBlack Mode Clock Reset", "lobby, White joined");
        CheckInt(view.PressButton(ChessBoardView.ButtonStart), ChessBoard.InputRejected,
            "lobby, White joined: Start refused");
        if (!board.IsModeForced()) {
            CheckInt(view.PressButton(ChessBoardView.ButtonMode), ChessBoard.InputAccepted, "Mode pressed");
            CheckInt(board.mode, ChessGame.ModeChess960, "Mode pressed: Chess960");
            CheckLabel(ChessBoardView.ButtonMode, "Mode: Chess960", "Mode pressed");
            view.PressMode();
            CheckInt(board.mode, ChessGame.ModeStandard, "Mode pressed again: Standard");
            CheckLabel(ChessBoardView.ButtonMode, "Mode: Standard", "Mode pressed again");
        }
        if (!board.IsClockForced()) {
            CheckLabel(ChessBoardView.ButtonClock, "Clock: No clock", "lobby, no clock");
            CheckInt(view.PressButton(ChessBoardView.ButtonClock), ChessBoard.InputAccepted, "Clock pressed");
            CheckInt(board.baseTime * 1000 + board.increment, 60000, "Clock pressed: 1+0");
            CheckLabel(ChessBoardView.ButtonClock, "Clock: 1+0", "Clock pressed");
            view.PressClock();
            CheckInt(board.baseTime * 1000 + board.increment, 180002, "Clock pressed again: 3+2");
            CheckLabel(ChessBoardView.ButtonClock, "Clock: 3+2", "Clock pressed again");
        }
        view.PressJoinBlack();
        CheckString(board.blackPlayer, localName, "Join Black: seated as Black too");
        CheckButtons("Mode Clock Start Reset", "lobby, both seats");
        int baseTime = board.baseTime;
        view.PressStart();
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "Start: in game");
        CheckInt(board.baseTime, baseTime, "Start: with the clock chosen");
        CheckButtons("Reset", "own game");
        CheckInt(view.PressButton(ChessBoardView.ButtonJoinWhite), ChessBoard.InputRejected,
            "own game: Join White refused");
        CheckInt(board.ResignAs(ChessRules.White, localName), ChessBoard.InputAccepted, "own game: resigned");
        CheckButtons("Reset", "own game ended");
        view.PressReset();
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, "Reset: the board is reset");
        CheckButtons("JoinWhite JoinBlack", "after Reset");

        // Someone else's lobby and game: only Reset, for an organizer or once the board is idle.
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "others: Ana joined White");
        CheckButtons("JoinBlack Reset?", "others' lobby, Black open");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "others: Bob joined Black");
        CheckButtons("Reset?", "others' lobby");
        CheckInt(view.PressButton(ChessBoardView.ButtonMode), ChessBoard.InputRejected, "others' lobby: Mode refused");
        CheckInt(view.PressButton(ChessBoardView.ButtonClock), ChessBoard.InputRejected,
            "others' lobby: Clock refused");
        CheckInt(view.PressButton(ChessBoardView.ButtonStart), ChessBoard.InputRejected,
            "others' lobby: Start refused");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, "others: Ana started");
        CheckButtons("Reset?", "others' game");

        // A loaded game: both seats joined, the clock chosen, continued from ply 3.
        board.ClearState();
        Check(Utilities.IsValid(importer) && chess960Pgn != null, "buttons: importer and PGN assigned",
            "assign ViewTest's Importer and PGNs in the Inspector");
        if (Utilities.IsValid(importer) && chess960Pgn != null) {
            importer.StartImport(chess960Pgn.text, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
            CheckInt(importer.ImportFor(float.MaxValue), ChessPgn.ImportDone, "loaded game: imported");
            CheckInt(board.LoadImport(importer), ChessBoard.InputAccepted, "loaded game: loaded");
            CheckButtons("JoinWhite JoinBlack Reset?", "loaded game");
            view.PressJoinWhite();
            CheckButtons("JoinBlack Clock Reset", "loaded game, White joined");
            CheckInt(view.PressButton(ChessBoardView.ButtonContinue), ChessBoard.InputRejected,
                "loaded game, White joined: Continue refused");
            view.PressJoinBlack();
            CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "loaded game, both seats: still loaded");
            CheckButtons("Clock Continue Reset", "loaded game, both seats");
            CheckInt(view.PressButton(ChessBoardView.ButtonMode), ChessBoard.InputRejected,
                "loaded game: Mode refused");
            if (!board.IsClockForced()) {
                CheckInt(view.PressButton(ChessBoardView.ButtonClock), ChessBoard.InputAccepted,
                    "loaded game: Clock pressed");
                CheckLabel(ChessBoardView.ButtonClock, "Clock: " + ChessBoardView.ClockText(board.baseTime,
                    board.increment), "loaded game: Clock pressed");
            }
            CheckInt(board.StepToAs(localName, true, 3), ChessBoard.InputAccepted, "loaded game: stepped to ply 3");
            CheckButtons("Clock Continue Reset", "loaded game at ply 3");
            view.PressContinue();
            CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "Continue: in game");
            CheckInt(board.moveCount, 3, "Continue: from ply 3");
            CheckButtons("Reset", "continued game");
        }

        board.ClearState();
        CheckButtons("JoinWhite JoinBlack", "cleared after the button checks");
        CheckView("cleared after the button checks");
        LogSummary();
    }

    // After a Refresh, the buttons shown are exactly the actions in expected (names from buttonNames, separated by
    // spaces), each shown one labelled as the view says; "Reset?" means Reset is shown when the board lets the local
    // player reset (an organizer, or an idle board), and Mode and Clock are hidden when the snapshot forces them. The
    // view's ButtonApplies agrees.
    void CheckButtons(string expected, string name) {
        view.Refresh();
        string padded = " " + expected + " ";
        string errors = "";
        for (int i = 0; i < view.buttons.Length && i < view.buttonActions.Length; i++) {
            GameObject button = view.buttons[i];
            if (!Utilities.IsValid(button)) continue;
            int action = view.buttonActions[i];
            if (action < 0 || action >= ChessBoardView.ButtonCount) continue;
            string actionName = buttonNames[action];
            bool want = padded.Contains(" " + actionName + " ");
            if (action == ChessBoardView.ButtonReset && padded.Contains(" Reset? ")) want = board.CanReset();
            if (action == ChessBoardView.ButtonMode && board.IsModeForced()) want = false;
            if (action == ChessBoardView.ButtonClock && board.IsClockForced()) want = false;
            if (button.activeSelf != want) {
                errors += actionName + (want ? " hidden; " : " shown; ");
            } else if (view.ButtonApplies(action) != want) {
                errors += actionName + " applies " + view.ButtonApplies(action) + "; ";
            } else if (want && i < view.buttonLabels.Length && Utilities.IsValid(view.buttonLabels[i])
                && view.buttonLabels[i].text != view.ButtonText(action)) {
                errors += actionName + " labelled " + view.buttonLabels[i].text + "; ";
            }
        }
        Check(errors.Length == 0, name + ": buttons shown " + expected, errors);
    }

    // Every button for action is shown and labelled text.
    void CheckLabel(int action, string text, string name) {
        view.Refresh();
        string errors = "";
        for (int i = 0; i < view.buttons.Length && i < view.buttonActions.Length; i++) {
            if (view.buttonActions[i] != action || !Utilities.IsValid(view.buttons[i])) continue;
            if (!view.buttons[i].activeSelf) {
                errors += "hidden; ";
            } else if (i < view.buttonLabels.Length && Utilities.IsValid(view.buttonLabels[i])
                && view.buttonLabels[i].text != text) {
                errors += "labelled " + view.buttonLabels[i].text + "; ";
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
        view.Refresh();
        CheckView(name);
        CheckInt(SquareTargetCount(), 0, name + ": no square clickable");
    }

    // Whether obj stands lifted over square.
    void CheckLifted(int obj, string square, string name) {
        Vector3 expected = view.SquarePosition(Sq(square))
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
