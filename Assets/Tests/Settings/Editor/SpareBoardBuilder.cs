using SiggiChessBoard;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// SettingsTest's overrides use board numbers 0 to 2, with 2 a number no playing board has, so the arbiter needs a
// Board Count of 3. Since 14.11 the wiring script sets it to the highest board number plus one, inactive boards
// included, so the test scene holds an inactive Chess Board numbered 2 beside the arbiter: Spare Board (SettingsTest).
// Menu: Tools > Chess Board > Create Spare Board; an existing one is kept. Plain editor C#, not Udon.
public static class SpareBoardBuilder {
    const string BoardPrefabPath = "Assets/Siggi/ChessBoard/Prefabs/Chess Board.prefab";
    const string SpareName = "Spare Board (SettingsTest)";
    const int SpareNumber = 2;

    [MenuItem("Tools/Chess Board/Create Spare Board")]
    static void Create() {
        ChessArbiter[] arbiters = Object.FindObjectsOfType<ChessArbiter>(true);
        if (arbiters.Length != 1) {
            Debug.LogError("[SpareBoardBuilder] The scene needs exactly one arbiter, has " + arbiters.Length + ".");
            return;
        }
        foreach (ChessBoard board in Object.FindObjectsOfType<ChessBoard>(true)) {
            if (board.gameObject.name != SpareName) continue;
            Debug.Log("[SpareBoardBuilder] " + SpareName + " is already there (board " + board.boardNumber + ").");
            return;
        }
        ChessArbiter arbiter = arbiters[0];
        GameObject spare = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(BoardPrefabPath), arbiter.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(spare, "Create Spare Board");
        spare.name = SpareName;
        spare.transform.SetParent(arbiter.transform.parent, false);
        spare.transform.SetSiblingIndex(arbiter.transform.GetSiblingIndex() + 1);
        ChessBoard spareBoard = spare.GetComponent<ChessBoard>();
        SerializedObject fields = new SerializedObject(spareBoard);
        fields.FindProperty("arbiter").objectReferenceValue = arbiter;
        fields.FindProperty("boardNumber").intValue = SpareNumber;
        fields.ApplyModifiedProperties();
        UdonSharpEditorUtility.CopyProxyToUdon(spareBoard);
        PrefabUtility.RecordPrefabInstancePropertyModifications(UdonSharpEditorUtility.GetBackingUdonBehaviour(spareBoard));
        spare.SetActive(false);
        EditorSceneManager.MarkSceneDirty(spare.scene);
        Debug.Log("[SpareBoardBuilder] Created " + SpareName + " (board " + SpareNumber + ", inactive). Save the scene.");
    }
}
