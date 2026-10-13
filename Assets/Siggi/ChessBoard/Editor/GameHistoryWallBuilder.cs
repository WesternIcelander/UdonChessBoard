using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the history wall (docs/archive.md, GameHistoryWall.cs) in the open scene for its only GameArchive: a
// world-space canvas with a title and one row per game the archive holds, in Columns columns, oldest first down each
// column. Placed to the left of the boards (along -x), facing them; an existing wall is replaced in place (same parent, position, rotation
// and scale). Menu: Tools > Chess Board > Create History Wall. Plain editor C#, not Udon.
public static class GameHistoryWallBuilder {
    const string WallScriptPath = "Assets/Siggi/ChessBoard/GameHistoryWall.cs";
    const string WallProgramPath = "Assets/Siggi/ChessBoard/GameHistoryWall.asset";

    const int Columns = 2;
    const float CanvasWidth = 3000;
    const float Margin = 40;
    const float TitleHeight = 100;
    const float RowHeight = 64;
    const float ColumnGap = 40;
    const int TitleFontSize = 60;
    const int RowFontSize = 30;
    // Where a new wall goes: this far to the left of the leftmost board (along -x, beside the spawn's view, which
    // looks along +z from behind the boards), its centre this high above them.
    const float WallDistance = 2.5f;
    const float WallHeight = 1.6f;

    [MenuItem("Tools/Chess Board/Create History Wall")]
    static void Create() {
        GameArchive[] archives = Object.FindObjectsOfType<GameArchive>(true);
        if (archives.Length != 1) {
            EditorUtility.DisplayDialog("Create History Wall", archives.Length == 0
                ? "There's no GameArchive in the open scene. Create one with Tools > Chess Board > Create Game Archive."
                : "There are several GameArchives in the scene; the wall needs exactly one.", "OK");
            return;
        }
        if (!EnsureProgramAsset()) return;
        GameArchive archive = archives[0];
        int rowCount = archive.holders.Length * GameArchiveHolder.Capacity;
        int rowsPerColumn = (rowCount + Columns - 1) / Columns;
        float canvasHeight = Margin * 2 + TitleHeight + Margin / 2 + rowsPerColumn * RowHeight;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        GameHistoryWall[] olds = Object.FindObjectsOfType<GameHistoryWall>(true);
        GameObject root = new GameObject("GameHistoryWall");
        Undo.RegisterCreatedObjectUndo(root, "Create History Wall");
        root.layer = LayerMask.NameToLayer("Default");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(CanvasWidth, canvasHeight);
        rootRect.localScale = Vector3.one * 0.001f;
        rootRect.position = NewWallPosition();
        // Facing the boards: seen from them, looking along -x.
        rootRect.rotation = Quaternion.Euler(0, -90, 0);
        root.AddComponent<CanvasScaler>();
        Image background = root.AddComponent<Image>();
        background.color = new Color(0.1f, 0.1f, 0.12f, 1f);
        background.raycastTarget = false;

        GameHistoryWall wall = UdonSharpUndo.AddComponent<GameHistoryWall>(root);
        wall.archive = archive;
        wall.title = NewText(root, "Title", font, TitleFontSize, new Vector2(Margin, Margin),
            new Vector2(CanvasWidth - 2 * Margin, TitleHeight));
        wall.title.fontStyle = FontStyle.Bold;
        wall.title.text = "Previous games";
        float columnWidth = (CanvasWidth - 2 * Margin - (Columns - 1) * ColumnGap) / Columns;
        float rowsTop = Margin + TitleHeight + Margin / 2;
        wall.rows = new Text[rowCount];
        for (int i = 0; i < rowCount; i++) {
            int column = i / rowsPerColumn;
            int row = i % rowsPerColumn;
            wall.rows[i] = NewText(root, "Row " + (i + 1), font, RowFontSize,
                new Vector2(Margin + column * (columnWidth + ColumnGap), rowsTop + row * RowHeight),
                new Vector2(columnWidth, RowHeight));
            wall.rows[i].horizontalOverflow = HorizontalWrapMode.Wrap;
            wall.rows[i].verticalOverflow = VerticalWrapMode.Truncate;
        }
        UdonSharpEditorUtility.CopyProxyToUdon(wall);

        for (int i = 0; i < olds.Length; i++) {
            if (i == 0) {
                root.transform.SetParent(olds[i].transform.parent, false);
                root.transform.SetSiblingIndex(olds[i].transform.GetSiblingIndex());
                rootRect.position = olds[i].transform.position;
                rootRect.rotation = olds[i].transform.rotation;
                rootRect.localScale = olds[i].transform.localScale;
            }
            Undo.DestroyObjectImmediate(olds[i].gameObject);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[GameHistoryWallBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " the history wall with "
            + rowCount + " rows. Move it where you like and save the scene.");
    }

    // Left of the leftmost board, level with the boards' middle along z; beside the origin when there are none.
    static Vector3 NewWallPosition() {
        ChessBoard[] boards = Object.FindObjectsOfType<ChessBoard>(true);
        if (boards.Length == 0) return new Vector3(-WallDistance, WallHeight, 0);
        Vector3 sum = Vector3.zero;
        float minX = float.MaxValue;
        for (int i = 0; i < boards.Length; i++) {
            Vector3 position = boards[i].transform.position;
            sum += position;
            if (position.x < minX) minX = position.x;
        }
        Vector3 middle = sum / boards.Length;
        return new Vector3(minX - WallDistance, middle.y + WallHeight, middle.z);
    }

    // A text at a position from the canvas's top left, in canvas pixels.
    static Text NewText(GameObject parent, string name, Font font, int size, Vector2 topLeft, Vector2 extent) {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent.transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
        rect.sizeDelta = extent;
        Text text = child.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    // As ChessBoardViewBuilder.EnsureProgramAsset.
    static bool EnsureProgramAsset() {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(WallProgramPath) != null) return true;
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(WallScriptPath);
        if (script == null) {
            Debug.LogError("[GameHistoryWallBuilder] " + WallScriptPath + " not found.");
            return false;
        }
        UdonSharpProgramAsset program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        program.sourceCsScript = script;
        AssetDatabase.CreateAsset(program, WallProgramPath);
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        EditorUtility.DisplayDialog("Create History Wall", "Created the wall's program asset (" + WallProgramPath
            + "). Once UdonSharp has finished compiling, run Tools > Chess Board > Create History Wall again.", "OK");
        return false;
    }
}
