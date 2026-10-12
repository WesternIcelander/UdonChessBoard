using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
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
        // No castle drop space (ChessBoard.CastleKingSide and CastleQueenSide are the others).
        public const int NoCastleSide = -1;

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
        // The castle drop spaces (docs/player-input.md), by side (ChessBoard.CastleKingSide, CastleQueenSide): pads
        // the size of a square just off the board, beside the king's castling destination on the outer side of its
        // back rank. Shown only while the local player holds a king whose castle on that side needs one.
        public GameObject[] castleSpaces = new GameObject[2];
        // Desktop mode's click targets (step 9.3c): each piece's ChessPiece (the same index as pieces), and the
        // ChessSquare of each square (by square, each its own collider, active only while a click on it does something)
        // and of each castle drop space (by side, on the space's object).
        public ChessPiece[] pieceBehaviours = new ChessPiece[PieceCount];
        public ChessSquare[] squareTargets = new ChessSquare[64];
        public ChessSquare[] castleSpaceTargets = new ChessSquare[2];
        // How far a selected piece is lifted above its square, in squares.
        public const float SelectLift = 0.5f;
        // The board's buttons (docs/player-input.md, Buttons; step 9.4a), by what they do. Each is shown only while
        // the board's Can... check for it holds for the local player.
        public const int ButtonJoinWhite = 0;
        public const int ButtonJoinBlack = 1;
        public const int ButtonMode = 2;
        public const int ButtonClock = 3;
        public const int ButtonStart = 4;
        public const int ButtonContinue = 5;
        public const int ButtonReset = 6;
        public const int ButtonCount = 7;
        // The button objects in the world (any number: the builder puts a panel at each end of the table), with the
        // Button* action of each and its label at the same index. A button's UI Button calls the view's Press...
        // event for its action.
        public GameObject[] buttons = new GameObject[0];
        public int[] buttonActions = new int[0];
        public Text[] buttonLabels = new Text[0];
        // The time controls the Clock button steps through, in seconds (base ChessArbiter.NoClock for none).
        public int[] clockBases = { ChessArbiter.NoClock, 60, 180, 300, 600, 900, 1800 };
        public int[] clockIncrements = { 0, 0, 2, 3, 0, 10, 0 };

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

        // The object the local player holds, picked up with the board's consent (NoObject for none), and the square it
        // was picked up from. It isn't placed while held; it's let go when the board stops showing it on that square or
        // the local player may no longer touch the pieces.
        [NonSerialized] public int heldObject = NoObject;
        [NonSerialized] public int heldFrom = ChessRules.NoSquare;
        // A promotion waiting for its piece: the pawn's object, its from and to squares (NoObject when none). The pawn
        // stands on to meanwhile; ChoosePromotion plays it, and any change to the board (but activity) cancels it.
        [NonSerialized] public int promotionObject = NoObject;
        [NonSerialized] public int promotionFrom = ChessRules.NoSquare;
        [NonSerialized] public int promotionTo = ChessRules.NoSquare;
        // Which castle drop spaces are shown (by side), for the held or selected king's colour.
        [NonSerialized] public bool[] castleSpaceShown = new bool[2];
        // Desktop mode: the object the local player has selected by clicking, with the board's consent (NoObject for
        // none), the square it was selected on and whether that was in replay. It stands lifted over its square until
        // a click places it or puts it back, or the board changes so that it can no longer be picked up there.
        [NonSerialized] public int selectedObject = NoObject;
        [NonSerialized] public int selectedFrom = ChessRules.NoSquare;
        bool selectedInReplay;
        // The squares a click places the selected piece on: its legal moves' (a castle's rook square and, when the
        // king moves, its destination too), or in replay every other square.
        [NonSerialized] public bool[] selectedTargets = new bool[64];
        int[] targetMoves = new int[ChessMoves.MaxMoves];

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
                // Being an organizer and the board's idle time (for Reset) change without the board changing.
                UpdateButtons();
            }
        }

        // A move one ply on from the position the mapping shows moves its objects now, so the right object travels
        // (and, from step 9.5b, animates); everything is checked against the board and placed on the next frame.
        public override void OnBoardChanged() {
            dirty = true;
            if (!Utilities.IsValid(board)) return;
            int change = board.lastChange;
            // The position the promotion was asked in may be gone; a touch from another client changed nothing.
            if (change != ChessBoard.ChangeActivity) promotionObject = NoObject;
            if (mappingStale) return;
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
            // A held piece the board no longer shows where it was picked up is let go, and placed with the rest.
            if (heldObject != NoObject && objectSquare[heldObject] != heldFrom) LetGo();
            // So does a selected piece that can no longer be picked up there; one that still can has its targets
            // worked out again, since the position may have changed (a takeback, another player's free move).
            CheckSelection();
            if (selectedObject != NoObject) FindTargets();
            for (int i = 0; i < PieceCount; i++) {
                if (i != heldObject) PlaceObject(i);
            }
            UpdatePickupable();
            // The castle a held king could make may have changed (or the game ended, or replay began).
            UpdateCastleSpaces();
            UpdateButtons();
        }

        // ---- Input (docs/player-input.md) ----

        // A piece picked up by the local player (its ChessPiece's OnPickup): asks the board, and lets go of the piece
        // when it says no; the board's rejection then puts it back. A promotion waiting for its piece is dropped.
        public void OnPiecePickedUp(int obj) {
            if (obj < 0 || obj >= PieceCount) return;
            promotionObject = NoObject;
            heldObject = NoObject;
            int square = objectSquare[obj];
            if (Utilities.IsValid(board) && square != ChessRules.NoSquare
                && board.PiecePickedUp(square) == ChessBoard.InputAccepted) {
                heldObject = obj;
                heldFrom = square;
                UpdateCastleSpaces();
                return;
            }
            // A piece off the board (never a pickup) is just put back; the board told its rejection itself.
            dirty = true;
            UpdateCastleSpaces();
            LetGoOf(obj);
        }

        // A piece let go by the local player (its ChessPiece's OnDrop): forwards the drop on the square under it (off
        // the board too, which the board rejects) if the board accepted its pickup, or a king's drop on a castle drop
        // space shown for it as that castle. A promotion waits for ChoosePromotion with the pawn standing on its
        // square; anything else is placed from the board after its change, so a rejected drop goes back where it was.
        public void OnPieceDropped(int obj) {
            if (obj < 0 || obj >= PieceCount) return;
            dirty = true;
            if (obj != heldObject) return;
            if (!Utilities.IsValid(board) || !Utilities.IsValid(pieces[obj])) {
                heldObject = NoObject;
                UpdateCastleSpaces();
                return;
            }
            Vector3 position = pieces[obj].position;
            int castleSide = CastleSpaceUnder(position, ObjectColor(obj));
            heldObject = NoObject;
            UpdateCastleSpaces();
            if (castleSide != NoCastleSide) {
                board.KingDroppedOnCastleSpace(castleSide);
                return;
            }
            int to = DropSquare(position);
            int from = heldFrom;
            if (board.PieceDropped(from, to) == ChessBoard.InputPromotionNeeded) {
                promotionObject = obj;
                promotionFrom = from;
                promotionTo = to;
            }
        }

        // The promotion picker's answer (step 9.5a): plays the waiting promotion with pieceType (ChessRules.Queen, Rook,
        // Bishop or Knight). Any other type is rejected by the board, which puts the pawn back. InputRejected with no
        // promotion waiting.
        public int ChoosePromotion(int pieceType) {
            if (promotionObject == NoObject || !Utilities.IsValid(board)) return ChessBoard.InputRejected;
            promotionObject = NoObject;
            dirty = true;
            return board.PromotionChosen(promotionFrom, promotionTo, pieceType);
        }

        // The picker closed without a choice: the pawn goes back.
        public void CancelPromotion() {
            ChoosePromotion(ChessRules.NoPieceType);
        }

        // The square under a point (world space) on the playing surface, by where it is over the board whatever its
        // height; NoSquare outside the board.
        public int DropSquare(Vector3 position) {
            Vector3 local = transform.InverseTransformPoint(position);
            int file = Mathf.FloorToInt(local.x / squareSize + 4);
            int rank = Mathf.FloorToInt(local.z / squareSize + 4);
            if (file < 0 || file > 7 || rank < 0 || rank > 7) return ChessRules.NoSquare;
            return ChessRules.SquareOf(file, rank);
        }

        // ---- Castle drop space (docs/player-input.md) ----

        // Shows each castle drop space while the local player holds the side to move's king and the board says that
        // castle needs one (it's legal, and a drop on its destination wouldn't castle), beside the destination on the
        // outer side of the king's back rank; hides it otherwise. Called on every pickup, drop and change.
        // A selected king (desktop mode) shows them the same way, and they're then click targets.
        public void UpdateCastleSpaces() {
            int lifted = heldObject != NoObject ? heldObject : selectedObject;
            bool king = lifted != NoObject && objectType[lifted] == ChessRules.King && Utilities.IsValid(board);
            for (int side = 0; side < 2; side++) {
                bool shown = king && board.IsCastleDropSpaceNeeded(side);
                castleSpaceShown[side] = shown;
                GameObject space = castleSpaces[side];
                if (!Utilities.IsValid(space)) continue;
                if (shown) space.transform.localPosition = CastleSpacePosition(ObjectColor(lifted), side);
                if (space.activeSelf != shown) space.SetActive(shown);
                ChessSquare target = castleSpaceTargets[side];
                if (!Utilities.IsValid(target)) continue;
                bool clickable = shown && lifted == selectedObject;
                if (target.DisableInteractive == clickable) target.DisableInteractive = !clickable;
                if (clickable) target.InteractionText = "Castle";
            }
        }

        // The centre of color's castle drop space for side (ChessBoard.CastleKingSide or CastleQueenSide), in the
        // view's local space: the square off the board next to the castling destination (g or c) on the back rank.
        public Vector3 CastleSpacePosition(int color, int side) {
            int file = side == ChessBoard.CastleKingSide ? ChessRules.FileG : ChessRules.FileC;
            int rank = color == ChessRules.White ? -1 : 8;
            return new Vector3((file - 3.5f) * squareSize, 0, (rank - 3.5f) * squareSize);
        }

        // The side of the castle drop space shown for color's king under a point (world space), by where it is over
        // the space whatever its height; NoCastleSide when there's none.
        public int CastleSpaceUnder(Vector3 position, int color) {
            Vector3 local = transform.InverseTransformPoint(position);
            for (int side = 0; side < 2; side++) {
                if (!castleSpaceShown[side]) continue;
                Vector3 centre = CastleSpacePosition(color, side);
                if (Mathf.Abs(local.x - centre.x) <= squareSize / 2 && Mathf.Abs(local.z - centre.z) <= squareSize / 2) {
                    return side;
                }
            }
            return NoCastleSide;
        }

        // ---- Desktop mode (docs/player-input.md, step 9.3c) ----
        // Clicks instead of carrying, for players not using pickups: selecting is PiecePickedUp and placing is
        // PieceDropped (or KingDroppedOnCastleSpace), so the board treats both input styles alike.

        // A click on a piece (its ChessPiece's Interact). With nothing selected it selects the piece; on the selected
        // piece it puts it back; on a square the selected piece can go to it places it there (a capture, or a castle on
        // its own rook; in replay, a free move onto the other colour); on another piece the board would let the player
        // pick up it selects that one instead; anything else is a placing the board rejects, which puts it back.
        public void OnPieceClicked(int obj) {
            if (obj < 0 || obj >= PieceCount || UsesPickups() || !Utilities.IsValid(board)) return;
            int square = objectSquare[obj];
            if (square == ChessRules.NoSquare) return;
            if (selectedObject == NoObject) {
                Select(obj);
            } else if (obj == selectedObject) {
                Unselect();
            } else if (PlacesOn(obj)) {
                Place(square);
            } else if (board.CanPickUp(square)) {
                Select(obj);
            } else {
                Place(square);
            }
        }

        // A click on a square's target (only active while a piece is selected): on the square the selected piece came
        // from it puts it back, anywhere else it places it there.
        public void OnSquareClicked(int square) {
            if (square < 0 || square >= 64 || selectedObject == NoObject || UsesPickups()) return;
            if (!Utilities.IsValid(board)) return;
            if (square == selectedFrom) {
                Unselect();
            } else {
                Place(square);
            }
        }

        // A click on a castle drop space shown for the selected king: castles on that side, as a drop on it would.
        public void OnCastleSpaceClicked(int side) {
            if (side < 0 || side > 1 || selectedObject == NoObject || !castleSpaceShown[side]) return;
            if (!Utilities.IsValid(board)) return;
            ClearSelection();
            UpdateCastleSpaces();
            board.KingDroppedOnCastleSpace(side);
        }

        // Selects obj if the board accepts its pickup (the previous selection going back down), taking the piece's
        // ownership as a grab would, so its VRCObjectSync shows the lift to everyone. A refused pickup leaves nothing
        // selected; the board's rejection puts everything back.
        void Select(int obj) {
            promotionObject = NoObject;
            ClearSelection();
            int square = objectSquare[obj];
            if (board.PiecePickedUp(square) == ChessBoard.InputAccepted) {
                selectedObject = obj;
                selectedFrom = square;
                selectedInReplay = board.replayMode;
                FindTargets();
                dirty = true;
                VRCPlayerApi local = Networking.LocalPlayer;
                GameObject piece = Utilities.IsValid(pieces[obj]) ? pieces[obj].gameObject : null;
                if (Utilities.IsValid(local) && Utilities.IsValid(piece) && !Networking.IsOwner(local, piece)) {
                    Networking.SetOwner(local, piece);
                }
            }
            UpdateCastleSpaces();
            UpdateClickables();
        }

        // Puts the selected piece back down where it was, unselected; the board isn't asked.
        void Unselect() {
            ClearSelection();
            UpdateCastleSpaces();
            UpdateClickables();
        }

        // Plays the selected piece's move to square: a promotion waits for ChoosePromotion as a dropped one does, with
        // the pawn standing on square; anything else is placed from the board after its change, so a rejected placing
        // puts the piece back, unselected.
        void Place(int square) {
            int obj = selectedObject;
            int from = selectedFrom;
            ClearSelection();
            UpdateCastleSpaces();
            if (board.PieceDropped(from, square) == ChessBoard.InputPromotionNeeded) {
                promotionObject = obj;
                promotionFrom = from;
                promotionTo = square;
            }
            UpdateClickables();
        }

        // Nothing selected any more; the piece is placed back down on the next frame.
        void ClearSelection() {
            if (selectedObject == NoObject) return;
            selectedObject = NoObject;
            selectedFrom = ChessRules.NoSquare;
            for (int i = 0; i < 64; i++) selectedTargets[i] = false;
            dirty = true;
        }

        // Puts a selected piece back down, unselected, once it can no longer be picked up where it was: the board
        // changed (a reset, the game ending, replay entered or left, the piece's square taken, the turn passing), or
        // the local player switched to pickups.
        void CheckSelection() {
            if (selectedObject == NoObject) return;
            if (!UsesPickups() && Utilities.IsValid(board) && objectSquare[selectedObject] == selectedFrom
                && board.replayMode == selectedInReplay && board.CanPickUp(selectedFrom)) {
                return;
            }
            ClearSelection();
            UpdateCastleSpaces();
        }

        // Whether a click on obj places the selected piece on its square rather than selecting obj: the square is a
        // target and obj isn't one of the selected piece's own (outside replay, a target with an own piece on it is
        // a castling rook).
        bool PlacesOn(int obj) {
            int square = objectSquare[obj];
            if (selectedObject == NoObject || square == ChessRules.NoSquare || !selectedTargets[square]) return false;
            return !selectedInReplay || ObjectColor(obj) != ObjectColor(selectedObject);
        }

        // Works out selectedTargets for the selected piece from the board's legal moves (every other square in replay,
        // where moves are free).
        void FindTargets() {
            for (int i = 0; i < 64; i++) selectedTargets[i] = selectedInReplay && i != selectedFrom;
            if (selectedInReplay) return;
            int count = board.LegalMovesFrom(selectedFrom, targetMoves);
            for (int i = 0; i < count; i++) {
                int move = targetMoves[i];
                int to = ChessRules.MoveTo(move);
                selectedTargets[to] = true;
                if (!ChessRules.MoveIsCastle(move)) continue;
                // A castle stores its rook's square; the board also castles on a drop on the king's destination,
                // unless a normal move ends there (which is then a target anyway) or the king doesn't move.
                int destination = ChessRules.SquareOf(
                    ChessRules.FileOf(to) > ChessRules.FileOf(selectedFrom) ? ChessRules.FileG : ChessRules.FileC,
                    ChessRules.RankOf(selectedFrom));
                if (destination != selectedFrom) selectedTargets[destination] = true;
            }
        }

        // Makes the pieces and squares click targets exactly where a click does something for the local player in
        // desktop mode, with interact text saying what: with nothing selected, the pieces the board would let them
        // pick up; with one selected, that piece and its square (put back), its targets (pieces to capture or castle
        // with, squares to place on) and the other pieces they could select instead. Players using pickups get none.
        // Called after every change, once a second and on every click.
        public void UpdateClickables() {
            bool clicks = !UsesPickups() && Utilities.IsValid(board);
            for (int obj = 0; obj < PieceCount; obj++) {
                ChessPiece behaviour = pieceBehaviours[obj];
                if (!Utilities.IsValid(behaviour)) continue;
                int square = objectSquare[obj];
                string text = null;
                if (clicks && square != ChessRules.NoSquare) {
                    if (obj == selectedObject) {
                        text = "Put back";
                    } else if (PlacesOn(obj)) {
                        text = ObjectColor(obj) == ObjectColor(selectedObject) ? "Castle" : "Capture";
                    } else if (board.CanPickUp(square)) {
                        text = "Select";
                    }
                }
                bool clickable = text != null;
                if (behaviour.DisableInteractive == clickable) behaviour.DisableInteractive = !clickable;
                if (clickable) behaviour.InteractionText = text;
            }
            bool selected = clicks && selectedObject != NoObject;
            for (int square = 0; square < 64; square++) {
                ChessSquare target = squareTargets[square];
                if (!Utilities.IsValid(target)) continue;
                bool clickable = selected && (square == selectedFrom || selectedTargets[square]);
                GameObject targetObject = target.gameObject;
                if (targetObject.activeSelf != clickable) targetObject.SetActive(clickable);
                if (clickable) target.InteractionText = square == selectedFrom ? "Put back" : "Place";
            }
        }

        // Whether obj can be clicked now (desktop mode), for tests.
        public bool IsPieceClickable(int obj) {
            ChessPiece behaviour = pieceBehaviours[obj];
            return Utilities.IsValid(behaviour) && !behaviour.DisableInteractive;
        }

        public bool IsSquareClickable(int square) {
            ChessSquare target = squareTargets[square];
            return Utilities.IsValid(target) && target.gameObject.activeSelf;
        }

        // Stops treating a piece as held and takes it out of the local player's hand, to be placed from the board.
        void LetGo() {
            int obj = heldObject;
            heldObject = NoObject;
            UpdateCastleSpaces();
            LetGoOf(obj);
        }

        void LetGoOf(int obj) {
            VRCPickup pickup = piecePickups[obj];
            // Its OnDrop is then ignored, since it's no longer heldObject.
            if (Utilities.IsValid(pickup) && pickup.IsHeld) pickup.Drop();
        }

        // ---- Buttons (docs/player-input.md, Buttons; step 9.4a) ----
        // Each button is shown only while its action applies to the local player (the board's Can... check), and
        // pressing it calls the board's method, which refuses whenever the check doesn't hold.

        // The UI Buttons' events, one per action.
        public void PressJoinWhite() {
            PressButton(ButtonJoinWhite);
        }

        public void PressJoinBlack() {
            PressButton(ButtonJoinBlack);
        }

        public void PressMode() {
            PressButton(ButtonMode);
        }

        public void PressClock() {
            PressButton(ButtonClock);
        }

        public void PressStart() {
            PressButton(ButtonStart);
        }

        public void PressContinue() {
            PressButton(ButtonContinue);
        }

        public void PressReset() {
            PressButton(ButtonReset);
        }

        // Does what the button for action does, for the local player: the board's answer (InputRejected for an
        // unknown action). Mode switches between Standard and Chess960; Clock picks the next of clockBases and
        // clockIncrements after the board's time control (the first when it isn't one of them).
        public int PressButton(int action) {
            if (!Utilities.IsValid(board)) return ChessBoard.InputRejected;
            int result = ChessBoard.InputRejected;
            if (action == ButtonJoinWhite) {
                result = board.JoinWhite();
            } else if (action == ButtonJoinBlack) {
                result = board.JoinBlack();
            } else if (action == ButtonMode) {
                result = board.SetMode(board.mode == ChessGame.ModeChess960
                    ? ChessGame.ModeStandard : ChessGame.ModeChess960);
            } else if (action == ButtonClock) {
                int next = NextClockChoice();
                if (next >= 0) result = board.SetClock(clockBases[next], clockIncrements[next]);
            } else if (action == ButtonStart) {
                result = board.StartGame();
            } else if (action == ButtonContinue) {
                result = board.ContinueGame();
            } else if (action == ButtonReset) {
                result = board.ResetBoard();
            }
            // A refusal tells nobody, so the buttons are brought up to date here too.
            UpdateButtons();
            return result;
        }

        // The index in clockBases of the choice after the board's time control; -1 with no choices.
        int NextClockChoice() {
            int count = Mathf.Min(clockBases.Length, clockIncrements.Length);
            if (count == 0) return -1;
            for (int i = 0; i < count; i++) {
                if (clockBases[i] == board.baseTime && clockIncrements[i] == board.increment) return (i + 1) % count;
            }
            return 0;
        }

        // Whether the button for action applies to the local player now: its Can... check on the board.
        public bool ButtonApplies(int action) {
            if (!Utilities.IsValid(board)) return false;
            if (action == ButtonJoinWhite) return board.CanJoin(ChessRules.White);
            if (action == ButtonJoinBlack) return board.CanJoin(ChessRules.Black);
            if (action == ButtonMode) return board.CanSetMode();
            if (action == ButtonClock) return board.CanSetClock();
            if (action == ButtonStart) return board.CanStartGame();
            if (action == ButtonContinue) return board.CanContinue();
            if (action == ButtonReset) return board.CanReset();
            return false;
        }

        // The label of the button for action: Mode and Clock show the board's current choice.
        public string ButtonText(int action) {
            if (action == ButtonJoinWhite) return "Join White";
            if (action == ButtonJoinBlack) return "Join Black";
            if (action == ButtonMode) {
                bool chess960 = Utilities.IsValid(board) && board.mode == ChessGame.ModeChess960;
                return "Mode: " + (chess960 ? "Chess960" : "Standard");
            }
            if (action == ButtonClock) {
                if (!Utilities.IsValid(board)) return "Clock";
                return "Clock: " + ClockText(board.baseTime, board.increment);
            }
            if (action == ButtonStart) return "Start";
            if (action == ButtonContinue) return "Continue from here";
            if (action == ButtonReset) return "Reset";
            return "";
        }

        // A time control as players say it: "No clock", "5+3" (minutes plus seconds), or "45s+0" for a base that
        // isn't whole minutes.
        public static string ClockText(int baseSeconds, int incrementSeconds) {
            if (baseSeconds == ChessArbiter.NoClock) return "No clock";
            string baseText = baseSeconds % 60 == 0 ? (baseSeconds / 60).ToString() : baseSeconds + "s";
            return baseText + "+" + incrementSeconds;
        }

        // Shows each button whose action applies to the local player and hides the rest, with its label. Called on
        // every Refresh, once a second and after every press.
        public void UpdateButtons() {
            for (int i = 0; i < buttons.Length; i++) {
                GameObject button = buttons[i];
                if (!Utilities.IsValid(button) || i >= buttonActions.Length) continue;
                int action = buttonActions[i];
                bool shown = ButtonApplies(action);
                if (button.activeSelf != shown) button.SetActive(shown);
                if (!shown || i >= buttonLabels.Length || !Utilities.IsValid(buttonLabels[i])) continue;
                string text = ButtonText(action);
                if (buttonLabels[i].text != text) buttonLabels[i].text = text;
            }
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
            // A piece the local player may no longer touch leaves their hand.
            if (!allowed && heldObject != NoObject) {
                LetGo();
                dirty = true;
            }
            for (int obj = 0; obj < PieceCount; obj++) {
                VRCPickup pickup = piecePickups[obj];
                if (!Utilities.IsValid(pickup)) continue;
                bool pickupable = allowed && objectSquare[obj] != ChessRules.NoSquare;
                if (pickup.pickupable != pickupable) pickup.pickupable = pickupable;
            }
            // Desktop mode's selection and click targets depend on the same things (and on whose turn it is).
            CheckSelection();
            UpdateClickables();
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
            // A pawn waiting for its promotion piece stands where it was dropped.
            if (obj == promotionObject) square = promotionTo;
            piece.localPosition = square != ChessRules.NoSquare ? SquarePosition(square) : SparePosition(obj);
            // A selected piece (desktop mode) stands lifted over its square.
            if (obj == selectedObject) piece.localPosition += new Vector3(0, SelectLift * squareSize, 0);
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
