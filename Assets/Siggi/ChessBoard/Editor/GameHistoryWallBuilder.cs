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

// Builds the history wall (docs/archive.md, GameHistoryWall.cs) in the open scene for its only GameArchive: a portrait
// world-space canvas with a title and a vertical scroll view holding one two-line entry per game the archive holds,
// newest first from the top (the wall hides those it has no game for and sizes the content to the rest). Each entry
// is a UI Button (step 10.2b) covering both its lines, whose GameHistoryRow picks its game, so the canvas takes the
// pointer (VRCUiShape, a trigger collider); dragging or the wheel over the entries scrolls them. Placed to the left of
// the boards (along -x), facing them; an existing wall is replaced in place (same parent, position, rotation and
// scale). Every board UI on the archive's boards gets the wall for its Load it here button. Menu: Tools > Chess Board
// > Create History Wall. Plain editor C#, not Udon.
public static class GameHistoryWallBuilder {
    const string WallScriptPath = "Assets/Siggi/ChessBoard/GameHistoryWall.cs";
    const string WallProgramPath = "Assets/Siggi/ChessBoard/GameHistoryWall.asset";
    const string RowScriptPath = "Assets/Siggi/ChessBoard/GameHistoryRow.cs";
    const string RowProgramPath = "Assets/Siggi/ChessBoard/GameHistoryRow.asset";

    // The canvas, in canvas pixels (a millimetre each): 1.2 m wide and 1.6 m tall.
    const float CanvasWidth = 1200;
    const float CanvasHeight = 1600;
    const float Margin = 40;
    const float TitleHeight = 100;
    const float EntryHeight = 100;
    const float ScrollbarWidth = 24;
    const int TitleFontSize = 60;
    const int RowFontSize = 32;
    // An entry's text inset from its button's edges, in canvas pixels.
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

        TMP_FontAsset font = DefaultFont();
        GameHistoryWall[] olds = Object.FindObjectsOfType<GameHistoryWall>(true);
        GameObject root = new GameObject("GameHistoryWall");
        Undo.RegisterCreatedObjectUndo(root, "Create History Wall");
        root.layer = LayerMask.NameToLayer("Default");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
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
        // The wall writes every text from the language pack (PLAN.md 13.5), the title too; it's written here as
        // well, so the wall's orientation shows in the editor.
        wall.title.text = "Previous games";
        float listTop = Margin + TitleHeight + Margin / 2;
        GameObject content = BuildList(root, wall, new Vector2(Margin, listTop),
            new Vector2(CanvasWidth - 2 * Margin, CanvasHeight - listTop - Margin));
        wall.entryHeight = EntryHeight;
        wall.rows = new TextMeshProUGUI[rowCount];
        wall.rowTargets = new GameHistoryRow[rowCount];
        float entryWidth = wall.content.sizeDelta.x;
        for (int i = 0; i < rowCount; i++) {
            AddRow(content, wall, i, font, new Vector2(0, i * EntryHeight), new Vector2(entryWidth, EntryHeight));
        }
        // The content as tall as every entry in the editor; the wall sizes it to the games at run time.
        wall.content.sizeDelta = new Vector2(entryWidth, rowCount * EntryHeight);
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
        // The wall's text comes from the shared Chess Language (PLAN.md 13.5), made when the scene has
        // none; the I18n fields are left empty, for the I18n sanity check to fill on build and before Play Mode.
        EnsureChessLanguage(root.scene);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[GameHistoryWallBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " the history wall with "
            + rowCount + " entries, for " + uis + " board UI(s). Move it where you like and save the scene.");
    }

    // The scroll view at topLeft, extent in size: a scroll rect (vertical only) over a masked viewport holding the
    // content, an empty rect from the viewport's top for the entries, and a scrollbar along the right edge, shown
    // while the entries don't fit (as the 2D view's move list, 12.15). Its scrolling calls the wall's _OnScrolled,
    // which makes each turn of the mouse wheel one step (scrollSensitivity, an entry). Sets the wall's scroll,
    // viewport, scrollbar and content, and returns the content.
    static GameObject BuildList(GameObject root, GameHistoryWall wall, Vector2 topLeft, Vector2 extent) {
        GameObject list = NewChild(root, "Games", topLeft, extent);
        Image background = list.AddComponent<Image>();
        background.color = new Color(0.14f, 0.14f, 0.17f, 1f);
        background.raycastTarget = false;
        ScrollRect scroll = list.AddComponent<ScrollRect>();

        float viewportWidth = extent.x - ScrollbarWidth;
        GameObject viewport = NewChild(list, "Viewport", Vector2.zero, new Vector2(viewportWidth, extent.y));
        Image viewportImage = viewport.AddComponent<Image>();
        // Clear but hit by the pointer, so a drag anywhere on the list scrolls it.
        viewportImage.color = new Color(0, 0, 0, 0);
        viewport.AddComponent<RectMask2D>();
        GameObject content = NewChild(viewport, "Content", Vector2.zero, new Vector2(viewportWidth, 0));

        GameObject bar = NewChild(list, "Scrollbar", new Vector2(viewportWidth, 0),
            new Vector2(ScrollbarWidth, extent.y));
        Image track = bar.AddComponent<Image>();
        track.color = new Color(0.22f, 0.22f, 0.26f, 1f);
        GameObject area = AddStretched(bar, "Sliding Area", new Vector2(3, 3), new Vector2(-3, -3));
        GameObject handle = AddStretched(area, "Handle", Vector2.zero, Vector2.zero);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = new Color(0.7f, 0.7f, 0.75f, 1f);
        Scrollbar scrollbar = bar.AddComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        scroll.content = content.GetComponent<RectTransform>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = EntryHeight;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        UnityEventTools.AddStringPersistentListener(scroll.onValueChanged,
            new UnityAction<string>(UdonSharpEditorUtility.GetBackingUdonBehaviour(wall).SendCustomEvent),
            nameof(GameHistoryWall._OnScrolled));
        wall.scroll = scroll;
        wall.scrollbar = scrollbar;
        wall.viewport = viewport.GetComponent<RectTransform>();
        wall.content = content.GetComponent<RectTransform>();
        return content;
    }

    // An entry: a button over both its lines (clear until pointed at) with a GameHistoryRow calling the wall's
    // _SelectRow, and the entry's text inside it. Dragging or the wheel over it scrolls the list (the button takes
    // only clicks, so they reach the scroll rect).
    static void AddRow(GameObject content, GameHistoryWall wall, int index, TMP_FontAsset font, Vector2 topLeft,
        Vector2 extent) {
        GameObject rowObject = NewChild(content, "Entry " + (index + 1), topLeft, extent);
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
            "_Press");

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
            // An override on a Chess Board UI instance (step 14.4).
            RecordInstance(ui);
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
