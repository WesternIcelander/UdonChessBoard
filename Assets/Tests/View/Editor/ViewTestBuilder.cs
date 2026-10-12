using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Adds ViewTest (PLAN.md section 9) to the open scene for the scene's board view: the board, the view, the scene's
// BoardTest to wait for, an importer of its own (a child) and the two PGNs. An existing ViewTest is replaced.
// Menu: Tools > Chess Board > Create View Test. Plain editor C#, not Udon.
public static class ViewTestBuilder {
    const string TestScriptPath = "Assets/Tests/View/ViewTest.cs";
    const string TestProgramPath = "Assets/Tests/View/ViewTest.asset";
    const string Chess960Path = "Assets/Tests/PGN/chess960.pgn.txt";
    const string PromotionsPath = "Assets/Tests/PGN/promotions.pgn.txt";

    [MenuItem("Tools/Chess Board/Create View Test")]
    static void Create() {
        ChessBoardView[] views = Object.FindObjectsOfType<ChessBoardView>(true);
        if (views.Length != 1 || views[0].board == null) {
            EditorUtility.DisplayDialog("Create View Test", views.Length == 0
                ? "There's no ChessBoardView in the open scene. Create one with Tools > Chess Board > Create Board View."
                : views.Length > 1 ? "There are several ChessBoardViews in the scene; the test needs exactly one."
                : "The ChessBoardView has no board.", "OK");
            return;
        }
        if (!EnsureProgramAsset()) return;
        ChessBoardView view = views[0];

        ViewTest[] olds = Object.FindObjectsOfType<ViewTest>(true);
        GameObject root = new GameObject("ViewTest");
        Undo.RegisterCreatedObjectUndo(root, "Create View Test");
        GameObject importerObject = new GameObject("ChessPgnImporter");
        importerObject.transform.SetParent(root.transform, false);
        ChessPgnImporter importer = UdonSharpUndo.AddComponent<ChessPgnImporter>(importerObject);

        ViewTest test = UdonSharpUndo.AddComponent<ViewTest>(root);
        test.board = view.board;
        test.view = view;
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
            Undo.DestroyObjectImmediate(olds[i].gameObject);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[ViewTestBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " ViewTest for "
            + view.gameObject.name + (test.boardTest != null ? ", waiting for " + test.boardTest.gameObject.name
                : ", with no BoardTest to wait for") + ". Save the scene.");
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
