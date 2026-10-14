using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // A set of chess piece models (step 12.3, docs/customizing.md): one template object per piece type and colour, each
    // with the model's MeshFilter and MeshRenderer and its materials (every slot) for that colour. A template's local
    // position, rotation and scale (under its parent, which stands for the square's centre on the playing surface) are
    // the piece's offset, rotation and size on its square, in the view's local units: a model facing any way, of any
    // size or with its origin off its base is set up by moving, turning and scaling its template where it can be seen.
    // The builder puts each template in a slot of its own with a one-square guide beside the others.
    //
    // Board views read the templates and copy them onto their piece objects (ChessBoardView.SetChessPieces, or the
    // arbiter's defaultPieceSet); the set itself changes nothing. On Start it hides its children (the slots, templates
    // and guides), which the views go on reading, so keep the set's own object active. No synced variables.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ChessPieceSet : UdonSharpBehaviour {
        public Transform whitePawn;
        public Transform whiteKnight;
        public Transform whiteBishop;
        public Transform whiteRook;
        public Transform whiteQueen;
        public Transform whiteKing;
        public Transform blackPawn;
        public Transform blackKnight;
        public Transform blackBishop;
        public Transform blackRook;
        public Transform blackQueen;
        public Transform blackKing;

        void Start() {
            for (int i = 0; i < transform.childCount; i++) {
                GameObject child = transform.GetChild(i).gameObject;
                if (child.activeSelf) child.SetActive(false);
            }
        }

        // The template for a ChessRules colour and piece type; null for none.
        public Transform Template(int color, int type) {
            bool white = color == ChessRules.White;
            switch (type) {
                case ChessRules.Pawn: return white ? whitePawn : blackPawn;
                case ChessRules.Knight: return white ? whiteKnight : blackKnight;
                case ChessRules.Bishop: return white ? whiteBishop : blackBishop;
                case ChessRules.Rook: return white ? whiteRook : blackRook;
                case ChessRules.Queen: return white ? whiteQueen : blackQueen;
                case ChessRules.King: return white ? whiteKing : blackKing;
            }
            return null;
        }

        // What's missing, for a log: "" when every template has a mesh and a renderer.
        public string Problem() {
            for (int color = ChessRules.White; color <= ChessRules.Black; color++) {
                for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) {
                    Transform template = Template(color, type);
                    string name = (color == ChessRules.White ? "white " : "black ") + TypeName(type);
                    if (!Utilities.IsValid(template)) return "no " + name + " template";
                    MeshFilter filter = template.GetComponent<MeshFilter>();
                    if (!Utilities.IsValid(filter) || !Utilities.IsValid(filter.sharedMesh)) {
                        return "the " + name + " template has no mesh";
                    }
                    if (!Utilities.IsValid(template.GetComponent<MeshRenderer>())) {
                        return "the " + name + " template has no MeshRenderer";
                    }
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
