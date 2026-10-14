using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The physical board (docs/architecture.md, docs/physical-pieces.md): the 32 piece objects in the world, placed on
    // the squares the logical board says, with what goes with them on the table: pickups and clicks, highlights,
    // animations, the promotion picker and the player blocker. The buttons, the status display and the PGN box are the
    // UI's (ChessBoardUI, step 12.1), which this view doesn't know. Holds no game state and no synced fields; it reads
    // the board after each change. Built in the scene by Tools > Chess Board > Create Board View
    // (Editor/ChessBoardViewBuilder.cs).
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
        // Desktop mode's click targets (step 9.3c): each piece's ChessPiece (the same index as pieces), and the
        // ChessSquare of each square (by square, each its own collider, active only while a click on it does something).
        public ChessPiece[] pieceBehaviours = new ChessPiece[PieceCount];
        public ChessSquare[] squareTargets = new ChessSquare[64];
        // How far a selected piece is lifted above its square, in squares.
        public const float SelectLift = 0.5f;
        // The promotion picker (step 9.5a): a panel of buttons (Queen, Rook, Bishop, Knight, Cancel, each calling its
        // PressPromote... event), a child of this view, shown only to the local player while their promotion waits for
        // its piece, standing over the promotion square and facing the promoting side.
        public GameObject promotionPicker;
        // How high the picker's centre stands over the board, and how far it may stand off the board's centre along
        // the files, in squares (over the a and h files it would hang off the board).
        public const float PickerHeight = 2.2f;
        public const float PickerMaxOffset = 2;
        // Highlights (step 9.5a), children of this view lying on the board: the last move's two squares (objects
        // moved onto them, hidden for none), and one legal-target mark per square, standing over it, shown for the held
        // or selected piece's targets.
        public GameObject[] lastMoveHighlights = new GameObject[2];
        public GameObject[] targetHighlights = new GameObject[64];
        // The table, the board and the buttons have no solid colliders, so the players can walk up to and into the
        // board. The blocker, a solid box filling the table up to its top, keeps everyone else out: enabled on each
        // client unless the local player has a seat (UpdateBlocker). Players collide only on their own client, so
        // each client's blocker stops only its own player. Below the surface, so it doesn't catch pointing or clicks.
        public Collider blocker;
        // Move animations (step 9.5b): after a change that moves a few pieces (a move, a replay step, a free move, a
        // takeback, a rejection), each object whose place changed travels there over moveSeconds in a low arc
        // (MoveLift squares high for two squares or more), the ones leaving the board starting once the others have
        // landed. A reset, a load, entering, leaving or continuing replay, or a change moving more than
        // MaxAnimatedObjects objects between squares puts every object in place at once, as does animateMoves off.
        public bool animateMoves = true;
        public float moveSeconds = 0.35f;
        public const float MoveLift = 1;
        public const int MaxAnimatedObjects = 4;
        // Each object's animation: whether it's under way, where it started and goes (local space), and when it
        // starts moving (Time.time; later than now while it waits for the others to land).
        [NonSerialized] public bool[] animating = new bool[PieceCount];
        [NonSerialized] public Vector3[] animationFrom = new Vector3[PieceCount];
        [NonSerialized] public Vector3[] animationTo = new Vector3[PieceCount];
        [NonSerialized] public float[] animationStart = new float[PieceCount];
        float[] animationLift = new float[PieceCount];
        int animatingCount;

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
        // The square each object was on when last placed (NeverPlaced before the first time), to count how many
        // changed squares since; the board changes since the last Refresh and whether all of them may animate; and
        // the objects the local player has just let go of, put in place at once since they're already where they
        // landed (or are going back from where a rejected drop left them).
        const int NeverPlaced = -2;
        int[] placedSquare = new int[PieceCount];
        int changesSinceRefresh;
        bool changesAnimate;
        bool[] snapNext = new bool[PieceCount];
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
        // Desktop mode: the object the local player has selected by clicking, with the board's consent (NoObject for
        // none), the square it was selected on and whether that was in replay. It stands lifted over its square until
        // a click places it or puts it back, or the board changes so that it can no longer be picked up there.
        [NonSerialized] public int selectedObject = NoObject;
        [NonSerialized] public int selectedFrom = ChessRules.NoSquare;
        bool selectedInReplay;
        // The squares a click places the selected piece on: its legal moves' (a castle's rook square and, when the
        // king moves, its destination too), or in replay every other square.
        [NonSerialized] public bool[] selectedTargets = new bool[64];
        // The same for the held piece, for its highlights.
        [NonSerialized] public bool[] heldTargets = new bool[64];
        int[] targetMoves = new int[ChessMoves.MaxMoves];

        void Start() {
            for (int i = 0; i < 64; i++) squareObject[i] = NoObject;
            for (int i = 0; i < PieceCount; i++) {
                objectSquare[i] = ChessRules.NoSquare;
                objectLastSquare[i] = ChessRules.NoSquare;
                objectType[i] = ChessRules.NoPieceType;
                objectMeshType[i] = -1;
                placedSquare[i] = NeverPlaced;
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
            if (animatingCount > 0) StepAnimations();
        }

        // A move one ply on from the position the mapping shows moves its objects now, so the right object travels
        // (and animates); everything is checked against the board and placed on the next frame.
        public override void OnBoardChanged() {
            dirty = true;
            if (!Utilities.IsValid(board)) return;
            int change = board.lastChange;
            if (changesSinceRefresh == 0) changesAnimate = true;
            changesSinceRefresh++;
            if (!ChangeAnimates(change)) changesAnimate = false;
            // The position the promotion was asked in may be gone; a touch from another client changed nothing.
            if (change != ChessBoard.ChangeActivity) ClearPromotion();
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
            if (selectedObject != NoObject) FindTargets(selectedFrom, selectedInReplay, selectedTargets);
            PlaceObjects();
            UpdatePickupable();
            // A held piece's targets may have changed (or the game ended, or replay began).
            if (heldObject != NoObject) FindTargets(heldFrom, replay, heldTargets);
            UpdateLiftedMarks();
            UpdateLastMoveHighlight();
            UpdatePromotionPicker();
            UpdateBlocker();
        }

        // Enables the blocker unless the local player has a seat (either colour), so only the seated players can walk
        // into the table. Called on every Refresh: seats change only with the board.
        public void UpdateBlocker() {
            if (!Utilities.IsValid(blocker)) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            bool seated = Utilities.IsValid(board) && Utilities.IsValid(local) && board.IsPlayer(local.displayName);
            if (blocker.enabled == seated) blocker.enabled = !seated;
        }

        // ---- Input (docs/player-input.md) ----

        // A piece picked up by the local player (its ChessPiece's OnPickup): asks the board, and lets go of the piece
        // when it says no; the board's rejection then puts it back. A promotion waiting for its piece is dropped.
        public void OnPiecePickedUp(int obj) {
            if (obj < 0 || obj >= PieceCount) return;
            // The hand has it now, wherever it was going.
            StopAnimation(obj);
            ClearPromotion();
            heldObject = NoObject;
            int square = objectSquare[obj];
            if (Utilities.IsValid(board) && square != ChessRules.NoSquare
                && board.PiecePickedUp(square) == ChessBoard.InputAccepted) {
                heldObject = obj;
                heldFrom = square;
                FindTargets(heldFrom, board.replayMode, heldTargets);
                UpdateLiftedMarks();
                return;
            }
            // A piece off the board (never a pickup) is just put back; the board told its rejection itself.
            dirty = true;
            UpdateLiftedMarks();
            LetGoOf(obj);
        }

        // A piece let go by the local player (its ChessPiece's OnDrop): forwards the drop on the square under it (off
        // the board too, which the board rejects) if the board accepted its pickup; a king castles by being dropped on
        // its rook. A promotion waits for ChoosePromotion with the pawn standing on its square; anything else is placed
        // from the board after its change, so a rejected drop goes back where it was.
        public void OnPieceDropped(int obj) {
            if (obj < 0 || obj >= PieceCount) return;
            dirty = true;
            snapNext[obj] = true;
            if (obj != heldObject) return;
            if (!Utilities.IsValid(board) || !Utilities.IsValid(pieces[obj])) {
                heldObject = NoObject;
                UpdateLiftedMarks();
                return;
            }
            heldObject = NoObject;
            UpdateLiftedMarks();
            int to = DropSquare(pieces[obj].position);
            int from = heldFrom;
            if (board.PieceDropped(from, to) == ChessBoard.InputPromotionNeeded) {
                WaitForPromotion(obj, from, to);
            }
        }

        // The promotion picker's answer (step 9.5a): plays the waiting promotion with pieceType (ChessRules.Queen, Rook,
        // Bishop or Knight). Any other type is rejected by the board, which puts the pawn back. InputRejected with no
        // promotion waiting.
        public int ChoosePromotion(int pieceType) {
            if (promotionObject == NoObject || !Utilities.IsValid(board)) return ChessBoard.InputRejected;
            ClearPromotion();
            dirty = true;
            return board.PromotionChosen(promotionFrom, promotionTo, pieceType);
        }

        // The picker closed without a choice: the pawn goes back.
        public void CancelPromotion() {
            ChoosePromotion(ChessRules.NoPieceType);
        }

        // ---- Promotion picker (step 9.5a) ----

        // The picker's buttons.
        public void PressPromoteQueen() {
            ChoosePromotion(ChessRules.Queen);
        }

        public void PressPromoteRook() {
            ChoosePromotion(ChessRules.Rook);
        }

        public void PressPromoteBishop() {
            ChoosePromotion(ChessRules.Bishop);
        }

        public void PressPromoteKnight() {
            ChoosePromotion(ChessRules.Knight);
        }

        public void PressPromoteCancel() {
            CancelPromotion();
        }

        // The board asked for a promotion's piece: obj, the pawn, stands on to while the picker asks. Meanwhile no piece
        // is a pickup or a click target, so a hand near a piece reaches the picker's buttons rather than the piece.
        void WaitForPromotion(int obj, int from, int to) {
            promotionObject = obj;
            promotionFrom = from;
            promotionTo = to;
            dirty = true;
            UpdatePromotionPicker();
            UpdatePickupable();
        }

        // No promotion waits any more (chosen, cancelled, or dropped by a pickup or a change to the board); the pawn
        // is placed from the board on the next frame.
        void ClearPromotion() {
            if (promotionObject == NoObject) return;
            promotionObject = NoObject;
            dirty = true;
            UpdatePromotionPicker();
            UpdatePickupable();
        }

        // Shows the picker while a promotion waits, over the promotion square (no further off the board's centre than
        // PickerMaxOffset squares along the files, PickerHeight squares up), facing the promoting side's end of the
        // table; hides it otherwise.
        public void UpdatePromotionPicker() {
            if (!Utilities.IsValid(promotionPicker)) return;
            bool shown = promotionObject != NoObject && promotionTo != ChessRules.NoSquare;
            if (shown) {
                Transform picker = promotionPicker.transform;
                Vector3 position = SquarePosition(promotionTo);
                float limit = PickerMaxOffset * squareSize;
                position.x = Mathf.Clamp(position.x, -limit, limit);
                position.y = PickerHeight * squareSize;
                picker.localPosition = position;
                picker.localRotation = ObjectColor(promotionObject) == ChessRules.White
                    ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
            }
            if (promotionPicker.activeSelf != shown) promotionPicker.SetActive(shown);
        }

        // ---- Highlights (step 9.5a) ----

        // The two squares the last-move highlight marks for move (NoSquare for none): where the piece came from and
        // where it stands now (a castle's king destination; when the king doesn't move, its rook's square instead).
        public static int LastMoveSquare(int move, int which) {
            if (move == 0) return ChessRules.NoSquare;
            int from = ChessRules.MoveFrom(move);
            if (which == 0) return from;
            int to = ChessRules.MoveTo(move);
            if (!ChessRules.MoveIsCastle(move)) return to;
            int destination = ChessRules.SquareOf(
                ChessRules.FileOf(to) > ChessRules.FileOf(from) ? ChessRules.FileG : ChessRules.FileC,
                ChessRules.RankOf(from));
            return destination != from ? destination : to;
        }

        // Puts the last-move highlights on the squares of the move that led to the position shown (in replay too),
        // hidden when there's none. Called on every Refresh.
        public void UpdateLastMoveHighlight() {
            int move = Utilities.IsValid(board) ? board.LastMove() : 0;
            for (int i = 0; i < 2 && i < lastMoveHighlights.Length; i++) {
                GameObject highlight = lastMoveHighlights[i];
                if (!Utilities.IsValid(highlight)) continue;
                int square = LastMoveSquare(move, i);
                bool shown = square != ChessRules.NoSquare;
                if (shown) highlight.transform.localPosition = SquarePosition(square);
                if (highlight.activeSelf != shown) highlight.SetActive(shown);
            }
        }

        // Marks the held or selected piece's legal targets (heldTargets or selectedTargets); none in replay, where
        // every square is one, and none with nothing lifted. Called on every lift change.
        public void UpdateTargetHighlights() {
            bool[] targets = null;
            if (heldObject != NoObject) targets = heldTargets;
            else if (selectedObject != NoObject) targets = selectedTargets;
            bool replay = !Utilities.IsValid(board) || board.replayMode;
            for (int square = 0; square < 64 && square < targetHighlights.Length; square++) {
                GameObject highlight = targetHighlights[square];
                if (!Utilities.IsValid(highlight)) continue;
                bool shown = targets != null && !replay && targets[square];
                if (highlight.activeSelf != shown) highlight.SetActive(shown);
            }
        }

        // Whether square's legal-target mark is shown, for tests.
        public bool IsTargetHighlighted(int square) {
            GameObject highlight = targetHighlights[square];
            return Utilities.IsValid(highlight) && highlight.activeSelf;
        }

        // Everything shown for the piece the local player has lifted (held or selected): the legal-target marks.
        public void UpdateLiftedMarks() {
            UpdateTargetHighlights();
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

        // ---- Desktop mode (docs/player-input.md, step 9.3c) ----
        // Clicks instead of carrying, for players not using pickups: selecting is PiecePickedUp and placing is
        // PieceDropped, so the board treats both input styles alike.

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

        // Selects obj if the board accepts its pickup (the previous selection going back down), taking the piece's
        // ownership as a grab would. The lift is seen only here (the pieces have no VRCObjectSync, step 9.6). A refused
        // pickup leaves nothing selected; the board's rejection puts everything back.
        void Select(int obj) {
            ClearPromotion();
            ClearSelection();
            int square = objectSquare[obj];
            if (board.PiecePickedUp(square) == ChessBoard.InputAccepted) {
                selectedObject = obj;
                selectedFrom = square;
                selectedInReplay = board.replayMode;
                FindTargets(selectedFrom, selectedInReplay, selectedTargets);
                dirty = true;
                VRCPlayerApi local = Networking.LocalPlayer;
                GameObject piece = Utilities.IsValid(pieces[obj]) ? pieces[obj].gameObject : null;
                if (Utilities.IsValid(local) && Utilities.IsValid(piece) && !Networking.IsOwner(local, piece)) {
                    Networking.SetOwner(local, piece);
                }
            }
            UpdateLiftedMarks();
            UpdateClickables();
        }

        // Puts the selected piece back down where it was, unselected; the board isn't asked.
        void Unselect() {
            ClearSelection();
            UpdateLiftedMarks();
            UpdateClickables();
        }

        // Plays the selected piece's move to square: a promotion waits for ChoosePromotion as a dropped one does, with
        // the pawn standing on square; anything else is placed from the board after its change, so a rejected placing
        // puts the piece back, unselected.
        void Place(int square) {
            int obj = selectedObject;
            int from = selectedFrom;
            ClearSelection();
            UpdateLiftedMarks();
            if (board.PieceDropped(from, square) == ChessBoard.InputPromotionNeeded) {
                WaitForPromotion(obj, from, square);
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
            UpdateLiftedMarks();
        }

        // Whether a click on obj places the selected piece on its square rather than selecting obj: the square is a
        // target and obj isn't one of the selected piece's own (outside replay, a target with an own piece on it is
        // a castling rook).
        bool PlacesOn(int obj) {
            int square = objectSquare[obj];
            if (selectedObject == NoObject || square == ChessRules.NoSquare || !selectedTargets[square]) return false;
            return !selectedInReplay || ObjectColor(obj) != ObjectColor(selectedObject);
        }

        // Works out targets (selectedTargets or heldTargets) for the piece lifted from from, from the board's legal
        // moves (every other square in replay, where moves are free).
        void FindTargets(int from, bool replay, bool[] targets) {
            for (int i = 0; i < 64; i++) targets[i] = replay && i != from;
            if (replay) return;
            int count = board.LegalMovesFrom(from, targetMoves);
            for (int i = 0; i < count; i++) {
                int move = targetMoves[i];
                int to = ChessRules.MoveTo(move);
                targets[to] = true;
                if (!ChessRules.MoveIsCastle(move)) continue;
                // A castle stores its rook's square; the board also castles on a drop on the king's destination,
                // unless a normal move ends there (which is then a target anyway) or the king doesn't move.
                int destination = ChessRules.SquareOf(
                    ChessRules.FileOf(to) > ChessRules.FileOf(from) ? ChessRules.FileG : ChessRules.FileC,
                    ChessRules.RankOf(from));
                if (destination != from) targets[destination] = true;
            }
        }

        // Makes the pieces and squares click targets exactly where a click does something for the local player in
        // desktop mode, with interact text saying what: with nothing selected, the pieces the board would let them
        // pick up; with one selected, that piece and its square (put back), its targets (pieces to capture or castle
        // with, squares to place on) and the other pieces they could select instead. Players using pickups get none, and
        // nobody does while the promotion picker shows.
        // Called after every change, once a second and on every click.
        public void UpdateClickables() {
            bool clicks = !UsesPickups() && Utilities.IsValid(board) && promotionObject == NoObject;
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
            UpdateLiftedMarks();
            LetGoOf(obj);
        }

        void LetGoOf(int obj) {
            snapNext[obj] = true;
            VRCPickup pickup = piecePickups[obj];
            // Its OnDrop is then ignored, since it's no longer heldObject.
            if (Utilities.IsValid(pickup) && pickup.IsHeld) pickup.Drop();
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
        // pieces off the board never are, and none while the promotion picker shows. Called after every change, once a
        // second and when a promotion starts or stops waiting.
        public void UpdatePickupable() {
            nextPickupCheck = Time.time + PickupCheckInterval;
            bool allowed = UsesPickups() && Utilities.IsValid(board) && board.MayTouchPieces()
                && promotionObject == NoObject;
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

        // Puts every object but the held one where the mapping says (PrepareObject). If the board's changes since the
        // last Refresh may animate (ChangeAnimates) and moved at most MaxAnimatedObjects objects between squares, each
        // object whose place changed travels there, those leaving the board waiting for those landing on it; otherwise
        // (and for a piece the local player has just let go of) it's put there at once. An object already travelling
        // to its place carries on.
        void PlaceObjects() {
            bool animate = animateMoves && moveSeconds > 0 && changesSinceRefresh > 0 && changesAnimate;
            if (animate) {
                int moved = 0;
                for (int obj = 0; obj < PieceCount; obj++) {
                    if (obj != heldObject && objectSquare[obj] != placedSquare[obj]) moved++;
                }
                animate = moved <= MaxAnimatedObjects;
            }
            changesSinceRefresh = 0;
            // The objects on the board first, so those leaving it know whether to wait.
            bool landing = false;
            for (int pass = 0; pass < 2; pass++) {
                for (int obj = 0; obj < PieceCount; obj++) {
                    if (obj == heldObject || !Utilities.IsValid(pieces[obj])) continue;
                    bool onBoard = objectSquare[obj] != ChessRules.NoSquare || obj == promotionObject;
                    if (onBoard != (pass == 0)) continue;
                    Vector3 target = PrepareObject(obj);
                    placedSquare[obj] = objectSquare[obj];
                    Transform piece = pieces[obj];
                    bool snap = snapNext[obj] || !animateMoves;
                    if (!snap && animating[obj] && (animationTo[obj] - target).sqrMagnitude < 1e-10f) continue;
                    if (!snap && animate && (piece.localPosition - target).sqrMagnitude > 1e-10f) {
                        AnimateObject(obj, target, pass == 1 && landing ? moveSeconds : 0);
                        if (pass == 0) landing = true;
                    } else {
                        StopAnimation(obj);
                        piece.localPosition = target;
                    }
                }
            }
            for (int obj = 0; obj < PieceCount; obj++) snapNext[obj] = false;
        }

        // Whether a change may animate the pieces it moves: anything but a reset, a load, entering, leaving or
        // continuing replay (whose pieces jump to another position), and nothing yet.
        public static bool ChangeAnimates(int change) {
            return change != ChessBoard.ChangeNone && change != ChessBoard.ChangeReset
                && change != ChessBoard.ChangeLoaded && change != ChessBoard.ChangeContinued
                && change != ChessBoard.ChangeEnteredReplay && change != ChessBoard.ChangeLeftReplay;
        }

        // Starts obj travelling from where it is to target (local space), after delay seconds.
        void AnimateObject(int obj, Vector3 target, float delay) {
            Vector3 from = pieces[obj].localPosition;
            animationFrom[obj] = from;
            animationTo[obj] = target;
            animationStart[obj] = Time.time + delay;
            float distance = Vector3.Distance(from, target);
            animationLift[obj] = MoveLift * squareSize * Mathf.Clamp01(distance / (2 * squareSize));
            if (!animating[obj]) {
                animating[obj] = true;
                animatingCount++;
            }
        }

        // Leaves obj where it is (the caller places it, or a hand holds it).
        void StopAnimation(int obj) {
            if (!animating[obj]) return;
            animating[obj] = false;
            animatingCount--;
        }

        // Moves the travelling objects on (every frame while any travels): eased along the straight line, lifted in an
        // arc over it, landing exactly on their place.
        void StepAnimations() {
            float now = Time.time;
            for (int obj = 0; obj < PieceCount; obj++) {
                if (!animating[obj]) continue;
                Transform piece = pieces[obj];
                if (obj == heldObject || !Utilities.IsValid(piece)) {
                    StopAnimation(obj);
                    continue;
                }
                float t = moveSeconds > 0 ? (now - animationStart[obj]) / moveSeconds : 1;
                if (t >= 1) {
                    piece.localPosition = animationTo[obj];
                    StopAnimation(obj);
                    continue;
                }
                // Still waiting for the others to land.
                if (t <= 0) continue;
                float s = t * t * (3 - 2 * t);
                piece.localPosition = Vector3.Lerp(animationFrom[obj], animationTo[obj], s)
                    + new Vector3(0, animationLift[obj] * 4 * s * (1 - s), 0);
            }
        }

        // Brings obj's mesh (and collider) and turn up to the mapping, and returns where it belongs: on its square, or
        // beside the board when it's off it. Black's pieces face the other way, for the knights.
        Vector3 PrepareObject(int obj) {
            Transform piece = pieces[obj];
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
            piece.localRotation = ObjectColor(obj) == ChessRules.White
                ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
            return ObjectPlace(obj);
        }

        // Where obj belongs now (local space), changing nothing: on its square, or beside the board when it's off it.
        // Public for the debug view's placement check (step 9.6).
        public Vector3 ObjectPlace(int obj) {
            int square = objectSquare[obj];
            // A pawn waiting for its promotion piece stands where it was dropped.
            if (obj == promotionObject) square = promotionTo;
            Vector3 position = square != ChessRules.NoSquare ? SquarePosition(square) : SparePosition(obj);
            // A selected piece (desktop mode) stands lifted over its square.
            if (obj == selectedObject) position += new Vector3(0, SelectLift * squareSize, 0);
            return position;
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
