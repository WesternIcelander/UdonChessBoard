using SiggiChessBoard;
using SiggiUSTools;
using UdonSharp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;

// Debug view (PLAN.md 8.1): a minimal ChessBoardListener that shows a board's state as text and has a button for each
// of the board's input methods, for the local player, so every client can act before ChessBoardView and the settings
// menu exist. It stays as a testing tool. Built in the scene by Tools > Chess Board > Create Debug View
// (Editor/ChessDebugViewBuilder.cs), which wires each button to the public method of the same name.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ChessDebugView : ChessBoardListener {
    // The board shown and driven; the view adds itself as a listener on Start.
    public ChessBoard board;
    // The board's squares, a1 = 0 to h8 = 63: a background and the piece's image (step 12.13: the 2D view's sprites,
    // hidden on an empty square).
    public Image[] squareImages = new Image[64];
    public Image[] pieceImages = new Image[64];
    // Each colour's piece sprites, pawn to king, as the 2D view's (set by the builder from Sprites2D/).
    public Sprite[] whiteSprites = new Sprite[6];
    public Sprite[] blackSprites = new Sprite[6];
    // Everything else about the board, and the result of the last button pressed.
    public TextMeshProUGUI statusText;
    // The PGN the import buttons import (8.7): Assets/Tests/PGN/Siggi-vs-Nuka.pgn.txt, set by the builder.
    public TextAsset importPgn;
    // The board's physical view, for the pieces' buttons and lines (9.2); found by the builder.
    public ChessBoardView boardView;

    // Clock choices cycled by CycleClock (base seconds, increment): none, 15 s (to see a flag fall), 1+1, 3+2.
    int[] clockBases = { ChessArbiter.NoClock, 15, 60, 180 };
    int[] clockIncrements = { 0, 0, 1, 2 };
    // Scripted games, as SAN by ply from the standard start: fool's mate, and a knight shuffle (repeated, for the
    // threefold claim and fivefold repetition).
    string[] foolsMate = { "f3", "e5", "g4", "Qh4#" };
    string[] knightShuffle = { "Nf3", "Nf6", "Ng1", "Ng8" };

    Color lightSquare;
    Color darkSquare;
    Color lastMoveTint;
    Color checkTint;

    int[] legalMoves = new int[ChessMoves.MaxMoves];
    string lastAction = "";
    // The arbiter's settings as last logged, so a change (made here or arriving from another client) is logged once.
    string lastArbiterSettings = "";
    // The board's owner as last logged (8.8).
    string lastBoardOwner = "";
    // The archive's changes and owner as last logged (10.1b).
    int lastArchiveChanges = -1;
    string lastArchiveOwner = "";
    float nextRefresh;
    bool dirty;

    void Start() {
        lightSquare = new Color(0.93f, 0.85f, 0.71f);
        darkSquare = new Color(0.71f, 0.53f, 0.39f);
        lastMoveTint = new Color(0.85f, 0.85f, 0.3f);
        checkTint = new Color(0.9f, 0.3f, 0.3f);
        if (!Utilities.IsValid(board)) {
            Debug.LogError("[ChessDebugView] " + gameObject.name + ": the Board field is empty.");
            return;
        }
        board.AddListener(this);
        Refresh();
    }

    // Redrawn on the frame after a change, and a few times a second for the running clocks. Drawing is slow (the
    // claimable draw, every Can… check), so a change only marks it: a test making thousands of changes in one frame
    // would otherwise run past Udon's time limit.
    void Update() {
        if (settleCheckAt >= 0 && Time.time >= settleCheckAt) {
            settleCheckAt = -1;
            LogPlacement("settled after " + settleChange, false);
        }
        if (!dirty && Time.time < nextRefresh) return;
        Refresh();
    }

    // A change arriving from another client (8.3) is logged with the state it brought, so each client's log shows the
    // updates reaching it. Every change but activity has the pieces checked once they've settled (9.6).
    public override void OnBoardChanged() {
        dirty = true;
        if (board.lastChange != ChessBoard.ChangeActivity) {
            settleCheckAt = Time.time + SettleSeconds;
            settleChange = ChangeName(board.lastChange) + (board.lastChangeReceived ? " (received)" : "");
        }
        if (!board.lastChangeReceived) return;
        Debug.Log("[ChessDebugView] received " + ChangeName(board.lastChange) + ": " + BoardSummary());
        string[] sent = board.syncedNotation;
        int nulls = 0;
        for (int i = 0; sent != null && i < sent.Length; i++) {
            if (sent[i] == null) nulls++;
        }
        if (sent == null || nulls > 0) {
            Debug.LogError("[ChessDebugView] received notation with "
                + (sent == null ? "no array" : nulls + " null entries"));
        }
        // Missing entries are filled in by the board over the next frames (OnNotationFilled).
        int missing = board.FirstMissingNotation();
        if (missing >= 0) Debug.Log("[ChessDebugView] received notation missing from ply " + missing);
    }

    // The board filled in missing notation (8.6): logged with how many entries it has filled in, how many frames the
    // walk took, and, after Forget notation, whether it filled them in as they were.
    public override void OnNotationFilled() {
        dirty = true;
        string text = "[ChessDebugView] notation filled in: " + board.filledNotation + " entries so far, the last walk "
            + "took " + board.lastFillFrames + " frames";
        if (forgottenCount > 0) {
            int compared = forgottenCount < board.moveCount ? forgottenCount : board.moveCount;
            int differ = 0;
            string firstDiffer = "";
            for (int ply = 0; ply < compared; ply++) {
                if (board.notation[ply] == forgotten[ply]) continue;
                if (differ == 0) {
                    firstDiffer = "; first at ply " + ply + ": " + board.notation[ply] + " for " + forgotten[ply];
                }
                differ++;
            }
            text += "; the " + compared + " entries before forgetting "
                + (differ == 0 ? "all match" : differ + " DIFFER" + firstDiffer);
            forgottenCount = 0;
        }
        Debug.Log(text);
    }

    // The board's state in one line, for the log: lifecycle, seats, moves, position, result, clocks, owner. The clocks
    // are the synced values (each side's stored ms and the turn start), so clients' lines can be compared exactly.
    string BoardSummary() {
        string last = board.moveCount > 0 ? " (last " + board.notation[board.moveCount - 1] + ")" : "";
        string result = board.ending != ChessGame.EndingNone
            ? ", " + board.ResultText() + " (" + EndingName(board.ending) + ")" : "";
        string clocks = board.HasClock()
            ? ", clocks White " + board.whiteTime + " ms, Black " + board.blackTime + " ms, turn start "
                + board.turnStart.ToString("F3")
            : "";
        return LifecycleName(board.lifecycle) + ", White " + SeatText(board.whitePlayer) + ", Black "
            + SeatText(board.blackPlayer)
            + (board.loadedBy.Length > 0
                ? ", loaded by " + board.loadedBy + (board.IsLockedToLoader() ? " (locked)" : "") : "")
            + ", " + board.moveCount + " moves" + last + ", "
            + ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber) + result + clocks
            + ", game " + (board.gameId.Length > 0 ? board.gameId : "none")
            + ", owner " + ChessUtil.OwnerName(board.gameObject, "nobody");
    }

    // Logs the board's owner when it changes (polled on each redraw), so a client's log shows ownership passing on,
    // including VRChat's handover when the owner leaves.
    void LogBoardOwner() {
        string owner = ChessUtil.OwnerName(board.gameObject, "nobody");
        if (owner == lastBoardOwner) return;
        Debug.Log("[ChessDebugView] board owner now " + owner + (lastBoardOwner.Length > 0 ? " (was " + lastBoardOwner
            + ")" : "") + ", " + VRCPlayerApi.GetPlayerCount() + " players here");
        lastBoardOwner = owner;
    }

    // ---- Buttons ----

    public void JoinWhite() {
        Report("Join White", board.JoinWhite());
    }

    public void JoinBlack() {
        Report("Join Black", board.JoinBlack());
    }

    public void LeaveWhite() {
        Report("Leave White", board.LeaveSeat(ChessRules.White));
    }

    public void LeaveBlack() {
        Report("Leave Black", board.LeaveSeat(ChessRules.Black));
    }

    public void ToggleMode() {
        int newMode = board.mode == ChessGame.ModeStandard ? ChessGame.ModeChess960 : ChessGame.ModeStandard;
        Report("Mode " + ModeName(newMode), board.SetMode(newMode));
    }

    // The next clock choice after the board's current one.
    public void CycleClock() {
        int next = 0;
        for (int i = 0; i < clockBases.Length; i++) {
            if (clockBases[i] == board.baseTime && clockIncrements[i] == board.increment) next = (i + 1) % clockBases.Length;
        }
        Report("Clock " + ClockName(clockBases[next], clockIncrements[next]),
            board.SetClock(clockBases[next], clockIncrements[next]));
    }

    public void StartGame() {
        Report("Start", board.StartGame());
    }

    public void PlayFoolsMate() {
        PlayScripted("Fool's mate", foolsMate, false);
    }

    public void PlayKnightShuffle() {
        PlayScripted("Knight shuffle", knightShuffle, true);
    }

    // A random legal move of the side to move.
    public void PlayRandom() {
        int count = ChessMoves.GenerateLegalMoves(board.board, board.state, legalMoves, true);
        if (count == 0) {
            lastAction = "Random move: no legal moves";
            Refresh();
            return;
        }
        PlayAsInput("Random move", legalMoves[Random.Range(0, count)]);
    }

    public void Resign() {
        int color = ChooseColor(board.SideToMove(), board.CanResign(board.SideToMove()));
        Report("Resign " + ColorName(color), board.Resign(color));
    }

    public void OfferDraw() {
        int color = ChooseColor(board.SideToMove(), board.CanOfferDraw(board.SideToMove()));
        Report("Offer draw " + ColorName(color), board.OfferDraw(color));
    }

    public void AcceptDraw() {
        int color = ChessRules.OtherColor(board.drawOfferColor);
        Report("Accept draw " + ColorName(color), board.AcceptDraw(color));
    }

    public void DeclineDraw() {
        int color = ChessRules.OtherColor(board.drawOfferColor);
        Report("Decline draw " + ColorName(color), board.DeclineDraw(color));
    }

    public void ClaimDraw() {
        Report("Claim draw", board.ClaimDraw());
    }

    public void EnterReplay() {
        Report("Enter replay", board.EnterReplay());
    }

    public void LeaveReplay() {
        Report("Leave replay", board.LeaveReplay());
    }

    public void StepBack() {
        Report("Step to " + (board.replayIndex - 1), board.StepTo(board.replayIndex - 1));
    }

    public void StepForward() {
        Report("Step to " + (board.replayIndex + 1), board.StepTo(board.replayIndex + 1));
    }

    // Asked by the side that moved last, when the local player holds both seats.
    public void RequestTakeback() {
        int preferred = ChessRules.OtherColor(board.SideToMove());
        int color = ChooseColor(preferred, board.CanRequestTakeback(preferred));
        Report("Request takeback " + ColorName(color), board.RequestTakeback(color));
    }

    public void AcceptTakeback() {
        int color = ChessRules.OtherColor(board.takebackColor);
        Report("Accept takeback " + ColorName(color), board.AcceptTakeback(color));
    }

    public void DeclineTakeback() {
        int color = ChessRules.OtherColor(board.takebackColor);
        Report("Decline takeback " + ColorName(color), board.DeclineTakeback(color));
    }

    public void ContinueGame() {
        Report("Continue from here", board.ContinueGame());
    }

    public void ResetBoard() {
        Report("Reset", board.ResetBoard());
    }

    // ---- Settings buttons: the arbiter's settings, changed as an organizer (taking ownership of the arbiter and
    // syncing them to every client; refused for anyone else). Overrides are for this board; the board only takes them
    // on a re-snapshot (or the next first join). ----

    public void ToggleTakebacks() {
        if (!BeginArbiterChange("Takebacks")) return;
        bool on = !board.arbiter.IsEffectiveOn(board.boardNumber, ChessArbiter.SettingTakebacks);
        EndArbiterChange("Arbiter takebacks " + OnOff(on),
            board.arbiter.SetSettingOverride(board.boardNumber, ChessArbiter.SettingTakebacks, on));
    }

    // All draw rules on, or "No draws" when any is on.
    public void ToggleDraws() {
        if (!BeginArbiterChange("Draws")) return;
        bool on = (board.arbiter.EffectiveSettings(board.boardNumber) & ChessArbiter.SettingsDrawRules) == 0;
        EndArbiterChange("Arbiter draw rules " + (on ? "all on" : "No draws"),
            board.arbiter.SetSettingOverride(board.boardNumber, ChessArbiter.SettingsDrawRules, on));
    }

    // Clock forced to 3+2, or not forced.
    public void ToggleClockForced() {
        if (!BeginArbiterChange("Clock forced")) return;
        bool on = !board.arbiter.IsEffectiveOn(board.boardNumber, ChessArbiter.SettingClockForced);
        bool done = board.arbiter.SetSettingOverride(board.boardNumber, ChessArbiter.SettingClockForced, on);
        if (on) {
            board.arbiter.SetNumberOverride(board.boardNumber, ChessArbiter.NumberForcedBaseTime, 180);
            board.arbiter.SetNumberOverride(board.boardNumber, ChessArbiter.NumberForcedIncrement, 2);
        }
        EndArbiterChange("Arbiter clock forced " + (on ? "3+2" : "off"), done);
    }

    public void ClearOverrides() {
        if (!BeginArbiterChange("Clear overrides")) return;
        EndArbiterChange("Arbiter overrides cleared", board.arbiter.ClearOverrides(board.boardNumber));
    }

    // The instance default for takebacks (what boards without an override get).
    public void ToggleDefaultTakebacks() {
        if (!BeginArbiterChange("Default takebacks")) return;
        bool on = !board.arbiter.IsDefaultOn(ChessArbiter.SettingTakebacks);
        board.arbiter.SetDefaultSetting(ChessArbiter.SettingTakebacks, on);
        EndArbiterChange("Arbiter default takebacks " + OnOff(on), true);
    }

    // The instance default reset-available time, cycled through 30 s, 120 s and never.
    public void CycleDefaultResetTime() {
        if (!BeginArbiterChange("Default reset time")) return;
        int current = board.arbiter.DefaultNumber(ChessArbiter.NumberResetAvailableTime);
        int next = current == 30 ? 120 : current == 120 ? ChessArbiter.Never : 30;
        bool done = board.arbiter.SetDefaultNumber(ChessArbiter.NumberResetAvailableTime, next);
        EndArbiterChange("Arbiter default reset-available time "
            + board.arbiter.DefaultNumber(ChessArbiter.NumberResetAvailableTime), done);
    }

    // The instance default auto-reset time, cycled through 60 s, 600 s and never. The arbiter keeps the
    // reset-available time no longer than it.
    public void CycleDefaultAutoResetTime() {
        if (!BeginArbiterChange("Default auto-reset time")) return;
        int current = board.arbiter.DefaultNumber(ChessArbiter.NumberAutoResetTime);
        int next = current == 60 ? 600 : current == 600 ? ChessArbiter.Never : 60;
        bool done = board.arbiter.SetDefaultNumber(ChessArbiter.NumberAutoResetTime, next);
        EndArbiterChange("Arbiter default auto-reset time "
            + board.arbiter.DefaultNumber(ChessArbiter.NumberAutoResetTime) + " (reset-available "
            + board.arbiter.DefaultNumber(ChessArbiter.NumberResetAvailableTime) + ")", done);
    }

    // ---- Fidget (8.5): picks up a piece every half second for FidgetSeconds, as a player handling pieces without
    // moving, then logs how many pickups were made, how many stamped the board's activity, and how many times this
    // client sent the board meanwhile. The activity throttle allows one send per ChessBoard.ActivityThrottle seconds.
    // Pressed again while running, it stops early. ----

    const float FidgetSeconds = 30;
    const float FidgetInterval = 0.5f;
    bool fidgeting;
    float fidgetEnd;
    int fidgetPickups;
    int fidgetStamps;
    int fidgetSerializations;

    public void Fidget() {
        if (fidgeting) {
            EndFidget();
            return;
        }
        fidgeting = true;
        fidgetEnd = Time.time + FidgetSeconds;
        fidgetPickups = 0;
        fidgetStamps = 0;
        fidgetSerializations = board.serializations;
        ReportDone("Fidget for " + FidgetSeconds + " s started", true);
        FidgetStep();
    }

    public void FidgetStep() {
        if (!fidgeting) return;
        if (Time.time >= fidgetEnd) {
            EndFidget();
            return;
        }
        double before = board.lastActivity;
        int square = FidgetSquare();
        if (square >= 0) {
            fidgetPickups++;
            // A pickup put back where it was: accepted (the side to move's own piece) or rejected, it changes nothing.
            if (board.PiecePickedUp(square) == ChessBoard.InputAccepted) board.PieceDropped(square, square);
        }
        if (board.lastActivity != before) fidgetStamps++;
        SendCustomEventDelayedSeconds(nameof(FidgetStep), FidgetInterval);
    }

    void EndFidget() {
        fidgeting = false;
        ReportDone("Fidget ended: " + fidgetPickups + " pickups, " + fidgetStamps + " activity stamps, "
            + (board.serializations - fidgetSerializations) + " sends (throttle " + ChessBoard.ActivityThrottle + " s)",
            true);
    }

    // A square with a piece of the side to move on the board shown (any piece if it has none), -1 if it's empty.
    int FidgetSquare() {
        int any = -1;
        for (int square = 0; square < 64; square++) {
            int piece = board.PieceAt(square);
            if (piece == ChessRules.Empty) continue;
            if (ChessRules.PieceColorOf(piece) == board.SideToMove()) return square;
            if (any < 0) any = square;
        }
        return any;
    }

    // Asks VRChat for ownership of the arbiter directly, without BeginChange's organizer check, to see that the
    // arbiter refuses a non-organizer. The owner is reported now and again after a few seconds, once the current
    // owner has answered.
    public void TakeArbiter() {
        if (!HasArbiter("Take arbiter")) return;
        Networking.SetOwner(Networking.LocalPlayer, board.arbiter.gameObject);
        ReportDone("Take arbiter (owner now " + ChessUtil.OwnerName(board.arbiter.gameObject, "nobody") + ")",
            Networking.IsOwner(board.arbiter.gameObject));
        SendCustomEventDelayedSeconds(nameof(ReportArbiterOwner), 3);
    }

    public void ReportArbiterOwner() {
        ReportDone("Take arbiter, 3 s later (owner " + ChessUtil.OwnerName(board.arbiter.gameObject, "nobody") + ")",
            Networking.IsOwner(board.arbiter.gameObject));
    }

    // Asks VRChat for ownership of the board directly, as every change does but without the input methods' own
    // checks, to see that the board refuses a bystander while both players are here and the board isn't idle. The
    // owner is reported now and again after a few seconds, once the current owner has answered.
    public void TakeBoard() {
        Networking.SetOwner(Networking.LocalPlayer, board.gameObject);
        ReportDone("Take board (owner now " + ChessUtil.OwnerName(board.gameObject, "nobody") + ")",
            Networking.IsOwner(board.gameObject));
        SendCustomEventDelayedSeconds(nameof(ReportBoardOwner), 3);
    }

    public void ReportBoardOwner() {
        ReportDone("Take board, 3 s later (owner " + ChessUtil.OwnerName(board.gameObject, "nobody") + ")",
            Networking.IsOwner(board.gameObject));
    }

    // ---- Archive (10.1b): the board's GameArchive. The text's Archive line shows its games, owner, holders and the
    // recent games waiting; each change to it (made here or arriving) is logged as "archive: …". ----

    // Asks VRChat for ownership of the archive's first holder directly, to see that a holder refuses anyone but the
    // archive's owner. The owner is reported now and again after a few seconds, once the current owner has answered.
    public void TakeHolder() {
        if (!HasArchive("Take holder")) return;
        GameArchiveHolder holder = board.archive.holders[0];
        Networking.SetOwner(Networking.LocalPlayer, holder.gameObject);
        ReportDone("Take holder (owner now " + ChessUtil.OwnerName(holder.gameObject, "nobody") + ")",
            Networking.IsOwner(holder.gameObject));
        SendCustomEventDelayedSeconds(nameof(ReportHolderOwner), 3);
    }

    public void ReportHolderOwner() {
        GameArchiveHolder holder = board.archive.holders[0];
        ReportDone("Take holder, 3 s later (owner " + ChessUtil.OwnerName(holder.gameObject, "nobody") + ")",
            Networking.IsOwner(holder.gameObject));
    }

    // Holds this client's archive (GameArchive.held): even as the owner it archives nothing, and the games it logs
    // wait in its recent games, as if the owner had left before archiving them. The owner holding it and then leaving
    // has the next owner archive them. Pressed again, it lets go and archives what waited.
    public void HoldArchive() {
        if (!HasArchive("Hold archive")) return;
        GameArchive archive = board.archive;
        archive.held = !archive.held;
        if (archive.held) {
            ReportDone("Hold archive: held, games logged here wait in the recent games", true);
            return;
        }
        int added = archive.Flush();
        ReportDone("Hold archive: let go, " + added + " recent game(s) added"
            + (Networking.IsOwner(archive.gameObject) ? "" : " (not the archive's owner)"), true);
    }

    // Resigns for the local player at the next whole minute of server time (at least 3 s away), so players on two
    // boards pressing it within the same minute end their games at the same moment. (It was the next ten seconds, but
    // two presses a couple of seconds apart too often fell either side of the 3 s cutoff, 10.3.) A game with no moves
    // isn't logged, so it says when there's none yet.
    public void ResignAtMinute() {
        double now = board.ServerTime();
        double at = System.Math.Floor(now / 60) * 60 + 60;
        if (at - now < 3) at += 60;
        ReportDone("Resign at server time " + at.ToString("F0") + " (in " + (at - now).ToString("F1") + " s)"
            + (board.moveCount == 0 ? "; no moves yet, so the game won't be logged or archived" : ""), true);
        SendCustomEventDelayedSeconds(nameof(ResignNow), (float)(at - now));
    }

    public void ResignNow() {
        int color = ChooseColor(board.SideToMove(), board.CanResign(board.SideToMove()));
        Report("Resign " + ColorName(color) + " at server time " + board.ServerTime().ToString("F3"),
            board.Resign(color));
    }

    // Logs every archived game, oldest first, and the recent games.
    public void CheckArchive() {
        if (!HasArchive("Check archive")) return;
        GameArchive archive = board.archive;
        string text = "[ChessDebugView] archive games:";
        int count = archive.GameCount();
        for (int i = 0; i < count; i++) text += "\n  " + ArchivedGameText(i);
        GameArchiveHolder recent = archive.recent;
        text += "\n  recent here:";
        for (int i = 0; Utilities.IsValid(recent) && i < GameArchiveHolder.Capacity; i++) {
            if (recent.sequences[i] <= 0) continue;
            text += " " + recent.gameIds[i] + (archive.IsRecentWaiting(i) ? " (WAITING)" : "");
        }
        Debug.Log(text);
        ReportDone("Check archive: " + ArchiveText(), true);
    }

    // ---- Filling the archive (10.3): Add 10 games hands the archive 10 random legal games of up to FillMaxPlies
    // plies from the standard start, one a frame (a game's moves are a frame's work), as a board logging them would.
    // On the archive's owner each is archived at once; elsewhere they wait in the recent games (the last 5 kept). ----

    const int FillGames = 10;
    const int FillMaxPlies = 80;
    int fillLeft;
    // Games added by this client so far, numbering their players' names.
    int fillNumber;
    byte[] fillBoard = new byte[64];
    int[] fillState = new int[ChessPosition.StateLength];
    int[] fillMoves = new int[FillMaxPlies];
    int[] fillClocks = new int[FillMaxPlies];

    public void AddTenGames() {
        if (!HasArchive("Add 10 games")) return;
        if (fillLeft > 0) {
            ReportDone("Add 10 games (still adding the last 10)", false);
            return;
        }
        fillLeft = FillGames;
        ReportDone("Add 10 games: adding one a frame"
            + (Networking.IsOwner(board.archive.gameObject) ? "" : " (not the archive's owner)"), true);
        SendCustomEventDelayedFrames(nameof(AddFillGame), 1);
    }

    public void AddFillGame() {
        if (fillLeft <= 0) return;
        fillLeft--;
        fillNumber++;
        int ending;
        int winner;
        int count = RandomGame(out ending, out winner);
        string gameId = board.NewGameIdAt(board.NetworkDateTime());
        string white = "Fill White " + fillNumber;
        string black = "Fill Black " + fillNumber;
        bool added = board.archive.AddGame(gameId, "", white, black, ChessGame.ModeStandard, ending, winner,
            ChessFen.StartFen, ChessArbiter.NoClock, 0, fillMoves, fillClocks, count);
        Debug.Log("[ChessDebugView] Add 10 games: " + gameId + " (" + white + " vs " + black + ", "
            + ChessPgn.ResultText(ending, winner) + ", " + count + " plies) " + (added ? "added" : "REFUSED")
            + (fillLeft > 0 ? ", " + fillLeft + " to go" : ", done"));
        if (fillLeft > 0) SendCustomEventDelayedFrames(nameof(AddFillGame), 1);
        else Refresh();
    }

    // Plays random legal moves from the standard start into fillMoves (clocks unknown) until the game ends or
    // reaches FillMaxPlies, and returns the count: a checkmate or stalemate ends it (on the last ply too), otherwise
    // it's unfinished.
    int RandomGame(out int ending, out int winner) {
        int halfmoveClock;
        int fullmoveNumber;
        ChessFen.ParseFen(ChessFen.StartFen, fillBoard, fillState, out halfmoveClock, out fullmoveNumber);
        ending = ChessGame.EndingUnterminated;
        winner = ChessRules.NoColor;
        int count = 0;
        while (true) {
            int legal = ChessMoves.GenerateLegalMoves(fillBoard, fillState, legalMoves, true);
            if (legal == 0) {
                int side = fillState[ChessPosition.StateSideToMove];
                if (ChessMoves.IsInCheck(fillBoard, fillState, side)) {
                    ending = ChessGame.EndingCheckmate;
                    winner = ChessRules.OtherColor(side);
                } else {
                    ending = ChessGame.EndingStalemate;
                }
                break;
            }
            if (count >= FillMaxPlies) break;
            int move = legalMoves[Random.Range(0, legal)];
            ChessPosition.MakeMove(fillBoard, fillState, move);
            fillMoves[count] = move;
            fillClocks[count] = -1;
            count++;
        }
        return count;
    }

    bool HasArchive(string label) {
        if (Utilities.IsValid(board.archive) && board.archive.holders.Length > 0) return true;
        ReportDone(label + " (the board has no archive)", false);
        return false;
    }

    // Logs the archive when its games or its owner change (polled on each redraw), with the newest games.
    void LogArchive() {
        GameArchive archive = board.archive;
        if (!Utilities.IsValid(archive)) return;
        string owner = ChessUtil.OwnerName(archive.gameObject, "nobody");
        if (archive.changes == lastArchiveChanges && owner == lastArchiveOwner) return;
        lastArchiveChanges = archive.changes;
        lastArchiveOwner = owner;
        string text = "[ChessDebugView] archive: " + ArchiveText();
        int count = archive.GameCount();
        int first = count > 3 ? count - 3 : 0;
        if (count > 0) text += "; newest:";
        for (int i = count - 1; i >= first; i--) text += " " + ArchivedGameText(i) + (i > first ? ";" : "");
        Debug.Log(text);
    }

    // The archive in one line: games, owner, holders ready and their owners, the recent games and how many of them
    // aren't archived.
    string ArchiveText() {
        GameArchive archive = board.archive;
        if (!Utilities.IsValid(archive)) return "none";
        int ready = 0;
        string[] owners = new string[archive.holders.Length];
        int[] ownerCounts = new int[archive.holders.Length];
        int ownerKinds = 0;
        for (int i = 0; i < archive.holders.Length; i++) {
            GameArchiveHolder holder = archive.holders[i];
            if (!Utilities.IsValid(holder)) continue;
            if (holder.ready) ready++;
            string name = ChessUtil.OwnerName(holder.gameObject, "nobody");
            int at = 0;
            while (at < ownerKinds && owners[at] != name) at++;
            if (at == ownerKinds) {
                owners[at] = name;
                ownerKinds++;
            }
            ownerCounts[at]++;
        }
        string ownersText = "";
        for (int i = 0; i < ownerKinds; i++) ownersText += (i > 0 ? ", " : "") + owners[i] + " " + ownerCounts[i];
        int recentCount = 0;
        int waiting = 0;
        GameArchiveHolder recent = archive.recent;
        for (int i = 0; Utilities.IsValid(recent) && i < GameArchiveHolder.Capacity; i++) {
            if (recent.sequences[i] <= 0) continue;
            recentCount++;
            if (archive.IsRecentWaiting(i)) waiting++;
        }
        return archive.GameCount() + "/" + archive.Capacity() + " games, owner "
            + ChessUtil.OwnerName(archive.gameObject, "nobody")
            + ", holders ready " + ready + "/" + archive.holders.Length + " (owners: " + ownersText + "), recent "
            + recentCount + (waiting > 0 ? " (" + waiting + " WAITING)" : "") + (archive.held ? ", HELD" : "");
    }

    // The index-th archived game (0 the oldest): its sequence, ID, players, result, move count and ContinuedFrom.
    string ArchivedGameText(int index) {
        GameArchive archive = board.archive;
        return "#" + archive.SequenceAt(index) + " " + archive.GameIdAt(index) + " (" + archive.WhiteAt(index) + " vs "
            + archive.BlackAt(index) + ", " + ChessPgn.ResultText(archive.EndingAt(index), archive.WinnerAt(index))
            + ", " + archive.MoveCountAt(index) + " plies"
            + (archive.ContinuedFromAt(index) == "" ? "" : ", continued from " + archive.ContinuedFromAt(index)) + ")";
    }

    public void Resnapshot() {
        Report("Re-snapshot", board.Resnapshot());
    }

    // ---- Notation fill-in (8.6) ----

    // The notation as it was before Forget notation, compared with what the board fills in.
    string[] forgotten = new string[0];
    int forgottenCount;

    // Forgets this client's notation of the game, locally (nothing is sent): every entry but every fourth, so the
    // board's walk both computes entries and passes known ones. The board fills them in over the next frames, and
    // OnNotationFilled logs whether they match.
    public void ForgetNotation() {
        int count = board.moveCount;
        if (count == 0) {
            ReportDone("Forget notation: no moves", false);
            return;
        }
        forgotten = new string[count];
        int forgot = 0;
        for (int ply = 0; ply < count; ply++) {
            forgotten[ply] = board.notation[ply];
            if (ply % 4 == 3) continue;
            board.notation[ply] = "";
            forgot++;
        }
        forgottenCount = count;
        board.RecheckNotation();
        ReportDone("Forget notation: " + forgot + " of " + count + " entries forgotten", true);
    }

    // ---- PGN import (8.7) ----

    // Imports importPgn over the next frames; the board loads and sends it when done (OnImportChanged logs it).
    public void ImportPgn() {
        StartImport("Import PGN", false);
    }

    // Starts importing importPgn but holds the import (the importer paused), so the board can be changed meanwhile,
    // here or on another client, to see the import abandoned. Resume import lets it finish.
    public void ImportPaused() {
        StartImport("Import PGN (paused)", true);
    }

    public void ResumeImport() {
        bool running = board.importStatus == ChessBoard.ImportRunning && Utilities.IsValid(board.importer);
        if (running) board.importer.paused = false;
        ReportDone("Resume import" + (running ? "" : " (no import under way)"), running);
    }

    void StartImport(string label, bool paused) {
        if (importPgn == null) {
            ReportDone(label + " (the Import Pgn field is empty)", false);
            return;
        }
        int result = board.StartImport(importPgn.text);
        if (result == ChessBoard.InputAccepted && paused) board.importer.paused = true;
        Report(label + " " + importPgn.name, result);
    }

    // The board's import started, was loaded, failed or was abandoned: logged with the board's state.
    public override void OnImportChanged() {
        dirty = true;
        Debug.Log("[ChessDebugView] import " + ImportText() + "; now " + BoardSummary());
    }

    string ImportText() {
        string text = ImportStatusName(board.importStatus);
        if (board.importStatus == ChessBoard.ImportFailed) {
            text += board.importResult == ChessPgn.ImportDone ? " (the game couldn't be loaded)"
                : " (at move " + board.importFailedAt + ": " + ChessPgn.ImportErrorText(board.importResult) + ")";
        } else if (board.importStatus == ChessBoard.ImportAbandoned) {
            text += " (the board changed before the import finished)";
        }
        if (board.importStatus == ChessBoard.ImportRunning && Utilities.IsValid(board.importer)
            && board.importer.paused) {
            text += ", paused";
        }
        return text;
    }

    // ---- Helpers ----

    // The colour to act for: preferred when the check for it holds, else the other one if the local player holds that
    // seat (for a player in one seat), else preferred, which the board then refuses.
    int ChooseColor(int preferred, bool preferredApplies) {
        if (preferredApplies) return preferred;
        int other = ChessRules.OtherColor(preferred);
        string name = ChessUtil.LocalName("");
        return board.IsSeated(other, name) && !board.IsSeated(preferred, name) ? other : preferred;
    }

    // Plays the next move of a scripted game (the ply after the board's last), as piece input. A repeating script
    // starts over at its end.
    void PlayScripted(string label, string[] script, bool repeat) {
        int ply = board.moveCount;
        if (repeat) ply %= script.Length;
        if (ply >= script.Length) {
            lastAction = label + ": the script has no move " + (ply + 1);
            Refresh();
            return;
        }
        int count = ChessMoves.GenerateLegalMoves(board.board, board.state, legalMoves, true);
        int move = ChessNotation.MoveFromSan(script[ply], legalMoves, count);
        if (move == ChessNotation.SanNoMove || move == ChessNotation.SanAmbiguous) {
            lastAction = label + ": " + script[ply] + " isn't legal here";
            Refresh();
            return;
        }
        PlayAsInput(label + " " + script[ply], move);
    }

    // Plays a move as the view will: pick up, drop (a castle drops the king on its rook, the move's to square), and
    // the queen when the board asks for the promotion piece.
    void PlayAsInput(string label, int move) {
        int from = (move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask;
        int to = (move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask;
        string text = label + " (" + ChessRules.SquareName(from) + "-" + ChessRules.SquareName(to) + ")";
        if (board.PiecePickedUp(from) != ChessBoard.InputAccepted) {
            Report(text + " pickup", ChessBoard.InputRejected);
            return;
        }
        int result = board.PieceDropped(from, to);
        if (result == ChessBoard.InputPromotionNeeded) {
            text += " =" + ChessRules.PieceTypeLetter(ChessRules.Queen);
            result = board.PromotionChosen(from, to, ChessRules.Queen);
        }
        Report(text, result);
    }

    bool HasArbiter(string label) {
        if (Utilities.IsValid(board.arbiter)) return true;
        lastAction = label + ": the board has no arbiter";
        Refresh();
        return false;
    }

    // Takes ownership of the arbiter for a change, reporting a refusal (no arbiter, or not an organizer).
    bool BeginArbiterChange(string label) {
        if (!HasArbiter(label)) return false;
        if (board.arbiter.BeginChange()) return true;
        ReportDone(label + " (not an organizer)", false);
        return false;
    }

    // Syncs the change and reports it.
    void EndArbiterChange(string label, bool done) {
        bool sent = board.arbiter.EndChange();
        ReportDone(label + (sent ? "" : " (not sent: not the owner)"), done && sent);
    }

    void Report(string label, int result) {
        lastAction = label + ": " + (result == ChessBoard.InputAccepted ? "accepted"
            : result == ChessBoard.InputPromotionNeeded ? "promotion needed" : "REFUSED");
        Debug.Log("[ChessDebugView] " + lastAction
            + (result == ChessBoard.InputAccepted ? "; now " + BoardSummary() : ""));
        Refresh();
    }

    void ReportDone(string label, bool done) {
        Report(label, done ? ChessBoard.InputAccepted : ChessBoard.InputRejected);
    }

    // ---- Piece sets (13.6), test-only: no UI of ours changes them for players ----

    // Cycles the local player's preferred set (the arbiter's preferredPieceSet) through none and each listed set; the
    // next join puts it on the board.
    public void CyclePreferredSet() {
        ChessArbiter arbiter = board.arbiter;
        if (!Utilities.IsValid(arbiter)) return;
        int next = arbiter.PreferredPieceSet() + 1;
        if (next >= arbiter.PieceSetCount()) next = -1;
        ReportDone("Preferred set " + next, arbiter.SetPreferredPieceSet(next));
    }

    // Cycles this client's override for both colours through none and each listed set.
    public void CycleSetOverride() {
        ChessArbiter arbiter = board.arbiter;
        if (!Utilities.IsValid(arbiter)) return;
        int next = arbiter.PieceSetOverride(ChessRules.White) + 1;
        if (next >= arbiter.PieceSetCount()) next = -1;
        bool done = arbiter.SetPieceSetOverride(ChessRules.White, next)
            && arbiter.SetPieceSetOverride(ChessRules.Black, next);
        ReportDone("Override sets " + next, done);
    }

    string PieceSetText() {
        ChessArbiter arbiter = board.arbiter;
        string text = "Piece sets: White " + board.whitePieceSet + ", Black " + board.blackPieceSet;
        if (!Utilities.IsValid(arbiter)) return text + " (no arbiter)";
        return text + " (of " + arbiter.PieceSetCount() + ")   your preference: " + arbiter.PreferredPieceSet()
            + "   your override: White " + arbiter.PieceSetOverride(ChessRules.White) + ", Black "
            + arbiter.PieceSetOverride(ChessRules.Black);
    }

    // ---- Pieces (9.2) ----

    // The pieces' state as last logged, so each change of the local player's pickups is logged once.
    string lastPiecesText = "";

    // Cycles the board view's input style on this client only (Auto, Pickups, Clicks), so a desktop client can try
    // pickups without a rebuild (docs/testing.md, One VR client).
    public void CycleInputStyle() {
        if (!Utilities.IsValid(boardView)) {
            ReportDone("Input style (the Board View field is empty)", false);
            return;
        }
        boardView.CycleInputStyle();
        ReportDone("Input style: " + boardView.InputStyleName(), true);
    }

    // Asks VRChat for ownership of a piece on the board directly, as a grab does but past pickupable, to see the
    // piece's OnOwnershipRequest agree with the board (it logs a refusal). The owner is reported now and again after
    // a few seconds, once the current owner has answered.
    public void TakePiece() {
        GameObject piece = PieceToTake();
        if (piece == null) {
            ReportDone("Take piece (no board view, or no piece on the board)", false);
            return;
        }
        Networking.SetOwner(Networking.LocalPlayer, piece);
        ReportDone("Take piece " + piece.name + " (owner now " + ChessUtil.OwnerName(piece, "nobody")
            + "; the board says you may touch pieces: " + YesNo(board.MayTouchPieces()) + ")",
            Networking.IsOwner(piece));
        SendCustomEventDelayedSeconds(nameof(ReportPieceOwner), 3);
    }

    public void ReportPieceOwner() {
        GameObject piece = PieceToTake();
        if (piece == null) return;
        ReportDone("Take piece " + piece.name + ", 3 s later (owner " + ChessUtil.OwnerName(piece, "nobody") + ")",
            Networking.IsOwner(piece));
    }

    // The first piece object standing on a square.
    GameObject PieceToTake() {
        if (!Utilities.IsValid(boardView)) return null;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            if (boardView.objectSquare[obj] != ChessRules.NoSquare && Utilities.IsValid(boardView.pieces[obj])) {
                return boardView.pieces[obj].gameObject;
            }
        }
        return null;
    }

    // The local player's pieces in one line: the input style, whether they use pickups, whether the board lets them
    // touch the pieces, and how many pieces are pickups for them.
    string PiecesText() {
        if (!Utilities.IsValid(boardView)) return "no board view";
        int pickupable = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            VRCPickup pickup = boardView.piecePickups[obj];
            if (Utilities.IsValid(pickup) && pickup.pickupable) pickupable++;
        }
        VRCPlayerApi local = Networking.LocalPlayer;
        return "input style " + boardView.InputStyleName() + ", in VR " + YesNo(Utilities.IsValid(local)
            && local.IsUserInVR()) + ", pickups " + YesNo(boardView.UsesPickups()) + ", may touch "
            + YesNo(board.MayTouchPieces()) + ", " + pickupable + " pickupable";
    }

    // Logs the pieces' line when it changes, so each client's log shows who could grab when.
    void LogPieces() {
        string text = PiecesText();
        if (text == lastPiecesText) return;
        lastPiecesText = text;
        Debug.Log("[ChessDebugView] pieces: " + text);
    }

    static string YesNo(bool value) {
        return value ? "yes" : "no";
    }

    // ---- Piece watch (9.6) ----

    // Each piece object's distance from where this client's view puts it (ChessBoardView.ObjectPlace), so a piece
    // held here, and one that doesn't end up where the board puts it (moved by anything but the view: the pieces have
    // no VRCObjectSync since step 9.6), show in the log. Objects still travelling are left alone until they land.
    const float AwaySquares = 0.05f;
    // How long after a board change the pieces are checked as settled: animations have finished.
    const float SettleSeconds = 2;
    bool[] pieceAway = new bool[ChessBoardView.PieceCount];
    float[] awaySince = new float[ChessBoardView.PieceCount];
    float[] awayFurthest = new float[ChessBoardView.PieceCount];
    float settleCheckAt = -1;
    string settleChange = "";

    // Logs each object leaving its place and coming back (with how long and how far), with its owner.
    void WatchPieces() {
        if (!Utilities.IsValid(boardView)) return;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            Transform piece = boardView.pieces[obj];
            if (!Utilities.IsValid(piece) || boardView.animating[obj]) continue;
            float squares = AwayBy(obj).magnitude;
            if (squares > AwaySquares) {
                if (!pieceAway[obj]) {
                    pieceAway[obj] = true;
                    awaySince[obj] = Time.time;
                    awayFurthest[obj] = 0;
                    Debug.Log("[ChessDebugView] piece " + piece.name + " left its place (" + PlaceName(obj) + "): "
                        + PieceState(obj));
                }
                if (squares > awayFurthest[obj]) awayFurthest[obj] = squares;
            } else if (pieceAway[obj]) {
                pieceAway[obj] = false;
                Debug.Log("[ChessDebugView] piece " + piece.name + " in place (" + PlaceName(obj) + ") after "
                    + (Time.time - awaySince[obj]).ToString("F1") + " s away, furthest "
                    + awayFurthest[obj].ToString("F2") + " squares; owner "
                    + ChessUtil.OwnerName(piece.gameObject, "nobody"));
            }
        }
    }

    // How far obj stands from its place, in squares (view space).
    Vector3 AwayBy(int obj) {
        Vector3 offset = boardView.pieces[obj].localPosition - boardView.ObjectPlace(obj);
        return offset / boardView.squareSize;
    }

    string PlaceName(int obj) {
        int square = boardView.objectSquare[obj];
        if (obj == boardView.promotionObject) return "promoting on " + ChessRules.SquareName(boardView.promotionTo);
        if (square != ChessRules.NoSquare) return ChessRules.SquareName(square);
        if (!boardView.IsObjectShown(obj)) return "hidden";
        return "captured place " + (boardView.objectPlace[obj] + 1);
    }

    // The owner, and whether this client holds, has selected or is animating obj.
    string PieceState(int obj) {
        Vector3 away = AwayBy(obj);
        string text = "off by (" + away.x.ToString("F2") + ", " + away.y.ToString("F2") + ", " + away.z.ToString("F2")
            + ") squares, owner " + ChessUtil.OwnerName(boardView.pieces[obj].gameObject, "nobody");
        if (obj == boardView.heldObject) text += ", held here";
        if (obj == boardView.selectedObject) text += ", selected here";
        if (boardView.animating[obj]) text += ", travelling";
        return text;
    }

    // Logs how many objects stand away from their place (each with its state) and who owns the pieces; with full,
    // also every object's place and owner, so clients' logs can be compared object by object.
    void LogPlacement(string label, bool full) {
        if (!Utilities.IsValid(boardView)) return;
        int away = 0;
        string awayText = "";
        string placesText = "";
        string[] owners = new string[ChessBoardView.PieceCount];
        int[] ownerCounts = new int[ChessBoardView.PieceCount];
        int ownerCount = 0;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            Transform piece = boardView.pieces[obj];
            if (!Utilities.IsValid(piece)) continue;
            string owner = ChessUtil.OwnerName(piece.gameObject, "nobody");
            int found = -1;
            for (int i = 0; i < ownerCount; i++) {
                if (owners[i] == owner) found = i;
            }
            if (found < 0) {
                found = ownerCount++;
                owners[found] = owner;
            }
            ownerCounts[found]++;
            if (AwayBy(obj).magnitude > AwaySquares) {
                away++;
                awayText += "; " + piece.name + " (" + PlaceName(obj) + ") " + PieceState(obj);
            }
            if (full) placesText += "; " + piece.name + " " + PlaceName(obj) + " " + owner;
        }
        string ownersText = "";
        for (int i = 0; i < ownerCount; i++) {
            ownersText += (i > 0 ? ", " : "") + owners[i] + " " + ownerCounts[i];
        }
        Debug.Log("[ChessDebugView] pieces " + label + ": " + (away == 0 ? "all in place" : away + " AWAY") + awayText
            + " (owners: " + ownersText + "; " + board.moveCount + " moves"
            + (board.replayMode ? ", replay at ply " + board.replayIndex : "") + ", "
            + ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber) + "; captured: White "
            + CapturedText(ChessRules.White) + ", Black " + CapturedText(ChessRules.Black) + ")");
        if (full) Debug.Log("[ChessDebugView] places" + placesText);
    }

    // What stands in color's captured places (step 12.4), as piece letters in place order: "-" for a place no object
    // stands in, lower case for one hidden (beyond the last target). The same on every client, late joiners too.
    string CapturedText(int color) {
        string text = "";
        int first = color == ChessRules.White ? 0 : ChessBoardView.PiecesPerColor;
        for (int place = 0; place < boardView.capturedCounts[color]; place++) {
            string letter = "-";
            for (int obj = first; obj < first + ChessBoardView.PiecesPerColor; obj++) {
                if (boardView.objectSquare[obj] != ChessRules.NoSquare || boardView.objectPlace[obj] != place) continue;
                letter = ChessRules.PieceTypeLetter(boardView.objectType[obj]);
                if (!boardView.IsObjectShown(obj)) letter = letter.ToLower();
            }
            text += letter;
        }
        return text.Length > 0 ? text : "none";
    }

    // Logs every object's place and owner, and any standing away from its place.
    public void CheckPieces() {
        if (!Utilities.IsValid(boardView)) {
            ReportDone("Check pieces (the Board View field is empty)", false);
            return;
        }
        LogPlacement("checked", true);
        ReportDone("Check pieces", true);
    }

    // ---- Display ----

    void Refresh() {
        dirty = false;
        nextRefresh = Time.time + 0.25f;
        if (!Utilities.IsValid(board)) return;
        RefreshSquares();
        if (Utilities.IsValid(statusText)) statusText.text = StatusText();
        LogArbiterSettings();
        LogBoardOwner();
        LogArchive();
        LogPieces();
        WatchPieces();
    }

    // Logs the arbiter's settings when they differ from the last logged ones (and once at the start), so each client's
    // log shows when a change reached it.
    void LogArbiterSettings() {
        if (!Utilities.IsValid(board.arbiter)) return;
        int[] numbers = new int[ChessArbiter.NumberCount];
        board.arbiter.EffectiveNumbers(board.boardNumber, numbers);
        string text = "board " + board.boardNumber + ": "
            + ChessArbiter.SettingsToString(board.arbiter.EffectiveSettings(board.boardNumber)) + " | "
            + ChessArbiter.NumbersToString(numbers) + "; defaults: "
            + ChessArbiter.SettingsToString(board.arbiter.defaultSettings) + " | "
            + ChessArbiter.NumbersToString(board.arbiter.defaultNumbers)
            + "; owner " + ChessUtil.OwnerName(board.arbiter.gameObject, "nobody");
        if (text == lastArbiterSettings) return;
        lastArbiterSettings = text;
        Debug.Log("[ChessDebugView] arbiter settings: " + text);
    }

    void RefreshSquares() {
        int last = board.LastMove();
        int lastFrom = last == 0 ? -1 : (last >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask;
        int lastTo = last == 0 ? -1 : (last >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask;
        int checkedKing = -1;
        if (!board.replayMode && board.IsInCheck()) {
            checkedKing = board.state[board.SideToMove() == ChessRules.White
                ? ChessPosition.StateWhiteKingSquare : ChessPosition.StateBlackKingSquare];
        }
        for (int square = 0; square < 64; square++) {
            int piece = board.PieceAt(square);
            if (pieceImages != null && square < pieceImages.Length && Utilities.IsValid(pieceImages[square])) {
                Sprite sprite = null;
                if (piece != ChessRules.Empty) {
                    Sprite[] sprites = ChessRules.PieceColorOf(piece) == ChessRules.White ? whiteSprites : blackSprites;
                    int index = ChessRules.PieceTypeOf(piece) - ChessRules.Pawn;
                    if (sprites != null && index >= 0 && index < sprites.Length) sprite = sprites[index];
                }
                pieceImages[square].sprite = sprite;
                pieceImages[square].enabled = sprite != null;
            }
            if (squareImages != null && square < squareImages.Length && Utilities.IsValid(squareImages[square])) {
                bool light = (ChessRules.FileOf(square) + ChessRules.RankOf(square)) % 2 == 1;
                Color color = light ? lightSquare : darkSquare;
                if (square == lastFrom || square == lastTo) color = Color.Lerp(color, lastMoveTint, 0.6f);
                if (square == checkedKing) color = checkTint;
                squareImages[square].color = color;
            }
        }
    }

    string StatusText() {
        string name = ChessUtil.LocalName("");
        string text = "<b>" + board.gameObject.name + "</b> (board " + board.boardNumber + ")   you: " + name
            + (IsLocalOrganizer() ? " [organizer]" : "") + "\n";
        text += "Lifecycle: <b>" + LifecycleName(board.lifecycle) + "</b>   last change: " + ChangeName(board.lastChange)
            + (board.lastChangeReceived ? " (received)" : "") + "   logged games: " + board.loggedGames
            + "   board owner: " + ChessUtil.OwnerName(board.gameObject, "nobody") + "\n";
        double now = board.ServerTime();
        string idle = "-";
        if (board.lifecycle != ChessBoard.LifecycleReset) {
            double idleSeconds = now - board.lastActivity;
            idle = Mathf.FloorToInt((float)idleSeconds) + " s (reset available "
                + IdleDeadline(board.GameNumber(ChessArbiter.NumberResetAvailableTime), idleSeconds) + ", auto-reset "
                + IdleDeadline(board.GameNumber(ChessArbiter.NumberAutoResetTime), idleSeconds) + ")";
        }
        text += "Ownership: both players here: " + (board.BothPlayersPresent() ? "yes" : "no") + "   idle: " + idle
            + "   you may take it: " + (board.MayOwn(Networking.LocalPlayer) ? "yes" : "no") + "   sends: "
            + board.serializations + "\n";
        text += "Pieces: " + PiecesText() + "\n";
        text += "White: " + SeatText(board.whitePlayer) + ClockSuffix(ChessRules.White) + "   Black: "
            + SeatText(board.blackPlayer) + ClockSuffix(ChessRules.Black)
            + (board.loadedBy.Length > 0
                ? "   loaded by: " + board.loadedBy + (board.IsLockedToLoader() ? " (locked to them)" : "") : "")
            + "\n";
        text += PieceSetText() + "\n";
        text += "Mode: " + ModeName(board.mode) + "   clock: " + ClockName(board.baseTime, board.increment)
            + "   to move: " + ColorName(board.SideToMove()) + (board.IsInCheck() ? " (check)" : "") + "\n";
        text += "Result: " + board.ResultText();
        if (board.ending != ChessGame.EndingNone) text += " (" + EndingName(board.ending) + ")";
        text += "   draw offer: " + ColorName(board.drawOfferColor) + "   takeback: "
            + (board.takebackColor == ChessRules.NoColor ? "none"
                : ColorName(board.takebackColor) + " at ply " + board.takebackPly) + "\n";
        text += "Replay: " + (board.replayMode
            ? "ply " + board.replayIndex + "/" + board.moveCount + (board.replayCustom ? " (custom)" : "") : "off");
        if (board.replaySourceWhite.Length > 0 || board.replaySourceBlack.Length > 0) {
            text += "   source: " + board.replaySourceWhite + " vs " + board.replaySourceBlack + " "
                + board.replaySourceResult;
        }
        text += "\n";
        text += "Game ID: " + (board.gameId.Length > 0 ? board.gameId : "none")
            + (board.continuedFrom.Length > 0 ? "   continued from " + board.continuedFrom : "") + "\n";
        text += "Snapshot:" + (board.lifecycle == ChessBoard.LifecycleReset ? " none"
            : ChessArbiter.SettingsToString(board.gameSettings) + " | " + ChessArbiter.NumbersToString(board.gameNumbers))
            + "\n";
        if (Utilities.IsValid(board.arbiter)) {
            int[] numbers = new int[ChessArbiter.NumberCount];
            board.arbiter.EffectiveNumbers(board.boardNumber, numbers);
            text += "Arbiter now:" + ChessArbiter.SettingsToString(board.arbiter.EffectiveSettings(board.boardNumber))
                + " | " + ChessArbiter.NumbersToString(numbers) + "\n";
            text += "Arbiter defaults: " + ChessArbiter.SettingsToString(board.arbiter.defaultSettings) + " | "
                + ChessArbiter.NumbersToString(board.arbiter.defaultNumbers) + "\n";
            text += "Arbiter owner: " + ChessUtil.OwnerName(board.arbiter.gameObject, "nobody")
                + "   first joiner: " + FirstJoinerText() + "\n";
        } else {
            text += "Arbiter: none\n";
        }
        text += "Claimable: " + EndingName(board.ClaimableDraw()) + "\n";
        text += "Import: " + ImportText() + "\n";
        text += "Archive: " + ArchiveText() + "\n";
        text += "Moves (" + board.moveCount + "): " + MovesText() + "\n";
        text += "Applies to you: " + ApplicableButtons(name) + "\n";
        text += "Last action: " + lastAction;
        return text;
    }

    // The game's moves as numbered SAN, the last 24 plies at most.
    string MovesText() {
        int count = board.moveCount;
        if (count == 0) return "none";
        // The side that played the first move: the side to move now, flipped for an odd number of moves.
        int firstSide = count % 2 == 0 ? board.SideToMove() : ChessRules.OtherColor(board.SideToMove());
        int start = count > 24 ? count - 24 : 0;
        string text = start > 0 ? "... " : "";
        for (int ply = start; ply < count; ply++) {
            bool white = (firstSide + ply) % 2 == 0;
            if (white || ply == start) text += ChessPgn.MoveLabel(board.startMoveNumber, firstSide, ply) + " ";
            string san = board.notation[ply];
            text += (san.Length > 0 ? san : "?") + " ";
        }
        return text;
    }

    // The buttons whose Can… check holds for the local player.
    string ApplicableButtons(string name) {
        string text = "";
        if (board.CanJoin(ChessRules.White)) text += " join-white";
        if (board.CanJoin(ChessRules.Black)) text += " join-black";
        if (board.CanLeaveSeat(ChessRules.White)) text += " leave-white";
        if (board.CanLeaveSeat(ChessRules.Black)) text += " leave-black";
        if (board.CanSetMode()) text += " mode";
        if (board.CanSetClock()) text += " clock";
        if (board.CanStartGame()) text += " start";
        for (int color = ChessRules.White; color <= ChessRules.Black; color++) {
            string side = color == ChessRules.White ? "W" : "B";
            if (board.CanResign(color)) text += " resign-" + side;
            if (board.CanOfferDraw(color)) text += " offer-" + side;
            if (board.CanAcceptDraw(color)) text += " accept-draw-" + side;
            if (board.CanRequestTakeback(color)) text += " takeback-" + side;
            if (board.CanAcceptTakeback(color)) text += " accept-takeback-" + side;
        }
        if (board.CanClaimDraw()) text += " claim";
        if (board.CanEnterReplay()) text += " enter-replay";
        if (board.CanLeaveReplay()) text += " leave-replay";
        if (board.CanStepTo(board.replayIndex - 1)) text += " step-back";
        if (board.CanStepTo(board.replayIndex + 1)) text += " step-forward";
        if (board.CanContinue()) text += " continue";
        if (board.CanReset()) text += " reset";
        if (board.CanImport()) text += " import";
        return text.Length > 0 ? text : " none";
    }

    string ClockSuffix(int color) {
        if (!board.HasClock()) return "";
        return " [" + ChessPgn.ClockText(board.RemainingTime(color))
            + (board.IsClockRunning() && board.SideToMove() == color ? " running" : "") + "]";
    }

    bool IsLocalOrganizer() {
        return Utilities.IsValid(board.arbiter) && board.arbiter.IsOrganizer(Networking.LocalPlayer);
    }

    // The arbiter's FirstJoinerAuth name, if that's its authorization.
    string FirstJoinerText() {
        FirstJoinerAuth auth = board.arbiter.GetComponent<FirstJoinerAuth>();
        if (!Utilities.IsValid(auth) || board.arbiter.simpleAuth != auth) return "(not FirstJoinerAuth)";
        return auth.firstJoiner.Length > 0 ? auth.firstJoiner : "(not known yet)";
    }

    // When an inactivity time of wait seconds (or Never) is reached, idleSeconds into the board's idleness.
    static string IdleDeadline(int wait, double idleSeconds) {
        if (wait == ChessArbiter.Never) return "never";
        if (idleSeconds >= wait) return "now";
        return "in " + Mathf.CeilToInt((float)(wait - idleSeconds)) + " s";
    }

    static string SeatText(string seat) {
        return seat.Length > 0 ? seat : "(empty)";
    }

    static string OnOff(bool on) {
        return on ? "on" : "off";
    }

    static string ColorName(int color) {
        if (color == ChessRules.White) return "White";
        if (color == ChessRules.Black) return "Black";
        return "none";
    }

    static string ModeName(int mode) {
        return mode == ChessGame.ModeChess960 ? "Chess960" : "Standard";
    }

    static string ClockName(int baseSeconds, int incrementSeconds) {
        if (baseSeconds == ChessArbiter.NoClock) return "none";
        return ChessPgn.TimeControlText(baseSeconds, incrementSeconds);
    }

    static string EndingName(int ending) {
        if (ending == ChessGame.EndingNone) return "none";
        if (ending == ChessGame.EndingUnknown) return "unknown";
        return ChessPgn.TerminationText(ending);
    }

    static string ImportStatusName(int status) {
        switch (status) {
            case ChessBoard.ImportNone: return "none";
            case ChessBoard.ImportRunning: return "under way";
            case ChessBoard.ImportLoaded: return "loaded";
            case ChessBoard.ImportFailed: return "failed";
            case ChessBoard.ImportAbandoned: return "abandoned";
        }
        return "?" + status;
    }

    static string LifecycleName(int lifecycle) {
        switch (lifecycle) {
            case ChessBoard.LifecycleReset: return "Reset";
            case ChessBoard.LifecycleLoaded: return "Loaded";
            case ChessBoard.LifecycleLobby: return "Lobby";
            case ChessBoard.LifecycleInGame: return "In game";
            case ChessBoard.LifecycleEnded: return "Ended";
        }
        return "?" + lifecycle;
    }

    static string ChangeName(int change) {
        switch (change) {
            case ChessBoard.ChangeNone: return "none";
            case ChessBoard.ChangeRejected: return "rejected";
            case ChessBoard.ChangeReset: return "reset";
            case ChessBoard.ChangeJoined: return "joined";
            case ChessBoard.ChangeSettings: return "settings";
            case ChessBoard.ChangeStarted: return "started";
            case ChessBoard.ChangeMove: return "move";
            case ChessBoard.ChangeEnded: return "ended";
            case ChessBoard.ChangeRequest: return "request";
            case ChessBoard.ChangeEnteredReplay: return "entered replay";
            case ChessBoard.ChangeLeftReplay: return "left replay";
            case ChessBoard.ChangeReplayStep: return "replay step";
            case ChessBoard.ChangeFreeMove: return "free move";
            case ChessBoard.ChangeTakeback: return "takeback";
            case ChessBoard.ChangeLoaded: return "loaded";
            case ChessBoard.ChangeContinued: return "continued";
            case ChessBoard.ChangeActivity: return "activity";
            case ChessBoard.ChangeLeft: return "left";
            case ChessBoard.ChangePieceSet: return "piece set";
        }
        return "?" + change;
    }
}
