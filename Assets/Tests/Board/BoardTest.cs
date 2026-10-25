using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Step 7 test behaviour (PLAN.md section 7): the logical board driven with no world objects, plus the single-client
// sync checks of 8.3 (packing and receiving the synced state), the ownership rules of 8.4, the inactivity rules of
// 8.5, the notation fill-in of 8.6 and the import of 8.7. Runs only with no one else in the instance. Logs
// "[BoardTest] PASS: <check>" or "[BoardTest] FAIL: <check>: <details>" per check, then a summary line.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class BoardTest : ChessTest {
    protected override string TestName() {
        return "BoardTest";
    }

    // The board under test; its checks fail if it isn't assigned. The checks clear it and leave it in the reset state.
    public ChessBoard board;
    // For the loading checks (7.9a): Assets/Tests/PGN/Siggi-vs-Nuka.pgn.txt and an importer to read PGNs with. The
    // loading checks fail if either isn't assigned.
    public TextAsset siggiVsNuka;
    public ChessPgnImporter importer;

    string localName;
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

    // The checks run over three frames (RunChecks, RunSyncChecks, RunFillChecks), each ending with the board reset:
    // Udon halts a behaviour whose event runs past 10 s, and all of them in one frame take longer than that. An import
    // over real frames follows (PollImport), then the summary.
    public void RunChecks() {
        if (ran || !started || !joined) return;
        ran = true;
        // The board is synced (8.3): every change the checks make takes it over and is sent, so with anyone else here
        // they would overwrite the board those players see (a game in progress, for a late joiner).
        if (VRCPlayerApi.GetPlayerCount() > 1) {
            Debug.Log("[BoardTest] skipped: other players are in the instance, and the checks would send their games "
                + "to them through the synced board");
            finished = true;
            return;
        }
        if (!Utilities.IsValid(board)) {
            Check(false, "board assigned", "the board field is empty");
            LogSummary();
            return;
        }
        localName = Networking.LocalPlayer.displayName;
        NothingForced();
        TestResetState();
        TestJoin();
        TestJoinBothSeats();
        TestSeatTaken();
        TestJoinInvalid();
        TestJoinByLifecycle();
        TestLeaveSeat();
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
        TestGameIdFormat();
        TestGameIds();
        TestPgnLog();
        TestReplay();
        TestPlaceCaptured();
        TestTakeback();
        TestContinueAlone();
        TestLoadGame();
        TestContinueLoaded();
        TestLoadedBy();
        TestReset();
        board._ClearState();
        SendCustomEventDelayedFrames(nameof(RunSyncChecks), 1);
    }

    // The second frame: snapshots, listeners and scenarios (7.9 to 7.12), sync, ownership and inactivity (8.3 to 8.5),
    // playing on untimed after a flag fall (12.9), the synced piece sets (13.6), who may touch the pieces (9.2) and
    // pick them up (9.3c).
    public void RunSyncChecks() {
        TestSnapshot();
        TestListeners();
        TestScenarios();
        TestSync();
        TestPlayOn();
        TestOwnership();
        TestPieceSets();
        TestInactivity();
        TestTouchPieces();
        TestCanPickUp();
        board._ClearState();
        SendCustomEventDelayedFrames(nameof(RunFillChecks), 1);
    }

    // The third frame: the notation fill-in (8.6) and the import (8.7), then an import over the next frames
    // (PollImport), then the summary.
    public void RunFillChecks() {
        TestNotationFill();
        TestImport();
        StartFrameImport();
    }

    void Finish() {
        // Every [ChessPGN] entry in the log is one of these, so the two counts must agree.
        Debug.Log("[BoardTest] games logged by the board: " + board.loggedGames);
        if (Utilities.IsValid(board.arbiter)) board.arbiter.ClearOverrides(board.boardNumber);
        board._ClearState();
        LogSummary();
    }

    void CheckSeats(string white, string black, string name) {
        CheckString(board.whitePlayer, white, name + ": white seat");
        CheckString(board.blackPlayer, black, name + ": black seat");
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
        board._ClearState();

        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, "reset: lifecycle");
        CheckSeats("", "", "reset");
        CheckInt(board.ending, ChessGame.EndingNone, "reset: ending");
        CheckInt(board.winner, ChessRules.NoColor, "reset: winner");
        CheckInt(board.moveCount, 0, "reset: move count");
        CheckString(board.gameId, "", "reset: game ID");
        CheckString(board.continuedFrom, "", "reset: continued from");
        CheckInt(board.mode, ChessGame.ModeStandard, "reset: mode");
        Check(!board.replayMode && !board.replayCustom, "reset: not in replay", "replay flags set");
        CheckInt(board.takebackColor, ChessRules.NoColor, "reset: no takeback request");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "reset: no draw offer");
        CheckString(ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber),
            ChessFen.StartFen, "reset: standard starting position");
        CheckString(board.startFen, ChessFen.StartFen, "reset: starting FEN");
        bool notationEmpty = true;
        for (int i = 0; i < board.notation.Length; i++) {
            if (board.notation[i] != "") notationEmpty = false;
        }
        Check(notationEmpty, "reset: notation entries all \"\" (never null)", "an entry is null or not empty");
    }

    void TestJoin() {
        board._ClearState();
        CheckInt(board.JoinWhite(), ChessBoard.InputAccepted, "join white: accepted");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "join white: reset board enters the lobby");
        CheckSeats(localName, "", "join white");
        Check(board.IsSeated(ChessRules.White, localName), "join white: IsSeated", "not seated");

        board._ClearState();
        CheckInt(board.JoinBlack(), ChessBoard.InputAccepted, "join black first: accepted");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "join black first: reset board enters the lobby");
        CheckSeats("", localName, "join black first");
    }

    void TestJoinBothSeats() {
        board._ClearState();
        board.JoinWhite();
        CheckInt(board.JoinBlack(), ChessBoard.InputAccepted, "same player both seats: accepted");
        CheckSeats(localName, localName, "same player both seats");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "same player both seats: still lobby");
        CheckInt(board.JoinWhite(), ChessBoard.InputAccepted, "joining own seat again: accepted");
        CheckSeats(localName, localName, "joining own seat again");
    }

    void TestSeatTaken() {
        board._ClearState();
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
        board._ClearState();
        CheckInt(board.JoinAs(ChessRules.NoColor, "Ana"), ChessBoard.InputRejected, "join no colour: rejected");
        CheckInt(board.JoinAs(2, "Ana"), ChessBoard.InputRejected, "join colour 2: rejected");
        CheckInt(board.JoinAs(ChessRules.White, ""), ChessBoard.InputRejected, "join empty name: rejected");
        CheckInt(board.JoinAs(ChessRules.White, null), ChessBoard.InputRejected, "join null name: rejected");
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, "rejected joins: still reset");
        CheckSeats("", "", "rejected joins");
    }

    void TestJoinByLifecycle() {
        // There's no Start yet (7.1b), so the lifecycle is set directly.
        board._ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        board.lifecycle = ChessBoard.LifecycleInGame;
        CheckInt(board.JoinBlack(), ChessBoard.InputRejected, "in game: join empty seat rejected");
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputRejected, "in game: join own seat rejected");
        CheckSeats("Ana", "", "in game");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "in game: lifecycle unchanged");

        board.lifecycle = ChessBoard.LifecycleEnded;
        CheckInt(board.JoinBlack(), ChessBoard.InputRejected, "ended: join rejected");
        CheckSeats("Ana", "", "ended");

        board._ClearState();
        board.lifecycle = ChessBoard.LifecycleLoaded;
        CheckInt(board.JoinWhite(), ChessBoard.InputAccepted, "loaded: join white accepted");
        CheckInt(board.JoinAs(ChessRules.Black, "Ana"), ChessBoard.InputAccepted, "loaded: join black accepted");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "loaded: stays loaded");
        CheckSeats(localName, "Ana", "loaded");
    }

    // ---- 13.1 leaving a seat before the game starts ----

    void TestLeaveSeat() {
        // The lobby: leaving one seat keeps the other, and the emptied seat can be joined again.
        LobbyWithTwoPlayers();
        Check(board.CanLeaveSeatAs(ChessRules.White, "Ana"), "leave: Ana may leave White in the lobby", "can't");
        Check(!board.CanLeaveSeatAs(ChessRules.Black, "Ana"), "leave: Ana may not leave Bob's seat", "can");
        Check(!board.CanLeaveSeatAs(ChessRules.White, "Ana "), "leave: names compared exactly", "a lookalike can");
        CheckInt(board.LeaveSeatAs(ChessRules.Black, "Ana"), ChessBoard.InputRejected,
            "leave: someone else's seat refused");
        CheckInt(board.LeaveSeatAs(ChessRules.NoColor, "Ana"), ChessBoard.InputRejected, "leave: no colour refused");
        CheckSeats("Ana", "Bob", "leave: refused leaves change nothing");
        CheckInt(board.LeaveSeatAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "leave: Ana leaves White");
        CheckSeats("", "Bob", "leave: White emptied");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "leave: still the lobby with Bob seated");
        CheckInt(board.lastChange, ChessBoard.ChangeLeft, "leave: told as ChangeLeft");
        CheckInt(board.LeaveSeatAs(ChessRules.White, "Ana"), ChessBoard.InputRejected,
            "leave: a seat nobody holds refused");
        CheckInt(board.JoinAs(ChessRules.White, "Cleo"), ChessBoard.InputAccepted,
            "leave: Cleo joins the emptied seat");
        CheckSeats("Cleo", "Bob", "leave: White joined again");

        // The same player in both seats leaves one at a time; the lobby's last seat emptied resets the board.
        board._ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        board.JoinAs(ChessRules.Black, "Ana");
        CheckInt(board.LeaveSeatAs(ChessRules.Black, "Ana"), ChessBoard.InputAccepted, "leave: Ana leaves Black");
        CheckSeats("Ana", "", "leave: Ana keeps White");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "leave: one seat left, still the lobby");
        CheckInt(board.LeaveSeatAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "leave: Ana leaves White too");
        CheckSeats("", "", "leave: lobby emptied");
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, "leave: an emptied lobby is reset");
        CheckInt(board.lastChange, ChessBoard.ChangeLeft, "leave: the emptied lobby told as ChangeLeft");
        string numbers = "";
        for (int i = 0; i < board.gameNumbers.Length; i++) numbers += board.gameNumbers[i] != 0 ? i + " " : "";
        Check(board.gameSettings == 0 && numbers.Length == 0, "leave: the emptied lobby's snapshot dropped",
            "settings " + board.gameSettings + ", numbers set: " + numbers);
        CheckInt(board.LeaveSeatAs(ChessRules.White, "Ana"), ChessBoard.InputRejected,
            "leave: refused on a reset board");
        Check(!board.CanLeaveSeatAs(ChessRules.White, "Ana") && !board.CanLeaveSeatAs(ChessRules.Black, "Ana"),
            "leave: nothing to leave on a reset board", "can");

        // In game and once ended: no leaving, a reset is the way out.
        LobbyWithTwoPlayers();
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, "leave: game started");
        Check(!board.CanLeaveSeatAs(ChessRules.White, "Ana"), "leave: Ana can't leave in game", "can");
        CheckInt(board.LeaveSeatAs(ChessRules.White, "Ana"), ChessBoard.InputRejected, "leave: refused in game");
        CheckInt(board.LeaveSeatAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected, "leave: Bob refused in game");
        CheckSeats("Ana", "Bob", "leave: in game, seats kept");
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "leave: Ana resigned");
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, "leave: the game ended");
        Check(!board.CanLeaveSeatAs(ChessRules.Black, "Bob"), "leave: Bob can't leave once ended", "can");
        CheckInt(board.LeaveSeatAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected, "leave: refused once ended");
        CheckSeats("Ana", "Bob", "leave: ended, seats kept");

        // A loaded board: leaving keeps the game loaded, even with both seats emptied.
        if (!Utilities.IsValid(importer) || siggiVsNuka == null) {
            Check(false, "leave: importer and Siggi-vs-Nuka assigned", "assign them in the Inspector");
            board._ClearState();
            return;
        }
        LoadText(siggiVsNuka.text, "leave: loaded game");
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "leave: Ana joins the loaded game");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "leave: Bob joins the loaded game");
        Check(board.CanLeaveSeatAs(ChessRules.White, "Ana"), "leave: Ana may leave a loaded board", "can't");
        CheckInt(board.LeaveSeatAs(ChessRules.Black, "Ana"), ChessBoard.InputRejected,
            "leave: loaded, someone else's seat refused");
        CheckInt(board.LeaveSeatAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "leave: Ana leaves, loaded");
        CheckSeats("", "Bob", "leave: loaded, White emptied");
        CheckInt(board.JoinAs(ChessRules.White, "Cleo"), ChessBoard.InputAccepted, "leave: loaded, Cleo joins White");
        CheckInt(board.LeaveSeatAs(ChessRules.White, "Cleo"), ChessBoard.InputAccepted, "leave: Cleo leaves");
        CheckInt(board.LeaveSeatAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "leave: Bob leaves");
        CheckSeats("", "", "leave: loaded board emptied");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "leave: an emptied loaded board stays loaded");
        Check(board.replayMode, "leave: emptied loaded board still in replay", "not in replay");
        CheckInt(board.moveCount, 54, "leave: emptied loaded board keeps its game");
        CheckString(board.gameId, "20250710001921", "leave: emptied loaded board keeps its game ID");
        CheckInt(board.lastChange, ChessBoard.ChangeLeft, "leave: loaded, told as ChangeLeft");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted,
            "leave: the emptied loaded board can be joined");
        board._ClearState();
    }

    // ---- 7.1b ----

    // A board in the lobby with Ana as White and Bob as Black (no snapshot until 7.2, so nothing is forced).
    void LobbyWithTwoPlayers() {
        board._ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        board.JoinAs(ChessRules.Black, "Bob");
    }

    void TestSetMode() {
        board._ClearState();
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
        board._ClearState();
        board.JoinWhite();
        CheckInt(board.SetMode(ChessGame.ModeChess960), ChessBoard.InputAccepted, "mode: seated local player sets it");
        CheckInt(board.mode, ChessGame.ModeChess960, "mode: set by the local player");

        // The mode of a loaded game is the game's own; once started or ended it's fixed.
        board._ClearState();
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
        board._ClearState();
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
        board._ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        CheckInt(board.SetClock(180, 2), ChessBoard.InputRejected, "clock: unseated local player rejected");
        board.JoinBlack();
        CheckInt(board.SetClock(180, 2), ChessBoard.InputAccepted, "clock: seated local player sets it");
        CheckInt(board.baseTime * 1000 + board.increment, 180002, "clock: 3+2 set by the local player");

        // A loaded board chooses the clock for continuing; once started or ended it's fixed.
        board._ClearState();
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
        CheckString(board.startFen, ChessFen.StartFen, "forced Standard: standard starting position");

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
        board._ClearState();
        board.lifecycle = ChessBoard.LifecycleLoaded;
        board.JoinAs(ChessRules.White, "Ana");
        board.gameSettings = ChessArbiter.SettingClockForced;
        CheckInt(board.SetClockAs("Ana", 300, 0), ChessBoard.InputRejected, "forced clock: loaded board rejects");
    }

    void TestStart() {
        board._ClearState();
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputRejected, "start: reset board rejects");
        board.JoinAs(ChessRules.White, "Ana");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputRejected, "start: only white seated rejected");
        board._ClearState();
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
        CheckString(ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber),
            ChessFen.StartFen, "start: standard starting position");
        CheckString(board.startFen, ChessFen.StartFen, "start: starting FEN");
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
        board._ClearState();
        board.lifecycle = ChessBoard.LifecycleLoaded;
        board.JoinAs(ChessRules.White, "Ana");
        board.JoinAs(ChessRules.Black, "Bob");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputRejected, "start: loaded board rejects");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "start: loaded board stays loaded");

        // The local player's button, seated as both colours, with no clock.
        board._ClearState();
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
        CheckString(ChessGame.Chess960Fen(ChessGame.Chess960StandardIndex), ChessFen.StartFen,
            "960: position 518 is the standard position");
        CheckString(ChessGame.Chess960Fen(0), "bbqnnrkr/pppppppp/8/8/8/8/PPPPPPPP/BBQNNRKR w HFhf - 0 1",
            "960: position 0");
        CheckString(ChessGame.Chess960Fen(959), "rkrnnqbb/pppppppp/8/8/8/8/PPPPPPPP/RKRNNQBB w CAca - 0 1",
            "960: position 959");
        CheckString(ChessGame.Chess960Fen(-1), "", "960: index -1 gives \"\"");
        CheckString(ChessGame.Chess960Fen(ChessGame.Chess960Count), "", "960: index 960 gives \"\"");
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
            int index = board._RandomChess960Index();
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
        CheckString(ChessArbiter._SettingsToString(board.gameSettings), ChessArbiter._SettingsToString(settings),
            name + ": setting bits");
        int[] expected = new int[ChessArbiter.NumberCount];
        expected[ChessArbiter.NumberResetAvailableTime] = resetAvailable;
        expected[ChessArbiter.NumberAutoResetTime] = autoReset;
        expected[ChessArbiter.NumberForcedMode] = forcedMode;
        expected[ChessArbiter.NumberForcedBaseTime] = forcedBase;
        expected[ChessArbiter.NumberForcedIncrement] = forcedInc;
        CheckString(ChessArbiter._NumbersToString(board.gameNumbers), ChessArbiter._NumbersToString(expected),
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
        board._ClearState();
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
        board._ClearState();
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
        board._ClearState();
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

        // On a loaded board (loading, step 7.9a, calls _TakeSnapshot) the forced clock is applied but not the forced
        // mode: the mode is the loaded game's. Joining it doesn't take another.
        board._ClearState();
        board.lifecycle = ChessBoard.LifecycleLoaded;
        board.mode = ChessGame.ModeChess960;
        int forcedBoth = ChessArbiter.SettingModeForced | ChessArbiter.SettingClockForced;
        OverrideAll(forcedBoth, 30, 300, ChessGame.ModeStandard, 120, 1);
        board._TakeSnapshot();
        CheckSnapshot(forcedBoth, 30, 300, ChessGame.ModeStandard, 120, 1, "snapshot: loaded board");
        CheckInt(board.mode, ChessGame.ModeChess960, "snapshot: loaded board keeps the game's mode");
        CheckInt(board.baseTime * 1000 + board.increment, 120001, "snapshot: loaded board shows the forced clock");
        OverrideAll(s1, 30, 300, ChessGame.ModeChess960, 180, 2);
        board.JoinAs(ChessRules.White, "Ana");
        CheckSnapshot(forcedBoth, 30, 300, ChessGame.ModeStandard, 120, 1, "snapshot: joining a loaded board keeps it");

        // A conflicting override is corrected in the snapshot, as in EffectiveNumbers: auto-reset 60 clamps the
        // reset-available time 120 to 60.
        OverrideAll(0, 120, 60, ChessGame.ModeStandard, ChessArbiter.NoClock, 0);
        board._ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        CheckSnapshot(0, 60, 60, ChessGame.ModeStandard, ChessArbiter.NoClock, 0, "snapshot: numbers corrected");

        // With no overrides the snapshot is the arbiter's defaults, and changing the defaults afterwards doesn't
        // affect it either (they're swapped for a moment and restored).
        arbiter.ClearOverrides(n);
        int defaults = arbiter.defaultSettings;
        int[] defaultNumbers = arbiter.defaultNumbers;
        int[] effective = new int[ChessArbiter.NumberCount];
        arbiter.EffectiveNumbers(n, effective);
        board._ClearState();
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
        board._ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        CheckSnapshot(defaults, effective[0], effective[1], effective[2], effective[3], effective[4],
            "snapshot: board number out of range gets the defaults");
        board.boardNumber = n;

        // With no arbiter, everything off, and no re-snapshot.
        board.arbiter = null;
        board._ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        CheckSnapshot(0, ChessArbiter.Never, ChessArbiter.Never, ChessGame.ModeStandard, ChessArbiter.NoClock, 0,
            "snapshot: no arbiter, everything off");
        CheckInt(board.Resnapshot(), ChessBoard.InputRejected, "re-snapshot: no arbiter rejected");
        board.arbiter = arbiter;

        arbiter.ClearOverrides(n);
        board._ClearState();
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
        CheckInt(board._RemainingTimeAt(ChessRules.White, now), white, name + ": white's remaining time");
        CheckInt(board._RemainingTimeAt(ChessRules.Black, now), black, name + ": black's remaining time");
    }

    // Plays from-to at server time now, checking it's legal and accepted.
    void Play(int from, int to, double now, string name) {
        int move = FindMove(from, to);
        Check(move != 0, name + ": move found",
            ChessRules.SquareName(from) + ChessRules.SquareName(to) + " isn't legal");
        CheckInt(board._PlayMoveAt(move, now), ChessBoard.InputAccepted, name + ": accepted");
    }

    // The times are binary fractions of a second, so the milliseconds come out exact.
    void TestClocks() {
        NothingForced();

        StartedGame(60, 2, 1000);
        Check(board.HasClock() && board.IsClockRunning(), "clock: running after start", "not running");
        CheckTimes(60000, 60000, 1000, "clock: at the turn start");
        CheckTimes(57500, 60000, 1002.5, "clock: white's runs, black's doesn't");
        CheckTimes(60000, 60000, 999, "clock: a time before the turn start counts as none passed");
        CheckInt(board._FlaggedColorAt(1059.5), ChessRules.NoColor, "clock: 0.5 s left is no flag fall");

        Play(ChessRules.E2, ChessRules.E4, 1002.5, "clock: 1. e4 after 2.5 s");
        CheckInt(board.whiteTime, 59500, "clock: 1. e4 takes 2.5 s and adds 2 s");
        CheckInt(board.clockHistory[0], 59500, "clock: history entry for 1. e4");
        Check(board.turnStart == 1002.5, "clock: turn start reset to the move's time", "turn start " + board.turnStart);
        CheckInt(board.moveCount, 1, "clock: one move");
        CheckString(board.notation[0], "e4", "clock: SAN recorded");
        CheckInt(board.state[ChessPosition.StateSideToMove], ChessRules.Black, "clock: black to move");
        CheckTimes(59500, 52500, 1010, "clock: black's runs, white's stopped");

        Play(ChessRules.E7, ChessRules.E5, 1010.25, "clock: 1... e5 after 7.75 s");
        CheckInt(board.blackTime, 54250, "clock: 1... e5 takes 7.75 s and adds 2 s");
        CheckInt(board.moveNumber, 2, "clock: move number after black's move");
        Play(ChessRules.G1, ChessRules.F3, 1010.25, "clock: 2. Nf3 at once");
        CheckInt(board.whiteTime, 61500, "clock: the increment can take the time over the base");
        CheckInt(board.halfmoveClock, 1, "clock: halfmove clock after a knight move");
        CheckInt(board.halfmoveClocks[2], 0, "clock: halfmove clock before 2. Nf3");
        CheckString(board.notation[2], "Nf3", "clock: SAN of 2. Nf3");

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
        CheckInt(board._PlayMoveAt(nc6, 1011.25), ChessBoard.InputRejected, "clock: no move in replay");
        board.replayMode = false;

        // Black has 54.25 s from 1010.25, so the flag falls at 1064.5.
        CheckInt(board._FlaggedColorAt(1064), ChessRules.NoColor, "clock: 0.5 s left, no flag fall");
        CheckInt(board._FlaggedColorAt(1064.5), ChessRules.Black, "clock: black's flag falls at 0");
        CheckInt(board._FlaggedColorAt(5000), ChessRules.Black, "clock: and stays fallen");
        CheckTimes(61500, 0, 5000, "clock: out of time shows 0, not less");
        CheckInt(board._PlayMoveAt(nc6, 1064.5), ChessBoard.InputRejected, "clock: no move once the flag has fallen");
        CheckInt(board.moveCount, 3, "clock: refused move not recorded");
        CheckInt(board.blackTime, 54250, "clock: refused move leaves the time");
        Check(board.turnStart == 1010.25, "clock: refused move leaves the turn start", "turn start " + board.turnStart);
        Play(ChessRules.B8, ChessRules.C6, 1064.25, "clock: 2... Nc6 with 0.25 s left");
        CheckInt(board.blackTime, 2250, "clock: 0.25 s left plus 2 s");
        CheckInt(board._FlaggedColorAt(1064.5), ChessRules.NoColor, "clock: no flag fall after moving in time");

        // Moves that aren't legal.
        CheckInt(board._PlayMoveAt(FindMove(ChessRules.E4, ChessRules.E5), 1065), ChessBoard.InputRejected,
            "clock: no move (0) refused");
        int illegal = ChessRules.PackMove(ChessRules.D2, ChessRules.D5, ChessRules.Pawn, 0, 0, false, 15, -1);
        CheckInt(board._PlayMoveAt(illegal, 1065), ChessBoard.InputRejected, "clock: an illegal move refused");
        CheckInt(board.moveCount, 4, "clock: refused moves not recorded");

        // The clocks stop when the game ends (set directly here; TestEndings ends games).
        board.lifecycle = ChessBoard.LifecycleEnded;
        Check(!board.IsClockRunning(), "clock: stopped once ended", "running");
        CheckTimes(61500, 2250, 1e6, "clock: ended, times as stored");
        CheckInt(board._FlaggedColorAt(1e6), ChessRules.NoColor, "clock: no flag fall once ended");
        CheckInt(board._PlayMoveAt(FindMove(ChessRules.D2, ChessRules.D4), 1065), ChessBoard.InputRejected,
            "clock: no move once ended");

        // Not running before a game: in the lobby and on a loaded board.
        LobbyWithTwoPlayers();
        board.SetClockAs("Ana", 60, 0);
        Check(!board.IsClockRunning(), "clock: not running in the lobby", "running");
        CheckInt(board._PlayMoveAt(FindMove(ChessRules.E2, ChessRules.E4), 1000), ChessBoard.InputRejected,
            "clock: no move in the lobby");
        board.lifecycle = ChessBoard.LifecycleLoaded;
        Check(!board.IsClockRunning(), "clock: not running on a loaded board", "running");

        // A short time control, 1+0: a move with 1 ms left, then black's flag falls after exactly 1 s.
        StartedGame(1, 0, 20);
        Play(ChessRules.E2, ChessRules.E4, 20.9990234375, "clock: 1+0, 1. e4");
        CheckInt(board.whiteTime, 1, "clock: 1+0, 1 ms left after 999 ms");
        CheckInt(board._FlaggedColorAt(21.998046875), ChessRules.NoColor, "clock: 1+0, black with 1 ms left");
        CheckInt(board._FlaggedColorAt(21.9990234375), ChessRules.Black, "clock: 1+0, black's flag falls after 1 s");

        // The real server time: Start sets the turn start, so the clocks start full and run down.
        LobbyWithTwoPlayers();
        board.SetClockAs("Ana", 600, 0);
        board.StartGameAs("Bob");
        int left = board.RemainingTime(ChessRules.White);
        Check(left > 590000 && left <= 600000, "clock: server time, white's clock starts full", "left " + left);
        CheckInt(board.FlaggedColor(), ChessRules.NoColor, "clock: server time, no flag at the start");
        CheckInt(board._PlayMove(FindMove(ChessRules.D2, ChessRules.D4)), ChessBoard.InputAccepted,
            "clock: server time, 1. d4");
        board.turnStart = board._ServerTime() - 601;
        CheckInt(board.RemainingTime(ChessRules.Black), 0, "clock: server time, black out of time after 601 s");
        CheckInt(board.FlaggedColor(), ChessRules.Black, "clock: server time, black's flag falls");
        CheckInt(board._PlayMove(FindMove(ChessRules.D7, ChessRules.D5)), ChessBoard.InputRejected,
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
        CheckInt(board._FlaggedColorAt(4e6), ChessRules.NoColor, "no clock: no flag fall");
        Play(ChessRules.D8, ChessRules.H4, 4e6, "no clock: 2... Qh4#");
        CheckString(board.notation[3], "Qh4#", "no clock: SAN with the mate suffix");
        bool noClocks = true;
        for (int i = 0; i < 4; i++) {
            if (board.clockHistory[i] != -1) noClocks = false;
        }
        Check(noClocks, "no clock: history entries -1", "an entry isn't -1");
        CheckString(ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber),
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
            if (board._PlayMoveAt(FindMove(route[step], route[step + 1]), i + 1) != ChessBoard.InputAccepted) {
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

        board._ClearState();
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
        CheckString(Fen(), start, "drop: rejected drops leave the position");

        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4), ChessBoard.InputAccepted, "drop: 1. e4");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.D2, ChessRules.D4), ChessBoard.InputRejected,
            "drop: white again out of turn");
        CheckInt(board.PiecePickedUpAs("Bob", ChessRules.E7), ChessBoard.InputAccepted, "pickup: now black's turn");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E7, ChessRules.E5), ChessBoard.InputAccepted, "drop: 1... e5");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.G1, ChessRules.F3), ChessBoard.InputAccepted, "drop: 2. Nf3");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.B8, ChessRules.C6), ChessBoard.InputAccepted, "drop: 2... Nc6");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.F3, ChessRules.E5), ChessBoard.InputAccepted, "drop: 3. Nxe5");
        CheckInt(board.moveCount, 5, "drop: five moves recorded");
        CheckString(board.notation[4], "Nxe5", "drop: SAN of the capture");
        CheckString(Fen(), "r1bqkbnr/pppp1ppp/2n5/4N3/4P3/8/PPPP1PPP/RNBQKB1R b KQkq - 0 3", "drop: position");

        // In replay a drop is a free move on the replay board (7.8a), never a game move; out of game nothing.
        string beforeReplay = Fen();
        CheckInt(board.EnterReplayAs("Bob", false), ChessBoard.InputAccepted, "drop: replay entered");
        CheckInt(board.PiecePickedUpAs("Bob", ChessRules.C6), ChessBoard.InputAccepted, "pickup: in replay, free");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.C6, ChessRules.E5), ChessBoard.InputAccepted,
            "drop: in replay, a free move");
        CheckInt(board.moveCount, 5, "drop: no game move in replay");
        CheckString(Fen(), beforeReplay, "drop: the game position untouched in replay");
        CheckInt(board.LeaveReplayAs("Bob", false), ChessBoard.InputAccepted, "drop: replay left");
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
        board.turnStart = board._ServerTime() - 61;
        CheckInt(board.PiecePickedUpAs("Ana", ChessRules.E2), ChessBoard.InputRejected, "pickup: not after the flag");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4), ChessBoard.InputRejected,
            "drop: not after the flag");
        board.turnStart = board._ServerTime();
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4), ChessBoard.InputAccepted,
            "drop: with time left");

        // The local player in both seats plays both sides.
        board._ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        CheckInt(board.PiecePickedUp(ChessRules.D2), ChessBoard.InputAccepted, "local: pickup as white");
        CheckInt(board.PieceDropped(ChessRules.D2, ChessRules.D4), ChessBoard.InputAccepted, "local: 1. d4");
        CheckInt(board.PiecePickedUp(ChessRules.D7), ChessBoard.InputAccepted, "local: pickup as black");
        CheckInt(board.PieceDropped(ChessRules.D7, ChessRules.D5), ChessBoard.InputAccepted, "local: 1... d5");
        CheckInt(board.PieceDropped(ChessRules.D4, ChessRules.D5), ChessBoard.InputRejected, "local: 2. d4-d5 blocked");
        CheckInt(board.moveCount, 2, "local: two moves");

        board._ClearState();
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
            CheckString(Fen(), PromotionFen, "promotion " + letter + ": position unchanged");
            CheckInt(board.PromotionChosenAs("Ana", ChessRules.B7, ChessRules.B8, type), ChessBoard.InputAccepted,
                "promotion " + letter + ": chosen");
            CheckInt(board.board[ChessRules.B8], ChessRules.PieceOf(ChessRules.White, type),
                "promotion " + letter + ": piece on b8");
            CheckInt(board.board[ChessRules.B7], ChessRules.Empty, "promotion " + letter + ": b7 empty");
            CheckInt(board.moveCount, 1, "promotion " + letter + ": one move");
            CheckInt(ChessRules.MovePromotion(board.moveHistory[0]), type, "promotion " + letter + ": move's type");
            CheckString(board.notation[0], "b8=" + letter, "promotion " + letter + ": SAN");
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
        CheckString(Fen(), PromotionFen, "promotion: refused choices leave the position");

        CheckInt(board.PromotionChosenAs("Ana", ChessRules.B7, ChessRules.A8, ChessRules.Knight),
            ChessBoard.InputAccepted, "promotion: bxa8=N");
        CheckInt(board.board[ChessRules.A8], ChessRules.WhiteKnight, "promotion: knight on a8");
        CheckString(board.notation[0], "bxa8=N", "promotion: SAN of the capture");

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
        board._ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        SetPosition(PromotionFen);
        CheckInt(board.PieceDropped(ChessRules.B7, ChessRules.B8), ChessBoard.InputPromotionNeeded,
            "local: promotion needs a choice");
        CheckInt(board.PromotionChosen(ChessRules.B7, ChessRules.B8, ChessRules.Rook), ChessBoard.InputAccepted,
            "local: b8=R");
        CheckInt(board.board[ChessRules.B8], ChessRules.WhiteRook, "local: rook on b8");

        board._ClearState();
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
        if (board.moveCount > 0) CheckString(board.notation[board.moveCount - 1], san, name + ": SAN");
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
        CheckString(Fen(), "2kr3r/8/8/8/8/8/8/R4RK1 w - - 2 2", "castling: position after both");

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

        // Refused castles: no rights, through check, by the wrong player or out of turn, a rook dropped on the king and
        // a promotion choice on a castle.
        CastlingGame("r3k2r/8/8/8/8/8/8/R3K2R w - - 0 1");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.G1), ChessBoard.InputRejected,
            "castling: no rights, e1 on g1");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.H1), ChessBoard.InputRejected,
            "castling: no rights, e1 on h1");
        CastlingGame("r3k2r/8/8/8/8/8/5r2/R3K2R w KQkq - 0 1");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.G1), ChessBoard.InputRejected,
            "castling: through check, e1 on g1");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.H1), ChessBoard.InputRejected,
            "castling: through check, e1 on h1");
        CheckInt(board.moveCount, 0, "castling: no castle without rights or through check");
        CastlingGame(CastlingFen);
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E1, ChessRules.G1), ChessBoard.InputRejected,
            "castling: by the other player");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E8, ChessRules.G8), ChessBoard.InputRejected,
            "castling: out of turn");
        CheckInt(board.PieceDroppedAs("Cleo", ChessRules.E1, ChessRules.H1), ChessBoard.InputRejected,
            "castling: by a non-player");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.H1, ChessRules.E1), ChessBoard.InputRejected,
            "castling: rook dropped on the king");
        CheckInt(board.PromotionChosenAs("Ana", ChessRules.E1, ChessRules.H1, ChessRules.Queen),
            ChessBoard.InputRejected, "castling: a promotion choice on the rook");
        CheckInt(board.moveCount, 0, "castling: refused castles play nothing");
        CheckString(Fen(), CastlingFen, "castling: refused castles leave the position");

        // Not in replay or once ended.
        CastlingGame(CastlingFen);
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, "castling: replay entered");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.G1), ChessBoard.InputAccepted,
            "castling: in replay only a free move");
        Check(board.replayBoard[ChessRules.G1] == ChessRules.WhiteKing
            && board.replayBoard[ChessRules.H1] == ChessRules.WhiteRook, "castling: the free move doesn't castle",
            "the rook moved or the king didn't");
        board.LeaveReplayAs("Ana", false);
        board.lifecycle = ChessBoard.LifecycleEnded;
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.H1), ChessBoard.InputRejected,
            "castling: e1 on h1 not once ended");
        CheckInt(board.moveCount, 0, "castling: nothing played in replay or once ended");

        // Chess960, king f1 and rook h1: castling and Kg1 both put the king on g1, so the destination drop is Kg1 and
        // the castle is a drop on the rook. The queen side castle moves the king three squares, so its destination
        // castles too. The f pawns keep a castled rook from giving check.
        string oneSquare = "1r3k1r/5p2/8/8/8/8/5P2/1R3K1R w KQkq - 0 1";
        CastlingGame(oneSquare);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.F1, ChessRules.G1), ChessBoard.InputAccepted,
            "chess960 f1: f1 on g1 accepted");
        Check(!ChessRules.MoveIsCastle(board.moveHistory[0]), "chess960 f1: f1 on g1 isn't a castle", "it castled");
        CheckString(board.notation[0], "Kg1", "chess960 f1: f1 on g1 SAN");
        CheckInt(board.board[ChessRules.G1], ChessRules.WhiteKing, "chess960 f1: king on g1");
        CheckInt(board.board[ChessRules.H1], ChessRules.WhiteRook, "chess960 f1: rook still on h1");
        // Black has the same position.
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.F8, ChessRules.H8), ChessBoard.InputAccepted,
            "chess960 f8: f8 on h8 accepted");
        CheckCastled("O-O", ChessRules.BlackKing, ChessRules.G8, ChessRules.BlackRook, ChessRules.F8,
            "chess960 f8: f8 on h8");
        CheckInt(board.board[ChessRules.H8], ChessRules.Empty, "chess960 f8: h8 empty");

        CastlingGame(oneSquare);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.F1, ChessRules.H1), ChessBoard.InputAccepted,
            "chess960 f1: f1 on h1 accepted");
        CheckCastled("O-O", ChessRules.WhiteKing, ChessRules.G1, ChessRules.WhiteRook, ChessRules.F1,
            "chess960 f1: f1 on h1");

        CastlingGame(oneSquare);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.F1, ChessRules.C1), ChessBoard.InputAccepted,
            "chess960 f1: f1 on c1 accepted");
        CheckCastled("O-O-O", ChessRules.WhiteKing, ChessRules.C1, ChessRules.WhiteRook, ChessRules.D1,
            "chess960 f1: f1 on c1");
        CheckInt(board.board[ChessRules.B1], ChessRules.Empty, "chess960 f1: f1 on c1: b1 empty");

        // King d1 and rook a1: the queen side castle moves the king one square to c1.
        string queenOneSquare = "r2k3r/3p4/8/8/8/8/3P4/R2K3R w KQkq - 0 1";
        CastlingGame(queenOneSquare);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.D1, ChessRules.C1), ChessBoard.InputAccepted,
            "chess960 d1: d1 on c1 accepted");
        CheckString(board.notation[0], "Kc1", "chess960 d1: d1 on c1 is Kc1");
        CastlingGame(queenOneSquare);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.D1, ChessRules.A1), ChessBoard.InputAccepted,
            "chess960 d1: d1 on a1 accepted");
        CheckCastled("O-O-O", ChessRules.WhiteKing, ChessRules.C1, ChessRules.WhiteRook, ChessRules.D1,
            "chess960 d1: d1 on a1");
        CheckInt(board.board[ChessRules.A1], ChessRules.Empty, "chess960 d1: a1 empty");

        // King already on g1: castling doesn't move it, so dropping it on its own square is refused and dropping it
        // on the rook castles.
        string kingOnG = "6kr/8/8/8/8/8/8/6KR w Kk - 0 1";
        CastlingGame(kingOnG);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.G1, ChessRules.G1), ChessBoard.InputRejected,
            "chess960 g1: g1 on g1 refused");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.G1, ChessRules.H1), ChessBoard.InputAccepted,
            "chess960 g1: g1 on h1 accepted");
        CheckCastled("O-O", ChessRules.WhiteKing, ChessRules.G1, ChessRules.WhiteRook, ChessRules.F1,
            "chess960 g1: g1 on h1");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.G8, ChessRules.H8), ChessBoard.InputAccepted,
            "chess960 g8: g8 on h8 accepted");
        CheckCastled("O-O", ChessRules.BlackKing, ChessRules.G8, ChessRules.BlackRook, ChessRules.F8,
            "chess960 g8: g8 on h8");

        // King already on c1, the queen side the same way.
        string kingOnC = "1rk5/8/8/8/8/8/8/1RK5 w Qq - 0 1";
        CastlingGame(kingOnC);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.C1, ChessRules.B1), ChessBoard.InputAccepted,
            "chess960 c1: c1 on b1 accepted");
        CheckCastled("O-O-O", ChessRules.WhiteKing, ChessRules.C1, ChessRules.WhiteRook, ChessRules.D1,
            "chess960 c1: c1 on b1");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.C8, ChessRules.B8), ChessBoard.InputAccepted,
            "chess960 c8: c8 on b8 accepted");
        CheckCastled("O-O-O", ChessRules.BlackKing, ChessRules.C8, ChessRules.BlackRook, ChessRules.D8,
            "chess960 c8: c8 on b8");

        // King f1 and rook g1: castling swaps them and the destination is the rook's square, so dropping on g1
        // castles (no normal Kg1 is possible). The g pawns keep the black rook off g1.
        CastlingGame("r4kr1/5pp1/8/8/8/8/5PP1/R4KR1 w KQkq - 0 1");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.F1, ChessRules.G1), ChessBoard.InputAccepted,
            "chess960 swap: f1 on g1 accepted");
        CheckCastled("O-O", ChessRules.WhiteKing, ChessRules.G1, ChessRules.WhiteRook, ChessRules.F1,
            "chess960 swap: f1 on g1");

        // The local player's methods.
        board._ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        SetPosition(oneSquare);
        CheckInt(board.PieceDropped(ChessRules.F1, ChessRules.H1), ChessBoard.InputAccepted, "local: f1 on h1");
        CheckCastled("O-O", ChessRules.WhiteKing, ChessRules.G1, ChessRules.WhiteRook, ChessRules.F1,
            "local: f1 on h1");
        CheckInt(board.PieceDropped(ChessRules.F8, ChessRules.H8), ChessBoard.InputAccepted, "local: f8 on h8");
        CheckCastled("O-O", ChessRules.BlackKing, ChessRules.G8, ChessRules.BlackRook, ChessRules.F8,
            "local: f8 on h8");

        board._ClearState();
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
        board.turnStart = board._ServerTime() - 61;
    }

    void TestLobbyButtons() {
        NothingForced();
        board._ClearState();
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
        board._ClearState();
        board.lifecycle = ChessBoard.LifecycleLoaded;
        board.replayMode = true;
        Check(board.CanJoinAs(ChessRules.White, "Ana"), "join button: on a loaded board", "refused");
        board.JoinAs(ChessRules.White, "Ana");
        board.JoinAs(ChessRules.Black, "Bob");
        Check(board.CanSetClockAs("Ana"), "clock button: on a loaded board", "refused");
        Check(!board.CanSetModeAs("Ana"), "mode button: not on a loaded board", "allowed");
        Check(!board.CanStartGameAs("Ana"), "start button: not on a loaded board", "allowed");

        board._ClearState();
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
        board.turnStart = board._ServerTime();
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
        board.turnStart = board._ServerTime();
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
        board.turnStart = board._ServerTime();
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

        board._ClearState();
    }

    void TestReplayButtons() {
        NothingForced();
        LobbyWithTwoPlayers();
        Check(!board.CanEnterReplayAs("Ana", false), "enter replay button: not in the lobby", "allowed");
        Check(!board.CanEnterReplayAs("Cleo", true), "enter replay button: not in the lobby for an organizer",
            "allowed");
        board._ClearState();
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
        board.turnStart = board._ServerTime();

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

        board._ClearState();
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
        board.turnStart = board._ServerTime();
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

        board._ClearState();
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
        board._ClearState();
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

        board._ClearState();
    }

    // The local player's checks, seated in both seats.
    void TestLocalButtons() {
        NothingForced();
        board._ClearState();
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
        Check(!board.CanRequestTakeback(ChessRules.White), "local buttons: no takeback request in both seats",
            "allowed");
        Check(board.CanContinue(), "local buttons: continue from here in both seats", "refused");
        board.takebackColor = ChessRules.White;
        board.takebackPly = 0;
        Check(!board.CanAcceptTakeback(ChessRules.Black), "local buttons: no accepting a takeback in both seats",
            "allowed");
        Check(!board.CanDeclineTakeback(ChessRules.Black), "local buttons: no declining a takeback in both seats",
            "allowed");
        board.takebackColor = ChessRules.NoColor;
        Check(board.CanReset(), "local buttons: reset", "refused");
        board.lifecycle = ChessBoard.LifecycleLoaded;
        Check(board.CanContinue(), "local buttons: continue a loaded game", "refused");

        board._ClearState();
    }

    // ---- 7.5a game endings ----

    void CheckEnded(int ending, int winner, string result, string name) {
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, name + ": ended");
        CheckInt(board.ending, ending, name + ": ending");
        CheckInt(board.winner, winner, name + ": winner");
        CheckString(ChessPgn.ResultText(board.ending, board.winner), result, name + ": result");
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
        CheckInt(board._FlaggedColorAt(1e6), ChessRules.NoColor, "mate: no flag fall later");
        Check(!board._CheckFlagFallAt(1e6), "mate: no time forfeit later", "forfeit");
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
        CheckString(board.notation[6], "Qxf7#", "mate: SAN Qxf7#");
        CheckEnded(ChessGame.EndingCheckmate, ChessRules.White, "1-0", "mate: scholar's mate");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "mate: a pending draw offer lapses");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E8, ChessRules.F7), ChessBoard.InputRejected,
            "mate: no reply");

        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4);
        board.PieceDroppedAs("Bob", ChessRules.F7, ChessRules.F6);
        board.PieceDroppedAs("Ana", ChessRules.D1, ChessRules.H5);
        CheckString(board.notation[2], "Qh5+", "check: SAN Qh5+");
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

        board._ClearState();
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
        StartedGame(60, 0, board._ServerTime() - 2);
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "resign: with a clock");
        Check(board.whiteTime > 57000 && board.whiteTime <= 58000, "resign: white's time stopped after 2 s",
            "time " + board.whiteTime);
        CheckInt(board.blackTime, 60000, "resign: black's time untouched");
        Check(!board.IsClockRunning(), "resign: clock stopped", "running");
        CheckInt(board._RemainingTimeAt(ChessRules.White, board._ServerTime() + 1000), board.whiteTime,
            "resign: white's time stays");

        // The local player in both seats chooses the side.
        board._ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        CheckInt(board.Resign(ChessRules.Black), ChessBoard.InputAccepted, "resign: local player for black");
        CheckEnded(ChessGame.EndingResignation, ChessRules.White, "1-0", "resign: local player");
        CheckInt(board.Resign(ChessRules.White), ChessBoard.InputRejected, "resign: local player, not twice");

        board._ClearState();
    }

    // A game with a 60 s clock from server time 1000 in the position fen, with these settings, ended by
    // _CheckFlagFallAt at 1060, when the side to move's flag falls.
    void CheckForfeit(string fen, int settings, int winner, string result, string name) {
        StartedGame(60, 0, 1000);
        board.gameSettings = settings;
        SetPosition(fen);
        CheckString(Fen(), fen, name + ": position set up");
        Check(board._CheckFlagFallAt(1060), name + ": forfeit", "not ended");
        CheckEnded(ChessGame.EndingTimeForfeit, winner, result, name);
    }

    void TestTimeForfeit() {
        NothingForced();
        int allDraws = ChessArbiter.SettingsDrawRules;
        int dead = ChessArbiter.SettingDeadPosition;

        // White's flag falls at exactly 0.
        StartedGame(60, 0, 1000);
        board.drawOfferColor = ChessRules.Black;
        Check(!board._CheckFlagFallAt(1059.5), "forfeit: not with 0.5 s left", "ended");
        CheckInGame("forfeit: 0.5 s left");
        Check(board._CheckFlagFallAt(1060), "forfeit: white's flag", "not ended");
        CheckEnded(ChessGame.EndingTimeForfeit, ChessRules.Black, "0-1", "forfeit: white out of time");
        CheckInt(board.whiteTime, 0, "forfeit: white's time 0");
        CheckInt(board.blackTime, 60000, "forfeit: black's time untouched");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, "forfeit: the draw offer lapses");
        Check(!board.IsClockRunning(), "forfeit: clock stopped", "running");
        CheckTimes(0, 60000, 1e6, "forfeit: times kept");
        Check(!board._CheckFlagFallAt(1e6), "forfeit: only once", "ended again");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4), ChessBoard.InputRejected,
            "forfeit: no move after it");
        Check(!board.CanResignAs(ChessRules.Black, "Bob"), "forfeit: no resigning after it", "allowed");

        // Black's flag; every draw rule on doesn't help with mating material on the board.
        StartedGame(60, 0, 1000);
        board.gameSettings = allDraws;
        Play(ChessRules.E2, ChessRules.E4, 1000, "forfeit: 1. e4");
        Check(!board._CheckFlagFallAt(1059.5), "forfeit: black not yet", "ended");
        Check(board._CheckFlagFallAt(1060), "forfeit: black's flag", "not ended");
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
        Check(board._CheckFlagFallAt(1060), "forfeit: in replay", "not ended");
        CheckEnded(ChessGame.EndingTimeForfeit, ChessRules.Black, "0-1", "forfeit: in replay");
        Check(board.replayMode, "forfeit: replay kept", "left replay");

        // Never without a clock, before a game, or on a loaded board.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        Check(!board._CheckFlagFallAt(1e9), "forfeit: never without a clock", "ended");
        CheckInGame("forfeit: no clock");
        LobbyWithTwoPlayers();
        board.SetClockAs("Ana", 60, 0);
        Check(!board._CheckFlagFallAt(1e9), "forfeit: not in the lobby", "ended");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "forfeit: lobby stays");
        board.lifecycle = ChessBoard.LifecycleLoaded;
        Check(!board._CheckFlagFallAt(1e9), "forfeit: not on a loaded board", "ended");

        // The real server time.
        StartedGame(60, 0, 0);
        board.turnStart = board._ServerTime();
        Check(!board._CheckFlagFall(), "forfeit: server time, time left", "ended");
        FlagFallen();
        Check(board._CheckFlagFall(), "forfeit: server time, flag fallen", "not ended");
        CheckEnded(ChessGame.EndingTimeForfeit, ChessRules.Black, "0-1", "forfeit: server time");

        board._ClearState();
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
        StartedGame(60, 0, board._ServerTime() - 2);
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
        board._ClearState();
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

        board._ClearState();
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

        board._ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        board.gameSettings = fifty;
        SetPosition("8/8/4k3/8/8/4K3/8/R7 b - - 100 80");
        CheckInt(board.ClaimDraw(), ChessBoard.InputAccepted, "claim: local player");
        CheckEnded(ChessGame.EndingFiftyMoveRule, ChessRules.NoColor, "1/2-1/2", "claim: local player");

        board._ClearState();
    }

    void TestAutomaticDraws() {
        NothingForced();
        int fivefold = ChessArbiter.SettingFivefoldRepetition;
        int seventyFive = ChessArbiter.SettingSeventyFiveMoveRule;
        int dead = ChessArbiter.SettingDeadPosition;
        int allDraws = ChessArbiter.SettingsDrawRules;

        // Fivefold: the starting position for the fifth time after 16 plies ends the game.
        StartedGame(60, 0, board._ServerTime());
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

        board._ClearState();
    }

    // ---- 7.6 Game IDs ----

    // What's wrong with id as a Game ID (yyyyMMddHHmmss-xxxxxx, a real date and time), "" if nothing.
    string GameIdProblem(string id) {
        if (id == null) return "null";
        if (id.Length != 21) return "length " + id.Length + " in \"" + id + "\"";
        for (int i = 0; i < 21; i++) {
            char c = id[i];
            if (i == 14 ? c != '-' : (c < '0' || c > '9')) return "character " + i + " in \"" + id + "\"";
        }
        int month = int.Parse(id.Substring(4, 2));
        int day = int.Parse(id.Substring(6, 2));
        int hour = int.Parse(id.Substring(8, 2));
        int minute = int.Parse(id.Substring(10, 2));
        int second = int.Parse(id.Substring(12, 2));
        if (month < 1 || month > 12 || day < 1 || day > 31 || hour > 23 || minute > 59 || second > 59) {
            return "not a date and time: \"" + id + "\"";
        }
        return "";
    }

    void CheckGameId(string id, string name) {
        string problem = GameIdProblem(id);
        Check(problem == "", name, problem);
    }

    // The formatting itself, on fixed times and suffixes.
    void TestGameIdFormat() {
        CheckString(ChessGame._GameId(new System.DateTime(2026, 10, 5, 14, 30, 27), 48213), "20261005143027-048213",
            "game ID format: the docs' example");
        CheckString(ChessGame._GameId(new System.DateTime(2026, 1, 2, 3, 4, 5), 0), "20260102030405-000000",
            "game ID format: single digits padded, suffix 0");
        CheckString(ChessGame._GameId(new System.DateTime(9999, 12, 31, 23, 59, 59), 999999), "99991231235959-999999",
            "game ID format: largest values");
        CheckString(ChessGame._GameId(new System.DateTime(33, 7, 9, 0, 0, 0), 7), "00330709000000-000007",
            "game ID format: a short year padded");
        CheckString(ChessGame._GameId(new System.DateTime(2026, 10, 5, 14, 30, 27), -1), "",
            "game ID format: negative suffix refused");
        CheckString(ChessGame._GameId(new System.DateTime(2026, 10, 5, 14, 30, 27), 1000000), "",
            "game ID format: seven-digit suffix refused");
        CheckGameId(ChessGame._GameId(new System.DateTime(2026, 10, 5, 14, 30, 27), 48213),
            "game ID format: the checker accepts the docs' example");
        Check(GameIdProblem("20261005143027048213") != "" && GameIdProblem("20261305143027-048213") != ""
            && GameIdProblem("2026100514302-0048213") != "", "game ID format: the checker refuses malformed IDs",
            "a malformed ID was accepted");

        CheckString(ChessGame._ContinuedFromText("20261005143027-048213", 3), "20261005143027-048213/3",
            "continued from: the docs' example");
        CheckString(ChessGame._ContinuedFromText("20261005143027-048213", 0), "20261005143027-048213/0",
            "continued from: ply 0");
        CheckString(ChessGame._ContinuedFromText("20261005143027-048213", 142), "20261005143027-048213/142",
            "continued from: three-digit ply");
        CheckString(ChessGame._ContinuedFromText("h0rrawli", 5), "h0rrawli/5", "continued from: any imported ID kept");
        CheckString(ChessGame._ContinuedFromText("", 3), "", "continued from: none without a Game ID");
        CheckString(ChessGame._ContinuedFromText(null, 3), "", "continued from: none for null");

        // The random suffix: 1000 IDs for one time, all well formed, nearly all distinct (two of 1000 draws below a
        // million coincide with chance about 0.4), spread over the range so short suffixes get padded.
        System.DateTime time = new System.DateTime(2026, 10, 11, 12, 0, 0);
        bool[] seen = new bool[ChessGame.GameIdSuffixCount];
        int distinct = 0;
        int bad = 0;
        int low = 0;
        int high = 0;
        string firstBad = "";
        for (int i = 0; i < 1000; i++) {
            string id = board._NewGameIdAt(time);
            if (GameIdProblem(id) != "" || !id.StartsWith("20261011120000-")) {
                if (bad == 0) firstBad = id;
                bad++;
                continue;
            }
            int suffix = int.Parse(id.Substring(15));
            if (!seen[suffix]) distinct++;
            seen[suffix] = true;
            if (suffix < 100000) low++;
            if (suffix >= 900000) high++;
        }
        CheckInt(bad, 0, "random game IDs: all well formed for the given time (first bad: \"" + firstBad + "\")");
        Check(distinct >= 995, "random game IDs: nearly all distinct", distinct + " distinct of 1000");
        Check(low >= 50 && high >= 50, "random game IDs: suffixes spread over the range",
            low + " below 100000, " + high + " from 900000");
    }

    // The board's IDs: created on Start (not before), from the network's UTC time; continued games.
    void TestGameIds() {
        LobbyWithTwoPlayers();
        CheckString(board.gameId, "", "game ID: none in the lobby");
        System.DateTime before = board._NetworkDateTime();
        board.StartGameAs("Ana");
        System.DateTime after = board._NetworkDateTime();
        string id = board.gameId;
        CheckGameId(id, "game ID: set on Start");
        CheckString(board.continuedFrom, "", "game ID: a started game continues nothing");
        // Fixed-width digits compare as text in time order.
        string earliest = ChessGame._GameId(before, 0).Substring(0, 14);
        string latest = ChessGame._GameId(after, 0).Substring(0, 14);
        Check(id.Length == 21 && string.CompareOrdinal(id.Substring(0, 14), earliest) >= 0
            && string.CompareOrdinal(id.Substring(0, 14), latest) <= 0, "game ID: the network time at Start",
            id + " not from " + earliest + " to " + latest);
        // The network time is UTC: within two minutes of this computer's UTC clock (a local time would be hours off,
        // unless the local time zone is UTC).
        double offset = (before - System.DateTime.UtcNow).TotalSeconds;
        Check(offset > -120 && offset < 120, "game ID: network time is UTC",
            "network time " + before + " is " + offset + " s from UTC now " + System.DateTime.UtcNow);
        Debug.Log("[BoardTest] game ID at Start: " + id + " (network time " + before + ", UTC now "
            + System.DateTime.UtcNow + ", local now " + System.DateTime.Now + ")");
        board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4);
        CheckString(board.gameId, id, "game ID: unchanged by a move");
        board.ResignAs(ChessRules.Black, "Bob");
        CheckString(board.gameId, id, "game ID: kept when the game ends");

        // Each Start gets its own ID; Chess960 too.
        string[] ids = new string[20];
        int distinct = 0;
        for (int i = 0; i < 20; i++) {
            LobbyWithTwoPlayers();
            if (i % 2 == 1) board.SetModeAs("Ana", ChessGame.ModeChess960);
            board.StartGameAs("Bob");
            ids[i] = board.gameId;
            bool repeated = false;
            for (int j = 0; j < i; j++) {
                if (ids[j] == ids[i]) repeated = true;
            }
            if (!repeated && GameIdProblem(ids[i]) == "") distinct++;
        }
        CheckInt(distinct, 20, "game ID: 20 starts (half Chess960) give 20 distinct well-formed IDs");

        // Continuing: ContinuedFrom is the previous ID and the ply, the new ID from the given time.
        ButtonGame(0);
        string first = board.gameId;
        System.DateTime time = new System.DateTime(2026, 10, 11, 12, 0, 0);
        board._ContinueGameIdAt(3, time);
        CheckString(board.continuedFrom, first + "/3", "continued: ContinuedFrom is the old ID and ply 3");
        Check(board.gameId.StartsWith("20261011120000-") && GameIdProblem(board.gameId) == "",
            "continued: new ID from the given time", "got \"" + board.gameId + "\"");
        Check(board.gameId != first, "continued: the ID changes", "still " + first);
        string second = board.gameId;
        board._ContinueGameIdAt(0, time.AddSeconds(5));
        CheckString(board.continuedFrom, second + "/0", "continued again: references the game it came from, ply 0");
        Check(board.gameId.StartsWith("20261011120005-"), "continued again: new ID from the given time",
            "got \"" + board.gameId + "\"");

        // A game with no ID (a PGN import without a GameID tag) gets a new one but no ContinuedFrom.
        board.gameId = "";
        board._ContinueGameIdAt(2, time);
        CheckString(board.continuedFrom, "", "continued without an ID: no ContinuedFrom");
        CheckGameId(board.gameId, "continued without an ID: gets a new ID");

        // An imported ID that isn't one of ours is referenced as it is.
        board.gameId = "h0rrawli";
        board._ContinueGameIdAt(7, time);
        CheckString(board.continuedFrom, "h0rrawli/7", "continued from a foreign ID: kept as it is");

        // A new Start clears what an earlier continued game left.
        board._ClearState();
        CheckString(board.continuedFrom, "", "reset clears ContinuedFrom");
        LobbyWithTwoPlayers();
        board.continuedFrom = "20261011120000-000001/4";
        board.StartGameAs("Ana");
        CheckString(board.continuedFrom, "", "Start clears ContinuedFrom");

        board._ClearState();
    }

    // ---- 7.8a replay ----

    // A position's 64 squares as text, for comparing.
    string BoardText(byte[] squares) {
        string text = "";
        for (int i = 0; i < 64; i++) text += (char)('A' + squares[i]);
        return text;
    }

    // Everything replay must leave alone, as text.
    string GameText() {
        string text = Fen() + "|" + board.moveCount + "|" + board.lifecycle + "|" + board.whiteTime + "|"
            + board.blackTime + "|" + board.turnStart + "|" + board.halfmoveClock + "|" + board.ending + "|"
            + board.winner + "|" + board.drawOfferColor + "|" + board.gameId + "|" + BoardText(board.board);
        for (int i = 0; i < board.moveHistory.Length; i++) {
            text += "|" + board.moveHistory[i] + "," + board.notation[i] + "," + board.clockHistory[i] + ","
                + board.halfmoveClocks[i];
        }
        return text;
    }

    void CheckShows(int ply, string expected, string name) {
        CheckInt(board.replayIndex, ply, name + ": replay index");
        CheckString(BoardText(board.replayBoard), expected, name + ": replay board");
        Check(!board.replayCustom, name + ": not custom", "custom");
    }

    void TestReplay() {
        NothingForced();

        // A game with en passant, captures and castling, each position recorded.
        StartedGame(60, 0, board._ServerTime());
        int[] from = { ChessRules.E2, ChessRules.G8, ChessRules.E4, ChessRules.D7, ChessRules.E5, ChessRules.C7,
            ChessRules.G1, ChessRules.B8, ChessRules.F1, ChessRules.G7, ChessRules.E1, ChessRules.F8 };
        int[] to = { ChessRules.E4, ChessRules.F6, ChessRules.E5, ChessRules.D5, ChessRules.D6, ChessRules.D6,
            ChessRules.F3, ChessRules.C6, ChessRules.E2, ChessRules.G6, ChessRules.G1, ChessRules.G7 };
        int plies = from.Length;
        string[] positions = new string[plies + 1];
        positions[0] = BoardText(board.board);
        for (int i = 0; i < plies; i++) {
            Drop(i % 2 == 0 ? "Ana" : "Bob", from[i], to[i], "replay game ply " + i);
            positions[i + 1] = BoardText(board.board);
        }
        CheckString(board.notation[4], "exd6", "replay game: en passant played");
        CheckString(board.notation[10], "O-O", "replay game: castling played");
        string game = GameText();

        // Entering.
        CheckInt(board.EnterReplayAs("Cleo", false), ChessBoard.InputRejected, "enter replay: not a bystander");
        Check(!board.replayMode, "enter replay: refused leaves it off", "on");
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, "enter replay: white");
        Check(board.replayMode, "enter replay: on", "off");
        CheckShows(plies, positions[plies], "enter replay: the current position");
        CheckInt(board.EnterReplayAs("Bob", false), ChessBoard.InputRejected, "enter replay: not twice");
        Check(board.IsClockRunning(), "replay: the clock keeps running", "stopped");

        // Stepping reaches every position, back to the start, forward again, and out of order.
        int wrong = 0;
        for (int ply = plies - 1; ply >= 0; ply--) {
            if (board.StepToAs("Bob", false, ply) != ChessBoard.InputAccepted || board.replayIndex != ply
                || BoardText(board.replayBoard) != positions[ply] || board.replayCustom) {
                wrong++;
            }
        }
        CheckInt(wrong, 0, "step: every position back to the start");
        for (int ply = 1; ply <= plies; ply++) {
            if (board.StepToAs("Ana", false, ply) != ChessBoard.InputAccepted
                || BoardText(board.replayBoard) != positions[ply]) {
                wrong++;
            }
        }
        CheckInt(wrong, 0, "step: every position forward to the end");
        int[] order = { 0, 12, 5, 3, 11, 4, 7 };
        for (int i = 0; i < order.Length; i++) {
            CheckInt(board.StepToAs("Ana", false, order[i]), ChessBoard.InputAccepted, "step: to " + order[i]);
            CheckShows(order[i], positions[order[i]], "step: to " + order[i]);
        }
        CheckInt(board.StepToAs("Ana", false, 7), ChessBoard.InputRejected, "step: not to the ply shown");
        CheckInt(board.StepToAs("Ana", false, -1), ChessBoard.InputRejected, "step: not before the start");
        CheckInt(board.StepToAs("Ana", false, plies + 1), ChessBoard.InputRejected, "step: not past the end");
        CheckInt(board.StepToAs("Cleo", false, 2), ChessBoard.InputRejected, "step: not a bystander");
        CheckInt(board.StepToAs("Cleo", true, 2), ChessBoard.InputAccepted, "step: an organizer");
        CheckShows(2, positions[2], "step: by an organizer");
        CheckString(GameText(), game, "step: the game untouched");

        // Free moves: either colour's pieces, any square, no legality; custom until the next step.
        CheckInt(board.PiecePickedUpAs("Bob", ChessRules.E4), ChessBoard.InputAccepted, "free: pick up white's");
        CheckInt(board.PiecePickedUpAs("Ana", ChessRules.E4), ChessBoard.InputAccepted, "free: pick up as white");
        CheckInt(board.PiecePickedUpAs("Ana", ChessRules.E3), ChessBoard.InputRejected, "free: not an empty square");
        CheckInt(board.PiecePickedUpAs("Cleo", ChessRules.E4), ChessBoard.InputRejected, "free: not a bystander");
        CheckInt(board.PiecePickedUpAs("Ana", 64), ChessBoard.InputRejected, "free: not off the board");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E4, ChessRules.E4), ChessBoard.InputRejected,
            "free: not on its own square");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E4, -1), ChessBoard.InputRejected, "free: not off the board");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E3, ChessRules.E4), ChessBoard.InputRejected,
            "free: not from an empty square");
        CheckInt(board.PieceDroppedAs("Cleo", ChessRules.E4, ChessRules.E5), ChessBoard.InputRejected,
            "free: not by a bystander");
        Check(!board.replayCustom, "free: refused drops change nothing", "custom");
        CheckString(BoardText(board.replayBoard), positions[2], "free: refused drops leave the replay board");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E4, ChessRules.E8), ChessBoard.InputAccepted,
            "free: a white pawn takes the black king");
        Check(board.replayCustom, "free: custom", "not custom");
        CheckInt(board.replayIndex, 2, "free: the replay index stays");
        Check(board.replayBoard[ChessRules.E8] == ChessRules.WhitePawn && board.replayBoard[ChessRules.E4] == 0,
            "free: the pawn placed on e8, not promoted", "e8 " + board.replayBoard[ChessRules.E8]);
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.D1, ChessRules.E1), ChessBoard.InputAccepted,
            "free: the queen on her own king");
        CheckInt(board._ReplayPieceDroppedAs("Cleo", true, ChessRules.A2, ChessRules.A7), ChessBoard.InputAccepted,
            "free: an organizer");
        CheckInt(board.PromotionChosenAs("Ana", ChessRules.H2, ChessRules.H4, ChessRules.Queen),
            ChessBoard.InputRejected, "free: no promotion choice in replay");
        CheckString(GameText(), game, "free: the game untouched");
        Check(board.CanStepToAs("Ana", false, 2), "free: stepping to its own ply allowed from custom", "refused");
        CheckInt(board.StepToAs("Ana", false, 2), ChessBoard.InputAccepted, "free: step back onto ply 2");
        CheckShows(2, positions[2], "free: back on the game history");
        board.PieceDroppedAs("Ana", ChessRules.B1, ChessRules.C3);
        Check(board.replayCustom, "free: custom again", "not custom");
        CheckInt(board.StepToAs("Bob", false, 9), ChessBoard.InputAccepted, "free: step elsewhere from custom");
        CheckShows(9, positions[9], "free: stepping elsewhere clears custom");

        // Leaving restores the game exactly, and play goes on.
        board.PieceDroppedAs("Bob", ChessRules.H7, ChessRules.H5);
        CheckInt(board.LeaveReplayAs("Cleo", false), ChessBoard.InputRejected, "leave replay: not a bystander");
        CheckInt(board.LeaveReplayAs("Bob", false), ChessBoard.InputAccepted, "leave replay: black");
        Check(!board.replayMode && !board.replayCustom, "leave replay: off, not custom", "replay flags set");
        CheckString(GameText(), game, "leave replay: the game exactly as it was");
        CheckShows(plies, positions[plies], "leave replay: replay fields back on the current position");
        CheckInt(board.LeaveReplayAs("Bob", false), ChessBoard.InputRejected, "leave replay: not twice");
        Drop("Ana", ChessRules.D2, ChessRules.D4, "after replay: d4");
        CheckInt(board.moveCount, plies + 1, "after replay: the game goes on");

        // No moves yet: the start position; a free move, then back to ply 0.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        CheckInt(board.EnterReplayAs("Bob", false), ChessBoard.InputAccepted, "replay with no moves: entered");
        CheckShows(0, positions[0], "replay with no moves");
        CheckInt(board.StepToAs("Bob", false, 0), ChessBoard.InputRejected, "replay with no moves: no step to 0");
        board.PieceDroppedAs("Bob", ChessRules.E7, ChessRules.E5);
        CheckInt(board.StepToAs("Bob", false, 0), ChessBoard.InputAccepted, "replay with no moves: back to 0");
        CheckShows(0, positions[0], "replay with no moves: back to the start");

        // An ended game: replay works and leaving keeps it ended.
        ButtonGame(0);
        board.ResignAs(ChessRules.White, "Ana");
        string ended = GameText();
        CheckInt(board.EnterReplayAs("Bob", false), ChessBoard.InputAccepted, "replay once ended: entered");
        CheckInt(board.StepToAs("Bob", false, 1), ChessBoard.InputAccepted, "replay once ended: step");
        board.PieceDroppedAs("Ana", ChessRules.F3, ChessRules.E5);
        Check(board.replayCustom, "replay once ended: free move", "not custom");
        CheckInt(board.LeaveReplayAs("Ana", false), ChessBoard.InputAccepted, "replay once ended: left");
        CheckString(GameText(), ended, "replay once ended: the game as it was");
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, "replay once ended: still ended");

        // A loaded board (set up by hand until 7.9a): in replay for good; anyone while nobody is seated.
        ButtonGame(0);
        board.ResignAs(ChessRules.White, "Ana");
        board.whitePlayer = "";
        board.blackPlayer = "";
        board.lifecycle = ChessBoard.LifecycleLoaded;
        board.replayMode = true;
        board.replayIndex = 4;
        board.replayCustom = false;
        CheckInt(board.StepToAs("Cleo", false, 2), ChessBoard.InputAccepted, "loaded: anyone steps");
        CheckInt(board.PieceDroppedAs("Cleo", ChessRules.E2, ChessRules.E4), ChessBoard.InputAccepted,
            "loaded: anyone moves freely");
        CheckInt(board.LeaveReplayAs("Cleo", true), ChessBoard.InputRejected, "loaded: no leaving replay");
        board.whitePlayer = "Ana";
        CheckInt(board.StepToAs("Cleo", false, 1), ChessBoard.InputRejected, "loaded: not a bystander once seated");
        CheckInt(board.PieceDroppedAs("Cleo", ChessRules.E4, ChessRules.E5), ChessBoard.InputRejected,
            "loaded: no free moves by a bystander once seated");
        CheckInt(board.StepToAs("Ana", false, 1), ChessBoard.InputAccepted, "loaded: the seated player steps");

        // The local player's methods, seated in both seats.
        board._ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        board.PieceDropped(ChessRules.E2, ChessRules.E4);
        board.PieceDropped(ChessRules.E7, ChessRules.E5);
        string local = GameText();
        CheckInt(board.EnterReplay(), ChessBoard.InputAccepted, "local replay: enter");
        CheckInt(board.StepTo(1), ChessBoard.InputAccepted, "local replay: step");
        CheckInt(board.PiecePickedUp(ChessRules.E4), ChessBoard.InputAccepted, "local replay: pick up");
        CheckInt(board.PieceDropped(ChessRules.E4, ChessRules.E6), ChessBoard.InputAccepted, "local replay: free move");
        Check(board.replayCustom, "local replay: custom", "not custom");
        CheckInt(board.LeaveReplay(), ChessBoard.InputAccepted, "local replay: leave");
        CheckString(GameText(), local, "local replay: the game as it was");

        board._ClearState();
    }

    // ---- 13.4 putting captured pieces back in replay ----

    int[] capturedTypes = new int[2 * ChessCaptures.MaxCaptured];
    int[] capturedCounts = new int[2];

    void TestPlaceCaptured() {
        NothingForced();
        string c = "put back";
        // 1. e4 d5 2. exd5 Qxd5 3. Nc3 Qxa2 4. Rxa2 b5 5. Nxb5: White has lost two pawns, Black a pawn, the queen
        // and another pawn.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        int[] gameFrom = { ChessRules.E2, ChessRules.D7, ChessRules.E4, ChessRules.D8, ChessRules.B1, ChessRules.D5,
            ChessRules.A1, ChessRules.B7, ChessRules.C3 };
        int[] gameTo = { ChessRules.E4, ChessRules.D5, ChessRules.D5, ChessRules.D5, ChessRules.C3, ChessRules.A2,
            ChessRules.A2, ChessRules.B5, ChessRules.B5 };
        for (int i = 0; i < gameFrom.Length; i++) {
            Drop(i % 2 == 0 ? "Ana" : "Bob", gameFrom[i], gameTo[i], c + ": game");
        }
        string current = BoardText(board.board);
        string game = GameText();
        CheckCaptured("PP", "PQP", c + ": the game's places");

        // Outside replay: refused.
        Check(!board.CanPlaceCapturedAs("Ana", false, ChessRules.White, ChessRules.Pawn), c + ": not outside replay",
            "allowed");
        CheckInt(board.PlaceCapturedAs("Ana", false, ChessRules.White, ChessRules.Pawn, ChessRules.E2),
            ChessBoard.InputRejected, c + ": no putting back outside replay");
        CheckInt(board.CapturedPickedUpAs("Ana", false, ChessRules.White, ChessRules.Pawn), ChessBoard.InputRejected,
            c + ": no pickup outside replay");
        CheckString(BoardText(board.board), current, c + ": refused outside replay: the game untouched");

        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, c + ": replay entered");
        CheckCaptured("PP", "PQP", c + ": replay, the last ply");
        // Who may, and what: whoever may make free moves, for a type in that colour's places.
        Check(board.CanPlaceCapturedAs("Ana", false, ChessRules.White, ChessRules.Pawn)
            && board.CanPlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Queen)
            && board.CanPlaceCapturedAs("Ana", false, ChessRules.Black, ChessRules.Pawn), c + ": the players, either "
            + "colour", "refused");
        Check(board.CanPlaceCapturedAs("Cleo", true, ChessRules.White, ChessRules.Pawn), c + ": an organizer",
            "refused");
        Check(!board.CanPlaceCapturedAs("Cleo", false, ChessRules.White, ChessRules.Pawn), c + ": not a bystander",
            "allowed");
        Check(!board.CanPlaceCapturedAs("Ana", false, ChessRules.White, ChessRules.Queen)
            && !board.CanPlaceCapturedAs("Ana", false, ChessRules.Black, ChessRules.Knight)
            && !board.CanPlaceCapturedAs("Ana", false, ChessRules.NoColor, ChessRules.Pawn)
            && !board.CanPlaceCapturedAs("Ana", false, 2, ChessRules.Pawn), c + ": not a type not captured, nor no colour",
            "allowed");
        CheckInt(board.CapturedPickedUpAs("Bob", false, ChessRules.Black, ChessRules.Queen), ChessBoard.InputAccepted,
            c + ": the queen picked up");
        CheckInt(board.CapturedPickedUpAs("Cleo", false, ChessRules.Black, ChessRules.Queen), ChessBoard.InputRejected,
            c + ": no pickup by a bystander");
        CheckInt(board.CapturedPickedUpAs("Bob", false, ChessRules.White, ChessRules.Rook), ChessBoard.InputRejected,
            c + ": no pickup of a type not captured");
        CheckInt(board.PlaceCapturedAs("Cleo", false, ChessRules.Black, ChessRules.Queen, ChessRules.D8),
            ChessBoard.InputRejected, c + ": not by a bystander");
        CheckInt(board.PlaceCapturedAs("Bob", false, ChessRules.White, ChessRules.Knight, ChessRules.B1),
            ChessBoard.InputRejected, c + ": not a type not captured");
        CheckInt(board.PlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Queen, -1), ChessBoard.InputRejected,
            c + ": not off the board");
        CheckInt(board.PlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Queen, 64), ChessBoard.InputRejected,
            c + ": not past the board");
        Check(!board.replayCustom, c + ": refused ones change nothing", "custom");
        CheckShows(9, current, c + ": refused ones leave the replay board");

        // Onto an empty square: the piece stands there, out of the last place of its type, and the position is custom.
        CheckInt(board.PlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Pawn, ChessRules.D7),
            ChessBoard.InputAccepted, c + ": a pawn back on d7");
        CheckInt(board.replayBoard[ChessRules.D7], ChessRules.PieceOf(ChessRules.Black, ChessRules.Pawn),
            c + ": a pawn on d7");
        Check(board.replayCustom, c + ": custom", "not custom");
        CheckInt(board.replayIndex, 9, c + ": the replay index stays");
        CheckInt(board.lastChange, ChessBoard.ChangeFreeMove, c + ": told as a free move");
        CheckCaptured("PP", "PQ", c + ": the pawn out of the last pawn place");
        CheckInt(board.PlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Queen, ChessRules.D8),
            ChessBoard.InputAccepted, c + ": the queen back on d8");
        CheckInt(board.replayBoard[ChessRules.D8], ChessRules.PieceOf(ChessRules.Black, ChessRules.Queen),
            c + ": the queen on d8");
        CheckCaptured("PP", "P", c + ": the queen out of her place");
        Check(!board.CanPlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Queen), c + ": the queen can't be put "
            + "back twice", "allowed");
        CheckString(GameText(), game, c + ": the game untouched");

        // Free moves take Black's pieces of every other type, which then go back, each onto an occupied square or an
        // empty one; the king last in the places, though taken first.
        Drop("Ana", ChessRules.D1, ChessRules.E8, c + ": Qd1 takes the king");
        Drop("Ana", ChessRules.E8, ChessRules.C8, c + ": the queen takes the bishop");
        Drop("Ana", ChessRules.C8, ChessRules.B8, c + ": the queen takes the knight");
        Drop("Ana", ChessRules.B8, ChessRules.A8, c + ": the queen takes the rook");
        Drop("Ana", ChessRules.A8, ChessRules.C7, c + ": the queen takes the c pawn");
        CheckCaptured("PP", "PPRBNK", c + ": free moves");
        CheckInt(board.PlaceCapturedAs("Ana", false, ChessRules.Black, ChessRules.King, ChessRules.C7),
            ChessBoard.InputAccepted, c + ": the king back on c7, taking the white queen");
        CheckInt(board.replayBoard[ChessRules.C7], ChessRules.BlackKing, c + ": the king on c7");
        CheckCaptured("PPQ", "PPRBN", c + ": the white queen in White's places");
        CheckInt(board.PlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Rook, ChessRules.A8),
            ChessBoard.InputAccepted, c + ": the rook back on a8");
        CheckInt(board.PlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Bishop, ChessRules.C8),
            ChessBoard.InputAccepted, c + ": the bishop back on c8");
        CheckInt(board.PlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Knight, ChessRules.B8),
            ChessBoard.InputAccepted, c + ": the knight back on b8");
        CheckCaptured("PPQ", "PP", c + ": Black's pieces back");
        CheckInt(board.PlaceCapturedAs("Ana", false, ChessRules.White, ChessRules.Queen, ChessRules.D1),
            ChessBoard.InputAccepted, c + ": the white queen back on d1");
        CheckInt(board.PlaceCapturedAs("Ana", false, ChessRules.White, ChessRules.Pawn, ChessRules.A2),
            ChessBoard.InputAccepted, c + ": a white pawn back on a2, taking the rook");
        CheckCaptured("PR", "PP", c + ": the rook in a place, a pawn out of one");
        CheckInt(board.PlaceCapturedAs("Cleo", true, ChessRules.White, ChessRules.Rook, ChessRules.A1),
            ChessBoard.InputAccepted, c + ": the rook back on a1, by an organizer");
        CheckInt(board.PlaceCapturedAs("Ana", false, ChessRules.White, ChessRules.Pawn, ChessRules.E2),
            ChessBoard.InputAccepted, c + ": the other white pawn back on e2");
        CheckInt(board.PlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Pawn, ChessRules.H6),
            ChessBoard.InputAccepted, c + ": a black pawn back on h6");
        CheckInt(board.PlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Pawn, ChessRules.H5),
            ChessBoard.InputAccepted, c + ": the last black pawn back, on h5");
        CheckCaptured("", "", c + ": every place empty");
        CheckInt(board.PlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Pawn, ChessRules.H4),
            ChessBoard.InputRejected, c + ": nothing left to put back");
        CheckString(GameText(), game, c + ": the game still untouched");

        // A takeback request is cancelled by putting a piece back, as by any free move.
        board.gameSettings = ChessArbiter.SettingTakebacks;
        CheckInt(board.StepToAs("Ana", false, 5), ChessBoard.InputAccepted, c + ": stepped to Nc3");
        CheckInt(board.RequestTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, c + ": takeback asked");
        CheckInt(board.PlaceCapturedAs("Bob", false, ChessRules.Black, ChessRules.Pawn, ChessRules.D7),
            ChessBoard.InputAccepted, c + ": a pawn back on d7 at Nc3");
        CheckInt(board.takebackColor, ChessRules.NoColor, c + ": the takeback request cancelled");
        board.gameSettings = 0;

        // Stepping to a game ply shows the game's position and places again.
        CheckInt(board.StepToAs("Ana", false, 9), ChessBoard.InputAccepted, c + ": stepped to the last ply");
        CheckShows(9, current, c + ": the game's position again");
        CheckCaptured("PP", "PQP", c + ": the game's places again");

        // A promoted pawn captured stands in its place as a pawn, and goes back as one.
        board._ClearState();
        StartedGame(ChessArbiter.NoClock, 0, 0);
        int[] from = { ChessRules.A2, ChessRules.B7, ChessRules.A4, ChessRules.A7, ChessRules.B5, ChessRules.C8,
            ChessRules.A6, ChessRules.B8 };
        int[] to = { ChessRules.A4, ChessRules.B5, ChessRules.B5, ChessRules.A6, ChessRules.A6, ChessRules.B7,
            ChessRules.B7, ChessRules.C6 };
        for (int i = 0; i < from.Length; i++) Drop(i % 2 == 0 ? "Ana" : "Bob", from[i], to[i], c + ": promotion game");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.B7, ChessRules.A8), ChessBoard.InputPromotionNeeded,
            c + ": bxa8 asks for a piece");
        CheckInt(board.PromotionChosenAs("Ana", ChessRules.B7, ChessRules.A8, ChessRules.Queen), ChessBoard.InputAccepted,
            c + ": bxa8=Q");
        Drop("Bob", ChessRules.D8, ChessRules.A8, c + ": Qxa8");
        CheckCaptured("P", "PPBR", c + ": the promoted queen in its place as a pawn");
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, c + ": promotion game: replay");
        Check(!board.CanPlaceCapturedAs("Ana", false, ChessRules.White, ChessRules.Queen), c + ": not as a queen",
            "allowed");
        CheckInt(board.PlaceCapturedAs("Ana", false, ChessRules.White, ChessRules.Pawn, ChessRules.A2),
            ChessBoard.InputAccepted, c + ": the promoted pawn back on a2");
        CheckInt(board.replayBoard[ChessRules.A2], ChessRules.WhitePawn, c + ": a pawn on a2");
        CheckCaptured("", "PPBR", c + ": the promoted pawn out of its place");

        board._ClearState();
    }

    // Whether each colour's captured places in the position shown are expected (piece letters in place order).
    void CheckCaptured(string white, string black, string name) {
        board.CapturedPieces(capturedTypes, capturedCounts);
        for (int color = ChessRules.White; color <= ChessRules.Black; color++) {
            string text = "";
            for (int place = 0; place < capturedCounts[color]; place++) {
                text += ChessRules.PieceTypeLetter(capturedTypes[color * ChessCaptures.MaxCaptured + place]);
            }
            CheckString(text, color == ChessRules.White ? white : black,
                name + ": " + (color == ChessRules.White ? "White's" : "Black's") + " places");
        }
    }

    // ---- 7.8b takebacks ----

    // In replay (entered if needed) at ply, requester (Ana for White, Bob for Black) asks to continue from there
    // and the other side accepts; both must be accepted.
    void Takeback(int ply, int requester, string name) {
        if (!board.replayMode) board.EnterReplayAs("Ana", false);
        if (board.replayIndex != ply || board.replayCustom) board.StepToAs("Ana", false, ply);
        string requesterName = requester == ChessRules.White ? "Ana" : "Bob";
        string answerer = requester == ChessRules.White ? "Bob" : "Ana";
        CheckInt(board.RequestTakebackAs(requester, requesterName), ChessBoard.InputAccepted, name + ": requested");
        CheckInt(board.AcceptTakebackAs(ChessRules.OtherColor(requester), answerer), ChessBoard.InputAccepted,
            name + ": accepted");
    }

    // The board after a takeback to ply: in game, out of replay, nothing pending, no result, the history cut at
    // ply with empty entries after it, and the replay fields on the position.
    void CheckContinued(int ply, string fen, string name) {
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, name + ": in game");
        Check(!board.replayMode && !board.replayCustom, name + ": out of replay", "replay flags set");
        CheckInt(board.takebackColor, ChessRules.NoColor, name + ": no request pending");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, name + ": no draw offer");
        CheckInt(board.ending, ChessGame.EndingNone, name + ": no ending");
        CheckInt(board.winner, ChessRules.NoColor, name + ": no winner");
        CheckInt(board.moveCount, ply, name + ": move count");
        CheckString(Fen(), fen, name + ": the position at that ply");
        int dirty = 0;
        for (int i = ply; i < board.moveHistory.Length; i++) {
            if (board.moveHistory[i] != 0 || board.notation[i] != "" || board.clockHistory[i] != -1
                || board.halfmoveClocks[i] != 0) {
                dirty++;
            }
        }
        CheckInt(dirty, 0, name + ": history entries after the ply empty");
        CheckShows(ply, BoardText(board.board), name + ": replay fields");
    }

    void TestTakeback() {
        NothingForced();
        int takebacks = ChessArbiter.SettingTakebacks;

        // A 60+2 game, each position and its clocks recorded: White 61, 62, 63 s, Black 60, 59, 57 s.
        StartedGame(60, 2, 1000);
        board.gameSettings = takebacks;
        string[] fens = new string[7];
        fens[0] = Fen();
        int[] from = { ChessRules.E2, ChessRules.E7, ChessRules.G1, ChessRules.B8, ChessRules.F1, ChessRules.G8 };
        int[] to = { ChessRules.E4, ChessRules.E5, ChessRules.F3, ChessRules.C6, ChessRules.C4, ChessRules.F6 };
        double[] times = { 1001, 1003, 1004, 1007, 1008, 1012 };
        for (int i = 0; i < 6; i++) {
            Play(from[i], to[i], times[i], "takeback game ply " + i);
            fens[i + 1] = Fen();
        }
        board.turnStart = board._ServerTime();
        string game = GameText();
        string firstId = board.gameId;

        // Requests: only by a seated player for their own colour, in replay on a game position with a move after it.
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected,
            "takeback: no request out of replay");
        board.EnterReplayAs("Bob", false);
        board.StepToAs("Bob", false, 3);
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Cleo"), ChessBoard.InputRejected,
            "takeback: no request by a non-player");
        CheckInt(board.RequestTakebackAs(ChessRules.White, "Bob"), ChessBoard.InputRejected,
            "takeback: no request for the other colour");
        board.gameSettings = 0;
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected,
            "takeback: no request with takebacks off");
        board.gameSettings = takebacks;
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "takeback: requested");
        CheckInt(board.takebackColor, ChessRules.Black, "takeback: requesting colour");
        CheckInt(board.takebackPly, 3, "takeback: requested ply");
        CheckInt(board.RequestTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputRejected,
            "takeback: no second request while one is pending");
        CheckInt(board.AcceptTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected,
            "takeback: the requester can't accept");
        CheckInt(board.AcceptTakebackAs(ChessRules.White, "Cleo"), ChessBoard.InputRejected,
            "takeback: a non-player can't accept");
        CheckInt(board.AcceptTakebackAs(ChessRules.White, "Bob"), ChessBoard.InputRejected,
            "takeback: not accepted for the wrong colour");
        CheckInt(board.DeclineTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected,
            "takeback: the requester can't decline");
        board.gameSettings = 0;
        CheckInt(board.AcceptTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputRejected,
            "takeback: not accepted once takebacks are off");
        board.gameSettings = takebacks;
        CheckString(GameText(), game, "takeback: refused calls leave the game");

        // A request is cancelled whenever the replay position changes: stepping, a free move, leaving.
        board.StepToAs("Ana", false, 2);
        CheckInt(board.takebackColor, ChessRules.NoColor, "takeback: stepping cancels the request");
        board.StepToAs("Ana", false, 3);
        CheckInt(board.AcceptTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputRejected,
            "takeback: a cancelled request can't be accepted back on its ply");
        board.RequestTakebackAs(ChessRules.Black, "Bob");
        board.PieceDroppedAs("Bob", ChessRules.A7, ChessRules.A6);
        CheckInt(board.takebackColor, ChessRules.NoColor, "takeback: a free move cancels the request");
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected,
            "takeback: no request from a custom position");
        board.StepToAs("Ana", false, 3);
        board.RequestTakebackAs(ChessRules.Black, "Bob");
        board.LeaveReplayAs("Ana", false);
        CheckInt(board.takebackColor, ChessRules.NoColor, "takeback: leaving replay cancels the request");
        board.EnterReplayAs("Ana", false);
        board.StepToAs("Ana", false, 3);
        board.RequestTakebackAs(ChessRules.Black, "Bob");
        CheckInt(board.DeclineTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "takeback: declined");
        CheckInt(board.takebackColor, ChessRules.NoColor, "takeback: declining clears the request");
        CheckInt(board.AcceptTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputRejected,
            "takeback: a declined request can't be accepted");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "takeback: declining changes nothing else");
        board.LeaveReplayAs("Ana", false);
        CheckString(GameText(), game, "takeback: requests and answers leave the game");

        // Accepted from a game in progress: logged once as unfinished, then cut at ply 3 with the clocks there.
        board.gameSettings = takebacks | ChessArbiter.SettingDrawAgreement;
        board.OfferDrawAs(ChessRules.White, "Ana");
        int before = board.loggedGames;
        double timeBefore = board._ServerTime();
        Takeback(3, ChessRules.Black, "takeback to 3");
        double timeAfter = board._ServerTime();
        CheckLogged(before, 1, "takeback to 3: the unfinished game logged once");
        CheckLogHas("[Result \"*\"]\n", "takeback to 3: logged as *");
        CheckLogHas("[GameID \"" + firstId + "\"]\n", "takeback to 3: logged with its own ID");
        CheckLogHas("3. Bc4 { [%clk 0:01:03] } 3... Nf6", "takeback to 3: logged with the whole history");
        Check(board.lastLoggedPgn.EndsWith("[%clk 0:00:57] } *\n"), "takeback to 3: the move text ends with *",
            "PGN:\n" + board.lastLoggedPgn);
        CheckContinued(3, fens[3], "takeback to 3");
        CheckInt(board.state[ChessPosition.StateSideToMove], ChessRules.Black, "takeback to 3: black to move");
        CheckInt(board.whiteTime, 62000, "takeback to 3: white's clock after its second move");
        CheckInt(board.blackTime, 60000, "takeback to 3: black's clock after its first move");
        Check(board.turnStart >= timeBefore && board.turnStart <= timeAfter, "takeback to 3: the turn starts now",
            "turn start " + board.turnStart + ", not between " + timeBefore + " and " + timeAfter);
        CheckString(board.continuedFrom, firstId + "/3", "takeback to 3: ContinuedFrom");
        Check(board.gameId != firstId && board.gameId.Length == 21 && board.gameId[14] == '-',
            "takeback to 3: a new game ID", "\"" + board.gameId + "\"");
        CheckString(board.notation[2], "Nf3", "takeback to 3: the history up to it kept");
        CheckInt(board.clockHistory[2], 62000, "takeback to 3: the clock history up to it kept");

        // The continued game plays on and is logged in full when it ends, referencing the first.
        string secondId = board.gameId;
        board.turnStart = 2000;
        Play(ChessRules.G8, ChessRules.F6, 2001, "continued: 2... Nf6");
        Play(ChessRules.D2, ChessRules.D4, 2002, "continued: 3. d4");
        CheckInt(board.blackTime, 61000, "continued: black's clock runs on from the restored time");
        board.turnStart = board._ServerTime();
        before = board.loggedGames;
        board.ResignAs(ChessRules.Black, "Bob");
        CheckLoggedEnding(before, "1-0", "resignation", "continued");
        CheckLogHas("[GameID \"" + secondId + "\"]\n[ContinuedFrom \"" + firstId + "/3\"]\n",
            "continued: GameID and ContinuedFrom");
        CheckLogHas("1. e4 { [%clk 0:01:01] } 1... e5 { [%clk 0:01:00] } 2. Nf3 { [%clk 0:01:02] }",
            "continued: the moves before the takeback");
        CheckLogHas("2... Nf6 { [%clk 0:01:01] } 3. d4", "continued: the moves after it");

        // Accepted once ended: nothing more logged, the result gone, and a chain of continuations.
        before = board.loggedGames;
        Takeback(1, ChessRules.White, "takeback once ended");
        CheckLogged(before, 0, "takeback once ended: nothing more logged");
        CheckContinued(1, fens[1], "takeback once ended");
        CheckInt(board.whiteTime, 61000, "takeback once ended: white's clock after its first move");
        CheckInt(board.blackTime, 60000, "takeback once ended: black's base time");
        CheckString(board.continuedFrom, secondId + "/1", "takeback once ended: ContinuedFrom the game before");
        Check(board.gameId != secondId && board.gameId != firstId, "takeback once ended: a new game ID",
            "\"" + board.gameId + "\"");

        // Back to the start: the base time for both, and nothing left to take back.
        before = board.loggedGames;
        Takeback(0, ChessRules.Black, "takeback to the start");
        CheckLogged(before, 1, "takeback to the start: the unfinished game logged");
        CheckContinued(0, fens[0], "takeback to the start");
        CheckInt(board.whiteTime, 60000, "takeback to the start: white's base time");
        CheckInt(board.blackTime, 60000, "takeback to the start: black's base time");
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, "takeback to the start: replay again");
        CheckInt(board.RequestTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputRejected,
            "takeback to the start: nothing left to take back");
        board.LeaveReplayAs("Ana", false);

        // White to move: each side's clock after its own last move.
        StartedGame(60, 2, 1000);
        board.gameSettings = takebacks;
        for (int i = 0; i < 5; i++) Play(from[i], to[i], times[i], "white-to-move takeback game ply " + i);
        board.turnStart = board._ServerTime();
        Takeback(4, ChessRules.White, "takeback to 4");
        CheckContinued(4, fens[4], "takeback to 4");
        CheckInt(board.whiteTime, 62000, "takeback to 4: white's clock after its second move");
        CheckInt(board.blackTime, 59000, "takeback to 4: black's clock after its second move");

        // A time forfeit taken back: the flagged side's clock comes back.
        StartedGame(60, 0, 1000);
        board.gameSettings = takebacks;
        Play(ChessRules.E2, ChessRules.E4, 1001, "forfeit takeback: 1. e4");
        Check(board._CheckFlagFallAt(1100), "forfeit takeback: black's flag", "not ended");
        before = board.loggedGames;
        Takeback(0, ChessRules.White, "forfeit takeback");
        CheckLogged(before, 0, "forfeit takeback: nothing more logged");
        CheckContinued(0, fens[0], "forfeit takeback");
        CheckInt(board.whiteTime, 60000, "forfeit takeback: white's base time");
        CheckInt(board.blackTime, 60000, "forfeit takeback: black's base time");
        CheckInt(board.FlaggedColor(), ChessRules.NoColor, "forfeit takeback: no flag");

        // Castling, en passant and the counters come back exactly; no clock stays 0.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = takebacks;
        int[] from2 = { ChessRules.E2, ChessRules.G8, ChessRules.E4, ChessRules.D7, ChessRules.E5, ChessRules.C7,
            ChessRules.G1, ChessRules.B8, ChessRules.F1, ChessRules.G7, ChessRules.E1, ChessRules.F8 };
        int[] to2 = { ChessRules.E4, ChessRules.F6, ChessRules.E5, ChessRules.D5, ChessRules.D6, ChessRules.D6,
            ChessRules.F3, ChessRules.C6, ChessRules.E2, ChessRules.G6, ChessRules.G1, ChessRules.G7 };
        string[] fens2 = new string[from2.Length + 1];
        fens2[0] = Fen();
        for (int i = 0; i < from2.Length; i++) {
            Drop(i % 2 == 0 ? "Ana" : "Bob", from2[i], to2[i], "position takeback game ply " + i);
            fens2[i + 1] = Fen();
        }
        Takeback(10, ChessRules.White, "takeback before castling");
        CheckContinued(10, fens2[10], "takeback before castling");
        CheckInt(board.whiteTime + board.blackTime, 0, "takeback with no clock: both 0");
        Drop("Ana", ChessRules.E1, ChessRules.G1, "takeback before castling: castles again");
        Takeback(4, ChessRules.Black, "takeback before en passant");
        CheckContinued(4, fens2[4], "takeback before en passant");
        Drop("Ana", ChessRules.E5, ChessRules.D6, "takeback before en passant: en passant again");
        CheckString(Fen(), fens2[5], "takeback before en passant: the same position after it");

        // The local player's methods, seated in both seats: Continue from here in place of a takeback (13.3).
        board._ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        board.gameSettings = takebacks;
        board.PieceDropped(ChessRules.E2, ChessRules.E4);
        board.PieceDropped(ChessRules.E7, ChessRules.E5);
        board.EnterReplay();
        board.StepTo(1);
        CheckInt(board.RequestTakeback(ChessRules.White), ChessBoard.InputRejected,
            "local takeback: no request in both seats");
        CheckInt(board.ContinueGame(), ChessBoard.InputAccepted, "local continue: accepted");
        CheckContinued(1, fens[1], "local continue");

        board._ClearState();
    }

    // ---- 13.3 continuing from here in both seats ----

    // One player in both seats continues from a replay position at once, as an accepted takeback does.
    void TestContinueAlone() {
        NothingForced();
        int takebacks = ChessArbiter.SettingTakebacks;

        // The takeback test's 60+2 game, Ana in both seats: White 61, 62, 63 s, Black 60, 59, 57 s.
        board._ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        board.JoinAs(ChessRules.Black, "Ana");
        board.SetClockAs("Ana", 60, 2);
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, "continue alone: started");
        board.turnStart = 1000;
        board.gameSettings = takebacks;
        string[] fens = new string[7];
        fens[0] = Fen();
        int[] from = { ChessRules.E2, ChessRules.E7, ChessRules.G1, ChessRules.B8, ChessRules.F1, ChessRules.G8 };
        int[] to = { ChessRules.E4, ChessRules.E5, ChessRules.F3, ChessRules.C6, ChessRules.C4, ChessRules.F6 };
        double[] times = { 1001, 1003, 1004, 1007, 1008, 1012 };
        for (int i = 0; i < 6; i++) {
            Play(from[i], to[i], times[i], "continue alone game ply " + i);
            fens[i + 1] = Fen();
        }
        board.turnStart = board._ServerTime();
        string game = GameText();
        string firstId = board.gameId;

        // Refused out of replay, by someone else, from the last or a custom position, and with takebacks off; no
        // takeback requests or answers for a player in both seats.
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputRejected, "continue alone: not out of replay");
        board.EnterReplayAs("Ana", false);
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputRejected, "continue alone: not from the last position");
        board.StepToAs("Ana", false, 3);
        CheckInt(board.ContinueGameAs("Cleo"), ChessBoard.InputRejected, "continue alone: not by a non-player");
        CheckInt(board.ContinueGameAs(""), ChessBoard.InputRejected, "continue alone: not by nobody");
        Check(!board.CanRequestTakebackAs(ChessRules.White, "Ana"), "continue alone: no request for white",
            "allowed");
        Check(!board.CanRequestTakebackAs(ChessRules.Black, "Ana"), "continue alone: no request for black",
            "allowed");
        board.takebackColor = ChessRules.White;
        board.takebackPly = 3;
        Check(!board.CanAcceptTakebackAs(ChessRules.Black, "Ana"), "continue alone: no accepting", "allowed");
        Check(!board.CanDeclineTakebackAs(ChessRules.Black, "Ana"), "continue alone: no declining", "allowed");
        board.takebackColor = ChessRules.NoColor;
        board.takebackPly = 0;
        board.gameSettings = 0;
        Check(!board.CanContinueAs("Ana"), "continue alone: not with takebacks off", "allowed");
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputRejected, "continue alone: refused with takebacks off");
        board.gameSettings = takebacks;
        board.PieceDroppedAs("Ana", ChessRules.A7, ChessRules.A6);
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputRejected, "continue alone: not from a custom position");
        board.StepToAs("Ana", false, 3);
        CheckString(GameText(), game, "continue alone: refused calls leave the game");

        // From ply 3 of the game in progress: logged once as unfinished, cut there with the clocks there.
        board.gameSettings = takebacks | ChessArbiter.SettingDrawAgreement;
        board.OfferDrawAs(ChessRules.White, "Ana");
        Check(board.CanContinueAs("Ana"), "continue alone: in game", "refused");
        int before = board.loggedGames;
        double timeBefore = board._ServerTime();
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputAccepted, "continue alone to 3: accepted");
        double timeAfter = board._ServerTime();
        CheckInt(board.lastChange, ChessBoard.ChangeTakeback, "continue alone to 3: told as a takeback");
        CheckLogged(before, 1, "continue alone to 3: the unfinished game logged once");
        CheckLogHas("[Result \"*\"]\n", "continue alone to 3: logged as *");
        CheckLogHas("[GameID \"" + firstId + "\"]\n", "continue alone to 3: logged with its own ID");
        CheckLogHas("3. Bc4 { [%clk 0:01:03] } 3... Nf6", "continue alone to 3: logged with the whole history");
        CheckContinued(3, fens[3], "continue alone to 3");
        CheckInt(board.whiteTime, 62000, "continue alone to 3: white's clock after its second move");
        CheckInt(board.blackTime, 60000, "continue alone to 3: black's clock after its first move");
        Check(board.turnStart >= timeBefore && board.turnStart <= timeAfter, "continue alone to 3: the turn starts now",
            "turn start " + board.turnStart + ", not between " + timeBefore + " and " + timeAfter);
        CheckString(board.continuedFrom, firstId + "/3", "continue alone to 3: ContinuedFrom");
        Check(board.gameId != firstId && board.gameId.Length == 21, "continue alone to 3: a new game ID",
            "\"" + board.gameId + "\"");
        CheckString(board.whitePlayer + "|" + board.blackPlayer, "Ana|Ana", "continue alone to 3: the seats kept");

        // Once ended: nothing more logged, the result gone.
        string secondId = board.gameId;
        board.ResignAs(ChessRules.Black, "Ana");
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, "continue alone: resigned");
        board.EnterReplayAs("Ana", false);
        board.StepToAs("Ana", false, 1);
        Check(board.CanContinueAs("Ana"), "continue alone: once ended", "refused");
        before = board.loggedGames;
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputAccepted, "continue alone once ended: accepted");
        CheckLogged(before, 0, "continue alone once ended: nothing more logged");
        CheckContinued(1, fens[1], "continue alone once ended");
        CheckInt(board.whiteTime, 61000, "continue alone once ended: white's clock after its first move");
        CheckInt(board.blackTime, 60000, "continue alone once ended: black's base time");
        CheckString(board.continuedFrom, secondId + "/1", "continue alone once ended: ContinuedFrom the game before");

        // Two different players: only takebacks.
        StartedGame(60, 2, 1000);
        board.gameSettings = takebacks;
        Play(ChessRules.E2, ChessRules.E4, 1001, "continue, two players: 1. e4");
        Play(ChessRules.E7, ChessRules.E5, 1002, "continue, two players: 1... e5");
        board.EnterReplayAs("Ana", false);
        board.StepToAs("Ana", false, 1);
        Check(!board.CanContinueAs("Ana"), "continue, two players: not for white", "allowed");
        Check(!board.CanContinueAs("Bob"), "continue, two players: not for black", "allowed");
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputRejected, "continue, two players: refused");
        Check(board.CanRequestTakebackAs(ChessRules.White, "Ana"), "continue, two players: a takeback instead",
            "refused");

        board._ClearState();
    }

    // ---- 7.9a loading a game ----

    const string NukaFinalFen = "6k1/1b4pp/p3p3/2p1P1b1/NPPn4/7P/P5q1/RN2Kr2 w - - 0 28";

    // Imports text in one go with the importer and returns the result.
    int ImportText(string text) {
        importer._StartImport(text, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
        return importer._ImportFor(float.MaxValue);
    }

    // Imports text and loads it onto a reset board, checking both are accepted.
    void LoadText(string text, string name) {
        board._ClearState();
        CheckInt(ImportText(text), ChessPgn.ImportDone, name + ": imported");
        CheckInt(board._LoadImportAs("", importer), ChessBoard.InputAccepted, name + ": loaded");
    }

    // Whether the board's PGN has this tag line.
    void CheckPgnHas(string line, bool expected, string name) {
        string pgn = board.GamePgn();
        Check(pgn.Contains(line + "\n") == expected, name, (expected ? "missing " : "has ") + line + " in\n" + pgn);
    }

    // A refused load leaves the board reset, as _ClearState makes it.
    void CheckStillReset(string name) {
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, name + ": still reset");
        CheckString(Fen(), ChessFen.StartFen, name + ": the standard start");
        CheckInt(board.moveCount, 0, name + ": no moves");
        Check(!board.replayMode, name + ": not in replay", "in replay");
        CheckString(board.replaySourceWhite + board.replaySourceBlack + board.replaySourceResult + board.gameId, "",
            name + ": no loaded tags");
    }

    void TestLoadGame() {
        Check(Utilities.IsValid(importer) && siggiVsNuka != null, "load: importer and Siggi-vs-Nuka assigned",
            "assign BoardTest's Importer and Siggi Vs Nuka (Assets/Tests/PGN/Siggi-vs-Nuka.pgn.txt) in the Inspector");
        if (!Utilities.IsValid(importer) || siggiVsNuka == null) return;
        string nuka = siggiVsNuka.text;

        // Siggi-vs-Nuka: the loaded state in replay on the last position, the PGN's names, result and ID, its time
        // control and clocks, and the moves played through (FEN counters included).
        board._ClearState();
        Check(board.CanLoadGame(), "load: a reset board can load", "refused");
        CheckInt(ImportText(nuka), ChessPgn.ImportDone, "load: Siggi-vs-Nuka imported");
        CheckInt(importer.MoveCount(), 54, "load: Siggi-vs-Nuka has 54 plies");
        CheckInt(board._LoadImportAs("", importer), ChessBoard.InputAccepted, "load: Siggi-vs-Nuka loaded");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "load: loaded state");
        Check(board.replayMode, "load: in replay", "not in replay");
        CheckShows(54, BoardText(board.board), "load: shows the last position");
        CheckString(Fen(), NukaFinalFen, "load: the final position");
        CheckInt(board.moveCount, 54, "load: 54 moves");
        CheckInt(board.mode, ChessGame.ModeStandard, "load: Standard");
        CheckString(board.replaySourceWhite, "Siggі", "load: White from the PGN");
        CheckString(board.replaySourceBlack, "Nukaǃ", "load: Black from the PGN");
        CheckString(board.replaySourceResult, "0-1", "load: Result from the PGN");
        CheckString(board.gameId, "20250710001921", "load: the PGN's GameID");
        CheckString(board.continuedFrom, "", "load: no ContinuedFrom");
        CheckSeats("", "", "load: nobody seated");
        CheckInt(board.ending, ChessGame.EndingCheckmate, "load: ended by checkmate");
        CheckInt(board.winner, ChessRules.Black, "load: Black won");
        CheckInt(board.replaySourceBaseTime * 1000 + board.replaySourceIncrement, 600000,
            "load: the game's time control 600");
        CheckInt(board.baseTime * 1000 + board.increment, 600000, "load: the clock for continuing starts as 600");
        Check(!board.IsClockRunning() && board.RemainingTime(ChessRules.White) == 0, "load: no clock running",
            "running");
        CheckInt(board.clockHistory[0], 600000, "load: clock of ply 1");
        CheckInt(board.clockHistory[1], 597600, "load: clock of ply 2");
        CheckInt(board.clockHistory[53], 480500, "load: clock of ply 54");
        string errors = "";
        int[] clocks = new int[600];
        ChessDraws._BuildHalfmoveClocks(board.moveHistory, 54, 0, clocks);
        for (int i = 0; i < board.moveHistory.Length; i++) {
            bool played = i < 54;
            if (board.moveHistory[i] != (played ? importer.moves[i] : 0)
                || board.notation[i] != (played ? importer.sans[i] : "")
                || board.clockHistory[i] != (played ? importer.clocks[i] : -1)
                || board.halfmoveClocks[i] != (played ? clocks[i] : 0)) {
                errors += " ply " + (i + 1) + ": " + board.moveHistory[i] + " " + board.notation[i] + " "
                    + board.clockHistory[i] + " " + board.halfmoveClocks[i];
            }
        }
        Check(errors == "", "load: the import's moves, SAN and clocks, the halfmove clocks, nothing after", errors);
        CheckString(board.notation[53], "Rxf1#", "load: the last SAN with its #");

        // The board's export: the PGN's own tags and the same [%clk] values (read back with the importer).
        CheckPgnHas("[White \"Siggі\"]", true, "load: export White");
        CheckPgnHas("[Black \"Nukaǃ\"]", true, "load: export Black");
        CheckPgnHas("[Result \"0-1\"]", true, "load: export Result");
        CheckPgnHas("[TimeControl \"600\"]", true, "load: export TimeControl");
        CheckPgnHas("[GameID \"20250710001921\"]", true, "load: export GameID");
        CheckPgnHas("[Termination \"checkmate\"]", true, "load: export Termination");
        CheckPgnHas("[Date \"????.??.??\"]", true, "load: export Date unknown for a foreign ID");
        Check(!board.GamePgn().Contains("ContinuedFrom") && !board.GamePgn().Contains("[FEN"),
            "load: export without ContinuedFrom or FEN", board.GamePgn());
        Check(board.GamePgn().Contains("1. e4 { [%clk 0:10:00] } 1... c5 { [%clk 0:09:57.6] } 2. Nf3"),
            "load: export move text with clocks", board.GamePgn());
        string exported = board.GamePgn();
        CheckInt(ImportText(exported), ChessPgn.ImportDone, "load: export imported again");
        errors = "";
        for (int i = 0; i < 54; i++) {
            if (importer.clocks[i] != board.clockHistory[i] || importer.sans[i] != board.notation[i]) {
                errors += " ply " + (i + 1) + ": " + importer.sans[i] + " " + importer.clocks[i];
            }
        }
        if (importer.MoveCount() != 54) errors += " " + importer.MoveCount() + " plies";
        Check(errors == "", "load: the export's [%clk] values and moves are the loaded game's", errors);
        CheckString(importer.tags[ChessPgn.TagResult], "0-1", "load: the export's Result read back");

        // Replay on the loaded board: stepping by anyone while nobody is seated, no leaving.
        CheckInt(board.StepToAs("Cleo", false, 0), ChessBoard.InputAccepted, "load: step to the start");
        CheckString(BoardText(board.replayBoard), BoardText(StartBoard()), "load: the start shown");
        CheckInt(board.StepToAs("Cleo", false, 54), ChessBoard.InputAccepted, "load: step to the end");
        CheckInt(board.LeaveReplayAs("Cleo", true), ChessBoard.InputRejected, "load: no leaving replay");
        CheckInt(board.EnterReplayAs("Cleo", true), ChessBoard.InputRejected, "load: no entering replay again");
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, "load: a seat joined");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "load: still loaded after joining");
        CheckPgnHas("[White \"Siggі\"]", true, "load: export keeps the PGN's White after joining");

        // Refused unless the board is reset: loaded (a second load), lobby, in game and ended. Nothing changes.
        string loaded = GameText();
        Check(!board.CanLoadGame(), "load: a loaded board can't load", "allowed");
        CheckInt(ImportText(nuka), ChessPgn.ImportDone, "load: imported again");
        CheckInt(board._LoadImportAs("", importer), ChessBoard.InputRejected, "load: refused on a loaded board");
        CheckString(GameText(), loaded, "load: refused on a loaded board, nothing changed");
        LobbyWithTwoPlayers();
        string lobby = GameText();
        CheckInt(board._LoadImportAs("", importer), ChessBoard.InputRejected, "load: refused in the lobby");
        CheckString(GameText(), lobby, "load: refused in the lobby, nothing changed");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "load: refused in the lobby, still the lobby");
        board.StartGameAs("Ana");
        board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4);
        string inGame = GameText();
        CheckInt(board._LoadImportAs("", importer), ChessBoard.InputRejected, "load: refused in game");
        CheckString(GameText(), inGame, "load: refused in game, nothing changed");
        board.ResignAs(ChessRules.Black, "Bob");
        string ended = GameText();
        CheckInt(board._LoadImportAs("", importer), ChessBoard.InputRejected, "load: refused once ended");
        CheckString(GameText(), ended, "load: refused once ended, nothing changed");
        Check(!board.CanLoadGame(), "load: an ended board can't load", "allowed");

        // An import still under way or failed is refused, and so is no importer.
        board._ClearState();
        importer._StartImport(nuka, 600, ChessPgnImporter.DefaultBudgetMs, null, "");
        CheckInt(board._LoadImportAs("", importer), ChessBoard.InputRejected, "load: refused while importing");
        CheckStillReset("load: refused while importing");
        importer._Abandon();
        CheckInt(ImportText("1. e4 hello"), ChessPgn.ImportBadSyntax, "load: a failed import");
        CheckInt(board._LoadImportAs("", importer), ChessBoard.InputRejected, "load: refused after a failed import");
        CheckStillReset("load: refused after a failed import");
        CheckInt(board._LoadImportAs("", null), ChessBoard.InputRejected, "load: refused with no importer");

        // _LoadGame refuses invalid data and leaves the board reset.
        int[] moves = new int[4];
        string[] sans = new string[] { "", "", "", "" };
        int[] noClocks = new int[] { -1, -1, -1, -1 };
        CheckInt(board._LoadGameAs("", "not a fen", moves, sans, noClocks, 0, ChessGame.ModeStandard, "", "", "", "",
            ChessArbiter.NoClock, 0), ChessBoard.InputRejected, "load: bad FEN refused");
        CheckStillReset("load: bad FEN refused");
        CheckInt(board._LoadGameAs("", "", moves, sans, noClocks, 5, ChessGame.ModeStandard, "", "", "", "",
            ChessArbiter.NoClock, 0), ChessBoard.InputRejected, "load: more moves than given refused");
        CheckInt(board._LoadGameAs("", "", moves, sans, noClocks, -1, ChessGame.ModeStandard, "", "", "", "",
            ChessArbiter.NoClock, 0), ChessBoard.InputRejected, "load: a negative count refused");
        CheckInt(board._LoadGameAs("", "", null, sans, noClocks, 0, ChessGame.ModeStandard, "", "", "", "",
            ChessArbiter.NoClock, 0), ChessBoard.InputRejected, "load: no moves array refused");
        CheckInt(board._LoadGameAs("", "", moves, sans, noClocks, 0, 7, "", "", "", "", ChessArbiter.NoClock, 0),
            ChessBoard.InputRejected, "load: unknown mode refused");
        CheckInt(board._LoadGameAs("", "", moves, sans, noClocks, 0, ChessGame.ModeStandard, "", "", "", "", 0, 0),
            ChessBoard.InputRejected, "load: base time 0 refused");
        CheckInt(board._LoadGameAs("", "", moves, sans, noClocks, 0, ChessGame.ModeStandard, "", "", "", "", 60, -1),
            ChessBoard.InputRejected, "load: negative increment refused");
        CheckStillReset("load: invalid data refused");

        // _LoadGame directly (as the archive will): no moves, null tags, the standard start.
        CheckInt(board._LoadGameAs("", null, moves, sans, noClocks, 0, ChessGame.ModeStandard, null, null, null, null,
            ChessArbiter.NoClock, 0), ChessBoard.InputAccepted, "load: a game with no moves");
        CheckShows(0, BoardText(StartBoard()), "load: a game with no moves");
        CheckString(board.replaySourceWhite + board.replaySourceBlack + board.replaySourceResult + board.gameId, "",
            "load: null tags are empty");
        CheckInt(board.ending, ChessGame.EndingNone, "load: a game with no moves has no result");
        CheckPgnHas("[TimeControl \"-\"]", true, "load: no clock exported as -");
        CheckPgnHas("[Result \"*\"]", true, "load: no result exported as *");

        // Time controls: "-" is no clock, a missing or unreadable one unknown ("?", no clock to continue with), one
        // a player couldn't choose kept for the export only.
        LoadText("[TimeControl \"-\"]\n\n1. e4 *", "load: TimeControl -");
        CheckInt(board.replaySourceBaseTime, ChessArbiter.NoClock, "load: TimeControl - is no clock");
        CheckInt(board.baseTime, ChessArbiter.NoClock, "load: TimeControl -, no clock to continue with");
        CheckPgnHas("[TimeControl \"-\"]", true, "load: TimeControl - exported");
        LoadText("1. e4 *", "load: no TimeControl");
        CheckInt(board.replaySourceBaseTime, ChessBoard.UnknownClock, "load: no TimeControl is unknown");
        CheckInt(board.baseTime, ChessArbiter.NoClock, "load: no TimeControl, no clock to continue with");
        CheckPgnHas("[TimeControl \"?\"]", true, "load: no TimeControl exported as ?");
        LoadText("[TimeControl \"40/7200:3600\"]\n\n1. e4 *", "load: unreadable TimeControl");
        CheckPgnHas("[TimeControl \"?\"]", true, "load: unreadable TimeControl exported as ?");
        LoadText("[TimeControl \"300+3\"]\n\n1. e4 *", "load: TimeControl 300+3");
        CheckInt(board.baseTime * 1000 + board.increment, 300003, "load: 300+3 to continue with");
        CheckPgnHas("[TimeControl \"300+3\"]", true, "load: 300+3 exported");
        LoadText("[TimeControl \"90000+5\"]\n\n1. e4 *", "load: TimeControl over 24 h");
        CheckInt(board.replaySourceBaseTime * 10 + board.replaySourceIncrement, 900005, "load: 90000+5 kept");
        CheckInt(board.baseTime, ChessArbiter.NoClock, "load: 90000+5, no clock to continue with");
        CheckPgnHas("[TimeControl \"90000+5\"]", true, "load: 90000+5 exported");

        // Results: a Result tag without a mate on the board is an unknown ending with that winner; "*" or none is
        // no result; a stalemate on the board is one whatever the tag says.
        LoadText("[Result \"1-0\"]\n\n1. e4 1-0", "load: Result 1-0");
        CheckInt(board.ending, ChessGame.EndingUnknown, "load: Result 1-0, ending unknown");
        CheckInt(board.winner, ChessRules.White, "load: Result 1-0, White won");
        CheckPgnHas("[Result \"1-0\"]", true, "load: Result 1-0 exported");
        Check(!board.GamePgn().Contains("Termination"), "load: Result 1-0, no Termination", board.GamePgn());
        Check(board.GamePgn().EndsWith(" 1-0\n"), "load: Result 1-0 ends the move text", board.GamePgn());
        LoadText("[Result \"1/2-1/2\"]\n\n1. e4 1/2-1/2", "load: Result 1/2-1/2");
        CheckInt(board.ending * 10 + board.winner, ChessGame.EndingUnknown * 10 + ChessRules.NoColor,
            "load: Result 1/2-1/2, a draw");
        CheckPgnHas("[Result \"1/2-1/2\"]", true, "load: Result 1/2-1/2 exported");
        LoadText("[Result \"*\"]\n\n1. e4 *", "load: Result *");
        CheckInt(board.ending, ChessGame.EndingNone, "load: Result *, no result");
        CheckPgnHas("[Result \"*\"]", true, "load: Result * exported");
        LoadText("[Result \"0-1\"]\n[SetUp \"1\"]\n[FEN \"7k/5Q2/6K1/8/8/8/8/8 b - - 0 1\"]\n\n0-1", "load: stalemate");
        CheckInt(board.ending * 10 + board.winner, ChessGame.EndingStalemate * 10 + ChessRules.NoColor,
            "load: stalemate on the board");
        CheckString(board.replaySourceResult, "0-1", "load: stalemate, the tag shown as it is");
        CheckPgnHas("[FEN \"7k/5Q2/6K1/8/8/8/8/8 b - - 0 1\"]", true, "load: stalemate, FEN exported");

        // Chess960 from Variant, a game longer than the history's first room, and Black to move first.
        LoadText("[Variant \"Chess960\"]\n[SetUp \"1\"]\n[FEN \"qrkbbnrn/pppppppp/8/8/8/8/PPPPPPPP/QRKBBNRN w GBgb - 0 1\"]"
            + "\n\n1. e4 e5 2. Bf3 Bf6 3. O-O-O O-O-O *", "load: Chess960");
        CheckInt(board.mode, ChessGame.ModeChess960, "load: Chess960 from Variant");
        CheckString(Fen(), "q1krbnrn/pppp1ppp/5b2/4p3/4P3/5B2/PPPP1PPP/Q1KRBNRN w - - 4 4", "load: Chess960 position");
        CheckPgnHas("[Variant \"Chess960\"]", true, "load: Chess960 exported");
        string shuffle = "";
        for (int i = 0; i < 40; i++) shuffle += "Nf3 Nf6 Ng1 Ng8 ";
        LoadText(shuffle + "*", "load: 160 plies");
        CheckInt(board.moveCount, 160, "load: 160 plies loaded");
        Check(board.moveHistory.Length >= 160 && board.notation.Length == board.moveHistory.Length
            && board.clockHistory.Length == board.moveHistory.Length
            && board.halfmoveClocks.Length == board.moveHistory.Length, "load: 160 plies, the history grown",
            "length " + board.moveHistory.Length);
        CheckString(Fen(), "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 160 81", "load: 160 plies, counters");
        CheckString(board.notation[159] + "|" + board.notation[160], "Ng8|", "load: 160 plies, SAN");
        CheckInt(board.halfmoveClocks[159], 159, "load: 160 plies, halfmove clock before the last");
        LoadText("[SetUp \"1\"]\n[FEN \"4k3/8/8/8/8/8/4P3/4K3 b - - 3 30\"]\n\n30... Kd7 31. e4 *", "load: Black first");
        CheckString(Fen(), "8/3k4/8/8/4P3/8/8/4K3 b - e3 0 31", "load: Black first, the position");
        Check(board.GamePgn().Contains("30... Kd7 31. e4 *"), "load: Black first, the export's numbering",
            board.GamePgn());

        // The snapshot is taken once the board is loaded: a forced clock is applied (not the forced mode, the
        // game's), and the export keeps the game's own time control. Without an arbiter everything is off.
        if (Utilities.IsValid(board.arbiter)) {
            int forced = ChessArbiter.SettingModeForced | ChessArbiter.SettingClockForced
                | ChessArbiter.SettingTakebacks;
            OverrideAll(forced, 30, 300, ChessGame.ModeStandard, 120, 1);
            LoadText("[Variant \"Chess960\"]\n[TimeControl \"600\"]\n[SetUp \"1\"]\n"
                + "[FEN \"qrkbbnrn/pppppppp/8/8/8/8/PPPPPPPP/QRKBBNRN w GBgb - 0 1\"]\n\n1. e4 *", "load: snapshot");
            CheckSnapshot(forced, 30, 300, ChessGame.ModeStandard, 120, 1, "load: snapshot taken");
            CheckInt(board.baseTime * 1000 + board.increment, 120001, "load: snapshot, the forced clock");
            CheckInt(board.mode, ChessGame.ModeChess960, "load: snapshot, the game's mode");
            CheckPgnHas("[TimeControl \"600\"]", true, "load: snapshot, the game's TimeControl exported");
            CheckInt(board.SetClockAs("Ana", 60, 0), ChessBoard.InputRejected, "load: snapshot, the clock forced");
            NothingForced();
        } else {
            LoadText("1. e4 *", "load: no arbiter");
            CheckInt(board.gameSettings, 0, "load: no arbiter, every setting off");
            CheckInt(board.GameNumber(ChessArbiter.NumberAutoResetTime), ChessArbiter.Never,
                "load: no arbiter, never reset");
        }

        board._ClearState();
    }

    // ---- 7.9b continuing a loaded game ----

    // The FEN after the first count of moves from fen, with its counters.
    string FenAfter(string fen, int[] moves, int count) {
        byte[] squares = new byte[64];
        int[] scratchState = new int[ChessPosition.StateLength];
        int clock;
        int number;
        ChessFen.ParseFen(fen, squares, scratchState, out clock, out number);
        for (int i = 0; i < count; i++) {
            if (scratchState[ChessPosition.StateSideToMove] == ChessRules.Black) number++;
            clock = ChessDraws.HalfmoveClockAfter(clock, moves[i]);
            ChessPosition.MakeMove(squares, scratchState, moves[i]);
        }
        return ChessFen.WriteFen(squares, scratchState, clock, number);
    }

    // Loads text, seats Ana as White and Bob as Black, and steps to ply (by Ana).
    void LoadSeated(string text, int ply, string name) {
        LoadText(text, name);
        board.JoinAs(ChessRules.White, "Ana");
        board.JoinAs(ChessRules.Black, "Bob");
        if (board.replayIndex != ply) board.StepToAs("Ana", false, ply);
        CheckInt(board.replayIndex, ply, name + ": showing ply " + ply);
    }

    // The board after continuing a loaded game from ply: in game, out of replay, the position at ply with that side
    // to move, both clocks at the base time, no result or loaded tags, the moves before ply kept without their
    // clocks and nothing after, and the replay fields on the position.
    void CheckContinuedLoaded(int ply, string fen, int startMs, string name) {
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, name + ": in game");
        Check(!board.replayMode && !board.replayCustom, name + ": out of replay", "replay flags set");
        CheckString(Fen(), fen, name + ": the position at that ply");
        CheckInt(board.moveCount, ply, name + ": move count");
        CheckInt(board.whiteTime, startMs, name + ": white's fresh clock");
        CheckInt(board.blackTime, startMs, name + ": black's fresh clock");
        CheckInt(board.ending, ChessGame.EndingNone, name + ": no ending");
        CheckInt(board.winner, ChessRules.NoColor, name + ": no winner");
        CheckString(board.replaySourceWhite + "|" + board.replaySourceBlack + "|" + board.replaySourceResult, "||",
            name + ": the loaded tags dropped");
        CheckInt(board.replaySourceBaseTime * 1000 + board.replaySourceIncrement, ChessArbiter.NoClock * 1000,
            name + ": the loaded time control dropped");
        CheckSeats("Ana", "Bob", name + ": the seated players");
        CheckInt(board.takebackColor * 10 + board.drawOfferColor, ChessRules.NoColor * 11, name + ": nothing pending");
        string errors = "";
        for (int i = 0; i < board.moveHistory.Length; i++) {
            bool kept = i < ply;
            if ((board.moveHistory[i] == 0) == kept || (board.notation[i] == "") == kept
                || board.clockHistory[i] != -1 || (!kept && board.halfmoveClocks[i] != 0)) {
                errors += " ply " + (i + 1) + ": " + board.moveHistory[i] + " " + board.notation[i] + " "
                    + board.clockHistory[i] + " " + board.halfmoveClocks[i];
            }
        }
        Check(errors == "", name + ": the moves before the ply kept without clocks, nothing after", errors);
        CheckShows(ply, BoardText(board.board), name + ": replay fields");
    }

    // ---- 13.2 the player who loads a game keeps the board ----

    // Imports Siggi-vs-Nuka and loads it onto a reset board as loader.
    void LoadNukaAs(string loader, string name) {
        board._ClearState();
        CheckInt(ImportText(siggiVsNuka.text), ChessPgn.ImportDone, name + ": imported");
        CheckInt(board._LoadImportAs(loader, importer), ChessBoard.InputAccepted, name + ": loaded");
    }

    // The first square of the replay board with a piece on it (occupied) or without (not).
    int ReplaySquare(bool occupied) {
        for (int square = 0; square < 64; square++) {
            if ((board.replayBoard[square] != ChessRules.Empty) == occupied) return square;
        }
        return ChessRules.NoSquare;
    }

    // Whether name (an organizer or not) may act on the loaded board, as expected: step, pick up a piece for a free
    // move, touch the pieces, own the board (both players counted present, not idle) and, with withReset, reset it.
    // Only the Can… checks: nothing changes.
    void CheckMayAct(string name, bool organizer, bool expected, bool withReset, string label) {
        string errors = "";
        double now = board.lastActivity;
        int to = board.replayIndex == 0 ? 1 : 0;
        if (board.CanStepToAs(name, organizer, to) != expected) errors += "step; ";
        if (board.CanPickUpAs(name, organizer, ReplaySquare(true)) != expected) errors += "free move; ";
        if (board.MayTouchPiecesAs(name, organizer) != expected) errors += "touch; ";
        if (board._MayOwnAs(name, organizer, true, now) != expected) errors += "own; ";
        if (withReset && board.CanResetAs(name, organizer) != expected) errors += "reset; ";
        Check(errors.Length == 0, label, (expected ? "refused: " : "allowed: ") + errors);
    }

    void TestLoadedBy() {
        string s = "loaded by";
        if (!Utilities.IsValid(importer) || siggiVsNuka == null) {
            Check(false, s + ": importer and Siggi-vs-Nuka assigned", "assign them in the Inspector");
            return;
        }
        NothingForced();
        int resetTime = ChessArbiter.NumberResetAvailableTime;

        // The local-player forms record the local player; "" records nobody and locks nothing.
        board._ClearState();
        CheckInt(ImportText(siggiVsNuka.text), ChessPgn.ImportDone, s + ": imported");
        CheckInt(board._LoadImport(importer), ChessBoard.InputAccepted, s + ": LoadImport");
        CheckString(board.loadedBy, ChessUtil._LocalName(""), s + ": LoadImport records the local player");
        LoadNukaAs("", s + ": nobody");
        CheckString(board.loadedBy, "", s + ": nobody recorded");
        Check(!board.IsLockedToLoader(), s + ": nobody recorded, not locked", "locked");
        CheckMayAct("Cleo", false, true, false, s + ": nobody recorded, anyone may act");
        Check(!board.CanResetAs("Cleo", false), s + ": nobody recorded, a bystander may not reset (as before)",
            "allowed");

        // A load records its loader and locks the board to them.
        LoadNukaAs("Lena", s);
        CheckString(board.loadedBy, "Lena", s + ": Lena recorded");
        Check(board.IsLockedToLoader(), s + ": locked to Lena", "not locked");
        Check(board.IsLoader("Lena") && !board.IsLoader("Lena ") && !board.IsLoader("") && !board.IsLoader(null),
            s + ": the loader's name compared exactly", "a lookalike or no name counts");
        board._PackState();
        CheckString(board.syncedLoadedBy, "Lena", s + ": sent");
        board.loadedBy = "";
        board._ReceiveState();
        CheckString(board.loadedBy, "Lena", s + ": received");
        Check(board.IsLockedToLoader(), s + ": received, locked", "not locked");

        // While locked: Lena and organizers may act, nobody else may do anything.
        board.gameNumbers[resetTime] = 120;
        CheckMayAct("Lena", false, true, true, s + ": locked, Lena may act");
        CheckMayAct("Olaf", true, true, true, s + ": locked, an organizer may act");
        CheckMayAct("Cleo", false, false, true, s + ": locked, Cleo may not act");
        Check(board.CanJoinAs(ChessRules.White, "Lena") && board.CanJoinAs(ChessRules.Black, "Lena"),
            s + ": locked, Lena may join", "refused");
        Check(!board.CanJoinAs(ChessRules.White, "Cleo") && !board.CanJoinAs(ChessRules.Black, "Cleo"),
            s + ": locked, Cleo may not join", "allowed");
        Check(!board.CanJoinAs(ChessRules.White, "Olaf"), s + ": locked, organizers don't join for the loader",
            "allowed");
        string locked = GameText();
        CheckInt(board.StepToAs("Cleo", false, 0), ChessBoard.InputRejected, s + ": locked, Cleo's step refused");
        CheckInt(board._ReplayPieceDroppedAs("Cleo", false, ReplaySquare(true), ReplaySquare(false)),
            ChessBoard.InputRejected, s + ": locked, Cleo's free move refused");
        CheckInt(board.JoinAs(ChessRules.White, "Cleo"), ChessBoard.InputRejected, s + ": locked, Cleo's join refused");
        CheckInt(board.ResetBoardAs("Cleo", false), ChessBoard.InputRejected, s + ": locked, Cleo's reset refused");
        Check(!board._TouchAsAt("Cleo", false, board.lastActivity + 100), s + ": locked, Cleo's touch doesn't count",
            "counted");
        CheckString(GameText(), locked, s + ": locked, Cleo's refused inputs change nothing");
        CheckString(board.loadedBy, "Lena", s + ": locked, still Lena's");
        CheckInt(board.StepToAs("Olaf", true, 10), ChessBoard.InputAccepted, s + ": locked, an organizer steps");
        CheckInt(board.StepToAs("Lena", false, 20), ChessBoard.InputAccepted, s + ": locked, Lena steps");
        CheckInt(board._ReplayPieceDroppedAs("Lena", false, ReplaySquare(true), ReplaySquare(false)),
            ChessBoard.InputAccepted, s + ": locked, Lena's free move");
        CheckInt(board.StepToAs("Lena", false, 20), ChessBoard.InputAccepted, s + ": locked, Lena steps back");

        // Leaving the instance doesn't free a locked board: only the reset-available time does (or never).
        double t = board.lastActivity;
        Check(!board._MayOwnAs("Cleo", false, false, t), s + ": locked, Lena away, Cleo may not own it", "allowed");
        Check(!board._MayOwnAs("Cleo", false, false, t + 119.9), 
            s + ": locked, refused before the reset-available time", "allowed");
        Check(board._MayOwnAs("Cleo", false, false, t + 120), s + ": locked, anyone once idle for reset", "refused");
        Check(board._MayOwnAs("Olaf", true, false, t), s + ": locked, an organizer may own it", "refused");
        Check(board._MayOwnAs("Lena", false, false, t), s + ": locked, Lena may own it", "refused");
        board.gameNumbers[resetTime] = ChessArbiter.Never;
        Check(!board._MayOwnAs("Cleo", false, false, t + 10000000), s + ": locked, never: refused however long idle",
            "allowed");
        board.gameNumbers[resetTime] = 120;

        // Lena joins: anyone may join the other seat, and the seated players act as on any loaded board.
        CheckInt(board.JoinAs(ChessRules.White, "Lena"), ChessBoard.InputAccepted, s + ": Lena joins White");
        Check(!board.IsLockedToLoader(), s + ": Lena seated, not locked", "locked");
        CheckString(board.loadedBy, "Lena", s + ": Lena seated, still recorded");
        CheckInt(board.JoinAs(ChessRules.Black, "Cleo"), ChessBoard.InputAccepted, s + ": Cleo joins Black");
        CheckMayAct("Cleo", false, true, true, s + ": Cleo seated, may act");
        Check(board.CanSetClockAs("Cleo"), s + ": Cleo seated, may choose the clock", "refused");
        CheckMayAct("Dana", false, false, true, s + ": both seated, a bystander may not act");

        // Lena leaves her seat: the lock is back, and Cleo, still seated, may only leave.
        CheckInt(board.LeaveSeatAs(ChessRules.White, "Lena"), ChessBoard.InputAccepted, s + ": Lena leaves White");
        Check(board.IsLockedToLoader(), s + ": Lena left, locked again", "not locked");
        CheckString(board.loadedBy, "Lena", s + ": Lena left, still hers");
        CheckMayAct("Cleo", false, false, true, s + ": locked again, seated Cleo may not act");
        Check(!board.CanSetClockAs("Cleo"), s + ": locked again, Cleo may not choose the clock", "allowed");
        Check(!board.CanJoinAs(ChessRules.White, "Cleo"), s + ": locked again, Cleo may not join White", "allowed");
        Check(!board.CanContinueAs("Cleo"), s + ": locked again, Cleo may not continue", "allowed");
        CheckMayAct("Lena", false, true, true, s + ": locked again, Lena may act");
        Check(board.CanLeaveSeatAs(ChessRules.Black, "Cleo"), s + ": locked again, Cleo may leave", "refused");
        CheckInt(board.LeaveSeatAs(ChessRules.Black, "Cleo"), ChessBoard.InputAccepted, s + ": Cleo leaves Black");

        // Both seats empty: still loaded, still Lena's.
        CheckSeats("", "", s + ": both seats emptied");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, s + ": both seats emptied, still loaded");
        CheckString(board.loadedBy, "Lena", s + ": both seats emptied, still Lena's");
        Check(board.IsLockedToLoader(), s + ": both seats emptied, locked", "not locked");

        // Continue from here clears it: the game belongs to its players.
        board.JoinAs(ChessRules.White, "Lena");
        board.JoinAs(ChessRules.Black, "Cleo");
        CheckInt(board.ContinueGameAs("Cleo"), ChessBoard.InputAccepted, s + ": continued");
        CheckString(board.loadedBy, "", s + ": continued, cleared");
        Check(!board.IsLockedToLoader(), s + ": continued, not locked", "locked");

        // A reset clears it.
        LoadNukaAs("Lena", s + ": reset");
        CheckInt(board.ResetBoardAs("Lena", false), ChessBoard.InputAccepted, s + ": Lena resets");
        CheckString(board.loadedBy, "", s + ": reset, cleared");

        // Anyone may reset a locked board once it has been idle for the reset-available time.
        LoadNukaAs("Lena", s + ": idle");
        board.gameNumbers[resetTime] = 120;
        Check(!board.CanResetAs("Cleo", false), s + ": not idle yet, Cleo may not reset", "allowed");
        board.lastActivity = board._ServerTime() - 121;
        Check(board.CanResetAs("Cleo", false), s + ": idle, Cleo may reset", "refused");
        CheckInt(board.ResetBoardAs("Cleo", false), ChessBoard.InputAccepted, s + ": idle, Cleo resets");
        CheckString(board.loadedBy, "", s + ": idle reset, cleared");
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, s + ": idle reset, reset");
        board._ClearState();
    }

    void TestContinueLoaded() {
        if (!Utilities.IsValid(importer) || siggiVsNuka == null) return;
        NothingForced();
        string nuka = siggiVsNuka.text;
        int takebacks = ChessArbiter.SettingTakebacks;

        // Siggi-vs-Nuka continued from ply 20 (White to move) with a 300+3 clock: the seated players, fresh clocks,
        // a new ID referencing the PGN's at ply 20. Nothing is logged.
        LoadText(nuka, "continue");
        int[] nukaMoves = new int[54];
        System.Array.Copy(board.moveHistory, nukaMoves, 54);
        string[] nukaSans = new string[54];
        System.Array.Copy(board.notation, nukaSans, 54);
        int[] nukaHalfmove = new int[54];
        System.Array.Copy(board.halfmoveClocks, nukaHalfmove, 54);
        board.StepToAs("Cleo", false, 20);
        CheckInt(board.ContinueGameAs("Cleo"), ChessBoard.InputRejected, "continue: refused with nobody seated");
        board.JoinAs(ChessRules.White, "Ana");
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputRejected, "continue: refused with one seat");
        board.JoinAs(ChessRules.Black, "Bob");
        CheckInt(board.SetClockAs("Ana", 300, 3), ChessBoard.InputAccepted, "continue: clock chosen");
        CheckInt(board.ContinueGameAs("Cleo"), ChessBoard.InputRejected, "continue: refused for a non-player");
        board._ReplayPieceDroppedAs("Ana", false, ChessRules.A1, ChessRules.A3);
        string custom = GameText();
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputRejected, "continue: refused from a custom position");
        CheckString(GameText(), custom, "continue: refused, nothing changed");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "continue: refused, still loaded");
        board.StepToAs("Ana", false, 20);
        int logged = board.loggedGames;
        double before = board._ServerTime();
        CheckInt(board.ContinueGameAs("Bob"), ChessBoard.InputAccepted, "continue: from ply 20 by Black");
        double after = board._ServerTime();
        string fen20 = FenAfter(ChessFen.StartFen, nukaMoves, 20);
        CheckContinuedLoaded(20, fen20, 300000, "continue: ply 20");
        CheckInt(board.state[ChessPosition.StateSideToMove], ChessRules.White, "continue: ply 20, White to move");
        CheckString(board.notation[19], nukaSans[19], "continue: ply 20, the last SAN kept");
        CheckInt(board.halfmoveClocks[19], nukaHalfmove[19], "continue: ply 20, the halfmove clocks kept");
        CheckInt(board.baseTime * 1000 + board.increment, 300003, "continue: the chosen clock");
        Check(board.turnStart >= before && board.turnStart <= after, "continue: the turn starts now",
            board.turnStart + " not between " + before + " and " + after);
        Check(board.IsClockRunning(), "continue: the clock runs", "not running");
        CheckGameId(board.gameId, "continue: a new Game ID");
        CheckString(board.continuedFrom, "20250710001921/20", "continue: ContinuedFrom the PGN's ID at ply 20");
        CheckLogged(logged, 0, "continue: nothing logged");
        CheckInt(board.mode, ChessGame.ModeStandard, "continue: the game's mode");
        CheckPgnHas("[White \"Ana\"]", true, "continue: export White is the seat");
        CheckPgnHas("[Black \"Bob\"]", true, "continue: export Black is the seat");
        CheckPgnHas("[TimeControl \"300+3\"]", true, "continue: export the chosen clock");
        CheckPgnHas("[Result \"*\"]", true, "continue: export no result");
        CheckPgnHas("[ContinuedFrom \"20250710001921/20\"]", true, "continue: export ContinuedFrom");
        Check(!board.GamePgn().Contains("%clk"), "continue: the loaded clocks not exported", board.GamePgn());
        Check(!board.CanContinueAs("Ana"), "continue: not again in game", "allowed");
        CheckInt(board.JoinAs(ChessRules.White, "Cleo"), ChessBoard.InputRejected, "continue: seats closed");

        // Played on, then taken back: to ply 21 (White's clock from its move after continuing, Black's base time),
        // and past the continuation point to ply 18 (both the base time).
        board.gameSettings = takebacks;
        int[] legal = new int[ChessMoves.MaxMoves];
        ChessMoves.GenerateLegalMoves(board.board, board.state, legal, true);
        CheckInt(board._PlayMove(legal[0]), ChessBoard.InputAccepted, "continue: White plays on");
        ChessMoves.GenerateLegalMoves(board.board, board.state, legal, true);
        CheckInt(board._PlayMove(legal[0]), ChessBoard.InputAccepted, "continue: Black plays on");
        int whiteAfter = board.clockHistory[20];
        Check(whiteAfter > 300000 && whiteAfter <= 303000, "continue: White's clock after its move",
            "" + whiteAfter);
        string continuedId = board.gameId;
        Takeback(21, ChessRules.Black, "continue: takeback to ply 21");
        CheckInt(board.whiteTime, whiteAfter, "continue: takeback to ply 21, White's clock from its move");
        CheckInt(board.blackTime, 300000, "continue: takeback to ply 21, Black's base time");
        CheckString(board.continuedFrom, continuedId + "/21", "continue: takeback to ply 21, ContinuedFrom");
        Takeback(18, ChessRules.White, "continue: takeback to ply 18");
        CheckString(Fen(), FenAfter(ChessFen.StartFen, nukaMoves, 18), "continue: takeback to ply 18, the position");
        CheckTimes(300000, 300000, board.turnStart, "continue: takeback to ply 18, both the base time");
        logged = board.loggedGames;
        board.ResignAs(ChessRules.White, "Ana");
        CheckLogged(logged, 1, "continue: the continued game logged when it ends");
        CheckLogHas("[White \"Ana\"]\n", "continue: logged with the seat's name");
        Check(board.lastLoggedPgn.Contains("1. e4 c5 2. Nf3") && !board.lastLoggedPgn.Contains("%clk"),
            "continue: logged with the moves before, no clocks", board.lastLoggedPgn);

        // From ply 21: Black to move, with the move number.
        LoadSeated(nuka, 21, "continue ply 21");
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputAccepted, "continue ply 21: accepted");
        CheckContinuedLoaded(21, FenAfter(ChessFen.StartFen, nukaMoves, 21), 600000, "continue ply 21");
        CheckInt(board.state[ChessPosition.StateSideToMove], ChessRules.Black, "continue ply 21: Black to move");
        CheckInt(board.moveNumber, 11, "continue ply 21: move 11");
        CheckString(board.continuedFrom, "20250710001921/21", "continue ply 21: ContinuedFrom");

        // From the start: ply 0, nothing kept.
        LoadSeated(nuka, 0, "continue ply 0");
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputAccepted, "continue ply 0: accepted");
        CheckContinuedLoaded(0, ChessFen.StartFen, 600000, "continue ply 0");
        CheckString(board.continuedFrom, "20250710001921/0", "continue ply 0: ContinuedFrom");

        // Not from the last position, which is checkmate: nothing changes.
        LoadSeated(nuka, 54, "continue mate");
        string mate = GameText();
        Check(!board.CanContinueAs("Ana"), "continue mate: no button on the checkmate", "allowed");
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputRejected, "continue mate: refused");
        CheckString(GameText(), mate, "continue mate: nothing changed");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "continue mate: still loaded");
        board.StepToAs("Ana", false, 53);
        Check(board.CanContinueAs("Ana"), "continue mate: from the ply before", "refused");

        // A game without a GameID: no ContinuedFrom. From its last position, Black to move, with no clock.
        LoadSeated("1. e4 e5 2. Nf3 *", 3, "continue no ID");
        CheckInt(board.ContinueGameAs("Bob"), ChessBoard.InputAccepted, "continue no ID: accepted");
        CheckContinuedLoaded(3, "rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2", 0,
            "continue no ID");
        CheckString(board.continuedFrom, "", "continue no ID: no ContinuedFrom");
        CheckGameId(board.gameId, "continue no ID: a new Game ID");
        CheckPgnHas("[TimeControl \"-\"]", true, "continue no ID: no clock");
        Check(!board.GamePgn().Contains("ContinuedFrom"), "continue no ID: no ContinuedFrom tag", board.GamePgn());
        Check(!board.IsClockRunning(), "continue no ID: no clock running", "running");
        Drop("Bob", ChessRules.B8, ChessRules.C6, "continue no ID: Black plays on");

        // Chess960 keeps its mode and starting position.
        string chess960 = "[Variant \"Chess960\"]\n[SetUp \"1\"]\n"
            + "[FEN \"qrkbbnrn/pppppppp/8/8/8/8/PPPPPPPP/QRKBBNRN w GBgb - 0 1\"]\n\n1. e4 e5 2. Bf3 *";
        LoadSeated(chess960, 2, "continue Chess960");
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputAccepted, "continue Chess960: accepted");
        CheckInt(board.mode, ChessGame.ModeChess960, "continue Chess960: Chess960");
        CheckString(board.startFen, "qrkbbnrn/pppppppp/8/8/8/8/PPPPPPPP/QRKBBNRN w GBgb - 0 1",
            "continue Chess960: the starting position kept");
        CheckString(Fen(), "qrkbbnrn/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/QRKBBNRN w GBgb e6 0 2",
            "continue Chess960: the position");
        CheckPgnHas("[Variant \"Chess960\"]", true, "continue Chess960: Variant exported");

        // Positions already over: refused with their rule on, allowed with it off, and one ply earlier.
        LoadSeated("[SetUp \"1\"]\n[FEN \"8/8/4k3/8/8/4K3/3r4/8 w - - 0 1\"]\n\n1. Kxd2 *", 1, "continue dead");
        board.gameSettings = ChessArbiter.SettingDeadPosition;
        Check(!board.CanContinueAs("Ana"), "continue dead: refused with insufficient material", "allowed");
        board.StepToAs("Ana", false, 0);
        Check(board.CanContinueAs("Ana"), "continue dead: allowed before the capture", "refused");
        board.StepToAs("Ana", false, 1);
        board.gameSettings = 0;
        Check(board.CanContinueAs("Ana"), "continue dead: allowed with the rule off", "refused");
        string shuffle = "";
        for (int i = 0; i < 4; i++) shuffle += "Nf3 Nf6 Ng1 Ng8 ";
        LoadSeated(shuffle + "*", 16, "continue fivefold");
        board.gameSettings = ChessArbiter.SettingFivefoldRepetition;
        Check(!board.CanContinueAs("Ana"), "continue fivefold: refused on the fifth occurrence", "allowed");
        board.StepToAs("Ana", false, 15);
        Check(board.CanContinueAs("Ana"), "continue fivefold: allowed a ply before", "refused");
        board.StepToAs("Ana", false, 16);
        board.gameSettings = ChessArbiter.SettingThreefoldRepetition;
        Check(board.CanContinueAs("Ana"), "continue fivefold: allowed with only threefold on", "refused");
        LoadSeated("[SetUp \"1\"]\n[FEN \"k7/8/8/8/8/8/8/K6R w - - 149 80\"]\n\n80. Rh2 *", 1, "continue 75-move");
        board.gameSettings = ChessArbiter.SettingSeventyFiveMoveRule;
        Check(!board.CanContinueAs("Ana"), "continue 75-move: refused at 150 halfmoves", "allowed");
        board.StepToAs("Ana", false, 0);
        Check(board.CanContinueAs("Ana"), "continue 75-move: allowed at 149 halfmoves", "refused");
        board.StepToAs("Ana", false, 1);
        board.gameSettings = ChessArbiter.SettingFiftyMoveRule;
        Check(board.CanContinueAs("Ana"), "continue 75-move: allowed with only the fifty-move claim on", "refused");
        LoadSeated("[SetUp \"1\"]\n[FEN \"7k/5Q2/6K1/8/8/8/8/8 b - - 0 1\"]\n\n*", 0, "continue stalemate");
        Check(!board.CanContinueAs("Ana"), "continue stalemate: refused", "allowed");

        // A forced clock replaces the players' choice and the game's own; a forced mode doesn't change the game's.
        if (Utilities.IsValid(board.arbiter)) {
            OverrideAll(ChessArbiter.SettingModeForced | ChessArbiter.SettingClockForced, 30, 300,
                ChessGame.ModeStandard, 120, 1);
            LoadSeated("[TimeControl \"600\"]\n" + chess960, 1, "continue forced");
            CheckInt(board.SetClockAs("Ana", 60, 0), ChessBoard.InputRejected, "continue forced: no choosing");
            CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputAccepted, "continue forced: accepted");
            CheckInt(board.baseTime * 1000 + board.increment, 120001, "continue forced: the forced clock");
            CheckTimes(120000, 120000, board.turnStart, "continue forced: fresh forced clocks");
            CheckInt(board.mode, ChessGame.ModeChess960, "continue forced: the game's mode");
            CheckPgnHas("[TimeControl \"120+1\"]", true, "continue forced: the forced clock exported");
            NothingForced();
        }

        // The local player's method, seated in both seats.
        LoadText(nuka, "continue local");
        board.JoinWhite();
        board.JoinBlack();
        board.StepTo(10);
        Check(board.CanContinue(), "continue local: the button applies", "refused");
        CheckInt(board.ContinueGame(), ChessBoard.InputAccepted, "continue local: accepted");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, "continue local: in game");
        CheckString(board.whitePlayer + "|" + board.blackPlayer, localName + "|" + localName,
            "continue local: the local player in both seats");

        board._ClearState();
    }

    // The standard starting position's squares.
    byte[] StartBoard() {
        byte[] squares = new byte[64];
        int[] scratchState = new int[ChessPosition.StateLength];
        int clock;
        int number;
        ChessFen.ParseFen(ChessFen.StartFen, squares, scratchState, out clock, out number);
        return squares;
    }

    // ---- 7.7 PGN logging ----

    // The board has logged expected games since its count was before.
    void CheckLogged(int before, int expected, string name) {
        CheckInt(board.loggedGames - before, expected, name);
    }

    void CheckLogHas(string text, string name) {
        Check(board.lastLoggedPgn.Contains(text), name, "\"" + text + "\" not in:\n" + board.lastLoggedPgn);
    }

    // One game logged since before, with this Result and Termination, the result ending the move text, and the
    // logged PGN the board's own.
    void CheckLoggedEnding(int before, string result, string termination, string name) {
        CheckLogged(before, 1, name + ": logged once");
        CheckLogHas("[Result \"" + result + "\"]\n", name + ": Result tag");
        CheckLogHas("[Termination \"" + termination + "\"]\n", name + ": Termination tag");
        Check(board.lastLoggedPgn.EndsWith(" " + result + "\n") || board.lastLoggedPgn.EndsWith("\n" + result + "\n"),
            name + ": the move text ends with the result", "PGN:\n" + board.lastLoggedPgn);
        CheckString(board.lastLoggedPgn, board.GamePgn(), name + ": the logged PGN is the board's");
    }

    void TestPgnLog() {
        NothingForced();
        string savedEvent = board.pgnEvent;
        string savedSite = board.pgnSite;
        board.pgnEvent = "Test Event";
        board.pgnSite = "Test Site";
        int dead = ChessArbiter.SettingDeadPosition;

        CheckString(ChessGame._DateFromGameId("20261005143027-048213"), "2026.10.05", "PGN date: from a game ID");
        CheckString(ChessGame._DateFromGameId("h0rrawli"), "", "PGN date: none from a foreign ID");
        CheckString(ChessGame._DateFromGameId("202610051430270048213"), "", "PGN date: none without the dash");
        CheckString(ChessGame._DateFromGameId("2026100514302a-048213"), "", "PGN date: none with a letter");
        CheckString(ChessGame._DateFromGameId(""), "", "PGN date: none from no ID");
        CheckString(ChessGame._DateFromGameId(null), "", "PGN date: none from null");

        // Fool's mate with 60+2: the whole PGN, logged once.
        int before = board.loggedGames;
        StartedGame(60, 2, 1000);
        string id = board.gameId;
        Play(ChessRules.F2, ChessRules.F3, 1001, "log: 1. f3");
        Play(ChessRules.E7, ChessRules.E5, 1002, "log: 1... e5");
        Play(ChessRules.G2, ChessRules.G4, 1003, "log: 2. g4");
        CheckLogged(before, 0, "log: nothing while in game");
        Play(ChessRules.D8, ChessRules.H4, 1004.5, "log: 2... Qh4#");
        CheckLogged(before, 1, "log: fool's mate logged once");
        string date = id.Length >= 8 ? id.Substring(0, 4) + "." + id.Substring(4, 2) + "." + id.Substring(6, 2) : "?";
        string expected = "[Event \"Test Event\"]\n[Site \"Test Site\"]\n[Date \"" + date + "\"]\n[Round \"-\"]\n"
            + "[White \"Ana\"]\n[Black \"Bob\"]\n[Result \"0-1\"]\n[TimeControl \"60+2\"]\n[GameID \"" + id + "\"]\n"
            + "[Termination \"checkmate\"]\n\n"
            + "1. f3 { [%clk 0:01:01] } 1... e5 { [%clk 0:01:01] } 2. g4 { [%clk 0:01:02] }\n"
            + "2... Qh4# { [%clk 0:01:01.5] } 0-1\n";
        CheckString(board.lastLoggedPgn, expected, "log: fool's mate PGN");
        CheckString(board.GamePgn(), expected, "log: GamePgn gives it too");
        board._CheckFlagFallAt(1e6);
        board.ResignAs(ChessRules.White, "Ana");
        board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.F2);
        CheckLogged(before, 1, "log: nothing more once ended");

        // Games with no moves aren't logged, however they end.
        before = board.loggedGames;
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.ResignAs(ChessRules.White, "Ana");
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, "no moves: resigned");
        StartedGame(60, 0, 1000);
        Check(board._CheckFlagFallAt(1060), "no moves: flag fall", "not ended");
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = ChessArbiter.SettingDrawAgreement;
        board.OfferDrawAs(ChessRules.White, "Ana");
        CheckInt(board.AcceptDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, "no moves: draw agreed");
        LobbyWithTwoPlayers();
        board._ClearState();
        CheckLogged(before, 0, "no moves: nothing logged");

        // Resignation, with no clock and a ContinuedFrom.
        before = board.loggedGames;
        ButtonGame(0);
        board.continuedFrom = "20261011120000-000001/4";
        board.ResignAs(ChessRules.White, "Ana");
        CheckLoggedEnding(before, "0-1", "resignation", "log: resignation");
        CheckLogHas("[TimeControl \"-\"]\n", "log: resignation: no clock");
        CheckLogHas("[ContinuedFrom \"20261011120000-000001/4\"]\n", "log: resignation: ContinuedFrom");
        Check(board.lastLoggedPgn.EndsWith("\n\n1. Nf3 Nf6 2. Ng1 Ng8 0-1\n"), "log: resignation: move text",
            "PGN:\n" + board.lastLoggedPgn);
        Check(!board.lastLoggedPgn.Contains("[%clk"), "log: resignation: no clock comments", "has [%clk");

        // Time forfeit: a loss, and a draw when the opponent can't mate.
        before = board.loggedGames;
        StartedGame(60, 0, 1000);
        Play(ChessRules.E2, ChessRules.E4, 1001, "log: forfeit: 1. e4");
        Check(board._CheckFlagFallAt(1100), "log: forfeit: black's flag", "not ended");
        CheckLoggedEnding(before, "1-0", "time forfeit", "log: time forfeit");
        CheckLogHas("1. e4 { [%clk 0:00:59] } 1-0\n", "log: time forfeit: move text");

        before = board.loggedGames;
        StartedGame(60, 0, 1000);
        board.gameSettings = dead;
        SetPosition("k7/p7/8/8/8/8/8/K7 w - - 0 1");
        Play(ChessRules.A1, ChessRules.A2, 1001, "log: forfeit draw: Ka2");
        Check(board._CheckFlagFallAt(1100), "log: forfeit draw: black's flag", "not ended");
        CheckLoggedEnding(before, "1/2-1/2", "time forfeit", "log: time forfeit drawn");

        // Stalemate and every draw.
        before = board.loggedGames;
        StartedGame(ChessArbiter.NoClock, 0, 0);
        SetPosition("7k/8/8/5Q2/8/8/8/K7 w - - 0 1");
        Drop("Ana", ChessRules.F5, ChessRules.G6, "log: stalemate");
        CheckLoggedEnding(before, "1/2-1/2", "stalemate", "log: stalemate");

        before = board.loggedGames;
        ButtonGame(ChessArbiter.SettingDrawAgreement);
        board.OfferDrawAs(ChessRules.White, "Ana");
        board.AcceptDrawAs(ChessRules.Black, "Bob");
        CheckLoggedEnding(before, "1/2-1/2", "draw agreement", "log: draw agreement");

        before = board.loggedGames;
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = ChessArbiter.SettingThreefoldRepetition;
        KnightShuffle(2, "log: threefold");
        CheckInt(board.ClaimDrawAs("Ana"), ChessBoard.InputAccepted, "log: threefold claimed");
        CheckLoggedEnding(before, "1/2-1/2", "threefold repetition", "log: threefold repetition");

        before = board.loggedGames;
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = ChessArbiter.SettingFiftyMoveRule;
        SetPosition("k7/8/8/8/8/8/8/K6R w - - 99 80");
        Drop("Ana", ChessRules.H1, ChessRules.H2, "log: fifty-move");
        CheckInt(board.ClaimDrawAs("Bob"), ChessBoard.InputAccepted, "log: fifty-move claimed");
        CheckLoggedEnding(before, "1/2-1/2", "fifty-move rule", "log: fifty-move rule");

        before = board.loggedGames;
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = ChessArbiter.SettingFivefoldRepetition;
        KnightShuffle(4, "log: fivefold");
        CheckLoggedEnding(before, "1/2-1/2", "fivefold repetition", "log: fivefold repetition");

        before = board.loggedGames;
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = ChessArbiter.SettingSeventyFiveMoveRule;
        SetPosition("k7/8/8/8/8/8/8/K6R w - - 149 80");
        Drop("Ana", ChessRules.H1, ChessRules.H2, "log: seventy-five-move");
        CheckLoggedEnding(before, "1/2-1/2", "seventy-five-move rule", "log: seventy-five-move rule");

        before = board.loggedGames;
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.gameSettings = dead;
        SetPosition("8/8/4k3/8/8/4K3/3r4/8 w - - 0 1");
        Drop("Ana", ChessRules.E3, ChessRules.D2, "log: dead position");
        CheckLoggedEnding(before, "1/2-1/2", "insufficient material", "log: insufficient material");

        // Chess960: Variant, SetUp and the starting FEN.
        before = board.loggedGames;
        LobbyWithTwoPlayers();
        board.SetModeAs("Ana", ChessGame.ModeChess960);
        board.StartGameAs("Ana");
        int[] moves = new int[ChessMoves.MaxMoves];
        ChessMoves.GenerateLegalMoves(board.board, board.state, moves, true);
        CheckInt(board._PlayMove(moves[0]), ChessBoard.InputAccepted, "log: Chess960: a move");
        board.ResignAs(ChessRules.Black, "Bob");
        CheckLoggedEnding(before, "1-0", "resignation", "log: Chess960");
        CheckLogHas("[Variant \"Chess960\"]\n[SetUp \"1\"]\n[FEN \"" + board.startFen + "\"]\n",
            "log: Chess960: Variant, SetUp and FEN");

        // A game starting with Black to move at move 30, and a name to escape.
        before = board.loggedGames;
        StartedGame(ChessArbiter.NoClock, 0, 0);
        board.blackPlayer = "Nu\"ka";
        string fen = "4k3/4p3/8/8/8/8/4P3/4K3 b - - 0 30";
        SetPosition(fen);
        board.startFen = fen;
        board.startMoveNumber = 30;
        Drop("Nu\"ka", ChessRules.E7, ChessRules.E5, "log: black first");
        Drop("Ana", ChessRules.E2, ChessRules.E4, "log: black first");
        board.ResignAs(ChessRules.White, "Ana");
        CheckLoggedEnding(before, "0-1", "resignation", "log: black first");
        CheckLogHas("[Black \"Nu\\\"ka\"]\n", "log: black first: name escaped");
        CheckLogHas("[SetUp \"1\"]\n[FEN \"" + fen + "\"]\n", "log: black first: SetUp and FEN");
        Check(board.lastLoggedPgn.EndsWith("\n\n30... e5 31. e4 0-1\n"), "log: black first: numbered from 30...",
            "PGN:\n" + board.lastLoggedPgn);

        board.pgnEvent = savedEvent;
        board.pgnSite = savedSite;
        board._ClearState();
    }

    // ---- 7.10 reset ----

    // The board cleared as _ClearState leaves it, seats included.
    void CheckCleared(string name) {
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, name + ": reset");
        CheckSeats("", "", name);
        CheckString(Fen(), ChessFen.StartFen, name + ": the standard start");
        CheckString(board.startFen, ChessFen.StartFen, name + ": the starting FEN");
        CheckInt(board.moveCount, 0, name + ": no moves");
        string errors = "";
        for (int i = 0; i < board.moveHistory.Length; i++) {
            if (board.moveHistory[i] != 0 || board.notation[i] != "" || board.clockHistory[i] != -1) {
                errors += " ply " + (i + 1);
            }
        }
        Check(errors == "", name + ": the history emptied", errors);
        CheckInt(board.ending * 10 + board.winner, ChessGame.EndingNone * 10 + ChessRules.NoColor,
            name + ": no result");
        CheckString(board.gameId + "|" + board.continuedFrom, "|", name + ": no game ID");
        CheckString(board.replaySourceWhite + "|" + board.replaySourceBlack + "|" + board.replaySourceResult, "||",
            name + ": no loaded tags");
        Check(!board.replayMode && !board.replayCustom && board.replayIndex == 0, name + ": not in replay",
            "replay fields set");
        CheckInt(board.takebackColor * 10 + board.drawOfferColor, ChessRules.NoColor * 11, name + ": nothing pending");
        CheckInt(board.mode, ChessGame.ModeStandard, name + ": Standard mode");
        CheckInt(board.baseTime * 1000 + board.increment, ChessArbiter.NoClock * 1000, name + ": no clock");
        CheckInt(board.gameSettings, 0, name + ": no snapshot");
        CheckInt(board.whitePieceSet * 10 + board.blackPieceSet, 0, name + ": both piece sets 0");
        Check(board.CanLoadGame(), name + ": a game can be loaded", "refused");
    }

    // One game logged since before, with this Result and Termination and the result ending the move text (the board
    // is cleared by then, so unlike CheckLoggedEnding there's no PGN of its own to compare with).
    void CheckLoggedOnReset(int before, string result, string termination, string name) {
        CheckLogged(before, 1, name + ": logged once");
        CheckLogHas("[Result \"" + result + "\"]\n", name + ": Result tag");
        CheckLogHas("[Termination \"" + termination + "\"]\n", name + ": Termination tag");
        Check(board.lastLoggedPgn.EndsWith(" " + result + "\n") || board.lastLoggedPgn.EndsWith("\n" + result + "\n"),
            name + ": the move text ends with the result", "PGN:\n" + board.lastLoggedPgn);
    }

    string SeatedGameText() {
        return board.whitePlayer + "|" + board.blackPlayer + "|" + GameText();
    }

    void TestReset() {
        NothingForced();
        int takebacks = ChessArbiter.SettingTakebacks;

        // Nothing to reset on a reset board.
        board._ClearState();
        CheckInt(board.ResetBoardAs("Cleo", true), ChessBoard.InputRejected, "reset: refused on a reset board");

        // The lobby: a seated player resets, nothing is logged; a bystander can't.
        int before = board.loggedGames;
        LobbyWithTwoPlayers();
        board.SetModeAs("Ana", ChessGame.ModeChess960);
        board.SetClockAs("Ana", 300, 2);
        string seen = SeatedGameText();
        CheckInt(board.ResetBoardAs("Cleo", false), ChessBoard.InputRejected, "reset: refused for a bystander");
        CheckString(SeatedGameText(), seen, "reset: refused, nothing changed");
        CheckInt(board.ResetBoardAs("", false), ChessBoard.InputRejected, "reset: refused for an empty name");
        CheckInt(board.ResetBoardAs("Bob", false), ChessBoard.InputAccepted, "reset: the lobby by a player");
        CheckCleared("reset: the lobby");
        CheckLogged(before, 0, "reset: the lobby: nothing logged");
        CheckInt(board.JoinAs(ChessRules.Black, "Cleo"), ChessBoard.InputAccepted, "reset: the seats free again");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "reset: a join enters the lobby again");

        // A game in progress with moves: logged once, unfinished, with its whole history, then cleared.
        before = board.loggedGames;
        ButtonGame(0);
        string id = board.gameId;
        CheckInt(board.ResetBoardAs("Cleo", false), ChessBoard.InputRejected, "reset: refused for a bystander in game");
        CheckLogged(before, 0, "reset: refused, nothing logged");
        CheckInt(board.ResetBoardAs("Bob", false), ChessBoard.InputAccepted, "reset: in game by a player");
        CheckLoggedOnReset(before, "*", "unterminated", "reset: in game");
        CheckLogHas("[White \"Ana\"]\n[Black \"Bob\"]\n", "reset: in game: logged with the seats");
        CheckLogHas("[GameID \"" + id + "\"]\n", "reset: in game: logged with its ID");
        Check(board.lastLoggedPgn.EndsWith("\n\n1. Nf3 Nf6 2. Ng1 Ng8 *\n"), "reset: in game: the whole history",
            "PGN:\n" + board.lastLoggedPgn);
        CheckCleared("reset: in game");

        // By an organizer in game, with a clock; and in replay with a takeback request pending.
        before = board.loggedGames;
        StartedGame(60, 0, board._ServerTime());
        Play(ChessRules.E2, ChessRules.E4, board._ServerTime(), "reset: organizer: 1. e4");
        CheckInt(board.ResetBoardAs("Cleo", true), ChessBoard.InputAccepted, "reset: in game by an organizer");
        CheckLoggedOnReset(before, "*", "unterminated", "reset: by an organizer");
        CheckLogHas("[TimeControl \"60\"]\n", "reset: by an organizer: the clock");
        CheckLogHas("1. e4 { [%clk ", "reset: by an organizer: the move's clock");
        CheckCleared("reset: by an organizer");

        before = board.loggedGames;
        ButtonReplay(takebacks, 2);
        CheckInt(board.RequestTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted,
            "reset: in replay: takeback requested");
        CheckInt(board.ResetBoardAs("Ana", false), ChessBoard.InputAccepted, "reset: in replay");
        CheckLoggedOnReset(before, "*", "unterminated", "reset: in replay");
        Check(board.lastLoggedPgn.EndsWith("\n\n1. Nf3 Nf6 2. Ng1 Ng8 *\n"),
            "reset: in replay: the game logged, not the replay", "PGN:\n" + board.lastLoggedPgn);
        CheckCleared("reset: in replay");

        // A game in progress with no moves isn't logged.
        before = board.loggedGames;
        StartedGame(ChessArbiter.NoClock, 0, 0);
        CheckInt(board.ResetBoardAs("Ana", false), ChessBoard.InputAccepted, "reset: no moves");
        CheckLogged(before, 0, "reset: no moves: nothing logged");
        CheckCleared("reset: no moves");

        // An ended game was logged when it ended, and isn't logged again.
        before = board.loggedGames;
        ButtonGame(0);
        board.ResignAs(ChessRules.White, "Ana");
        CheckLogged(before, 1, "reset: ended: logged on resigning");
        CheckInt(board.ResetBoardAs("Cleo", false), ChessBoard.InputRejected, "reset: ended: refused for a bystander");
        CheckInt(board.ResetBoardAs("Ana", false), ChessBoard.InputAccepted, "reset: ended by the loser");
        CheckLogged(before, 1, "reset: ended: not logged again");
        CheckCleared("reset: ended");

        // A fallen flag the board hasn't acted on yet: the game is over on time, not unfinished.
        before = board.loggedGames;
        ButtonGame(0);
        FlagFallen();
        CheckInt(board.ResetBoardAs("Ana", false), ChessBoard.InputAccepted, "reset: flag fallen");
        CheckLoggedOnReset(before, "0-1", "time forfeit", "reset: flag fallen");
        CheckCleared("reset: flag fallen");

        // The local player's button.
        before = board.loggedGames;
        board._ClearState();
        board.JoinWhite();
        board.JoinBlack();
        board.StartGame();
        board.PieceDropped(ChessRules.E2, ChessRules.E4);
        Check(board.CanReset(), "reset: the local player may", "refused");
        CheckInt(board.ResetBoard(), ChessBoard.InputAccepted, "reset: the local player");
        CheckLoggedOnReset(before, "*", "unterminated", "reset: the local player");
        CheckCleared("reset: the local player");

        // A loaded game wasn't played here: never logged. With nobody seated, only an organizer may reset it.
        if (!Utilities.IsValid(importer) || siggiVsNuka == null) return;
        before = board.loggedGames;
        LoadText(siggiVsNuka.text, "reset: loaded");
        seen = SeatedGameText();
        CheckInt(board.ResetBoardAs("Cleo", false), ChessBoard.InputRejected, "reset: loaded: refused for a bystander");
        CheckString(SeatedGameText(), seen, "reset: loaded: refused, nothing changed");
        CheckInt(board.ResetBoardAs("Cleo", true), ChessBoard.InputAccepted, "reset: loaded by an organizer");
        CheckLogged(before, 0, "reset: loaded: nothing logged");
        CheckCleared("reset: loaded");

        LoadSeated(siggiVsNuka.text, 20, "reset: loaded and seated");
        CheckInt(board.ResetBoardAs("Bob", false), ChessBoard.InputAccepted, "reset: loaded by a seated player");
        CheckLogged(before, 0, "reset: loaded and seated: nothing logged");
        CheckCleared("reset: loaded and seated");

        // Continued from a loaded game and played on: logged as unfinished, with ContinuedFrom.
        LoadSeated(siggiVsNuka.text, 20, "reset: continued");
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputAccepted, "reset: continued");
        string continuedFrom = board.continuedFrom;
        int[] moves = new int[ChessMoves.MaxMoves];
        ChessMoves.GenerateLegalMoves(board.board, board.state, moves, true);
        CheckInt(board._PlayMove(moves[0]), ChessBoard.InputAccepted, "reset: continued: a move");
        CheckInt(board.ResetBoardAs("Ana", false), ChessBoard.InputAccepted, "reset: continued: reset");
        CheckLoggedOnReset(before, "*", "unterminated", "reset: continued");
        CheckLogHas("[ContinuedFrom \"" + continuedFrom + "\"]\n", "reset: continued: ContinuedFrom");
        CheckCleared("reset: continued");
    }

    // ---- 7.11 ----

    // How many times the board has told this test of a change and the last change it told of (while TestListeners
    // has it listening), and the count at the last check of them.
    int notifications;
    int notifiedChange = -1;
    int notificationMark;

    public override void OnBoardChanged() {
        notifications++;
        notifiedChange = board.lastChange;
    }

    void MarkNotifications() {
        notificationMark = notifications;
    }

    // Since the last mark: told exactly once, of change.
    void CheckTold(int change, string name) {
        CheckInt(notifications - notificationMark, 1, name + ": told once");
        CheckInt(notifiedChange, change, name + ": the change");
        CheckInt(board.lastChange, change, name + ": lastChange");
        MarkNotifications();
    }

    // Since the last mark: told nothing.
    void CheckNotTold(string name) {
        CheckInt(notifications - notificationMark, 0, name + ": nobody told");
        MarkNotifications();
    }

    // Every square PieceAt shows has the piece of fen's placement.
    void CheckShown(string fen, string name) {
        byte[] squares = new byte[64];
        int[] scratchState = new int[ChessPosition.StateLength];
        int clock;
        int number;
        ChessFen.ParseFen(fen, squares, scratchState, out clock, out number);
        int wrong = -1;
        for (int i = 0; i < 64 && wrong < 0; i++) {
            if (board.PieceAt(i) != squares[i]) wrong = i;
        }
        Check(wrong < 0, name + ": PieceAt shows the position",
            wrong < 0 ? "" : "wrong piece on " + ChessRules.SquareName(wrong));
    }

    // LastMove is the move from-to (from -1: none, 0).
    void CheckLastMove(int from, int to, string name) {
        int move = board.LastMove();
        if (from < 0) {
            CheckInt(move, 0, name + ": no last move");
            return;
        }
        CheckInt((move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask, from, name + ": last move from");
        CheckInt((move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask, to, name + ": last move to");
    }

    void TestListeners() {
        NothingForced();
        board._ClearState();
        int listenerCount = board.listeners == null ? 0 : board.listeners.Length;
        board.AddListener(this);
        board.AddListener(this);
        board.AddListener(null);
        CheckInt(board.listeners.Length, listenerCount + 1, "listeners: added once");
        MarkNotifications();

        // The lobby.
        board.JoinAs(ChessRules.White, "Ana");
        CheckTold(ChessBoard.ChangeJoined, "listeners: join");
        board.JoinAs(ChessRules.White, "Ana");
        CheckNotTold("listeners: joining own seat again");
        board.JoinAs(ChessRules.White, "Bob");
        CheckNotTold("listeners: refused join");
        board.JoinAs(ChessRules.Black, "Bob");
        CheckTold(ChessBoard.ChangeJoined, "listeners: second join");
        board.SetModeAs("Ana", ChessGame.ModeStandard);
        CheckTold(ChessBoard.ChangeSettings, "listeners: mode");
        board.SetClockAs("Bob", 300, 2);
        CheckTold(ChessBoard.ChangeSettings, "listeners: clock");
        board.SetModeAs("Cleo", ChessGame.ModeChess960);
        CheckNotTold("listeners: refused mode");
        if (board.Resnapshot() == ChessBoard.InputAccepted) {
            CheckTold(ChessBoard.ChangeSettings, "listeners: re-snapshot");
        } else {
            CheckNotTold("listeners: refused re-snapshot");
        }
        board.gameSettings = ChessArbiter.SettingDrawAgreement | ChessArbiter.SettingTakebacks;
        board.StartGameAs("Ana");
        CheckTold(ChessBoard.ChangeStarted, "listeners: start");
        CheckShown(ChessFen.StartFen, "listeners: started");
        CheckLastMove(-1, -1, "listeners: started");
        CheckInt(board.SideToMove(), ChessRules.White, "listeners: started: white to move");
        Check(!board.IsInCheck(), "listeners: started: not in check", "in check");
        CheckString(board.ResultText(), "*", "listeners: started: result");
        int[] moves = new int[ChessMoves.MaxMoves];
        CheckInt(board.LegalMovesFrom(ChessRules.E2, moves), 2, "listeners: two moves from e2");
        CheckInt(board.LegalMovesFrom(ChessRules.G1, moves), 2, "listeners: two moves from g1");
        CheckInt((moves[0] >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask, ChessRules.G1,
            "listeners: moves from g1 are from g1");
        CheckInt(board.LegalMovesFrom(ChessRules.E7, moves), 0, "listeners: none from e7 on White's move");
        CheckInt(board.LegalMovesFrom(ChessRules.E4, moves), 0, "listeners: none from an empty square");
        CheckInt(board.LegalMovesFrom(64, moves), 0, "listeners: none off the board");
        CheckNotTold("listeners: getters");

        // Piece input: an accepted pickup changes nothing, each rejection is told once.
        board.PiecePickedUpAs("Ana", ChessRules.E2);
        CheckNotTold("listeners: pickup");
        board.PiecePickedUpAs("Bob", ChessRules.E7);
        CheckTold(ChessBoard.ChangeRejected, "listeners: rejected pickup");
        board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E5);
        CheckTold(ChessBoard.ChangeRejected, "listeners: illegal drop");
        board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E2);
        CheckTold(ChessBoard.ChangeRejected, "listeners: drop back");
        board.PromotionChosenAs("Ana", ChessRules.E2, ChessRules.E4, ChessRules.NoPieceType);
        CheckTold(ChessBoard.ChangeRejected, "listeners: promotion picker cancelled");
        board.PromotionChosenAs("Ana", ChessRules.E2, ChessRules.E4, ChessRules.Queen);
        CheckTold(ChessBoard.ChangeRejected, "listeners: not a promotion");
        board._PlayMove(0);
        CheckNotTold("listeners: refused PlayMove (not piece input)");

        // Moves.
        board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4);
        CheckTold(ChessBoard.ChangeMove, "listeners: e4");
        string afterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
        CheckShown(afterE4, "listeners: e4");
        CheckLastMove(ChessRules.E2, ChessRules.E4, "listeners: e4");
        CheckInt(board.SideToMove(), ChessRules.Black, "listeners: e4: black to move");
        CheckInt(board.LegalMovesFrom(ChessRules.E2, moves), 0, "listeners: e4: none from e2");
        board.PieceDroppedAs("Bob", ChessRules.F7, ChessRules.F5);
        CheckTold(ChessBoard.ChangeMove, "listeners: f5");
        board.PieceDroppedAs("Ana", ChessRules.D1, ChessRules.H5);
        CheckTold(ChessBoard.ChangeMove, "listeners: Qh5+");
        Check(board.IsInCheck(), "listeners: Qh5+: in check", "not in check");
        CheckInt(board.LegalMovesFrom(ChessRules.G7, moves), 1, "listeners: Qh5+: g6 blocks, g5 doesn't");
        board.PieceDroppedAs("Bob", ChessRules.G7, ChessRules.G6);
        CheckTold(ChessBoard.ChangeMove, "listeners: g6");
        Check(!board.IsInCheck(), "listeners: g6: not in check", "in check");
        string afterG6 = "rnbqkbnr/ppppp2p/6p1/5p1Q/4P3/8/PPPP1PPP/RNB1KBNR w KQkq - 0 3";
        CheckShown(afterG6, "listeners: g6");

        // Draw offers.
        board.OfferDrawAs(ChessRules.White, "Ana");
        CheckTold(ChessBoard.ChangeRequest, "listeners: draw offer");
        board.OfferDrawAs(ChessRules.Black, "Bob");
        CheckNotTold("listeners: refused second offer");
        board.DeclineDrawAs(ChessRules.Black, "Bob");
        CheckTold(ChessBoard.ChangeRequest, "listeners: draw declined");

        // Replay: the getters show the replay board; the side to move stays the game's.
        board.EnterReplayAs("Ana", false);
        CheckTold(ChessBoard.ChangeEnteredReplay, "listeners: enter replay");
        CheckShown(afterG6, "listeners: replay of the current position");
        CheckLastMove(ChessRules.G7, ChessRules.G6, "listeners: replay of the current position");
        CheckInt(board.LegalMovesFrom(ChessRules.G1, moves), 0, "listeners: replay: no legal moves");
        board.StepToAs("Ana", false, 1);
        CheckTold(ChessBoard.ChangeReplayStep, "listeners: step");
        CheckShown(afterE4, "listeners: step to 1");
        CheckLastMove(ChessRules.E2, ChessRules.E4, "listeners: step to 1");
        CheckInt(board.SideToMove(), ChessRules.White, "listeners: step to 1: the game's side to move");
        board.StepToAs("Ana", false, 1);
        CheckNotTold("listeners: refused step to the ply shown");
        board.StepToAs("Bob", false, 0);
        CheckTold(ChessBoard.ChangeReplayStep, "listeners: step to 0");
        CheckShown(ChessFen.StartFen, "listeners: step to 0");
        CheckLastMove(-1, -1, "listeners: step to 0");
        board.StepToAs("Bob", false, 1);
        CheckTold(ChessBoard.ChangeReplayStep, "listeners: step back to 1");

        // Free moves: a custom position has no last move.
        board.PiecePickedUpAs("Ana", ChessRules.B1);
        CheckNotTold("listeners: replay pickup");
        board.PiecePickedUpAs("Cleo", ChessRules.B1);
        CheckTold(ChessBoard.ChangeRejected, "listeners: replay pickup by a bystander");
        board.PieceDroppedAs("Ana", ChessRules.B1, ChessRules.C3);
        CheckTold(ChessBoard.ChangeFreeMove, "listeners: free move");
        CheckShown("rnbqkbnr/pppppppp/8/8/4P3/2N5/PPPP1PPP/R1BQKBNR b KQkq - 0 1", "listeners: free move");
        CheckLastMove(-1, -1, "listeners: free move");
        board.PieceDroppedAs("Ana", ChessRules.B1, ChessRules.D2);
        CheckTold(ChessBoard.ChangeRejected, "listeners: free move from an empty square");
        board.PieceDroppedAs("Ana", ChessRules.C3, ChessRules.C3);
        CheckTold(ChessBoard.ChangeRejected, "listeners: free move back");

        // Takebacks.
        board.StepToAs("Ana", false, 2);
        CheckTold(ChessBoard.ChangeReplayStep, "listeners: step from a custom position");
        board.RequestTakebackAs(ChessRules.Black, "Bob");
        CheckTold(ChessBoard.ChangeRequest, "listeners: takeback request");
        board.DeclineTakebackAs(ChessRules.White, "Ana");
        CheckTold(ChessBoard.ChangeRequest, "listeners: takeback declined");
        board.RequestTakebackAs(ChessRules.Black, "Bob");
        CheckTold(ChessBoard.ChangeRequest, "listeners: takeback request again");
        int before = board.loggedGames;
        board.AcceptTakebackAs(ChessRules.White, "Ana");
        CheckTold(ChessBoard.ChangeTakeback, "listeners: takeback");
        CheckLogged(before, 1, "listeners: takeback: the game logged");
        CheckShown("rnbqkbnr/ppppp1pp/8/5p2/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", "listeners: takeback");
        CheckLastMove(ChessRules.F7, ChessRules.F5, "listeners: takeback");
        board.EnterReplayAs("Bob", false);
        CheckTold(ChessBoard.ChangeEnteredReplay, "listeners: enter replay again");
        board.LeaveReplayAs("Bob", false);
        CheckTold(ChessBoard.ChangeLeftReplay, "listeners: leave replay");
        board.LeaveReplayAs("Bob", false);
        CheckNotTold("listeners: refused leave");

        // Endings.
        board.ResignAs(ChessRules.White, "Ana");
        CheckTold(ChessBoard.ChangeEnded, "listeners: resign");
        CheckString(board.ResultText(), "0-1", "listeners: resign: result");
        board.ResignAs(ChessRules.Black, "Bob");
        CheckNotTold("listeners: refused resign once ended");
        before = board.loggedGames;
        board.ResetBoardAs("Ana", false);
        CheckTold(ChessBoard.ChangeReset, "listeners: reset");
        CheckLogged(before, 0, "listeners: reset: an ended game not logged again");
        CheckShown(ChessFen.StartFen, "listeners: reset");
        board.ResetBoardAs("Ana", false);
        CheckNotTold("listeners: refused reset");

        ButtonGame(ChessArbiter.SettingDrawAgreement);
        MarkNotifications();
        board.OfferDrawAs(ChessRules.White, "Ana");
        CheckTold(ChessBoard.ChangeRequest, "listeners: offer to accept");
        board.AcceptDrawAs(ChessRules.Black, "Bob");
        CheckTold(ChessBoard.ChangeEnded, "listeners: draw agreed");
        CheckString(board.ResultText(), "1/2-1/2", "listeners: draw agreed: result");

        ButtonGame(ChessArbiter.SettingThreefoldRepetition);
        KnightShuffle(1, "listeners: threefold");
        MarkNotifications();
        board.ClaimDrawAs("Ana");
        CheckTold(ChessBoard.ChangeEnded, "listeners: draw claimed");

        // A move that ends the game is told as a move.
        StartedGame(ChessArbiter.NoClock, 0, 0);
        Drop("Ana", ChessRules.F2, ChessRules.F3, "listeners: fool's mate");
        Drop("Bob", ChessRules.E7, ChessRules.E5, "listeners: fool's mate");
        Drop("Ana", ChessRules.G2, ChessRules.G4, "listeners: fool's mate");
        MarkNotifications();
        board.PieceDroppedAs("Bob", ChessRules.D8, ChessRules.H4);
        CheckTold(ChessBoard.ChangeMove, "listeners: checkmate");
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, "listeners: checkmate: ended");
        Check(board.IsInCheck(), "listeners: checkmate: in check", "not in check");
        CheckString(board.ResultText(), "0-1", "listeners: checkmate: result");

        // A castle by a drop on the rook is a move, told once.
        CastlingGame("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        MarkNotifications();
        board.PieceDroppedAs("Ana", ChessRules.E1, ChessRules.H1);
        CheckTold(ChessBoard.ChangeMove, "listeners: castle on the rook");
        CheckLastMove(ChessRules.E1, ChessRules.H1, "listeners: castle on the rook (to is the rook's square)");

        // Flag fall: told once by _CheckFlagFall; a reset that ends the game on time on its way is told as a reset.
        StartedGame(60, 0, board._ServerTime());
        Drop("Ana", ChessRules.E2, ChessRules.E4, "listeners: flag");
        FlagFallen();
        MarkNotifications();
        CheckInt(board.FlaggedColor(), ChessRules.Black, "listeners: flag: black's flag fallen");
        board._CheckFlagFall();
        CheckTold(ChessBoard.ChangeEnded, "listeners: flag fall");
        board._CheckFlagFall();
        CheckNotTold("listeners: flag fall once");
        StartedGame(60, 0, board._ServerTime());
        Drop("Ana", ChessRules.E2, ChessRules.E4, "listeners: flag on reset");
        FlagFallen();
        MarkNotifications();
        before = board.loggedGames;
        board.ResetBoardAs("Ana", false);
        CheckTold(ChessBoard.ChangeReset, "listeners: reset with a fallen flag");
        CheckLoggedOnReset(before, "1-0", "time forfeit", "listeners: reset with a fallen flag");

        // A loaded game.
        if (!Utilities.IsValid(importer) || !Utilities.IsValid(siggiVsNuka)) {
            Check(false, "listeners: loading", "the importer or siggiVsNuka field is empty");
        } else {
            board._ClearState();
            CheckInt(ImportText(siggiVsNuka.text), ChessPgn.ImportDone, "listeners: imported");
            MarkNotifications();
            board._LoadImportAs("", importer);
            CheckTold(ChessBoard.ChangeLoaded, "listeners: loaded");
            CheckShown("6k1/1b4pp/p3p3/2p1P1b1/NPPn4/7P/P5q1/RN2Kr2 w - - 0 28", "listeners: loaded");
            CheckLastMove(ChessRules.F8, ChessRules.F1, "listeners: loaded: the mating move");
            Check(board.IsInCheck(), "listeners: loaded: White in check", "not in check");
            CheckString(board.ResultText(), "0-1", "listeners: loaded: result");
            board._LoadImportAs("", importer);
            CheckNotTold("listeners: refused load");
            board.JoinAs(ChessRules.White, "Ana");
            CheckTold(ChessBoard.ChangeJoined, "listeners: loaded: join");
            board.JoinAs(ChessRules.Black, "Bob");
            CheckTold(ChessBoard.ChangeJoined, "listeners: loaded: second join");
            board.SetClockAs("Ana", 300, 3);
            CheckTold(ChessBoard.ChangeSettings, "listeners: loaded: clock");
            board.StepToAs("Ana", false, 20);
            CheckTold(ChessBoard.ChangeReplayStep, "listeners: loaded: step");
            int lastBefore = board.moveHistory[19];
            board.ContinueGameAs("Bob");
            CheckTold(ChessBoard.ChangeContinued, "listeners: continued");
            CheckInt(board.LastMove(), lastBefore, "listeners: continued: last move");
            CheckInt(board.SideToMove(), ChessRules.White, "listeners: continued: white to move");
            board.ResetBoardAs("Bob", false);
            CheckTold(ChessBoard.ChangeReset, "listeners: continued: reset");
        }

        // Removed, the test is told nothing.
        board.RemoveListener(this);
        board.RemoveListener(this);
        CheckInt(board.listeners.Length, listenerCount, "listeners: removed");
        board.JoinAs(ChessRules.White, "Ana");
        CheckNotTold("listeners: removed");
        board._ClearState();
    }

    // ---- 7.12 scenarios ----

    // Whole games from a reset board back to a reset board. Unlike the checks above, the settings come only from the
    // arbiter's overrides through the snapshot (never written on the board), and everything goes through the board's
    // input methods and buttons, with the test listening throughout. Needs the arbiter, with the local player an
    // organizer (TestSnapshot reports either missing).
    void TestScenarios() {
        if (!Utilities.IsValid(board.arbiter) || !board.arbiter.HasBoard(board.boardNumber)) return;
        board.AddListener(this);
        ScenarioTournament();
        ScenarioCasual();
        ScenarioResnapshot();
        ScenarioLoadedGame();
        ScenarioEndings(ChessArbiter.SettingTakebacks | ChessArbiter.SettingsDrawRules, "every rule on");
        ScenarioEndings(0, "no draws, no takebacks");
        board.RemoveListener(this);
        board.arbiter.ClearOverrides(board.boardNumber);
        NothingForced();
        board._ClearState();
    }

    // The arbiter's overrides for the board: these bits on and the rest off, both inactivity times never, and the
    // forced values (used only while their bit is on).
    void ScenarioSettings(int settings, int forcedMode, int forcedBase, int forcedInc) {
        OverrideAll(settings, ChessArbiter.Never, ChessArbiter.Never, forcedMode, forcedBase, forcedInc);
    }

    // Checks the snapshot against the bits of ScenarioSettings.
    void CheckScenarioSnapshot(int settings, int forcedMode, int forcedBase, int forcedInc, string name) {
        CheckSnapshot(settings, ChessArbiter.Never, ChessArbiter.Never, forcedMode, forcedBase, forcedInc, name);
    }

    // Ana and Bob join a reset board (the snapshot must have these bits), choose this clock unless it's none, and
    // Ana starts.
    void ScenarioGame(int settings, int baseSeconds, string name) {
        board._ClearState();
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, name + ": Ana joins");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, name + ": Bob joins");
        CheckString(ChessArbiter._SettingsToString(board.gameSettings), ChessArbiter._SettingsToString(settings),
            name + ": the snapshot");
        if (baseSeconds != ChessArbiter.NoClock) {
            CheckInt(board.SetClockAs("Ana", baseSeconds, 0), ChessBoard.InputAccepted, name + ": the clock chosen");
        }
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, name + ": started");
    }

    // A seated player resets the board, logging nothing.
    void ScenarioReset(string name) {
        int before = board.loggedGames;
        MarkNotifications();
        CheckInt(board.ResetBoardAs("Ana", false), ChessBoard.InputAccepted, name + ": reset");
        CheckTold(ChessBoard.ChangeReset, name + ": reset");
        CheckLogged(before, 0, name + ": nothing logged on reset");
        CheckCleared(name + ": reset");
    }

    // name drops the side to move's first legal move that's neither a promotion nor a castle.
    void DropAnyMove(string name, string label) {
        int[] moves = new int[ChessMoves.MaxMoves];
        int count = ChessMoves.GenerateLegalMoves(board.board, board.state, moves, true);
        int move = 0;
        for (int i = 0; i < count && move == 0; i++) {
            if (ChessRules.MovePromotion(moves[i]) == 0 && !ChessRules.MoveIsCastle(moves[i])) move = moves[i];
        }
        Check(move != 0, label + ": a move to play", "no legal move");
        if (move != 0) Drop(name, ChessRules.MoveFrom(move), ChessRules.MoveTo(move), label);
    }

    // A tournament board: Chess960 and 3+2 forced, no takebacks, "No draws". The snapshot is taken on the first join
    // and kept while the arbiter changes; the game ends on time, a loss under "No draws"; the next game after the
    // reset gets the arbiter's new settings.
    void ScenarioTournament() {
        string s = "scenario tournament";
        int settings = ChessArbiter.SettingModeForced | ChessArbiter.SettingClockForced;
        ScenarioSettings(settings, ChessGame.ModeChess960, 180, 2);
        board._ClearState();
        CheckInt(board.gameSettings, 0, s + ": no snapshot on the reset board");
        MarkNotifications();
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": Ana joins");
        CheckTold(ChessBoard.ChangeJoined, s + ": Ana joins");
        CheckScenarioSnapshot(settings, ChessGame.ModeChess960, 180, 2, s + ": snapshot on the first join");
        CheckInt(board.mode, ChessGame.ModeChess960, s + ": the lobby shows the forced mode");
        CheckInt(board.baseTime * 1000 + board.increment, 180002, s + ": the lobby shows the forced clock");

        // The organizers open everything up after the first join: the board keeps its snapshot.
        int open = ChessArbiter.SettingTakebacks | ChessArbiter.SettingsDrawRules;
        ScenarioSettings(open, ChessGame.ModeStandard, ChessArbiter.NoClock, 0);
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob joins");
        CheckScenarioSnapshot(settings, ChessGame.ModeChess960, 180, 2, s + ": the second join keeps the snapshot");
        CheckInt(board.SetModeAs("Ana", ChessGame.ModeStandard), ChessBoard.InputRejected, s + ": the mode is forced");
        CheckInt(board.SetClockAs("Bob", 60, 0), ChessBoard.InputRejected, s + ": the clock is forced");
        MarkNotifications();
        CheckInt(board.StartGameAs("Bob"), ChessBoard.InputAccepted, s + ": started");
        CheckTold(ChessBoard.ChangeStarted, s + ": started");
        CheckScenarioSnapshot(settings, ChessGame.ModeChess960, 180, 2, s + ": Start keeps the snapshot");
        CheckInt(board.mode, ChessGame.ModeChess960, s + ": a Chess960 game");
        CheckInt(board.whiteTime, 180000, s + ": white's clock");
        CheckInt(board.blackTime, 180000, s + ": black's clock");

        // Pawn pushes are legal from every Chess960 start.
        MarkNotifications();
        Drop("Ana", ChessRules.A2, ChessRules.A3, s);
        CheckTold(ChessBoard.ChangeMove, s + ": a3");
        Check(board.whiteTime > 180000 && board.whiteTime <= 182000, s + ": a3: the increment added",
            "white has " + board.whiteTime + " ms");
        Drop("Bob", ChessRules.H7, ChessRules.H6, s);
        Drop("Ana", ChessRules.B2, ChessRules.B3, s);
        Drop("Bob", ChessRules.G7, ChessRules.G6, s);
        CheckInt(board.OfferDrawAs(ChessRules.White, "Ana"), ChessBoard.InputRejected, s + ": no draw offers");
        CheckInt(board.ClaimableDraw(), ChessGame.EndingNone, s + ": no draw claims");
        CheckInt(board.EnterReplayAs("Bob", false), ChessBoard.InputAccepted, s + ": replay");
        CheckInt(board.StepToAs("Bob", false, 2), ChessBoard.InputAccepted, s + ": step to ply 2");
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected, s + ": no takebacks");
        CheckInt(board.LeaveReplayAs("Ana", false), ChessBoard.InputAccepted, s + ": leave replay");

        // White's flag falls: lost on time.
        double late = board.turnStart + 1000;
        CheckInt(board._FlaggedColorAt(late), ChessRules.White, s + ": white's flag falls");
        int before = board.loggedGames;
        MarkNotifications();
        Check(board._CheckFlagFallAt(late), s + ": the flag fall ends the game", "not ended");
        CheckTold(ChessBoard.ChangeEnded, s + ": flag fall");
        CheckEnded(ChessGame.EndingTimeForfeit, ChessRules.Black, "0-1", s + ": lost on time");
        CheckInt(board.whiteTime, 0, s + ": white's clock stopped at 0");
        CheckLoggedEnding(before, "0-1", "time forfeit", s + ": logged");
        CheckLogHas("[White \"Ana\"]\n", s + ": logged White");
        CheckLogHas("[Black \"Bob\"]\n", s + ": logged Black");
        CheckLogHas("[Variant \"Chess960\"]\n", s + ": logged Variant");
        CheckLogHas("[SetUp \"1\"]\n", s + ": logged SetUp");
        CheckLogHas("[FEN \"" + board.startFen + "\"]\n", s + ": logged FEN");
        CheckLogHas("[TimeControl \"180+2\"]\n", s + ": logged TimeControl");
        CheckLogHas("[GameID \"" + board.gameId + "\"]\n", s + ": logged GameID");
        CheckLogHas("1. a3 { [%clk 0:03:0", s + ": logged a3 with its clock");
        CheckLogHas("2... g6 { [%clk ", s + ": logged g6 with its clock");

        // The ended game can still be replayed; the reset logs nothing more and clears the snapshot.
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, s + ": replay once ended");
        CheckInt(board.StepToAs("Ana", false, 0), ChessBoard.InputAccepted, s + ": step to the start once ended");
        CheckInt(board.ResetBoardAs("Cleo", false), ChessBoard.InputRejected, s + ": a bystander can't reset");
        ScenarioReset(s);

        // The next game on the board takes the arbiter's settings as they are now.
        CheckInt(board.JoinAs(ChessRules.White, "Cleo"), ChessBoard.InputAccepted, s + ": Cleo joins");
        CheckScenarioSnapshot(open, ChessGame.ModeStandard, ChessArbiter.NoClock, 0, s + ": the next snapshot");
        Check(!board.IsModeForced() && !board.IsClockForced(), s + ": nothing forced now", "a forced bit is on");
        CheckInt(board.SetModeAs("Cleo", ChessGame.ModeChess960), ChessBoard.InputAccepted, s + ": the mode is free");
        board._ClearState();
    }

    // A casual board with takebacks and every draw rule on, nothing forced: the players choose the mode and clock,
    // a draw offer is declined, a takeback continues the game under a new ID (the old one logged unfinished), and the
    // continued game ends with a threefold repetition claimed.
    void ScenarioCasual() {
        string s = "scenario casual";
        int settings = ChessArbiter.SettingTakebacks | ChessArbiter.SettingsDrawRules;
        ScenarioSettings(settings, ChessGame.ModeChess960, 180, 2);
        board._ClearState();
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": Ana joins");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob joins");
        CheckScenarioSnapshot(settings, ChessGame.ModeChess960, 180, 2, s + ": snapshot");
        CheckInt(board.mode, ChessGame.ModeStandard, s + ": the forced mode unused while not forced");
        CheckInt(board.baseTime, ChessArbiter.NoClock, s + ": the forced clock unused while not forced");
        CheckInt(board.SetModeAs("Bob", ChessGame.ModeChess960), ChessBoard.InputAccepted, s + ": Bob picks Chess960");
        CheckInt(board.SetModeAs("Ana", ChessGame.ModeStandard), ChessBoard.InputAccepted, s + ": Ana picks Standard");
        CheckInt(board.SetClockAs("Bob", 60, 1), ChessBoard.InputAccepted, s + ": Bob picks 1+1");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, s + ": started");
        CheckInt(board.mode, ChessGame.ModeStandard, s + ": a Standard game");
        CheckString(Fen(), ChessFen.StartFen, s + ": the standard start");
        CheckInt(board.whiteTime, 60000, s + ": the chosen clock");
        string firstId = board.gameId;

        Drop("Ana", ChessRules.E2, ChessRules.E4, s);
        Drop("Bob", ChessRules.E7, ChessRules.E5, s);
        Drop("Ana", ChessRules.G1, ChessRules.F3, s);
        Drop("Bob", ChessRules.B8, ChessRules.C6, s);
        MarkNotifications();
        CheckInt(board.OfferDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob offers a draw");
        CheckTold(ChessBoard.ChangeRequest, s + ": draw offer");
        CheckInt(board.DeclineDrawAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": Ana declines");
        CheckInt(board.drawOfferColor, ChessRules.NoColor, s + ": no offer pending");

        // Bob asks to go back to the position after 1... e5, and Ana agrees.
        CheckInt(board.EnterReplayAs("Bob", false), ChessBoard.InputAccepted, s + ": replay");
        CheckInt(board.StepToAs("Bob", false, 2), ChessBoard.InputAccepted, s + ": step to ply 2");
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": takeback asked");
        int before = board.loggedGames;
        MarkNotifications();
        CheckInt(board.AcceptTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": takeback agreed");
        CheckTold(ChessBoard.ChangeTakeback, s + ": takeback");
        CheckLogged(before, 1, s + ": the unfinished game logged");
        CheckLogHas("[Result \"*\"]\n", s + ": logged unfinished");
        CheckLogHas("[Termination \"unterminated\"]\n", s + ": logged unterminated");
        CheckLogHas("[GameID \"" + firstId + "\"]\n", s + ": logged with the first ID");
        CheckLogHas("[TimeControl \"60+1\"]\n", s + ": logged with the chosen clock");
        CheckLogHas("2... Nc6 { [%clk ", s + ": logged with every move");
        CheckInGame(s + ": continued");
        Check(!board.replayMode, s + ": continued out of replay", "in replay");
        CheckInt(board.moveCount, 2, s + ": continued from ply 2");
        CheckString(Fen(), "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2",
            s + ": the position after e5");
        Check(board.gameId != "" && board.gameId != firstId, s + ": a new ID", "ID " + board.gameId);
        CheckString(board.continuedFrom, firstId + "/2", s + ": ContinuedFrom");

        // The knights go out and back twice: the position after 1... e5 for the third time.
        KnightShuffle(2, s);
        CheckInt(board.ClaimableDraw(), ChessGame.EndingThreefoldRepetition, s + ": threefold claimable");
        CheckInt(board.ClaimDrawAs("Bob"), ChessBoard.InputRejected, s + ": only the side to move claims");
        before = board.loggedGames;
        MarkNotifications();
        CheckInt(board.ClaimDrawAs("Ana"), ChessBoard.InputAccepted, s + ": Ana claims");
        CheckTold(ChessBoard.ChangeEnded, s + ": claim");
        CheckEnded(ChessGame.EndingThreefoldRepetition, ChessRules.NoColor, "1/2-1/2", s + ": threefold");
        CheckLoggedEnding(before, "1/2-1/2", "threefold repetition", s + ": the continued game");
        CheckLogHas("[GameID \"" + board.gameId + "\"]\n", s + ": logged with the new ID");
        CheckLogHas("[ContinuedFrom \"" + firstId + "/2\"]\n", s + ": logged ContinuedFrom");
        CheckLogHas("1. e4 { [%clk ", s + ": logged with the moves before the takeback");
        CheckLogHas("5... Ng8 { [%clk ", s + ": logged with the moves after the takeback");

        // An organizer passing by resets it.
        CheckInt(board.ResetBoardAs("Cleo", false), ChessBoard.InputRejected, s + ": a bystander can't reset");
        before = board.loggedGames;
        CheckInt(board.ResetBoardAs("Cleo", true), ChessBoard.InputAccepted, s + ": an organizer resets");
        CheckLogged(before, 0, s + ": nothing logged on reset");
        CheckCleared(s + ": reset");
    }

    // Takebacks off and only draws by agreement: the arbiter turning takebacks on changes nothing until an organizer
    // re-snapshots the board mid-game; turning draws off ("No draws") and re-snapshotting stops offers; turned back
    // on, a draw is agreed.
    void ScenarioResnapshot() {
        string s = "scenario re-snapshot";
        ScenarioSettings(ChessArbiter.SettingDrawAgreement, ChessGame.ModeStandard, ChessArbiter.NoClock, 0);
        board._ClearState();
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": Ana joins");
        int both = ChessArbiter.SettingDrawAgreement | ChessArbiter.SettingTakebacks;
        ScenarioSettings(both, ChessGame.ModeStandard, ChessArbiter.NoClock, 0);
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob joins");
        CheckInt(board.StartGameAs("Bob"), ChessBoard.InputAccepted, s + ": started");
        CheckString(ChessArbiter._SettingsToString(board.gameSettings),
            ChessArbiter._SettingsToString(ChessArbiter.SettingDrawAgreement), s + ": the first join's snapshot");
        Drop("Ana", ChessRules.E2, ChessRules.E4, s);
        Drop("Bob", ChessRules.E7, ChessRules.E5, s);
        Drop("Ana", ChessRules.D2, ChessRules.D4, s);
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, s + ": replay");
        CheckInt(board.StepToAs("Ana", false, 1), ChessBoard.InputAccepted, s + ": step to ply 1");
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected,
            s + ": takebacks off in the snapshot");

        MarkNotifications();
        CheckInt(board.Resnapshot(), ChessBoard.InputAccepted, s + ": an organizer re-snapshots");
        CheckTold(ChessBoard.ChangeSettings, s + ": re-snapshot");
        CheckScenarioSnapshot(both, ChessGame.ModeStandard, ChessArbiter.NoClock, 0, s + ": the re-snapshot");
        CheckInGame(s + ": re-snapshot");
        Check(board.replayMode && board.replayIndex == 1, s + ": re-snapshot: still showing ply 1",
            (board.replayMode ? "in replay" : "not in replay") + " at " + board.replayIndex);
        CheckInt(board.moveCount, 3, s + ": re-snapshot: the moves kept");
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": takeback asked");
        CheckInt(board.DeclineTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": Ana declines");
        CheckInt(board.LeaveReplayAs("Bob", false), ChessBoard.InputAccepted, s + ": leave replay");
        CheckInt(board.moveCount, 3, s + ": declined: the moves kept");

        ScenarioSettings(ChessArbiter.SettingTakebacks, ChessGame.ModeStandard, ChessArbiter.NoClock, 0);
        CheckInt(board.Resnapshot(), ChessBoard.InputAccepted, s + ": re-snapshot to \"No draws\"");
        CheckInt(board.OfferDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected, s + ": no offers under \"No draws\"");
        ScenarioSettings(both, ChessGame.ModeStandard, ChessArbiter.NoClock, 0);
        CheckInt(board.Resnapshot(), ChessBoard.InputAccepted, s + ": re-snapshot with agreement");
        CheckInt(board.OfferDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob offers a draw");
        Drop("Bob", ChessRules.E5, ChessRules.D4, s);
        CheckInt(board.drawOfferColor, ChessRules.Black, s + ": the offer kept by Bob's own move");
        int before = board.loggedGames;
        MarkNotifications();
        CheckInt(board.AcceptDrawAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": Ana accepts");
        CheckTold(ChessBoard.ChangeEnded, s + ": draw agreed");
        CheckEnded(ChessGame.EndingDrawAgreement, ChessRules.NoColor, "1/2-1/2", s + ": draw agreed");
        CheckLoggedEnding(before, "1/2-1/2", "draw agreement", s + ": logged");
        CheckLogHas("[TimeControl \"-\"]\n", s + ": logged with no clock");
        CheckLogHas("\n1. e4 e5 2. d4 exd4 1/2-1/2\n", s + ": logged move text");
        ScenarioReset(s);
    }

    // Siggi-vs-Nuka loaded onto a board with the mode and a 5+0 clock forced, takebacks on and "No draws": the
    // snapshot is taken on loading (the forced clock applied, the game's mode kept), the players join and continue
    // from ply 20, take back past the continuation point, and White resigns.
    void ScenarioLoadedGame() {
        string s = "scenario loaded";
        if (!Utilities.IsValid(importer) || !Utilities.IsValid(siggiVsNuka)) {
            Check(false, s, "the importer or siggiVsNuka field is empty");
            return;
        }
        int settings = ChessArbiter.SettingTakebacks | ChessArbiter.SettingModeForced
            | ChessArbiter.SettingClockForced;
        ScenarioSettings(settings, ChessGame.ModeChess960, 300, 0);
        board._ClearState();
        CheckInt(ImportText(siggiVsNuka.text), ChessPgn.ImportDone, s + ": imported");
        CheckInt(board.gameSettings, 0, s + ": no snapshot before loading");
        MarkNotifications();
        CheckInt(board._LoadImportAs("", importer), ChessBoard.InputAccepted, s + ": loaded");
        CheckTold(ChessBoard.ChangeLoaded, s + ": loaded");
        CheckScenarioSnapshot(settings, ChessGame.ModeChess960, 300, 0, s + ": snapshot on loading");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, s + ": the loaded state");
        Check(board.replayMode, s + ": in replay", "not in replay");
        CheckInt(board.mode, ChessGame.ModeStandard, s + ": the game's mode, not the forced one");
        CheckInt(board.baseTime * 1000 + board.increment, 300000, s + ": the forced clock for continuing");
        CheckPgnHas("[TimeControl \"600\"]", true, s + ": the loaded game's own time control exported");
        CheckInt(board.ending * 10 + board.winner, ChessGame.EndingCheckmate * 10 + ChessRules.Black,
            s + ": won by Black by checkmate");

        // The arbiter changing now doesn't reach the board.
        ScenarioSettings(ChessArbiter.SettingsDrawRules, ChessGame.ModeStandard, ChessArbiter.NoClock, 0);
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": Ana joins");
        CheckInt(board.JoinAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob joins");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, s + ": still loaded");
        CheckScenarioSnapshot(settings, ChessGame.ModeChess960, 300, 0, s + ": joining keeps the snapshot");
        CheckInt(board.SetClockAs("Ana", 60, 0), ChessBoard.InputRejected, s + ": the clock is forced");
        CheckInt(board.ContinueGameAs("Ana"), ChessBoard.InputRejected, s + ": not from the final checkmate");
        CheckInt(board.StepToAs("Bob", false, 20), ChessBoard.InputAccepted, s + ": step to ply 20");
        int before = board.loggedGames;
        MarkNotifications();
        CheckInt(board.ContinueGameAs("Bob"), ChessBoard.InputAccepted, s + ": continued");
        CheckTold(ChessBoard.ChangeContinued, s + ": continued");
        CheckLogged(before, 0, s + ": nothing logged on continuing");
        CheckInGame(s + ": continued");
        CheckInt(board.moveCount, 20, s + ": continued from ply 20");
        CheckInt(board.mode, ChessGame.ModeStandard, s + ": continued in the game's mode");
        CheckInt(board.whiteTime, 300000, s + ": white's fresh clock");
        CheckInt(board.blackTime, 300000, s + ": black's fresh clock");
        CheckString(board.continuedFrom, "20250710001921/20", s + ": ContinuedFrom the PGN's ID");
        string continuedId = board.gameId;
        Check(continuedId != "" && continuedId != "20250710001921", s + ": a new ID", "ID " + continuedId);
        DropAnyMove("Ana", s + ": White plays on");
        DropAnyMove("Bob", s + ": Black plays on");

        // Back to ply 18, before the continuation point: the continued game logged unfinished, both sides on the base
        // time.
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, s + ": replay");
        CheckInt(board.StepToAs("Ana", false, 18), ChessBoard.InputAccepted, s + ": step to ply 18");
        CheckInt(board.RequestTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": takeback asked");
        MarkNotifications();
        CheckInt(board.AcceptTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": takeback agreed");
        CheckTold(ChessBoard.ChangeTakeback, s + ": takeback");
        CheckLogged(before, 1, s + ": the continued game logged unfinished");
        CheckLogHas("[Result \"*\"]\n", s + ": logged unfinished");
        CheckLogHas("[White \"Ana\"]\n", s + ": logged with the seated White");
        CheckLogHas("[Black \"Bob\"]\n", s + ": logged with the seated Black");
        CheckLogHas("[TimeControl \"300\"]\n", s + ": logged with the forced clock");
        CheckLogHas("[GameID \"" + continuedId + "\"]\n", s + ": logged with the continued game's ID");
        CheckLogHas("[ContinuedFrom \"20250710001921/20\"]\n", s + ": logged ContinuedFrom");
        CheckInGame(s + ": taken back");
        CheckInt(board.moveCount, 18, s + ": taken back to ply 18");
        CheckInt(board.whiteTime, 300000, s + ": taken back: white's base time");
        CheckInt(board.blackTime, 300000, s + ": taken back: black's base time");
        CheckString(board.continuedFrom, continuedId + "/18", s + ": taken back: ContinuedFrom");

        CheckInt(board.OfferDrawAs(ChessRules.Black, "Bob"), ChessBoard.InputRejected, s + ": no offers under \"No draws\"");
        before = board.loggedGames;
        MarkNotifications();
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": Ana resigns");
        CheckTold(ChessBoard.ChangeEnded, s + ": resign");
        CheckEnded(ChessGame.EndingResignation, ChessRules.Black, "0-1", s + ": resigned");
        CheckLoggedEnding(before, "0-1", "resignation", s + ": logged");
        CheckLogHas("[ContinuedFrom \"" + continuedId + "/18\"]\n", s + ": logged ContinuedFrom the continued game");
        Check(!board.lastLoggedPgn.Contains("[%clk"), s + ": no clocks for the loaded moves",
            "PGN:\n" + board.lastLoggedPgn);
        ScenarioReset(s);
    }

    // The endings reachable by play under these settings (from the arbiter, nothing forced), each game logged once:
    // checkmate and stalemate whatever the draw rules; the knights shuffling to a fifth repetition, a draw with
    // fivefold on and otherwise played on until Black resigns; a flag fall with material on the board, lost either
    // way. After the mate, a takeback to before it is agreed when takebacks are on, and refused when they're off.
    void ScenarioEndings(int settings, string label) {
        string s = "scenario endings (" + label + ")";
        bool fivefold = (settings & ChessArbiter.SettingFivefoldRepetition) != 0;
        bool takebacks = (settings & ChessArbiter.SettingTakebacks) != 0;
        ScenarioSettings(settings, ChessGame.ModeStandard, ChessArbiter.NoClock, 0);

        // Fool's mate.
        string f = s + ": fool's mate";
        ScenarioGame(settings, ChessArbiter.NoClock, f);
        Drop("Ana", ChessRules.F2, ChessRules.F3, f);
        Drop("Bob", ChessRules.E7, ChessRules.E5, f);
        Drop("Ana", ChessRules.G2, ChessRules.G4, f);
        int before = board.loggedGames;
        MarkNotifications();
        Drop("Bob", ChessRules.D8, ChessRules.H4, f);
        CheckTold(ChessBoard.ChangeMove, f + ": the mating move");
        CheckEnded(ChessGame.EndingCheckmate, ChessRules.Black, "0-1", f);
        CheckLoggedEnding(before, "0-1", "checkmate", f);
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, f + ": replay");
        CheckInt(board.StepToAs("Ana", false, 2), ChessBoard.InputAccepted, f + ": step to ply 2");
        CheckInt(board.RequestTakebackAs(ChessRules.White, "Ana"),
            takebacks ? ChessBoard.InputAccepted : ChessBoard.InputRejected, f + ": takeback asked");
        if (takebacks) {
            before = board.loggedGames;
            CheckInt(board.AcceptTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, f + ": takeback agreed");
            CheckLogged(before, 0, f + ": the ended game not logged again");
            CheckInGame(f + ": taken back");
            CheckInt(board.moveCount, 2, f + ": taken back to ply 2");
            Drop("Ana", ChessRules.G2, ChessRules.G3, f + ": taken back");
            MarkNotifications();
            CheckInt(board.ResetBoardAs("Ana", false), ChessBoard.InputAccepted, f + ": reset");
            CheckTold(ChessBoard.ChangeReset, f + ": reset");
            CheckLoggedOnReset(before, "*", "unterminated", f + ": the continued game");
            CheckCleared(f + ": reset");
        } else {
            ScenarioReset(f);
        }

        // Sam Loyd's ten-move stalemate.
        string st = s + ": stalemate";
        ScenarioGame(settings, ChessArbiter.NoClock, st);
        int[] loyd = new int[] {
            ChessRules.E2, ChessRules.E3, ChessRules.A7, ChessRules.A5, ChessRules.D1, ChessRules.H5,
            ChessRules.A8, ChessRules.A6, ChessRules.H5, ChessRules.A5, ChessRules.H7, ChessRules.H5,
            ChessRules.H2, ChessRules.H4, ChessRules.A6, ChessRules.H6, ChessRules.A5, ChessRules.C7,
            ChessRules.F7, ChessRules.F6, ChessRules.C7, ChessRules.D7, ChessRules.E8, ChessRules.F7,
            ChessRules.D7, ChessRules.B7, ChessRules.D8, ChessRules.D3, ChessRules.B7, ChessRules.B8,
            ChessRules.D3, ChessRules.H7, ChessRules.B8, ChessRules.C8, ChessRules.F7, ChessRules.G6,
            ChessRules.C8, ChessRules.E6
        };
        before = board.loggedGames;
        for (int i = 0; i < loyd.Length; i += 2) {
            Drop(i % 4 == 0 ? "Ana" : "Bob", loyd[i], loyd[i + 1], st);
        }
        CheckString(Fen(), "5bnr/4p1pq/4Qpkr/7p/7P/4P3/PPPP1PP1/RNB1KBNR b KQ - 2 10", st + ": the position");
        CheckEnded(ChessGame.EndingStalemate, ChessRules.NoColor, "1/2-1/2", st);
        CheckLoggedEnding(before, "1/2-1/2", "stalemate", st);
        ScenarioReset(st);

        // The knights out and back four times: the starting position for the fifth time.
        string r = s + ": repetition";
        ScenarioGame(settings, ChessArbiter.NoClock, r);
        before = board.loggedGames;
        KnightShuffle(4, r);
        if (fivefold) {
            CheckEnded(ChessGame.EndingFivefoldRepetition, ChessRules.NoColor, "1/2-1/2", r + ": fivefold");
            CheckLoggedEnding(before, "1/2-1/2", "fivefold repetition", r);
        } else {
            CheckInGame(r + ": played on");
            CheckInt(board.ClaimableDraw(), ChessGame.EndingNone, r + ": nothing to claim");
            CheckInt(board.OfferDrawAs(ChessRules.White, "Ana"), ChessBoard.InputRejected, r + ": no offers");
            CheckLogged(before, 0, r + ": nothing logged yet");
            CheckInt(board.ResignAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, r + ": Bob resigns");
            CheckEnded(ChessGame.EndingResignation, ChessRules.White, "1-0", r + ": resigned");
            CheckLoggedEnding(before, "1-0", "resignation", r);
        }
        ScenarioReset(r);

        // Black's flag falls with all the material on the board.
        string t = s + ": time forfeit";
        ScenarioGame(settings, 60, t);
        Drop("Ana", ChessRules.E2, ChessRules.E4, t);
        double late = board.turnStart + 61;
        before = board.loggedGames;
        MarkNotifications();
        Check(board._CheckFlagFallAt(late), t + ": the flag fall ends the game", "not ended");
        CheckTold(ChessBoard.ChangeEnded, t + ": flag fall");
        CheckEnded(ChessGame.EndingTimeForfeit, ChessRules.White, "1-0", t);
        CheckLoggedEnding(before, "1-0", "time forfeit", t);
        CheckLogHas("[TimeControl \"60\"]\n", t + ": logged TimeControl");
        ScenarioReset(t);
    }

    // ---- 8.3 sync ----

    // The board's whole state as text: every field _PackState sends, and the halfmove clocks worked out from them, so
    // two states compare in one check.
    string StateText() {
        string text = "position " + ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber)
            + "; state";
        for (int i = 0; i < ChessPosition.StateLength; i++) text += " " + board.state[i];
        text += "; replay board";
        for (int i = 0; i < 64; i++) text += " " + board.replayBoard[i];
        text += "; start " + board.startFen + " " + board.startHalfmoveClock + " " + board.startMoveNumber;
        text += "; moves " + board.moveCount + ":";
        for (int i = 0; i < board.moveCount; i++) {
            text += " " + board.moveHistory[i] + "/" + board.notation[i] + "/" + board.clockHistory[i] + "/"
                + board.halfmoveClocks[i];
        }
        text += "; replay " + (board.replayMode ? "on" : "off") + " " + board.replayIndex
            + (board.replayCustom ? " custom" : "") + "; takeback " + board.takebackColor + " " + board.takebackPly
            + "; draw offer " + board.drawOfferColor + "; ids " + board.gameId + " " + board.continuedFrom
            + "; source " + board.replaySourceWhite + "|" + board.replaySourceBlack + "|" + board.replaySourceResult
            + "|" + board.replaySourceBaseTime + "|" + board.replaySourceIncrement
            + "; seats " + board.whitePlayer + "|" + board.blackPlayer + "; activity " + board.lastActivity
            + "; result " + board.ending + " " + board.winner + "; lifecycle " + board.lifecycle
            + "; mode " + board.mode + "; clock " + board.baseTime + "+" + board.increment + " " + board.whiteTime
            + " " + board.blackTime + " " + board.turnStart + (board.untimedAfterFlagFall ? " untimed" : "") + " "
            + board.flagFallPly + "; settings " + board.gameSettings + "; piece sets " + board.whitePieceSet + "|"
            + board.blackPieceSet;
        for (int i = 0; i < ChessArbiter.NumberCount; i++) text += " " + board.gameNumbers[i];
        return text;
    }

    // Ana and Bob start a game, with a clock of baseSeconds (or none), and play e4 e5.
    void SyncGame(int baseSeconds, string name) {
        board._ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        board.JoinAs(ChessRules.Black, "Bob");
        if (baseSeconds != ChessArbiter.NoClock) board.SetClockAs("Ana", baseSeconds, 0);
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, name + ": started");
        Drop("Ana", ChessRules.E2, ChessRules.E4, name);
        Drop("Bob", ChessRules.E7, ChessRules.E5, name);
    }

    // The synced fields as an update replacing the game with another (a reset or a takeback elsewhere): only the
    // game ID matters to the receiver's logging.
    void SyncReplaceGame() {
        board._PackState();
        board.syncedGameId = "";
        board.syncedInts[ChessBoard.SyncLifecycle] = ChessBoard.LifecycleReset;
    }

    void TestSync() {
        NothingForced();
        board.AddListener(this);

        // The reset state, as a late joiner gets it when nobody has played yet.
        string r = "sync reset";
        board._ClearState();
        board._PackState();
        string sent = StateText();
        board.JoinAs(ChessRules.White, "Ana");
        board._ReceiveState();
        CheckString(StateText(), sent, r + ": received as sent");
        CheckCleared(r);

        // A game in progress with a clock, a draw offer, and replay showing ply 1 with a takeback request.
        string s = "sync";
        board._ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        board.JoinAs(ChessRules.Black, "Bob");
        board.SetClockAs("Ana", 300, 2);
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, s + ": started");
        board.gameSettings = ChessArbiter.SettingDrawAgreement | ChessArbiter.SettingTakebacks;
        Drop("Ana", ChessRules.E2, ChessRules.E4, s);
        Drop("Bob", ChessRules.E7, ChessRules.E5, s);
        Drop("Ana", ChessRules.G1, ChessRules.F3, s);
        CheckInt(board.OfferDrawAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, s + ": draw offered");
        CheckInt(board.EnterReplayAs("Bob", false), ChessBoard.InputAccepted, s + ": replay entered");
        CheckInt(board.StepToAs("Bob", false, 1), ChessBoard.InputAccepted, s + ": stepped to ply 1");
        CheckInt(board.RequestTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": takeback asked");
        board.lastActivity = 12345.5;
        board._PackState();
        CheckInt(board.syncedMoves.Length * 100 + board.syncedClocks.Length * 10 + board.syncedNotation.Length, 333,
            s + ": only the game's moves, clocks and notation sent");
        CheckString(board.syncedNotation[0] + " " + board.syncedNotation[1] + " " + board.syncedNotation[2],
            "e4 e5 Nf3", s + ": notation sent");
        CheckInt(board.syncedInts[ChessBoard.SyncChange], ChessBoard.ChangeRequest, s + ": the change sent");
        sent = StateText();

        // A late joiner: a reset board receives it, with junk in the history arrays' spare room.
        int before = board.loggedGames;
        board._ClearState();
        board.moveHistory[5] = 123;
        board.notation[5] = "junk";
        board.clockHistory[5] = 7;
        MarkNotifications();
        board._ReceiveState();
        CheckTold(ChessBoard.ChangeRequest, s + ": received");
        Check(board.lastChangeReceived, s + ": the change is marked received", "not marked");
        CheckString(StateText(), sent, s + ": received as sent");
        CheckInt(board.halfmoveClock, 1, s + ": the halfmove clock worked out");
        CheckString(board.moveHistory[5] + "/" + board.notation[5] + "/" + board.clockHistory[5], "0//-1",
            s + ": the spare room emptied");
        CheckLogged(before, 0, s + ": a late joiner logs nothing");

        // A local change isn't marked received, and a rejection isn't sent.
        CheckInt(board.LeaveReplayAs("Bob", false), ChessBoard.InputAccepted, s + ": replay left");
        Check(!board.lastChangeReceived, s + ": a local change isn't marked received", "marked");
        CheckInt(board.PieceDroppedAs("Bob", ChessRules.E7, ChessRules.E5), ChessBoard.InputRejected,
            s + ": a drop from an empty square");
        CheckInt(board.lastChange, ChessBoard.ChangeRejected, s + ": rejected");
        board._PackState();
        CheckInt(board.syncedInts[ChessBoard.SyncChange], ChessBoard.ChangeLeftReplay, s + ": the rejection not sent");

        // A synced string[] must never hold null.
        board.notation[1] = null;
        board._PackState();
        board.notation[1] = "e5";
        string nulls = "";
        for (int i = 0; i < board.syncedNotation.Length; i++) {
            if (board.syncedNotation[i] == null) nulls += " " + i;
        }
        Check(nulls == "", s + ": no null notation sent", "null at" + nulls);
        CheckString(board.syncedNotation[1], "", s + ": a null entry sent as empty");

        // A custom replay position: a free move.
        string c = "sync custom replay";
        SyncGame(ChessArbiter.NoClock, c);
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, c + ": replay entered");
        CheckInt(board._ReplayPieceDroppedAs("Ana", false, ChessRules.D1, ChessRules.H5), ChessBoard.InputAccepted,
            c + ": a free move");
        board._PackState();
        sent = StateText();
        board._ClearState();
        board._ReceiveState();
        CheckString(StateText(), sent, c + ": received as sent");
        Check(board.replayCustom, c + ": custom", "not custom");

        // A history longer than the arrays' starting room: e4 e5 and 128 plies of knight shuffle, no clock.
        string l = "sync long";
        SyncGame(ChessArbiter.NoClock, l);
        KnightShuffle(32, l);
        CheckInt(board.moveCount, 130, l + ": 130 plies");
        board._PackState();
        sent = StateText();
        board._ClearState();
        board._ReceiveState();
        CheckString(StateText(), sent, l + ": received as sent");
        Check(board.moveHistory.Length >= 130, l + ": the history grown", "room for " + board.moveHistory.Length);

        // The game ending elsewhere: logged here once, as it ended there.
        string e = "sync ended";
        SyncGame(ChessArbiter.NoClock, e);
        CheckInt(board.ResignAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, e + ": Bob resigns");
        board._PackState();
        // As this client had it before the update.
        board.lifecycle = ChessBoard.LifecycleInGame;
        board.ending = ChessGame.EndingNone;
        board.winner = ChessRules.NoColor;
        before = board.loggedGames;
        board._ReceiveState();
        CheckLoggedEnding(before, "1-0", "resignation", e);
        before = board.loggedGames;
        board._ReceiveState();
        CheckLogged(before, 0, e + ": the same update again logs nothing");
        board._ClearState();
        board._ReceiveState();
        CheckLogged(before, 0, e + ": a late joiner logs nothing");
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, e + ": a late joiner sees it ended");
        SyncReplaceGame();
        board._ReceiveState();
        CheckLogged(before, 0, e + ": an ended game replaced logs nothing");

        // A game in progress replaced elsewhere (a reset, a takeback), logged here as unfinished.
        string g = "sync replaced";
        SyncGame(ChessArbiter.NoClock, g);
        SyncReplaceGame();
        before = board.loggedGames;
        board._ReceiveState();
        CheckLoggedOnReset(before, "*", "unterminated", g);
        CheckLogHas("1. e4 e5 *", g + ": its moves");
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, g + ": the update taken");

        // ... on time when its flag had fallen, as the reset logged it there: White, to move, loses.
        string f = "sync replaced on time";
        SyncGame(60, f);
        SyncReplaceGame();
        board.turnStart -= 61;
        before = board.loggedGames;
        board._ReceiveState();
        CheckLoggedOnReset(before, "0-1", "time forfeit", f);

        // ... and not at all with no moves.
        string n = "sync replaced, no moves";
        board._ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        board.JoinAs(ChessRules.Black, "Bob");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, n + ": started");
        SyncReplaceGame();
        before = board.loggedGames;
        board._ReceiveState();
        CheckLogged(before, 0, n + ": nothing logged");

        // An update that isn't valid is ignored.
        string v = "sync invalid";
        board._ClearState();
        board.JoinAs(ChessRules.White, "Ana");
        string kept = StateText();
        board._PackState();
        board.syncedInts = new int[3];
        MarkNotifications();
        board._ReceiveState();
        CheckNotTold(v + ": ints too short");
        board._PackState();
        board.syncedMoves = new int[1];
        board._ReceiveState();
        CheckNotTold(v + ": more moves than the count");
        board._PackState();
        board.syncedNotation = null;
        board._ReceiveState();
        CheckNotTold(v + ": no notation");
        board._PackState();
        board.syncedBlackPlayer = null;
        board._ReceiveState();
        CheckNotTold(v + ": a missing string");
        CheckString(StateText(), kept, v + ": the state kept");

        board.RemoveListener(this);
        board._ClearState();
        board._PackState();
    }

    // ---- 12.9 playing on untimed after a flag fall ----

    // A 60 s game between Ana and Bob with these settings: 1. e4 at 1001, leaving White 59 s; Black's flag falls at
    // 1061.
    void FlagFallAfterE4(int settings, string name) {
        StartedGame(60, 0, 1000);
        board.gameSettings = settings;
        Play(ChessRules.E2, ChessRules.E4, 1001, name + ": 1. e4");
    }

    void TestPlayOn() {
        NothingForced();
        board.AddListener(this);
        int playOn = ChessArbiter.SettingPlayOnAfterFlagFall;

        // Off: the game ends as before.
        string o = "play-on off";
        FlagFallAfterE4(0, o);
        Check(board._CheckFlagFallAt(1100), o + ": the flag falls", "no flag fall");
        CheckEnded(ChessGame.EndingTimeForfeit, ChessRules.White, "1-0", o);
        Check(!board.ClocksFromPreviousGame(), o + ": no clocks from a previous game", "set");

        // On: the timed game is logged as it ended, then plays on untimed at once.
        string s = "play-on";
        FlagFallAfterE4(playOn, s);
        string timedId = board.gameId;
        int before = board.loggedGames;
        MarkNotifications();
        Check(board._CheckFlagFallAt(1100), s + ": the flag falls", "no flag fall");
        CheckTold(ChessBoard.ChangeEnded, s + ": told as ended");
        CheckLogged(before, 1, s + ": the timed game logged once");
        CheckLogHas("[Result \"1-0\"]\n", s + ": logged 1-0");
        CheckLogHas("[TimeControl \"60\"]\n", s + ": logged with its time control");
        CheckLogHas("[GameID \"" + timedId + "\"]\n", s + ": logged with its own ID");
        CheckLogHas("[Termination \"time forfeit\"]\n", s + ": logged as a time forfeit");
        CheckLogHas("1. e4 { [%clk 0:00:59] } 1-0\n", s + ": logged with its clocks");
        CheckInGame(s + ": playing on");
        Check(board.gameId.Length > 0 && board.gameId != timedId, s + ": a new Game ID", "\"" + board.gameId + "\"");
        CheckString(board.continuedFrom, timedId + "/1", s + ": ContinuedFrom the flag fall's ply");
        Check(!board.HasClock() && !board.IsClockRunning(), s + ": no clock", "clock");
        Check(board.ClocksFromPreviousGame(), s + ": the clocks are the previous game's", "not");
        CheckTimes(59000, 0, 1e6, s + ": the times at the flag fall");
        CheckInt(board._FlaggedColorAt(1e9), ChessRules.NoColor, s + ": no flag can fall");
        CheckInt(board.clockHistory[0], -1, s + ": the timed game's clocks emptied");
        CheckInt(board.moveCount, 1, s + ": the moves kept");
        CheckSeats("Ana", "Bob", s);

        // Moves are played with no clock; the untimed game is logged as any continued game when it ends.
        Play(ChessRules.E7, ChessRules.E5, 5000, s + ": 1... e5");
        CheckInt(board.clockHistory[1], -1, s + ": e5 has no clock");
        CheckTimes(59000, 0, 1e7, s + ": the times unchanged by a move");
        Check(!board._CheckFlagFallAt(1e9), s + ": no flag fall later", "flag fell");
        string pgn = board.GamePgn();
        Check(pgn.Contains("[TimeControl \"-\"]\n") && !pgn.Contains("[%clk"), s + ": its PGN is untimed", pgn);
        string playOnId = board.gameId;
        before = board.loggedGames;
        CheckInt(board.ResignAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, s + ": Bob resigns");
        CheckLoggedEnding(before, "1-0", "resignation", s + ": resigned");
        CheckLogHas("[GameID \"" + playOnId + "\"]\n", s + ": logged with its new ID");
        CheckLogHas("[ContinuedFrom \"" + timedId + "/1\"]\n", s + ": logged with ContinuedFrom");
        CheckLogHas("[TimeControl \"-\"]\n", s + ": logged untimed");
        CheckLogHas("\n\n1. e4 e5 1-0\n", s + ": logged without clocks");
        Check(board.ClocksFromPreviousGame(), s + ": the previous game's clocks once ended", "not");
        CheckTimes(59000, 0, 1e7, s + ": the times once ended");
        before = board.loggedGames;
        CheckInt(board.ResetBoardAs("Ana", false), ChessBoard.InputAccepted, s + ": reset");
        CheckLogged(before, 0, s + ": the reset logs nothing more");
        Check(!board.ClocksFromPreviousGame() && board.flagFallPly == -1, s + ": the reset clears it", "kept");

        // With no move since the flag fell, a reset or a resignation logs nothing; after a move, a reset logs it.
        string n = "play-on, no move";
        FlagFallAfterE4(playOn, n);
        board._CheckFlagFallAt(1100);
        before = board.loggedGames;
        CheckInt(board.ResetBoardAs("Bob", false), ChessBoard.InputAccepted, n + ": reset");
        CheckLogged(before, 0, n + ": reset: nothing logged");
        FlagFallAfterE4(playOn, n);
        board._CheckFlagFallAt(1100);
        before = board.loggedGames;
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, n + ": Ana resigns");
        CheckLogged(before, 0, n + ": resigned: nothing logged");
        FlagFallAfterE4(playOn, n);
        board._CheckFlagFallAt(1100);
        Play(ChessRules.E7, ChessRules.E5, 5000, n + ": 1... e5");
        before = board.loggedGames;
        board.ResetBoardAs("Bob", false);
        CheckLoggedOnReset(before, "*", "unterminated", n + ": reset after a move");
        CheckLogHas("1. e4 e5 *", n + ": reset after a move: its moves");

        // A drawn time forfeit plays on too (the side out of time may still mate); a dead position doesn't, with the
        // dead-position rule on.
        string d = "play-on, drawn";
        StartedGame(60, 0, 1000);
        board.gameSettings = playOn | ChessArbiter.SettingDeadPosition;
        SetPosition("k7/8/8/8/8/8/8/KQR5 w - - 0 1");
        Check(board._CheckFlagFallAt(1060), d + ": White's flag falls", "no flag fall");
        CheckInGame(d + ": playing on");
        CheckTimes(0, 60000, 1e6, d + ": White out of time");
        string v = "play-on, dead position";
        StartedGame(60, 0, 1000);
        board.gameSettings = playOn | ChessArbiter.SettingDeadPosition;
        SetPosition("k7/8/8/8/8/8/8/K5n1 w - - 0 1");
        Check(board._CheckFlagFallAt(1060), v + ": White's flag falls", "no flag fall");
        CheckEnded(ChessGame.EndingTimeForfeit, ChessRules.NoColor, "1/2-1/2", v + ": no play-on");
        Check(!board.ClocksFromPreviousGame(), v + ": no clocks from a previous game", "set");

        // In replay, the game plays on in replay.
        string r = "play-on in replay";
        FlagFallAfterE4(playOn, r);
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, r + ": replay");
        CheckInt(board.StepToAs("Ana", false, 0), ChessBoard.InputAccepted, r + ": step to the start");
        Check(board._CheckFlagFallAt(1100), r + ": the flag falls", "no flag fall");
        CheckInGame(r + ": playing on");
        Check(board.replayMode && board.replayIndex == 0, r + ": replay kept at the start",
            "replay " + board.replayMode + " at " + board.replayIndex);

        // A takeback in the game played on, back before the flag fell: still untimed, the times kept.
        string t = "play-on takeback";
        StartedGame(60, 0, 1000);
        board.gameSettings = playOn | ChessArbiter.SettingTakebacks;
        Play(ChessRules.E2, ChessRules.E4, 1001, t + ": 1. e4");
        Play(ChessRules.E7, ChessRules.E5, 1002, t + ": 1... e5");
        Play(ChessRules.G1, ChessRules.F3, 1003, t + ": 2. Nf3");
        Check(board._CheckFlagFallAt(1100), t + ": Black's flag falls", "no flag fall");
        playOnId = board.gameId;
        Play(ChessRules.B8, ChessRules.C6, 5000, t + ": 2... Nc6");
        before = TakeBackTo(1, t);
        CheckLogged(before, 1, t + ": the game played on logged");
        CheckLogHas("[Result \"*\"]\n", t + ": logged unfinished");
        CheckLogHas("[GameID \"" + playOnId + "\"]\n", t + ": logged with its ID");
        CheckInGame(t + ": taken back");
        CheckInt(board.moveCount, 1, t + ": back to ply 1");
        CheckString(board.continuedFrom, playOnId + "/1", t + ": ContinuedFrom the game played on");
        Check(!board.HasClock() && board.ClocksFromPreviousGame(), t + ": still untimed", "clock or no times");
        CheckTimes(58000, 0, 1e6, t + ": the times kept");
        CheckInt(board.flagFallPly, -1, t + ": no longer the flag fall's own game");
        string u = "play-on takeback, no move";
        StartedGame(60, 0, 1000);
        board.gameSettings = playOn | ChessArbiter.SettingTakebacks;
        Play(ChessRules.E2, ChessRules.E4, 1001, u + ": 1. e4");
        Play(ChessRules.E7, ChessRules.E5, 1002, u + ": 1... e5");
        Play(ChessRules.G1, ChessRules.F3, 1003, u + ": 2. Nf3");
        board._CheckFlagFallAt(1100);
        before = TakeBackTo(1, u);
        CheckLogged(before, 0, u + ": nothing logged");

        // Sync: the game played on arrives at a client that had the timed game in progress. It's logged there on time,
        // as the sender logged it, even with this client's clock not yet run out.
        string y = "play-on sync";
        FlagFallAfterE4(playOn, y);
        timedId = board.gameId;
        board._CheckFlagFallAt(1100);
        playOnId = board.gameId;
        board._PackState();
        // As this client had it before the update.
        board.gameId = timedId;
        board.continuedFrom = "";
        board.baseTime = 60;
        board.whiteTime = 59000;
        board.blackTime = 60000;
        board.turnStart = board._ServerTime() + 1000;
        board.clockHistory[0] = 59000;
        board.untimedAfterFlagFall = false;
        board.flagFallPly = -1;
        before = board.loggedGames;
        board._ReceiveState();
        CheckLoggedOnReset(before, "1-0", "time forfeit", y);
        CheckLogHas("[GameID \"" + timedId + "\"]\n", y + ": logged with the timed game's ID");
        CheckInGame(y + ": playing on");
        CheckString(board.gameId, playOnId, y + ": the new game taken");
        Check(board.ClocksFromPreviousGame() && board.flagFallPly == 1, y + ": playing on after the flag fall",
            "untimed " + board.untimedAfterFlagFall + ", ply " + board.flagFallPly);
        board._PackState();
        string sent = StateText();
        board._ClearState();
        board._ReceiveState();
        CheckString(StateText(), sent, y + ": received as sent");
        before = board.loggedGames;
        board._ReceiveState();
        CheckLogged(before, 0, y + ": the same update again logs nothing");
        SyncReplaceGame();
        board._ReceiveState();
        CheckLogged(before, 0, y + ": a reset arriving with no move since the flag fell logs nothing");

        board.RemoveListener(this);
        board._ClearState();
        board._PackState();
    }

    // Steps the replay to ply, White asks to take back to there and Black accepts. Returns the games logged before
    // the accept.
    int TakeBackTo(int ply, string name) {
        CheckInt(board.EnterReplayAs("Ana", false), ChessBoard.InputAccepted, name + ": replay");
        CheckInt(board.StepToAs("Ana", false, ply), ChessBoard.InputAccepted, name + ": step to ply " + ply);
        CheckInt(board.RequestTakebackAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, name + ": asked");
        int logged = board.loggedGames;
        CheckInt(board.AcceptTakebackAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, name + ": accepted");
        return logged;
    }

    // ---- 13.6 piece sets ----

    // The synced piece set indexes: 0 on a reset board, set (ChangePieceSet), sent and received, refused out of range,
    // for no colour and from someone _MayOwnAs refuses, back to 0 on a reset; a join's preferred set put on both sides
    // of an empty board (a loaded one too), on the joined colour alone with the other seat held (also when the joiner
    // holds it), none for -1 or out of range; the local Join reading the arbiter's preferredPieceSet. With the arbiter's
    // list made three entries (copies of its first), put back afterwards.
    void TestPieceSets() {
        string s = "piece sets";
        ChessArbiter arbiter = board.arbiter;
        if (!Utilities.IsValid(arbiter)) {
            Check(false, s + ": the board's arbiter assigned", "the board has no arbiter");
            return;
        }
        NothingForced();
        int white = ChessRules.White;
        int black = ChessRules.Black;
        ChessPieceSet[] savedSets = arbiter.pieceSets;
        int savedPreferred = arbiter.preferredPieceSet;
        ChessPieceSet first = arbiter.PieceSet(0);
        arbiter.pieceSets = new ChessPieceSet[] { first, first, first };
        arbiter.preferredPieceSet = -1;

        board._ClearState();
        CheckSets(0, 0, s + ": a reset board");
        CheckInt(board.SetPieceSetAs("Ana", false, white, 2), ChessBoard.InputAccepted, s + ": White set 2");
        CheckInt(board.lastChange, ChessBoard.ChangePieceSet, s + ": told as ChangePieceSet");
        CheckSets(2, 0, s + ": White 2");
        CheckInt(board.SetPieceSetAs("Ana", false, black, 1), ChessBoard.InputAccepted, s + ": Black set 1");
        CheckSets(2, 1, s + ": Black 1");
        CheckInt(board.PieceSetIndex(ChessRules.NoColor), -1, s + ": no colour's index -1");

        // Sent and received.
        board._PackState();
        CheckInt(board.syncedInts[ChessBoard.SyncWhitePieceSet] * 10 + board.syncedInts[ChessBoard.SyncBlackPieceSet],
            21, s + ": packed (White * 10 + Black)");
        board.whitePieceSet = 0;
        board.blackPieceSet = 0;
        board._ReceiveState();
        CheckSets(2, 1, s + ": unpacked");

        // Refused: out of range, no colour; nothing changes.
        CheckInt(board.SetPieceSetAs("Ana", false, white, 3), ChessBoard.InputRejected, s + ": index 3 refused");
        CheckInt(board.SetPieceSetAs("Ana", false, black, -1), ChessBoard.InputRejected, s + ": index -1 refused");
        CheckInt(board.SetPieceSetAs("Ana", false, ChessRules.NoColor, 0), ChessBoard.InputRejected,
            s + ": no colour refused");
        Check(!board.CanSetPieceSetAs("Ana", false, 2, 0) && !board.CanSetPieceSetAs("", false, white, 0),
            s + ": colour 2 and no name refused", "allowed");
        CheckSets(2, 1, s + ": refused: unchanged");

        // A reset puts both back to 0.
        CheckInt(board.JoinAs(white, "Ana"), ChessBoard.InputAccepted, s + ": Ana joins");
        CheckSets(2, 1, s + ": a join without a preference keeps the sets");
        CheckInt(board.ResetBoardAs("Ana", false), ChessBoard.InputAccepted, s + ": reset");
        CheckSets(0, 0, s + ": reset");

        // Who may: as _MayOwnAs, so a board locked to its loader refuses a bystander and allows the loader and
        // organizers.
        if (Utilities.IsValid(importer) && siggiVsNuka != null) {
            LoadNukaAs("Lena", s + ": locked");
            board.gameNumbers[ChessArbiter.NumberResetAvailableTime] = 120;
            Check(!board.CanSetPieceSetAs("Cleo", false, white, 1), s + ": locked: Cleo may not", "she may");
            CheckInt(board.SetPieceSetAs("Cleo", false, white, 1), ChessBoard.InputRejected, s + ": locked: Cleo refused");
            CheckSets(0, 0, s + ": locked: Cleo's refused");
            CheckInt(board.SetPieceSetAs("Lena", false, white, 1), ChessBoard.InputAccepted, s + ": locked: Lena may");
            CheckInt(board.SetPieceSetAs("Olaf", true, black, 2), ChessBoard.InputAccepted,
                s + ": locked: an organizer may");
            CheckSets(1, 2, s + ": locked: Lena's and the organizer's");
        } else {
            Check(false, s + ": importer and Siggi-vs-Nuka assigned", "assign them in the Inspector");
        }

        // A join's preferred set: on both sides of an empty board, in the same change.
        string j = s + ": preferred";
        board._ClearState();
        CheckInt(board.JoinAs(white, "Ana", 1), ChessBoard.InputAccepted, j + ": Ana joins an empty board with 1");
        CheckInt(board.lastChange, ChessBoard.ChangeJoined, j + ": told as the join");
        CheckSets(1, 1, j + ": an empty board: both sides");
        // The other seat held: the joined colour alone.
        CheckInt(board.JoinAs(black, "Bob", 2), ChessBoard.InputAccepted, j + ": Bob joins Black with 2");
        CheckSets(1, 2, j + ": the other seat held: Black alone");
        // A refused join, or a seat already held by the joiner, changes nothing.
        CheckInt(board.JoinAs(black, "Cleo", 0), ChessBoard.InputRejected, j + ": Cleo refused");
        CheckInt(board.JoinAs(white, "Ana", 0), ChessBoard.InputAccepted, j + ": Ana's own seat again");
        CheckSets(1, 2, j + ": refused and repeated joins: unchanged");
        // The second seat of a player already seated: that colour alone.
        board._ClearState();
        board.JoinAs(white, "Ana", 1);
        CheckInt(board.JoinAs(black, "Ana", 2), ChessBoard.InputAccepted, j + ": Ana takes Black too, with 2");
        CheckSets(1, 2, j + ": the joiner's second seat: Black alone");
        // -1 and out of range: nothing.
        board._ClearState();
        board.SetPieceSetAs("Ana", false, white, 2);
        CheckInt(board.JoinAs(white, "Ana", -1), ChessBoard.InputAccepted, j + ": Ana joins with -1");
        CheckSets(2, 0, j + ": -1 changes nothing");
        CheckInt(board.JoinAs(black, "Bob", 3), ChessBoard.InputAccepted, j + ": Bob joins with 3");
        CheckSets(2, 0, j + ": out of range changes nothing");
        // An empty loaded board: both sides.
        if (Utilities.IsValid(importer) && siggiVsNuka != null) {
            LoadNukaAs("", j + ": loaded");
            CheckInt(board.JoinAs(black, "Ana", 2), ChessBoard.InputAccepted, j + ": Ana joins the loaded board, 2");
            CheckSets(2, 2, j + ": an empty loaded board: both sides");
        }

        // The local Join reads the arbiter's preferredPieceSet.
        string l = s + ": local";
        Check(!arbiter.SetPreferredPieceSet(3) && !arbiter.SetPreferredPieceSet(-2), l + ": preference 3 and -2 refused",
            "accepted");
        CheckInt(arbiter.PreferredPieceSet(), -1, l + ": refused: still none");
        Check(arbiter.SetPreferredPieceSet(2), l + ": preference 2 accepted", "refused");
        CheckInt(arbiter.PreferredPieceSet(), 2, l + ": preference 2");
        board._ClearState();
        CheckInt(board.JoinWhite(), ChessBoard.InputAccepted, l + ": the local player joins White");
        CheckSets(2, 2, l + ": the preference on both sides");
        Check(arbiter.SetPreferredPieceSet(-1), l + ": preference -1 accepted", "refused");
        CheckInt(board.JoinBlack(), ChessBoard.InputAccepted, l + ": the local player joins Black with none");
        CheckSets(2, 2, l + ": no preference: unchanged");

        arbiter.pieceSets = savedSets;
        arbiter.preferredPieceSet = savedPreferred;
        board._ClearState();
    }

    void CheckSets(int whiteSet, int blackSet, string name) {
        CheckString(board.whitePieceSet + "|" + board.blackPieceSet, whiteSet + "|" + blackSet,
            name + ": White|Black piece sets");
    }

    // ---- 8.4 ownership ----

    // Who may take the board (_MayOwnAs), with each player's presence given; then _MayOwn for the local player, alone
    // in the instance.
    void TestOwnership() {
        NothingForced();
        string o = "ownership";
        int resetTime = ChessArbiter.NumberResetAvailableTime;

        board._ClearState();
        double now = board._ServerTime();
        Check(board._MayOwnAs("Cleo", false, true, now), o + ": anyone on a reset board", "refused");
        Check(!board._IsIdleForResetAt(now + 100000), o + ": a reset board is never idle for reset", "idle");
        board.JoinAs(ChessRules.White, "Ana");
        Check(board._MayOwnAs("Cleo", false, true, now), o + ": anyone while a seat is empty", "refused");

        // Every change sent stamps the activity time; a rejection doesn't.
        double before = board._ServerTime();
        board.JoinAs(ChessRules.Black, "Bob");
        double after = board._ServerTime();
        Check(board.lastActivity >= before && board.lastActivity <= after, o + ": a change stamps the activity",
            "activity " + board.lastActivity + ", change between " + before + " and " + after);
        double t = board.lastActivity;
        CheckInt(board.JoinAs(ChessRules.Black, "Cleo"), ChessBoard.InputRejected, o + ": seat taken");
        Check(board.lastActivity == t, o + ": a rejection doesn't stamp the activity", "activity " + board.lastActivity);

        // The lobby with both players present.
        board.gameNumbers[resetTime] = 120;
        CheckOwners(t, o + ": lobby");
        Check(board._MayOwnAs("Cleo", false, false, t), o + ": anyone while a player is away", "refused");
        Check(!board._MayOwnAs("Cleo", false, true, t + 119.9), o + ": refused before the reset-available time",
            "allowed");
        Check(board._MayOwnAs("Cleo", false, true, t + 120), o + ": anyone once idle for the reset-available time",
            "refused");
        Check(board._IsIdleForResetAt(t + 120), o + ": idle for reset", "not idle");
        board.gameNumbers[resetTime] = ChessArbiter.Never;
        Check(!board._MayOwnAs("Cleo", false, true, t + 10000000), o + ": never: refused however long idle",
            "allowed");
        Check(!board._IsIdleForResetAt(t + 10000000), o + ": never: not idle for reset", "idle");
        board.gameNumbers[resetTime] = 0;
        Check(board._MayOwnAs("Cleo", false, true, t), o + ": 0: anyone at once", "refused");

        // A game in progress, then ended.
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, o + ": started");
        board.gameNumbers[resetTime] = 120;
        t = board.lastActivity;
        CheckOwners(t, o + ": in game");
        Drop("Ana", ChessRules.E2, ChessRules.E4, o);
        Check(board.lastActivity >= t, o + ": a move stamps the activity", "activity " + board.lastActivity);
        t = board.lastActivity;
        Check(!board._MayOwnAs("Cleo", false, true, t + 119.9), o + ": in game: refused before the move's time + 120",
            "allowed");
        Check(board._MayOwnAs("Cleo", false, true, t + 120), o + ": in game: anyone once idle", "refused");
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, o + ": resigned");
        t = board.lastActivity;
        CheckOwners(t, o + ": ended");
        Check(board._MayOwnAs("Cleo", false, true, t + 120), o + ": ended: anyone once idle", "refused");

        // The local player, alone: a seat held by someone absent lets anyone take the board.
        VRCPlayerApi local = Networking.LocalPlayer;
        Check(!board._MayOwn(null), o + ": no player refused", "allowed");
        LobbyWithTwoPlayers();
        Check(!board._BothPlayersPresent(), o + ": Ana and Bob aren't here", "present");
        Check(board._MayOwn(local), o + ": the local player while Ana and Bob are away", "refused");
        board._ClearState();
        board.JoinAs(ChessRules.White, localName);
        board.JoinAs(ChessRules.Black, "Bob");
        Check(!board._BothPlayersPresent(), o + ": Bob isn't here", "present");
        board._ClearState();
        board.JoinAs(ChessRules.White, localName);
        board.JoinAs(ChessRules.Black, localName);
        Check(board._BothPlayersPresent(), o + ": the local player in both seats is present", "absent");
        Check(board._MayOwn(local), o + ": the local player in both seats", "refused");
        Check(board.OnOwnershipRequest(local, local) && !board.OnOwnershipRequest(local, null),
            o + ": OnOwnershipRequest answers MayOwn", "wrong answer");
        board._ClearState();
    }

    // With both players present at server time now (the last activity): the players and an organizer may own the
    // board, a bystander may not, names compared exactly.
    void CheckOwners(double now, string name) {
        Check(board._MayOwnAs("Ana", false, true, now), name + ": the white player", "refused");
        Check(board._MayOwnAs("Bob", false, true, now), name + ": the black player", "refused");
        Check(board._MayOwnAs("Cleo", true, true, now), name + ": an organizer", "refused");
        Check(!board._MayOwnAs("Cleo", false, true, now), name + ": a bystander refused", "allowed");
        Check(!board._MayOwnAs("ana", false, true, now) && !board._MayOwnAs("Ana ", false, true, now),
            name + ": names compared exactly", "allowed");
        Check(!board._MayOwnAs(null, false, true, now) && !board._MayOwnAs("", false, true, now),
            name + ": no name refused", "allowed");
    }
    // ---- 8.5 inactivity ----

    void TestInactivity() {
        NothingForced();
        string s = "inactivity";
        int autoTime = ChessArbiter.NumberAutoResetTime;
        board.AddListener(this);
        MarkNotifications();

        // A reset board is never idle, and a touch on it counts for nobody.
        board._ClearState();
        double now = board._ServerTime();
        Check(!board._IsIdleForAutoResetAt(now + 10000000), s + ": a reset board never resets itself", "idle");
        Check(!board._CheckAutoResetAt(now + 10000000), s + ": nothing to auto-reset on a reset board", "reset");
        Check(!board._TouchAsAt("Ana", true, now + 100), s + ": a touch on a reset board doesn't count", "sent");

        // The reset button for a bystander, once the board is idle for the reset-available time.
        int before = board.loggedGames;
        IdleTestGame(120, 600);
        Check(!board.CanResetAs("Cleo", false), s + ": a bystander can't reset an active board", "allowed");
        CheckInt(board.ResetBoardAs("Cleo", false), ChessBoard.InputRejected, s + ": bystander's reset refused");
        board.lastActivity = board._ServerTime() - 119;
        Check(!board.CanResetAs("Cleo", false), s + ": nor before the reset-available time", "allowed");
        board.lastActivity = board._ServerTime() - 121;
        Check(board.CanResetAs("Cleo", false), s + ": a bystander may reset once idle", "refused");
        CheckInt(board.ResetBoardAs("Cleo", false), ChessBoard.InputAccepted, s + ": bystander's reset");
        CheckLoggedOnReset(before, "*", "unterminated", s + ": bystander's reset");
        CheckCleared(s + ": bystander's reset");
        IdleTestGame(ChessArbiter.Never, 600);
        board.lastActivity = board._ServerTime() - 10000000;
        Check(!board.CanResetAs("Cleo", false), s + ": never: a bystander can't reset however long idle", "allowed");
        Check(board.CanResetAs("Ana", false) && board.CanResetAs("Cleo", true),
            s + ": never: the players and organizers still may", "refused");

        // The automatic reset: once, at the auto-reset time, logging the game in progress as unfinished.
        before = board.loggedGames;
        IdleTestGame(120, 600);
        double t = board.lastActivity;
        MarkNotifications();
        Check(!board._IsIdleForAutoResetAt(t + 599.9), s + ": not idle before the auto-reset time", "idle");
        Check(!board._CheckAutoResetAt(t + 599.9), s + ": no auto-reset before its time", "reset");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, s + ": still in game before the auto-reset time");
        CheckNotTold(s + ": before the auto-reset time");
        Check(board._IsIdleForAutoResetAt(t + 600), s + ": idle at the auto-reset time", "not idle");
        Check(board._CheckAutoResetAt(t + 600), s + ": auto-reset at its time", "not reset");
        CheckTold(ChessBoard.ChangeReset, s + ": auto-reset");
        CheckCleared(s + ": auto-reset");
        CheckLoggedOnReset(before, "*", "unterminated", s + ": auto-reset");
        board._PackState();
        CheckInt(board.syncedInts[ChessBoard.SyncChange], ChessBoard.ChangeReset, s + ": the auto-reset is sent");
        Check(!board._CheckAutoResetAt(t + 100000), s + ": auto-reset only once", "reset again");
        CheckLogged(before, 1, s + ": auto-reset logged once");

        // Never, 0, an ended game, a fallen flag and the lobby.
        IdleTestGame(120, ChessArbiter.Never);
        t = board.lastActivity;
        Check(!board._CheckAutoResetAt(t + 10000000), s + ": never: no auto-reset however long idle", "reset");
        CheckInt(board.lifecycle, ChessBoard.LifecycleInGame, s + ": never: still in game");
        IdleTestGame(0, 0);
        Check(board._CheckAutoResetAt(board.lastActivity), s + ": 0: auto-reset at once", "not reset");
        before = board.loggedGames;
        IdleTestGame(120, 600);
        board.ResignAs(ChessRules.Black, "Bob");
        CheckLogged(before, 1, s + ": ended: logged on resigning");
        Check(board._CheckAutoResetAt(board.lastActivity + 600), s + ": an ended game resets itself", "not reset");
        CheckLogged(before, 1, s + ": ended: not logged again");
        before = board.loggedGames;
        IdleTestGame(120, 600);
        FlagFallen();
        Check(board._CheckAutoResetAt(board.lastActivity + 600), s + ": flag fallen: auto-reset", "not reset");
        CheckLoggedOnReset(before, "0-1", "time forfeit", s + ": flag fallen");
        LobbyWithTwoPlayers();
        board.gameNumbers[autoTime] = 600;
        Check(board._CheckAutoResetAt(board.lastActivity + 600), s + ": the lobby resets itself", "not reset");
        CheckCleared(s + ": the lobby");

        // Touches: the side to move's, sent at most once per ActivityThrottle seconds and not told locally.
        IdleTestGame(120, 600);
        t = board.lastActivity;
        MarkNotifications();
        Check(!board._TouchAsAt("Ana", false, t + ChessBoard.ActivityThrottle - 0.1),
            s + ": a touch within the throttle isn't sent", "sent");
        Check(board.lastActivity == t, s + ": a touch within the throttle doesn't stamp", "activity " + board.lastActivity);
        Check(board._TouchAsAt("Ana", false, t + ChessBoard.ActivityThrottle), s + ": a touch after the throttle is sent",
            "not sent");
        Check(board.lastActivity == t + ChessBoard.ActivityThrottle, s + ": the touch stamps the activity",
            "activity " + board.lastActivity);
        CheckNotTold(s + ": a touch");
        board._PackState();
        CheckInt(board.syncedInts[ChessBoard.SyncChange], ChessBoard.ChangeActivity, s + ": the touch sent as activity");
        Check(board.syncedLastActivity == t + ChessBoard.ActivityThrottle, s + ": the touch's time sent",
            "sent " + board.syncedLastActivity);
        Check(!board._TouchAsAt("Ana", false, t + ChessBoard.ActivityThrottle + 1), s + ": the throttle starts again",
            "sent");
        t = board.lastActivity + 100;
        Check(!board._TouchAsAt("Bob", false, t), s + ": the player not to move doesn't count", "sent");
        Check(!board._TouchAsAt("Cleo", false, t) && !board._TouchAsAt("Cleo", true, t),
            s + ": a bystander or an organizer outside replay doesn't count", "sent");
        Check(board._TouchAsAt("Ana", false, t), s + ": the side to move counts", "not sent");
        Check(!board._IsIdleForResetAt(t + 119), s + ": a touch keeps the board from going idle", "idle");

        // Received: every client gets the new activity time, and the change says nothing else happened.
        string sent = SeatedGameText();
        before = board.loggedGames;
        board._PackState();
        board.lastActivity = 0;
        MarkNotifications();
        board._ReceiveState();
        CheckTold(ChessBoard.ChangeActivity, s + ": activity received");
        Check(board.lastChangeReceived, s + ": activity marked received", "not marked");
        Check(board.lastActivity == t, s + ": the activity time received", "activity " + board.lastActivity);
        CheckString(SeatedGameText(), sent, s + ": nothing else changed");
        CheckLogged(before, 0, s + ": activity received: nothing logged");

        // The piece inputs touch: picked up, rejected or dropped back, by the side to move.
        CheckTouched("Ana", true, ChessRules.G1, ChessRules.G1, s + ": pickup and drop back by the side to move");
        CheckTouched("Ana", true, ChessRules.D7, ChessRules.D5, s + ": rejected pickup and drop by the side to move");
        CheckTouched("Bob", false, ChessRules.D7, ChessRules.D5, s + ": the other player's pickup and drop");
        board.lastActivity = board._ServerTime() - 10;
        CheckInt(board.PromotionChosenAs("Ana", ChessRules.G1, ChessRules.F3, ChessRules.Queen),
            ChessBoard.InputRejected, s + ": not a promotion");
        Check(board.lastActivity >= board._ServerTime() - 1, s + ": a promotion choice touches",
            "activity " + board.lastActivity);

        // In replay, whoever may work the replay controls.
        CheckInt(board.EnterReplayAs("Bob", false), ChessBoard.InputAccepted, s + ": replay entered");
        CheckTouched("Bob", true, ChessRules.D7, ChessRules.D7, s + ": replay: the player not to move");
        CheckTouched("Cleo", false, ChessRules.D7, ChessRules.D7, s + ": replay: a bystander");
        Check(board._TouchAsAt("Cleo", true, board.lastActivity + 100), s + ": replay: an organizer counts",
            "not sent");

        // Lobby and ended: nothing to touch.
        LobbyWithTwoPlayers();
        Check(!board._TouchAsAt("Ana", false, board.lastActivity + 100), s + ": the lobby: a touch doesn't count",
            "sent");
        IdleTestGame(120, 600);
        board.ResignAs(ChessRules.White, "Ana");
        Check(!board._TouchAsAt("Bob", false, board.lastActivity + 100), s + ": ended: a touch doesn't count", "sent");

        board.RemoveListener(this);
        board._ClearState();
    }

    // A game between Ana and Bob with no clock, 1. e4 e5 played (White, Ana, to move), with these inactivity times in
    // the snapshot.
    void IdleTestGame(int resetSeconds, int autoSeconds) {
        StartedGame(ChessArbiter.NoClock, 0, 0);
        Drop("Ana", ChessRules.E2, ChessRules.E4, "inactivity: game");
        Drop("Bob", ChessRules.E7, ChessRules.E5, "inactivity: game");
        board.gameNumbers[ChessArbiter.NumberResetAvailableTime] = resetSeconds;
        board.gameNumbers[ChessArbiter.NumberAutoResetTime] = autoSeconds;
    }

    // name picks up the piece on from, then drops it on to, each with the board idle for 10 s before: whether each
    // stamped the activity (touched) or left it as it was.
    void CheckTouched(string name, bool touched, int from, int to, string label) {
        double old = board._ServerTime() - 10;
        board.lastActivity = old;
        board.PiecePickedUpAs(name, from);
        CheckStamped(touched, old, label + ": pickup");
        board.lastActivity = old;
        board.PieceDroppedAs(name, from, to);
        CheckStamped(touched, old, label + ": drop");
    }

    void CheckStamped(bool touched, double old, string label) {
        if (touched) {
            Check(board.lastActivity >= old + 9, label + " touches", "activity " + board.lastActivity);
        } else {
            Check(board.lastActivity == old, label + " doesn't touch", "activity " + board.lastActivity);
        }
    }

    // ---- 9.2 touching the pieces ----

    void TestTouchPieces() {
        NothingForced();
        string t = "touch";
        board._ClearState();
        CheckTouch(true, true, true, t + ": a reset board, anyone");
        Check(!board.MayTouchPiecesAs("", false) && !board.MayTouchPiecesAs(null, true), t + ": no name refused",
            "allowed");

        board.JoinAs(ChessRules.White, "Ana");
        CheckTouch(true, false, false, t + ": lobby with one seat");
        Check(!board.MayTouchPiecesAs("Cleo", true), t + ": lobby: an organizer isn't a player", "allowed");
        board.JoinAs(ChessRules.Black, "Bob");
        CheckTouch(true, true, false, t + ": lobby");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, t + ": started");
        CheckTouch(true, true, false, t + ": in game, White to move");
        Drop("Ana", ChessRules.E2, ChessRules.E4, t);
        CheckTouch(true, true, false, t + ": in game, Black to move");
        Check(!board.MayTouchPiecesAs("Cleo", true), t + ": in game: an organizer isn't a player", "allowed");
        Check(!board.MayTouchPiecesAs("ana", false) && !board.MayTouchPiecesAs("Ana ", false),
            t + ": names compared exactly", "allowed");

        // Replay: whoever may make free moves, organizers included.
        CheckInt(board.EnterReplayAs("Bob", false), ChessBoard.InputAccepted, t + ": replay entered");
        CheckTouch(true, true, false, t + ": in replay");
        Check(board.MayTouchPiecesAs("Cleo", true), t + ": in replay: an organizer", "refused");
        CheckInt(board.LeaveReplayAs("Bob", false), ChessBoard.InputAccepted, t + ": replay left");
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, t + ": resigned");
        CheckTouch(true, true, false, t + ": ended");

        // A loaded game nobody has joined: in replay, so anyone; once seated, the players and organizers.
        LoadText(siggiVsNuka.text, t + ": Siggi-vs-Nuka");
        CheckTouch(true, true, true, t + ": loaded, nobody seated");
        board.JoinAs(ChessRules.White, "Ana");
        CheckTouch(true, false, false, t + ": loaded, Ana seated");
        Check(board.MayTouchPiecesAs("Cleo", true), t + ": loaded, Ana seated: an organizer", "refused");

        // The local player, alone: through the player forms.
        VRCPlayerApi local = Networking.LocalPlayer;
        board._ClearState();
        Check(board.MayTouchPieces() && board.PlayerMayTouchPieces(local), t + ": the local player on a reset board",
            "refused");
        Check(!board.PlayerMayTouchPieces(null), t + ": no player refused", "allowed");
        LobbyWithTwoPlayers();
        Check(!board.MayTouchPieces() && !board.PlayerMayTouchPieces(local),
            t + ": the local player, not seated, in the lobby", "allowed");
        board._ClearState();
        board.JoinAs(ChessRules.Black, localName);
        Check(board.MayTouchPieces() && board.PlayerMayTouchPieces(local), t + ": the local player seated", "refused");
        board._ClearState();
    }

    // Whether Ana (White, if seated), Bob (Black, if seated) and the bystander Cleo may touch the pieces.
    void CheckTouch(bool ana, bool bob, bool cleo, string name) {
        Check(board.MayTouchPiecesAs("Ana", false) == ana, name + ": Ana " + (ana ? "may" : "may not"),
            ana ? "refused" : "allowed");
        Check(board.MayTouchPiecesAs("Bob", false) == bob, name + ": Bob " + (bob ? "may" : "may not"),
            bob ? "refused" : "allowed");
        Check(board.MayTouchPiecesAs("Cleo", false) == cleo, name + ": the bystander Cleo " + (cleo ? "may" : "may not"),
            cleo ? "refused" : "allowed");
    }
    // ---- 9.3c: whether a pickup would be accepted, for desktop clicks ----

    void TestCanPickUp() {
        NothingForced();
        string p = "can pick up";
        board._ClearState();
        CheckPickUps("Ana", 0, p + ": a reset board");
        LobbyWithTwoPlayers();
        CheckPickUps("Ana", 0, p + ": the lobby");
        CheckInt(board.StartGameAs("Ana"), ChessBoard.InputAccepted, p + ": started");
        Check(board.CanPickUpAs("Ana", false, ChessRules.E2) && board.CanPickUpAs("Ana", false, ChessRules.G1),
            p + ": White to move: Ana's pieces", "refused");
        Check(!board.CanPickUpAs("Ana", false, ChessRules.E7) && !board.CanPickUpAs("Ana", false, ChessRules.E4),
            p + ": White to move: not Black's piece or an empty square", "allowed");
        Check(!board.CanPickUpAs("Ana", false, -1) && !board.CanPickUpAs("Ana", false, 64),
            p + ": no square refused", "allowed");
        Check(!board.CanPickUpAs("Bob", false, ChessRules.E7) && !board.CanPickUpAs("Bob", false, ChessRules.E2),
            p + ": White to move: Bob nothing", "allowed");
        Check(!board.CanPickUpAs("Cleo", true, ChessRules.E2), p + ": an organizer isn't a player", "allowed");
        CheckPickUps("Ana", 16, p + ": White to move, Ana");
        CheckPickUps("Bob", 0, p + ": White to move, Bob");
        CheckPickUps("Cleo", 0, p + ": White to move, Cleo");

        // Asking changes nothing: nobody told, no activity stamped.
        board.AddListener(this);
        MarkNotifications();
        double activity = board.lastActivity = 12345.5;
        for (int square = 0; square < 64; square++) board.CanPickUpAs("Ana", false, square);
        CheckInt(notifications - notificationMark, 0, p + ": asking tells nobody");
        Check(board.lastActivity == activity, p + ": asking stamps no activity", "activity " + board.lastActivity);
        board.RemoveListener(this);

        Drop("Ana", ChessRules.E2, ChessRules.E4, p);
        Check(board.CanPickUpAs("Bob", false, ChessRules.E7) && !board.CanPickUpAs("Ana", false, ChessRules.E4),
            p + ": Black to move: Bob's piece, not Ana's", "wrong");
        CheckPickUps("Ana", 0, p + ": Black to move, Ana");
        CheckPickUps("Bob", 16, p + ": Black to move, Bob");

        // Replay (on the last position, e4 played): free moves of either colour, organizers counting.
        CheckInt(board.EnterReplayAs("Bob", false), ChessBoard.InputAccepted, p + ": replay entered");
        Check(board.CanPickUpAs("Bob", false, ChessRules.E4) && board.CanPickUpAs("Bob", false, ChessRules.E7)
            && !board.CanPickUpAs("Bob", false, ChessRules.E2), p + ": replay: either colour, not an empty square",
            "wrong");
        Check(board.CanPickUpAs("Cleo", true, ChessRules.E4) && !board.CanPickUpAs("Cleo", false, ChessRules.E4),
            p + ": replay: an organizer, not a bystander", "wrong");
        CheckPickUps("Ana", 32, p + ": replay, Ana");
        CheckPickUps("Cleo", 0, p + ": replay, Cleo");
        CheckInt(board.LeaveReplayAs("Bob", false), ChessBoard.InputAccepted, p + ": replay left");
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, p + ": resigned");
        CheckPickUps("Bob", 0, p + ": ended, Bob");

        // The local player, in both seats.
        board._ClearState();
        board.JoinAs(ChessRules.White, localName);
        board.JoinAs(ChessRules.Black, localName);
        CheckInt(board.StartGameAs(localName), ChessBoard.InputAccepted, p + ": local game started");
        Check(board.CanPickUp(ChessRules.E2) && !board.CanPickUp(ChessRules.E7), p + ": the local player, White to move",
            "wrong");
        board._ClearState();
    }

    // Whether CanPickUpAs (no organizer) agrees with the pickup itself on every square for name, and allows count.
    void CheckPickUps(string name, int count, string label) {
        int wrong = 0;
        int allowed = 0;
        string first = "";
        for (int square = 0; square < 64; square++) {
            bool can = board.CanPickUpAs(name, false, square);
            int result = board.replayMode ? board._ReplayPiecePickedUpAs(name, false, square)
                : board.PiecePickedUpAs(name, square);
            if (can) allowed++;
            if (can != (result == ChessBoard.InputAccepted) && wrong++ == 0) first = ChessRules.SquareName(square);
        }
        Check(wrong == 0, label + ": agrees with the pickup on every square", wrong + " differ, first " + first);
        CheckInt(allowed, count, label + ": " + count + " pieces");
    }

    // ---- 8.6 notation fill-in ----

    int notationTold;

    public override void OnNotationFilled() {
        notationTold++;
    }

    void TestNotationFill() {
        NothingForced();
        string s = "fill";
        board.AddListener(this);

        // Loaded games with every entry forgotten (as the archive will load them) are filled in as imported: check
        // and mate, promotions with and without capture, underpromotion, disambiguation, castling (Chess960), a FEN
        // with Black to move.
        if (Utilities.IsValid(importer) && siggiVsNuka != null) {
            CheckFillLoaded(siggiVsNuka.text, "", s + ": Siggi-vs-Nuka");
            Check(board.notation[53].EndsWith("#"), s + ": Siggi-vs-Nuka ends in mate", "last " + board.notation[53]);
            CheckFillLoaded("[SetUp \"1\"]\n[FEN \"r3k3/1P4P1/8/8/8/8/1p4p1/R3K2R b KQq - 3 40\"]\n\n40...gxh1=Q+ "
                + "41.Kd2 bxa1=N 42.bxa8=Q+ Kf7 43.g8=Q+ Kf6 44.Qgf8+ Ke5 *",
                "gxh1=Q+ Kd2 bxa1=N bxa8=Q+ Kf7 g8=Q+ Kf6 Qgf8+ Ke5", s + ": promotions");
            CheckFillLoaded("[SetUp \"1\"]\n[FEN \"7k/8/8/RN3N2/8/1N3N2/8/R5RK w - - 0 1\"]\n\n1.Nb5d4 Kh7 2.R1a3 Kh8 "
                + "3.Nbd2 Kh7 *", "Nb5d4 Kh7 R1a3 Kh8 Nbd2 Kh7", s + ": disambiguation");
            CheckFillLoaded("[Variant \"Chess960\"]\n[SetUp \"1\"]\n[FEN \"qrkbbnrn/pppppppp/8/8/8/8/PPPPPPPP/QRKBBNRN "
                + "w GBgb - 0 1\"]\n\n1.e4 e5 2.Bf3 Bf6 3.O-O-O O-O-O *", "e4 e5 Bf3 Bf6 O-O-O O-O-O",
                s + ": Chess960 castling");
        } else {
            Check(false, s + ": importer and Siggi-vs-Nuka assigned", "assign them in the Inspector");
        }

        // Filling in tells the listeners once, through OnNotationFilled only, on the next frame (_FillNotationFrame, as
        // Update calls it), and sends nothing.
        SyncGame(ChessArbiter.NoClock, s);
        Drop("Ana", ChessRules.G1, ChessRules.F3, s);
        board._FillNotationFrame();
        string full = NotationText(board, board.moveCount);
        CheckString(full, "e4 e5 Nf3", s + ": the game's notation");
        ForgetNotation(1);
        CheckInt(board._FirstMissingNotation(), 0, s + ": the first missing entry");
        int filled = board.filledNotation;
        int told = notationTold;
        MarkNotifications();
        Check(board._FillNotationFor(float.MaxValue), s + ": filled in", "still missing from ply "
            + board._FirstMissingNotation());
        CheckString(NotationText(board, board.moveCount), full, s + ": filled in as played");
        CheckInt(board.filledNotation - filled, 3, s + ": three entries filled in");
        CheckInt(notationTold - told, 0, s + ": listeners not told at once");
        board._FillNotationFrame();
        CheckInt(notationTold - told, 1, s + ": listeners told on the next frame");
        board._FillNotationFrame();
        CheckInt(notationTold - told, 1, s + ": listeners told once");
        CheckNotTold(s + ": OnBoardChanged");
        CheckInt(board.lastChange, ChessBoard.ChangeMove, s + ": lastChange kept");
        board._PackState();
        CheckInt(board.syncedInts[ChessBoard.SyncChange], ChessBoard.ChangeMove, s + ": nothing new to send");
        ForgetNotation(1);
        CheckInt(FillFrames(ChessBoard.DefaultFillBudgetMs), 1, s + ": filled in in a frame");
        CheckString(NotationText(board, board.moveCount), full, s + ": filled in as played in a frame");
        CheckInt(notationTold - told, 2, s + ": listeners told after the frame");
        board._FillNotationFrame();
        CheckInt(notationTold - told, 2, s + ": nothing more filled, nobody told");

        // A long gap over frames: with no budget, one entry per frame (passing the entries known), with the normal
        // budget fewer.
        string l = "fill long";
        SyncGame(ChessArbiter.NoClock, l);
        KnightShuffle(32, l);
        full = NotationText(board, board.moveCount);
        ForgetNotation(4);
        CheckInt(board._FirstMissingNotation(), 0, l + ": the first missing entry");
        told = notationTold;
        board.fillBudgetMs = 0f;
        board._FillNotationFrame();
        board.fillBudgetMs = ChessBoard.DefaultFillBudgetMs;
        CheckInt(notationTold - told, 0, l + ": nobody told during the walk");
        CheckInt(FillFrames(0f), 97, l + ": one frame per missing entry with no budget");
        CheckInt(notationTold - told, 1, l + ": listeners told once, at the end of the walk");
        CheckInt(board.lastFillFrames, 98, l + ": the walk's frames");
        CheckString(NotationText(board, board.moveCount), full, l + ": filled in as played");
        ForgetNotation(1);
        int frames = FillFrames(ChessBoard.DefaultFillBudgetMs);
        Check(frames >= 1 && frames <= 130, l + ": the normal budget", frames + " frames");
        CheckString(NotationText(board, board.moveCount), full, l + ": filled in as played with the normal budget");
        Debug.Log("[BoardTest] fill timing: 130 plies with a " + ChessBoard.DefaultFillBudgetMs + " ms budget: "
            + frames + " frames");

        // An update arriving with gaps of its own drops a walk under way here, and its gaps are filled in.
        string c = "fill received";
        SyncGame(ChessArbiter.NoClock, c);
        Drop("Ana", ChessRules.G1, ChessRules.F3, c);
        Drop("Bob", ChessRules.B8, ChessRules.C6, c);
        Drop("Ana", ChessRules.F1, ChessRules.B5, c);
        Drop("Bob", ChessRules.A7, ChessRules.A6, c);
        Drop("Ana", ChessRules.B5, ChessRules.C6, c);
        full = NotationText(board, board.moveCount);
        CheckString(full, "e4 e5 Nf3 Nc6 Bb5 a6 Bxc6", c + ": the game's notation");
        board._PackState();
        string[] sent = board.syncedNotation;
        for (int i = 0; i < sent.Length; i++) {
            if (i % 2 == 0) sent[i] = "";
        }
        // Another game here, ended (so it isn't logged, which would finish the walk), partly walked.
        SyncGame(ChessArbiter.NoClock, c);
        KnightShuffle(4, c);
        CheckInt(board.ResignAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, c + ": the game here ended");
        ForgetNotation(1);
        board.fillBudgetMs = 0f;
        board._FillNotationFrame();
        board._FillNotationFrame();
        board.fillBudgetMs = ChessBoard.DefaultFillBudgetMs;
        CheckInt(board._FirstMissingNotation(), 2, c + ": a walk under way");
        board._ReceiveState();
        CheckInt(board._FirstMissingNotation(), 0, c + ": the update's gaps");
        FillFrames(ChessBoard.DefaultFillBudgetMs);
        CheckInt(board._FirstMissingNotation(), -1, c + ": the update filled in");
        CheckString(NotationText(board, board.moveCount), full, c + ": the update filled in as played there");

        // A reset drops a walk under way.
        ForgetNotation(1);
        board.fillBudgetMs = 0f;
        board._FillNotationFrame();
        board.fillBudgetMs = ChessBoard.DefaultFillBudgetMs;
        filled = board.filledNotation;
        board._ClearState();
        CheckInt(FillFrames(ChessBoard.DefaultFillBudgetMs), 0, c + ": nothing to fill after a reset");
        board._FillNotationFrame();
        CheckInt(board.filledNotation - filled, 0, c + ": nothing filled after a reset");

        // A game's PGN fills in its gaps first: logging and export.
        string p = "fill pgn";
        SyncGame(ChessArbiter.NoClock, p);
        Drop("Ana", ChessRules.G1, ChessRules.F3, p);
        ForgetNotation(1);
        Check(board.GamePgn().Contains("1. e4 e5 2. Nf3 *"), p + ": exported", board.GamePgn());
        ForgetNotation(1);
        int before = board.loggedGames;
        CheckInt(board.ResignAs(ChessRules.Black, "Bob"), ChessBoard.InputAccepted, p + ": Bob resigns");
        CheckLogged(before, 1, p + ": logged once");
        CheckLogHas("1. e4 e5 2. Nf3 1-0", p + ": logged");

        board._FillNotationFrame();
        board.RemoveListener(this);
        board._ClearState();
    }

    // Loads pgn with every notation entry forgotten, fills it in, and checks it's as the import had it (and
    // expected, unless that's "").
    void CheckFillLoaded(string pgn, string expected, string name) {
        LoadText(pgn, name);
        string imported = NotationText(board, board.moveCount);
        if (expected != "") CheckString(imported, expected, name + ": imported");
        ForgetNotation(1);
        CheckInt(board._FirstMissingNotation(), 0, name + ": forgotten");
        Check(board._FillNotationFor(float.MaxValue), name + ": filled in", "still missing from ply "
            + board._FirstMissingNotation());
        CheckString(NotationText(board, board.moveCount), imported, name + ": filled in as imported");
    }


    // Forgets the game's notation but every keep-th entry (all of it for 1), and has the board look again.
    void ForgetNotation(int keep) {
        for (int i = 0; i < board.moveCount; i++) {
            if (keep > 1 && i % keep == keep - 1) continue;
            board.notation[i] = "";
        }
        board._RecheckNotation();
    }

    // Runs frames of filling in (_FillNotationFrame) with this budget until no notation is missing, and returns how
    // many it took (at most 1000).
    int FillFrames(float budget) {
        board.fillBudgetMs = budget;
        int frames = 0;
        while (board._FirstMissingNotation() >= 0 && frames < 1000) {
            board._FillNotationFrame();
            frames++;
        }
        board.fillBudgetMs = ChessBoard.DefaultFillBudgetMs;
        return frames;
    }

    // ---- 8.7 import ----

    int importTold;

    public override void OnImportChanged() {
        importTold++;
    }

    // The board's import on one client, driven by hand: the importer run in one go (_ImportFor) and its done event
    // called directly (_ImportFinished), as its Update would.
    void TestImport() {
        NothingForced();
        string s = "import";
        if (!Utilities.IsValid(importer) || siggiVsNuka == null) {
            Check(false, s + ": importer and Siggi-vs-Nuka assigned", "assign them in the Inspector");
            return;
        }
        ChessPgnImporter boardImporter = board.importer;
        board.AddListener(this);
        string nuka = siggiVsNuka.text;

        // Refused with no importer and on a board that isn't reset; nobody told.
        board._ClearState();
        board.importer = null;
        int told = importTold;
        int status = board.importStatus;
        Check(!board.CanImport(), s + ": no importer: can't import", "CanImport");
        CheckInt(board.StartImport(nuka), ChessBoard.InputRejected, s + ": no importer: refused");
        board.importer = importer;
        Check(board.CanImport(), s + ": reset: can import", "not CanImport");
        board.JoinAs(ChessRules.White, "Ana");
        Check(!board.CanImport(), s + ": lobby: can't import", "CanImport");
        CheckInt(board.StartImport(nuka), ChessBoard.InputRejected, s + ": lobby: refused");
        CheckInt(importTold - told, 0, s + ": refusals: nobody told of an import");
        CheckInt(board.importStatus, status, s + ": refusals: status unchanged");

        // Started: nothing changes on the board or is sent until it finishes, then it's loaded and sent in one update.
        board._ClearState();
        int sentChange = board.lastSentChange;
        MarkNotifications();
        told = importTold;
        CheckInt(board.StartImport(nuka), ChessBoard.InputAccepted, s + ": started");
        CheckInt(board.importStatus, ChessBoard.ImportRunning, s + ": under way");
        CheckInt(importTold - told, 1, s + ": started: told");
        Check(importer.importing, s + ": the importer is importing", "not importing");
        CheckInt(importer._ImportFor(float.MaxValue), ChessPgn.ImportDone, s + ": imported");
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, s + ": the board still reset");
        CheckNotTold(s + ": while importing: OnBoardChanged");
        CheckInt(board.lastSentChange, sentChange, s + ": while importing: nothing sent");
        board._ImportFinished();
        CheckInt(board.importStatus, ChessBoard.ImportLoaded, s + ": loaded");
        CheckInt(board.importResult, ChessPgn.ImportDone, s + ": loaded: read whole");
        CheckString(board.importFailedAt, "", s + ": loaded: failed at no move");
        CheckInt(importTold - told, 2, s + ": loaded: told");
        CheckTold(ChessBoard.ChangeLoaded, s + ": loaded");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, s + ": the board loaded");
        CheckInt(board.moveCount, 54, s + ": 54 plies");
        CheckString(board.gameId, "20250710001921", s + ": the PGN's GameID");
        board._PackState();
        CheckInt(board.syncedInts[ChessBoard.SyncChange], ChessBoard.ChangeLoaded, s + ": sent as loaded");
        CheckInt(board.syncedMoves.Length, 54, s + ": every move sent");
        CheckString(board.syncedNotation[53], board.notation[53], s + ": the notation sent");
        board._ImportFinished();
        CheckInt(importTold - told, 2, s + ": a second done event: nobody told");
        CheckNotTold(s + ": a second done event: OnBoardChanged");
        CheckInt(board.importStatus, ChessBoard.ImportLoaded, s + ": a second done event: still loaded");
        board._ClearState();
        board._PackState();
        CheckInt(board.syncedInts[ChessBoard.SyncChange], ChessBoard.ChangeReset, s + ": cleared: sent as reset");

        // A failed import: the reason, nothing changed or sent.
        string f = "import failed";
        board._ClearState();
        sentChange = board.lastSentChange;
        told = importTold;
        MarkNotifications();
        CheckInt(board.StartImport("1. e4 e5 2. Ke3 *"), ChessBoard.InputAccepted, f + ": started");
        importer._ImportFor(float.MaxValue);
        board._ImportFinished();
        CheckInt(board.importStatus, ChessBoard.ImportFailed, f + ": failed");
        CheckInt(board.importResult, ChessPgn.ImportIllegalMove, f + ": the reason: an illegal move");
        CheckString(board.importFailedAt, "2.", f + ": the move it failed at");
        CheckInt(importTold - told, 2, f + ": told when started and when failed");
        CheckNotTold(f + ": OnBoardChanged");
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, f + ": the board still reset");
        CheckInt(board.lastSentChange, sentChange, f + ": nothing sent");

        // Abandoned by a change made here: the importer stops, and its result is never loaded.
        string a = "import abandoned";
        board._ClearState();
        told = importTold;
        CheckInt(board.StartImport(nuka), ChessBoard.InputAccepted, a + ": started");
        importer._ImportFor(0f);
        CheckInt(board.JoinAs(ChessRules.White, "Ana"), ChessBoard.InputAccepted, a + ": Ana joins");
        CheckInt(board.importStatus, ChessBoard.ImportAbandoned, a + ": abandoned");
        CheckInt(board.importResult, ChessPgn.ImportMore, a + ": no failure recorded");
        CheckInt(importTold - told, 2, a + ": told when started and when abandoned");
        Check(!importer.importing, a + ": the importer stopped", "still importing");
        importer._ImportFor(float.MaxValue);
        MarkNotifications();
        board._ImportFinished();
        CheckInt(board.importStatus, ChessBoard.ImportAbandoned, a + ": a late done event: still abandoned");
        CheckNotTold(a + ": a late done event: OnBoardChanged");
        CheckInt(importTold - told, 2, a + ": a late done event: nobody told");
        CheckInt(board.lifecycle * 1000 + board.moveCount, ChessBoard.LifecycleLobby * 1000,
            a + ": the board still in the lobby with no moves");

        // A rejected piece input and a received touch change nothing, so the import goes on; any other update
        // arriving abandons it.
        string r = "import received";
        board._ClearState();
        board.lastSentChange = ChessBoard.ChangeActivity;
        board._PackState();
        told = importTold;
        CheckInt(board.StartImport(nuka), ChessBoard.InputAccepted, r + ": started");
        CheckInt(board.PieceDroppedAs("Ana", ChessRules.E2, ChessRules.E4), ChessBoard.InputRejected,
            r + ": a drop on a reset board");
        CheckInt(board.importStatus, ChessBoard.ImportRunning, r + ": a rejection: still under way");
        board._ReceiveState();
        CheckInt(board.importStatus, ChessBoard.ImportRunning, r + ": a touch received: still under way");
        board.JoinAs(ChessRules.Black, "Bob");
        board._PackState();
        board._ClearState();
        CheckInt(board.StartImport(nuka), ChessBoard.InputAccepted, r + ": started again");
        board._ReceiveState();
        CheckInt(board.importStatus, ChessBoard.ImportAbandoned, r + ": a join received: abandoned");
        CheckString(board.blackPlayer, "Bob", r + ": the update taken");
        CheckInt(importTold - told, 4, r + ": told twice for each import");
        Check(!importer.importing, r + ": the importer stopped", "still importing");

        // Starting again replaces an import under way.
        string g = "import again";
        board._ClearState();
        CheckInt(board.StartImport(nuka), ChessBoard.InputAccepted, g + ": the first");
        importer._ImportFor(0f);
        CheckInt(board.StartImport("1. e4 *"), ChessBoard.InputAccepted, g + ": the second");
        CheckInt(importer._ImportFor(float.MaxValue), ChessPgn.ImportDone, g + ": imported");
        board._ImportFinished();
        CheckInt(board.importStatus, ChessBoard.ImportLoaded, g + ": loaded");
        CheckInt(board.moveCount, 1, g + ": the second one's moves");

        board.RemoveListener(this);
        board.importer = boardImporter;
        board._ClearState();
    }

    // An import over real frames: the importer's Update runs it and sends its done event to the board, which loads it.
    ChessPgnImporter frameBoardImporter;
    int frameImportStart;

    void StartFrameImport() {
        if (!Utilities.IsValid(importer) || siggiVsNuka == null) {
            Finish();
            return;
        }
        board._ClearState();
        frameBoardImporter = board.importer;
        board.importer = importer;
        board.AddListener(this);
        frameImportStart = Time.frameCount;
        CheckInt(board.StartImport(siggiVsNuka.text), ChessBoard.InputAccepted, "import over frames: started");
        SendCustomEventDelayedFrames(nameof(PollImport), 1);
    }

    public void PollImport() {
        string s = "import over frames";
        int frames = Time.frameCount - frameImportStart;
        if (board.importStatus == ChessBoard.ImportRunning && frames < 600) {
            SendCustomEventDelayedFrames(nameof(PollImport), 1);
            return;
        }
        CheckInt(board.importStatus, ChessBoard.ImportLoaded, s + ": loaded");
        CheckInt(board.lifecycle * 1000 + board.moveCount, ChessBoard.LifecycleLoaded * 1000 + 54,
            s + ": the board loaded with 54 plies");
        CheckInt(board.lastSentChange, ChessBoard.ChangeLoaded, s + ": sent");
        Check(frames >= 2, s + ": over several frames", frames + " frames");
        Debug.Log("[BoardTest] import timing: Siggi-vs-Nuka in " + frames + " frames (importer: " + importer.frames
            + " frames, longest " + importer.longestFrameMs + " ms, " + importer.totalMs + " ms in all)");
        board.RemoveListener(this);
        board.importer = frameBoardImporter;
        Finish();
    }
}
