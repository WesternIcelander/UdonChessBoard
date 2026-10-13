using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The history wall (docs/archive.md): a view of the GameArchive listing every archived game, oldest first, one row
    // each with its number, players and result. Holds no state of its own and touches only its texts; it redraws when
    // the archive's changes count moves on (a game archived here or a holder update arriving). Built in the scene by
    // Tools > Chess Board > Create History Wall (Editor/GameHistoryWallBuilder.cs).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GameHistoryWall : UdonSharpBehaviour {
        public GameArchive archive;
        // The heading ("Previous games").
        public Text title;
        // One text per archived game, oldest first: as many as the archive holds.
        public Text[] rows = new Text[0];

        // The archive's changes count when last drawn; -1 to draw on the first frame.
        [System.NonSerialized] public int drawnChanges = -1;

        void Update() {
            RefreshIfChanged();
        }

        // Redraws if the archive has changed since the last drawing. Returns whether it did.
        public bool RefreshIfChanged() {
            if (!Utilities.IsValid(archive) || archive.changes == drawnChanges) return false;
            Refresh();
            return true;
        }

        public void Refresh() {
            if (!Utilities.IsValid(archive)) return;
            drawnChanges = archive.changes;
            int count = archive.GameCount();
            SetText(title, TitleText());
            for (int i = 0; i < rows.Length; i++) SetText(rows[i], i < count ? RowText(i) : "");
        }

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
