using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ChessBuilderUtil;

// Adds the instance's GameArchive (docs/archive.md) to the open scene: the archive with HolderCount synced holders and
// the local-only recent holder as children, and every ChessBoard's Archive field set to it. An existing archive is
// replaced in place. Menu: Tools > Chess Board > Create Game Archive. Plain editor C#, not Udon.
public static class GameArchiveBuilder {
    const int HolderCount = 10;
    const string ArchiveScriptPath = "Assets/Siggi/ChessBoard/GameArchive.cs";
    const string ArchiveProgramPath = "Assets/Siggi/ChessBoard/GameArchive.asset";
    const string HolderScriptPath = "Assets/Siggi/ChessBoard/GameArchiveHolder.cs";
    const string HolderProgramPath = "Assets/Siggi/ChessBoard/GameArchiveHolder.asset";

    [MenuItem("Tools/Chess Board/Create Game Archive")]
    static void Create() {
        if (!EnsureProgramAssets("Create Game Archive", ArchiveScriptPath, ArchiveProgramPath, HolderScriptPath,
            HolderProgramPath)) return;

        GameArchive[] olds = Object.FindObjectsOfType<GameArchive>(true);
        GameObject root = new GameObject("GameArchive");
        Undo.RegisterCreatedObjectUndo(root, "Create Game Archive");
        GameArchive archive = UdonSharpUndo.AddComponent<GameArchive>(root);

        GameArchiveHolder[] holders = new GameArchiveHolder[HolderCount];
        for (int i = 0; i < HolderCount; i++) holders[i] = AddHolder(root, "Holder " + i, archive, false);
        archive.holders = holders;
        archive.recent = AddHolder(root, "Recent (local only)", archive, true);
        UdonSharpEditorUtility.CopyProxyToUdon(archive);

        for (int i = 0; i < olds.Length; i++) {
            if (i == 0) {
                root.transform.SetParent(olds[i].transform.parent, false);
                root.transform.SetSiblingIndex(olds[i].transform.GetSiblingIndex());
                root.transform.localPosition = olds[i].transform.localPosition;
            }
            Undo.DestroyObjectImmediate(olds[i].gameObject);
        }

        // As ChessDebugViewBuilder.EnsureImporter: only the archive field is set on each board's proxy, then copied.
        ChessBoard[] boards = Object.FindObjectsOfType<ChessBoard>(true);
        for (int i = 0; i < boards.Length; i++) {
            SerializedObject boardFields = new SerializedObject(boards[i]);
            boardFields.FindProperty("archive").objectReferenceValue = archive;
            boardFields.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(boards[i]);
            PrefabUtility.RecordPrefabInstancePropertyModifications(
                UdonSharpEditorUtility.GetBackingUdonBehaviour(boards[i]));
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[GameArchiveBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " the GameArchive with "
            + HolderCount + " holders of " + GameArchiveHolder.Capacity + " games, for " + boards.Length
            + " board(s). Save the scene.");
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
