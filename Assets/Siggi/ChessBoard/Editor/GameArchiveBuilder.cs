using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ChessBuilderUtil;

// Adds the instance's GameArchive (docs/archive.md) to the open scene: an instance of the Game Archive prefab (step
// 14.5, docs/prefabs.md; BuildArchive makes it when missing, EnsureArchivePrefab), the archive with HolderCount synced
// holders and the local-only recent holder as children, and every ChessBoard's Archive field set to it. An existing
// archive is replaced in place, whatever pointed at it (boards, the history wall, tests) pointed at the new one.
// Tools > Chess Board > Update Archive Prefab updates the prefab and makes the scene's archives built before 14.5
// instances of it, keeping their objects and values. Menu: Tools > Chess Board > Create Game Archive. Plain editor C#,
// not Udon.
public static class GameArchiveBuilder {
    const int HolderCount = 10;
    const string ArchiveScriptPath = "Assets/Siggi/ChessBoard/GameArchive.cs";
    const string ArchiveProgramPath = "Assets/Siggi/ChessBoard/GameArchive.asset";
    const string HolderScriptPath = "Assets/Siggi/ChessBoard/GameArchiveHolder.cs";
    const string HolderProgramPath = "Assets/Siggi/ChessBoard/GameArchiveHolder.asset";
    const string ArchivePrefabName = "Game Archive";
    const string ArchivePrefabPath = PrefabsFolder + "/" + ArchivePrefabName + ".prefab";

    [MenuItem("Tools/Chess Board/Create Game Archive")]
    static void Create() {
        if (!EnsureArchiveProgramAssets("Create Game Archive")) return;
        string prefabNote = EnsureArchivePrefab();

        GameArchive[] olds = Object.FindObjectsOfType<GameArchive>(true);
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(ArchivePrefabPath));
        Undo.RegisterCreatedObjectUndo(root, "Create Game Archive");
        root.name = "GameArchive";
        GameArchive archive = root.GetComponent<GameArchive>();

        int repointed = 0;
        for (int i = 0; i < olds.Length; i++) {
            if (i == 0) {
                root.transform.SetParent(olds[i].transform.parent, false);
                root.transform.SetSiblingIndex(olds[i].transform.GetSiblingIndex());
                root.transform.localPosition = olds[i].transform.localPosition;
            }
            repointed += ChessBoardViewBuilder.RepointReferences(olds[i], archive);
            Undo.DestroyObjectImmediate(olds[i].gameObject);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(root);
        PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);

        // As ChessDebugViewBuilder.EnsureImporter: only the archive field is set on each board's proxy, then copied, an
        // override on a Chess Board instance.
        ChessBoard[] boards = Object.FindObjectsOfType<ChessBoard>(true);
        for (int i = 0; i < boards.Length; i++) {
            SerializedObject boardFields = new SerializedObject(boards[i]);
            boardFields.FindProperty("archive").objectReferenceValue = archive;
            boardFields.ApplyModifiedProperties();
            RecordInstance(boards[i]);
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[GameArchiveBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " the GameArchive with "
            + archive.holders.Length + " holders of " + GameArchiveHolder.Capacity + " games, for " + boards.Length
            + " board(s)" + (olds.Length > 0 ? " (" + repointed + " behaviour(s) pointed at the new archive)" : "")
            + prefabNote + ". Save the scene.");
    }

    // ---- The Game Archive prefab (step 14.5, docs/prefabs.md) ----

    [MenuItem("Tools/Chess Board/Update Archive Prefab")]
    static void UpdateArchivePrefab() {
        if (!EnsureArchiveProgramAssets("Update Archive Prefab")) return;
        string note = EnsureArchivePrefab();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArchivePrefabPath);
        foreach (GameArchive archive in Object.FindObjectsOfType<GameArchive>(true)) {
            if (ConvertToInstance(archive.gameObject, prefab)) {
                note += ", " + archive.gameObject.name + " made an instance";
            }
        }
        Debug.Log("[GameArchiveBuilder] Archive prefab: " + (note.Length > 0 ? note.Substring(2) : "unchanged"));
    }

    static bool EnsureArchiveProgramAssets(string menuItem) {
        return EnsureProgramAssets(menuItem, ArchiveScriptPath, ArchiveProgramPath, HolderScriptPath,
            HolderProgramPath);
    }

    // Makes the Game Archive prefab when it's missing (BuildArchive), and otherwise saves it again in place. Returns a
    // note for the log ("" when it was there).
    static string EnsureArchivePrefab() {
        return EnsurePrefab(ArchivePrefabPath, ArchivePrefabName, BuildArchive);
    }

    // The archive (the prefab's contents): a GameArchive with its HolderCount synced holders and the local-only recent
    // holder as children, each holder's archive pointing back.
    static GameObject BuildArchive() {
        GameObject root = new GameObject(ArchivePrefabName);
        GameArchive archive = UdonSharpUndo.AddComponent<GameArchive>(root);
        GameArchiveHolder[] holders = new GameArchiveHolder[HolderCount];
        for (int i = 0; i < HolderCount; i++) holders[i] = AddHolder(root, "Holder " + i, archive, false);
        archive.holders = holders;
        archive.recent = AddHolder(root, "Recent (local only)", archive, true);
        UdonSharpEditorUtility.CopyProxyToUdon(archive);
        return root;
    }

    static GameArchiveHolder AddHolder(GameObject root, string name, GameArchive archive, bool localOnly) {
        GameObject child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        GameArchiveHolder holder = UdonSharpUndo.AddComponent<GameArchiveHolder>(child);
        holder.archive = archive;
        holder.localOnly = localOnly;
        UdonSharpEditorUtility.CopyProxyToUdon(holder);
        return holder;
    }
}
