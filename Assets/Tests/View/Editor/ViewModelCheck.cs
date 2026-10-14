using SiggiChessBoard;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Puts the ViewTest's board view in the configurations of PLAN.md 12.2, to run the tests with them: its Board and
// Squares replaced by a cube, and the view (with its UI, so the UI's panels stay around the table) scaled. Meant for
// a run without saving: Revert Scene reloads the scene from disk afterwards.
// Menu: Tools > Chess Board > Model Check. Plain editor C#, not Udon.
public static class ViewModelCheck {
    [MenuItem("Tools/Chess Board/Model Check/Replace Board Model With Cube")]
    static void ReplaceModel() {
        ChessBoardView view = FindView();
        if (view == null) return;
        Transform root = view.transform;
        float width = 0.48f;
        for (int i = 0; i < 2; i++) {
            Transform part = root.Find(i == 0 ? "Board" : "Squares");
            if (part == null) continue;
            if (i == 0) width = part.localScale.x;
            Undo.DestroyObjectImmediate(part.gameObject);
        }
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(cube, "Replace Board Model");
        cube.name = "Custom Board";
        Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
        cube.transform.SetParent(root, false);
        // The playing surface at the view's y = 0.
        cube.transform.localPosition = new Vector3(0, -0.01f, 0);
        cube.transform.localScale = new Vector3(width, 0.02f, width);
        EditorSceneManager.MarkSceneDirty(cube.scene);
        Debug.Log("[ViewModelCheck] " + root.name + ": Board and Squares replaced by a cube.");
    }

    [MenuItem("Tools/Chess Board/Model Check/Scale View 0.5")]
    static void ScaleHalf() {
        Scale(0.5f);
    }

    [MenuItem("Tools/Chess Board/Model Check/Scale View 2")]
    static void ScaleDouble() {
        Scale(2);
    }

    [MenuItem("Tools/Chess Board/Model Check/Scale View 1")]
    static void ScaleOne() {
        Scale(1);
    }

    [MenuItem("Tools/Chess Board/Model Check/Revert Scene")]
    static void Revert() {
        string path = EditorSceneManager.GetActiveScene().path;
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        Debug.Log("[ViewModelCheck] reloaded " + path + " from disk.");
    }

    static void Scale(float scale) {
        ChessBoardView view = FindView();
        if (view == null) return;
        Undo.RecordObject(view.transform, "Scale View");
        view.transform.localScale = Vector3.one * scale;
        ChessBoardUI[] uis = Object.FindObjectsOfType<ChessBoardUI>(true);
        for (int i = 0; i < uis.Length; i++) {
            if (uis[i].board != view.board) continue;
            Undo.RecordObject(uis[i].transform, "Scale View");
            uis[i].transform.localScale = Vector3.one * scale;
        }
        EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        Debug.Log("[ViewModelCheck] " + view.name + " and its UI scaled to " + scale + ".");
    }

    static ChessBoardView FindView() {
        ViewTest test = Object.FindObjectOfType<ViewTest>(true);
        if (test == null || test.view == null) {
            Debug.LogError("[ViewModelCheck] no ViewTest with a view in the scene.");
            return null;
        }
        return test.view;
    }
}
