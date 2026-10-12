using UdonSharp;

namespace SiggiChessBoard {
    // Something the board tells about its changes (docs/architecture.md, Logical → physical): the view, a move list,
    // a clock, a test. Udon has no events or interfaces, so listeners derive from this and the board calls them. A
    // listener reads what changed from the board (ChessBoard.lastChange) and the rest of its state through its public
    // fields and getters.
    public abstract class ChessBoardListener : UdonSharpBehaviour {
        // Called once per change to a board this listens to, after the change.
        public virtual void OnBoardChanged() {
        }
    }
}
