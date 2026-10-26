using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // One of a board view's 32 piece objects (docs/physical-pieces.md), next to its VRCPickup.
    // Grabbing a piece takes ownership of the piece, which the board's own ownership rule doesn't cover, so the piece
    // asks the board who may touch the pieces, as a second check behind the view's pickupable. Part of the Chess Board
    // View prefab. Nothing synced: every client places the pieces from the board, and a carried piece is seen only by
    // its carrier (no VRCObjectSync, step 9.6).
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ChessPiece : UdonSharpBehaviour {
        // The view this piece belongs to, and its index there (0 to 15 White's, 16 to 31 Black's).
        public ChessBoardView view;
        public int index;

        // Asked on the requesting client and on the owner. A refusal is logged, so a client's log shows it.
        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) {
            bool allowed = Utilities.IsValid(view) && view._PlayerMayTouchPieces(requestedOwner);
            if (!allowed) {
                Debug.Log("[ChessPiece] " + gameObject.name + ": ownership refused for "
                    + (Utilities.IsValid(requestedOwner) ? requestedOwner.displayName : "nobody"));
            }
            return allowed;
        }

        // Only on the client picking it up or letting it go; the view forwards both to the board.
        public override void OnPickup() {
            if (Utilities.IsValid(view)) view._OnPiecePickedUp(index);
        }

        public override void OnDrop() {
            if (Utilities.IsValid(view)) view._OnPieceDropped(index);
        }

        // A click in desktop mode (step 9.3c); the view makes the piece interactive only where a click does something.
        public override void Interact() {
            if (Utilities.IsValid(view)) view._OnPieceClicked(index);
        }
    }
}
