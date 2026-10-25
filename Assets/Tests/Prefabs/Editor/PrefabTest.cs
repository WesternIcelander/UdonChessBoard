using System.Collections.Generic;
using SiggiChessBoard;
using SiggiUSTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;

// Step 14's check of the prefabs (docs/prefabs.md), without Play Mode: run it from Tools > Chess Board > Test Prefabs.
// It drops each prefab into a new scene of its own, opened beside the open one and closed unsaved afterwards, and
// checks what it holds. 14.1: Chess Pieces on its own, and the Chess Arbiter with its nested Chess Pieces, the build
// check on it, a change to the Chess Pieces prefab reaching the nested instance but not an unpacked copy. The change
// is undone and the prefab saved again before it ends. 14.2: the Chess Board with its importer, two placed with their
// own board numbers, and the open scene's boards instances of it. Every prefab: nothing it uses is under Assets/Tests/.
// Logs "[PrefabTest] PASS: <check>" or "[PrefabTest] FAIL: <check>: <details>" per check, then a summary line.
public static class PrefabTest {
    const string PieceSetPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Pieces.prefab";
    const string ArbiterPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Arbiter.prefab";
    const string BoardPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Board.prefab";

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
        if (setPrefab == null || arbiterPrefab == null || boardPrefab == null) {
            Summary();
            return;
        }
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try {
            CheckPieceSet(setPrefab, scene);
            CheckArbiter(setPrefab, arbiterPrefab, scene);
            CheckBoard(boardPrefab, scene);
        } finally {
            EditorSceneManager.CloseScene(scene, true);
        }
        CheckSceneBoards(boardPrefab);
        foreach (string path in new[] { PieceSetPrefabPath, ArbiterPrefabPath, BoardPrefabPath }) CheckNoTests(path);
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
        Check(board.arbiter == null && board.archive == null, "the board's arbiter and archive are left to set",
            "arbiter " + board.arbiter + ", archive " + board.archive);
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
            Check(board.arbiter != null && board.archive != null, name + " has its arbiter and archive",
                "arbiter " + board.arbiter + ", archive " + board.archive);
            Check(board.importer != null && board.importer.transform.parent == board.transform,
                name + " has its own importer", board.importer == null ? "empty" : "on " + board.importer.name);
            Check(numbers.Add(board.boardNumber), name + "'s number " + board.boardNumber + " is its own",
                "another board has it");
        }
    }

    // Nothing a prefab uses (scripts, programs, assets) is a test's (step 14: no Assets/Tests/ references).
    static void CheckNoTests(string path) {
        List<string> tests = new List<string>();
        foreach (string dependency in AssetDatabase.GetDependencies(path, true)) {
            if (dependency.StartsWith("Assets/Tests/")) tests.Add(dependency);
        }
        Check(tests.Count == 0, path + " uses nothing under Assets/Tests/", string.Join(", ", tests));
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
