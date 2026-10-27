using System.Collections.Generic;
using SiggiChessBoard;
using SiggiUSTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.Udon;

// Step 14's check of the prefabs (docs/prefabs.md), without Play Mode: run it from Tools > Chess Board > Test Prefabs.
// It drops each prefab into a new scene of its own, opened beside the open one and closed unsaved afterwards, and
// checks what it holds. 14.1: Chess Pieces on its own, and the Chess Arbiter with its nested Chess Pieces, the build
// check on it, a change to the Chess Pieces prefab reaching the nested instance but not an unpacked copy. The change
// is undone and the prefab saved again before it ends. 14.2: the Chess Board with its importer, two placed with their
// own board numbers, and the open scene's boards instances of it. 14.3: the Chess Board View with every object it uses,
// its pieces standing on their starting squares, pointed at a placed board, and the open scene's views instances of
// it. 14.4: the Chess Board UI with its buttons (each calling its own UI), status panels, PGN box and sounds, pointed
// at a placed board, and the open scene's UIs instances of it. 14.5: the Game Archive with its own holders, a board
// pointed at it, and the open scene's archive an instance of it, every board on it. Every prefab: nothing it uses is
// under Assets/Tests/.
// Logs "[PrefabTest] PASS: <check>" or "[PrefabTest] FAIL: <check>: <details>" per check, then a summary line.
public static class PrefabTest {
    const string PieceSetPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Pieces.prefab";
    const string ArbiterPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Arbiter.prefab";
    const string BoardPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Board.prefab";
    const string ViewPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Board View.prefab";
    const string UIPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Board UI.prefab";
    const string ArchivePrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Game Archive.prefab";

    static int passed;
    static int failed;

