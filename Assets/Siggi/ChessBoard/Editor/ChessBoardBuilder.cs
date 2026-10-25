using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ChessBuilderUtil;

// The Chess Board prefab (step 14.2, docs/prefabs.md): a ChessBoard (the logical board, docs/architecture.md) with a
// ChessPgnImporter of its own as a child, made when missing and otherwise updated in place (EnsureBoardPrefab).
// Tools > Chess Board > Create Board places another instance of it in the open scene, beside the last board: the next
// free board number, the scene's arbiter and archive. Its physical view and debug view are built afterwards with the
// new board selected (Create Board View, Create Debug View). Tools > Chess Board > Update Board Prefab updates the
// prefab and makes the scene's boards built before 14.2 instances of it, keeping their objects and values. Plain
// editor C#, not Udon.
public static class ChessBoardBuilder {
    const string BoardScriptPath = "Assets/Siggi/ChessBoard/ChessBoard.cs";
    const string BoardProgramPath = "Assets/Siggi/ChessBoard/ChessBoard.asset";
    const string ImporterScriptPath = "Assets/Siggi/ChessBoard/ChessPgnImporter.cs";
    const string ImporterProgramPath = "Assets/Siggi/ChessBoard/ChessPgnImporter.asset";
    const string PrefabsFolder = "Assets/Siggi/ChessBoard/Prefabs";
    const string BoardPrefabPath = PrefabsFolder + "/Chess Board.prefab";
    const string ImporterName = "ChessPgnImporter";
    // How far along x a new board stands from the last one: clear of its table and its debug view.
    const float Spacing = 2.5f;

    [MenuItem("Tools/Chess Board/Create Board")]
    static void Create() {
        if (!EnsureProgramAssets("Create Board", BoardScriptPath, BoardProgramPath, ImporterScriptPath,
            ImporterProgramPath)) return;
        string prefabNote = EnsureBoardPrefab();
        ChessBoard[] boards = Object.FindObjectsOfType<ChessBoard>(true);
        ChessArbiter arbiter = null;
        GameArchive archive = null;
        ChessBoard last = null;
        int number = 0;
        for (int i = 0; i < boards.Length; i++) {
            if (arbiter == null) arbiter = boards[i].arbiter;
            if (archive == null) archive = boards[i].archive;
            if (boards[i].boardNumber >= number) {
                number = boards[i].boardNumber + 1;
                last = boards[i];
            }
        }
        if (arbiter == null) {
            ChessArbiter[] arbiters = Object.FindObjectsOfType<ChessArbiter>(true);
            if (arbiters.Length > 0) arbiter = arbiters[0];
        }
        if (archive == null) {
            GameArchive[] archives = Object.FindObjectsOfType<GameArchive>(true);
            if (archives.Length > 0) archive = archives[0];
        }
        if (arbiter != null && number >= arbiter.boardCount) {
            Debug.LogWarning("[ChessBoardBuilder] Board number " + number + " has no per-board overrides: the arbiter's "
                + "Board Count is " + arbiter.boardCount + ". Raise it to give the new board its own.");
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoardPrefabPath);
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(root, "Create Board");
        root.name = "ChessBoard " + (number + 1);
        if (last != null) {
            root.transform.SetParent(last.transform.parent, false);
            root.transform.SetSiblingIndex(last.transform.GetSiblingIndex() + 1);
            root.transform.position = last.transform.position + new Vector3(Spacing, 0, 0);
            root.transform.rotation = last.transform.rotation;
        }
        ChessBoard board = root.GetComponent<ChessBoard>();
        board.arbiter = arbiter;
        board.boardNumber = number;
        board.archive = archive;
        RecordInstance(board);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[ChessBoardBuilder] Created " + root.name + " (board " + number + ", arbiter "
            + (arbiter != null ? arbiter.gameObject.name : "none") + ", archive "
            + (archive != null ? archive.gameObject.name : "none") + prefabNote + "). With it selected, run Tools > "
            + "Chess Board > Create Board View and Create Debug View, then save the scene.");
    }

