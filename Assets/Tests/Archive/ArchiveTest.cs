using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

// Steps 10.1a to 10.2b and 13.10 test behaviour (PLAN.md sections 10 and 13): the instance archive and its holders,
// the history wall and loading an archived game onto the board (docs/archive.md); since 13.10 also the archives on the
// arbiter, the personal archive (on test keys of its own) and its wall, the pick on the arbiter, Export PGN, deleting,
// and the walls and UIs told of changes instead of polling. The holders are synced, so like BoardTest it runs only
// with no one else in the instance; it uses the board once BoardTest and ViewTest have finished with it, and puts
// back the games it found archived when done. Logs
// "[ArchiveTest] PASS: <check>" or "[ArchiveTest] FAIL: <check>: <details>" per check, then a summary line.
// Built in the scene by Tools > Chess Board > Create Archive Test (Editor/ArchiveTestBuilder.cs).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ArchiveTest : ChessTest {
    protected override string TestName() {
        return "ArchiveTest";
    }

    public InstanceGameArchive archive;
    // A board whose arbiter lists the archive above (13.10), for a game logged by a board.
    public ChessBoard board;
    // Waited for, since they change the board; none to start at once.
    public BoardTest boardTest;
    public ViewTest viewTest;
    // The scene's history wall on this archive (step 10.2a).
    public GameHistoryWall wall;
    // The board's UI, for its Load it here button (steps 10.2b, 12.1).
    public ChessBoardUI ui;
    // The scene's personal archive and its wall (step 13.10).
    public PersonalGameArchive personal;
    public GameHistoryWall personalWall;

    const int HolderCount = 10;
    const int Capacity = HolderCount * GameArchiveHolder.Capacity;
    // A kept game's numbers: mode, ending, winner, base time, increment.
    const int InfoLength = 5;

    bool joined;
    bool ran;
    int[] sendsBefore = new int[HolderCount];
    int[] scratch = new int[16];
    // The games archived before the checks (by BoardTest, ViewTest, or played), put back when done (SaveGames).
    int savedCount;
    string[] savedIds;
    string[] savedContinuedFroms;
    string[] savedWhites;
    string[] savedBlacks;
    string[] savedFens;
    string[] savedEvents;
    string[] savedSites;
    int[] savedInfos;
    int[] savedOffsets;
    int[] savedMoves;
    int[] savedClocks;

    void Start() {
        SendCustomEventDelayedFrames(nameof(WaitForTests), 1);
    }

    public override void OnPlayerJoined(VRCPlayerApi player) {
        if (player.isLocal) joined = true;
    }

    public void WaitForTests() {
        if (ran) return;
        if (!ReadyAfter(joined, boardTest, viewTest, null, "BoardTest and ViewTest to finish")) {
            SendCustomEventDelayedFrames(nameof(WaitForTests), 1);
            return;
        }
        ran = true;
        if (VRCPlayerApi.GetPlayerCount() > 1) {
            Debug.Log("[ArchiveTest] skipped: other players are in the instance, and the checks would send the "
                + "archive to them");
            finished = true;
            return;
        }
        if (Utilities.IsValid(wall)) UseEnglish(wall.i18n);
        if (!Utilities.IsValid(archive)) {
            Check(false, "archive assigned", "assign ArchiveTest's Archive in the Inspector");
            LogSummary();
            return;
        }
        RunWiringChecks();
        RunArbiterChecks();
        SaveGames();
        bool hasWall = Utilities.IsValid(wall) && wall.archive == archive;
        Check(hasWall, "wall: assigned on this archive", "assign ArchiveTest's Wall (place the Game History Wall "
            + "prefab, then "
            + "Create Archive Test again)");
        if (hasWall) RunWallSetupChecks();
        RunBoardChecks();
        if (hasWall) RunWallBoardChecks();
        if (hasWall) RunLoadChecks();
        if (hasWall) RunPersonalChecks();
        if (hasWall) RunPgnTagChecks();
        RunAddChecks();
        if (hasWall) RunWallAddChecks();
        RunWrapChecks();
        if (hasWall) RunWallFullChecks();
        RunRecentChecks();
        if (hasWall) RunWallPickGoneChecks();
        RunSyncChecks();
        RunOverwrittenRecentChecks();
        archive._ClearArchive();
        CheckInt(archive.GameCount(), 0, "end: archive emptied");
        if (hasWall) {
            CheckTold(wall, "wall: redrawn once emptied");
            CheckWallRows(0, 0, "wall: emptied");
            CheckString(wall.title.text, Text("wall_title"), "wall: emptied, title");
        }
        RestoreGames(hasWall);
        LogSummary();
    }

    // ---- Wiring and ownership ----

    void RunWiringChecks() {
        CheckInt(archive.holders.Length, HolderCount, "wiring: holder count");
        CheckInt(archive.Capacity(), Capacity, "wiring: capacity 50");
        bool holdersOk = archive.holders.Length == HolderCount;
        for (int i = 0; i < archive.holders.Length; i++) {
            GameArchiveHolder holder = archive.holders[i];
            if (!Utilities.IsValid(holder) || holder.archive != archive || holder.localOnly) holdersOk = false;
        }
        Check(holdersOk, "wiring: every holder assigned, synced, pointing at the archive", "a holder is wrong");
        Check(Utilities.IsValid(archive.recent) && archive.recent.localOnly && archive.recent.archive == archive,
            "wiring: recent holder local only", "the recent holder is missing or synced");
        if (!holdersOk || !Utilities.IsValid(archive.recent)) return;
        if (Utilities.IsValid(board)) {
            Check(BoardUsesArchive(), "wiring: the board's arbiter lists the archive", "not listed");
        }

        VRCPlayerApi local = Networking.LocalPlayer;
        Check(Networking.IsOwner(archive.gameObject), "ownership: local player owns the archive alone", "not owner");
        Check(archive._AllReady(), "ownership: every holder ready", "a holder isn't ready");
        Check(archive.holders[3]._MayOwn(local), "ownership: archive owner may own a holder", "refused");
        Check(!archive.recent._MayOwn(local), "ownership: nobody may own the recent holder", "allowed");
    }

    // ---- A game logged by the board ----

    void RunBoardChecks() {
        if (!BoardUsesArchive()) {
            Check(false, "board: assigned, its arbiter listing this archive", "assign ArchiveTest's Board");
            return;
        }
        if (Utilities.IsValid(boardTest) && board.loggedGames > 0) {
            Check(archive.GameCount() > 0, "board: earlier tests' games archived",
                board.loggedGames + " logged, none archived");
        }
        Check(archive._ClearArchive(), "clear: accepted for the owner", "refused");
        CheckInt(archive.GameCount(), 0, "clear: no games");

        board._ClearState();
        string white = "Archive Ana";
        string black = "Archive Bo";
        board.JoinAs(ChessRules.White, white);
        board.JoinAs(ChessRules.Black, black);
        CheckInt(board.StartGameAs(white), ChessBoard.InputAccepted, "board: game started");
        CheckInt(board.PieceDroppedAs(white, Sq("e2"), Sq("e4")), ChessBoard.InputAccepted, "board: e4");
        CheckInt(board.PieceDroppedAs(black, Sq("c7"), Sq("c5")), ChessBoard.InputAccepted, "board: c5");
        string id = board.gameId;
        int move0 = board.moveHistory[0];
        int move1 = board.moveHistory[1];
        int mode = board.mode;
        string startFen = board.startFen;
        SaveSends();
        CheckInt(board.ResetBoardAs(white, false), ChessBoard.InputAccepted, "board: reset (logged unfinished)");
        CheckInt(archive.GameCount(), 1, "board: one game archived");
        CheckSends(1, 0, "board: only holder 0 sent");
        if (archive.GameCount() != 1) return;
        CheckString(archive.GameIdAt(0), id, "board: game ID");
        CheckString(archive.WhiteAt(0), white, "board: White");
        CheckString(archive.BlackAt(0), black, "board: Black");
        CheckString(archive.ContinuedFromAt(0), "", "board: no ContinuedFrom");
        CheckString(archive.StartFenAt(0), startFen, "board: starting FEN");
        CheckInt(archive.ModeAt(0), mode, "board: mode");
        CheckInt(archive.EndingAt(0), ChessGame.EndingUnterminated, "board: ending unterminated");
        CheckInt(archive.WinnerAt(0), ChessRules.NoColor, "board: no winner");
        CheckInt(archive.MoveCountAt(0), 2, "board: two moves");
        archive.CopyMovesAt(0, scratch);
        Check(scratch[0] == move0 && scratch[1] == move1, "board: moves", "moves differ");
        board._ClearState();
    }

    // ---- Adding: holders filled in order, read back, duplicates ----

    void RunAddChecks() {
        archive._ClearArchive();
        SaveSends();
        for (int n = 1; n <= 7; n++) Check(AddTestGame(n), "add: test-" + n + " accepted", "refused");
        CheckSends(5, 2, "add: holder 0 sent 5 times, holder 1 twice, the rest never");
        CheckInt(archive.GameCount(), 7, "add: 7 games");
        bool inOrder = true;
        for (int i = 0; i < 5; i++) {
            if (archive.holders[0].sequences[i] != i + 1) inOrder = false;
        }
        if (archive.holders[1].sequences[0] != 6 || archive.holders[1].sequences[1] != 7
            || archive.holders[1].sequences[2] != 0 || archive.holders[2].sequences[0] != 0) {
            inOrder = false;
        }
        Check(inOrder, "add: holders filled in order", "sequences out of place");
        for (int i = 0; i < 7; i++) {
            CheckInt(archive._SlotAt(i), i, "read: game " + i + " slot");
            CheckInt(archive._SequenceAt(i), i + 1, "read: game " + i + " sequence");
            CheckTestGame(i, i + 1);
        }

        SaveSends();
        Check(!AddTestGame(7), "duplicate: test-7 again refused (in the recent games)", "accepted");
        // test-1 has left the recent games (they hold the last 5), but it's archived: not added again.
        AddTestGame(1);
        CheckInt(archive.GameCount(), 7, "duplicate: still 7 games");
        CheckSends(0, 0, "duplicate: nothing sent");
        int[] moves = new int[4];
        Check(!archive._AddGame("", "", "W", "B", 0, 0, 0, "", -1, 0, moves, moves, 2, "", "", ""),
            "refused: no game ID", "accepted");
        Check(!archive._AddGame("bad-1", "", "W", "B", 0, 0, 0, "", -1, 0, moves, moves, 0, "", "", ""),
            "refused: no moves", "accepted");
        Check(!archive._AddGame("bad-2", "", "W", "B", 0, 0, 0, "", -1, 0, moves, moves, 5, "", "", ""),
            "refused: more moves than given", "accepted");
        CheckInt(archive.GameCount(), 7, "refused: still 7 games");
    }

    // ---- Full: the oldest overwritten, still read oldest first ----

    void RunWrapChecks() {
        for (int n = 8; n <= Capacity; n++) AddTestGame(n);
        CheckInt(archive.GameCount(), Capacity, "full: 50 games");
        CheckString(archive.GameIdAt(0), "test-1", "full: oldest test-1");
        SaveSends();
        AddTestGame(Capacity + 1);
        CheckSends(1, 0, "wrap: the 51st game sends only holder 0");
        CheckInt(archive.GameCount(), Capacity, "wrap: still 50 games");
        CheckInt(archive.holders[0].sequences[0], Capacity + 1, "wrap: the 51st in holder 0 slot 0");
        CheckInt(archive._SlotAt(Capacity - 1), 0, "wrap: newest read from slot 0");
        bool ordered = true;
        for (int i = 0; i < Capacity; i++) {
            if (archive.GameIdAt(i) != "test-" + (i + 2) || archive._SequenceAt(i) != i + 2) ordered = false;
        }
        Check(ordered, "wrap: read oldest first, test-2 to test-51", "order differs");
        CheckTestGame(0, 2);
        CheckTestGame(Capacity - 1, Capacity + 1);
        AddTestGame(Capacity + 2);
        AddTestGame(Capacity + 3);
        CheckString(archive.GameIdAt(0), "test-4", "wrap: after two more, oldest test-4");
        CheckInt(archive._SlotAt(Capacity - 1), 2, "wrap: newest in slot 2");
    }

    // ---- Recent games: kept locally, archived when the owner flushes ----

    void RunRecentChecks() {
        GameArchiveHolder recent = archive.recent;
        bool lastFive = true;
        for (int n = Capacity - 1; n <= Capacity + 3; n++) {
            if (recent._SlotOf("test-" + n) < 0) lastFive = false;
        }
        Check(lastFive && recent._SlotOf("test-" + (Capacity - 2)) < 0, "recent: the last 5 games kept",
            "the recent games differ");
        SaveSends();
        Check(archive._StoreRecent("rec-1", "", "R W", "R B", 0, 0, 0, "", -1, 0, new int[] { 11, 12 },
            new int[] { 1, 2 }, 2, "Rec Event", "Rec Site"), "recent: rec-1 stored", "refused");
        archive._StoreRecent("rec-2", "x/1", "R W", "R B", 0, 0, 0, "", -1, 0, new int[] { 21 }, new int[] { 3 }, 1, "",
            "");
        Check(!archive.IsArchived("rec-1") && !archive.IsArchived("rec-2"), "recent: stored only (no flush)",
            "archived");
        CheckSends(0, 0, "recent: nothing sent while only stored");
        CheckInt(archive._Flush(), 2, "flush: two recent games archived");
        CheckString(archive.GameIdAt(Capacity - 2), "rec-1", "flush: rec-1 second newest");
        CheckString(archive.GameIdAt(Capacity - 1), "rec-2", "flush: rec-2 newest");
        CheckString(archive.ContinuedFromAt(Capacity - 1), "x/1", "flush: rec-2's ContinuedFrom");
        CheckString(archive.EventAt(Capacity - 2) + "/" + archive.SiteAt(Capacity - 2), "Rec Event/Rec Site",
            "flush: rec-1's Event and Site (14.13)");
        CheckInt(archive.MoveCountAt(Capacity - 2), 2, "flush: rec-1's moves");
        archive.CopyMovesAt(Capacity - 2, scratch);
        Check(scratch[0] == 11 && scratch[1] == 12, "flush: rec-1's moves read back", "differ");
        CheckString(archive.GameIdAt(0), "test-6", "flush: oldest now test-6");
        CheckInt(archive._Flush(), 0, "flush: nothing more to add");
    }

    // ---- A holder update arriving ----

    void RunSyncChecks() {
        GameArchiveHolder holder = archive.holders[1];
        // Holder 1 holds test-6 to test-10; test-6 is the oldest game.
        holder._PackState();
        holder._Clear();
        holder._ReceiveState();
        CheckString(archive.GameIdAt(0), "test-6", "sync: holder 1 back from its update");
        CheckTestGame(0, 6);
        CheckTestGame(4, 10);

        holder._PackState();
        holder.syncedMoves = new int[1];
        holder._ReceiveState();
        CheckString(archive.GameIdAt(0), "test-6", "sync: an invalid update ignored");
        holder._PackState();
        holder.syncedGameIds = new string[GameArchiveHolder.Capacity];
        holder._ReceiveState();
        CheckString(archive.GameIdAt(0), "test-6", "sync: an update with a null string ignored");
        holder._PackState();
        holder.syncedEvents = new string[GameArchiveHolder.Capacity];
        holder._ReceiveState();
        CheckString(archive.EventAt(0), "Event 6", "sync: an update with a null Event ignored (14.13)");
        CheckTestGame(0, 6);

        GameArchiveHolder recent = archive.recent;
        int recentSlot = recent._SlotOf("rec-2");
        recent.syncedSequences = new int[GameArchiveHolder.Capacity];
        recent._ReceiveState();
        CheckInt(recent._SlotOf("rec-2"), recentSlot, "sync: the recent holder ignores updates");
        recent._PackState();
        CheckInt(recent.syncedMoves.Length, 0, "sync: the recent holder packs nothing");
    }

    // ---- Recent games overwritten in the archive (10.3): once seen archived, never archived again ----

    // The recent games (test-51 to test-53, rec-1, rec-2) are all archived. Holder updates then replace every game
    // (the owner archiving 50 others, as in a busy instance): the recent games have left the archive, but a client
    // that saw them archived mustn't add them back as the newest when it flushes (as on becoming the owner).
    void RunOverwrittenRecentChecks() {
        GameArchiveHolder recent = archive.recent;
        bool noneWaiting = true;
        for (int i = 0; i < GameArchiveHolder.Capacity; i++) {
            if (archive._IsRecentWaiting(i)) noneWaiting = false;
        }
        Check(noneWaiting, "overwritten: the archived recent games aren't waiting", "one is");
        SaveSends();
        int[] one = { 1 };
        for (int h = 0; h < HolderCount; h++) {
            for (int s = 0; s < GameArchiveHolder.Capacity; s++) {
                int k = h * GameArchiveHolder.Capacity + s;
                archive.holders[h]._WriteSlot(s, 1001 + k, "other-" + k, "", "Other W", "Other B", 0, 0,
                    ChessRules.NoColor, "", ChessArbiter.NoClock, 0, one, one, 1, "", "");
            }
            archive._OnHolderChanged();
        }
        CheckInt(archive.GameCount(), Capacity, "overwritten: 50 other games");
        CheckString(archive.GameIdAt(Capacity - 1), "other-" + (Capacity - 1), "overwritten: the newest is other-49");
        Check(!archive.IsArchived("rec-2") && !archive.IsArchived("test-53"),
            "overwritten: the recent games not archived again", "archived again");
        CheckSends(0, 0, "overwritten: nothing sent");
        CheckInt(archive._Flush(), 0, "overwritten: a flush adds nothing");
        noneWaiting = true;
        for (int i = 0; i < GameArchiveHolder.Capacity; i++) {
            if (archive._IsRecentWaiting(i)) noneWaiting = false;
        }
        Check(noneWaiting, "overwritten: none waiting", "one is");

        // A game stored and never seen archived still waits and goes in; one already archived when stored doesn't.
        archive._StoreRecent("rec-3", "", "R W", "R B", 0, 0, 0, "", -1, 0, one, one, 1, "", "");
        Check(archive._IsRecentWaiting(recent._SlotOf("rec-3")), "overwritten: rec-3 stored, waiting", "not waiting");
        CheckInt(archive._Flush(), 1, "overwritten: rec-3 archived");
        CheckString(archive.GameIdAt(Capacity - 1), "rec-3", "overwritten: rec-3 newest");
        Check(!archive._IsRecentWaiting(recent._SlotOf("rec-3")), "overwritten: rec-3 no longer waiting", "waiting");
        archive._StoreRecent("other-49", "", "Other W", "Other B", 0, 0, 0, "", -1, 0, one, one, 1, "", "");
        Check(!archive._IsRecentWaiting(recent._SlotOf("other-49")), 
            "overwritten: an archived game stored, not waiting", "waiting");
        CheckInt(archive._Flush(), 0, "overwritten: nothing more to add");
    }

    // ---- The games archived before the checks, kept and put back, so the wall shows them afterwards ----

    void SaveGames() {
        savedCount = archive.GameCount();
        savedIds = new string[savedCount];
        savedContinuedFroms = new string[savedCount];
        savedWhites = new string[savedCount];
        savedBlacks = new string[savedCount];
        savedFens = new string[savedCount];
        savedEvents = new string[savedCount];
        savedSites = new string[savedCount];
        savedInfos = new int[savedCount * InfoLength];
        savedOffsets = new int[savedCount + 1];
        for (int i = 0; i < savedCount; i++) savedOffsets[i + 1] = savedOffsets[i] + archive.MoveCountAt(i);
        savedMoves = new int[savedOffsets[savedCount]];
        savedClocks = new int[savedOffsets[savedCount]];
        for (int i = 0; i < savedCount; i++) {
            savedIds[i] = archive.GameIdAt(i);
            savedContinuedFroms[i] = archive.ContinuedFromAt(i);
            savedWhites[i] = archive.WhiteAt(i);
            savedBlacks[i] = archive.BlackAt(i);
            savedFens[i] = archive.StartFenAt(i);
            savedEvents[i] = archive.EventAt(i);
            savedSites[i] = archive.SiteAt(i);
            int at = i * InfoLength;
            savedInfos[at] = archive.ModeAt(i);
            savedInfos[at + 1] = archive.EndingAt(i);
            savedInfos[at + 2] = archive.WinnerAt(i);
            savedInfos[at + 3] = archive.BaseTimeAt(i);
            savedInfos[at + 4] = archive.IncrementAt(i);
            int count = archive.MoveCountAt(i);
            int[] moves = new int[count];
            int[] clocks = new int[count];
            archive.CopyMovesAt(i, moves);
            archive.CopyClocksAt(i, clocks);
            System.Array.Copy(moves, 0, savedMoves, savedOffsets[i], count);
            System.Array.Copy(clocks, 0, savedClocks, savedOffsets[i], count);
        }
        Debug.Log("[ArchiveTest] kept the " + savedCount + " game(s) archived before the checks, to put back after");
    }

    // Puts the kept games back, oldest first (numbered from #1 again), and checks they all read back.
    void RestoreGames(bool hasWall) {
        bool same = true;
        for (int i = 0; i < savedCount; i++) {
            int count = savedOffsets[i + 1] - savedOffsets[i];
            int[] moves = new int[count];
            int[] clocks = new int[count];
            System.Array.Copy(savedMoves, savedOffsets[i], moves, 0, count);
            System.Array.Copy(savedClocks, savedOffsets[i], clocks, 0, count);
            int at = i * InfoLength;
            archive._AddGame(savedIds[i], savedContinuedFroms[i], savedWhites[i], savedBlacks[i], savedInfos[at],
                savedInfos[at + 1], savedInfos[at + 2], savedFens[i], savedInfos[at + 3], savedInfos[at + 4], moves,
                clocks, count, "", savedEvents[i], savedSites[i]);
        }
        CheckInt(archive.GameCount(), savedCount, "end: the games archived before the checks put back");
        for (int i = 0; i < savedCount && i < archive.GameCount(); i++) {
            if (archive.GameIdAt(i) != savedIds[i] || archive.WhiteAt(i) != savedWhites[i]
                || archive.MoveCountAt(i) != savedOffsets[i + 1] - savedOffsets[i]
                || archive.EventAt(i) != savedEvents[i] || archive.SiteAt(i) != savedSites[i]) {
                same = false;
            }
        }
        Check(same, "end: the games put back read back oldest first", "a game differs");
        if (!hasWall) return;
        wall._RefreshIfChanged();
        // Newest first: the last game put back at the top.
        bool shown = savedCount == 0 || wall.rows[0].text == wall._RowText(savedCount - 1);
        if (savedCount > 0 && wall.rows[savedCount - 1].text != wall._RowText(0)) shown = false;
        if (savedCount < wall.rows.Length && wall.rows[savedCount].text != "") shown = false;
        Check(shown, "end: the wall shows the " + savedCount + " game(s) put back, newest first", "its entries don't");
    }

    // ---- The history wall (step 10.2a) ----

    void RunWallSetupChecks() {
        CheckInt(wall.rows.Length, Capacity, "wall: an entry per game the archive holds");
        bool assigned = Utilities.IsValid(wall.title) && Utilities.IsValid(wall.content)
            && Utilities.IsValid(wall.scroll) && Utilities.IsValid(wall.viewport) && Utilities.IsValid(wall.scrollbar)
            && wall.rowTargets.Length == wall.rows.Length;
        for (int i = 0; i < wall.rows.Length; i++) {
            if (!Utilities.IsValid(wall.rows[i]) || !Utilities.IsValid(wall.rowTargets[i])) assigned = false;
        }
        Check(assigned, "wall: title, scroll view, viewport, scrollbar, content and entries assigned",
            "one is missing (the Game History Wall prefab)");
        Check(wall.entryHeight > 0, "wall: entry height set", "" + wall.entryHeight);
        CheckString(ChessGame.EndingKey(ChessGame.EndingUnterminated, ChessRules.NoColor), "ending_unfinished",
            "wall: ending key, unfinished");
        CheckString(WallEndingText(ChessGame.EndingUnterminated, ChessRules.NoColor), Text("ending_unfinished"),
            "wall: ending text, unfinished");
        CheckString(WallEndingText(ChessGame.EndingResignation, ChessRules.White),
            Text("ending_resignation_white"), "wall: ending text, resignation");
        CheckString(WallEndingText(ChessGame.EndingTimeForfeit, ChessRules.NoColor), Text("ending_time_forfeit_draw"),
            "wall: ending text, time forfeit drawn");
        CheckString(WallEndingText(ChessGame.EndingUnknown, ChessRules.Black), Text("ending_black_wins"),
            "wall: ending text, a loaded game's result");
    }

    // After RunBoardChecks: the board's game alone on the wall, at the top on two lines.
    void RunWallBoardChecks() {
        CheckTold(wall, "wall: redrawn after the board's game");
        Check(!wall._RefreshIfChanged(), "wall: not redrawn again without a change", "redrawn");
        CheckString(wall.title.text, Text("wall_title"), "wall: board's game, title");
        CheckString(wall.rows[0].text, Row("wall_row", 1, "Archive Ana", "Archive Bo", Text("ending_unfinished"), "*",
            1), "wall: board's game, entry");
        string[] lines = wall.rows[0].text.Split(new char[] { '\n' });
        Check(lines.Length == 2 && lines[0].StartsWith("#1 ") && lines[1].Length > 0,
            "wall: board's game, its entry on two lines", "\"" + wall.rows[0].text + "\"");
        CheckWallShown(1, "wall: board's game");
    }

    // After RunAddChecks: test-7 down to test-1, newest first.
    void RunWallAddChecks() {
        CheckTold(wall, "wall: redrawn after adding");
        CheckString(wall.title.text, Text("wall_title"), "wall: 7 games, title");
        CheckString(wall.rows[6].text, Row("wall_row_chess960", 1, "White 1", "Black 1",
            Text("ending_checkmate_black"), "0-1", 1), "wall: 7 games, test-1's entry (the last)");
        CheckWallRows(1, 7, "wall: 7 games");
    }

    // After RunWrapChecks: full and wrapped, test-53 down to test-4 across every holder.
    void RunWallFullChecks() {
        CheckTold(wall, "wall: redrawn after wrapping");
        CheckString(wall.title.text, Text("wall_title"), "wall: full, title");
        CheckWallRows(4, Capacity, "wall: full, test-53 to test-4");
        CheckString(wall.rows[0].text, ExpectedRow(Capacity + 3), "wall: full, the newest at the top");
        // Scrolling: the mouse wheel's step can't be simulated (Input.GetAxisRaw), so this checks that a move
        // without it (a drag) stays where it went.
        if (Utilities.IsValid(wall.content) && Utilities.IsValid(wall.viewport)) {
            float max = Capacity * wall.entryHeight - wall.viewport.rect.height;
            Check(Mathf.Abs(wall._MaxScroll() - max) < 0.5f, "wall: full, scrolls as far as the content overflows",
                "got " + wall._MaxScroll() + ", expected " + max);
            Vector2 at = wall.content.anchoredPosition;
            wall.content.anchoredPosition = new Vector2(at.x, 3.5f * wall.entryHeight);
            wall._OnScrolled();
            Check(wall.content.anchoredPosition.y == 3.5f * wall.entryHeight, "wall: a drag's scroll kept",
                "moved to " + wall.content.anchoredPosition.y);
            wall.content.anchoredPosition = at;
            wall._OnScrolled();
        }
        // Picked for RunWallPickGoneChecks, after RunRecentChecks overwrites it.
        wall._SelectRow(Capacity - 1);
        CheckString(board.arbiter.selectedGameId, "test-4", "pick: the oldest game picked, from the last entry");
        if (Utilities.IsValid(ui) && ui.board == board && board.CanLoadGame()) {
            CheckLoadButtonNow(true, "pick: Load it here shown in the frame of the pick");
        }
    }

    // The wall's first count entries are test-(first + count - 1) down to test-first, newest first, and the rest
    // empty and hidden (CheckWallShown).
    void CheckWallRows(int first, int count, string name) {
        string differs = "";
        for (int i = 0; i < count && differs == ""; i++) {
            string expected = ExpectedRow(first + count - 1 - i);
            if (wall.rows[i].text != expected) {
                differs = "entry " + i + " is \"" + wall.rows[i].text + "\", expected \"" + expected + "\"";
            }
        }
        Check(differs == "", name + ": entries", differs);
        CheckWallShown(count, name);
    }

    // The wall's first count entries shown and the rest empty and hidden, the scroll content as tall as those shown.
    void CheckWallShown(int count, string name) {
        string differs = "";
        for (int i = 0; i < wall.rows.Length && differs == ""; i++) {
            bool shown = i < count;
            if (!shown && wall.rows[i].text != "") {
                differs = "entry " + i + " isn't empty: \"" + wall.rows[i].text + "\"";
            } else if (i >= wall.rowTargets.Length || !Utilities.IsValid(wall.rowTargets[i])) {
                differs = "entry " + i + " has no target";
            } else if (wall.rowTargets[i].gameObject.activeSelf != shown) {
                differs = "entry " + i + (shown ? " hidden" : " shown");
            }
        }
        Check(differs == "", name + ": the entries past the archive's count empty and hidden", differs);
        float height = Utilities.IsValid(wall.content) ? wall.content.sizeDelta.y : -1;
        Check(height == count * wall.entryHeight, name + ": the scroll content as tall as the entries shown",
            "got " + height + ", expected " + count * wall.entryHeight);
    }

    // How a game ended, as the wall writes it (its game texts).
    string WallEndingText(int ending, int winner) {
        return ChessBoardUI._EndingText(wall._GameTexts(), ending, winner);
    }

    // Test game n's row (AddTestGame): its sequence is n, as each was archived in turn.
    string ExpectedRow(int n) {
        int ending = n % 11;
        int winner = n % 3 - 1 < 0 ? ChessRules.NoColor : n % 2;
        return Row(n % 2 == ChessGame.ModeChess960 ? "wall_row_chess960" : "wall_row", n, "White " + n, "Black " + n,
            Text(ChessGame.EndingKey(ending, winner)), ChessPgn.ResultText(ending, winner), (n % 7 + 2) / 2);
    }

    // A wall row by its key (wall_row, or wall_row_chess960 for a Chess960 game), PLAN.md 13.5a.
    string Row(string key, int number, string white, string black, string ending, string result, int moves) {
        return TextWith(key, new string[] {
            "number", number.ToString(), "white", white, "black", black, "ending", ending, "result", result,
            "moves", moves == 1 ? Text("moves_one") : TextWith("moves_many", new string[] { "count", moves.ToString() })
        });
    }

    // ---- Picking a game and loading it (step 10.2b) ----

    // After RunWallBoardChecks, with the board's unfinished game archived: a fool's mate is played and archived too,
    // then the wall's rows pick games, Load it here shows only on a reset board with a game picked, and loading puts
    // the board in its loaded state with the game's moves, players and result, its notation filled in.
    void RunLoadChecks() {
        if (!BoardUsesArchive() || archive.GameCount() != 1) {
            Check(false, "load: the board's game archived", "no board, or " + archive.GameCount() + " games");
            return;
        }
        string firstId = archive.GameIdAt(0);
        board._ClearState();
        string white = "Load Ana";
        string black = "Load Bo";
        board.JoinAs(ChessRules.White, white);
        board.JoinAs(ChessRules.Black, black);
        CheckInt(board.StartGameAs(white), ChessBoard.InputAccepted, "load: fool's mate started");
        CheckInt(board.PieceDroppedAs(white, Sq("f2"), Sq("f3")), ChessBoard.InputAccepted, "load: f3");
        CheckInt(board.PieceDroppedAs(black, Sq("e7"), Sq("e5")), ChessBoard.InputAccepted, "load: e5");
        CheckInt(board.PieceDroppedAs(white, Sq("g2"), Sq("g4")), ChessBoard.InputAccepted, "load: g4");
        CheckInt(board.PieceDroppedAs(black, Sq("d8"), Sq("h4")), ChessBoard.InputAccepted, "load: Qh4#");
        CheckInt(board.ending, ChessGame.EndingCheckmate, "load: fool's mate ended by checkmate");
        string mateId = board.gameId;
        // The PGN the board logged, which the wall's Export PGN writes again from the archive (step 13.10).
        string matePgn = board.lastLoggedPgn;
        board._ClearState();
        CheckInt(archive.GameCount(), 2, "load: fool's mate archived");
        if (archive.GameCount() != 2 || archive.GameIdAt(1) != mateId) return;
        wall._RefreshIfChanged();

        // Picking on the wall: the newest game, the fool's mate, is entry 0 (the top).
        bool targets = wall.rowTargets.Length == wall.rows.Length;
        for (int i = 0; i < wall.rowTargets.Length; i++) {
            GameHistoryRow target = wall.rowTargets[i];
            if (!Utilities.IsValid(target) || target.wall != wall || target.index != i) targets = false;
        }
        Check(targets, "pick: every entry has its target",
            "an entry's target is missing or wrong (the Game History Wall prefab)");
        // The pick is on the arbiter (step 13.10): its archive and the game's ID.
        ChessArbiter arbiter = board.arbiter;
        arbiter._ClearSelection();
        int draws = wall.selectionDraws;
        wall._SelectRow(5);
        Check(!arbiter.HasSelection() && wall.selectionDraws == draws, "pick: an empty entry picks nothing",
            "picked " + arbiter.selectedGameId);
        if (targets) wall.rowTargets[0]._Press();
        else wall._SelectRow(0);
        Check(arbiter.selectedArchive == archive, "pick: the arbiter's pick in this archive", "another archive");
        CheckString(arbiter.selectedGameId, mateId, "pick: entry 1 (the top) picks the fool's mate");
        CheckInt(wall._SelectedIndex(), 1, "pick: its archive index");
        Check(wall.selectionDraws == draws + 1, "pick: the wall told at once, not polling",
            "drawn " + (wall.selectionDraws - draws) + " times");
        Check(wall.rows[0].color == wall.selectedColor && wall.rows[1].color == wall.rowColor,
            "pick: only the picked entry coloured",
            "entry 1 " + wall.rows[0].color + ", entry 2 " + wall.rows[1].color);
        wall._SelectRow(0);
        Check(!arbiter.HasSelection() && arbiter.selectedGameId == "" && !Utilities.IsValid(arbiter.selectedArchive),
            "pick: pressed again, unpicked", "still " + arbiter.selectedGameId);
        Check(wall.rows[0].color == wall.rowColor, "pick: unpicked, its entry uncoloured", "" + wall.rows[0].color);
        wall._SelectRow(1);
        CheckString(arbiter.selectedGameId, firstId, "pick: entry 2 picks the board's first game");
        Check(wall.rows[1].color == wall.selectedColor, "pick: entry 2 coloured", "" + wall.rows[1].color);
        wall._SelectRow(0);
        CheckString(arbiter.selectedGameId, mateId, "pick: another entry replaces the pick");
        Check(wall.rows[1].color == wall.rowColor, "pick: replaced, entry 2 uncoloured", "" + wall.rows[1].color);

        // Export PGN (step 13.10): the instance archive keeps no notation, so the wall works it out over frames.
        RunExportChecks(wall, mateId, matePgn, true, "export (instance)");

        // Load it here: only on a reset board with a game picked.
        bool hasUI = Utilities.IsValid(ui) && ui.board == board;
        Check(hasUI, "UI: assigned, on this board", "assign ArchiveTest's UI (a Chess Table's, then Create Archive "
            + "Test again)");
        if (hasUI) {
            CheckLoadButton(true, "Load it here: shown on a reset board with a game picked");
            CheckString(ui._ButtonText(ChessBoardUI.ButtonLoadHere), Text("button_load_here"), "Load it here: label");
            board.JoinAs(ChessRules.White, white);
            CheckLoadButton(false, "Load it here: hidden in the lobby");
            CheckInt(ui._PressButton(ChessBoardUI.ButtonLoadHere), ChessBoard.InputRejected,
                "Load it here: refused in the lobby");
            CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "Load it here: refused, still in the lobby");
            CheckInt(board.LoadArchivedGame(mateId), ChessBoard.InputRejected, "load: refused in the lobby");
            board._ClearState();
            arbiter._ClearSelection();
            CheckLoadButton(false, "Load it here: hidden with nothing picked");
            CheckInt(ui._PressButton(ChessBoardUI.ButtonLoadHere), ChessBoard.InputRejected,
                "Load it here: refused with nothing picked");
            CheckInt(board.lifecycle, ChessBoard.LifecycleReset, "Load it here: refused, the board still reset");
            wall._SelectRow(0);
        }
        CheckInt(board.LoadArchivedGame("no-such-game"), ChessBoard.InputRejected, "load: a game not archived refused");
        CheckInt(board.LoadArchivedGame(""), ChessBoard.InputRejected, "load: no ID refused");
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, "load: refused, the board still reset");

        // The fool's mate, loaded through the button.
        int loaded = hasUI ? ui._PressButton(ChessBoardUI.ButtonLoadHere) : board.LoadArchivedGame(mateId);
        CheckInt(loaded, ChessBoard.InputAccepted, "load: fool's mate loaded");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "load: the board in its loaded state");
        Check(board.replayMode, "load: in replay", "not in replay");
        CheckInt(board.moveCount, 4, "load: four moves");
        CheckInt(board.replayIndex, 4, "load: showing the last position");
        archive.CopyMovesAt(1, scratch);
        bool sameMoves = true;
        for (int i = 0; i < 4; i++) {
            if (board.moveHistory[i] != scratch[i]) sameMoves = false;
        }
        Check(sameMoves, "load: the archived moves", "moves differ");
        CheckString(board.gameId, mateId, "load: the game's ID");
        CheckString(board.continuedFrom, "", "load: no ContinuedFrom");
        CheckString(board.replaySourceWhite, white, "load: White");
        CheckString(board.replaySourceBlack, black, "load: Black");
        CheckString(board.replaySourceResult, "0-1", "load: result 0-1");
        CheckInt(board.ending, ChessGame.EndingCheckmate, "load: ended by checkmate");
        CheckInt(board.winner, ChessRules.Black, "load: Black won");
        CheckString(board.loadedBy, ChessUtil._LocalName(""), "load: the local player keeps the board (step 13.2)");
        if (hasUI) {
            Check(!arbiter.HasSelection(), "Load it here: the pick cleared once loaded",
                "still " + arbiter.selectedGameId);
            CheckLoadButton(false, "Load it here: hidden on the loaded board");
        }
        CheckInt(board._FirstMissingNotation(), 0, "notation: none on loading (the archive keeps none)");
        Check(board._FillNotationFor(float.MaxValue), "notation: filled in", "gaps left");
        CheckString(NotationText(board, 4), "f3 e5 g4 Qh4#", "notation: the fool's mate's");
        CheckInt(board.LoadArchivedGame(firstId), ChessBoard.InputRejected, "load: refused on a loaded board");

        // The unfinished game: no result.
        board._ClearState();
        CheckInt(board.LoadArchivedGame(firstId), ChessBoard.InputAccepted, "load: the unfinished game loaded");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "load: unfinished, the loaded state");
        CheckString(board.replaySourceResult, "*", "load: unfinished, result *");
        CheckInt(board.ending, ChessGame.EndingNone, "load: unfinished, no result");
        Check(board._FillNotationFor(float.MaxValue), "notation: unfinished, filled in", "gaps left");
        CheckString(NotationText(board, 2), "e4 c5", "notation: unfinished, the game's");
        board._ClearState();
        CheckInt(board.LoadArchivedGameAs("Lena", firstId), ChessBoard.InputAccepted, "load: as Lena");
        CheckString(board.loadedBy, "Lena", "load: as Lena, Lena keeps the board");
        Check(board.IsLockedToLoader(), "load: as Lena, locked to her", "not locked");
        board._ClearState();
        if (hasUI) ui._UpdateButtons();
    }

    // After RunWallFullChecks picked the oldest game (test-4) and RunRecentChecks overwrote it: the pick is gone,
    // cleared by the archive as it overwrote the game (step 13.10), and Load it here hidden at once.
    void RunWallPickGoneChecks() {
        ChessArbiter arbiter = board.arbiter;
        Check(!arbiter.HasSelection() && arbiter.selectedGameId == "", "pick: overwritten in the archive, unpicked",
            "still " + arbiter.selectedGameId);
        if (Utilities.IsValid(ui) && ui.board == board) {
            CheckLoadButtonNow(false, "pick: overwritten, Load it here hidden in that frame");
        }
    }

    // Every Load it here button is shown (or hidden) after the UI brings its buttons up to date, and the UI's
    // _ButtonApplies agrees.
    void CheckLoadButton(bool shown, string name) {
        ui._UpdateButtons();
        CheckLoadButtonNow(shown, name);
    }

    // CheckLoadButton without bringing the buttons up to date first: what the UI shows as it is, after the arbiter
    // told it of a change (step 13.10).
    void CheckLoadButtonNow(bool shown, string name) {
        string errors = "";
        int found = 0;
        for (int i = 0; i < ui.buttons.Length && i < ui.buttonActions.Length; i++) {
            if (ui.buttonActions[i] != ChessBoardUI.ButtonLoadHere || !Utilities.IsValid(ui.buttons[i])) continue;
            found++;
            if (ui.buttons[i].activeSelf != shown) errors += "button " + i + (shown ? " hidden; " : " shown; ");
        }
        if (found == 0) errors += "no Load it here button (the Chess Board UI prefab); ";
        if (ui._ButtonApplies(ChessBoardUI.ButtonLoadHere) != shown) errors += "applies " + !shown + "; ";
        Check(errors.Length == 0, name, errors);
    }


    // ---- The archives on the arbiter (step 13.10) ----

    // Whether the board's arbiter lists the instance archive.
    bool BoardUsesArchive() {
        if (!Utilities.IsValid(board) || !Utilities.IsValid(board.arbiter)) return false;
        GameArchive[] archives = board.arbiter.archives;
        for (int i = 0; i < archives.Length; i++) {
            if (archives[i] == archive) return true;
        }
        return false;
    }

    // The instance archive works as before through a field typed as the base, reached from the arbiter's list, and
    // refuses deleting; the arbiter introduced itself to both archives, and the walls registered with theirs.
    void RunArbiterChecks() {
        if (!BoardUsesArchive()) {
            Check(false, "arbiter: lists the instance archive",
                "set the arbiter's Archives (empty it to have it wired)");
            return;
        }
        ChessArbiter arbiter = board.arbiter;
        GameArchive found = null;
        bool listsPersonal = false;
        for (int i = 0; i < arbiter.archives.Length; i++) {
            if (arbiter.archives[i] == archive) found = arbiter.archives[i];
            if (Utilities.IsValid(personal) && arbiter.archives[i] == personal) listsPersonal = true;
        }
        Check(!Utilities.IsValid(personal) || listsPersonal, "arbiter: lists the personal archive",
            "not listed (empty the arbiter's Archives to have it wired)");
        Check(found.arbiter == arbiter && (!Utilities.IsValid(personal) || personal.arbiter == arbiter),
            "arbiter: introduced itself to its archives", "an archive has no arbiter");
        CheckInt(found.Capacity(), Capacity, "base: the instance archive's capacity through a GameArchive field");
        int count = archive.GameCount();
        CheckInt(found.GameCount(), count, "base: its game count");
        if (count > 0) {
            int last = count - 1;
            CheckString(found.GameIdAt(last), archive.GameIdAt(last), "base: its newest game's ID");
            CheckInt(found.MoveCountAt(last), archive.MoveCountAt(last), "base: its newest game's moves");
            CheckInt(found.IndexOf(archive.GameIdAt(last)), last, "base: IndexOf");
            Check(found.HasMovesAt(last), "base: every game has its moves", "not");
            CheckString(found.PgnAt(last), "", "base: no PGN kept");
            Check(!found.DeleteGame(archive.GameIdAt(last)) && archive.GameCount() == count,
                "base: DeleteGame refused on the instance archive", "deleted");
        }
        Check(!found.CanDelete(), "base: nobody may delete from the instance archive", "CanDelete");
        Check(Registered(archive, wall), "wall: registered with its archive", "not in its walls");
        if (Utilities.IsValid(personal) && Utilities.IsValid(personalWall)) {
            Check(Registered(personal, personalWall), "personal wall: registered with its archive", "not in its walls");
        }
    }

    static bool Registered(GameArchive on, GameHistoryWall what) {
        if (!Utilities.IsValid(on) || !Utilities.IsValid(what)) return false;
        for (int i = 0; i < on.walls.Length; i++) {
            if (on.walls[i] == what) return true;
        }
        return false;
    }

    // The wall was told of the archive's last change (_OnArchiveChanged) without being asked to look, and isn't
    // redrawn again without one.
    void CheckTold(GameHistoryWall on, string name) {
        Check(on.drawnChanges == on.archive.changes, name + ", told by the archive",
            "drawn at " + on.drawnChanges + ", archive at " + on.archive.changes);
        Check(!on._RefreshIfChanged(), name + ", not again without a change", "redrawn");
    }

    // ---- Export PGN (step 13.10) ----

    // With gameId picked in on's archive: Export PGN shown, opening the box with expected (after the notation is
    // worked out when works), closing when pressed again and with the pick; the pick is put back.
    void RunExportChecks(GameHistoryWall on, string gameId, string expected, bool works, string name) {
        ChessArbiter arbiter = board.arbiter;
        bool ready = Utilities.IsValid(on.exportButton) && Utilities.IsValid(on.exportLabel)
            && Utilities.IsValid(on.pgnBox) && Utilities.IsValid(on.pgnField);
        Check(ready, name + ": button and box assigned", "one is missing (rebuild the wall)");
        if (!ready) return;
        Check(arbiter.selectedArchive == on.archive && arbiter.selectedGameId == gameId, name + ": the game picked",
            "picked " + arbiter.selectedGameId);
        Check(on.exportButton.activeSelf, name + ": Export PGN shown with a game picked", "hidden");
        CheckString(on.exportLabel.text, Text("wall_export"), name + ": labelled");
        Check(on._PressExport() && on.pgnBox.activeSelf, name + ": opened", "closed");
        CheckString(on.exportLabel.text, Text("wall_export_hide"), name + ": labelled Hide PGN while open");
        if (works) {
            CheckString(on.pgnField.text, Text("wall_pgn_working"),
                name + ": Working… while the notation is worked out");
            Check(on._ExportFor(float.MaxValue), name + ": the notation worked out", "not done");
        }
        CheckString(on._ExportText(), expected, name + ": the PGN as the board wrote it");
        Check(!on._PressExport() && !on.pgnBox.activeSelf, name + ": pressed again, closed", "still open");
        on._PressExport();
        arbiter._ClearSelection();
        Check(!on.exportShown && !on.pgnBox.activeSelf && !on.exportButton.activeSelf,
            name + ": closed and hidden with the pick", "still shown");
        arbiter._SelectGame(on.archive, gameId);
    }

    // ---- The personal archive (step 13.10), on test keys with a capacity of 3 ----

    const int PersonalCapacity = 3;
    const string PersonalTestPrefix = "siggi.chess.test.personal.";
    // The test games' IDs, so their order (by ID) is known: P2 the oldest, then P1, P3, P4, P5, P6.
    const string P1 = "20261001100000-000001";
    const string P2 = "20260901100000-000002";
    const string P3 = "20261003100000-000003";
    const string P4 = "20261005100000-000004";
    const string P5 = "20261007100000-000005";
    const string P6 = "20261009100000-000006";

    void RunPersonalChecks() {
        bool hasPersonal = Utilities.IsValid(personal) && Utilities.IsValid(personalWall)
            && personalWall.archive == personal;
        Check(hasPersonal, "personal: archive and wall assigned", "assign ArchiveTest's Personal and Personal Wall "
            + "(place the Personal Game Archive and Personal History Wall prefabs, then Create Archive Test again)");
        if (!hasPersonal || !BoardUsesArchive()) return;
        bool hasUI = Utilities.IsValid(ui) && ui.board == board;
        ChessArbiter arbiter = board.arbiter;
        string me = ChessUtil._LocalName("");
        int savedCapacity = personal.capacity;
        string savedPrefix = personal.keyPrefix;
        arbiter._ClearSelection();

        // The wall's entries: one per game at start, and more made from its last for a raised capacity.
        CheckInt(personalWall.rows.Length, personal.Capacity(), "personal wall: an entry per game the archive holds");
        int built = personalWall.rows.Length;
        personal.capacity = built + 2;
        personalWall._EnsureEntries();
        CheckInt(personalWall.rows.Length, built + 2, "personal wall: entries made for a raised capacity");
        bool made = built > 0 && personalWall.rowTargets.Length == built + 2
            && personalWall.deleteButtons.Length == built + 2 && personalWall.deleteLabels.Length == built + 2;
        for (int i = built; made && i < built + 2; i++) {
            GameHistoryRow row = personalWall.rowTargets[i];
            if (!Utilities.IsValid(row) || row.index != i || row.wall != personalWall
                || row == personalWall.rowTargets[built - 1] || !Utilities.IsValid(personalWall.rows[i])
                || personalWall.rows[i].transform.parent != row.transform
                || !Utilities.IsValid(personalWall.deleteButtons[i])
                || personalWall.deleteButtons[i].transform.parent != row.transform) {
                made = false;
            }
        }
        Check(made, "personal wall: each entry made with its index, text and delete button", "an entry is wrong");

        // Capacity 3 on test keys, emptied, then forgotten as before the PlayerData arrives.
        personal.capacity = PersonalCapacity;
        personal.keyPrefix = PersonalTestPrefix;
        personal._Forget();
        personal._Restore();
        personal._ClearSaved();
        personal._Forget();
        CheckInt(personal.GameCount(), 0, "personal: no games before the PlayerData is restored");
        CheckString(personalWall.title.text, Text("wall_title_personal"), "personal wall: titled");
        CheckWallShownOn(personalWall, 0, "personal wall: before restoring, just the title");

        // P1 waits in memory until restored, then is saved.
        string p1Pgn = PlayGame(me, "Opp A", P1, "e2e4 e7e5", ChessRules.White);
        CheckInt(personal.GameCount(), 0, "personal: a game logged before restoring waits");
        int refreshes = personalWall.refreshes;
        personal._Restore();
        Check(personal.restored, "personal: restored", "not");
        CheckInt(personal.GameCount(), 1, "personal: the waiting game saved once restored");
        Check(personalWall.refreshes > refreshes, "personal wall: told of the restore", "not redrawn");

        PlayGame("Some W", "Some B", "20261002100000-000009", "e2e4", ChessRules.NoColor);
        CheckInt(personal.GameCount(), 1, "personal: a game without the local player not kept");
        refreshes = personalWall.refreshes;
        PlayGame("Opp B", me, P2, "d2d4 d7d5", ChessRules.White);
        Check(personalWall.refreshes > refreshes && personalWall.drawnChanges == personal.changes,
            "personal wall: told of a game added, not polling", "not redrawn");
        string p3Pgn = PlayGame(me, "Opp C", P3, "f2f3 e7e5 g2g4 d8h4", ChessRules.NoColor);
        CheckInt(personal.GameCount(), 3, "personal: 3 games, full");
        Check(personal.GameIdAt(0) == P2 && personal.GameIdAt(1) == P1 && personal.GameIdAt(2) == P3,
            "personal: ordered by ID", personal.GameIdAt(0) + " " + personal.GameIdAt(1) + " " + personal.GameIdAt(2));
        CheckInt(personal._SequenceAt(0), 0, "personal: no sequence numbers");
        int p3 = personal.IndexOf(P3);
        CheckString(personal.WhiteAt(p3), me, "personal: P3's White");
        CheckString(personal.BlackAt(p3), "Opp C", "personal: P3's Black");
        CheckInt(personal.EndingAt(p3), ChessGame.EndingCheckmate, "personal: P3's ending");
        CheckInt(personal.WinnerAt(p3), ChessRules.Black, "personal: P3's winner");
        CheckInt(personal.MoveCountAt(p3), 4, "personal: P3's plies");
        CheckString(personal.StartFenAt(p3), ChessFen.StartFen, "personal: P3's starting FEN");
        CheckInt(personal.ModeAt(p3), ChessGame.ModeStandard, "personal: P3's mode");
        Check(personal.HasMovesAt(p3), "personal: a game handed in keeps its moves", "no moves");
        CheckString(personal.PgnAt(p3), p3Pgn, "personal: PgnAt gives the logged PGN");
        CheckString(SavedText(P3), "[PlyCount \"4\"]\n[Ending \"checkmate\"]\n" + p3Pgn,
            "personal: saved as the logged PGN after PlyCount and Ending");

        // Full: P4 overwrites the lowest ID (P2); P1 again replaces P1 in its slot.
        PlayGame(me, "Opp D", P4, "e2e4", ChessRules.NoColor);
        CheckInt(personal.GameCount(), 3, "personal: full, still 3");
        Check(personal.IndexOf(P2) < 0 && personal.GameIdAt(0) == P1 && personal.GameIdAt(2) == P4,
            "personal: full, the lowest ID overwritten", personal.GameIdAt(0) + " " + personal.GameIdAt(2));
        int saves = personal.saves;
        p1Pgn = PlayGame(me, "Opp A", P1, "e2e4 e7e5", ChessRules.Black);
        CheckInt(personal.GameCount(), 3, "personal: a game held replaced, still 3");
        CheckInt(personal.saves, saves + 1, "personal: replaced in one slot");
        CheckInt(personal.WinnerAt(personal.IndexOf(P1)), ChessRules.White, "personal: P1 replaced (now won by White)");
        CheckInt(personal.EndingAt(personal.IndexOf(P1)), ChessGame.EndingResignation, "personal: P1 by resignation");

        // The wall: newest first, each with its date.
        CheckString(personalWall.rows[0].text, TextWith("wall_row_personal", new string[] {
            "date", GameHistoryWall._DateText(P4), "white", me, "black", "Opp D",
            "ending", Text("ending_unfinished"), "result", "*", "moves", Text("moves_one") }),
            "personal wall: the newest at the top, with its date");
        Check(GameHistoryWall._DateText(P4).StartsWith("2026-10-0"), "personal wall: the date from the ID",
            GameHistoryWall._DateText(P4));
        CheckString(personalWall.rows[2].text, personalWall._RowText(personal.IndexOf(P1)),
            "personal wall: the oldest at the bottom");
        CheckWallShownOn(personalWall, 3, "personal wall: 3 games");

        // The pick: a game on the instance wall, then one on the personal wall replaces it.
        if (archive.GameCount() > 0) {
            wall._SelectRow(0);
            Check(arbiter.selectedArchive == archive, "pick: on the instance wall", "not");
            if (hasUI) CheckLoadButtonNow(true, "pick: Load it here shown in the frame of the pick");
        }
        int instanceDraws = wall.selectionDraws;
        personalWall._SelectRow(1);
        Check(arbiter.selectedArchive == personal && arbiter.selectedGameId == P3,
            "pick: the personal wall's pick replaces the instance wall's", arbiter.selectedGameId);
        Check(wall.selectionDraws > instanceDraws && wall._SelectedIndex() < 0 && wall.rows[0].color == wall.rowColor
            && !wall.exportButton.activeSelf, "pick: the instance wall stops showing it", "still shown");
        Check(personalWall.rows[1].color == personalWall.selectedColor, "pick: drawn on the personal wall", "not");
        RunExportChecks(personalWall, P3, p3Pgn, false, "export (personal)");

        // Load it here: P1, handed in this session, loads at once.
        personalWall._SelectRow(personal.GameCount() - 1 - personal.IndexOf(P1));
        CheckString(arbiter.selectedGameId, P1, "personal load: P1 picked");
        int loaded = hasUI ? ui._PressButton(ChessBoardUI.ButtonLoadHere) : board.LoadSelectedGame();
        CheckInt(loaded, ChessBoard.InputAccepted, "personal load: P1 loaded at once");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "personal load: the loaded state");
        Check(!arbiter.HasSelection(), "personal load: the pick cleared", "still " + arbiter.selectedGameId);
        string direct = LoadedText();
        CheckInt(board.ending, ChessGame.EndingResignation, "personal load: the logged ending kept");
        board._ClearState();

        // Read back from the PlayerData: the same readers, but no moves until imported.
        string before = PersonalText();
        personal._Forget();
        personal._Restore();
        CheckString(PersonalText(), before, "personal: read back from the PlayerData, the same");
        Check(!personal.HasMovesAt(personal.IndexOf(P1)), "personal: a game read back has no moves", "has moves");

        // Load it here: P1 read back is imported, with its Ending tag.
        personalWall._SelectRow(personal.GameCount() - 1 - personal.IndexOf(P1));
        loaded = hasUI ? ui._PressButton(ChessBoardUI.ButtonLoadHere) : board.LoadSelectedGame();
        CheckInt(loaded, ChessBoard.InputAccepted, "personal import: P1's import started");
        CheckInt(board.importStatus, ChessBoard.ImportRunning, "personal import: running");
        Check(!arbiter.HasSelection(), "personal import: the pick cleared", "still " + arbiter.selectedGameId);
        board.importer._ImportFor(float.MaxValue);
        board._ImportFinished();
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "personal import: loaded");
        CheckString(LoadedText(), direct, "personal import: the same moves, clocks, players and ending");
        CheckInt(board.ending, ChessGame.EndingResignation, "personal import: the Ending tag's ending");
        board._ClearState();
        if (hasUI) ui._UpdateButtons();

        // Delete: two presses, the first only arming it.
        Check(personal.CanDelete(), "delete: allowed on the personal archive", "not");
        CheckDeleteButtons(3, "delete: a button on each entry with a game");
        Check(!personalWall._PressDelete(0), "delete: the first press only arms it", "deleted");
        CheckString(personalWall.deleteLabels[0].text, Text("wall_delete_sure"), "delete: armed, Sure?");
        CheckInt(personal.GameCount(), 3, "delete: armed, nothing deleted");
        Check(personalWall._PressDelete(0), "delete: the second press deletes", "not deleted");
        Check(personal.GameCount() == 2 && personal.IndexOf(P4) < 0, "delete: P4 gone", "" + personal.GameCount());
        CheckString(SavedSlotWith(""), "", "delete: its slot saved empty");
        CheckDeleteButtons(2, "delete: the buttons follow the games");

        // A picked game deleted clears the pick.
        personalWall._SelectRow(personal.GameCount() - 1 - personal.IndexOf(P3));
        CheckString(arbiter.selectedGameId, P3, "delete: P3 picked");
        int entry = personal.GameCount() - 1 - personal.IndexOf(P3);
        personalWall._PressDelete(entry);
        personalWall._PressDelete(entry);
        Check(!arbiter.HasSelection(), "delete: the picked game deleted, the pick cleared",
            "still " + arbiter.selectedGameId);
        if (hasUI) CheckLoadButtonNow(false, "delete: Load it here hidden in that frame");

        // A picked oldest game overwritten by a new one clears the pick.
        PlayGame(me, "Opp E", P5, "e2e4", ChessRules.NoColor);
        PlayGame(me, "Opp F", P6, "e2e4", ChessRules.NoColor);
        CheckInt(personal.GameCount(), 3, "overwrite: full again");
        personalWall._SelectRow(2);
        CheckString(arbiter.selectedGameId, P1, "overwrite: the oldest (P1) picked");
        PlayGame(me, "Opp G", "20261011100000-000007", "e2e4", ChessRules.NoColor);
        Check(personal.IndexOf(P1) < 0 && !arbiter.HasSelection(), "overwrite: the picked oldest game overwritten, "
            + "the pick cleared", "still " + arbiter.selectedGameId);
        if (hasUI) CheckLoadButtonNow(false, "overwrite: Load it here hidden in that frame");

        // The test keys emptied, the real ones read again.
        personal._ClearSaved();
        personal.capacity = savedCapacity;
        personal.keyPrefix = savedPrefix;
        personal._Forget();
        personal._Restore();
        board._ClearState();
        if (hasUI) ui._UpdateButtons();
    }

    // ---- The PGN Event and Site kept with each game (step 14.13) ----

    const string TagGame = "20261013100000-000013";
    // With a " and a \, written escaped.
    const string EscapedEvent = "Tag \"Event\" 2 \\";
    const string EscapedEventLine = "[Event \"Tag \\\"Event\\\" 2 \\\\\"]\n";

    // A game in progress follows the arbiter's Event and Site as they change; once ended it keeps them, on the board,
    // in both archives (the personal one read back from PlayerData too), in both walls' exports and in a reload from
    // either archive, whatever the arbiter says afterwards. A pasted PGN keeps its own tags ("?" without them), and a
    // loaded game continued takes the arbiter's. The arbiter's tags are set locally and put back; the personal
    // archive runs on its test keys.
    void RunPgnTagChecks() {
        if (!BoardUsesArchive() || !Utilities.IsValid(personal) || !Utilities.IsValid(personalWall)
            || !Utilities.IsValid(board.importer)) {
            Check(false, "tags: board, archives, walls and importer assigned", "one is missing");
            return;
        }
        ChessArbiter arbiter = board.arbiter;
        string savedEvent = arbiter.pgnEvent;
        string savedSite = arbiter.pgnSite;
        int savedCapacity = personal.capacity;
        string savedPrefix = personal.keyPrefix;
        personal.capacity = PersonalCapacity;
        personal.keyPrefix = PersonalTestPrefix;
        personal._Forget();
        personal._Restore();
        personal._ClearSaved();
        arbiter._ClearSelection();
        string me = ChessUtil._LocalName("");

        // In progress: the arbiter's now, a change reaching it at once.
        arbiter.SetPgnEvent("Tag Event 1");
        arbiter.SetPgnSite("Tag Site 1");
        board._ClearState();
        board.JoinAs(ChessRules.White, me);
        board.JoinAs(ChessRules.Black, "Tag Opp");
        board.StartGameAs(me);
        board.gameId = TagGame;
        board.PieceDroppedAs(me, Sq("f2"), Sq("f3"));
        CheckTags(board.GamePgn(), "[Event \"Tag Event 1\"]\n", "Tag Site 1", "tags: in progress, the arbiter's");
        arbiter.SetPgnEvent(EscapedEvent);
        CheckTags(board.GamePgn(), EscapedEventLine, "Tag Site 1", "tags: changed mid-game, the game's PGN follows");
        CheckString(board.gameEvent + "|" + board.gameSite, "|", "tags: in progress, none kept yet");
        board.PieceDroppedAs("Tag Opp", Sq("e7"), Sq("e5"));
        board.PieceDroppedAs(me, Sq("g2"), Sq("g4"));
        board.PieceDroppedAs("Tag Opp", Sq("d8"), Sq("h4"));
        CheckInt(board.lifecycle, ChessBoard.LifecycleEnded, "tags: the fool's mate ended");
        string logged = board.lastLoggedPgn;
        CheckTags(logged, EscapedEventLine, "Tag Site 1", "tags: logged with the values as it ended");
        CheckString(board.gameEvent + "|" + board.gameSite, EscapedEvent + "|Tag Site 1", "tags: kept by the board");

        // Synced with the ended state: a receiver gets them.
        board._PackState();
        CheckString(board.syncedGameEvent + "|" + board.syncedGameSite, EscapedEvent + "|Tag Site 1",
            "tags: sent with the ended state");
        board.gameEvent = "";
        board.gameSite = "";
        board._ReceiveState();
        CheckString(board.gameEvent + "|" + board.gameSite, EscapedEvent + "|Tag Site 1", "tags: received");

        // Changed afterwards: the ended game keeps its own everywhere.
        arbiter.SetPgnEvent("Tag Event 3");
        arbiter.SetPgnSite("Tag Site 3");
        CheckString(board.GamePgn(), logged, "tags: changed after the end, the board's PGN unchanged");
        int index = archive.IndexOf(TagGame);
        int mine = personal.IndexOf(TagGame);
        Check(index >= 0 && mine >= 0, "tags: archived in both archives", "instance " + index + ", personal " + mine);
        if (index < 0 || mine < 0) {
            EndPgnTagChecks(savedEvent, savedSite, savedCapacity, savedPrefix);
            return;
        }
        CheckString(archive.EventAt(index) + "|" + archive.SiteAt(index), EscapedEvent + "|Tag Site 1",
            "tags: the instance archive's");
        CheckString(personal.EventAt(mine) + "|" + personal.SiteAt(mine), EscapedEvent + "|Tag Site 1",
            "tags: the personal archive's");
        board._ClearState();
        wall._RefreshIfChanged();
        arbiter._SelectGame(archive, TagGame);
        RunExportChecks(wall, TagGame, logged, true, "tags: export (instance)");
        arbiter._SelectGame(personal, TagGame);
        RunExportChecks(personalWall, TagGame, logged, false, "tags: export (personal)");
        arbiter._ClearSelection();

        // Reloaded from either archive: the game's own, not the arbiter's.
        CheckInt(board.LoadArchivedGameFrom(archive, TagGame), ChessBoard.InputAccepted, "tags: loaded (instance)");
        CheckTags(board.GamePgn(), EscapedEventLine, "Tag Site 1", "tags: loaded from the instance archive");
        board._ClearState();
        CheckInt(board.LoadArchivedGameFrom(personal, TagGame), ChessBoard.InputAccepted, "tags: loaded (personal)");
        CheckTags(board.GamePgn(), EscapedEventLine, "Tag Site 1", "tags: loaded from the personal archive");
        board._ClearState();
        personal._Forget();
        personal._Restore();
        mine = personal.IndexOf(TagGame);
        CheckString(mine < 0 ? "none" : personal.EventAt(mine) + "|" + personal.SiteAt(mine),
            EscapedEvent + "|Tag Site 1", "tags: the personal archive's read back from PlayerData");
        arbiter._SelectGame(personal, TagGame);
        CheckInt(board.LoadSelectedGame(), ChessBoard.InputAccepted, "tags: the personal game's import started");
        board.importer._ImportFor(float.MaxValue);
        board._ImportFinished();
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "tags: the personal game imported");
        CheckTags(board.GamePgn(), EscapedEventLine, "Tag Site 1", "tags: imported from the personal archive");

        // A pasted PGN keeps its own tags; one without them writes "?", not this instance's.
        board._ClearState();
        CheckTags(Import("[Event \"Pasted Event\"]\n[Site \"Pasted Site\"]\n\n1. e4 e5 *\n"),
            "[Event \"Pasted Event\"]\n", "Pasted Site", "tags: a pasted PGN's own");
        CheckString(board.gameEvent + "|" + board.gameSite, "Pasted Event|Pasted Site", "tags: a pasted PGN's kept");
        board._ClearState();
        CheckTags(Import("1. e4 e5 *\n"), "[Event \"?\"]\n", "?", "tags: a pasted PGN without them");
        board._ClearState();
        CheckTags(Import("[Event \"?\"]\n[Site \"?\"]\n\n1. e4 *\n"), "[Event \"?\"]\n", "?",
            "tags: a pasted PGN's ? read as none");
        CheckString(board.gameEvent + "|" + board.gameSite, "|", "tags: ? kept as none");

        // Continued: a new game here, with this instance's.
        board._ClearState();
        Import("[Event \"Pasted Event\"]\n[Site \"Pasted Site\"]\n\n1. e4 e5 *\n");
        board.JoinAs(ChessRules.White, "Tag Ana");
        board.JoinAs(ChessRules.Black, "Tag Bob");
        CheckInt(board.ContinueGameAs("Tag Ana"), ChessBoard.InputAccepted, "tags: the pasted game continued");
        CheckTags(board.GamePgn(), "[Event \"Tag Event 3\"]\n", "Tag Site 3", "tags: continued, the arbiter's");
        CheckString(board.gameEvent + "|" + board.gameSite, "|", "tags: continued, none kept");

        EndPgnTagChecks(savedEvent, savedSite, savedCapacity, savedPrefix);
    }

    // The arbiter's tags, the personal archive's keys and the board put back.
    void EndPgnTagChecks(string savedEvent, string savedSite, int savedCapacity, string savedPrefix) {
        board._ClearState();
        board.arbiter._ClearSelection();
        board.arbiter.pgnEvent = savedEvent;
        board.arbiter.pgnSite = savedSite;
        personal._ClearSaved();
        personal.capacity = savedCapacity;
        personal.keyPrefix = savedPrefix;
        personal._Forget();
        personal._Restore();
        if (Utilities.IsValid(ui) && ui.board == board) ui._UpdateButtons();
    }

    // Imports text onto the reset board at once, and returns the board's PGN once loaded ("" if it wasn't).
    string Import(string text) {
        if (board._StartImportAs("", text) != ChessBoard.InputAccepted) return "";
        board.importer._ImportFor(float.MaxValue);
        board._ImportFinished();
        return board.lifecycle == ChessBoard.LifecycleLoaded ? board.GamePgn() : "";
    }

    // pgn's Event line is eventLine and its Site tag site.
    void CheckTags(string pgn, string eventLine, string site, string name) {
        Check(pgn.StartsWith(eventLine + "[Site \"" + site + "\"]\n"), name,
            "expected " + eventLine.Trim() + " [Site \"" + site + "\"], PGN:\n" + pgn);
    }

    // Plays a game on the board between white and black with the Game ID id: the moves ("e2e4 e7e5", alternately
    // White's and Black's), then, unless they ended it, resigns for resigner (a ChessRules colour), or resets it
    // unfinished for NoColor. Returns the PGN the board logged ("" if none), and leaves the board reset.
    string PlayGame(string white, string black, string id, string moves, int resigner) {
        board._ClearState();
        board.JoinAs(ChessRules.White, white);
        board.JoinAs(ChessRules.Black, black);
        board.StartGameAs(white);
        board.gameId = id;
        int logged = board.loggedGames;
        string[] list = moves.Split(new char[] { ' ' });
        for (int i = 0; i < list.Length; i++) {
            string mover = i % 2 == 0 ? white : black;
            board.PieceDroppedAs(mover, Sq(list[i].Substring(0, 2)), Sq(list[i].Substring(2, 2)));
        }
        if (board.lifecycle == ChessBoard.LifecycleInGame) {
            if (resigner == ChessRules.NoColor) board.ResetBoardAs(white, false);
            else board.ResignAs(resigner, resigner == ChessRules.White ? white : black);
        }
        string pgn = board.loggedGames > logged ? board.lastLoggedPgn : "";
        board._ClearState();
        return pgn;
    }

    // The saved text of the personal archive's slot holding the game with this ID ("" if none), from the PlayerData.
    string SavedText(string gameId) {
        for (int i = 0; i < personal.Capacity(); i++) {
            string text;
            if (PlayerData.TryGetString(Networking.LocalPlayer, personal._SlotKey(i), out text)
                && text.IndexOf("[GameID \"" + gameId + "\"]") >= 0) {
                return text;
            }
        }
        return "";
    }

    // The text of the first empty slot in the PlayerData, "-" if none is empty (for SavedSlotWith("")).
    string SavedSlotWith(string value) {
        for (int i = 0; i < personal.Capacity(); i++) {
            string text;
            if (PlayerData.TryGetString(Networking.LocalPlayer, personal._SlotKey(i), out text) && text == value) {
                return text;
            }
        }
        return "-";
    }

    // Every reader of every game in the personal archive, as one text.
    string PersonalText() {
        string text = "";
        for (int i = 0; i < personal.GameCount(); i++) {
            text += personal.GameIdAt(i) + "|" + personal.ContinuedFromAt(i) + "|" + personal.WhiteAt(i) + "|"
                + personal.BlackAt(i) + "|" + personal.StartFenAt(i) + "|" + personal.ModeAt(i) + "|"
                + personal.EndingAt(i) + "|" + personal.WinnerAt(i) + "|" + personal.BaseTimeAt(i) + "|"
                + personal.IncrementAt(i) + "|" + personal.MoveCountAt(i) + "|" + personal._SequenceAt(i) + "|"
                + personal.PgnAt(i) + "\n";
        }
        return text;
    }

    // The loaded game on the board: its moves, clocks, players, result and ending, as one text.
    string LoadedText() {
        string text = board.replaySourceWhite + "|" + board.replaySourceBlack + "|" + board.replaySourceResult + "|"
            + board.ending + "|" + board.winner + "|" + board.gameId + "|" + board.moveCount + ":";
        for (int i = 0; i < board.moveCount; i++) text += " " + board.moveHistory[i] + "/" + board.clockHistory[i];
        return text;
    }

    // The personal wall's delete buttons shown on its first count entries only.
    void CheckDeleteButtons(int count, string name) {
        string errors = "";
        for (int i = 0; i < personalWall.deleteButtons.Length; i++) {
            GameObject button = personalWall.deleteButtons[i];
            if (Utilities.IsValid(button) && button.activeSelf != i < count) errors += " " + i;
        }
        Check(errors == "", name, "wrong on entries" + errors);
    }

    // On's first count entries shown and the rest hidden, the scroll content as tall as those shown.
    void CheckWallShownOn(GameHistoryWall on, int count, string name) {
        string differs = "";
        for (int i = 0; i < on.rowTargets.Length && differs == ""; i++) {
            if (Utilities.IsValid(on.rowTargets[i]) && on.rowTargets[i].gameObject.activeSelf != i < count) {
                differs = "entry " + i + (i < count ? " hidden" : " shown");
            }
        }
        Check(differs == "", name + ": entries shown", differs);
        CheckInt((int)on.content.sizeDelta.y, (int)(count * on.entryHeight), name + ": content height");
    }

    // ---- Test games ----

    // Test game n: every field derived from n, with n % 7 + 1 moves.
    bool AddTestGame(int n) {
        int count = n % 7 + 1;
        int[] moves = new int[count];
        int[] clocks = new int[count];
        for (int k = 0; k < count; k++) {
            moves[k] = n * 100 + k;
            clocks[k] = n * 1000 + k;
        }
        return archive._AddGame("test-" + n, n % 3 == 0 ? "prev-" + n + "/4" : "", "White " + n, "Black " + n, n % 2,
            n % 11, n % 3 - 1 < 0 ? ChessRules.NoColor : n % 2, "fen " + n, n % 4 == 0 ? ChessArbiter.NoClock : n * 10,
            n % 5, moves, clocks, count, "", "Event " + n, n % 2 == 0 ? "" : "Site " + n);
    }

    void CheckTestGame(int index, int n) {
        string name = "read: game " + index + " is test-" + n + ": ";
        CheckString(archive.GameIdAt(index), "test-" + n, name + "ID");
        CheckString(archive.ContinuedFromAt(index), n % 3 == 0 ? "prev-" + n + "/4" : "", name + "ContinuedFrom");
        CheckString(archive.WhiteAt(index), "White " + n, name + "White");
        CheckString(archive.BlackAt(index), "Black " + n, name + "Black");
        CheckString(archive.StartFenAt(index), "fen " + n, name + "FEN");
        CheckInt(archive.ModeAt(index), n % 2, name + "mode");
        CheckInt(archive.EndingAt(index), n % 11, name + "ending");
        CheckInt(archive.WinnerAt(index), n % 3 - 1 < 0 ? ChessRules.NoColor : n % 2, name + "winner");
        CheckInt(archive.BaseTimeAt(index), n % 4 == 0 ? ChessArbiter.NoClock : n * 10, name + "base time");
        CheckInt(archive.IncrementAt(index), n % 5, name + "increment");
        CheckString(archive.EventAt(index), "Event " + n, name + "Event");
        CheckString(archive.SiteAt(index), n % 2 == 0 ? "" : "Site " + n, name + "Site");
        int count = n % 7 + 1;
        CheckInt(archive.MoveCountAt(index), count, name + "move count");
        if (archive.MoveCountAt(index) != count) return;
        int[] moves = new int[count];
        int[] clocks = new int[count];
        archive.CopyMovesAt(index, moves);
        archive.CopyClocksAt(index, clocks);
        bool same = true;
        for (int k = 0; k < count; k++) {
            if (moves[k] != n * 100 + k || clocks[k] != n * 1000 + k) same = false;
        }
        Check(same, name + "moves and clocks", "differ");
    }

    void SaveSends() {
        for (int i = 0; i < HolderCount; i++) sendsBefore[i] = archive.holders[i].sends;
    }

    // Holder 0 sent first times and holder 1 second times since SaveSends, and no other holder anything.
    void CheckSends(int first, int second, string name) {
        string got = "";
        bool ok = true;
        for (int i = 0; i < HolderCount; i++) {
            int sent = archive.holders[i].sends - sendsBefore[i];
            int expected = i == 0 ? first : i == 1 ? second : 0;
            if (sent != expected) ok = false;
            got += (i == 0 ? "" : " ") + sent;
        }
        Check(ok, name, "sends per holder: " + got);
    }

    int Sq(string name) {
        return ChessRules.ParseSquare(name);
    }
}
