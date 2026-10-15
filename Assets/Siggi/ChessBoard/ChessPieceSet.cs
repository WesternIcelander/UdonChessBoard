using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // One side's chess piece models (steps 12.3 and 12.10, docs/customizing.md): one template object per piece type,
    // each with the model's MeshFilter and MeshRenderer and its materials (every slot). A template's local position,
    // rotation and scale (under its parent, which stands for the square's centre on the playing surface) are the
    // piece's offset, rotation and size on its square, in the view's local units: a model facing any way, of any size
    // or with its origin off its base is set up by moving, turning and scaling its template where it can be seen.
    // The builder puts each template in a slot of its own with a one-square guide beside the others.
    //
    // A set has no colour of its own: either side can wear it, and White and Black each wear a set of their own
    // (ChessBoardView.SetChessPieces, or the arbiter's defaultWhitePieceSet and defaultBlackPieceSet). Templates are set
    // up as White's pieces, facing Black (+z); a view turns Black's pieces half round. Light and dark variants of a
    // design are two sets. The set itself changes nothing. On Start it hides its children (the slots, templates and
    // guides), which the views go on reading, so keep the set's own object active. No synced variables.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ChessPieceSet : UdonSharpBehaviour {
        public Transform pawn;
        public Transform knight;
        public Transform bishop;
        public Transform rook;
        public Transform queen;
        public Transform king;

        void Start() {
            for (int i = 0; i < transform.childCount; i++) {
                GameObject child = transform.GetChild(i).gameObject;
                if (child.activeSelf) child.SetActive(false);
            }
        }

        // The template for a ChessRules piece type; null for none.
        public Transform Template(int type) {
            switch (type) {
                case ChessRules.Pawn: return pawn;
                case ChessRules.Knight: return knight;
                case ChessRules.Bishop: return bishop;
                case ChessRules.Rook: return rook;
                case ChessRules.Queen: return queen;
                case ChessRules.King: return king;
            }
            return null;
        }

        // What's missing, for a log: "" when every template has a mesh and a renderer.
        public string Problem() {
            for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) {
                Transform template = Template(type);
                string name = TypeName(type);
                if (!Utilities.IsValid(template)) return "no " + name + " template";
                MeshFilter filter = template.GetComponent<MeshFilter>();
                if (!Utilities.IsValid(filter) || !Utilities.IsValid(filter.sharedMesh)) {
                    return "the " + name + " template has no mesh";
                }
                if (!Utilities.IsValid(template.GetComponent<MeshRenderer>())) {
                    return "the " + name + " template has no MeshRenderer";
                }
            }
            return "";
        }

        public static string TypeName(int type) {
            switch (type) {
                case ChessRules.Pawn: return "pawn";
                case ChessRules.Knight: return "knight";
                case ChessRules.Bishop: return "bishop";
                case ChessRules.Rook: return "rook";
                case ChessRules.Queen: return "queen";
                case ChessRules.King: return "king";
            }
            return "piece";
        }
    }
}
