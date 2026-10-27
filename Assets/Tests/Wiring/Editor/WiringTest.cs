using System.Collections.Generic;
using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Step 14.11's check of the wiring script (ChessSceneWiring, docs/prefabs.md), without Play Mode: run it from
// Tools > Chess Board > Test Wiring. In a new scene of its own, opened beside the open one and closed unsaved, it drops
// in one of each prefab and two Chess Tables and wires them: every reference filled, the boards numbered 0 and 1 and
// the arbiter's Board Count 2; wiring again changes nothing; a third table takes number 2 and raises the count to 3,
// undone by one undo; an inactive table counts; one removed lowers the count again; a second arbiter leaves a new
// table's arbiter empty, naming both. Then a scene with an arbiter and two tables is saved (to a file deleted again)
// and is wired by saving.
// Its Play Mode half: Tools > Chess Board > Open Wiring Play Test saves the open scenes and opens a scene of its own
// with the same prefabs, saved unwired, and WiringPlayTest; Play Mode wires it and the test plays a game on each table.
// Tools > Chess Board > Close Wiring Play Test checks the wiring stayed in the scene, then deletes it and reopens the
// scene that was open before.
// Logs "[WiringTest] PASS: <check>" or "[WiringTest] FAIL: <check>: <details>" per check, then a summary line.
public static class WiringTest {
    const string Prefabs = "Assets/Siggi/ChessBoard/Prefabs/";
    const string SaveScenePath = "Assets/Tests/Wiring/WiringSaveTest.unity";
    const string PlayScenePath = "Assets/Tests/Wiring/WiringPlayTest.unity";
    const string PlayTestScriptPath = "Assets/Tests/Wiring/WiringPlayTest.cs";
    const string PlayTestProgramPath = "Assets/Tests/Wiring/WiringPlayTest.asset";
    const string WorldPrefabPath = "Packages/com.vrchat.worlds/Samples/UdonExampleScene/Prefabs/VRCWorld.prefab";
    const string ReturnSceneKey = "SiggiChessBoard.WiringTest.ReturnScene";

    static int passed;
    static int failed;

    // The objects of one wired scene.
    class Placed {
        public ChessArbiter arbiter;
        public InstanceGameArchive archive;
        public PersonalGameArchive personal;
        public GameHistoryWall wall;
        public GameHistoryWall personalWall;
        public ChessSettingsMenu menu;
        public ChessBoard2DView view;
        public ChessBoard2DView movableView;
        public List<GameObject> tables = new List<GameObject>();
        public List<ChessBoard> boards = new List<ChessBoard>();
    }

