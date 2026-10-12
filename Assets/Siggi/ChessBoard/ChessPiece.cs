using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // One of a board view's 32 piece objects (docs/physical-pieces.md), next to its VRCPickup and VRCObjectSync.
    // Grabbing a piece takes ownership of the piece, which the board's own ownership rule doesn't cover, so the piece
    // asks the board who may touch the pieces, as a second check behind the view's pickupable. Built by
    // Tools > Chess Board > Create Board View. No synced variables of its own: VRCObjectSync carries the piece.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class ChessPiece : UdonSharpBehaviour {
        // The view this piece belongs to, and its index there (0 to 15 White's, 16 to 31 Black's).
        public ChessBoardView view;
        public int index;

        // Asked on the requesting client and on the owner. A refusal is logged, so a client's log shows it.
        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) {
            bool allowed = Utilities.IsValid(view) && view.PlayerMayTouchPieces(requestedOwner);
            if (!allowed) {
                Debug.Log("[ChessPiece] " + gameObject.name + ": ownership refused for "
                    + (Utilities.IsValid(requestedOwner) ? requestedOwner.displayName : "nobody"));
            }
            return allowed;
        }
    }
}
