using System;
using SiggiUSTools;
using UdonSharp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // A history wall (docs/archive.md): a view of a GameArchive, either kind (the instance's, or the local player's own
    // since step 13.10), listing its games newest first, one two-line entry each with its number (or its date on the
    // personal wall), players and result, in a scroll view. Touches only its own texts, entries, buttons and PGN box.
    // Its archive tells it of every change (_OnArchiveChanged) and of every change to the arbiter's pick
    // (_OnSelectionChanged); it registers with the archive on Start. Picking an entry (step 10.2b) picks its game on
    // the arbiter for the local player only, nothing synced: every board's UI then offers "Load it here". Export PGN
    // shows the picked game's PGN in a box on the wall; on the personal wall each entry has a delete button. Placed
    // from the Game History Wall and Personal History Wall prefabs (docs/prefabs.md), its archive set after placing it.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GameHistoryWall : UdonSharpBehaviour {
        // A delete button's press arms it for this long (seconds): a second press within it deletes.
        public const float DeleteArmTime = 3;
        // The time per frame the export's notation walk takes, in milliseconds (the board's fill-in's).
        public const float ExportBudgetMs = 10;

        public GameArchive archive;
        [Tooltip("The chess system's shared I18n (the Chess Language object), for the wall's text. Left empty: the "
            + "I18n sanity check fills it in on build and before Play Mode.")]
        public I18n i18n;
        [Tooltip("The language pack key of the wall's title: wall_title (Previous games) or wall_title_personal (My "
            + "games).")]
        public string titleKey = "wall_title";
        // The heading ("Previous games").
        public TextMeshProUGUI title;
        // One text per entry, from the top (entry 0, the newest game). The wall makes more at start, by instantiating
        // its last entry, when its archive holds more games than it has entries.
        public TextMeshProUGUI[] rows = new TextMeshProUGUI[0];
        // Each entry's click target (the same index), calling _SelectRow. Its object is the entry, hidden while the
        // archive has no game for it; its text is its child Label.
        public GameHistoryRow[] rowTargets = new GameHistoryRow[0];
        // Each entry's delete button (its child Delete, calling the entry's _PressDelete) and its label, on the personal
        // wall; empty on the instance's. Shown while the archive allows deleting and the entry has a game.
        public GameObject[] deleteButtons = new GameObject[0];
        public TextMeshProUGUI[] deleteLabels = new TextMeshProUGUI[0];
        // The scroll view's content, holding the entries from its top, sized to the entries shown; and an entry's
        // height in it.
        public RectTransform content;
        public float entryHeight = 100;
        // The scroll view, its viewport and its scrollbar, for the mouse wheel's step (_OnScrolled).
        public ScrollRect scroll;
        public RectTransform viewport;
        public Scrollbar scrollbar;
        // A row's text colour, and the selected game's.
        public Color rowColor = Color.white;
        public Color selectedColor = new Color(1f, 0.8f, 0.3f);
        // Export PGN (step 13.10): the button (shown while the arbiter's pick is in this wall's archive, calling
        // _PressExport) and its label, and the box it opens, with its hint and read-only text field.
        public GameObject exportButton;
        public TextMeshProUGUI exportLabel;
        public GameObject pgnBox;
        public TextMeshProUGUI pgnHint;
        public TMP_InputField pgnField;

        // The archive's changes count when last drawn; -1 before the first drawing.
        [NonSerialized] public int drawnChanges = -1;
        // How many times the wall has been drawn (_Refresh) and its pick drawn (DrawSelection), for tests.
        [NonSerialized] public int refreshes;
        [NonSerialized] public int selectionDraws;
        // Whether the PGN box is open, for which game, and whether its PGN is in (false while the notation is worked
        // out).
        [NonSerialized] public bool exportShown;
        [NonSerialized] public string exportGameId = "";
        [NonSerialized] public bool exportReady;
        // The game whose delete button was pressed once ("" for none), and until when a second press deletes it.
        [NonSerialized] public string armedGameId = "";
        [NonSerialized] public float armedUntil;
        // The strings the wall shows, from i18n (LoadTexts), read on first use and again on a language change: the game
        // texts for the names, endings and move counts (ChessBoardUI._LoadGameTexts), and the wall's own.
        string[] gameTexts;
        string textTitle = "";
        string textRow = "";
        string textRowChess960 = "";
        string textRowPersonal = "";
        string textRowPersonalChess960 = "";
        string textExport = "";
        string textHide = "";
        string textDelete = "";
        string textSure = "";
        string textWorking = "";
        string textHint = "";
        // Whether the wall has been drawn, so the first drawing scrolls to the top.
        bool drawn;
        // The scroll position (the content's y) when the list last scrolled, for _OnScrolled.
        float scrolledTo;
        // The export's notation walk (an archive without the PGN: the instance's): the game's index, moves, clocks and
        // SAN so far, the ply the walk's position is at (-1 for no walk), and its position.
        int walkIndex;
        int walkCount;
        int walkPly = -1;
        int[] walkMoves = new int[0];
        int[] walkClocks = new int[0];
        string[] walkSans = new string[0];
        byte[] walkBoard = new byte[64];
        int[] walkState = new int[ChessPosition.StateLength];
        int[] walkLegal = new int[ChessMoves.MaxMoves];
        int[] walkScratch = new int[ChessMoves.MaxMoves];
        System.Diagnostics.Stopwatch walkWatch;

        void Start() {
            if (Utilities.IsValid(i18n)) i18n.AddLanguageChangeListener(this);
            if (!Utilities.IsValid(archive)) {
                Debug.LogError("[GameHistoryWall] " + gameObject.name + ": the Archive field is empty.");
                return;
            }
            _EnsureEntries();
            archive._AddWall(this);
        }

        void Update() {
            // The first drawing; later ones come from the archive (_OnArchiveChanged, _OnSelectionChanged).
            if (!drawn) _RefreshIfChanged();
            if (walkPly >= 0) _ExportFor(ExportBudgetMs);
            if (armedGameId.Length > 0 && Time.time >= armedUntil) {
                armedGameId = "";
                DrawDeleteButtons(Utilities.IsValid(archive) ? archive.GameCount() : 0);
            }
        }

        // The game texts in the language now. Public for tests.
        public string[] _GameTexts() {
            if (gameTexts == null) LoadTexts();
            return gameTexts;
        }

        void LoadTexts() {
            ChessUtil._CheckI18n(i18n, "[GameHistoryWall] " + gameObject.name);
            gameTexts = ChessBoardUI._LoadGameTexts(i18n);
            textTitle = ChessUtil.Text(i18n, titleKey);
            textRow = ChessUtil.Text(i18n, "wall_row");
            textRowChess960 = ChessUtil.Text(i18n, "wall_row_chess960");
            textRowPersonal = ChessUtil.Text(i18n, "wall_row_personal");
            textRowPersonalChess960 = ChessUtil.Text(i18n, "wall_row_personal_chess960");
            textExport = ChessUtil.Text(i18n, "wall_export");
            textHide = ChessUtil.Text(i18n, "wall_export_hide");
            textDelete = ChessUtil.Text(i18n, "wall_delete");
            textSure = ChessUtil.Text(i18n, "wall_delete_sure");
            textWorking = ChessUtil.Text(i18n, "wall_pgn_working");
            textHint = ChessUtil.Text(i18n, "pgn_copy_hint");
        }

        // The I18n's language changed (its AddLanguageChangeListener): every row is written again.
        public void OnLanguageChange() {
            LoadTexts();
            _Refresh();
            if (exportShown && !exportReady) SetField(textWorking);
        }

        // ---- Entries ----

        // Makes the entries the wall lacks for its archive's capacity, by instantiating its last entry (with its
        // GameHistoryRow, text and delete button), so a capacity changed in the Inspector needs no rebuilt wall.
        // Public for tests.
        public void _EnsureEntries() {
            if (!Utilities.IsValid(archive)) return;
            int needed = archive.Capacity();
            int have = rowTargets.Length;
            if (have == 0 || needed <= have || rows.Length != have) return;
            GameHistoryRow template = rowTargets[have - 1];
            if (!Utilities.IsValid(template)) return;
            bool deletes = deleteButtons.Length == have && deleteLabels.Length == have;
            TextMeshProUGUI[] grownRows = new TextMeshProUGUI[needed];
            GameHistoryRow[] grownTargets = new GameHistoryRow[needed];
            GameObject[] grownDeletes = new GameObject[deletes ? needed : 0];
            TextMeshProUGUI[] grownDeleteLabels = new TextMeshProUGUI[deletes ? needed : 0];
            for (int i = 0; i < have; i++) {
                grownRows[i] = rows[i];
                grownTargets[i] = rowTargets[i];
                if (!deletes) continue;
                grownDeletes[i] = deleteButtons[i];
                grownDeleteLabels[i] = deleteLabels[i];
            }
            Transform parent = template.transform.parent;
            RectTransform templateRect = template.GetComponent<RectTransform>();
            for (int i = have; i < needed; i++) {
                GameObject entry = Instantiate(template.gameObject);
                entry.transform.SetParent(parent, false);
                entry.name = "Entry " + (i + 1);
                RectTransform rect = entry.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(templateRect.anchoredPosition.x, -i * entryHeight);
                GameHistoryRow row = entry.GetComponent<GameHistoryRow>();
                row.wall = this;
                row.index = i;
                grownTargets[i] = row;
                Transform label = entry.transform.Find("Label");
                if (Utilities.IsValid(label)) grownRows[i] = label.GetComponent<TextMeshProUGUI>();
                if (!deletes) continue;
                Transform delete = entry.transform.Find("Delete");
                if (!Utilities.IsValid(delete)) continue;
                grownDeletes[i] = delete.gameObject;
                Transform deleteLabel = delete.Find("Label");
                if (Utilities.IsValid(deleteLabel)) grownDeleteLabels[i] = deleteLabel.GetComponent<TextMeshProUGUI>();
            }
            rows = grownRows;
            rowTargets = grownTargets;
            if (deletes) {
                deleteButtons = grownDeletes;
                deleteLabels = grownDeleteLabels;
            }
        }

        // ---- Told by the archive ----

        // The archive's games changed: everything is drawn again.
        public void _OnArchiveChanged() {
            _Refresh();
        }

        // The arbiter's pick changed: the picked entry's colour, the Export PGN button, and the PGN box (closed when
        // its game is no longer the pick).
        public void _OnSelectionChanged() {
            DrawSelection();
        }

        // Redraws if the archive has changed since the last drawing. Returns whether it did. For tests and the first
        // drawing; the archive tells the wall of every change.
        public bool _RefreshIfChanged() {
            if (!Utilities.IsValid(archive) || (drawn && archive.changes == drawnChanges)) return false;
            _Refresh();
            return true;
        }

        // Redraws every entry, newest game at the top: entry e shows the archive's game count - 1 - e, and the entries
        // past the archive's count are hidden, the content as tall as the entries shown. The first drawing scrolls to
        // the top; later ones leave the scroll position.
        public void _Refresh() {
            if (!Utilities.IsValid(archive)) return;
            drawnChanges = archive.changes;
            refreshes++;
            int count = archive.GameCount();
            ChessUtil._SetText(title, _TitleText());
            for (int i = 0; i < rows.Length; i++) {
                bool shown = i < count;
                ChessUtil._SetText(rows[i], shown ? _RowText(count - 1 - i) : "");
                if (i < rowTargets.Length && Utilities.IsValid(rowTargets[i])) {
                    GameObject entry = rowTargets[i].gameObject;
                    if (entry.activeSelf != shown) entry.SetActive(shown);
                }
            }
            DrawDeleteButtons(count);
            if (Utilities.IsValid(content)) {
                Vector2 size = content.sizeDelta;
                float height = Mathf.Min(count, rows.Length) * entryHeight;
                if (size.y != height) content.sizeDelta = new Vector2(size.x, height);
                if (!drawn) {
                    content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0);
                    scrolledTo = 0;
                }
            }
            drawn = true;
            DrawSelection();
        }

        // ---- Scrolling ----

        // The list scrolled (its ScrollRect's onValueChanged, in the frame it moved). A ScrollRect moves the content by
        // the wheel's delta times its scrollSensitivity, and that delta's size depends on the input: about 1 a notch
        // through the legacy input, about 120 through the Input System (as in ClientSim), which threw the list to
        // its top or bottom. So when the wheel turned this frame, the move is replaced by one scrollSensitivity step
        // in its direction. Dragging is left as it is. The scroll rect has already set its scrollbar from its own move
        // (in its LateUpdate, before calling this), so the scrollbar is set here too, or it shows the list's end for
        // a frame.
        public void _OnScrolled() {
            if (!Utilities.IsValid(content)) return;
            float offset = content.anchoredPosition.y;
            if (offset != scrolledTo && Input.GetAxisRaw("Mouse ScrollWheel") != 0 && Utilities.IsValid(scroll)) {
                float step = offset > scrolledTo ? scroll.scrollSensitivity : -scroll.scrollSensitivity;
                float max = _MaxScroll();
                offset = Mathf.Clamp(scrolledTo + step, 0, max);
                content.anchoredPosition = new Vector2(content.anchoredPosition.x, offset);
                // 1 at the top, 0 at the bottom.
                if (Utilities.IsValid(scrollbar) && max > 0) scrollbar.SetValueWithoutNotify(1 - offset / max);
            }
            scrolledTo = offset;
        }

        // How far the list can scroll down, in canvas pixels: the content's height past the viewport's.
        public float _MaxScroll() {
            if (!Utilities.IsValid(content)) return 0;
            float shown = Utilities.IsValid(viewport) ? viewport.rect.height : 0;
            return Mathf.Max(0, content.sizeDelta.y - shown);
        }

        // ---- Picking a game (step 10.2b; on the arbiter since 13.10) ----

        // An entry pressed (0 at the top, the newest game): picks its game on the arbiter, or unpicks it when it's the
        // one picked (ChessArbiter._SelectGame). An entry with no game does nothing.
        public void _SelectRow(int entry) {
            if (!Utilities.IsValid(archive) || !Utilities.IsValid(archive.arbiter)) return;
            int count = archive.GameCount();
            if (entry < 0 || entry >= count) return;
            archive.arbiter._SelectGame(archive, archive.GameIdAt(count - 1 - entry));
        }

        // The picked game's index in this wall's archive, oldest first; -1 when the pick is none or in another archive.
        public int _SelectedIndex() {
            if (!Utilities.IsValid(archive)) return -1;
            ChessArbiter arbiter = archive.arbiter;
            if (!Utilities.IsValid(arbiter) || arbiter.selectedArchive != archive) return -1;
            return archive.IndexOf(arbiter.selectedGameId);
        }

        // Colours the picked game's entry (and every other entry as usual), shows Export PGN while there is one, and
        // closes the PGN box once its game isn't the pick.
        void DrawSelection() {
            selectionDraws++;
            int index = _SelectedIndex();
            int selected = index < 0 ? -1 : archive.GameCount() - 1 - index;
            for (int i = 0; i < rows.Length; i++) {
                TextMeshProUGUI row = rows[i];
                if (!Utilities.IsValid(row)) continue;
                Color color = i == selected ? selectedColor : rowColor;
                if (row.color != color) row.color = color;
            }
            if (exportShown && (index < 0 || archive.GameIdAt(index) != exportGameId)) CloseExport();
            if (Utilities.IsValid(exportButton) && exportButton.activeSelf != (index >= 0)) {
                exportButton.SetActive(index >= 0);
            }
            if (gameTexts == null) LoadTexts();
            ChessUtil._SetText(exportLabel, exportShown ? textHide : textExport);
        }

        // ---- Export PGN (step 13.10) ----

        // The Export PGN button: opens the PGN box for the picked game, or closes it when open. Returns whether it's
        // open now.
        public bool _PressExport() {
            if (exportShown) {
                CloseExport();
            } else {
                int index = _SelectedIndex();
                if (index >= 0) OpenExport(index);
            }
            DrawSelection();
            return exportShown;
        }

        // The box with the game's PGN: the archive's own (PgnAt, at once), or, when it keeps none (the instance's), one
        // written from what it holds once the notation is worked out over the next frames (_ExportFor), the box saying
        // "Working…" meanwhile.
        void OpenExport(int index) {
            exportShown = true;
            exportGameId = archive.GameIdAt(index);
            if (gameTexts == null) LoadTexts();
            ChessUtil._SetText(pgnHint, textHint);
            if (Utilities.IsValid(pgnBox) && !pgnBox.activeSelf) pgnBox.SetActive(true);
            string pgn = archive.PgnAt(index);
            if (pgn.Length > 0) {
                ShowExport(pgn);
                return;
            }
            exportReady = false;
            SetField(textWorking);
            StartWalk(index);
        }

        void CloseExport() {
            exportShown = false;
            exportReady = false;
            exportGameId = "";
            walkPly = -1;
            if (Utilities.IsValid(pgnBox) && pgnBox.activeSelf) pgnBox.SetActive(false);
            if (Utilities.IsValid(pgnField)) pgnField.text = "";
        }

        void ShowExport(string pgn) {
            exportReady = true;
            SetField(pgn);
        }

        // Sets the PGN box's text, only when it changed, so a selection in it survives.
        void SetField(string text) {
            if (Utilities.IsValid(pgnField) && pgnField.text != text) pgnField.text = text;
        }

        // The PGN box's text: the PGN once ready, "" otherwise. For tests.
        public string _ExportText() {
            return exportReady && Utilities.IsValid(pgnField) ? pgnField.text : "";
        }

        // Starts working out the SAN of the index-th game's moves from its starting position.
        void StartWalk(int index) {
            walkIndex = index;
            walkCount = archive.MoveCountAt(index);
            if (walkMoves.Length < walkCount) {
                walkMoves = new int[walkCount];
                walkClocks = new int[walkCount];
                walkSans = new string[walkCount];
            }
            archive.CopyMovesAt(index, walkMoves);
            archive.CopyClocksAt(index, walkClocks);
            int halfmoves;
            int number;
            string fen = archive.StartFenAt(index);
            if (!ChessFen.ParseFen(fen.Length == 0 ? ChessFen.StartFen : fen, walkBoard, walkState, out halfmoves,
                    out number)) {
                walkPly = -1;
                ShowExport("");
                return;
            }
            walkPly = 0;
        }

        // Works out the export's notation for about budgetMs (at least one move), and once it's all in, writes the
        // PGN into the box. Returns whether no walk is left. Public for tests (float.MaxValue: all of it).
        public bool _ExportFor(float budgetMs) {
            if (walkPly < 0) return true;
            if (walkWatch == null) walkWatch = new System.Diagnostics.Stopwatch();
            walkWatch.Reset();
            walkWatch.Start();
            int done = 0;
            while (walkPly < walkCount) {
                if (done > 0 && walkWatch.Elapsed.TotalMilliseconds >= budgetMs) {
                    walkWatch.Stop();
                    return false;
                }
                int move = walkMoves[walkPly];
                int legalCount = ChessMoves.GenerateLegalMoves(walkBoard, walkState, walkLegal, true);
                walkSans[walkPly] = ChessNotation.MoveToSan(walkBoard, walkState, move, walkLegal, legalCount,
                    walkScratch, true);
                ChessPosition.MakeMove(walkBoard, walkState, move);
                walkPly++;
                done++;
            }
            walkWatch.Stop();
            walkPly = -1;
            ShowExport(_WritePgn(walkIndex, walkSans, walkClocks, walkCount));
            return true;
        }

        // The index-th game's PGN as a board that loaded it would write it (ChessBoard.GamePgn): the tags from what
        // the archive holds (the Event and Site it was played with, step 14.13, not the arbiter's now), with sans and
        // clocks, the first count of them.
        public string _WritePgn(int index, string[] sans, int[] clocks, int count) {
            string fen = archive.StartFenAt(index);
            if (fen.Length == 0) fen = ChessFen.StartFen;
            byte[] startBoard = new byte[64];
            int[] startState = new int[ChessPosition.StateLength];
            int halfmoves;
            int number;
            int side = ChessRules.White;
            if (ChessFen.ParseFen(fen, startBoard, startState, out halfmoves, out number)) {
                side = startState[ChessPosition.StateSideToMove];
            } else {
                number = 1;
            }
            int baseTime = archive.BaseTimeAt(index);
            return ChessPgn.WriteGame(archive.EventAt(index), archive.SiteAt(index),
                archive.WhiteAt(index), archive.BlackAt(index), archive.EndingAt(index), archive.WinnerAt(index),
                archive.ModeAt(index), fen, ChessBoard._PgnBaseSeconds(baseTime),
                baseTime < 1 ? 0 : archive.IncrementAt(index), archive.GameIdAt(index), archive.ContinuedFromAt(index),
                sans, clocks, count, number, side);
        }

        // ---- Deleting (step 13.10, the personal wall) ----

        // An entry's delete button pressed: the first press arms it ("Sure?") for DeleteArmTime seconds, a second
        // within them deletes the entry's game from the archive. Returns whether it deleted.
        public bool _PressDelete(int entry) {
            if (!Utilities.IsValid(archive) || !archive.CanDelete()) return false;
            int count = archive.GameCount();
            if (entry < 0 || entry >= count) return false;
            string gameId = archive.GameIdAt(count - 1 - entry);
            if (armedGameId == gameId && Time.time < armedUntil) {
                armedGameId = "";
                bool deleted = archive.DeleteGame(gameId);
                DrawDeleteButtons(archive.GameCount());
                return deleted;
            }
            armedGameId = gameId;
            armedUntil = Time.time + DeleteArmTime;
            DrawDeleteButtons(count);
            return false;
        }

        // Shows each entry's delete button while the archive allows deleting and the entry has a game, labelled
        // "Sure?" for the armed one.
        void DrawDeleteButtons(int count) {
            if (deleteButtons.Length == 0) return;
            if (gameTexts == null) LoadTexts();
            bool canDelete = Utilities.IsValid(archive) && archive.CanDelete();
            for (int i = 0; i < deleteButtons.Length; i++) {
                GameObject button = deleteButtons[i];
                if (!Utilities.IsValid(button)) continue;
                bool shown = canDelete && i < count;
                if (button.activeSelf != shown) button.SetActive(shown);
                if (!shown || i >= deleteLabels.Length) continue;
                bool armed = armedGameId.Length > 0 && archive.GameIdAt(count - 1 - i) == armedGameId;
                ChessUtil._SetText(deleteLabels[i], armed ? textSure : textDelete);
            }
        }

        // ---- Texts ----

        // "Previous games" (or the titleKey's text), however many there are.
        public string _TitleText() {
            if (gameTexts == null) LoadTexts();
            return textTitle;
        }

        // The index-th archived game (the archive's index, oldest first) on two lines: "#12  Ana vs Bo" and "White wins
        // by checkmate (1-0), 12 moves", with ", Chess960" for that mode. A name missing is "?". A game without a
        // sequence number (the personal archive's) starts with its date and time instead (_DateText).
        public string _RowText(int index) {
            string[] texts = _GameTexts();
            int moves = (archive.MoveCountAt(index) + 1) / 2;
            int ending = archive.EndingAt(index);
            int winner = archive.WinnerAt(index);
            int sequence = archive._SequenceAt(index);
            bool chess960 = archive.ModeAt(index) == ChessGame.ModeChess960;
            string row = sequence > 0 ? (chess960 ? textRowChess960 : textRow)
                : (chess960 ? textRowPersonalChess960 : textRowPersonal);
            return I18n.Format(row, new string[] {
                "number", sequence.ToString(),
                "date", _DateText(archive.GameIdAt(index)),
                "white", ChessBoardUI._NameText(texts, archive.WhiteAt(index)),
                "black", ChessBoardUI._NameText(texts, archive.BlackAt(index)),
                "ending", ChessBoardUI._EndingText(texts, ending, winner),
                "result", ChessPgn.ResultText(ending, winner),
                "moves", ChessBoardUI._MovesText(texts, moves) });
        }

        // When the game with this ID started, from the ID's yyyyMMddHHmmss (UTC, docs/game-ids.md), in the player's
        // local time: "2026-10-26 21:30". "?" for an ID in another format.
        public static string _DateText(string gameId) {
            if (ChessGame._DateFromGameId(gameId).Length == 0) return "?";
            DateTime utc = new DateTime(Digits(gameId, 0, 4), Digits(gameId, 4, 2), Digits(gameId, 6, 2),
                Digits(gameId, 8, 2), Digits(gameId, 10, 2), Digits(gameId, 12, 2), DateTimeKind.Utc);
            return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        static int Digits(string text, int start, int length) {
            int value = 0;
            for (int i = start; i < start + length; i++) value = value * 10 + (text[i] - '0');
            return value;
        }
    }
}
