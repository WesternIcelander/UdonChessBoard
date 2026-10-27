using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ChessBuilderUtil;

// Adds ArchiveTest (PLAN.md step 10.1a) to the open scene for the scene's only instance archive: a board whose arbiter
// lists it (13.10), its history wall (10.2a), the board's UI (10.2b, 12.1), the scene's personal archive and its wall
// (13.10), and the scene's BoardTest and ViewTest to wait for. An existing ArchiveTest is replaced. Menu: Tools > Chess Board > Create Archive Test. Plain editor C#, not Udon.
public static class ArchiveTestBuilder {
    const string TestScriptPath = "Assets/Tests/Archive/ArchiveTest.cs";
    const string TestProgramPath = "Assets/Tests/Archive/ArchiveTest.asset";

    [MenuItem("Tools/Chess Board/Create Archive Test")]
    static void Create() {
        InstanceGameArchive[] archives = Object.FindObjectsOfType<InstanceGameArchive>(true);
        if (archives.Length != 1) {
            EditorUtility.DisplayDialog("Create Archive Test", archives.Length == 0
                ? "There's no GameArchive in the open scene. Place the Game Archive prefab "
                    + "(Assets/Siggi/ChessBoard/Prefabs)."
                : "There are several GameArchives in the scene; the test needs exactly one.", "OK");
            return;
        }
        if (!EnsureProgramAssets("Create Archive Test", TestScriptPath, TestProgramPath)) return;
        InstanceGameArchive archive = archives[0];

        ArchiveTest[] olds = Object.FindObjectsOfType<ArchiveTest>(true);
        GameObject root = new GameObject("ArchiveTest");
        Undo.RegisterCreatedObjectUndo(root, "Create Archive Test");
        ArchiveTest test = UdonSharpUndo.AddComponent<ArchiveTest>(root);
        test.archive = archive;
        ChessBoard[] boards = Object.FindObjectsOfType<ChessBoard>(true);
        for (int i = 0; i < boards.Length && test.board == null; i++) {
            // Not the inactive spare board SettingsTest numbers 2 (14.11, SpareBoardBuilder), which has no UI.
            if (boards[i].gameObject.activeInHierarchy && boards[i].arbiter != null && System.Array.IndexOf(boards[i].arbiter.archives, archive) >= 0) {
                test.board = boards[i];
            }
        }
        ChessBoardUI[] uis = Object.FindObjectsOfType<ChessBoardUI>(true);
        for (int i = 0; i < uis.Length && test.ui == null; i++) {
            if (uis[i].board != null && uis[i].board == test.board) test.ui = uis[i];
        }
        BoardTest[] boardTests = Object.FindObjectsOfType<BoardTest>(true);
        test.boardTest = boardTests.Length > 0 ? boardTests[0] : null;
        ViewTest[] viewTests = Object.FindObjectsOfType<ViewTest>(true);
        test.viewTest = viewTests.Length > 0 ? viewTests[0] : null;
        GameHistoryWall[] walls = Object.FindObjectsOfType<GameHistoryWall>(true);
        for (int i = 0; i < walls.Length && test.wall == null; i++) {
            if (walls[i].archive == archive) test.wall = walls[i];
        }
        PersonalGameArchive[] personals = Object.FindObjectsOfType<PersonalGameArchive>(true);
        test.personal = personals.Length > 0 ? personals[0] : null;
        for (int i = 0; i < walls.Length && test.personalWall == null; i++) {
            if (test.personal != null && walls[i].archive == test.personal) test.personalWall = walls[i];
        }
        UdonSharpEditorUtility.CopyProxyToUdon(test);

        for (int i = 0; i < olds.Length; i++) {
            if (i == 0) {
                root.transform.SetParent(olds[i].transform.parent, false);
                root.transform.SetSiblingIndex(olds[i].transform.GetSiblingIndex());
            }
            Undo.DestroyObjectImmediate(olds[i].gameObject);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[ArchiveTestBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " ArchiveTest"
            + (test.board != null ? " with board " + test.board.gameObject.name : " with no board using the archive")
            + ", waiting for " + (test.boardTest != null ? "BoardTest" : "no BoardTest") + " and "
            + (test.viewTest != null ? "ViewTest" : "no ViewTest") + ", "
            + (test.wall != null ? "with the history wall"
                : "with no history wall (place the Game History Wall prefab first)")
            + (test.ui != null ? ", with its UI" : ", with no UI of the board")
            + (test.personalWall != null ? ", with the personal archive and its wall"
                : ", with no personal archive or wall (place the Personal Game Archive and Personal History Wall "
                    + "prefabs)")
            + ". Save the scene.");
    }
}
