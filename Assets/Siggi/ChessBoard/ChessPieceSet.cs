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
    // The promotion templates (step 12.11) are set up the same way: the see-through pieces floating over a pawn
    // waiting on the last rank, one per piece it can become, which the promoting player clicks to choose. They're in
    // the set, so White's and Black's can differ.
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
        // The promotion pieces (step 12.11), shown see-through while a promotion waits for its piece.
        public Transform promotionKnight;
        public Transform promotionBishop;
        public Transform promotionRook;
        public Transform promotionQueen;

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

        // The promotion template for a ChessRules piece type a pawn can become (knight, bishop, rook or queen); null
        // for none.
        public Transform PromotionTemplate(int type) {
            switch (type) {
                case ChessRules.Knight: return promotionKnight;
                case ChessRules.Bishop: return promotionBishop;
                case ChessRules.Rook: return promotionRook;
                case ChessRules.Queen: return promotionQueen;
            }
            return null;
        }

        // What's missing, for a log: "" when every template (the promotion ones too) has a mesh and a renderer.
        public string Problem() {
            for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) {
                string problem = TemplateProblem(Template(type), TypeName(type));
                if (problem.Length > 0) return problem;
            }
            for (int type = ChessRules.Knight; type <= ChessRules.Queen; type++) {
                string problem = TemplateProblem(PromotionTemplate(type), "promotion " + TypeName(type));
                if (problem.Length > 0) return problem;
            }
            return "";
        }

        string TemplateProblem(Transform template, string name) {
            if (!Utilities.IsValid(template)) return "no " + name + " template";
            MeshFilter filter = template.GetComponent<MeshFilter>();
            if (!Utilities.IsValid(filter) || !Utilities.IsValid(filter.sharedMesh)) {
                return "the " + name + " template has no mesh";
            }
            if (!Utilities.IsValid(template.GetComponent<MeshRenderer>())) {
                return "the " + name + " template has no MeshRenderer";
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