    [MenuItem("Tools/Chess Board/Update Board Prefab")]
    static void UpdateBoardPrefab() {
        if (!EnsureProgramAssets("Update Board Prefab", BoardScriptPath, BoardProgramPath, ImporterScriptPath,
            ImporterProgramPath)) return;
        string note = EnsureBoardPrefab();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoardPrefabPath);
        foreach (ChessBoard board in Object.FindObjectsOfType<ChessBoard>(true)) {
            note += ConvertToInstance(board, prefab);
        }
        Debug.Log("[ChessBoardBuilder] Board prefab: " + (note.Length > 0 ? note.Substring(2) : "unchanged"));
    }

    // Makes the Chess Board prefab when it's missing: a ChessBoard on "Chess Board", with board number 0, no arbiter
    // or archive (a prefab can't point into a scene), and a ChessPgnImporter child as its importer. Otherwise adds the
    // importer if the prefab lacks one, keeping everything already there (rule 1 of docs/compatibility.md). Returns a
    // note for the log ("" when nothing changed).
    static string EnsureBoardPrefab() {
        string note = "";
        if (!AssetDatabase.IsValidFolder(PrefabsFolder)) AssetDatabase.CreateFolder("Assets/Siggi/ChessBoard", "Prefabs");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(BoardPrefabPath) == null) {
            GameObject made = new GameObject("Chess Board");
            UdonSharpUndo.AddComponent<ChessBoard>(made);
            PrefabUtility.SaveAsPrefabAsset(made, BoardPrefabPath);
            Object.DestroyImmediate(made);
            note += ", made " + BoardPrefabPath;
        }
        GameObject root = PrefabUtility.LoadPrefabContents(BoardPrefabPath);
        root.name = "Chess Board";
        ChessBoard board = root.GetComponent<ChessBoard>();
        if (board.importer == null) {
            Transform child = root.transform.Find(ImporterName);
            ChessPgnImporter importer = child != null ? child.GetComponent<ChessPgnImporter>() : null;
            if (importer == null) {
                GameObject importerObject = new GameObject(ImporterName);
                importerObject.transform.SetParent(root.transform, false);
                importer = UdonSharpUndo.AddComponent<ChessPgnImporter>(importerObject);
                note += ", a " + ImporterName + " in the board prefab";
            }
            board.importer = importer;
            UdonSharpEditorUtility.CopyProxyToUdon(importer);
        }
        // Saved every time, so the prefab holds every field of the current code.
        UdonSharpEditorUtility.CopyProxyToUdon(board);
        PrefabUtility.SaveAsPrefabAsset(root, BoardPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        return note;
    }

    // Makes a scene board that isn't from the prefab an instance of it (a board built by Create Board before 14.2):
    // matched by hierarchy, so the board and its importer stay the same objects (whatever points at them keeps doing
    // so) and their values (board number, arbiter, archive, listeners) become the instance's overrides. A board whose
    // object holds more than the prefab keeps it, as added objects. Returns a note for the log.
    static string ConvertToInstance(ChessBoard board, GameObject prefab) {
        if (PrefabUtility.IsPartOfPrefabInstance(board.gameObject)) return "";
        ConvertToPrefabInstanceSettings settings = new ConvertToPrefabInstanceSettings {
            objectMatchMode = ObjectMatchMode.ByHierarchy,
            componentsNotMatchedBecomesOverride = true,
            gameObjectsNotMatchedBecomesOverride = true,
            recordPropertyOverridesOfMatches = true,
            changeRootNameToAssetName = false,
            logInfo = true,
        };
        PrefabUtility.ConvertToPrefabInstance(board.gameObject, prefab, settings, InteractionMode.UserAction);
        RecordInstance(board);
        if (board.importer != null) RecordInstance(board.importer);
        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        return ", " + board.gameObject.name + " made an instance";
    }

    // Copies a placed behaviour's fields to its Udon data and records both as the instance's overrides.
    static void RecordInstance(UdonSharpBehaviour behaviour) {
        UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
        PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
        PrefabUtility.RecordPrefabInstancePropertyModifications(
            UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour));
        EditorUtility.SetDirty(behaviour);
    }
}