    static GameObject Place(string name, Scene scene, Vector3 position) {
        GameObject placed = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + name + ".prefab"), scene);
        placed.transform.position = position;
        return placed;
    }

    static ChessBoard PlaceTable(Placed placed, Scene scene, Vector3 position) {
        GameObject table = Place("Chess Table", scene, position);
        ChessBoard board = table.GetComponentInChildren<ChessBoard>(true);
        placed.tables.Add(table);
        placed.boards.Add(board);
        return board;
    }

    // One of each prefab and two tables, nothing set.
    static Placed PlaceAll(Scene scene) {
        Placed placed = new Placed();
        placed.arbiter = Place("Chess Arbiter", scene, new Vector3(0, 0, -4)).GetComponent<ChessArbiter>();
        Place("Chess Language", scene, new Vector3(0, 0, -4));
        placed.archive = Place("Game Archive", scene, new Vector3(1, 0, -4)).GetComponent<InstanceGameArchive>();
        placed.personal = Place("Personal Game Archive", scene, new Vector3(2, 0, -4)).GetComponent<PersonalGameArchive>();
        placed.wall = Place("Game History Wall", scene, new Vector3(-2, 1.5f, 6)).GetComponent<GameHistoryWall>();
        placed.personalWall = Place("Personal History Wall", scene, new Vector3(0, 1.5f, 6)).GetComponent<GameHistoryWall>();
        placed.menu = Place("Chess Settings Menu", scene, new Vector3(2, 1.5f, 6)).GetComponent<ChessSettingsMenu>();
        placed.view = Place("2D Chess Board View", scene, new Vector3(4, 1.5f, 6)).GetComponent<ChessBoard2DView>();
        placed.movableView = Place("Movable 2D Chess Board View", scene, new Vector3(6, 1.5f, 6))
            .GetComponentInChildren<ChessBoard2DView>(true);
        PlaceTable(placed, scene, new Vector3(-1.5f, 0, 3));
        PlaceTable(placed, scene, new Vector3(1.5f, 0, 3));
        return placed;
    }

    [MenuItem("Tools/Chess Board/Test Wiring")]
    public static void Run() {
        passed = 0;
        failed = 0;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try {
            CheckWiring(scene);
        } finally {
            EditorSceneManager.CloseScene(scene, true);
        }
        CheckSaving();
        CheckOpenScene();
        Summary();
    }

    // The test scene (saved since 14.11, so wired): wiring it changes nothing, and its Board Count of 3 comes from
    // the inactive spare board numbered 2 (SpareBoardBuilder).
    static void CheckOpenScene() {
        Scene scene = SceneManager.GetActiveScene();
        ChessSceneWiring.Result result = ChessSceneWiring.Wire(scene);
        Check(result.changes.Count == 0 && result.problems.Count == 0 && result.warnings.Count == 0,
            "the open scene: already wired", string.Join(" ", result.changes) + string.Join(" ", result.problems)
                + string.Join(" ", result.warnings));
        List<ChessArbiter> arbiters = ChessSceneWiring.InScene<ChessArbiter>(scene);
        bool spare = false;
        foreach (ChessBoard board in ChessSceneWiring.InScene<ChessBoard>(scene)) {
            if (!board.gameObject.activeInHierarchy && board.boardNumber == 2) spare = true;
        }
        Check(arbiters.Count == 1 && arbiters[0].boardCount == 3 && spare,
            "the open scene: Board Count 3, from the inactive spare board 2", arbiters.Count + " arbiter(s), spare "
                + spare);
    }

    static void CheckWiring(Scene scene) {
        Placed placed = PlaceAll(scene);
        ChessBoard first = placed.boards[0];
        ChessBoard second = placed.boards[1];
        Check(first.arbiter == null && placed.wall.archive == null && placed.menu.arbiter == null
                && placed.view.boards.Length == 0 && placed.arbiter.archives.Length == 0
                && first.boardNumber == 0 && second.boardNumber == 0 && placed.arbiter.boardCount == 4,
            "placed: nothing set, both boards number 0, Board Count 4", "something was set");

        ChessSceneWiring.Result result = ChessSceneWiring.Wire(scene);
        Check(result.problems.Count == 0, "wired: nothing left unfilled", string.Join(" ", result.problems));
        Check(first.arbiter == placed.arbiter && second.arbiter == placed.arbiter, "wired: both boards on the arbiter",
            first.arbiter + ", " + second.arbiter);
        Check(first.boardNumber == 0 && second.boardNumber == 1, "wired: boards numbered 0 and 1",
            first.boardNumber + ", " + second.boardNumber);
        CheckInt(placed.arbiter.boardCount, 2, "wired: Board Count 2");
        Check(placed.arbiter.archives.Length == 2 && placed.arbiter.archives[0] == placed.archive
                && placed.arbiter.archives[1] == placed.personal, "wired: the arbiter lists both archives",
            placed.arbiter.archives.Length + " listed");
        Check(placed.wall.archive == placed.archive, "wired: the history wall on the Game Archive",
            "on " + placed.wall.archive);
        Check(placed.personalWall.archive == placed.personal,
            "wired: the personal wall (with delete buttons) on the Personal Game Archive",
            "on " + placed.personalWall.archive);
        Check(placed.menu.arbiter == placed.arbiter && SameBoards(placed.menu.boards, first, second),
            "wired: the settings menu on the arbiter with both boards, by number", placed.menu.boards.Length + " boards");
        Check(SameBoards(placed.view.boards, first, second) && SameBoards(placed.movableView.boards, first, second),
            "wired: both 2D views on both boards, by number", placed.view.boards.Length + ", "
                + placed.movableView.boards.Length + " boards");
        Check(placed.view.lockedBoard == null, "wired: the 2D view left unlocked", "locked");
        Check(PrefabUtility.IsPartOfPrefabInstance(first)
                && new SerializedObject(second).FindProperty("boardNumber").prefabOverride
                && new SerializedObject(first).FindProperty("arbiter").prefabOverride
                && new SerializedObject(placed.arbiter).FindProperty("boardCount").prefabOverride,
            "wired: the values are overrides on the instances", "not recorded");
        Check(UdonSharpEditorUtility.GetBackingUdonBehaviour(second).publicVariables
                .TryGetVariableValue("boardNumber", out int udonNumber) && udonNumber == 1,
            "wired: copied to the Udon data", "not copied");
        Check(scene.isDirty, "wired: the scene marked dirty", "not dirty");

        result = ChessSceneWiring.Wire(scene);
        Check(result.changes.Count == 0 && result.problems.Count == 0 && result.warnings.Count == 0,
            "wired again: nothing changes", string.Join(" ", result.changes));

        // A third table: the next number, the count raised; undone by one undo.
        Undo.IncrementCurrentGroup();
        ChessBoard third = PlaceTable(placed, scene, new Vector3(4.5f, 0, 3));
        Undo.IncrementCurrentGroup();
        result = ChessSceneWiring.Wire(scene);
        Check(third.arbiter == placed.arbiter && third.boardNumber == 2 && first.boardNumber == 0
                && second.boardNumber == 1, "third table: numbered 2, the others kept",
            first.boardNumber + ", " + second.boardNumber + ", " + third.boardNumber);
        CheckInt(placed.arbiter.boardCount, 3, "third table: Board Count raised to 3");
        Check(Logged(result, "raised from 2"), "third table: the raise logged", string.Join(" ", result.changes));
        Check(placed.view.boards.Length == 2 && placed.menu.boards.Length == 2,
            "third table: the lists already set are kept", "changed");
        Check(result.warnings.Count == 1 && result.warnings[0].Contains("Apply now"),
            "third table: a warning that the menu's Boards lacks it", string.Join(" ", result.warnings));
        Undo.PerformUndo();
        Check(third.arbiter == null && third.boardNumber == 0 && placed.arbiter.boardCount == 2,
            "third table: one undo takes the wiring back", third.boardNumber + ", count " + placed.arbiter.boardCount);
        ChessSceneWiring.Wire(scene);
        CheckInt(third.boardNumber, 2, "third table: wired again");

        // Inactive boards count too.
        ChessBoard inactive = PlaceTable(placed, scene, new Vector3(7.5f, 0, 3));
        SerializedObject fields = new SerializedObject(inactive);
        fields.FindProperty("boardNumber").intValue = 5;
        fields.ApplyModifiedProperties();
        placed.tables[3].SetActive(false);
        result = ChessSceneWiring.Wire(scene);
        Check(inactive.arbiter == placed.arbiter && inactive.boardNumber == 5 && placed.arbiter.boardCount == 6,
            "inactive table numbered 5: wired, its number kept, Board Count 6",
            inactive.boardNumber + ", count " + placed.arbiter.boardCount);
        Check(result.warnings.Count == 1, "inactive table: no warning that the menu lacks it",
            string.Join(" ", result.warnings));
        Object.DestroyImmediate(placed.tables[3]);
        Object.DestroyImmediate(placed.tables[2]);
        result = ChessSceneWiring.Wire(scene);
        CheckInt(placed.arbiter.boardCount, 2, "two tables removed: Board Count lowered to 2");
        Check(Logged(result, "lowered from 6"), "tables removed: the lowering logged", string.Join(" ", result.changes));

        // A table dropped above the numbered ones in the hierarchy takes the next number; they keep theirs.
        ChessBoard top = PlaceTable(placed, scene, new Vector3(-4.5f, 0, 3));
        placed.tables[placed.tables.Count - 1].transform.SetAsFirstSibling();
        ChessSceneWiring.Wire(scene);
        Check(top.boardNumber == 2 && first.boardNumber == 0 && second.boardNumber == 1,
            "a table placed first in the hierarchy: numbered 2, the others kept",
            first.boardNumber + ", " + second.boardNumber + ", " + top.boardNumber);
        Object.DestroyImmediate(placed.tables[placed.tables.Count - 1]);

        // Two arbiters: a new table's arbiter can't be chosen, which names both.
        ChessArbiter other = Place("Chess Arbiter", scene, new Vector3(0, 0, -6)).GetComponent<ChessArbiter>();
        other.gameObject.name = "Other Arbiter";
        ChessBoard unwired = PlaceTable(placed, scene, new Vector3(4.5f, 0, 3));
        result = ChessSceneWiring.Wire(scene);
        Check(unwired.arbiter == null, "two arbiters: the new table's arbiter left empty", "on " + unwired.arbiter);
        Check(result.problems.Count == 1 && result.problems[0].Contains("Chess Arbiter")
                && result.problems[0].Contains("Other Arbiter") && result.problemObjects[0] == unwired,
            "two arbiters: one problem, naming both, on the new board", string.Join(" ", result.problems));
        Check(!ChessSceneWiring.Report(result, false), "two arbiters: the build would be cancelled", "allowed");
        Object.DestroyImmediate(placed.tables[placed.tables.Count - 1]);
        Object.DestroyImmediate(other.gameObject);

        // Missing archives: a wall with none of its kind.
        Object.DestroyImmediate(placed.personal.gameObject);
        SerializedObject wallFields = new SerializedObject(placed.personalWall);
        wallFields.FindProperty("archive").objectReferenceValue = null;
        wallFields.ApplyModifiedProperties();
        result = ChessSceneWiring.Wire(scene);
        Check(result.problems.Count == 1 && result.problems[0].Contains("no Personal Game Archive"),
            "no personal archive: the personal wall's archive left empty, said", string.Join(" ", result.problems));
    }

    // Saving wires a scene too.
    static void CheckSaving() {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try {
            Placed placed = new Placed();
            placed.arbiter = Place("Chess Arbiter", scene, Vector3.zero).GetComponent<ChessArbiter>();
            PlaceTable(placed, scene, new Vector3(-1.5f, 0, 3));
            PlaceTable(placed, scene, new Vector3(1.5f, 0, 3));
            Check(EditorSceneManager.SaveScene(scene, SaveScenePath), "saving: the scene saved", "failed");
            Check(placed.boards[0].arbiter == placed.arbiter && placed.boards[1].arbiter == placed.arbiter
                    && placed.boards[0].boardNumber == 0 && placed.boards[1].boardNumber == 1
                    && placed.arbiter.boardCount == 2, "saving: wired, numbered 0 and 1, Board Count 2",
                placed.boards[0].boardNumber + ", " + placed.boards[1].boardNumber + ", count "
                    + placed.arbiter.boardCount);
            string saved = System.IO.File.ReadAllText(SaveScenePath);
            Check(saved.Contains("propertyPath: boardCount\n      value: 2"), "saving: the wiring is in the saved file",
                "Board Count 2 not found");
        } finally {
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.DeleteAsset(SaveScenePath);
        }
    }

    static bool SameBoards(ChessBoard[] list, ChessBoard first, ChessBoard second) {
        return list != null && list.Length == 2 && list[0] == first && list[1] == second;
    }

    static bool Logged(ChessSceneWiring.Result result, string text) {
        foreach (string change in result.changes) if (change.Contains(text)) return true;
        return false;
    }

    // ---- The Play Mode half ----

    [MenuItem("Tools/Chess Board/Open Wiring Play Test")]
    static void OpenPlayTest() {
        if (!ChessBuilderUtil.EnsureProgramAssets("Open Wiring Play Test", PlayTestScriptPath, PlayTestProgramPath)) {
            return;
        }
        string returnScene = SceneManager.GetActiveScene().path;
        if (returnScene == PlayScenePath) {
            Debug.LogError("[WiringTest] The wiring play test is already open; close it first.");
            return;
        }
        EditorSceneManager.SaveOpenScenes();
        EditorPrefs.SetString(ReturnSceneKey, returnScene);
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(WorldPrefabPath), scene);
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(3, 1, 3);
        Placed placed = PlaceAll(scene);

        GameObject testObject = new GameObject("WiringPlayTest");
        WiringPlayTest test = UdonSharpUndo.AddComponent<WiringPlayTest>(testObject);
        test.arbiter = placed.arbiter;
        test.boards = placed.boards.ToArray();
        test.archive = placed.archive;
        test.personal = placed.personal;
        test.wall = placed.wall;
        test.personalWall = placed.personalWall;
        test.menu = placed.menu;
        test.views = new[] { placed.view, placed.movableView };
        UdonSharpEditorUtility.CopyProxyToUdon(test);

        // Saved unwired, so that entering Play Mode is what wires it.
        ChessSceneWiring.skipOnSave = true;
        try {
            EditorSceneManager.SaveScene(scene, PlayScenePath);
        } finally {
            ChessSceneWiring.skipOnSave = false;
        }
        Debug.Log("[WiringTest] Opened the wiring play test, saved unwired: board arbiters "
            + placed.boards[0].arbiter + ", " + placed.boards[1].arbiter + ", numbers " + placed.boards[0].boardNumber
            + ", " + placed.boards[1].boardNumber + ". Enter Play Mode, then run Close Wiring Play Test.");
    }

    [MenuItem("Tools/Chess Board/Close Wiring Play Test")]
    static void ClosePlayTest() {
        passed = 0;
        failed = 0;
        if (SceneManager.GetActiveScene().path != PlayScenePath) {
            Debug.LogError("[WiringTest] The wiring play test isn't open.");
            return;
        }
        WiringPlayTest test = Object.FindObjectOfType<WiringPlayTest>(true);
        Check(test != null && test.boards[0].arbiter == test.arbiter && test.boards[1].arbiter == test.arbiter
                && test.boards[1].boardNumber == 1 && test.arbiter.boardCount == 2 && test.wall.archive == test.archive,
            "after Play Mode: the wiring stayed in the scene", "it didn't");
        Check(SceneManager.GetActiveScene().isDirty, "after Play Mode: the scene marked dirty", "not dirty");
        string returnScene = EditorPrefs.GetString(ReturnSceneKey, "");
        if (returnScene.Length > 0) {
            EditorSceneManager.OpenScene(returnScene, OpenSceneMode.Single);
        } else {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        }
        AssetDatabase.DeleteAsset(PlayScenePath);
        Summary();
    }

    static void CheckInt(int got, int expected, string name) {
        Check(got == expected, name, "got " + got + ", expected " + expected);
    }

    static void Check(bool ok, string name, string details) {
        if (ok) {
            passed++;
            Debug.Log("[WiringTest] PASS: " + name);
        } else {
            failed++;
            Debug.LogError("[WiringTest] FAIL: " + name + ": " + details);
        }
    }

    static void Summary() {
        string line = "[WiringTest] done: " + passed + " passed, " + failed + " failed";
        if (failed == 0) Debug.Log(line);
        else Debug.LogError(line);
    }
}
