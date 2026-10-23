using System.Collections.Generic;
using SiggiChessBoard;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;
using static ChessBuilderUtil;

// Builds the physical board (PLAN.md 9.1, ChessBoardView.cs) in the open scene for the selected ChessBoard, or the
// scene's only one: a table, the board's surface and squares, the 32 piece objects, the highlights, the promotion
// ghosts (step 12.11) and the captured-piece places (step 12.4), wired to a ChessBoardView; and its UI (step 12.1,
// ChessBoardUI.cs) on an object of its own beside the view, standing where the view does (the board's centre): the
// button panels (steps 9.4a and 9.4b), the
// status panels (step 9.5c) and the PGN box (steps 11.2a, 11.2b); and a sound source on each, with the clips in
// Sounds/ (step 12.12: the view's move sounds, the UI's the rest). Players walk through all of it (no solid colliders
// but the pieces', on the Pickup layer); the view's blocker keeps out those without a seat (step 9.5a2). An existing
// view for the same board is replaced, keeping its place, and so is its UI; a world creator's own model in it, and
// the model parts they deleted, are kept (step 12.2, KeepCustomModel). The piece meshes, the highlight meshes
// and the materials are generated placeholders in Assets/Siggi/ChessBoard/Models/, made only when missing, so they
// can be replaced there. The piece models come from a ChessPieceSet per side (steps 12.3, 12.10): a board's arbiter
// without its Default White / Black Piece Set gets one made from the generated meshes, as a child
// (EnsureDefaultPieceSet). Menu: Tools > Chess Board > Create Board View. Plain editor C#, not Udon.
public static class ChessBoardViewBuilder {
    const string ViewScriptPath = "Assets/Siggi/ChessBoard/ChessBoardView.cs";
    const string ViewProgramPath = "Assets/Siggi/ChessBoard/ChessBoardView.asset";
    const string UIScriptPath = "Assets/Siggi/ChessBoard/ChessBoardUI.cs";
    const string UIProgramPath = "Assets/Siggi/ChessBoard/ChessBoardUI.asset";
    const string PieceScriptPath = "Assets/Siggi/ChessBoard/ChessPiece.cs";
    const string PieceProgramPath = "Assets/Siggi/ChessBoard/ChessPiece.asset";
    const string SquareScriptPath = "Assets/Siggi/ChessBoard/ChessSquare.cs";
    const string SquareProgramPath = "Assets/Siggi/ChessBoard/ChessSquare.asset";
    const string PieceSetScriptPath = "Assets/Siggi/ChessBoard/ChessPieceSet.cs";
    const string PieceSetProgramPath = "Assets/Siggi/ChessBoard/ChessPieceSet.asset";
    const string GhostScriptPath = "Assets/Siggi/ChessBoard/ChessPromotionGhost.cs";
    const string GhostProgramPath = "Assets/Siggi/ChessBoard/ChessPromotionGhost.asset";
    const string ModelsFolder = "Assets/Siggi/ChessBoard/Models";

    const float SquareSize = 0.05f;
    const float Border = 0.02f;
    const float BoardThickness = 0.02f;
    const float TableHeight = 0.8f;
    // The table's depth (along z), in board widths: room at each end for the status panel and the button panel, the
    // status panel's near edge about 2 cm clear of the board.
    const float TableDepthFactor = 2.25f;

    // The status panels' and the PGN box's light text.
    static readonly Color PaleText = new Color(0.95f, 0.95f, 0.95f);
    static readonly string[] TypeNames = { "", "Pawn", "Knight", "Bishop", "Rook", "Queen", "King" };
    // The back rank from a to h, by piece type, for the pieces' places in the editor.
    static readonly int[] BackRank = {
        ChessRules.Rook, ChessRules.Knight, ChessRules.Bishop, ChessRules.Queen, ChessRules.King, ChessRules.Bishop,
        ChessRules.Knight, ChessRules.Rook,
    };

    [MenuItem("Tools/Chess Board/Create Board View")]
    static void Create() {
        ChessBoard board = FindBoard("Create Board View", "view");
        if (board == null) return;
        if (!EnsureProgramAssets("Create Board View", ViewScriptPath, ViewProgramPath, UIScriptPath, UIProgramPath,
            PieceScriptPath, PieceProgramPath, SquareScriptPath, SquareProgramPath, PieceSetScriptPath,
            PieceSetProgramPath, GhostScriptPath, GhostProgramPath)) return;

        Mesh[] meshes = PieceMeshes();
        Material whitePieces = MaterialAsset("White Pieces", new Color(0.92f, 0.89f, 0.82f), 0.55f);
        Material blackPieces = MaterialAsset("Black Pieces", new Color(0.13f, 0.12f, 0.12f), 0.6f);
        Material lightSquares = MaterialAsset("Light Squares", new Color(0.93f, 0.85f, 0.71f), 0.3f);
        Material darkSquares = MaterialAsset("Dark Squares", new Color(0.55f, 0.37f, 0.24f), 0.3f);
        Material frame = MaterialAsset("Board Frame", new Color(0.3f, 0.19f, 0.11f), 0.35f);
        Material ghost = GhostMaterialAsset();
        Mesh squares = SquaresMesh();

        GameObject root = new GameObject("ChessBoardView (" + board.gameObject.name + ")");
        Undo.RegisterCreatedObjectUndo(root, "Create Board View");
        GameObject old = ExistingFor<ChessBoardView>(board, existing => existing.board);
        root.transform.position = board.transform.position + new Vector3(0, TableHeight, 0);

        float boardWidth = 8 * SquareSize + 2 * Border;
        // Deep enough for the status and button panels beyond each end of the board.
        GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "Table";
        table.transform.SetParent(root.transform, false);
        table.transform.localPosition = new Vector3(0, -(TableHeight + BoardThickness) / 2, 0);
        table.transform.localScale = new Vector3(boardWidth * 1.6f, TableHeight - BoardThickness,
            boardWidth * TableDepthFactor);
        table.GetComponent<MeshRenderer>().sharedMaterial = frame;
        // Players walk through the table and the board; the blocker stops those without a seat.
        Object.DestroyImmediate(table.GetComponent<BoxCollider>());

        GameObject blocker = new GameObject("Player Blocker");
        blocker.transform.SetParent(root.transform, false);
        BoxCollider blockerBox = blocker.AddComponent<BoxCollider>();
        blockerBox.center = table.transform.localPosition;
        blockerBox.size = table.transform.localScale;

        GameObject boardBase = GameObject.CreatePrimitive(PrimitiveType.Cube);
        boardBase.name = "Board";
        boardBase.transform.SetParent(root.transform, false);
        boardBase.transform.localPosition = new Vector3(0, -BoardThickness / 2, 0);
        boardBase.transform.localScale = new Vector3(boardWidth, BoardThickness, boardWidth);
        boardBase.GetComponent<MeshRenderer>().sharedMaterial = frame;
        Object.DestroyImmediate(boardBase.GetComponent<BoxCollider>());

        GameObject squaresObject = new GameObject("Squares");
        squaresObject.transform.SetParent(root.transform, false);
        squaresObject.AddComponent<MeshFilter>().sharedMesh = squares;
        squaresObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { lightSquares, darkSquares };

        ChessBoardView view = UdonSharpUndo.AddComponent<ChessBoardView>(root);
        view.board = board;
        view.squareSize = SquareSize;
        string setNote = EnsureDefaultPieceSet(board, meshes, whitePieces, blackPieces, ghost);
        view.blocker = blockerBox;
        view.pieces = new Transform[ChessBoardView.PieceCount];
        view.pieceFilters = new MeshFilter[ChessBoardView.PieceCount];
        view.pieceRenderers = new MeshRenderer[ChessBoardView.PieceCount];
        view.piecePickups = new VRCPickup[ChessBoardView.PieceCount];
        view.pieceColliders = new BoxCollider[ChessBoardView.PieceCount];
        view.pieceBehaviours = new ChessPiece[ChessBoardView.PieceCount];
        GameObject piecesParent = new GameObject("Pieces");
        piecesParent.transform.SetParent(root.transform, false);
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            bool white = obj < ChessBoardView.PiecesPerColor;
            int index = obj % ChessBoardView.PiecesPerColor;
            // Placed as at the start, in the order the view maps them on Start (a1 to h8): White back rank then pawns,
            // Black pawns then back rank.
            int file = index % 8;
            bool backRank = white == (index < 8);
            int type = backRank ? BackRank[file] : ChessRules.Pawn;
            int rank = white ? (backRank ? 0 : 1) : (backRank ? 7 : 6);
            GameObject piece = new GameObject((white ? "White " : "Black ") + (index + 1));
            piece.transform.SetParent(piecesParent.transform, false);
            piece.transform.localPosition = new Vector3((file - 3.5f) * SquareSize, 0, (rank - 3.5f) * SquareSize);
            piece.transform.localRotation = white ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
            MeshFilter filter = piece.AddComponent<MeshFilter>();
            filter.sharedMesh = meshes[type];
            MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = white ? whitePieces : blackPieces;
            view.pieceRenderers[obj] = renderer;
            view.pieces[obj] = piece.transform;
            view.pieceFilters[obj] = filter;
            AddPickup(piece, view, obj, meshes[type]);
        }
        AddSquareTargets(root, view);
        AddHighlights(root, view);
        AddPromotionGhosts(root, view, meshes, ghost);
        AddCapturedPlaces(root, view);
        AddViewSounds(root, view);
        UdonSharpEditorUtility.CopyProxyToUdon(view);

