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
            NothingForced();
            TestResetState();
            TestJoin();
            TestJoinBothSeats();
            TestSeatTaken();
            TestJoinInvalid();
            TestJoinByLifecycle();
            TestSetMode();
            TestSetClock();
            TestForcedSettings();
            TestStart();
            TestChess960Positions();
            TestChess960Random();
            TestChess960Start();
            TestClocks();
            TestPieceInput();
            TestPromotion();
            TestCastling();
            TestLobbyButtons();
            TestResignAndDrawButtons();
            TestReplayButtons();
            TestTakebackButtons();
            TestContinueResetLoadButtons();
            TestLocalButtons();
            TestMateAndStalemate();
            TestResign();
            TestTimeForfeit();
            TestDrawAgreement();
            TestDrawClaims();
            TestAutomaticDraws();
            TestSnapshot();
            if (Utilities.IsValid(board.arbiter)) board.arbiter.ClearOverrides(board.boardNumber);
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

    // The 7.1 checks expect snapshots with nothing forced, whatever the arbiter's defaults: every setting bit of the
    // board's number is overridden off (the overrides are cleared after the checks).
    void NothingForced() {
        if (!Utilities.IsValid(board.arbiter)) return;
        board.arbiter.SetSettingOverride(board.boardNumber, ChessArbiter.OverrideSettingsMask, false);
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

    // ---- 7.1b ----

    // A board in the lobby with Ana as White and Bob as Black (no snapshot until 7.2, so nothing is forced).
    void LobbyWithTwoPlayers() {
        board.ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        board.JoinAs(ChessRules.Black, "Bob");
    }

    void TestSetMode() {
        board.ClearState();
        CheckInt(board.SetModeAs("Ana", ChessGame.ModeChess960), ChessBoard.InputRejected, "mode: reset board rejects");
        board.JoinAs(ChessRules.White, "Ana");
        CheckInt(board.SetModeAs("Ana", ChessGame.ModeChess960), ChessBoard.InputAccepted, "mode: white player sets it");
        CheckInt(board.mode, ChessGame.ModeChess960, "mode: now Chess960");
        CheckInt(board.SetModeAs("Bob", ChessGame.ModeStandard), ChessBoard.InputRejected, "mode: non-player rejected");
        CheckInt(board.SetModeAs("ana", ChessGame.ModeStandard), ChessBoard.InputRejected, "mode: other case rejected");
        CheckInt(board.SetModeAs("", ChessGame.ModeStandard), ChessBoard.InputRejected, "mode: empty name rejected");
        CheckInt(board.SetModeAs(null, ChessGame.ModeStandard), ChessBoard.InputRejected, "mode: null name rejected");
        CheckInt(board.mode, ChessGame.ModeChess960, "mode: unchanged by rejected changes");
        board.JoinAs(ChessRules.Black, "Bob");
        CheckInt(board.SetModeAs("Bob", ChessGame.ModeStandard), ChessBoard.InputAccepted, "mode: black player sets it");
        CheckInt(board.mode, ChessGame.ModeStandard, "mode: now Standard");
        CheckInt(board.SetModeAs("Bob", 2), ChessBoard.InputRejected, "mode: unknown mode 2 rejected");
        CheckInt(board.SetModeAs("Bob", -1), ChessBoard.InputRejected, "mode: unknown mode -1 rejected");
        CheckInt(board.mode, ChessGame.ModeStandard, "mode: unchanged by unknown modes");

        // The local player's button.
        CheckInt(board.SetMode(ChessGame.ModeChess960), ChessBoard.InputRejected, "mode: unseated local player rejected");
        board.ClearState();
        board.JoinWhite();
        CheckInt(board.SetMode(ChessGame.ModeChess960), ChessBoard.InputAccepted, "mode: seated local player sets it");
        CheckInt(board.mode, ChessGame.ModeChess960, "mode: set by the local player");

        // The mode of a loaded game is the game's own; once started or ended it's fixed.
        board.ClearState();
        board.lifecycle = ChessBoard.LifecycleLoaded;
        board.JoinAs(ChessRules.White, "Ana");
        CheckInt(board.SetModeAs("Ana", ChessGame.ModeChess960), ChessBoard.InputRejected, "mode: loaded board rejects");
        LobbyWithTwoPlayers();
        board.lifecycle = ChessBoard.LifecycleInGame;
        CheckInt(board.SetModeAs("Ana", ChessGame.ModeChess960), ChessBoard.InputRejected, "mode: in game rejects");
        board.lifecycle = ChessBoard.LifecycleEnded;
        CheckInt(board.SetModeAs("Ana", ChessGame.ModeChess960), ChessBoard.InputRejected, "mode: ended rejects");
        CheckInt(board.mode, ChessGame.ModeStandard, "mode: unchanged after the lobby");
    }

    void TestSetClock() {
        board.ClearState();
        CheckInt(board.SetClockAs("Ana", 300, 3), ChessBoard.InputRejected, "clock: reset board rejects");
        board.JoinAs(ChessRules.White, "Ana");
        CheckInt(board.SetClockAs("Ana", 300, 3), ChessBoard.InputAccepted, "clock: player sets 5+3");
        CheckInt(board.baseTime, 300, "clock: base 300");
        CheckInt(board.increment, 3, "clock: increment 3");
        CheckInt(board.SetClockAs("Bob", 60, 0), ChessBoard.InputRejected, "clock: non-player rejected");
        CheckInt(board.SetClockAs("Ana", ChessArbiter.NoClock, 0), ChessBoard.InputAccepted, "clock: no clock");
        CheckInt(board.baseTime, ChessArbiter.NoClock, "clock: base NoClock");
        CheckInt(board.increment, 0, "clock: no clock, increment 0");

        // Validation: each rejected value leaves the time control as it was.
        board.SetClockAs("Ana", 600, 5);
        CheckInt(board.SetClockAs("Ana", ChessArbiter.NoClock, 3), ChessBoard.InputRejected,
            "clock: no clock with an increment rejected");
        CheckInt(board.SetClockAs("Ana", 0, 0), ChessBoard.InputRejected, "clock: base 0 rejected");
        CheckInt(board.SetClockAs("Ana", -2, 0), ChessBoard.InputRejected, "clock: base -2 rejected");
        CheckInt(board.SetClockAs("Ana", ChessBoard.MaxBaseTime + 1, 0), ChessBoard.InputRejected,
            "clock: base over the maximum rejected");
        CheckInt(board.SetClockAs("Ana", 300, -1), ChessBoard.InputRejected, "clock: negative increment rejected");
        CheckInt(board.SetClockAs("Ana", 300, ChessBoard.MaxIncrement + 1), ChessBoard.InputRejected,
            "clock: increment over the maximum rejected");
        CheckInt(board.baseTime, 600, "clock: base unchanged by rejected values");
        CheckInt(board.increment, 5, "clock: increment unchanged by rejected values");
        CheckInt(board.SetClockAs("Ana", 1, 0), ChessBoard.InputAccepted, "clock: base 1 s accepted");
        CheckInt(board.SetClockAs("Ana", ChessBoard.MaxBaseTime, ChessBoard.MaxIncrement), ChessBoard.InputAccepted,
            "clock: the maximum accepted");

        // The local player's button.
        board.ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        CheckInt(board.SetClock(180, 2), ChessBoard.InputRejected, "clock: unseated local player rejected");
        board.JoinBlack();
        CheckInt(board.SetClock(180, 2), ChessBoard.InputAccepted, "clock: seated local player sets it");
        CheckInt(board.baseTime * 1000 + board.increment, 180002, "clock: 3+2 set by the local player");

        // A loaded board chooses the clock for continuing; once started or ended it's fixed.
        board.ClearState();
        board.lifecycle = ChessBoard.LifecycleLoaded;
        CheckInt(board.SetClockAs("Ana", 300, 0), ChessBoard.InputRejected, "clock: loaded, nobody seated, rejected");
        board.JoinAs(ChessRules.Black, "Ana");
        CheckInt(board.SetClockAs("Ana", 300, 0), ChessBoard.InputAccepted, "clock: loaded board accepts");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "clock: loaded board stays loaded");
        LobbyWithTwoPlayers();
        board.lifecycle = ChessBoard.LifecycleInGame;
        CheckInt(board.SetClockAs("Ana", 300, 0), ChessBoard.InputRejected, "clock: in game rejects");
        board.lifecycle = ChessBoard.LifecycleEnded;
        CheckInt(board.SetClockAs("Ana", 300, 0), ChessBoard.InputRejected, "clock: ended rejects");
        CheckInt(board.baseTime, ChessArbiter.NoClock, "clock: unchanged after the lobby");
    }

    void TestForcedSettings() {
        // The joins take a snapshot with nothing forced (NothingForced); here its fields are then set directly.
        LobbyWithTwoPlayers();
        Check(!board.IsModeForced() && !board.IsClockForced(), "forced: nothing forced in the snapshot",
            "a forced bit is on");
        board.gameSettings = ChessArbiter.SettingModeForced;
        board.gameNumbers[ChessArbiter.NumberForcedMode] = ChessGame.ModeChess960;
        Check(board.IsModeForced() && !board.IsClockForced(), "forced mode: IsModeForced", "wrong forced bits");
        CheckInt(board.SetModeAs("Ana", ChessGame.ModeStandard), ChessBoard.InputRejected, "forced mode: change rejected");
        CheckInt(board.SetModeAs("Ana", ChessGame.ModeChess960), ChessBoard.InputRejected,
            "forced mode: choosing the forced value rejected too");
        CheckInt(board.SetClockAs("Ana", 300, 3), ChessBoard.InputAccepted, "forced mode: clock still free");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, "forced mode: start");
        CheckInt(board.mode, ChessGame.ModeChess960, "forced mode: the game has the forced mode");
        CheckInt(board.baseTime * 1000 + board.increment, 300003, "forced mode: the players' clock kept");

        // A forced Standard overrides a Chess960 chosen before the snapshot.
        LobbyWithTwoPlayers();
        board.SetModeAs("Ana", ChessGame.ModeChess960);
        board.gameSettings = ChessArbiter.SettingModeForced;
        board.gameNumbers[ChessArbiter.NumberForcedMode] = ChessGame.ModeStandard;
        board.StartGameAs("Bob");
        CheckInt(board.mode, ChessGame.ModeStandard, "forced Standard: overrides the earlier choice");
        CheckText(board.startFen, ChessFen.StartFen, "forced Standard: standard starting position");

        LobbyWithTwoPlayers();
        board.SetModeAs("Ana", ChessGame.ModeChess960);
        board.SetClockAs("Ana", 60, 1);
        board.gameSettings = ChessArbiter.SettingClockForced;
        board.gameNumbers[ChessArbiter.NumberForcedBaseTime] = 600;
        board.gameNumbers[ChessArbiter.NumberForcedIncrement] = 5;
        Check(!board.IsModeForced() && board.IsClockForced(), "forced clock: IsClockForced", "wrong forced bits");
        CheckInt(board.SetClockAs("Ana", 300, 3), ChessBoard.InputRejected, "forced clock: change rejected");
        CheckInt(board.SetClockAs("Ana", 600, 5), ChessBoard.InputRejected, "forced clock: forced value rejected too");
        CheckInt(board.SetModeAs("Bob", ChessGame.ModeStandard), ChessBoard.InputAccepted, "forced clock: mode still free");
        board.StartGameAs("Ana");
        CheckInt(board.baseTime, 600, "forced clock: base");
        CheckInt(board.increment, 5, "forced clock: increment");
        CheckInt(board.whiteTime, 600000, "forced clock: white time");
        CheckInt(board.blackTime, 600000, "forced clock: black time");
        CheckInt(board.mode, ChessGame.ModeStandard, "forced clock: the players' mode kept");

        // Forced "no clock" ignores the forced increment.
        LobbyWithTwoPlayers();
        board.SetClockAs("Ana", 60, 1);
        board.gameSettings = ChessArbiter.SettingModeForced | ChessArbiter.SettingClockForced;
        board.gameNumbers[ChessArbiter.NumberForcedMode] = ChessGame.ModeStandard;
        board.gameNumbers[ChessArbiter.NumberForcedBaseTime] = ChessArbiter.NoClock;
        board.gameNumbers[ChessArbiter.NumberForcedIncrement] = 5;
        board.StartGameAs("Ana");
        CheckInt(board.baseTime, ChessArbiter.NoClock, "forced no clock: base");
        CheckInt(board.increment, 0, "forced no clock: increment 0");
        CheckInt(board.whiteTime + board.blackTime, 0, "forced no clock: no time on the clocks");

        // On a loaded board the forced clock blocks the choice too.
        board.ClearState();
        board.lifecycle = ChessBoard.LifecycleLoaded;
        board.JoinAs(ChessRules.White, "Ana");
        board.gameSettings = ChessArbiter.SettingClockForced;
        CheckInt(board.SetClockAs("Ana", 300, 0), ChessBoard.InputRejected, "forced clock: loaded board rejects");
    }

    void TestStart() {
        board.ClearState();
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputRejected, "start: reset board rejects");
        board.JoinAs(ChessRules.White, "Ana");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputRejected, "start: only white seated rejected");
        board.ClearState();
        board.JoinAs(ChessRules.Black, "Bob");
        CheckInt(board.StartGameAs("Bob"), ChessBoard.InputRejected, "start: only black seated rejected");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "start: rejected start stays in the lobby");

        LobbyWithTwoPlayers();
        board.SetClockAs("Ana", 300, 3);
        CheckInt(board.StartGameAs("Cleo"), ChessBoard.InputRejected, "start: non-player rejected");
        CheckInt(board.StartGameAs(""), ChessBoard.InputRejected, "start: empty name rejected");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "start: still lobby after rejected starts");
        double before = Networking.GetServerTimeInSeconds();
        CheckInt(board.StartGameAs("Bob"), ChessBoard.InputAccepted, "start: black player starts");
        double after = Networking.GetServerTimeInSeconds();
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "start: in game");
        CheckInt(board.mode, ChessGame.ModeStandard, "start: Standard");
        CheckText(ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber),
            ChessFen.StartFen, "start: standard starting position");
        CheckText(board.startFen, ChessFen.StartFen, "start: starting FEN");
        CheckInt(board.moveCount, 0, "start: no moves");
        CheckInt(board.whiteTime, 300000, "start: white has the base time");
        CheckInt(board.blackTime, 300000, "start: black has the base time");
        CheckInt(board.increment, 3, "start: increment kept");
        Check(board.turnStart >= before && board.turnStart <= after, "start: turn start is the server time now",
            "turn start " + board.turnStart + " not between " + before + " and " + after);
        CheckSeats("Ana", "Bob", "start");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputRejected, "start: second start rejected");
        CheckInt(board.SetModeAs("Ana", ChessGame.ModeChess960), ChessBoard.InputRejected, "start: mode fixed in game");
        CheckInt(board.JoinAs(ChessRules.White, "Cleo"), ChessBoard.InputRejected, "start: no joining in game");
        board.lifecycle = ChessBoard.LifecycleEnded;
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputRejected, "start: ended board rejects");

        // A loaded board continues with "Continue from here" (7.9b), not Start.
        board.ClearState();
        board.lifecycle = ChessBoard.LifecycleLoaded;
        board.JoinAs(ChessRules.White, "Ana");
        board.JoinAs(ChessRules.Black, "Bob");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputRejected, "start: loaded board rejects");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "start: loaded board stays loaded");

        // The local player's button, seated as both colours, with no clock.
        board.ClearState();
        board.JoinWhite();
        CheckInt(board.StartGame(), ChessBoard.InputRejected, "start: local player alone, one seat, rejected");
        board.JoinBlack();
        CheckInt(board.StartGame(), ChessBoard.InputAccepted, "start: local player in both seats starts");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "start: local start in game");
        CheckInt(board.baseTime, ChessArbiter.NoClock, "start: no clock by default");
        CheckInt(board.whiteTime + board.blackTime, 0, "start: no time on the clocks without a clock");
    }

    // ---- Chess960 positions ----

    // Checks a position as a Chess960 start without using the generator's method: pawns on their ranks, the middle
    // empty, Black mirroring White, the piece counts, bishops on opposite colours, the king between the rooks, and
    // the state (White to move, all castling rights, files and king squares). Returns "" if valid, otherwise what's
    // wrong.
    string Chess960Problem(byte[] b, int[] s) {
        for (int file = 0; file < 8; file++) {
            if (b[ChessRules.A2 + file] != ChessRules.WhitePawn) return "no white pawn on file " + file;
            if (b[ChessRules.A7 + file] != ChessRules.BlackPawn) return "no black pawn on file " + file;
            for (int rank = ChessRules.Rank3; rank <= ChessRules.Rank6; rank++) {
                if (b[rank * 8 + file] != ChessRules.Empty) return "piece in the middle";
            }
            int white = b[file];
            if (white == ChessRules.Empty || ChessRules.PieceColorOf(white) != ChessRules.White) {
                return "no white piece on file " + file;
            }
            if (b[ChessRules.A8 + file] != ChessRules.PieceOf(ChessRules.Black, ChessRules.PieceTypeOf(white))) {
                return "black doesn't mirror white on file " + file;
            }
        }
        int kings = 0;
        int queens = 0;
        int rooks = 0;
        int bishops = 0;
        int knights = 0;
        int kingFile = -1;
        int rook1 = -1;
        int rook2 = -1;
        int bishop1 = -1;
        int bishop2 = -1;
        for (int file = 0; file < 8; file++) {
            int type = ChessRules.PieceTypeOf(b[file]);
            if (type == ChessRules.King) {
                kings++;
                kingFile = file;
            } else if (type == ChessRules.Queen) {
                queens++;
            } else if (type == ChessRules.Rook) {
                rooks++;
                if (rook1 < 0) rook1 = file;
                else rook2 = file;
            } else if (type == ChessRules.Bishop) {
                bishops++;
                if (bishop1 < 0) bishop1 = file;
                else bishop2 = file;
            } else if (type == ChessRules.Knight) {
                knights++;
            }
        }
        if (kings != 1 || queens != 1 || rooks != 2 || bishops != 2 || knights != 2) return "wrong piece counts";
        if (((bishop1 + bishop2) & 1) == 0) return "bishops on the same colour";
        if (kingFile < rook1 || kingFile > rook2) return "king not between the rooks";
        if (s[ChessPosition.StateSideToMove] != ChessRules.White) return "black to move";
        if (s[ChessPosition.StateCastlingRights] != ChessRules.CastleAll) return "not all castling rights";
        if (s[ChessPosition.StateEnPassantFile] != -1) return "en passant file set";
        if (s[ChessPosition.StateKingFile] != kingFile) return "wrong king file in the state";
        if (s[ChessPosition.StateLeftRookFile] != rook1 || s[ChessPosition.StateRightRookFile] != rook2) {
            return "wrong rook files in the state";
        }
        if (s[ChessPosition.StateWhiteKingSquare] != kingFile
            || s[ChessPosition.StateBlackKingSquare] != ChessRules.A8 + kingFile) {
            return "wrong king squares in the state";
        }
        return "";
    }

    // The Scharnagl number of a valid Chess960 position, worked out from its back rank.
    int Chess960Number(byte[] b) {
        int light = 0;
        int dark = 0;
        int queen = 0;
        int knightA = -1;
        int knightB = -1;
        for (int file = 0; file < 8; file++) {
            if (ChessRules.PieceTypeOf(b[file]) != ChessRules.Bishop) continue;
            if ((file & 1) == 1) light = file / 2;
            else dark = file / 2;
        }
        int slot = 0;
        for (int file = 0; file < 8; file++) {
            int type = ChessRules.PieceTypeOf(b[file]);
            if (type == ChessRules.Bishop) continue;
            if (type == ChessRules.Queen) queen = slot;
            slot++;
        }
        slot = 0;
        for (int file = 0; file < 8; file++) {
            int type = ChessRules.PieceTypeOf(b[file]);
            if (type == ChessRules.Bishop || type == ChessRules.Queen) continue;
            if (type == ChessRules.Knight) {
                if (knightA < 0) knightA = slot;
                else knightB = slot;
            }
            slot++;
        }
        // The knight pairs in order: (0,1) (0,2) (0,3) (0,4) (1,2) (1,3) (1,4) (2,3) (2,4) (3,4).
        int knights = 0;
        for (int first = 0; first < 5; first++) {
            for (int second = first + 1; second < 5; second++) {
                if (first == knightA && second == knightB) return light + 4 * dark + 16 * queen + 96 * knights;
                knights++;
            }
        }
        return -1;
    }

    void TestChess960Positions() {
        CheckText(ChessGame.Chess960Fen(ChessGame.Chess960StandardIndex), ChessFen.StartFen,
            "960: position 518 is the standard position");
        CheckText(ChessGame.Chess960Fen(0), "bbqnnrkr/pppppppp/8/8/8/8/PPPPPPPP/BBQNNRKR w HFhf - 0 1",
            "960: position 0");
        CheckText(ChessGame.Chess960Fen(959), "rkrnnqbb/pppppppp/8/8/8/8/PPPPPPPP/RKRNNQBB w CAca - 0 1",
            "960: position 959");
        CheckText(ChessGame.Chess960Fen(-1), "", "960: index -1 gives \"\"");
        CheckText(ChessGame.Chess960Fen(ChessGame.Chess960Count), "", "960: index 960 gives \"\"");
        byte[] b = new byte[64];
        int[] s = new int[ChessPosition.StateLength];
        b[ChessRules.E4] = ChessRules.WhiteQueen;
        Check(!ChessGame.Chess960Position(ChessGame.Chess960Count, b, s) && b[ChessRules.E4] == ChessRules.WhiteQueen,
            "960: out of range leaves the board alone", "changed the board or returned true");

        // Every number: a valid position, numbered as Scharnagl does (so all 960 differ), and its FEN reads back
        // to the same position.
        int invalid = 0;
        int misnumbered = 0;
        int badFen = 0;
        string firstProblem = "";
        byte[] parsed = new byte[64];
        int[] parsedState = new int[ChessPosition.StateLength];
        for (int i = 0; i < ChessGame.Chess960Count; i++) {
            ChessGame.Chess960Position(i, b, s);
            string problem = Chess960Problem(b, s);
            if (problem != "") {
                if (firstProblem == "") firstProblem = i + ": " + problem;
                invalid++;
                continue;
            }
            int numbered = Chess960Number(b);
            if (numbered != i) {
                if (firstProblem == "") firstProblem = i + ": numbered " + numbered;
                misnumbered++;
            }
            string fen = ChessGame.Chess960Fen(i);
            int clock;
            int number;
            bool same = ChessFen.ParseFen(fen, parsed, parsedState, out clock, out number) && clock == 0
                && number == 1;
            for (int sq = 0; sq < 64 && same; sq++) same = parsed[sq] == b[sq];
            for (int k = 0; k < ChessPosition.StateLength && same; k++) same = parsedState[k] == s[k];
            if (!same) {
                if (firstProblem == "") firstProblem = i + ": FEN " + fen;
                badFen++;
            }
        }
        string details = firstProblem == "" ? "" : " (first: " + firstProblem + ")";
        CheckInt(invalid, 0, "960: all 960 positions valid" + details);
        CheckInt(misnumbered, 0, "960: all 960 positions distinct, numbered as Scharnagl" + details);
        CheckInt(badFen, 0, "960: every FEN reads back to its position" + details);
    }

    void TestChess960Random() {
        byte[] b = new byte[64];
        int[] s = new int[ChessPosition.StateLength];
        bool[] seen = new bool[ChessGame.Chess960Count];
        int distinct = 0;
        int invalid = 0;
        string firstProblem = "";
        for (int i = 0; i < 1000; i++) {
            int index = board.RandomChess960Index();
            string problem = "";
            if (!ChessGame.Chess960Position(index, b, s)) problem = "index " + index + " out of range";
            else problem = Chess960Problem(b, s);
            if (problem != "") {
                if (firstProblem == "") firstProblem = problem;
                invalid++;
                continue;
            }
            int n = Chess960Number(b);
            if (!seen[n]) distinct++;
            seen[n] = true;
        }
        CheckInt(invalid, 0, "960: 1000 random positions all valid" + (firstProblem == "" ? "" : " (first: "
            + firstProblem + ")"));
        // 1000 draws from 960 give about 620 distinct positions; fewer than 500 would mean a skewed generator.
        Check(distinct >= 500, "960: 1000 random positions, many distinct (" + distinct + ")",
            "only " + distinct + " distinct");
    }

    void TestChess960Start() {
        bool[] seen = new bool[ChessGame.Chess960Count];
        int distinct = 0;
        int bad = 0;
        string firstProblem = "";
        for (int i = 0; i < 20; i++) {
            LobbyWithTwoPlayers();
            board.SetModeAs("Ana", ChessGame.ModeChess960);
            string problem = "";
            if (board.StartGameAs("Ana") != ChessBoard.InputAccepted || board.lifecycle != ChessBoard.LifecycleInGame
                || board.mode != ChessGame.ModeChess960) {
                problem = "not in a Chess960 game";
            }
            if (problem == "") problem = Chess960Problem(board.board, board.state);
            if (problem == "" && board.startFen != ChessFen.WriteFen(board.board, board.state, 0, 1)) {
                problem = "starting FEN " + board.startFen;
            }
            if (problem == "" && (board.halfmoveClock != 0 || board.moveNumber != 1 || board.startHalfmoveClock != 0
                || board.startMoveNumber != 1)) {
                problem = "counters not 0 and 1";
            }
            for (int sq = 0; sq < 64 && problem == ""; sq++) {
                if (board.replayBoard[sq] != board.board[sq]) problem = "replay board differs";
            }
            if (problem != "") {
                if (firstProblem == "") firstProblem = "start " + i + ": " + problem;
                bad++;
                continue;
            }
            int n = Chess960Number(board.board);
            if (!seen[n]) distinct++;
            seen[n] = true;
        }
        CheckInt(bad, 0, "960 start: 20 games start from valid Chess960 positions"
            + (firstProblem == "" ? "" : " (first: " + firstProblem + ")"));
        Check(distinct >= 2, "960 start: different positions across starts (" + distinct + ")", "always the same");
    }

    // ---- 7.2 ----

    // Overrides every setting of the board's number on its arbiter: the bits in settings on, the rest off, and the
    // five numbers.
    void OverrideAll(int settings, int resetAvailable, int autoReset, int forcedMode, int forcedBase, int forcedInc) {
        ChessArbiter arbiter = board.arbiter;
        int n = board.boardNumber;
        arbiter.SetSettingOverride(n, ChessArbiter.OverrideSettingsMask, false);
        arbiter.SetSettingOverride(n, settings, true);
        arbiter.SetNumberOverride(n, ChessArbiter.NumberResetAvailableTime, resetAvailable);
        arbiter.SetNumberOverride(n, ChessArbiter.NumberAutoResetTime, autoReset);
        arbiter.SetNumberOverride(n, ChessArbiter.NumberForcedMode, forcedMode);
        arbiter.SetNumberOverride(n, ChessArbiter.NumberForcedBaseTime, forcedBase);
        arbiter.SetNumberOverride(n, ChessArbiter.NumberForcedIncrement, forcedInc);
    }

    // Checks the board's snapshot against expected bits and numbers.
    void CheckSnapshot(int settings, int resetAvailable, int autoReset, int forcedMode, int forcedBase, int forcedInc,
            string name) {
        CheckText(ChessArbiter.SettingsToString(board.gameSettings), ChessArbiter.SettingsToString(settings),
            name + ": setting bits");
        int[] expected = new int[ChessArbiter.NumberCount];
        expected[ChessArbiter.NumberResetAvailableTime] = resetAvailable;
        expected[ChessArbiter.NumberAutoResetTime] = autoReset;
        expected[ChessArbiter.NumberForcedMode] = forcedMode;
        expected[ChessArbiter.NumberForcedBaseTime] = forcedBase;
        expected[ChessArbiter.NumberForcedIncrement] = forcedInc;
        CheckText(ChessArbiter.NumbersToString(board.gameNumbers), ChessArbiter.NumbersToString(expected),
            name + ": numbers");
    }

    // The snapshot is taken when the board leaves the reset state and kept until it's reset; changing the arbiter
    // doesn't affect it, and a re-snapshot by an organizer picks the change up. Works on the overrides of the board's
    // number (cleared at the end), so the arbiter's defaults don't matter except where they're the point.
    void TestSnapshot() {
        Check(Utilities.IsValid(board.arbiter), "snapshot: the board's arbiter is assigned", "the Arbiter field is empty");
        if (!Utilities.IsValid(board.arbiter)) return;
        ChessArbiter arbiter = board.arbiter;
        int n = board.boardNumber;
        Check(arbiter.HasBoard(n), "snapshot: the board's number has overrides",
            "board number " + n + " is outside 0 to " + (arbiter.BoardCount() - 1));
        if (!arbiter.HasBoard(n)) return;
        Check(arbiter.IsOrganizer(Networking.LocalPlayer), "snapshot: the local player is an organizer",
            "the arbiter's simpleAuth doesn't authorize the local player; the re-snapshot checks need it to");

        int s1 = ChessArbiter.SettingTakebacks | ChessArbiter.SettingDrawAgreement | ChessArbiter.SettingFiftyMoveRule;
        int s2 = ChessArbiter.SettingsDrawRules | ChessArbiter.SettingModeForced | ChessArbiter.SettingClockForced;

        // A reset board has no snapshot, even with settings to copy.
        OverrideAll(s1, 30, 300, ChessGame.ModeChess960, 180, 2);
        board.ClearState();
        CheckSnapshot(0, 0, 0, 0, 0, 0, "snapshot: none on a reset board");

        // The first join takes it.
        board.JoinAs(ChessRules.White, "Ana");
        CheckSnapshot(s1, 30, 300, ChessGame.ModeChess960, 180, 2, "snapshot: taken on the first join");
        Check(board.IsGameSettingOn(ChessArbiter.SettingTakebacks)
            && !board.IsGameSettingOn(ChessArbiter.SettingThreefoldRepetition), "snapshot: IsGameSettingOn",
            "wrong answer");
        CheckInt(board.GameNumber(ChessArbiter.NumberAutoResetTime), 300, "snapshot: GameNumber");
        CheckInt(board.mode, ChessGame.ModeStandard, "snapshot: forced mode value unused while not forced");
        CheckInt(board.baseTime, ChessArbiter.NoClock, "snapshot: forced clock value unused while not forced");

        // Changing the arbiter afterwards doesn't affect the board: not on the second join, not on Start.
        OverrideAll(s2, 60, 900, ChessGame.ModeChess960, 300, 0);
        CheckSnapshot(s1, 30, 300, ChessGame.ModeChess960, 180, 2, "snapshot: arbiter changed, board unchanged");
        board.JoinAs(ChessRules.Black, "Bob");
        CheckSnapshot(s1, 30, 300, ChessGame.ModeChess960, 180, 2, "snapshot: second join keeps it");
        Check(!board.IsModeForced() && !board.IsClockForced(), "snapshot: the arbiter's new forcing not applied",
            "a forced bit is on");
        CheckInt(board.SetClockAs("Ana", 60, 1), ChessBoard.InputAccepted, "snapshot: clock still free");
        board.StartGameAs("Ana");
        CheckSnapshot(s1, 30, 300, ChessGame.ModeChess960, 180, 2, "snapshot: Start keeps it");
        CheckInt(board.mode, ChessGame.ModeStandard, "snapshot: Start uses the players' mode");
        CheckInt(board.baseTime * 1000 + board.increment, 60001, "snapshot: Start uses the players' clock");

        // A re-snapshot by an organizer picks the change up; a game in progress keeps its mode and clock.
        CheckInt(board.Resnapshot(), ChessBoard.InputAccepted, "re-snapshot: in game, accepted");
        CheckSnapshot(s2, 60, 900, ChessGame.ModeChess960, 300, 0, "re-snapshot: in game");
        CheckInt(board.mode, ChessGame.ModeStandard, "re-snapshot: in game, mode kept");
        CheckInt(board.baseTime * 1000 + board.increment, 60001, "re-snapshot: in game, clock kept");
        CheckInt(board.whiteTime, 60000, "re-snapshot: in game, time kept");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "re-snapshot: in game, still in game");
        CheckSeats("Ana", "Bob", "re-snapshot: in game");

        // Refused for a non-organizer (the arbiter's simpleAuth emptied for a moment), for no player, and on a reset
        // board.
        OverrideAll(s1, 30, 300, ChessGame.ModeChess960, 180, 2);
        SiggiUSTools.SimpleAuth auth = arbiter.simpleAuth;
        arbiter.simpleAuth = null;
        CheckInt(board.Resnapshot(), ChessBoard.InputRejected, "re-snapshot: non-organizer rejected");
        arbiter.simpleAuth = auth;
        CheckInt(board.ResnapshotAs(null), ChessBoard.InputRejected, "re-snapshot: no player rejected");
        CheckSnapshot(s2, 60, 900, ChessGame.ModeChess960, 300, 0, "re-snapshot: rejected ones change nothing");
        board.ClearState();
        CheckInt(board.Resnapshot(), ChessBoard.InputRejected, "re-snapshot: reset board rejected");
        CheckSnapshot(0, 0, 0, 0, 0, 0, "re-snapshot: reset board still has none");

        // On an ended board it's accepted.
        LobbyWithTwoPlayers();
        board.StartGameAs("Ana");
        board.lifecycle = ChessBoard.LifecycleEnded;
        OverrideAll(ChessArbiter.SettingDeadPosition, 10, 20, ChessGame.ModeStandard, ChessArbiter.NoClock, 0);
        CheckInt(board.Resnapshot(), ChessBoard.InputAccepted, "re-snapshot: ended, accepted");
        CheckSnapshot(ChessArbiter.SettingDeadPosition, 10, 20, ChessGame.ModeStandard, ChessArbiter.NoClock, 0,
            "re-snapshot: ended");

        // Forced settings are applied when the snapshot is taken, so the lobby shows them.
        OverrideAll(ChessArbiter.SettingModeForced | ChessArbiter.SettingClockForced, 30, 300, ChessGame.ModeChess960,
            300, 5);
        board.ClearState();
        board.JoinAs(ChessRules.Black, "Bob");
        CheckInt(board.mode, ChessGame.ModeChess960, "snapshot: forced mode shown in the lobby");
        CheckInt(board.baseTime * 1000 + board.increment, 300005, "snapshot: forced clock shown in the lobby");
        Check(board.IsModeForced() && board.IsClockForced(), "snapshot: forced bits from the arbiter",
            "a forced bit is off");

        // A re-snapshot in the lobby applies new forcing and lifts old forcing (the shown values stay as choices).
        board.JoinAs(ChessRules.White, "Ana");
        OverrideAll(ChessArbiter.SettingClockForced, 30, 300, ChessGame.ModeStandard, 900, 10);
        CheckInt(board.Resnapshot(), ChessBoard.InputAccepted, "re-snapshot: lobby, accepted");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "re-snapshot: lobby, still lobby");
        CheckInt(board.baseTime * 1000 + board.increment, 900010, "re-snapshot: lobby, new forced clock");
        CheckInt(board.mode, ChessGame.ModeChess960, "re-snapshot: lobby, mode left as it was");
        CheckInt(board.SetModeAs("Ana", ChessGame.ModeStandard), ChessBoard.InputAccepted,
            "re-snapshot: lobby, mode no longer forced");
        CheckInt(board.SetClockAs("Ana", 60, 0), ChessBoard.InputRejected, "re-snapshot: lobby, clock now forced");
        board.StartGameAs("Bob");
        CheckInt(board.baseTime * 1000 + board.increment, 900010, "re-snapshot: lobby, the game has the forced clock");

        // On a loaded board (loading, step 7.9a, calls TakeSnapshot) the forced clock is applied but not the forced
        // mode: the mode is the loaded game's. Joining it doesn't take another.
        board.ClearState();
        board.lifecycle = ChessBoard.LifecycleLoaded;
        board.mode = ChessGame.ModeChess960;
        int forcedBoth = ChessArbiter.SettingModeForced | ChessArbiter.SettingClockForced;
        OverrideAll(forcedBoth, 30, 300, ChessGame.ModeStandard, 120, 1);
        board.TakeSnapshot();
        CheckSnapshot(forcedBoth, 30, 300, ChessGame.ModeStandard, 120, 1, "snapshot: loaded board");
        CheckInt(board.mode, ChessGame.ModeChess960, "snapshot: loaded board keeps the game's mode");
        CheckInt(board.baseTime * 1000 + board.increment, 120001, "snapshot: loaded board shows the forced clock");
        OverrideAll(s1, 30, 300, ChessGame.ModeChess960, 180, 2);
        board.JoinAs(ChessRules.White, "Ana");
        CheckSnapshot(forcedBoth, 30, 300, ChessGame.ModeStandard, 120, 1, "snapshot: joining a loaded board keeps it");

        // A conflicting override is corrected in the snapshot, as in EffectiveNumbers: auto-reset 60 clamps the
        // reset-available time 120 to 60.
        OverrideAll(0, 120, 60, ChessGame.ModeStandard, ChessArbiter.NoClock, 0);
        board.ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        CheckSnapshot(0, 60, 60, ChessGame.ModeStandard, ChessArbiter.NoClock, 0, "snapshot: numbers corrected");

        // With no overrides the snapshot is the arbiter's defaults, and changing the defaults afterwards doesn't
        // affect it either (they're swapped for a moment and restored).
        arbiter.ClearOverrides(n);
        int defaults = arbiter.defaultSettings;
        int[] defaultNumbers = arbiter.defaultNumbers;
        int[] effective = new int[ChessArbiter.NumberCount];
        arbiter.EffectiveNumbers(n, effective);
        board.ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        CheckSnapshot(defaults, effective[0], effective[1], effective[2], effective[3], effective[4],
            "snapshot: the defaults without overrides");
        int[] changed = new int[ChessArbiter.NumberCount];
        changed[ChessArbiter.NumberResetAvailableTime] = 5;
        changed[ChessArbiter.NumberAutoResetTime] = 7;
        changed[ChessArbiter.NumberForcedMode] = ChessGame.ModeChess960;
        changed[ChessArbiter.NumberForcedBaseTime] = 42;
        changed[ChessArbiter.NumberForcedIncrement] = 3;
        int flipped = ~defaults & ChessArbiter.OverrideSettingsMask;
        arbiter.defaultSettings = flipped;
        arbiter.defaultNumbers = changed;
        CheckSnapshot(defaults, effective[0], effective[1], effective[2], effective[3], effective[4],
            "snapshot: defaults changed, board unchanged");
        CheckInt(board.Resnapshot(), ChessBoard.InputAccepted, "re-snapshot: defaults, accepted");
        CheckSnapshot(flipped, 5, 7, ChessGame.ModeChess960, 42, 3, "re-snapshot: picks up the changed defaults");
        arbiter.defaultSettings = defaults;
        arbiter.defaultNumbers = defaultNumbers;

        // A board number without overrides gets the defaults, whatever the board's own number has.
        OverrideAll(s1, 30, 300, ChessGame.ModeChess960, 180, 2);
        board.boardNumber = arbiter.BoardCount();
        board.ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        CheckSnapshot(defaults, effective[0], effective[1], effective[2], effective[3], effective[4],
            "snapshot: board number out of range gets the defaults");
        board.boardNumber = n;

        // With no arbiter, everything off, and no re-snapshot.
        board.arbiter = null;
        board.ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        CheckSnapshot(0, ChessArbiter.Never, ChessArbiter.Never, ChessGame.ModeStandard, ChessArbiter.NoClock, 0,
            "snapshot: no arbiter, everything off");
        CheckInt(board.Resnapshot(), ChessBoard.InputRejected, "re-snapshot: no arbiter rejected");
        board.arbiter = arbiter;

        arbiter.ClearOverrides(n);
        board.ClearState();
    }

    // ---- 7.3 clocks ----

    // The legal move from one square to another in the board's position (the first one, so a promotion isn't
    // chosen), 0 if there's none.
    int FindMove(int from, int to) {
        int[] moves = new int[ChessMoves.MaxMoves];
        int count = ChessMoves.GenerateLegalMoves(board.board, board.state, moves, true);
        for (int i = 0; i < count; i++) {
            if (ChessRules.MoveFrom(moves[i]) == from && ChessRules.MoveTo(moves[i]) == to) return moves[i];
        }
        return 0;
    }

    // A game between Ana and Bob with this time control, started, with its turn start set to t.
    void StartedGame(int baseSeconds, int incrementSeconds, double t) {
        LobbyWithTwoPlayers();
        board.SetClockAs("Ana", baseSeconds, incrementSeconds);
        board.StartGameAs("Ana");
        board.turnStart = t;
    }

    void CheckTimes(int white, int black, double now, string name) {
        CheckInt(board.RemainingTimeAt(ChessRules.White, now), white, name + ": white's remaining time");
        CheckInt(board.RemainingTimeAt(ChessRules.Black, now), black, name + ": black's remaining time");
    }

    // Plays from-to at server time now, checking it's legal and accepted.
    void Play(int from, int to, double now, string name) {
        int move = FindMove(from, to);
        Check(move != 0, name + ": move found",
            ChessRules.SquareName(from) + ChessRules.SquareName(to) + " isn't legal");
        CheckInt(board.PlayMoveAt(move, now), ChessBoard.InputAccepted, name + ": accepted");
    }

    // The times are binary fractions of a second, so the milliseconds come out exact.
    void TestClocks() {
        NothingForced();

        StartedGame(60, 2, 1000);
        Check(board.HasClock() && board.IsClockRunning(), "clock: running after start", "not running");
        CheckTimes(60000, 60000, 1000, "clock: at the turn start");
        CheckTimes(57500, 60000, 1002.5, "clock: white's runs, black's doesn't");
        CheckTimes(60000, 60000, 999, "clock: a time before the turn start counts as none passed");
        CheckInt(board.FlaggedColorAt(1059.5), ChessRules.NoColor, "clock: 0.5 s left is no flag fall");

        Play(ChessRules.E2, ChessRules.E4, 1002.5, "clock: 1. e4 after 2.5 s");
        CheckInt(board.whiteTime, 59500, "clock: 1. e4 takes 2.5 s and adds 2 s");
        CheckInt(board.clockHistory[0], 59500, "clock: history entry for 1. e4");
        Check(board.turnStart == 1002.5, "clock: turn start reset to the move's time", "turn start " + board.turnStart);
        CheckInt(board.moveCount, 1, "clock: one move");
        CheckText(board.notation[0], "e4", "clock: SAN recorded");
        CheckInt(board.state[ChessPosition.StateSideToMove], ChessRules.Black, "clock: black to move");
        CheckTimes(59500, 52500, 1010, "clock: black's runs, white's stopped");

        Play(ChessRules.E7, ChessRules.E5, 1010.25, "clock: 1... e5 after 7.75 s");
        CheckInt(board.blackTime, 54250, "clock: 1... e5 takes 7.75 s and adds 2 s");
        CheckInt(board.moveNumber, 2, "clock: move number after black's move");
        Play(ChessRules.G1, ChessRules.F3, 1010.25, "clock: 2. Nf3 at once");
        CheckInt(board.whiteTime, 61500, "clock: the increment can take the time over the base");
        CheckInt(board.halfmoveClock, 1, "clock: halfmove clock after a knight move");
        CheckInt(board.halfmoveClocks[2], 0, "clock: halfmove clock before 2. Nf3");
        CheckText(board.notation[2], "Nf3", "clock: SAN of 2. Nf3");

        // One history entry per move, and none after.
        CheckInt(board.moveCount, 3, "clock: three moves");
        CheckInt(board.clockHistory[0], 59500, "clock: history 1");
        CheckInt(board.clockHistory[1], 54250, "clock: history 2");
        CheckInt(board.clockHistory[2], 61500, "clock: history 3");
        CheckInt(board.clockHistory[3], -1, "clock: no history entry past the last move");

        // Replay pauses moves, not time.
        board.replayMode = true;
        CheckTimes(61500, 53250, 1011.25, "clock: still running in replay");
        int nc6 = FindMove(ChessRules.B8, ChessRules.C6);
        CheckInt(board.PlayMoveAt(nc6, 1011.25), ChessBoard.InputRejected, "clock: no move in replay");
        board.replayMode = false;

        // Black has 54.25 s from 1010.25, so the flag falls at 1064.5.
        CheckInt(board.FlaggedColorAt(1064), ChessRules.NoColor, "clock: 0.5 s left, no flag fall");
        CheckInt(board.FlaggedColorAt(1064.5), ChessRules.Black, "clock: black's flag falls at 0");
        CheckInt(board.FlaggedColorAt(5000), ChessRules.Black, "clock: and stays fallen");
        CheckTimes(61500, 0, 5000, "clock: out of time shows 0, not less");
        CheckInt(board.PlayMoveAt(nc6, 1064.5), ChessBoard.InputRejected, "clock: no move once the flag has fallen");
        CheckInt(board.moveCount, 3, "clock: refused move not recorded");
        CheckInt(board.blackTime, 54250, "clock: refused move leaves the time");
        Check(board.turnStart == 1010.25, "clock: refused move leaves the turn start", "turn start " + board.turnStart);
        Play(ChessRules.B8, ChessRules.C6, 1064.25, "clock: 2... Nc6 with 0.25 s left");
        CheckInt(board.blackTime, 2250, "clock: 0.25 s left plus 2 s");
        CheckInt(board.FlaggedColorAt(1064.5), ChessRules.NoColor, "clock: no flag fall after moving in time");

        // Moves that aren't legal.
        CheckInt(board.PlayMoveAt(FindMove(ChessRules.E4, ChessRules.E5), 1065), ChessBoard.InputRejected,
            "clock: no move (0) refused");
        int illegal = ChessRules.PackMove(ChessRules.D2, ChessRules.D5, ChessRules.Pawn, 0, 0, false, 15, -1);
        CheckInt(board.PlayMoveAt(illegal, 1065), ChessBoard.InputRejected, "clock: an illegal move refused");
        CheckInt(board.moveCount, 4, "clock: refused moves not recorded");

        // The clocks stop when the game ends (set directly here; TestEndings ends games).
        board.lifecycle = ChessBoard.LifecycleEnded;
        Check(!board.IsClockRunning(), "clock: stopped once ended", "running");
        CheckTimes(61500, 2250, 1e6, "clock: ended, times as stored");
        CheckInt(board.FlaggedColorAt(1e6), ChessRules.NoColor, "clock: no flag fall once ended");
        CheckInt(board.PlayMoveAt(FindMove(ChessRules.D2, ChessRules.D4), 1065), ChessBoard.InputRejected,
            "clock: no move once ended");

        // Not running before a game: in the lobby and on a loaded board.
        LobbyWithTwoPlayers();
        board.SetClockAs("Ana", 60, 0);
        Check(!board.IsClockRunning(), "clock: not running in the lobby", "running");
        CheckInt(board.PlayMoveAt(FindMove(ChessRules.E2, ChessRules.E4), 1000), ChessBoard.InputRejected,
            "clock: no move in the lobby");
        board.lifecycle = ChessBoard.LifecycleLoaded;
        Check(!board.IsClockRunning(), "clock: not running on a loaded board", "running");

        // A short time control, 1+0: a move with 1 ms left, then black's flag falls after exactly 1 s.
        StartedGame(1, 0, 20);
        Play(ChessRules.E2, ChessRules.E4, 20.9990234375, "clock: 1+0, 1. e4");
        CheckInt(board.whiteTime, 1, "clock: 1+0, 1 ms left after 999 ms");
        CheckInt(board.FlaggedColorAt(21.998046875), ChessRules.NoColor, "clock: 1+0, black with 1 ms left");
        CheckInt(board.FlaggedColorAt(21.9990234375), ChessRules.Black, "clock: 1+0, black's flag falls after 1 s");

        // The real server time: Start sets the turn start, so the clocks start full and run down.
        LobbyWithTwoPlayers();
        board.SetClockAs("Ana", 600, 0);
        board.StartGameAs("Bob");
        int left = board.RemainingTime(ChessRules.White);
        Check(left > 590000 && left <= 600000, "clock: server time, white's clock starts full", "left " + left);
        CheckInt(board.FlaggedColor(), ChessRules.NoColor, "clock: server time, no flag at the start");
        CheckInt(board.PlayMove(FindMove(ChessRules.D2, ChessRules.D4)), ChessBoard.InputAccepted,
            "clock: server time, 1. d4");
        board.turnStart = board.ServerTime() - 601;
        CheckInt(board.RemainingTime(ChessRules.Black), 0, "clock: server time, black out of time after 601 s");
        CheckInt(board.FlaggedColor(), ChessRules.Black, "clock: server time, black's flag falls");
        CheckInt(board.PlayMove(FindMove(ChessRules.D7, ChessRules.D5)), ChessBoard.InputRejected,
            "clock: server time, no move after the flag");

        // A time can't overflow.
        StartedGame(60, 3600, 0);
        board.whiteTime = int.MaxValue - 1000;
        Play(ChessRules.E2, ChessRules.E4, 0, "clock: huge time");
        CheckInt(board.whiteTime, int.MaxValue, "clock: huge time plus the increment stops at int.MaxValue");

        // No clock: never running, no flag fall, -1 in the history. Also SAN with the mate suffix.
        LobbyWithTwoPlayers();
        board.StartGameAs("Ana");
        board.turnStart = 0;
        Check(!board.HasClock() && !board.IsClockRunning(), "no clock: not running", "running");
        Play(ChessRules.F2, ChessRules.F3, 1e6, "no clock: 1. f3");
        Play(ChessRules.E7, ChessRules.E5, 2e6, "no clock: 1... e5");
        Play(ChessRules.G2, ChessRules.G4, 3e6, "no clock: 2. g4");
        CheckTimes(0, 0, 4e6, "no clock: times stay 0");
        CheckInt(board.FlaggedColorAt(4e6), ChessRules.NoColor, "no clock: no flag fall");
        Play(ChessRules.D8, ChessRules.H4, 4e6, "no clock: 2... Qh4#");
        CheckText(board.notation[3], "Qh4#", "no clock: SAN with the mate suffix");
        bool noClocks = true;
        for (int i = 0; i < 4; i++) {
            if (board.clockHistory[i] != -1) noClocks = false;
        }
        Check(noClocks, "no clock: history entries -1", "an entry isn't -1");
        CheckText(ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber),
            "rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3", "no clock: position after the moves");

        // Past the history arrays' first capacity: knights out and back, 1 s per move with a 1 s increment.
        StartedGame(10, 1, 0);
        int plies = ChessBoard.InitialHistoryCapacity * 2 + 10;
        int[] route = new int[] {
            ChessRules.G1, ChessRules.F3, ChessRules.G8, ChessRules.F6,
            ChessRules.F3, ChessRules.G1, ChessRules.F6, ChessRules.G8
        };
        bool allAccepted = true;
        for (int i = 0; i < plies; i++) {
            int step = (i % 4) * 2;
            if (board.PlayMoveAt(FindMove(route[step], route[step + 1]), i + 1) != ChessBoard.InputAccepted) {
                allAccepted = false;
            }
        }
        Check(allAccepted, "history: " + plies + " moves accepted", "a move was refused");
        CheckInt(board.moveCount, plies, "history: move count");
        Check(board.moveHistory.Length >= plies && board.notation.Length == board.moveHistory.Length
            && board.clockHistory.Length == board.moveHistory.Length
            && board.halfmoveClocks.Length == board.moveHistory.Length,
            "history: arrays grown together", "lengths " + board.moveHistory.Length + ", " + board.notation.Length
            + ", " + board.clockHistory.Length + ", " + board.halfmoveClocks.Length);
        bool clocksOk = true;
        bool entriesOk = true;
        for (int i = 0; i < board.moveCount; i++) {
            // Each move takes 1 s and adds 1 s back.
            if (board.clockHistory[i] != 10000) clocksOk = false;
            string san = i % 4 == 0 ? "Nf3" : i % 4 == 1 ? "Nf6" : i % 4 == 2 ? "Ng1" : "Ng8";
            if (board.notation[i] != san || board.halfmoveClocks[i] != i) entriesOk = false;
        }
        Check(clocksOk, "history: a clock entry for every move", "an entry is wrong");
        Check(entriesOk, "history: SAN and halfmove clock for every move", "an entry is wrong");
        bool restEmpty = true;
        for (int i = board.moveCount; i < board.notation.Length; i++) {
            if (board.notation[i] != "" || board.clockHistory[i] != -1) restEmpty = false;
        }
        Check(restEmpty, "history: entries past the game are \"\" and -1", "an entry is null or set");
        CheckInt(board.halfmoveClock, plies, "history: halfmove clock");
        CheckInt(board.moveNumber, 1 + plies / 2, "history: move number");

        board.ClearState();
    }

    // ---- 7.4a piece input ----

    string Fen() {
        return ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber);
    }

    // Puts a position on the board of a game in progress (as if reached by play; the history stays empty).
    void SetPosition(string fen) {
        int clock;
        int number;
        ChessFen.ParseFen(fen, board.board, board.state, out clock, out number);
        board.halfmoveClock = clock;
        board.moveNumber = number;
    }

    void TestPieceInput() {
        NothingForced();
        StartedGame(ChessArbiter.NoClock, 0, 0);

        // Pickups: the side to move's own pieces, by that side's player.
        CheckInt(board.PiecePickedUpAs("Ana", ChessRules.E2), ChessBoard.InputAccepted, "pickup: white pawn by white");
        CheckInt(board.PiecePickedUpAs("Ana", ChessRules.G1), ChessBoard.InputAccepted,
            "pickup: white knight by white");
        CheckInt(board.PiecePickedUpAs("Ana", ChessRules.E7), ChessBoard.InputRejected, "pickup: black pawn by white");
        CheckInt(board.PiecePickedUpAs("Bob", ChessRules.E7), ChessBoard.InputRejected,
            "pickup: black pawn while white is to move");
        CheckInt(board.PiecePickedUpAs("Bob", ChessRules.E2), ChessBoard.InputRejected, "pickup: white pawn by black");
        CheckInt(board.PiecePickedUpAs("Ana", ChessRules.E4), ChessBoard.InputRejected, "pickup: empty square");
        CheckInt(board.PiecePickedUpAs("Cleo", ChessRules.E2), ChessBoard.InputRejected, "pickup: non-player");
        CheckInt(board.PiecePickedUpAs("", ChessRules.E2), ChessBoard.InputRejected, "pickup: empty name");
        CheckInt(board.PiecePickedUpAs("Ana", -1), ChessBoard.InputRejected, "pickup: square -1");
        CheckInt(board.PiecePickedUpAs("Ana", 64), ChessBoard.InputRejected, "pickup: square 64");

        // Drops that aren't moves, then legal ones.
        string start = Fen();
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E5), ChessBoard.InputRejected,
            "drop: e2-e5 not legal");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E2), ChessBoard.InputRejected,
            "drop: back on its own square");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E7, ChessRules.E5), ChessBoard.InputRejected,
            "drop: white moving a black pawn");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E7, ChessRules.E5), ChessBoard.InputRejected,
            "drop: black out of turn");
        CheckInt(board.PieceDroppedAs("Cleo", ChessRules.E2, ChessRules.E4), ChessBoard.InputRejected,
            "drop: non-player");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E4, ChessRules.E5), ChessBoard.InputRejected,
            "drop: from an empty square");
        CheckInt(board.PieceDroppedAs("Ana", -1, ChessRules.E4), ChessBoard.InputRejected, "drop: from square -1");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, 64), ChessBoard.InputRejected, "drop: to square 64");
        CheckInt(board.PromotionChosenAs("Ana", ChessRules.E2, ChessRules.E4, ChessRules.Queen),
            ChessBoard.InputRejected, "drop: promotion choice on a move that isn't a promotion");
        CheckInt(board.moveCount, 0, "drop: rejected drops play nothing");
        CheckText(Fen(), start, "drop: rejected drops leave the position");

        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4), ChessBoard.InputAccepted, "drop: 1. e4");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.D2, ChessRules.D4), ChessBoard.InputRejected,
            "drop: white again out of turn");
        CheckInt(board.PiecePickedUpAs("Bob", ChessRules.E7), ChessBoard.InputAccepted, "pickup: now black's turn");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E7, ChessRules.E5), ChessBoard.InputAccepted, "drop: 1... e5");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.G1, ChessRules.F3), ChessBoard.InputAccepted, "drop: 2. Nf3");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.B8, ChessRules.C6), ChessBoard.InputAccepted, "drop: 2... Nc6");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.F3, ChessRules.E5), ChessBoard.InputAccepted, "drop: 3. Nxe5");
        CheckInt(board.moveCount, 5, "drop: five moves recorded");
        CheckText(board.notation[4], "Nxe5", "drop: SAN of the capture");
        CheckText(Fen(), "r1bqkbnr/pppp1ppp/2n5/4N3/4P3/8/PPPP1PPP/RNBQKB1R b KQkq - 0 3", "drop: position");

        // Not while in replay, or out of game.
        board.replayMode = true;
        CheckInt(board.PiecePickedUpAs("Bob", ChessRules.C6), ChessBoard.InputRejected, "pickup: not in replay");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.C6, ChessRules.E5), ChessBoard.InputRejected,
            "drop: not in replay");
        board.replayMode = false;
        board.lifecycle = ChessBoard.LifecycleEnded;
        CheckInt(board.PiecePickedUpAs("Bob", ChessRules.C6), ChessBoard.InputRejected, "pickup: not once ended");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.C6, ChessRules.E5), ChessBoard.InputRejected,
            "drop: not once ended");
        LobbyWithTwoPlayers();
        CheckInt(board.PiecePickedUpAs("Ana", ChessRules.E2), ChessBoard.InputRejected, "pickup: not in the lobby");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4), ChessBoard.InputRejected,
            "drop: not in the lobby");
        board.lifecycle = ChessBoard.LifecycleLoaded;
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4), ChessBoard.InputRejected,
            "drop: not on a loaded board");

        // Not once the side to move's flag has fallen.
        StartedGame(60, 0, 0);
        board.turnStart = board.ServerTime() - 61;
        CheckInt(board.PiecePickedUpAs("Ana", ChessRules.E2), ChessBoard.InputRejected, "pickup: not after the flag");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4), ChessBoard.InputRejected,
            "drop: not after the flag");
        board.turnStart = board.ServerTime();
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4), ChessBoard.InputAccepted,
            "drop: with time left");

        // The local player in both seats plays both sides.
        board.ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        CheckInt(board.PiecePickedUp(ChessRules.D2), ChessBoard.InputAccepted, "local: pickup as white");
        CheckInt(board.PieceDropped(ChessRules.D2, ChessRules.D4), ChessBoard.InputAccepted, "local: 1. d4");
        CheckInt(board.PiecePickedUp(ChessRules.D7), ChessBoard.InputAccepted, "local: pickup as black");
        CheckInt(board.PieceDropped(ChessRules.D7, ChessRules.D5), ChessBoard.InputAccepted, "local: 1... d5");
        CheckInt(board.PieceDropped(ChessRules.D4, ChessRules.D5), ChessBoard.InputRejected, "local: 2. d4-d5 blocked");
        CheckInt(board.moveCount, 2, "local: two moves");

        board.ClearState();
    }

    // White pawn b7 can promote on b8 or by capturing the rook on a8 or c8; black pawn g2 on g1 or by capturing h1.
    const string PromotionFen = "r1r1k3/1P6/8/8/8/8/6p1/4K2R w - - 0 1";

    void TestPromotion() {
        NothingForced();
        int[] types = new int[] { ChessRules.Queen, ChessRules.Rook, ChessRules.Bishop, ChessRules.Knight };
        for (int t = 0; t < types.Length; t++) {
            int type = types[t];
            string letter = ChessRules.PieceTypeLetter(type);
            StartedGame(ChessArbiter.NoClock, 0, 0);
            SetPosition(PromotionFen);
            CheckInt(board.PieceDroppedAs("Ana", ChessRules.B7, ChessRules.B8), ChessBoard.InputPromotionNeeded,
                "promotion " + letter + ": b8 needs a choice");
            CheckInt(board.moveCount, 0, "promotion " + letter + ": nothing played yet");
            CheckText(Fen(), PromotionFen, "promotion " + letter + ": position unchanged");
            CheckInt(board.PromotionChosenAs("Ana", ChessRules.B7, ChessRules.B8, type), ChessBoard.InputAccepted,
                "promotion " + letter + ": chosen");
            CheckInt(board.board[ChessRules.B8], ChessRules.PieceOf(ChessRules.White, type),
                "promotion " + letter + ": piece on b8");
            CheckInt(board.board[ChessRules.B7], ChessRules.Empty, "promotion " + letter + ": b7 empty");
            CheckInt(board.moveCount, 1, "promotion " + letter + ": one move");
            CheckInt(ChessRules.MovePromotion(board.moveHistory[0]), type, "promotion " + letter + ": move's type");
            CheckText(board.notation[0], "b8=" + letter, "promotion " + letter + ": SAN");
        }

        // Promotion by capture, and Black's promotions.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        SetPosition(PromotionFen);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.B7, ChessRules.A8), ChessBoard.InputPromotionNeeded,
            "promotion: bxa8 needs a choice");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.B7, ChessRules.C8), ChessBoard.InputPromotionNeeded,
            "promotion: bxc8 needs a choice");

        // Choices that are refused.
        CheckInt(board.PromotionChosenAs("Ana", ChessRules.B7, ChessRules.B8, ChessRules.Pawn),
            ChessBoard.InputRejected, "promotion: to a pawn refused");
        CheckInt(board.PromotionChosenAs("Ana", ChessRules.B7, ChessRules.B8, ChessRules.King),
            ChessBoard.InputRejected, "promotion: to a king refused");
        CheckInt(board.PromotionChosenAs("Ana", ChessRules.B7, ChessRules.B8, ChessRules.NoPieceType),
            ChessBoard.InputRejected, "promotion: to no piece refused");
        CheckInt(board.PromotionChosenAs("Ana", ChessRules.B7, ChessRules.B8, 7), ChessBoard.InputRejected,
            "promotion: to type 7 refused");
        CheckInt(board.PromotionChosenAs("Bob", ChessRules.B7, ChessRules.B8, ChessRules.Queen),
            ChessBoard.InputRejected, "promotion: by the other player refused");
        CheckInt(board.PromotionChosenAs("Cleo", ChessRules.B7, ChessRules.B8, ChessRules.Queen),
            ChessBoard.InputRejected, "promotion: by a non-player refused");
        CheckInt(board.PromotionChosenAs("Ana", ChessRules.B7, ChessRules.C7, ChessRules.Queen),
            ChessBoard.InputRejected, "promotion: on an illegal square refused");
        CheckInt(board.PromotionChosenAs("Ana", ChessRules.E1, ChessRules.D1, ChessRules.Queen),
            ChessBoard.InputRejected, "promotion: on a king move refused");
        CheckInt(board.moveCount, 0, "promotion: refused choices play nothing");
        CheckText(Fen(), PromotionFen, "promotion: refused choices leave the position");

        CheckInt(board.PromotionChosenAs("Ana", ChessRules.B7, ChessRules.A8, ChessRules.Knight),
            ChessBoard.InputAccepted, "promotion: bxa8=N");
        CheckInt(board.board[ChessRules.A8], ChessRules.WhiteKnight, "promotion: knight on a8");
        CheckText(board.notation[0], "bxa8=N", "promotion: SAN of the capture");

        CheckInt(board.PieceDroppedAs("Bob", ChessRules.G2, ChessRules.G1), ChessBoard.InputPromotionNeeded,
            "promotion: g1 needs a choice");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.G2, ChessRules.H1), ChessBoard.InputPromotionNeeded,
            "promotion: gxh1 needs a choice");
        CheckInt(board.PromotionChosenAs("Bob", ChessRules.G2, ChessRules.H1, ChessRules.Bishop),
            ChessBoard.InputAccepted, "promotion: gxh1=B");
        CheckInt(board.board[ChessRules.H1], ChessRules.BlackBishop, "promotion: black bishop on h1");
        CheckInt(board.board[ChessRules.G2], ChessRules.Empty, "promotion: g2 empty");
        CheckInt(board.moveCount, 2, "promotion: two moves");

        // The local player's methods.
        board.ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        SetPosition(PromotionFen);
        CheckInt(board.PieceDropped(ChessRules.B7, ChessRules.B8), ChessBoard.InputPromotionNeeded,
            "local: promotion needs a choice");
        CheckInt(board.PromotionChosen(ChessRules.B7, ChessRules.B8, ChessRules.Rook), ChessBoard.InputAccepted,
            "local: b8=R");
        CheckInt(board.board[ChessRules.B8], ChessRules.WhiteRook, "local: rook on b8");

        board.ClearState();
    }

    // ---- 7.4b ----

    // A game in progress between Ana and Bob, with no clock, in the position fen.
    void CastlingGame(string fen) {
        StartedGame(ChessArbiter.NoClock, 0, 0);
        SetPosition(fen);
    }

    // Checks the last move played was a castle with this SAN and that the king and rook stand on these squares.
    void CheckCastled(string san, int king, int kingSquare, int rook, int rookSquare, string name) {
        Check(board.moveCount > 0 && ChessRules.MoveIsCastle(board.moveHistory[board.moveCount - 1]),
            name + ": played a castle", "the last move isn't a castle");
        if (board.moveCount > 0) CheckText(board.notation[board.moveCount - 1], san, name + ": SAN");
        CheckInt(board.board[kingSquare], king, name + ": king on " + ChessRules.SquareName(kingSquare));
        CheckInt(board.board[rookSquare], rook, name + ": rook on " + ChessRules.SquareName(rookSquare));
    }

    const string CastlingFen = "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1";

    void TestCastling() {
        NothingForced();

        // Standard castles: on the destination and on the rook, both sides, both colours.
        CastlingGame(CastlingFen);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.G1), ChessBoard.InputAccepted,
            "castling: e1 on g1 accepted");
        CheckCastled("O-O", ChessRules.WhiteKing, ChessRules.G1, ChessRules.WhiteRook, ChessRules.F1,
            "castling: e1 on g1");
        CheckInt(board.board[ChessRules.H1], ChessRules.Empty, "castling: e1 on g1: h1 empty");
        CheckInt(board.board[ChessRules.E1], ChessRules.Empty, "castling: e1 on g1: e1 empty");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E8, ChessRules.A8), ChessBoard.InputAccepted,
            "castling: e8 on a8 accepted");
        CheckCastled("O-O-O", ChessRules.BlackKing, ChessRules.C8, ChessRules.BlackRook, ChessRules.D8,
            "castling: e8 on a8");
        CheckInt(board.moveCount, 2, "castling: two moves");
        CheckText(Fen(), "2kr3r/8/8/8/8/8/8/R4RK1 w - - 2 2", "castling: position after both");

        CastlingGame(CastlingFen);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.H1), ChessBoard.InputAccepted,
            "castling: e1 on h1 accepted");
        CheckCastled("O-O", ChessRules.WhiteKing, ChessRules.G1, ChessRules.WhiteRook, ChessRules.F1,
            "castling: e1 on h1");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E8, ChessRules.C8), ChessBoard.InputAccepted,
            "castling: e8 on c8 accepted");
        CheckCastled("O-O-O", ChessRules.BlackKing, ChessRules.C8, ChessRules.BlackRook, ChessRules.D8,
            "castling: e8 on c8");

        CastlingGame(CastlingFen);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.A1), ChessBoard.InputAccepted,
            "castling: e1 on a1 accepted");
        CheckCastled("O-O-O", ChessRules.WhiteKing, ChessRules.C1, ChessRules.WhiteRook, ChessRules.D1,
            "castling: e1 on a1");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E8, ChessRules.H8), ChessBoard.InputAccepted,
            "castling: e8 on h8 accepted");
        CheckCastled("O-O", ChessRules.BlackKing, ChessRules.G8, ChessRules.BlackRook, ChessRules.F8,
            "castling: e8 on h8");

        // The standard castles move the king two squares, so the destination is never ambiguous.
        CastlingGame(CastlingFen);
        Check(!board.IsCastleDropSpaceNeeded(ChessBoard.CastleKingSide), "castling: standard needs no king side space",
            "the space is offered");
        Check(!board.IsCastleDropSpaceNeeded(ChessBoard.CastleQueenSide),
            "castling: standard needs no queen side space", "the space is offered");

        // Refused castles: no rights, through check, by the wrong player or out of turn, a rook dropped on the king,
        // a promotion choice on a castle, and invalid sides.
        CastlingGame("r3k2r/8/8/8/8/8/8/R3K2R w - - 0 1");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.G1), ChessBoard.InputRejected,
            "castling: no rights, e1 on g1");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.H1), ChessBoard.InputRejected,
            "castling: no rights, e1 on h1");
        CheckInt(board.KingDroppedOnCastleSpaceAs("Ana", ChessBoard.CastleKingSide), ChessBoard.InputRejected,
            "castling: no rights, castle space");
        CastlingGame("r3k2r/8/8/8/8/8/5r2/R3K2R w KQkq - 0 1");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.G1), ChessBoard.InputRejected,
            "castling: through check, e1 on g1");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.H1), ChessBoard.InputRejected,
            "castling: through check, e1 on h1");
        CheckInt(board.KingDroppedOnCastleSpaceAs("Ana", ChessBoard.CastleKingSide), ChessBoard.InputRejected,
            "castling: through check, castle space");
        Check(!board.IsCastleDropSpaceNeeded(ChessBoard.CastleKingSide),
            "castling: through check, no space offered", "the space is offered");
        CheckInt(board.moveCount, 0, "castling: no castle without rights or through check");
        CastlingGame(CastlingFen);
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E1, ChessRules.G1), ChessBoard.InputRejected,
            "castling: by the other player");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E8, ChessRules.G8), ChessBoard.InputRejected,
            "castling: out of turn");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.H1, ChessRules.E1), ChessBoard.InputRejected,
            "castling: rook dropped on the king");
        CheckInt(board.PromotionChosenAs("Ana", ChessRules.E1, ChessRules.H1, ChessRules.Queen),
            ChessBoard.InputRejected, "castling: a promotion choice on the rook");
        CheckInt(board.KingDroppedOnCastleSpaceAs("Bob", ChessBoard.CastleKingSide), ChessBoard.InputRejected,
            "castling: castle space by the other player");
        CheckInt(board.KingDroppedOnCastleSpaceAs("Cleo", ChessBoard.CastleKingSide), ChessBoard.InputRejected,
            "castling: castle space by a non-player");
        CheckInt(board.KingDroppedOnCastleSpaceAs("Ana", 2), ChessBoard.InputRejected, "castling: castle space side 2");
        CheckInt(board.KingDroppedOnCastleSpaceAs("Ana", -1), ChessBoard.InputRejected,
            "castling: castle space side -1");
        Check(!board.IsCastleDropSpaceNeeded(2), "castling: no space for side 2", "offered");
        CheckInt(board.moveCount, 0, "castling: refused castles play nothing");
        CheckText(Fen(), CastlingFen, "castling: refused castles leave the position");

        // The castle drop space also castles when the destination isn't ambiguous.
        CheckInt(board.KingDroppedOnCastleSpaceAs("Ana", ChessBoard.CastleQueenSide), ChessBoard.InputAccepted,
            "castling: queen side space in a standard position");
        CheckCastled("O-O-O", ChessRules.WhiteKing, ChessRules.C1, ChessRules.WhiteRook, ChessRules.D1,
            "castling: queen side space in a standard position");

        // Not in replay or once ended, and no space offered there.
        CastlingGame(CastlingFen);
        board.replayMode = true;
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.G1), ChessBoard.InputRejected,
            "castling: not in replay");
        CheckInt(board.KingDroppedOnCastleSpaceAs("Ana", ChessBoard.CastleKingSide), ChessBoard.InputRejected,
            "castling: castle space not in replay");
        board.replayMode = false;
        board.lifecycle = ChessBoard.LifecycleEnded;
        CheckInt(board.KingDroppedOnCastleSpaceAs("Ana", ChessBoard.CastleKingSide), ChessBoard.InputRejected,
            "castling: castle space not once ended");
        CheckInt(board.moveCount, 0, "castling: nothing played in replay or once ended");

        // Chess960, king f1 and rook h1: castling and Kg1 both put the king on g1, so the destination drop is Kg1 and
        // the castle uses the space. The queen side castle moves the king three squares and needs no space. The f
        // pawns keep a castled rook from giving check.
        string oneSquare = "1r3k1r/5p2/8/8/8/8/5P2/1R3K1R w KQkq - 0 1";
        CastlingGame(oneSquare);
        Check(board.IsCastleDropSpaceNeeded(ChessBoard.CastleKingSide), "chess960 f1: king side space offered",
            "not offered");
        Check(!board.IsCastleDropSpaceNeeded(ChessBoard.CastleQueenSide), "chess960 f1: no queen side space",
            "offered");
        board.replayMode = true;
        Check(!board.IsCastleDropSpaceNeeded(ChessBoard.CastleKingSide), "chess960 f1: no space in replay", "offered");
        board.replayMode = false;
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.F1, ChessRules.G1), ChessBoard.InputAccepted,
            "chess960 f1: f1 on g1 accepted");
        Check(!ChessRules.MoveIsCastle(board.moveHistory[0]), "chess960 f1: f1 on g1 isn't a castle", "it castled");
        CheckText(board.notation[0], "Kg1", "chess960 f1: f1 on g1 SAN");
        CheckInt(board.board[ChessRules.G1], ChessRules.WhiteKing, "chess960 f1: king on g1");
        CheckInt(board.board[ChessRules.H1], ChessRules.WhiteRook, "chess960 f1: rook still on h1");
        // Black has the same position.
        Check(board.IsCastleDropSpaceNeeded(ChessBoard.CastleKingSide), "chess960 f8: king side space offered",
            "not offered");
        CheckInt(board.KingDroppedOnCastleSpaceAs("Bob", ChessBoard.CastleKingSide), ChessBoard.InputAccepted,
            "chess960 f8: castle space accepted");
        CheckCastled("O-O", ChessRules.BlackKing, ChessRules.G8, ChessRules.BlackRook, ChessRules.F8,
            "chess960 f8: castle space");
        CheckInt(board.board[ChessRules.H8], ChessRules.Empty, "chess960 f8: h8 empty");

        CastlingGame(oneSquare);
        CheckInt(board.KingDroppedOnCastleSpaceAs("Ana", ChessBoard.CastleKingSide), ChessBoard.InputAccepted,
            "chess960 f1: castle space accepted");
        CheckCastled("O-O", ChessRules.WhiteKing, ChessRules.G1, ChessRules.WhiteRook, ChessRules.F1,
            "chess960 f1: castle space");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.F8, ChessRules.H8), ChessBoard.InputAccepted,
            "chess960 f8: f8 on h8 accepted");
        CheckCastled("O-O", ChessRules.BlackKing, ChessRules.G8, ChessRules.BlackRook, ChessRules.F8,
            "chess960 f8: f8 on h8");

        CastlingGame(oneSquare);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.F1, ChessRules.C1), ChessBoard.InputAccepted,
            "chess960 f1: f1 on c1 accepted");
        CheckCastled("O-O-O", ChessRules.WhiteKing, ChessRules.C1, ChessRules.WhiteRook, ChessRules.D1,
            "chess960 f1: f1 on c1");
        CheckInt(board.board[ChessRules.B1], ChessRules.Empty, "chess960 f1: f1 on c1: b1 empty");

        // King d1 and rook a1: the queen side castle moves the king one square to c1.
        string queenOneSquare = "r2k3r/3p4/8/8/8/8/3P4/R2K3R w KQkq - 0 1";
        CastlingGame(queenOneSquare);
        Check(board.IsCastleDropSpaceNeeded(ChessBoard.CastleQueenSide), "chess960 d1: queen side space offered",
            "not offered");
        Check(!board.IsCastleDropSpaceNeeded(ChessBoard.CastleKingSide), "chess960 d1: no king side space", "offered");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.D1, ChessRules.C1), ChessBoard.InputAccepted,
            "chess960 d1: d1 on c1 accepted");
        CheckText(board.notation[0], "Kc1", "chess960 d1: d1 on c1 is Kc1");
        CastlingGame(queenOneSquare);
        CheckInt(board.KingDroppedOnCastleSpaceAs("Ana", ChessBoard.CastleQueenSide), ChessBoard.InputAccepted,
            "chess960 d1: castle space accepted");
        CheckCastled("O-O-O", ChessRules.WhiteKing, ChessRules.C1, ChessRules.WhiteRook, ChessRules.D1,
            "chess960 d1: castle space");
        CheckInt(board.board[ChessRules.A1], ChessRules.Empty, "chess960 d1: a1 empty");

        // King already on g1: castling doesn't move it, so dropping it on its own square is refused and the space
        // castles; dropping it on the rook castles too.
        string kingOnG = "6kr/8/8/8/8/8/8/6KR w Kk - 0 1";
        CastlingGame(kingOnG);
        Check(board.IsCastleDropSpaceNeeded(ChessBoard.CastleKingSide), "chess960 g1: king side space offered",
            "not offered");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.G1, ChessRules.G1), ChessBoard.InputRejected,
            "chess960 g1: g1 on g1 refused");
        CheckInt(board.KingDroppedOnCastleSpaceAs("Ana", ChessBoard.CastleKingSide), ChessBoard.InputAccepted,
            "chess960 g1: castle space accepted");
        CheckCastled("O-O", ChessRules.WhiteKing, ChessRules.G1, ChessRules.WhiteRook, ChessRules.F1,
            "chess960 g1: castle space");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.G8, ChessRules.H8), ChessBoard.InputAccepted,
            "chess960 g8: g8 on h8 accepted");
        CheckCastled("O-O", ChessRules.BlackKing, ChessRules.G8, ChessRules.BlackRook, ChessRules.F8,
            "chess960 g8: g8 on h8");

        // King already on c1, the queen side the same way.
        string kingOnC = "1rk5/8/8/8/8/8/8/1RK5 w Qq - 0 1";
        CastlingGame(kingOnC);
        Check(board.IsCastleDropSpaceNeeded(ChessBoard.CastleQueenSide), "chess960 c1: queen side space offered",
            "not offered");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.C1, ChessRules.B1), ChessBoard.InputAccepted,
            "chess960 c1: c1 on b1 accepted");
        CheckCastled("O-O-O", ChessRules.WhiteKing, ChessRules.C1, ChessRules.WhiteRook, ChessRules.D1,
            "chess960 c1: c1 on b1");
        CheckInt(board.KingDroppedOnCastleSpaceAs("Bob", ChessBoard.CastleQueenSide), ChessBoard.InputAccepted,
            "chess960 c8: castle space accepted");
        CheckCastled("O-O-O", ChessRules.BlackKing, ChessRules.C8, ChessRules.BlackRook, ChessRules.D8,
            "chess960 c8: castle space");

        // King f1 and rook g1: castling swaps them and the destination is the rook's square, so dropping on g1
        // castles (no normal Kg1 is possible) and no space is offered. The g pawns keep the black rook off g1.
        CastlingGame("r4kr1/5pp1/8/8/8/8/5PP1/R4KR1 w KQkq - 0 1");
        Check(!board.IsCastleDropSpaceNeeded(ChessBoard.CastleKingSide), "chess960 swap: no space offered",
            "offered");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.F1, ChessRules.G1), ChessBoard.InputAccepted,
            "chess960 swap: f1 on g1 accepted");
        CheckCastled("O-O", ChessRules.WhiteKing, ChessRules.G1, ChessRules.WhiteRook, ChessRules.F1,
            "chess960 swap: f1 on g1");

        // No space is offered outside a game.
        LobbyWithTwoPlayers();
        SetPosition(oneSquare);
        Check(!board.IsCastleDropSpaceNeeded(ChessBoard.CastleKingSide), "castling: no space in the lobby", "offered");

        // The local player's methods.
        board.ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        SetPosition(oneSquare);
        CheckInt(board.KingDroppedOnCastleSpace(ChessBoard.CastleKingSide), ChessBoard.InputAccepted,
            "local: castle space");
        CheckCastled("O-O", ChessRules.WhiteKing, ChessRules.G1, ChessRules.WhiteRook, ChessRules.F1,
            "local: castle space");
        CheckInt(board.PieceDropped(ChessRules.F8, ChessRules.H8), ChessBoard.InputAccepted, "local: f8 on h8");
        CheckCastled("O-O", ChessRules.BlackKing, ChessRules.G8, ChessRules.BlackRook, ChessRules.F8,
            "local: f8 on h8");

        board.ClearState();
    }

    // ---- 7.4c buttons ----

    // A game between Ana and Bob with no clock, 1. Nf3 Nf6 2. Ng1 Ng8 played, and the snapshot's setting bits set to
    // settings.
    void ButtonGame(int settings) {
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = settings;
        board.PieceDroppedAs("Ana", ChessRules.G1, ChessRules.F3);
        board.PieceDroppedAs("Bob", ChessRules.G8, ChessRules.F6);
        board.PieceDroppedAs("Ana", ChessRules.F3, ChessRules.G1);
        board.PieceDroppedAs("Bob", ChessRules.F6, ChessRules.G8);
        CheckInt(board.moveCount, 4, "buttons: the game has four moves");
    }

    // The board of ButtonGame in replay showing ply.
    void ButtonReplay(int settings, int ply) {
        ButtonGame(settings);
        board.replayMode = true;
        board.replayIndex = ply;
    }

    // Lets the side to move's flag fall: a 60 s clock whose turn began 61 s ago.
    void FlagFallen() {
        board.baseTime = 60;
        board.whiteTime = 60000;
        board.blackTime = 60000;
        board.turnStart = board.ServerTime() - 61;
    }

    void TestLobbyButtons() {
        NothingForced();
        board.ClearState();
        Check(board.CanJoinAs(ChessRules.White, "Ana"), "join button: white on a reset board", "refused");
        Check(board.CanJoinAs(ChessRules.Black, "Ana"), "join button: black on a reset board", "refused");
        Check(!board.CanJoinAs(ChessRules.NoColor, "Ana"), "join button: no colour", "allowed");
        Check(!board.CanJoinAs(2, "Ana"), "join button: colour 2", "allowed");
        Check(!board.CanJoinAs(ChessRules.White, ""), "join button: empty name", "allowed");
        Check(!board.CanJoinAs(ChessRules.White, null), "join button: null name", "allowed");
        Check(!board.CanSetModeAs("Ana"), "mode button: not on a reset board", "allowed");
        Check(!board.CanSetClockAs("Ana"), "clock button: not on a reset board", "allowed");
        Check(!board.CanStartGameAs("Ana"), "start button: not on a reset board", "allowed");

        board.JoinAs(ChessRules.White, "Ana");
        Check(!board.CanJoinAs(ChessRules.White, "Ana"), "join button: not the seat already held", "allowed");
        Check(!board.CanJoinAs(ChessRules.White, "Bob"), "join button: not a seat held by someone else", "allowed");
        Check(board.CanJoinAs(ChessRules.Black, "Bob"), "join button: the empty seat in the lobby", "refused");
        Check(board.CanJoinAs(ChessRules.Black, "Ana"), "join button: the other seat for the same player", "refused");
        Check(board.CanSetModeAs("Ana"), "mode button: seated player in the lobby", "refused");
        Check(!board.CanSetModeAs("Bob"), "mode button: not a non-player", "allowed");
        Check(board.CanSetClockAs("Ana"), "clock button: seated player in the lobby", "refused");
        Check(!board.CanSetClockAs("Bob"), "clock button: not a non-player", "allowed");
        Check(!board.CanStartGameAs("Ana"), "start button: not with one seat filled", "allowed");

        board.JoinAs(ChessRules.Black, "Bob");
        Check(!board.CanJoinAs(ChessRules.Black, "Cleo"), "join button: not with both seats filled", "allowed");
        Check(board.CanStartGameAs("Ana"), "start button: white with both seats filled", "refused");
        Check(board.CanStartGameAs("Bob"), "start button: black with both seats filled", "refused");
        Check(!board.CanStartGameAs("Cleo"), "start button: not a non-player", "allowed");
        Check(!board.CanStartGameAs(""), "start button: not an empty name", "allowed");

        // Forced settings take the buttons away, and the methods agree.
        board.gameSettings = ChessArbiter.SettingModeForced;
        Check(!board.CanSetModeAs("Ana"), "mode button: not when forced", "allowed");
        CheckInt(board.SetModeAs("Ana", ChessGame.ModeChess960), ChessBoard.InputRejected,
            "mode button: the method refuses when forced");
        Check(board.CanSetClockAs("Ana"), "clock button: the mode forced leaves the clock", "refused");
        board.gameSettings = ChessArbiter.SettingClockForced;
        Check(board.CanSetModeAs("Ana"), "mode button: the clock forced leaves the mode", "refused");
        Check(!board.CanSetClockAs("Ana"), "clock button: not when forced", "allowed");
        CheckInt(board.SetClockAs("Ana", 300, 0), ChessBoard.InputRejected,
            "clock button: the method refuses when forced");
        Check(board.CanStartGameAs("Ana"), "start button: forced settings leave Start", "refused");
        board.gameSettings = 0;

        board.StartGameAs("Ana");
        Check(!board.CanJoinAs(ChessRules.White, "Ana"), "join button: not in game", "allowed");
        Check(!board.CanSetModeAs("Ana"), "mode button: not in game", "allowed");
        Check(!board.CanSetClockAs("Ana"), "clock button: not in game", "allowed");
        Check(!board.CanStartGameAs("Ana"), "start button: not in game", "allowed");
        board.lifecycle = ChessBoard.LifecycleEnded;
        Check(!board.CanJoinAs(ChessRules.White, "Cleo"), "join button: not once ended", "allowed");
        Check(!board.CanStartGameAs("Ana"), "start button: not once ended", "allowed");

        // A loaded board: seats and the clock, but not the mode or Start (it continues instead).
        board.ClearState();
        board.lifecycle = ChessBoard.LifecycleLoaded;
        board.replayMode = true;
        Check(board.CanJoinAs(ChessRules.White, "Ana"), "join button: on a loaded board", "refused");
        board.JoinAs(ChessRules.White, "Ana");
        board.JoinAs(ChessRules.Black, "Bob");
        Check(board.CanSetClockAs("Ana"), "clock button: on a loaded board", "refused");
        Check(!board.CanSetModeAs("Ana"), "mode button: not on a loaded board", "allowed");
        Check(!board.CanStartGameAs("Ana"), "start button: not on a loaded board", "allowed");

        board.ClearState();
    }

    void TestResignAndDrawButtons() {
        NothingForced();

        // Resign: each player for their own colour while the game runs, in replay too.
        ButtonGame(0);
        Check(board.CanResignAs(ChessRules.White, "Ana"), "resign button: white for white", "refused");
        Check(board.CanResignAs(ChessRules.Black, "Bob"), "resign button: black for black", "refused");
        Check(!board.CanResignAs(ChessRules.Black, "Ana"), "resign button: not white for black", "allowed");
        Check(!board.CanResignAs(ChessRules.White, "Bob"), "resign button: not black for white", "allowed");
        Check(!board.CanResignAs(ChessRules.White, "Cleo"), "resign button: not a non-player", "allowed");
        Check(!board.CanResignAs(ChessRules.White, ""), "resign button: not an empty name", "allowed");
        Check(!board.CanResignAs(2, "Ana"), "resign button: not colour 2", "allowed");
        board.replayMode = true;
        Check(board.CanResignAs(ChessRules.White, "Ana"), "resign button: in replay", "refused");
        board.replayMode = false;
        FlagFallen();
        Check(!board.CanResignAs(ChessRules.White, "Ana"), "resign button: not after the flag", "allowed");
        board.turnStart = board.ServerTime();
        Check(board.CanResignAs(ChessRules.White, "Ana"), "resign button: with time left", "refused");
        board.lifecycle = ChessBoard.LifecycleEnded;
        Check(!board.CanResignAs(ChessRules.White, "Ana"), "resign button: not once ended", "allowed");
        LobbyWithTwoPlayers();
        Check(!board.CanResignAs(ChessRules.White, "Ana"), "resign button: not in the lobby", "allowed");
        board.lifecycle = ChessBoard.LifecycleLoaded;
        Check(!board.CanResignAs(ChessRules.White, "Ana"), "resign button: not on a loaded board", "allowed");

        // Draw offers only with draws by agreement on.
        ButtonGame(0);
        Check(!board.CanOfferDrawAs(ChessRules.White, "Ana"), "offer draw button: not with agreement off", "allowed");
        board.gameSettings = ChessArbiter.SettingsDrawRules & ~ChessArbiter.SettingDrawAgreement;
        Check(!board.CanOfferDrawAs(ChessRules.White, "Ana"), "offer draw button: not with only the other draw rules",
            "allowed");
        board.gameSettings = ChessArbiter.SettingDrawAgreement;
        Check(board.CanOfferDrawAs(ChessRules.White, "Ana"), "offer draw button: white", "refused");
        Check(board.CanOfferDrawAs(ChessRules.Black, "Bob"), "offer draw button: black, not to move", "refused");
        Check(!board.CanOfferDrawAs(ChessRules.Black, "Ana"), "offer draw button: not white for black", "allowed");
        Check(!board.CanOfferDrawAs(ChessRules.White, "Cleo"), "offer draw button: not a non-player", "allowed");
        Check(!board.CanAcceptDrawAs(ChessRules.Black, "Bob"), "accept draw button: not with no offer", "allowed");
        Check(!board.CanDeclineDrawAs(ChessRules.Black, "Bob"), "decline draw button: not with no offer", "allowed");
        board.replayMode = true;
        Check(board.CanOfferDrawAs(ChessRules.White, "Ana"), "offer draw button: in replay", "refused");
        board.replayMode = false;

        // White's offer pending: Black answers it, nobody offers again.
        board.drawOfferColor = ChessRules.White;
        Check(!board.CanOfferDrawAs(ChessRules.White, "Ana"), "offer draw button: not again", "allowed");
        Check(!board.CanOfferDrawAs(ChessRules.Black, "Bob"), "offer draw button: not with one pending", "allowed");
        Check(board.CanAcceptDrawAs(ChessRules.Black, "Bob"), "accept draw button: black", "refused");
        Check(board.CanDeclineDrawAs(ChessRules.Black, "Bob"), "decline draw button: black", "refused");
        Check(!board.CanAcceptDrawAs(ChessRules.White, "Ana"), "accept draw button: not the offerer", "allowed");
        Check(!board.CanDeclineDrawAs(ChessRules.White, "Ana"), "decline draw button: not the offerer", "allowed");
        Check(!board.CanAcceptDrawAs(ChessRules.Black, "Ana"), "accept draw button: not white for black", "allowed");
        Check(!board.CanAcceptDrawAs(ChessRules.Black, "Cleo"), "accept draw button: not a non-player", "allowed");
        Check(!board.CanAcceptDrawAs(2, "Bob"), "accept draw button: not colour 2", "allowed");
        Check(!board.CanAcceptDrawAs(ChessRules.NoColor, "Bob"), "accept draw button: not no colour", "allowed");
        board.replayMode = true;
        Check(board.CanAcceptDrawAs(ChessRules.Black, "Bob"), "accept draw button: in replay", "refused");
        board.replayMode = false;
        board.gameSettings = 0;
        Check(!board.CanAcceptDrawAs(ChessRules.Black, "Bob"), "accept draw button: not once agreement is off",
            "allowed");
        board.gameSettings = ChessArbiter.SettingDrawAgreement;
        board.drawOfferColor = ChessRules.Black;
        Check(board.CanAcceptDrawAs(ChessRules.White, "Ana"), "accept draw button: white, black's offer", "refused");
        Check(!board.CanAcceptDrawAs(ChessRules.Black, "Bob"), "accept draw button: not black, black's offer",
            "allowed");
        FlagFallen();
        Check(!board.CanAcceptDrawAs(ChessRules.White, "Ana"), "accept draw button: not after the flag", "allowed");
        board.turnStart = board.ServerTime();
        board.lifecycle = ChessBoard.LifecycleEnded;
        Check(!board.CanAcceptDrawAs(ChessRules.White, "Ana"), "accept draw button: not once ended", "allowed");
        board.drawOfferColor = ChessRules.NoColor;
        Check(!board.CanOfferDrawAs(ChessRules.White, "Ana"), "offer draw button: not once ended", "allowed");

        // Threefold repetition: the starting position again after 4. Ng1 Ng8, its third time, with White to move.
        ButtonGame(0);
        board.PieceDroppedAs("Ana", ChessRules.G1, ChessRules.F3);
        board.PieceDroppedAs("Bob", ChessRules.G8, ChessRules.F6);
        board.PieceDroppedAs("Ana", ChessRules.F3, ChessRules.G1);
        board.gameSettings = ChessArbiter.SettingsDrawRules;
        CheckInt(board.ClaimableDraw(), ChessGame.EndingNone, "claim: nothing after 7 plies");
        Check(!board.CanClaimDrawAs("Bob"), "claim button: not after 7 plies", "allowed");
        board.PieceDroppedAs("Bob", ChessRules.F6, ChessRules.G8);
        CheckInt(board.moveCount, 8, "claim: eight moves played");
        board.gameSettings = 0;
        CheckInt(board.ClaimableDraw(), ChessGame.EndingNone, "claim: threefold not claimable under No draws");
        Check(!board.CanClaimDrawAs("Ana"), "claim button: not under No draws", "allowed");
        board.gameSettings = ChessArbiter.SettingsDrawRules & ~ChessArbiter.SettingThreefoldRepetition;
        CheckInt(board.ClaimableDraw(), ChessGame.EndingNone, "claim: not with every rule but threefold");
        board.gameSettings = ChessArbiter.SettingThreefoldRepetition;
        CheckInt(board.ClaimableDraw(), ChessGame.EndingThreefoldRepetition, "claim: threefold");
        Check(board.CanClaimDrawAs("Ana"), "claim button: the side to move", "refused");
        Check(!board.CanClaimDrawAs("Bob"), "claim button: not the side not to move", "allowed");
        Check(!board.CanClaimDrawAs("Cleo"), "claim button: not a non-player", "allowed");
        Check(!board.CanClaimDrawAs(""), "claim button: not an empty name", "allowed");
        board.replayMode = true;
        Check(board.CanClaimDrawAs("Ana"), "claim button: in replay", "refused");
        board.replayMode = false;
        board.gameSettings = ChessArbiter.SettingThreefoldRepetition | ChessArbiter.SettingFiftyMoveRule;
        board.halfmoveClock = 100;
        CheckInt(board.ClaimableDraw(), ChessGame.EndingThreefoldRepetition, "claim: threefold before fifty-move");
        board.halfmoveClock = 8;
        FlagFallen();
        Check(!board.CanClaimDrawAs("Ana"), "claim button: not after the flag", "allowed");
        board.turnStart = board.ServerTime();
        board.lifecycle = ChessBoard.LifecycleEnded;
        Check(!board.CanClaimDrawAs("Ana"), "claim button: not once ended", "allowed");

        // Fifty-move rule: claimable at a halfmove clock of 100, not 99.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        SetPosition("8/8/4k3/8/8/4K3/8/R7 b - - 99 80");
        board.gameSettings = ChessArbiter.SettingsDrawRules;
        CheckInt(board.ClaimableDraw(), ChessGame.EndingNone, "claim: not at 99 halfmoves");
        SetPosition("8/8/4k3/8/8/4K3/8/R7 b - - 100 80");
        board.gameSettings = ChessArbiter.SettingsDrawRules & ~ChessArbiter.SettingFiftyMoveRule;
        CheckInt(board.ClaimableDraw(), ChessGame.EndingNone, "claim: not with every rule but fifty-move");
        board.gameSettings = ChessArbiter.SettingFiftyMoveRule;
        CheckInt(board.ClaimableDraw(), ChessGame.EndingFiftyMoveRule, "claim: fifty-move");
        Check(board.CanClaimDrawAs("Bob"), "claim button: fifty-move, black to move", "refused");
        Check(!board.CanClaimDrawAs("Ana"), "claim button: fifty-move, not white", "allowed");
        LobbyWithTwoPlayers();
        board.gameSettings = ChessArbiter.SettingsDrawRules;
        Check(!board.CanClaimDrawAs("Ana"), "claim button: not in the lobby", "allowed");

        board.ClearState();
    }

    void TestReplayButtons() {
        NothingForced();
        LobbyWithTwoPlayers();
        Check(!board.CanEnterReplayAs("Ana", false), "enter replay button: not in the lobby", "allowed");
        Check(!board.CanEnterReplayAs("Cleo", true), "enter replay button: not in the lobby for an organizer",
            "allowed");
        board.ClearState();
        Check(!board.CanEnterReplayAs("Cleo", true), "enter replay button: not on a reset board", "allowed");

        ButtonGame(0);
        Check(board.CanEnterReplayAs("Ana", false), "enter replay button: white", "refused");
        Check(board.CanEnterReplayAs("Bob", false), "enter replay button: black", "refused");
        Check(board.CanEnterReplayAs("Cleo", true), "enter replay button: an organizer", "refused");
        Check(!board.CanEnterReplayAs("Cleo", false), "enter replay button: not a bystander", "allowed");
        Check(!board.CanEnterReplayAs("", false), "enter replay button: not an empty name", "allowed");
        Check(!board.CanLeaveReplayAs("Ana", false), "leave replay button: not out of replay", "allowed");
        Check(!board.CanStepToAs("Ana", false, 2), "step button: not out of replay", "allowed");
        FlagFallen();
        Check(board.CanEnterReplayAs("Ana", false), "enter replay button: after the flag", "refused");
        board.turnStart = board.ServerTime();

        board.replayMode = true;
        board.replayIndex = 4;
        Check(!board.CanEnterReplayAs("Ana", false), "enter replay button: not in replay", "allowed");
        Check(board.CanLeaveReplayAs("Ana", false), "leave replay button: white", "refused");
        Check(board.CanLeaveReplayAs("Bob", false), "leave replay button: black", "refused");
        Check(board.CanLeaveReplayAs("Cleo", true), "leave replay button: an organizer", "refused");
        Check(!board.CanLeaveReplayAs("Cleo", false), "leave replay button: not a bystander", "allowed");
        Check(board.CanStepToAs("Ana", false, 3), "step button: back one", "refused");
        Check(board.CanStepToAs("Bob", false, 0), "step button: to the start", "refused");
        Check(board.CanStepToAs("Cleo", true, 1), "step button: an organizer", "refused");
        Check(!board.CanStepToAs("Cleo", false, 1), "step button: not a bystander", "allowed");
        Check(!board.CanStepToAs("Ana", false, 4), "step button: not to the ply shown", "allowed");
        Check(!board.CanStepToAs("Ana", false, 5), "step button: not past the last move", "allowed");
        Check(!board.CanStepToAs("Ana", false, -1), "step button: not before the start", "allowed");
        board.replayCustom = true;
        Check(board.CanStepToAs("Ana", false, 4), "step button: to the ply shown from a custom position", "refused");
        board.replayCustom = false;
        board.replayIndex = 0;
        Check(board.CanStepToAs("Ana", false, 4), "step button: forward to the last move", "refused");
        Check(!board.CanStepToAs("Ana", false, 0), "step button: not to the start shown", "allowed");

        board.lifecycle = ChessBoard.LifecycleEnded;
        Check(board.CanLeaveReplayAs("Ana", false), "leave replay button: once ended", "refused");
        Check(board.CanStepToAs("Bob", false, 2), "step button: once ended", "refused");
        board.replayMode = false;
        Check(board.CanEnterReplayAs("Bob", false), "enter replay button: once ended", "refused");
        Check(!board.CanEnterReplayAs("Cleo", false), "enter replay button: not a bystander once ended", "allowed");

        // A loaded game: always in replay, stepping by anyone while nobody is seated, by the player once one is.
        board.lifecycle = ChessBoard.LifecycleLoaded;
        board.replayMode = true;
        board.replayIndex = 4;
        board.whitePlayer = "";
        board.blackPlayer = "";
        Check(!board.CanLeaveReplayAs("Cleo", true), "leave replay button: not on a loaded board", "allowed");
        Check(!board.CanEnterReplayAs("Cleo", true), "enter replay button: not on a loaded board", "allowed");
        Check(board.CanStepToAs("Cleo", false, 2), "step button: anyone on a loaded board with no players",
            "refused");
        Check(!board.CanStepToAs("", false, 2), "step button: not an empty name", "allowed");
        board.JoinAs(ChessRules.White, "Ana");
        Check(board.CanStepToAs("Ana", false, 2), "step button: the seated player on a loaded board", "refused");
        Check(!board.CanStepToAs("Cleo", false, 2), "step button: not a bystander once someone is seated",
            "allowed");
        Check(board.CanStepToAs("Cleo", true, 2), "step button: an organizer on a loaded board", "refused");

        board.ClearState();
    }

    void TestTakebackButtons() {
        NothingForced();

        ButtonReplay(0, 2);
        Check(!board.CanRequestTakebackAs(ChessRules.White, "Ana"), "takeback button: not with takebacks off",
            "allowed");
        board.gameSettings = ChessArbiter.SettingTakebacks;
        Check(board.CanRequestTakebackAs(ChessRules.White, "Ana"), "takeback button: white", "refused");
        Check(board.CanRequestTakebackAs(ChessRules.Black, "Bob"), "takeback button: black", "refused");
        Check(!board.CanRequestTakebackAs(ChessRules.Black, "Ana"), "takeback button: not white for black",
            "allowed");
        Check(!board.CanRequestTakebackAs(ChessRules.White, "Cleo"), "takeback button: not a non-player", "allowed");
        Check(!board.CanRequestTakebackAs(2, "Ana"), "takeback button: not colour 2", "allowed");
        board.replayIndex = 0;
        Check(board.CanRequestTakebackAs(ChessRules.White, "Ana"), "takeback button: from the start", "refused");
        board.replayIndex = 4;
        Check(!board.CanRequestTakebackAs(ChessRules.White, "Ana"), "takeback button: not from the last position",
            "allowed");
        board.replayIndex = 2;
        board.replayCustom = true;
        Check(!board.CanRequestTakebackAs(ChessRules.White, "Ana"), "takeback button: not from a custom position",
            "allowed");
        board.replayCustom = false;
        board.replayMode = false;
        Check(!board.CanRequestTakebackAs(ChessRules.White, "Ana"), "takeback button: not out of replay", "allowed");
        board.replayMode = true;
        FlagFallen();
        Check(!board.CanRequestTakebackAs(ChessRules.White, "Ana"), "takeback button: not after the flag",
            "allowed");
        board.turnStart = board.ServerTime();
        board.lifecycle = ChessBoard.LifecycleEnded;
        Check(board.CanRequestTakebackAs(ChessRules.White, "Ana"), "takeback button: once ended", "refused");
        board.lifecycle = ChessBoard.LifecycleLoaded;
        Check(!board.CanRequestTakebackAs(ChessRules.White, "Ana"), "takeback button: not on a loaded board",
            "allowed");
        board.lifecycle = ChessBoard.LifecycleInGame;

        // White's request to continue from ply 2: Black answers it while ply 2 is still shown.
        board.takebackColor = ChessRules.White;
        board.takebackPly = 2;
        Check(!board.CanRequestTakebackAs(ChessRules.Black, "Bob"), "takeback button: not with one pending",
            "allowed");
        Check(board.CanAcceptTakebackAs(ChessRules.Black, "Bob"), "accept takeback button: black", "refused");
        Check(board.CanDeclineTakebackAs(ChessRules.Black, "Bob"), "decline takeback button: black", "refused");
        Check(!board.CanAcceptTakebackAs(ChessRules.White, "Ana"), "accept takeback button: not the requester",
            "allowed");
        Check(!board.CanDeclineTakebackAs(ChessRules.White, "Ana"), "decline takeback button: not the requester",
            "allowed");
        Check(!board.CanAcceptTakebackAs(ChessRules.Black, "Ana"), "accept takeback button: not white for black",
            "allowed");
        Check(!board.CanAcceptTakebackAs(ChessRules.Black, "Cleo"), "accept takeback button: not a non-player",
            "allowed");
        Check(!board.CanAcceptTakebackAs(2, "Bob"), "accept takeback button: not colour 2", "allowed");
        board.replayIndex = 1;
        Check(!board.CanAcceptTakebackAs(ChessRules.Black, "Bob"),
            "accept takeback button: not once the position changed", "allowed");
        board.replayIndex = 2;
        board.replayCustom = true;
        Check(!board.CanAcceptTakebackAs(ChessRules.Black, "Bob"),
            "accept takeback button: not from a custom position", "allowed");
        board.replayCustom = false;
        board.gameSettings = 0;
        Check(!board.CanAcceptTakebackAs(ChessRules.Black, "Bob"),
            "accept takeback button: not once takebacks are off", "allowed");
        board.gameSettings = ChessArbiter.SettingTakebacks;
        board.lifecycle = ChessBoard.LifecycleEnded;
        Check(board.CanAcceptTakebackAs(ChessRules.Black, "Bob"), "accept takeback button: once ended", "refused");
        board.takebackColor = ChessRules.Black;
        Check(board.CanAcceptTakebackAs(ChessRules.White, "Ana"), "accept takeback button: white, black's request",
            "refused");
        Check(!board.CanAcceptTakebackAs(ChessRules.Black, "Bob"),
            "accept takeback button: not black, black's request", "allowed");

        board.ClearState();
    }

    void TestContinueResetLoadButtons() {
        NothingForced();

        // "Continue from here" on a loaded game: both seats filled, a seated player, a position of the game.
        ButtonReplay(0, 2);
        Check(!board.CanContinueAs("Ana"), "continue button: not in game", "allowed");
        board.lifecycle = ChessBoard.LifecycleLoaded;
        Check(board.CanContinueAs("Ana"), "continue button: white", "refused");
        Check(board.CanContinueAs("Bob"), "continue button: black", "refused");
        Check(!board.CanContinueAs("Cleo"), "continue button: not a non-player", "allowed");
        board.replayIndex = 4;
        Check(board.CanContinueAs("Ana"), "continue button: from the last position", "refused");
        board.replayIndex = 0;
        Check(board.CanContinueAs("Ana"), "continue button: from the start", "refused");
        board.replayCustom = true;
        Check(!board.CanContinueAs("Ana"), "continue button: not from a custom position", "allowed");
        board.replayCustom = false;
        board.blackPlayer = "";
        Check(!board.CanContinueAs("Ana"), "continue button: not with a seat empty", "allowed");
        board.blackPlayer = "Bob";
        board.lifecycle = ChessBoard.LifecycleEnded;
        Check(!board.CanContinueAs("Ana"), "continue button: not once ended", "allowed");

        // Reset: a seated player or an organizer, on any board that isn't reset; loading only onto a reset board.
        board.ClearState();
        Check(!board.CanResetAs("Ana", false), "reset button: not on a reset board", "allowed");
        Check(!board.CanResetAs("Cleo", true), "reset button: not on a reset board for an organizer", "allowed");
        Check(board.CanLoadGame(), "load: on a reset board", "refused");
        board.JoinAs(ChessRules.White, "Ana");
        Check(board.CanResetAs("Ana", false), "reset button: the player in the lobby", "refused");
        Check(!board.CanResetAs("Cleo", false), "reset button: not a bystander in the lobby", "allowed");
        Check(board.CanResetAs("Cleo", true), "reset button: an organizer in the lobby", "refused");
        Check(!board.CanResetAs("", false), "reset button: not an empty name", "allowed");
        Check(!board.CanLoadGame(), "load: not in the lobby", "allowed");
        ButtonGame(0);
        Check(board.CanResetAs("Bob", false), "reset button: a player in game", "refused");
        Check(!board.CanResetAs("Cleo", false), "reset button: not a bystander in game", "allowed");
        Check(board.CanResetAs("Cleo", true), "reset button: an organizer in game", "refused");
        Check(!board.CanLoadGame(), "load: not in game", "allowed");
        board.lifecycle = ChessBoard.LifecycleEnded;
        Check(board.CanResetAs("Ana", false), "reset button: a player once ended", "refused");
        Check(!board.CanResetAs("Cleo", false), "reset button: not a bystander once ended", "allowed");
        Check(!board.CanLoadGame(), "load: not once ended", "allowed");
        board.lifecycle = ChessBoard.LifecycleLoaded;
        board.whitePlayer = "";
        board.blackPlayer = "";
        Check(!board.CanResetAs("Cleo", false), "reset button: not a bystander on a loaded board", "allowed");
        Check(board.CanResetAs("Cleo", true), "reset button: an organizer on a loaded board", "refused");
        Check(!board.CanLoadGame(), "load: not on a loaded board", "allowed");

        board.ClearState();
    }

    // The local player's checks, seated in both seats.
    void TestLocalButtons() {
        NothingForced();
        board.ClearState();
        Check(board.CanJoin(ChessRules.White), "local buttons: join white", "refused");
        board.JoinWhite();
        board.JoinBlack();
        Check(!board.CanJoin(ChessRules.Black), "local buttons: not a seat held", "allowed");
        Check(board.CanSetMode(), "local buttons: mode", "refused");
        Check(board.CanSetClock(), "local buttons: clock", "refused");
        Check(board.CanStartGame(), "local buttons: start", "refused");
        board.StartGame();
        board.gameSettings = ChessArbiter.SettingDrawAgreement | ChessArbiter.SettingTakebacks;
        Check(board.CanResign(ChessRules.White), "local buttons: resign for white", "refused");
        Check(board.CanResign(ChessRules.Black), "local buttons: resign for black", "refused");
        Check(board.CanOfferDraw(ChessRules.White), "local buttons: offer a draw", "refused");
        board.drawOfferColor = ChessRules.White;
        Check(board.CanAcceptDraw(ChessRules.Black), "local buttons: accept the own offer from the other seat",
            "refused");
        Check(board.CanDeclineDraw(ChessRules.Black), "local buttons: decline it", "refused");
        board.drawOfferColor = ChessRules.NoColor;
        Check(!board.CanClaimDraw(), "local buttons: nothing to claim", "allowed");
        Check(board.CanEnterReplay(), "local buttons: enter replay", "refused");
        board.PieceDropped(ChessRules.E2, ChessRules.E4);
        board.replayMode = true;
        board.replayIndex = 1;
        Check(board.CanLeaveReplay(), "local buttons: leave replay", "refused");
        Check(board.CanStepTo(0), "local buttons: step", "refused");
        board.replayIndex = 0;
        Check(board.CanRequestTakeback(ChessRules.White), "local buttons: request a takeback", "refused");
        board.takebackColor = ChessRules.White;
        board.takebackPly = 0;
        Check(board.CanAcceptTakeback(ChessRules.Black), "local buttons: accept a takeback", "refused");
        Check(board.CanDeclineTakeback(ChessRules.Black), "local buttons: decline a takeback", "refused");
        Check(board.CanReset(), "local buttons: reset", "refused");
        board.lifecycle = ChessBoard.LifecycleLoaded;
        Check(board.CanContinue(), "local buttons: continue a loaded game", "refused");

        board.ClearState();
    }

    // ---- 7.5a game endings ----

    void CheckEnded(int ending, int winner, string result, string name) {
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, name + ": ended");
        CheckInt(board.ending, ending, name + ": ending");
        CheckInt(board.winner, winner, name + ": winner");
        CheckText(ChessPgn.ResultText(board.ending, board.winner), result, name + ": result");
    }

    void CheckInGame(string name) {
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, name + ": still in game");
        CheckInt(board.ending, ChessGame.EndingNone, name + ": no ending");
        CheckInt(board.winner, ChessRules.NoColor, name + ": no winner");
    }

    void TestMateAndStalemate() {
        NothingForced();

        // Fool's mate with a clock: Black wins, and the clocks stop with what each side had left.
        StartedGame(60, 2, 1000);
        Play(ChessRules.F2, ChessRules.F3, 1001, "mate: 1. f3");
        Play(ChessRules.E7, ChessRules.E5, 1002, "mate: 1... e5");
        Play(ChessRules.G2, ChessRules.G4, 1003, "mate: 2. g4");
        CheckInGame("mate: before the mate");
        Play(ChessRules.D8, ChessRules.H4, 1004.5, "mate: 2... Qh4#");
        CheckEnded(ChessGame.EndingCheckmate, ChessRules.Black, "0-1", "mate: fool's mate");
        CheckInt(board.moveCount, 4, "mate: the mating move recorded");
        CheckInt(board.clockHistory[3], 61500, "mate: the mating move's clock entry");
        Check(!board.IsClockRunning(), "mate: clock stopped", "running");
        CheckTimes(62000, 61500, 1e6, "mate: times kept");
        CheckInt(board.FlaggedColorAt(1e6), ChessRules.NoColor, "mate: no flag fall later");
        Check(!board.CheckFlagFallAt(1e6), "mate: no time forfeit later", "forfeit");
        CheckInt(board.ending, ChessGame.EndingCheckmate, "mate: still checkmate");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.F2), ChessBoard.InputRejected,
            "mate: no move after it");
        Check(!board.CanResignAs(ChessRules.White, "Ana"), "mate: no resigning after it", "allowed");

        // Scholar's mate by drops with every draw rule on: White wins. A check that isn't mate goes on.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = ChessArbiter.SettingsDrawRules;
        board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4);
        board.PieceDroppedAs("Bob", ChessRules.E7, ChessRules.E5);
        board.PieceDroppedAs("Ana", ChessRules.F1, ChessRules.C4);
        board.PieceDroppedAs("Bob", ChessRules.B8, ChessRules.C6);
        board.PieceDroppedAs("Ana", ChessRules.D1, ChessRules.H5);
        board.PieceDroppedAs("Bob", ChessRules.G8, ChessRules.F6);
        CheckInGame("mate: scholar's, before the mate");
        // White's own offer would survive White's move; the mate lets it lapse.
        board.drawOfferColor = ChessRules.White;
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.H5, ChessRules.F7), ChessBoard.InputAccepted,
            "mate: 4. Qxf7# accepted");
        CheckText(board.notation[6], "Qxf7#", "mate: SAN Qxf7#");
        CheckEnded(ChessGame.EndingCheckmate, ChessRules.White, "1-0", "mate: scholar's mate");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "mate: a pending draw offer lapses");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E8, ChessRules.F7), ChessBoard.InputRejected,
            "mate: no reply");

        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4);
        board.PieceDroppedAs("Bob", ChessRules.F7, ChessRules.F6);
        board.PieceDroppedAs("Ana", ChessRules.D1, ChessRules.H5);
        CheckText(board.notation[2], "Qh5+", "check: SAN Qh5+");
        CheckInGame("check: a check that isn't mate");

        // Stalemate ends the game as a draw under "No draws" and with every draw rule on.
        StartedGame(60, 0, 1000);
        SetPosition("7k/8/8/5Q2/8/8/8/K7 w - - 0 1");
        Play(ChessRules.F5, ChessRules.G6, 1003, "stalemate: Qg6");
        CheckEnded(ChessGame.EndingStalemate, ChessRules.NoColor, "1/2-1/2", "stalemate: no draws");
        Check(!board.IsClockRunning(), "stalemate: clock stopped", "running");
        CheckTimes(57000, 60000, 1e6, "stalemate: times kept");

        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = ChessArbiter.SettingsDrawRules;
        SetPosition("7k/8/8/5Q2/8/8/8/K7 w - - 0 1");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.F5, ChessRules.G6), ChessBoard.InputAccepted,
            "stalemate: Qg6 dropped");
        CheckEnded(ChessGame.EndingStalemate, ChessRules.NoColor, "1/2-1/2", "stalemate: every draw rule on");

        // A king in the corner with a square left isn't stalemated.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        SetPosition("7k/8/4Q3/8/8/8/8/K7 w - - 0 1");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E6, ChessRules.E7), ChessBoard.InputAccepted,
            "no stalemate: Qe7");
        CheckInGame("no stalemate: Kg8 left");

        board.ClearState();
    }

    void TestResign() {
        NothingForced();

        // Each player for their own colour: the other side wins.
        ButtonGame(0);
        CheckInt(board.ResignAs(ChessRules.Black, "Ana"), ChessBoard.InputRejected, "resign: not white for black");
        CheckInt(board.ResignAs(ChessRules.White, "Bob"), ChessBoard.InputRejected, "resign: not black for white");
        CheckInt(board.ResignAs(ChessRules.White, "Cleo"), ChessBoard.InputRejected, "resign: not a non-player");
        CheckInt(board.ResignAs(ChessRules.White, ""), ChessBoard.InputRejected, "resign: not an empty name");
        CheckInt(board.ResignAs(2, "Ana"), ChessBoard.InputRejected, "resign: not colour 2");
        CheckInGame("resign: refused");
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "resign: white resigns");
        CheckEnded(ChessGame.EndingResignation, ChessRules.Black, "0-1", "resign: white resigned");
        CheckInt(board.ResignAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected, "resign: not once ended");
        CheckInt(board.winner, ChessRules.Black, "resign: the result stays");

        // Black resigns while White is to move, from replay; a draw offer lapses, a takeback request stays.
        ButtonGame(ChessArbiter.SettingDrawAgreement | ChessArbiter.SettingTakebacks);
        board.drawOfferColor = ChessRules.White;
        board.replayMode = true;
        board.replayIndex = 1;
        board.takebackColor = ChessRules.White;
        board.takebackPly = 1;
        CheckInt(board.ResignAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "resign: black, in replay");
        CheckEnded(ChessGame.EndingResignation, ChessRules.White, "1-0", "resign: black resigned");
        Check(board.replayMode && board.replayIndex == 1, "resign: replay kept", "replay changed");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "resign: the draw offer lapses");
        CheckInt(board.takebackColor, ChessRules.White, "resign: the takeback request stays");
        CheckInt(board.moveCount, 4, "resign: the moves stay");

        // Not once the flag has fallen (that's a time forfeit).
        ButtonGame(0);
        FlagFallen();
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputRejected, "resign: not after the flag");
        CheckInt(board.ending, ChessGame.EndingNone, "resign: refused after the flag, no ending");

        // Not before a game.
        LobbyWithTwoPlayers();
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputRejected, "resign: not in the lobby");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "resign: lobby stays");

        // With a clock: it stops with the time the side to move had left.
        StartedGame(60, 0, board.ServerTime() - 2);
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "resign: with a clock");
        Check(board.whiteTime > 57000 && board.whiteTime <= 58000, "resign: white's time stopped after 2 s",
            "time " + board.whiteTime);
        CheckInt(board.blackTime, 60000, "resign: black's time untouched");
        Check(!board.IsClockRunning(), "resign: clock stopped", "running");
        CheckInt(board.RemainingTimeAt(ChessRules.White, board.ServerTime() + 1000), board.whiteTime,
            "resign: white's time stays");

        // The local player in both seats chooses the side.
        board.ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        CheckInt(board.Resign(ChessRules.Black), ChessBoard.InputAccepted, "resign: local player for black");
        CheckEnded(ChessGame.EndingResignation, ChessRules.White, "1-0", "resign: local player");
        CheckInt(board.Resign(ChessRules.White), ChessBoard.InputRejected, "resign: local player, not twice");

        board.ClearState();
    }

    // A game with a 60 s clock from server time 1000 in the position fen, with these settings, ended by
    // CheckFlagFallAt at 1060, when the side to move's flag falls.
    void CheckForfeit(string fen, int settings, int winner, string result, string name) {
        StartedGame(60, 0, 1000);
        board.gameSettings = settings;
        SetPosition(fen);
        CheckText(Fen(), fen, name + ": position set up");
        Check(board.CheckFlagFallAt(1060), name + ": forfeit", "not ended");
        CheckEnded(ChessGame.EndingTimeForfeit, winner, result, name);
    }

    void TestTimeForfeit() {
        NothingForced();
        int allDraws = ChessArbiter.SettingsDrawRules;
        int dead = ChessArbiter.SettingDeadPosition;

        // White's flag falls at exactly 0.
        StartedGame(60, 0, 1000);
        board.drawOfferColor = ChessRules.Black;
        Check(!board.CheckFlagFallAt(1059.5), "forfeit: not with 0.5 s left", "ended");
        CheckInGame("forfeit: 0.5 s left");
        Check(board.CheckFlagFallAt(1060), "forfeit: white's flag", "not ended");
        CheckEnded(ChessGame.EndingTimeForfeit, ChessRules.Black, "0-1", "forfeit: white out of time");
        CheckInt(board.whiteTime, 0, "forfeit: white's time 0");
        CheckInt(board.blackTime, 60000, "forfeit: black's time untouched");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "forfeit: the draw offer lapses");
        Check(!board.IsClockRunning(), "forfeit: clock stopped", "running");
        CheckTimes(0, 60000, 1e6, "forfeit: times kept");
        Check(!board.CheckFlagFallAt(1e6), "forfeit: only once", "ended again");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4), ChessBoard.InputRejected,
            "forfeit: no move after it");
        Check(!board.CanResignAs(ChessRules.Black, "Bob"), "forfeit: no resigning after it", "allowed");

        // Black's flag; every draw rule on doesn't help with mating material on the board.
        StartedGame(60, 0, 1000);
        board.gameSettings = allDraws;
        Play(ChessRules.E2, ChessRules.E4, 1000, "forfeit: 1. e4");
        Check(!board.CheckFlagFallAt(1059.5), "forfeit: black not yet", "ended");
        Check(board.CheckFlagFallAt(1060), "forfeit: black's flag", "not ended");
        CheckEnded(ChessGame.EndingTimeForfeit, ChessRules.White, "1-0", "forfeit: black out of time");
        CheckInt(board.blackTime, 0, "forfeit: black's time 0");

        // The opponent can't checkmate: a draw with the dead-position rule on, a loss otherwise.
        string loneKing = "k7/8/8/8/8/8/8/KQR5 w - - 0 1";
        CheckForfeit(loneKing, dead, ChessRules.NoColor, "1/2-1/2", "forfeit: lone king, dead position on");
        CheckForfeit(loneKing, allDraws, ChessRules.NoColor, "1/2-1/2", "forfeit: lone king, every draw rule");
        CheckForfeit(loneKing, 0, ChessRules.Black, "0-1", "forfeit: lone king, no draws");
        CheckForfeit(loneKing, allDraws & ~dead, ChessRules.Black, "0-1", "forfeit: lone king, dead position off");
        CheckForfeit(loneKing, ChessArbiter.SettingTakebacks | ChessArbiter.SettingModeForced
            | ChessArbiter.SettingClockForced, ChessRules.Black, "0-1", "forfeit: lone king, only other settings");
        CheckForfeit("k7/8/8/8/8/8/8/K5n1 w - - 0 1", dead, ChessRules.NoColor, "1/2-1/2",
            "forfeit: K v K+N");
        CheckForfeit("k7/8/8/8/8/8/P7/K5n1 w - - 0 1", dead, ChessRules.Black, "0-1",
            "forfeit: K+P v K+N can be mated");
        CheckForfeit("k7/8/8/8/8/8/8/K1N4n w - - 0 1", dead, ChessRules.Black, "0-1",
            "forfeit: K+N v K+N can be mated");
        CheckForfeit("k4b2/8/8/8/8/8/8/K1B5 w - - 0 1", dead, ChessRules.NoColor, "1/2-1/2",
            "forfeit: bishops on one colour");
        CheckForfeit("k3b3/8/8/8/8/8/8/K1B5 w - - 0 1", dead, ChessRules.Black, "0-1",
            "forfeit: bishops on both colours");
        CheckForfeit("k7/8/8/8/8/8/8/K1B5 b - - 0 1", dead, ChessRules.NoColor, "1/2-1/2",
            "forfeit: black flags against K+B");
        CheckForfeit("k7/8/8/3q4/8/8/8/K1B5 b - - 0 1", dead, ChessRules.White, "1-0",
            "forfeit: K+Q flags against K+B");
        CheckForfeit("k7/8/8/8/8/8/P7/K7 b - - 0 1", dead, ChessRules.White, "1-0",
            "forfeit: a lone king flags against a pawn");

        // In replay too: replay doesn't stop the clock.
        StartedGame(60, 0, 1000);
        board.replayMode = true;
        Check(board.CheckFlagFallAt(1060), "forfeit: in replay", "not ended");
        CheckEnded(ChessGame.EndingTimeForfeit, ChessRules.Black, "0-1", "forfeit: in replay");
        Check(board.replayMode, "forfeit: replay kept", "left replay");

        // Never without a clock, before a game, or on a loaded board.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        Check(!board.CheckFlagFallAt(1e9), "forfeit: never without a clock", "ended");
        CheckInGame("forfeit: no clock");
        LobbyWithTwoPlayers();
        board.SetClockAs("Ana", 60, 0);
        Check(!board.CheckFlagFallAt(1e9), "forfeit: not in the lobby", "ended");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "forfeit: lobby stays");
        board.lifecycle = ChessBoard.LifecycleLoaded;
        Check(!board.CheckFlagFallAt(1e9), "forfeit: not on a loaded board", "ended");

        // The real server time.
        StartedGame(60, 0, 0);
        board.turnStart = board.ServerTime();
        Check(!board.CheckFlagFall(), "forfeit: server time, time left", "ended");
        FlagFallen();
        Check(board.CheckFlagFall(), "forfeit: server time, flag fallen", "not ended");
        CheckEnded(ChessGame.EndingTimeForfeit, ChessRules.Black, "0-1", "forfeit: server time");

        board.ClearState();
    }

    // ---- 7.5b draws ----

    // name drops from-to, which must be accepted.
    void Drop(string name, int from, int to, string label) {
        CheckInt(board.PieceDroppedAs(name, from, to), ChessBoard.InputAccepted,
            label + ": " + ChessRules.SquareName(from) + ChessRules.SquareName(to) + " accepted");
    }

    // Plays Nf3 Nf6 Ng1 Ng8 times times, each time bringing back the starting position.
    void KnightShuffle(int times, string label) {
        for (int i = 0; i < times; i++) {
            Drop("Ana", ChessRules.G1, ChessRules.F3, label);
            Drop("Bob", ChessRules.G8, ChessRules.F6, label);
            Drop("Ana", ChessRules.F3, ChessRules.G1, label);
            Drop("Bob", ChessRules.F6, ChessRules.G8, label);
        }
    }

    void TestDrawAgreement() {
        NothingForced();
        int agreement = ChessArbiter.SettingDrawAgreement;

        // White offers, Black declines; Black offers, White accepts.
        StartedGame(60, 0, board.ServerTime() - 2);
        board.gameSettings = agreement;
        CheckInt(board.OfferDrawAs(ChessRules.Black, "Ana"), ChessBoard.InputRejected, "draw offer: not white for black");
        CheckInt(board.OfferDrawAs(ChessRules.White, "Cleo"), ChessBoard.InputRejected, "draw offer: not a non-player");
        CheckInt(board.OfferDrawAs(2, "Ana"), ChessBoard.InputRejected, "draw offer: not colour 2");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "draw offer: refused offers leave none");
        CheckInt(board.OfferDrawAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "draw offer: white offers");
        CheckInt(board.drawOfferColor, ChessRules.White, "draw offer: white's offer pending");
        CheckInt(board.OfferDrawAs(ChessRules.White, "Ana"), ChessBoard.InputRejected, "draw offer: not twice");
        CheckInt(board.OfferDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected,
            "draw offer: not with one pending");
        CheckInt(board.AcceptDrawAs(ChessRules.White, "Ana"), ChessBoard.InputRejected, "draw offer: not one's own");
        CheckInt(board.DeclineDrawAs(ChessRules.White, "Ana"), ChessBoard.InputRejected,
            "draw offer: no declining one's own");
        CheckInt(board.AcceptDrawAs(ChessRules.Black, "Cleo"), ChessBoard.InputRejected,
            "draw offer: no accepting by a non-player");
        CheckInt(board.AcceptDrawAs(ChessRules.Black, "Ana"), ChessBoard.InputRejected,
            "draw offer: no accepting by white for black");
        CheckInGame("draw offer: pending");
        CheckInt(board.DeclineDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "draw offer: black declines");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "draw offer: declined, none pending");
        CheckInGame("draw offer: declined");
        CheckInt(board.AcceptDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected,
            "draw offer: no accepting a declined offer");
        CheckInt(board.OfferDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "draw offer: black offers");
        CheckInt(board.AcceptDrawAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "draw offer: white accepts");
        CheckEnded(ChessGame.EndingDrawAgreement, ChessRules.NoColor, "1/2-1/2", "draw offer: agreed");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "draw offer: none pending once agreed");
        Check(!board.IsClockRunning(), "draw offer: clock stopped", "running");
        Check(board.whiteTime > 57000 && board.whiteTime <= 58000, "draw offer: white's time stopped after 2 s",
            "time " + board.whiteTime);
        CheckInt(board.blackTime, 60000, "draw offer: black's time untouched");
        CheckInt(board.OfferDrawAs(ChessRules.White, "Ana"), ChessBoard.InputRejected, "draw offer: not once ended");
        CheckInt(board.AcceptDrawAs(ChessRules.White, "Ana"), ChessBoard.InputRejected,
            "draw offer: no accepting once ended");

        // A move by the side offered the draw declines it; the offerer's own move keeps it.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = agreement;
        board.OfferDrawAs(ChessRules.White, "Ana");
        Drop("Ana", ChessRules.E2, ChessRules.E4, "draw offer: 1. e4");
        CheckInt(board.drawOfferColor, ChessRules.White, "draw offer: kept after the offerer's move");
        Drop("Bob", ChessRules.E7, ChessRules.E5, "draw offer: 1... e5");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "draw offer: lapses on the other side's move");
        CheckInt(board.OfferDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted,
            "draw offer: black offers, not to move");
        Drop("Ana", ChessRules.G1, ChessRules.F3, "draw offer: 2. Nf3");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "draw offer: lapses on white's move");
        CheckInt(board.AcceptDrawAs(ChessRules.White, "Ana"), ChessBoard.InputRejected,
            "draw offer: no accepting a lapsed offer");
        CheckInGame("draw offer: lapsed");

        // In replay: offering and accepting aren't moves.
        ButtonGame(agreement);
        board.replayMode = true;
        board.replayIndex = 2;
        CheckInt(board.OfferDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "draw offer: in replay");
        CheckInt(board.AcceptDrawAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted,
            "draw offer: accepted in replay");
        CheckEnded(ChessGame.EndingDrawAgreement, ChessRules.NoColor, "1/2-1/2", "draw offer: agreed in replay");
        Check(board.replayMode && board.replayIndex == 2, "draw offer: replay kept", "replay changed");

        // Only with draws by agreement on: not under "No draws" or with only the other draw rules.
        ButtonGame(0);
        CheckInt(board.OfferDrawAs(ChessRules.White, "Ana"), ChessBoard.InputRejected, "draw offer: not under No draws");
        board.gameSettings = ChessArbiter.SettingsDrawRules & ~agreement;
        CheckInt(board.OfferDrawAs(ChessRules.White, "Ana"), ChessBoard.InputRejected,
            "draw offer: not with only the other draw rules");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "draw offer: none made with agreement off");
        // A re-snapshot that turns agreement off leaves a pending offer unanswerable.
        board.gameSettings = agreement;
        board.OfferDrawAs(ChessRules.White, "Ana");
        board.gameSettings = 0;
        CheckInt(board.AcceptDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected,
            "draw offer: no accepting once agreement is off");
        CheckInt(board.DeclineDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected,
            "draw offer: no declining once agreement is off");
        CheckInGame("draw offer: agreement off");

        // Not after the flag (that's a time forfeit), and not before a game.
        ButtonGame(agreement);
        board.OfferDrawAs(ChessRules.Black, "Bob");
        FlagFallen();
        CheckInt(board.AcceptDrawAs(ChessRules.White, "Ana"), ChessBoard.InputRejected,
            "draw offer: no accepting after the flag");
        CheckInt(board.ending, ChessGame.EndingNone, "draw offer: refused after the flag, no ending");
        LobbyWithTwoPlayers();
        board.gameSettings = agreement;
        CheckInt(board.OfferDrawAs(ChessRules.White, "Ana"), ChessBoard.InputRejected, "draw offer: not in the lobby");

        // The local player in both seats offers for one side and accepts for the other.
        board.ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        board.gameSettings = agreement;
        CheckInt(board.OfferDraw(ChessRules.White), ChessBoard.InputAccepted, "draw offer: local player offers");
        CheckInt(board.DeclineDraw(ChessRules.Black), ChessBoard.InputAccepted, "draw offer: local player declines");
        CheckInt(board.OfferDraw(ChessRules.Black), ChessBoard.InputAccepted, "draw offer: local player offers again");
        CheckInt(board.AcceptDraw(ChessRules.Black), ChessBoard.InputRejected,
            "draw offer: local player, not for the offering side");
        CheckInt(board.AcceptDraw(ChessRules.White), ChessBoard.InputAccepted, "draw offer: local player accepts");
        CheckEnded(ChessGame.EndingDrawAgreement, ChessRules.NoColor, "1/2-1/2", "draw offer: local player");

        board.ClearState();
    }

    void TestDrawClaims() {
        NothingForced();
        int threefold = ChessArbiter.SettingThreefoldRepetition;
        int fifty = ChessArbiter.SettingFiftyMoveRule;

        // Threefold: the starting position for the third time after 8 plies, claimed by White (to move).
        ButtonGame(threefold);
        Drop("Ana", ChessRules.G1, ChessRules.F3, "claim threefold");
        Drop("Bob", ChessRules.G8, ChessRules.F6, "claim threefold");
        Drop("Ana", ChessRules.F3, ChessRules.G1, "claim threefold");
        CheckInt(board.ClaimDrawAs("Bob"), ChessBoard.InputRejected, "claim threefold: nothing after 7 plies");
        Drop("Bob", ChessRules.F6, ChessRules.G8, "claim threefold");
        CheckInGame("claim threefold: not automatic");
        CheckInt(board.ClaimDrawAs("Bob"), ChessBoard.InputRejected, "claim threefold: not the side not to move");
        CheckInt(board.ClaimDrawAs("Cleo"), ChessBoard.InputRejected, "claim threefold: not a non-player");
        CheckInt(board.ClaimDrawAs(""), ChessBoard.InputRejected, "claim threefold: not an empty name");
        CheckInGame("claim threefold: refused claims");
        CheckInt(board.ClaimDrawAs("Ana"), ChessBoard.InputAccepted, "claim threefold: white claims");
        CheckEnded(ChessGame.EndingThreefoldRepetition, ChessRules.NoColor, "1/2-1/2", "claim threefold");
        CheckInt(board.ClaimDrawAs("Ana"), ChessBoard.InputRejected, "claim threefold: not once ended");

        // The same repetition with threefold off, or under "No draws": no claim, play goes on.
        ButtonGame(0);
        KnightShuffle(1, "no claim");
        board.gameSettings = ChessArbiter.SettingsDrawRules & ~threefold & ~ChessArbiter.SettingFivefoldRepetition;
        CheckInt(board.ClaimDrawAs("Ana"), ChessBoard.InputRejected, "claim threefold: not with the rule off");
        board.gameSettings = 0;
        CheckInt(board.ClaimDrawAs("Ana"), ChessBoard.InputRejected, "claim threefold: not under No draws");
        CheckInGame("claim threefold: no claim");
        Drop("Ana", ChessRules.G1, ChessRules.F3, "no claim: play goes on");

        // Both claims available: threefold is the one claimed.
        ButtonGame(threefold | fifty);
        KnightShuffle(1, "both claims");
        board.halfmoveClock = 100;
        CheckInt(board.ClaimDrawAs("Ana"), ChessBoard.InputAccepted, "claim: threefold and fifty-move");
        CheckEnded(ChessGame.EndingThreefoldRepetition, ChessRules.NoColor, "1/2-1/2", "claim: threefold first");

        // Fifty-move: claimable by Black once White's move makes the halfmove clock 100.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = fifty;
        SetPosition("8/8/4k3/8/8/4K3/8/R7 w - - 99 80");
        CheckInt(board.ClaimDrawAs("Ana"), ChessBoard.InputRejected, "claim fifty-move: not at 99");
        Drop("Ana", ChessRules.A1, ChessRules.A2, "claim fifty-move: Ra2");
        CheckInt(board.halfmoveClock, 100, "claim fifty-move: halfmove clock 100");
        CheckInGame("claim fifty-move: not automatic");
        CheckInt(board.ClaimDrawAs("Ana"), ChessBoard.InputRejected, "claim fifty-move: not the side not to move");
        board.gameSettings = ChessArbiter.SettingsDrawRules & ~fifty;
        CheckInt(board.ClaimDrawAs("Bob"), ChessBoard.InputRejected, "claim fifty-move: not with the rule off");
        board.gameSettings = fifty;
        board.replayMode = true;
        CheckInt(board.ClaimDrawAs("Bob"), ChessBoard.InputAccepted, "claim fifty-move: black claims, in replay");
        CheckEnded(ChessGame.EndingFiftyMoveRule, ChessRules.NoColor, "1/2-1/2", "claim fifty-move");
        Check(board.replayMode, "claim fifty-move: replay kept", "left replay");

        // A pawn move resets the count: nothing to claim.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = fifty;
        SetPosition("8/8/4k3/8/8/4K3/P7/8 w - - 99 80");
        Drop("Ana", ChessRules.A2, ChessRules.A3, "claim fifty-move: a3");
        CheckInt(board.ClaimDrawAs("Bob"), ChessBoard.InputRejected, "claim fifty-move: not after a pawn move");

        // Not after the flag, and the local player in both seats.
        ButtonGame(threefold);
        KnightShuffle(1, "claim after the flag");
        FlagFallen();
        CheckInt(board.ClaimDrawAs("Ana"), ChessBoard.InputRejected, "claim: not after the flag");
        CheckInt(board.ending, ChessGame.EndingNone, "claim: refused after the flag, no ending");

        board.ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        board.gameSettings = fifty;
        SetPosition("8/8/4k3/8/8/4K3/8/R7 b - - 100 80");
        CheckInt(board.ClaimDraw(), ChessBoard.InputAccepted, "claim: local player");
        CheckEnded(ChessGame.EndingFiftyMoveRule, ChessRules.NoColor, "1/2-1/2", "claim: local player");

        board.ClearState();
    }

    void TestAutomaticDraws() {
        NothingForced();
        int fivefold = ChessArbiter.SettingFivefoldRepetition;
        int seventyFive = ChessArbiter.SettingSeventyFiveMoveRule;
        int dead = ChessArbiter.SettingDeadPosition;
        int allDraws = ChessArbiter.SettingsDrawRules;

        // Fivefold: the starting position for the fifth time after 16 plies ends the game.
        StartedGame(60, 0, board.ServerTime());
        board.gameSettings = fivefold;
        KnightShuffle(3, "fivefold");
        Drop("Ana", ChessRules.G1, ChessRules.F3, "fivefold");
        Drop("Bob", ChessRules.G8, ChessRules.F6, "fivefold");
        Drop("Ana", ChessRules.F3, ChessRules.G1, "fivefold");
        CheckInGame("fivefold: not after 15 plies");
        Drop("Bob", ChessRules.F6, ChessRules.G8, "fivefold");
        CheckEnded(ChessGame.EndingFivefoldRepetition, ChessRules.NoColor, "1/2-1/2", "fivefold");
        CheckInt(board.moveCount, 16, "fivefold: the last move recorded");
        Check(!board.IsClockRunning(), "fivefold: clock stopped", "running");

        // With only the claim rules, or under "No draws", play goes on.
        ButtonGame(allDraws & ~fivefold);
        KnightShuffle(3, "no fivefold");
        CheckInGame("fivefold: not with the rule off");
        CheckInt(board.ClaimableDraw(), ChessGame.EndingThreefoldRepetition, "fivefold off: threefold claimable");
        ButtonGame(0);
        KnightShuffle(4, "no draws");
        CheckInGame("fivefold: not under No draws");
        CheckInt(board.moveCount, 20, "no draws: 20 plies played");

        // Seventy-five-move rule: the game ends when the halfmove clock reaches 150.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = seventyFive;
        SetPosition("8/8/4k3/8/8/4K3/8/R7 w - - 148 80");
        Drop("Ana", ChessRules.A1, ChessRules.A2, "seventy-five: Ra2");
        CheckInGame("seventy-five: not at 149");
        Drop("Bob", ChessRules.E6, ChessRules.D6, "seventy-five: Kd6");
        CheckEnded(ChessGame.EndingSeventyFiveMoveRule, ChessRules.NoColor, "1/2-1/2", "seventy-five");

        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = allDraws & ~seventyFive;
        SetPosition("8/8/4k3/8/8/4K3/8/R7 w - - 149 80");
        Drop("Ana", ChessRules.A1, ChessRules.A2, "seventy-five off: Ra2");
        CheckInGame("seventy-five: not with the rule off");
        board.gameSettings = 0;
        Drop("Bob", ChessRules.E6, ChessRules.D6, "seventy-five off: Kd6");
        CheckInGame("seventy-five: not under No draws");

        // Checkmate on the 150th ply is still checkmate.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = allDraws;
        SetPosition("7k/8/6K1/8/8/8/8/R7 w - - 149 80");
        Drop("Ana", ChessRules.A1, ChessRules.A8, "seventy-five: Ra8#");
        CheckEnded(ChessGame.EndingCheckmate, ChessRules.White, "1-0", "seventy-five: mate first");

        // Dead position: a capture leaving K v K or K+B v K ends the game; K+R v K and opposite bishops don't.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = dead;
        SetPosition("8/8/4k3/8/8/4K3/3r4/8 w - - 0 1");
        Drop("Ana", ChessRules.E3, ChessRules.D2, "dead: Kxd2");
        CheckEnded(ChessGame.EndingInsufficientMaterial, ChessRules.NoColor, "1/2-1/2", "dead: K v K");

        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = dead;
        SetPosition("8/8/4k3/8/8/4K3/3r4/2B5 w - - 0 1");
        Drop("Ana", ChessRules.C1, ChessRules.D2, "dead: Bxd2");
        CheckEnded(ChessGame.EndingInsufficientMaterial, ChessRules.NoColor, "1/2-1/2", "dead: K+B v K");

        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = dead;
        SetPosition("8/8/4k3/8/8/4K3/3n4/R7 w - - 0 1");
        Drop("Ana", ChessRules.E3, ChessRules.D2, "dead: Kxd2 with a rook");
        CheckInGame("dead: K+R v K");

        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = dead;
        SetPosition("4b3/8/4k3/8/8/4K3/3n4/2B5 w - - 0 1");
        Drop("Ana", ChessRules.C1, ChessRules.D2, "dead: Bxd2, bishops on both colours");
        CheckInGame("dead: opposite bishops");

        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = allDraws & ~dead;
        SetPosition("8/8/4k3/8/8/4K3/3r4/8 w - - 0 1");
        Drop("Ana", ChessRules.E3, ChessRules.D2, "dead off: Kxd2");
        CheckInGame("dead: not with the rule off");
        board.gameSettings = 0;
        Drop("Bob", ChessRules.E6, ChessRules.D6, "dead off: Kd6");
        CheckInGame("dead: not under No draws");

        // Stalemate with insufficient material is still stalemate.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = allDraws;
        SetPosition("k7/8/1K6/8/3N4/8/8/8 w - - 0 1");
        Drop("Ana", ChessRules.D4, ChessRules.C6, "dead: Nc6 stalemate");
        CheckEnded(ChessGame.EndingStalemate, ChessRules.NoColor, "1/2-1/2", "dead: stalemate first");

        // A pending draw offer lapses when an automatic draw ends the game.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = dead | ChessArbiter.SettingDrawAgreement;
        SetPosition("8/8/4k3/8/8/4K3/3r4/8 w - - 0 1");
        board.OfferDrawAs(ChessRules.White, "Ana");
        Drop("Ana", ChessRules.E3, ChessRules.D2, "dead with an offer: Kxd2");
        CheckEnded(ChessGame.EndingInsufficientMaterial, ChessRules.NoColor, "1/2-1/2", "dead with an offer");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "dead with an offer: the offer lapses");

        board.ClearState();
    }
}
