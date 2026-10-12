using UdonSharp;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // A click target for desktop mode (docs/player-input.md, Desktop mode): one of a board view's 64 squares, or one of
    // its two castle drop spaces, a collider the view switches on only while a click on it would do something. Interact
    // doesn't say where a collider was hit, so each square has its own. Built by Tools > Chess Board > Create Board
    // View. No synced variables.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ChessSquare : UdonSharpBehaviour {
        // The view this target belongs to; its square (ChessRules.NoSquare for a castle drop space) and castle side
        // (ChessBoardView.NoCastleSide for a square).
        public ChessBoardView view;
        public int square = ChessRules.NoSquare;
        public int castleSide = ChessBoardView.NoCastleSide;

        public override void Interact() {
            if (!Utilities.IsValid(view)) return;
            if (castleSide != ChessBoardView.NoCastleSide) view.OnCastleSpaceClicked(castleSide);
            else view.OnSquareClicked(square);
        }
    }
}
