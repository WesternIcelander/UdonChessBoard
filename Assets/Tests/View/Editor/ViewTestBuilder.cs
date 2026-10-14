using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Adds ViewTest (PLAN.md section 9) to the open scene for the scene's board view: the board, the view, the board's UI
// (step 12.1), the scene's BoardTest to wait for, an importer of its own (a child) and the two PGNs. An existing
// ViewTest is replaced.
// Menu: Tools > Chess Board > Create View Test. Plain editor C#, not Udon.
public static class ViewTestBuilder {
    const string TestScriptPath = "Assets/Tests/View/ViewTest.cs";
    const string TestProgramPath = "Assets/Tests/View/ViewTest.asset";
    const string Chess960Path = "Assets/Tests/PGN/chess960.pgn.txt";
    const string PromotionsPath = "Assets/Tests/PGN/promotions.pgn.txt";

    [MenuItem("Tools/Chess Board/Create View Test")]
    static void Create() {
        ChessBoardView view = FindView();
        if (view == null) return;
        if (!EnsureProgramAsset()) return;
        ChessBoardUI ui = null;
        ChessBoardUI[] uis = Object.FindObjectsOfType<ChessBoardUI>(true);
        for (int i = 0; i < uis.Length && ui == null; i++) {
            if (uis[i].board == view.board) ui = uis[i];
        }
        if (ui == null) {
            EditorUtility.DisplayDialog("Create View Test", "The board has no ChessBoardUI. Build it with Tools > "
                + "Chess Board > Create Board View (select the board first).", "OK");
            return;
        }

        ViewTest[] olds = Object.FindObjectsOfType<ViewTest>(true);
        GameObject root = new GameObject("ViewTest");
        Undo.RegisterCreatedObjectUndo(root, "Create View Test");
        GameObject importerObject = new GameObject("ChessPgnImporter");
        importerObject.transform.SetParent(root.transform, false);
        ChessPgnImporter importer = UdonSharpUndo.AddComponent<ChessPgnImporter>(importerObject);

        ViewTest test = UdonSharpUndo.AddComponent<ViewTest>(root);
        test.board = view.board;
        test.view = view;
        test.ui = ui;
        BoardTest[] boardTests = Object.FindObjectsOfType<BoardTest>(true);
        test.boardTest = boardTests.Length > 0 ? boardTests[0] : null;
        test.importer = importer;
        test.chess960Pgn = AssetDatabase.LoadAssetAtPath<TextAsset>(Chess960Path);
        test.promotionsPgn = AssetDatabase.LoadAssetAtPath<TextAsset>(PromotionsPath);
        UdonSharpEditorUtility.CopyProxyToUdon(test);

        for (int i = 0; i < olds.Length; i++) {
            if (i == 0) {
                root.transform.SetParent(olds[i].transform.parent, false);
                root.transform.SetSiblingIndex(olds[i].transform.GetSiblingIndex());
            }
            // ArchiveTest waits for the ViewTest it was given: point it at the new one.
            ChessBoardViewBuilder.RepointReferences(olds[i], test);
            Undo.DestroyObjectImmediate(olds[i].gameObject);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[ViewTestBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " ViewTest for "
            + view.gameObject.name + (test.boardTest != null ? ", waiting for " + test.boardTest.gameObject.name
                : ", with no BoardTest to wait for") + ". Save the scene.");
    }

    // The view to test: the scene's only one; with several, the one an existing ViewTest has, or else the selected
    // board's (12.1: rebuilding the test for the UI in a scene that already has a second board).
    static ChessBoardView FindView() {
        ChessBoardView[] views = Object.FindObjectsOfType<ChessBoardView>(true);
        ChessBoardView view = views.Length == 1 ? views[0] : null;
        if (view == null) {
            ViewTest[] tests = Object.FindObjectsOfType<ViewTest>(true);
            if (tests.Length > 0 && tests[0].view != null) view = tests[0].view;
        }
        if (view == null && Selection.activeGameObject != null) {
            ChessBoard selected = Selection.activeGameObject.GetComponent<ChessBoard>();
            for (int i = 0; i < views.Length && selected != null; i++) {
                if (views[i].board == selected) view = views[i];
            }
        }
        if (view == null || view.board == null) {
            EditorUtility.DisplayDialog("Create View Test", views.Length == 0
                ? "There's no ChessBoardView in the open scene. Create one with Tools > Chess Board > Create Board View."
                : view == null ? "There are several ChessBoardViews in the scene. Select the board to test, then try "
                    + "again."
                : "The ChessBoardView has no board.", "OK");
            return null;
        }
        return view;
    }

    // As ChessBoardViewBuilder.EnsureProgramAsset.
    static bool EnsureProgramAsset() {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(TestProgramPath) != null) return true;
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(TestScriptPath);
        if (script == null) {
            Debug.LogError("[ViewTestBuilder] " + TestScriptPath + " not found.");
            return false;
        }
        UdonSharpProgramAsset program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        program.sourceCsScript = script;
        AssetDatabase.CreateAsset(program, TestProgramPath);
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        EditorUtility.DisplayDialog("Create View Test", "Created the test's program asset (" + TestProgramPath
            + "). Once UdonSharp has finished compiling, run Tools > Chess Board > Create View Test again.", "OK");
        return false;
    }
}