    [MenuItem("Tools/Chess Board/Test Prefabs")]
    public static void Run() {
        passed = 0;
        failed = 0;
        GameObject setPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PieceSetPrefabPath);
        GameObject arbiterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArbiterPrefabPath);
        Check(setPrefab != null, "the Chess Pieces prefab at " + PieceSetPrefabPath, "missing");
        Check(arbiterPrefab != null, "the Chess Arbiter prefab at " + ArbiterPrefabPath, "missing");
        GameObject boardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoardPrefabPath);
        Check(boardPrefab != null, "the Chess Board prefab at " + BoardPrefabPath, "missing");
        GameObject viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPrefabPath);
        Check(viewPrefab != null, "the Chess Board View prefab at " + ViewPrefabPath, "missing");
        GameObject uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UIPrefabPath);
        Check(uiPrefab != null, "the Chess Board UI prefab at " + UIPrefabPath, "missing");
        GameObject archivePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArchivePrefabPath);
        Check(archivePrefab != null, "the Game Archive prefab at " + ArchivePrefabPath, "missing");
        if (setPrefab == null || arbiterPrefab == null || boardPrefab == null || viewPrefab == null
            || uiPrefab == null || archivePrefab == null) {
            Summary();
            return;
        }
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try {
            CheckPieceSet(setPrefab, scene);
            CheckArbiter(setPrefab, arbiterPrefab, scene);
            CheckBoard(boardPrefab, scene);
            CheckView(viewPrefab, boardPrefab, scene);
            CheckUI(uiPrefab, boardPrefab, scene);
            CheckArchive(archivePrefab, arbiterPrefab, scene);
        } finally {
            EditorSceneManager.CloseScene(scene, true);
        }
        CheckSceneBoards(boardPrefab);
        CheckSceneViews(viewPrefab);
        CheckSceneUIs(uiPrefab);
        CheckSceneArchive(archivePrefab);
        foreach (string path in new[] {
            PieceSetPrefabPath, ArbiterPrefabPath, BoardPrefabPath, ViewPrefabPath, UIPrefabPath, ArchivePrefabPath,
        }) {
            CheckNoTests(path);
            CheckSyncMethods(path);
        }
        Summary();
    }

    // ---- 14.1: Chess Pieces ----

    static void CheckPieceSet(GameObject setPrefab, Scene scene) {
        List<string> logged = new List<string>();
        Application.LogCallback listener = (text, stack, type) => logged.Add(type + ": " + text);
        Application.logMessageReceived += listener;
        GameObject placed;
        try {
            placed = (GameObject)PrefabUtility.InstantiatePrefab(setPrefab, scene);
        } finally {
            Application.logMessageReceived -= listener;
        }
        Check(logged.Count == 0, "Chess Pieces placed on its own logs nothing", string.Join("; ", logged));
        ChessPieceSet set = placed.GetComponent<ChessPieceSet>();
        Check(set != null, "Chess Pieces has a ChessPieceSet", "none");
        if (set == null) return;
        Check(set.Problem() == "", "Chess Pieces has every White template", set.Problem());
        string outside = "";
        int black = 0;
        foreach (Transform template in Templates(set)) {
            if (template == null) continue;
            if (!template.IsChildOf(set.transform) || template == set.transform) outside += " " + template.name;
            if (template.name != "Model") outside += " " + template.name + " (not a Model)";
        }
        Check(outside == "", "Chess Pieces' templates are its own Models", outside);
        for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) {
            Transform template = set._Template(ChessRules.Black, type);
            if (template == null) continue;
            MeshFilter filter = template.GetComponent<MeshFilter>();
            MeshRenderer renderer = template.GetComponent<MeshRenderer>();
            if ((filter == null || filter.sharedMesh == null) && renderer != null && renderer.sharedMaterial != null) {
                black++;
            }
        }
        Check(black == 6, "Chess Pieces' Black templates are renderers with materials and no mesh",
            black + " of 6");
        Object.DestroyImmediate(placed);
    }

    // Every template field of set, White's and Black's, pieces and promotion pieces.
    static List<Transform> Templates(ChessPieceSet set) {
        List<Transform> all = new List<Transform>();
        foreach (int color in new[] { ChessRules.White, ChessRules.Black }) {
            for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) all.Add(set._Template(color, type));
            for (int type = ChessRules.Knight; type <= ChessRules.Queen; type++) {
                all.Add(set._PromotionTemplate(color, type));
            }
        }
        return all;
    }

    // ---- 14.1: Chess Arbiter ----

    static void CheckArbiter(GameObject setPrefab, GameObject arbiterPrefab, Scene scene) {
        GameObject placed = (GameObject)PrefabUtility.InstantiatePrefab(arbiterPrefab, scene);
        ChessArbiter arbiter = placed.GetComponent<ChessArbiter>();
        Check(arbiter != null, "the Chess Arbiter has a ChessArbiter", "none");
        if (arbiter == null) return;
        Check(arbiter.simpleAuth != null && arbiter.simpleAuth.gameObject == placed
            && arbiter.simpleAuth is FirstJoinerAuth, "the arbiter's Simple Auth is its own FirstJoinerAuth",
            arbiter.simpleAuth == null ? "empty" : arbiter.simpleAuth.GetType().Name + " on "
                + arbiter.simpleAuth.gameObject.name);
        ChessPieceSet nested = arbiter.pieceSets != null && arbiter.pieceSets.Length == 1 ? arbiter.pieceSets[0] : null;
        bool isNested = nested != null && nested.transform.parent == placed.transform
            && PrefabUtility.IsAnyPrefabInstanceRoot(nested.gameObject)
            && PrefabUtility.GetCorrespondingObjectFromOriginalSource(nested.gameObject) == setPrefab;
        Check(isNested, "the arbiter's Piece Sets hold only its nested Chess Pieces",
            arbiter.pieceSets == null ? "null" : arbiter.pieceSets.Length + " entries");
        Check(arbiter.boardCount == 4, "the arbiter's boardCount is the default", arbiter.boardCount.ToString());
        CheckIntValue(arbiter.archives != null ? arbiter.archives.Length : -1, 0,
            "the arbiter's Archives are left to set (13.10)");

        Check(!ChessArbiterBuildCheck.Blocking(VRCSDKRequestedBuildType.Scene).Contains(arbiter),
            "the build check passes on the arbiter", "it blocks");
        SimpleAuth savedAuth = arbiter.simpleAuth;
        ChessPieceSet[] savedSets = arbiter.pieceSets;
        arbiter.simpleAuth = null;
        Check(ChessArbiterBuildCheck.Blocking(VRCSDKRequestedBuildType.Scene).Contains(arbiter),
            "Simple Auth cleared: the build check blocks", "it doesn't");
        arbiter.simpleAuth = savedAuth;
        arbiter.pieceSets = new ChessPieceSet[0];
        Check(ChessArbiterBuildCheck.Blocking(VRCSDKRequestedBuildType.Scene).Contains(arbiter),
            "Piece Sets emptied: the build check blocks", "it doesn't");
        arbiter.pieceSets = new ChessPieceSet[] { null };
        Check(ChessArbiterBuildCheck.Blocking(VRCSDKRequestedBuildType.Scene).Contains(arbiter),
            "a Piece Sets entry emptied: the build check blocks", "it doesn't");
        arbiter.pieceSets = savedSets;
        if (!isNested) return;

        // An unpacked copy, changed and listed at index 1.
        GameObject copyObject = (GameObject)PrefabUtility.InstantiatePrefab(setPrefab, scene);
        PrefabUtility.UnpackPrefabInstance(copyObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        ChessPieceSet copy = copyObject.GetComponent<ChessPieceSet>();
        Vector3 copyScale = new Vector3(2, 2, 2);
        copy.whiteKnight.localScale = copyScale;
        arbiter.pieceSets = new[] { nested, copy };
        // An override, as listing it in the inspector makes one; otherwise the prefab's save below puts it back.
        PrefabUtility.RecordPrefabInstancePropertyModifications(arbiter);
        Check(!PrefabUtility.IsPartOfAnyPrefab(copyObject), "the unpacked copy has no prefab link", "it has one");

        // A change to the Chess Pieces prefab: its White Pawn slot raised, then put back.
        const string Slot = "White Pawn";
        Vector3 rise = new Vector3(0, 0.25f, 0);
        GameObject contents = PrefabUtility.LoadPrefabContents(PieceSetPrefabPath);
        Transform prefabSlot = contents.transform.Find(Slot);
        Vector3 original = prefabSlot != null ? prefabSlot.localPosition : Vector3.zero;
        bool changed = false;
        try {
            Check(prefabSlot != null, "the Chess Pieces prefab has a " + Slot + " slot", "none");
            if (prefabSlot == null) return;
            prefabSlot.localPosition = original + rise;
            PrefabUtility.SaveAsPrefabAsset(contents, PieceSetPrefabPath);
            changed = true;
            Transform nestedSlot = nested.transform.Find(Slot);
            Transform copySlot = copy.transform.Find(Slot);
            Check(nestedSlot != null && nestedSlot.localPosition == original + rise,
                "a change to the Chess Pieces prefab reaches the arbiter's nested instance",
                nestedSlot == null ? "no slot" : nestedSlot.localPosition.ToString("F3"));
            Check(copySlot != null && copySlot.localPosition == original,
                "the unpacked copy is unaffected by the change",
                copySlot == null ? "no slot" : copySlot.localPosition.ToString("F3"));
            Check(copy.whiteKnight.localScale == copyScale, "the unpacked copy keeps its own change",
                copy.whiteKnight.localScale.ToString("F3"));
            Check(arbiter.pieceSets.Length == 2 && arbiter.pieceSets[1] == copy,
                "the unpacked copy stays at index 1", arbiter.pieceSets.Length + " sets");
        } finally {
            if (changed) {
                prefabSlot.localPosition = original;
                PrefabUtility.SaveAsPrefabAsset(contents, PieceSetPrefabPath);
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }
        Transform restored = setPrefab.transform.Find(Slot);
        Check(restored != null && restored.localPosition == original, "the Chess Pieces prefab is put back",
            restored == null ? "no slot" : restored.localPosition.ToString("F3"));
    }

    // ---- 14.2: Chess Board ----

    static void CheckBoard(GameObject boardPrefab, Scene scene) {
        List<string> logged = new List<string>();
        Application.LogCallback listener = (text, stack, type) => logged.Add(type + ": " + text);
        Application.logMessageReceived += listener;
        GameObject first;
        GameObject second;
        try {
            first = (GameObject)PrefabUtility.InstantiatePrefab(boardPrefab, scene);
            second = (GameObject)PrefabUtility.InstantiatePrefab(boardPrefab, scene);
        } finally {
            Application.logMessageReceived -= listener;
        }
        Check(logged.Count == 0, "the Chess Board placed logs nothing", string.Join("; ", logged));
        ChessBoard board = first.GetComponent<ChessBoard>();
        Check(board != null, "the Chess Board has a ChessBoard", "none");
        if (board == null) return;
        Check(board.importer != null && board.importer.transform.parent == first.transform,
            "the board's importer is its own ChessPgnImporter child",
            board.importer == null ? "empty" : "on " + board.importer.gameObject.name);
        Check(board.arbiter == null, "the board's arbiter is left to set", "arbiter " + board.arbiter);
        CheckIntValue(board.boardNumber, 0, "the board's number is 0");
        CheckIntValue(board.listeners != null ? board.listeners.Length : -1, 0, "the board has no listeners");
        Check(board.pgnSite == "VRChat" && board.pgnEvent == "", "the board's PGN tags are the defaults",
            "Event \"" + board.pgnEvent + "\", Site \"" + board.pgnSite + "\"");

        // Two placed boards wired by hand with their own numbers, as a world creator would set them in the Inspector.
        ChessBoard other = second.GetComponent<ChessBoard>();
        SerializedObject fields = new SerializedObject(other);
        fields.FindProperty("boardNumber").intValue = 1;
        fields.ApplyModifiedProperties();
        CheckIntValue(board.boardNumber, 0, "two placed boards: the first keeps number 0");
        CheckIntValue(other.boardNumber, 1, "two placed boards: the second has number 1");
        Check(other.importer != null && other.importer != board.importer
            && other.importer.transform.parent == second.transform, "two placed boards: each has its own importer",
            "shared or missing");
        CheckIntValue(boardPrefab.GetComponent<ChessBoard>().boardNumber, 0,
            "two placed boards: the prefab's number is unchanged");
        Object.DestroyImmediate(first);
        Object.DestroyImmediate(second);
    }

    // The open scene's boards (the test scene's) are instances of the Chess Board prefab, keeping their own wiring.
    static void CheckSceneBoards(GameObject boardPrefab) {
        ChessBoard[] boards = Object.FindObjectsOfType<ChessBoard>(true);
        Check(boards.Length > 0, "the open scene has boards", "none");
        HashSet<int> numbers = new HashSet<int>();
        foreach (ChessBoard board in boards) {
            string name = "the scene's " + board.gameObject.name;
            Check(PrefabUtility.IsAnyPrefabInstanceRoot(board.gameObject)
                && PrefabUtility.GetCorrespondingObjectFromOriginalSource(board.gameObject) == boardPrefab,
                name + " is a Chess Board instance", "it isn't");
            Check(board.arbiter != null, name + " has its arbiter", "empty");
            Check(board.importer != null && board.importer.transform.parent == board.transform,
                name + " has its own importer", board.importer == null ? "empty" : "on " + board.importer.name);
            Check(numbers.Add(board.boardNumber), name + "'s number " + board.boardNumber + " is its own",
                "another board has it");
        }
    }

    // ---- 14.3: Chess Board View ----

    // The back rank from a to h, by piece type, as the view maps the pieces on Start.
    static readonly int[] BackRank = {
        ChessRules.Rook, ChessRules.Knight, ChessRules.Bishop, ChessRules.Queen, ChessRules.King, ChessRules.Bishop,
        ChessRules.Knight, ChessRules.Rook,
    };

    static void CheckView(GameObject viewPrefab, GameObject boardPrefab, Scene scene) {
        List<string> logged = new List<string>();
        Application.LogCallback listener = (text, stack, type) => logged.Add(type + ": " + text);
        Application.logMessageReceived += listener;
        GameObject placed;
        try {
            placed = (GameObject)PrefabUtility.InstantiatePrefab(viewPrefab, scene);
        } finally {
            Application.logMessageReceived -= listener;
        }
        Check(logged.Count == 0, "the Chess Board View placed logs nothing", string.Join("; ", logged));
        ChessBoardView view = placed.GetComponent<ChessBoardView>();
        Check(view != null, "the Chess Board View has a ChessBoardView", "none");
        if (view == null) return;
        Check(view.board == null, "the view's board is left to set", "on " + view.board);
        Check(view.i18n == null, "the view's I18n is left for the sanity check", "on " + view.i18n);
        Check(view.LocalSpaceProblem() == "", "the view's objects are in its local space", view.LocalSpaceProblem());

        string pieces = "";
        float size = view.squareSize;
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            Transform piece = view.pieces != null && obj < view.pieces.Length ? view.pieces[obj] : null;
            if (piece == null || !piece.IsChildOf(placed.transform)) {
                pieces += " " + obj + " missing;";
                continue;
            }
            bool white = obj < ChessBoardView.PiecesPerColor;
            int index = obj % ChessBoardView.PiecesPerColor;
            int file = index % 8;
            bool backRank = white == (index < 8);
            int rank = white ? (backRank ? 0 : 1) : (backRank ? 7 : 6);
            int type = backRank ? BackRank[file] : ChessRules.Pawn;
            Vector3 square = new Vector3((file - 3.5f) * size, 0, (rank - 3.5f) * size);
            if ((piece.localPosition - square).sqrMagnitude > 1e-8f) {
                pieces += " " + piece.name + " at " + piece.localPosition.ToString("F3") + ";";
            }
            MeshFilter filter = view.pieceFilters[obj];
            if (filter == null || filter.transform != piece || filter.sharedMesh == null
                || filter.sharedMesh.name != new[] { "", "Pawn", "Knight", "Bishop", "Rook", "Queen", "King" }[type]) {
                pieces += " " + piece.name + "'s mesh;";
            }
            if (view.pieceRenderers[obj] == null || view.pieceRenderers[obj].transform != piece
                || view.piecePickups[obj] == null || view.piecePickups[obj].transform != piece
                || view.pieceColliders[obj] == null || view.pieceColliders[obj].transform != piece) {
                pieces += " " + piece.name + "'s renderer, pickup or collider;";
            }
            ChessPiece behaviour = view.pieceBehaviours[obj];
            if (behaviour == null || behaviour.transform != piece || behaviour.view != view || behaviour.index != obj) {
                pieces += " " + piece.name + "'s ChessPiece;";
            }
        }
        Check(pieces == "", "the view's 32 pieces stand on their starting squares, each wired to the view", pieces);

        string targets = "";
        for (int square = 0; square < 64; square++) {
            ChessSquare target = view.squareTargets != null && square < view.squareTargets.Length
                ? view.squareTargets[square] : null;
            if (target == null || target.view != view || target.square != square || target.gameObject.activeSelf) {
                targets += " " + square;
            }
            if (view.targetHighlights == null || square >= view.targetHighlights.Length
                || view.targetHighlights[square] == null) {
                targets += " highlight " + square;
            }
        }
        Check(targets == "", "the view's 64 square targets (hidden) and legal-target highlights", targets);
        Check(view.lastMoveLine != null && view.lastMoveMark != null && view.checkLines != null
            && view.checkLines.Length == ChessBoardView.CheckLineCount && System.Array.IndexOf(view.checkLines, null) < 0,
            "the view's last-move line and mark and its " + ChessBoardView.CheckLineCount + " check lines",
            "missing");
        string ghosts = "";
        for (int ghost = 0; ghost < ChessBoardView.GhostCount; ghost++) {
            ChessPromotionGhost g = view.promotionGhosts != null && ghost < view.promotionGhosts.Length
                ? view.promotionGhosts[ghost] : null;
            if (g == null || g.view != view || g.pieceType != ChessBoardView._GhostType(ghost)) ghosts += " " + ghost;
        }
        Check(ghosts == "", "the view's promotion ghosts", ghosts);
        Check(view.whiteCapturedPlaces != null && view.whiteCapturedPlaces.Length == ChessBoardView.PiecesPerColor
            && System.Array.IndexOf(view.whiteCapturedPlaces, null) < 0 && view.blackCapturedPlaces != null
            && view.blackCapturedPlaces.Length == ChessBoardView.PiecesPerColor
            && System.Array.IndexOf(view.blackCapturedPlaces, null) < 0, "the view's 16 captured places per colour",
            "missing");
        Check(view.blocker != null && view.blocker.transform.IsChildOf(placed.transform), "the view's player blocker",
            "missing");
        Check(view.audioSource != null && view.moveSound != null && view.captureSound != null
            && view.promoteSound != null && view.checkSound != null, "the view's sound source and move sounds",
            "missing");
        Check(viewPrefab.GetComponent<ChessBoardView>().board == null, "the view prefab points at no board",
            "it does");

        // Pointed at a placed board by hand, as a world creator would set it in the Inspector.
        GameObject boardObject = (GameObject)PrefabUtility.InstantiatePrefab(boardPrefab, scene);
        ChessBoard board = boardObject.GetComponent<ChessBoard>();
        SerializedObject fields = new SerializedObject(view);
        fields.FindProperty("board").objectReferenceValue = board;
        fields.ApplyModifiedProperties();
        Check(view.board == board, "the view pointed at a placed board", "on " + view.board);
        Check(viewPrefab.GetComponent<ChessBoardView>().board == null,
            "the view pointed at a board: the prefab still points at none", "it doesn't");
        Object.DestroyImmediate(placed);
        Object.DestroyImmediate(boardObject);
    }

    // The open scene's views (the test scene's) are instances of the Chess Board View prefab, each on its own board,
    // their pieces wired to them.
    static void CheckSceneViews(GameObject viewPrefab) {
        ChessBoardView[] views = Object.FindObjectsOfType<ChessBoardView>(true);
        Check(views.Length > 0, "the open scene has views", "none");
        HashSet<ChessBoard> boards = new HashSet<ChessBoard>();
        foreach (ChessBoardView view in views) {
            string name = "the scene's " + view.gameObject.name;
            Check(PrefabUtility.IsAnyPrefabInstanceRoot(view.gameObject)
                && PrefabUtility.GetCorrespondingObjectFromOriginalSource(view.gameObject) == viewPrefab,
                name + " is a Chess Board View instance", "it isn't");
            Check(view.board != null && boards.Add(view.board), name + " has a board of its own",
                view.board == null ? "empty" : "shared");
            int wired = 0;
            for (int obj = 0; obj < view.pieceBehaviours.Length; obj++) {
                ChessPiece piece = view.pieceBehaviours[obj];
                if (piece != null && piece.view == view && piece.transform.IsChildOf(view.transform)) wired++;
            }
            CheckIntValue(wired, ChessBoardView.PieceCount, name + "'s pieces are its own");
        }
    }

    // ---- 14.4: Chess Board UI ----

    static void CheckUI(GameObject uiPrefab, GameObject boardPrefab, Scene scene) {
        List<string> logged = new List<string>();
        Application.LogCallback listener = (text, stack, type) => logged.Add(type + ": " + text);
        Application.logMessageReceived += listener;
        GameObject placed;
        try {
            placed = (GameObject)PrefabUtility.InstantiatePrefab(uiPrefab, scene);
        } finally {
            Application.logMessageReceived -= listener;
        }
        Check(logged.Count == 0, "the Chess Board UI placed logs nothing", string.Join("; ", logged));
        ChessBoardUI ui = placed.GetComponent<ChessBoardUI>();
        Check(ui != null, "the Chess Board UI has a ChessBoardUI", "none");
        if (ui == null) return;
        Check(ui.board == null, "the UI's board is left to set", "board " + ui.board);
        Check(ui.i18n == null, "the UI's I18n is left for the sanity check", "on " + ui.i18n);

        // Each panel has every action but the other colour's: the colourless ones twice, each colour's once.
        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(ui);
        int[] seen = new int[ChessBoardUI.ButtonCount];
        string buttons = "";
        int count = ui.buttons != null ? ui.buttons.Length : 0;
        if (ui.buttonActions == null || ui.buttonActions.Length != count || ui.buttonLabels == null
            || ui.buttonLabels.Length != count) {
            buttons += " the arrays' lengths differ;";
            count = 0;
        }
        for (int i = 0; i < count; i++) {
            GameObject button = ui.buttons[i];
            int action = ui.buttonActions[i];
            if (button == null || !button.transform.IsChildOf(placed.transform) || action < 0
                || action >= ChessBoardUI.ButtonCount) {
                buttons += " " + i + " missing;";
                continue;
            }
            seen[action]++;
            if (ui.buttonLabels[i] == null || !ui.buttonLabels[i].transform.IsChildOf(button.transform)) {
                buttons += " " + button.name + "'s label;";
            }
            string pressed = PressEvent(button.GetComponent<UnityEngine.UI.Button>(), udon);
            if (pressed != "_Press" + button.name) buttons += " " + button.name + " calls " + pressed + ";";
        }
        for (int action = 0; action < ChessBoardUI.ButtonCount; action++) {
            int expected = ChessBoardUI._ButtonColor(action) == ChessRules.NoColor ? 2 : 1;
            if (seen[action] != expected) buttons += " action " + action + " " + seen[action] + " times;";
        }
        Check(count > 0 && buttons == "", "the UI's " + count + " buttons, each action on its panels, each calling the "
            + "UI's own event", buttons);
        Check(ui.statusPlayerLabels != null && ui.statusPlayerLabels.Length == 2
            && System.Array.IndexOf(ui.statusPlayerLabels, null) < 0 && ui.statusLabels != null
            && ui.statusLabels.Length == 2 && System.Array.IndexOf(ui.statusLabels, null) < 0,
            "the UI's two status panels", "missing");

        bool box = ui.pgnBox != null && ui.pgnBox.transform.IsChildOf(placed.transform) && !ui.pgnBox.activeSelf
            && ui.pgnField != null && ui.pgnField.readOnly && ui.pgnPasteField != null && !ui.pgnPasteField.readOnly
            && ui.pgnCopyPart != null && ui.pgnPastePart != null && !ui.pgnPastePart.activeSelf
            && ui.pgnCopyHint != null && ui.pgnPasteHint != null && ui.pgnImportLabel != null
            && ui.pgnImportButton != null && ui.pgnImportText != null && ui.pgnCloseText != null;
        Check(box, "the UI's PGN box (hidden) with its copy and paste parts", "missing");
        if (box) {
            CheckString(PressEvent(ui.pgnImportButton.GetComponent<UnityEngine.UI.Button>(), udon), "_PressImport",
                "the PGN box's Import calls the UI's own event");
            CheckString(PressEvent(ui.pgnCloseText.transform.parent.GetComponent<UnityEngine.UI.Button>(), udon),
                "_PressPgnClose", "the PGN box's Close calls the UI's own event");
        }
        Check(ui.audioSource != null && ui.audioSource.transform.IsChildOf(placed.transform) && ui.clickSound != null
            && ui.gameStartSound != null && ui.notificationSound != null && ui.gameEndSound != null
            && ui.resetSound != null && ui.timeWarningSound != null && ui.timeOutSound != null && ui.checkSound != null,
            "the UI's sound source and sounds", "missing");

        // Pointed at a placed board by hand, as a world creator would set it in the Inspector.
        GameObject boardObject = (GameObject)PrefabUtility.InstantiatePrefab(boardPrefab, scene);
        ChessBoard board = boardObject.GetComponent<ChessBoard>();
        SerializedObject fields = new SerializedObject(ui);
        fields.FindProperty("board").objectReferenceValue = board;
        fields.ApplyModifiedProperties();
        Check(ui.board == board, "the UI pointed at a placed board", "on " + ui.board);
        Check(uiPrefab.GetComponent<ChessBoardUI>().board == null,
            "the UI pointed at a board: the prefab still points at none", "it doesn't");
        Object.DestroyImmediate(placed);
        Object.DestroyImmediate(boardObject);
    }

    // The event a UI button sends: its one click listener's SendCustomEvent argument, when it calls udon; otherwise
    // what's wrong.
    static string PressEvent(UnityEngine.UI.Button button, UdonBehaviour udon) {
        if (button == null) return "(no button)";
        if (button.onClick.GetPersistentEventCount() != 1) return "(" + button.onClick.GetPersistentEventCount()
            + " listeners)";
        if (button.onClick.GetPersistentTarget(0) != udon) return "(another behaviour)";
        if (button.onClick.GetPersistentMethodName(0) != "SendCustomEvent") return "(not SendCustomEvent)";
        return new SerializedObject(button)
            .FindProperty("m_OnClick.m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_StringArgument").stringValue;
    }

    // The open scene's UIs (the test scene's) are instances of the Chess Board UI prefab, each on its own board, with
    // its buttons its own.
    static void CheckSceneUIs(GameObject uiPrefab) {
        ChessBoardUI[] uis = Object.FindObjectsOfType<ChessBoardUI>(true);
        Check(uis.Length > 0, "the open scene has UIs", "none");
        HashSet<ChessBoard> boards = new HashSet<ChessBoard>();
        foreach (ChessBoardUI ui in uis) {
            string name = "the scene's " + ui.gameObject.name;
            Check(PrefabUtility.IsAnyPrefabInstanceRoot(ui.gameObject)
                && PrefabUtility.GetCorrespondingObjectFromOriginalSource(ui.gameObject) == uiPrefab,
                name + " is a Chess Board UI instance", "it isn't");
            Check(ui.board != null && boards.Add(ui.board), name + " has a board of its own",
                ui.board == null ? "empty" : "shared");
            UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(ui);
            int own = 0;
            for (int i = 0; i < ui.buttons.Length; i++) {
                if (ui.buttons[i] != null && ui.buttons[i].transform.IsChildOf(ui.transform)
                    && PressEvent(ui.buttons[i].GetComponent<UnityEngine.UI.Button>(), udon).StartsWith("_Press")) {
                    own++;
                }
            }
            CheckIntValue(own, ui.buttons.Length, name + "'s buttons are its own, calling it");
        }
    }

    // ---- 14.5: Game Archive ----

    static void CheckArchive(GameObject archivePrefab, GameObject arbiterPrefab, Scene scene) {
        List<string> logged = new List<string>();
        Application.LogCallback listener = (text, stack, type) => logged.Add(type + ": " + text);
        Application.logMessageReceived += listener;
        GameObject placed;
        try {
            placed = (GameObject)PrefabUtility.InstantiatePrefab(archivePrefab, scene);
        } finally {
            Application.logMessageReceived -= listener;
        }
        Check(logged.Count == 0, "the Game Archive placed logs nothing", string.Join("; ", logged));
        InstanceGameArchive archive = placed.GetComponent<InstanceGameArchive>();
        Check(archive != null, "the Game Archive has an InstanceGameArchive (GameArchive before 13.10)", "none");
        if (archive == null) return;
        Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(archive).SyncMethod == VRC.SDKBase.Networking.SyncType.Manual,
            "the archive syncs manually", "it doesn't");
        CheckIntValue(archive.holders != null ? archive.holders.Length : -1, 10, "the archive has 10 holders");
        string holders = "";
        HashSet<GameArchiveHolder> distinct = new HashSet<GameArchiveHolder>();
        for (int i = 0; archive.holders != null && i < archive.holders.Length; i++) {
            GameArchiveHolder holder = archive.holders[i];
            if (holder == null || holder.transform.parent != placed.transform || !distinct.Add(holder)) {
                holders += " " + i + " missing, not a child or shared;";
                continue;
            }
            if (holder.archive != archive) holders += " " + i + "'s archive;";
            if (holder.localOnly) holders += " " + i + " local only;";
            if (UdonSharpEditorUtility.GetBackingUdonBehaviour(holder).SyncMethod
                != VRC.SDKBase.Networking.SyncType.Manual) {
                holders += " " + i + " not manual sync;";
            }
        }
        Check(holders == "", "the archive's holders are its own children, synced, each pointing back", holders);
        GameArchiveHolder recent = archive.recent;
        Check(recent != null && recent.transform.parent == placed.transform && recent.localOnly
            && recent.archive == archive && !distinct.Contains(recent),
            "the archive's recent holder is its own local-only child, pointing back", recent == null ? "empty"
                : "on " + recent.gameObject.name + ", local only " + recent.localOnly);

        // A placed arbiter listing it by hand, as a world creator would set it in the Inspector (13.10).
        GameObject arbiterObject = (GameObject)PrefabUtility.InstantiatePrefab(arbiterPrefab, scene);
        ChessArbiter arbiter = arbiterObject.GetComponent<ChessArbiter>();
        SerializedObject fields = new SerializedObject(arbiter);
        SerializedProperty list = fields.FindProperty("archives");
        list.arraySize = 1;
        list.GetArrayElementAtIndex(0).objectReferenceValue = archive;
        fields.ApplyModifiedProperties();
        Check(arbiter.archives.Length == 1 && arbiter.archives[0] == archive, "an arbiter listing the placed archive",
            arbiter.archives.Length + " entries");
        CheckIntValue(arbiterPrefab.GetComponent<ChessArbiter>().archives.Length, 0,
            "an arbiter listing the archive: the arbiter prefab still lists none");
        Object.DestroyImmediate(placed);
        Object.DestroyImmediate(arbiterObject);
    }

    // The open scene's one instance archive (the test scene's) is an instance of the Game Archive prefab, with its own
    // holders, and the arbiter's Archives hold the scene's archives (the personal one too, 13.10).
    static void CheckSceneArchive(GameObject archivePrefab) {
        InstanceGameArchive[] archives = Object.FindObjectsOfType<InstanceGameArchive>(true);
        CheckIntValue(archives.Length, 1, "the open scene has one instance archive");
        if (archives.Length == 0) return;
        InstanceGameArchive archive = archives[0];
        string name = "the scene's " + archive.gameObject.name;
        Check(PrefabUtility.IsAnyPrefabInstanceRoot(archive.gameObject)
            && PrefabUtility.GetCorrespondingObjectFromOriginalSource(archive.gameObject) == archivePrefab,
            name + " is a Game Archive instance", "it isn't");
        int own = 0;
        foreach (GameArchiveHolder holder in archive.holders) {
            if (holder != null && holder.archive == archive && holder.transform.parent == archive.transform) own++;
        }
        CheckIntValue(own, archive.holders.Length, name + "'s holders are its own");
        GameArchive[] all = Object.FindObjectsOfType<GameArchive>(true);
        foreach (ChessArbiter arbiter in Object.FindObjectsOfType<ChessArbiter>(true)) {
            string missing = "";
            foreach (GameArchive each in all) {
                if (System.Array.IndexOf(arbiter.archives, each) < 0) missing += " " + each.gameObject.name;
            }
            Check(missing == "" && arbiter.archives.Length == all.Length,
                "the scene's " + arbiter.gameObject.name + " lists the scene's archives",
                "missing:" + missing + ", " + arbiter.archives.Length + " entries");
        }
    }

    // Every UdonBehaviour in a prefab (nested prefabs' too) has the sync method its script asks for, so a placed
    // instance syncs as a scene object would (found in 14.5: the built prefabs had Unknown).
    static void CheckSyncMethods(string path) {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        string wrong = "";
        int count = 0;
        foreach (UdonBehaviour udon in prefab.GetComponentsInChildren<UdonBehaviour>(true)) {
            VRC.SDKBase.Networking.SyncType? expected = ChessBuilderUtil.ExpectedSyncMethod(udon);
            if (expected == null) continue;
            count++;
            if (udon.SyncMethod != expected.Value) wrong += " " + udon.gameObject.name + " " + udon.SyncMethod + ";";
        }
        Check(count > 0 && wrong == "", path + "'s " + count + " behaviours have their scripts' sync methods", wrong);
    }

    // Nothing a prefab uses (scripts, programs, assets) is a test's (step 14: no Assets/Tests/ references).
    static void CheckNoTests(string path) {
        List<string> tests = new List<string>();
        foreach (string dependency in AssetDatabase.GetDependencies(path, true)) {
            if (dependency.StartsWith("Assets/Tests/")) tests.Add(dependency);
        }
        Check(tests.Count == 0, path + " uses nothing under Assets/Tests/", string.Join(", ", tests));
    }

    static void CheckString(string got, string expected, string name) {
        Check(got == expected, name, "got \"" + got + "\", expected \"" + expected + "\"");
    }

    static void CheckIntValue(int got, int expected, string name) {
        Check(got == expected, name, "got " + got + ", expected " + expected);
    }

    static void Check(bool ok, string name, string details) {
        if (ok) {
            passed++;
            Debug.Log("[PrefabTest] PASS: " + name);
        } else {
            failed++;
            Debug.LogError("[PrefabTest] FAIL: " + name + ": " + details);
        }
    }

    static void Summary() {
        string line = "[PrefabTest] done: " + passed + " passed, " + failed + " failed";
        if (failed == 0) Debug.Log(line);
        else Debug.LogError(line);
    }
}
