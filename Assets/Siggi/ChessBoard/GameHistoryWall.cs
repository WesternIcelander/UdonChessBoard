using UdonSharp;
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
        // The heading ("Previous games").
        public Text title;
        // One text per archived game, oldest first: as many as the archive holds.
        public Text[] rows = new Text[0];
        // Each row's click target (the same index), calling SelectRow.
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

        void Update() {
            RefreshIfChanged();
        }

        // Redraws if the archive has changed since the last drawing. Returns whether it did.
        public bool RefreshIfChanged() {
            if (!Utilities.IsValid(archive) || archive.changes == drawnChanges) return false;
            Refresh();
            return true;
        }

        // Redraws every row. A selected game no longer archived (overwritten once the archive is full) is unselected.
        public void Refresh() {
            if (!Utilities.IsValid(archive)) return;
            drawnChanges = archive.changes;
            int count = archive.GameCount();
            if (selectedGameId.Length > 0 && archive.IndexOf(selectedGameId) < 0) SetSelection("");
            SetText(title, TitleText());
            for (int i = 0; i < rows.Length; i++) SetText(rows[i], i < count ? RowText(i) : "");
            DrawSelection();
        }

        // ---- Picking a game (step 10.2b) ----

        // A row pressed: selects its game, or unselects it when it's the one selected. An empty row does nothing.
        public void SelectRow(int index) {
            if (!Utilities.IsValid(archive) || index < 0 || index >= archive.GameCount()) return;
            string gameId = archive.GameIdAt(index);
            SetSelection(gameId == selectedGameId ? "" : gameId);
            DrawSelection();
        }

        public void ClearSelection() {
            if (selectedGameId.Length == 0) return;
            SetSelection("");
            DrawSelection();
        }

        // Whether the local player has picked a game that's still archived.
        public bool HasSelection() {
            return SelectedIndex() >= 0;
        }

        // The selected game's index, oldest first; -1 for none.
        public int SelectedIndex() {
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
            int selected = SelectedIndex();
            for (int i = 0; i < rows.Length; i++) {
                Text row = rows[i];
                if (!Utilities.IsValid(row)) continue;
                Color color = i == selected ? selectedColor : rowColor;
                if (row.color != color) row.color = color;
            }
        }

        // ---- Texts ----

        // "Previous games", or "Previous games: none yet" while the archive is empty.
        public string TitleText() {
            if (!Utilities.IsValid(archive)) return "";
            int count = archive.GameCount();
            return count == 0 ? "Previous games: none yet" : "Previous games";
        }

        // The index-th archived game, oldest first: "#12  Ana vs Bo: White wins by checkmate (1-0), 12 moves", with
        // ", Chess960" for that mode. A name missing is "?".
        public string RowText(int index) {
            int ending = archive.EndingAt(index);
            int winner = archive.WinnerAt(index);
            int moves = (archive.MoveCountAt(index) + 1) / 2;
            string text = "#" + archive.SequenceAt(index) + "  " + NameText(archive.WhiteAt(index)) + " vs "
                + NameText(archive.BlackAt(index)) + ": " + ChessGame.EndingDescription(ending, winner) + " ("
                + ChessPgn.ResultText(ending, winner) + "), " + moves + (moves == 1 ? " move" : " moves");
            if (archive.ModeAt(index) == ChessGame.ModeChess960) text += ", Chess960";
            return text;
        }

        static string NameText(string name) {
            return name != null && name.Length > 0 ? name : "?";
        }

        static void SetText(Text label, string text) {
            if (Utilities.IsValid(label) && label.text != text) label.text = text;
        }
    }
}
