using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // A chess piece set (steps 12.3, 12.10 and 13.6, docs/customizing.md): one template object per colour and piece
    // type, each with the model's MeshFilter and MeshRenderer and its materials (every slot). A template's local
    // position, rotation and scale (under its parent, which stands for the square's centre on the playing surface) are
    // the piece's offset, rotation and size on its square, in the view's local units: a model facing any way, of any
    // size or with its origin off its base is set up by moving, turning and scaling its template where it can be seen.
    // The builder puts each template in a slot of its own with a one-square guide beside the others.
    //
    // White's templates are set up facing Black (+z), Black's facing White (-z). White's are all required. Black's
    // are optional (step 13.6, the view's fallback): a Black template with no mesh wears White's mesh and transform
    // for that type turned half round, with Black's materials when it has a renderer with materials, otherwise White's.
    // So a set whose sides differ only in colour gives Black only materials, and one look for both sides gives Black
    // nothing.
    //
    // The promotion templates (step 12.11) are set up the same way: the see-through pieces floating over a pawn
    // waiting on the last rank, one per piece it can become, which the promoting player clicks to choose. Black's fall
    // back on White's as its pieces do.
    //
    // The world's sets are listed on the arbiter (ChessArbiter.pieceSets), and each board syncs which of them White
    // and Black wear. The set itself changes nothing. On Start it hides its children (the slots, templates and
    // guides), which the views go on reading, so keep the set's own object active. No synced variables.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ChessPieceSet : UdonSharpBehaviour {
        public Transform whitePawn;
        public Transform whiteKnight;
        public Transform whiteBishop;
        public Transform whiteRook;
        public Transform whiteQueen;
        public Transform whiteKing;
        // The promotion pieces (step 12.11), shown see-through while a promotion waits for its piece.
        public Transform whitePromotionKnight;
        public Transform whitePromotionBishop;
        public Transform whitePromotionRook;
        public Transform whitePromotionQueen;
        // Black's, each optional: one without a mesh falls back on White's (above).
        public Transform blackPawn;
        public Transform blackKnight;
        public Transform blackBishop;
        public Transform blackRook;
        public Transform blackQueen;
        public Transform blackKing;
        public Transform blackPromotionKnight;
        public Transform blackPromotionBishop;
        public Transform blackPromotionRook;
        public Transform blackPromotionQueen;

        void Start() {
            for (int i = 0; i < transform.childCount; i++) {
                GameObject child = transform.GetChild(i).gameObject;
                if (child.activeSelf) child.SetActive(false);
            }
        }

        // The template for a ChessRules colour and piece type; null for none.
        public Transform _Template(int color, int type) {
            if (color == ChessRules.White) {
                switch (type) {
                    case ChessRules.Pawn: return whitePawn;
                    case ChessRules.Knight: return whiteKnight;
                    case ChessRules.Bishop: return whiteBishop;
                    case ChessRules.Rook: return whiteRook;
                    case ChessRules.Queen: return whiteQueen;
                    case ChessRules.King: return whiteKing;
                }
            } else if (color == ChessRules.Black) {
                switch (type) {
                    case ChessRules.Pawn: return blackPawn;
                    case ChessRules.Knight: return blackKnight;
                    case ChessRules.Bishop: return blackBishop;
                    case ChessRules.Rook: return blackRook;
                    case ChessRules.Queen: return blackQueen;
                    case ChessRules.King: return blackKing;
                }
            }
            return null;
        }

        // The promotion template for a ChessRules colour and a piece type a pawn can become (knight, bishop, rook or
        // queen); null for none.
        public Transform _PromotionTemplate(int color, int type) {
            if (color == ChessRules.White) {
                switch (type) {
                    case ChessRules.Knight: return whitePromotionKnight;
                    case ChessRules.Bishop: return whitePromotionBishop;
                    case ChessRules.Rook: return whitePromotionRook;
                    case ChessRules.Queen: return whitePromotionQueen;
                }
            } else if (color == ChessRules.Black) {
                switch (type) {
                    case ChessRules.Knight: return blackPromotionKnight;
                    case ChessRules.Bishop: return blackPromotionBishop;
                    case ChessRules.Rook: return blackPromotionRook;
                    case ChessRules.Queen: return blackPromotionQueen;
                }
            }
            return null;
        }

        // What's missing, for a log: "" when every White template (the promotion ones too) has a mesh and a renderer.
        // Black's are optional.
        public string Problem() {
            for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) {
                string problem = TemplateProblem(_Template(ChessRules.White, type), "White " + _TypeName(type));
                if (problem.Length > 0) return problem;
            }
            for (int type = ChessRules.Knight; type <= ChessRules.Queen; type++) {
                string problem = TemplateProblem(_PromotionTemplate(ChessRules.White, type),
                    "White promotion " + _TypeName(type));
                if (problem.Length > 0) return problem;
            }
            return "";
        }

        string TemplateProblem(Transform template, string name) {
            if (!Utilities.IsValid(template)) return "no " + name + " template";
            if (!_HasMesh(template)) return "the " + name + " template has no mesh";
            if (!Utilities.IsValid(template.GetComponent<MeshRenderer>())) {
                return "the " + name + " template has no MeshRenderer";
            }
            return "";
        }

        // Whether template is there with a mesh in its MeshFilter: a Black one without falls back on White's.
        public static bool _HasMesh(Transform template) {
            if (!Utilities.IsValid(template)) return false;
            MeshFilter filter = template.GetComponent<MeshFilter>();
            return Utilities.IsValid(filter) && Utilities.IsValid(filter.sharedMesh);
        }

        public static string _TypeName(int type) {
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
