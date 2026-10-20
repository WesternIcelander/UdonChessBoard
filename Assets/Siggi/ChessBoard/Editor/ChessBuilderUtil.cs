using TMPro;
using UdonSharp;
using UnityEditor;
using UnityEngine;

// Helpers shared by the Tools > Chess Board builders and the test builders (step 12.18), so a change to one is made
// once.
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

    // Creates the program asset for a script if it's missing; whether it did, after which the menu item has to stop
    // (also when the script is missing, logged with builderName, which stops the build the same way).
    public static bool CreateProgramAssetIfMissing(string scriptPath, string programPath, string builderName) {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath) != null) return false;
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        if (script == null) {
            Debug.LogError("[" + builderName + "] " + scriptPath + " not found.");
            return true;
        }
        UdonSharpProgramAsset program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        program.sourceCsScript = script;
        AssetDatabase.CreateAsset(program, programPath);
        return true;
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

    // TextMeshPro's default font (LiberationSans SDF, from TMP Essential Resources), by the GUID it always has
    // (step 12.17), or TMP's own default when it isn't there.
    public static TMP_FontAsset DefaultFont() {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath("8f586378b4e144a9851e7b34d9b748ee"));
        return font != null ? font : TMP_Settings.defaultFontAsset;
    }
}
