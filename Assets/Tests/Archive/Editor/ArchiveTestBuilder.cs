using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Adds ArchiveTest (PLAN.md step 10.1a) to the open scene for the scene's only GameArchive: a board using it, its
// history wall (10.2a), the board's view (10.2b), and the scene's BoardTest and ViewTest to wait for. An existing
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
        if (!EnsureProgramAsset()) return;
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
        ChessBoardView[] views = Object.FindObjectsOfType<ChessBoardView>(true);
        for (int i = 0; i < views.Length && test.view == null; i++) {
            if (views[i].board != null && views[i].board == test.board) test.view = views[i];
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
            + (test.view != null ? ", with its view" : ", with no view of the board")
            + ". Save the scene.");
    }

    // As ChessBoardViewBuilder.EnsureProgramAsset.
    static bool EnsureProgramAsset() {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(TestProgramPath) != null) return true;
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(TestScriptPath);
        if (script == null) {
            Debug.LogError("[ArchiveTestBuilder] " + TestScriptPath + " not found.");
            return false;
        }
        UdonSharpProgramAsset program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        program.sourceCsScript = script;
        AssetDatabase.CreateAsset(program, TestProgramPath);
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        EditorUtility.DisplayDialog("Create Archive Test", "Created the test's program asset (" + TestProgramPath
            + "). Once UdonSharp has finished compiling, run Tools > Chess Board > Create Archive Test again.", "OK");
        return false;
    }
}
