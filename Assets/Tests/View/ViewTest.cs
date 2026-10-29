using SiggiChessBoard;
using SiggiUSTools;
using UdonSharp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDK3.Data;
using VRC.SDKBase;

// Step 9 test behaviour (PLAN.md section 9): the physical board (ChessBoardView) checked against the logical one it
// shows, and since step 12.1 the board's UI (ChessBoardUI: buttons, status display, PGN box), each checked on its own
// behaviour. It changes the board, so it runs once BoardTest has finished with it, and only with no one else in the
// instance. Logs "[ViewTest] PASS: <check>" or "[ViewTest] FAIL: <check>: <details>" per check, then a summary line.
// Built in the scene by Tools > Chess Board > Create View Test (Editor/ViewTestBuilder.cs).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ViewTest : ChessTest {
    protected override string TestName() {
        return "ViewTest";
    }

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
    // The package's language pack (Languages/en.json) and a test pack overriding a few of its keys
    // (View/test-language.json), which Tools > Chess Board > Create View Test adds to the Chess Language (step 13.5).
    public TextAsset languagePack;
    public TextAsset testLanguagePack;
    // The other boards' UIs, which share the Chess Language (step 14.10), and a world's own main I18n
    // (View/test-world-language.json, its code x-chess-test), elsewhere in the scene, switching it with
    // SetLanguageGlobally. Both from Create View Test.
    public ChessBoardUI[] otherUIs;
    public I18n worldI18n;

    const string Files = "abcdefgh";
    const string Ranks = "12345678";

    string localName;
    bool joined;
    bool ran;
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
        if (!ReadyAfter(joined, boardTest, null, null, "BoardTest to finish")) {
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
        UseEnglish(ui.i18n);
        Check(view.board == board && ui.board == board, "the view and the UI on the board", "they aren't");
        CheckString(view.LocalSpaceProblem(), "", "the view's objects in its local space (step 12.2)");
        localName = Networking.LocalPlayer.displayName;
        // Every check but the animation checks (9.5b) looks at pieces in place straight after a change.
        savedAnimate = view.animateMoves;
        view.animateMoves = false;
        // Nothing is heard during the checks (step 12.12); the sound checks read what would have played.
        view.muted = true;
        ui.muted = true;
        RunGameChecks();
    }

    // ---- 9.1: pieces placed from the board ----

    // The first frame: a cleared board, a game with an en passant capture and both castles, and a move left for the
    // view's own redraw on the next frame (RunResetChecks).
    void RunGameChecks() {
        board._ClearState();
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

        board._ClearState();
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

        board._ClearState();
        StartGame(ChessGame.ModeChess960, "Chess960 game");
        Redraw();
        CheckView("a Chess960 game started (" + ChessFen.WriteFen(board.board, board.state, 0, 1) + ")");
        board._ClearState();
        SendCustomEventDelayedFrames(nameof(RunLoadChecks), 1);
    }

    // The third frame: loaded games, stepped through in replay.
    public void RunLoadChecks() {
        CheckReplay(chess960Pgn, "chess960.pgn", 6);
        CheckReplay(promotionsPgn, "promotions.pgn", 9);
        board._ClearState();
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
        board._ClearState();
        importer._StartImport(pgn.text, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
        CheckInt(importer._ImportFor(float.MaxValue), ChessPgn.ImportDone, name + ": imported");
        CheckInt(board._LoadImport(importer), ChessBoard.InputAccepted, name + ": loaded");
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

        board._ClearState();
        view._SetInputStyle(ChessBoardView.InputClicks);
        Redraw();
        Check(!view._UsesPickups(), "Clicks: no pickups", "uses pickups");
        CheckPickupable(0, "Clicks: a reset board");
        view._SetInputStyle(ChessBoardView.InputAuto);
        bool inVR = local.IsUserInVR();
        Check(view._UsesPickups() == inVR, "Auto: pickups exactly in VR (in VR: " + inVR + ")",
            "uses pickups: " + view._UsesPickups());
        CheckPickupable(0, "Auto: a reset board");

        view._SetInputStyle(ChessBoardView.InputPickups);
        Check(view._UsesPickups(), "Pickups: pickups", "no pickups");
        CheckPickupable(0, "Pickups: a reset board, no one (step 14.14)");
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

        board._ClearState();
        CheckInt(board.JoinAs(ChessRules.White, localName), ChessBoard.InputAccepted, "pickups game: joined White");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "pickups game: Bob joined Black");
        CheckInt(board.StartGameAs(localName), ChessBoard.InputAccepted, "pickups game: started");
        Redraw();
        // Step 14.14: only the side to move's pieces, and only for its player.
        CheckPickupableColor(16, ChessRules.White, "Pickups: own game, White to move: White's");
        CheckOwnership(true, "own game");
        CheckInt(board.PieceDroppedAs(localName, Sq("e2"), Sq("e4")), ChessBoard.InputAccepted, "e4: played");
        Redraw();
        CheckPickupable(0, "Pickups: own game, the other side to move");
        CheckOwnership(true, "own game, the other side to move");
        CheckInt(board.PieceDroppedAs("Bob", Sq("d7"), Sq("d5")), ChessBoard.InputAccepted, "d5: played");
        Redraw();
        CheckPickupableColor(16, ChessRules.White, "Pickups: own game, White to move again");
        CheckInt(board.PieceDroppedAs(localName, Sq("e4"), Sq("d5")), ChessBoard.InputAccepted, "exd5: played");
        Redraw();
        CheckPickupable(0, "Pickups: exd5, Black to move");
        CheckInt(board.ResignAs(ChessRules.White, localName), ChessBoard.InputAccepted, "pickups game: resigned");
        Redraw();
        CheckPickupable(0, "Pickups: own ended game");
        CheckOwnership(true, "own ended game");

        board._ClearState();
        view._SetInputStyle(savedStyle);
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
        view._SetInputStyle(ChessBoardView.InputPickups);
        board._ClearState();
        StartGame(ChessGame.ModeStandard, "drop game");

        CheckInt(view._DropSquare(view.transform.TransformPoint(Over("e4"))), Sq("e4"), "drop square: over e4");
        CheckInt(view._DropSquare(view.transform.TransformPoint(Over("a1"))), Sq("a1"), "drop square: over a1");
        CheckInt(view._DropSquare(view.transform.TransformPoint(Over("h8"))), Sq("h8"), "drop square: over h8");
        CheckInt(view._DropSquare(view.transform.TransformPoint(OffBoard())), ChessRules.NoSquare,
            "drop square: beside the board");

        int pawn = view.squareObject[Sq("e2")];
        view._OnPiecePickedUp(pawn);
        CheckInt(view.heldObject, pawn, "e2 picked up: held");
        view.pieces[pawn].localPosition = Over("e4");
        Redraw();
        Check((view.pieces[pawn].localPosition - Over("e4")).sqrMagnitude < 1e-8f, "e2 held: not placed by Refresh",
            "at " + view.pieces[pawn].localPosition.ToString("F3"));
        view._OnPieceDropped(pawn);
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
        view._OnPiecePickedUp(d2);
        CheckInt(view.heldObject, d2, "d2 picked up");
        CheckInt(board.ResignAs(ChessRules.White, localName), ChessBoard.InputAccepted, "White resigned while holding d2");
        int ended = board.lastChange;
        Redraw();
        // Step 14.14: no longer a piece the local player may pick up, so it leaves their hand.
        CheckInt(view.heldObject, ChessBoardView.NoObject, "d2 let go once the game ended");
        Check(!view.piecePickups[d2].pickupable, "d2 no pickup once the game ended", "a pickup");
        view.pieces[d2].localPosition = Over("d4");
        view._OnPieceDropped(d2);
        CheckDropped(2, ended, "d2's drop over d4 after the game ended: ignored, put back");

        // In replay: free moves, and a held piece whose square another free move takes.
        CheckInt(board.EnterReplayAs(localName, true), ChessBoard.InputAccepted, "entered replay");
        Redraw();
        CheckInt(ViewDrop("a1", Over("a5")), ChessBoard.InputAccepted, "replay: a1 picked up");
        CheckDropped(2, ChessBoard.ChangeFreeMove, "replay: a1 dropped over a5, a free move");
        CheckInt(board.replayBoard[Sq("a5")], ChessRules.PieceOf(ChessRules.White, ChessRules.Rook),
            "replay: the rook on a5");
        int e4 = view.squareObject[Sq("e4")];
        view._OnPiecePickedUp(e4);
        CheckInt(view.heldObject, e4, "replay: e4 picked up");
        CheckInt(board.PieceDroppedAs(localName, Sq("d8"), Sq("e4")), ChessBoard.InputAccepted,
            "replay: the queen freely to e4 while e4 is held");
        Redraw();
        CheckInt(view.heldObject, ChessBoardView.NoObject, "replay: e4 let go once the queen took its square");
        CheckView("replay: e4 let go");

        // A promotion waits for its piece, refuses what isn't one, then is played.
        board._ClearState();
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
        Check((view.pieces[promoting].localPosition - view._SquarePosition(Sq("a8"))).sqrMagnitude < 1e-8f,
            "b7 dropped over a8: the pawn stands on a8 meanwhile",
            "at " + view.pieces[promoting].localPosition.ToString("F3"));
        // No cancel (step 12.11): a choice that isn't a promotion piece is refused, and the pawn goes on waiting.
        CheckInt(view._ChoosePromotion(ChessRules.NoPieceType), ChessBoard.InputRejected, "no piece chosen: refused");
        CheckInt(view._ChoosePromotion(ChessRules.King), ChessBoard.InputRejected, "a king chosen: refused");
        CheckInt(view.promotionObject, promoting, "refused choices: the pawn still waits");
        CheckInt(board.moveCount, 8, "refused choices: nothing played");
        CheckInt(view._ChoosePromotion(ChessRules.Knight), ChessBoard.InputAccepted, "bxa8=N chosen: played");
        CheckDropped(9, ChessBoard.ChangeMove, "bxa8=N");
        CheckInt(view.squareObject[Sq("a8")], promoting, "bxa8=N: the pawn's object on a8");
        CheckInt(view.objectType[promoting], ChessRules.Knight, "bxa8=N: the object stands for a knight");
        CheckInt(view._ChoosePromotion(ChessRules.Queen), ChessBoard.InputRejected,
            "a choice with no promotion waiting: refused");

        board._ClearState();
        view._SetInputStyle(savedStyle);
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
        view._SetInputStyle(ChessBoardView.InputPickups);

        CastleGame(OneSquareFen, "f1 game");
        int king = view.squareObject[Sq("f1")];
        int rook = view.squareObject[Sq("h1")];
        view._OnPiecePickedUp(king);
        CheckInt(view.heldObject, king, "f1 picked up");
        view.pieces[king].localPosition = Over("h1");
        view._OnPieceDropped(king);
        CheckDropped(1, ChessBoard.ChangeMove, "f1 dropped on h1: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "f1 dropped on h1: the move is a castle", "it isn't");
        CheckInt(view.squareObject[Sq("g1")], king, "f1 dropped on h1: the king's object on g1");
        CheckInt(view.squareObject[Sq("f1")], rook, "f1 dropped on h1: the rook's object on f1");

        // Black: a drop on the destination is Kg8, not a castle.
        int blackKing = view.squareObject[Sq("f8")];
        view._OnPiecePickedUp(blackKing);
        view.pieces[blackKing].localPosition = Over("g8");
        view._OnPieceDropped(blackKing);
        CheckDropped(2, ChessBoard.ChangeMove, "f8 dropped over g8: played");
        Check(!ChessRules.MoveIsCastle(board.LastMove()), "f8 dropped over g8: Kg8, not a castle", "it castled");
        CheckInt(board.PieceAt(Sq("h8")), ChessRules.PieceOf(ChessRules.Black, ChessRules.Rook),
            "f8 dropped over g8: the rook still on h8");

        // A king dropped beyond the back rank, where the space stood, is put back; over g1 it plays Kg1.
        CastleGame(OneSquareFen, "f1 game again");
        king = view.squareObject[Sq("f1")];
        view._OnPiecePickedUp(king);
        view.pieces[king].localPosition = Over("g1") + new Vector3(0, 0, -view.squareSize);
        view._OnPieceDropped(king);
        CheckDropped(0, ChessBoard.ChangeRejected, "f1 dropped beyond g1: put back");
        view._OnPiecePickedUp(king);
        view.pieces[king].localPosition = Over("g1");
        view._OnPieceDropped(king);
        CheckDropped(1, ChessBoard.ChangeMove, "f1 dropped over g1: played");
        Check(!ChessRules.MoveIsCastle(board.LastMove()), "f1 dropped over g1: Kg1, not a castle", "it castled");

        // King d1, rook a1: dropped on a1, it castles queen side.
        CastleGame(QueenOneSquareFen, "d1 game");
        king = view.squareObject[Sq("d1")];
        rook = view.squareObject[Sq("a1")];
        view._OnPiecePickedUp(king);
        view.pieces[king].localPosition = Over("a1");
        view._OnPieceDropped(king);
        CheckDropped(1, ChessBoard.ChangeMove, "d1 dropped on a1: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "d1 dropped on a1: the move is a castle", "it isn't");
        CheckInt(view.squareObject[Sq("c1")], king, "d1 dropped on a1: the king's object on c1");
        CheckInt(view.squareObject[Sq("d1")], rook, "d1 dropped on a1: the rook's object on d1");

        // King already on g1: castling doesn't move it; dropped on h1 it castles, the rook's object going to f1.
        CastleGame(KingStaysFen, "g1 game");
        king = view.squareObject[Sq("g1")];
        rook = view.squareObject[Sq("h1")];
        view._OnPiecePickedUp(king);
        view.pieces[king].localPosition = Over("h1");
        view._OnPieceDropped(king);
        CheckDropped(1, ChessBoard.ChangeMove, "g1 dropped on h1: castled");
        Check(ChessRules.MoveIsCastle(board.LastMove()), "g1 dropped on h1: the move is a castle", "it isn't");
        CheckInt(view.squareObject[Sq("g1")], king, "g1 dropped on h1: the king's object on g1");
        CheckInt(view.squareObject[Sq("f1")], rook, "g1 dropped on h1: the rook's object on f1");

        board._ClearState();
        view._SetInputStyle(savedStyle);
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
        view._SetInputStyle(ChessBoardView.InputClicks);
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
                && view._DropSquare(target.transform.position) == square) {
                squares++;
            }
        }
        CheckInt(squares, 64, "clicks: every square has its target, over that square");

        // Nobody's game: nothing to click.
        board._ClearState();
        Redraw();
        CheckClickables("", "", "clicks: a reset board");

        string c = "click game";
        board._ClearState();
        StartGame(ChessGame.ModeStandard, c);
        CheckClickables(WhitePieces, "", c + ": White to move");
        CheckText(Sq("e2"), "hover_select", c + ": e2");

        // Selected and put back, by clicking the piece and by clicking its square.
        int pawn = view.squareObject[Sq("e2")];
        ClickPiece("e2");
        CheckInt(view.selectedObject, pawn, c + ": e2 clicked: selected");
        Check(Networking.IsOwner(Networking.LocalPlayer, view.pieces[pawn].gameObject),
            c + ": e2 clicked: its ownership taken", "not the owner");
        Redraw();
        CheckLifted(pawn, "e2", c + ": e2 selected");
        CheckClickables(WhitePieces, "e2 e3 e4", c + ": e2 selected");
        CheckText(Sq("e2"), "hover_put_back", c + ": e2 selected");
        CheckSquareText("e2", "hover_put_back", c + ": e2 selected");
        CheckSquareText("e4", "hover_place", c + ": e2 selected");
        CheckText(Sq("d2"), "hover_select", c + ": e2 selected, d2");
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
        Check((view.pieces[pawn].localPosition - view._SquarePosition(Sq("e2"))).sqrMagnitude < 1e-8f,
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
        CheckText(Sq("e5"), "hover_capture", c + ": f3 selected, e5");
        Check(view._IsPieceClickable(view.squareObject[Sq("e5")]), c + ": f3 selected: e5 clickable", "it isn't");
        Check(!view._IsPieceClickable(view.squareObject[Sq("d7")]), c + ": f3 selected: d7 not clickable", "it is");
        ClickPiece("e5");
        CheckUnselected(3, c + ": e5 clicked with f3 selected: Nxe5 played");
        CheckInt(ChessRules.MoveTo(board.LastMove()), Sq("e5"), c + ": Nxe5 goes to e5");
        CheckInt(view.squareObject[Sq("e5")], knight, c + ": Nxe5: the knight's object on e5");

        // Illegal placings: put back, unselected.
        ClickPiece("a7");
        view._OnSquareClicked(Sq("a4"));
        CheckInt(board.lastChange, ChessBoard.ChangeRejected, c + ": a7 to a4 rejected");
        CheckUnselected(3, c + ": a7 selected, a4 clicked: put back");
        ClickPiece("d8");
        view._OnPieceClicked(view.squareObject[Sq("e5")]);
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
        // The captured one too, to put it back (step 13.4).
        CheckInt(ClickableCount(), ChessBoardView.PieceCount, c + ": replay: every piece on the board and the captured "
            + "one clickable");
        ClickPiece("a1");
        CheckInt(view.selectedObject, view.squareObject[Sq("a1")], c + ": replay: a1 selected");
        Redraw();
        CheckInt(SquareTargetCount(), 64, c + ": replay: a1 selected, every square clickable");
        CheckText(Sq("a8"), "hover_capture", c + ": replay: a1 selected, a8");
        CheckText(Sq("b1"), "hover_select", c + ": replay: a1 selected, b1");
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
        CheckText(Sq("h1"), "hover_castle", "click castling: e1 selected, h1");
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
        CheckText(Sq("h1"), "hover_castle", "click chess960: f1 selected, h1");
        CheckSquareText("g1", "hover_place", "click chess960: f1 selected, g1");
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
        Check((view.pieces[promoting].localPosition - view._SquarePosition(Sq("e8"))).sqrMagnitude < 1e-8f,
            "click promotion: the pawn stands on e8 meanwhile", "at " + view.pieces[promoting].localPosition.ToString("F3"));
        CheckInt(view._ChoosePromotion(ChessRules.Rook), ChessBoard.InputAccepted, "click promotion: e8=R chosen");
        CheckDropped(1, ChessBoard.ChangeMove, "click promotion: e8=R");
        CheckInt(view.objectType[promoting], ChessRules.Rook, "click promotion: the pawn's object is a rook");

        // A VR player (Pickups): pickups and no interacts; switching to it puts a selection back.
        board._ClearState();
        StartGame(ChessGame.ModeStandard, "pickups, no clicks");
        ClickPiece("e2");
        CheckInt(view.selectedObject, view.squareObject[Sq("e2")], "pickups, no clicks: e2 selected with Clicks");
        view._SetInputStyle(ChessBoardView.InputPickups);
        Redraw();
        CheckUnselected(0, "pickups, no clicks: switched to Pickups: e2 put back");
        CheckClickables("", "", "pickups, no clicks: Pickups");
        CheckPickupableColor(16, ChessRules.White, "pickups, no clicks: Pickups: White's");
        view._OnPieceClicked(view.squareObject[Sq("e2")]);
        CheckInt(view.selectedObject, ChessBoardView.NoObject, "pickups, no clicks: a click selects nothing");

        board._ClearState();
        view._SetInputStyle(savedStyle);
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
        "Pgn", "LoadPgn", "LeaveWhite", "LeaveBlack",
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
        CheckString(ui._ClockText(ChessArbiter.NoClock, 0), Text("clock_none"), "clock text: none");
        CheckString(ui._ClockText(300, 3), Clock(5, 3), "clock text: 5+3");
        CheckString(ui._ClockText(45, 0), TextWith("clock_seconds", new string[] { "seconds", "45", "increment", "0" }),
            "clock text: 45 s");

        board._ClearState();
        CheckButtons("JoinWhite JoinBlack", "reset board");
        CheckInt(ui._PressButton(ChessBoardUI.ButtonStart), ChessBoard.InputRejected, "reset board: Start refused");
        CheckInt(ui._PressButton(ChessBoardUI.ButtonReset), ChessBoard.InputRejected, "reset board: Reset refused");

        // The local player's lobby, White first.
        ui._PressJoinWhite();
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "Join White: in the lobby");
        CheckString(board.whitePlayer, localName, "Join White: seated as White");
        CheckButtons("JoinBlack LeaveWhite Mode Clock Reset", "lobby, White joined");
        CheckInt(ui._PressButton(ChessBoardUI.ButtonStart), ChessBoard.InputRejected,
            "lobby, White joined: Start refused");
        if (!board.IsModeForced()) {
            CheckInt(ui._PressButton(ChessBoardUI.ButtonMode), ChessBoard.InputAccepted, "Mode pressed");
            CheckInt(board.mode, ChessGame.ModeChess960, "Mode pressed: Chess960");
            CheckLabel(ChessBoardUI.ButtonMode, ModeLabel("mode_chess960"), "Mode pressed");
            ui._PressMode();
            CheckInt(board.mode, ChessGame.ModeStandard, "Mode pressed again: Standard");
            CheckLabel(ChessBoardUI.ButtonMode, ModeLabel("mode_standard"), "Mode pressed again");
        }
        if (!board.IsClockForced()) {
            CheckLabel(ChessBoardUI.ButtonClock, ClockLabel(Text("clock_none")), "lobby, no clock");
            CheckInt(ui._PressButton(ChessBoardUI.ButtonClock), ChessBoard.InputAccepted, "Clock pressed");
            CheckInt(board.baseTime * 1000 + board.increment, 60000, "Clock pressed: 1+0");
            CheckLabel(ChessBoardUI.ButtonClock, ClockLabel(Clock(1, 0)), "Clock pressed");
            ui._PressClock();
            CheckInt(board.baseTime * 1000 + board.increment, 180002, "Clock pressed again: 3+2");
            CheckLabel(ChessBoardUI.ButtonClock, ClockLabel(Clock(3, 2)), "Clock pressed again");
        }
        ui._PressJoinBlack();
        CheckString(board.blackPlayer, localName, "Join Black: seated as Black too");
        CheckButtons("LeaveWhite LeaveBlack Mode Clock Start Reset", "lobby, both seats");
        CheckLeaveEnds();
        CheckInt(ui._PressButton(ChessBoardUI.ButtonLeaveBlack), ChessBoard.InputAccepted, "Leave Black pressed");
        CheckString(board.whitePlayer + "|" + board.blackPlayer, localName + "|", "Leave Black: White kept");
        CheckButtons("JoinBlack LeaveWhite Mode Clock Reset", "lobby, Black left");
        ui._PressJoinBlack();
        CheckString(board.blackPlayer, localName, "Join Black again after leaving");
        int baseTime = board.baseTime;
        ui._PressStart();
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "Start: in game");
        CheckInt(board.baseTime, baseTime, "Start: with the clock chosen");
        CheckButtons(OwnGame, "own game");
        CheckInt(ui._PressButton(ChessBoardUI.ButtonJoinWhite), ChessBoard.InputRejected,
            "own game: Join White refused");
        CheckRefused(ChessBoardUI.ButtonLeaveWhite, "own game");
        CheckInt(board.ResignAs(ChessRules.White, localName), ChessBoard.InputAccepted, "own game: resigned");
        CheckButtons("Replay Reset", "own game ended");
        ui._PressReset();
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, "Reset: the board is reset");
        CheckButtons("JoinWhite JoinBlack", "after Reset");

        // Someone else's lobby and game: only Reset, for an organizer or once the board is idle.
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "others: Ana joined White");
        CheckButtons("JoinBlack Reset?", "others' lobby, Black open");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "others: Bob joined Black");
        CheckButtons("Reset?", "others' lobby");
        CheckInt(ui._PressButton(ChessBoardUI.ButtonMode), ChessBoard.InputRejected, "others' lobby: Mode refused");
        CheckInt(ui._PressButton(ChessBoardUI.ButtonClock), ChessBoard.InputRejected,
            "others' lobby: Clock refused");
        CheckInt(ui._PressButton(ChessBoardUI.ButtonStart), ChessBoard.InputRejected,
            "others' lobby: Start refused");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, "others: Ana started");
        CheckButtons("Replay? Reset?", "others' game");

        // A loaded game: both seats joined, the clock chosen, continued from ply 3.
        board._ClearState();
        Check(Utilities.IsValid(importer) && chess960Pgn != null, "buttons: importer and PGN assigned",
            "assign ViewTest's Importer and PGNs in the Inspector");
        if (Utilities.IsValid(importer) && chess960Pgn != null) {
            importer._StartImport(chess960Pgn.text, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
            CheckInt(importer._ImportFor(float.MaxValue), ChessPgn.ImportDone, "loaded game: imported");
            CheckInt(board._LoadImport(importer), ChessBoard.InputAccepted, "loaded game: loaded");
            CheckButtons("JoinWhite JoinBlack First Back Reset?", "loaded game");
            ui._PressJoinWhite();
            CheckButtons("JoinBlack LeaveWhite Clock First Back Reset", "loaded game, White joined");
            CheckInt(ui._PressButton(ChessBoardUI.ButtonContinue), ChessBoard.InputRejected,
                "loaded game, White joined: Continue refused");
            ui._PressJoinBlack();
            CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "loaded game, both seats: still loaded");
            CheckButtons("LeaveWhite LeaveBlack Clock Continue First Back Reset", "loaded game, both seats");
            CheckInt(ui._PressButton(ChessBoardUI.ButtonLeaveWhite), ChessBoard.InputAccepted,
                "loaded game: Leave White pressed");
            CheckString(board.whitePlayer + "|" + board.blackPlayer, "|" + localName, "loaded game: White left");
            CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "loaded game, White left: still loaded");
            CheckButtons("JoinWhite LeaveBlack Clock First Back Reset", "loaded game, White left");
            ui._PressJoinWhite();
            CheckInt(ui._PressButton(ChessBoardUI.ButtonMode), ChessBoard.InputRejected,
                "loaded game: Mode refused");
            if (!board.IsClockForced()) {
                CheckInt(ui._PressButton(ChessBoardUI.ButtonClock), ChessBoard.InputAccepted,
                    "loaded game: Clock pressed");
                CheckLabel(ChessBoardUI.ButtonClock, ClockLabel(ui._ClockText(board.baseTime, board.increment)),
                    "loaded game: Clock pressed");
            }
            CheckInt(board.StepToAs(localName, true, 3), ChessBoard.InputAccepted, "loaded game: stepped to ply 3");
            CheckButtons("LeaveWhite LeaveBlack Clock Continue First Back Forward Last Reset",
                "loaded game at ply 3");
            ui._PressContinue();
            CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "Continue: in game");
            CheckInt(board.moveCount, 3, "Continue: from ply 3");
            CheckButtons(OwnGame, "continued game");
        }

        board._ClearState();
        CheckButtons("JoinWhite JoinBlack", "cleared after the button checks");
        CheckView("cleared after the button checks");
        SendCustomEventDelayedFrames(nameof(RunGameButtonChecks), 1);
    }

    // ---- 9.4b: game and replay buttons ----

    // The buttons a seated player in both seats sees during their game, outside replay with nothing pending.
    const string OwnGame = "ResignWhite ResignBlack OfferDrawWhite? OfferDrawBlack? Replay Reset";
    // The same in replay, before the step buttons and Continue from here that apply.
    const string OwnReplay = "ResignWhite ResignBlack OfferDrawWhite? OfferDrawBlack? Leave Reset";

    // The ninth frame: on games of the local player against themselves, with takebacks, draws by agreement and
    // threefold claims on in the snapshot (SetRequestRules), resigning, draw offers (offered, declined, accepted), a
    // threefold claim, replay with each step button and Continue from here (13.3), and, against Bob, takeback requests
    // (requested, declined, accepted), each button shown exactly when it applies and pressing it doing what the board's
    // method does, a hidden one refused; then the same offers, requests and claim with those rules off, hidden and
    // refused; then someone else's game.
    public void RunGameButtonChecks() {
        // Resigning.
        board._ClearState();
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
        ui._PressResignBlack();
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, "Resign (Black): the game ended");
        CheckInt(board.ending, ChessGame.EndingResignation, "Resign (Black): by resignation");
        CheckInt(board.winner, ChessRules.White, "Resign (Black): White won");
        CheckButtons("Replay Reset", "resigned");
        CheckRefused(ChessBoardUI.ButtonResignWhite, "resigned");
        CheckRefused(ChessBoardUI.ButtonOfferDrawWhite, "resigned");

        // Draw offers.
        board._ClearState();
        StartGame(ChessGame.ModeStandard, "draw buttons");
        SetRequestRules(true);
        ui._PressOfferDrawWhite();
        CheckInt(board.drawOfferColor, ChessRules.White, "Offer draw (White): offered");
        CheckButtons("ResignWhite ResignBlack AcceptDrawBlack DeclineDrawBlack Replay Reset", "White's offer");
        CheckRefused(ChessBoardUI.ButtonOfferDrawBlack, "White's offer");
        CheckRefused(ChessBoardUI.ButtonAcceptDrawWhite, "White's offer");
        ui._PressDeclineDrawBlack();
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "Decline draw (Black): no offer");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "Decline draw (Black): still in game");
        CheckButtons(OwnGame, "offer declined");
        ui._PressOfferDrawBlack();
        CheckButtons("ResignWhite ResignBlack AcceptDrawWhite DeclineDrawWhite Replay Reset", "Black's offer");
        CheckLabel(ChessBoardUI.ButtonAcceptDrawWhite, Text("button_accept_draw"), "Black's offer");
        ui._PressAcceptDrawWhite();
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, "Accept draw (White): the game ended");
        CheckInt(board.ending, ChessGame.EndingDrawAgreement, "Accept draw (White): drawn by agreement");
        CheckButtons("Replay Reset", "draw accepted");

        // Replay and its steps, on 1. e4 e5 2. Nf3 Nc6.
        board._ClearState();
        StartGame(ChessGame.ModeStandard, "replay buttons");
        SetRequestRules(true);
        PlayMoves("e2e4 e7e5 g1f3 b8c6", "replay buttons");
        ui._PressEnterReplay();
        Check(board.replayMode, "Replay: in replay", "not in replay");
        CheckInt(board.replayIndex, 4, "Replay: at the current position");
        CheckButtons(OwnReplay + " First Back", "replay at ply 4");
        CheckRefused(ChessBoardUI.ButtonStepForward, "replay at ply 4");
        CheckRefused(ChessBoardUI.ButtonStepLast, "replay at ply 4");
        CheckRefused(ChessBoardUI.ButtonEnterReplay, "in replay");
        ui._PressStepBack();
        CheckInt(board.replayIndex, 3, "< : ply 3");
        Redraw();
        CheckView("< : ply 3");
        CheckButtons(OwnReplay + " First Back Forward Last Continue", "replay at ply 3");
        CheckRefused(ChessBoardUI.ButtonRequestTakebackWhite, "both seats");
        CheckRefused(ChessBoardUI.ButtonRequestTakebackBlack, "both seats");
        ui._PressStepFirst();
        CheckInt(board.replayIndex, 0, "<< : ply 0");
        Redraw();
        CheckView("<< : ply 0");
        CheckButtons(OwnReplay + " Forward Last Continue", "replay at ply 0");
        CheckRefused(ChessBoardUI.ButtonStepBack, "replay at ply 0");
        ui._PressStepForward();
        CheckInt(board.replayIndex, 1, "> : ply 1");
        ui._PressStepLast();
        CheckInt(board.replayIndex, 4, ">> : ply 4");
        Redraw();
        CheckView(">> : ply 4");
        ui._PressStepBack();
        ui._PressStepBack();
        CheckInt(board.replayIndex, 2, "< twice: ply 2");

        // Continue from here (13.3), from ply 2: the player in both seats needs no request.
        CheckLabel(ChessBoardUI.ButtonContinue, Text("button_continue"), "replay at ply 2");
        ui._PressContinue();
        CheckInt(board.moveCount, 2, "Continue (both seats): back to ply 2");
        Check(!board.replayMode, "Continue (both seats): out of replay", "in replay");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "Continue (both seats): in game");
        Redraw();
        CheckView("continued from ply 2");
        CheckButtons(OwnGame, "continued from ply 2");
        ui._PressEnterReplay();
        ui._PressStepFirst();
        ui._PressLeaveReplay();
        Check(!board.replayMode, "Back to game: out of replay", "in replay");
        CheckInt(board.moveCount, 2, "Back to game: the game unchanged");
        Redraw();
        CheckView("back to game");
        CheckButtons(OwnGame, "back to game");

        // Takeback requests between two players (the local player White, Bob Black), from ply 2 of the same game.
        board._ClearState();
        CheckInt(board.JoinAs(ChessRules.White, localName), ChessBoard.InputAccepted, "takeback buttons: joined White");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted,
            "takeback buttons: Bob joined Black");
        CheckInt(board.StartGameAs(localName), ChessBoard.InputAccepted, "takeback buttons: started");
        SetRequestRules(true);
        string[] twoPlayerMoves = { "e2e4", "e7e5", "g1f3", "b8c6" };
        for (int i = 0; i < twoPlayerMoves.Length; i++) {
            string move = twoPlayerMoves[i];
            CheckInt(board.PieceDroppedAs(i % 2 == 0 ? localName : "Bob", Sq(move.Substring(0, 2)),
                Sq(move.Substring(2, 2))), ChessBoard.InputAccepted, "takeback buttons: " + move + " played");
        }
        string twoReplay = "ResignWhite OfferDrawWhite? Leave First Back Forward Last Reset";
        ui._PressEnterReplay();
        ui._PressStepBack();
        ui._PressStepBack();
        CheckInt(board.replayIndex, 2, "takeback buttons: ply 2");
        CheckButtons(twoReplay + " TakebackWhite", "two players at ply 2");
        CheckRefused(ChessBoardUI.ButtonContinue, "two players");
        CheckLabel(ChessBoardUI.ButtonRequestTakebackWhite, Text("button_take_back_to_here"),
            "two players at ply 2");
        ui._PressRequestTakebackWhite();
        CheckInt(board.takebackColor, ChessRules.White, "Take back (White): requested");
        CheckInt(board.takebackPly, 2, "Take back (White): for ply 2");
        CheckButtons(twoReplay, "White's request");
        CheckRefused(ChessBoardUI.ButtonAcceptTakebackWhite, "White's request");
        CheckRefused(ChessBoardUI.ButtonAcceptTakebackBlack, "White's request, Bob's seat");
        CheckInt(board.DeclineTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted,
            "Decline takeback (Bob): declined");
        CheckButtons(twoReplay + " TakebackWhite", "White's request declined");
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted,
            "Take back (Bob): requested");
        CheckButtons(twoReplay + " AcceptTakebackWhite DeclineTakebackWhite", "Bob's request");
        ui._PressDeclineTakebackWhite();
        CheckInt(board.takebackColor, ChessRules.NoColor, "Decline takeback (White): no request");
        CheckInt(board.moveCount, 4, "Decline takeback (White): the game unchanged");
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted,
            "Take back (Bob): requested again");
        ui._PressAcceptTakebackWhite();
        CheckInt(board.moveCount, 2, "Accept takeback (White): back to ply 2");
        Check(!board.replayMode, "Accept takeback (White): out of replay", "in replay");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "Accept takeback (White): in game");
        Redraw();
        CheckView("takeback accepted");
        CheckButtons("ResignWhite OfferDrawWhite? Replay Reset", "takeback accepted");

        // A threefold repetition claim: the knights out and back twice.
        board._ClearState();
        StartGame(ChessGame.ModeStandard, "claim button");
        SetRequestRules(true);
        PlayMoves("g1f3 g8f6 f3g1 f6g8 g1f3 g8f6 f3g1", "claim button");
        CheckButtons(OwnGame, "claim button: twice repeated");
        PlayMoves("f6g8", "claim button");
        CheckButtons("ResignWhite ResignBlack OfferDrawWhite OfferDrawBlack ClaimDraw Replay Reset",
            "claim button: three times");
        CheckLabel(ChessBoardUI.ButtonClaimDraw, Text("button_claim_draw_threefold"), "claim button: three times");
        ui._PressClaimDraw();
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, "Claim draw: the game ended");
        CheckInt(board.ending, ChessGame.EndingThreefoldRepetition, "Claim draw: by threefold repetition");
        CheckButtons("Replay Reset", "draw claimed");

        // The same with takebacks, draws by agreement and threefold claims off.
        board._ClearState();
        StartGame(ChessGame.ModeStandard, "rules off");
        SetRequestRules(false);
        CheckButtons("ResignWhite ResignBlack Replay Reset", "rules off: own game");
        CheckRefused(ChessBoardUI.ButtonOfferDrawWhite, "rules off");
        PlayMoves("g1f3 g8f6 f3g1 f6g8 g1f3 g8f6 f3g1 f6g8", "rules off");
        CheckButtons("ResignWhite ResignBlack Replay Reset", "rules off: three times");
        CheckRefused(ChessBoardUI.ButtonClaimDraw, "rules off");
        ui._PressEnterReplay();
        ui._PressStepBack();
        CheckButtons("ResignWhite ResignBlack Leave First Back Forward Last Reset", "rules off: replay at ply 7");
        CheckRefused(ChessBoardUI.ButtonRequestTakebackWhite, "rules off");
        CheckRefused(ChessBoardUI.ButtonContinue, "rules off");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "rules off: still in game");

        // Someone else's game: none of the players' buttons.
        board._ClearState();
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

        board._ClearState();
        CheckButtons("JoinWhite JoinBlack", "cleared after the game button checks");
        CheckView("cleared after the game button checks");
        SendCustomEventDelayedFrames(nameof(RunHighlightChecks), 1);
    }

    // ---- 9.5a: highlights, and the promotion ghosts (12.11, in place of 9.5a's picker) ----

    // A black pawn about to promote on e1, a white one on a7 and a black one on h2 (the ghosts' row can't be centred
    // over the a and h files).
    const string BlackPromotionFen = "k7/8/8/8/8/8/4p3/K7 b - - 0 1";
    const string APawnPromotionFen = "4k3/P7/8/8/8/8/8/4K3 w - - 0 1";
    const string BlackHPawnPromotionFen = "4k3/8/8/8/8/8/7p/4K3 b - - 0 1";
    // The king already on g1 with its rook on h1: castling moves only the rook.
    const string KingStaysFen = "6k1/8/8/8/8/8/8/6KR w K - 0 1";
    // A knight on e4 in front of its rook: Nd6 gives a double check (the knight, and the rook it uncovers).
    const string DoubleCheckFen = "4k3/8/8/8/4N3/8/8/4RK2 w - - 0 1";
    // A pawn on e5 that can take a knight on f6 or the pawn on d5 en passant, or move to e6.
    const string CaptureMarksFen = "4k3/8/5n2/3pP3/8/8/8/4K3 w - d6 0 1";
    // The king on f1 with its rook on g1, the king's destination: castling swaps them.
    const string RookOnDestinationFen = "k7/8/8/8/8/8/8/5KR1 w K - 0 1";

    // The tenth frame: the promotion ghosts, highlights and marks wired; the last-move line and mark after moves
    // (castling on the rook showing the king's path, or the rook's when the king stays), in replay and on a cleared
    // board; the check lines (step 12.7) of a check, a double check and a checkmate, in replay and a custom position,
    // and gone once answered; the legal-target marks of a held piece (pickups) and of a selected one (clicks), each of
    // its kind (a capture, a castle's rook square, an empty square), none in replay, after the drop or once the game
    // ends; the promotion ghosts (step 12.11) floating over the pawn for a drop and for a placing click, for both
    // colours, in order from the promoting side's left, each wearing its promotion template and promoting to its
    // piece when clicked, nothing else a pickup or clickable meanwhile, and hidden when the board changes. The input
    // style is put back afterwards.
    public void RunHighlightChecks() {
        int savedStyle = view.inputStyle;
        int ghostsReady = 0;
        for (int i = 0; i < view.promotionGhosts.Length; i++) {
            ChessPromotionGhost ghost = view.promotionGhosts[i];
            if (Utilities.IsValid(ghost) && !ghost.gameObject.activeSelf
                && ghost.pieceType == ChessBoardView._GhostType(i)) {
                ghostsReady++;
            }
        }
        CheckInt(ghostsReady, ChessBoardView.GhostCount,
            "highlights: the promotion ghosts assigned (knight, bishop, rook, queen), hidden");
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
            if (Utilities.IsValid(mark) && view._DropSquare(mark.transform.position) == square) marks++;
        }
        CheckInt(marks, 64, "highlights: every square has its target mark, over that square");
        Check(Utilities.IsValid(view.targetMaterial) && Utilities.IsValid(view.captureMaterial)
            && view.targetMaterial != view.captureMaterial,
            "highlights: the target and capture materials assigned, different", "missing or the same");

        // The last move.
        string g = "last move";
        board._ClearState();
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
        view._SetInputStyle(ChessBoardView.InputPickups);
        int replayPawn = view.squareObject[Sq("e2")];
        view._OnPiecePickedUp(replayPawn);
        CheckInt(view.heldObject, replayPawn, g + ": e2 picked up in replay");
        CheckTargets("", g + ": e2 held in replay");
        view._OnPieceDropped(replayPawn);
        view._SetInputStyle(ChessBoardView.InputClicks);
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
        board._ClearState();
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

        board._ClearState();
        StartGame(ChessGame.ModeStandard, k + " (mate)");
        PlayMoves("e2e4 e7e5 d1h5 b8c6 f1c4 g8f6 h5f7", k + " (mate)");
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, k + " (mate): Qxf7# ends the game");
        CheckCheckLines("f7e8", k + " (mate): the checkmate's line stays");

        // Target marks of a held piece.
        string t = "held targets";
        view._SetInputStyle(ChessBoardView.InputPickups);
        board._ClearState();
        StartGame(ChessGame.ModeStandard, t);
        CheckTargets("", t + ": nothing held");
        int pawn = view.squareObject[Sq("e2")];
        view._OnPiecePickedUp(pawn);
        CheckTargets("e3 e4", t + ": e2 held");
        Redraw();
        CheckTargets("e3 e4", t + ": e2 held, refreshed");
        view.pieces[pawn].localPosition = Over("e4");
        view._OnPieceDropped(pawn);
        CheckTargets("", t + ": e2 dropped over e4");
        Redraw();
        CheckLastMove("e2", "e4", t + ": e4 by a drop");
        int knight = view.squareObject[Sq("g8")];
        view._OnPiecePickedUp(knight);
        CheckTargets("f6 h6", t + ": g8 held");
        view._OnPiecePickedUp(view.squareObject[Sq("d2")]);
        CheckTargets("", t + ": d2 picked up out of turn instead");
        Redraw();
        view._OnPiecePickedUp(knight);
        CheckTargets("f6 h6", t + ": g8 held again");
        CheckInt(board.ResignAs(ChessRules.Black, localName), ChessBoard.InputAccepted, t + ": Black resigned");
        Redraw();
        CheckTargets("", t + ": the game ended while g8 was held");
        if (view.heldObject == knight) view._OnPieceDropped(knight);

        CastleGame(StandardCastlingFen, t + " (castling)");
        int king = view.squareObject[Sq("e1")];
        view._OnPiecePickedUp(king);
        CheckTargets("d1 f1 d2 e2 f2 oa1 c1 g1 oh1", t + " (castling): e1 held");
        view.pieces[king].localPosition = Over("h1");
        view._OnPieceDropped(king);
        CheckTargets("", t + " (castling): dropped over h1");
        Redraw();
        CheckLastMove("e1", "g1", t + " (castling): O-O by a drop");

        // Captures (en passant marking the square the pawn moves to), and a castle whose rook stands on the king's
        // destination: one full-size yellow mark there.
        CastleGame(CaptureMarksFen, t + " (captures)");
        int capturing = view.squareObject[Sq("e5")];
        view._OnPiecePickedUp(capturing);
        CheckTargets("e6 xd6 xf6", t + " (captures): e5 held");
        view._OnPieceDropped(capturing);
        Redraw();
        CastleGame(RookOnDestinationFen, t + " (rook on g1)");
        int castlingKing = view.squareObject[Sq("f1")];
        view._OnPiecePickedUp(castlingKing);
        CheckTargets("e1 e2 f2 g2 og1", t + " (rook on g1): f1 held");
        view._OnPieceDropped(castlingKing);
        Redraw();

        // Target marks of a selected piece.
        string s = "selected targets";
        view._SetInputStyle(ChessBoardView.InputClicks);
        board._ClearState();
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

        // The promotion ghosts (step 12.11), for a drop.
        string p = "ghosts";
        view._SetInputStyle(ChessBoardView.InputPickups);
        CastleGame(PromotionFen, p);
        CheckGhosts(false, "", p + ": set up");
        int promoting = view.squareObject[Sq("e7")];
        CheckInt(ViewDrop("e7", Over("e8")), ChessBoard.InputAccepted, p + ": e7 picked up");
        CheckGhosts(true, "e8", p + ": e7 dropped over e8");
        CheckPickupable(0, p + ": e7 dropped over e8, no pickups");
        Redraw();
        CheckGhosts(true, "e8", p + ": e7 dropped over e8, refreshed");
        CheckPickupable(0, p + ": the ghosts shown");
        CheckInt(view._ChoosePromotion(ChessRules.NoPieceType), ChessBoard.InputRejected,
            p + ": no cancel: a choice of no piece refused");
        CheckGhosts(true, "e8", p + ": still waiting after the refused choice");
        ClickGhost(ChessRules.Queen, p + ": the queen clicked");
        CheckGhosts(false, "", p + ": the queen clicked");
        CheckPickupableColor(2, ChessRules.White, p + ": the ghosts gone, e8=Q not shown yet: White's");
        CheckDropped(1, ChessBoard.ChangeMove, p + ": e8=Q");
        CheckPickupableColor(1, ChessRules.Black, p + ": e8=Q shown: Black's king");
        CheckInt(view.objectType[promoting], ChessRules.Queen, p + ": e8=Q: the pawn's object is a queen");
        CheckLastMove("e7", "e8", p + ": e8=Q");

        CastleGame(PromotionFen, p + " (change)");
        ViewDrop("e7", Over("e8"));
        CheckGhosts(true, "e8", p + " (change): e7 dropped over e8");
        CheckInt(board.ResignAs(ChessRules.Black, localName), ChessBoard.InputAccepted, p + " (change): Black resigned");
        CheckGhosts(false, "", p + " (change): the game ended meanwhile");
        CheckInt(view._ChoosePromotion(ChessRules.Queen), ChessBoard.InputRejected,
            p + " (change): a choice afterwards refused");

        CastleGame(BlackPromotionFen, p + " (Black)");
        int blackPawn = view.squareObject[Sq("e2")];
        ViewDrop("e2", Over("e1"));
        CheckGhosts(true, "e1", p + " (Black): e2 dropped over e1");
        CheckPickupable(0, p + " (Black): the ghosts shown");
        ClickGhost(ChessRules.Knight, p + " (Black): the knight clicked");
        CheckDropped(1, ChessBoard.ChangeMove, p + " (Black): e1=N");
        CheckInt(view.objectType[blackPawn], ChessRules.Knight, p + " (Black): e1=N: the pawn's object is a knight");

        // The promotion ghosts, for a placing click.
        view._SetInputStyle(ChessBoardView.InputClicks);
        CastleGame(PromotionFen, p + " (clicks)");
        int clickPawn = view.squareObject[Sq("e7")];
        ClickPiece("e7");
        ClickSquare("e8");
        CheckGhosts(true, "e8", p + " (clicks): e8 clicked");
        CheckClickables("", "", p + " (clicks): the ghosts shown");
        ClickGhost(ChessRules.Rook, p + " (clicks): the rook clicked");
        CheckDropped(1, ChessBoard.ChangeMove, p + " (clicks): e8=R");
        CheckClickables("a8", "", p + " (clicks): e8=R, the ghosts gone");
        CheckInt(view.objectType[clickPawn], ChessRules.Rook, p + " (clicks): e8=R: the pawn's object is a rook");

        CastleGame(APawnPromotionFen, p + " (a file)");
        int aPawn = view.squareObject[Sq("a7")];
        ClickPiece("a7");
        ClickSquare("a8");
        CheckGhosts(true, "a8", p + " (a file): a8 clicked");
        ClickGhost(ChessRules.Bishop, p + " (a file): the bishop clicked");
        CheckDropped(1, ChessBoard.ChangeMove, p + " (a file): a8=B");
        CheckInt(view.objectType[aPawn], ChessRules.Bishop, p + " (a file): a8=B: the pawn's object is a bishop");

        CastleGame(BlackHPawnPromotionFen, p + " (Black, h file)");
        int hPawn = view.squareObject[Sq("h2")];
        ClickPiece("h2");
        ClickSquare("h1");
        CheckGhosts(true, "h1", p + " (Black, h file): h1 clicked");
        CheckClickables("", "", p + " (Black, h file): the ghosts shown");
        ClickGhost(ChessRules.Queen, p + " (Black, h file): the queen clicked");
        CheckDropped(1, ChessBoard.ChangeMove, p + " (Black, h file): h1=Q");
        CheckInt(view.objectType[hPawn], ChessRules.Queen, p + " (Black, h file): h1=Q: the pawn's object is a queen");

        board._ClearState();
        view._SetInputStyle(savedStyle);
        Redraw();
        CheckLastMove("", "", "cleared after the highlight checks");
        CheckTargets("", "cleared after the highlight checks");
        CheckGhosts(false, "", "cleared after the highlight checks");
        CheckView("cleared after the highlight checks");
        RunBlockerChecks();
        SendCustomEventDelayedFrames(nameof(RunAnimationChecks), 1);
    }

    // ---- 9.5a2: walking through the board ----

    // Run straight after the highlight checks: nothing a player walks into is solid but the blocker (the click
    // targets, the promotion ghosts and the UI canvases are triggers), which is enabled exactly while the local player
    // has no seat.
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
        CheckInt(solid, 0, "the button panels have trigger colliders: solid or missing");
        solid = 0;
        for (int i = 0; i < view.promotionGhosts.Length; i++) {
            ChessPromotionGhost ghost = view.promotionGhosts[i];
            if (!Utilities.IsValid(ghost) || !IsTrigger(ghost.transform)) solid++;
        }
        CheckInt(solid, 0, "the promotion ghosts have trigger colliders: solid or missing");
        if (!Utilities.IsValid(blocker)) return;

        board._ClearState();
        Redraw();
        Check(blocker.enabled, "blocker: enabled on a reset board", "disabled");
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "blocker: Ana joined White");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "blocker: Bob joined Black");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, "blocker: Ana and Bob started");
        Redraw();
        Check(blocker.enabled, "blocker: enabled in others' game", "disabled");

        board._ClearState();
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

        board._ClearState();
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
        view._SetInputStyle(ChessBoardView.InputPickups);
        string a = "animation";
        board._ClearState();
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
        Check(!view.animating[queen] && (view.pieces[queen].localPosition - view._SquarePosition(Sq("d2"))).sqrMagnitude
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
            board._ClearState();
            importer._StartImport(chess960Pgn.text, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
            CheckInt(importer._ImportFor(float.MaxValue), ChessPgn.ImportDone, a + ": chess960.pgn imported");
            CheckInt(board._LoadImport(importer), ChessBoard.InputAccepted, a + ": chess960.pgn loaded");
            Redraw();
            CheckAtRest(a + ": chess960.pgn loaded");
        }

        board._ClearState();
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
        board._ClearState();
        view._SetInputStyle(savedAnimationStyle);
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

    // The twelfth frame: the status panels wired; _ClockDisplay; the players' and status texts (and the panels showing
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
        CheckString(ChessBoardUI._ClockDisplay(300000), "5:00", s + ": clock 5:00");
        CheckString(ChessBoardUI._ClockDisplay(299999), "4:59", s + ": clock 4:59.999");
        CheckString(ChessBoardUI._ClockDisplay(61000), "1:01", s + ": clock 1:01");
        CheckString(ChessBoardUI._ClockDisplay(10000), "0:10", s + ": clock 0:10");
        CheckString(ChessBoardUI._ClockDisplay(9876), "0:09.8", s + ": clock 0:09.876");
        CheckString(ChessBoardUI._ClockDisplay(0), "0:00.0", s + ": clock 0");
        CheckString(ChessBoardUI._ClockDisplay(3600000), "1:00:00", s + ": clock an hour");
        CheckString(ChessBoardUI._ClockDisplay(3725000), "1:02:05", s + ": clock 1:02:05");

        // Two others' lobby and game, with a clock.
        board._ClearState();
        CheckStatus(Players(null, null), Text("status_reset"), s + ": reset board");
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": Ana joined White");
        Unforce();
        CheckStatus(Players("Ana", null), Lobby("mode_standard", Text("clock_none")), s + ": lobby");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob joined Black");
        CheckInt(board.SetClockAs("Ana", 300, 3), ChessBoard.InputAccepted, s + ": 5+3 chosen");
        CheckInt(board.SetModeAs("Bob", ChessGame.ModeChess960), ChessBoard.InputAccepted, s + ": Chess960 chosen");
        CheckStatus(Players("Ana", "Bob"), Lobby("mode_chess960", Clock(5, 3)), s + ": lobby, both seats");
        CheckInt(board.SetModeAs("Bob", ChessGame.ModeStandard), ChessBoard.InputAccepted, s + ": Standard chosen");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, s + ": started");
        string toMove = ColorLine("status_to_move", ChessRules.White);
        CheckStatus(PlayersWithClocks("Ana", "5:00", "Bob", "5:00"), toMove, s + ": game started");
        double start = board.turnStart;
        CheckString(ui._PlayersTextAt(start + 61.5), PlayersWithClocks("Ana", "3:58", "Bob", "5:00"),
            s + ": a minute on");
        CheckString(ui._StatusTextAt(start + 295.1), toMove, s + ": 4.9 s left");
        CheckString(ui._PlayersTextAt(start + 295.1), PlayersWithClocks("Ana", "0:04.9", "Bob", "5:00"),
            s + ": 4.9 s left");
        CheckString(ui._StatusTextAt(start + 301), ColorLine("status_time_up", ChessRules.White), s + ": flag fallen");
        CheckString(ui._PlayersTextAt(start + 301), PlayersWithClocks("Ana", "0:00.0", "Bob", "5:00"),
            s + ": flag fallen");
        CheckInt(board.PieceDroppedAs("Ana", Sq("e2"), Sq("e4")), ChessBoard.InputAccepted, s + ": e4");
        CheckInt(board.PieceDroppedAs("Bob", Sq("f7"), Sq("f6")), ChessBoard.InputAccepted, s + ": f6");
        CheckInt(board.PieceDroppedAs("Ana", Sq("d1"), Sq("h5")), ChessBoard.InputAccepted, s + ": Qh5+");
        string check = ColorLine("status_to_move_in_check", ChessRules.Black);
        CheckStatus(null, check, s + ": check");
        SetRequestRules(true);
        CheckInt(board.OfferDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob offered a draw");
        string offer = check + "\n" + ColorLine("status_draw_offer", ChessRules.Black);
        CheckStatus(null, offer, s + ": draw offer");
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, s + ": replay");
        CheckStatus(null, offer + "\n" + ReplayLine("2.", "Qh5+", 3, 3), s + ": replay");
        CheckInt(board.StepToAs("Ana", false, 1), ChessBoard.InputAccepted, s + ": stepped to ply 1");
        CheckInt(board.RequestTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": Ana asked");
        CheckStatus(null, offer + "\n" + ColorLine("status_takeback_to_here", ChessRules.White) + "\n"
            + ReplayLine("1.", "e4", 1, 3), s + ": takeback request");
        CheckInt(board.StepToAs("Ana", false, 0), ChessBoard.InputAccepted, s + ": stepped to the start");
        CheckStatus(null, offer + "\n" + ReplayStart(3), s + ": request cancelled by stepping");
        CheckInt(board.PieceDroppedAs("Ana", Sq("a2"), Sq("a4")), ChessBoard.InputAccepted, s + ": a free move");
        CheckStatus(null, offer + "\n" + Text("replay_custom"), s + ": free move");
        CheckInt(board.LeaveReplayAs("Ana", false), ChessBoard.InputAccepted, s + ": back to the game");
        CheckInt(board.ResignAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob resigned");
        CheckStatus(null, Ending("ending_resignation_white", "1-0"), s + ": resigned");
        CheckString(ui._PlayerTextAt(ChessRules.Black, board._ServerTime() + 60),
            ui._PlayerTextAt(ChessRules.Black, board._ServerTime()), s + ": the clocks stopped");

        // Every ending's text, set on the ended game (Black was to move).
        CheckEnding(ChessGame.EndingCheckmate, ChessRules.White, Ending("ending_checkmate_white", "1-0"));
        CheckEnding(ChessGame.EndingCheckmate, ChessRules.Black, Ending("ending_checkmate_black", "0-1"));
        CheckEnding(ChessGame.EndingResignation, ChessRules.Black, Ending("ending_resignation_black", "0-1"));
        CheckEnding(ChessGame.EndingTimeForfeit, ChessRules.White, Ending("ending_time_forfeit_white", "1-0"));
        CheckEnding(ChessGame.EndingTimeForfeit, ChessRules.NoColor, TextWith("status_draw_time_forfeit",
            new string[] { "flagged", ColorName(ChessRules.Black), "other", ColorName(ChessRules.White),
                "result", "1/2-1/2" }));
        CheckEnding(ChessGame.EndingStalemate, ChessRules.NoColor, Ending("ending_stalemate", "1/2-1/2"));
        CheckEnding(ChessGame.EndingDrawAgreement, ChessRules.NoColor, Ending("ending_draw_agreement", "1/2-1/2"));
        CheckEnding(ChessGame.EndingThreefoldRepetition, ChessRules.NoColor,
            Ending("ending_threefold_repetition", "1/2-1/2"));
        CheckEnding(ChessGame.EndingFivefoldRepetition, ChessRules.NoColor,
            Ending("ending_fivefold_repetition", "1/2-1/2"));
        CheckEnding(ChessGame.EndingFiftyMoveRule, ChessRules.NoColor, Ending("ending_fifty_move_rule", "1/2-1/2"));
        CheckEnding(ChessGame.EndingSeventyFiveMoveRule, ChessRules.NoColor,
            Ending("ending_seventy_five_move_rule", "1/2-1/2"));
        CheckEnding(ChessGame.EndingInsufficientMaterial, ChessRules.NoColor,
            Ending("ending_insufficient_material", "1/2-1/2"));
        CheckEnding(ChessGame.EndingKingCaptured, ChessRules.Black, Ending("ending_king_captured_black", "0-1"));
        CheckEnding(ChessGame.EndingUnknown, ChessRules.White, Ending("ending_white_wins", "1-0"));
        CheckEnding(ChessGame.EndingUnknown, ChessRules.NoColor, Ending("ending_draw", "1/2-1/2"));
        CheckEnding(ChessGame.EndingNone, ChessRules.NoColor, "");

        // Playing on untimed after Black's flag fell (12.9): the timed game's times, GG for the side out of time.
        board.ending = ChessGame.EndingNone;
        board.winner = ChessRules.NoColor;
        board.lifecycle = ChessBoard.LifecycleInGame;
        board.baseTime = ChessArbiter.NoClock;
        board.increment = 0;
        board.untimedAfterFlagFall = true;
        board.whiteTime = 59000;
        board.blackTime = 0;
        double later = board._ServerTime() + 100;
        string flagFallen = Text("player_flag_fallen");
        Check(ui._PlayerTextAt(ChessRules.White, later).EndsWith("  0:59"), s + ": play-on: White's time kept",
            ui._PlayerTextAt(ChessRules.White, later));
        Check(ui._PlayerTextAt(ChessRules.Black, later).EndsWith("  " + flagFallen), s + ": play-on: GG for Black",
            ui._PlayerTextAt(ChessRules.Black, later));
        Check(ui._StatusTextAt(later).Contains(ColorLine("status_playing_on", ChessRules.Black)),
            s + ": play-on: the status says so", ui._StatusTextAt(later));
        board.untimedAfterFlagFall = false;
        Check(!ui._PlayerTextAt(ChessRules.Black, later).Contains(flagFallen), s + ": no GG without play-on",
            ui._PlayerTextAt(ChessRules.Black, later));

        // A stalemate and a checkmate played on the local player's games, with no clock.
        board._ClearState();
        StartUnforced(s + ": stalemate game");
        PlayMoves(LoydStalemate, s + ": stalemate game");
        CheckInt(board.ending, ChessGame.EndingStalemate, s + ": stalemated");
        CheckStatus(Players(localName, localName), Ending("ending_stalemate", "1/2-1/2"), s + ": stalemate");
        board._ClearState();
        StartUnforced(s + ": checkmate game");
        PlayMoves(FoolsMate, s + ": checkmate game");
        string mate = Ending("ending_checkmate_black", "0-1");
        CheckStatus(Players(localName, localName), mate, s + ": checkmate");
        CheckInt(board.EnterReplayAs(localName, false), ChessBoard.InputAccepted, s + ": the mate in replay");
        CheckInt(board.StepToAs(localName, false, 2), ChessBoard.InputAccepted, s + ": stepped to ply 2");
        CheckStatus(null, mate + "\n" + ReplayLine("1...", "e5", 2, 4), s + ": the mate in replay");

        // Loaded games (the names are the PGNs' White and Black tags).
        string unfinished = Text("status_unfinished_game");
        if (LoadPgn(chess960Pgn, s + ": chess960.pgn")) {
            string loaded = Loaded("status_loaded_chess960") + "\n" + unfinished + "\n";
            CheckStatus(Players(null, null), loaded + ReplayLine("3...", "O-O-O", 6, 6), s + ": chess960.pgn loaded");
            CheckInt(board.StepToAs(localName, true, 0), ChessBoard.InputAccepted, s + ": chess960.pgn at the start");
            CheckStatus(null, loaded + ReplayStart(6), s + ": chess960.pgn at the start");
            CheckInt(board.JoinAs(ChessRules.White, localName), ChessBoard.InputAccepted, s + ": joined the loaded game");
            CheckStatus(Players(localName, null), loaded + ReplayStart(6), s + ": the loaded game joined");
        }
        if (LoadPgn(promotionsPgn, s + ": promotions.pgn")) {
            CheckInt(board.StepToAs(localName, true, 1), ChessBoard.InputAccepted, s + ": promotions.pgn at ply 1");
            CheckStatus(null, Loaded("status_loaded") + "\n" + unfinished + "\n" + ReplayLine("40...", "gxh1=Q+", 1, 9),
                s + ": promotions.pgn at ply 1");
        }

        board._ClearState();
        CheckStatus(Players(null, null), Text("status_reset"), s + ": cleared");
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
        Check(wired, s + ": box and field assigned", "the Chess Board UI prefab has them");
        if (!wired) {
            LogSummary();
            return;
        }
        Check(ui.pgnField.transform.IsChildOf(ui.pgnBox.transform), s + ": the field in the box", "it isn't");
        board._ClearState();
        Redraw();
        CheckPgnBox(false, s + ": reset board");
        CheckInt(ui._PressButton(ChessBoardUI.ButtonPgn), ChessBoard.InputRejected, s + ": reset board: refused");
        CheckPgnBox(false, s + ": reset board, refused");

        StartUnforced(s);
        CheckButtons(OwnGame, s + ": no moves yet");
        CheckInt(ui._PressButton(ChessBoardUI.ButtonPgn), ChessBoard.InputRejected, s + ": no moves yet: refused");
        PlayOnBoard("e2", "e4", s);
        PlayOnBoard("e7", "e5", s);
        CheckButtons(OwnGame + " Pgn", s + ": 1. e4 e5");
        CheckLabel(ChessBoardUI.ButtonPgn, Text("button_pgn"), s + ": closed");
        ui._PressPgn();
        CheckPgnBox(true, s + ": opened");
        CheckLabel(ChessBoardUI.ButtonPgn, Text("button_hide_pgn"), s + ": opened");
        string text = ui.pgnField.text;
        Check(text.Contains("[White \"" + localName + "\"]") && text.EndsWith("\n\n1. e4 e5 *\n"),
            s + ": opened: the tags and 1. e4 e5 *", text);
        bool white = ui._LocalEnd() == ChessRules.White;
        Check(ui.pgnBox.transform.localPosition.z > 0 == white, s + ": opened: beyond the board from the local end ("
            + (white ? "White" : "Black") + ")", "at " + ui.pgnBox.transform.localPosition.ToString("F3"));
        PlayOnBoard("g1", "f3", s);
        CheckPgnBox(true, s + ": after Nf3");
        text = ui.pgnField.text;
        Check(text.EndsWith("1. e4 e5 2. Nf3 *\n"), s + ": after Nf3: the move added", text);

        // Facing either end, over the far edge of the board from it.
        Vector3 placed = ui.pgnBox.transform.localPosition;
        ui._PlacePgnBox(ChessRules.Black);
        Transform box = ui.pgnBox.transform;
        Check(box.localPosition.z < 0 && Mathf.Abs(box.localPosition.z) == Mathf.Abs(placed.z)
            && box.localPosition.y == placed.y
            && Quaternion.Angle(box.localRotation, Quaternion.Euler(0, 180, 0)) < 0.01f,
            s + ": for Black: on White's side, facing Black", "at " + box.localPosition.ToString("F3") + ", turned "
            + box.localEulerAngles.ToString("F1"));
        ui._PlacePgnBox(ChessRules.White);
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
        ui._PressPgn();
        CheckPgnBox(false, s + ": closed");
        CheckLabel(ChessBoardUI.ButtonPgn, Text("button_pgn"), s + ": closed again");
        ui._PressPgn();
        CheckPgnBox(true, s + ": opened again");
        ui._PressReset();
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, s + ": reset");
        Redraw();
        CheckPgnBox(false, s + ": reset");
        StartUnforced(s + ": next game");
        PlayOnBoard("d2", "d4", s + ": next game");
        CheckPgnBox(false, s + ": next game, 1. d4");

        // A loaded game: its own tags.
        if (LoadPgn(chess960Pgn, s + ": chess960.pgn")) {
            Redraw();
            ui._PressPgn();
            CheckPgnBox(true, s + ": chess960.pgn loaded");
            text = ui.pgnField.text;
            Check(text.Contains("[Variant \"Chess960\"]") && text.Contains("[SetUp \"1\"]"),
                s + ": chess960.pgn loaded: a Chess960 PGN", text);
        }

        board._ClearState();
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
            "the Chess Board UI prefab has them");
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
        // TextMeshPro fields (12.17): the copy field read-only, the paste field editable. Udon can't read their line
        // type; LegacyTextCheck checks both are multi-line.
        Check(ui.pgnField.readOnly && !ui.pgnPasteField.readOnly, s + ": only the copy field read-only",
            "copy " + (ui.pgnField.readOnly ? "read-only" : "editable") + ", paste "
            + (ui.pgnPasteField.readOnly ? "read-only" : "editable"));
        savedImporter = board.importer;
        board.importer = importer;

        board._ClearState();
        Redraw();
        CheckButtons("JoinWhite JoinBlack LoadPgn", s + ": reset board");
        CheckLabel(ChessBoardUI.ButtonLoadPgn, Text("button_load_pgn"), s + ": closed");
        ui._PressLoadPgn();
        CheckPasteBox(true, true, "", s + ": opened");
        CheckLabel(ChessBoardUI.ButtonLoadPgn, Text("button_hide_pgn"), s + ": opened");
        bool white = ui._LocalEnd() == ChessRules.White;
        Check(box.localPosition.z > 0 == white, s + ": opened: beyond the board from the local end",
            "at " + box.localPosition.ToString("F3"));
        ui._PressLoadPgn();
        CheckPasteBox(false, false, "", s + ": closed by the button");
        CheckLabel(ChessBoardUI.ButtonLoadPgn, Text("button_load_pgn"), s + ": closed again");
        ui._PressLoadPgn();
        ui._PressPgnClose();
        CheckPasteBox(false, false, "", s + ": closed by Close");
        CheckInt(ui._ImportPasted(), ChessBoard.InputRejected, s + ": closed: Import refused");

        // Nothing pasted: refused, and the box says so.
        ui._PressLoadPgn();
        ui.pgnPasteField.text = "  \n";
        int status = board.importStatus;
        CheckInt(ui._ImportPasted(), ChessBoard.InputRejected, s + ": nothing pasted: refused");
        CheckPasteBox(true, true, Text("import_paste_first"), s + ": nothing pasted");
        CheckInt(board.importStatus, status, s + ": nothing pasted: no import");

        // Siggi-vs-Nuka over the next frames, run by the importer's Update.
        ui.pgnPasteField.text = nuka.text;
        CheckInt(ui._ImportPasted(), ChessBoard.InputAccepted, s + ": Siggi-vs-Nuka: started");
        CheckInt(board.importStatus, ChessBoard.ImportRunning, s + ": Siggi-vs-Nuka: under way");
        CheckPasteBox(true, false, ImportRunning(0), s + ": Siggi-vs-Nuka: started");
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
            // A running import's line (import_running, up to the count) with a count above 0.
            string label = ui.pgnImportLabel.text;
            string running = Text("import_running");
            int count = running.IndexOf("${");
            if (count >= 0 && label.StartsWith(running.Substring(0, count)) && label != ImportRunning(0)) {
                progressSeen = true;
            }
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
        ui._PressPgn();
        CheckPgnBox(true, n + ": PGN opens the copy part");
        Check(ui.pgnCopyPart.activeSelf && !ui.pgnPastePart.activeSelf, n + ": PGN: the copy part shown",
            "copy " + ui.pgnCopyPart.activeSelf + ", paste " + ui.pgnPastePart.activeSelf);
        string text = ui.pgnField.text;
        Check(text.Contains("[GameID \"20250710001921\"]") && text.Contains("[Result \"0-1\"]"),
            n + ": PGN: the imported game's", text);

        // Failed: the move it failed at and why; the board unchanged.
        string f = s + ": failed";
        board._ClearState();
        Redraw();
        CheckPgnBox(false, f + ": reset closed the copy part");
        ui._PressLoadPgn();
        ui.pgnPasteField.text = "1. e4 e5 2. Ke3 *";
        CheckInt(ui._ImportPasted(), ChessBoard.InputAccepted, f + ": started");
        importer._ImportFor(float.MaxValue);
        board._ImportFinished();
        CheckPasteBox(true, true, ImportFailed("2.", "import_error_illegal_move"), f);
        CheckInt(board.lifecycle * 1000 + board.moveCount, ChessBoard.LifecycleReset * 1000, f + ": the board reset");
        CheckString(ui.pgnPasteField.text, "1. e4 e5 2. Ke3 *", f + ": the paste kept");
        // Closed while it runs: opened again to say why.
        ui.pgnPasteField.text = "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ke3 *";
        CheckInt(ui._ImportPasted(), ChessBoard.InputAccepted, f + " while closed: started");
        ui._PressLoadPgn();
        CheckPasteBox(false, false, "", f + " while closed: closed");
        importer._ImportFor(float.MaxValue);
        board._ImportFinished();
        CheckPasteBox(true, true, ImportFailed("4.", "import_error_illegal_move"), f + " while closed: opened again");
        // Opened afresh, the old outcome is gone.
        ui._PressLoadPgn();
        ui._PressLoadPgn();
        CheckPasteBox(true, true, "", f + ": opened afresh");

        // Abandoned: someone joins meanwhile.
        string a = s + ": abandoned";
        ui.pgnPasteField.text = boardTest.siggiVsNuka.text;
        CheckInt(ui._ImportPasted(), ChessBoard.InputAccepted, a + ": started");
        importer._ImportFor(0f);
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, a + ": Ana joins");
        CheckInt(board.importStatus, ChessBoard.ImportAbandoned, a);
        Redraw();
        CheckPasteBox(true, false, Text("import_abandoned"), a);
        CheckButtons("JoinBlack Reset?", a + ": Ana's lobby");
        ui._PressPgnClose();
        CheckPasteBox(false, false, "", a + ": closed");
        CheckInt(ui._PressButton(ChessBoardUI.ButtonLoadPgn), ChessBoard.InputRejected, a + ": Load PGN refused");

        board.importer = savedImporter;
        board._ClearState();
        Redraw();
        CheckPgnBox(false, s + ": cleared");
        CheckView("cleared after the Load PGN checks");
        SendCustomEventDelayedFrames(nameof(RunPieceSetChecks), 1);
    }

    // ---- 12.3, 12.10 and 13.6: chess piece sets ----

    // The arbiter's list and overrides as the scene had them, put back at the end; the default set (index 0).
    ChessPieceSet[] savedPieceSets;
    int savedOverrideWhite;
    int savedOverrideBlack;
    ChessPieceSet defaultSet;
    // Each object's world bounds under the default set on a cleared board: the centre from its square's centre, and
    // the size.
    Vector3[] boundsCentre = new Vector3[ChessBoardView.PieceCount];
    Vector3[] boundsSize = new Vector3[ChessBoardView.PieceCount];

    // A fifteenth frame, with the arbiter's list made the scene's default set and the test set (index 1): the default
    // set complete, its Black only materials, so Black wears White's meshes turned half round, facing White, with
    // Black's material, each Black piece standing as White's of its type does turned half round; a missing White
    // template named, a missing Black one not; the default set's promotion templates one see-through green material;
    // the test set put on White alone by the board's index (ChangePieceSet, at once: meshes, both material slots,
    // turn, size, collider, the pieces standing exactly as the default's), then on Black (its own Black templates,
    // turned as set up); indexes out of range and other colours refused; a game played with it by drops and clicks
    // with captures and a promotion (its ghosts changing in place as either side's index changes while they show),
    // Black resigning (its king laid down); each side back to index 0 alone, the captured pieces, the promoted queen
    // and the fallen king changing in place; the arbiter's override shown whatever the board has synced, and the
    // board's back once cleared, out of range refused, the view a listener once, and one removed not told; a synced
    // index the list doesn't have showing the first set; everything put back.
    public void RunPieceSetChecks() {
        string s = "piece sets";
        ChessArbiter arbiter = board.arbiter;
        bool ready = Utilities.IsValid(testPieceSet) && Utilities.IsValid(arbiter)
            && Utilities.IsValid(arbiter.PieceSet(0));
        Check(ready, s + ": the test set and the arbiter's default set assigned", "list the arbiter's default set, "
            + "Chess Pieces, first, then run Create View Test");
        if (!ready) {
            LogSummary();
            return;
        }
        int white = ChessRules.White;
        int black = ChessRules.Black;
        savedPieceSets = arbiter.pieceSets;
        savedOverrideWhite = arbiter.PieceSetOverride(white);
        savedOverrideBlack = arbiter.PieceSetOverride(black);
        defaultSet = arbiter.PieceSet(0);
        arbiter.SetPieceSetOverride(white, -1);
        arbiter.SetPieceSetOverride(black, -1);
        arbiter.pieceSets = new ChessPieceSet[] { defaultSet, testPieceSet };
        view.animateMoves = false;
        board._ClearState();
        Redraw();
        Check(view._AppliedPieceSet(white) == view.CurrentPieceSet(white)
            && view._AppliedPieceSet(black) == view.CurrentPieceSet(black), s + ": the current sets applied",
            "they aren't");
        CheckInt(board.PieceSetIndex(white) * 10 + board.PieceSetIndex(black), 0, s + ": both indexes 0 on a reset "
            + "board (White * 10 + Black)");
        CheckString(testPieceSet.Problem(), "", s + ": the test set complete");
        CheckString(defaultSet.Problem(), "", s + ": the default set complete");
        Check(!testPieceSet.whitePawn.gameObject.activeInHierarchy && !testPieceSet.blackQueen.gameObject.activeInHierarchy
            && !defaultSet.whiteKing.gameObject.activeInHierarchy
            && !defaultSet.whitePromotionQueen.gameObject.activeInHierarchy, s + ": the sets' templates hidden in play",
            "shown");
        // A missing White template is named (a promotion one too); Black's are optional.
        Transform savedTemplate = testPieceSet.whitePromotionRook;
        testPieceSet.whitePromotionRook = null;
        CheckString(testPieceSet.Problem(), "no White promotion rook template",
            s + ": a missing White promotion template named");
        testPieceSet.whitePromotionRook = savedTemplate;
        savedTemplate = testPieceSet.whiteKnight;
        testPieceSet.whiteKnight = null;
        CheckString(testPieceSet.Problem(), "no White knight template", s + ": a missing White template named");
        testPieceSet.whiteKnight = savedTemplate;
        savedTemplate = testPieceSet.blackRook;
        testPieceSet.blackRook = null;
        CheckString(testPieceSet.Problem(), "", s + ": a missing Black template not a problem");
        testPieceSet.blackRook = savedTemplate;
        CheckString(testPieceSet.Problem(), "", s + ": the templates back");
        // The default set: White's promotion templates one see-through green material on the pieces' meshes, and
        // Black only a material, no meshes, no promotion templates of its own.
        Material ghostMaterial = MaterialOf(defaultSet.whitePromotionKnight.gameObject);
        int ghostWrong = 0;
        for (int type = ChessRules.Knight; type <= ChessRules.Queen; type++) {
            Transform template = defaultSet._PromotionTemplate(white, type);
            if (MaterialOf(template.gameObject) != ghostMaterial
                || template.GetComponent<MeshFilter>().sharedMesh
                    != defaultSet._Template(white, type).GetComponent<MeshFilter>().sharedMesh
                || ChessPieceSet._HasMesh(defaultSet._PromotionTemplate(black, type))) {
                ghostWrong++;
            }
        }
        CheckInt(ghostWrong, 0, s + ": the default promotion templates: one material, the pieces' meshes, none of "
            + "Black's own: wrong ones");
        Color ghostColor = Utilities.IsValid(ghostMaterial) ? ghostMaterial.color : Color.black;
        Check(ghostColor.a < 1 && ghostColor.g > ghostColor.r && ghostColor.g > ghostColor.b,
            s + ": the default promotion material see-through green", "colour " + ghostColor);
        Material whiteMaterial = MaterialOf(defaultSet.whitePawn.gameObject);
        int blackWrong = 0;
        for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) {
            Transform template = defaultSet._Template(black, type);
            Material material = Utilities.IsValid(template) ? MaterialOf(template.gameObject) : null;
            if (ChessPieceSet._HasMesh(template) || !Utilities.IsValid(material) || material == whiteMaterial) {
                blackWrong++;
            }
        }
        CheckInt(blackWrong, 0, s + ": the default set's Black templates only a material of their own: wrong ones");

        // The default set: Black's fallback faces White, wears Black's material and stands on its squares as White's
        // pieces do, turned half round.
        CheckPieceSets(defaultSet, defaultSet, s + ": the default set");
        CheckView(s + ": the default set");
        int blackKing = view.squareObject[Sq("e8")];
        int whiteKing = view.squareObject[Sq("e1")];
        Check(view.pieceFilters[blackKing].sharedMesh == defaultSet.whiteKing.GetComponent<MeshFilter>().sharedMesh
            && view.pieceRenderers[blackKing].sharedMaterial == MaterialOf(defaultSet.blackKing.gameObject)
            && Mathf.Abs(Quaternion.Angle(view.pieces[blackKing].localRotation,
                view.pieces[whiteKing].localRotation) - 180) < 0.1f,
            s + ": the default set: Black's king White's mesh in Black's material, turned to face White",
            "mesh " + view.pieceFilters[blackKing].sharedMesh.name + ", turned "
                + view.pieces[blackKing].localRotation.eulerAngles.ToString("F1"));
        CheckMirroredBounds(s + ": the default set");
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            Bounds bounds = view.pieceRenderers[obj].bounds;
            boundsCentre[obj] = bounds.center - SquareWorld(view.objectSquare[obj]);
            boundsSize[obj] = bounds.size;
        }

        // The board's index: applied at once to White's pieces alone, nothing moving off its square; then to Black's
        // too, with its own templates.
        string t = s + ": the test set";
        SetPieceSet(white, 1, t + " on White");
        CheckPieceSets(testPieceSet, defaultSet, t + " on White, at once: Black's unchanged");
        CheckView(t + " on White");
        CheckBounds(t + " on White");
        SetPieceSet(black, 1, t + " on Black");
        CheckPieceSets(testPieceSet, testPieceSet, t + " on both sides, at once");
        Check(!FallsBack(testPieceSet, black, ChessRules.Pawn, false)
            && view.pieceRenderers[view.squareObject[Sq("a7")]].sharedMaterial
                == MaterialOf(testPieceSet.blackPawn.gameObject),
            t + ": Black wears its own templates", "it doesn't");
        CheckView(t + " on both sides");
        CheckBounds(t + " on both sides: Black's own templates turned as set up");
        CheckInt(board.PieceSetIndex(white) * 10 + board.PieceSetIndex(black), 11, t + ": both indexes 1");

        // Refused: an index outside the list, no colour; nothing changes.
        string r = s + ": refused";
        CheckInt(board.SetPieceSetAs(localName, false, white, 2), ChessBoard.InputRejected, r + ": index 2");
        CheckInt(board.SetPieceSetAs(localName, false, black, -1), ChessBoard.InputRejected, r + ": index -1");
        CheckInt(board.SetPieceSetAs(localName, false, ChessRules.NoColor, 0), ChessBoard.InputRejected,
            r + ": no colour");
        Check(!board.CanSetPieceSetAs(localName, false, 2, 0), r + ": colour 2", "allowed");
        CheckInt(board.PieceSetIndex(white) * 10 + board.PieceSetIndex(black), 11, r + ": both indexes still 1");
        CheckPieceSets(testPieceSet, testPieceSet, r + ": the test set still worn");

        // A game: drops (pickups) and clicks, captures and a promotion.
        int savedStyle = view.inputStyle;
        StartGame(ChessGame.ModeStandard, t + " game");
        view._SetInputStyle(ChessBoardView.InputPickups);
        CheckInt(view.piecePickups[view.squareObject[Sq("e2")]].pickupable ? 1 : 0, 1, t + ": e2 a pickup");
        DropOver("e2", "e4", t + ": e4 dropped");
        CheckDropped(1, ChessBoard.ChangeMove, t + ": e4");
        view._SetInputStyle(ChessBoardView.InputClicks);
        ClickPiece("d7");
        Check(view.selectedObject != ChessBoardView.NoObject, t + ": d7 clicked: selected", "nothing selected");
        Redraw();
        CheckLifted(view.selectedObject, "d7", t + ": d7 selected");
        ClickSquare("d5");
        CheckUnselected(2, t + ": d5 placed by a click");
        view._SetInputStyle(ChessBoardView.InputPickups);
        int blackPawn = view.squareObject[Sq("d5")];
        DropOver("e4", "d5", t + ": exd5 dropped");
        CheckDropped(3, ChessBoard.ChangeMove, t + ": exd5");
        Check(view.objectSquare[blackPawn] == ChessRules.NoSquare && (view.pieces[blackPawn].localPosition
            - view._CapturedPlacePosition(ChessRules.Black, 0) - view._PieceOffset(blackPawn)).sqrMagnitude < 1e-8f,
            t + ": the captured pawn in its place, offset as on a square", "at "
            + view.pieces[blackPawn].localPosition.ToString("F3"));
        Play("c7", "c6", "c6", t + ": c6");
        view._SetInputStyle(ChessBoardView.InputClicks);
        ClickPiece("d5");
        ClickPiece("c6");
        CheckUnselected(5, t + ": dxc6 by clicking the pawn");
        Play("g8", "f6", "f6", t + ": Nf6");
        Play("c6", "b7", "b7", t + ": cxb7");
        Play("b8", "d7", "d7", t + ": Nbd7");
        view._SetInputStyle(ChessBoardView.InputPickups);
        int promoting = view.squareObject[Sq("b7")];
        DropOver("b7", "a8", t + ": bxa8 dropped");
        CheckInt(view.promotionObject, promoting, t + ": bxa8 waits for its piece");
        // The ghosts wear the promoting side's set's promotion templates, and follow a change of that set at once; a
        // set change leaves the promotion waiting.
        CheckGhosts(true, "a8", t + ": bxa8 waiting");
        SetPieceSet(white, 0, t + ": White back to the default set while the ghosts show");
        CheckGhosts(true, "a8", t + ": White back to the default set while the ghosts show");
        SetPieceSet(black, 0, t + ": Black's set changed while White's ghosts show");
        CheckGhosts(true, "a8", t + ": Black's set changed while White's ghosts show");
        SetPieceSet(white, 1, t + ": the test set back on White");
        SetPieceSet(black, 1, t + ": the test set back on Black");
        CheckGhosts(true, "a8", t + ": the test set back on both sides while the ghosts show");
        ClickGhost(ChessRules.Queen, t + ": the queen clicked");
        CheckDropped(9, ChessBoard.ChangeMove, t + ": bxa8=Q");
        CheckInt(view.objectType[promoting], ChessRules.Queen, t + ": bxa8=Q: the pawn's object a queen");
        CheckPieceSets(testPieceSet, testPieceSet, t + ": after the game");
        view._SetInputStyle(savedStyle);
        fallStanding = LowestPoint(view.squareObject[Sq("e8")]);
        CheckInt(board.ResignAs(ChessRules.Black, localName), ChessBoard.InputAccepted, t + ": Black resigned");
        Redraw();
        CheckFallen("e8", Vector3.back, t + ": Black resigned");

        // Each side back to the default set alone, at once: Black's captured pieces and fallen king, then White's
        // promoted queen.
        string b = s + ": index 0";
        SetPieceSet(black, 0, b + ": Black's");
        CheckPieceSets(testPieceSet, defaultSet, b + ": Black's back to the default, at once, White's kept");
        CheckView(b + ": Black's");
        CheckFallen("e8", Vector3.back, b + ": Black's");
        SetPieceSet(white, 0, b + ": White's");
        CheckPieceSets(defaultSet, defaultSet, b + ": White's back to the default, at once");
        CheckView(b + ": White's");
        CheckInt(view.objectType[promoting], ChessRules.Queen, b + ": the promoted queen still a queen");
        CheckFallen("e8", Vector3.back, b + ": White's");

        // The arbiter's overrides: the view a listener of the arbiter's, once; out of range refused; an override worn
        // at once by its side alone, whatever the board has synced, and the board's back when it's cleared.
        int listeners = arbiter._PieceSetListenerCount();
        int found = 0;
        for (int i = 0; i < arbiter.pieceSetListeners.Length; i++) {
            if (arbiter.pieceSetListeners[i] == view) found++;
        }
        CheckInt(found, 1, s + ": the view among the arbiter's piece set listeners");
        arbiter._AddPieceSetListener(view);
        CheckInt(arbiter._PieceSetListenerCount(), listeners, s + ": the view added again: no change");
        string o = s + ": SetPieceSetOverride";
        Check(!arbiter.SetPieceSetOverride(white, 2), o + "(White, 2) refused", "accepted");
        Check(!arbiter.SetPieceSetOverride(black, -2), o + "(Black, -2) refused", "accepted");
        Check(!arbiter.SetPieceSetOverride(2, 0), o + "(2, 0) refused", "accepted");
        CheckInt(arbiter.PieceSetOverride(white) * 10 + arbiter.PieceSetOverride(black), -11,
            o + ": refused: no overrides (White * 10 + Black)");
        Check(arbiter.SetPieceSetOverride(white, 1), o + "(White, 1) accepted", "refused");
        CheckPieceSets(testPieceSet, defaultSet, o + "(White, 1): worn at once by White alone over the board's 0");
        CheckView(o + "(White, 1)");
        SetPieceSet(black, 1, o + ": the board's Black 1");
        Check(arbiter.SetPieceSetOverride(black, 0), o + "(Black, 0) accepted", "refused");
        CheckPieceSets(testPieceSet, defaultSet, o + "(Black, 0): worn over the board's 1");
        SetPieceSet(white, 1, o + ": the board's White 1 under the override");
        SetPieceSet(white, 0, o + ": the board's White 0 under the override");
        CheckPieceSets(testPieceSet, defaultSet, o + ": the board's changes not shown under the overrides");
        Check(arbiter.SetPieceSetOverride(white, -1), o + "(White, -1) accepted", "refused");
        CheckPieceSets(defaultSet, defaultSet, o + "(White, -1): the board's White 0 back");
        Check(arbiter.SetPieceSetOverride(black, -1), o + "(Black, -1) accepted", "refused");
        CheckPieceSets(defaultSet, testPieceSet, o + "(Black, -1): the board's Black 1 back");
        CheckView(o + ": cleared");
        SetPieceSet(black, 0, o + ": the board's Black 0");

        // A listener removed isn't told.
        arbiter._RemovePieceSetListener(view);
        CheckInt(arbiter._PieceSetListenerCount(), listeners - 1, s + ": the view removed as a listener");
        arbiter.SetPieceSetOverride(white, 1);
        CheckPieceSets(defaultSet, defaultSet, s + ": the view removed: not told of the override");
        arbiter.SetPieceSetOverride(white, -1);
        arbiter._AddPieceSetListener(view);
        CheckInt(arbiter._PieceSetListenerCount(), listeners, s + ": the view added back");

        // A synced index the list doesn't have (as from a client whose list is longer) shows the first set.
        board.whitePieceSet = 5;
        Redraw();
        Check(view.CurrentPieceSet(white) == defaultSet, s + ": a synced index 5 shows the first set", "another");
        board.whitePieceSet = 0;

        // Put back as it was.
        arbiter.pieceSets = savedPieceSets;
        arbiter.SetPieceSetOverride(white, savedOverrideWhite);
        arbiter.SetPieceSetOverride(black, savedOverrideBlack);
        board._ClearState();
        Redraw();
        Check(view._AppliedPieceSet(white) == view.CurrentPieceSet(white)
            && view._AppliedPieceSet(black) == view.CurrentPieceSet(black) && arbiter.PieceSet(0) == defaultSet,
            s + ": put back", "they aren't");
        CheckView(s + ": cleared");
        SendCustomEventDelayedFrames(nameof(RunCapturedPlaceChecks), 1);
    }

    // Sets a colour's piece set on the board to index as the local player (accepted, told as ChangePieceSet unless it
    // was the set already).
    void SetPieceSet(int color, int index, string name) {
        bool changes = board.PieceSetIndex(color) != index;
        CheckInt(board.SetPieceSetAs(localName, false, color, index), ChessBoard.InputAccepted, name + ": set "
            + index);
        if (changes) CheckInt(board.lastChange, ChessBoard.ChangePieceSet, name + ": told as a piece set change");
        CheckInt(board.PieceSetIndex(color), index, name + ": the index");
    }

    // Whether every Black piece on a cleared board stands as White's piece of its type does, turned half round about
    // its square's centre: its world bounds' centre from the square's centre mirrored in x and z, the same size.
    void CheckMirroredBounds(string name) {
        int wrong = 0;
        string details = "";
        Vector3 axis = view.transform.up;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (view._ObjectColor(obj) != ChessRules.Black || view.objectSquare[obj] == ChessRules.NoSquare) continue;
            int match = -1;
            for (int other = 0; other < ChessBoardView.PieceCount && match < 0; other++) {
                if (view._ObjectColor(other) == ChessRules.White && view.objectType[other] == view.objectType[obj]
                    && view.objectSquare[other] != ChessRules.NoSquare) {
                    match = other;
                }
            }
            if (match < 0) continue;
            Bounds bounds = view.pieceRenderers[obj].bounds;
            Bounds whiteBounds = view.pieceRenderers[match].bounds;
            Vector3 centre = bounds.center - SquareWorld(view.objectSquare[obj]);
            Vector3 expected = Quaternion.AngleAxis(180, axis) * (whiteBounds.center
                - SquareWorld(view.objectSquare[match]));
            if ((centre - expected).magnitude > 1e-4f || (bounds.size - whiteBounds.size).magnitude > 1e-4f) {
                if (wrong++ < 2) {
                    details += "object " + obj + " bounds " + centre.ToString("F4") + " size "
                        + bounds.size.ToString("F4") + ", White's turned " + expected.ToString("F4") + " size "
                        + whiteBounds.size.ToString("F4") + "; ";
                }
            }
        }
        Check(wrong == 0, name + ": each Black piece stands as White's turned half round", wrong + " differ: "
            + details);
    }

    // Whether every object stands as it did under the starting sets (RunPieceSetChecks): its world bounds the same
    // about its square's centre, so upright on its square at the default's size.
    void CheckBounds(string name) {
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
        Check(wrong == 0, name + ": every piece upright on its square at the default's size", wrong + " differ: "
            + details);
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

        board._ClearState();
        StartGame(ChessGame.ModeStandard, c);
        int blackD = view.squareObject[Sq("d7")];
        int whiteE = view.squareObject[Sq("e2")];
        int whiteA = view.squareObject[Sq("a2")];
        int blackQ = view.squareObject[Sq("d8")];
        Play("e2", "e4", "e4", c + ": e4");
        Play("d7", "d5", "d5", c + ": d5");
        Play("e4", "d5", "d5", c + ": exd5");
        CheckPlaces("", "P", c + ": exd5");
        Check(Mathf.Abs(view._CapturedPlacePosition(ChessRules.Black, 0).y) > 0.001f, c + ": the places at another "
            + "height than the board", "at " + view._CapturedPlacePosition(ChessRules.Black, 0).ToString("F3"));
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

        // A takeback (Continue from here, in both seats) after a capture empties the last place.
        SetRequestRules(true);
        CheckInt(board.StepToAs(localName, true, 6), ChessBoard.InputAccepted, c + ": takeback: stepped to Qxa2");
        CheckInt(board.ContinueGameAs(localName), ChessBoard.InputAccepted, c + ": takeback: continued");
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
        Check(!view._IsObjectShown(blackQ) && !view.pieces[blackQ].gameObject.activeSelf, f + ": the queen hidden",
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
        board._ClearState();
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

        CheckPutBack(c + ": put back");

        board._ClearState();
        Redraw();
        CheckView(c + ": cleared");
        CheckPlaces("", "", c + ": cleared");
        SendCustomEventDelayedFrames(nameof(RunFallChecks), 1);
    }

    // Putting captured pieces back in replay (step 13.4): a captured piece is a pickup only in replay; dropped on a
    // square (empty, or another piece's, which goes to its places) it stands there and leaves its place, dropped off
    // the board it goes back; then with clicks, selected in its place, put back by a second click, and placed on a
    // square.
    void CheckPutBack(string b) {
        int savedStyle = view.inputStyle;
        view._SetInputStyle(ChessBoardView.InputPickups);
        board._ClearState();
        StartGame(ChessGame.ModeStandard, b);
        PlayMoves("e2e4 d7d5 e4d5 d8d5", b);
        CheckPlaces("P", "P", b + ": exd5 Qxd5");
        CheckPickupableColor(15, ChessRules.White, b + ": in game, White's on the board");
        CheckInt(board.EnterReplayAs(localName, true), ChessBoard.InputAccepted, b + ": replay");
        Redraw();
        CheckPickupableOff(32, 2, b + ": replay, the captured pieces too");

        // Dropped over an empty square.
        int whitePawn = PlaceObject(ChessRules.White, 0);
        view._OnPiecePickedUp(whitePawn);
        CheckInt(view.heldObject, whitePawn, b + ": the white pawn picked up from its place");
        view.pieces[whitePawn].localPosition = Over("e3") + view._PieceOffset(whitePawn);
        view._OnPieceDropped(whitePawn);
        CheckInt(board.lastChange, ChessBoard.ChangeFreeMove, b + ": dropped over e3: a free move");
        CheckInt(board.replayBoard[Sq("e3")], ChessRules.WhitePawn, b + ": dropped over e3: a white pawn on e3");
        Redraw();
        CheckView(b + ": dropped over e3");
        CheckInt(view.squareObject[Sq("e3")], whitePawn, b + ": dropped over e3: the dropped object stands there");
        CheckPlaces("", "P", b + ": dropped over e3");
        CheckPickupableOff(32, 1, b + ": dropped over e3: pickups");

        // Dropped over the queen: she goes to Black's places.
        int blackPawn = PlaceObject(ChessRules.Black, 0);
        view._OnPiecePickedUp(blackPawn);
        CheckInt(view.heldObject, blackPawn, b + ": the black pawn picked up from its place");
        view.pieces[blackPawn].localPosition = Over("d5") + view._PieceOffset(blackPawn);
        view._OnPieceDropped(blackPawn);
        CheckInt(board.replayBoard[Sq("d5")], ChessRules.PieceOf(ChessRules.Black, ChessRules.Pawn),
            b + ": dropped over d5: a black pawn on d5");
        Redraw();
        CheckView(b + ": dropped over d5");
        CheckInt(view.squareObject[Sq("d5")], blackPawn, b + ": dropped over d5: the dropped object stands there");
        CheckPlaces("", "Q", b + ": dropped over d5: the queen in Black's place");

        // Dropped off the board: back in its place.
        int blackQueen = PlaceObject(ChessRules.Black, 0);
        view._OnPiecePickedUp(blackQueen);
        CheckInt(view.heldObject, blackQueen, b + ": the queen picked up from her place");
        view.pieces[blackQueen].localPosition = OffBoard();
        view._OnPieceDropped(blackQueen);
        CheckInt(board.lastChange, ChessBoard.ChangeRejected, b + ": dropped off the board: rejected");
        Redraw();
        CheckView(b + ": dropped off the board");
        CheckInt(view.objectPlace[blackQueen], 0, b + ": dropped off the board: the queen in her place again");

        // Clicks: selected in its place (lifted), put back by a second click, then placed on a square.
        view._SetInputStyle(ChessBoardView.InputClicks);
        Redraw();
        CheckPickupable(0, b + ": clicks: no pickups");
        Check(view._IsPieceClickable(blackQueen), b + ": clicks: the queen in her place clickable", "not clickable");
        view._OnPieceClicked(blackQueen);
        CheckInt(view.selectedObject, blackQueen, b + ": clicks: the queen selected");
        Redraw();
        Vector3 lifted = view._CapturedPlacePosition(ChessRules.Black, 0) + view._PieceOffset(blackQueen)
            + new Vector3(0, ChessBoardView.SelectLift * view.squareSize, 0);
        Check((view.pieces[blackQueen].localPosition - lifted).sqrMagnitude < 1e-8f, b + ": clicks: the queen lifted "
            + "over her place", "at " + view.pieces[blackQueen].localPosition.ToString("F3"));
        Check(view._IsSquareClickable(Sq("d8")) && view._IsSquareClickable(Sq("e3"))
                && view._IsPieceClickable(blackQueen),
            b + ": clicks: the squares and the queen clickable", "not clickable");
        view._OnPieceClicked(blackQueen);
        CheckInt(view.selectedObject, ChessBoardView.NoObject, b + ": clicks: a second click puts her back");
        Check(!view._IsSquareClickable(Sq("d8")), b + ": clicks: put back: no square clickable", "d8 clickable");
        Redraw();
        CheckView(b + ": clicks: put back");
        view._OnPieceClicked(blackQueen);
        CheckInt(view.selectedObject, blackQueen, b + ": clicks: the queen selected again");
        view._OnSquareClicked(Sq("d8"));
        CheckInt(view.selectedObject, ChessBoardView.NoObject, b + ": clicks: placed, unselected");
        CheckInt(board.replayBoard[Sq("d8")], ChessRules.PieceOf(ChessRules.Black, ChessRules.Queen),
            b + ": clicks: the queen on d8");
        Redraw();
        CheckView(b + ": clicks: placed on d8");
        CheckInt(view.squareObject[Sq("d8")], blackQueen, b + ": clicks: the queen's object on d8");
        CheckPlaces("", "", b + ": clicks: placed on d8");

        // Out of replay, the captured pieces are no pickups or click targets again.
        CheckInt(board.LeaveReplayAs(localName, true), ChessBoard.InputAccepted, b + ": left replay");
        Redraw();
        CheckPlaces("P", "P", b + ": left replay");
        Check(!view._IsPieceClickable(PlaceObject(ChessRules.White, 0))
            && !view._IsPieceClickable(PlaceObject(ChessRules.Black, 0)), b + ": left replay: captured pieces not "
            + "clickable", "clickable");
        view._SetInputStyle(ChessBoardView.InputPickups);
        CheckPickupableColor(15, ChessRules.White, b + ": left replay: White's on the board");
        view._SetInputStyle(savedStyle);
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
        board._ClearState();
        Redraw();
        CheckFalls(f + ": a cleared board");
        fallKing = view.squareObject[Sq("e1")];
        fallStanding = LowestPoint(fallKing);
        StartGame(ChessGame.ModeStandard, f);
        PlayMoves("f2f3 e7e5 g2g4", f);
        CheckFalls(f + ": before the mate");
        PlayMoves("d8h4", f);
        CheckInt(board.FallenKingSquare(), Sq("e1"), f + ": Qh4#: White's king mated");
        CheckView(f + ": Qh4#");
        CheckFallen("e1", Vector3.forward, f + ": Qh4#");
        CheckInt(board.EnterReplayAs(localName, false), ChessBoard.InputAccepted, f + ": replay entered");
        Redraw();
        CheckFallen("e1", Vector3.forward, f + ": replay entered");

        view.animateMoves = true;
        CheckInt(board.StepToAs(localName, false, 3), ChessBoard.InputAccepted, f + ": stepped to ply 3");
        Redraw();
        CheckInt(board.FallenKingSquare(), ChessRules.NoSquare, f + ": ply 3: no king mated");
        Check(view.fallTarget[fallKing] == 0 && view.fallAmount[fallKing] == 1 && view._IsFalling(fallKing)
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
        Check(amount > 0 && amount < 1 && view._IsFalling(fallKing), f + ": partly over", FallText(fallKing));
        Check((view.pieces[fallKing].localPosition - view._ObjectPlace(fallKing)).sqrMagnitude < 1e-8f,
            f + ": in its place", "at " + view.pieces[fallKing].localPosition.ToString("F3") + ", its place "
            + view._ObjectPlace(fallKing).ToString("F3"));
        CheckInt(view._DropSquare(view._PieceBase(fallKing)), Sq("e1"), f + ": its base over e1");
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
        CheckInt(board.ContinueGameAs(localName), ChessBoard.InputAccepted, f + ": takeback: continued");
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
            board._ClearState();
            importer._StartImport(MatePgn, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
            CheckInt(importer._ImportFor(float.MaxValue), ChessPgn.ImportDone, f + ": loaded mate: imported");
            CheckInt(board._LoadImport(importer), ChessBoard.InputAccepted, f + ": loaded mate: loaded");
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

        board._ClearState();
        StartGame(ChessGame.ModeStandard, f + " (Black)");
        PlayMoves("e2e4 e7e5 d1h5 b8c6 f1c4 g8f6 h5f7", f + " (Black)");
        CheckInt(board.FallenKingSquare(), Sq("e8"), f + " (Black): Qxf7#: Black's king mated");
        CheckFallen("e8", Vector3.back, f + " (Black): Qxf7#");

        LoadFen(SideMateFen, f + " (h file)");
        CheckInt(board.FallenKingSquare(), Sq("h5"), f + " (h file): Black's king on h5 mated");
        CheckFallen("h5", Vector3.left, f + " (h file)");

        // A resignation lays the resigning side's king down, at once with nothing travelling.
        string r = "fall: resigned";
        board._ClearState();
        StartGame(ChessGame.ModeStandard, r);
        PlayMoves("e2e4 e7e5", r);
        view.animateMoves = true;
        CheckInt(board.ResignAs(ChessRules.White, localName), ChessBoard.InputAccepted, r + ": White resigns");
        Redraw();
        CheckInt(board.FallenKingSquare(), Sq("e1"), r + ": White's king fallen");
        int resigned = view.squareObject[Sq("e1")];
        Check(view.fallTarget[resigned] == 1 && view._IsFalling(resigned) && view.fallStart[resigned] <= Time.time,
            r + ": the king starts falling at once", FallText(resigned));
        view.animateMoves = false;
        Redraw();
        CheckView(r + " (White)");
        CheckFallen("e1", Vector3.forward, r + " (White)");
        CheckInt(board.EnterReplayAs(localName, false), ChessBoard.InputAccepted, r + ": replay entered");
        Redraw();
        CheckFallen("e1", Vector3.forward, r + ": replay entered");
        CheckInt(board.StepToAs(localName, false, 1), ChessBoard.InputAccepted, r + ": stepped to ply 1");
        Redraw();
        CheckFalls(r + ": ply 1");

        board._ClearState();
        StartGame(ChessGame.ModeStandard, r + " (Black)");
        PlayMoves("e2e4", r + " (Black)");
        CheckInt(board.ResignAs(ChessRules.Black, localName), ChessBoard.InputAccepted, r + " (Black): Black resigns");
        Redraw();
        CheckInt(board.FallenKingSquare(), Sq("e8"), r + " (Black): Black's king fallen");
        CheckFallen("e8", Vector3.back, r + " (Black)");

        // A draw lays nobody down.
        board._ClearState();
        StartGame(ChessGame.ModeStandard, f + " (draw)");
        SetRequestRules(true);
        PlayMoves("e2e4", f + " (draw)");
        CheckInt(board.OfferDrawAs(ChessRules.Black, localName), ChessBoard.InputAccepted, f + " (draw): offered");
        CheckInt(board.AcceptDrawAs(ChessRules.White, localName), ChessBoard.InputAccepted, f + " (draw): accepted");
        Redraw();
        CheckInt(board.ending, ChessGame.EndingDrawAgreement, f + " (draw): drawn");
        CheckFalls(f + " (draw)");

        board._ClearState();
        Redraw();
        CheckFalls(f + ": cleared");
        CheckView(f + ": cleared");
        SendCustomEventDelayedFrames(nameof(RunSoundChecks), 1);
    }

    // ---- 12.12: sound effects ----

    // The view's and the UI's sound counts when last checked (CheckSounds).
    int viewSounds;
    int uiSounds;
    // The view's input style before the sound checks, put back at the end.
    int savedSoundStyle;

    // A frame after the fall checks: the sources and clips wired; each change's sound, one per change, the move sounds
    // the view's and every other one the UI's; none for several changes at once; the promotion's landing; the time
    // warning; then the checkmate's game over, waiting for the check sound (RunSoundEndChecks).
    public void RunSoundChecks() {
        string s = "sounds";
        CheckSource(view.audioSource, s + ": the view's source");
        CheckSource(ui.audioSource, s + ": the UI's source");
        CheckClip(view.moveSound, "move", s + ": view: moveSound");
        CheckClip(view.captureSound, "capture", s + ": view: captureSound");
        CheckClip(view.promoteSound, "promote", s + ": view: promoteSound");
        CheckClip(view.checkSound, "move-check", s + ": view: checkSound");
        CheckClip(ui.clickSound, "ui-click", s + ": UI: clickSound");
        CheckClip(ui.gameStartSound, "game-start", s + ": UI: gameStartSound");
        CheckClip(ui.notificationSound, "notification-pop", s + ": UI: notificationSound");
        CheckClip(ui.gameEndSound, "game-end", s + ": UI: gameEndSound");
        CheckClip(ui.resetSound, "table-reset", s + ": UI: resetSound");
        CheckClip(ui.timeWarningSound, "time-warning", s + ": UI: timeWarningSound");
        CheckClip(ui.timeOutSound, "time-out", s + ": UI: timeOutSound");
        CheckClip(ui.checkSound, "move-check", s + ": UI: checkSound (the view's)");
        savedSoundStyle = view.inputStyle;
        view._SetInputStyle(ChessBoardView.InputPickups);

        board._ClearState();
        Redraw();
        MarkSounds();
        // A press clicks at once, refused or not; the change's own sound follows on the next Refresh.
        ui._PressButton(ChessBoardUI.ButtonResignWhite);
        CheckSounds(null, ui.clickSound, s + ": a refused press clicks");
        Redraw();
        CheckSounds(null, null, s + ": nothing follows a refused press");
        CheckInt(board.JoinAs(ChessRules.White, localName), ChessBoard.InputAccepted, s + ": joined White");
        Redraw();
        CheckSounds(null, null, s + ": joined White");
        CheckInt(board.JoinAs(ChessRules.Black, localName), ChessBoard.InputAccepted, s + ": joined Black");
        Redraw();
        CheckSounds(null, null, s + ": joined Black");
        Unforce();
        board.mode = ChessGame.ModeStandard;
        CheckInt(board.StartGameAs(localName), ChessBoard.InputAccepted, s + ": started");
        Redraw();
        CheckSounds(null, ui.gameStartSound, s + ": started");
        SetRequestRules(true);

        MoveSound("e2e4", view.moveSound, null, s + ": e4");
        MoveSound("e7e5", view.moveSound, null, s + ": e5");
        MoveSound("f1c4", view.moveSound, null, s + ": Bc4");
        MoveSound("b8c6", view.moveSound, null, s + ": Nc6");
        // A capture that checks plays only the check sound.
        MoveSound("c4f7", view.checkSound, null, s + ": Bxf7+");
        MoveSound("e8f7", view.captureSound, null, s + ": Kxf7");
        // Several changes before a Refresh play nothing (a test's, or a received update's is one change).
        PlayMoves("g1f3 g8f6", s + ": two moves at once");
        CheckSounds(null, null, s + ": two moves before a Refresh");
        MoveSound("e1g1", view.moveSound, null, s + ": O-O");
        MoveSound("c6d4", view.moveSound, null, s + ": Nd4");
        MoveSound("f3e5", view.checkSound, null, s + ": Nxe5+");

        // Requests: made, the notification; declined, nothing.
        CheckInt(board.OfferDrawAs(ChessRules.Black, localName), ChessBoard.InputAccepted, s + ": Black offers a draw");
        Redraw();
        CheckSounds(null, ui.notificationSound, s + ": draw offered");
        CheckInt(board.DeclineDrawAs(ChessRules.White, localName), ChessBoard.InputAccepted, s + ": declined");
        Redraw();
        CheckSounds(null, null, s + ": draw declined");

        // Replay: each step plays the shown position's sound (none at the start), entering and leaving none.
        CheckInt(board.EnterReplayAs(localName, false), ChessBoard.InputAccepted, s + ": replay entered");
        Redraw();
        CheckSounds(null, null, s + ": replay entered");
        StepSound(4, view.moveSound, s + ": stepped to ply 4 (Nc6)");
        StepSound(5, view.checkSound, s + ": stepped to ply 5 (Bxf7+)");
        StepSound(6, view.captureSound, s + ": stepped to ply 6 (Kxf7)");
        StepSound(0, null, s + ": stepped to the start");
        StepSound(1, view.moveSound, s + ": stepped to ply 1");
        // The step buttons don't click: only the step's own sound.
        PressStepSound(ChessBoardUI.ButtonStepForward, 2, view.moveSound, s + ": > pressed");
        PressStepSound(ChessBoardUI.ButtonStepFirst, 0, null, s + ": << pressed");
        PressStepSound(ChessBoardUI.ButtonStepLast, board.moveCount, view.checkSound, s + ": >> pressed (Nxe5+)");
        PressStepSound(ChessBoardUI.ButtonStepBack, board.moveCount - 1, view.moveSound, s + ": < pressed (Nd4)");
        StepSound(1, view.moveSound, s + ": stepped back to ply 1");
        // A takeback request needs two players (13.3): Bob takes Black for it.
        board.blackPlayer = "Bob";
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted,
            s + ": Black asks to take back");
        Redraw();
        CheckSounds(null, ui.notificationSound, s + ": takeback requested");
        CheckInt(board.DeclineTakebackAs(ChessRules.White, localName), ChessBoard.InputAccepted,
            s + ": takeback declined");
        Redraw();
        CheckSounds(null, null, s + ": takeback declined");
        board.blackPlayer = localName;
        // Free moves: a move, a capture, a check.
        FreeMoveSound("e4", "e5", view.moveSound, s + ": free move e4-e5");
        FreeMoveSound("d1", "d7", view.checkSound, s + ": free move Qxd7+");
        FreeMoveSound("e8", "d7", view.captureSound, s + ": free move Kxd7");
        CheckInt(board.LeaveReplayAs(localName, false), ChessBoard.InputAccepted, s + ": back to the game");
        Redraw();
        CheckSounds(null, null, s + ": back to the game");

        // Ending: a resignation is the UI's game over alone; stepping to its end in replay is just the last move's.
        CheckInt(board.ResignAs(ChessRules.White, localName), ChessBoard.InputAccepted, s + ": White resigns");
        Redraw();
        CheckSounds(null, ui.gameEndSound, s + ": resigned");
        CheckInt(board.EnterReplayAs(localName, false), ChessBoard.InputAccepted, s + ": resigned: replay");
        Redraw();
        StepSound(1, view.moveSound, s + ": resigned: ply 1");
        StepSound(board.moveCount, view.checkSound, s + ": resigned: the last ply (Nxe5+)");
        Check(ui.gameEndAt < 0, s + ": resigned: no game over waits at the last ply", "at " + ui.gameEndAt);
        CheckInt(board.ResetBoardAs(localName, true), ChessBoard.InputAccepted, s + ": reset");
        Redraw();
        CheckSounds(null, ui.resetSound, s + ": reset");

        // Promotion: the landing's move or capture sound at once, then the promotion's (check when it checks).
        PromotionSound("8/1P6/8/7k/8/8/8/K7 w - - 0 1", "b8", ChessRules.Queen, view.moveSound, view.promoteSound,
            s + ": b8=Q");
        PromotionSound("r7/1P6/8/7k/8/8/8/K7 w - - 0 1", "a8", ChessRules.Rook, view.captureSound,
            view.promoteSound, s + ": bxa8=R");
        PromotionSound("7k/1P6/8/8/8/8/8/K7 w - - 0 1", "b8", ChessRules.Queen, view.moveSound, view.checkSound,
            s + ": b8=Q+");
        // Continue from here is a game start.
        LoadFen("8/1P6/8/7k/8/8/8/K7 w - - 0 1", s + ": continued");
        MarkSounds();
        CheckInt(board.JoinAs(ChessRules.White, localName), ChessBoard.InputAccepted, s + ": continued: joined White");
        CheckInt(board.JoinAs(ChessRules.Black, localName), ChessBoard.InputAccepted, s + ": continued: joined Black");
        Redraw();
        CheckInt(board.ContinueGameAs(localName), ChessBoard.InputAccepted, s + ": continued");
        Redraw();
        CheckSounds(null, ui.gameStartSound, s + ": continued");

        // The time warning, sampled at chosen times in a 3+0 game: at each of 1:00, 0:30 and 0:10 once, nothing
        // between, once for two passed at one sample; then a flag fall's time-out in place of game over.
        board._ClearState();
        CheckInt(board.JoinAs(ChessRules.White, localName), ChessBoard.InputAccepted, s + ": timed: joined White");
        CheckInt(board.JoinAs(ChessRules.Black, localName), ChessBoard.InputAccepted, s + ": timed: joined Black");
        Redraw();
        Unforce();
        board.mode = ChessGame.ModeStandard;
        board.baseTime = 180;
        board.increment = 0;
        CheckInt(board.StartGameAs(localName), ChessBoard.InputAccepted, s + ": timed: started");
        Redraw();
        CheckSounds(null, ui.gameStartSound, s + ": timed: started");
        double start = board.turnStart;
        WarningSound(start + 1, false, s + ": timed: 2:59 left");
        WarningSound(start + 119.5, false, s + ": timed: 1:00.5 left");
        WarningSound(start + 120.5, true, s + ": timed: 0:59.5 left");
        WarningSound(start + 121, false, s + ": timed: 0:59 left");
        WarningSound(start + 150.5, true, s + ": timed: 0:29.5 left");
        WarningSound(start + 170.5, true, s + ": timed: 0:09.5 left");
        WarningSound(start + 175, false, s + ": timed: 0:05 left");
        WarningSound(start + 1, false, s + ": timed: a new look from 2:59");
        WarningSound(start + 171, true, s + ": timed: all three passed at once: one warning");
        Check(board._CheckFlagFallAt(start + 181), s + ": timed: the flag falls", "the game didn't end on time");
        Redraw();
        CheckSounds(null, ui.timeOutSound, s + ": timed: flag fall");

        // A game ended by its last move: the view's sound, then game over once the check sound has finished.
        board._ClearState();
        StartUnforced(s + ": mate");
        PlayMoves("f2f3 e7e5 g2g4", s + ": mate");
        MarkSounds();
        CheckInt(board.PieceDroppedAs(localName, Sq("d8"), Sq("h4")), ChessBoard.InputAccepted, s + ": Qh4#");
        Redraw();
        CheckSounds(view.checkSound, null, s + ": Qh4#: the check sound first");
        float wait = view.checkSound != null ? view.checkSound.length : 0;
        Check(ui.gameEndAt >= Time.time + wait - 0.01f && ui.gameEndAt <= Time.time + wait + 0.01f,
            s + ": Qh4#: game over waits for the check sound", "at " + ui.gameEndAt + ", now " + Time.time + ", the "
            + "check sound " + wait + " s");
        SendCustomEventDelayedSeconds(nameof(RunSoundEndChecks), wait + 0.3f);
    }

    // The frame after the check sound: the checkmate's game over played once; in replay, stepping onto the mate
    // waiting for game over again and a step back dropping it; a stalemate's game over waiting too; cleared.
    public void RunSoundEndChecks() {
        string s = "sounds";
        CheckSounds(null, ui.gameEndSound, s + ": Qh4#: then game over");
        CheckInt(board.EnterReplayAs(localName, false), ChessBoard.InputAccepted, s + ": mate: replay");
        Redraw();
        StepSound(3, view.moveSound, s + ": mate: ply 3");
        Check(ui.gameEndAt < 0, s + ": mate: no game over waits at ply 3", "at " + ui.gameEndAt);
        StepSound(4, view.checkSound, s + ": mate: stepped to the mate");
        Check(ui.gameEndAt > Time.time, s + ": mate: stepped to the mate: game over waits", "at " + ui.gameEndAt);
        StepSound(3, view.moveSound, s + ": mate: stepped back");
        Check(ui.gameEndAt < 0, s + ": mate: stepped back: game over no longer waits", "at " + ui.gameEndAt);

        board._ClearState();
        StartUnforced(s + ": stalemate");
        PlayMoves(LoydStalemate.Substring(0, LoydStalemate.Length - 5), s + ": stalemate");
        MoveSound("c8e6", view.moveSound, null, s + ": Qe6 stalemates");
        CheckInt(board.ending, ChessGame.EndingStalemate, s + ": stalemated");
        Check(ui.gameEndAt > Time.time, s + ": stalemate: game over waits", "at " + ui.gameEndAt);
        CheckInt(board.ResetBoardAs(localName, true), ChessBoard.InputAccepted, s + ": stalemate: reset");
        Redraw();
        CheckSounds(null, ui.resetSound, s + ": stalemate: reset");
        Check(ui.gameEndAt < 0, s + ": stalemate: the reset drops the waiting game over", "at " + ui.gameEndAt);

        board._ClearState();
        Redraw();
        SetRequestRules(false);
        view._SetInputStyle(savedSoundStyle);
        view.animateMoves = savedAnimate;
        view.muted = false;
        ui.muted = false;
        TestLanguage();
        LogSummary();
    }

    // ---- 13.5: the UI's text in a language file ----

    // The game texts' keys (ChessBoardUI._GameTextKeys, one per index before TextEndings, and every ending's
    // ChessGame.EndingKey) are in en.json (any other key missing logs an I18n error as it's read); the view and the UI share one I18n, English
    // here; the fixed texts and the hover texts the view sets; and a test pack (View/test-language.json, its code
    // x-chess-test, added to the Chess Language by Tools > Chess Board > Create View Test) overriding a few keys
    // changes those texts on SetLanguage, leaving the rest English. English again afterwards. In one frame, so no
    // other test sees the test pack.
    void TestLanguage() {
        string s = "language";
        I18n i18n = ui.i18n;
        Check(Utilities.IsValid(i18n) && view.i18n == i18n, s + ": the view and the UI share one I18n",
            "the UI's " + (Utilities.IsValid(i18n) ? i18n.gameObject.name : "is empty") + ", the view's "
            + (Utilities.IsValid(view.i18n) ? view.i18n.gameObject.name : "is empty"));
        if (!Utilities.IsValid(i18n) || !Utilities.IsValid(languagePack) || !Utilities.IsValid(testLanguagePack)) {
            Check(false, s, "the I18n or a language pack isn't assigned (Tools > Chess Board > Create View Test)");
            return;
        }
        DataToken parsed;
        bool read = VRCJson.TryDeserializeFromJson(languagePack.text, out parsed)
            && parsed.TokenType == TokenType.DataDictionary;
        Check(read, s + ": en.json read", "not a JSON object");
        if (!read) return;
        DataDictionary pack = parsed.DataDictionary;
        string[] keys = ChessBoardUI._GameTextKeys();
        CheckInt(keys.Length, ChessBoardUI.TextEndings, s + ": a game text key for each index before the endings");
        string missing = "";
        for (int i = 0; i < keys.Length; i++) {
            if (!pack.ContainsKey(keys[i])) missing += " " + keys[i];
        }
        for (int ending = 0; ending < ChessGame.EndingCount; ending++) {
            for (int winner = ChessRules.NoColor; winner <= ChessRules.Black; winner++) {
                string key = ChessGame.EndingKey(ending, winner);
                if (!pack.ContainsKey(key) && missing.IndexOf(" " + key) < 0) missing += " " + key;
            }
        }
        CheckString(missing, "", s + ": every game text key is in en.json");
        CheckString(i18n.GetCurrentLanguage(), "en", s + ": English in use");

        CheckString(ui.pgnImportText.text + "|" + ui.pgnCloseText.text, "Import|Close", s + ": the PGN box's buttons");
        Check(ui.pgnCopyHint.text.StartsWith("PGN of this game") && ui.pgnPasteHint.text.StartsWith("Paste a PGN"),
            s + ": the PGN box's hints", "\"" + ui.pgnCopyHint.text + "\", \"" + ui.pgnPasteHint.text + "\"");
        CheckString(PickupTexts(), "Move", s + ": every piece's pickup");
        string ghosts = "";
        for (int ghost = 0; ghost < view.promotionGhosts.Length; ghost++) {
            ChessPromotionGhost behaviour = view.promotionGhosts[ghost];
            ghosts += (ghost > 0 ? "|" : "") + (Utilities.IsValid(behaviour) ? behaviour.InteractionText : "(none)");
        }
        CheckString(ghosts, "Promote to knight|Promote to bishop|Promote to rook|Promote to queen",
            s + ": the promotion ghosts");

        Check(i18n.SetLanguage("x-chess-test"), s + ": the test pack chosen", "no pack x-chess-test");
        CheckString(ui._ButtonText(ChessBoardUI.ButtonStart), "Begin (test)", s + ": test pack: Start overridden");
        CheckString(ui._ButtonText(ChessBoardUI.ButtonReset), "Reset", s + ": test pack: Reset left English");
        CheckString(I18n.Format(ui._GameTexts()[ChessBoardUI.TextPlayerLine], new string[] {
            "color", ChessBoardUI._ColorText(ui._GameTexts(), ChessRules.White), "name", "Ana" }), "Light: Ana",
            s + ": test pack: White overridden in a line left English");
        CheckString(PickupTexts(), "Shift (test)", s + ": test pack: the pieces' pickups");
        CheckString(ui.pgnImportText.text + "|" + ui.pgnCloseText.text, "Bring in (test)|Close",
            s + ": test pack: the PGN box's buttons");
        CheckString(OtherStartTexts(), "Begin (test)", s + ": test pack: Start on every other board");
        i18n.SetLanguage("en");
        CheckString(i18n.GetCurrentLanguage(), "en", s + ": English again");
        CheckString(ui._ButtonText(ChessBoardUI.ButtonStart), "Start", s + ": English again: Start");
        CheckString(PickupTexts(), "Move", s + ": English again: the pieces' pickups");
        CheckString(ui.pgnImportText.text, "Import", s + ": English again: the PGN box's Import");
        CheckString(OtherStartTexts(), "Start", s + ": English again: Start on every other board");
        TestWorldLanguage(i18n);
    }

    // 14.10: a world's own main I18n (worldI18n, elsewhere in the scene, its one pack x-chess-test) switches the Chess
    // Language with SetLanguageGlobally, as the sanity check linked them: every board's texts follow, from our pack
    // of that code. Back with "en", which only ours has (so the call returns false). In one frame, as above.
    void TestWorldLanguage(I18n i18n) {
        string s = "world language";
        if (!Utilities.IsValid(worldI18n) || worldI18n == i18n) {
            Check(false, s, "no world I18n of its own (Tools > Chess Board > Create View Test)");
            return;
        }
        Check(worldI18n.SetLanguageGlobally("x-chess-test"), s + ": SetLanguageGlobally(x-chess-test) on the "
            + "world's I18n", "it returned false");
        CheckString(worldI18n._("world_greeting"), "Hello (world test)", s + ": the world's own text");
        CheckString(i18n.GetCurrentLanguage(), "x-chess-test", s + ": the Chess Language switched");
        CheckString(ui._ButtonText(ChessBoardUI.ButtonStart), "Begin (test)", s + ": Start");
        CheckString(PickupTexts(), "Shift (test)", s + ": the pieces' pickups");
        CheckString(OtherStartTexts(), "Begin (test)", s + ": Start on every other board");
        Check(!worldI18n.SetLanguageGlobally("en"), s + ": SetLanguageGlobally(en) false (the world has no en)",
            "it returned true");
        CheckString(i18n.GetCurrentLanguage(), "en", s + ": the Chess Language English again");
        CheckString(ui._ButtonText(ChessBoardUI.ButtonStart), "Start", s + ": English again: Start");
        CheckString(OtherStartTexts(), "Start", s + ": English again: Start on every other board");
    }

    // The Start button's text on every board's UI but this one's, when they're all the same; otherwise each,
    // separated by |. "(none)" without other boards.
    string OtherStartTexts() {
        if (otherUIs == null || otherUIs.Length == 0) return "(none)";
        string first = null;
        string all = "";
        bool same = true;
        for (int i = 0; i < otherUIs.Length; i++) {
            string text = Utilities.IsValid(otherUIs[i]) ? otherUIs[i]._ButtonText(ChessBoardUI.ButtonStart) : "(none)";
            if (first == null) first = text;
            else if (text != first) same = false;
            all += (i > 0 ? "|" : "") + text;
        }
        return same ? first : all;
    }

    // The pieces' pickups' hover text, when every one has the same; otherwise each, separated by |.
    string PickupTexts() {
        string first = null;
        string all = "";
        bool same = true;
        for (int obj = 0; obj < view.piecePickups.Length; obj++) {
            VRCPickup pickup = view.piecePickups[obj];
            string text = Utilities.IsValid(pickup) ? pickup.InteractionText : "(none)";
            if (first == null) first = text;
            else if (text != first) same = false;
            all += (obj > 0 ? "|" : "") + text;
        }
        return same && first != null ? first : all;
    }

    void CheckSource(AudioSource source, string name) {
        Check(Utilities.IsValid(source) && source.spatialBlend == 1 && !source.playOnAwake, name + ": spatial, not "
            + "playing on awake", Utilities.IsValid(source) ? "spatialBlend " + source.spatialBlend + ", playOnAwake "
            + source.playOnAwake : "none");
    }

    void CheckClip(AudioClip clip, string expected, string name) {
        CheckString(clip != null ? clip.name : "(none)", expected, name);
    }

    void MarkSounds() {
        viewSounds = view.soundsPlayed;
        uiSounds = ui.soundsPlayed;
    }

    // Whether, since the last check, the view played exactly viewClip and the UI exactly uiClip (none for null).
    void CheckSounds(AudioClip viewClip, AudioClip uiClip, string name) {
        int viewCount = view.soundsPlayed - viewSounds;
        int uiCount = ui.soundsPlayed - uiSounds;
        bool viewOk = viewClip == null ? viewCount == 0 : viewCount == 1 && view.lastSound == viewClip;
        bool uiOk = uiClip == null ? uiCount == 0 : uiCount == 1 && ui.lastSound == uiClip;
        Check(viewOk && uiOk, name + ": view " + ClipName(viewClip) + ", UI " + ClipName(uiClip), "view played "
            + viewCount + " (last " + ClipName(view.lastSound) + "), UI " + uiCount + " (last "
            + ClipName(ui.lastSound) + ")");
        MarkSounds();
    }

    string ClipName(AudioClip clip) {
        return clip != null ? clip.name : "nothing";
    }

    // Plays a move ("e2e4") on the board, then checks the sounds after the Refresh.
    void MoveSound(string move, AudioClip viewClip, AudioClip uiClip, string name) {
        MarkSounds();
        CheckInt(board.PieceDroppedAs(localName, Sq(move.Substring(0, 2)), Sq(move.Substring(2, 2))),
            ChessBoard.InputAccepted, name + ": played");
        Redraw();
        CheckSounds(viewClip, uiClip, name);
    }

    // Steps the replay to ply, then checks the view's sound after the Refresh (the UI's none).
    void StepSound(int ply, AudioClip viewClip, string name) {
        MarkSounds();
        CheckInt(board.StepToAs(localName, false, ply), ChessBoard.InputAccepted, name + ": stepped");
        Redraw();
        CheckSounds(viewClip, null, name);
    }

    // Presses a step button on the UI, landing on ply, then checks the view's sound after the Refresh (the UI's none,
    // no click).
    void PressStepSound(int action, int ply, AudioClip viewClip, string name) {
        MarkSounds();
        CheckInt(ui._PressButton(action), ChessBoard.InputAccepted, name + ": pressed");
        CheckInt(board.replayIndex, ply, name + ": at ply " + ply);
        Redraw();
        CheckSounds(viewClip, null, name);
    }

    // A free move in replay, then the view's sound after the Refresh.
    void FreeMoveSound(string from, string to, AudioClip viewClip, string name) {
        MarkSounds();
        CheckInt(board.PieceDroppedAs(localName, Sq(from), Sq(to)), ChessBoard.InputAccepted, name + ": played");
        Redraw();
        CheckSounds(viewClip, null, name);
    }

    // A promotion dropped through the view on a game continued from fen: landing on square plays landing at once,
    // choosing type plays chosen after the Refresh.
    void PromotionSound(string fen, string square, int type, AudioClip landing, AudioClip chosen, string name) {
        LoadFen(fen, name);
        ContinueLoaded(name);
        MarkSounds();
        DropOver("b7", square, name + ": dropped");
        CheckSounds(landing, null, name + ": landed");
        CheckInt(view._ChoosePromotion(type), ChessBoard.InputAccepted, name + ": chosen");
        Redraw();
        CheckSounds(chosen, null, name + ": chosen");
    }

    // Samples the time warning at server time now: whether it played.
    void WarningSound(double now, bool warns, string name) {
        MarkSounds();
        ui._CheckTimeWarningAt(now);
        CheckSounds(null, warns ? ui.timeWarningSound : null, name);
    }

    // No king fallen in the position shown, and every object standing, none falling.
    void CheckFalls(string name) {
        CheckInt(board.FallenKingSquare(), ChessRules.NoSquare, name + ": no king fallen");
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
        Check(view.fallTarget[obj] == 1 && view.fallAmount[obj] == 1 && !view._IsFalling(obj),
            name + ": the king on " + square + " fallen", FallText(obj));
        Vector3 up = PieceUp(obj);
        Check((up - direction).sqrMagnitude < 1e-6f, name + ": lying towards " + direction.ToString("F0"),
            "its top towards " + up.ToString("F3"));
        CheckInt(view._DropSquare(view._PieceBase(obj)), Sq(square), name + ": its base over " + square);
        float lowest = LowestPoint(obj);
        Check(Mathf.Abs(lowest - fallStanding) < 1e-5f, name + ": lying on the board",
            "lowest " + lowest.ToString("F5") + ", standing " + fallStanding.ToString("F5"));
        Check((view.pieces[obj].localPosition - view._ObjectPlace(obj)).sqrMagnitude < 1e-8f, name + ": in its place",
            "at " + view.pieces[obj].localPosition.ToString("F3"));
    }

    string FallText(int obj) {
        return "fallen " + view.fallAmount[obj].ToString("F3") + ", going to " + view.fallTarget[obj]
            + ", starting in " + (view.fallStart[obj] - Time.time).ToString("F3") + " s";
    }

    // Which way obj's top points (view space), its template's turn taken out: up when it stands.
    Vector3 PieceUp(int obj) {
        Quaternion template = Quaternion.identity;
        int color = view._ObjectColor(obj);
        ChessPieceSet set = view._AppliedPieceSet(color);
        int type = view.objectType[obj];
        if (Utilities.IsValid(set) && type != ChessRules.NoPieceType
            && Utilities.IsValid(WornTemplate(set, color, type, false))) {
            template = WornTurn(set, color, type, false);
        }
        return view.pieces[obj].localRotation * Quaternion.Inverse(template) * Vector3.up;
    }

    // Whether a ChessRules colour's piece (or promotion piece) of type in set falls back on White's template (step
    // 13.6): a Black one without a mesh.
    bool FallsBack(ChessPieceSet set, int color, int type, bool promotion) {
        if (color != ChessRules.Black) return false;
        return !ChessPieceSet._HasMesh(promotion ? set._PromotionTemplate(color, type) : set._Template(color, type));
    }

    // The template whose mesh and transform a colour's piece of type wears in set: its own, or White's when it falls
    // back (null for none).
    Transform WornTemplate(ChessPieceSet set, int color, int type, bool promotion) {
        int from = FallsBack(set, color, type, promotion) ? ChessRules.White : color;
        return promotion ? set._PromotionTemplate(from, type) : set._Template(from, type);
    }

    // How it stands turned: as the template is, half round more when Black falls back on White's.
    Quaternion WornTurn(ChessPieceSet set, int color, int type, bool promotion) {
        Quaternion turn = WornTemplate(set, color, type, promotion).localRotation;
        return FallsBack(set, color, type, promotion) ? Quaternion.Euler(0, 180, 0) * turn : turn;
    }

    // Its offset from the square's centre, turned the same way.
    Vector3 WornOffset(ChessPieceSet set, int color, int type, bool promotion) {
        Vector3 offset = WornTemplate(set, color, type, promotion).localPosition;
        return FallsBack(set, color, type, promotion) ? Quaternion.Euler(0, 180, 0) * offset : offset;
    }

    // Its materials: the template's own, or when falling back Black's own template's if it has a renderer with any,
    // otherwise White's.
    Material[] WornMaterials(ChessPieceSet set, int color, int type, bool promotion) {
        if (FallsBack(set, color, type, promotion)) {
            Transform own = promotion ? set._PromotionTemplate(color, type) : set._Template(color, type);
            MeshRenderer ownRenderer = Utilities.IsValid(own) ? own.GetComponent<MeshRenderer>() : null;
            if (Utilities.IsValid(ownRenderer)) {
                Material[] list = ownRenderer.sharedMaterials;
                for (int i = 0; i < list.Length; i++) {
                    if (Utilities.IsValid(list[i])) return list;
                }
            }
        }
        return WornTemplate(set, color, type, promotion).GetComponent<MeshRenderer>().sharedMaterials;
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
        board._ClearState();
        CheckInt(board._LoadGame(fen, new int[0], new string[0], new int[0], 0, ChessGame.ModeStandard, "", "", "*",
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
        view._OnPiecePickedUp(obj);
        Check(view.heldObject == obj, name + ": picked up", "not held");
        view.pieces[obj].localPosition = Over(to) + view._PieceOffset(obj);
        view._OnPieceDropped(obj);
    }

    // The centre of a square on the playing surface, in the world.
    Vector3 SquareWorld(int square) {
        return view.transform.TransformPoint(view._SquarePosition(square));
    }

    // The mesh the current set of its side gives piece (null for none).
    Mesh SetMesh(int piece) {
        int color = ChessRules.PieceColorOf(piece);
        ChessPieceSet set = view.CurrentPieceSet(color);
        if (!Utilities.IsValid(set)) return null;
        Transform template = WornTemplate(set, color, ChessRules.PieceTypeOf(piece), false);
        if (!Utilities.IsValid(template)) return null;
        MeshFilter filter = template.GetComponent<MeshFilter>();
        return Utilities.IsValid(filter) ? filter.sharedMesh : null;
    }

    // Whether whiteSet and blackSet are applied to their sides and every object standing for a piece wears its side's
    // template (WornTemplate: Black's own, or White's turned half round with Black's materials when it has none): the
    // mesh, every material slot, the turn (a fallen king's left to CheckFallen) and the size, with its collider fitted
    // to the mesh.
    void CheckPieceSets(ChessPieceSet whiteSet, ChessPieceSet blackSet, string name) {
        Check(view._AppliedPieceSet(ChessRules.White) == whiteSet, name + ": White's set applied", "another set is");
        Check(view._AppliedPieceSet(ChessRules.Black) == blackSet, name + ": Black's set applied", "another set is");
        int wrong = 0;
        string details = "";
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            int type = view.objectType[obj];
            if (type == ChessRules.NoPieceType) continue;
            int color = view._ObjectColor(obj);
            ChessPieceSet set = color == ChessRules.White ? whiteSet : blackSet;
            Transform template = WornTemplate(set, color, type, false);
            Transform piece = view.pieces[obj];
            Quaternion turn = WornTurn(set, color, type, false);
            Mesh mesh = template.GetComponent<MeshFilter>().sharedMesh;
            Material[] want = WornMaterials(set, color, type, false);
            Material[] have = view.pieceRenderers[obj].sharedMaterials;
            BoxCollider box = view.pieceColliders[obj];
            string error = "";
            if (view.pieceFilters[obj].sharedMesh != mesh) {
                error = "mesh";
            } else if (have.Length != want.Length) {
                error = have.Length + " materials, not " + want.Length;
            } else if (obj != view.heldObject && view.fallAmount[obj] == 0
                && Quaternion.Angle(piece.localRotation, turn) > 0.1f) {
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
        double before = board._ServerTime();
        Redraw();
        double after = board._ServerTime();
        double now = board.turnStart;
        if (players != null) CheckString(ui._PlayersTextAt(now), players, name + ": players");
        CheckString(ui._StatusTextAt(now), status, name + ": status");
        string playersBefore = ui._PlayersTextAt(before);
        string playersAfter = ui._PlayersTextAt(after);
        string statusBefore = ui._StatusTextAt(before);
        string statusAfter = ui._StatusTextAt(after);
        string errors = "";
        for (int i = 0; i < ui.statusLabels.Length; i++) {
            TextMeshProUGUI label = ui.statusLabels[i];
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
        CheckString(ui._EndingText(), expected, "status: ending " + ending + ", winner " + winner);
    }

    // ---- The expected texts, by key (PLAN.md 13.5a) ----

    string ColorName(int color) {
        return Text(color == ChessRules.White ? "color_white" : "color_black");
    }

    // A text naming a side: "White to move", "Black offers a draw".
    string ColorLine(string key, int color) {
        return TextWith(key, new string[] { "color", ColorName(color) });
    }

    // The players text, each seat's line ("White: Ana", null for an open seat).
    string Players(string white, string black) {
        return PlayersWithClocks(white, "", black, "");
    }

    // The players text with each side's clock after its name ("White: Ana  5:00"), "" for none.
    string PlayersWithClocks(string white, string whiteClock, string black, string blackClock) {
        return PlayerLine(ChessRules.White, white, whiteClock) + "\n" + PlayerLine(ChessRules.Black, black, blackClock);
    }

    string PlayerLine(int color, string name, string clock) {
        string line = TextWith("player_line", new string[] {
            "color", ColorName(color), "name", name == null ? Text("player_open") : name });
        return clock.Length > 0 ? line + "  " + clock : line;
    }

    // A time control in whole minutes: "5+3".
    string Clock(int minutes, int increment) {
        return TextWith("clock_minutes", new string[] {
            "minutes", minutes.ToString(), "increment", increment.ToString() });
    }

    // The lobby's status line for the mode's key and a time control.
    string Lobby(string modeKey, string clock) {
        return TextWith("status_lobby", new string[] { "mode", Text(modeKey), "clock", clock });
    }

    // The Mode button's label for the mode's key, and the Clock button's for a time control.
    string ModeLabel(string modeKey) {
        return TextWith("button_mode", new string[] { "mode", Text(modeKey) });
    }

    string ClockLabel(string clock) {
        return TextWith("button_clock", new string[] { "clock", clock });
    }

    // An ended game's line: the ending's text and the result, "White wins by checkmate (1-0)".
    string Ending(string endingKey, string result) {
        return TextWith("status_ending", new string[] { "ending", Text(endingKey), "result", result });
    }

    // A loaded game's line (status_loaded, or status_loaded_chess960) for the test PGNs, whose players are named
    // White and Black.
    string Loaded(string key) {
        return TextWith(key, new string[] { "white", "White", "black", "Black" });
    }

    // The replay line of the position after a move ("Replay: 2. Qh5+ (3 of 3)") and of the start.
    string ReplayLine(string move, string san, int ply, int count) {
        return TextWith("replay_move", new string[] {
            "move", move, "san", san, "ply", ply.ToString(), "count", count.ToString() });
    }

    string ReplayStart(int count) {
        return TextWith("replay_start", new string[] { "ply", "0", "count", count.ToString() });
    }

    // The PGN box's line while an import runs, moves read so far.
    string ImportRunning(int moves) {
        return TextWith("import_running", new string[] {
            "moves", moves == 1 ? Text("moves_one") : TextWith("moves_many", new string[] { "count", moves.ToString() })
        });
    }

    // The PGN box's line for an import failed at move (its label, "2.") for the reason's key.
    string ImportFailed(string move, string reasonKey) {
        return TextWith("import_failed", new string[] { "move", move, "reason", Text(reasonKey) });
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
        board._ClearState();
        importer._StartImport(pgn.text, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
        CheckInt(importer._ImportFor(float.MaxValue), ChessPgn.ImportDone, name + ": imported");
        return board._LoadImport(importer) == ChessBoard.InputAccepted;
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
        CheckTravelTo(obj, view._SquarePosition(Sq(square)), waits, name + ": travels to " + square);
    }

    // The same for a captured piece, to its captured place (step 12.4): once the mover has landed (waits), or at once
    // when the mover was dropped in place.
    void CheckTravelOff(int obj, bool waits, string name) {
        if (obj == ChessBoardView.NoObject) {
            Check(false, name + ": travels off the board", "no object");
            return;
        }
        CheckTravelTo(obj, view._ObjectPlace(obj), waits,
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
        view._Refresh();
        ui._Refresh();
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
            int shown = view._TargetMarkShown(square);
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
            } else if (want && (t.localPosition - view._SquarePosition(Sq(from))).sqrMagnitude > 1e-8f) {
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
            } else if (want && (t.localPosition - view._SquarePosition(Sq(from))).sqrMagnitude > 1e-8f) {
                errors += "line starting at " + t.localPosition.ToString("F3") + "; ";
            } else if (want && (end - view._SquarePosition(Sq(to))).sqrMagnitude > 1e-8f) {
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
            if ((position - view._SquarePosition(square)).sqrMagnitude < 1e-8f) return SquareText(square);
        }
        return "?";
    }

    // The material of obj's renderer (null for none).
    Material MaterialOf(GameObject obj) {
        if (!Utilities.IsValid(obj)) return null;
        MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
        return Utilities.IsValid(renderer) ? renderer.sharedMaterial : null;
    }

    // Whether the promotion ghosts show (with a promotion waiting) or are hidden (with none); shown, whether each
    // floats in its place over square (step 12.11): GhostHeight squares up, GhostSpacing apart, the row's middle over
    // the square but no further along the files than the view allows, knight to queen from left to right as the
    // promoting side sees it; and wears that side's current set's promotion template: its mesh, every material slot,
    // its turn, size and offset (WornTemplate: Black's falling back on White's turned half round), with a trigger
    // collider fitted to the mesh.
    void CheckGhosts(bool shown, string square, string name) {
        int active = 0;
        for (int i = 0; i < view.promotionGhosts.Length; i++) {
            if (Utilities.IsValid(view.promotionGhosts[i]) && view.promotionGhosts[i].gameObject.activeSelf) active++;
        }
        bool waiting = view.promotionObject != ChessBoardView.NoObject;
        Check(active == (shown ? ChessBoardView.GhostCount : 0) && waiting == shown && view._AreGhostsShown() == shown,
            name + ": the ghosts " + (shown ? "shown" : "hidden"),
            active + " active, waiting object " + view.promotionObject);
        if (!shown || active != ChessBoardView.GhostCount || !waiting) return;
        int color = view._ObjectColor(view.promotionObject);
        bool white = color == ChessRules.White;
        ChessPieceSet set = view.CurrentPieceSet(color);
        Vector3 middle = view._SquarePosition(Sq(square));
        float limit = ChessBoardView.GhostMaxOffset * view.squareSize;
        middle.x = Mathf.Clamp(middle.x, -limit, limit);
        int wrong = 0;
        string details = "";
        float lastAcross = float.MinValue;
        for (int ghost = 0; ghost < ChessBoardView.GhostCount; ghost++) {
            int type = ChessBoardView._GhostType(ghost);
            Transform template = Utilities.IsValid(set) ? WornTemplate(set, color, type, true) : null;
            Transform t = view.promotionGhosts[ghost].transform;
            string error = "";
            if (!Utilities.IsValid(template)) {
                error = "no promotion template";
            } else {
                Vector3 offset = WornOffset(set, color, type, true);
                float along = (ghost - 1.5f) * ChessBoardView.GhostSpacing * view.squareSize;
                Vector3 expected = middle + new Vector3(white ? along : -along,
                    ChessBoardView.GhostHeight * view.squareSize, 0) + offset;
                // Left to right as the promoting side sees it: along +x from White's end, along -x from Black's.
                float across = white ? t.localPosition.x - offset.x : offset.x - t.localPosition.x;
                Mesh mesh = template.GetComponent<MeshFilter>().sharedMesh;
                Material[] want = WornMaterials(set, color, type, true);
                Material[] have = t.GetComponent<MeshRenderer>().sharedMaterials;
                BoxCollider box = t.GetComponent<BoxCollider>();
                if ((t.localPosition - expected).sqrMagnitude > 1e-8f) {
                    error = "at " + t.localPosition.ToString("F3") + ", not " + expected.ToString("F3");
                } else if (across <= lastAcross) {
                    error = "out of order";
                } else if (t.GetComponent<MeshFilter>().sharedMesh != mesh) {
                    error = "mesh";
                } else if (have.Length != want.Length) {
                    error = have.Length + " materials, not " + want.Length;
                } else if (Quaternion.Angle(t.localRotation, WornTurn(set, color, type, true)) > 0.1f) {
                    error = "turned " + t.localRotation.eulerAngles.ToString("F1");
                } else if ((t.localScale - template.localScale).sqrMagnitude > 1e-8f) {
                    error = "scale " + t.localScale.ToString("F3");
                } else if (!Utilities.IsValid(box) || !box.isTrigger
                    || (box.center - mesh.bounds.center).sqrMagnitude > 1e-10f
                    || (box.size - mesh.bounds.size).sqrMagnitude > 1e-10f) {
                    error = "collider not a trigger fitted to the mesh";
                }
                for (int i = 0; error.Length == 0 && i < want.Length; i++) {
                    if (have[i] != want[i]) error = "material " + i;
                }
                lastAcross = across;
            }
            if (error.Length > 0 && wrong++ < 3) {
                details += ChessPieceSet._TypeName(ChessBoardView._GhostType(ghost)) + ": " + error + "; ";
            }
        }
        Check(wrong == 0, name + ": each ghost in its place over " + square + ", wearing its promotion template",
            wrong + " wrong: " + details);
    }

    // Clicks the promotion ghost of a piece type through its ChessPromotionGhost (as a VR pointer or a desktop click
    // would), and checks that the view took the choice.
    void ClickGhost(int type, string name) {
        ChessPromotionGhost found = null;
        for (int i = 0; i < view.promotionGhosts.Length; i++) {
            ChessPromotionGhost ghost = view.promotionGhosts[i];
            if (Utilities.IsValid(ghost) && ghost.pieceType == type) found = ghost;
        }
        if (!Utilities.IsValid(found)) {
            Check(false, name, "no ghost for " + ChessPieceSet._TypeName(type));
            return;
        }
        found.Interact();
        CheckInt(view.promotionObject, ChessBoardView.NoObject, name + ": nothing waits any more");
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
        CheckInt(ui._PressButton(action), ChessBoard.InputRejected, name + ": " + buttonNames[action] + " refused");
    }

    static bool IsStepAction(int action) {
        return action == ChessBoardUI.ButtonStepFirst || action == ChessBoardUI.ButtonStepBack
            || action == ChessBoardUI.ButtonStepForward || action == ChessBoardUI.ButtonStepLast;
    }

    // After a Refresh, the buttons shown are exactly the actions in expected (names from buttonNames, separated by
    // spaces), each shown one labelled as the view says; a name followed by "?" is shown as OptionalShown says, and
    // Mode and Clock are hidden when the snapshot forces them. The view's _ButtonApplies agrees. The step buttons are
    // the exception: all four are shown while the board's CanControlReplay holds, and those in expected are the ones
    // enabled (_ButtonEnabled and their UI Button interactable), the rest greyed out.
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
            // The step buttons are shown together while the replay can be controlled; a named one is enabled, the
            // others shown greyed out.
            bool enabled = want;
            if (IsStepAction(action)) want = board.CanControlReplay();
            if (button.activeSelf != want) {
                errors += actionName + (want ? " hidden; " : " shown; ");
            } else if (ui._ButtonApplies(action) != want) {
                errors += actionName + " applies " + ui._ButtonApplies(action) + "; ";
            } else if (want && IsStepAction(action)
                && (ui._ButtonEnabled(action) != enabled || ui._IsButtonInteractable(i) != enabled)) {
                errors += actionName + (enabled ? " disabled; " : " enabled; ");
            } else if (want && i < ui.buttonLabels.Length && Utilities.IsValid(ui.buttonLabels[i])
                && ui.buttonLabels[i].text != ui._ButtonText(action)) {
                errors += actionName + " labelled " + ui.buttonLabels[i].text + "; ";
            }
        }
        Check(errors.Length == 0, name + ": buttons shown " + expected, errors);
    }

    // Each Leave button (13.1) stands at its own seat's end: White's at -z in the UI's local space, Black's at +z.
    void CheckLeaveEnds() {
        string errors = "";
        int found = 0;
        for (int i = 0; i < ui.buttons.Length && i < ui.buttonActions.Length; i++) {
            int action = ui.buttonActions[i];
            if (action != ChessBoardUI.ButtonLeaveWhite && action != ChessBoardUI.ButtonLeaveBlack) continue;
            if (!Utilities.IsValid(ui.buttons[i])) continue;
            found++;
            float z = ui.transform.InverseTransformPoint(ui.buttons[i].transform.position).z;
            bool white = action == ChessBoardUI.ButtonLeaveWhite;
            if (white != (z < 0)) errors += buttonNames[action] + " at z " + z + "; ";
        }
        Check(found == 2 && errors.Length == 0, "Leave buttons: one at each seat's end", found + " found; " + errors);
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
        Vector3 expected = view._SquarePosition(Sq(square)) + view._PieceOffset(obj)
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
            bool piece = obj != ChessBoardView.NoObject && view._IsPieceClickable(obj);
            if (piece != (" " + pieceSquares + " ").Contains(text)) wrongPieces += SquareText(square) + " ";
            if (view._IsSquareClickable(square) != (" " + targetSquares + " ").Contains(text)) {
                wrongSquares += SquareText(square) + " ";
            }
        }
        int offBoard = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (view.objectSquare[obj] == ChessRules.NoSquare && view._IsPieceClickable(obj)) offBoard++;
        }
        Check(wrongPieces.Length == 0 && wrongSquares.Length == 0 && offBoard == 0, name + ": clickable pieces ["
            + pieceSquares + "], squares [" + targetSquares + "]", "pieces wrong on " + wrongPieces
            + "; squares wrong on " + wrongSquares + "; " + offBoard + " off the board");
    }

    // Whether the piece on square has the interact text of key (hover_select, …).
    void CheckText(int square, string key, string name) {
        string text = Text(key);
        int obj = view.squareObject[square];
        ChessPiece piece = obj == ChessBoardView.NoObject ? null : PieceBehaviour(obj);
        string actual = Utilities.IsValid(piece) ? piece.InteractionText : "(no piece)";
        Check(actual == text, name + ": labelled " + text, "labelled " + actual);
    }

    // Whether square's target is clickable with the interact text of key.
    void CheckSquareText(string square, string key, string name) {
        string text = Text(key);
        ChessSquare target = view.squareTargets[Sq(square)];
        string actual = Utilities.IsValid(target) ? target.InteractionText : "(no target)";
        Check(view._IsSquareClickable(Sq(square)) && actual == text, name + ": the square " + square + " labelled "
            + text, "clickable " + view._IsSquareClickable(Sq(square)) + ", labelled " + actual);
    }

    int ClickableCount() {
        int count = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (view._IsPieceClickable(obj)) count++;
        }
        return count;
    }

    int SquareTargetCount() {
        int count = 0;
        for (int square = 0; square < 64; square++) {
            if (view._IsSquareClickable(square)) count++;
        }
        return count;
    }

    // A game of the local player against themselves, from fen.
    void CastleGame(string fen, string name) {
        board._ClearState();
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
        view._OnPiecePickedUp(obj);
        int result = view.heldObject == obj ? ChessBoard.InputAccepted : ChessBoard.InputRejected;
        view.pieces[obj].localPosition = position;
        view._OnPieceDropped(obj);
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
        return view._SquarePosition(Sq(square)) + new Vector3(0.3f, 0.8f, -0.4f) * view.squareSize;
    }

    Vector3 OffBoard() {
        return new Vector3(6 * view.squareSize, 0.8f * view.squareSize, 0);
    }

    // Whether exactly count of the pieces are pickups for the local player, all of them on the board.
    void CheckPickupable(int count, string name) {
        CheckPickupableOff(count, 0, name);
    }

    // Whether exactly count of the pieces are pickups for the local player, all of them color's on the board.
    void CheckPickupableColor(int count, int color, string name) {
        int pickupable = 0;
        int ofColor = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (!Utilities.IsValid(view.piecePickups[obj]) || !view.piecePickups[obj].pickupable) continue;
            pickupable++;
            if (view.objectSquare[obj] != ChessRules.NoSquare && view._ObjectColor(obj) == color) ofColor++;
        }
        Check(pickupable == count && ofColor == count, name + ": " + count + " pickupable, all of colour " + color,
            pickupable + " pickupable, " + ofColor + " of them of colour " + color + " on the board");
    }

    // Whether exactly count of the pieces are pickups for the local player, off of them off the board (captured
    // pieces in replay, step 13.4).
    void CheckPickupableOff(int count, int off, string name) {
        int pickupable = 0;
        int offBoard = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (!Utilities.IsValid(view.piecePickups[obj]) || !view.piecePickups[obj].pickupable) continue;
            pickupable++;
            if (view.objectSquare[obj] == ChessRules.NoSquare) offBoard++;
        }
        Check(pickupable == count && offBoard == off, name + ": " + count + " pickupable, " + off + " off the board",
            pickupable + " pickupable, " + offBoard + " of them off the board");
    }

    // The object standing in color's captured place (NoObject for none).
    int PlaceObject(int color, int place) {
        int first = color == ChessRules.White ? 0 : ChessBoardView.PiecesPerColor;
        for (int obj = first; obj < first + ChessBoardView.PiecesPerColor; obj++) {
            if (view.objectSquare[obj] == ChessRules.NoSquare && view.objectPlace[obj] == place) return obj;
        }
        return ChessBoardView.NoObject;
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
        int mated = board.FallenKingSquare();
        for (int square = 0; square < 64; square++) {
            int piece = board.PieceAt(square);
            int obj = view.squareObject[square];
            string error = "";
            if (piece == ChessRules.Empty) {
                if (obj != ChessBoardView.NoObject) error = "object " + obj + " on the empty square";
            } else if (obj == ChessBoardView.NoObject) {
                error = "no object for " + PieceText(piece);
            } else if (view._ObjectPiece(obj) != piece) {
                error = "object " + obj + " stands for " + PieceText(view._ObjectPiece(obj)) + ", not " + PieceText(piece);
            } else if (view.objectSquare[obj] != square) {
                error = "object " + obj + " thinks it's on " + SquareText(view.objectSquare[obj]);
            } else if (view.pieceFilters[obj].sharedMesh != SetMesh(piece)) {
                error = "object " + obj + " has the wrong mesh";
            } else if (view.fallTarget[obj] != (square == mated ? 1 : 0)) {
                // Step 12.8: only the king mated in the position shown lies (or is on its way down).
                error = "object " + obj + (square == mated ? " isn't falling, mated" : " is falling, not mated");
            } else if (square != mated && (view.fallAmount[obj] != 0 || (PieceUp(obj) - Vector3.up).sqrMagnitude > 1e-6f)) {
                error = "object " + obj + " isn't upright (fallen " + view.fallAmount[obj] + ")";
            } else if (square == mated && (view.pieces[obj].localPosition - view._ObjectPlace(obj)).sqrMagnitude > 1e-8f) {
                error = "the mated king's object " + obj + " is at " + view.pieces[obj].localPosition.ToString("F3");
            } else if (square != mated && (view.pieces[obj].localPosition - view._SquarePosition(square)
                - view._PieceOffset(obj)).sqrMagnitude > 1e-8f) {
                error = "object " + obj + " is at " + view.pieces[obj].localPosition.ToString("F3");
            } else if (view._DropSquare(view._PieceBase(obj)) != square) {
                // In the world too, whatever the view's scale (step 12.2), from its base (step 12.3).
                error = "object " + obj + " stands over " + SquareText(view._DropSquare(view._PieceBase(obj)))
                    + " in the world";
            }
            if (error.Length > 0 && count++ < 3) errors += SquareText(square) + ": " + error + "; ";
        }
        // Off the board (step 12.4): shown in a captured place with a target, standing on it; otherwise hidden.
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (view.objectSquare[obj] != ChessRules.NoSquare || obj == view.promotionObject) continue;
            int color = view._ObjectColor(obj);
            int place = view.objectPlace[obj];
            bool active = view.pieces[obj].gameObject.activeSelf;
            string error = "";
            if (!view._IsObjectShown(obj)) {
                if (active) error = "is shown, with no place to stand in";
            } else if (!active) {
                error = "is hidden in place " + place;
            } else if (place >= view.capturedCounts[color]
                || view.capturedTypes[color * ChessBoardView.MaxCaptured + place] != view.objectType[obj]) {
                error = "stands in place " + place + ", which isn't its";
            } else if ((view.pieces[obj].localPosition - view._CapturedPlacePosition(color, place)
                - view._PieceOffset(obj)).sqrMagnitude > 1e-8f) {
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
}
