using SiggiUSTools;
using UdonSharp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The history wall (docs/archive.md): a view of the GameArchive listing every archived game, newest first, one
    // two-line entry each with its number, players and result, in a scroll view. Touches only its texts, its entries'
    // visibility and its scroll content; it redraws when the archive's changes count moves on (a game archived here or
    // a holder update arriving). Picking an entry (step 10.2b) selects its game for the local player only, nothing
    // synced: the board views then offer "Load it here" on every reset board. Built in the scene by Tools > Chess
    // Board > Create History Wall (Editor/GameHistoryWallBuilder.cs).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GameHistoryWall : UdonSharpBehaviour {
        public GameArchive archive;
        [Tooltip("The chess system's shared I18n (the Chess Language object), for the wall's text. Left empty: the "
            + "I18n sanity check fills it in on build and before Play Mode.")]
        public I18n i18n;
        // The heading ("Previous games").
        public TextMeshProUGUI title;
        // One text per entry, from the top (entry 0, the newest game): as many as the archive holds.
        public TextMeshProUGUI[] rows = new TextMeshProUGUI[0];
        // Each entry's click target (the same index), calling _SelectRow. Its object is the entry, hidden while the
        // archive has no game for it.
        public GameHistoryRow[] rowTargets = new GameHistoryRow[0];
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

        // The archive's changes count when last drawn; -1 to draw on the first frame.
        [System.NonSerialized] public int drawnChanges = -1;
        // The game the local player picked ("" for none), by ID, since a game's entry moves with every game archived.
        [System.NonSerialized] public string selectedGameId = "";
        // Counts every change to the selection, for the board views.
        [System.NonSerialized] public int selectionChanges;
        // The strings the wall shows, from i18n (LoadTexts), read on first use and again on a language change: the game
        // texts for the names, endings and move counts (ChessBoardUI._LoadGameTexts), and the wall's own.
        string[] gameTexts;
        string textTitle = "";
        string textRow = "";
        string textRowChess960 = "";
        // Whether the wall has been drawn, so the first drawing scrolls to the top.
        bool drawn;
        // The scroll position (the content's y) when the list last scrolled, for _OnScrolled.
        float scrolledTo;
        void Start() {
            if (Utilities.IsValid(i18n)) i18n.AddLanguageChangeListener(this);
        }

        void Update() {
            _RefreshIfChanged();
        }

        // The game texts in the language now. Public for tests.
        public string[] _GameTexts() {
            if (gameTexts == null) LoadTexts();
            return gameTexts;
        }

        void LoadTexts() {
            ChessUtil._CheckI18n(i18n, "[GameHistoryWall] " + gameObject.name);
            gameTexts = ChessBoardUI._LoadGameTexts(i18n);
            textTitle = ChessUtil.Text(i18n, "wall_title");
            textRow = ChessUtil.Text(i18n, "wall_row");
            textRowChess960 = ChessUtil.Text(i18n, "wall_row_chess960");
        }

        // The I18n's language changed (its AddLanguageChangeListener): every row is written again.
        public void OnLanguageChange() {
            LoadTexts();
            _Refresh();
        }

        // Redraws if the archive has changed since the last drawing. Returns whether it did.
        public bool _RefreshIfChanged() {
            if (!Utilities.IsValid(archive) || archive.changes == drawnChanges) return false;
            _Refresh();
            return true;
        }

        // Redraws every entry, newest game at the top: entry e shows the archive's game count - 1 - e, and the entries
        // past the archive's count are hidden, the content as tall as the entries shown. The first drawing scrolls to
        // the top; later ones leave the scroll position. A selected game no longer archived (overwritten once the
        // archive is full) is unselected.
        public void _Refresh() {
            if (!Utilities.IsValid(archive)) return;
            drawnChanges = archive.changes;
            int count = archive.GameCount();
            if (selectedGameId.Length > 0 && archive.IndexOf(selectedGameId) < 0) SetSelection("");
            ChessUtil._SetText(title, _TitleText());
            for (int i = 0; i < rows.Length; i++) {
                bool shown = i < count;
                ChessUtil._SetText(rows[i], shown ? _RowText(count - 1 - i) : "");
                if (i < rowTargets.Length && Utilities.IsValid(rowTargets[i])) {
                    GameObject entry = rowTargets[i].gameObject;
                    if (entry.activeSelf != shown) entry.SetActive(shown);
                }
            }
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

        // ---- Picking a game (step 10.2b) ----

        // An entry pressed (0 at the top, the newest game): selects its game, or unselects it when it's the one
        // selected. An entry with no game does nothing.
        public void _SelectRow(int entry) {
            if (!Utilities.IsValid(archive)) return;
            int count = archive.GameCount();
            if (entry < 0 || entry >= count) return;
            string gameId = archive.GameIdAt(count - 1 - entry);
            SetSelection(gameId == selectedGameId ? "" : gameId);
            DrawSelection();
        }

        public void _ClearSelection() {
            if (selectedGameId.Length == 0) return;
            SetSelection("");
            DrawSelection();
        }

        // Whether the local player has picked a game that's still archived.
        public bool _HasSelection() {
            return _SelectedIndex() >= 0;
        }

        // The selected game's index, oldest first; -1 for none.
        public int _SelectedIndex() {
            if (!Utilities.IsValid(archive) || selectedGameId.Length == 0) return -1;
            return archive.IndexOf(selectedGameId);
        }

        void SetSelection(string gameId) {
            if (gameId == selectedGameId) return;
            selectedGameId = gameId;
            selectionChanges++;
        }

        // Colours the selected game's entry, and every other entry as usual.
        void DrawSelection() {
            int index = _SelectedIndex();
            int selected = index < 0 ? -1 : archive.GameCount() - 1 - index;
            for (int i = 0; i < rows.Length; i++) {
                TextMeshProUGUI row = rows[i];
                if (!Utilities.IsValid(row)) continue;
                Color color = i == selected ? selectedColor : rowColor;
                if (row.color != color) row.color = color;
            }
        }

        // ---- Texts ----

        // "Previous games", however many there are.
        public string _TitleText() {
            if (gameTexts == null) LoadTexts();
            return textTitle;
        }

        // The index-th archived game (the archive's index, oldest first) on two lines: "#12  Ana vs Bo" and "White wins
        // by checkmate (1-0), 12 moves", with ", Chess960" for that mode. A name missing is "?".
        public string _RowText(int index) {
            string[] texts = _GameTexts();
            int moves = (archive.MoveCountAt(index) + 1) / 2;
            int ending = archive.EndingAt(index);
            int winner = archive.WinnerAt(index);
            string row = archive.ModeAt(index) == ChessGame.ModeChess960 ? textRowChess960 : textRow;
            return I18n.Format(row, new string[] {
                "number", archive._SequenceAt(index).ToString(),
                "white", ChessBoardUI._NameText(texts, archive.WhiteAt(index)),
                "black", ChessBoardUI._NameText(texts, archive.BlackAt(index)),
                "ending", ChessBoardUI._EndingText(texts, ending, winner),
                "result", ChessPgn.ResultText(ending, winner),
                "moves", ChessBoardUI._MovesText(texts, moves) });
        }
    }
}
