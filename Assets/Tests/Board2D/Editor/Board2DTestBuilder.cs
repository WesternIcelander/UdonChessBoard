using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ChessBuilderUtil;

// Adds Board2DTest (PLAN.md step 12.13) to the open scene for the scene's only 2D board view (or the selected one),
// with the scene's BoardTest, ViewTest and ArchiveTest to wait for. An existing Board2DTest is replaced.
// Menu: Tools > Chess Board > Create 2D View Test, and Toggle 2D View Synced. Plain editor C#, not Udon.
public static class Board2DTestBuilder {
    const string TestScriptPath = "Assets/Tests/Board2D/Board2DTest.cs";
    const string TestProgramPath = "Assets/Tests/Board2D/Board2DTest.asset";

    [MenuItem("Tools/Chess Board/Create 2D View Test")]
    static void Create() {
        ChessBoard2DView view = null;
        if (Selection.activeGameObject != null) view = Selection.activeGameObject.GetComponent<ChessBoard2DView>();
        ChessBoard2DView[] views = Object.FindObjectsOfType<ChessBoard2DView>(true);
        if (view == null && views.Length == 1) view = views[0];
        if (view == null) {
            EditorUtility.DisplayDialog("Create 2D View Test", views.Length == 0
                ? "There's no 2D board view in the open scene. Create one with Tools > Chess Board > Create 2D Board "
                    + "View."
                : "There are several 2D board views in the scene. Select the one to test, then try again.", "OK");
            return;
        }
        if (!EnsureProgramAssets("Create 2D View Test", TestScriptPath, TestProgramPath)) return;

        Board2DTest[] olds = Object.FindObjectsOfType<Board2DTest>(true);
        GameObject root = new GameObject("Board2DTest");
        Undo.RegisterCreatedObjectUndo(root, "Create 2D View Test");
        Board2DTest test = UdonSharpUndo.AddComponent<Board2DTest>(root);
        test.view = view;
        test.holder = ChessBoard2DViewBuilder.HolderOf(view);
        BoardTest[] boardTests = Object.FindObjectsOfType<BoardTest>(true);
        test.boardTest = boardTests.Length > 0 ? boardTests[0] : null;
        ViewTest[] viewTests = Object.FindObjectsOfType<ViewTest>(true);
        test.viewTest = viewTests.Length > 0 ? viewTests[0] : null;
        ArchiveTest[] archiveTests = Object.FindObjectsOfType<ArchiveTest>(true);
        test.archiveTest = archiveTests.Length > 0 ? archiveTests[0] : null;
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
        Debug.Log("[Board2DTestBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " Board2DTest for "
            + view.gameObject.name + " (" + view.boards.Length + " board(s), "
            + (test.holder != null ? "in a holder" : "no holder") + "), waiting for "
            + (test.boardTest != null ? "BoardTest" : "no BoardTest") + ", "
            + (test.viewTest != null ? "ViewTest" : "no ViewTest") + " and "
            + (test.archiveTest != null ? "ArchiveTest" : "no ArchiveTest") + ". Save the scene.");
    }

    // Turns the selected 2D view's Synced on or off, or every 2D view's in the scene when none is selected, for the
    // several-client checks of both cases (12.13, 12.14).
    [MenuItem("Tools/Chess Board/Toggle 2D View Synced")]
    static void ToggleSynced() {
        ChessBoard2DView selected = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<ChessBoard2DView>()
            : null;
        ChessBoard2DView[] views = selected != null
            ? new[] { selected }
            : Object.FindObjectsOfType<ChessBoard2DView>(true);
        for (int i = 0; i < views.Length; i++) {
            Undo.RecordObject(views[i], "Toggle 2D View Synced");
            views[i].synced = !views[i].synced;
            UdonSharpEditorUtility.CopyProxyToUdon(views[i]);
            ChessBoard2DViewHolder holder = ChessBoard2DViewBuilder.HolderOf(views[i]);
            if (holder != null) ChessBoard2DViewBuilder.MatchObjectSync(holder);
            EditorSceneManager.MarkSceneDirty(views[i].gameObject.scene);
            Debug.Log("[Board2DTestBuilder] " + views[i].gameObject.name + ": Synced " + (views[i].synced ? "on" : "off")
                + (holder != null ? " (its grip's VRCObjectSync matched)" : "") + ". Save the scene.");
        }
    }
}
