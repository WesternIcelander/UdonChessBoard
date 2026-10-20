using SiggiChessBoard;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;
using static ChessBuilderUtil;

// Builds the history wall (docs/archive.md, GameHistoryWall.cs) in the open scene for its only GameArchive: a
// world-space canvas with a title and one row per game the archive holds, in Columns columns, oldest first down each
// column. Each row is a UI Button (step 10.2b) whose GameHistoryRow picks its game, so the canvas takes the pointer
// (VRCUiShape, a trigger collider). Placed to the left of the boards (along -x), facing them; an existing wall is
// replaced in place (same parent, position, rotation and scale). Every board UI on the archive's boards gets the
// wall for its Load it here button. Menu: Tools > Chess Board > Create History Wall. Plain editor C#, not Udon.
public static class GameHistoryWallBuilder {
    const string WallScriptPath = "Assets/Siggi/ChessBoard/GameHistoryWall.cs";
    const string WallProgramPath = "Assets/Siggi/ChessBoard/GameHistoryWall.asset";
    const string RowScriptPath = "Assets/Siggi/ChessBoard/GameHistoryRow.cs";
    const string RowProgramPath = "Assets/Siggi/ChessBoard/GameHistoryRow.asset";

    const int Columns = 2;
    const float CanvasWidth = 3000;
    const float Margin = 40;
    const float TitleHeight = 100;
    const float RowHeight = 64;
    const float ColumnGap = 40;
    const int TitleFontSize = 60;
    const int RowFontSize = 30;
    // A row's text inset from its button's edges, in canvas pixels.
    const float RowInset = 12;
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
        if (!EnsureProgramAssets("Create History Wall", WallScriptPath, WallProgramPath, RowScriptPath,
            RowProgramPath)) return;
        GameArchive archive = archives[0];
        int rowCount = archive.holders.Length * GameArchiveHolder.Capacity;
        int rowsPerColumn = (rowCount + Columns - 1) / Columns;
        float canvasHeight = Margin * 2 + TitleHeight + Margin / 2 + rowsPerColumn * RowHeight;

        TMP_FontAsset font = DefaultFont();
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
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<VRCUiShape>();
        AddTriggerCollider(root);
        Image background = root.AddComponent<Image>();
        background.color = new Color(0.1f, 0.1f, 0.12f, 1f);
        background.raycastTarget = false;

        GameHistoryWall wall = UdonSharpUndo.AddComponent<GameHistoryWall>(root);
        wall.archive = archive;
        wall.title = NewText(root, "Title", font, TitleFontSize, new Vector2(Margin, Margin),
            new Vector2(CanvasWidth - 2 * Margin, TitleHeight));
        wall.title.fontStyle = FontStyles.Bold;
        wall.title.text = "Previous games";
        float columnWidth = (CanvasWidth - 2 * Margin - (Columns - 1) * ColumnGap) / Columns;
        float rowsTop = Margin + TitleHeight + Margin / 2;
        wall.rows = new TextMeshProUGUI[rowCount];
        wall.rowTargets = new GameHistoryRow[rowCount];
        for (int i = 0; i < rowCount; i++) {
            int column = i / rowsPerColumn;
            int row = i % rowsPerColumn;
            Vector2 topLeft = new Vector2(Margin + column * (columnWidth + ColumnGap), rowsTop + row * RowHeight);
            AddRow(root, wall, i, font, topLeft, new Vector2(columnWidth, RowHeight));
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
            // ArchiveTest's wall, and anything else pointing at the old one.
            ChessBoardViewBuilder.RepointReferences(olds[i], wall);
            Undo.DestroyObjectImmediate(olds[i].gameObject);
        }
        int uis = WireUIs(wall);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[GameHistoryWallBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " the history wall with "
            + rowCount + " rows, for " + uis + " board UI(s). Move it where you like and save the scene.");
    }

    // A row: a button over the row's area (clear until pointed at) with a GameHistoryRow calling the wall's
    // SelectRow, and the row's text inside it.
    static void AddRow(GameObject root, GameHistoryWall wall, int index, TMP_FontAsset font, Vector2 topLeft, Vector2 extent) {
        GameObject rowObject = new GameObject("Row " + (index + 1), typeof(RectTransform));
        rowObject.layer = root.layer;
        RectTransform rect = rowObject.GetComponent<RectTransform>();
        rect.SetParent(root.transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
        rect.sizeDelta = extent;
        Image image = rowObject.AddComponent<Image>();
        image.color = Color.white;
        Button button = rowObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        // The tint multiplies the white image: clear normally, faint while pointed at or pressed.
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(1, 1, 1, 0);
        colors.highlightedColor = new Color(1, 1, 1, 0.12f);
        colors.pressedColor = new Color(1, 1, 1, 0.22f);
        colors.selectedColor = new Color(1, 1, 1, 0);
        colors.disabledColor = new Color(1, 1, 1, 0);
        colors.colorMultiplier = 1;
        button.colors = colors;

        GameHistoryRow target = UdonSharpUndo.AddComponent<GameHistoryRow>(rowObject);
        target.wall = wall;
        target.index = index;
        UdonSharpEditorUtility.CopyProxyToUdon(target);
        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
        UnityEventTools.AddStringPersistentListener(button.onClick, new UnityAction<string>(udon.SendCustomEvent),
            "Press");

        TextMeshProUGUI text = NewText(rowObject, "Label", font, RowFontSize, new Vector2(RowInset, 0),
            new Vector2(extent.x - 2 * RowInset, extent.y));
        wall.rows[index] = text;
        wall.rowTargets[index] = target;
    }

    // Gives every board UI on the wall's archive's boards the wall (steps 10.2b, 12.1). Returns how many.
    static int WireUIs(GameHistoryWall wall) {
        int count = 0;
        ChessBoardUI[] uis = Object.FindObjectsOfType<ChessBoardUI>(true);
        for (int i = 0; i < uis.Length; i++) {
            ChessBoardUI ui = uis[i];
            if (ui.board == null || ui.board.archive != wall.archive) continue;
            Undo.RecordObject(ui, "Create History Wall");
            ui.historyWall = wall;
            UdonSharpEditorUtility.CopyProxyToUdon(ui);
            count++;
        }
        return count;
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
}
