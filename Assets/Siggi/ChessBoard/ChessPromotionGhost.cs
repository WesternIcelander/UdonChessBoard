using UdonSharp;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // One of a board view's promotion ghosts (step 12.11, docs/physical-pieces.md): a see-through piece the view shows
    // over a pawn waiting on the last rank, for the local player alone; clicking it (Interact: a VR pointer or a
    // desktop click alike) promotes the pawn to its piece. The view dresses it in the promoting side's promotion
    // template and places it. Built by Tools > Chess Board > Create Board View. No synced variables.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ChessPromotionGhost : UdonSharpBehaviour {
        // The view this ghost belongs to, and the ChessRules piece type it promotes to.
        public ChessBoardView view;
        public int pieceType = ChessRules.Queen;

        void Start() {
            string name = ChessPieceSet.TypeName(pieceType);
            InteractionText = "Promote to " + name;
        }

        public override void Interact() {
            if (Utilities.IsValid(view)) view.ChoosePromotion(pieceType);
        }
    }
}
