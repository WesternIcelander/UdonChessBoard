using SiggiChessBoard;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Step 12.17's check that every UI text is TextMeshPro: on entering Play Mode (and from Tools > Chess Board > Check
// Legacy Text), it looks for a UnityEngine.UI.Text or InputField anywhere in the open scenes, inactive objects
// included, leaving out ClientSim's own menu (under __ClientSimSystem, added in Play Mode), and checks that each
// board UI's PGN fields are multi-line (ViewTest checks the rest, but Udon can't read a field's line type). Udon
// can't search the whole scene, so this is plain editor C#, run beside the Play Mode tests.
// Logs "[LegacyTextCheck] PASS: …", or "[LegacyTextCheck] FAIL: …" as an error naming each object.
[InitializeOnLoad]
public static class LegacyTextCheck {
    static LegacyTextCheck() {
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode) Run();
        };
    }

    [MenuItem("Tools/Chess Board/Check Legacy Text")]
    public static void Run() {
        string found = "";
        int count = 0;
        foreach (Text text in Object.FindObjectsOfType<Text>(true)) {
            if (IsClientSim(text.transform)) continue;
            found += "\n  Text: " + PathOf(text.transform);
            count++;
        }
        foreach (InputField field in Object.FindObjectsOfType<InputField>(true)) {
            if (IsClientSim(field.transform)) continue;
            found += "\n  InputField: " + PathOf(field.transform);
            count++;
        }
        if (count == 0) {
            Debug.Log("[LegacyTextCheck] PASS: no UI Text or InputField in the scene");
        } else {
            Debug.LogError("[LegacyTextCheck] FAIL: " + count + " UI Text or InputField component(s) left:" + found);
        }

        string wrong = "";
        ChessBoardUI[] uis = Object.FindObjectsOfType<ChessBoardUI>(true);
        foreach (ChessBoardUI ui in uis) {
            wrong += CheckMultiLine(ui.pgnField);
            wrong += CheckMultiLine(ui.pgnPasteField);
        }
        if (wrong == "") {
            Debug.Log("[LegacyTextCheck] PASS: " + uis.Length + " board UI(s)' PGN fields multi-line");
        } else {
            Debug.LogError("[LegacyTextCheck] FAIL: PGN fields not multi-line:" + wrong);
        }
    }

    static string CheckMultiLine(TMP_InputField field) {
        if (field == null) return "\n  (a board UI's field is empty)";
        if (field.lineType == TMP_InputField.LineType.MultiLineNewline) return "";
        return "\n  " + PathOf(field.transform) + ": " + field.lineType;
    }

    static bool IsClientSim(Transform transform) {
        return transform.root.name.StartsWith("__ClientSim");
    }

    static string PathOf(Transform transform) {
        string path = transform.name;
        for (Transform parent = transform.parent; parent != null; parent = parent.parent) {
            path = parent.name + "/" + path;
        }
        return path;
    }
}
