using System;
using SiggiUSTools;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The physical board (docs/architecture.md, docs/physical-pieces.md): the 32 piece objects in the world, placed on
    // the squares the logical board says, with what goes with them on the table: pickups and clicks, highlights,
    // animations, the promotion ghosts, the player blocker and the move sounds. The buttons, the status display, the
    // PGN box and the other sounds are the UI's (ChessBoardUI, step 12.1), which this view doesn't know. Holds no game
    // state and no synced fields; it reads the board after each change. Built in the scene by Tools > Chess Board >
    // Create Board View (Editor/ChessBoardViewBuilder.cs).
    //
    // The view's local space: the playing surface at y = 0, files a to h along +x, ranks 1 to 8 along +z (White at
    // -z), the board's centre at the origin. Everything is placed in it, so the view can be scaled as a whole, and
    // nothing reads the board model (Board, Squares), so a world creator can replace it (step 12.2,
    // docs/customizing.md).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ChessBoardView : ChessBoardListener {
        public const int PieceCount = 32;
        // Objects 0 to 15 are White's, 16 to 31 Black's: a side never has more than 16 pieces, since promotion turns
        // a pawn into another piece. Each object wears the model of the piece it stands for.
        public const int PiecesPerColor = 16;
        public const int NoObject = -1;
        // How the local player handles the pieces (inputStyle): Auto is pickups in VR and clicks otherwise; the other
        // two force one style, for testing pickups on desktop clients (docs/testing.md, One VR client).
        public const int InputAuto = 0;
        public const int InputPickups = 1;
        public const int InputClicks = 2;

        // The board shown; the view adds itself as a listener on Start.
        public ChessBoard board;
        // The piece objects, children of this view, and their mesh filters and renderers (the same index).
        public Transform[] pieces = new Transform[PieceCount];
        public MeshFilter[] pieceFilters = new MeshFilter[PieceCount];
        public MeshRenderer[] pieceRenderers = new MeshRenderer[PieceCount];
        // Each piece object takes its side's piece set's template (steps 12.3, 12.10 and 13.6, docs/customizing.md):
        // mesh, materials, offset, rotation and size, the set chosen by the board's synced index into the arbiter's
        // list or the arbiter's local override (CurrentPieceSet).
        // Each piece object's pickup and collider (the same index). The collider is fitted to the piece's mesh.
        public VRCPickup[] piecePickups = new VRCPickup[PieceCount];
        public BoxCollider[] pieceColliders = new BoxCollider[PieceCount];
        // The width of a square, in the view's local units. The builder's square targets and highlights are made for
        // it, so it isn't for resizing: scale the view's transform instead (step 12.2, docs/customizing.md).
        [Tooltip("The builder's square size, in the view's units. Don't change it to resize the board: scale the view.")]
        public float squareSize = 0.05f;
        // InputAuto, InputPickups or InputClicks, the same for every client of a build; _CycleInputStyle changes it on
        // one client.
        [Tooltip("Auto: pickups in VR, clicks on desktop and mobile. Pickups or Clicks: that for everyone, for testing.")]
        public int inputStyle = InputAuto;
        // Desktop mode's click targets (step 9.3c): each piece's ChessPiece (the same index as pieces), and the
        // ChessSquare of each square (by square, each its own collider, active only while a click on it does something).
        public ChessPiece[] pieceBehaviours = new ChessPiece[PieceCount];
        public ChessSquare[] squareTargets = new ChessSquare[64];
        // How far a selected piece is lifted above its square, in squares.
        public const float SelectLift = 0.5f;
        // The promotion ghosts (step 12.11): see-through pieces, a knight, bishop, rook and queen (_GhostType), children of
        // this view, shown only to the local player while their pawn waits on the last rank, floating in a row above
        // it; clicking one (Interact) promotes to its piece. Each wears the promoting side's promotion template
        // (ChessPieceSet._PromotionTemplate), Black's falling back on White's as its pieces do.
        public ChessPromotionGhost[] promotionGhosts = new ChessPromotionGhost[GhostCount];
        public const int GhostCount = 4;
        // Where the row floats, in squares: its ghosts' bases GhostHeight above the playing surface over the promotion
        // square's rank, GhostSpacing apart along the files, the row's middle no further off the board's centre than
        // GhostMaxOffset (so the row stays over the board by the a and h files).
        public const float GhostHeight = 1.5f;
        public const float GhostSpacing = 1;
        public const float GhostMaxOffset = 2;
        // Highlights (step 9.5a), children of this view lying on the board: the last move (hidden for none), and one
        // legal-target mark per square, standing over it, shown for the held or selected piece's targets.
        // The last move (step 12.6): a line from its from square to its to square (a flat bar starting at its origin
        // and running along its +z, one local unit long; the view stands it on the from square, turns it towards the
        // to square and stretches its z to reach it), and a mark (a small diamond) the view moves onto the from square.
        public GameObject lastMoveLine;
        public GameObject lastMoveMark;
        // Check lines (step 12.7): while a king is in check in the position shown (in replay too), a red line from
        // each piece giving check to the king, each a bar like lastMoveLine; the lines beyond the checks are hidden,
        // and checks beyond the lines aren't shown. A game has at most two (a double check); a custom replay position
        // can have more. The builder makes CheckLineCount.
        public GameObject[] checkLines = new GameObject[0];
        public const int CheckLineCount = 16;
        public GameObject[] targetHighlights = new GameObject[64];
        // The legal-target marks' look (step 12.5): each is a diamond as wide as its square, given targetMaterial
        // (yellow) and scaled down to SmallMarkScale on an empty square (MarkMove), targetMaterial at full size on a
        // castle's rook square (MarkCastle), and captureMaterial (red) at full size on a capture (MarkCapture).
        public Material targetMaterial;
        public Material captureMaterial;
        public const float SmallMarkScale = 0.5f;
        public const int MarkNone = 0;
        public const int MarkMove = 1;
        public const int MarkCastle = 2;
        public const int MarkCapture = 3;
        // The table, the board and the buttons have no solid colliders, so the players can walk up to and into the
        // board. The blocker, a solid box filling the table up to its top, keeps everyone else out: enabled on each
        // client unless the local player has a seat (_UpdateBlocker). Players collide only on their own client, so
        // each client's blocker stops only its own player. Below the surface, so it doesn't catch pointing or clicks.
        public Collider blocker;
        // Captured-piece places (step 12.4, docs/customizing.md): where each colour's captured pieces stand, in the
        // order they were captured, placed by the world creator anywhere (read through their world position, so they
        // needn't be children of the view). A captured piece stands on its place as on a square: its template's offset
        // and turn, in the view's space. Empty entries are skipped; pieces beyond the last place are hidden.
        [Tooltip("Where White's captured pieces stand, in capture order. Pieces beyond the last are hidden.")]
        public Transform[] whiteCapturedPlaces = new Transform[PiecesPerColor];
        [Tooltip("Where Black's captured pieces stand, in capture order. Pieces beyond the last are hidden.")]
        public Transform[] blackCapturedPlaces = new Transform[PiecesPerColor];
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
        // The checkmated or resigning king falls over (step 12.8): while the position shown is the end of a game lost
        // by checkmate or resignation (ChessBoard.FallenKingSquare), the loser's king lies on the board, tipped over
        // about its base (which stays over its square) towards the board's centre along the files or the ranks,
        // whichever it stands further from it along, and lifted so that its lowest point is as high as when it stands.
        // It tips over in fallSeconds once any travelling piece (the mating one) has landed, at once on a resignation,
        // and stands up the same way when the position shown is no longer that end. A change
        // that puts everything in place at once (a reset, a load, entering or leaving replay, animateMoves off) lays it
        // down or stands it up at once too, as does its leaving its square.
        public float fallSeconds = 0.6f;
        // How far each object has fallen (0 standing, 1 lying), how far it's going (0 or 1), and from when
        // (Time.time; later than now while it waits for the travelling pieces to land).
        [NonSerialized] public float[] fallAmount = new float[PieceCount];
        [NonSerialized] public float[] fallTarget = new float[PieceCount];
        [NonSerialized] public float[] fallStart = new float[PieceCount];
        bool falling;
        // Sound effects (step 12.12): the move sounds, played from audioSource (a spatial source on the table) on every
        // client, for a change as it happens: a move, a replay step or a free move plays the sound the position it
        // leads to would play if its last move had just been made, the most specific one (checkSound when a king is in
        // check, then promoteSound, captureSound, moveSound); the start of a game plays nothing. Every other sound is
        // the UI's (ChessBoardUI). A promoting pawn landing on the last rank plays its move or capture sound on the
        // promoting player's client (WaitForPromotion). Nothing plays for a late joiner's first update, during the
        // first StartupSilence seconds, or for changes that come several between two Refreshes (a test's thousands; a
        // received update is one change, whatever it holds).
        public AudioSource audioSource;
        public AudioClip moveSound;
        public AudioClip captureSound;
        public AudioClip promoteSound;
        public AudioClip checkSound;
        public const float StartupSilence = 1;
        // For tests: nothing is heard while muted, but lastSound and soundsPlayed follow what would have been.
        [NonSerialized] public bool muted;
        [NonSerialized] public AudioClip lastSound;
        [NonSerialized] public int soundsPlayed;
        // When sounds may start (Time.time), whether this client has caught up with the board (a change seen after
        // the silence: made here, or a received one, the first of which is silent), the change whose sound the next
        // Refresh plays (ChangeNone for none) and how many changes have come since the last Refresh, and the number of
        // pieces in the position shown at the last _Refresh (a free move that leaves fewer captured one).
        float soundsFrom;
        bool caughtUp;
        int soundChange = ChessBoard.ChangeNone;
        int soundChanges;
        int shownPieceCount = -1;
        int shownChecks;

        // Which object stands on each square (NoObject for none), and the other way round (NoSquare for an object
        // off the board: captured, or not needed by the position). objectType is the piece type each object stands
        // for (its mesh); its colour is fixed by its index.
        [NonSerialized] public int[] squareObject = new int[64];
        [NonSerialized] public int[] objectSquare = new int[PieceCount];
        [NonSerialized] public int[] objectType = new int[PieceCount];
        // The square each object last stood on, so a piece coming back to the board (a reset, stepping back in
        // replay) is taken from near where it is needed. NoSquare for never.
        int[] objectLastSquare = new int[PieceCount];
        // The type each object's model was last set to, so a model is only swapped when it changes (-1 to set it on
        // the next placing, as after a set change).
        int[] objectMeshType = new int[PieceCount];

        // The captured pieces of the position shown (ChessBoard.CapturedPieces): each colour's piece types in place
        // order (colour * MaxCaptured + place) and how many; the place each object off the board stands in (NoPlace
        // for none: on the board, or hidden as not needed); and the places' targets that exist, in order, by colour.
        public const int MaxCaptured = ChessCaptures.MaxCaptured;
        public const int NoPlace = -1;
        [NonSerialized] public int[] capturedTypes = new int[2 * MaxCaptured];
        [NonSerialized] public int[] capturedCounts = new int[2];
        [NonSerialized] public int[] objectPlace = new int[PieceCount];
        Transform[] placeTargets = new Transform[2 * MaxCaptured];
        int[] placeTargetCounts = new int[2];

        // The piece set applied to each side (null for none) and the arbiter whose list and overrides choose it, and
        // the sets' templates copied by _TemplateIndex (colour * 6 + type - 1): mesh, materials, offset from the
        // square's centre, rotation, scale (a Black template falling back on White's turned half round), and the
        // point under the square's centre in the piece's own space (where a drop is read from).
        public const int TemplateCount = 12;
        ChessPieceSet appliedWhiteSet;
        ChessPieceSet appliedBlackSet;
        bool[] hasSet = new bool[2];
        ChessArbiter arbiter;
        bool[] warnedNoSet = new bool[2];
        // Whether a synced index the arbiter's list doesn't have has been logged, per colour.
        bool[] warnedBadIndex = new bool[2];
        Mesh[] setMeshes = new Mesh[TemplateCount];
        Material[][] setMaterials = new Material[TemplateCount][];
        Vector3[] setOffsets = new Vector3[TemplateCount];
        Quaternion[] setRotations = new Quaternion[TemplateCount];
        Vector3[] setScales = new Vector3[TemplateCount];
        Vector3[] setBases = new Vector3[TemplateCount];
        // The promotion templates copied the same way, by _GhostIndex (colour * GhostCount + ghost), and the colour the
        // ghosts were last dressed for (-1 to dress them again when next shown, as after a set change).
        Mesh[] ghostMeshes = new Mesh[2 * GhostCount];
        Material[][] ghostMaterials = new Material[2 * GhostCount][];
        Vector3[] ghostOffsets = new Vector3[2 * GhostCount];
        Quaternion[] ghostRotations = new Quaternion[2 * GhostCount];
        Vector3[] ghostScales = new Vector3[2 * GhostCount];
        int ghostsDressed = -1;

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
        // A captured piece put back in replay (step 13.4) is picked up from its place: heldFrom is NoSquare and
        // heldPlace its place (NoPlace for a piece from a square).
        [NonSerialized] public int heldObject = NoObject;
        [NonSerialized] public int heldFrom = ChessRules.NoSquare;
        [NonSerialized] public int heldPlace = NoPlace;
        // A promotion waiting for its piece: the pawn's object, its from and to squares (NoObject when none). The pawn
        // stands on to meanwhile; _ChoosePromotion plays it, and any change to the board (but activity) cancels it.
        [NonSerialized] public int promotionObject = NoObject;
        [NonSerialized] public int promotionFrom = ChessRules.NoSquare;
        [NonSerialized] public int promotionTo = ChessRules.NoSquare;
        // Desktop mode: the object the local player has selected by clicking, with the board's consent (NoObject for
        // none), the square it was selected on and whether that was in replay. It stands lifted over its square until
        // a click places it or puts it back, or the board changes so that it can no longer be picked up there. A
        // captured piece (step 13.4) is selected in its place: selectedFrom NoSquare, selectedPlace its place.
        [NonSerialized] public int selectedObject = NoObject;
        [NonSerialized] public int selectedFrom = ChessRules.NoSquare;
        [NonSerialized] public int selectedPlace = NoPlace;
        bool selectedInReplay;
        // The squares a click places the selected piece on: its legal moves' (a castle's rook square and, when the
        // king moves, its destination too), or in replay every other square.
        [NonSerialized] public bool[] selectedTargets = new bool[64];
        // The same for the held piece, for its highlights.
        [NonSerialized] public bool[] heldTargets = new bool[64];
        // Each target's legal-target mark (Mark...), for the selected and the held piece.
        [NonSerialized] public int[] selectedMarks = new int[64];
        [NonSerialized] public int[] heldMarks = new int[64];
        // The checks in the position shown, for the check lines: the squares of the pieces giving check and of the
        // kings they attack (ChessBoard.Checks).
        [NonSerialized] public int[] checkers = new int[CheckLineCount];
        [NonSerialized] public int[] checkedKings = new int[CheckLineCount];
        int[] targetMoves = new int[ChessMoves.MaxMoves];
        // The hover texts, from the language pack (PLAN.md 13.5) through i18n, read again on a language change: the
        // pieces' pickups ("Move"), the click targets' and the promotion ghosts'.
        [Tooltip("The chess system's shared I18n (the Chess Language object), for the hover texts. Left empty: the "
            + "I18n sanity check fills it in on build and before Play Mode.")]
        public I18n i18n;
        string hoverSelect = "";
        string hoverPutBack = "";
        string hoverPlace = "";
        string hoverCapture = "";
        string hoverCastle = "";

        void Start() {
            for (int i = 0; i < 64; i++) squareObject[i] = NoObject;
            for (int i = 0; i < PieceCount; i++) {
                objectSquare[i] = ChessRules.NoSquare;
                objectLastSquare[i] = ChessRules.NoSquare;
                objectType[i] = ChessRules.NoPieceType;
                objectMeshType[i] = -1;
                objectPlace[i] = NoPlace;
                placedSquare[i] = NeverPlaced;
            }
            if (Utilities.IsValid(i18n)) i18n.AddLanguageChangeListener(this);
            SetHoverTexts();
            soundsFrom = Time.time + StartupSilence;
            if (!Utilities.IsValid(board)) {
                Debug.LogError("[ChessBoardView] " + gameObject.name + ": the Board field is empty.");
                return;
            }
            string problem = LocalSpaceProblem();
            if (problem.Length > 0) Debug.LogWarning("[ChessBoardView] " + gameObject.name + ": " + problem);
            // Told when the arbiter's piece set override changes (_OnPieceSetOverrideChanged).
            arbiter = board.arbiter;
            if (Utilities.IsValid(arbiter)) arbiter._AddPieceSetListener(this);
            ApplyPieceSet(ChessRules.White);
            ApplyPieceSet(ChessRules.Black);
            board.AddListener(this);
            _Refresh();
        }

        // ---- Local space (step 12.2, docs/customizing.md) ----

        // The view places its objects in its own local space, so it can be scaled as a whole, and its board model
        // (Board, Squares) replaced: nothing reads the model. The groups holding its objects (Pieces, Square Targets,
        // Highlights) must leave that space as it is. "" when they do; otherwise what's wrong, which Start logs.
        public string LocalSpaceProblem() {
            for (int i = 0; i < pieces.Length; i++) {
                string problem = ParentProblem(pieces[i]);
                if (problem.Length > 0) return problem;
            }
            for (int i = 0; i < squareTargets.Length; i++) {
                string problem = Utilities.IsValid(squareTargets[i]) ? ParentProblem(squareTargets[i].transform) : "";
                if (problem.Length > 0) return problem;
            }
            for (int i = 0; i < targetHighlights.Length; i++) {
                string problem = Utilities.IsValid(targetHighlights[i])
                    ? ParentProblem(targetHighlights[i].transform) : "";
                if (problem.Length > 0) return problem;
            }
            string lineProblem = Utilities.IsValid(lastMoveLine) ? ParentProblem(lastMoveLine.transform) : "";
            if (lineProblem.Length > 0) return lineProblem;
            string markProblem = Utilities.IsValid(lastMoveMark) ? ParentProblem(lastMoveMark.transform) : "";
            if (markProblem.Length > 0) return markProblem;
            for (int i = 0; i < checkLines.Length; i++) {
                string problem = Utilities.IsValid(checkLines[i]) ? ParentProblem(checkLines[i].transform) : "";
                if (problem.Length > 0) return problem;
            }
            for (int i = 0; i < promotionGhosts.Length; i++) {
                ChessPromotionGhost ghost = promotionGhosts[i];
                string problem = Utilities.IsValid(ghost) ? ParentProblem(ghost.transform) : "";
                if (problem.Length > 0) return problem;
            }
            return "";
        }

        // "" when every group between obj and the view is unmoved, unturned and unscaled.
        string ParentProblem(Transform obj) {
            if (!Utilities.IsValid(obj)) return "";
            Transform parent = obj.parent;
            while (Utilities.IsValid(parent) && parent != transform) {
                if (parent.localPosition.sqrMagnitude > 1e-10f
                    || Quaternion.Angle(parent.localRotation, Quaternion.identity) > 0.01f
                    || (parent.localScale - Vector3.one).sqrMagnitude > 1e-8f) {
                    return obj.name + " is misplaced: " + parent.name + " is moved, turned or scaled. Scale or move the "
                        + "whole view instead.";
                }
                parent = parent.parent;
            }
            if (!Utilities.IsValid(parent)) return obj.name + " isn't inside the view.";
            return "";
        }

        // ---- Piece sets (steps 12.3, 12.10 and 13.6, docs/customizing.md) ----

        // The set a ChessRules colour's pieces should wear: the arbiter's override for that colour if it has one,
        // otherwise the set at the board's synced index for it in the arbiter's list, or the first set for an index
        // the list doesn't have (logged once per colour). Null for none (no arbiter, or no set there).
        public ChessPieceSet CurrentPieceSet(int color) {
            if (color != ChessRules.White && color != ChessRules.Black) return null;
            if (!Utilities.IsValid(arbiter)) return null;
            int index = arbiter.PieceSetOverride(color);
            if (index < 0) {
                index = board.PieceSetIndex(color);
                if (!arbiter.IsPieceSetIndex(index)) {
                    if (!warnedBadIndex[color]) {
                        Debug.LogWarning("[ChessBoardView] " + gameObject.name + ": the board's piece set "
                            + index + " for " + (color == ChessRules.White ? "White" : "Black") + " isn't in the "
                            + "Chess Arbiter's Piece Sets (" + arbiter.PieceSetCount() + "); showing the first.");
                        warnedBadIndex[color] = true;
                    }
                    index = 0;
                }
            }
            return arbiter.PieceSet(index);
        }

        // The set applied to a ChessRules colour's pieces now, for tests.
        public ChessPieceSet _AppliedPieceSet(int color) {
            if (color == ChessRules.White) return appliedWhiteSet;
            if (color == ChessRules.Black) return appliedBlackSet;
            return null;
        }

        // The arbiter's override for a side changed (ChessArbiter.SetPieceSetOverride tells its piece set listeners):
        // the side's set worn at once if it's another.
        public void _OnPieceSetOverrideChanged(int color) {
            if (color == ChessRules.White || color == ChessRules.Black) ApplyChangedPieceSet(color);
        }

        // Applies a colour's current set at once if it isn't the one applied (the board's change, an override).
        void ApplyChangedPieceSet(int color) {
            if (CurrentPieceSet(color) != _AppliedPieceSet(color)) ApplyPieceSet(color);
        }

        // Copies a colour's current set's templates (a Black one without a mesh falling back on White's,
        // CopyTemplate) and puts them on that side's piece objects at once, each staying where the mapping has it (a
        // travelling one lands at once, a held one stays in the hand).
        void ApplyPieceSet(int color) {
            ChessPieceSet set = CurrentPieceSet(color);
            bool white = color == ChessRules.White;
            if (white) appliedWhiteSet = set;
            else appliedBlackSet = set;
            hasSet[color] = Utilities.IsValid(set);
            if (!hasSet[color]) {
                if (!warnedNoSet[color]) {
                    string side = white ? "White" : "Black";
                    Debug.LogError("[ChessBoardView] " + gameObject.name + ": no chess piece set for " + side + ": add "
                        + "one to the Chess Arbiter's Piece Sets (Tools > Chess Board > Create Board View makes one). "
                        + side + "'s pieces keep the models they have.");
                    warnedNoSet[color] = true;
                }
                return;
            }
            string problem = set.Problem();
            if (problem.Length > 0) {
                Debug.LogError("[ChessBoardView] " + gameObject.name + ": chess piece set " + set.gameObject.name + ": "
                    + problem + ".");
            }
            for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) {
                int index = _TemplateIndex(color, type);
                CopyTemplate(set._Template(color, type), set._Template(ChessRules.White, type), !white, index,
                    setMeshes, setMaterials, setOffsets, setRotations, setScales);
                // The square's centre, -offset from the piece's origin, in the piece's own (turned, scaled) space.
                Vector3 scale = setScales[index];
                Vector3 local = Quaternion.Inverse(setRotations[index]) * -setOffsets[index];
                setBases[index] = new Vector3(SafeDivide(local.x, scale.x), SafeDivide(local.y, scale.y),
                    SafeDivide(local.z, scale.z));
            }
            for (int ghost = 0; ghost < GhostCount; ghost++) {
                int type = _GhostType(ghost);
                CopyTemplate(set._PromotionTemplate(color, type), set._PromotionTemplate(ChessRules.White, type),
                    !white, _GhostIndex(color, ghost), ghostMeshes, ghostMaterials, ghostOffsets, ghostRotations, ghostScales);
            }
            // Ghosts showing for this side change at once.
            if (ghostsDressed == color) ghostsDressed = -1;
            _UpdatePromotionGhosts();
            for (int obj = 0; obj < PieceCount; obj++) {
                if (_ObjectColor(obj) != color) continue;
                objectMeshType[obj] = -1;
                if (!Utilities.IsValid(pieces[obj]) || objectType[obj] == ChessRules.NoPieceType) continue;
                Vector3 target = PrepareObject(obj);
                if (obj == heldObject) continue;
                StopAnimation(obj);
                pieces[obj].localPosition = target;
            }
        }

        // Copies a template into the arrays at index: own, as set up; or for Black (black) when own has no mesh, White's
        // template for the same piece (white) turned half round about the vertical through the square's centre (as if
        // set up facing White, so the base point stays the same), with own's materials when it has a renderer with
        // any, otherwise White's. A missing template copies no mesh, no materials and no offset.
        static void CopyTemplate(Transform own, Transform white, bool black, int index, Mesh[] meshes,
            Material[][] materials, Vector3[] offsets, Quaternion[] rotations, Vector3[] scales) {
            Transform source = own;
            Quaternion turn = Quaternion.identity;
            Material[] ownMaterials = null;
            if (black && !ChessPieceSet._HasMesh(own)) {
                ownMaterials = MaterialsOf(own);
                source = white;
                turn = Quaternion.Euler(0, 180, 0);
            }
            if (!Utilities.IsValid(source)) {
                meshes[index] = null;
                materials[index] = null;
                offsets[index] = Vector3.zero;
                rotations[index] = turn;
                scales[index] = Vector3.one;
                return;
            }
            MeshFilter filter = source.GetComponent<MeshFilter>();
            MeshRenderer renderer = source.GetComponent<MeshRenderer>();
            meshes[index] = Utilities.IsValid(filter) ? filter.sharedMesh : null;
            materials[index] = ownMaterials != null ? ownMaterials
                : Utilities.IsValid(renderer) ? renderer.sharedMaterials : null;
            offsets[index] = turn * source.localPosition;
            rotations[index] = turn * source.localRotation;
            scales[index] = source.localScale;
        }

        // A template's renderer's materials, null for none (no template or renderer, or only empty slots).
        static Material[] MaterialsOf(Transform template) {
            if (!Utilities.IsValid(template)) return null;
            MeshRenderer renderer = template.GetComponent<MeshRenderer>();
            if (!Utilities.IsValid(renderer)) return null;
            Material[] list = renderer.sharedMaterials;
            if (list == null) return null;
            for (int i = 0; i < list.Length; i++) {
                if (Utilities.IsValid(list[i])) return list;
            }
            return null;
        }

        static float SafeDivide(float a, float b) {
            return Mathf.Abs(b) > 1e-6f ? a / b : 0;
        }

        // The index of a colour's piece type in the copied templates.
        public static int _TemplateIndex(int color, int type) {
            return color * 6 + type - 1;
        }

        // The mesh obj wears for the piece it stands for, from the applied set (null for none).
        public Mesh _ObjectMesh(int obj) {
            int type = objectType[obj];
            return type == ChessRules.NoPieceType ? null : setMeshes[_TemplateIndex(_ObjectColor(obj), type)];
        }

        // How far obj's origin stands from its square's centre (or its place beside the board), in the view's local
        // space: its template's position.
        public Vector3 _PieceOffset(int obj) {
            int type = objectType[obj];
            if (type == ChessRules.NoPieceType || !hasSet[_ObjectColor(obj)]) return Vector3.zero;
            return setOffsets[_TemplateIndex(_ObjectColor(obj), type)];
        }

        // The point of obj that stands on its square's centre (world space): where a drop is read from, following the
        // piece as a hand turns it.
        public Vector3 _PieceBase(int obj) {
            Transform piece = pieces[obj];
            int type = objectType[obj];
            if (type == ChessRules.NoPieceType || !hasSet[_ObjectColor(obj)]) return piece.position;
            return piece.TransformPoint(setBases[_TemplateIndex(_ObjectColor(obj), type)]);
        }

        void Update() {
            if (dirty) {
                _Refresh();
            } else if (Time.time >= nextPickupCheck) {
                _UpdatePickupable();
            }
            if (animatingCount > 0) StepAnimations();
            if (falling) StepFalls();
        }

        // A move one ply on from the position the mapping shows moves its objects now, so the right object travels
        // (and animates); everything is checked against the board and placed on the next frame.
        public override void OnBoardChanged() {
            dirty = true;
            if (!Utilities.IsValid(board)) return;
            int change = board.lastChange;
            if (changesSinceRefresh == 0) changesAnimate = true;
            changesSinceRefresh++;
            if (!_ChangeAnimates(change)) changesAnimate = false;
            ChessUtil._NoteSound(change, MakesSound(change), board, soundsFrom, ref caughtUp, ref soundChanges,
                ref soundChange);
            // A side whose synced set changed (step 13.6: ChangePieceSet, a join with a preferred set, a reset, an
            // update from another client) wears it at once.
            ApplyChangedPieceSet(ChessRules.White);
            ApplyChangedPieceSet(ChessRules.Black);
            // The position the promotion was asked in may be gone; a touch from another client or a set change changed
            // nothing.
            if (change != ChessBoard.ChangeActivity && change != ChessBoard.ChangePieceSet) ClearPromotion();
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
        public void _Refresh() {
            dirty = false;
            int change = soundChange;
            soundChange = ChessBoard.ChangeNone;
            soundChanges = 0;
            if (!Utilities.IsValid(board)) return;
            // Each side's set as the board has it now, also after a change no listener was told of.
            ApplyChangedPieceSet(ChessRules.White);
            ApplyChangedPieceSet(ChessRules.Black);
            bool replay = board.replayMode;
            byte[] shown = replay ? board.replayBoard : board.board;
            // The captured pieces first, so the mapping takes pieces coming back to the board from the places that
            // empty.
            board.CapturedPieces(capturedTypes, capturedCounts);
            CollectPlaceTargets();
            Reconcile(shown);
            shownReplay = replay;
            shownPly = replay ? board.replayIndex : board.moveCount;
            shownCustom = replay && board.replayCustom;
            mappingStale = false;
            // A held piece the board no longer shows where it was picked up (its square, or for a captured piece its
            // place, step 13.4) is let go, and placed with the rest. Before the places are handed out again, so a
            // captured piece whose place has gone isn't given another meanwhile.
            if (heldObject != NoObject && !StillOn(heldObject, heldFrom, heldPlace)) LetGo();
            // So does a selected piece that can no longer be picked up there; one that still can has its targets
            // worked out again, since the position may have changed (a takeback, another player's free move).
            CheckSelection();
            AssignPlaces();
            if (selectedObject != NoObject) FindTargets(selectedFrom, selectedInReplay, selectedTargets, selectedMarks);
            bool animate = PlacingAnimates();
            UpdateFalls(animate);
            PlaceObjects(animate);
            _UpdatePickupable();
            // A held piece's targets may have changed (or the game ended, or replay began).
            if (heldObject != NoObject) FindTargets(heldFrom, replay, heldTargets, heldMarks);
            _UpdateLiftedMarks();
            _UpdateLastMoveHighlight();
            _UpdateCheckLines();
            _UpdatePromotionGhosts();
            _UpdateBlocker();
            int pieceCount = CountPieces(shown);
            PlaySound(_ChangeSound(change, pieceCount));
            shownPieceCount = pieceCount;
        }

        // ---- Sound effects (step 12.12) ----

        // Whether a change has a sound (ChessUtil._NoteSound): a move, a replay step or a free move.
        static bool MakesSound(int change) {
            return change == ChessBoard.ChangeMove || change == ChessBoard.ChangeReplayStep
                || change == ChessBoard.ChangeFreeMove;
        }

        // The move sound for change, placed with the position shown now holding pieceCount pieces and shownChecks
        // checks: for a move or a replay step, the sound of the move that led to the position (checkSound when it
        // checks, then promoteSound for a promotion, captureSound, moveSound), none at the start; for a free move,
        // checkSound when a king is in check, captureSound when it took a piece, moveSound otherwise. None for any
        // other change. Public for tests.
        public AudioClip _ChangeSound(int change, int pieceCount) {
            if (change == ChessBoard.ChangeFreeMove) {
                if (shownChecks > 0) return checkSound;
                return pieceCount < shownPieceCount ? captureSound : moveSound;
            }
            if (change != ChessBoard.ChangeMove && change != ChessBoard.ChangeReplayStep) return null;
            int move = board.LastMove();
            if (move == 0) return null;
            if (shownChecks > 0) return checkSound;
            if (ChessRules.MovePromotion(move) != ChessRules.NoPieceType) return promoteSound;
            if (ChessRules.MoveCaptured(move) != ChessRules.NoPieceType) return captureSound;
            return moveSound;
        }

        // Plays clip from audioSource (nothing for none) unless muted; lastSound and soundsPlayed follow it either way.
        void PlaySound(AudioClip clip) {
            if (!ChessUtil._PlaySound(clip, audioSource, muted)) return;
            lastSound = clip;
            soundsPlayed++;
        }

        static int CountPieces(byte[] squares) {
            int count = 0;
            for (int i = 0; i < 64; i++) {
                if (squares[i] != ChessRules.Empty) count++;
            }
            return count;
        }

        // Enables the blocker unless the local player has a seat (either colour), so only the seated players can walk
        // into the table. Called on every Refresh: seats change only with the board.
        public void _UpdateBlocker() {
            if (!Utilities.IsValid(blocker)) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            bool seated = Utilities.IsValid(board) && Utilities.IsValid(local) && board.IsPlayer(local.displayName);
            if (blocker.enabled == seated) blocker.enabled = !seated;
        }

        // ---- Input (docs/player-input.md) ----

        // A piece picked up by the local player (its ChessPiece's OnPickup): asks the board, and lets go of the piece
        // when it says no; the board's rejection then puts it back. A promotion waiting for its piece is dropped. A
        // captured piece (a pickup only in replay, step 13.4) is asked about by its colour and type.
        public void _OnPiecePickedUp(int obj) {
            if (obj < 0 || obj >= PieceCount) return;
            // The hand has it now, wherever it was going.
            StopAnimation(obj);
            ClearPromotion();
            heldObject = NoObject;
            if (Utilities.IsValid(board) && LiftAccepted(obj)) {
                heldObject = obj;
                heldFrom = objectSquare[obj];
                heldPlace = heldFrom == ChessRules.NoSquare ? objectPlace[obj] : NoPlace;
                FindTargets(heldFrom, board.replayMode, heldTargets, heldMarks);
                _UpdateLiftedMarks();
                return;
            }
            // A piece the board refused (or hidden, never a pickup) is just put back; the board told its rejection
            // itself.
            dirty = true;
            _UpdateLiftedMarks();
            LetGoOf(obj);
        }

        // A piece let go by the local player (its ChessPiece's OnDrop): forwards the drop on the square under its base
        // (off the board too, which the board rejects) if the board accepted its pickup; a king castles by being dropped
        // on its rook. A promotion waits for _ChoosePromotion with the pawn standing on its square; anything else is placed
        // from the board after its change, so a rejected drop goes back where it was. A captured piece is put back on
        // the square (PlaceCapturedObject), and goes back to its place dropped anywhere else.
        public void _OnPieceDropped(int obj) {
            if (obj < 0 || obj >= PieceCount) return;
            dirty = true;
            snapNext[obj] = true;
            if (obj != heldObject) return;
            if (!Utilities.IsValid(board) || !Utilities.IsValid(pieces[obj])) {
                heldObject = NoObject;
                _UpdateLiftedMarks();
                return;
            }
            heldObject = NoObject;
            _UpdateLiftedMarks();
            int to = _DropSquare(_PieceBase(obj));
            int from = heldFrom;
            if (from == ChessRules.NoSquare) {
                PlaceCapturedObject(obj, to);
            } else if (board.PieceDropped(from, to) == ChessBoard.InputPromotionNeeded) {
                WaitForPromotion(obj, from, to);
            }
        }

        // Asks the board to let the local player lift obj: PiecePickedUp for a piece on a square, CapturedPickedUp
        // for one in a captured place (step 13.4). Refused for a hidden object.
        bool LiftAccepted(int obj) {
            int square = objectSquare[obj];
            if (square != ChessRules.NoSquare) return board.PiecePickedUp(square) == ChessBoard.InputAccepted;
            if (!CapturedLiftable(obj)) return false;
            return board.CapturedPickedUp(_ObjectColor(obj), objectType[obj]) == ChessBoard.InputAccepted;
        }

        // Puts the captured piece obj back on square in replay (ChessBoard.PlaceCaptured; NoSquare, off the board, is
        // refused and it goes back to its place). Accepted, obj is the object standing there from now on, rather than
        // whichever free one the mapping would pick: the places are worked out from the position alone, so the place
        // that empties is the last of its type, not necessarily obj's.
        void PlaceCapturedObject(int obj, int square) {
            if (board.PlaceCaptured(_ObjectColor(obj), objectType[obj], square) != ChessBoard.InputAccepted) return;
            Unmap(square);
            Map(obj, square);
        }

        // Whether obj is a captured piece the local player could lift in replay (step 13.4), if they may touch the
        // pieces: standing, shown, in a captured place it keeps, in the replay the mapping was last brought up to.
        bool CapturedLiftable(int obj) {
            return shownReplay && objectSquare[obj] == ChessRules.NoSquare && obj != promotionObject && KeepsPlace(obj)
                && _IsObjectShown(obj);
        }

        // Whether obj still stands where it was lifted from: on square from, or (from NoSquare) in the captured place
        // place, which it still keeps, in replay.
        bool StillOn(int obj, int from, int place) {
            if (from != ChessRules.NoSquare) return objectSquare[obj] == from;
            return Utilities.IsValid(board) && board.replayMode && objectSquare[obj] == ChessRules.NoSquare
                && objectPlace[obj] == place && KeepsPlace(obj);
        }

        // A promotion ghost's answer (step 12.11): plays the waiting promotion with pieceType (ChessRules.Knight,
        // Bishop, Rook or Queen). There's no cancel: any other type is refused (InputRejected) and the pawn goes on
        // waiting. InputRejected with no promotion waiting.
        public int _ChoosePromotion(int pieceType) {
            if (promotionObject == NoObject || !Utilities.IsValid(board)) return ChessBoard.InputRejected;
            if (pieceType < ChessRules.Knight || pieceType > ChessRules.Queen) return ChessBoard.InputRejected;
            ClearPromotion();
            dirty = true;
            return board.PromotionChosen(promotionFrom, promotionTo, pieceType);
        }

        // ---- Promotion ghosts (step 12.11) ----

        // The board asked for a promotion's piece: obj, the pawn, stands on to while the ghosts float over it, and
        // dropping it there has committed to promoting, so its move or capture sound plays now (step 12.12; the
        // promotion's own sound when it's chosen). Meanwhile no piece is a pickup or a click target, so a hand near a
        // piece reaches the ghosts rather than the piece.
        void WaitForPromotion(int obj, int from, int to) {
            PlaySound(board.PieceAt(to) != ChessRules.Empty ? captureSound : moveSound);
            promotionObject = obj;
            promotionFrom = from;
            promotionTo = to;
            dirty = true;
            _UpdatePromotionGhosts();
            _UpdatePickupable();
        }

        // No promotion waits any more (chosen, or dropped by a change to the board); the pawn is placed from the board
        // on the next frame.
        void ClearPromotion() {
            if (promotionObject == NoObject) return;
            promotionObject = NoObject;
            dirty = true;
            _UpdatePromotionGhosts();
            _UpdatePickupable();
        }

        // The ChessRules piece type of a ghost: knight, bishop, rook, queen.
        public static int _GhostType(int ghost) {
            return ChessRules.Knight + ghost;
        }

        // The index of a colour's ghost in the copied promotion templates.
        public static int _GhostIndex(int color, int ghost) {
            return color * GhostCount + ghost;
        }

        // Where a ghost's base floats while a promotion waits (local space): in a row over the promotion square's
        // rank, GhostHeight squares up, GhostSpacing squares apart, its middle over the promotion square but no further
        // off the board's centre than GhostMaxOffset; knight to queen from left to right as the promoting side sees it
        // (from White's end along +x, from Black's along -x). Its template's offset is added to this.
        public Vector3 _GhostPosition(int ghost) {
            if (promotionObject == NoObject || promotionTo == ChessRules.NoSquare) return Vector3.zero;
            Vector3 position = _SquarePosition(promotionTo);
            float limit = GhostMaxOffset * squareSize;
            position.x = Mathf.Clamp(position.x, -limit, limit);
            float along = (ghost - (GhostCount - 1) * 0.5f) * GhostSpacing * squareSize;
            position.x += _ObjectColor(promotionObject) == ChessRules.White ? along : -along;
            position.y = GhostHeight * squareSize;
            return position;
        }

        // Shows the ghosts while a promotion waits, each dressed in the promoting side's promotion template (mesh,
        // materials, turn, size and a collider fitted to the mesh) and floating in its place (_GhostPosition); hides
        // them otherwise. Called on every Refresh, when a promotion starts or stops waiting, and on a set change.
        public void _UpdatePromotionGhosts() {
            bool shown = promotionObject != NoObject && promotionTo != ChessRules.NoSquare;
            int color = shown ? _ObjectColor(promotionObject) : -1;
            bool dress = shown && ghostsDressed != color;
            if (dress) ghostsDressed = color;
            for (int ghost = 0; ghost < promotionGhosts.Length && ghost < GhostCount; ghost++) {
                ChessPromotionGhost behaviour = promotionGhosts[ghost];
                if (!Utilities.IsValid(behaviour)) continue;
                GameObject ghostObject = behaviour.gameObject;
                if (shown) {
                    int index = _GhostIndex(color, ghost);
                    Transform t = ghostObject.transform;
                    if (dress) DressGhost(ghostObject, index);
                    t.localPosition = _GhostPosition(ghost) + ghostOffsets[index];
                    t.localRotation = ghostRotations[index];
                }
                if (ghostObject.activeSelf != shown) ghostObject.SetActive(shown);
            }
        }

        // Gives a ghost the copied promotion template at index: mesh, materials, size and fitted collider.
        void DressGhost(GameObject ghostObject, int index) {
            Mesh mesh = ghostMeshes[index];
            MeshFilter filter = ghostObject.GetComponent<MeshFilter>();
            if (Utilities.IsValid(filter) && Utilities.IsValid(mesh)) filter.sharedMesh = mesh;
            Material[] materials = ghostMaterials[index];
            MeshRenderer renderer = ghostObject.GetComponent<MeshRenderer>();
            if (Utilities.IsValid(renderer) && materials != null) renderer.sharedMaterials = materials;
            ghostObject.transform.localScale = ghostScales[index];
            BoxCollider box = ghostObject.GetComponent<BoxCollider>();
            if (Utilities.IsValid(box) && Utilities.IsValid(mesh)) {
                box.center = mesh.bounds.center;
                box.size = mesh.bounds.size;
            }
        }

        // Whether the ghosts show (every one of them active), for tests.
        public bool _AreGhostsShown() {
            if (promotionGhosts.Length < GhostCount) return false;
            for (int ghost = 0; ghost < GhostCount; ghost++) {
                if (!Utilities.IsValid(promotionGhosts[ghost]) || !promotionGhosts[ghost].gameObject.activeSelf) {
                    return false;
                }
            }
            return true;
        }

        // ---- Highlights (step 9.5a) ----

        // The two squares the last-move line joins for move (which 0 for its start, 1 for its end; NoSquare for none):
        // where the piece came from and where it stands now. A castle shows the king's path to its destination; when
        // the king doesn't move (Chess960), its rook's path instead (from its square to the f or d file).
        public static int _LastMoveSquare(int move, int which) {
            if (move == 0) return ChessRules.NoSquare;
            int from = ChessRules.MoveFrom(move);
            int to = ChessRules.MoveTo(move);
            if (!ChessRules.MoveIsCastle(move)) return which == 0 ? from : to;
            bool kingSide = ChessRules.FileOf(to) > ChessRules.FileOf(from);
            int rank = ChessRules.RankOf(from);
            int destination = ChessRules.SquareOf(kingSide ? ChessRules.FileG : ChessRules.FileC, rank);
            if (destination != from) return which == 0 ? from : destination;
            if (which == 0) return to;
            return ChessRules.SquareOf(kingSide ? ChessRules.FileF : ChessRules.FileD, rank);
        }

        // Shows the move that led to the position shown (in replay too): the line from its start to its end and the
        // mark on its start; both hidden when there's none, and the line when it starts and ends on one square (a
        // Chess960 castle moving neither piece). Called on every Refresh.
        public void _UpdateLastMoveHighlight() {
            int move = Utilities.IsValid(board) ? board.LastMove() : 0;
            int from = _LastMoveSquare(move, 0);
            int to = _LastMoveSquare(move, 1);
            if (Utilities.IsValid(lastMoveMark)) {
                bool shown = from != ChessRules.NoSquare;
                if (shown) lastMoveMark.transform.localPosition = _SquarePosition(from);
                if (lastMoveMark.activeSelf != shown) lastMoveMark.SetActive(shown);
            }
            if (Utilities.IsValid(lastMoveLine)) _PlaceLine(lastMoveLine, from, to);
        }

        // Shows a line (a bar one local unit long along its +z, as lastMoveLine) from square from to square to: stood
        // on from, turned towards to and stretched to reach it. Hidden for NoSquare, or when both are one square.
        public void _PlaceLine(GameObject lineObject, int from, int to) {
            bool shown = from != ChessRules.NoSquare && to != ChessRules.NoSquare && to != from;
            if (shown) {
                Vector3 start = _SquarePosition(from);
                Vector3 path = _SquarePosition(to) - start;
                Transform line = lineObject.transform;
                line.localPosition = start;
                line.localRotation = Quaternion.LookRotation(path, Vector3.up);
                line.localScale = new Vector3(1, 1, path.magnitude);
            }
            if (lineObject.activeSelf != shown) lineObject.SetActive(shown);
        }

        // Shows a check line from each piece giving check to the king it attacks in the position shown (in replay
        // too, and once a game ends in checkmate), hiding the rest. Called on every Refresh.
        public void _UpdateCheckLines() {
            int count = Utilities.IsValid(board) ? board.Checks(checkers, checkedKings) : 0;
            shownChecks = count;
            for (int i = 0; i < checkLines.Length; i++) {
                if (!Utilities.IsValid(checkLines[i])) continue;
                if (i < count) _PlaceLine(checkLines[i], checkers[i], checkedKings[i]);
                else if (checkLines[i].activeSelf) checkLines[i].SetActive(false);
            }
        }

        // Marks the held or selected piece's legal targets (heldMarks or selectedMarks), each with its look; none in
        // replay, where every square is one, and none with nothing lifted. Called on every lift change.
        public void _UpdateTargetHighlights() {
            int[] marks = null;
            if (heldObject != NoObject) marks = heldMarks;
            else if (selectedObject != NoObject) marks = selectedMarks;
            bool replay = !Utilities.IsValid(board) || board.replayMode;
            for (int square = 0; square < 64 && square < targetHighlights.Length; square++) {
                GameObject highlight = targetHighlights[square];
                if (!Utilities.IsValid(highlight)) continue;
                int mark = marks != null && !replay ? marks[square] : MarkNone;
                bool shown = mark != MarkNone;
                if (shown) StyleTargetMark(highlight, mark);
                if (highlight.activeSelf != shown) highlight.SetActive(shown);
            }
        }

        // Gives a legal-target mark the look of its kind: size, and material (when the view has one for it).
        void StyleTargetMark(GameObject highlight, int mark) {
            float size = mark == MarkMove ? SmallMarkScale : 1;
            Transform t = highlight.transform;
            if (t.localScale.x != size) t.localScale = new Vector3(size, 1, size);
            Material material = mark == MarkCapture ? captureMaterial : targetMaterial;
            if (!Utilities.IsValid(material)) return;
            MeshRenderer renderer = highlight.GetComponent<MeshRenderer>();
            if (Utilities.IsValid(renderer) && renderer.sharedMaterial != material) renderer.sharedMaterial = material;
        }

        // Whether square's legal-target mark is shown, for tests.
        public bool _IsTargetHighlighted(int square) {
            GameObject highlight = targetHighlights[square];
            return Utilities.IsValid(highlight) && highlight.activeSelf;
        }

        // The kind of legal-target mark square shows (MarkNone when hidden), read from how it looks, for tests:
        // MarkMove when scaled down, otherwise MarkCapture with captureMaterial and MarkCastle with targetMaterial
        // (-1 for a look that's none of these).
        public int _TargetMarkShown(int square) {
            if (!_IsTargetHighlighted(square)) return MarkNone;
            GameObject highlight = targetHighlights[square];
            MeshRenderer renderer = highlight.GetComponent<MeshRenderer>();
            Material material = Utilities.IsValid(renderer) ? renderer.sharedMaterial : null;
            Vector3 scale = highlight.transform.localScale;
            bool small = Mathf.Abs(scale.x - SmallMarkScale) < 0.001f && Mathf.Abs(scale.z - SmallMarkScale) < 0.001f;
            bool full = Mathf.Abs(scale.x - 1) < 0.001f && Mathf.Abs(scale.z - 1) < 0.001f;
            if (small && material == targetMaterial) return MarkMove;
            if (full && material == targetMaterial) return MarkCastle;
            if (full && material == captureMaterial) return MarkCapture;
            return -1;
        }

        // Everything shown for the piece the local player has lifted (held or selected): the legal-target marks.
        public void _UpdateLiftedMarks() {
            _UpdateTargetHighlights();
        }

        // The square under a point (world space) on the playing surface, by where it is over the board whatever its
        // height; NoSquare outside the board.
        public int _DropSquare(Vector3 position) {
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
        // pick up it selects that one instead; anything else is a placing the board rejects, which puts it back. A
        // captured piece in replay (step 13.4) is selected in its place, and put back there by a second click.
        public void _OnPieceClicked(int obj) {
            if (obj < 0 || obj >= PieceCount || _UsesPickups() || !Utilities.IsValid(board)) return;
            int square = objectSquare[obj];
            if (square == ChessRules.NoSquare) {
                if (obj == selectedObject) Unselect();
                else if (CapturedLiftable(obj)) Select(obj);
                return;
            }
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
        public void _OnSquareClicked(int square) {
            if (square < 0 || square >= 64 || selectedObject == NoObject || _UsesPickups()) return;
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
            if (LiftAccepted(obj)) {
                selectedObject = obj;
                selectedFrom = objectSquare[obj];
                selectedPlace = selectedFrom == ChessRules.NoSquare ? objectPlace[obj] : NoPlace;
                selectedInReplay = board.replayMode;
                FindTargets(selectedFrom, selectedInReplay, selectedTargets, selectedMarks);
                dirty = true;
                VRCPlayerApi local = Networking.LocalPlayer;
                GameObject piece = Utilities.IsValid(pieces[obj]) ? pieces[obj].gameObject : null;
                if (Utilities.IsValid(local) && Utilities.IsValid(piece) && !Networking.IsOwner(local, piece)) {
                    Networking.SetOwner(local, piece);
                }
            }
            _UpdateLiftedMarks();
            _UpdateClickables();
        }

        // Puts the selected piece back down where it was, unselected; the board isn't asked.
        void Unselect() {
            ClearSelection();
            _UpdateLiftedMarks();
            _UpdateClickables();
        }

        // Plays the selected piece's move to square: a promotion waits for _ChoosePromotion as a dropped one does, with
        // the pawn standing on square; anything else is placed from the board after its change, so a rejected placing
        // puts the piece back, unselected. A captured piece is put back on square (PlaceCapturedObject).
        void Place(int square) {
            int obj = selectedObject;
            int from = selectedFrom;
            ClearSelection();
            _UpdateLiftedMarks();
            if (from == ChessRules.NoSquare) {
                PlaceCapturedObject(obj, square);
            } else if (board.PieceDropped(from, square) == ChessBoard.InputPromotionNeeded) {
                WaitForPromotion(obj, from, square);
            }
            _UpdateClickables();
        }

        // Nothing selected any more; the piece is placed back down on the next frame.
        void ClearSelection() {
            if (selectedObject == NoObject) return;
            selectedObject = NoObject;
            selectedFrom = ChessRules.NoSquare;
            selectedPlace = NoPlace;
            for (int i = 0; i < 64; i++) {
                selectedTargets[i] = false;
                selectedMarks[i] = MarkNone;
            }
            dirty = true;
        }

        // Puts a selected piece back down, unselected, once it can no longer be picked up where it was: the board
        // changed (a reset, the game ending, replay entered or left, the piece's square taken, the turn passing; a
        // captured piece's place gone, or touching the pieces no longer allowed), or the local player switched to
        // pickups.
        void CheckSelection() {
            if (selectedObject == NoObject) return;
            if (!_UsesPickups() && Utilities.IsValid(board) && board.replayMode == selectedInReplay
                && StillOn(selectedObject, selectedFrom, selectedPlace)
                && (selectedFrom != ChessRules.NoSquare ? board.CanPickUp(selectedFrom) : board.MayTouchPieces())) {
                return;
            }
            ClearSelection();
            _UpdateLiftedMarks();
        }

        // Whether a click on obj places the selected piece on its square rather than selecting obj: the square is a
        // target and obj isn't one of the selected piece's own (outside replay, a target with an own piece on it is
        // a castling rook).
        bool PlacesOn(int obj) {
            int square = objectSquare[obj];
            if (selectedObject == NoObject || square == ChessRules.NoSquare || !selectedTargets[square]) return false;
            return !selectedInReplay || _ObjectColor(obj) != _ObjectColor(selectedObject);
        }

        // Works out targets (selectedTargets or heldTargets) for the piece lifted from from, from the board's legal
        // moves (every other square in replay, where moves are free), and each target's mark (selectedMarks or
        // heldMarks; none in replay): MarkCapture for a capture (en passant on the square the pawn moves to),
        // MarkCastle on a castle's rook square, MarkMove on the rest (a castle's king destination included).
        void FindTargets(int from, bool replay, bool[] targets, int[] marks) {
            for (int i = 0; i < 64; i++) {
                targets[i] = replay && i != from;
                marks[i] = MarkNone;
            }
            if (replay) return;
            int count = board.LegalMovesFrom(from, targetMoves);
            for (int i = 0; i < count; i++) {
                int move = targetMoves[i];
                int to = ChessRules.MoveTo(move);
                targets[to] = true;
                if (!ChessRules.MoveIsCastle(move)) {
                    int mark = ChessRules.MoveCaptured(move) != ChessRules.NoPieceType ? MarkCapture : MarkMove;
                    if (mark > marks[to]) marks[to] = mark;
                    continue;
                }
                marks[to] = MarkCastle;
                // A castle stores its rook's square; the board also castles on a drop on the king's destination,
                // unless a normal move ends there (which is then a target anyway) or the king doesn't move.
                int destination = ChessRules.SquareOf(
                    ChessRules.FileOf(to) > ChessRules.FileOf(from) ? ChessRules.FileG : ChessRules.FileC,
                    ChessRules.RankOf(from));
                if (destination == from) continue;
                targets[destination] = true;
                if (marks[destination] == MarkNone) marks[destination] = MarkMove;
            }
        }

        // The I18n's language changed (its AddLanguageChangeListener): the hover texts are set again.
        public void OnLanguageChange() {
            SetHoverTexts();
            _UpdateClickables();
        }

        // Reads the hover texts in the language now and puts the fixed ones on: "Move" on every piece's pickup, and
        // each promotion ghost's "Promote to queen". The click targets' are put on as they become clickable.
        void SetHoverTexts() {
            ChessUtil._CheckI18n(i18n, "[ChessBoardView] " + gameObject.name);
            hoverSelect = ChessUtil.Text(i18n, "hover_select");
            hoverPutBack = ChessUtil.Text(i18n, "hover_put_back");
            hoverPlace = ChessUtil.Text(i18n, "hover_place");
            hoverCapture = ChessUtil.Text(i18n, "hover_capture");
            hoverCastle = ChessUtil.Text(i18n, "hover_castle");
            string move = ChessUtil.Text(i18n, "hover_move");
            for (int obj = 0; obj < piecePickups.Length; obj++) {
                if (Utilities.IsValid(piecePickups[obj])) piecePickups[obj].InteractionText = move;
            }
            for (int ghost = 0; ghost < promotionGhosts.Length; ghost++) {
                ChessPromotionGhost behaviour = promotionGhosts[ghost];
                if (!Utilities.IsValid(behaviour)) continue;
                string type = ChessPieceSet._TypeName(behaviour.pieceType);
                behaviour.InteractionText = ChessUtil.Text(i18n, "hover_promote_" + type);
            }
        }

        // Makes the pieces and squares click targets exactly where a click does something for the local player in
        // desktop mode, with interact text saying what: with nothing selected, the pieces the board would let them
        // pick up; with one selected, that piece and its square (put back), its targets (pieces to capture or castle
        // with, squares to place on) and the other pieces they could select instead. In replay, the captured pieces
        // too (step 13.4: "Select", or "Put back" for the selected one). Players using pickups get none, and nobody
        // does while the promotion ghosts show.
        // Called after every change, once a second and on every click.
        public void _UpdateClickables() {
            bool clicks = !_UsesPickups() && Utilities.IsValid(board) && promotionObject == NoObject;
            bool capturedClicks = clicks && shownReplay && board.MayTouchPieces();
            for (int obj = 0; obj < PieceCount; obj++) {
                ChessPiece behaviour = pieceBehaviours[obj];
                if (!Utilities.IsValid(behaviour)) continue;
                int square = objectSquare[obj];
                string text = null;
                if (clicks && square != ChessRules.NoSquare) {
                    if (obj == selectedObject) {
                        text = hoverPutBack;
                    } else if (PlacesOn(obj)) {
                        text = _ObjectColor(obj) == _ObjectColor(selectedObject) ? hoverCastle : hoverCapture;
                    } else if (board.CanPickUp(square)) {
                        text = hoverSelect;
                    }
                } else if (capturedClicks && CapturedLiftable(obj)) {
                    text = obj == selectedObject ? hoverPutBack : hoverSelect;
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
                if (clickable) target.InteractionText = square == selectedFrom ? hoverPutBack : hoverPlace;
            }
        }

        // Whether obj can be clicked now (desktop mode), for tests.
        public bool _IsPieceClickable(int obj) {
            ChessPiece behaviour = pieceBehaviours[obj];
            return Utilities.IsValid(behaviour) && !behaviour.DisableInteractive;
        }

        public bool _IsSquareClickable(int square) {
            ChessSquare target = squareTargets[square];
            return Utilities.IsValid(target) && target.gameObject.activeSelf;
        }

        // Stops treating a piece as held and takes it out of the local player's hand, to be placed from the board.
        void LetGo() {
            int obj = heldObject;
            heldObject = NoObject;
            _UpdateLiftedMarks();
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
        public bool _UsesPickups() {
            if (inputStyle == InputPickups) return true;
            if (inputStyle == InputClicks) return false;
            VRCPlayerApi local = Networking.LocalPlayer;
            return Utilities.IsValid(local) && local.IsUserInVR();
        }

        // Whether player may touch the pieces (the board decides); asked by each piece's OnOwnershipRequest.
        public bool _PlayerMayTouchPieces(VRCPlayerApi player) {
            return Utilities.IsValid(board) && board.PlayerMayTouchPieces(player);
        }

        // Makes the pieces on the board pickups for the local player while they use pickups and may touch the pieces;
        // pieces off the board only in replay, standing in their captured places (step 13.4: put back as free moves),
        // and none while the promotion ghosts show. Called after every change, once a second and when a promotion
        // starts or stops waiting.
        public void _UpdatePickupable() {
            nextPickupCheck = Time.time + PickupCheckInterval;
            bool allowed = _UsesPickups() && Utilities.IsValid(board) && board.MayTouchPieces()
                && promotionObject == NoObject;
            // A piece the local player may no longer touch leaves their hand.
            if (!allowed && heldObject != NoObject) {
                LetGo();
                dirty = true;
            }
            for (int obj = 0; obj < PieceCount; obj++) {
                VRCPickup pickup = piecePickups[obj];
                if (!Utilities.IsValid(pickup)) continue;
                bool pickupable = allowed && (objectSquare[obj] != ChessRules.NoSquare || CapturedLiftable(obj));
                if (pickup.pickupable != pickupable) pickup.pickupable = pickupable;
            }
            // Desktop mode's selection and click targets depend on the same things (and on whose turn it is).
            CheckSelection();
            _UpdateClickables();
        }

        // The debug view's button: Auto, then Pickups, then Clicks, on this client only.
        public void _CycleInputStyle() {
            _SetInputStyle((inputStyle + 1) % 3);
        }

        public void _SetInputStyle(int style) {
            inputStyle = style;
            _UpdatePickupable();
        }

        public string _InputStyleName() {
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
        // board, then each square without its object gets a free one of its colour, preferring one not standing in a
        // captured place that stays (KeepsPlace), then one already of its type, then the one that stood nearest.
        void Reconcile(byte[] shown) {
            for (int square = 0; square < 64; square++) {
                int obj = squareObject[square];
                if (obj != NoObject && _ObjectPiece(obj) != shown[square]) Unmap(square);
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
                    int score = (KeepsPlace(obj) ? 2000 : 0) + (objectType[obj] == type ? 0 : 1000)
                        + Distance(objectLastSquare[obj], square);
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
            objectPlace[obj] = NoPlace;
        }

        // ---- Captured pieces (step 12.4, docs/physical-pieces.md) ----

        // The places' targets that exist (a world creator may delete some), in order, by colour.
        void CollectPlaceTargets() {
            for (int color = ChessRules.White; color <= ChessRules.Black; color++) {
                Transform[] places = color == ChessRules.White ? whiteCapturedPlaces : blackCapturedPlaces;
                int count = 0;
                if (places != null) {
                    for (int i = 0; i < places.Length && count < MaxCaptured; i++) {
                        if (Utilities.IsValid(places[i])) placeTargets[color * MaxCaptured + count++] = places[i];
                    }
                }
                placeTargetCounts[color] = count;
            }
        }

        // Whether obj stands in a captured place it keeps: one the position shown still has, for its type.
        bool KeepsPlace(int obj) {
            int place = objectPlace[obj];
            if (place == NoPlace || objectSquare[obj] != ChessRules.NoSquare) return false;
            int color = _ObjectColor(obj);
            return place < capturedCounts[color] && capturedTypes[color * MaxCaptured + place] == objectType[obj];
        }

        // Puts the objects off the board in the captured places: each keeps a place it still has, and each empty place
        // takes a free object of its colour, preferring one that has just left the board (the piece captured), then
        // one already of its type, and wears the place's type. Free objects left over have no place (hidden).
        void AssignPlaces() {
            for (int obj = 0; obj < PieceCount; obj++) {
                if (!KeepsPlace(obj)) objectPlace[obj] = NoPlace;
            }
            for (int color = ChessRules.White; color <= ChessRules.Black; color++) {
                int first = color == ChessRules.White ? 0 : PiecesPerColor;
                for (int place = 0; place < capturedCounts[color]; place++) {
                    bool taken = false;
                    for (int obj = first; obj < first + PiecesPerColor && !taken; obj++) {
                        taken = objectPlace[obj] == place;
                    }
                    if (taken) continue;
                    int type = capturedTypes[color * MaxCaptured + place];
                    int best = NoObject;
                    int bestScore = int.MaxValue;
                    for (int obj = first; obj < first + PiecesPerColor; obj++) {
                        if (objectSquare[obj] != ChessRules.NoSquare || objectPlace[obj] != NoPlace) continue;
                        if (obj == promotionObject) continue;
                        bool justLeft = placedSquare[obj] >= 0;
                        int score = (justLeft ? 0 : 2000) + (objectType[obj] == type ? 0 : 1000);
                        if (score < bestScore) {
                            best = obj;
                            bestScore = score;
                        }
                    }
                    // More captured than objects off the board (a hand-made position): that place stays empty.
                    if (best == NoObject) continue;
                    objectPlace[best] = place;
                    objectType[best] = type;
                }
            }
        }

        // Whether obj is shown: on the board (or waiting for its promotion piece), or in a captured place with a
        // target. Objects not needed by the position and captured pieces beyond the last target are hidden.
        public bool _IsObjectShown(int obj) {
            if (objectType[obj] == ChessRules.NoPieceType) return false;
            if (objectSquare[obj] != ChessRules.NoSquare || obj == promotionObject) return true;
            int place = objectPlace[obj];
            return place != NoPlace && place < placeTargetCounts[_ObjectColor(obj)];
        }

        // Where a captured place stands in the view's local space (its target's world position brought into it).
        public Vector3 _CapturedPlacePosition(int color, int place) {
            Transform target = placeTargets[color * MaxCaptured + place];
            if (!Utilities.IsValid(target)) return Vector3.zero;
            return transform.InverseTransformPoint(target.position);
        }

        void Unmap(int square) {
            int obj = squareObject[square];
            if (obj == NoObject) return;
            squareObject[square] = NoObject;
            objectSquare[obj] = ChessRules.NoSquare;
        }

        // The piece object obj stands for (ChessRules piece encoding), whether on the board or not.
        public int _ObjectPiece(int obj) {
            if (objectType[obj] == ChessRules.NoPieceType) return ChessRules.Empty;
            return ChessRules.PieceOf(_ObjectColor(obj), objectType[obj]);
        }

        public int _ObjectColor(int obj) {
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

        // Whether this Refresh's placing animates: the board's changes since the last Refresh may animate
        // (_ChangeAnimates) and moved at most MaxAnimatedObjects objects between squares. Counts the changes as placed.
        bool PlacingAnimates() {
            bool animate = animateMoves && moveSeconds > 0 && changesSinceRefresh > 0 && changesAnimate;
            if (animate) {
                int moved = 0;
                for (int obj = 0; obj < PieceCount; obj++) {
                    if (obj != heldObject && objectSquare[obj] != placedSquare[obj]) moved++;
                }
                animate = moved <= MaxAnimatedObjects;
            }
            changesSinceRefresh = 0;
            return animate;
        }

        // Puts every object but the held one where the mapping says (PrepareObject). If the placing animates
        // (PlacingAnimates), each object whose place changed travels there, those leaving the board waiting for those
        // landing on it; otherwise (and for a piece the local player has just let go of) it's put there at once. An
        // object already travelling to its place carries on.
        void PlaceObjects(bool animate) {
            // The objects on the board first, so those leaving it know whether to wait.
            bool landing = false;
            for (int pass = 0; pass < 2; pass++) {
                for (int obj = 0; obj < PieceCount; obj++) {
                    if (obj == heldObject || !Utilities.IsValid(pieces[obj])) continue;
                    bool onBoard = objectSquare[obj] != ChessRules.NoSquare || obj == promotionObject;
                    if (onBoard != (pass == 0)) continue;
                    Transform piece = pieces[obj];
                    GameObject pieceObject = piece.gameObject;
                    // Not needed by the position, or captured beyond the last place: hidden until it is.
                    if (!_IsObjectShown(obj)) {
                        StopAnimation(obj);
                        placedSquare[obj] = objectSquare[obj];
                        if (pieceObject.activeSelf) pieceObject.SetActive(false);
                        continue;
                    }
                    // Coming back from hidden: put straight in place, from wherever it was left.
                    bool appearing = !pieceObject.activeSelf;
                    if (appearing) pieceObject.SetActive(true);
                    Vector3 target = PrepareObject(obj);
                    placedSquare[obj] = objectSquare[obj];
                    bool snap = snapNext[obj] || !animateMoves || appearing;
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
        public static bool _ChangeAnimates(int change) {
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

        // ---- The checkmated or resigning king (step 12.8) ----

        // Sets each object's fall: the king fallen in the position shown (mated or resigning) is to lie, every other
        // object to stand. A king starts falling once the pieces travelling in this placing have landed (moveSeconds
        // on; at once when none does, as on a resignation), one standing up at once. Without
        // animation (this placing's, or animateMoves off), and for an object that left its square or has none, the
        // fall is done at once. Called on every Refresh, before placing.
        void UpdateFalls(bool animate) {
            int square = Utilities.IsValid(board) ? board.FallenKingSquare() : ChessRules.NoSquare;
            int fallen = square == ChessRules.NoSquare ? NoObject : squareObject[square];
            bool travelling = false;
            for (int obj = 0; obj < PieceCount; obj++) {
                if (obj != heldObject && objectSquare[obj] != placedSquare[obj]) travelling = true;
            }
            for (int obj = 0; obj < PieceCount; obj++) {
                float target = obj == fallen ? 1 : 0;
                if (target != fallTarget[obj]) {
                    fallTarget[obj] = target;
                    fallStart[obj] = Time.time + (target > 0 && travelling ? moveSeconds : 0);
                    if (!animate) fallAmount[obj] = target;
                }
                if (!animateMoves || fallSeconds <= 0 || objectSquare[obj] == ChessRules.NoSquare
                    || objectSquare[obj] != placedSquare[obj]) {
                    fallAmount[obj] = target;
                }
                if (fallAmount[obj] != target) falling = true;
            }
        }

        // Moves the falling (or rising) objects on, every frame while any is: a whole fall takes fallSeconds. A held
        // or travelling object's fall goes on without moving it.
        void StepFalls() {
            falling = false;
            float now = Time.time;
            float step = fallSeconds > 0 ? Time.deltaTime / fallSeconds : 1;
            for (int obj = 0; obj < PieceCount; obj++) {
                float target = fallTarget[obj];
                float amount = fallAmount[obj];
                if (amount == target) continue;
                if (now >= fallStart[obj]) {
                    amount = target > amount ? Mathf.Min(target, amount + step) : Mathf.Max(target, amount - step);
                    fallAmount[obj] = amount;
                    Transform piece = pieces[obj];
                    if (obj != heldObject && !animating[obj] && Utilities.IsValid(piece)) {
                        piece.localPosition = _ObjectPlace(obj);
                        piece.localRotation = _ObjectRotation(obj);
                    }
                }
                if (amount != target) falling = true;
            }
        }

        // Whether obj is falling over or standing up now (or waiting to), for tests. A falling object always stands
        // where _ObjectPlace says, so the debug view's placement check needs no exception for it.
        public bool _IsFalling(int obj) {
            return fallAmount[obj] != fallTarget[obj];
        }

        // The turn obj's fall adds to its template's, about its base, in the view's local space: none standing, a
        // quarter turn towards _FallDirection lying, eased in as a falling piece speeds up.
        public Quaternion _FallTip(int obj) {
            float amount = fallAmount[obj];
            int square = objectSquare[obj];
            if (amount <= 0 || square == ChessRules.NoSquare) return Quaternion.identity;
            Quaternion lying = Quaternion.FromToRotation(Vector3.up, _FallDirection(square));
            return Quaternion.Slerp(Quaternion.identity, lying, amount * amount);
        }

        // Which way a king on square falls (local space): towards the board's centre along the files (x) or the
        // ranks (z), whichever it stands further from it along (the ranks when as far), so it lies on the board. Along
        // an axis rather than straight at the centre, so the mesh's bounds give its lowest point (FallLift).
        public static Vector3 _FallDirection(int square) {
            float file = ChessRules.FileOf(square) - 3.5f;
            float rank = ChessRules.RankOf(square) - 3.5f;
            if (Mathf.Abs(file) > Mathf.Abs(rank)) return new Vector3(file > 0 ? -1 : 1, 0, 0);
            return new Vector3(0, 0, rank > 0 ? -1 : 1);
        }

        // How far obj, turned by tip about its base, is lifted so its lowest point (by its mesh's bounds, as its
        // template turns and sizes it) is as high as when it stands: it rolls over the edge of its base as it falls.
        float FallLift(int obj, Quaternion tip) {
            int type = objectType[obj];
            if (type == ChessRules.NoPieceType || !hasSet[_ObjectColor(obj)]) return 0;
            int index = _TemplateIndex(_ObjectColor(obj), type);
            Mesh mesh = setMeshes[index];
            if (!Utilities.IsValid(mesh)) return 0;
            Bounds bounds = mesh.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            Quaternion rotation = setRotations[index];
            Vector3 scale = setScales[index];
            Vector3 offset = setOffsets[index];
            float standing = float.MaxValue;
            float tipped = float.MaxValue;
            for (int corner = 0; corner < 8; corner++) {
                Vector3 local = center + new Vector3((corner & 1) == 0 ? -extents.x : extents.x,
                    (corner & 2) == 0 ? -extents.y : extents.y, (corner & 4) == 0 ? -extents.z : extents.z);
                Vector3 point = offset + rotation * Vector3.Scale(scale, local);
                standing = Mathf.Min(standing, point.y);
                tipped = Mathf.Min(tipped, (tip * point).y);
            }
            return Mathf.Max(0, standing - tipped);
        }

        // How obj is turned (local space): as its template is, tipped over by its fall; as it is without a template.
        public Quaternion _ObjectRotation(int obj) {
            int type = objectType[obj];
            if (type == ChessRules.NoPieceType || !hasSet[_ObjectColor(obj)]) {
                return Utilities.IsValid(pieces[obj]) ? pieces[obj].localRotation : Quaternion.identity;
            }
            return _FallTip(obj) * setRotations[_TemplateIndex(_ObjectColor(obj), type)];
        }

        // Brings obj's model (mesh, materials, size and collider) and turn up to the mapping and the applied set, and
        // returns where it belongs: on its square, or beside the board when it's off it. Each piece is turned as its
        // template is (every time, since a hand may have turned it).
        Vector3 PrepareObject(int obj) {
            Transform piece = pieces[obj];
            int type = objectType[obj];
            if (type == ChessRules.NoPieceType || !hasSet[_ObjectColor(obj)]) return _ObjectPlace(obj);
            int index = _TemplateIndex(_ObjectColor(obj), type);
            if (type != objectMeshType[obj]) {
                objectMeshType[obj] = type;
                Mesh mesh = setMeshes[index];
                if (Utilities.IsValid(pieceFilters[obj]) && Utilities.IsValid(mesh)) pieceFilters[obj].sharedMesh = mesh;
                Material[] materials = setMaterials[index];
                if (Utilities.IsValid(pieceRenderers[obj]) && materials != null) {
                    pieceRenderers[obj].sharedMaterials = materials;
                }
                piece.localScale = setScales[index];
                BoxCollider box = pieceColliders[obj];
                if (Utilities.IsValid(box) && Utilities.IsValid(mesh)) {
                    box.center = mesh.bounds.center;
                    box.size = mesh.bounds.size;
                }
            }
            piece.localRotation = _FallTip(obj) * setRotations[index];
            return _ObjectPlace(obj);
        }

        // Where obj's origin belongs now (local space), changing nothing: on its square, or in its captured place when
        // it's off the board, moved by its template's offset; where it is when it's hidden. Public for the debug view's
        // placement check (step 9.6).
        public Vector3 _ObjectPlace(int obj) {
            int square = objectSquare[obj];
            // A pawn waiting for its promotion piece stands where it was dropped.
            if (obj == promotionObject) square = promotionTo;
            if (square == ChessRules.NoSquare && !_IsObjectShown(obj)) {
                return Utilities.IsValid(pieces[obj]) ? pieces[obj].localPosition : Vector3.zero;
            }
            Vector3 position = (square != ChessRules.NoSquare ? _SquarePosition(square)
                : _CapturedPlacePosition(_ObjectColor(obj), objectPlace[obj])) + _PieceOffset(obj);
            // A selected piece (desktop mode) stands lifted over its square.
            if (obj == selectedObject) position += new Vector3(0, SelectLift * squareSize, 0);
            // A falling or fallen king (step 12.8) is tipped over about its base and lifted to keep its lowest point.
            if (objectSquare[obj] != ChessRules.NoSquare && fallAmount[obj] > 0) {
                Quaternion tip = _FallTip(obj);
                Vector3 offset = _PieceOffset(obj);
                position += tip * offset - offset + new Vector3(0, FallLift(obj, tip), 0);
            }
            return position;
        }

        // The centre of a square on the playing surface, in the view's local space.
        public Vector3 _SquarePosition(int square) {
            return new Vector3((ChessRules.FileOf(square) - 3.5f) * squareSize, 0,
                (ChessRules.RankOf(square) - 3.5f) * squareSize);
        }
    }
}
