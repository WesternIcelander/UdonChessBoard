using SiggiChessBoard;
using SiggiUSTools;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ChessBuilderUtil;

// Adds ViewTest (PLAN.md section 9) to the open scene for the scene's board view: the board, the view, the board's UI
// (step 12.1), the scene's BoardTest to wait for, an importer of its own (a child), the two PGNs and a second piece
// set (steps 12.3 and 13.6, a child: BuildTestPieceSet) and a third (Test Colours), both listed on the board's arbiter
// after its own (ListTestSets), and the language packs (step 13.5): the test pack is added
// to the Chess Language after the package's own, so English stays the fallback; the other boards' UIs and a world
// I18n of its own (step 14.10, a child: BuildWorldLanguage; an I18nLock keeps the sanity check off its field). An
// existing ViewTest is replaced.
// Menu: Tools > Chess Board > Create View Test. Plain editor C#, not Udon.
public static class ViewTestBuilder {
    const string TestScriptPath = "Assets/Tests/View/ViewTest.cs";
    const string TestProgramPath = "Assets/Tests/View/ViewTest.asset";
    const string Chess960Path = "Assets/Tests/PGN/chess960.pgn.txt";
    const string PromotionsPath = "Assets/Tests/PGN/promotions.pgn.txt";
    const string TestLanguagePath = "Assets/Tests/View/test-language.json";
    const string WorldLanguagePath = "Assets/Tests/View/test-world-language.json";

