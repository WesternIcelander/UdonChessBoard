using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Steps 10.1a to 10.2b test behaviour (PLAN.md section 10): the GameArchive and its holders, the history wall and
// loading an archived game onto the board (docs/archive.md). The holders are synced, so like BoardTest it runs only
// with no one else in the instance; it uses the board once BoardTest and ViewTest have finished with it, and puts
// back the games it found archived when done. Logs
// "[ArchiveTest] PASS: <check>" or "[ArchiveTest] FAIL: <check>: <details>" per check, then a summary line.
// Built in the scene by Tools > Chess Board > Create Archive Test (Editor/ArchiveTestBuilder.cs).
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ArchiveTest : ChessTest {
    protected override string TestName() {
        return "ArchiveTest";
    }

    public GameArchive archive;
    // A board whose archive is the one above, for a game logged by a board.
    public ChessBoard board;
    // Waited for, since they change the board; none to start at once.
    public BoardTest boardTest;
    public ViewTest viewTest;
    // The scene's history wall on this archive (step 10.2a).
    public GameHistoryWall wall;
    // The board's UI, for its Load it here button (steps 10.2b, 12.1).
    public ChessBoardUI ui;

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
        if (!Utilities.IsValid(archive)) {
            Check(false, "archive assigned", "assign ArchiveTest's Archive in the Inspector");
            LogSummary();
            return;
        }
        RunWiringChecks();
        SaveGames();
        bool hasWall = Utilities.IsValid(wall) && wall.archive == archive;
        Check(hasWall, "wall: assigned on this archive", "assign ArchiveTest's Wall (Create History Wall, then "
            + "Create Archive Test again)");
        if (hasWall) RunWallSetupChecks();
        RunBoardChecks();
        if (hasWall) RunWallBoardChecks();
        if (hasWall) RunLoadChecks();
        RunAddChecks();
        if (hasWall) RunWallAddChecks();
        RunWrapChecks();
        if (hasWall) RunWallFullChecks();
        RunRecentChecks();
        if (hasWall) RunWallPickGoneChecks();
        RunSyncChecks();
        RunOverwrittenRecentChecks();
        archive.ClearArchive();
        CheckInt(archive.GameCount(), 0, "end: archive emptied");
        if (hasWall) {
            Check(wall.RefreshIfChanged(), "wall: redrawn once emptied", "not redrawn");
            CheckWallRows(0, 0, "wall: emptied");
            CheckString(wall.title.text, "Previous games: none yet", "wall: emptied, title");
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
        if (Utilities.IsValid(board)) Check(board.archive == archive, "wiring: board's archive", "not this archive");

        VRCPlayerApi local = Networking.LocalPlayer;
        Check(Networking.IsOwner(archive.gameObject), "ownership: local player owns the archive alone", "not owner");
        Check(archive.AllReady(), "ownership: every holder ready", "a holder isn't ready");
        Check(archive.holders[3].MayOwn(local), "ownership: archive owner may own a holder", "refused");
        Check(!archive.recent.MayOwn(local), "ownership: nobody may own the recent holder", "allowed");
    }

    // ---- A game logged by the board ----

    void RunBoardChecks() {
        if (!Utilities.IsValid(board) || board.archive != archive) {
            Check(false, "board: assigned with this archive", "assign ArchiveTest's Board");
            return;
        }
        if (Utilities.IsValid(boardTest) && board.loggedGames > 0) {
            Check(archive.GameCount() > 0, "board: earlier tests' games archived",
                board.loggedGames + " logged, none archived");
        }
        Check(archive.ClearArchive(), "clear: accepted for the owner", "refused");
        CheckInt(archive.GameCount(), 0, "clear: no games");

        board.ClearState();
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
        board.ClearState();
    }

    // ---- Adding: holders filled in order, read back, duplicates ----

    void RunAddChecks() {
        archive.ClearArchive();
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
            CheckInt(archive.SlotAt(i), i, "read: game " + i + " slot");
            CheckInt(archive.SequenceAt(i), i + 1, "read: game " + i + " sequence");
            CheckTestGame(i, i + 1);
        }

        SaveSends();
        Check(!AddTestGame(7), "duplicate: test-7 again refused (in the recent games)", "accepted");
        // test-1 has left the recent games (they hold the last 5), but it's archived: not added again.
        AddTestGame(1);
        CheckInt(archive.GameCount(), 7, "duplicate: still 7 games");
        CheckSends(0, 0, "duplicate: nothing sent");
        int[] moves = new int[4];
        Check(!archive.AddGame("", "", "W", "B", 0, 0, 0, "", -1, 0, moves, moves, 2), "refused: no game ID",
            "accepted");
        Check(!archive.AddGame("bad-1", "", "W", "B", 0, 0, 0, "", -1, 0, moves, moves, 0), "refused: no moves",
            "accepted");
        Check(!archive.AddGame("bad-2", "", "W", "B", 0, 0, 0, "", -1, 0, moves, moves, 5),
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
        CheckInt(archive.SlotAt(Capacity - 1), 0, "wrap: newest read from slot 0");
        bool ordered = true;
        for (int i = 0; i < Capacity; i++) {
            if (archive.GameIdAt(i) != "test-" + (i + 2) || archive.SequenceAt(i) != i + 2) ordered = false;
        }
        Check(ordered, "wrap: read oldest first, test-2 to test-51", "order differs");
        CheckTestGame(0, 2);
        CheckTestGame(Capacity - 1, Capacity + 1);
        AddTestGame(Capacity + 2);
        AddTestGame(Capacity + 3);
        CheckString(archive.GameIdAt(0), "test-4", "wrap: after two more, oldest test-4");
        CheckInt(archive.SlotAt(Capacity - 1), 2, "wrap: newest in slot 2");
    }

    // ---- Recent games: kept locally, archived when the owner flushes ----

    void RunRecentChecks() {
        GameArchiveHolder recent = archive.recent;
        bool lastFive = true;
        for (int n = Capacity - 1; n <= Capacity + 3; n++) {
            if (recent.SlotOf("test-" + n) < 0) lastFive = false;
        }
        Check(lastFive && recent.SlotOf("test-" + (Capacity - 2)) < 0, "recent: the last 5 games kept",
            "the recent games differ");
        SaveSends();
        Check(archive.StoreRecent("rec-1", "", "R W", "R B", 0, 0, 0, "", -1, 0, new int[] { 11, 12 },
            new int[] { 1, 2 }, 2), "recent: rec-1 stored", "refused");
        archive.StoreRecent("rec-2", "x/1", "R W", "R B", 0, 0, 0, "", -1, 0, new int[] { 21 }, new int[] { 3 }, 1);
        Check(!archive.IsArchived("rec-1") && !archive.IsArchived("rec-2"), "recent: stored only (no flush)",
            "archived");
        CheckSends(0, 0, "recent: nothing sent while only stored");
        CheckInt(archive.Flush(), 2, "flush: two recent games archived");
        CheckString(archive.GameIdAt(Capacity - 2), "rec-1", "flush: rec-1 second newest");
        CheckString(archive.GameIdAt(Capacity - 1), "rec-2", "flush: rec-2 newest");
        CheckString(archive.ContinuedFromAt(Capacity - 1), "x/1", "flush: rec-2's ContinuedFrom");
        CheckInt(archive.MoveCountAt(Capacity - 2), 2, "flush: rec-1's moves");
        archive.CopyMovesAt(Capacity - 2, scratch);
        Check(scratch[0] == 11 && scratch[1] == 12, "flush: rec-1's moves read back", "differ");
        CheckString(archive.GameIdAt(0), "test-6", "flush: oldest now test-6");
        CheckInt(archive.Flush(), 0, "flush: nothing more to add");
    }

    // ---- A holder update arriving ----

    void RunSyncChecks() {
        GameArchiveHolder holder = archive.holders[1];
        // Holder 1 holds test-6 to test-10; test-6 is the oldest game.
        holder.PackState();
        holder.Clear();
        holder.ReceiveState();
        CheckString(archive.GameIdAt(0), "test-6", "sync: holder 1 back from its update");
        CheckTestGame(0, 6);
        CheckTestGame(4, 10);

        holder.PackState();
        holder.syncedMoves = new int[1];
        holder.ReceiveState();
        CheckString(archive.GameIdAt(0), "test-6", "sync: an invalid update ignored");
        holder.PackState();
        holder.syncedGameIds = new string[GameArchiveHolder.Capacity];
        holder.ReceiveState();
        CheckString(archive.GameIdAt(0), "test-6", "sync: an update with a null string ignored");
        CheckTestGame(0, 6);

        GameArchiveHolder recent = archive.recent;
        int recentSlot = recent.SlotOf("rec-2");
        recent.syncedSequences = new int[GameArchiveHolder.Capacity];
        recent.ReceiveState();
        CheckInt(recent.SlotOf("rec-2"), recentSlot, "sync: the recent holder ignores updates");
        recent.PackState();
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
            if (archive.IsRecentWaiting(i)) noneWaiting = false;
        }
        Check(noneWaiting, "overwritten: the archived recent games aren't waiting", "one is");
        SaveSends();
        int[] one = { 1 };
        for (int h = 0; h < HolderCount; h++) {
            for (int s = 0; s < GameArchiveHolder.Capacity; s++) {
                int k = h * GameArchiveHolder.Capacity + s;
                archive.holders[h].WriteSlot(s, 1001 + k, "other-" + k, "", "Other W", "Other B", 0, 0,
                    ChessRules.NoColor, "", ChessArbiter.NoClock, 0, one, one, 1);
            }
            archive.OnHolderChanged();
        }
        CheckInt(archive.GameCount(), Capacity, "overwritten: 50 other games");
        CheckString(archive.GameIdAt(Capacity - 1), "other-" + (Capacity - 1), "overwritten: the newest is other-49");
        Check(!archive.IsArchived("rec-2") && !archive.IsArchived("test-53"),
            "overwritten: the recent games not archived again", "archived again");
        CheckSends(0, 0, "overwritten: nothing sent");
        CheckInt(archive.Flush(), 0, "overwritten: a flush adds nothing");
        noneWaiting = true;
        for (int i = 0; i < GameArchiveHolder.Capacity; i++) {
            if (archive.IsRecentWaiting(i)) noneWaiting = false;
        }
        Check(noneWaiting, "overwritten: none waiting", "one is");

        // A game stored and never seen archived still waits and goes in; one already archived when stored doesn't.
        archive.StoreRecent("rec-3", "", "R W", "R B", 0, 0, 0, "", -1, 0, one, one, 1);
        Check(archive.IsRecentWaiting(recent.SlotOf("rec-3")), "overwritten: rec-3 stored, waiting", "not waiting");
        CheckInt(archive.Flush(), 1, "overwritten: rec-3 archived");
        CheckString(archive.GameIdAt(Capacity - 1), "rec-3", "overwritten: rec-3 newest");
        Check(!archive.IsRecentWaiting(recent.SlotOf("rec-3")), "overwritten: rec-3 no longer waiting", "waiting");
        archive.StoreRecent("other-49", "", "Other W", "Other B", 0, 0, 0, "", -1, 0, one, one, 1);
        Check(!archive.IsRecentWaiting(recent.SlotOf("other-49")), "overwritten: an archived game stored, not waiting",
            "waiting");
        CheckInt(archive.Flush(), 0, "overwritten: nothing more to add");
    }

    // ---- The games archived before the checks, kept and put back, so the wall shows them afterwards ----

    void SaveGames() {
        savedCount = archive.GameCount();
        savedIds = new string[savedCount];
        savedContinuedFroms = new string[savedCount];
        savedWhites = new string[savedCount];
        savedBlacks = new string[savedCount];
        savedFens = new string[savedCount];
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
            archive.AddGame(savedIds[i], savedContinuedFroms[i], savedWhites[i], savedBlacks[i], savedInfos[at],
                savedInfos[at + 1], savedInfos[at + 2], savedFens[i], savedInfos[at + 3], savedInfos[at + 4], moves,
                clocks, count);
        }
        CheckInt(archive.GameCount(), savedCount, "end: the games archived before the checks put back");
        for (int i = 0; i < savedCount && i < archive.GameCount(); i++) {
            if (archive.GameIdAt(i) != savedIds[i] || archive.WhiteAt(i) != savedWhites[i]
                || archive.MoveCountAt(i) != savedOffsets[i + 1] - savedOffsets[i]) {
                same = false;
            }
        }
        Check(same, "end: the games put back read back oldest first", "a game differs");
        if (!hasWall) return;
        wall.RefreshIfChanged();
        bool shown = savedCount == 0 || wall.rows[savedCount - 1].text != "";
        if (savedCount < wall.rows.Length && wall.rows[savedCount].text != "") shown = false;
        Check(shown, "end: the wall shows the " + savedCount + " game(s) put back", "its rows don't");
    }

    // ---- The history wall (step 10.2a) ----

    void RunWallSetupChecks() {
        CheckInt(wall.rows.Length, Capacity, "wall: a row per game the archive holds");
        bool assigned = Utilities.IsValid(wall.title);
        for (int i = 0; i < wall.rows.Length; i++) {
            if (!Utilities.IsValid(wall.rows[i])) assigned = false;
        }
        Check(assigned, "wall: title and rows assigned", "a text is missing");
        CheckString(ChessGame.EndingDescription(ChessGame.EndingUnterminated, ChessRules.NoColor), "Unfinished",
            "wall: ending text, unfinished");
        CheckString(ChessGame.EndingDescription(ChessGame.EndingResignation, ChessRules.White),
            "White wins by resignation", "wall: ending text, resignation");
        CheckString(ChessGame.EndingDescription(ChessGame.EndingTimeForfeit, ChessRules.NoColor), "Draw on time",
            "wall: ending text, time forfeit drawn");
        CheckString(ChessGame.EndingDescription(ChessGame.EndingUnknown, ChessRules.Black), "Black wins",
            "wall: ending text, a loaded game's result");
    }

    // After RunBoardChecks: the board's game alone on the wall.
    void RunWallBoardChecks() {
        Check(wall.RefreshIfChanged(), "wall: redrawn after the board's game", "not redrawn");
        Check(!wall.RefreshIfChanged(), "wall: not redrawn again without a change", "redrawn");
        CheckString(wall.title.text, "Previous games", "wall: board's game, title");
        CheckString(wall.rows[0].text, "#1  Archive Ana vs Archive Bo: Unfinished (*), 1 move",
            "wall: board's game, row");
        bool rest = true;
        for (int i = 1; i < wall.rows.Length; i++) {
            if (wall.rows[i].text != "") rest = false;
        }
        Check(rest, "wall: board's game, the other rows empty", "a row isn't empty");
    }

    // After RunAddChecks: test-1 to test-7.
    void RunWallAddChecks() {
        Check(wall.RefreshIfChanged(), "wall: redrawn after adding", "not redrawn");
        CheckString(wall.title.text, "Previous games", "wall: 7 games, title");
        CheckString(wall.rows[0].text, "#1  White 1 vs Black 1: Black wins by checkmate (0-1), 1 move, Chess960",
            "wall: 7 games, test-1's row");
        CheckWallRows(1, 7, "wall: 7 games");
    }

    // After RunWrapChecks: full and wrapped, test-4 to test-53, oldest first across every holder.
    void RunWallFullChecks() {
        Check(wall.RefreshIfChanged(), "wall: redrawn after wrapping", "not redrawn");
        CheckString(wall.title.text, "Previous games", "wall: full, title");
        CheckWallRows(4, Capacity, "wall: full, test-4 to test-53");
        CheckString(wall.rows[Capacity - 1].text, ExpectedRow(Capacity + 3), "wall: full, the newest last");
        // Picked for RunWallPickGoneChecks, after RunRecentChecks overwrites it.
        wall.SelectRow(0);
        CheckString(wall.selectedGameId, "test-4", "pick: the oldest game picked");
    }

    // The wall's first count rows are test-first onwards, oldest first, and the rest empty.
    void CheckWallRows(int first, int count, string name) {
        string differs = "";
        for (int i = 0; i < wall.rows.Length && differs == ""; i++) {
            string expected = i < count ? ExpectedRow(first + i) : "";
            if (wall.rows[i].text != expected) {
                differs = "row " + i + " is \"" + wall.rows[i].text + "\", expected \"" + expected + "\"";
            }
        }
        Check(differs == "", name + ": rows", differs);
    }

    // Test game n's row (AddTestGame): its sequence is n, as each was archived in turn.
    string ExpectedRow(int n) {
        int ending = n % 11;
        int winner = n % 3 - 1 < 0 ? ChessRules.NoColor : n % 2;
        int moves = (n % 7 + 2) / 2;
        return "#" + n + "  White " + n + " vs Black " + n + ": " + ChessGame.EndingDescription(ending, winner)
            + " (" + ChessPgn.ResultText(ending, winner) + "), " + moves + (moves == 1 ? " move" : " moves")
            + (n % 2 == ChessGame.ModeChess960 ? ", Chess960" : "");
    }

    // ---- Picking a game and loading it (step 10.2b) ----

    // After RunWallBoardChecks, with the board's unfinished game archived: a fool's mate is played and archived too,
    // then the wall's rows pick games, Load it here shows only on a reset board with a game picked, and loading puts
    // the board in its loaded state with the game's moves, players and result, its notation filled in.
    void RunLoadChecks() {
        if (!Utilities.IsValid(board) || board.archive != archive || archive.GameCount() != 1) {
            Check(false, "load: the board's game archived", "no board, or " + archive.GameCount() + " games");
            return;
        }
        string firstId = archive.GameIdAt(0);
        board.ClearState();
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
        board.ClearState();
        CheckInt(archive.GameCount(), 2, "load: fool's mate archived");
        if (archive.GameCount() != 2 || archive.GameIdAt(1) != mateId) return;
        wall.RefreshIfChanged();

        // Picking on the wall.
        bool targets = wall.rowTargets.Length == wall.rows.Length;
        for (int i = 0; i < wall.rowTargets.Length; i++) {
            GameHistoryRow target = wall.rowTargets[i];
            if (!Utilities.IsValid(target) || target.wall != wall || target.index != i) targets = false;
        }
        Check(targets, "pick: every row has its target", "a row's target is missing or wrong (Create History Wall)");
        wall.ClearSelection();
        int changes = wall.selectionChanges;
        wall.SelectRow(5);
        Check(!wall.HasSelection() && wall.selectionChanges == changes, "pick: an empty row picks nothing",
            "picked " + wall.selectedGameId);
        if (targets) wall.rowTargets[1].Press();
        else wall.SelectRow(1);
        CheckString(wall.selectedGameId, mateId, "pick: row 2 picks the fool's mate");
        CheckInt(wall.SelectedIndex(), 1, "pick: its index");
        Check(wall.selectionChanges == changes + 1, "pick: counted as a change", "changes " + wall.selectionChanges);
        Check(wall.rows[1].color == wall.selectedColor && wall.rows[0].color == wall.rowColor,
            "pick: only the picked row coloured", "row 1 " + wall.rows[0].color + ", row 2 " + wall.rows[1].color);
        wall.SelectRow(1);
        Check(!wall.HasSelection() && wall.selectedGameId == "", "pick: pressed again, unpicked",
            "still " + wall.selectedGameId);
        Check(wall.rows[1].color == wall.rowColor, "pick: unpicked, its row uncoloured", "" + wall.rows[1].color);
        wall.SelectRow(0);
        CheckString(wall.selectedGameId, firstId, "pick: row 1 picks the board's first game");
        wall.SelectRow(1);
        CheckString(wall.selectedGameId, mateId, "pick: another row replaces the pick");
        Check(wall.rows[0].color == wall.rowColor, "pick: replaced, the first row uncoloured", "" + wall.rows[0].color);

        // Load it here: only on a reset board with a game picked.
        bool hasUI = Utilities.IsValid(ui) && ui.board == board && ui.historyWall == wall;
        Check(hasUI, "UI: assigned, on this board and the wall", "assign ArchiveTest's UI (Create Board View, "
            + "then Create Archive Test again)");
        if (hasUI) {
            CheckLoadButton(true, "Load it here: shown on a reset board with a game picked");
            CheckString(ui.ButtonText(ChessBoardUI.ButtonLoadHere), "Load it here", "Load it here: label");
            board.JoinAs(ChessRules.White, white);
            CheckLoadButton(false, "Load it here: hidden in the lobby");
            CheckInt(ui.PressButton(ChessBoardUI.ButtonLoadHere), ChessBoard.InputRejected,
                "Load it here: refused in the lobby");
            CheckInt(board.lifecycle, ChessBoard.LifecycleLobby, "Load it here: refused, still in the lobby");
            CheckInt(board.LoadArchivedGame(mateId), ChessBoard.InputRejected, "load: refused in the lobby");
            board.ClearState();
            wall.ClearSelection();
            CheckLoadButton(false, "Load it here: hidden with nothing picked");
            CheckInt(ui.PressButton(ChessBoardUI.ButtonLoadHere), ChessBoard.InputRejected,
                "Load it here: refused with nothing picked");
            CheckInt(board.lifecycle, ChessBoard.LifecycleReset, "Load it here: refused, the board still reset");
            wall.SelectRow(1);
        }
        CheckInt(board.LoadArchivedGame("no-such-game"), ChessBoard.InputRejected, "load: a game not archived refused");
        CheckInt(board.LoadArchivedGame(""), ChessBoard.InputRejected, "load: no ID refused");
        CheckInt(board.lifecycle, ChessBoard.LifecycleReset, "load: refused, the board still reset");

        // The fool's mate, loaded through the button.
        int loaded = hasUI ? ui.PressButton(ChessBoardUI.ButtonLoadHere) : board.LoadArchivedGame(mateId);
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
        CheckString(board.loadedBy, ChessUtil.LocalName(""), "load: the local player keeps the board (step 13.2)");
        if (hasUI) {
            Check(!wall.HasSelection(), "Load it here: the pick cleared once loaded", "still " + wall.selectedGameId);
            CheckLoadButton(false, "Load it here: hidden on the loaded board");
        }
        CheckInt(board.FirstMissingNotation(), 0, "notation: none on loading (the archive keeps none)");
        Check(board.FillNotationFor(float.MaxValue), "notation: filled in", "gaps left");
        CheckString(NotationText(board, 4), "f3 e5 g4 Qh4#", "notation: the fool's mate's");
        CheckInt(board.LoadArchivedGame(firstId), ChessBoard.InputRejected, "load: refused on a loaded board");

        // The unfinished game: no result.
        board.ClearState();
        CheckInt(board.LoadArchivedGame(firstId), ChessBoard.InputAccepted, "load: the unfinished game loaded");
        CheckInt(board.lifecycle, ChessBoard.LifecycleLoaded, "load: unfinished, the loaded state");
        CheckString(board.replaySourceResult, "*", "load: unfinished, result *");
        CheckInt(board.ending, ChessGame.EndingNone, "load: unfinished, no result");
        Check(board.FillNotationFor(float.MaxValue), "notation: unfinished, filled in", "gaps left");
        CheckString(NotationText(board, 2), "e4 c5", "notation: unfinished, the game's");
        board.ClearState();
        CheckInt(board.LoadArchivedGameAs("Lena", firstId), ChessBoard.InputAccepted, "load: as Lena");
        CheckString(board.loadedBy, "Lena", "load: as Lena, Lena keeps the board");
        Check(board.IsLockedToLoader(), "load: as Lena, locked to her", "not locked");
        board.ClearState();
        if (hasUI) ui.UpdateButtons();
    }

    // After RunWallFullChecks picked the oldest game (test-4) and RunRecentChecks overwrote it: the pick is gone.
    void RunWallPickGoneChecks() {
        wall.RefreshIfChanged();
        Check(!wall.HasSelection() && wall.selectedGameId == "", "pick: overwritten in the archive, unpicked",
            "still " + wall.selectedGameId);
    }

    // Every Load it here button is shown (or hidden) after the UI brings its buttons up to date, and the UI's
    // ButtonApplies agrees.
    void CheckLoadButton(bool shown, string name) {
        ui.UpdateButtons();
        string errors = "";
        int found = 0;
        for (int i = 0; i < ui.buttons.Length && i < ui.buttonActions.Length; i++) {
            if (ui.buttonActions[i] != ChessBoardUI.ButtonLoadHere || !Utilities.IsValid(ui.buttons[i])) continue;
            found++;
            if (ui.buttons[i].activeSelf != shown) errors += "button " + i + (shown ? " hidden; " : " shown; ");
        }
        if (found == 0) errors += "no Load it here button (Create Board View); ";
        if (ui.ButtonApplies(ChessBoardUI.ButtonLoadHere) != shown) errors += "applies " + !shown + "; ";
        Check(errors.Length == 0, name, errors);
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
        return archive.AddGame("test-" + n, n % 3 == 0 ? "prev-" + n + "/4" : "", "White " + n, "Black " + n, n % 2,
            n % 11, n % 3 - 1 < 0 ? ChessRules.NoColor : n % 2, "fen " + n, n % 4 == 0 ? ChessArbiter.NoClock : n * 10,
            n % 5, moves, clocks, count);
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
