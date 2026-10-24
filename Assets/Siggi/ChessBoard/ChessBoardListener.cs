using UdonSharp;

namespace SiggiChessBoard {
    // Something the board tells about its changes (docs/architecture.md, Logical → physical): the view, a move list,
    // a clock, a test. Udon has no events or interfaces, so listeners derive from this and the board calls them. A
    // listener reads what changed from the board (ChessBoard.lastChange) and the rest of its state through its public
    // fields and getters.
    public abstract class ChessBoardListener : UdonSharpBehaviour {
        // Called once per change to a board this listens to, after the change. A change of a side's piece set alone
        // (ChessBoard.ChangePieceSet, step 13.6) leaves the position as it was: only views showing the pieces care.
        public virtual void OnBoardChanged() {
        }

        // Called after the board has filled in missing notation by itself (ChessBoard.FillNotationFor), which changes
        // nothing but the move list's text, so lastChange stays as it was.
        public virtual void OnNotationFilled() {
        }

        // Called on the importing client when a PGN import (ChessBoard.StartImport) starts, is loaded, fails or is
        // abandoned (ChessBoard.importStatus, and why it failed as codes: importResult and importFailedAt). A loaded
        // import is also an OnBoardChanged (ChangeLoaded), told first.
        public virtual void OnImportChanged() {
        }
    }
}
