using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Step 7 test behaviour (PLAN.md section 7): the logical board driven with no world objects.
// Logs "[BoardTest] PASS: <check>" or "[BoardTest] FAIL: <check>: <details>" per check, then a summary line.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class BoardTest : UdonSharpBehaviour {
    // The board under test; its checks fail if it isn't assigned. The checks clear it and leave it in the reset state.
    public ChessBoard board;

    string localName;
    int passed;
    int failed;
    bool started;
    bool joined;
    bool ran;

    // The checks run a frame after both this Start and the local player joining: the board clears itself in its own
    // Start, which may run after this one, and the join buttons use the local player's name.
    void Start() {
        started = true;
        SendCustomEventDelayedFrames(nameof(RunChecks), 1);
    }

    public override void OnPlayerJoined(VRCPlayerApi player) {
        if (!player.isLocal) return;
        joined = true;
        SendCustomEventDelayedFrames(nameof(RunChecks), 1);
    }

    public void RunChecks() {
        if (ran || !started || !joined) return;
        ran = true;
        if (!Utilities.IsValid(board)) {
            Check(false, "board assigned", "the board field is empty");
        } else {
            localName = Networking.LocalPlayer.displayName;
            TestResetState();
            TestJoin();
            TestJoinBothSeats();
            TestSeatTaken();
            TestJoinInvalid();
            TestJoinByLifecycle();
            board.ClearState();
        }
        Debug.Log("[BoardTest] done: " + passed + " passed, " + failed + " failed"
            + (failed == 0 ? " ALL PASS" : " SOME FAIL"));
    }

    void Check(bool ok, string name, string details) {
        if (ok) {
            passed++;
            Debug.Log("[BoardTest] PASS: " + name);
        } else {
            failed++;
            Debug.Log("[BoardTest] FAIL: " + name + ": " + details);
        }
    }

    void CheckInt(int actual, int expected, string name) {
        Check(actual == expected, name, "expected " + expected + ", got " + actual);
    }

    void CheckText(string actual, string expected, string name) {
        Check(actual == expected, name, "expected \"" + expected + "\", got \"" + actual + "\"");
    }

    void CheckSeats(string white, string black, string name) {
        CheckText(board.whitePlayer, white, name + ": white seat");
        CheckText(board.blackPlayer, black, name + ": black seat");
    }

    // ---- 7.1a ----

    void TestResetState() {
        // Dirty a few fields first, so clearing has something to do.
        board.JoinAs(ChessRules.White, "Ana");
        board.lifecycle = ChessBoard.LifecycleEnded;
        board.ending = ChessGame.EndingCheckmate;
        board.winner = ChessRules.Black;
        board.moveCount = 3;
        board.gameId = "20261011120000-000001";
        board.board[ChessRules.E4] = ChessRules.WhitePawn;
        board.ClearState();

        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, "reset: lifecycle");
        CheckSeats("", "", "reset");
        CheckInt(board.ending, ChessGame.EndingNone, "reset: ending");
        CheckInt(board.winner, ChessRules.NoColor, "reset: winner");
        CheckInt(board.moveCount, 0, "reset: move count");
        CheckText(board.gameId, "", "reset: game ID");
        CheckText(board.continuedFrom, "", "reset: continued from");
        CheckInt(board.mode, ChessGame.ModeStandard, "reset: mode");
        Check(!board.replayMode && !board.replayCustom, "reset: not in replay", "replay flags set");
        CheckInt(board.takebackColor, ChessRules.NoColor, "reset: no takeback request");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "reset: no draw offer");
        CheckText(ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber),
            ChessFen.StartFen, "reset: standard starting position");
        CheckText(board.startFen, ChessFen.StartFen, "reset: starting FEN");
        bool notationEmpty = true;
        for (int i = 0; i < board.notation.Length; i++) {
            if (board.notation[i] != "") notationEmpty = false;
        }
        Check(notationEmpty, "reset: notation entries all \"\" (never null)", "an entry is null or not empty");
    }

    void TestJoin() {
        board.ClearState();
        CheckInt(board.JoinWhite(), ChessBoard.InputAccepted, "join white: accepted");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "join white: reset board enters the lobby");
        CheckSeats(localName, "", "join white");
        Check(board.IsSeated(ChessRules.White, localName), "join white: IsSeated", "not seated");

        board.ClearState();
        CheckInt(board.JoinBlack(), ChessBoard.InputAccepted, "join black first: accepted");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "join black first: reset board enters the lobby");
        CheckSeats("", localName, "join black first");
    }

    void TestJoinBothSeats() {
        board.ClearState();
        board.JoinWhite();
        CheckInt(board.JoinBlack(), ChessBoard.InputAccepted, "same player both seats: accepted");
        CheckSeats(localName, localName, "same player both seats");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "same player both seats: still lobby");
        CheckInt(board.JoinWhite(), ChessBoard.InputAccepted, "joining own seat again: accepted");
        CheckSeats(localName, localName, "joining own seat again");
    }

    void TestSeatTaken() {
        board.ClearState();
        CheckInt(board.JoinAs(ChessRules.Black, "Siggi"), ChessBoard.InputAccepted, "named join: accepted");
        CheckInt(board.JoinAs(ChessRules.Black, "Ana"), ChessBoard.InputRejected, "seat taken by another: rejected");
        // A lookalike name (Cyrillic і) is someone else.
        CheckInt(board.JoinAs(ChessRules.Black, "Siggі"), ChessBoard.InputRejected, "lookalike name: rejected");
        CheckInt(board.JoinAs(ChessRules.Black, "siggi"), ChessBoard.InputRejected, "other case: rejected");
        CheckSeats("", "Siggi", "seat taken");
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "other seat: accepted");
        CheckSeats("Ana", "Siggi", "both seats, two players");
        Check(!board.IsSeated(ChessRules.Black, "Siggі"), "IsSeated compares exactly", "lookalike counted as seated");
    }

    void TestJoinInvalid() {
        board.ClearState();
        CheckInt(board.JoinAs(ChessRules.NoColor, "Ana"), ChessBoard.InputRejected, "join no colour: rejected");
        CheckInt(board.JoinAs(2, "Ana"), ChessBoard.InputRejected, "join colour 2: rejected");
        CheckInt(board.JoinAs(ChessRules.White, ""), ChessBoard.InputRejected, "join empty name: rejected");
        CheckInt(board.JoinAs(ChessRules.White, null), ChessBoard.InputRejected, "join null name: rejected");
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, "rejected joins: still reset");
        CheckSeats("", "", "rejected joins");
    }

    void TestJoinByLifecycle() {
        // There's no Start yet (7.1b), so the lifecycle is set directly.
        board.ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        board.lifecycle = ChessBoard.LifecycleInGame;
        CheckInt(board.JoinBlack(), ChessBoard.InputRejected, "in game: join empty seat rejected");
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputRejected, "in game: join own seat rejected");
        CheckSeats("Ana", "", "in game");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "in game: lifecycle unchanged");

        board.lifecycle = ChessBoard.LifecycleEnded;
        CheckInt(board.JoinBlack(), ChessBoard.InputRejected, "ended: join rejected");
        CheckSeats("Ana", "", "ended");

        board.ClearState();
        board.lifecycle = ChessBoard.LifecycleLoaded;
        CheckInt(board.JoinWhite(), ChessBoard.InputAccepted, "loaded: join white accepted");
        CheckInt(board.JoinAs(ChessRules.Black, "Ana"), ChessBoard.InputAccepted, "loaded: join black accepted");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "loaded: stays loaded");
        CheckSeats(localName, "Ana", "loaded");
    }
}
