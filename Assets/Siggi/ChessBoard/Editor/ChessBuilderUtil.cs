using SiggiChessBoard;
using SiggiUSTools;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Helpers shared by the Tools > Chess Board builders and the test builders (steps 12.18 and 12.19), so a change to
// one is made once.
public static class ChessBuilderUtil {
    // A UI canvas's collider: a trigger box over the whole canvas, so players walk through it. VRCUiShape uses a box
    // collider the canvas already has (and adds a solid one when there's none); a trigger still takes the pointer.
    public static void AddTriggerCollider(GameObject canvasObject) {
        BoxCollider box = canvasObject.GetComponent<BoxCollider>();
        if (box == null) box = canvasObject.AddComponent<BoxCollider>();
        Vector2 size = canvasObject.GetComponent<RectTransform>().sizeDelta;
        box.center = Vector3.zero;
        box.size = new Vector3(size.x, size.y, 1);
        box.isTrigger = true;
    }

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

    public static void EnsurePrefabsFolder() {
        if (!AssetDatabase.IsValidFolder(PrefabsFolder)) AssetDatabase.CreateFolder("Assets/Siggi/ChessBoard", "Prefabs");
    }

    // Makes the prefab at path when it's missing, from the object build returns (destroyed once saved), and otherwise
    // saves it again in place, so it holds every field of the current code, keeping everything already there (rule 1
    // of docs/compatibility.md); its root named name either way. Returns a note for the log ("" when it was there).
    public static string EnsurePrefab(string path, string name, System.Func<GameObject> build) {
        EnsurePrefabsFolder();
        string note = "";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) {
            GameObject built = build();
            PrefabUtility.SaveAsPrefabAsset(built, path);
            Object.DestroyImmediate(built);
            note = ", made " + path;
        }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        root.name = name;
        CopyProxiesToUdon(root);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        return note;
    }

    // Copies every behaviour's fields under root (a prefab's contents) to its Udon data, so the saved prefab holds
    // every field of the current code.
    public static void CopyProxiesToUdon(GameObject root) {
        foreach (UdonSharpBehaviour behaviour in root.GetComponentsInChildren<UdonSharpBehaviour>(true)) {
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
        }
    }

    // Makes a scene object that isn't from prefab an instance of it (one a builder made before its prefab existed):
    // matched by hierarchy, so the object and its children stay the same objects (whatever points at them keeps doing
    // so) and their values become the instance's overrides. Whatever it holds beyond the prefab is kept, as added
    // objects and components. Whether it was converted (false when it already was an instance).
    public static bool ConvertToInstance(GameObject root, GameObject prefab) {
        if (PrefabUtility.IsPartOfPrefabInstance(root)) return false;
        ConvertToPrefabInstanceSettings settings = new ConvertToPrefabInstanceSettings {
            objectMatchMode = ObjectMatchMode.ByHierarchy,
            componentsNotMatchedBecomesOverride = true,
            gameObjectsNotMatchedBecomesOverride = true,
            recordPropertyOverridesOfMatches = true,
            changeRootNameToAssetName = false,
            logInfo = true,
        };
        PrefabUtility.ConvertToPrefabInstance(root, prefab, settings, InteractionMode.UserAction);
        foreach (UdonSharpBehaviour behaviour in root.GetComponentsInChildren<UdonSharpBehaviour>(true)) {
            RecordInstance(behaviour);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        return true;
    }

    // Copies a placed behaviour's fields to its Udon data and records both as the instance's overrides.
    public static void RecordInstance(UdonSharpBehaviour behaviour) {
        UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
        PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
        PrefabUtility.RecordPrefabInstancePropertyModifications(
            UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour));
        EditorUtility.SetDirty(behaviour);
    }

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

    // A child of parent filling it, inset by offsetMin and offsetMax.
    public static GameObject AddStretched(GameObject parent, string name, Vector2 offsetMin, Vector2 offsetMax) {
        return AddAnchored(parent, name, Vector2.zero, Vector2.one, offsetMin, offsetMax);
    }

    // A child of parent between the anchors (fractions of parent), inset by offsetMin and offsetMax. The anchors are
    // set first: setting them afterwards would move the offsets.
    public static GameObject AddAnchored(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax) {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent.transform, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
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

    // A white, left-aligned text at a position from the parent's top left, in canvas pixels (AddText).
    public static TextMeshProUGUI NewText(GameObject parent, string name, TMP_FontAsset font, float size,
        Vector2 topLeft, Vector2 extent) {
        return AddText(NewChild(parent, name, topLeft, extent), font, size, TextAlignmentOptions.Left, Color.white);
    }

    // TextMeshPro's default font (LiberationSans SDF, from TMP Essential Resources), by the GUID it always has
    // (step 12.17), or TMP's own default when it isn't there.
    public static TMP_FontAsset DefaultFont() {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath("8f586378b4e144a9851e7b34d9b748ee"));
        return font != null ? font : TMP_Settings.defaultFontAsset;
    }

    // ---- Player text (PLAN.md 13.5) ----

    // The package's language pack, and the name of the object holding the chess system's I18n.
    public const string LanguagePackPath = "Assets/Siggi/ChessBoard/Languages/en.json";
    public const string ChessLanguageName = "Chess Language";

    // The chess system's shared I18n in scene: the I18n with the package's language pack, or, when there's none, a new
    // one on its own object (Chess Language) at the scene's root, where the I18n sanity check (I18nSanityChecks)
    // finds it as the closest I18n of every object in the scene that has none nearer, and fills their empty I18n
    // fields with it on build and before Play Mode (the builders leave them empty). Not main: a world's own main I18n
    // switches it with SetLanguageGlobally.
    public static I18n EnsureChessLanguage(Scene scene) {
        TextAsset pack = AssetDatabase.LoadAssetAtPath<TextAsset>(LanguagePackPath);
        foreach (I18n i18n in Object.FindObjectsOfType<I18n>(true)) {
            if (i18n.gameObject.scene != scene) continue;
            SerializedProperty packs = new SerializedObject(i18n).FindProperty("languagePacks");
            for (int i = 0; i < packs.arraySize; i++) {
                if (packs.GetArrayElementAtIndex(i).objectReferenceValue == pack) return i18n;
            }
        }
        GameObject languageObject = new GameObject(ChessLanguageName);
        Undo.RegisterCreatedObjectUndo(languageObject, "Create Chess Language");
        SceneManager.MoveGameObjectToScene(languageObject, scene);
        I18n created = UdonSharpUndo.AddComponent<I18n>(languageObject);
        SerializedObject serialized = new SerializedObject(created);
        SerializedProperty languagePacks = serialized.FindProperty("languagePacks");
        languagePacks.arraySize = 1;
        languagePacks.GetArrayElementAtIndex(0).objectReferenceValue = pack;
        serialized.ApplyModifiedProperties();
        UdonSharpEditorUtility.CopyProxyToUdon(created);
        Debug.Log("[ChessBuilderUtil] Created " + ChessLanguageName + " (the chess system's I18n, with " + LanguagePackPath
            + ") at the scene's root.");
        return created;
    }
}
