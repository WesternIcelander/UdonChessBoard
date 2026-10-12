using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The physical board (docs/architecture.md, docs/physical-pieces.md): the 32 piece objects in the world, placed on
    // the squares the logical board says. Holds no game state and no synced fields; it reads the board after each
    // change. Built in the scene by Tools > Chess Board > Create Board View (Editor/ChessBoardViewBuilder.cs).
    //
    // The view's local space: the playing surface at y = 0, files a to h along +x, ranks 1 to 8 along +z (White at
    // -z), the board's centre at the origin.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ChessBoardView : ChessBoardListener {
        public const int PieceCount = 32;
        // Objects 0 to 15 are White's, 16 to 31 Black's: a side never has more than 16 pieces, since promotion turns
        // a pawn into another piece. Each object swaps its mesh to the piece it stands for.
        public const int PiecesPerColor = 16;
        public const int NoObject = -1;
        // How the local player handles the pieces (inputStyle): Auto is pickups in VR and clicks otherwise; the other
        // two force one style, for testing pickups on desktop clients (docs/testing.md, One VR client).
        public const int InputAuto = 0;
        public const int InputPickups = 1;
        public const int InputClicks = 2;

        // The board shown; the view adds itself as a listener on Start.
        public ChessBoard board;
        // The piece objects, children of this view, and their mesh filters (the same index).
        public Transform[] pieces = new Transform[PieceCount];
        public MeshFilter[] pieceFilters = new MeshFilter[PieceCount];
        // The mesh for each piece type, by ChessRules type (Pawn = 1 ... King = 6; entry 0 unused).
        public Mesh[] meshes = new Mesh[7];
        // Each piece object's pickup and collider (the same index). The collider is fitted to the piece's mesh.
        public VRCPickup[] piecePickups = new VRCPickup[PieceCount];
        public BoxCollider[] pieceColliders = new BoxCollider[PieceCount];
        // The width of a square, in the view's local units.
        public float squareSize = 0.05f;
        // InputAuto, InputPickups or InputClicks, the same for every client of a build; CycleInputStyle changes it on
        // one client.
        [Tooltip("Auto: pickups in VR, clicks on desktop and mobile. Pickups or Clicks: that for everyone, for testing.")]
        public int inputStyle = InputAuto;

        // Which object stands on each square (NoObject for none), and the other way round (NoSquare for an object
        // off the board: captured, or not needed by the position). objectType is the piece type each object stands
        // for (its mesh); its colour is fixed by its index.
        [NonSerialized] public int[] squareObject = new int[64];
        [NonSerialized] public int[] objectSquare = new int[PieceCount];
        [NonSerialized] public int[] objectType = new int[PieceCount];
        // The square each object last stood on, so a piece coming back to the board (a reset, stepping back in
        // replay) is taken from near where it is needed. NoSquare for never.
        int[] objectLastSquare = new int[PieceCount];
        // The type each object's mesh was last set to, so a mesh is only swapped when it changes.
        int[] objectMeshType = new int[PieceCount];

        // The position the mapping was last brought up to: replay or the game, its ply, and whether it was a custom
        // replay position. A move one ply on from it is applied to the mapping as it happens (ApplyMove), so the
        // object that moved is the one that was on its from square; anything else rebuilds the mapping from the board.
        int shownPly;
        bool shownReplay;
        bool shownCustom;
        // Whether the mapping may no longer be the position shown before the latest change (anything but a move), so
        // a move mustn't be applied to it.
        bool mappingStale = true;
        // The objects are placed on the frame after a change: a test making thousands of changes in one frame would
        // otherwise run past Udon's time limit.
        bool dirty;
        bool warnedShort;
        // When the pieces' pickupable is next brought up to date without a change: who may touch the pieces also
        // depends on whether the local player is an organizer, which the arbiter can change without telling the board.
        float nextPickupCheck;
        const float PickupCheckInterval = 1;

        void Start() {
            for (int i = 0; i < 64; i++) squareObject[i] = NoObject;
            for (int i = 0; i < PieceCount; i++) {
                objectSquare[i] = ChessRules.NoSquare;
                objectLastSquare[i] = ChessRules.NoSquare;
                objectType[i] = ChessRules.NoPieceType;
                objectMeshType[i] = -1;
            }
            if (!Utilities.IsValid(board)) {
                Debug.LogError("[ChessBoardView] " + gameObject.name + ": the Board field is empty.");
                return;
            }
            board.AddListener(this);
            Refresh();
        }

        void Update() {
            if (dirty) {
                Refresh();
            } else if (Time.time >= nextPickupCheck) {
                UpdatePickupable();
            }
        }

        // A move one ply on from the position the mapping shows moves its objects now, so the right object travels
        // (and, from step 9.5b, animates); everything is checked against the board and placed on the next frame.
        public override void OnBoardChanged() {
            dirty = true;
            if (mappingStale || !Utilities.IsValid(board)) return;
            int change = board.lastChange;
            bool replay = board.replayMode;
            bool oneOn;
            if (change == ChessBoard.ChangeMove) {
                oneOn = !replay && !shownReplay && board.moveCount == shownPly + 1;
            } else if (change == ChessBoard.ChangeReplayStep) {
                oneOn = replay && shownReplay && !shownCustom && !board.replayCustom
                    && board.replayIndex == shownPly + 1;
            } else {
                // Anything else is rebuilt from the board: a change received from another client can hold several.
                mappingStale = true;
                return;
            }
            int move = board.LastMove();
            if (!oneOn || move == 0 || !ApplyMove(move)) {
                mappingStale = true;
                return;
            }
            shownPly++;
        }

        // Brings the objects up to the board now: the mapping checked against the position shown (rebuilt where it
        // differs), the meshes swapped and every object placed. Called on the frame after a change; public for tests.
        public void Refresh() {
            dirty = false;
            if (!Utilities.IsValid(board)) return;
            bool replay = board.replayMode;
            byte[] shown = replay ? board.replayBoard : board.board;
            Reconcile(shown);
            shownReplay = replay;
            shownPly = replay ? board.replayIndex : board.moveCount;
            shownCustom = replay && board.replayCustom;
            mappingStale = false;
            for (int i = 0; i < PieceCount; i++) PlaceObject(i);
            UpdatePickupable();
        }

        // ---- Pickups (docs/physical-pieces.md) ----

        // Whether the local player handles the pieces as pickups (otherwise by clicking, step 9.3c).
        public bool UsesPickups() {
            if (inputStyle == InputPickups) return true;
            if (inputStyle == InputClicks) return false;
            VRCPlayerApi local = Networking.LocalPlayer;
            return Utilities.IsValid(local) && local.IsUserInVR();
        }

        // Whether player may touch the pieces (the board decides); asked by each piece's OnOwnershipRequest.
        public bool PlayerMayTouchPieces(VRCPlayerApi player) {
            return Utilities.IsValid(board) && board.PlayerMayTouchPieces(player);
        }

        // Makes the pieces on the board pickups for the local player while they use pickups and may touch the pieces;
        // pieces off the board never are. Called after every change and once a second.
        public void UpdatePickupable() {
            nextPickupCheck = Time.time + PickupCheckInterval;
            bool allowed = UsesPickups() && Utilities.IsValid(board) && board.MayTouchPieces();
            for (int obj = 0; obj < PieceCount; obj++) {
                VRCPickup pickup = piecePickups[obj];
                if (!Utilities.IsValid(pickup)) continue;
                bool pickupable = allowed && objectSquare[obj] != ChessRules.NoSquare;
                if (pickup.pickupable != pickupable) pickup.pickupable = pickupable;
            }
        }

        // The debug view's button: Auto, then Pickups, then Clicks, on this client only.
        public void CycleInputStyle() {
            SetInputStyle((inputStyle + 1) % 3);
        }

        public void SetInputStyle(int style) {
            inputStyle = style;
            UpdatePickupable();
        }

        public string InputStyleName() {
            if (inputStyle == InputPickups) return "Pickups";
            if (inputStyle == InputClicks) return "Clicks";
            return "Auto";
        }

        // ---- Mapping ----

        // Moves the objects of move (the position shown being the one before it): the mover to its square (its mesh
        // the promotion's), a capture off the board, castling's king and rook to their squares. False, changing
        // nothing, when the mapping doesn't have the mover where the move starts.
        bool ApplyMove(int move) {
            int from = ChessRules.MoveFrom(move);
            int to = ChessRules.MoveTo(move);
            int mover = squareObject[from];
            if (mover == NoObject || objectType[mover] != ChessRules.MovePieceType(move)) return false;
            if (ChessRules.MoveIsCastle(move)) {
                int rook = squareObject[to];
                if (rook == NoObject || objectType[rook] != ChessRules.Rook) return false;
                int rank = ChessRules.RankOf(from);
                bool kingSide = ChessRules.FileOf(to) > ChessRules.FileOf(from);
                int kingTo = ChessRules.SquareOf(kingSide ? ChessRules.FileG : ChessRules.FileC, rank);
                int rookTo = ChessRules.SquareOf(kingSide ? ChessRules.FileF : ChessRules.FileD, rank);
                // Both leave before either lands: in Chess960 a destination can be the other's starting square.
                Unmap(from);
                Unmap(to);
                if (squareObject[kingTo] != NoObject || squareObject[rookTo] != NoObject) return false;
                Map(mover, kingTo);
                Map(rook, rookTo);
                return true;
            }
            int captureSquare = to;
            if (ChessRules.IsEnPassantCapture(move)) {
                captureSquare = ChessRules.SquareOf(ChessRules.FileOf(to), ChessRules.RankOf(from));
            }
            Unmap(captureSquare);
            Unmap(from);
            Map(mover, to);
            int promotion = ChessRules.MovePromotion(move);
            if (promotion != ChessRules.NoPieceType) objectType[mover] = promotion;
            return true;
        }

        // Makes the mapping the position shown: objects standing on a square that holds something else leave the
        // board, then each square without its object gets a free one of its colour, preferring one already of its
        // type, then the one that stood nearest.
        void Reconcile(byte[] shown) {
            for (int square = 0; square < 64; square++) {
                int obj = squareObject[square];
                if (obj != NoObject && ObjectPiece(obj) != shown[square]) Unmap(square);
            }
            for (int square = 0; square < 64; square++) {
                int piece = shown[square];
                if (piece == ChessRules.Empty || squareObject[square] != NoObject) continue;
                int type = ChessRules.PieceTypeOf(piece);
                int first = ChessRules.PieceColorOf(piece) == ChessRules.White ? 0 : PiecesPerColor;
                int best = NoObject;
                int bestScore = int.MaxValue;
                for (int obj = first; obj < first + PiecesPerColor; obj++) {
                    if (objectSquare[obj] != ChessRules.NoSquare) continue;
                    int score = (objectType[obj] == type ? 0 : 1000) + Distance(objectLastSquare[obj], square);
                    if (score < bestScore) {
                        best = obj;
                        bestScore = score;
                    }
                }
                if (best == NoObject) {
                    // More than 16 pieces of a colour (only a hand-made position can have that): not shown.
                    if (!warnedShort) {
                        Debug.LogWarning("[ChessBoardView] " + gameObject.name + ": more than " + PiecesPerColor
                            + " pieces of one colour; the extra ones aren't shown.");
                        warnedShort = true;
                    }
                    continue;
                }
                Map(best, square);
                objectType[best] = type;
            }
        }

        void Map(int obj, int square) {
            squareObject[square] = obj;
            objectSquare[obj] = square;
            objectLastSquare[obj] = square;
        }

        void Unmap(int square) {
            int obj = squareObject[square];
            if (obj == NoObject) return;
            squareObject[square] = NoObject;
            objectSquare[obj] = ChessRules.NoSquare;
        }

        // The piece object obj stands for (ChessRules piece encoding), whether on the board or not.
        public int ObjectPiece(int obj) {
            if (objectType[obj] == ChessRules.NoPieceType) return ChessRules.Empty;
            return ChessRules.PieceOf(ObjectColor(obj), objectType[obj]);
        }

        public int ObjectColor(int obj) {
            return obj < PiecesPerColor ? ChessRules.White : ChessRules.Black;
        }

        // Squares apart (the larger of the file and rank distances); large for no square.
        int Distance(int a, int b) {
            if (a == ChessRules.NoSquare || b == ChessRules.NoSquare) return 100;
            int files = ChessRules.FileOf(a) - ChessRules.FileOf(b);
            int ranks = ChessRules.RankOf(a) - ChessRules.RankOf(b);
            if (files < 0) files = -files;
            if (ranks < 0) ranks = -ranks;
            return files > ranks ? files : ranks;
        }

        // ---- Placing ----

        // Puts obj where the mapping says, with its mesh: on its square, or beside the board when it's off it.
        // Black's pieces face the other way, for the knights.
        void PlaceObject(int obj) {
            Transform piece = pieces[obj];
            if (!Utilities.IsValid(piece)) return;
            int type = objectType[obj];
            if (type != objectMeshType[obj] && type != ChessRules.NoPieceType && Utilities.IsValid(pieceFilters[obj])) {
                Mesh mesh = meshes[type];
                pieceFilters[obj].sharedMesh = mesh;
                objectMeshType[obj] = type;
                BoxCollider box = pieceColliders[obj];
                if (Utilities.IsValid(box) && Utilities.IsValid(mesh)) {
                    box.center = mesh.bounds.center;
                    box.size = mesh.bounds.size;
                }
            }
            int square = objectSquare[obj];
            piece.localPosition = square != ChessRules.NoSquare ? SquarePosition(square) : SparePosition(obj);
            piece.localRotation = ObjectColor(obj) == ChessRules.White
                ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
        }

        // The centre of a square on the playing surface, in the view's local space.
        public Vector3 SquarePosition(int square) {
            return new Vector3((ChessRules.FileOf(square) - 3.5f) * squareSize, 0,
                (ChessRules.RankOf(square) - 3.5f) * squareSize);
        }

        // Where an object off the board stands: its colour's pieces off the board in two columns beside the board,
        // White's to White's right (+x) from White's end, Black's to Black's right (-x) from Black's end, in object
        // order.
        public Vector3 SparePosition(int obj) {
            int color = ObjectColor(obj);
            int first = color == ChessRules.White ? 0 : PiecesPerColor;
            int slot = 0;
            for (int i = first; i < obj; i++) {
                if (objectSquare[i] == ChessRules.NoSquare) slot++;
            }
            float column = 4.75f + (slot / 8) * 0.9f;
            float row = (slot % 8) - 3.5f;
            if (color == ChessRules.White) return new Vector3(column * squareSize, 0, row * squareSize);
            return new Vector3(-column * squareSize, 0, -row * squareSize);
        }
    }
}
