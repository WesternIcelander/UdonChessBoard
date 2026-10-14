using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The board's UI (docs/architecture.md, docs/player-input.md; PLAN.md 12.1): the button panels, the status
    // display, the PGN box (copying a game out, pasting one in) and Load it here for the history wall's pick. A
    // listener of its own, holding no game state and no synced fields: it reads the board after each change and acts
    // through the board's methods, never through the physical board (ChessBoardView), which it doesn't know. Built in
    // the scene with the view by Tools > Chess Board > Create Board View (Editor/ChessBoardViewBuilder.cs).
    //
    // Its local space is the view's: the builder stands it at the board's centre, files along +x, White's end at -z,
    // so the PGN box (and LocalEnd) need nothing from the table. Its parts are placed around that origin by the builder.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ChessBoardUI : ChessBoardListener {
        // The board shown and acted on; the UI adds itself as a listener on Start.
        public ChessBoard board;
        // The board's buttons (docs/player-input.md, Buttons; steps 9.4a and 9.4b), by what they do. Each is shown only
        // while the board's Can... check for it holds for the local player.
        public const int ButtonJoinWhite = 0;
        public const int ButtonJoinBlack = 1;
        public const int ButtonMode = 2;
        public const int ButtonClock = 3;
        public const int ButtonStart = 4;
        public const int ButtonContinue = 5;
        public const int ButtonReset = 6;
        // Step 9.4b. The actions taken for one colour come in pairs, as Join does: a panel at each end of the table
        // acts for its own colour (ButtonColor).
        public const int ButtonResignWhite = 7;
        public const int ButtonResignBlack = 8;
        public const int ButtonOfferDrawWhite = 9;
        public const int ButtonOfferDrawBlack = 10;
        public const int ButtonAcceptDrawWhite = 11;
        public const int ButtonAcceptDrawBlack = 12;
        public const int ButtonDeclineDrawWhite = 13;
        public const int ButtonDeclineDrawBlack = 14;
        public const int ButtonClaimDraw = 15;
        public const int ButtonEnterReplay = 16;
        public const int ButtonLeaveReplay = 17;
        public const int ButtonStepFirst = 18;
        public const int ButtonStepBack = 19;
        public const int ButtonStepForward = 20;
        public const int ButtonStepLast = 21;
        public const int ButtonRequestTakebackWhite = 22;
        public const int ButtonRequestTakebackBlack = 23;
        public const int ButtonAcceptTakebackWhite = 24;
        public const int ButtonAcceptTakebackBlack = 25;
        public const int ButtonDeclineTakebackWhite = 26;
        public const int ButtonDeclineTakebackBlack = 27;
        // Step 10.2b: loads the game picked on the history wall onto this (reset) board.
        public const int ButtonLoadHere = 28;
        // Step 11.2a: opens or closes the PGN box for the local player.
        public const int ButtonPgn = 29;
        // Step 11.2b: opens the PGN box to paste a PGN into and import it, on a reset board.
        public const int ButtonLoadPgn = 30;
        public const int ButtonCount = 31;
        // The button objects in the world (any number: the builder puts a panel at each end of the table), with the
        // Button* action of each and its label at the same index. A button's UI Button calls the UI's Press...
        // event for its action.
        public GameObject[] buttons = new GameObject[0];
        public int[] buttonActions = new int[0];
        public Text[] buttonLabels = new Text[0];
        // The time controls the Clock button steps through, in seconds (base ChessArbiter.NoClock for none).
        public int[] clockBases = { ChessArbiter.NoClock, 60, 180, 300, 600, 900, 1800 };
        public int[] clockIncrements = { 0, 0, 2, 3, 0, 10, 0 };
        // The history wall whose picked game Load it here loads (step 10.2b); none hides the button.
        public GameHistoryWall historyWall;
        // The wall's selectionChanges when the buttons last saw it.
        int seenSelectionChanges;
        // The status display (step 9.5c): any number of panels (the builder puts one at each end of the table), each
        // with a players text (both seats, with their clocks during and after a game) and a status text (whose turn,
        // check, the result, pending offers and requests, a loaded game's names, the replay position), at the same
        // index. Brought up to date on every Refresh, every StatusInterval while a clock runs and once a second
        // otherwise.
        public Text[] statusPlayerLabels = new Text[0];
        public Text[] statusLabels = new Text[0];
        public const float StatusInterval = 0.1f;
        float nextStatusUpdate;
        // The PGN box (step 11.2a): a child of this UI, shown only to the local player once they open it with the PGN
        // button, with the game's PGN as it stands (ChessBoard.GamePgn) in a read-only text field to copy it from. It
        // stands upright over the far edge of the board, facing the end the player opened it from; the builder sets its
        // height and distance, the UI only which side of the board it stands on. Kept up to date while it shows, and
        // closed when the game has no moves left (a reset).
        public GameObject pgnBox;
        public InputField pgnField;
        [NonSerialized] public bool pgnShown;
        // The box's two parts (step 11.2b), one shown at a time: copying (its hint and pgnField) and pasting, opened
        // with Load PGN on a reset board: its hint, an editable field (pgnPasteField), a line saying how the import is
        // going or why it failed or was abandoned (pgnImportLabel), and the Import (pgnImportButton, shown while the
        // board can take an import and none is under way) and Close buttons. pgnPasting is set while the box shows
        // the paste part.
        public GameObject pgnCopyPart;
        public GameObject pgnPastePart;
        public InputField pgnPasteField;
        public Text pgnImportLabel;
        public GameObject pgnImportButton;
        [NonSerialized] public bool pgnPasting;
        // Whether the board's import was started from this box (so its progress and outcome are shown here), and a
        // note of the box's own (an empty paste), shown instead.
        bool importWatched;
        string importNote = "";
        // The UI is brought up to date on the frame after a change, as the view is: a test making thousands of changes
        // in one frame would otherwise run past Udon's time limit.
        bool dirty;
        // When the buttons are next brought up to date without a change: being an organizer and the board's idle time
        // (for Reset) change without the board changing.
        float nextButtonCheck;
        const float ButtonCheckInterval = 1;

        void Start() {
            if (!Utilities.IsValid(board)) {
                Debug.LogError("[ChessBoardUI] " + gameObject.name + ": the Board field is empty.");
                return;
            }
            board.AddListener(this);
            Refresh();
        }

        void Update() {
            if (dirty) {
                Refresh();
            } else if (Time.time >= nextButtonCheck) {
                UpdateButtons();
            } else if (Utilities.IsValid(historyWall) && historyWall.selectionChanges != seenSelectionChanges) {
                // A game picked on the wall shows Load it here at once.
                UpdateButtons();
            }
            // The clocks run without the board changing.
            if (Time.time >= nextStatusUpdate) UpdateStatus();
            // An import's progress moves on without the board changing.
            if (pgnShown && pgnPasting && importWatched && Utilities.IsValid(board)
                && board.importStatus == ChessBoard.ImportRunning) {
                UpdatePgnBox();
            }
        }

        public override void OnBoardChanged() {
            dirty = true;
        }

        // An import started from the PGN box (step 11.2b) moved on: once loaded the box closes (the board shows the
        // game), and when it fails or is abandoned the box opens again if it was closed meanwhile, to say why.
        public override void OnImportChanged() {
            if (!importWatched || !Utilities.IsValid(board)) return;
            int status = board.importStatus;
            if (status == ChessBoard.ImportLoaded) {
                importWatched = false;
                pgnShown = false;
                pgnPasting = false;
                if (Utilities.IsValid(pgnPasteField)) pgnPasteField.text = "";
            } else if ((status == ChessBoard.ImportFailed || status == ChessBoard.ImportAbandoned)
                && !(pgnShown && pgnPasting)) {
                pgnShown = true;
                pgnPasting = true;
                PlacePgnBox(LocalEnd());
            }
            UpdatePgnBox();
            UpdateButtons();
        }

        // Only the move list's text changed: the replay line may name a move it didn't know before, and the PGN box's move
        // text may have had entries missing.
        public override void OnNotationFilled() {
            UpdateStatus();
            UpdatePgnBox();
        }

        // Brings the UI up to the board now: the PGN box, the buttons and the status display. Called on the frame after
        // a change; public for tests.
        public void Refresh() {
            dirty = false;
            if (!Utilities.IsValid(board)) return;
            UpdatePgnBox();
            UpdateButtons();
            UpdateStatus();
        }

        // ---- Buttons (docs/player-input.md, Buttons; steps 9.4a and 9.4b) ----
        // Each button is shown only while its action applies to the local player (the board's Can... check), and
        // pressing it calls the board's method, which refuses whenever the check doesn't hold.

        // The UI Buttons' events, one per action.
        public void PressJoinWhite() {
            PressButton(ButtonJoinWhite);
        }

        public void PressJoinBlack() {
            PressButton(ButtonJoinBlack);
        }

        public void PressMode() {
            PressButton(ButtonMode);
        }

        public void PressClock() {
            PressButton(ButtonClock);
        }

        public void PressStart() {
            PressButton(ButtonStart);
        }

        public void PressContinue() {
            PressButton(ButtonContinue);
        }

        public void PressReset() {
            PressButton(ButtonReset);
        }

        public void PressResignWhite() {
            PressButton(ButtonResignWhite);
        }

        public void PressResignBlack() {
            PressButton(ButtonResignBlack);
        }

        public void PressOfferDrawWhite() {
            PressButton(ButtonOfferDrawWhite);
        }

        public void PressOfferDrawBlack() {
            PressButton(ButtonOfferDrawBlack);
        }

        public void PressAcceptDrawWhite() {
            PressButton(ButtonAcceptDrawWhite);
        }

        public void PressAcceptDrawBlack() {
            PressButton(ButtonAcceptDrawBlack);
        }

        public void PressDeclineDrawWhite() {
            PressButton(ButtonDeclineDrawWhite);
        }

        public void PressDeclineDrawBlack() {
            PressButton(ButtonDeclineDrawBlack);
        }

        public void PressClaimDraw() {
            PressButton(ButtonClaimDraw);
        }

        public void PressEnterReplay() {
            PressButton(ButtonEnterReplay);
        }

        public void PressLeaveReplay() {
            PressButton(ButtonLeaveReplay);
        }

        public void PressStepFirst() {
            PressButton(ButtonStepFirst);
        }

        public void PressStepBack() {
            PressButton(ButtonStepBack);
        }

        public void PressStepForward() {
            PressButton(ButtonStepForward);
        }

        public void PressStepLast() {
            PressButton(ButtonStepLast);
        }

        public void PressRequestTakebackWhite() {
            PressButton(ButtonRequestTakebackWhite);
        }

        public void PressRequestTakebackBlack() {
            PressButton(ButtonRequestTakebackBlack);
        }

        public void PressAcceptTakebackWhite() {
            PressButton(ButtonAcceptTakebackWhite);
        }

        public void PressAcceptTakebackBlack() {
            PressButton(ButtonAcceptTakebackBlack);
        }

        public void PressDeclineTakebackWhite() {
            PressButton(ButtonDeclineTakebackWhite);
        }

        public void PressDeclineTakebackBlack() {
            PressButton(ButtonDeclineTakebackBlack);
        }

        public void PressLoadHere() {
            PressButton(ButtonLoadHere);
        }

        public void PressPgn() {
            PressButton(ButtonPgn);
        }

        public void PressLoadPgn() {
            PressButton(ButtonLoadPgn);
        }

        // The PGN box's own buttons (step 11.2b).
        public void PressImport() {
            ImportPasted();
        }

        public void PressPgnClose() {
            OpenPgnBox(false, false);
            UpdateButtons();
        }

        // The colour the button for action acts for (ChessRules.White or Black), or ChessRules.NoColor for one that
        // acts for no colour (Claim draw is the side to move's).
        public static int ButtonColor(int action) {
            if (action == ButtonJoinWhite || action == ButtonResignWhite || action == ButtonOfferDrawWhite
                || action == ButtonAcceptDrawWhite || action == ButtonDeclineDrawWhite
                || action == ButtonRequestTakebackWhite || action == ButtonAcceptTakebackWhite
                || action == ButtonDeclineTakebackWhite) {
                return ChessRules.White;
            }
            if (action == ButtonJoinBlack || action == ButtonResignBlack || action == ButtonOfferDrawBlack
                || action == ButtonAcceptDrawBlack || action == ButtonDeclineDrawBlack
                || action == ButtonRequestTakebackBlack || action == ButtonAcceptTakebackBlack
                || action == ButtonDeclineTakebackBlack) {
                return ChessRules.Black;
            }
            return ChessRules.NoColor;
        }

        // The ply a step button for action steps the replay to: the start, one back, one on, or the game's current
        // position. -1 for any other action (and for one back from the start).
        int StepTarget(int action) {
            if (action == ButtonStepFirst) return 0;
            if (action == ButtonStepBack) return board.replayIndex - 1;
            if (action == ButtonStepForward) return board.replayIndex + 1;
            if (action == ButtonStepLast) return board.moveCount;
            return -1;
        }

        // Does what the button for action does, for the local player: the board's answer (InputRejected for an
        // unknown action). Mode switches between Standard and Chess960; Clock picks the next of clockBases and
        // clockIncrements after the board's time control (the first when it isn't one of them); the step buttons
        // step the replay to StepTarget; the others call the board's method for their colour.
        public int PressButton(int action) {
            if (!Utilities.IsValid(board)) return ChessBoard.InputRejected;
            int result = ChessBoard.InputRejected;
            int color = ButtonColor(action);
            int step = StepTarget(action);
            if (step >= 0) {
                result = board.StepTo(step);
            } else if (action == ButtonResignWhite || action == ButtonResignBlack) {
                result = board.Resign(color);
            } else if (action == ButtonOfferDrawWhite || action == ButtonOfferDrawBlack) {
                result = board.OfferDraw(color);
            } else if (action == ButtonAcceptDrawWhite || action == ButtonAcceptDrawBlack) {
                result = board.AcceptDraw(color);
            } else if (action == ButtonDeclineDrawWhite || action == ButtonDeclineDrawBlack) {
                result = board.DeclineDraw(color);
            } else if (action == ButtonClaimDraw) {
                result = board.ClaimDraw();
            } else if (action == ButtonEnterReplay) {
                result = board.EnterReplay();
            } else if (action == ButtonLeaveReplay) {
                result = board.LeaveReplay();
            } else if (action == ButtonRequestTakebackWhite || action == ButtonRequestTakebackBlack) {
                result = board.RequestTakeback(color);
            } else if (action == ButtonAcceptTakebackWhite || action == ButtonAcceptTakebackBlack) {
                result = board.AcceptTakeback(color);
            } else if (action == ButtonDeclineTakebackWhite || action == ButtonDeclineTakebackBlack) {
                result = board.DeclineTakeback(color);
            } else if (action == ButtonJoinWhite) {
                result = board.JoinWhite();
            } else if (action == ButtonJoinBlack) {
                result = board.JoinBlack();
            } else if (action == ButtonMode) {
                result = board.SetMode(board.mode == ChessGame.ModeChess960
                    ? ChessGame.ModeStandard : ChessGame.ModeChess960);
            } else if (action == ButtonClock) {
                int next = NextClockChoice();
                if (next >= 0) result = board.SetClock(clockBases[next], clockIncrements[next]);
            } else if (action == ButtonStart) {
                result = board.StartGame();
            } else if (action == ButtonContinue) {
                result = board.ContinueGame();
            } else if (action == ButtonReset) {
                result = board.ResetBoard();
            } else if (action == ButtonLoadHere) {
                if (Utilities.IsValid(historyWall) && historyWall.HasSelection()) {
                    result = board.LoadArchivedGame(historyWall.selectedGameId);
                }
                // Loaded once: the other boards stop offering it.
                if (result == ChessBoard.InputAccepted) historyWall.ClearSelection();
            } else if (action == ButtonPgn) {
                // Local to this player: nothing on the board changes.
                if (ButtonApplies(ButtonPgn)) {
                    OpenPgnBox(!pgnShown || pgnPasting, false);
                    result = ChessBoard.InputAccepted;
                }
            } else if (action == ButtonLoadPgn) {
                // Local too: the board changes only once an import has finished.
                if (ButtonApplies(ButtonLoadPgn)) {
                    OpenPgnBox(!pgnShown || !pgnPasting, true);
                    result = ChessBoard.InputAccepted;
                }
            }
            // A refusal tells nobody, so the buttons are brought up to date here too.
            UpdateButtons();
            return result;
        }

        // The index in clockBases of the choice after the board's time control; -1 with no choices.
        int NextClockChoice() {
            int count = Mathf.Min(clockBases.Length, clockIncrements.Length);
            if (count == 0) return -1;
            for (int i = 0; i < count; i++) {
                if (clockBases[i] == board.baseTime && clockIncrements[i] == board.increment) return (i + 1) % count;
            }
            return 0;
        }

        // Whether the button for action applies to the local player now: its Can... check on the board.
        public bool ButtonApplies(int action) {
            if (!Utilities.IsValid(board)) return false;
            if (action == ButtonJoinWhite) return board.CanJoin(ChessRules.White);
            if (action == ButtonJoinBlack) return board.CanJoin(ChessRules.Black);
            if (action == ButtonMode) return board.CanSetMode();
            if (action == ButtonClock) return board.CanSetClock();
            if (action == ButtonStart) return board.CanStartGame();
            if (action == ButtonContinue) return board.CanContinue();
            if (action == ButtonReset) return board.CanReset();
            if (action == ButtonLoadHere) {
                return board.CanLoadGame() && Utilities.IsValid(historyWall) && historyWall.HasSelection();
            }
            if (action == ButtonPgn) return board.moveCount > 0;
            if (action == ButtonLoadPgn) return board.CanImport();
            int color = ButtonColor(action);
            int step = StepTarget(action);
            if (step >= 0) return board.CanStepTo(step);
            if (action == ButtonResignWhite || action == ButtonResignBlack) return board.CanResign(color);
            if (action == ButtonOfferDrawWhite || action == ButtonOfferDrawBlack) return board.CanOfferDraw(color);
            if (action == ButtonAcceptDrawWhite || action == ButtonAcceptDrawBlack) return board.CanAcceptDraw(color);
            if (action == ButtonDeclineDrawWhite || action == ButtonDeclineDrawBlack) {
                return board.CanDeclineDraw(color);
            }
            if (action == ButtonClaimDraw) return board.CanClaimDraw();
            if (action == ButtonEnterReplay) return board.CanEnterReplay();
            if (action == ButtonLeaveReplay) return board.CanLeaveReplay();
            if (action == ButtonRequestTakebackWhite || action == ButtonRequestTakebackBlack) {
                return board.CanRequestTakeback(color);
            }
            if (action == ButtonAcceptTakebackWhite || action == ButtonAcceptTakebackBlack) {
                return board.CanAcceptTakeback(color);
            }
            if (action == ButtonDeclineTakebackWhite || action == ButtonDeclineTakebackBlack) {
                return board.CanDeclineTakeback(color);
            }
            return false;
        }

        // The label of the button for action: Mode and Clock show the board's current choice, Claim draw the draw
        // claimed.
        public string ButtonText(int action) {
            if (action == ButtonJoinWhite) return "Join White";
            if (action == ButtonJoinBlack) return "Join Black";
            if (action == ButtonMode) {
                return "Mode: " + ModeText(Utilities.IsValid(board) ? board.mode : ChessGame.ModeStandard);
            }
            if (action == ButtonClock) {
                if (!Utilities.IsValid(board)) return "Clock";
                return "Clock: " + ClockText(board.baseTime, board.increment);
            }
            if (action == ButtonStart) return "Start";
            if (action == ButtonContinue) return "Continue from here";
            if (action == ButtonReset) return "Reset";
            if (action == ButtonResignWhite || action == ButtonResignBlack) return "Resign";
            if (action == ButtonOfferDrawWhite || action == ButtonOfferDrawBlack) return "Offer draw";
            if (action == ButtonAcceptDrawWhite || action == ButtonAcceptDrawBlack) return "Accept draw";
            if (action == ButtonDeclineDrawWhite || action == ButtonDeclineDrawBlack) return "Decline draw";
            if (action == ButtonClaimDraw) {
                int claim = Utilities.IsValid(board) ? board.ClaimableDraw() : ChessGame.EndingNone;
                if (claim == ChessGame.EndingThreefoldRepetition) return "Claim draw (threefold)";
                if (claim == ChessGame.EndingFiftyMoveRule) return "Claim draw (50 moves)";
                return "Claim draw";
            }
            if (action == ButtonEnterReplay) return "Replay";
            if (action == ButtonLeaveReplay) return "Back to game";
            if (action == ButtonStepFirst) return "<<";
            if (action == ButtonStepBack) return "<";
            if (action == ButtonStepForward) return ">";
            if (action == ButtonStepLast) return ">>";
            if (action == ButtonRequestTakebackWhite || action == ButtonRequestTakebackBlack) {
                return "Take back to here";
            }
            if (action == ButtonAcceptTakebackWhite || action == ButtonAcceptTakebackBlack) return "Accept takeback";
            if (action == ButtonDeclineTakebackWhite || action == ButtonDeclineTakebackBlack) {
                return "Decline takeback";
            }
            if (action == ButtonLoadHere) return "Load it here";
            if (action == ButtonPgn) return pgnShown && !pgnPasting ? "Hide PGN" : "PGN";
            if (action == ButtonLoadPgn) return pgnShown && pgnPasting ? "Hide PGN" : "Load PGN";
            return "";
        }

        // A time control as players say it: "No clock", "5+3" (minutes plus seconds), or "45s+0" for a base that
        // isn't whole minutes.
        public static string ClockText(int baseSeconds, int incrementSeconds) {
            if (baseSeconds == ChessArbiter.NoClock) return "No clock";
            string baseText = baseSeconds % 60 == 0 ? (baseSeconds / 60).ToString() : baseSeconds + "s";
            return baseText + "+" + incrementSeconds;
        }

        // Shows each button whose action applies to the local player and hides the rest, with its label. Called on
        // every Refresh, once a second and after every press.
        public void UpdateButtons() {
            nextButtonCheck = Time.time + ButtonCheckInterval;
            if (Utilities.IsValid(historyWall)) seenSelectionChanges = historyWall.selectionChanges;
            for (int i = 0; i < buttons.Length; i++) {
                GameObject button = buttons[i];
                if (!Utilities.IsValid(button) || i >= buttonActions.Length) continue;
                int action = buttonActions[i];
                bool shown = ButtonApplies(action);
                if (button.activeSelf != shown) button.SetActive(shown);
                if (!shown || i >= buttonLabels.Length || !Utilities.IsValid(buttonLabels[i])) continue;
                string text = ButtonText(action);
                if (buttonLabels[i].text != text) buttonLabels[i].text = text;
            }
        }

        // ---- PGN box (step 11.2a; docs/pgn.md, Input / output) ----

        // Shows the PGN box while the local player has it open, hides it otherwise. Copying, it holds the game's PGN as
        // it stands (written only when it changed, so a selection survives a redraw that changed nothing), and closes
        // once the game has no moves (a reset), so it doesn't open again by itself on the next game. Pasting (11.2b), it
        // stays open while the board can take an import or while it says how the last one went, and shows Import only
        // while an import can start. Called on every Refresh, when notation is filled in, after the box's buttons and
        // the board's import changes, and every frame while an import from it is under way.
        public void UpdatePgnBox() {
            if (!Utilities.IsValid(board)) return;
            if (pgnShown && !pgnPasting && !ButtonApplies(ButtonPgn)) pgnShown = false;
            if (pgnShown && pgnPasting && !board.CanImport() && ImportText().Length == 0) pgnShown = false;
            if (!pgnShown) pgnPasting = false;
            if (pgnShown && !pgnPasting && Utilities.IsValid(pgnField)) {
                string pgn = board.GamePgn();
                if (pgnField.text != pgn) pgnField.text = pgn;
            }
            if (pgnPasting) {
                string note = ImportText();
                if (Utilities.IsValid(pgnImportLabel) && pgnImportLabel.text != note) pgnImportLabel.text = note;
                bool canStart = board.CanImport() && board.importStatus != ChessBoard.ImportRunning;
                if (Utilities.IsValid(pgnImportButton) && pgnImportButton.activeSelf != canStart) {
                    pgnImportButton.SetActive(canStart);
                }
            }
            if (Utilities.IsValid(pgnCopyPart) && pgnCopyPart.activeSelf == pgnPasting) {
                pgnCopyPart.SetActive(!pgnPasting);
            }
            if (Utilities.IsValid(pgnPastePart) && pgnPastePart.activeSelf != pgnPasting) {
                pgnPastePart.SetActive(pgnPasting);
            }
            if (Utilities.IsValid(pgnBox) && pgnBox.activeSelf != pgnShown) pgnBox.SetActive(pgnShown);
        }

        // Opens the PGN box for the local player, to copy or (paste) to paste into, standing it over the far side of the
        // board from them; or closes it. Closing it, or opening the paste part afresh, forgets how an earlier import
        // went, unless one is still under way (which opens the box again if it fails).
        void OpenPgnBox(bool open, bool paste) {
            bool forget = !open || (paste && !(pgnShown && pgnPasting));
            pgnShown = open;
            pgnPasting = open && paste;
            if (open) PlacePgnBox(LocalEnd());
            if (forget) {
                importNote = "";
                if (!Utilities.IsValid(board) || board.importStatus != ChessBoard.ImportRunning) importWatched = false;
            }
            UpdatePgnBox();
        }

        // The Import button (step 11.2b): starts importing the text pasted into the box onto the board
        // (ChessBoard.StartImport), which loads it once it has been read over the next frames. Refused with the box
        // closed, with nothing pasted (the box says so) and where the board refuses. Public for tests.
        public int ImportPasted() {
            if (!Utilities.IsValid(board) || !pgnShown || !pgnPasting || !Utilities.IsValid(pgnPasteField)) {
                return ChessBoard.InputRejected;
            }
            string text = pgnPasteField.text;
            int result = ChessBoard.InputRejected;
            if (text == null || text.Trim().Length == 0) {
                importNote = "Paste a PGN into the box first.";
            } else {
                importNote = "";
                // Set first: the board tells its listeners the import started before it returns.
                importWatched = true;
                result = board.StartImport(text);
                if (result != ChessBoard.InputAccepted) importWatched = false;
            }
            UpdatePgnBox();
            UpdateButtons();
            return result;
        }

        // The paste part's line: the box's own note, or how the import started from it is going: the moves read so
        // far, why it failed ("Import failed at move 2...: illegal move") or that it was abandoned; "" otherwise.
        public string ImportText() {
            if (importNote.Length > 0) return importNote;
            if (!importWatched || !Utilities.IsValid(board)) return "";
            int status = board.importStatus;
            if (status == ChessBoard.ImportRunning) {
                int read = Utilities.IsValid(board.importer) ? board.importer.MoveCount() : 0;
                return "Importing: " + read + (read == 1 ? " move" : " moves") + " read";
            }
            if (status == ChessBoard.ImportFailed) return "Import " + board.importMessage;
            if (status == ChessBoard.ImportAbandoned) return "Import abandoned: " + board.importMessage;
            return "";
        }

        // Stands the PGN box over the far edge of the board from color's end of the table, facing that end (unturned it
        // faces -z, White's end), at the height and distance the builder gave it.
        public void PlacePgnBox(int color) {
            if (!Utilities.IsValid(pgnBox)) return;
            Transform box = pgnBox.transform;
            Vector3 position = box.localPosition;
            position.x = 0;
            position.z = Mathf.Abs(position.z) * (color == ChessRules.White ? 1 : -1);
            box.localPosition = position;
            box.localRotation = color == ChessRules.White ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
        }

        // The end of the table the local player stands at: White's (-z) or Black's.
        public int LocalEnd() {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return ChessRules.White;
            return transform.InverseTransformPoint(local.GetPosition()).z <= 0 ? ChessRules.White : ChessRules.Black;
        }

        // ---- Status display (step 9.5c) ----

        // Brings every status panel up to the board at the server time now, and schedules the next update: soon while
        // a clock runs (the side to move's time counts down, and its flag can fall), once a second otherwise.
        public void UpdateStatus() {
            bool valid = Utilities.IsValid(board);
            nextStatusUpdate = Time.time + (valid && board.IsClockRunning() ? StatusInterval : 1);
            double now = valid ? board.ServerTime() : 0;
            string players = PlayersTextAt(now);
            string status = StatusTextAt(now);
            for (int i = 0; i < statusPlayerLabels.Length; i++) {
                Text label = statusPlayerLabels[i];
                if (Utilities.IsValid(label) && label.text != players) label.text = players;
            }
            for (int i = 0; i < statusLabels.Length; i++) {
                Text label = statusLabels[i];
                if (Utilities.IsValid(label) && label.text != status) label.text = status;
            }
        }

        // Both seats at server time now, White's line first (PlayerTextAt).
        public string PlayersTextAt(double now) {
            return PlayerTextAt(ChessRules.White, now) + "\n" + PlayerTextAt(ChessRules.Black, now);
        }

        // A seat's line: "White: Ana", "(open)" for an empty seat, with the side's remaining time at server time now
        // during and after a game with a clock ("White: Ana  4:58").
        public string PlayerTextAt(int color, double now) {
            if (!Utilities.IsValid(board)) return "";
            string name = color == ChessRules.White ? board.whitePlayer : board.blackPlayer;
            string text = ColorText(color) + ": " + (name != null && name.Length > 0 ? name : "(open)");
            bool played = board.lifecycle == ChessBoard.LifecycleInGame || board.lifecycle == ChessBoard.LifecycleEnded;
            if (played && board.HasClock()) text += "  " + ClockDisplay(board.RemainingTimeAt(color, now));
            return text;
        }

        // The board's state at server time now, a line each: a reset board's invitation; the lobby's mode and clock; a
        // loaded game's names (and mode, for Chess960); in a game, whose turn it is and whether they're in check, or that
        // their time is up; the result of an ended or loaded game (EndingText); a pending draw offer and takeback
        // request; the replay position (ReplayText).
        public string StatusTextAt(double now) {
            if (!Utilities.IsValid(board)) return "";
            int lifecycle = board.lifecycle;
            if (lifecycle == ChessBoard.LifecycleReset) return "Join White or Black to play";
            string text = "";
            if (lifecycle == ChessBoard.LifecycleLobby) {
                text = "Lobby: " + ModeText(board.mode) + ", " + ClockText(board.baseTime, board.increment);
            } else if (lifecycle == ChessBoard.LifecycleLoaded) {
                text = "Loaded game: " + TagText(board.replaySourceWhite) + " vs " + TagText(board.replaySourceBlack);
                if (board.mode == ChessGame.ModeChess960) text += " (Chess960)";
            } else if (lifecycle == ChessBoard.LifecycleInGame) {
                int flagged = board.FlaggedColorAt(now);
                if (flagged != ChessRules.NoColor) {
                    text = ColorText(flagged) + "'s time is up";
                } else {
                    text = ColorText(board.SideToMove()) + " to move";
                    if (board.IsInCheck()) text += ", in check";
                }
            }
            text = AddLine(text, EndingText());
            if (board.drawOfferColor != ChessRules.NoColor) {
                text = AddLine(text, ColorText(board.drawOfferColor) + " offers a draw");
            }
            if (board.takebackColor != ChessRules.NoColor) {
                text = AddLine(text, ColorText(board.takebackColor) + " asks to take back to here");
            }
            if (board.replayMode) text = AddLine(text, ReplayText());
            return text;
        }

        // How an ended or loaded game ended, with its PGN result: "White wins by checkmate (1-0)", "Draw by agreement
        // (1/2-1/2)"; "Unfinished game (*)" for a loaded game without a result; "" otherwise.
        public string EndingText() {
            if (!Utilities.IsValid(board)) return "";
            int lifecycle = board.lifecycle;
            if (lifecycle != ChessBoard.LifecycleEnded && lifecycle != ChessBoard.LifecycleLoaded) return "";
            int ending = board.ending;
            int winner = board.winner;
            if (ending == ChessGame.EndingNone || ending == ChessGame.EndingUnterminated) {
                return lifecycle == ChessBoard.LifecycleLoaded ? "Unfinished game (*)" : "";
            }
            string result = " (" + board.ResultText() + ")";
            if (ending == ChessGame.EndingTimeForfeit && winner == ChessRules.NoColor) {
                // A time forfeit drawn: the side out of time is the one that was to move, and its opponent couldn't
                // mate.
                int flagged = board.SideToMove();
                return "Draw: " + ColorText(flagged) + " ran out of time, "
                    + ColorText(ChessRules.OtherColor(flagged)) + " can't checkmate" + result;
            }
            return ChessGame.EndingDescription(ending, winner) + result;
        }

        // The replay position: "Replay: 2. Nf3 (3 of 4)" (the move that led to it, numbered as PGN numbers it, "?"
        // while its notation is missing), "Replay: start (0 of 4)", or "Replay: custom position" after free moves.
        public string ReplayText() {
            if (board.replayCustom) return "Replay: custom position";
            int ply = board.replayIndex;
            string of = " (" + ply + " of " + board.moveCount + ")";
            if (ply <= 0 || ply > board.moveCount) return "Replay: start" + of;
            // The side that made the first move: the side to move now, or the other after an odd number of moves.
            int firstSide = board.SideToMove();
            if (board.moveCount % 2 == 1) firstSide = ChessRules.OtherColor(firstSide);
            string san = board.notation[ply - 1];
            if (san == null || san.Length == 0) san = "?";
            return "Replay: " + ChessPgn.MoveLabel(board.startMoveNumber, firstSide, ply - 1) + " " + san + of;
        }

        // A clock as players read it: "4:58", "1:02:05" from an hour, tenths under ten seconds ("0:09.8").
        public static string ClockDisplay(int milliseconds) {
            if (milliseconds < 0) milliseconds = 0;
            int seconds = milliseconds / 1000;
            int minutes = seconds / 60;
            if (minutes >= 60) return (minutes / 60) + ":" + TwoDigits(minutes % 60) + ":" + TwoDigits(seconds % 60);
            string text = minutes + ":" + TwoDigits(seconds % 60);
            if (milliseconds < 10000) text += "." + (milliseconds / 100 % 10);
            return text;
        }

        static string TwoDigits(int value) {
            return value < 10 ? "0" + value : value.ToString();
        }

        public static string ColorText(int color) {
            return color == ChessRules.White ? "White" : "Black";
        }

        public static string ModeText(int mode) {
            return mode == ChessGame.ModeChess960 ? "Chess960" : "Standard";
        }

        // A loaded game's name tag, "?" when it had none.
        static string TagText(string value) {
            return value != null && value.Length > 0 ? value : "?";
        }

        static string AddLine(string text, string line) {
            if (line.Length == 0) return text;
            return text.Length == 0 ? line : text + "\n" + line;
        }
    }
}
