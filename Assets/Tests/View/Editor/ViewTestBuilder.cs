using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Adds ViewTest (PLAN.md section 9) to the open scene for the scene's board view: the board, the view, the board's UI
// (step 12.1), the scene's BoardTest to wait for, an importer of its own (a child), the two PGNs and a second piece
// set (steps 12.3 and 12.10, a child: BuildTestPieceSet). An existing ViewTest is replaced.
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
        test.testPieceSet = BuildTestPieceSet(root.transform);
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

    // ---- The second piece set (step 12.3) ----

    const string TestModelsFolder = "Assets/Tests/View/Models";
    // Where each test model's origin is, from the generated piece's base centre, in the model's own (doubled) units.
    static readonly Vector3 TestOrigin = new Vector3(0.012f, 0.03f, -0.02f);

    // "Test Chess Pieces" under parent: the generated pieces made awkward (TestMesh: turned 90° onto their backs,
    // twice the size, the origin off their base, two material slots), with templates turned, scaled and moved back
    // so the pieces stand as the default ones do. One set (step 12.10), set up as White's; the view test puts it on
    // either side or both, and back.
    static ChessPieceSet BuildTestPieceSet(Transform parent) {
        Mesh[] meshes = ChessBoardViewBuilder.PieceMeshes();
        Mesh[] testMeshes = new Mesh[meshes.Length];
        for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) testMeshes[type] = TestMesh(meshes[type]);
        Material[] white = { TestMaterial("Test White Body", new Color(0.95f, 0.9f, 0.75f)),
            TestMaterial("Test White Top", new Color(0.9f, 0.6f, 0.2f)) };
        // Undoes TestMesh: turned back up, halved, and moved so the base centre is on the square's centre.
        Quaternion rotation = Quaternion.Euler(-90, 0, 0);
        Vector3 offset = -(rotation * (TestOrigin * 0.5f));
        ChessPieceSet set = ChessBoardViewBuilder.BuildPieceSet("Test Chess Pieces", parent, testMeshes, white, offset,
            rotation, Vector3.one * 0.5f);
        set.transform.localPosition = new Vector3(0, 0, 0.3f);
        return set;
    }

    // The mesh asset "Test <name>" made from a generated piece, if missing: each vertex doubled, turned 90° about x
    // (lying on its back) and moved by TestOrigin; submesh 0 its lower half, 1 its upper half (by the piece's height).
    static Mesh TestMesh(Mesh source) {
        string path = TestModelsFolder + "/Test " + source.name + ".asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        if (!AssetDatabase.IsValidFolder(TestModelsFolder)) AssetDatabase.CreateFolder("Assets/Tests/View", "Models");
        Vector3[] vertices = source.vertices;
        int[] triangles = source.triangles;
        float middle = source.bounds.center.y;
        Quaternion turn = Quaternion.Euler(90, 0, 0);
        Vector3[] moved = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++) moved[i] = turn * (vertices[i] * 2) + TestOrigin;
        var lower = new System.Collections.Generic.List<int>();
        var upper = new System.Collections.Generic.List<int>();
        for (int i = 0; i + 2 < triangles.Length; i += 3) {
            float y = (vertices[triangles[i]].y + vertices[triangles[i + 1]].y + vertices[triangles[i + 2]].y) / 3;
            var part = y < middle ? lower : upper;
            part.Add(triangles[i]);
            part.Add(triangles[i + 1]);
            part.Add(triangles[i + 2]);
        }
        mesh = new Mesh { name = "Test " + source.name };
        mesh.vertices = moved;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(lower, 0);
        mesh.SetTriangles(upper, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Material TestMaterial(string name, Color color) {
        string path = TestModelsFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        if (!AssetDatabase.IsValidFolder(TestModelsFolder)) AssetDatabase.CreateFolder("Assets/Tests/View", "Models");
        material = new Material(Shader.Find("Standard"));
        material.color = color;
        AssetDatabase.CreateAsset(material, path);
        return material;
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
