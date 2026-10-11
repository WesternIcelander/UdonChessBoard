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

        // The clocks stop when the game ends (ending it is 7.5).
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
}
