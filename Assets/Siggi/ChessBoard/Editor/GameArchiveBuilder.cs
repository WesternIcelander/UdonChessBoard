using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ChessBuilderUtil;

// Adds the instance's archive (InstanceGameArchive, docs/archive.md) to the open scene: an instance of the Game Archive
// prefab (step 14.5, docs/prefabs.md; BuildArchive makes it when missing, EnsureArchivePrefab), the archive with
// HolderCount synced holders and the local-only recent holder as children, listed in the arbiter's Archives (step
// 13.10; each board's Archive field before). An existing archive is replaced in place, whatever pointed at it (the
// arbiter, the history wall, tests) pointed at the new one. Tools > Chess Board > Update Archive Prefab updates the
// prefab and makes the scene's archives built before 14.5 instances of it, keeping their objects and values. Tools >
// Chess Board > Create Personal Game Archive (step 13.10) adds the local player's own archive (PersonalGameArchive)
// beside it and lists it on the arbiter too. Menu: Tools > Chess Board > Create Game Archive. Plain editor C#, not
// Udon.
public static class GameArchiveBuilder {
    const int HolderCount = 10;
    const string ArchiveScriptPath = "Assets/Siggi/ChessBoard/InstanceGameArchive.cs";
    const string ArchiveProgramPath = "Assets/Siggi/ChessBoard/InstanceGameArchive.asset";
    const string HolderScriptPath = "Assets/Siggi/ChessBoard/GameArchiveHolder.cs";
    const string HolderProgramPath = "Assets/Siggi/ChessBoard/GameArchiveHolder.asset";
    const string PersonalScriptPath = "Assets/Siggi/ChessBoard/PersonalGameArchive.cs";
    const string PersonalProgramPath = "Assets/Siggi/ChessBoard/PersonalGameArchive.asset";
    const string ArchivePrefabName = "Game Archive";
    const string ArchivePrefabPath = PrefabsFolder + "/" + ArchivePrefabName + ".prefab";