    [MenuItem("Tools/Chess Board/Create View Test")]
    static void Create() {
        ChessBoardView view = FindView();
        if (view == null) return;
        if (!EnsureProgramAssets("Create View Test", TestScriptPath, TestProgramPath)) return;
        ChessBoardUI ui = null;
        ChessBoardUI[] uis = Object.FindObjectsOfType<ChessBoardUI>(true);
        for (int i = 0; i < uis.Length && ui == null; i++) {
            if (uis[i].board == view.board) ui = uis[i];
        }
        if (ui == null) {
            EditorUtility.DisplayDialog("Create View Test", "The board has no ChessBoardUI. Place the Chess Board "
                + "UI prefab with its Board set to the board, or test a Chess Table's board.", "OK");
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
        ChessPieceSet colourSet = BuildColourPieceSet(root.transform);
        test.languagePack = AssetDatabase.LoadAssetAtPath<TextAsset>(LanguagePackPath);
        test.testLanguagePack = AssetDatabase.LoadAssetAtPath<TextAsset>(TestLanguagePath);
        AddTestLanguagePack(EnsureChessLanguage(root.scene), test.testLanguagePack);
        var others = new System.Collections.Generic.List<ChessBoardUI>();
        foreach (ChessBoardUI other in uis) {
            if (other != ui) others.Add(other);
        }
        test.otherUIs = others.ToArray();
        test.worldI18n = BuildWorldLanguage(root.transform);
        // worldI18n is an I18n field too, which the I18n sanity check would point at the Chess Language (the closest):
        // the lock keeps it as set here.
        Undo.AddComponent<I18nLock>(root);
        UdonSharpEditorUtility.CopyProxyToUdon(test);

        for (int i = 0; i < olds.Length; i++) {
            if (i == 0) {
                root.transform.SetParent(olds[i].transform.parent, false);
                root.transform.SetSiblingIndex(olds[i].transform.GetSiblingIndex());
            }
            // ArchiveTest waits for the ViewTest it was given: point it at the new one.
            RepointReferences(olds[i], test);
            Undo.DestroyObjectImmediate(olds[i].gameObject);
        }
        string setNote = ListTestSets(view.board.arbiter, new[] { test.testPieceSet, colourSet });
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[ViewTestBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " ViewTest for "
            + view.gameObject.name + (test.boardTest != null ? ", waiting for " + test.boardTest.gameObject.name
                : ", with no BoardTest to wait for") + setNote + ". Save the scene.");
    }

    // "World Language (test)" under parent: a world's own I18n (step 14.10), main, with one pack of another language
    // (View/test-world-language.json, x-chess-test). Under the test's object, not beside the chess objects at the
    // scene's root, so the I18n sanity check still finds the Chess Language closest to them.
    static I18n BuildWorldLanguage(Transform parent) {
        GameObject child = new GameObject("World Language (test)");
        child.transform.SetParent(parent, false);
        I18n i18n = UdonSharpUndo.AddComponent<I18n>(child);
        SerializedObject serialized = new SerializedObject(i18n);
        serialized.FindProperty("mainI18n").boolValue = true;
        SerializedProperty packs = serialized.FindProperty("languagePacks");
        packs.arraySize = 1;
        packs.GetArrayElementAtIndex(0).objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<TextAsset>(WorldLanguagePath);
        serialized.ApplyModifiedProperties();
        UdonSharpEditorUtility.CopyProxyToUdon(i18n);
        return i18n;
    }

    // Adds the test language pack to the Chess Language's packs, after the others, unless it's there.
    static void AddTestLanguagePack(I18n language, TextAsset pack) {
        SerializedObject serialized = new SerializedObject(language);
        SerializedProperty packs = serialized.FindProperty("languagePacks");
        for (int i = 0; i < packs.arraySize; i++) {
            if (packs.GetArrayElementAtIndex(i).objectReferenceValue == pack) return;
        }
        packs.arraySize++;
        packs.GetArrayElementAtIndex(packs.arraySize - 1).objectReferenceValue = pack;
        serialized.ApplyModifiedProperties();
        // An override on the Chess Language instance (step 14.10), as a world adds a pack.
        RecordInstance(language);
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
                ? "There's no ChessBoardView in the open scene. Place a Chess Table prefab."
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
    // so the pieces stand as the default ones do. Black has templates of its own (step 13.6): the same meshes with
    // materials of their own, set up facing White (White's turned half round). Its promotion pieces are White's only,
    // so Black's fall back on them.
    static ChessPieceSet BuildTestPieceSet(Transform parent) {
        Mesh[] meshes = PieceMeshes();
        Mesh[] testMeshes = new Mesh[meshes.Length];
        for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) testMeshes[type] = TestMesh(meshes[type]);
        Material[] white = { TestMaterial("Test White Body", new Color(0.95f, 0.9f, 0.75f)),
            TestMaterial("Test White Top", new Color(0.9f, 0.6f, 0.2f)) };
        Material[] black = { TestMaterial("Test Black Body", new Color(0.15f, 0.12f, 0.1f)),
            TestMaterial("Test Black Top", new Color(0.55f, 0.2f, 0.7f)) };
        // Undoes TestMesh: turned back up, halved, and moved so the base centre is on the square's centre.
        Quaternion rotation = Quaternion.Euler(-90, 0, 0);
        Vector3 offset = -(rotation * (TestOrigin * 0.5f));
        // Its promotion templates (step 12.11) take the test pieces' transforms and a material of their own.
        Material ghost = TestMaterial("Test Promotion Ghost", new Color(0.3f, 0.4f, 0.95f));
        ChessPieceSet set = BuildPieceSet("Test Chess Pieces", parent, testMeshes, white, offset,
            rotation, Vector3.one * 0.5f, ghost, testMeshes, black);
        set.transform.localPosition = new Vector3(0, 0, 0.3f);
        return set;
    }

    // "Test Colours" under parent: the generated pieces in colours of their own, Black's only materials (step 13.6),
    // so the several-client checks can tell it from the others at a glance.
    static ChessPieceSet BuildColourPieceSet(Transform parent) {
        Material[] white = { TestMaterial("Test Red", new Color(0.85f, 0.2f, 0.15f)) };
        Material[] black = { TestMaterial("Test Blue", new Color(0.15f, 0.3f, 0.85f)) };
        Material ghost = TestMaterial("Test Promotion Ghost", new Color(0.3f, 0.4f, 0.95f));
        ChessPieceSet set = BuildPieceSet("Test Colours", parent,
            PieceMeshes(), white, Vector3.zero, Quaternion.identity, Vector3.one, ghost, null,
            black);
        set.transform.localPosition = new Vector3(0, 0, 0.6f);
        return set;
    }

    // Lists the test's sets on the arbiter after its own (step 13.6: indexes 1 and 2 for the several-client checks),
    // dropping empty entries (an earlier test's sets, destroyed with it). Nothing when the list is empty: the board's
    // own set comes first, the Chess Arbiter prefab's default (Chess Pieces).
    static string ListTestSets(ChessArbiter arbiter, ChessPieceSet[] testSets) {
        if (arbiter == null) return ", no arbiter to list the test sets on";
        var sets = new System.Collections.Generic.List<ChessPieceSet>();
        if (arbiter.pieceSets != null) {
            foreach (ChessPieceSet set in arbiter.pieceSets) {
                if (set != null) sets.Add(set);
            }
        }
        if (sets.Count == 0) {
            Debug.LogWarning("[ViewTestBuilder] " + arbiter.gameObject.name + " has no piece sets: list the default, "
                + "Chess Pieces (nested in the Chess Arbiter prefab), first, then Create View Test again.");
            return ", the test sets not listed (the arbiter has none)";
        }
        foreach (ChessPieceSet set in testSets) {
            if (!sets.Contains(set)) sets.Add(set);
        }
        Undo.RecordObject(arbiter, "Create View Test");
        arbiter.pieceSets = sets.ToArray();
        UdonSharpEditorUtility.CopyProxyToUdon(arbiter);
        EditorUtility.SetDirty(arbiter);
        return ", the test sets listed on " + arbiter.gameObject.name + " (" + sets.Count + " sets)";
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

    // ---- Building a piece set (laid out as the Chess Pieces prefab is, step 14.1) ----

    const string ModelsFolder = "Assets/Siggi/ChessBoard/Models";
    static readonly string[] TypeNames = { "", "Pawn", "Knight", "Bishop", "Rook", "Queen", "King" };
    // The board view's square (m), and how far apart a set's slots are, in squares.
    const float SquareSize = 0.05f;
    const float SlotSpacing = 1.5f;

    // The package's placeholder piece meshes in Models/, by ChessRules type (entry 0 empty).
    static Mesh[] PieceMeshes() {
        Mesh[] meshes = new Mesh[7];
        for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) {
            meshes[type] = AssetDatabase.LoadAssetAtPath<Mesh>(ModelsFolder + "/" + TypeNames[type] + ".asset");
        }
        return meshes;
    }

    // A ChessPieceSet named name under parent (step 13.6), in rows SlotSpacing squares apart: White's, a slot per
    // piece type holding a one-square guide on the playing surface and the template, its mesh meshes[type] with
    // materials, standing at offset, turned by rotation and scaled by scale (facing +z); White's promotion slots
    // behind it (AddPromotionSlots), with promotionMaterial; and Black's behind those. With blackMeshes, each Black
    // template has its type's mesh and blackMaterials, at White's offset and rotation turned half round about the
    // square's centre (facing -z); without, only a renderer with blackMaterials and no mesh, so Black wears White's
    // mesh turned (the view's fallback); with neither, the Black row is left out.
    static ChessPieceSet BuildPieceSet(string name, Transform parent, Mesh[] meshes, Material[] materials,
            Vector3 offset, Quaternion rotation, Vector3 scale, Material promotionMaterial, Mesh[] blackMeshes,
            Material[] blackMaterials) {
        GameObject root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, "Create Piece Set");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(0, 0, -2 * SquareSize * SlotSpacing);
        ChessPieceSet set = UdonSharpUndo.AddComponent<ChessPieceSet>(root);
        Transform[] templates = new Transform[7];
        Transform[] blackTemplates = new Transform[7];
        Quaternion turn = Quaternion.Euler(0, 180, 0);
        for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) {
            Transform model = AddSlot(root.transform, "White " + TypeNames[type],
                new Vector3((type - 1) * SlotSpacing * SquareSize, 0, 0), meshes[type], materials);
            model.localPosition = offset;
            model.localRotation = rotation;
            model.localScale = scale;
            templates[type] = model;
            if (blackMeshes == null && blackMaterials == null) continue;
            Transform black = AddSlot(root.transform, "Black " + TypeNames[type],
                new Vector3((type - 1) * SlotSpacing * SquareSize, 0, 2 * SlotSpacing * SquareSize),
                blackMeshes != null ? blackMeshes[type] : null, blackMaterials);
            if (blackMeshes != null) {
                black.localPosition = turn * offset;
                black.localRotation = turn * rotation;
                black.localScale = scale;
            }
            blackTemplates[type] = black;
        }
        set.whitePawn = templates[ChessRules.Pawn];
        set.whiteKnight = templates[ChessRules.Knight];
        set.whiteBishop = templates[ChessRules.Bishop];
        set.whiteRook = templates[ChessRules.Rook];
        set.whiteQueen = templates[ChessRules.Queen];
        set.whiteKing = templates[ChessRules.King];
        set.blackPawn = blackTemplates[ChessRules.Pawn];
        set.blackKnight = blackTemplates[ChessRules.Knight];
        set.blackBishop = blackTemplates[ChessRules.Bishop];
        set.blackRook = blackTemplates[ChessRules.Rook];
        set.blackQueen = blackTemplates[ChessRules.Queen];
        set.blackKing = blackTemplates[ChessRules.King];
        UdonSharpEditorUtility.CopyProxyToUdon(set);
        AddPromotionSlots(set, promotionMaterial);
        return set;
    }

    // Adds to set its White promotion slots (step 12.11), in a row behind White's pieces (+z, SlotSpacing squares),
    // each behind its piece's slot: a guide and a template with the mesh and local transform of the set's own White
    // template of that type, and material on every slot of the mesh. Black's promotion pieces fall back on these.
    static void AddPromotionSlots(ChessPieceSet set, Material material) {
        Transform[] templates = new Transform[7];
        for (int type = ChessRules.Knight; type <= ChessRules.Queen; type++) {
            Transform piece = set._Template(ChessRules.White, type);
            Vector3 place = piece.parent.localPosition;
            place.z += SlotSpacing * SquareSize;
            Mesh mesh = piece.GetComponent<MeshFilter>().sharedMesh;
            Material[] materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            Transform model = AddSlot(set.transform, "White Promotion " + TypeNames[type], place, mesh, materials);
            model.localPosition = piece.localPosition;
            model.localRotation = piece.localRotation;
            model.localScale = piece.localScale;
            templates[type] = model;
        }
        set.whitePromotionKnight = templates[ChessRules.Knight];
        set.whitePromotionBishop = templates[ChessRules.Bishop];
        set.whitePromotionRook = templates[ChessRules.Rook];
        set.whitePromotionQueen = templates[ChessRules.Queen];
        UdonSharpEditorUtility.CopyProxyToUdon(set);
    }

    // A slot named name under setRoot at place: a one-square guide on the playing surface (the package's Piece Set
    // Guide mesh and material) and a "Model" with mesh (no MeshFilter for none) and materials (no MeshRenderer for
    // none), unmoved. Returns the model.
    static Transform AddSlot(Transform setRoot, string name, Vector3 place, Mesh mesh, Material[] materials) {
        GameObject slot = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(slot, "Create Piece Set");
        slot.transform.SetParent(setRoot, false);
        slot.transform.localPosition = place;
        GameObject guide = new GameObject("Guide");
        guide.transform.SetParent(slot.transform, false);
        guide.AddComponent<MeshFilter>().sharedMesh =
            AssetDatabase.LoadAssetAtPath<Mesh>(ModelsFolder + "/Piece Set Guide.asset");
        MeshRenderer guideRenderer = guide.AddComponent<MeshRenderer>();
        guideRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ModelsFolder + "/Piece Set Guide.mat");
        guideRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        GameObject model = new GameObject("Model");
        model.transform.SetParent(slot.transform, false);
        if (mesh != null) model.AddComponent<MeshFilter>().sharedMesh = mesh;
        if (materials != null) model.AddComponent<MeshRenderer>().sharedMaterials = materials;
        return model.transform;
    }

    // Points every other behaviour's field holding the old test (ArchiveTest's) at the new one, so replacing it
    // doesn't leave them on a destroyed object.
    static void RepointReferences(UdonSharpBehaviour oldTest, UdonSharpBehaviour newTest) {
        foreach (UdonSharpBehaviour behaviour in Object.FindObjectsOfType<UdonSharpBehaviour>(true)) {
            if (behaviour == newTest || behaviour.transform.IsChildOf(oldTest.transform)) continue;
            SerializedObject serialized = new SerializedObject(behaviour);
            SerializedProperty property = serialized.GetIterator();
            bool found = false;
            while (property.Next(true)) {
                if (property.propertyType == SerializedPropertyType.ObjectReference
                    && property.objectReferenceValue == oldTest) {
                    property.objectReferenceValue = newTest;
                    found = true;
                }
            }
            if (!found) continue;
            serialized.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
        }
    }
}
