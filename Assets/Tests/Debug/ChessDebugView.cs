using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

// Debug view (PLAN.md 8.1): a minimal ChessBoardListener that shows a board's state as text and has a button for each
// of the board's input methods, for the local player, so every client can act before ChessBoardView and the settings
// menu exist. It stays as a testing tool. Built in the scene by Tools > Chess Board > Create Debug View
// (Editor/ChessDebugViewBuilder.cs), which wires each button to the public method of the same name.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class ChessDebugView : ChessBoardListener {
    // The board shown and driven; the view adds itself as a listener on Start.
    public ChessBoard board;
    // The board's squares, a1 = 0 to h8 = 63: a background and the piece letter (FEN letters, "" for empty).
    public Image[] squareImages = new Image[64];
    public Text[] squareTexts = new Text[64];
    // Everything else about the board, and the result of the last button pressed.
    public Text statusText;
    // The PGN the import buttons import (8.7): Assets/Tests/PGN/Siggi-vs-Nuka.pgn.txt, set by the builder.
    public TextAsset importPgn;

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
        if (!dirty && Time.time < nextRefresh) return;
        Refresh();
    }

    // A change arriving from another client (8.3) is logged with the state it brought, so each client's log shows the
    // updates reaching it.
    public override void OnBoardChanged() {
        dirty = true;
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

    // The board's state in one line, for the log: lifecycle, seats, moves, position, owner.
    string BoardSummary() {
        string last = board.moveCount > 0 ? " (last " + board.notation[board.moveCount - 1] + ")" : "";
        return LifecycleName(board.lifecycle) + ", White " + SeatText(board.whitePlayer) + ", Black "
            + SeatText(board.blackPlayer) + ", " + board.moveCount + " moves" + last + ", "
            + ChessFen.WriteFen(board.board, board.state, board.halfmoveClock, board.moveNumber) + ", game "
            + (board.gameId.Length > 0 ? board.gameId : "none") + ", owner " + BoardOwnerName();
    }

    string BoardOwnerName() {
        VRCPlayerApi owner = Networking.GetOwner(board.gameObject);
        return Utilities.IsValid(owner) ? owner.displayName : "nobody";
    }

    // ---- Buttons ----

    public void JoinWhite() {
        Report("Join White", board.JoinWhite());
    }

    public void JoinBlack() {
        Report("Join Black", board.JoinBlack());
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
        ReportDone("Take arbiter (owner now " + ArbiterOwnerName() + ")", Networking.IsOwner(board.arbiter.gameObject));
        SendCustomEventDelayedSeconds(nameof(ReportArbiterOwner), 3);
    }

    public void ReportArbiterOwner() {
        ReportDone("Take arbiter, 3 s later (owner " + ArbiterOwnerName() + ")",
            Networking.IsOwner(board.arbiter.gameObject));
    }

    // Asks VRChat for ownership of the board directly, as every change does but without the input methods' own
    // checks, to see that the board refuses a bystander while both players are here and the board isn't idle. The
    // owner is reported now and again after a few seconds, once the current owner has answered.
    public void TakeBoard() {
        Networking.SetOwner(Networking.LocalPlayer, board.gameObject);
        ReportDone("Take board (owner now " + BoardOwnerName() + ")", Networking.IsOwner(board.gameObject));
        SendCustomEventDelayedSeconds(nameof(ReportBoardOwner), 3);
    }

    public void ReportBoardOwner() {
        ReportDone("Take board, 3 s later (owner " + BoardOwnerName() + ")", Networking.IsOwner(board.gameObject));
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
        if (board.importMessage.Length > 0) text += " (" + board.importMessage + ")";
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
        string name = LocalName();
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

    string ArbiterOwnerName() {
        VRCPlayerApi owner = Networking.GetOwner(board.arbiter.gameObject);
        return Utilities.IsValid(owner) ? owner.displayName : "nobody";
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

    // ---- Display ----

    void Refresh() {
        dirty = false;
        nextRefresh = Time.time + 0.25f;
        if (!Utilities.IsValid(board)) return;
        RefreshSquares();
        if (Utilities.IsValid(statusText)) statusText.text = StatusText();
        LogArbiterSettings();
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
            + ChessArbiter.NumbersToString(board.arbiter.defaultNumbers) + "; owner " + ArbiterOwnerName();
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
            if (squareTexts != null && square < squareTexts.Length && Utilities.IsValid(squareTexts[square])) {
                squareTexts[square].text = piece == ChessRules.Empty ? "" : ChessFen.FenChar(piece).ToString();
                squareTexts[square].color = piece != ChessRules.Empty && ChessRules.PieceColorOf(piece) == ChessRules.Black
                    ? Color.black : Color.white;
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
        string name = LocalName();
        string text = "<b>" + board.gameObject.name + "</b> (board " + board.boardNumber + ")   you: " + name
            + (IsLocalOrganizer() ? " [organizer]" : "") + "\n";
        text += "Lifecycle: <b>" + LifecycleName(board.lifecycle) + "</b>   last change: " + ChangeName(board.lastChange)
            + (board.lastChangeReceived ? " (received)" : "") + "   logged games: " + board.loggedGames
            + "   board owner: " + BoardOwnerName() + "\n";
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
        text += "White: " + SeatText(board.whitePlayer) + ClockSuffix(ChessRules.White) + "   Black: "
            + SeatText(board.blackPlayer) + ClockSuffix(ChessRules.Black) + "\n";
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
            text += "Arbiter owner: " + ArbiterOwnerName() + "   first joiner: " + FirstJoinerText() + "\n";
        } else {
            text += "Arbiter: none\n";
        }
        text += "Claimable: " + EndingName(board.ClaimableDraw()) + "\n";
        text += "Import: " + ImportText() + "\n";
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

    static string LocalName() {
        VRCPlayerApi player = Networking.LocalPlayer;
        return Utilities.IsValid(player) ? player.displayName : "";
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
        }
        return "?" + change;
    }
}