    [MenuItem("Tools/Chess Board/Create Game Archive")]
    static void Create() {
        if (!EnsureArchiveProgramAssets("Create Game Archive")) return;
        string prefabNote = EnsureArchivePrefab();

        InstanceGameArchive[] olds = Object.FindObjectsOfType<InstanceGameArchive>(true);
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(ArchivePrefabPath));
        Undo.RegisterCreatedObjectUndo(root, "Create Game Archive");
        root.name = "GameArchive";
        InstanceGameArchive archive = root.GetComponent<InstanceGameArchive>();

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
        int arbiters = AddToArbiters(archive);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[GameArchiveBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " the GameArchive with "
            + archive.holders.Length + " holders of " + GameArchiveHolder.Capacity + " games, on " + arbiters
            + " arbiter(s)" + (olds.Length > 0 ? " (" + repointed + " behaviour(s) pointed at the new archive)" : "")
            + prefabNote + ". Save the scene.");
    }

    // ---- The personal archive (step 13.10) ----

    // Adds a PersonalGameArchive (capacity and key prefix at their defaults) as the instance archive's next sibling,
    // or replaces the scene's in place (keeping its capacity and key prefix), and lists it in the arbiter's Archives.
    // Its prefab is step 14.6's.
    [MenuItem("Tools/Chess Board/Create Personal Game Archive")]
    static void CreatePersonal() {
        if (!EnsureProgramAssets("Create Personal Game Archive", PersonalScriptPath, PersonalProgramPath)) return;
        PersonalGameArchive[] olds = Object.FindObjectsOfType<PersonalGameArchive>(true);
        GameObject root = new GameObject("PersonalGameArchive");
        Undo.RegisterCreatedObjectUndo(root, "Create Personal Game Archive");
        PersonalGameArchive archive = UdonSharpUndo.AddComponent<PersonalGameArchive>(root);
        InstanceGameArchive[] instances = Object.FindObjectsOfType<InstanceGameArchive>(true);
        if (instances.Length > 0) {
            root.transform.SetParent(instances[0].transform.parent, false);
            root.transform.SetSiblingIndex(instances[0].transform.GetSiblingIndex() + 1);
        }
        int repointed = 0;
        for (int i = 0; i < olds.Length; i++) {
            if (i == 0) {
                root.transform.SetParent(olds[i].transform.parent, false);
                root.transform.SetSiblingIndex(olds[i].transform.GetSiblingIndex());
                archive.capacity = olds[i].capacity;
                archive.keyPrefix = olds[i].keyPrefix;
            }
            repointed += ChessBoardViewBuilder.RepointReferences(olds[i], archive);
            Undo.DestroyObjectImmediate(olds[i].gameObject);
        }
        UdonSharpEditorUtility.CopyProxyToUdon(archive);
        int arbiters = AddToArbiters(archive);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[GameArchiveBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " the PersonalGameArchive ("
            + archive.capacity + " games), on " + arbiters + " arbiter(s)"
            + (olds.Length > 0 ? " (" + repointed + " behaviour(s) pointed at the new archive)" : "")
            + ". Save the scene.");
    }

    // Lists archive in every arbiter's Archives that doesn't list it yet, in an empty entry if there is one (a
    // destroyed archive's), as an override on a Chess Arbiter instance. Returns how many arbiters there are.
    static int AddToArbiters(GameArchive archive) {
        ChessArbiter[] arbiters = Object.FindObjectsOfType<ChessArbiter>(true);
        for (int i = 0; i < arbiters.Length; i++) {
            SerializedObject fields = new SerializedObject(arbiters[i]);
            SerializedProperty list = fields.FindProperty("archives");
            bool listed = false;
            int empty = -1;
            for (int j = 0; j < list.arraySize; j++) {
                Object entry = list.GetArrayElementAtIndex(j).objectReferenceValue;
                if (entry == archive) listed = true;
                if (entry == null && empty < 0) empty = j;
            }
            if (listed) continue;
            if (empty < 0) {
                empty = list.arraySize;
                list.arraySize++;
            }
            list.GetArrayElementAtIndex(empty).objectReferenceValue = archive;
            fields.ApplyModifiedProperties();
            RecordInstance(arbiters[i]);
        }
        return arbiters.Length;
    }

    // ---- The Game Archive prefab (step 14.5, docs/prefabs.md) ----

    [MenuItem("Tools/Chess Board/Update Archive Prefab")]
    static void UpdateArchivePrefab() {
        if (!EnsureArchiveProgramAssets("Update Archive Prefab")) return;
        string note = EnsureArchivePrefab();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArchivePrefabPath);
        foreach (InstanceGameArchive archive in Object.FindObjectsOfType<InstanceGameArchive>(true)) {
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

    // The archive (the prefab's contents): an InstanceGameArchive with its HolderCount synced holders and the
    // local-only recent holder as children, each holder's archive pointing back.
    static GameObject BuildArchive() {
        GameObject root = new GameObject(ArchivePrefabName);
        InstanceGameArchive archive = UdonSharpUndo.AddComponent<InstanceGameArchive>(root);
        GameArchiveHolder[] holders = new GameArchiveHolder[HolderCount];
        for (int i = 0; i < HolderCount; i++) holders[i] = AddHolder(root, "Holder " + i, archive, false);
        archive.holders = holders;
        archive.recent = AddHolder(root, "Recent (local only)", archive, true);
        UdonSharpEditorUtility.CopyProxyToUdon(archive);
        return root;
    }

    static GameArchiveHolder AddHolder(GameObject root, string name, InstanceGameArchive archive, bool localOnly) {
        GameObject child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        GameArchiveHolder holder = UdonSharpUndo.AddComponent<GameArchiveHolder>(child);
        holder.archive = archive;
        holder.localOnly = localOnly;
        UdonSharpEditorUtility.CopyProxyToUdon(holder);
        return holder;
    }
}
