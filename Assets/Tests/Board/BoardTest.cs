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
}
