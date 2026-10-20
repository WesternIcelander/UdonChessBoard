using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ChessBuilderUtil;

// Adds ArchiveTest (PLAN.md step 10.1a) to the open scene for the scene's only GameArchive: a board using it, its
// history wall (10.2a), the board's UI (10.2b, 12.1), and the scene's BoardTest and ViewTest to wait for. An existing
// ArchiveTest is replaced. Menu: Tools > Chess Board > Create Archive Test. Plain editor C#, not Udon.
public static class ArchiveTestBuilder {
    const string TestScriptPath = "Assets/Tests/Archive/ArchiveTest.cs";
    const string TestProgramPath = "Assets/Tests/Archive/ArchiveTest.asset";

    [MenuItem("Tools/Chess Board/Create Archive Test")]
    static void Create() {
        GameArchive[] archives = Object.FindObjectsOfType<GameArchive>(true);
        if (archives.Length != 1) {
            EditorUtility.DisplayDialog("Create Archive Test", archives.Length == 0
                ? "There's no GameArchive in the open scene. Create one with Tools > Chess Board > Create Game Archive."
                : "There are several GameArchives in the scene; the test needs exactly one.", "OK");
            return;
        }
        if (!EnsureProgramAssets("Create Archive Test", TestScriptPath, TestProgramPath)) return;
        GameArchive archive = archives[0];

        ArchiveTest[] olds = Object.FindObjectsOfType<ArchiveTest>(true);
        GameObject root = new GameObject("ArchiveTest");
        Undo.RegisterCreatedObjectUndo(root, "Create Archive Test");
        ArchiveTest test = UdonSharpUndo.AddComponent<ArchiveTest>(root);
        test.archive = archive;
        ChessBoard[] boards = Object.FindObjectsOfType<ChessBoard>(true);
        for (int i = 0; i < boards.Length && test.board == null; i++) {
            if (boards[i].archive == archive) test.board = boards[i];
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
            + (test.wall != null ? "with the history wall" : "with no history wall (Create History Wall first)")
            + (test.ui != null ? ", with its UI" : ", with no UI of the board")
            + ". Save the scene.");
    }
}