        // The UI (step 12.1), a behaviour of its own on an object beside the view, standing where the view does (the
        // board's centre), so the view's local space is its too: its parts are placed around the table as before, and
        // neither behaviour knows the other.
        GameObject uiObject = new GameObject("ChessBoardUI (" + board.gameObject.name + ")");
        Undo.RegisterCreatedObjectUndo(uiObject, "Create Board View");
        uiObject.transform.position = root.transform.position;
        ChessBoardUI ui = UdonSharpUndo.AddComponent<ChessBoardUI>(uiObject);
        ui.board = board;
        ui.historyWall = HistoryWallFor(board);
        AddButtons(uiObject, ui, boardWidth * TableDepthFactor);
        AddStatusPanels(uiObject, ui, boardWidth * TableDepthFactor);
        AddPgnBox(uiObject, ui);
        AddUISounds(uiObject, ui);
        UdonSharpEditorUtility.CopyProxyToUdon(ui);

        // Read before old is destroyed, after which Unity compares it equal to null.
        bool replacing = old != null;
        int repointed = 0;
        string kept = "";
        ChessBoardUI oldUi = ExistingUI(board, ui);
        // An earlier UI inside the old view (as the first 12.1 builds made it) goes with the view.
        bool oldUiBeside = oldUi != null && (old == null || !oldUi.transform.IsChildOf(old.transform));
        if (oldUi != null) repointed += RepointReferences(oldUi, ui);
        if (old != null) {
            repointed += RepointReferences(old.GetComponent<ChessBoardView>(), view);
            root.transform.SetParent(old.transform.parent, false);
            root.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
            root.transform.position = old.transform.position;
            root.transform.rotation = old.transform.rotation;
            root.transform.localScale = old.transform.localScale;
            kept = KeepCustomModel(old.transform, root.transform);
            kept += KeepCapturedPlaces(old.GetComponent<ChessBoardView>(), view, root.transform);
            Undo.DestroyObjectImmediate(old);
        }
        // An earlier UI beside the view is replaced where it stands; otherwise the UI stands beside the view, just
        // after it, where the view stands.
        Transform place = oldUiBeside ? oldUi.transform : root.transform;
        uiObject.transform.SetParent(place.parent, false);
        uiObject.transform.SetSiblingIndex(place.GetSiblingIndex() + (oldUiBeside ? 0 : 1));
        uiObject.transform.position = place.position;
        uiObject.transform.rotation = place.rotation;
        uiObject.transform.localScale = place.localScale;
        if (oldUiBeside) Undo.DestroyObjectImmediate(oldUi.gameObject);
        // The view's and the UI's text comes from the shared Chess Language (PLAN.md 13.5), made when the scene has
        // none; the I18n fields are left empty, for the I18n sanity check to fill on build and before Play Mode.
        EnsureChessLanguage(root.scene);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[ChessBoardViewBuilder] " + (replacing ? "Replaced " : "Created ") + root.name + " for "
            + board.gameObject.name + (replacing ? " (" + repointed + " behaviour(s) pointed at the new view" + kept
                + ")" : "")
            + " and " + uiObject.name + (ui.historyWall != null ? ", with the history wall" : "") + setNote
            + ". Move them where you like (the UI's origin at the board's centre) and save the scene.");
    }

    // The parts of the table and board model a world creator may delete (step 12.2, docs/customizing.md), and every
    // child the builder makes (the promotion picker of steps 9.5a to 12.10 too, so a rebuild doesn't keep it).
    static readonly string[] ModelParts = { "Table", "Board", "Squares" };
    static readonly string[] BuiltChildren = {
        "Table", "Player Blocker", "Board", "Squares", "Pieces", "Square Targets", "Highlights", "Promotion Picker",
        PromotionGhostsName, CapturedPlacesName, SoundsName,
    };

    // The captured-piece places (step 12.4), under "Captured Places": 16 per colour (enough for every piece a side
    // can lose, a king taken by a free move included), in two columns of 8 beside the board on the table's surface,
    // below the board's, on the side of the player who captures them: White's pieces to Black's right (-x) from
    // Black's end, Black's to White's right (+x) from White's end. Empty objects; a world creator moves them, or puts
    // their own in the view's fields.
    const string CapturedPlacesName = "Captured Places";
    const string PromotionGhostsName = "Promotion Ghosts";

    // Sound effects (step 12.12): a spatial AudioSource on "Sounds" at the parent's origin (the board's centre), just
    // above the surface, heard around the table and fading out over a room. The clips are the user's in
    // Assets/Siggi/ChessBoard/Sounds/.
    const string SoundsName = "Sounds";
    const string SoundsFolder = "Assets/Siggi/ChessBoard/Sounds";

    static AudioSource AddSoundSource(GameObject parent) {
        GameObject sounds = new GameObject(SoundsName);
        sounds.transform.SetParent(parent.transform, false);
        sounds.transform.localPosition = new Vector3(0, 0.1f, 0);
        AudioSource source = sounds.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1;
        source.dopplerLevel = 0;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 1;
        source.maxDistance = 15;
        return source;
    }

    // The clip Sounds/<name>.wav, with a warning when it's missing (null).
    static AudioClip SoundClip(string name) {
        string path = SoundsFolder + "/" + name + ".wav";
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null) Debug.LogWarning("[ChessBoardViewBuilder] No sound at " + path + "; left empty.");
        return clip;
    }

    // The view's move sounds.
    static void AddViewSounds(GameObject root, ChessBoardView view) {
        view.audioSource = AddSoundSource(root);
        view.moveSound = SoundClip("move");
        view.captureSound = SoundClip("capture");
        view.promoteSound = SoundClip("promote");
        view.checkSound = SoundClip("move-check");
    }

    // The UI's sounds: every other one, and the view's check sound for its length.
    static void AddUISounds(GameObject root, ChessBoardUI ui) {
        ui.audioSource = AddSoundSource(root);
        ui.clickSound = SoundClip("ui-click");
        ui.gameStartSound = SoundClip("game-start");
        ui.notificationSound = SoundClip("notification-pop");
        ui.gameEndSound = SoundClip("game-end");
        ui.resetSound = SoundClip("table-reset");
        ui.timeWarningSound = SoundClip("time-warning");
        ui.timeOutSound = SoundClip("time-out");
        ui.checkSound = SoundClip("move-check");
    }

    static void AddCapturedPlaces(GameObject root, ChessBoardView view) {
        GameObject parent = new GameObject(CapturedPlacesName);
        parent.transform.SetParent(root.transform, false);
        view.whiteCapturedPlaces = new Transform[ChessBoardView.PiecesPerColor];
        view.blackCapturedPlaces = new Transform[ChessBoardView.PiecesPerColor];
        for (int color = ChessRules.White; color <= ChessRules.Black; color++) {
            bool white = color == ChessRules.White;
            Transform[] places = white ? view.whiteCapturedPlaces : view.blackCapturedPlaces;
            for (int place = 0; place < places.Length; place++) {
                float column = 4.75f + (place / 8) * 0.9f;
                float row = (place % 8) - 3.5f;
                GameObject target = new GameObject((white ? "White " : "Black ") + (place + 1));
                target.transform.SetParent(parent.transform, false);
                // Beside the capturer: Black takes White's pieces, so they line up from Black's end.
                target.transform.localPosition = white
                    ? new Vector3(-column * SquareSize, -BoardThickness, -row * SquareSize)
                    : new Vector3(column * SquareSize, -BoardThickness, row * SquareSize);
                places[place] = target.transform;
            }
        }
    }

    // Keeps a world creator's captured places when a view is rebuilt (step 12.4): when the old view has any, the new
    // view uses the same objects (its own Captured Places replaced by the old view's, which moves over keeping its
    // place). Returns what was kept, for the log ("" for nothing).
    static string KeepCapturedPlaces(ChessBoardView oldView, ChessBoardView view, Transform root) {
        if (oldView == null || !HasPlace(oldView.whiteCapturedPlaces) && !HasPlace(oldView.blackCapturedPlaces)) {
            return "";
        }
        Transform built = root.Find(CapturedPlacesName);
        Transform oldPlaces = oldView.transform.Find(CapturedPlacesName);
        if (oldPlaces != null) {
            if (built != null) Object.DestroyImmediate(built.gameObject);
            Undo.SetTransformParent(oldPlaces, root, "Create Board View");
        }
        view.whiteCapturedPlaces = oldView.whiteCapturedPlaces;
        view.blackCapturedPlaces = oldView.blackCapturedPlaces;
        // Built places nothing points at any more (the old view's are elsewhere) go.
        if (oldPlaces == null && built != null) Object.DestroyImmediate(built.gameObject);
        UdonSharpEditorUtility.CopyProxyToUdon(view);
        return ", captured places kept";
    }

    static bool HasPlace(Transform[] places) {
        if (places == null) return false;
        for (int i = 0; i < places.Length; i++) {
            if (places[i] != null) return true;
        }
        return false;
    }

    // Keeps a world creator's changes to the model when a view is rebuilt (step 12.2): a model part deleted from the
    // old view is left out of the new one, and the old view's children the builder didn't make (their own model) move
    // to the new view, keeping their local place. An earlier UI inside the old view goes with it. Returns what was
    // kept, for the log ("" for nothing).
    static string KeepCustomModel(Transform old, Transform root) {
        string kept = "";
        for (int i = 0; i < ModelParts.Length; i++) {
            if (old.Find(ModelParts[i]) != null) continue;
            Transform part = root.Find(ModelParts[i]);
            if (part != null) Object.DestroyImmediate(part.gameObject);
            kept += ", " + ModelParts[i] + " left out";
        }
        List<Transform> custom = new List<Transform>();
        foreach (Transform child in old) {
            if (System.Array.IndexOf(BuiltChildren, child.name) < 0
                && child.GetComponentInChildren<ChessBoardUI>(true) == null) {
                custom.Add(child);
            }
        }
        for (int i = 0; i < custom.Count; i++) {
            Undo.SetTransformParent(custom[i], root, "Create Board View");
            kept += ", " + custom[i].name + " kept";
        }
        return kept;
    }

    // Points every other behaviour's field holding the old view (ViewTest's, a debug view's, ArchiveTest's; or the old
    // UI) at the new one, so replacing a view doesn't leave them on a destroyed object. Returns how many behaviours
    // changed.
    // GameHistoryWallBuilder uses it for the wall too.
    public static int RepointReferences(UdonSharpBehaviour oldView, UdonSharpBehaviour newView) {
        if (oldView == null) return 0;
        int changed = 0;
        UdonSharpBehaviour[] behaviours = Object.FindObjectsOfType<UdonSharpBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++) {
            UdonSharpBehaviour behaviour = behaviours[i];
            // The old view's own pieces and squares go with it.
            if (behaviour == newView || behaviour.transform.IsChildOf(oldView.transform)) continue;
            SerializedObject serialized = new SerializedObject(behaviour);
            SerializedProperty property = serialized.GetIterator();
            bool found = false;
            while (property.Next(true)) {
                if (property.propertyType == SerializedPropertyType.ObjectReference
                    && property.objectReferenceValue == oldView) {
                    property.objectReferenceValue = newView;
                    found = true;
                }
            }
            if (!found) continue;
            serialized.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
            changed++;
        }
        return changed;
    }

    // Makes a piece object something to carry (PLAN.md 9.2, docs/physical-pieces.md): a kinematic rigidbody (the
    // board, not physics, puts pieces down), a box collider fitted to its mesh (the view refits it when the mesh
    // changes), a VRCPickup that the view makes pickupable only for players who may touch the pieces, and a ChessPiece
    // asking the board about ownership. No VRCObjectSync: it fought each client placing the pieces itself (step 9.6),
    // so others don't see a piece carried, only the move it makes. On the Pickup layer, so carried pieces don't push
    // players around.
    static void AddPickup(GameObject piece, ChessBoardView view, int obj, Mesh mesh) {
        int pickupLayer = LayerMask.NameToLayer("Pickup");
        if (pickupLayer >= 0) piece.layer = pickupLayer;
        Rigidbody body = piece.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        BoxCollider box = piece.AddComponent<BoxCollider>();
        box.center = mesh.bounds.center;
        box.size = mesh.bounds.size;
        VRCPickup pickup = piece.AddComponent<VRCPickup>();
        pickup.pickupable = false;
        pickup.DisallowTheft = true;
        // The view gives it its hover text from the language pack (PLAN.md 13.5).
        pickup.InteractionText = "";
        ChessPiece behaviour = UdonSharpUndo.AddComponent<ChessPiece>(piece);
        behaviour.view = view;
        behaviour.index = obj;
        UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
        view.piecePickups[obj] = pickup;
        view.pieceColliders[obj] = box;
        view.pieceBehaviours[obj] = behaviour;
    }

    // ---- Piece sets (steps 12.3, 12.10 and 12.11, docs/customizing.md) ----

    // How far apart the builder lays out a set's slots, in squares.
    const float SlotSpacing = 1.5f;

    // The board's arbiter's default piece sets (step 12.10), one per side: each kept when the arbiter has it;
    // otherwise "White Pieces" or "Black Pieces", made from the generated meshes and that side's material as a child of
    // the arbiter, and put in its Default White / Black Piece Set. A kept set without promotion slots (from before step
    // 12.11) gets them (AddPromotionSlots), the ghost material on each. The combined "Chess Pieces" of step 12.3 (both
    // colours in one set) is removed when found. Returns a note for the log ("" when nothing changed).
    static string EnsureDefaultPieceSet(ChessBoard board, Mesh[] meshes, Material white, Material black,
            Material ghost) {
        ChessArbiter arbiter = board.arbiter;
        if (arbiter == null) {
            Debug.LogWarning("[ChessBoardViewBuilder] " + board.gameObject.name + " has no arbiter, so its view has "
                + "no default piece sets: set the board's Arbiter, then run Create Board View again.");
            return ", no piece sets (the board has no arbiter)";
        }
        string note = "";
        Transform combined = arbiter.transform.Find("Chess Pieces");
        if (combined != null && combined.GetComponent<ChessPieceSet>() != null) {
            Undo.DestroyObjectImmediate(combined.gameObject);
            note += ", removing the combined Chess Pieces";
        }
        if (arbiter.defaultWhitePieceSet == null || arbiter.defaultBlackPieceSet == null) {
            Undo.RecordObject(arbiter, "Create Board View");
            if (arbiter.defaultWhitePieceSet == null) {
                arbiter.defaultWhitePieceSet = BuildPieceSet("White Pieces", arbiter.transform, meshes,
                    new[] { white }, Vector3.zero, Quaternion.identity, Vector3.one, ghost);
                note += ", the default White Pieces";
            }
            if (arbiter.defaultBlackPieceSet == null) {
                ChessPieceSet set = BuildPieceSet("Black Pieces", arbiter.transform, meshes, new[] { black },
                    Vector3.zero, Quaternion.identity, Vector3.one, ghost);
                // Behind White's two rows (its pieces and its promotion pieces).
                set.transform.localPosition += new Vector3(0, 0, 2 * SlotSpacing * SquareSize);
                arbiter.defaultBlackPieceSet = set;
                note += ", the default Black Pieces";
            }
            UdonSharpEditorUtility.CopyProxyToUdon(arbiter);
            EditorUtility.SetDirty(arbiter);
        }
        ChessPieceSet whiteSet = arbiter.defaultWhitePieceSet;
        ChessPieceSet blackSet = arbiter.defaultBlackPieceSet;
        if (AddPromotionSlots(whiteSet, meshes, ghost) > 0) note += ", promotion slots in " + whiteSet.gameObject.name;
        if (blackSet != whiteSet && AddPromotionSlots(blackSet, meshes, ghost) > 0) {
            note += ", promotion slots in " + blackSet.gameObject.name;
            // The built Black Pieces stood a row behind White Pieces, where White's promotion row is now: moved back.
            Vector3 oldPlace = whiteSet.transform.localPosition + new Vector3(0, 0, SlotSpacing * SquareSize);
            if (whiteSet.transform.parent == arbiter.transform && blackSet.transform.parent == arbiter.transform
                && (blackSet.transform.localPosition - oldPlace).sqrMagnitude < 1e-8f) {
                Undo.RecordObject(blackSet.transform, "Create Board View");
                blackSet.transform.localPosition = oldPlace + new Vector3(0, 0, SlotSpacing * SquareSize);
            }
        }
        return note.Length > 0 ? note + " on " + arbiter.gameObject.name : "";
    }

    // A ChessPieceSet named name under parent, one side's pieces (step 12.10): for each piece type, a slot (laid out in
    // a row, SlotSpacing squares apart) holding a one-square guide on the playing surface and the template, its mesh
    // meshes[type] with materials, standing at offset, turned by rotation and scaled by scale; and the promotion slots
    // behind them (AddPromotionSlots), with promotionMaterial. Set up as White's pieces (knights facing +z); a view
    // turns Black's half round. Public for the view test's set.
    public static ChessPieceSet BuildPieceSet(string name, Transform parent, Mesh[] meshes, Material[] materials,
            Vector3 offset, Quaternion rotation, Vector3 scale, Material promotionMaterial) {
        GameObject root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, "Create Piece Set");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(0, 0, -2 * SquareSize * SlotSpacing);
        ChessPieceSet set = UdonSharpUndo.AddComponent<ChessPieceSet>(root);
        Transform[] templates = new Transform[7];
        for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) {
            Transform model = AddSlot(root.transform, TypeNames[type],
                new Vector3((type - 1) * SlotSpacing * SquareSize, 0, 0), meshes[type], materials);
            model.localPosition = offset;
            model.localRotation = rotation;
            model.localScale = scale;
            templates[type] = model;
        }
        set.pawn = templates[ChessRules.Pawn];
        set.knight = templates[ChessRules.Knight];
        set.bishop = templates[ChessRules.Bishop];
        set.rook = templates[ChessRules.Rook];
        set.queen = templates[ChessRules.Queen];
        set.king = templates[ChessRules.King];
        UdonSharpEditorUtility.CopyProxyToUdon(set);
        AddPromotionSlots(set, meshes, promotionMaterial);
        return set;
    }

    // Adds to set the promotion slots (step 12.11) it lacks, in a second row behind its pieces (+z, SlotSpacing
    // squares), each behind its piece's slot: a guide and a template with the mesh and local transform of the set's
    // own template of that type (meshes[type], unturned, when it has none), and material on every slot of the mesh.
    // The set's other templates are kept. Returns how many it added.
    public static int AddPromotionSlots(ChessPieceSet set, Mesh[] meshes, Material material) {
        if (set == null) return 0;
        Transform[] templates = new Transform[7];
        int added = 0;
        for (int type = ChessRules.Knight; type <= ChessRules.Queen; type++) {
            templates[type] = set.PromotionTemplate(type);
            if (templates[type] != null) continue;
            Transform piece = set.Template(type);
            Vector3 place = new Vector3((type - 1) * SlotSpacing * SquareSize, 0, 0);
            if (piece != null && piece.parent != null && piece.parent.parent == set.transform) {
                place = piece.parent.localPosition;
            }
            place.z += SlotSpacing * SquareSize;
            MeshFilter filter = piece != null ? piece.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null && filter.sharedMesh != null ? filter.sharedMesh : meshes[type];
            Material[] materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            Transform model = AddSlot(set.transform, "Promotion " + TypeNames[type], place, mesh, materials);
            if (piece != null) {
                model.localPosition = piece.localPosition;
                model.localRotation = piece.localRotation;
                model.localScale = piece.localScale;
            }
            templates[type] = model;
            added++;
        }
        if (added == 0) return 0;
        Undo.RecordObject(set, "Add Promotion Slots");
        set.promotionKnight = templates[ChessRules.Knight];
        set.promotionBishop = templates[ChessRules.Bishop];
        set.promotionRook = templates[ChessRules.Rook];
        set.promotionQueen = templates[ChessRules.Queen];
        UdonSharpEditorUtility.CopyProxyToUdon(set);
        EditorUtility.SetDirty(set);
        return added;
    }

    // A slot named name under setRoot at place: a one-square guide on the playing surface and a "Model" with mesh and
    // materials, unmoved. Returns the model.
    static Transform AddSlot(Transform setRoot, string name, Vector3 place, Mesh mesh, Material[] materials) {
        Mesh guideMesh = FlatMesh("Piece Set Guide", 0.0005f, SquareSize * 0.06f);
        Material guideMaterial = MaterialAsset("Piece Set Guide", new Color(0.3f, 0.75f, 0.4f), 0.2f);
        GameObject slot = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(slot, "Create Piece Set");
        slot.transform.SetParent(setRoot, false);
        slot.transform.localPosition = place;
        GameObject guide = new GameObject("Guide");
        guide.transform.SetParent(slot.transform, false);
        guide.AddComponent<MeshFilter>().sharedMesh = guideMesh;
        MeshRenderer guideRenderer = guide.AddComponent<MeshRenderer>();
        guideRenderer.sharedMaterial = guideMaterial;
        guideRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        GameObject model = new GameObject("Model");
        model.transform.SetParent(slot.transform, false);
        model.AddComponent<MeshFilter>().sharedMesh = mesh;
        model.AddComponent<MeshRenderer>().sharedMaterials = materials;
        return model.transform;
    }

    // The generated piece meshes by ChessRules type (entry 0 empty), made when missing. Public for the view test.
    public static Mesh[] PieceMeshes() {
        Mesh[] meshes = new Mesh[7];
        for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) meshes[type] = PieceMesh(type);
        return meshes;
    }

    // Desktop mode's square targets (step 9.3c): one object per square, a1 to h8, under "Square Targets", each a thin
    // box collider over its square with a ChessSquare, all inactive; the view activates the ones a click on would do
    // something. Interact doesn't say where a collider was hit, so each square needs its own.
    static void AddSquareTargets(GameObject root, ChessBoardView view) {
        GameObject parent = new GameObject("Square Targets");
        parent.transform.SetParent(root.transform, false);
        view.squareTargets = new ChessSquare[64];
        const string files = "abcdefgh";
        for (int square = 0; square < 64; square++) {
            int file = square % 8;
            int rank = square / 8;
            GameObject target = new GameObject(files[file] + (rank + 1).ToString());
            target.transform.SetParent(parent.transform, false);
            target.transform.localPosition = new Vector3((file - 3.5f) * SquareSize, 0, (rank - 3.5f) * SquareSize);
            BoxCollider box = target.AddComponent<BoxCollider>();
            box.center = new Vector3(0, 0.001f, 0);
            box.size = new Vector3(SquareSize, 0.002f, SquareSize);
            box.isTrigger = true;
            ChessSquare behaviour = UdonSharpUndo.AddComponent<ChessSquare>(target);
            behaviour.view = view;
            behaviour.square = square;
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
            view.squareTargets[square] = behaviour;
            target.SetActive(false);
        }
    }

    // The highlights (step 9.5a), under "Highlights", all hidden and without colliders, so clicks reach the square
    // targets and pieces: the last move's line (a blue bar one unit long along +z, just above the squares; the view
    // stands it on the move's start, turned and stretched to its end) and its mark (a small blue diamond above the
    // line, which the view moves onto the start; step 12.6), the check lines (red bars like the last move's, just above
    // it and under its mark; step 12.7), and one legal-target diamond per square, a1 to h8, over its square and above
    // them all, with the view's yellow and red materials for the marks (step 12.5).
    static void AddHighlights(GameObject root, ChessBoardView view) {
        Material lastMove = MaterialAsset("Last Move Mark", new Color(0.2f, 0.5f, 0.95f), 0.3f);
        Material target = MaterialAsset("Target Mark", new Color(0.95f, 0.8f, 0.1f), 0.3f);
        Material capture = MaterialAsset("Capture Mark", new Color(0.85f, 0.12f, 0.1f), 0.3f);
        Material check = MaterialAsset("Check Line", new Color(0.9f, 0.08f, 0.08f), 0.3f);
        view.targetMaterial = target;
        view.captureMaterial = capture;
        Mesh line = LineMesh("Last Move Line", 0.0008f, SquareSize * LastMoveLineWidth);
        Mesh checkLine = LineMesh("Check Line", 0.00085f, SquareSize * LastMoveLineWidth);
        Mesh fromDiamond = DiamondMesh("Last Move Diamond", 0.00095f);
        Mesh diamond = DiamondMesh("Legal Target Diamond", 0.0011f);
        GameObject parent = new GameObject("Highlights");
        parent.transform.SetParent(root.transform, false);
        view.lastMoveLine = FlatObject("Last Move Line", parent, line, lastMove, Vector3.zero);
        view.lastMoveMark = FlatObject("Last Move From", parent, fromDiamond, lastMove, Vector3.zero);
        view.lastMoveMark.transform.localScale =
            new Vector3(ChessBoardView.SmallMarkScale, 1, ChessBoardView.SmallMarkScale);
        GameObject checks = new GameObject("Check Lines");
        checks.transform.SetParent(parent.transform, false);
        view.checkLines = new GameObject[ChessBoardView.CheckLineCount];
        for (int i = 0; i < view.checkLines.Length; i++) {
            view.checkLines[i] = FlatObject("Check Line " + (i + 1), checks, checkLine, check, Vector3.zero);
        }
        GameObject targets = new GameObject("Legal Targets");
        targets.transform.SetParent(parent.transform, false);
        view.targetHighlights = new GameObject[64];
        const string files = "abcdefgh";
        for (int square = 0; square < 64; square++) {
            int file = square % 8;
            int rank = square / 8;
            view.targetHighlights[square] = FlatObject(files[file] + (rank + 1).ToString(), targets, diamond, target,
                new Vector3((file - 3.5f) * SquareSize, 0, (rank - 3.5f) * SquareSize));
        }
    }

    // A hidden object showing a flat mesh at a place in the view.
    static GameObject FlatObject(string name, GameObject parent, Mesh mesh, Material material, Vector3 position) {
        GameObject flat = new GameObject(name);
        flat.transform.SetParent(parent.transform, false);
        flat.transform.localPosition = position;
        flat.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = flat.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        flat.SetActive(false);
        return flat;
    }

    // The promotion ghosts (step 12.11), under "Promotion Ghosts": a knight, bishop, rook and queen (ChessBoardView
    // GhostType order), each with the generated mesh and the Promotion Ghost material, a trigger box collider fitted to
    // it (so players walk through it and a click still reaches it) and a ChessPromotionGhost promoting to its piece.
    // Built hidden; the view dresses them in the promoting side's promotion templates and floats them over the pawn.
    static void AddPromotionGhosts(GameObject root, ChessBoardView view, Mesh[] meshes, Material ghostMaterial) {
        GameObject parent = new GameObject(PromotionGhostsName);
        parent.transform.SetParent(root.transform, false);
        view.promotionGhosts = new ChessPromotionGhost[ChessBoardView.GhostCount];
        for (int ghost = 0; ghost < ChessBoardView.GhostCount; ghost++) {
            int type = ChessBoardView.GhostType(ghost);
            GameObject ghostObject = new GameObject(TypeNames[type]);
            ghostObject.transform.SetParent(parent.transform, false);
            ghostObject.transform.localPosition = new Vector3((ghost - 1.5f) * SquareSize,
                ChessBoardView.GhostHeight * SquareSize, 0);
            ghostObject.AddComponent<MeshFilter>().sharedMesh = meshes[type];
            MeshRenderer renderer = ghostObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ghostMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            BoxCollider box = ghostObject.AddComponent<BoxCollider>();
            box.center = meshes[type].bounds.center;
            box.size = meshes[type].bounds.size;
            box.isTrigger = true;
            ChessPromotionGhost behaviour = UdonSharpUndo.AddComponent<ChessPromotionGhost>(ghostObject);
            behaviour.view = view;
            behaviour.pieceType = type;
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
            view.promotionGhosts[ghost] = behaviour;
            ghostObject.SetActive(false);
        }
    }

    // The PGN box's size in canvas pixels (a millimetre each; as wide as the board), the height of its hint line, and
    // how far its bottom edge stands above the board's surface, clear of the pieces.
    static readonly Vector2 PgnBoxSize = new Vector2(440, 560);
    const float PgnHintHeight = 34;
    const float PgnBoxLift = 0.1f;
    // The paste part's bottom bar (step 11.2b): its height and its buttons' width.
    const float PgnBarHeight = 40;
    const float PgnBarButtonWidth = 80;

    // The PGN box (steps 11.2a, 11.2b): a world-space canvas (with VRCUiShape, so the pointer reaches the text)
    // standing upright over the far edge of the board from White's end, facing it unturned, holding two parts the UI
    // shows one at a time. Copying: a hint line and a read-only multi-line text field the UI fills with the game's
    // PGN, 80 characters a line (ChessPgn.LineWidth). Pasting: a hint line, an editable field to paste into, and a bar
    // with the import's status line and the Import and Close buttons. Built hidden; the UI shows it for the local
    // player and turns it to face the end they opened it from.
    static void AddPgnBox(GameObject root, ChessBoardUI ui) {
        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(ui);
        TMP_FontAsset font = DefaultFont();
        GameObject canvasObject = new GameObject("PGN Box", typeof(RectTransform));
        canvasObject.layer = LayerMask.NameToLayer("Default");
        canvasObject.transform.SetParent(root.transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = PgnBoxSize;
        rect.localScale = Vector3.one * 0.001f;
        rect.localPosition = new Vector3(0, PgnBoxLift + PgnBoxSize.y * 0.001f / 2, 4 * SquareSize + Border);
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();
        canvasObject.AddComponent<VRCUiShape>();
        AddTriggerCollider(canvasObject);
        canvasObject.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 0.95f);

        GameObject copy = AddStretched(canvasObject, "Copy", Vector2.zero, Vector2.zero);
        ui.pgnCopyHint = AddPgnHint(copy, font);
        TMP_InputField field = AddPgnField(copy, font, 8, true);

        GameObject paste = AddStretched(canvasObject, "Paste", Vector2.zero, Vector2.zero);
        ui.pgnPasteHint = AddPgnHint(paste, font);
        TMP_InputField pasteField = AddPgnField(paste, font, 8 + PgnBarHeight, false);
        GameObject bar = AddAnchored(paste, "Bar", Vector2.zero, new Vector2(1, 0), new Vector2(8, 4),
            new Vector2(-8, PgnBarHeight));
        GameObject statusObject = AddStretched(bar, "Status", Vector2.zero,
            new Vector2(-2 * (PgnBarButtonWidth + 6), 0));
        TextMeshProUGUI status = AddText(statusObject, font, 12, TextAlignmentOptions.Left, PaleText);
        status.richText = false;
        status.text = "";
        GameObject importButton = AddBarButton(bar, font, udon, "Import", "PressImport", 1, out ui.pgnImportText);
        AddBarButton(bar, font, udon, "Close", "PressPgnClose", 0, out ui.pgnCloseText);
        paste.SetActive(false);

        canvasObject.SetActive(false);
        ui.pgnBox = canvasObject;
        ui.pgnField = field;
        ui.pgnCopyPart = copy;
        ui.pgnPastePart = paste;
        ui.pgnPasteField = pasteField;
        ui.pgnImportLabel = status;
        ui.pgnImportButton = importButton;
    }

    // The PGN box's hint line, along the top of part, empty: the UI writes it from the language pack.
    static TextMeshProUGUI AddPgnHint(GameObject part, TMP_FontAsset font) {
        GameObject hintObject = AddAnchored(part, "Hint", new Vector2(0, 1), Vector2.one,
            new Vector2(10, -PgnHintHeight), new Vector2(-10, 0));
        TextMeshProUGUI hint = AddText(hintObject, font, 12, TextAlignmentOptions.Left, PaleText);
        hint.text = "";
        return hint;
    }

    // The PGN box's multi-line text field, filling part under the hint line down to bottom: read-only for copying,
    // editable for pasting. The text sits in a masked text area (the field's viewport), which it scrolls in.
    static TMP_InputField AddPgnField(GameObject part, TMP_FontAsset font, float bottom, bool readOnly) {
        GameObject fieldObject = AddStretched(part, "Field", new Vector2(8, bottom), new Vector2(-8, -PgnHintHeight));
        Image fieldImage = fieldObject.AddComponent<Image>();
        fieldImage.color = new Color(0.95f, 0.95f, 0.93f);
        GameObject areaObject = AddStretched(fieldObject, "Text Area", new Vector2(6, 4), new Vector2(-6, -4));
        areaObject.AddComponent<RectMask2D>();
        GameObject textObject = AddStretched(areaObject, "Text", Vector2.zero, Vector2.zero);
        TextMeshProUGUI text = AddText(textObject, font, 10, TextAlignmentOptions.TopLeft, Color.black);
        // The field scrolls its text in the text area, so it isn't cut off at its rect, and it takes the pointer.
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = true;
        // Names may hold anything; the field draws its text raw.
        text.richText = false;
        TMP_InputField field = fieldObject.AddComponent<TMP_InputField>();
        field.textViewport = areaObject.GetComponent<RectTransform>();
        field.textComponent = text;
        field.fontAsset = font;
        field.pointSize = 10;
        field.richText = false;
        field.targetGraphic = fieldImage;
        field.lineType = TMP_InputField.LineType.MultiLineNewline;
        field.readOnly = readOnly;
        field.characterLimit = 0;
        field.navigation = new Navigation { mode = Navigation.Mode.None };
        field.text = "";
        return field;
    }

    // A button named name at the right end of the PGN box's bar, slot places from the right (0 the rightmost), calling
    // the UI's eventName, with its label (empty: the UI writes it from the language pack).
    static GameObject AddBarButton(GameObject bar, TMP_FontAsset font, UdonBehaviour udon, string name, string eventName,
        int slot, out TextMeshProUGUI label) {
        float right = -slot * (PgnBarButtonWidth + 6);
        GameObject buttonObject = AddAnchored(bar, name, new Vector2(1, 0), Vector2.one,
            new Vector2(right - PgnBarButtonWidth, 4), new Vector2(right, -4));
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.85f, 0.85f, 0.88f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        UnityEventTools.AddStringPersistentListener(button.onClick,
            new UnityAction<string>(udon.SendCustomEvent), eventName);
        GameObject labelObject = AddStretched(buttonObject, "Label", Vector2.zero, Vector2.zero);
        label = AddText(labelObject, font, 14, TextAlignmentOptions.Center, Color.black);
        label.text = "";
        return buttonObject;
    }

    // The UI's Press... event for each ChessBoardUI.Button* action, by action.
    static readonly string[] ButtonEvents = {
        "PressJoinWhite", "PressJoinBlack", "PressMode", "PressClock", "PressStart", "PressContinue", "PressReset",
        "PressResignWhite", "PressResignBlack", "PressOfferDrawWhite", "PressOfferDrawBlack", "PressAcceptDrawWhite",
        "PressAcceptDrawBlack", "PressDeclineDrawWhite", "PressDeclineDrawBlack", "PressClaimDraw", "PressEnterReplay",
        "PressLeaveReplay", "PressStepFirst", "PressStepBack", "PressStepForward", "PressStepLast",
        "PressRequestTakebackWhite", "PressRequestTakebackBlack", "PressAcceptTakebackWhite",
        "PressAcceptTakebackBlack", "PressDeclineTakebackWhite", "PressDeclineTakebackBlack", "PressLoadHere",
        "PressPgn", "PressLoadPgn", "PressLeaveWhite", "PressLeaveBlack",
    };
    // The order of the buttons on a panel (the grid packs the shown ones in this order): the lobby's, replay's and
    // the game's, Reset last. Each panel leaves out the other colour's. Load it here (step 10.2b) and Load PGN (step
    // 11.2b) show only on a reset board, with the Join buttons. PGN (step 11.2a) comes just before Reset. Leave (step
    // 13.1) follows Join, in the lobby and on a loaded board.
    static readonly int[] ButtonOrder = {
        ChessBoardUI.ButtonJoinWhite, ChessBoardUI.ButtonJoinBlack, ChessBoardUI.ButtonLeaveWhite,
        ChessBoardUI.ButtonLeaveBlack, ChessBoardUI.ButtonLoadHere,
        ChessBoardUI.ButtonLoadPgn,
        ChessBoardUI.ButtonMode, ChessBoardUI.ButtonClock, ChessBoardUI.ButtonStart, ChessBoardUI.ButtonContinue,
        ChessBoardUI.ButtonEnterReplay, ChessBoardUI.ButtonLeaveReplay, ChessBoardUI.ButtonStepFirst,
        ChessBoardUI.ButtonStepBack, ChessBoardUI.ButtonStepForward, ChessBoardUI.ButtonStepLast,
        ChessBoardUI.ButtonRequestTakebackWhite, ChessBoardUI.ButtonRequestTakebackBlack,
        ChessBoardUI.ButtonAcceptTakebackWhite, ChessBoardUI.ButtonAcceptTakebackBlack,
        ChessBoardUI.ButtonDeclineTakebackWhite, ChessBoardUI.ButtonDeclineTakebackBlack,
        ChessBoardUI.ButtonResignWhite, ChessBoardUI.ButtonResignBlack, ChessBoardUI.ButtonOfferDrawWhite,
        ChessBoardUI.ButtonOfferDrawBlack, ChessBoardUI.ButtonAcceptDrawWhite, ChessBoardUI.ButtonAcceptDrawBlack,
        ChessBoardUI.ButtonDeclineDrawWhite, ChessBoardUI.ButtonDeclineDrawBlack, ChessBoardUI.ButtonClaimDraw,
        ChessBoardUI.ButtonPgn, ChessBoardUI.ButtonReset,
    };
    // A button panel's size in canvas pixels (a millimetre each), its grid cells and columns and how far it leans
    // back from upright. Five columns by three rows fit the most buttons one player sees at once (13: in replay of
    // their game, with an offer and a takeback request pending, and PGN).
    const float PanelWidth = 544;
    const float PanelHeight = 160;
    const float PanelTilt = 70;
    const int PanelColumns = 5;
    static readonly Vector2 ButtonCell = new Vector2(100, 44);

    // The board's buttons (steps 9.4a, 9.4b): a world-space canvas at each end of the table, leaning back towards
    // that side's player, each with one button per action but the other colour's (ChessBoardUI.ButtonColor), so
    // each end joins, resigns, offers and answers for its own colour. Every button calls the UI's Press... event for
    // its action; the UI shows only those whose action applies, and the grid packs the shown ones together.
    static void AddButtons(GameObject root, ChessBoardUI ui, float tableDepth) {
        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(ui);
        TMP_FontAsset font = DefaultFont();
        int perPanel = 0;
        foreach (int action in ButtonOrder) {
            if (ChessBoardUI.ButtonColor(action) != ChessRules.Black) perPanel++;
        }
        ui.buttons = new GameObject[2 * perPanel];
        ui.buttonActions = new int[2 * perPanel];
        ui.buttonLabels = new TextMeshProUGUI[2 * perPanel];
        int next = 0;
        float z = ButtonPanelZ(tableDepth);
        for (int panel = 0; panel < 2; panel++) {
            bool white = panel == 0;
            GameObject canvasObject = new GameObject(white ? "Buttons (White)" : "Buttons (Black)", typeof(RectTransform));
            canvasObject.layer = LayerMask.NameToLayer("Default");
            canvasObject.transform.SetParent(root.transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            rect.localScale = Vector3.one * 0.001f;
            rect.localPosition = new Vector3(0, 0.016f, white ? -z : z);
            rect.localRotation = Quaternion.Euler(PanelTilt, white ? 0 : 180, 0);
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
            canvasObject.AddComponent<VRCUiShape>();
            AddTriggerCollider(canvasObject);
            canvasObject.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 0.9f);
            GridLayoutGroup grid = canvasObject.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(6, 6, 6, 6);
            grid.cellSize = ButtonCell;
            grid.spacing = new Vector2(8, 8);
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = PanelColumns;
            foreach (int action in ButtonOrder) {
                // Each end acts only for its own colour.
                if (ChessBoardUI.ButtonColor(action) == (white ? ChessRules.Black : ChessRules.White)) continue;
                int index = next++;
                GameObject buttonObject = new GameObject(ButtonEvents[action].Substring(5), typeof(RectTransform));
                buttonObject.layer = canvasObject.layer;
                buttonObject.transform.SetParent(canvasObject.transform, false);
                Image image = buttonObject.AddComponent<Image>();
                image.color = new Color(0.85f, 0.85f, 0.88f);
                Button button = buttonObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                UnityEventTools.AddStringPersistentListener(button.onClick,
                    new UnityAction<string>(udon.SendCustomEvent), ButtonEvents[action]);
                GameObject labelObject = AddStretched(buttonObject, "Label", new Vector2(3, 2), new Vector2(-3, -2));
                TextMeshProUGUI label = AddText(labelObject, font, 16, TextAlignmentOptions.Center, Color.black);
                // The UI writes every label from the language pack (PLAN.md 13.5).
                label.text = "";
                ui.buttons[index] = buttonObject;
                ui.buttonActions[index] = action;
                ui.buttonLabels[index] = label;
            }
        }
    }

    // How far from the board's centre (along z) each end's button panel stands: at the table's edge.
    static float ButtonPanelZ(float tableDepth) {
        return tableDepth / 2 - PanelHeight * 0.001f / 2 - 0.004f;
    }

    // A status panel's size in canvas pixels, and how much of its width the players' column takes.
    const float StatusHeight = 84;
    const float PlayersWidth = 0.4f;

    // The status display (step 9.5c): a world-space canvas at each end of the table, between the board and that end's
    // buttons, leaning back as they do, with the players' names and clocks on the left and the board's status on the
    // right. Nothing on it is clickable, so it has no collider and no VRCUiShape. The UI writes both texts.
    static void AddStatusPanels(GameObject root, ChessBoardUI ui, float tableDepth) {
        TMP_FontAsset font = DefaultFont();
        ui.statusPlayerLabels = new TextMeshProUGUI[2];
        ui.statusLabels = new TextMeshProUGUI[2];
        // Nearer the board than the buttons, the gap measured along the leaning panels; its near edge stays clear of
        // the board.
        float z = ButtonPanelZ(tableDepth) - (PanelHeight + StatusHeight) * 0.001f / 2 - 0.008f;
        for (int panel = 0; panel < 2; panel++) {
            bool white = panel == 0;
            GameObject canvasObject = new GameObject(white ? "Status (White)" : "Status (Black)", typeof(RectTransform));
            canvasObject.layer = LayerMask.NameToLayer("Default");
            canvasObject.transform.SetParent(root.transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(PanelWidth, StatusHeight);
            rect.localScale = Vector3.one * 0.001f;
            rect.localPosition = new Vector3(0, 0.016f, white ? -z : z);
            rect.localRotation = Quaternion.Euler(PanelTilt, white ? 0 : 180, 0);
            canvasObject.AddComponent<CanvasScaler>();
            Image background = canvasObject.AddComponent<Image>();
            background.color = new Color(0.12f, 0.12f, 0.14f, 0.9f);
            background.raycastTarget = false;
            ui.statusPlayerLabels[panel] = StatusText(canvasObject, "Players", font, 0, PlayersWidth, 17);
            ui.statusLabels[panel] = StatusText(canvasObject, "Status", font, PlayersWidth, 1, 14);
        }
    }

    // A column of a status panel, from xMin to xMax of its width, left-aligned and shrinking its text to fit. Empty:
    // the UI writes it.
    static TextMeshProUGUI StatusText(GameObject canvasObject, string name, TMP_FontAsset font, float xMin,
        float xMax, int size) {
        GameObject labelObject = AddAnchored(canvasObject, name, new Vector2(xMin, 0), new Vector2(xMax, 1),
            new Vector2(10, 6), new Vector2(-10, -6));
        TextMeshProUGUI label = AddText(labelObject, font, size, TextAlignmentOptions.Left, PaleText);
        label.enableAutoSizing = true;
        label.fontSizeMin = 8;
        label.fontSizeMax = size;
        label.text = "";
        return label;
    }

    // The scene's UI for a board (built earlier) other than ui, or null.
    static ChessBoardUI ExistingUI(ChessBoard board, ChessBoardUI ui) {
        ChessBoardUI[] uis = Object.FindObjectsOfType<ChessBoardUI>(true);
        for (int i = 0; i < uis.Length; i++) {
            if (uis[i] != ui && uis[i].board == board) return uis[i];
        }
        return null;
    }

    // The scene's history wall on the board's archive (step 10.2b), or null; Create History Wall wires views built
    // before the wall.
    static GameHistoryWall HistoryWallFor(ChessBoard board) {
        if (board.archive == null) return null;
        GameHistoryWall[] walls = Object.FindObjectsOfType<GameHistoryWall>(true);
        for (int i = 0; i < walls.Length; i++) {
            if (walls[i].archive == board.archive) return walls[i];
        }
        return null;
    }

    // ---- Generated assets ----

    static void EnsureModelsFolder() {
        if (!AssetDatabase.IsValidFolder(ModelsFolder)) AssetDatabase.CreateFolder("Assets/Siggi/ChessBoard", "Models");
    }

    // The material asset of this name, made with the Standard shader if missing.
    public static Material MaterialAsset(string name, Color color, float smoothness) {
        string path = ModelsFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        EnsureModelsFolder();
        material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.SetFloat("_Glossiness", smoothness);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // The see-through green "Promotion Ghost" material (step 12.11), made with the Standard shader in Fade mode if
    // missing.
    public static Material GhostMaterialAsset() {
        string path = ModelsFolder + "/Promotion Ghost.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        EnsureModelsFolder();
        material = new Material(Shader.Find("Standard"));
        material.color = new Color(0.25f, 0.95f, 0.4f, 0.45f);
        material.SetFloat("_Glossiness", 0.4f);
        material.SetFloat("_Mode", 2);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // The piece type's mesh asset, generated if missing: turned profiles (radius, height in metres, bottom to top)
    // with a few boxes for the rook's battlements, the knight's head and the king's cross. Origin at the piece's
    // base centre; the knight faces +z.
    static Mesh PieceMesh(int type) {
        string path = ModelsFolder + "/" + TypeNames[type] + ".asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        EnsureModelsFolder();
        MeshBuilder b = new MeshBuilder();
        switch (type) {
            case ChessRules.Pawn:
                b.Lathe(new[] {
                    new Vector2(0.016f, 0), new Vector2(0.016f, 0.004f), new Vector2(0.012f, 0.007f),
                    new Vector2(0.007f, 0.012f), new Vector2(0.005f, 0.02f), new Vector2(0.008f, 0.022f),
                    new Vector2(0.005f, 0.024f),
                });
                b.Sphere(new Vector3(0, 0.03f, 0), 0.0075f);
                break;
            case ChessRules.Rook:
                b.Lathe(new[] {
                    new Vector2(0.017f, 0), new Vector2(0.017f, 0.005f), new Vector2(0.013f, 0.008f),
                    new Vector2(0.01f, 0.012f), new Vector2(0.01f, 0.03f), new Vector2(0.013f, 0.032f),
                    new Vector2(0.013f, 0.038f),
                });
                for (int i = 0; i < 4; i++) {
                    float angle = i * 90f + 45f;
                    Quaternion turn = Quaternion.Euler(0, angle, 0);
                    b.Box(turn * new Vector3(0, 0, 0.0105f) + new Vector3(0, 0.041f, 0),
                        new Vector3(0.008f, 0.006f, 0.005f), turn);
                }
                break;
            case ChessRules.Knight:
                b.Lathe(new[] {
                    new Vector2(0.017f, 0), new Vector2(0.017f, 0.005f), new Vector2(0.013f, 0.008f),
                    new Vector2(0.011f, 0.011f),
                });
                b.Box(new Vector3(0, 0.022f, -0.002f), new Vector3(0.011f, 0.024f, 0.014f), Quaternion.Euler(-15, 0, 0));
                b.Box(new Vector3(0, 0.034f, 0.006f), new Vector3(0.01f, 0.01f, 0.022f), Quaternion.Euler(20, 0, 0));
                b.Box(new Vector3(0, 0.041f, -0.003f), new Vector3(0.008f, 0.006f, 0.004f), Quaternion.identity);
                break;
            case ChessRules.Bishop:
                b.Lathe(new[] {
                    new Vector2(0.016f, 0), new Vector2(0.016f, 0.004f), new Vector2(0.012f, 0.007f),
                    new Vector2(0.007f, 0.014f), new Vector2(0.006f, 0.028f), new Vector2(0.009f, 0.03f),
                    new Vector2(0.006f, 0.032f), new Vector2(0.009f, 0.038f), new Vector2(0.007f, 0.044f),
                    new Vector2(0.001f, 0.049f),
                });
                b.Sphere(new Vector3(0, 0.051f, 0), 0.0025f);
                break;
            case ChessRules.Queen:
                b.Lathe(new[] {
                    new Vector2(0.018f, 0), new Vector2(0.018f, 0.005f), new Vector2(0.013f, 0.008f),
                    new Vector2(0.008f, 0.016f), new Vector2(0.006f, 0.036f), new Vector2(0.01f, 0.038f),
                    new Vector2(0.007f, 0.04f), new Vector2(0.011f, 0.052f), new Vector2(0.004f, 0.054f),
                });
                b.Sphere(new Vector3(0, 0.058f, 0), 0.004f);
                break;
            case ChessRules.King:
                b.Lathe(new[] {
                    new Vector2(0.018f, 0), new Vector2(0.018f, 0.005f), new Vector2(0.013f, 0.008f),
                    new Vector2(0.008f, 0.016f), new Vector2(0.006f, 0.04f), new Vector2(0.01f, 0.042f),
                    new Vector2(0.007f, 0.044f), new Vector2(0.01f, 0.055f), new Vector2(0.003f, 0.057f),
                });
                b.Box(new Vector3(0, 0.063f, 0), new Vector3(0.003f, 0.012f, 0.003f), Quaternion.identity);
                b.Box(new Vector3(0, 0.065f, 0), new Vector3(0.009f, 0.003f, 0.003f), Quaternion.identity);
                break;
        }
        mesh = b.ToMesh(TypeNames[type]);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // The 64 squares as one mesh just above the surface, a1 dark at the view's -x -z corner: submesh 0 the light
    // squares, 1 the dark ones.
    static Mesh SquaresMesh() {
        string path = ModelsFolder + "/Squares.asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        EnsureModelsFolder();
        List<Vector3> vertices = new List<Vector3>();
        List<int> light = new List<int>();
        List<int> dark = new List<int>();
        const float y = 0.0005f;
        for (int square = 0; square < 64; square++) {
            int file = square % 8;
            int rank = square / 8;
            float x0 = (file - 4) * SquareSize;
            float z0 = (rank - 4) * SquareSize;
            int first = vertices.Count;
            vertices.Add(new Vector3(x0, y, z0));
            vertices.Add(new Vector3(x0, y, z0 + SquareSize));
            vertices.Add(new Vector3(x0 + SquareSize, y, z0 + SquareSize));
            vertices.Add(new Vector3(x0 + SquareSize, y, z0));
            List<int> triangles = (file + rank) % 2 == 0 ? dark : light;
            triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }
        mesh = new Mesh { name = "Squares" };
        mesh.SetVertices(vertices);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(light, 0);
        mesh.SetTriangles(dark, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // A flat mesh asset facing up, a square's size and centred on the origin at height y, generated if missing: the
    // whole square for frameWidth 0, otherwise a frame of that width along its edges.
    static Mesh FlatMesh(string name, float y, float frameWidth) {
        string path = ModelsFolder + "/" + name + ".asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        EnsureModelsFolder();
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        float h = SquareSize / 2;
        if (frameWidth <= 0) {
            FlatQuad(vertices, triangles, -h, -h, h, h, y);
        } else {
            float w = frameWidth;
            FlatQuad(vertices, triangles, -h, -h, h, -h + w, y);
            FlatQuad(vertices, triangles, -h, h - w, h, h, y);
            FlatQuad(vertices, triangles, -h, -h + w, -h + w, h - w, y);
            FlatQuad(vertices, triangles, h - w, -h + w, h, h - w, y);
        }
        mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // The last-move line's width, in squares.
    const float LastMoveLineWidth = 0.12f;

    // A flat bar mesh asset facing up (step 12.6), generated if missing: width wide, centred on the origin's x, from
    // the origin to one unit along +z, at height y.
    static Mesh LineMesh(string name, float y, float width) {
        string path = ModelsFolder + "/" + name + ".asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        EnsureModelsFolder();
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        FlatQuad(vertices, triangles, -width / 2, 0, width / 2, 1, y);
        mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // A diamond mesh asset facing up (step 12.5), generated if missing: a square turned 45°, centred on the origin at
    // height y, its corners at the middles of a square's edges.
    static Mesh DiamondMesh(string name, float y) {
        string path = ModelsFolder + "/" + name + ".asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        EnsureModelsFolder();
        float h = SquareSize / 2;
        mesh = new Mesh { name = name };
        // Clockwise from above, as FlatQuad's rectangles, so it faces up.
        mesh.SetVertices(new List<Vector3> {
            new Vector3(0, y, -h), new Vector3(-h, y, 0), new Vector3(0, y, h), new Vector3(h, y, 0),
        });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // A rectangle from (x0, z0) to (x1, z1) at height y, facing up (wound as SquaresMesh's squares).
    static void FlatQuad(List<Vector3> vertices, List<int> triangles, float x0, float z0, float x1, float z1, float y) {
        int first = vertices.Count;
        vertices.Add(new Vector3(x0, y, z0));
        vertices.Add(new Vector3(x0, y, z1));
        vertices.Add(new Vector3(x1, y, z1));
        vertices.Add(new Vector3(x1, y, z0));
        triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
    }

    // Collects the triangles of a generated piece. Each lathe band and each box face has its own vertices, so the
    // edges between them stay sharp.
    class MeshBuilder {
        const int Segments = 32;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<int> triangles = new List<int>();

        // A surface turned around the y axis from a profile of (radius, height), bottom to top, closed at both ends.
        public void Lathe(Vector2[] profile) {
            Disc(profile[0].y, profile[0].x, false);
            for (int i = 0; i + 1 < profile.Length; i++) Band(profile[i], profile[i + 1]);
            Vector2 top = profile[profile.Length - 1];
            if (top.x > 0) Disc(top.y, top.x, true);
        }

        public void Sphere(Vector3 centre, float radius) {
            const int rings = 8;
            for (int i = 0; i < rings; i++) {
                float a0 = Mathf.PI * i / rings - Mathf.PI / 2;
                float a1 = Mathf.PI * (i + 1) / rings - Mathf.PI / 2;
                Vector2 p0 = new Vector2(Mathf.Cos(a0) * radius, centre.y + Mathf.Sin(a0) * radius);
                Vector2 p1 = new Vector2(Mathf.Cos(a1) * radius, centre.y + Mathf.Sin(a1) * radius);
                Band(p0, p1);
            }
        }

        public void Box(Vector3 centre, Vector3 size, Quaternion rotation) {
            Vector3 h = size / 2;
            Vector3[] c = new Vector3[8];
            for (int i = 0; i < 8; i++) {
                Vector3 corner = new Vector3((i & 1) != 0 ? h.x : -h.x, (i & 2) != 0 ? h.y : -h.y,
                    (i & 4) != 0 ? h.z : -h.z);
                c[i] = centre + rotation * corner;
            }
            Quad(c[0], c[2], c[3], c[1]); // -z
            Quad(c[5], c[7], c[6], c[4]); // +z
            Quad(c[4], c[6], c[2], c[0]); // -x
            Quad(c[1], c[3], c[7], c[5]); // +x
            Quad(c[2], c[6], c[7], c[3]); // +y
            Quad(c[4], c[0], c[1], c[5]); // -y
        }

        void Band(Vector2 low, Vector2 high) {
            for (int s = 0; s < Segments; s++) {
                Quaternion a = Quaternion.Euler(0, 360f * s / Segments, 0);
                Quaternion b = Quaternion.Euler(0, 360f * (s + 1) / Segments, 0);
                Quad(a * new Vector3(low.x, 0, 0) + Vector3.up * low.y, b * new Vector3(low.x, 0, 0) + Vector3.up * low.y,
                    b * new Vector3(high.x, 0, 0) + Vector3.up * high.y, a * new Vector3(high.x, 0, 0) + Vector3.up * high.y);
            }
        }

        void Disc(float y, float radius, bool up) {
            int centre = vertices.Count;
            vertices.Add(new Vector3(0, y, 0));
            for (int s = 0; s < Segments; s++) {
                vertices.Add(Quaternion.Euler(0, 360f * s / Segments, 0) * new Vector3(radius, 0, 0) + Vector3.up * y);
            }
            for (int s = 0; s < Segments; s++) {
                int a = centre + 1 + s;
                int b = centre + 1 + (s + 1) % Segments;
                if (up) triangles.AddRange(new[] { centre, a, b });
                else triangles.AddRange(new[] { centre, b, a });
            }
        }

        // Two triangles, a b c d clockwise seen from the front (Unity's winding).
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) {
            int first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }

        public Mesh ToMesh(string name) {
            Mesh mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
