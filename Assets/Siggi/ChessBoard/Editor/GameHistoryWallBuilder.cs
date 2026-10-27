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

// Builds a history wall (docs/archive.md, GameHistoryWall.cs) in the open scene: Create History Wall for its only
// instance archive (InstanceGameArchive), Create Personal History Wall (step 13.10) for its only PersonalGameArchive.
// Each is a portrait world-space canvas with a title, an Export PGN button beside it, and a vertical scroll view
// holding one two-line entry per game the archive can hold, newest first from the top (the wall hides those it has no
// game for and sizes the content to the rest, and makes more at run time if the archive's capacity grows). Each entry
// is a UI Button (step 10.2b) covering both its lines, whose GameHistoryRow picks its game, so the canvas takes the
// pointer (VRCUiShape, a trigger collider); dragging or the wheel over the entries scrolls them. On the personal wall
// each entry also has a delete button. The PGN box (Export PGN, step 13.10) covers the list's lower half while open.
// The instance's wall goes to the left of the boards (along -x), facing them; the personal one further left beside
// it. An existing wall of the same kind is replaced in place (same parent, position, rotation and scale). Menu: Tools >
// Chess Board > Create History Wall, Create Personal History Wall. Plain editor C#, not Udon.
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
    // The personal wall's delete button on each entry, at its right end.
    const float DeleteWidth = 170;
    const float DeleteHeight = 64;
    // The Export PGN button, at the title's right end.
    const float ExportWidth = 320;
    const float ExportHeight = 80;
    // The PGN box's hint line and text sizes (larger than the board's box, as the wall is read from further away).
    const float PgnHintHeight = 60;
    const float PgnHintSize = 24;
    const float PgnTextSize = 20;
    // Where a new wall goes: this far to the left of the leftmost board (along -x, beside the spawn's view, which
    // looks along +z from behind the boards), its centre this high above them. The personal wall goes PersonalGap
    // beyond the instance's, further left.
    const float WallDistance = 2.5f;
    const float WallHeight = 1.6f;
    const float PersonalGap = 0.3f;

    [MenuItem("Tools/Chess Board/Create History Wall")]
    static void Create() {
        InstanceGameArchive[] archives = Object.FindObjectsOfType<InstanceGameArchive>(true);
        if (archives.Length != 1) {
            EditorUtility.DisplayDialog("Create History Wall", archives.Length == 0
                ? "There's no GameArchive in the open scene. Create one with Tools > Chess Board > Create Game Archive."
                : "There are several GameArchives in the scene; the wall needs exactly one.", "OK");
            return;
        }
        Build(archives[0], false);
    }

    [MenuItem("Tools/Chess Board/Create Personal History Wall")]
    static void CreatePersonal() {
        PersonalGameArchive[] archives = Object.FindObjectsOfType<PersonalGameArchive>(true);
        if (archives.Length != 1) {
            EditorUtility.DisplayDialog("Create Personal History Wall", archives.Length == 0
                ? "There's no PersonalGameArchive in the open scene. Create one with Tools > Chess Board > Create "
                    + "Personal Game Archive."
                : "There are several PersonalGameArchives in the scene; the wall needs exactly one.", "OK");
            return;
        }
        Build(archives[0], true);
    }

    // Builds a wall for archive (the personal kind with its delete buttons and title), replacing the scene's wall of
    // the same kind in place.
    static void Build(GameArchive archive, bool personal) {
        string menuItem = personal ? "Create Personal History Wall" : "Create History Wall";
        if (!EnsureProgramAssets(menuItem, WallScriptPath, WallProgramPath, RowScriptPath, RowProgramPath)) return;
        int rowCount = archive.Capacity();
        if (archive is InstanceGameArchive instance) rowCount = instance.holders.Length * GameArchiveHolder.Capacity;
        if (archive is PersonalGameArchive own) rowCount = Mathf.Max(1, own.capacity);

        TMP_FontAsset font = DefaultFont();
        GameHistoryWall[] olds = WallsOf(personal);
        GameObject root = new GameObject(personal ? "PersonalHistoryWall" : "GameHistoryWall");
        Undo.RegisterCreatedObjectUndo(root, menuItem);
        root.layer = LayerMask.NameToLayer("Default");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
        rootRect.localScale = Vector3.one * 0.001f;
        // Facing the boards: seen from them, looking along -x.
        rootRect.rotation = Quaternion.Euler(0, -90, 0);
        rootRect.position = personal ? NewPersonalWallPosition() : NewWallPosition();
        if (personal) {
            GameHistoryWall[] instanceWalls = WallsOf(false);
            if (instanceWalls.Length > 0) rootRect.rotation = instanceWalls[0].transform.rotation;
        }
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<VRCUiShape>();
        AddTriggerCollider(root);
        Image background = root.AddComponent<Image>();
        background.color = new Color(0.1f, 0.1f, 0.12f, 1f);
        background.raycastTarget = false;

        GameHistoryWall wall = UdonSharpUndo.AddComponent<GameHistoryWall>(root);
        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(wall);
        wall.archive = archive;
        wall.titleKey = personal ? "wall_title_personal" : "wall_title";
        wall.title = NewText(root, "Title", font, TitleFontSize, new Vector2(Margin, Margin),
            new Vector2(CanvasWidth - 3 * Margin - ExportWidth, TitleHeight));
        wall.title.fontStyle = FontStyles.Bold;
        // The wall writes every text from the language pack (PLAN.md 13.5), the title too; it's written here as
        // well, so the wall's orientation shows in the editor.
        wall.title.text = personal ? "My games" : "Previous games";
        wall.exportButton = AddButton(root, font, udon, "Export PGN", nameof(GameHistoryWall._PressExport),
            new Vector2(CanvasWidth - Margin - ExportWidth, Margin + (TitleHeight - ExportHeight) / 2),
            new Vector2(ExportWidth, ExportHeight), 32, out wall.exportLabel);
        wall.exportButton.SetActive(false);
        float listTop = Margin + TitleHeight + Margin / 2;
        float listHeight = CanvasHeight - listTop - Margin;
        GameObject content = BuildList(root, wall, new Vector2(Margin, listTop),
            new Vector2(CanvasWidth - 2 * Margin, listHeight));
        wall.entryHeight = EntryHeight;
        wall.rows = new TextMeshProUGUI[rowCount];
        wall.rowTargets = new GameHistoryRow[rowCount];
        wall.deleteButtons = new GameObject[personal ? rowCount : 0];
        wall.deleteLabels = new TextMeshProUGUI[personal ? rowCount : 0];
        float entryWidth = wall.content.sizeDelta.x;
        for (int i = 0; i < rowCount; i++) {
            AddRow(content, wall, i, font, new Vector2(0, i * EntryHeight), new Vector2(entryWidth, EntryHeight),
                personal);
        }
        // The content as tall as every entry in the editor; the wall sizes it to the games at run time.
        wall.content.sizeDelta = new Vector2(entryWidth, rowCount * EntryHeight);
        AddPgnBox(root, wall, font, new Vector2(Margin, listTop + listHeight / 2),
            new Vector2(CanvasWidth - 2 * Margin, listHeight / 2));
        UdonSharpEditorUtility.CopyProxyToUdon(wall);

        for (int i = 0; i < olds.Length; i++) {
            if (i == 0) {
                root.transform.SetParent(olds[i].transform.parent, false);
                root.transform.SetSiblingIndex(olds[i].transform.GetSiblingIndex());
                rootRect.position = olds[i].transform.position;
                rootRect.rotation = olds[i].transform.rotation;
                rootRect.localScale = olds[i].transform.localScale;
            }
            // ArchiveTest's walls, and anything else pointing at the old one.
            ChessBoardViewBuilder.RepointReferences(olds[i], wall);
            Undo.DestroyObjectImmediate(olds[i].gameObject);
        }
        // The wall's text comes from the shared Chess Language (PLAN.md 13.5), made when the scene has
        // none; the I18n fields are left empty, for the I18n sanity check to fill on build and before Play Mode.
        EnsureChessLanguage(root.scene);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[GameHistoryWallBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " the "
            + (personal ? "personal " : "") + "history wall with " + rowCount + " entries. Move it where you like and "
            + "save the scene.");
    }

    // The scene's walls on the instance's archive, or on a personal one.
    static GameHistoryWall[] WallsOf(bool personal) {
        GameHistoryWall[] walls = Object.FindObjectsOfType<GameHistoryWall>(true);
        int count = 0;
        for (int i = 0; i < walls.Length; i++) {
            if ((walls[i].archive is PersonalGameArchive) == personal) walls[count++] = walls[i];
        }
        GameHistoryWall[] found = new GameHistoryWall[count];
        System.Array.Copy(walls, found, count);
        return found;
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
    // _SelectRow, and the entry's text inside it (its first child, Label: the wall finds it by name in the entries it
    // makes). Dragging or the wheel over it scrolls the list (the button takes only clicks, so they reach the scroll
    // rect). With a delete button (the personal wall), a second child, Delete, at the entry's right end, calling the
    // row's _PressDelete, with its own Label.
    static void AddRow(GameObject content, GameHistoryWall wall, int index, TMP_FontAsset font, Vector2 topLeft,
        Vector2 extent, bool withDelete) {
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
            nameof(GameHistoryRow._Press));

        float textWidth = extent.x - 2 * RowInset - (withDelete ? DeleteWidth + RowInset : 0);
        TextMeshProUGUI text = NewText(rowObject, "Label", font, RowFontSize, new Vector2(RowInset, 0),
            new Vector2(textWidth, extent.y));
        wall.rows[index] = text;
        wall.rowTargets[index] = target;
        if (!withDelete) return;
        TextMeshProUGUI deleteLabel;
        GameObject delete = AddButton(rowObject, font, udon, "Delete", nameof(GameHistoryRow._PressDelete),
            new Vector2(extent.x - RowInset - DeleteWidth, (extent.y - DeleteHeight) / 2),
            new Vector2(DeleteWidth, DeleteHeight), 28, out deleteLabel);
        wall.deleteButtons[index] = delete;
        wall.deleteLabels[index] = deleteLabel;
    }

    // A pale button named name at topLeft, extent in size, calling udon's eventName, with its label (a child named
    // Label, empty: the wall writes it from the language pack) in size text.
    static GameObject AddButton(GameObject parent, TMP_FontAsset font, UdonBehaviour udon, string name,
        string eventName, Vector2 topLeft, Vector2 extent, float size, out TextMeshProUGUI label) {
        GameObject buttonObject = NewChild(parent, name, topLeft, extent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.85f, 0.85f, 0.88f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        UnityEventTools.AddStringPersistentListener(button.onClick, new UnityAction<string>(udon.SendCustomEvent),
            eventName);
        GameObject labelObject = AddStretched(buttonObject, "Label", Vector2.zero, Vector2.zero);
        label = AddText(labelObject, font, size, TextAlignmentOptions.Center, Color.black);
        label.text = "";
        return buttonObject;
    }

    // The PGN box (Export PGN, step 13.10) at topLeft, extent in size, over the list's lower half: a hint line and a
    // read-only multi-line text field (the board's PGN box's, ChessBoardViewBuilder.AddPgnField), 80 characters a
    // line. Built hidden; the wall shows it for the local player.
    static void AddPgnBox(GameObject root, GameHistoryWall wall, TMP_FontAsset font, Vector2 topLeft, Vector2 extent) {
        GameObject box = NewChild(root, "PGN Box", topLeft, extent);
        Image image = box.AddComponent<Image>();
        image.color = new Color(0.12f, 0.12f, 0.14f, 0.97f);
        wall.pgnHint = ChessBoardViewBuilder.AddPgnHint(box, font, PgnHintHeight, PgnHintSize);
        wall.pgnField = ChessBoardViewBuilder.AddPgnField(box, font, 8, true, PgnHintHeight, PgnTextSize);
        box.SetActive(false);
        wall.pgnBox = box;
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

    // Beside the instance's wall, further left as seen from the boards: a canvas's width and PersonalGap along the
    // wall's own left (-z for a wall facing the boards, towards the spawn), or where a new instance wall would go,
    // moved that far.
    static Vector3 NewPersonalWallPosition() {
        float shift = CanvasWidth * 0.001f + PersonalGap;
        GameHistoryWall[] walls = WallsOf(false);
        if (walls.Length == 0) return NewWallPosition() - new Vector3(0, 0, shift);
        Transform wall = walls[0].transform;
        return wall.position - wall.right * shift;
    }
}
