using UdonSharp;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // One cell of the 2D view's move list (step 12.15, docs/board-2d.md): its UI Button calls Press, which passes the
    // cell's index to the view. A UI event carries no arguments, so each cell has its own. Part of the 2D Chess Board View
    // prefab. No synced variables.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ChessMoveListCell : UdonSharpBehaviour {
        public ChessBoard2DView view;
        // The cell's index in the view's moveCells: 2 * row + 0 for White's move, + 1 for Black's.
        public int cell;

        public void _Press() {
            if (Utilities.IsValid(view)) view._PressMoveCell(cell);
        }
    }
}
