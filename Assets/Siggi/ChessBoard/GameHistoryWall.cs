using SiggiUSTools;
using UdonSharp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The history wall (docs/archive.md): a view of the GameArchive listing every archived game, oldest first, one row
    // each with its number, players and result. Touches only its texts; it redraws when the archive's changes count
    // moves on (a game archived here or a holder update arriving). Picking a row (step 10.2b) selects its game for the
    // local player only, nothing synced: the board views then offer "Load it here" on every reset board. Built in the
    // scene by Tools > Chess Board > Create History Wall (Editor/GameHistoryWallBuilder.cs).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GameHistoryWall : UdonSharpBehaviour {
        public GameArchive archive;
        [Tooltip("The chess system's shared I18n (the Chess Language object), for the wall's text. Left empty: the "
            + "I18n sanity check fills it in on build and before Play Mode.")]
        public I18n i18n;
        // The heading ("Previous games").
        public TextMeshProUGUI title;
        // One text per archived game, oldest first: as many as the archive holds.
        public TextMeshProUGUI[] rows = new TextMeshProUGUI[0];
        // Each row's click target (the same index), calling _SelectRow.
        public GameHistoryRow[] rowTargets = new GameHistoryRow[0];
        // A row's text colour, and the selected game's.
        public Color rowColor = Color.white;
        public Color selectedColor = new Color(1f, 0.8f, 0.3f);

        // The archive's changes count when last drawn; -1 to draw on the first frame.
        [System.NonSerialized] public int drawnChanges = -1;
        // The game the local player picked ("" for none), by ID, since a game's row moves once the archive is full.
        [System.NonSerialized] public string selectedGameId = "";
        // Counts every change to the selection, for the board views.
        [System.NonSerialized] public int selectionChanges;
        // The strings the wall shows, from i18n (LoadTexts), read on first use and again on a language change: the game
        // texts for the names, endings and move counts (ChessBoardUI._LoadGameTexts), and the wall's own.
        string[] gameTexts;
        string textTitle = "";
        string textTitleEmpty = "";
        string textRow = "";
        string textRowChess960 = "";

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
            textTitleEmpty = ChessUtil.Text(i18n, "wall_title_empty");
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

        // Redraws every row. A selected game no longer archived (overwritten once the archive is full) is unselected.
        public void _Refresh() {
            if (!Utilities.IsValid(archive)) return;
            drawnChanges = archive.changes;
            int count = archive.GameCount();
            if (selectedGameId.Length > 0 && archive.IndexOf(selectedGameId) < 0) SetSelection("");
            ChessUtil._SetText(title, _TitleText());
            for (int i = 0; i < rows.Length; i++) ChessUtil._SetText(rows[i], i < count ? _RowText(i) : "");
            DrawSelection();
        }

        // ---- Picking a game (step 10.2b) ----

        // A row pressed: selects its game, or unselects it when it's the one selected. An empty row does nothing.
        public void _SelectRow(int index) {
            if (!Utilities.IsValid(archive) || index < 0 || index >= archive.GameCount()) return;
            string gameId = archive.GameIdAt(index);
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

        // Colours the selected game's row, and every other row as usual.
        void DrawSelection() {
            int selected = _SelectedIndex();
            for (int i = 0; i < rows.Length; i++) {
                TextMeshProUGUI row = rows[i];
                if (!Utilities.IsValid(row)) continue;
                Color color = i == selected ? selectedColor : rowColor;
                if (row.color != color) row.color = color;
            }
        }

        // ---- Texts ----

        // "Previous games", or "Previous games: none yet" while the archive is empty.
        public string _TitleText() {
            if (!Utilities.IsValid(archive)) return "";
            int count = archive.GameCount();
            if (gameTexts == null) LoadTexts();
            return count == 0 ? textTitleEmpty : textTitle;
        }

        // The index-th archived game, oldest first: "#12  Ana vs Bo: White wins by checkmate (1-0), 12 moves", with
        // ", Chess960" for that mode. A name missing is "?".
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
