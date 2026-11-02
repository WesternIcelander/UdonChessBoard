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
    // the Chess Board UI prefab (step 14.4, docs/prefabs.md), its board set on the instance; the Chess Table prefab
    // (step 14.9) nests it with its board and view, already on that board.
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
        string textJoin = "";
        string textLeave = "";
        string textMode = "";
        string textClockCaption = "";
        string textStart = "";
        string textContinue = "";
        string textReset = "";
        string textResign = "";
        string textSure = "";
        string textDraw = "";
        string textDrawNo = "";
        string textDeclineDraw = "";
        string textReplay = "";
        string textBackToGame = "";
        string textTakeBackToHere = "";
        string textAcceptTakeback = "";
        string textDeclineTakeback = "";
        string textLoadHere = "";
        string textPgn = "";
        string textLoadPgn = "";
        string textHidePgn = "";
        string textImHere = "";
        string textResetWarningTitle = "";
        string textResetWarningAuto = "";
        string textResetWarningOthersAuto = "";
        string textResetWarningOthers = "";
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
        // The board's buttons (docs/player-input.md, Buttons), by what they do. The prefab's panels at each end of the
        // table act for their own colour (_ButtonColor). Several of them merge what the board has as separate actions:
        // Join/Start, Draw (claim, accept or offer), Takeback (continue, accept or request) and Back (decline a
        // takeback or leave replay, and on a loaded board, which has no game to go back to, join or leave the seat).
        public const int ButtonJoinStartWhite = 0;
        public const int ButtonJoinStartBlack = 1;
        public const int ButtonLeaveWhite = 2;
        public const int ButtonLeaveBlack = 3;
        public const int ButtonLoadPgn = 4;
        public const int ButtonLoadHere = 5;
        public const int ButtonMode = 6;
        public const int ButtonClockMore = 7;
        public const int ButtonClockLess = 8;
        public const int ButtonIncrementMore = 9;
        public const int ButtonIncrementLess = 10;
        public const int ButtonReset = 11;
        public const int ButtonResignWhite = 12;
        public const int ButtonResignBlack = 13;
        public const int ButtonDrawWhite = 14;
        public const int ButtonDrawBlack = 15;
        public const int ButtonDeclineDrawWhite = 16;
        public const int ButtonDeclineDrawBlack = 17;
        public const int ButtonEnterReplay = 18;
        public const int ButtonPgn = 19;
        public const int ButtonStepFirst = 20;
        public const int ButtonStepBack = 21;
        public const int ButtonStepForward = 22;
        public const int ButtonStepLast = 23;
        public const int ButtonTakebackWhite = 24;
        public const int ButtonTakebackBlack = 25;
        public const int ButtonBackWhite = 26;
        public const int ButtonBackBlack = 27;
        public const int ButtonImHere = 28;
        public const int ButtonCount = 29;
        // The button objects in the world, with the Button* action of each and its label at the same index. A button's
        // UI Button calls the UI's _Press... event for its action.
        public GameObject[] buttons = new GameObject[0];
        public int[] buttonActions = new int[0];
        public TextMeshProUGUI[] buttonLabels = new TextMeshProUGUI[0];
        // The panels holding them, shown by the board's state (_ModeSetupShown and the like); any number of each.
        public GameObject[] modeSetupPanels = new GameObject[0];
        public GameObject[] resetPanels = new GameObject[0];
        public GameObject[] preGamePanels = new GameObject[0];
        public GameObject[] inGamePanels = new GameObject[0];
        public GameObject[] replayPanels = new GameObject[0];
        // Mode Setup's clock part beside its buttons: what's hidden while only the mode can be chosen, the caption, and
        // the time control chosen.
        public GameObject[] clockSetupParts = new GameObject[0];
        public TextMeshProUGUI[] clockCaptionLabels = new TextMeshProUGUI[0];
        public TextMeshProUGUI[] clockValueLabels = new TextMeshProUGUI[0];
        // The replay position on the Replay Controls.
        public TextMeshProUGUI[] replayStatusLabels = new TextMeshProUGUI[0];
        [Tooltip("The base times the clock's + and - buttons step through, in seconds, shortest first. Below the "
            + "shortest is no clock.")]
        public int[] clockBaseSteps = { 60, 120, 180, 300, 600, 900, 1200, 1800, 2700, 3600, 5400, 7200 };
        [Tooltip("The increments the clock's ++ and -- buttons step through, in seconds, smallest first.")]
        public int[] clockIncrementSteps = { 0, 1, 2, 3, 5, 10, 15, 20, 30, 45, 60 };
        // The warning the seated players and the loader get before their idle board can be reset (docs/inactivity.md), with its I'm
        // here! button among the buttons above. It faces White's end as the prefab places it, and is turned about the
        // UI's origin to face Black's end for a player only in the black seat.
        public GameObject resetWarning;
        public TextMeshProUGUI resetWarningTitle;
        public TextMeshProUGUI resetWarningText;
        [Tooltip("Seconds before the board resets itself to warn, when nobody can reset it before then.")]
        public int resetWarningAutoSeconds = 30;
        [Tooltip("Seconds before anyone can reset the board to warn.")]
        public int resetWarningOthersSeconds = 15;
        // What the warning says (_ResetWarningKindAt): nothing, the board will reset itself, others may reset it and it
        // will reset itself, others may reset it (it never resets itself).
        public const int ResetWarningNone = 0;
        public const int ResetWarningAuto = 1;
        public const int ResetWarningOthersAuto = 2;
        public const int ResetWarningOthers = 3;
        bool resetWarningPlaced;
        Vector3 resetWarningPosition;
        Quaternion resetWarningRotation;
        // Until when (Time.time) a button's first press holds: Resign and Reset ask "Sure?" and act on a second press
        // within ConfirmSeconds, and Draw says "No!" for DrawNoSeconds where nothing can be done.
        [NonSerialized] public float[] armedUntil = new float[ButtonCount];
        public const float ConfirmSeconds = 3;
        public const float DrawNoSeconds = 1;
        // The status display (step 9.5c): any number of panels (the prefab has one, beside the board), each with a name
        // and a clock text per seat (the clock during and after a game) and a status text (whose turn, check, the
        // result, pending offers and requests, a loaded game's names, the replay position). Brought up to date on every
        // Refresh, every StatusInterval while a clock runs and once a second otherwise.
        public TextMeshProUGUI[] whiteClockLabels = new TextMeshProUGUI[0];
        public TextMeshProUGUI[] whiteNameLabels = new TextMeshProUGUI[0];
        public TextMeshProUGUI[] blackClockLabels = new TextMeshProUGUI[0];
        public TextMeshProUGUI[] blackNameLabels = new TextMeshProUGUI[0];
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
        // The label's alpha on a step button shown but not pressable (the UI Button's own tint greys its image).
        const float DisabledLabelAlpha = 0.5f;
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
        // here at once, and a settings change brings the buttons and the PGN box up to date (a game in progress takes
        // the arbiter's PGN Event and Site as they change, step 14.13).
        public override void OnArbiterChanged(ChessBoard changed, int change) {
            if (change == ChessArbiter.ChangeSettings) _UpdatePgnBox();
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
            textJoin = ChessUtil.Text(i18n, "button_join");
            textLeave = ChessUtil.Text(i18n, "button_leave");
            textMode = ChessUtil.Text(i18n, "button_mode");
            textClockCaption = ChessUtil.Text(i18n, "clock_caption");
            textStart = ChessUtil.Text(i18n, "button_start");
            textContinue = ChessUtil.Text(i18n, "button_continue");
            textReset = ChessUtil.Text(i18n, "button_reset");
            textResign = ChessUtil.Text(i18n, "button_resign");
            textSure = ChessUtil.Text(i18n, "button_sure");
            textDraw = ChessUtil.Text(i18n, "button_draw");
            textDrawNo = ChessUtil.Text(i18n, "button_draw_no");
            textDeclineDraw = ChessUtil.Text(i18n, "button_decline_draw");
            textReplay = ChessUtil.Text(i18n, "button_replay");
            textBackToGame = ChessUtil.Text(i18n, "button_back_to_game");
            textTakeBackToHere = ChessUtil.Text(i18n, "button_take_back_to_here");
            textAcceptTakeback = ChessUtil.Text(i18n, "button_accept_takeback");
            textDeclineTakeback = ChessUtil.Text(i18n, "button_decline_takeback");
            textLoadHere = ChessUtil.Text(i18n, "button_load_here");
            textPgn = ChessUtil.Text(i18n, "button_pgn");
            textLoadPgn = ChessUtil.Text(i18n, "button_load_pgn");
            textHidePgn = ChessUtil.Text(i18n, "button_hide_pgn");
            textImHere = ChessUtil.Text(i18n, "button_im_here");
            textResetWarningTitle = ChessUtil.Text(i18n, "reset_warning_title");
            textResetWarningAuto = ChessUtil.Text(i18n, "reset_warning_auto");
            textResetWarningOthersAuto = ChessUtil.Text(i18n, "reset_warning_others_auto");
            textResetWarningOthers = ChessUtil.Text(i18n, "reset_warning_others");
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
            ChessUtil._SetText(resetWarningTitle, textResetWarningTitle);
            SetLabels(clockCaptionLabels, textClockCaption);
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
        public const int TextReplayPanelCustom = 31;
        public const int TextReplayPanelStart = 32;
        public const int TextReplayPanelMove = 33;
        public const int TextEndings = 34;
        public const int GameTextCount = TextEndings + ChessGame.EndingCount * 3;

        // The language pack's keys of the game texts before TextEndings, by index.
        public static string[] _GameTextKeys() {
            return new string[] {
                "color_white", "color_black", "mode_standard", "mode_chess960", "unknown_name", "board_number",
                "moves_one", "moves_many", "clock_none", "clock_minutes", "clock_seconds", "player_line", "player_open",
                "player_flag_fallen", "status_reset", "status_lobby", "status_loaded", "status_loaded_chess960",
                "status_time_up", "status_to_move", "status_to_move_in_check", "status_playing_on", "status_draw_offer",
                "status_takeback_to_here", "status_takeback", "status_ending", "status_unfinished_game",
                "status_draw_time_forfeit", "replay_custom", "replay_start", "replay_move", "replay_panel_custom",
                "replay_panel_start", "replay_panel_move",
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
        public void _PressJoinStartWhite() {
            _PressButton(ButtonJoinStartWhite);
        }

        public void _PressJoinStartBlack() {
            _PressButton(ButtonJoinStartBlack);
        }

        public void _PressLeaveWhite() {
            _PressButton(ButtonLeaveWhite);
        }

        public void _PressLeaveBlack() {
            _PressButton(ButtonLeaveBlack);
        }

        public void _PressLoadPgn() {
            _PressButton(ButtonLoadPgn);
        }

        public void _PressLoadHere() {
            _PressButton(ButtonLoadHere);
        }

        public void _PressMode() {
            _PressButton(ButtonMode);
        }

        public void _PressClockMore() {
            _PressButton(ButtonClockMore);
        }

        public void _PressClockLess() {
            _PressButton(ButtonClockLess);
        }

        public void _PressIncrementMore() {
            _PressButton(ButtonIncrementMore);
        }

        public void _PressIncrementLess() {
            _PressButton(ButtonIncrementLess);
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

        public void _PressDrawWhite() {
            _PressButton(ButtonDrawWhite);
        }

        public void _PressDrawBlack() {
            _PressButton(ButtonDrawBlack);
        }

        public void _PressDeclineDrawWhite() {
            _PressButton(ButtonDeclineDrawWhite);
        }

        public void _PressDeclineDrawBlack() {
            _PressButton(ButtonDeclineDrawBlack);
        }

        public void _PressEnterReplay() {
            _PressButton(ButtonEnterReplay);
        }

        public void _PressPgn() {
            _PressButton(ButtonPgn);
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

        public void _PressTakebackWhite() {
            _PressButton(ButtonTakebackWhite);
        }

        public void _PressTakebackBlack() {
            _PressButton(ButtonTakebackBlack);
        }

        public void _PressBackWhite() {
            _PressButton(ButtonBackWhite);
        }

        public void _PressBackBlack() {
            _PressButton(ButtonBackBlack);
        }

        public void _PressImHere() {
            _PressButton(ButtonImHere);
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
        // acts for no colour.
        public static int _ButtonColor(int action) {
            if (action == ButtonJoinStartWhite || action == ButtonLeaveWhite || action == ButtonResignWhite
                || action == ButtonDrawWhite || action == ButtonDeclineDrawWhite || action == ButtonTakebackWhite
                || action == ButtonBackWhite) {
                return ChessRules.White;
            }
            if (action == ButtonJoinStartBlack || action == ButtonLeaveBlack || action == ButtonResignBlack
                || action == ButtonDrawBlack || action == ButtonDeclineDrawBlack || action == ButtonTakebackBlack
                || action == ButtonBackBlack) {
                return ChessRules.Black;
            }
            return ChessRules.NoColor;
        }

        static bool IsStepButton(int action) {
            return action == ButtonStepFirst || action == ButtonStepBack || action == ButtonStepForward
                || action == ButtonStepLast;
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

        // Whether action's first press only asks "Sure?".
        static bool NeedsConfirm(int action) {
            return action == ButtonReset || action == ButtonResignWhite || action == ButtonResignBlack;
        }

        // Whether a press of action is holding (armedUntil): for Resign and Reset that the next press acts, for Draw
        // that it's saying "No!".
        bool IsArmed(int action) {
            return armedUntil[action] > Time.time;
        }

        // Does what the button for action does, for the local player: the board's answer (InputRejected for an unknown
        // action, and for a first press of Resign or Reset, which only asks "Sure?"). Plays the click sound here (step
        // 12.12), and the change's own sound follows on the next Refresh; not for the step buttons, whose step plays
        // the shown position's move sound instead.
        public int _PressButton(int action) {
            if (!IsStepButton(action)) PlaySound(clickSound);
            if (!Utilities.IsValid(board)) return ChessBoard.InputRejected;
            if (NeedsConfirm(action) && _ButtonApplies(action) && !IsArmed(action)) {
                armedUntil[action] = Time.time + ConfirmSeconds;
                _UpdateButtons();
                return ChessBoard.InputRejected;
            }
            if (action >= 0 && action < ButtonCount) armedUntil[action] = 0;
            int result = ChessBoard.InputRejected;
            int color = _ButtonColor(action);
            int step = StepTarget(action);
            if (step >= 0) {
                result = board.StepTo(step);
            } else if (action == ButtonJoinStartWhite || action == ButtonJoinStartBlack) {
                // Hidden on a loaded board, which joins with the Back button and continues instead of starting.
                if (_ButtonApplies(action)) result = board.CanJoin(color) ? board.Join(color) : board.StartGame();
            } else if (action == ButtonLeaveWhite || action == ButtonLeaveBlack) {
                if (_ButtonApplies(action)) result = board.LeaveSeat(color);
            } else if (action == ButtonMode) {
                result = board.SetMode(board.mode == ChessGame.ModeChess960
                    ? ChessGame.ModeStandard : ChessGame.ModeChess960);
            } else if (action == ButtonClockMore || action == ButtonClockLess) {
                int nextBase = NextBase(action == ButtonClockMore);
                if (nextBase != NoStep) {
                    result = board.SetClock(nextBase, nextBase == ChessArbiter.NoClock
                        || board.baseTime == ChessArbiter.NoClock ? 0 : board.increment);
                }
            } else if (action == ButtonIncrementMore || action == ButtonIncrementLess) {
                int nextIncrement = NextIncrement(action == ButtonIncrementMore);
                if (nextIncrement != NoStep) result = board.SetClock(board.baseTime, nextIncrement);
            } else if (action == ButtonReset) {
                result = board.ResetBoard();
            } else if (action == ButtonResignWhite || action == ButtonResignBlack) {
                result = board.Resign(color);
            } else if (action == ButtonDrawWhite || action == ButtonDrawBlack) {
                result = PressDraw(action, color);
            } else if (action == ButtonDeclineDrawWhite || action == ButtonDeclineDrawBlack) {
                result = board.DeclineDraw(color);
            } else if (action == ButtonEnterReplay) {
                result = board.EnterReplay();
            } else if (action == ButtonTakebackWhite || action == ButtonTakebackBlack) {
                if (board.CanContinue()) result = board.ContinueGame();
                else if (board.CanAcceptTakeback(color)) result = board.AcceptTakeback(color);
                else result = board.RequestTakeback(color);
            } else if (action == ButtonBackWhite || action == ButtonBackBlack) {
                if (board.CanDeclineTakeback(color)) result = board.DeclineTakeback(color);
                else if (board.CanLeaveReplay()) result = board.LeaveReplay();
                else if (board.lifecycle == ChessBoard.LifecycleLoaded && board.CanJoin(color)) result = board.Join(color);
                else if (board.lifecycle == ChessBoard.LifecycleLoaded) result = board.LeaveSeat(color);
            } else if (action == ButtonImHere) {
                if (_ButtonApplies(action)) result = board.ConfirmPresence();
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

        // The Draw button: claims when a claim is there; otherwise accepts the other side's offer, or offers; and says
        // "No!" for a moment where it can do none of them.
        int PressDraw(int action, int color) {
            if (board.CanClaimDraw()) return board.ClaimDraw();
            if (board.CanAcceptDraw(color)) return board.AcceptDraw(color);
            if (board.CanOfferDraw(color)) return board.OfferDraw(color);
            if (_ButtonApplies(action)) armedUntil[action] = Time.time + DrawNoSeconds;
            return ChessBoard.InputRejected;
        }

        // No step to take on a clock button (ChessArbiter.NoClock, -1, is a value a step can land on).
        const int NoStep = -2;

        // The base time after the board's in clockBaseSteps, up or down: below the shortest is no clock, above no clock
        // the shortest. A time between two steps (a loaded game's) goes to the nearer one that way. NoStep at either
        // end.
        int NextBase(bool more) {
            int current = board.baseTime;
            if (more) {
                for (int i = 0; i < clockBaseSteps.Length; i++) {
                    if (current == ChessArbiter.NoClock || clockBaseSteps[i] > current) return clockBaseSteps[i];
                }
                return NoStep;
            }
            if (current == ChessArbiter.NoClock) return NoStep;
            for (int i = clockBaseSteps.Length - 1; i >= 0; i--) {
                if (clockBaseSteps[i] < current) return clockBaseSteps[i];
            }
            return ChessArbiter.NoClock;
        }

        // The increment after the board's in clockIncrementSteps, up or down; NoStep at either end and with no clock,
        // which has no increment.
        int NextIncrement(bool more) {
            if (board.baseTime == ChessArbiter.NoClock) return NoStep;
            int current = board.increment;
            if (more) {
                for (int i = 0; i < clockIncrementSteps.Length; i++) {
                    if (clockIncrementSteps[i] > current) return clockIncrementSteps[i];
                }
                return NoStep;
            }
            for (int i = clockIncrementSteps.Length - 1; i >= 0; i--) {
                if (clockIncrementSteps[i] < current) return clockIncrementSteps[i];
            }
            return NoStep;
        }

        // Whether the button for action applies to the local player now, so it's shown.
        public bool _ButtonApplies(int action) {
            if (!Utilities.IsValid(board)) return false;
            int color = _ButtonColor(action);
            int lifecycle = board.lifecycle;
            if (action == ButtonJoinStartWhite || action == ButtonJoinStartBlack) {
                if (lifecycle == ChessBoard.LifecycleLoaded) return false;
                return board.CanJoin(color)
                    || (lifecycle == ChessBoard.LifecycleLobby && board.IsPlayer(ChessUtil._LocalName("")));
            }
            if (action == ButtonLeaveWhite || action == ButtonLeaveBlack) {
                return lifecycle == ChessBoard.LifecycleLobby && board.CanLeaveSeat(color);
            }
            if (action == ButtonMode) return board.CanSetMode();
            if (action == ButtonClockMore || action == ButtonClockLess || action == ButtonIncrementMore
                || action == ButtonIncrementLess) {
                return board.CanSetClock();
            }
            if (action == ButtonReset) return board.CanReset();
            if (action == ButtonLoadHere) {
                return board.CanLoadGame() && Utilities.IsValid(board.arbiter) && board.arbiter.HasSelection();
            }
            if (action == ButtonPgn) return board.moveCount > 0;
            if (action == ButtonLoadPgn) return board.CanImport();
            if (IsStepButton(action)) return board.CanControlReplay();
            if (action == ButtonResignWhite || action == ButtonResignBlack) return board.CanResign(color);
            if (action == ButtonDrawWhite || action == ButtonDrawBlack) {
                // CanResign: the game is running and the local player has this seat.
                return board.CanResign(color) && (board.IsGameSettingOn(ChessArbiter.SettingDrawAgreement)
                    || board.IsGameSettingOn(ChessArbiter.SettingThreefoldRepetition)
                    || board.IsGameSettingOn(ChessArbiter.SettingFiftyMoveRule));
            }
            if (action == ButtonDeclineDrawWhite || action == ButtonDeclineDrawBlack) {
                return board.CanDeclineDraw(color);
            }
            if (action == ButtonEnterReplay) return board.CanEnterReplay();
            if (action == ButtonTakebackWhite || action == ButtonTakebackBlack) {
                return board.CanContinue() || board.CanAcceptTakeback(color) || board.CanRequestTakeback(color);
            }
            if (action == ButtonBackWhite || action == ButtonBackBlack) {
                return board.CanDeclineTakeback(color) || board.CanLeaveReplay()
                    || (lifecycle == ChessBoard.LifecycleLoaded && (board.CanJoin(color) || board.CanLeaveSeat(color)));
            }
            if (action == ButtonImHere) return _ResetWarningKindAt(board._ServerTime()) != ResetWarningNone;
            return false;
        }

        // Whether the button for action, while shown, can be pressed: a step button only where it steps somewhere, a
        // clock button only where there's a step left, Join/Start's Start only with both seats filled, every other
        // button always (it's hidden otherwise).
        public bool _ButtonEnabled(int action) {
            if (!Utilities.IsValid(board)) return false;
            if (IsStepButton(action)) {
                int step = StepTarget(action);
                return step >= 0 && board.CanStepTo(step);
            }
            if (action == ButtonClockMore) return NextBase(true) != NoStep;
            if (action == ButtonClockLess) return NextBase(false) != NoStep;
            if (action == ButtonIncrementMore) return NextIncrement(true) != NoStep;
            if (action == ButtonIncrementLess) return NextIncrement(false) != NoStep;
            if (action == ButtonJoinStartWhite || action == ButtonJoinStartBlack) {
                return board.CanJoin(_ButtonColor(action)) || board.CanStartGame();
            }
            return true;
        }

        // The label of the button for action. The arrows and the clock's signs are the same in every language.
        public string _ButtonText(int action) {
            string[] texts = _GameTexts();
            bool valid = Utilities.IsValid(board);
            int color = _ButtonColor(action);
            if (action == ButtonJoinStartWhite || action == ButtonJoinStartBlack) {
                return valid && !board.CanJoin(color) ? textStart : textJoin;
            }
            if (action == ButtonLeaveWhite || action == ButtonLeaveBlack) return textLeave;
            if (action == ButtonMode) {
                int mode = valid ? board.mode : ChessGame.ModeStandard;
                return I18n.Format(textMode, new string[] { "mode", _ModeText(texts, mode) });
            }
            if (action == ButtonClockMore) return "+";
            if (action == ButtonClockLess) return "-";
            if (action == ButtonIncrementMore) return "++";
            if (action == ButtonIncrementLess) return "--";
            if (action == ButtonReset) return IsArmed(action) ? textSure : textReset;
            if (action == ButtonResignWhite || action == ButtonResignBlack) {
                return IsArmed(action) ? textSure : textResign;
            }
            if (action == ButtonDrawWhite || action == ButtonDrawBlack) return IsArmed(action) ? textDrawNo : textDraw;
            if (action == ButtonDeclineDrawWhite || action == ButtonDeclineDrawBlack) return textDeclineDraw;
            if (action == ButtonEnterReplay) return textReplay;
            if (action == ButtonStepFirst) return "<<";
            if (action == ButtonStepBack) return "<";
            if (action == ButtonStepForward) return ">";
            if (action == ButtonStepLast) return ">>";
            if (action == ButtonTakebackWhite || action == ButtonTakebackBlack) {
                if (!valid || board.CanContinue()) return textContinue;
                return board.CanAcceptTakeback(color) ? textAcceptTakeback : textTakeBackToHere;
            }
            if (action == ButtonBackWhite || action == ButtonBackBlack) {
                if (!valid) return textBackToGame;
                if (board.CanDeclineTakeback(color)) return textDeclineTakeback;
                if (board.CanLeaveReplay()) return textBackToGame;
                return board.CanJoin(color) ? textJoin : textLeave;
            }
            if (action == ButtonLoadHere) return textLoadHere;
            if (action == ButtonPgn) return pgnShown && !pgnPasting ? textHidePgn : textPgn;
            if (action == ButtonLoadPgn) return pgnShown && pgnPasting ? textHidePgn : textLoadPgn;
            if (action == ButtonImHere) return textImHere;
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

        // ---- Panels ----

        // Mode Setup: while the mode or the clock can be chosen.
        public bool _ModeSetupShown() {
            return Utilities.IsValid(board) && (board.CanSetMode() || board.CanSetClock());
        }

        // The Reset button's panel: while the board isn't reset and the local player may reset it.
        public bool _ResetPanelShown() {
            return Utilities.IsValid(board) && board.CanReset();
        }

        // Pre-game: before a game, outside replay (a loaded board is always in replay).
        public bool _PreGameShown() {
            if (!Utilities.IsValid(board) || board.replayMode) return false;
            return board.lifecycle == ChessBoard.LifecycleReset || board.lifecycle == ChessBoard.LifecycleLobby;
        }

        // In-game: a game in progress, or ended (Resign and Draw hidden; Replay and Export PGN are still needed),
        // outside replay.
        public bool _InGameShown() {
            if (!Utilities.IsValid(board) || board.replayMode) return false;
            return board.lifecycle == ChessBoard.LifecycleInGame || board.lifecycle == ChessBoard.LifecycleEnded;
        }

        // The Replay Controls: in replay.
        public bool _ReplayShown() {
            return Utilities.IsValid(board) && board.replayMode;
        }

        static void SetShown(GameObject[] objects, bool shown) {
            for (int i = 0; i < objects.Length; i++) {
                GameObject shownObject = objects[i];
                if (Utilities.IsValid(shownObject) && shownObject.activeSelf != shown) shownObject.SetActive(shown);
            }
        }

        // ---- Reset warning (docs/inactivity.md) ----

        // What the warning should say to the local player at server time now: nothing unless they're seated, or loaded
        // the game, at a board that isn't reset. When anyone can reset the board before it resets itself, from
        // resetWarningOthersSeconds
        // before that (or resetWarningAutoSeconds before the reset, if that's sooner); otherwise from
        // resetWarningAutoSeconds before the reset. It stays until the board is touched or reset.
        public int _ResetWarningKindAt(double now) {
            if (!Utilities.IsValid(board) || !board.CanConfirmPresence()) {
                return ResetWarningNone;
            }
            double available = board.ResetAvailableTime();
            double auto = board.AutoResetTime();
            bool hasAuto = auto != ChessArbiter.Never;
            if (available == ChessArbiter.Never || (hasAuto && available >= auto)) {
                return hasAuto && now >= auto - resetWarningAutoSeconds ? ResetWarningAuto : ResetWarningNone;
            }
            if (!hasAuto) return now >= available - resetWarningOthersSeconds ? ResetWarningOthers : ResetWarningNone;
            bool shown = now >= available - resetWarningOthersSeconds || now >= auto - resetWarningAutoSeconds;
            return shown ? ResetWarningOthersAuto : ResetWarningNone;
        }

        // Shows the warning for the local player at server time now, with its text, facing their end of the table:
        // Black's for a player only in the black seat, White's otherwise.
        void UpdateResetWarning(double now) {
            if (!Utilities.IsValid(resetWarning)) return;
            int kind = _ResetWarningKindAt(now);
            bool shown = kind != ResetWarningNone;
            if (resetWarning.activeSelf != shown) resetWarning.SetActive(shown);
            if (!shown) return;
            ChessUtil._SetText(resetWarningText, kind == ResetWarningAuto ? textResetWarningAuto
                : kind == ResetWarningOthersAuto ? textResetWarningOthersAuto : textResetWarningOthers);
            _PlaceResetWarning(_ResetWarningEnd());
        }

        // The end the warning faces for the local player.
        public int _ResetWarningEnd() {
            string name = ChessUtil._LocalName("");
            return board.IsSeated(ChessRules.Black, name) && !board.IsSeated(ChessRules.White, name)
                ? ChessRules.Black : ChessRules.White;
        }

        // Stands the warning as the prefab places it for White's end, or turned half way about the UI's origin for
        // Black's.
        public void _PlaceResetWarning(int color) {
            if (!Utilities.IsValid(resetWarning)) return;
            Transform warning = resetWarning.transform;
            if (!resetWarningPlaced) {
                resetWarningPlaced = true;
                resetWarningPosition = warning.localPosition;
                resetWarningRotation = warning.localRotation;
            }
            Quaternion turn = color == ChessRules.Black ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;
            warning.localPosition = turn * resetWarningPosition;
            warning.localRotation = turn * resetWarningRotation;
        }

        // Shows the panels, and in them each button whose action applies to the local player, with its label. A shown
        // button is pressable (its UI Button interactable, its label opaque) only where _ButtonEnabled says, greyed out
        // otherwise. Called on every Refresh, after every press, once a second and when a press stops holding.
        public void _UpdateButtons() {
            nextButtonCheck = Time.time + ButtonCheckInterval;
            for (int i = 0; i < armedUntil.Length; i++) {
                if (armedUntil[i] > Time.time && armedUntil[i] < nextButtonCheck) nextButtonCheck = armedUntil[i];
            }
            if (Utilities.IsValid(board)) {
                SetShown(modeSetupPanels, _ModeSetupShown());
                SetShown(resetPanels, _ResetPanelShown());
                SetShown(preGamePanels, _PreGameShown());
                SetShown(inGamePanels, _InGameShown());
                SetShown(replayPanels, _ReplayShown());
                SetShown(clockSetupParts, board.CanSetClock());
                SetLabels(clockValueLabels, _ClockText(board.baseTime, board.increment));
                UpdateResetWarning(board._ServerTime());
            }
            for (int i = 0; i < buttons.Length; i++) {
                GameObject button = buttons[i];
                if (!Utilities.IsValid(button) || i >= buttonActions.Length) continue;
                int action = buttonActions[i];
                bool shown = _ButtonApplies(action);
                if (button.activeSelf != shown) button.SetActive(shown);
                if (!shown) continue;
                bool hasLabel = i < buttonLabels.Length && Utilities.IsValid(buttonLabels[i]);
                bool enabled = _ButtonEnabled(action);
                Button uiButton = button.GetComponent<Button>();
                if (Utilities.IsValid(uiButton) && uiButton.interactable != enabled) uiButton.interactable = enabled;
                if (!hasLabel) continue;
                float alpha = enabled ? 1 : DisabledLabelAlpha;
                if (buttonLabels[i].color.a != alpha) {
                    Color color = buttonLabels[i].color;
                    color.a = alpha;
                    buttonLabels[i].color = color;
                }
                string text = _ButtonText(action);
                if (buttonLabels[i].text != text) buttonLabels[i].text = text;
            }
        }

        // Whether the UI Button of buttons[i] is pressable. For tests.
        public bool _IsButtonInteractable(int i) {
            if (i < 0 || i >= buttons.Length || !Utilities.IsValid(buttons[i])) return false;
            Button uiButton = buttons[i].GetComponent<Button>();
            return Utilities.IsValid(uiButton) && uiButton.interactable;
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
            SetLabels(whiteClockLabels, _PlayerClockAt(ChessRules.White, now));
            SetLabels(whiteNameLabels, _PlayerName(ChessRules.White));
            SetLabels(blackClockLabels, _PlayerClockAt(ChessRules.Black, now));
            SetLabels(blackNameLabels, _PlayerName(ChessRules.Black));
            SetLabels(statusLabels, _StatusTextAt(now));
            SetLabels(replayStatusLabels, valid && board.replayMode ? _ReplayPanelText() : "");
        }

        static void SetLabels(TextMeshProUGUI[] labels, string text) {
            for (int i = 0; i < labels.Length; i++) {
                TextMeshProUGUI label = labels[i];
                if (Utilities.IsValid(label) && label.text != text) label.text = text;
            }
        }

        public string _PlayerName(int color) {
            return _PlayerNameFor(board, color, _GameTexts());
        }

        public string _PlayerClockAt(int color, double now) {
            return _PlayerClockFor(board, color, now, _GameTexts());
        }

        // Both seats at server time now, White's line first (_PlayerTextAt), as the 2D view shows them. The status texts
        // are static, taking the board and the game texts to write them with (_GameTexts), so the 2D view (step 12.13)
        // writes the same lines with its own copy; the instance forms read this UI's board and texts.
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

        public string _ReplayPanelText() {
            return _ReplayPanelTextFor(board, _GameTexts());
        }

        public static string _PlayersTextFor(ChessBoard board, double now, string[] texts) {
            return _PlayerTextFor(board, ChessRules.White, now, texts) + "\n"
                + _PlayerTextFor(board, ChessRules.Black, now, texts);
        }

        // A seat's line: "White: Ana", "White: (open)" for an empty seat, with its clock (_PlayerClockFor) when it has
        // one ("White: Ana  4:58").
        public static string _PlayerTextFor(ChessBoard board, int color, double now, string[] texts) {
            if (!Utilities.IsValid(board)) return "";
            string text = I18n.Format(texts[TextPlayerLine], new string[] {
                "color", _ColorText(texts, color), "name", _PlayerNameFor(board, color, texts) });
            string clock = _PlayerClockFor(board, color, now, texts);
            return clock.Length > 0 ? text + "  " + clock : text;
        }

        // A seat's player: their name, "(open)" for an empty seat.
        public static string _PlayerNameFor(ChessBoard board, int color, string[] texts) {
            if (!Utilities.IsValid(board)) return "";
            string name = color == ChessRules.White ? board.whitePlayer : board.blackPlayer;
            return name == null || name.Length == 0 ? texts[TextPlayerOpen] : name;
        }

        // A seat's clock at server time now: during and after a game with a clock, the remaining time; before one
        // (reset, lobby, loaded), the time control it would start with (_ClockSettingDisplay); "" with no clock. In a
        // game played on untimed after a flag fall (step 12.9) the times are the timed game's, and the side out of time
        // shows GG instead of 0:00.
        public static string _PlayerClockFor(ChessBoard board, int color, double now, string[] texts) {
            if (!Utilities.IsValid(board)) return "";
            bool played = board.lifecycle == ChessBoard.LifecycleInGame || board.lifecycle == ChessBoard.LifecycleEnded;
            if (!played) return _ClockSettingDisplay(board._StartingBaseTime(), board._StartingIncrement());
            int left = board._RemainingTimeAt(color, now);
            if (board.ClocksFromPreviousGame()) return left == 0 ? texts[TextPlayerFlagFallen] : _ClockDisplay(left);
            return board.HasClock() ? _ClockDisplay(left) : "";
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
            return PlyText(board, ply, texts, TextReplayStart, TextReplayMove);
        }

        // The replay position on the Replay Controls, which say it's a replay themselves, with the ply on a line of its
        // own: "2. Nf3\n(3 of 4)", "Start\n(0 of 4)", or "Custom position" after free moves.
        public static string _ReplayPanelTextFor(ChessBoard board, string[] texts) {
            if (board.replayCustom) return texts[TextReplayPanelCustom];
            return PlyText(board, board.replayIndex, texts, TextReplayPanelStart, TextReplayPanelMove);
        }

        static string PlyText(ChessBoard board, int ply, string[] texts, int startText, int moveText) {
            string plyText = ply.ToString();
            string count = board.moveCount.ToString();
            if (ply <= 0 || ply > board.moveCount) {
                return I18n.Format(texts[startText], new string[] { "ply", plyText, "count", count });
            }
            // The side that made the first move: the side to move now, or the other after an odd number of moves.
            int firstSide = board.SideToMove();
            if (board.moveCount % 2 == 1) firstSide = ChessRules.OtherColor(firstSide);
            string san = board.notation[ply - 1];
            if (san == null || san.Length == 0) san = texts[TextUnknownName];
            return I18n.Format(texts[moveText], new string[] {
                "move", ChessPgn._MoveLabel(board.startMoveNumber, firstSide, ply - 1), "san", san, "ply", plyText,
                "count", count });
        }

        // A clock as players read it: "4:58", "1:02:05" from an hour, tenths under ten seconds ("0:09.8").
        public static string _ClockDisplay(int milliseconds) {
            if (milliseconds < 0) milliseconds = 0;
            string text = WholeSecondsDisplay(milliseconds / 1000);
            if (milliseconds < 10000) text += "." + (milliseconds / 100 % 10);
            return text;
        }

        // A time control on a seat's clock before the game: "10:00", "1:30:00", with "+3" when it has an increment;
        // "" with no clock.
        public static string _ClockSettingDisplay(int baseSeconds, int incrementSeconds) {
            if (baseSeconds < 1) return "";
            string text = WholeSecondsDisplay(baseSeconds);
            return incrementSeconds != 0 ? text + "+" + incrementSeconds : text;
        }

        // m:ss, or h:mm:ss from an hour up.
        static string WholeSecondsDisplay(int seconds) {
            int minutes = seconds / 60;
            if (minutes >= 60) {
                return (minutes / 60) + ":" + ChessUtil._TwoDigits(minutes % 60) + ":"
                    + ChessUtil._TwoDigits(seconds % 60);
            }
            return minutes + ":" + ChessUtil._TwoDigits(seconds % 60);
        }
    }
}
