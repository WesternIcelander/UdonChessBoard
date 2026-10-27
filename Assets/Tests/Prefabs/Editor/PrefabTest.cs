using System.Collections.Generic;
using SiggiChessBoard;
using SiggiUSTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.Udon;

// Step 14's check of the prefabs (docs/prefabs.md), without Play Mode: run it from Tools > Chess Board > Test Prefabs.
// It drops each prefab into a new scene of its own, opened beside the open one and closed unsaved afterwards, and
// checks what it holds. 14.1: Chess Pieces on its own, and the Chess Arbiter with its nested Chess Pieces, the build
// check on it, a change to the Chess Pieces prefab reaching the nested instance but not an unpacked copy. The change
// is undone and the prefab saved again before it ends. 14.2: the Chess Board with its importer, two placed with their
// own board numbers, and the open scene's boards instances of it. 14.3: the Chess Board View with every object it uses
// and no table, its pieces standing on their starting squares, pointed at a placed board, and the open scene's views
// instances of it. 14.4: the Chess Board UI with its buttons (each calling its own UI), status panels, PGN box and sounds, pointed
// at a placed board, and the open scene's UIs instances of it. 14.5: the Game Archive with its own holders, a board
// pointed at it, and the open scene's archive an instance of it, every board on it. 14.6: the Personal Game Archive
// at its defaults, the Game History Wall and Personal History Wall with their entries (the personal one's with delete
// buttons), Export PGN and PGN box, each pointed at a placed archive of its kind, and the open scene's personal archive
// and walls instances of them. 14.7: the Chess Settings Menu with its rows and buttons (each passing its row and
// action to the menu), pointed at a placed arbiter and board, and the open scene's menu an instance of it on its
// arbiter with every board. 14.8: the 2D Chess Board View with its squares, sprites, buttons (each calling its own
// view) and move list, not grabbable, pointed at two placed boards; the Movable 2D Chess Board View, its view a nested
// 2D Chess Board View, its grip and corner pickups, Synced ticked on an instance giving the grip a VRCObjectSync as an
// added component and unticked taking it away, a change to the fixed prefab reaching it (undone); and the open
// scene's 2D views instances of them, Board2DTest on a movable one. 14.9: the Chess Table, its board, view and UI
// nested instances of their prefabs (the view and UI 2 cm above the board, on it), two placed and wired by hand, a
// change to the view prefab reaching it (undone), and the open scene's boards each in a table with its view and UI.
// 14.10: the Chess Language with the package's packs, the I18n sanity check's rule wiring a table's, a wall's, a
// menu's and a 2D view's I18n fields to it beside them and grouped beside a world's own main I18n, ambiguous beside it
// at the root, a pack added to it an override; and the open scene's Chess Language an instance, every field on it.
// Every prefab: nothing it uses is under Assets/Tests/.
// Logs "[PrefabTest] PASS: <check>" or "[PrefabTest] FAIL: <check>: <details>" per check, then a summary line.
public static class PrefabTest {
    const string PieceSetPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Pieces.prefab";
    const string ArbiterPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Arbiter.prefab";
    const string BoardPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Board.prefab";
    const string ViewPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Board View.prefab";
    const string UIPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Board UI.prefab";
    const string ArchivePrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Game Archive.prefab";
    const string PersonalPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Personal Game Archive.prefab";
    const string WallPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Game History Wall.prefab";
    const string PersonalWallPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Personal History Wall.prefab";
    const string MenuPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Settings Menu.prefab";
    const string View2DPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/2D Chess Board View.prefab";
    const string Movable2DPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Movable 2D Chess Board View.prefab";
    const string TablePrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Table.prefab";
    const string LanguagePrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Language.prefab";
    // The Game Archive prefab's synced holders (step 10.1a).
    const int ArchiveHolderCount = 10;

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
        GameObject personalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PersonalPrefabPath);
        Check(personalPrefab != null, "the Personal Game Archive prefab at " + PersonalPrefabPath, "missing");
        GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
        Check(wallPrefab != null, "the Game History Wall prefab at " + WallPrefabPath, "missing");
        GameObject personalWallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PersonalWallPrefabPath);
        Check(personalWallPrefab != null, "the Personal History Wall prefab at " + PersonalWallPrefabPath, "missing");
        GameObject menuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefabPath);
        Check(menuPrefab != null, "the Chess Settings Menu prefab at " + MenuPrefabPath, "missing");
        GameObject view2DPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(View2DPrefabPath);
        Check(view2DPrefab != null, "the 2D Chess Board View prefab at " + View2DPrefabPath, "missing");
        GameObject movable2DPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Movable2DPrefabPath);
        Check(movable2DPrefab != null, "the Movable 2D Chess Board View prefab at " + Movable2DPrefabPath, "missing");
        GameObject tablePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TablePrefabPath);
        Check(tablePrefab != null, "the Chess Table prefab at " + TablePrefabPath, "missing");
        GameObject languagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LanguagePrefabPath);
        Check(languagePrefab != null, "the Chess Language prefab at " + LanguagePrefabPath, "missing");
        if (setPrefab == null || arbiterPrefab == null || boardPrefab == null || viewPrefab == null
            || uiPrefab == null || archivePrefab == null || personalPrefab == null || wallPrefab == null
            || personalWallPrefab == null || menuPrefab == null || view2DPrefab == null || movable2DPrefab == null
            || tablePrefab == null || languagePrefab == null) {
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
            CheckTable(tablePrefab, boardPrefab, viewPrefab, uiPrefab, arbiterPrefab, scene);
            CheckArchive(archivePrefab, arbiterPrefab, scene);
            CheckPersonalArchive(personalPrefab, arbiterPrefab, scene);
            CheckWall(wallPrefab, archivePrefab, false, scene);
            CheckWall(personalWallPrefab, personalPrefab, true, scene);
            CheckMenu(menuPrefab, arbiterPrefab, boardPrefab, scene);
            Check2DView(view2DPrefab, boardPrefab, scene);
            CheckMovable2DView(movable2DPrefab, view2DPrefab, scene);
            CheckLanguage(languagePrefab, tablePrefab, wallPrefab, menuPrefab, view2DPrefab, scene);
        } finally {
            EditorSceneManager.CloseScene(scene, true);
        }
        CheckSceneBoards(boardPrefab);
        CheckSceneViews(viewPrefab);
        CheckSceneUIs(uiPrefab);
        CheckSceneTables();
        CheckSceneArchive(archivePrefab);
        CheckScenePersonalArchive(personalPrefab);
        CheckSceneWalls(wallPrefab, personalWallPrefab);
        CheckSceneMenu(menuPrefab);
        CheckScene2DViews(view2DPrefab, movable2DPrefab);
        CheckSceneLanguage(languagePrefab);
        foreach (string path in new[] {
            PieceSetPrefabPath, ArbiterPrefabPath, BoardPrefabPath, ViewPrefabPath, UIPrefabPath, ArchivePrefabPath,
            PersonalPrefabPath, WallPrefabPath, PersonalWallPrefabPath, MenuPrefabPath,
            View2DPrefabPath, Movable2DPrefabPath, TablePrefabPath, LanguagePrefabPath,
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
        Check(arbiter.pgnEvent == "" && arbiter.pgnSite == "VRChat", "the arbiter's PGN tags are the defaults (13.11)",
            "Event \"" + arbiter.pgnEvent + "\", Site \"" + arbiter.pgnSite + "\"");
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
        // The PGN tags moved to the arbiter (13.11): the saved prefab holds them no more.
        string boardYaml = System.IO.File.ReadAllText(BoardPrefabPath);
        Check(!boardYaml.Contains("pgnEvent:") && !boardYaml.Contains("pgnSite:"),
            "the board prefab is saved without the PGN tags (13.11)", "still in " + BoardPrefabPath);

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
        // The table under it is the world's own (the test scene has placeholders beside its Chess Tables).
        Check(placed.transform.Find("Table") == null, "the view has no table", "it has one");

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

    // ---- 14.9: Chess Table ----

    static void CheckTable(GameObject tablePrefab, GameObject boardPrefab, GameObject viewPrefab, GameObject uiPrefab,
            GameObject arbiterPrefab, Scene scene) {
        List<string> logged = new List<string>();
        Application.LogCallback listener = (text, stack, type) => logged.Add(type + ": " + text);
        Application.logMessageReceived += listener;
        GameObject first;
        GameObject second;
        try {
            first = (GameObject)PrefabUtility.InstantiatePrefab(tablePrefab, scene);
            second = (GameObject)PrefabUtility.InstantiatePrefab(tablePrefab, scene);
        } finally {
            Application.logMessageReceived -= listener;
        }
        Check(logged.Count == 0, "the Chess Table placed logs nothing", string.Join("; ", logged));
        Check(first.GetComponents<Component>().Length == 1, "the table's root holds nothing but its transform",
            first.GetComponents<Component>().Length + " components");
        ChessBoard[] boards = first.GetComponentsInChildren<ChessBoard>(true);
        ChessBoardView[] views = first.GetComponentsInChildren<ChessBoardView>(true);
        ChessBoardUI[] uis = first.GetComponentsInChildren<ChessBoardUI>(true);
        Check(boards.Length == 1 && views.Length == 1 && uis.Length == 1, "the table has one board, view and UI",
            boards.Length + ", " + views.Length + ", " + uis.Length);
        if (boards.Length != 1 || views.Length != 1 || uis.Length != 1) return;
        ChessBoard board = boards[0];
        ChessBoardView view = views[0];
        ChessBoardUI ui = uis[0];
        string nested = NestedProblem(board.gameObject, first, boardPrefab, Vector3.zero)
            + NestedProblem(view.gameObject, first, viewPrefab, new Vector3(0, 0.02f, 0))
            + NestedProblem(ui.gameObject, first, uiPrefab, new Vector3(0, 0.02f, 0));
        Check(nested == "", "the table's board (at its root), view and UI (2 cm up, the board's thickness) are its "
            + "children, nested instances of their prefabs", nested);
        Check(view.board == board && ui.board == board, "the table's view and UI are on its board", "they aren't");
        Check(board.arbiter == null, "the table's arbiter is left to set", "on " + board.arbiter);
        CheckIntValue(board.boardNumber, 0, "the table's board number is 0");
        CheckIntValue(board.listeners != null ? board.listeners.Length : -1, 0, "the table's board has no listeners");
        Check(board.importer != null && board.importer.transform.parent == board.transform,
            "the table's board has its own importer", board.importer == null ? "empty" : "on " + board.importer.name);
        Check(view.i18n == null && ui.i18n == null, "the table's view's and UI's I18n are left for the sanity check",
            "set");
        Check(view.LocalSpaceProblem() == "", "the table's view's objects are in its local space",
            view.LocalSpaceProblem());
        ChessBoard prefabBoard = tablePrefab.GetComponentInChildren<ChessBoard>(true);
        Check(prefabBoard != null && tablePrefab.GetComponentInChildren<ChessBoardView>(true).board == prefabBoard
            && tablePrefab.GetComponentInChildren<ChessBoardUI>(true).board == prefabBoard,
            "the table prefab's view and UI are on its own board", "they aren't");

        // Its two fields set by hand, as a world creator would: the first table's arbiter, the second's number 1 and
        // the same arbiter. Each keeps its own board, view and UI.
        ChessArbiter arbiter = ((GameObject)PrefabUtility.InstantiatePrefab(arbiterPrefab, scene))
            .GetComponent<ChessArbiter>();
        ChessBoard other = second.GetComponentInChildren<ChessBoard>(true);
        SerializedObject fields = new SerializedObject(board);
        fields.FindProperty("arbiter").objectReferenceValue = arbiter;
        fields.ApplyModifiedProperties();
        fields = new SerializedObject(other);
        fields.FindProperty("arbiter").objectReferenceValue = arbiter;
        fields.FindProperty("boardNumber").intValue = 1;
        fields.ApplyModifiedProperties();
        Check(board.arbiter == arbiter && other.arbiter == arbiter && board.boardNumber == 0 && other.boardNumber == 1,
            "two placed tables wired by hand: both on the arbiter, numbers 0 and 1",
            board.boardNumber + ", " + other.boardNumber);
        Check(second.GetComponentInChildren<ChessBoardView>(true).board == other
            && second.GetComponentInChildren<ChessBoardUI>(true).board == other && other.importer != board.importer,
            "two placed tables: each view and UI on its own table's board, each board with its own importer",
            "shared");
        Check(prefabBoard != null && prefabBoard.arbiter == null && prefabBoard.boardNumber == 0,
            "two placed tables: the prefab's board is unchanged", "it changed");

        // A change to the Chess Board View prefab (its player blocker moved up, then back) reaches the table prefab's
        // view and a placed table's.
        const string Blocker = "Player Blocker";
        Vector3 shift = new Vector3(0, 0.1f, 0);
        GameObject contents = PrefabUtility.LoadPrefabContents(ViewPrefabPath);
        Transform prefabBlocker = contents.transform.Find(Blocker);
        Vector3 original = prefabBlocker != null ? prefabBlocker.localPosition : Vector3.zero;
        bool changed = false;
        try {
            Check(prefabBlocker != null, "the Chess Board View prefab has its " + Blocker, "none");
            if (prefabBlocker != null) {
                prefabBlocker.localPosition = original + shift;
                PrefabUtility.SaveAsPrefabAsset(contents, ViewPrefabPath);
                changed = true;
                ChessBoardView reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(TablePrefabPath)
                    .GetComponentInChildren<ChessBoardView>(true);
                Transform nestedBlocker = reloaded != null ? reloaded.transform.Find(Blocker) : null;
                Transform placedBlocker = view.transform.Find(Blocker);
                Check(nestedBlocker != null && nestedBlocker.localPosition == original + shift,
                    "a change to the Chess Board View prefab reaches the table prefab's view",
                    nestedBlocker == null ? "no blocker" : nestedBlocker.localPosition.ToString("F2"));
                Check(placedBlocker != null && placedBlocker.localPosition == original + shift,
                    "a change to the Chess Board View prefab reaches a placed table's view",
                    placedBlocker == null ? "no blocker" : placedBlocker.localPosition.ToString("F2"));
            }
        } finally {
            if (changed) {
                prefabBlocker.localPosition = original;
                PrefabUtility.SaveAsPrefabAsset(contents, ViewPrefabPath);
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }
        Transform restored = viewPrefab.transform.Find(Blocker);
        Check(restored != null && restored.localPosition == original, "the Chess Board View prefab is put back",
            restored == null ? "no blocker" : restored.localPosition.ToString("F2"));
        Object.DestroyImmediate(first);
        Object.DestroyImmediate(second);
        Object.DestroyImmediate(arbiter.gameObject);
    }

    // What's wrong with part as table's nested instance of prefab at position (local, unturned, unscaled), or "".
    static string NestedProblem(GameObject part, GameObject table, GameObject prefab, Vector3 position) {
        string name = " " + part.name;
        if (part.transform.parent != table.transform) return name + " isn't the table's child;";
        if (!PrefabUtility.IsAnyPrefabInstanceRoot(part)
            || PrefabUtility.GetCorrespondingObjectFromOriginalSource(part) != prefab) {
            return name + " isn't a nested " + prefab.name + ";";
        }
        if ((part.transform.localPosition - position).sqrMagnitude > 1e-8f
            || part.transform.localRotation != Quaternion.identity || part.transform.localScale != Vector3.one) {
            return name + " at " + part.transform.localPosition.ToString("F3") + ";";
        }
        return "";
    }

    // The Chess Table instance board is in, or null.
    static GameObject TableOf(ChessBoard board) {
        GameObject outer = PrefabUtility.GetOutermostPrefabInstanceRoot(board.gameObject);
        return outer != null && outer != board.gameObject
            && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(outer) == TablePrefabPath ? outer : null;
    }

    // The open scene's boards (the test scene's) are each in a Chess Table instance with the view and UI on it.
    static void CheckSceneTables() {
        ChessBoard[] boards = Object.FindObjectsOfType<ChessBoard>(true);
        foreach (ChessBoard board in boards) {
            // Not the inactive spare board SettingsTest numbers 2 (14.11, SpareBoardBuilder), which has no table.
            if (!board.gameObject.activeInHierarchy) continue;
            string name = "the scene's " + board.gameObject.name + " (board " + board.boardNumber + ")";
            GameObject table = TableOf(board);
            Check(table != null, name + " is in a Chess Table instance", "it isn't");
            if (table == null) continue;
            ChessBoardView[] views = table.GetComponentsInChildren<ChessBoardView>(true);
            ChessBoardUI[] uis = table.GetComponentsInChildren<ChessBoardUI>(true);
            Check(views.Length == 1 && views[0].board == board && uis.Length == 1 && uis[0].board == board,
                name + "'s table holds its view and UI, on it", views.Length + " view(s), " + uis.Length + " UI(s)");
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

    // ---- 14.6: Personal Game Archive ----

    static void CheckPersonalArchive(GameObject personalPrefab, GameObject arbiterPrefab, Scene scene) {
        List<string> logged = new List<string>();
        Application.LogCallback listener = (text, stack, type) => logged.Add(type + ": " + text);
        Application.logMessageReceived += listener;
        GameObject placed;
        try {
            placed = (GameObject)PrefabUtility.InstantiatePrefab(personalPrefab, scene);
        } finally {
            Application.logMessageReceived -= listener;
        }
        Check(logged.Count == 0, "the Personal Game Archive placed logs nothing", string.Join("; ", logged));
        PersonalGameArchive archive = placed.GetComponent<PersonalGameArchive>();
        Check(archive != null, "the Personal Game Archive has a PersonalGameArchive", "none");
        if (archive == null) return;
        CheckIntValue(archive.capacity, PersonalGameArchive.DefaultCapacity,
            "the personal archive's capacity is the default");
        CheckString(archive.keyPrefix, PersonalGameArchive.DefaultKeyPrefix,
            "the personal archive's key prefix is the default");
        Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(archive).SyncMethod == VRC.SDKBase.Networking.SyncType.None,
            "the personal archive syncs nothing", "it does");
        CheckIntValue(placed.transform.childCount, 0, "the personal archive has no children");

        // A placed arbiter listing it by hand, as a world creator would set it in the Inspector.
        GameObject arbiterObject = (GameObject)PrefabUtility.InstantiatePrefab(arbiterPrefab, scene);
        ChessArbiter arbiter = arbiterObject.GetComponent<ChessArbiter>();
        SerializedObject fields = new SerializedObject(arbiter);
        SerializedProperty list = fields.FindProperty("archives");
        list.arraySize = 1;
        list.GetArrayElementAtIndex(0).objectReferenceValue = archive;
        fields.ApplyModifiedProperties();
        Check(arbiter.archives.Length == 1 && arbiter.archives[0] == archive,
            "an arbiter listing the placed personal archive", arbiter.archives.Length + " entries");
        CheckIntValue(arbiterPrefab.GetComponent<ChessArbiter>().archives.Length, 0,
            "an arbiter listing the personal archive: the arbiter prefab still lists none");
        Object.DestroyImmediate(placed);
        Object.DestroyImmediate(arbiterObject);
    }

    // The open scene's one personal archive (the test scene's) is an instance of the Personal Game Archive prefab.
    static void CheckScenePersonalArchive(GameObject personalPrefab) {
        PersonalGameArchive[] archives = Object.FindObjectsOfType<PersonalGameArchive>(true);
        CheckIntValue(archives.Length, 1, "the open scene has one personal archive");
        if (archives.Length == 0) return;
        Check(PrefabUtility.IsAnyPrefabInstanceRoot(archives[0].gameObject)
            && PrefabUtility.GetCorrespondingObjectFromOriginalSource(archives[0].gameObject) == personalPrefab,
            "the scene's " + archives[0].gameObject.name + " is a Personal Game Archive instance", "it isn't");
    }

    // ---- 14.6: Game History Wall, Personal History Wall ----

    static void CheckWall(GameObject wallPrefab, GameObject archivePrefab, bool personal, Scene scene) {
        string kind = personal ? "the Personal History Wall" : "the Game History Wall";
        List<string> logged = new List<string>();
        Application.LogCallback listener = (text, stack, type) => logged.Add(type + ": " + text);
        Application.logMessageReceived += listener;
        GameObject placed;
        try {
            placed = (GameObject)PrefabUtility.InstantiatePrefab(wallPrefab, scene);
        } finally {
            Application.logMessageReceived -= listener;
        }
        Check(logged.Count == 0, kind + " placed logs nothing", string.Join("; ", logged));
        GameHistoryWall wall = placed.GetComponent<GameHistoryWall>();
        Check(wall != null, kind + " has a GameHistoryWall", "none");
        if (wall == null) return;
        Check(wall.archive == null, kind + "'s archive is left to set", "on " + wall.archive);
        Check(wall.i18n == null, kind + "'s I18n is left for the sanity check", "on " + wall.i18n);
        CheckString(wall.titleKey, personal ? "wall_title_personal" : "wall_title", kind + "'s title key");
        Canvas canvas = placed.GetComponent<Canvas>();
        BoxCollider box = placed.GetComponent<BoxCollider>();
        Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace
            && placed.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null && box != null && box.isTrigger,
            kind + " is a world-space canvas taking the pointer through a trigger", "it isn't");
        Check(wall.title != null && wall.title.transform.IsChildOf(placed.transform), kind + "'s title", "missing");

        int expected = personal ? PersonalGameArchive.DefaultCapacity
            : ArchiveHolderCount * GameArchiveHolder.Capacity;
        CheckIntValue(wall.rowTargets != null ? wall.rowTargets.Length : -1, expected,
            kind + " has an entry per game of its archive's default capacity");
        string entries = "";
        int count = wall.rowTargets != null ? wall.rowTargets.Length : 0;
        if (wall.rows == null || wall.rows.Length != count
            || wall.deleteButtons == null || wall.deleteButtons.Length != (personal ? count : 0)
            || wall.deleteLabels == null || wall.deleteLabels.Length != (personal ? count : 0)) {
            entries += " the arrays' lengths differ;";
            count = 0;
        }
        for (int i = 0; i < count; i++) {
            GameHistoryRow row = wall.rowTargets[i];
            if (row == null || row.wall != wall || row.index != i || row.transform.parent != wall.content) {
                entries += " " + i + " missing or not the wall's;";
                continue;
            }
            UdonBehaviour rowUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(row);
            if (PressEvent(row.GetComponent<UnityEngine.UI.Button>(), rowUdon) != "_Press") {
                entries += " " + i + "'s press;";
            }
            if (wall.rows[i] == null || wall.rows[i].transform.parent != row.transform) entries += " " + i + "'s text;";
            if (!personal) continue;
            GameObject delete = wall.deleteButtons[i];
            if (delete == null || delete.transform.parent != row.transform
                || PressEvent(delete.GetComponent<UnityEngine.UI.Button>(), rowUdon) != "_PressDelete"
                || wall.deleteLabels[i] == null || !wall.deleteLabels[i].transform.IsChildOf(delete.transform)) {
                entries += " " + i + "'s delete button;";
            }
        }
        Check(count > 0 && entries == "", kind + "'s " + count + " entries, each picking its game"
            + (personal ? ", with its delete button" : ", without delete buttons"), entries);
        Check(wall.scroll != null && wall.viewport != null && wall.content != null && wall.scrollbar != null
            && wall.content.IsChildOf(wall.viewport) && wall.scroll.content == wall.content
            && wall.scroll.verticalScrollbar == wall.scrollbar, kind + "'s scroll view", "missing");

        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(wall);
        Check(wall.exportButton != null && wall.exportButton.transform.IsChildOf(placed.transform)
            && !wall.exportButton.activeSelf && wall.exportLabel != null,
            kind + "'s Export PGN button (hidden)", "missing");
        if (wall.exportButton != null) {
            CheckString(PressEvent(wall.exportButton.GetComponent<UnityEngine.UI.Button>(), udon), "_PressExport",
                kind + "'s Export PGN calls the wall's own event");
        }
        Check(wall.pgnBox != null && wall.pgnBox.transform.IsChildOf(placed.transform) && !wall.pgnBox.activeSelf
            && wall.pgnField != null && wall.pgnField.readOnly && wall.pgnHint != null,
            kind + "'s PGN box (hidden, read only)", "missing");

        // Pointed at a placed archive of its kind by hand, as a world creator would set it in the Inspector.
        GameObject archiveObject = (GameObject)PrefabUtility.InstantiatePrefab(archivePrefab, scene);
        GameArchive archive = archiveObject.GetComponent<GameArchive>();
        SerializedObject fields = new SerializedObject(wall);
        fields.FindProperty("archive").objectReferenceValue = archive;
        fields.ApplyModifiedProperties();
        Check(wall.archive == archive, kind + " pointed at a placed archive", "on " + wall.archive);
        Check(wallPrefab.GetComponent<GameHistoryWall>().archive == null,
            kind + " pointed at an archive: the prefab still points at none", "it doesn't");
        Object.DestroyImmediate(placed);
        Object.DestroyImmediate(archiveObject);
    }

    // The open scene's walls (the test scene's) are instances of their kind's prefab: one on the instance's archive
    // and one on the personal one, each with its entries its own.
    static void CheckSceneWalls(GameObject wallPrefab, GameObject personalWallPrefab) {
        GameHistoryWall[] walls = Object.FindObjectsOfType<GameHistoryWall>(true);
        int instanceWalls = 0;
        int personalWalls = 0;
        foreach (GameHistoryWall wall in walls) {
            string name = "the scene's " + wall.gameObject.name;
            bool personal = wall.archive is PersonalGameArchive;
            if (personal) personalWalls++;
            else if (wall.archive is InstanceGameArchive) instanceWalls++;
            Check(wall.archive != null, name + " has its archive", "empty");
            GameObject prefab = personal ? personalWallPrefab : wallPrefab;
            Check(PrefabUtility.IsAnyPrefabInstanceRoot(wall.gameObject)
                && PrefabUtility.GetCorrespondingObjectFromOriginalSource(wall.gameObject) == prefab,
                name + " is a " + prefab.name + " instance", "it isn't");
            int own = 0;
            foreach (GameHistoryRow row in wall.rowTargets) {
                if (row != null && row.wall == wall && row.transform.IsChildOf(wall.transform)) own++;
            }
            CheckIntValue(own, wall.rowTargets.Length, name + "'s entries are its own");
        }
        CheckIntValue(instanceWalls, 1, "the open scene has one wall on the instance's archive");
        CheckIntValue(personalWalls, 1, "the open scene has one wall on the personal archive");
    }

    // ---- 14.7: Chess Settings Menu ----

    static void CheckMenu(GameObject menuPrefab, GameObject arbiterPrefab, GameObject boardPrefab, Scene scene) {
        List<string> logged = new List<string>();
        Application.LogCallback listener = (text, stack, type) => logged.Add(type + ": " + text);
        Application.logMessageReceived += listener;
        GameObject placed;
        try {
            placed = (GameObject)PrefabUtility.InstantiatePrefab(menuPrefab, scene);
        } finally {
            Application.logMessageReceived -= listener;
        }
        Check(logged.Count == 0, "the Chess Settings Menu placed logs nothing", string.Join("; ", logged));
        ChessSettingsMenu menu = placed.GetComponent<ChessSettingsMenu>();
        Check(menu != null, "the Chess Settings Menu has a ChessSettingsMenu", "none");
        if (menu == null) return;
        Check(menu.arbiter == null, "the menu's arbiter is left to set", "on " + menu.arbiter);
        Check(menu.boards != null && menu.boards.Length == 0, "the menu's boards are left to set",
            menu.boards == null ? "null" : menu.boards.Length + " boards");
        Check(menu.i18n == null, "the menu's I18n is left for the sanity check", "on " + menu.i18n);
        Canvas canvas = placed.GetComponent<Canvas>();
        BoxCollider box = placed.GetComponent<BoxCollider>();
        Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace
            && placed.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null && box != null && box.isTrigger,
            "the menu is a world-space canvas taking the pointer through a trigger", "it isn't");
        Check(menu.title != null && menu.notice != null && menu.title.transform.IsChildOf(placed.transform)
            && menu.notice.transform.IsChildOf(placed.transform), "the menu's title and notice", "missing");
        Check(menu.controls != null && menu.controls.transform.parent == placed.transform
            && menu.notice != null && !menu.notice.transform.IsChildOf(menu.controls.transform),
            "the menu's controls, its own child, the notice outside them", "missing");
        bool under = menu.controls != null && menu.scopeLabel != null && menu.scopeTitle != null
            && menu.resnapshotTitle != null && menu.resnapshotLabel != null
            && menu.scopeLabel.transform.IsChildOf(menu.controls.transform)
            && menu.scopeTitle.transform.IsChildOf(menu.controls.transform)
            && menu.resnapshotTitle.transform.IsChildOf(menu.controls.transform)
            && menu.resnapshotLabel.transform.IsChildOf(menu.controls.transform);
        Check(under, "the menu's scope and re-snapshot rows among the controls", "missing");
        string rows = "";
        if (menu.values == null || menu.values.Length != ChessSettingsMenu.ItemCount || menu.itemLabels == null
            || menu.itemLabels.Length != ChessSettingsMenu.ItemCount) {
            rows = " the arrays' lengths differ";
        } else {
            for (int item = 0; item < ChessSettingsMenu.ItemCount; item++) {
                if (menu.values[item] == null || menu.itemLabels[item] == null || menu.controls == null
                    || !menu.values[item].transform.IsChildOf(menu.controls.transform)
                    || !menu.itemLabels[item].transform.IsChildOf(menu.controls.transform)) {
                    rows += " " + item + ";";
                }
            }
        }
        Check(rows == "", "the menu's " + ChessSettingsMenu.ItemCount + " rows, each with its label and value", rows);

        // Three buttons per row and the scope row, and Apply now; each calling its own ChessSettingsButton, which
        // passes its row and action to the placed menu.
        int expectedButtons = 3 * (ChessSettingsMenu.ItemCount + 1) + 1;
        int count = menu.buttons != null ? menu.buttons.Length : 0;
        string buttons = "";
        if (menu.buttonImages == null || menu.buttonImages.Length != count || menu.buttonItems == null
            || menu.buttonItems.Length != count || menu.buttonActions == null || menu.buttonActions.Length != count
            || menu.buttonLabels == null || menu.buttonLabels.Length != count) {
            buttons += " the arrays' lengths differ;";
            count = 0;
        }
        bool resnapshot = false;
        for (int i = 0; i < count; i++) {
            GameObject button = menu.buttons[i];
            if (button == null || menu.controls == null || !button.transform.IsChildOf(menu.controls.transform)) {
                buttons += " " + i + " missing;";
                continue;
            }
            ChessSettingsButton target = button.GetComponent<ChessSettingsButton>();
            if (target == null || target.menu != menu || target.item != menu.buttonItems[i]
                || target.action != menu.buttonActions[i]) {
                buttons += " " + button.name + "'s ChessSettingsButton;";
                continue;
            }
            if (target.item == ChessSettingsMenu.ItemResnapshot && target.action == ChessSettingsMenu.ActionOn) {
                resnapshot = true;
            }
            string pressed = PressEvent(button.GetComponent<UnityEngine.UI.Button>(),
                UdonSharpEditorUtility.GetBackingUdonBehaviour(target));
            if (pressed != "_Press") buttons += " " + button.name + " calls " + pressed + ";";
            if (menu.buttonImages[i] == null || menu.buttonImages[i].gameObject != button) {
                buttons += " " + button.name + "'s image;";
            }
            if (menu.buttonLabels[i] == null || !menu.buttonLabels[i].transform.IsChildOf(button.transform)) {
                buttons += " " + button.name + "'s label;";
            }
        }
        CheckIntValue(count, expectedButtons, "the menu has three buttons per row, the scope row's and Apply now");
        Check(count > 0 && buttons == "", "the menu's " + count + " buttons, each passing its row and action to the "
            + "placed menu", buttons);
        Check(resnapshot, "the menu's Apply now button", "missing");

        // Pointed at a placed arbiter and board by hand, as a world creator would set them in the Inspector.
        GameObject arbiterObject = (GameObject)PrefabUtility.InstantiatePrefab(arbiterPrefab, scene);
        GameObject boardObject = (GameObject)PrefabUtility.InstantiatePrefab(boardPrefab, scene);
        ChessArbiter arbiter = arbiterObject.GetComponent<ChessArbiter>();
        ChessBoard board = boardObject.GetComponent<ChessBoard>();
        SerializedObject fields = new SerializedObject(menu);
        fields.FindProperty("arbiter").objectReferenceValue = arbiter;
        SerializedProperty boards = fields.FindProperty("boards");
        boards.arraySize = 1;
        boards.GetArrayElementAtIndex(0).objectReferenceValue = board;
        fields.ApplyModifiedProperties();
        Check(menu.arbiter == arbiter && menu.boards.Length == 1 && menu.boards[0] == board,
            "the menu pointed at a placed arbiter and board", "it isn't");
        ChessSettingsMenu prefabMenu = menuPrefab.GetComponent<ChessSettingsMenu>();
        Check(prefabMenu.arbiter == null && prefabMenu.boards.Length == 0,
            "the menu pointed at an arbiter and board: the prefab still points at none", "it doesn't");
        Object.DestroyImmediate(placed);
        Object.DestroyImmediate(arbiterObject);
        Object.DestroyImmediate(boardObject);
    }

    // The open scene's settings menu (the test scene's) is an instance of the prefab, on the scene's arbiter with
    // every board on it, its buttons its own.
    static void CheckSceneMenu(GameObject menuPrefab) {
        ChessSettingsMenu[] menus = Object.FindObjectsOfType<ChessSettingsMenu>(true);
        CheckIntValue(menus.Length, 1, "the open scene has one settings menu");
        if (menus.Length != 1) return;
        ChessSettingsMenu menu = menus[0];
        Check(PrefabUtility.IsAnyPrefabInstanceRoot(menu.gameObject)
            && PrefabUtility.GetCorrespondingObjectFromOriginalSource(menu.gameObject) == menuPrefab,
            "the scene's settings menu is a Chess Settings Menu instance", "it isn't");
        ChessArbiter[] arbiters = Object.FindObjectsOfType<ChessArbiter>(true);
        Check(arbiters.Length == 1 && menu.arbiter == arbiters[0], "the scene's settings menu is on its arbiter",
            "on " + menu.arbiter);
        int onArbiter = 0;
        // The inactive spare board (14.11) isn't listed: it isn't played.
        foreach (ChessBoard board in Object.FindObjectsOfType<ChessBoard>(true)) {
            if (board.arbiter == menu.arbiter && board.gameObject.activeInHierarchy) onArbiter++;
        }
        bool every = menu.boards.Length == onArbiter;
        foreach (ChessBoard board in menu.boards) {
            if (board == null || board.arbiter != menu.arbiter) every = false;
        }
        Check(onArbiter > 0 && every, "the scene's settings menu lists every board on the arbiter",
            menu.boards.Length + " listed, " + onArbiter + " on the arbiter");
        int own = 0;
        foreach (GameObject button in menu.buttons) {
            ChessSettingsButton target = button != null ? button.GetComponent<ChessSettingsButton>() : null;
            if (target != null && target.menu == menu && button.transform.IsChildOf(menu.transform)) own++;
        }
        CheckIntValue(own, menu.buttons.Length, "the scene's settings menu's buttons are its own");
    }

    // ---- 14.8: 2D Chess Board View, Movable 2D Chess Board View ----

    static void Check2DView(GameObject viewPrefab, GameObject boardPrefab, Scene scene) {
        List<string> logged = new List<string>();
        Application.LogCallback listener = (text, stack, type) => logged.Add(type + ": " + text);
        Application.logMessageReceived += listener;
        GameObject placed;
        try {
            placed = (GameObject)PrefabUtility.InstantiatePrefab(viewPrefab, scene);
        } finally {
            Application.logMessageReceived -= listener;
        }
        Check(logged.Count == 0, "the 2D Chess Board View placed logs nothing", string.Join("; ", logged));
        ChessBoard2DView view = placed.GetComponent<ChessBoard2DView>();
        Check(view != null, "the 2D Chess Board View has a ChessBoard2DView", "none");
        if (view == null) return;
        Check(view.boards != null && view.boards.Length == 0, "the 2D view's boards are left to set",
            view.boards == null ? "null" : view.boards.Length + " boards");
        Check(view.lockedBoard == null, "the 2D view isn't locked", "on " + view.lockedBoard);
        Check(!view.synced, "the 2D view's Synced is off, for the world creator to choose", "on");
        Check(view.i18n == null, "the 2D view's I18n is left for the sanity check", "on " + view.i18n);
        Canvas canvas = placed.GetComponent<Canvas>();
        BoxCollider box = placed.GetComponent<BoxCollider>();
        Check(canvas != null && canvas.renderMode == RenderMode.WorldSpace
            && placed.GetComponent<VRC.SDK3.Components.VRCUiShape>() != null && box != null && box.isTrigger,
            "the 2D view is a world-space canvas taking the pointer through a trigger", "it isn't");
        Check(placed.GetComponentsInChildren<VRC.SDK3.Components.VRCPickup>(true).Length == 0
            && placed.GetComponentsInChildren<Rigidbody>(true).Length == 0
            && placed.GetComponentsInChildren<ChessBoard2DViewHolder>(true).Length == 0,
            "the fixed 2D view has no pickup, rigidbody or holder: it can't be grabbed", "it has one");

        string squares = "";
        if (view.squareImages == null || view.squareImages.Length != 64 || view.pieceImages == null
            || view.pieceImages.Length != 64) {
            squares = " the arrays aren't 64 long";
        } else {
            for (int square = 0; square < 64; square++) {
                if (view.squareImages[square] == null || view.pieceImages[square] == null
                    || !view.squareImages[square].transform.IsChildOf(placed.transform)
                    || view.pieceImages[square].transform.parent != view.squareImages[square].transform) {
                    squares += " " + square + ";";
                }
            }
        }
        Check(squares == "", "the 2D view's 64 squares, each with its piece image", squares);
        HashSet<Sprite> sprites = new HashSet<Sprite>();
        bool sixEach = view.whiteSprites != null && view.whiteSprites.Length == 6 && view.blackSprites != null
            && view.blackSprites.Length == 6;
        if (sixEach) {
            foreach (Sprite sprite in view.whiteSprites) if (sprite != null) sprites.Add(sprite);
            foreach (Sprite sprite in view.blackSprites) if (sprite != null) sprites.Add(sprite);
        }
        Check(sixEach && sprites.Count == 12, "the 2D view's 6 sprites per colour, all 12 different",
            sprites.Count + " different");
        Check(view.boardLabel != null && view.playersLabel != null && view.statusLabel != null
            && view.boardLabel.transform.IsChildOf(placed.transform)
            && view.playersLabel.transform.IsChildOf(placed.transform)
            && view.statusLabel.transform.IsChildOf(placed.transform), "the 2D view's board label and texts", "missing");

        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(view);
        CheckString(PressEvent(view.leftButton, udon), nameof(ChessBoard2DView.PressLeft),
            "the 2D view's left button calls its own view");
        CheckString(PressEvent(view.rightButton, udon), nameof(ChessBoard2DView.PressRight),
            "the 2D view's right button calls its own view");
        string[] stepEvents = {
            nameof(ChessBoard2DView.PressFirst), nameof(ChessBoard2DView.PressBack),
            nameof(ChessBoard2DView.PressForward), nameof(ChessBoard2DView.PressLast),
        };
        string steps = "";
        if (view.stepButtons == null || view.stepButtons.Length != 4 || view.stepLabels == null
            || view.stepLabels.Length != 4) {
            steps = " the arrays aren't 4 long";
        } else {
            for (int i = 0; i < 4; i++) {
                string pressed = PressEvent(view.stepButtons[i], udon);
                if (pressed != stepEvents[i]) steps += " " + i + " calls " + pressed + ";";
                if (view.stepLabels[i] == null || view.stepButtons[i] == null
                    || !view.stepLabels[i].transform.IsChildOf(view.stepButtons[i].transform)) {
                    steps += " " + i + "'s label;";
                }
            }
        }
        Check(steps == "", "the 2D view's four step buttons, each calling its own view, with their labels", steps);

        // The move list (12.15): its scroll rect calling the view, and its pooled cells, each passing its index to the
        // placed view through its own ChessMoveListCell.
        Check(view.moveList != null && view.moveListViewport != null && view.moveListContent != null
            && view.moveListRows != null && view.moveList.content == view.moveListContent
            && view.moveList.viewport == view.moveListViewport && view.moveListRows.IsChildOf(view.moveListViewport),
            "the 2D view's move list, its viewport, content and rows", "missing");
        if (view.moveList != null) {
            UnityEngine.UI.ScrollRect.ScrollRectEvent scrolled = view.moveList.onValueChanged;
            Check(scrolled.GetPersistentEventCount() == 1 && scrolled.GetPersistentTarget(0) == udon
                && new SerializedObject(view.moveList).FindProperty(
                    "m_OnValueChanged.m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_StringArgument")
                    .stringValue == nameof(ChessBoard2DView._OnMoveListScrolled),
                "the 2D view's move list scrolling calls its own view", "it doesn't");
        }
        int cells = view.moveCells != null ? view.moveCells.Length : 0;
        string cellProblems = "";
        if (view.moveNumbers == null || 2 * view.moveNumbers.Length != cells || view.moveCellImages == null
            || view.moveCellImages.Length != cells || view.moveCellLabels == null
            || view.moveCellLabels.Length != cells) {
            cellProblems = " the arrays' lengths differ;";
            cells = 0;
        }
        for (int i = 0; i < cells; i++) {
            UnityEngine.UI.Button button = view.moveCells[i];
            ChessMoveListCell cell = button != null ? button.GetComponent<ChessMoveListCell>() : null;
            if (cell == null || cell.view != view || cell.cell != i || view.moveListRows == null
                || !button.transform.IsChildOf(view.moveListRows)) {
                cellProblems += " " + i + " missing or not the view's;";
                continue;
            }
            string pressed = PressEvent(button, UdonSharpEditorUtility.GetBackingUdonBehaviour(cell));
            if (pressed != nameof(ChessMoveListCell._Press)) cellProblems += " " + i + " calls " + pressed + ";";
            if (view.moveCellImages[i] == null || view.moveCellImages[i].gameObject != button.gameObject
                || view.moveCellLabels[i] == null || !view.moveCellLabels[i].transform.IsChildOf(button.transform)) {
                cellProblems += " " + i + "'s image or label;";
            }
        }
        Check(cells > 0 && cellProblems == "", "the 2D view's " + cells + " move list cells, each passing its index to "
            + "the placed view", cellProblems);

        // Pointed at two placed boards by hand, as a world creator would set them in the Inspector.
        GameObject first = (GameObject)PrefabUtility.InstantiatePrefab(boardPrefab, scene);
        GameObject second = (GameObject)PrefabUtility.InstantiatePrefab(boardPrefab, scene);
        SerializedObject fields = new SerializedObject(view);
        SerializedProperty boards = fields.FindProperty("boards");
        boards.arraySize = 2;
        boards.GetArrayElementAtIndex(0).objectReferenceValue = first.GetComponent<ChessBoard>();
        boards.GetArrayElementAtIndex(1).objectReferenceValue = second.GetComponent<ChessBoard>();
        fields.FindProperty("synced").boolValue = true;
        fields.ApplyModifiedProperties();
        Check(view.boards.Length == 2 && view.boards[0] == first.GetComponent<ChessBoard>()
            && view.boards[1] == second.GetComponent<ChessBoard>() && view.synced,
            "the 2D view pointed at two placed boards and synced", "it isn't");
        ChessBoard2DView prefabView = viewPrefab.GetComponent<ChessBoard2DView>();
        Check(prefabView.boards.Length == 0 && !prefabView.synced,
            "the 2D view pointed at boards and synced: the prefab still has none and isn't synced", "it changed");
        Object.DestroyImmediate(placed);
        Object.DestroyImmediate(first);
        Object.DestroyImmediate(second);
    }

    static void CheckMovable2DView(GameObject movablePrefab, GameObject viewPrefab, Scene scene) {
        List<string> logged = new List<string>();
        Application.LogCallback listener = (text, stack, type) => logged.Add(type + ": " + text);
        Application.logMessageReceived += listener;
        GameObject placed;
        try {
            placed = (GameObject)PrefabUtility.InstantiatePrefab(movablePrefab, scene);
        } finally {
            Application.logMessageReceived -= listener;
        }
        Check(logged.Count == 0, "the Movable 2D Chess Board View placed logs nothing", string.Join("; ", logged));
        ChessBoard2DViewHolder holder = placed.GetComponent<ChessBoard2DViewHolder>();
        Check(holder != null, "the Movable 2D Chess Board View has a ChessBoard2DViewHolder", "none");
        if (holder == null) return;
        ChessBoard2DView view = holder.view;
        bool nested = view != null && view.transform.parent == placed.transform
            && PrefabUtility.IsAnyPrefabInstanceRoot(view.gameObject)
            && PrefabUtility.GetCorrespondingObjectFromOriginalSource(view.gameObject) == viewPrefab;
        Check(nested, "the holder's view is its child, a nested 2D Chess Board View", "it isn't");
        ChessBoard2DViewHolder prefabHolder = movablePrefab.GetComponent<ChessBoard2DViewHolder>();
        Check(prefabHolder != null && prefabHolder.view != null
            && PrefabUtility.GetCorrespondingObjectFromSource(prefabHolder.view.gameObject) == viewPrefab,
            "the Movable 2D Chess Board View prefab nests the 2D Chess Board View prefab", "it doesn't");
        if (view == null) return;
        Check(view.boards.Length == 0 && view.lockedBoard == null && !view.synced && view.i18n == null,
            "the movable view's boards are left to set, not locked or synced, its I18n left for the sanity check",
            "it isn't");
        Check(holder.i18n == null, "the holder's I18n is left for the sanity check", "on " + holder.i18n);
        Check(holder.movable && holder.minSize == ChessBoard2DViewHolder.DefaultMinSize && holder.maxSize == 3,
            "the holder is movable, sizes 0.25 to 3", holder.movable + ", " + holder.minSize + " to " + holder.maxSize);
        Check(view.GetComponentsInChildren<VRC.SDK3.Components.VRCPickup>(true).Length == 0,
            "the movable view's canvas has no pickup of its own", "it has one");
        string handles = "";
        foreach (Transform handle in new[] { holder.grip, holder.corner }) {
            if (handle == null || handle.parent != placed.transform) {
                handles += " a handle missing;";
                continue;
            }
            VRC.SDK3.Components.VRCPickup pickup = handle.GetComponent<VRC.SDK3.Components.VRCPickup>();
            Rigidbody body = handle.GetComponent<Rigidbody>();
            BoxCollider collider = handle.GetComponent<BoxCollider>();
            if (pickup == null || body == null || !body.isKinematic || body.useGravity || collider == null
                || !collider.isTrigger || handle.gameObject.layer != LayerMask.NameToLayer("Pickup")) {
                handles += " " + handle.name + "'s pickup;";
            }
            if (handle.GetComponent<UdonBehaviour>() != null) handles += " " + handle.name + " has a behaviour;";
        }
        Check(handles == "" && holder.gripPickup != null && holder.gripPickup.transform == holder.grip
            && holder.cornerPickup != null && holder.cornerPickup.transform == holder.corner,
            "the holder's grip and corner, each a kinematic trigger pickup on the Pickup layer with no behaviour",
            handles);
        Check(holder.gripModel != null && holder.gripModel.parent == holder.grip && holder.handleRenderers != null
            && holder.handleRenderers.Length == 2 && holder.handleRenderers[0] != null
            && holder.handleRenderers[1] != null, "the holder's bar and handle renderers", "missing");
        Check(holder.grip != null && holder.grip.GetComponent<VRC.SDK3.Components.VRCObjectSync>() == null,
            "the movable view isn't synced: its grip has no VRCObjectSync", "it has one");

        // Synced ticked on the instance: the build callback's match gives its grip a VRCObjectSync as an added
        // component (an override), the prefab's grip still without; unticked, it goes again (decided in 14.8: no
        // variant per case).
        if (holder.grip != null) {
            view.synced = true;
            ChessBuilderUtil.RecordInstance(view);
            bool matched = ChessBoard2DViewSyncCheck.MatchObjectSync(holder);
            VRC.SDK3.Components.VRCObjectSync sync = holder.grip.GetComponent<VRC.SDK3.Components.VRCObjectSync>();
            bool added = false;
            foreach (UnityEditor.SceneManagement.AddedComponent component in PrefabUtility.GetAddedComponents(placed)) {
                if (component.instanceComponent == sync) added = true;
            }
            Check(matched && sync != null && added, "Synced ticked: the grip gets a VRCObjectSync as an added component",
                matched + ", " + (sync != null) + ", " + added);
            Check(prefabHolder != null && prefabHolder.grip != null
                && prefabHolder.grip.GetComponent<VRC.SDK3.Components.VRCObjectSync>() == null,
                "Synced ticked on the instance: the prefab's grip still has no VRCObjectSync", "it has one");
            view.synced = false;
            ChessBuilderUtil.RecordInstance(view);
            matched = ChessBoard2DViewSyncCheck.MatchObjectSync(holder);
            Check(matched && holder.grip.GetComponent<VRC.SDK3.Components.VRCObjectSync>() == null
                && PrefabUtility.GetAddedComponents(placed).Count == 0,
                "Synced unticked: the grip's VRCObjectSync goes, no component added", "it stays");
        }

        // A change to the fixed view's prefab (its board label moved down, then back) reaches the movable prefab's
        // nested view and its placed instance.
        const string Label = "Board Label";
        Vector3 shift = new Vector3(0, -10, 0);
        GameObject contents = PrefabUtility.LoadPrefabContents(View2DPrefabPath);
        Transform prefabLabel = contents.transform.Find(Label);
        Vector3 original = prefabLabel != null ? prefabLabel.localPosition : Vector3.zero;
        bool changed = false;
        try {
            Check(prefabLabel != null, "the 2D Chess Board View prefab has its " + Label, "none");
            if (prefabLabel == null) return;
            prefabLabel.localPosition = original + shift;
            PrefabUtility.SaveAsPrefabAsset(contents, View2DPrefabPath);
            changed = true;
            ChessBoard2DViewHolder reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(Movable2DPrefabPath)
                .GetComponent<ChessBoard2DViewHolder>();
            Transform nestedLabel = reloaded != null && reloaded.view != null
                ? reloaded.view.transform.Find(Label) : null;
            Transform placedLabel = view.transform.Find(Label);
            Check(nestedLabel != null && nestedLabel.localPosition == original + shift,
                "a change to the 2D Chess Board View prefab reaches the movable prefab's view",
                nestedLabel == null ? "no label" : nestedLabel.localPosition.ToString("F1"));
            Check(placedLabel != null && placedLabel.localPosition == original + shift,
                "a change to the 2D Chess Board View prefab reaches a placed movable view",
                placedLabel == null ? "no label" : placedLabel.localPosition.ToString("F1"));
        } finally {
            if (changed) {
                prefabLabel.localPosition = original;
                PrefabUtility.SaveAsPrefabAsset(contents, View2DPrefabPath);
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }
        Transform restored = viewPrefab.transform.Find(Label);
        Check(restored != null && restored.localPosition == original, "the 2D Chess Board View prefab is put back",
            restored == null ? "no label" : restored.localPosition.ToString("F1"));
        Object.DestroyImmediate(placed);
    }

    // The open scene's 2D views (the test scene's) are instances: each in a holder a Movable 2D Chess Board View
    // instance (its view the nested one), any other a 2D Chess Board View instance; each with its boards, its grip's
    // VRCObjectSync matching its Synced; Board2DTest on one of them and its holder.
    static void CheckScene2DViews(GameObject viewPrefab, GameObject movablePrefab) {
        ChessBoard2DView[] views = Object.FindObjectsOfType<ChessBoard2DView>(true);
        Check(views.Length > 0, "the open scene has 2D views", "none");
        foreach (ChessBoard2DView view in views) {
            string name = "the scene's " + view.gameObject.name;
            ChessBoard2DViewHolder holder = ChessBuilderUtil.HolderOf(view);
            if (holder != null) {
                Check(PrefabUtility.IsAnyPrefabInstanceRoot(holder.gameObject)
                    && PrefabUtility.GetCorrespondingObjectFromOriginalSource(holder.gameObject) == movablePrefab
                    && PrefabUtility.GetCorrespondingObjectFromOriginalSource(view.gameObject) == viewPrefab,
                    name + "'s holder is a Movable 2D Chess Board View instance", "it isn't");
                bool hasSync = holder.grip != null
                    && holder.grip.GetComponent<VRC.SDK3.Components.VRCObjectSync>() != null;
                Check(hasSync == view.synced, name + "'s grip has a VRCObjectSync exactly when it's synced",
                    "synced " + view.synced + ", VRCObjectSync " + hasSync);
            } else {
                Check(PrefabUtility.IsAnyPrefabInstanceRoot(view.gameObject)
                    && PrefabUtility.GetCorrespondingObjectFromOriginalSource(view.gameObject) == viewPrefab,
                    name + " is a 2D Chess Board View instance", "it isn't");
            }
            Check(view.lockedBoard != null || view.boards.Length > 0, name + " has its boards", "none");
        }
        Board2DTest[] tests = Object.FindObjectsOfType<Board2DTest>(true);
        Check(tests.Length == 1 && tests[0].view != null && tests[0].holder != null
            && tests[0].holder == ChessBuilderUtil.HolderOf(tests[0].view),
            "Board2DTest is on a scene's movable 2D view and its holder", "it isn't");
    }

    // ---- 14.10: Chess Language ----

    // The I18n sanity check's own rules (I18nSanityChecks, in the io.siggi.ustools package), called by reflection:
    // they're private, and its public entry points ask in a dialog before wiring.
    static readonly System.Type SanityChecks =
        System.Type.GetType("SiggiUSTools.SimpleI18nEditor.I18nSanityChecks, SiggiUSTools.SimpleI18nEditor");

    static object CallSanityCheck(string method, params object[] args) {
        System.Reflection.MethodInfo info = SanityChecks?.GetMethod(method,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (info == null) {
            Check(false, "I18nSanityChecks." + method + " found", "missing (has the package changed?)");
            return null;
        }
        object result = info.Invoke(null, args);
        return result;
    }

    // The I18n the sanity check would wire into the I18n fields of behaviour's object (FindClosest), or null; ambiguous
    // set to the level where it found several.
    static I18n Closest(Component behaviour, I18n[] instances, out string ambiguous) {
        object[] args = { behaviour.transform, instances, null };
        I18n found = CallSanityCheck("FindClosest", args) as I18n;
        ambiguous = args[2] as string;
        return found;
    }

    // Every behaviour of ours under root with an i18n field.
    static List<Component> WithI18nField(GameObject root) {
        List<Component> found = new List<Component>();
        foreach (UdonSharpBehaviour behaviour in root.GetComponentsInChildren<UdonSharpBehaviour>(true)) {
            if (behaviour is I18n) continue;
            if (new SerializedObject(behaviour).FindProperty("i18n") != null) found.Add(behaviour);
        }
        return found;
    }

    // The behaviours in found whose closest I18n isn't expected (or is ambiguous), or "".
    static string WiredElsewhere(List<Component> found, I18n[] instances, I18n expected) {
        string wrong = "";
        foreach (Component behaviour in found) {
            I18n closest = Closest(behaviour, instances, out string ambiguous);
            if (closest != expected) {
                wrong += " " + behaviour.GetType().Name + " on " + behaviour.gameObject.name + ": "
                    + (closest != null ? closest.gameObject.name : ambiguous ?? "none") + ";";
            }
        }
        return wrong;
    }

    static string PackNames(I18n i18n) {
        SerializedProperty packs = new SerializedObject(i18n).FindProperty("languagePacks");
        string names = "";
        for (int i = 0; i < packs.arraySize; i++) {
            Object pack = packs.GetArrayElementAtIndex(i).objectReferenceValue;
            names += (i > 0 ? "|" : "") + (pack != null ? pack.name : "(none)");
        }
        return names;
    }

    static bool IsMain(I18n i18n) {
        return new SerializedObject(i18n).FindProperty("mainI18n").boolValue;
    }

    // The Chess Language placed: an I18n alone, not main, with the package's packs (en first); beside a table, a wall, a
    // settings menu and a 2D view, the sanity check wires every one of their I18n fields to it. Grouped under a parent
    // with them, beside a world's own main I18n (with a pack of another language) and a world object at the scene's
    // root: the chess fields still get ours, the world's object its own, the packs and main pass (the build isn't
    // blocked), and the world's I18n is main for ours, so its SetLanguageGlobally reaches ours (ViewTest switches it
    // in Play Mode). Ours moved to the root beside the world's: ambiguous, as docs/prefabs.md warns. A pack added to
    // the instance is an override: the prefab keeps its own.
    static void CheckLanguage(GameObject languagePrefab, GameObject tablePrefab, GameObject wallPrefab,
            GameObject menuPrefab, GameObject view2DPrefab, Scene scene) {
        List<string> logged = new List<string>();
        Application.LogCallback listener = (text, stack, type) => logged.Add(type + ": " + text);
        Application.logMessageReceived += listener;
        GameObject placed;
        try {
            placed = (GameObject)PrefabUtility.InstantiatePrefab(languagePrefab, scene);
        } finally {
            Application.logMessageReceived -= listener;
        }
        Check(logged.Count == 0, "the Chess Language placed logs nothing", string.Join("; ", logged));
        I18n language = placed.GetComponent<I18n>();
        Check(language != null, "the Chess Language has an I18n", "none");
        if (language == null) return;
        CheckString(placed.name, "Chess Language", "the Chess Language's name");
        CheckIntValue(placed.transform.childCount, 0, "the Chess Language has no children");
        Check(!IsMain(language), "the Chess Language isn't main", "it is");
        string expected = "";
        TextAsset[] packs = ChessBuilderUtil.LanguagePacks();
        for (int i = 0; i < packs.Length; i++) expected += (i > 0 ? "|" : "") + packs[i].name;
        Check(expected.StartsWith("en"), "the package's packs start with en.json", expected);
        CheckString(PackNames(language), expected, "the Chess Language holds the package's packs, en first");
        Check(Equals(CallSanityCheck("ValidateLanguagePacks", new I18n[] { language }, "test"), true),
            "the sanity check passes the Chess Language's packs", "it doesn't");

        // Beside a table, a wall, a settings menu and a 2D view, as dropped into a scene.
        GameObject[] chess = {
            (GameObject)PrefabUtility.InstantiatePrefab(tablePrefab, scene),
            (GameObject)PrefabUtility.InstantiatePrefab(wallPrefab, scene),
            (GameObject)PrefabUtility.InstantiatePrefab(menuPrefab, scene),
            (GameObject)PrefabUtility.InstantiatePrefab(view2DPrefab, scene),
        };
        List<Component> fields = new List<Component>();
        foreach (GameObject root in chess) fields.AddRange(WithI18nField(root));
        CheckIntValue(fields.Count, 5, "a table, a wall, a menu and a 2D view have 5 I18n fields");
        I18n[] alone = { language };
        CheckString(WiredElsewhere(fields, alone, language), "",
            "beside them, the sanity check wires every chess I18n field to the Chess Language");

        // Grouped under a parent with them, a world's own main I18n and object at the root.
        GameObject group = new GameObject("Chess");
        SceneManager.MoveGameObjectToScene(group, scene);
        placed.transform.SetParent(group.transform, false);
        foreach (GameObject root in chess) root.transform.SetParent(group.transform, false);
        GameObject worldObject = new GameObject("World Language");
        SceneManager.MoveGameObjectToScene(worldObject, scene);
        I18n world = UdonSharpUndo.AddComponent<I18n>(worldObject);
        SerializedObject worldFields = new SerializedObject(world);
        worldFields.FindProperty("mainI18n").boolValue = true;
        SerializedProperty worldPacks = worldFields.FindProperty("languagePacks");
        worldPacks.arraySize = 1;
        worldPacks.GetArrayElementAtIndex(0).objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Tests/View/test-world-language.json");
        worldFields.ApplyModifiedProperties();
        GameObject worldUI = new GameObject("World UI");
        SceneManager.MoveGameObjectToScene(worldUI, scene);
        I18n[] both = { language, world };
        CheckString(WiredElsewhere(fields, both, language), "",
            "grouped with a world I18n elsewhere, every chess I18n field is wired to the Chess Language");
        Check(Closest(worldUI.transform, both, out _) == world, "grouped: the world's object gets the world's I18n",
            "it doesn't");
        Check(Equals(CallSanityCheck("ValidateLanguagePacks", both, "test"), true)
            && Equals(CallSanityCheck("ValidateMainI18n", both, "test"), true),
            "grouped: the packs and the one main pass (the build isn't blocked)", "they don't");
        CallSanityCheck("LinkMainI18n", new object[] { both });
        SerializedObject linked = new SerializedObject(language);
        SerializedProperty all = new SerializedObject(world).FindProperty("allI18ns");
        bool listed = false;
        for (int i = 0; i < all.arraySize; i++) listed |= all.GetArrayElementAtIndex(i).objectReferenceValue == language;
        Check(linked.FindProperty("main").objectReferenceValue == world && listed,
            "grouped: the world's I18n is main for the Chess Language, which it lists for SetLanguageGlobally",
            "it isn't");

        // Ours beside the world's at the root: neither is closer.
        placed.transform.SetParent(null, false);
        Closest(fields[0], both, out string ambiguous);
        Check(ambiguous != null && ambiguous.Contains("root objects"),
            "the Chess Language beside a world I18n at the root: ambiguous for the chess objects",
            ambiguous ?? "not ambiguous");
        placed.transform.SetParent(group.transform, false);

        // A pack added to the instance, as a world adds a language: an override, the prefab keeps its own.
        SerializedObject instanceFields = new SerializedObject(language);
        SerializedProperty instancePacks = instanceFields.FindProperty("languagePacks");
        instancePacks.arraySize++;
        instancePacks.GetArrayElementAtIndex(instancePacks.arraySize - 1).objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Tests/View/test-language.json");
        instanceFields.ApplyModifiedProperties();
        CheckString(PackNames(language), expected + "|test-language", "a pack added to the Chess Language instance");
        Check(PrefabUtility.HasPrefabInstanceAnyOverrides(placed, false), "the added pack is an instance override",
            "no override");
        CheckString(PackNames(languagePrefab.GetComponent<I18n>()), expected,
            "a pack added to the instance: the prefab keeps its own packs");
        CheckString(WiredElsewhere(fields, both, language), "",
            "a pack added: every chess I18n field still wired to the one Chess Language");
        Object.DestroyImmediate(group);
        Object.DestroyImmediate(worldObject);
        Object.DestroyImmediate(worldUI);
    }

    // The open scene's Chess Language (the test scene's) is an instance of the prefab, not main, with the test pack
    // added after the package's (ViewTestBuilder), and every chess I18n field is on it.
    static void CheckSceneLanguage(GameObject languagePrefab) {
        TextAsset en = AssetDatabase.LoadAssetAtPath<TextAsset>(ChessBuilderUtil.LanguagePackPath);
        List<I18n> found = new List<I18n>();
        foreach (I18n i18n in Object.FindObjectsOfType<I18n>(true)) {
            if (PackNames(i18n).Split('|')[0] == en.name) found.Add(i18n);
        }
        CheckIntValue(found.Count, 1, "the open scene has one Chess Language");
        if (found.Count == 0) return;
        I18n language = found[0];
        Check(PrefabUtility.IsAnyPrefabInstanceRoot(language.gameObject)
            && PrefabUtility.GetCorrespondingObjectFromOriginalSource(language.gameObject) == languagePrefab,
            "the scene's " + language.gameObject.name + " is a Chess Language instance", "it isn't");
        Check(!IsMain(language), "the scene's Chess Language isn't main", "it is");
        CheckString(PackNames(language), PackNames(languagePrefab.GetComponent<I18n>()) + "|test-language",
            "the scene's Chess Language has the test pack after the package's");
        string wrong = "";
        int count = 0;
        foreach (GameObject root in language.gameObject.scene.GetRootGameObjects()) {
            foreach (Component behaviour in WithI18nField(root)) {
                count++;
                Object value = new SerializedObject(behaviour).FindProperty("i18n").objectReferenceValue;
                if (value != language) wrong += " " + behaviour.gameObject.name + ";";
            }
        }
        Check(count > 0 && wrong == "", "the scene's " + count + " chess I18n fields are on the Chess Language", wrong);
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
