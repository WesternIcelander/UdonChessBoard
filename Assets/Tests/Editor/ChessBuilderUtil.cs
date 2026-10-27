using SiggiChessBoard;
using SiggiUSTools;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Udon;

// Helpers shared by the test builders and the editor tests (steps 12.18 and 12.19; moved here from the package's
// Editor/ when the Tools > Chess Board builders were deleted, 14.12), so a change to one is made once.
public static class ChessBuilderUtil {
    // UdonSharp needs a compiled program asset next to a script before its behaviour can be added. The repository
    // has them; any that's missing is created and compiled, after which menuItem (under Tools > Chess Board) has to
    // be run again, as a dialog says. paths are pairs of a script's path and its program asset's. Whether every
    // program asset was there; false also when a script is missing (logged).
    public static bool EnsureProgramAssets(string menuItem, params string[] paths) {
        string created = "";
        for (int i = 0; i + 1 < paths.Length; i += 2) {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(paths[i + 1]) != null) continue;
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(paths[i]);
            if (script == null) {
                Debug.LogError("[" + menuItem + "] " + paths[i] + " not found.");
                return false;
            }
            UdonSharpProgramAsset program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            program.sourceCsScript = script;
            AssetDatabase.CreateAsset(program, paths[i + 1]);
            created += (created.Length > 0 ? ", " : "") + paths[i + 1];
        }
        if (created.Length == 0) return true;
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        EditorUtility.DisplayDialog(menuItem, "Created the missing program assets (" + created + "). Once UdonSharp "
            + "has finished compiling, run Tools > Chess Board > " + menuItem + " again.", "OK");
        return false;
    }

    // The selected ChessBoard, or the scene's only one; otherwise null, after a dialog titled menuItem saying to
    // select the board the what (e.g. "view") is for.
    public static ChessBoard FindBoard(string menuItem, string what) {
        if (Selection.activeGameObject != null) {
            ChessBoard selected = Selection.activeGameObject.GetComponent<ChessBoard>();
            if (selected != null) return selected;
        }
        ChessBoard[] boards = Object.FindObjectsOfType<ChessBoard>(true);
        if (boards.Length == 1) return boards[0];
        EditorUtility.DisplayDialog(menuItem, boards.Length == 0
            ? "There's no ChessBoard in the open scene."
            : "There are several ChessBoards in the scene. Select the one the " + what + " is for, then try again.",
            "OK");
        return null;
    }

    // The object of the scene's T (built earlier) for board, the one whose board (read by boardOf) it is, or null.
    public static GameObject ExistingFor<T>(ChessBoard board, System.Func<T, ChessBoard> boardOf) where T : Component {
        T[] found = Object.FindObjectsOfType<T>(true);
        for (int i = 0; i < found.Length; i++) {
            if (boardOf(found[i]) == board) return found[i].gameObject;
        }
        return null;
    }

    // ---- Prefabs (step 14, docs/prefabs.md) ----

    public const string PrefabsFolder = "Assets/Siggi/ChessBoard/Prefabs";

    // The sync method an UdonBehaviour should have, from its UdonSharp script's UdonBehaviourSyncMode as UdonSharp sets
    // it on scene objects: NoVariableSync (or Any) is Manual beside a manually synced behaviour on the same object and
    // Continuous otherwise. Null for a behaviour that isn't UdonSharp's.
    public static VRC.SDKBase.Networking.SyncType? ExpectedSyncMethod(UdonBehaviour udon) {
        if (!(udon.programSource is UdonSharpProgramAsset program)) return null;
        switch (program.behaviourSyncMode) {
            case BehaviourSyncMode.Manual: return VRC.SDKBase.Networking.SyncType.Manual;
            case BehaviourSyncMode.Continuous: return VRC.SDKBase.Networking.SyncType.Continuous;
            case BehaviourSyncMode.None: return VRC.SDKBase.Networking.SyncType.None;
        }
        foreach (UdonBehaviour other in udon.GetComponents<UdonBehaviour>()) {
            if (other.programSource is UdonSharpProgramAsset otherProgram
                && otherProgram.behaviourSyncMode == BehaviourSyncMode.Manual) {
                return VRC.SDKBase.Networking.SyncType.Manual;
            }
        }
        return VRC.SDKBase.Networking.SyncType.Continuous;
    }

    // Copies a placed behaviour's fields to its Udon data and records both as the instance's overrides.
    public static void RecordInstance(UdonSharpBehaviour behaviour) {
        UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
        PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
        PrefabUtility.RecordPrefabInstancePropertyModifications(
            UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour));
        EditorUtility.SetDirty(behaviour);
    }

    // The holder whose view this is (its parent's, in the Movable 2D Chess Board View, 12.16), or null.
    public static ChessBoard2DViewHolder HolderOf(ChessBoard2DView view) {
        Transform parent = view.transform.parent;
        ChessBoard2DViewHolder holder = parent != null ? parent.GetComponent<ChessBoard2DViewHolder>() : null;
        return holder != null && holder.view == view ? holder : null;
    }

    // ---- UI (the debug view's canvas) ----

    // A child rect at a position from the parent's top left, in canvas pixels.
    public static GameObject NewChild(GameObject parent, string name, Vector2 topLeft, Vector2 size) {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent.transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
        rect.sizeDelta = size;
        return child;
    }

    // A child of parent filling it, inset by offsetMin and offsetMax. The anchors are set first: setting them
    // afterwards would move the offsets.
    public static GameObject AddStretched(GameObject parent, string name, Vector2 offsetMin, Vector2 offsetMax) {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return child;
    }

    // A text on target (a rect, e.g. from NewChild or AddStretched) filling it: wrapping, cut off at its rect, and
    // not taking the pointer, so a button under it does.
    public static TextMeshProUGUI AddText(GameObject target, TMP_FontAsset font, float size,
        TextAlignmentOptions alignment, Color color) {
        TextMeshProUGUI text = target.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Truncate;
        return text;
    }

    // TextMeshPro's default font (LiberationSans SDF, from TMP Essential Resources), by the GUID it always has
    // (step 12.17), or TMP's own default when it isn't there.
    public static TMP_FontAsset DefaultFont() {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath("8f586378b4e144a9851e7b34d9b748ee"));
        return font != null ? font : TMP_Settings.defaultFontAsset;
    }

    // ---- Player text (PLAN.md 13.5) ----

    // The package's language packs (en.json the fallback), and the Chess Language prefab holding the chess system's
    // I18n (step 14.10, docs/prefabs.md).
    public const string LanguagesFolder = "Assets/Siggi/ChessBoard/Languages";
    public const string LanguagePackPath = LanguagesFolder + "/en.json";
    public const string LanguagePrefabPath = PrefabsFolder + "/Chess Language.prefab";

    // The chess system's shared I18n in scene: the I18n with the package's language pack, or, when there's none, a new
    // instance of the Chess Language prefab at the scene's root, where the I18n sanity check (I18nSanityChecks) finds
    // it as the closest I18n of every object in the scene that has none nearer.
    public static I18n EnsureChessLanguage(Scene scene) {
        TextAsset pack = AssetDatabase.LoadAssetAtPath<TextAsset>(LanguagePackPath);
        foreach (I18n i18n in Object.FindObjectsOfType<I18n>(true)) {
            if (i18n.gameObject.scene != scene) continue;
            SerializedProperty packs = new SerializedObject(i18n).FindProperty("languagePacks");
            for (int i = 0; i < packs.arraySize; i++) {
                if (packs.GetArrayElementAtIndex(i).objectReferenceValue == pack) return i18n;
            }
        }
        GameObject languageObject = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(LanguagePrefabPath), scene);
        Undo.RegisterCreatedObjectUndo(languageObject, "Place Chess Language");
        Debug.Log("[ChessBuilderUtil] Placed the Chess Language prefab (the chess system's I18n) at the scene's root.");
        return languageObject.GetComponent<I18n>();
    }

    // Every pack in Languages/, en.json first (the fallback for keys another pack leaves out), the rest by file name:
    // what the Chess Language prefab should hold.
    public static TextAsset[] LanguagePacks() {
        var packs = new System.Collections.Generic.List<TextAsset> {
            AssetDatabase.LoadAssetAtPath<TextAsset>(LanguagePackPath) };
        string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { LanguagesFolder });
        var paths = new System.Collections.Generic.List<string>();
        foreach (string guid in guids) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        paths.Sort(System.StringComparer.Ordinal);
        foreach (string path in paths) {
            if (path == LanguagePackPath || !path.EndsWith(".json")) continue;
            packs.Add(AssetDatabase.LoadAssetAtPath<TextAsset>(path));
        }
        return packs.ToArray();
    }
}
