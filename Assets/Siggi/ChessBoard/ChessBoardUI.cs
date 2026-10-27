using System;
using UdonSharp;
using TMPro;
using UnityEngine;
using SiggiUSTools;
using UnityEngine.UI;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The board's UI (docs/architecture.md, docs/player-input.md; PLAN.md 12.1): the button panels, the status
    // display, the PGN box (copying a game out, pasting one in) and Load it here for the arbiter's pick. A
    // listener of its own, holding no game state and no synced fields: it reads the board after each change and acts
    // through the board's methods, never through the physical board (ChessBoardView), which it doesn't know. It plays
    // every sound but the move sounds (step 12.12), so a UI of a world's own replaces them all with it. Placed from
    // the Chess Board UI prefab (step 14.4, docs/prefabs.md), its board set on the instance.
    //
    // Its local space is the view's: it stands at the board's centre, files along +x, White's end at -z, so the PGN box
    // (and _LocalEnd) need nothing from the table. The prefab places its parts around that origin.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ChessBoardUI : ChessBoardListener {
        // The board shown and acted on; the UI adds itself as a listener on Start.
        public ChessBoard board;
        [Tooltip("The chess system's shared I18n (the Chess Language object), for the UI's text. Left empty: the I18n "
            + "sanity check fills it in on build and before Play Mode.")]
        public I18n i18n;
        // The strings the UI shows, from i18n (LoadTexts), read on first use and again on a language change: the game
        // texts the status lines are written with (_LoadGameTexts), and the UI's own.
        string[] gameTexts;
        string textJoinWhite = "";
        string textJoinBlack = "";
        string textLeave = "";
        string textMode = "";
        string textClockNone = "";
        string textClock = "";
        string textStart = "";
        string textContinue = "";
        string textReset = "";
        string textResign = "";
        string textOfferDraw = "";
        string textAcceptDraw = "";
        string textDeclineDraw = "";
        string textClaimDraw = "";
        string textClaimDrawThreefold = "";
        string textClaimDrawFiftyMoves = "";
        string textReplay = "";
        string textBackToGame = "";
        string textTakeBackToHere = "";
        string textAcceptTakeback = "";
        string textDeclineTakeback = "";
        string textLoadHere = "";
        string textPgn = "";
        string textLoadPgn = "";
        string textHidePgn = "";
        string textPgnCopyHint = "";
        string textPgnPasteHint = "";
        string textPgnImport = "";
        string textPgnClose = "";
        string textImportPasteFirst = "";
        string textImportRunning = "";
        string textImportFailed = "";
        string textImportFailedLoad = "";
        string textImportAbandoned = "";
        string textImportErrorSyntax = "";
        string textImportErrorIllegalMove = "";
        string textImportErrorAmbiguousMove = "";
        string textImportErrorFen = "";
        string textImportErrorTooLong = "";
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
        // acts for its own colour (_ButtonColor).
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
        // Step 10.2b: loads the game picked on a history wall (the arbiter's pick) onto this (reset) board.
        public const int ButtonLoadHere = 28;
        // Step 11.2a: opens or closes the PGN box for the local player.
        public const int ButtonPgn = 29;
        // Step 11.2b: opens the PGN box to paste a PGN into and import it, on a reset board.
        public const int ButtonLoadPgn = 30;
        // Step 13.1: leaves the seat of its colour before a game has started.
        public const int ButtonLeaveWhite = 31;
        public const int ButtonLeaveBlack = 32;
        public const int ButtonCount = 33;
        // The button objects in the world (any number: the prefab has a panel at each end of the table), with the
        // Button* action of each and its label at the same index. A button's UI Button calls the UI's Press...
        // event for its action.
        public GameObject[] buttons = new GameObject[0];
        public int[] buttonActions = new int[0];
        public TextMeshProUGUI[] buttonLabels = new TextMeshProUGUI[0];
        // The time controls the Clock button steps through, in seconds (base ChessArbiter.NoClock for none).
        public int[] clockBases = { ChessArbiter.NoClock, 60, 180, 300, 600, 900, 1800 };
        public int[] clockIncrements = { 0, 0, 2, 3, 0, 10, 0 };
        // The status display (step 9.5c): any number of panels (the prefab has one at each end of the table), each
        // with a players text (both seats, with their clocks during and after a game) and a status text (whose turn,
        // check, the result, pending offers and requests, a loaded game's names, the replay position), at the same
        // index. Brought up to date on every Refresh, every StatusInterval while a clock runs and once a second
        // otherwise.
        public TextMeshProUGUI[] statusPlayerLabels = new TextMeshProUGUI[0];
        public TextMeshProUGUI[] statusLabels = new TextMeshProUGUI[0];
        public const float StatusInterval = 0.1f;
        float nextStatusUpdate;
        // The PGN box (step 11.2a): a child of this UI, shown only to the local player once they open it with the PGN
        // button, with the game's PGN as it stands (ChessBoard.GamePgn) in a read-only text field to copy it from. It
        // stands upright over the far edge of the board, facing the end the player opened it from; the prefab sets its
        // height and distance, the UI only which side of the board it stands on. Kept up to date while it shows, and
        // closed when the game has no moves left (a reset).
        public GameObject pgnBox;
        public TMP_InputField pgnField;
        [NonSerialized] public bool pgnShown;
        // The box's two parts (step 11.2b), one shown at a time: copying (its hint and pgnField) and pasting, opened
        // with Load PGN on a reset board: its hint, an editable field (pgnPasteField), a line saying how the import is
        // going or why it failed or was abandoned (pgnImportLabel), and the Import (pgnImportButton, shown while the
        // board can take an import and none is under way) and Close buttons. pgnPasting is set while the box shows
        // the paste part.
        public GameObject pgnCopyPart;
        public GameObject pgnPastePart;
        public TMP_InputField pgnPasteField;
        public TextMeshProUGUI pgnImportLabel;
        public GameObject pgnImportButton;
        // The box's fixed texts, from the language pack (PLAN.md 13.5): the copy and paste parts' hints, and the Import
        // and Close buttons' labels.
        public TextMeshProUGUI pgnCopyHint;
        public TextMeshProUGUI pgnPasteHint;
        public TextMeshProUGUI pgnImportText;
        public TextMeshProUGUI pgnCloseText;
        [NonSerialized] public bool pgnPasting;
        // Whether the board's import was started from this box (so its progress and outcome are shown here), and whether
        // the box says to paste first (an empty paste), shown instead.
        bool importWatched;
        bool pasteFirstNoted;
        // The UI is brought up to date on the frame after a change, as the view is: a test making thousands of changes
        // in one frame would otherwise run past Udon's time limit.
        bool dirty;
        // When the buttons are next brought up to date without a change: being an organizer and the board's idle time
        // (for Reset) change without the board changing.
        float nextButtonCheck;
        const float ButtonCheckInterval = 1;
        // Sound effects (step 12.12): every sound but the move sounds (ChessBoardView's), from audioSource (a spatial
        // source the prefab stands at the UI's origin, the board's centre). clickSound plays for a button pressed, on
        // the presser's client; the rest on every client, for a change as it happens: gameStartSound for Start or
        // Continue from here, notificationSound for a draw offered or a takeback requested, gameEndSound when a game
        // ends (timeOutSound instead for a flag fall), resetSound for a reset, and timeWarningSound as the side to
        // move's clock passes each of timeWarningSeconds. A move that ends the game, or a replay step onto such an end,
        // plays game over once the view's check sound has finished (checkSound, held here for its length only; the
        // view plays the move's sound meanwhile). Nothing plays for a late joiner's first update, during the first
        // StartupSilence seconds, or for changes that come several between two Refreshes (a test's thousands).
        public AudioSource audioSource;
        public AudioClip clickSound;
        public AudioClip gameStartSound;
        public AudioClip notificationSound;
        public AudioClip gameEndSound;
        public AudioClip resetSound;
        public AudioClip timeWarningSound;
        public AudioClip timeOutSound;
        [Tooltip("The board view's Check Sound, for its length only: game over waits for it after a mating move.")]
        public AudioClip checkSound;
        [Tooltip("The times left on the side to move's clock, in seconds, at which the time warning plays.")]
        public int[] timeWarningSeconds = { 60, 30, 10 };
        public const float StartupSilence = 1;
        // For tests: nothing is heard while muted, but lastSound and soundsPlayed follow what would have been.
        [NonSerialized] public bool muted;
        [NonSerialized] public AudioClip lastSound;
        [NonSerialized] public int soundsPlayed;
        // When the waiting game-over sound plays (Time.time; -1 for none).
        [NonSerialized] public float gameEndAt = -1;
        // When sounds may start (Time.time), whether this client has caught up with the board (a change seen after
        // the silence: made here, or a received one, the first of which is silent), the change whose sound the next
        // Refresh plays (ChangeNone for none) and how many changes have come since the last Refresh, and the draw
        // offer and takeback request at the last _Refresh (a ChangeRequest that makes one plays its sound).
        float soundsFrom;
        bool caughtUp;
        int soundChange = ChessBoard.ChangeNone;
        int soundChanges;
        int seenDrawOffer = ChessRules.NoColor;
        int seenTakeback = ChessRules.NoColor;
        // The side to move's clock at the last status update: its colour (NoColor while no clock runs), its turn's
        // start and its time left, for the time warning.
        int warnColor = ChessRules.NoColor;
        double warnTurnStart;
        int warnLeft;

        void Start() {
            soundsFrom = Time.time + StartupSilence;
            if (Utilities.IsValid(i18n)) i18n.AddLanguageChangeListener(this);
            SetFixedTexts();
            if (!Utilities.IsValid(board)) {
                Debug.LogError("[ChessBoardUI] " + gameObject.name + ": the Board field is empty.");
                return;
            }
            board.AddListener(this);
            _Refresh();
        }

        void Update() {
            if (dirty) {
                _Refresh();
            } else if (Time.time >= nextButtonCheck) {
                _UpdateButtons();
            }
            // The clocks run without the board changing.
            if (Time.time >= nextStatusUpdate) _UpdateStatus();
            if (gameEndAt >= 0 && Time.time >= gameEndAt) {
                gameEndAt = -1;
                PlaySound(gameEndSound);
            }
            // An import's progress moves on without the board changing.
            if (pgnShown && pgnPasting && importWatched && Utilities.IsValid(board)
                && board.importStatus == ChessBoard.ImportRunning) {
                _UpdatePgnBox();
            }
        }

        public override void OnBoardChanged() {
            dirty = true;
            if (Utilities.IsValid(board)) {
                ChessUtil._NoteSound(board.lastChange, MakesSound(board.lastChange), board, soundsFrom, ref caughtUp,
                    ref soundChanges, ref soundChange);
            }
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
                _PlacePgnBox(_LocalEnd());
            }
            _UpdatePgnBox();
            _UpdateButtons();
        }

        // The board's arbiter changed (step 13.10): a game picked or unpicked on a history wall shows or hides Load it
        // here at once, and a settings change brings the buttons up to date.
        public override void OnArbiterChanged(ChessBoard changed, int change) {
            _UpdateButtons();
        }

        // Only the move list's text changed: the replay line may name a move it didn't know before, and the PGN box's move
        // text may have had entries missing.
        public override void OnNotationFilled() {
            _UpdateStatus();
            _UpdatePgnBox();
        }

        // Brings the UI up to the board now: the PGN box, the buttons and the status display. Called on the frame after
        // a change; public for tests.
        public void _Refresh() {
            dirty = false;
            int change = soundChange;
            soundChange = ChessBoard.ChangeNone;
            soundChanges = 0;
            if (!Utilities.IsValid(board)) return;
            _UpdatePgnBox();
            _UpdateButtons();
            _UpdateStatus();
            PlayChangeSound(change);
            seenDrawOffer = board.drawOfferColor;
            seenTakeback = board.takebackColor;
        }

        // ---- Text (PLAN.md 13.5) ----

        // The game texts the status lines are written with (_LoadGameTexts), in the language now; read on first use.
        // Public for tests.
        public string[] _GameTexts() {
            if (gameTexts == null) LoadTexts();
            return gameTexts;
        }

        // The I18n's language changed (its AddLanguageChangeListener): every string is read again and every text
        // written again.
        public void OnLanguageChange() {
            LoadTexts();
            SetFixedTexts();
            if (!Utilities.IsValid(board)) return;
            _UpdatePgnBox();
            _UpdateButtons();
            _UpdateStatus();
        }

        // Reads every string the UI shows from i18n into its fields: one call into the I18n each, here only, so a
        // redraw calls none.
        void LoadTexts() {
            ChessUtil._CheckI18n(i18n, "[ChessBoardUI] " + gameObject.name);
            gameTexts = _LoadGameTexts(i18n);
            textJoinWhite = ChessUtil.Text(i18n, "button_join_white");
            textJoinBlack = ChessUtil.Text(i18n, "button_join_black");
            textLeave = ChessUtil.Text(i18n, "button_leave");
            textMode = ChessUtil.Text(i18n, "button_mode");
            textClockNone = ChessUtil.Text(i18n, "button_clock_none");
            textClock = ChessUtil.Text(i18n, "button_clock");
            textStart = ChessUtil.Text(i18n, "button_start");
            textContinue = ChessUtil.Text(i18n, "button_continue");
            textReset = ChessUtil.Text(i18n, "button_reset");
            textResign = ChessUtil.Text(i18n, "button_resign");
            textOfferDraw = ChessUtil.Text(i18n, "button_offer_draw");
            textAcceptDraw = ChessUtil.Text(i18n, "button_accept_draw");
            textDeclineDraw = ChessUtil.Text(i18n, "button_decline_draw");
            textClaimDraw = ChessUtil.Text(i18n, "button_claim_draw");
            textClaimDrawThreefold = ChessUtil.Text(i18n, "button_claim_draw_threefold");
            textClaimDrawFiftyMoves = ChessUtil.Text(i18n, "button_claim_draw_fifty_moves");
            textReplay = ChessUtil.Text(i18n, "button_replay");
            textBackToGame = ChessUtil.Text(i18n, "button_back_to_game");
            textTakeBackToHere = ChessUtil.Text(i18n, "button_take_back_to_here");
            textAcceptTakeback = ChessUtil.Text(i18n, "button_accept_takeback");
            textDeclineTakeback = ChessUtil.Text(i18n, "button_decline_takeback");
            textLoadHere = ChessUtil.Text(i18n, "button_load_here");
            textPgn = ChessUtil.Text(i18n, "button_pgn");
            textLoadPgn = ChessUtil.Text(i18n, "button_load_pgn");
            textHidePgn = ChessUtil.Text(i18n, "button_hide_pgn");
            textPgnCopyHint = ChessUtil.Text(i18n, "pgn_copy_hint");
            textPgnPasteHint = ChessUtil.Text(i18n, "pgn_paste_hint");
            textPgnImport = ChessUtil.Text(i18n, "pgn_import");
            textPgnClose = ChessUtil.Text(i18n, "pgn_close");
            textImportPasteFirst = ChessUtil.Text(i18n, "import_paste_first");
            textImportRunning = ChessUtil.Text(i18n, "import_running");
            textImportFailed = ChessUtil.Text(i18n, "import_failed");
            textImportFailedLoad = ChessUtil.Text(i18n, "import_failed_load");
            textImportAbandoned = ChessUtil.Text(i18n, "import_abandoned");
            textImportErrorSyntax = ChessUtil.Text(i18n, "import_error_syntax");
            textImportErrorIllegalMove = ChessUtil.Text(i18n, "import_error_illegal_move");
            textImportErrorAmbiguousMove = ChessUtil.Text(i18n, "import_error_ambiguous_move");
            textImportErrorFen = ChessUtil.Text(i18n, "import_error_fen");
            textImportErrorTooLong = ChessUtil.Text(i18n, "import_error_too_long");
        }

        // The texts that change only with the language: the PGN box's hints and its buttons' labels.
        void SetFixedTexts() {
            if (gameTexts == null) LoadTexts();
            ChessUtil._SetText(pgnCopyHint, textPgnCopyHint);
            ChessUtil._SetText(pgnPasteHint, textPgnPasteHint);
            ChessUtil._SetText(pgnImportText, textPgnImport);
            ChessUtil._SetText(pgnCloseText, textPgnClose);
        }

        // ---- Game texts ----

        // The game texts (_LoadGameTexts): the strings the status lines are written with, which the 2D view and the
        // history wall write with too, each holding its own copy. By index; the endings follow TextEndings, three to
        // an ending (White's win, Black's, no winner: _EndingText).
        public const int TextColorWhite = 0;
        public const int TextColorBlack = 1;
        public const int TextModeStandard = 2;
        public const int TextModeChess960 = 3;
        public const int TextUnknownName = 4;
        public const int TextBoardNumber = 5;
        public const int TextMovesOne = 6;
        public const int TextMovesMany = 7;
        public const int TextClockNone = 8;
        public const int TextClockMinutes = 9;
        public const int TextClockSeconds = 10;
        public const int TextPlayerLine = 11;
        public const int TextPlayerOpen = 12;
        public const int TextPlayerFlagFallen = 13;
        public const int TextStatusReset = 14;
        public const int TextStatusLobby = 15;
        public const int TextStatusLoaded = 16;
        public const int TextStatusLoadedChess960 = 17;
        public const int TextStatusTimeUp = 18;
        public const int TextStatusToMove = 19;
        public const int TextStatusToMoveInCheck = 20;
        public const int TextStatusPlayingOn = 21;
        public const int TextStatusDrawOffer = 22;
        public const int TextStatusTakebackToHere = 23;
        public const int TextStatusTakeback = 24;
        public const int TextStatusEnding = 25;
        public const int TextStatusUnfinishedGame = 26;
        public const int TextStatusDrawTimeForfeit = 27;
        public const int TextReplayCustom = 28;
        public const int TextReplayStart = 29;
        public const int TextReplayMove = 30;
        public const int TextEndings = 31;
        public const int GameTextCount = TextEndings + ChessGame.EndingCount * 3;

        // The language pack's keys of the game texts before TextEndings, by index.
        public static string[] _GameTextKeys() {
            return new string[] {
                "color_white", "color_black", "mode_standard", "mode_chess960", "unknown_name", "board_number",
                "moves_one", "moves_many", "clock_none", "clock_minutes", "clock_seconds", "player_line", "player_open",
                "player_flag_fallen", "status_reset", "status_lobby", "status_loaded", "status_loaded_chess960",
                "status_time_up", "status_to_move", "status_to_move_in_check", "status_playing_on", "status_draw_offer",
                "status_takeback_to_here", "status_takeback", "status_ending", "status_unfinished_game",
                "status_draw_time_forfeit", "replay_custom", "replay_start", "replay_move",
            };
        }

        // The game texts in i18n's language now (each its key with no I18n), for a behaviour to keep in a field and
        // read again on a language change. Each ending's text is ChessGame.EndingKey's, read once however many endings
        // share it.
        public static string[] _LoadGameTexts(I18n i18n) {
            string[] texts = new string[GameTextCount];
            string[] keys = _GameTextKeys();
            for (int i = 0; i < keys.Length && i < TextEndings; i++) texts[i] = ChessUtil.Text(i18n, keys[i]);
            string[] endingKeys = new string[ChessGame.EndingCount * 3];
            for (int slot = 0; slot < endingKeys.Length; slot++) {
                int side = slot % 3;
                int winner = side == 0 ? ChessRules.White : side == 1 ? ChessRules.Black : ChessRules.NoColor;
                string key = ChessGame.EndingKey(slot / 3, winner);
                endingKeys[slot] = key;
                int same = 0;
                while (same < slot && endingKeys[same] != key) same++;
                texts[TextEndings + slot] = same < slot ? texts[TextEndings + same] : ChessUtil.Text(i18n, key);
            }
            return texts;
        }

        public static string _ColorText(string[] texts, int color) {
            return texts[color == ChessRules.White ? TextColorWhite : TextColorBlack];
        }

        public static string _ModeText(string[] texts, int mode) {
            return texts[mode == ChessGame.ModeChess960 ? TextModeChess960 : TextModeStandard];
        }

        // "Board 2" for board number 1.
        public static string _BoardText(string[] texts, int boardNumber) {
            return I18n.Format(texts[TextBoardNumber], new string[] { "number", (boardNumber + 1).ToString() });
        }

        // How a game ended (ChessGame.EndingKey): "White wins by checkmate", "Unfinished".
        public static string _EndingText(string[] texts, int ending, int winner) {
            if (ending < 0 || ending >= ChessGame.EndingCount) ending = ChessGame.EndingUnknown;
            int side = winner == ChessRules.White ? 0 : winner == ChessRules.Black ? 1 : 2;
            return texts[TextEndings + ending * 3 + side];
        }

        // "1 move", "12 moves".
        public static string _MovesText(string[] texts, int moves) {
            if (moves == 1) return texts[TextMovesOne];
            return I18n.Format(texts[TextMovesMany], new string[] { "count", moves.ToString() });
        }

        // A player's name, or "?" when there's none.
        public static string _NameText(string[] texts, string name) {
            return name != null && name.Length > 0 ? name : texts[TextUnknownName];
        }

        // ---- Sound effects (step 12.12) ----

        // Whether a change has a sound (ChessUtil._NoteSound): a reset, a start or continuation, a request, an ending,
        // a move or a replay step (for a game it ends).
        static bool MakesSound(int change) {
            return change == ChessBoard.ChangeReset || change == ChessBoard.ChangeStarted
                || change == ChessBoard.ChangeContinued || change == ChessBoard.ChangeRequest
                || change == ChessBoard.ChangeEnded || change == ChessBoard.ChangeMove
                || change == ChessBoard.ChangeReplayStep;
        }

        // Plays the sound of change, which Refresh has just brought the UI up to (ChangeNone for none). Any sound
        // change drops a game over still waiting.
        void PlayChangeSound(int change) {
            if (change == ChessBoard.ChangeNone) return;
            gameEndAt = -1;
            if (change == ChessBoard.ChangeReset) {
                PlaySound(resetSound);
            } else if (change == ChessBoard.ChangeStarted || change == ChessBoard.ChangeContinued) {
                PlaySound(gameStartSound);
            } else if (change == ChessBoard.ChangeRequest) {
                bool offered = board.drawOfferColor != ChessRules.NoColor && seenDrawOffer == ChessRules.NoColor;
                bool asked = board.takebackColor != ChessRules.NoColor && seenTakeback == ChessRules.NoColor;
                if (offered || asked) PlaySound(notificationSound);
            } else if (change == ChessBoard.ChangeEnded) {
                PlaySound(_IsFlagFall(board) ? timeOutSound : gameEndSound);
            } else if (_ShowsEndByMove(board)) {
                // The view plays the move's sound first.
                gameEndAt = Time.time + (Utilities.IsValid(checkSound) ? checkSound.length : 0);
            }
        }

        // Whether the board's game has just ended on time (told as ChangeEnded): ended by a time forfeit, or playing on
        // untimed after its flag fell (step 12.9).
        public static bool _IsFlagFall(ChessBoard board) {
            if (board.lifecycle == ChessBoard.LifecycleEnded) return board.ending == ChessGame.EndingTimeForfeit;
            return board.lifecycle == ChessBoard.LifecycleInGame && board.ClocksFromPreviousGame();
        }

        // Whether the position shown is the end of a game (ended or loaded) that its last move ended: checkmate,
        // stalemate, a draw the board makes by itself, the king captured. Not a game resigned, agreed drawn, claimed
        // drawn or lost on time after its last move.
        public static bool _ShowsEndByMove(ChessBoard board) {
            int lifecycle = board.lifecycle;
            if (lifecycle != ChessBoard.LifecycleEnded && lifecycle != ChessBoard.LifecycleLoaded) return false;
            if (board.moveCount == 0) return false;
            if (board.replayMode && (board.replayCustom || board.replayIndex != board.moveCount)) return false;
            int ending = board.ending;
            return ending == ChessGame.EndingCheckmate || ending == ChessGame.EndingStalemate
                || ending == ChessGame.EndingFivefoldRepetition || ending == ChessGame.EndingSeventyFiveMoveRule
                || ending == ChessGame.EndingInsufficientMaterial || ending == ChessGame.EndingKingCaptured;
        }

        // The time warning: timeWarningSound as the side to move's clock passes each of timeWarningSeconds during its
        // turn, at server time now, seen by sampling it on every status update (every StatusInterval while it runs). A
        // turn first seen with less time left (a late joiner, a takeback) warns only for what passes from then on.
        // Public for tests.
        public void _CheckTimeWarningAt(double now) {
            if (!Utilities.IsValid(board) || !board.IsClockRunning()) {
                warnColor = ChessRules.NoColor;
                return;
            }
            int color = board.SideToMove();
            int left = board._RemainingTimeAt(color, now);
            if (color == warnColor && board.turnStart == warnTurnStart && Time.time >= soundsFrom) {
                for (int i = 0; i < timeWarningSeconds.Length; i++) {
                    int at = timeWarningSeconds[i] * 1000;
                    if (warnLeft > at && left <= at) {
                        PlaySound(timeWarningSound);
                        break;
                    }
                }
            }
            warnColor = color;
            warnTurnStart = board.turnStart;
            warnLeft = left;
        }

        // Plays clip from audioSource (nothing for none) unless muted; lastSound and soundsPlayed follow it either way.
        void PlaySound(AudioClip clip) {
            if (!ChessUtil._PlaySound(clip, audioSource, muted)) return;
            lastSound = clip;
            soundsPlayed++;
        }

        // ---- Buttons (docs/player-input.md, Buttons; steps 9.4a and 9.4b) ----
        // Each button is shown only while its action applies to the local player (the board's Can... check), and
        // pressing it calls the board's method, which refuses whenever the check doesn't hold.

        // The UI Buttons' events, one per action.
        public void _PressJoinWhite() {
            _PressButton(ButtonJoinWhite);
        }

        public void _PressJoinBlack() {
            _PressButton(ButtonJoinBlack);
        }

        public void _PressMode() {
            _PressButton(ButtonMode);
        }

        public void _PressClock() {
            _PressButton(ButtonClock);
        }

        public void _PressStart() {
            _PressButton(ButtonStart);
        }

        public void _PressContinue() {
            _PressButton(ButtonContinue);
        }

        public void _PressReset() {
            _PressButton(ButtonReset);
        }

        public void _PressResignWhite() {
            _PressButton(ButtonResignWhite);
        }

        public void _PressResignBlack() {
            _PressButton(ButtonResignBlack);
        }

        public void _PressOfferDrawWhite() {
            _PressButton(ButtonOfferDrawWhite);
        }

        public void _PressOfferDrawBlack() {
            _PressButton(ButtonOfferDrawBlack);
        }

        public void _PressAcceptDrawWhite() {
            _PressButton(ButtonAcceptDrawWhite);
        }

        public void _PressAcceptDrawBlack() {
            _PressButton(ButtonAcceptDrawBlack);
        }

        public void _PressDeclineDrawWhite() {
            _PressButton(ButtonDeclineDrawWhite);
        }

        public void _PressDeclineDrawBlack() {
            _PressButton(ButtonDeclineDrawBlack);
        }

        public void _PressClaimDraw() {
            _PressButton(ButtonClaimDraw);
        }

        public void _PressEnterReplay() {
            _PressButton(ButtonEnterReplay);
        }

        public void _PressLeaveReplay() {
            _PressButton(ButtonLeaveReplay);
        }

        public void _PressStepFirst() {
            _PressButton(ButtonStepFirst);
        }

        public void _PressStepBack() {
            _PressButton(ButtonStepBack);
        }

        public void _PressStepForward() {
            _PressButton(ButtonStepForward);
        }

        public void _PressStepLast() {
            _PressButton(ButtonStepLast);
        }

        public void _PressRequestTakebackWhite() {
            _PressButton(ButtonRequestTakebackWhite);
        }

        public void _PressRequestTakebackBlack() {
            _PressButton(ButtonRequestTakebackBlack);
        }

        public void _PressAcceptTakebackWhite() {
            _PressButton(ButtonAcceptTakebackWhite);
        }

        public void _PressAcceptTakebackBlack() {
            _PressButton(ButtonAcceptTakebackBlack);
        }

        public void _PressDeclineTakebackWhite() {
            _PressButton(ButtonDeclineTakebackWhite);
        }

        public void _PressDeclineTakebackBlack() {
            _PressButton(ButtonDeclineTakebackBlack);
        }

        public void _PressLoadHere() {
            _PressButton(ButtonLoadHere);
        }

        public void _PressPgn() {
            _PressButton(ButtonPgn);
        }

        public void _PressLoadPgn() {
            _PressButton(ButtonLoadPgn);
        }

        public void _PressLeaveWhite() {
            _PressButton(ButtonLeaveWhite);
        }

        public void _PressLeaveBlack() {
            _PressButton(ButtonLeaveBlack);
        }

        // The PGN box's own buttons (step 11.2b).
        public void _PressImport() {
            PlaySound(clickSound);
            _ImportPasted();
        }

        public void _PressPgnClose() {
            PlaySound(clickSound);
            OpenPgnBox(false, false);
            _UpdateButtons();
        }

        // The colour the button for action acts for (ChessRules.White or Black), or ChessRules.NoColor for one that
        // acts for no colour (Claim draw is the side to move's).
        public static int _ButtonColor(int action) {
            if (action == ButtonJoinWhite || action == ButtonLeaveWhite || action == ButtonResignWhite
                || action == ButtonOfferDrawWhite || action == ButtonAcceptDrawWhite || action == ButtonDeclineDrawWhite
                || action == ButtonRequestTakebackWhite || action == ButtonAcceptTakebackWhite
                || action == ButtonDeclineTakebackWhite) {
                return ChessRules.White;
            }
            if (action == ButtonJoinBlack || action == ButtonLeaveBlack || action == ButtonResignBlack
                || action == ButtonOfferDrawBlack || action == ButtonAcceptDrawBlack || action == ButtonDeclineDrawBlack
                || action == ButtonRequestTakebackBlack || action == ButtonAcceptTakebackBlack
                || action == ButtonDeclineTakebackBlack) {
                return ChessRules.Black;
            }
            return ChessRules.NoColor;
        }

        // The ply a step button for action steps the replay to: the start, one back, one on, or the game's current
        // position. -1 for any other action (and for one back from the start).
        static bool IsStepButton(int action) {
            return action == ButtonStepFirst || action == ButtonStepBack || action == ButtonStepForward
                || action == ButtonStepLast;
        }

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
        // step the replay to _StepTarget; the others call the board's method for their colour. Plays the click sound
        // here (step 12.12), and the change's own sound follows on the next Refresh; not for the step buttons, whose
        // step plays the shown position's move sound instead.
        public int _PressButton(int action) {
            if (!IsStepButton(action)) PlaySound(clickSound);
            if (!Utilities.IsValid(board)) return ChessBoard.InputRejected;
            int result = ChessBoard.InputRejected;
            int color = _ButtonColor(action);
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
            } else if (action == ButtonLeaveWhite || action == ButtonLeaveBlack) {
                result = board.LeaveSeat(color);
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
                // The board clears the pick once loaded. A game it has to import (a personal game from an earlier
                // session) shows its progress and failure as a pasted one's: set first, since the board tells its
                // listeners the import started before it returns.
                importWatched = true;
                result = board.LoadSelectedGame();
                if (board.importStatus != ChessBoard.ImportRunning) importWatched = false;
            } else if (action == ButtonPgn) {
                // Local to this player: nothing on the board changes.
                if (_ButtonApplies(ButtonPgn)) {
                    OpenPgnBox(!pgnShown || pgnPasting, false);
                    result = ChessBoard.InputAccepted;
                }
            } else if (action == ButtonLoadPgn) {
                // Local too: the board changes only once an import has finished.
                if (_ButtonApplies(ButtonLoadPgn)) {
                    OpenPgnBox(!pgnShown || !pgnPasting, true);
                    result = ChessBoard.InputAccepted;
                }
            }
            // A refusal tells nobody, so the buttons are brought up to date here too.
            _UpdateButtons();
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
        public bool _ButtonApplies(int action) {
            if (!Utilities.IsValid(board)) return false;
            if (action == ButtonJoinWhite) return board.CanJoin(ChessRules.White);
            if (action == ButtonJoinBlack) return board.CanJoin(ChessRules.Black);
            if (action == ButtonLeaveWhite || action == ButtonLeaveBlack) {
                return board.CanLeaveSeat(_ButtonColor(action));
            }
            if (action == ButtonMode) return board.CanSetMode();
            if (action == ButtonClock) return board.CanSetClock();
            if (action == ButtonStart) return board.CanStartGame();
            if (action == ButtonContinue) return board.CanContinue();
            if (action == ButtonReset) return board.CanReset();
            if (action == ButtonLoadHere) {
                return board.CanLoadGame() && Utilities.IsValid(board.arbiter) && board.arbiter.HasSelection();
            }
            if (action == ButtonPgn) return board.moveCount > 0;
            if (action == ButtonLoadPgn) return board.CanImport();
            int color = _ButtonColor(action);
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
        // claimed. The step buttons' arrows are signs, the same in every language.
        public string _ButtonText(int action) {
            string[] texts = _GameTexts();
            if (action == ButtonJoinWhite) return textJoinWhite;
            if (action == ButtonJoinBlack) return textJoinBlack;
            if (action == ButtonLeaveWhite || action == ButtonLeaveBlack) return textLeave;
            if (action == ButtonMode) {
                int mode = Utilities.IsValid(board) ? board.mode : ChessGame.ModeStandard;
                return I18n.Format(textMode, new string[] { "mode", _ModeText(texts, mode) });
            }
            if (action == ButtonClock) {
                if (!Utilities.IsValid(board)) return textClockNone;
                return I18n.Format(textClock, new string[] {
                    "clock", _ClockTextFor(texts, board.baseTime, board.increment) });
            }
            if (action == ButtonStart) return textStart;
            if (action == ButtonContinue) return textContinue;
            if (action == ButtonReset) return textReset;
            if (action == ButtonResignWhite || action == ButtonResignBlack) return textResign;
            if (action == ButtonOfferDrawWhite || action == ButtonOfferDrawBlack) return textOfferDraw;
            if (action == ButtonAcceptDrawWhite || action == ButtonAcceptDrawBlack) return textAcceptDraw;
            if (action == ButtonDeclineDrawWhite || action == ButtonDeclineDrawBlack) return textDeclineDraw;
            if (action == ButtonClaimDraw) {
                int claim = Utilities.IsValid(board) ? board.ClaimableDraw() : ChessGame.EndingNone;
                if (claim == ChessGame.EndingThreefoldRepetition) return textClaimDrawThreefold;
                if (claim == ChessGame.EndingFiftyMoveRule) return textClaimDrawFiftyMoves;
                return textClaimDraw;
            }
            if (action == ButtonEnterReplay) return textReplay;
            if (action == ButtonLeaveReplay) return textBackToGame;
            if (action == ButtonStepFirst) return "<<";
            if (action == ButtonStepBack) return "<";
            if (action == ButtonStepForward) return ">";
            if (action == ButtonStepLast) return ">>";
            if (action == ButtonRequestTakebackWhite || action == ButtonRequestTakebackBlack) return textTakeBackToHere;
            if (action == ButtonAcceptTakebackWhite || action == ButtonAcceptTakebackBlack) return textAcceptTakeback;
            if (action == ButtonDeclineTakebackWhite || action == ButtonDeclineTakebackBlack) return textDeclineTakeback;
            if (action == ButtonLoadHere) return textLoadHere;
            if (action == ButtonPgn) return pgnShown && !pgnPasting ? textHidePgn : textPgn;
            if (action == ButtonLoadPgn) return pgnShown && pgnPasting ? textHidePgn : textLoadPgn;
            return "";
        }

        // A time control as players say it: "No clock", "5+3" (minutes plus seconds), or "45s+0" for a base that
        // isn't whole minutes.
        public string _ClockText(int baseSeconds, int incrementSeconds) {
            return _ClockTextFor(_GameTexts(), baseSeconds, incrementSeconds);
        }

        public static string _ClockTextFor(string[] texts, int baseSeconds, int incrementSeconds) {
            if (baseSeconds == ChessArbiter.NoClock) return texts[TextClockNone];
            string increment = incrementSeconds.ToString();
            if (baseSeconds % 60 == 0) {
                return I18n.Format(texts[TextClockMinutes], new string[] {
                    "minutes", (baseSeconds / 60).ToString(), "increment", increment });
            }
            return I18n.Format(texts[TextClockSeconds], new string[] {
                "seconds", baseSeconds.ToString(), "increment", increment });
        }

        // Shows each button whose action applies to the local player and hides the rest, with its label. Called on
        // every Refresh, once a second and after every press.
        public void _UpdateButtons() {
            nextButtonCheck = Time.time + ButtonCheckInterval;
            for (int i = 0; i < buttons.Length; i++) {
                GameObject button = buttons[i];
                if (!Utilities.IsValid(button) || i >= buttonActions.Length) continue;
                int action = buttonActions[i];
                bool shown = _ButtonApplies(action);
                if (button.activeSelf != shown) button.SetActive(shown);
                if (!shown || i >= buttonLabels.Length || !Utilities.IsValid(buttonLabels[i])) continue;
                string text = _ButtonText(action);
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
        public void _UpdatePgnBox() {
            if (!Utilities.IsValid(board)) return;
            if (pgnShown && !pgnPasting && !_ButtonApplies(ButtonPgn)) pgnShown = false;
            if (pgnShown && pgnPasting && !board.CanImport() && _ImportText().Length == 0) pgnShown = false;
            if (!pgnShown) pgnPasting = false;
            if (pgnShown && !pgnPasting && Utilities.IsValid(pgnField)) {
                string pgn = board.GamePgn();
                if (pgnField.text != pgn) pgnField.text = pgn;
            }
            if (pgnPasting) {
                string note = _ImportText();
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
            if (open) _PlacePgnBox(_LocalEnd());
            if (forget) {
                pasteFirstNoted = false;
                if (!Utilities.IsValid(board) || board.importStatus != ChessBoard.ImportRunning) importWatched = false;
            }
            _UpdatePgnBox();
        }

        // The Import button (step 11.2b): starts importing the text pasted into the box onto the board
        // (ChessBoard.StartImport), which loads it once it has been read over the next frames. Refused with the box
        // closed, with nothing pasted (the box says so) and where the board refuses. Public for tests.
        public int _ImportPasted() {
            if (!Utilities.IsValid(board) || !pgnShown || !pgnPasting || !Utilities.IsValid(pgnPasteField)) {
                return ChessBoard.InputRejected;
            }
            string text = pgnPasteField.text;
            int result = ChessBoard.InputRejected;
            if (text == null || text.Trim().Length == 0) {
                pasteFirstNoted = true;
            } else {
                pasteFirstNoted = false;
                // Set first: the board tells its listeners the import started before it returns.
                importWatched = true;
                result = board.StartImport(text);
                if (result != ChessBoard.InputAccepted) importWatched = false;
            }
            _UpdatePgnBox();
            _UpdateButtons();
            return result;
        }

        // The paste part's line: the box's own note, or how the import started from it is going: the moves read so
        // far, why it failed ("Import failed at move 2.: illegal move", from the board's codes) or that it was
        // abandoned; "" otherwise.
        public string _ImportText() {
            string[] texts = _GameTexts();
            if (pasteFirstNoted) return textImportPasteFirst;
            if (!importWatched || !Utilities.IsValid(board)) return "";
            int status = board.importStatus;
            if (status == ChessBoard.ImportRunning) {
                int read = Utilities.IsValid(board.importer) ? board.importer.MoveCount() : 0;
                return I18n.Format(textImportRunning, new string[] { "moves", _MovesText(texts, read) });
            }
            if (status == ChessBoard.ImportFailed) {
                if (board.importResult == ChessPgn.ImportDone) return textImportFailedLoad;
                return I18n.Format(textImportFailed, new string[] {
                    "move", board.importFailedAt, "reason", ImportErrorText(board.importResult) });
            }
            if (status == ChessBoard.ImportAbandoned) return textImportAbandoned;
            return "";
        }

        // Why an import failed (a ChessPgn.Import* result), for players: "illegal move".
        string ImportErrorText(int result) {
            if (result == ChessPgn.ImportBadSyntax) return textImportErrorSyntax;
            if (result == ChessPgn.ImportIllegalMove) return textImportErrorIllegalMove;
            if (result == ChessPgn.ImportAmbiguousMove) return textImportErrorAmbiguousMove;
            if (result == ChessPgn.ImportBadFen) return textImportErrorFen;
            return textImportErrorTooLong;
        }

        // Stands the PGN box over the far edge of the board from color's end of the table, facing that end (unturned it
        // faces -z, White's end), at the height and distance the prefab gives it.
        public void _PlacePgnBox(int color) {
            if (!Utilities.IsValid(pgnBox)) return;
            Transform box = pgnBox.transform;
            Vector3 position = box.localPosition;
            position.x = 0;
            position.z = Mathf.Abs(position.z) * (color == ChessRules.White ? 1 : -1);
            box.localPosition = position;
            box.localRotation = color == ChessRules.White ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
        }

        // The end of the table the local player stands at: White's (-z) or Black's.
        public int _LocalEnd() {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return ChessRules.White;
            return transform.InverseTransformPoint(local.GetPosition()).z <= 0 ? ChessRules.White : ChessRules.Black;
        }

        // ---- Status display (step 9.5c) ----

        // Brings every status panel up to the board at the server time now, and schedules the next update: soon while
        // a clock runs (the side to move's time counts down, and its flag can fall), once a second otherwise.
        public void _UpdateStatus() {
            bool valid = Utilities.IsValid(board);
            nextStatusUpdate = Time.time + (valid && board.IsClockRunning() ? StatusInterval : 1);
            double now = valid ? board._ServerTime() : 0;
            _CheckTimeWarningAt(now);
            string players = _PlayersTextAt(now);
            string status = _StatusTextAt(now);
            for (int i = 0; i < statusPlayerLabels.Length; i++) {
                TextMeshProUGUI label = statusPlayerLabels[i];
                if (Utilities.IsValid(label) && label.text != players) label.text = players;
            }
            for (int i = 0; i < statusLabels.Length; i++) {
                TextMeshProUGUI label = statusLabels[i];
                if (Utilities.IsValid(label) && label.text != status) label.text = status;
            }
        }

        // Both seats at server time now, White's line first (_PlayerTextAt). The status texts are static, taking the
        // board and the game texts to write them with (_GameTexts), so the 2D view (step 12.13) writes the same lines
        // with its own copy; the instance forms read this UI's board and texts.
        public string _PlayersTextAt(double now) {
            return _PlayersTextFor(board, now, _GameTexts());
        }

        public string _PlayerTextAt(int color, double now) {
            return _PlayerTextFor(board, color, now, _GameTexts());
        }

        public string _StatusTextAt(double now) {
            return _StatusTextFor(board, now, _GameTexts());
        }

        public string _EndingText() {
            return _EndingTextFor(board, _GameTexts());
        }

        public string _ReplayText() {
            return _ReplayTextFor(board, _GameTexts());
        }

        public static string _PlayersTextFor(ChessBoard board, double now, string[] texts) {
            return _PlayerTextFor(board, ChessRules.White, now, texts) + "\n"
                + _PlayerTextFor(board, ChessRules.Black, now, texts);
        }

        // A seat's line: "White: Ana", "(open)" for an empty seat, with the side's remaining time at server time now
        // during and after a game with a clock ("White: Ana  4:58"). In a game played on untimed after a flag fall
        // (step 12.9) the times are the timed game's, and the side out of time shows GG instead of 0:00.
        public static string _PlayerTextFor(ChessBoard board, int color, double now, string[] texts) {
            if (!Utilities.IsValid(board)) return "";
            string name = color == ChessRules.White ? board.whitePlayer : board.blackPlayer;
            if (name == null || name.Length == 0) name = texts[TextPlayerOpen];
            string text = I18n.Format(texts[TextPlayerLine], new string[] {
                "color", _ColorText(texts, color), "name", name });
            bool played = board.lifecycle == ChessBoard.LifecycleInGame || board.lifecycle == ChessBoard.LifecycleEnded;
            if (!played) return text;
            int left = board._RemainingTimeAt(color, now);
            if (board.ClocksFromPreviousGame()) {
                text += "  " + (left == 0 ? texts[TextPlayerFlagFallen] : _ClockDisplay(left));
            } else if (board.HasClock()) {
                text += "  " + _ClockDisplay(left);
            }
            return text;
        }

        // Which side's time ran out before the game played on untimed (step 12.9): the one with no time left, NoColor
        // if the game isn't playing on.
        static int PlayOnFlaggedColor(ChessBoard board) {
            if (!board.ClocksFromPreviousGame()) return ChessRules.NoColor;
            if (board.whiteTime == 0) return ChessRules.White;
            return board.blackTime == 0 ? ChessRules.Black : ChessRules.NoColor;
        }

        // A game text (by index) with ${color} filled in with a side's name: "White to move".
        static string ColorLine(string[] texts, int index, int color) {
            return I18n.Format(texts[index], new string[] { "color", _ColorText(texts, color) });
        }

        // The board's state at server time now, a line each: a reset board's invitation; the lobby's mode and clock; a
        // loaded game's names (and mode, for Chess960); in a game, whose turn it is and whether they're in check, or
        // that their time is up, and, playing on untimed after a flag fall, whose time ran out (12.9); the result of an
        // ended or loaded game (_EndingText); a pending draw offer and takeback request; the replay position
        // (_ReplayText).
        public static string _StatusTextFor(ChessBoard board, double now, string[] texts) {
            string text = _GameStatusTextFor(board, now, texts);
            if (!Utilities.IsValid(board)) return text;
            if (board.takebackColor != ChessRules.NoColor) {
                text = ChessUtil._AddLine(text, ColorLine(texts, TextStatusTakebackToHere, board.takebackColor));
            }
            if (board.replayMode) text = ChessUtil._AddLine(text, _ReplayTextFor(board, texts));
            return text;
        }

        // _StatusTextFor without the lines about the board's replay position (the takeback request, which is for the
        // ply shown, and the replay line), for the 2D view (step 12.14), whose replay is its own.
        public static string _GameStatusTextFor(ChessBoard board, double now, string[] texts) {
            if (!Utilities.IsValid(board)) return "";
            int lifecycle = board.lifecycle;
            if (lifecycle == ChessBoard.LifecycleReset) return texts[TextStatusReset];
            string text = "";
            if (lifecycle == ChessBoard.LifecycleLobby) {
                text = I18n.Format(texts[TextStatusLobby], new string[] {
                    "mode", _ModeText(texts, board.mode), "clock", _ClockTextFor(texts, board.baseTime, board.increment) });
            } else if (lifecycle == ChessBoard.LifecycleLoaded) {
                int index = board.mode == ChessGame.ModeChess960 ? TextStatusLoadedChess960 : TextStatusLoaded;
                text = I18n.Format(texts[index], new string[] {
                    "white", _NameText(texts, board.replaySourceWhite), "black", _NameText(texts, board.replaySourceBlack) });
            } else if (lifecycle == ChessBoard.LifecycleInGame) {
                int flagged = board._FlaggedColorAt(now);
                if (flagged != ChessRules.NoColor) {
                    text = ColorLine(texts, TextStatusTimeUp, flagged);
                } else {
                    text = ColorLine(texts, board.IsInCheck() ? TextStatusToMoveInCheck : TextStatusToMove,
                        board.SideToMove());
                }
                int playOnFlagged = PlayOnFlaggedColor(board);
                if (playOnFlagged != ChessRules.NoColor) {
                    text = ChessUtil._AddLine(text, ColorLine(texts, TextStatusPlayingOn, playOnFlagged));
                }
            }
            text = ChessUtil._AddLine(text, _EndingTextFor(board, texts));
            if (board.drawOfferColor != ChessRules.NoColor) {
                text = ChessUtil._AddLine(text, ColorLine(texts, TextStatusDrawOffer, board.drawOfferColor));
            }
            return text;
        }

        // How an ended or loaded game ended, with its PGN result: "White wins by checkmate (1-0)", "Draw by agreement
        // (1/2-1/2)"; "Unfinished game (*)" for a loaded game without a result; "" otherwise.
        public static string _EndingTextFor(ChessBoard board, string[] texts) {
            if (!Utilities.IsValid(board)) return "";
            int lifecycle = board.lifecycle;
            if (lifecycle != ChessBoard.LifecycleEnded && lifecycle != ChessBoard.LifecycleLoaded) return "";
            int ending = board.ending;
            int winner = board.winner;
            if (ending == ChessGame.EndingNone || ending == ChessGame.EndingUnterminated) {
                return lifecycle == ChessBoard.LifecycleLoaded ? texts[TextStatusUnfinishedGame] : "";
            }
            string result = board.ResultText();
            if (ending == ChessGame.EndingTimeForfeit && winner == ChessRules.NoColor) {
                // A time forfeit drawn: the side out of time is the one that was to move, and its opponent couldn't
                // mate.
                int flagged = board.SideToMove();
                return I18n.Format(texts[TextStatusDrawTimeForfeit], new string[] {
                    "flagged", _ColorText(texts, flagged), "other", _ColorText(texts, ChessRules.OtherColor(flagged)),
                    "result", result });
            }
            return I18n.Format(texts[TextStatusEnding], new string[] {
                "ending", _EndingText(texts, ending, winner), "result", result });
        }

        // The replay position: "Replay: 2. Nf3 (3 of 4)" (the move that led to it, numbered as PGN numbers it, "?"
        // while its notation is missing), "Replay: start (0 of 4)", or "Replay: custom position" after free moves.
        public static string _ReplayTextFor(ChessBoard board, string[] texts) {
            if (board.replayCustom) return texts[TextReplayCustom];
            return _PlyTextFor(board, board.replayIndex, texts);
        }

        // The game's position before move ply as the replay line names it: "Replay: 2. Nf3 (3 of 4)", "Replay: start
        // (0 of 4)". For the board's replay (_ReplayTextFor) and the 2D view's own (step 12.14).
        public static string _PlyTextFor(ChessBoard board, int ply, string[] texts) {
            string plyText = ply.ToString();
            string count = board.moveCount.ToString();
            if (ply <= 0 || ply > board.moveCount) {
                return I18n.Format(texts[TextReplayStart], new string[] { "ply", plyText, "count", count });
            }
            // The side that made the first move: the side to move now, or the other after an odd number of moves.
            int firstSide = board.SideToMove();
            if (board.moveCount % 2 == 1) firstSide = ChessRules.OtherColor(firstSide);
            string san = board.notation[ply - 1];
            if (san == null || san.Length == 0) san = texts[TextUnknownName];
            return I18n.Format(texts[TextReplayMove], new string[] {
                "move", ChessPgn._MoveLabel(board.startMoveNumber, firstSide, ply - 1), "san", san, "ply", plyText,
                "count", count });
        }

        // A clock as players read it: "4:58", "1:02:05" from an hour, tenths under ten seconds ("0:09.8").
        public static string _ClockDisplay(int milliseconds) {
            if (milliseconds < 0) milliseconds = 0;
            int seconds = milliseconds / 1000;
            int minutes = seconds / 60;
            if (minutes >= 60) {
                return (minutes / 60) + ":" + ChessUtil._TwoDigits(minutes % 60) + ":"
                    + ChessUtil._TwoDigits(seconds % 60);
            }
            string text = minutes + ":" + ChessUtil._TwoDigits(seconds % 60);
            if (milliseconds < 10000) text += "." + (milliseconds / 100 % 10);
            return text;
        }
    }
}
