using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Adds another ChessBoard (the logical board, docs/architecture.md) to the open scene, beside the last one: the next
// free board number, the scene's arbiter and archive, and a ChessPgnImporter of its own as a child. Its physical view
// and debug view are built afterwards with the new board selected (Create Board View, Create Debug View). Menu:
// Tools > Chess Board > Create Board. Plain editor C#, not Udon.
public static class ChessBoardBuilder {
    const string BoardScriptPath = "Assets/Siggi/ChessBoard/ChessBoard.cs";
    const string BoardProgramPath = "Assets/Siggi/ChessBoard/ChessBoard.asset";
    const string ImporterScriptPath = "Assets/Siggi/ChessBoard/ChessPgnImporter.cs";
    const string ImporterProgramPath = "Assets/Siggi/ChessBoard/ChessPgnImporter.asset";
    // How far along x a new board stands from the last one: clear of its table and its debug view.
    const float Spacing = 2.5f;

    [MenuItem("Tools/Chess Board/Create Board")]
    static void Create() {
        if (!EnsureProgramAssets()) return;
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

        GameObject root = new GameObject("ChessBoard " + (number + 1));
        Undo.RegisterCreatedObjectUndo(root, "Create Board");
        if (last != null) {
            root.transform.SetParent(last.transform.parent, false);
            root.transform.SetSiblingIndex(last.transform.GetSiblingIndex() + 1);
            root.transform.position = last.transform.position + new Vector3(Spacing, 0, 0);
            root.transform.rotation = last.transform.rotation;
        }
        ChessBoard board = UdonSharpUndo.AddComponent<ChessBoard>(root);

        GameObject child = new GameObject("ChessPgnImporter");
        child.transform.SetParent(root.transform, false);
        ChessPgnImporter importer = UdonSharpUndo.AddComponent<ChessPgnImporter>(child);

        board.arbiter = arbiter;
        board.boardNumber = number;
        board.importer = importer;
        board.archive = archive;
        UdonSharpEditorUtility.CopyProxyToUdon(board);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[ChessBoardBuilder] Created " + root.name + " (board " + number + ", arbiter "
            + (arbiter != null ? arbiter.gameObject.name : "none") + ", archive "
            + (archive != null ? archive.gameObject.name : "none") + "). With it selected, run Tools > Chess Board > "
            + "Create Board View and Create Debug View, then save the scene.");
    }

    // As ChessBoardViewBuilder.EnsureProgramAssets.
    static bool EnsureProgramAssets() {
        bool created = CreateProgramAssetIfMissing(BoardScriptPath, BoardProgramPath);
        created |= CreateProgramAssetIfMissing(ImporterScriptPath, ImporterProgramPath);
        if (!created) return true;
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        EditorUtility.DisplayDialog("Create Board", "Created the missing program assets next to " + BoardScriptPath
            + " or " + ImporterScriptPath + ". Once UdonSharp has finished compiling, run Tools > Chess Board > Create "
            + "Board again.", "OK");
        return false;
    }

    static bool CreateProgramAssetIfMissing(string scriptPath, string programPath) {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath) != null) return false;
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        if (script == null) {
            Debug.LogError("[ChessBoardBuilder] " + scriptPath + " not found.");
            return true;
        }
        UdonSharpProgramAsset program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        program.sourceCsScript = script;
        AssetDatabase.CreateAsset(program, programPath);
        return true;
    }
}
