using UdonSharp;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // A click target for desktop mode (docs/player-input.md, Desktop mode): one of a board view's 64 squares, a collider
    // the view switches on only while a click on it would do something. Interact doesn't say where a collider was hit,
    // so each square has its own. Built by Tools > Chess Board > Create Board View. No synced variables.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ChessSquare : UdonSharpBehaviour {
        // The view this target belongs to, and its square.
        public ChessBoardView view;
        public int square = ChessRules.NoSquare;

        public override void Interact() {
            if (Utilities.IsValid(view)) view._OnSquareClicked(square);
        }
    }
}
