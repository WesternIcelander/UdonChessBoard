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

// Builds the settings menu (docs/settings.md, ChessSettingsMenu.cs) in the open scene for its only ChessArbiter: a
// world-space canvas with a title, a notice, and the controls only organizers see: the scope row (All boards or one
// board, and All default to clear a board's overrides), then one row per setting with its value and its buttons (On,
// Off and Default for a setting, < > and Default for a time or the forced mode). Below them, Apply now re-snapshots
// the boards shown (every board on the arbiter for All boards; step 11.1b), with its outcome on the line under it.
// Every button has a ChessSettingsButton calling the menu's Press. Placed to the right of the boards (along +x), facing
// them; an existing menu is replaced in place (same parent, position, rotation and scale), and whatever pointed at it
// (SettingsTest's menu) is pointed at the new one; an empty ChessSettingsMenu field (SettingsTest's menu) gets it.
// Menu: Tools > Chess Board > Create Settings Menu. Plain editor C#, not Udon.
public static class ChessSettingsMenuBuilder {
    const string MenuScriptPath = "Assets/Siggi/ChessBoard/ChessSettingsMenu.cs";
    const string MenuProgramPath = "Assets/Siggi/ChessBoard/ChessSettingsMenu.asset";
    const string ButtonScriptPath = "Assets/Siggi/ChessBoard/ChessSettingsButton.cs";
    const string ButtonProgramPath = "Assets/Siggi/ChessBoard/ChessSettingsButton.asset";

    const float CanvasWidth = 1500;
    const float Margin = 40;
    const float TitleHeight = 90;
    const float NoticeHeight = 60;
    const float ScopeHeight = 76;
    // The space above the re-snapshot row (step 11.1b), setting it apart from the settings.
    const float SectionGap = 30;
    const float RowHeight = 60;
    const float LabelWidth = 580;
    const float ValueWidth = 400;
    const float ButtonWidth = 130;
    const float ButtonGap = 10;
    const float ButtonHeight = 48;
    const int TitleFontSize = 56;
    const int TextFontSize = 30;
    const int ButtonFontSize = 24;
    // Where a new menu goes: this far to the right of the rightmost board (along +x), its centre this high above them.
    const float MenuDistance = 2.5f;
    const float MenuHeight = 1.4f;

    [MenuItem("Tools/Chess Board/Create Settings Menu")]
    static void Create() {
        ChessArbiter[] arbiters = Object.FindObjectsOfType<ChessArbiter>(true);
        if (arbiters.Length != 1) {
            EditorUtility.DisplayDialog("Create Settings Menu", arbiters.Length == 0
                ? "There's no ChessArbiter in the open scene. Add the ChessArbiter prefab first."
                : "There are several ChessArbiters in the scene; the menu needs exactly one.", "OK");
            return;
        }
        if (!EnsureProgramAssets()) return;
        ChessArbiter arbiter = arbiters[0];
        float itemsTop = Margin + TitleHeight + NoticeHeight + ScopeHeight;
        float resnapshotTop = itemsTop + ChessSettingsMenu.ItemCount * RowHeight + SectionGap;
        float canvasHeight = resnapshotTop + 2 * RowHeight + Margin;

        TMP_FontAsset font = DefaultFont();
        ChessSettingsMenu[] olds = Object.FindObjectsOfType<ChessSettingsMenu>(true);
        GameObject root = new GameObject("ChessSettingsMenu");
        Undo.RegisterCreatedObjectUndo(root, "Create Settings Menu");
        root.layer = LayerMask.NameToLayer("Default");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(CanvasWidth, canvasHeight);
        rootRect.localScale = Vector3.one * 0.001f;
        rootRect.position = NewMenuPosition();
        // Facing the boards: seen from them, looking along +x.
        rootRect.rotation = Quaternion.Euler(0, 90, 0);
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<VRCUiShape>();
        AddTriggerCollider(root);
        Image background = root.AddComponent<Image>();
        background.color = new Color(0.1f, 0.1f, 0.12f, 1f);
        background.raycastTarget = false;

        ChessSettingsMenu menu = UdonSharpUndo.AddComponent<ChessSettingsMenu>(root);
        menu.arbiter = arbiter;
        menu.title = NewText(root, "Title", font, TitleFontSize, new Vector2(Margin, Margin),
            new Vector2(CanvasWidth - 2 * Margin, TitleHeight));
        menu.title.fontStyle = FontStyles.Bold;
        menu.title.text = "Chess settings";
        menu.notice = NewText(root, "Notice", font, TextFontSize, new Vector2(Margin, Margin + TitleHeight),
            new Vector2(CanvasWidth - 2 * Margin, NoticeHeight));
        menu.notice.color = new Color(1f, 0.75f, 0.45f);
        menu.notice.text = "Only organizers can change these settings.";

        GameObject controls = new GameObject("Controls", typeof(RectTransform));
        controls.layer = root.layer;
        RectTransform controlsRect = controls.GetComponent<RectTransform>();
        controlsRect.SetParent(root.transform, false);
        controlsRect.anchorMin = Vector2.zero;
        controlsRect.anchorMax = Vector2.one;
        controlsRect.offsetMin = Vector2.zero;
        controlsRect.offsetMax = Vector2.zero;
        menu.controls = controls;

        // Three per row and the scope row, and the re-snapshot row's one.
        int buttonCount = 3 * (ChessSettingsMenu.ItemCount + 1) + 1;
        menu.buttons = new GameObject[buttonCount];
        menu.buttonImages = new Image[buttonCount];
        menu.buttonItems = new int[buttonCount];
        menu.buttonActions = new int[buttonCount];
        menu.values = new TextMeshProUGUI[ChessSettingsMenu.ItemCount];
        int next = 0;
        float scopeTop = Margin + TitleHeight + NoticeHeight;
        menu.scopeLabel = AddRow(controls, menu, ChessSettingsMenu.ItemScope, "Settings for", font,
            scopeTop + (ScopeHeight - RowHeight) / 2, ref next);
        menu.scopeLabel.fontStyle = FontStyles.Bold;
        for (int item = 0; item < ChessSettingsMenu.ItemCount; item++) {
            menu.values[item] = AddRow(controls, menu, item, ChessSettingsMenu.ItemText(item), font,
                itemsTop + item * RowHeight, ref next);
        }
        // The re-snapshot row: its label and Apply now, and the outcome across the line below.
        NewText(controls, "Label resnapshot", font, TextFontSize, new Vector2(Margin, resnapshotTop),
            new Vector2(LabelWidth + ValueWidth, RowHeight)).text =
            ChessSettingsMenu.ItemText(ChessSettingsMenu.ItemResnapshot);
        AddButton(controls, menu, ChessSettingsMenu.ItemResnapshot, ChessSettingsMenu.ActionOn, font,
            new Vector2(Margin + LabelWidth + ValueWidth, resnapshotTop + (RowHeight - ButtonHeight) / 2), next++);
        menu.resnapshotLabel = NewText(controls, "Value resnapshot", font, TextFontSize,
            new Vector2(Margin, resnapshotTop + RowHeight), new Vector2(CanvasWidth - 2 * Margin, RowHeight));
        menu.boards = BoardsOn(arbiter);
        UdonSharpEditorUtility.CopyProxyToUdon(menu);

        int repointed = 0;
        for (int i = 0; i < olds.Length; i++) {
            if (i == 0) {
                root.transform.SetParent(olds[i].transform.parent, false);
                root.transform.SetSiblingIndex(olds[i].transform.GetSiblingIndex());
                rootRect.position = olds[i].transform.position;
                rootRect.rotation = olds[i].transform.rotation;
                rootRect.localScale = olds[i].transform.localScale;
            }
            repointed += ChessBoardViewBuilder.RepointReferences(olds[i], menu);
            Undo.DestroyObjectImmediate(olds[i].gameObject);
        }
        int filled = FillEmptyFields(menu);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[ChessSettingsMenuBuilder] " + (olds.Length > 0 ? "Replaced" : "Created") + " the settings menu for "
            + arbiter.gameObject.name + " (" + menu.boards.Length + " board(s); " + (repointed + filled)
            + " behaviour(s) pointed at it). Move it where you like and save the scene.");
    }

    // Every board on the arbiter, by board number, for re-snapshots (step 11.1b).
    static ChessBoard[] BoardsOn(ChessArbiter arbiter) {
        System.Collections.Generic.List<ChessBoard> list = new System.Collections.Generic.List<ChessBoard>();
        foreach (ChessBoard board in Object.FindObjectsOfType<ChessBoard>(true)) {
            if (board.arbiter == arbiter) list.Add(board);
        }
        list.Sort((a, b) => a.boardNumber.CompareTo(b.boardNumber));
        return list.ToArray();
    }

    // A row: its label, its value text (returned), and its three buttons, each a ChessSettingsButton calling Press.
    static TextMeshProUGUI AddRow(GameObject parent, ChessSettingsMenu menu, int item, string label, TMP_FontAsset font, float top,
            ref int next) {
        NewText(parent, "Label " + item, font, TextFontSize, new Vector2(Margin, top),
                new Vector2(LabelWidth, RowHeight))
            .text = label;
        TextMeshProUGUI value = NewText(parent, "Value " + item, font, TextFontSize, new Vector2(Margin + LabelWidth, top),
            new Vector2(ValueWidth, RowHeight));
        bool setting = ChessSettingsMenu.IsSettingItem(item);
        int[] actions = setting
            ? new[] { ChessSettingsMenu.ActionOn, ChessSettingsMenu.ActionOff, ChessSettingsMenu.ActionDefault }
            : new[] { ChessSettingsMenu.ActionPrevious, ChessSettingsMenu.ActionNext, ChessSettingsMenu.ActionDefault };
        float left = Margin + LabelWidth + ValueWidth;
        for (int slot = 0; slot < actions.Length; slot++) {
            Vector2 topLeft = new Vector2(left + slot * (ButtonWidth + ButtonGap),
                top + (RowHeight - ButtonHeight) / 2);
            AddButton(parent, menu, item, actions[slot], font, topLeft, next++);
        }
        return value;
    }

    static void AddButton(GameObject parent, ChessSettingsMenu menu, int item, int action, TMP_FontAsset font, Vector2 topLeft,
            int index) {
        string text = ChessSettingsMenu.ActionText(item, action);
        GameObject buttonObject = new GameObject("Button " + item + " " + text, typeof(RectTransform));
        buttonObject.layer = parent.layer;
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent.transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
        rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        Image image = buttonObject.AddComponent<Image>();
        image.color = menu.buttonColor;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        ChessSettingsButton target = UdonSharpUndo.AddComponent<ChessSettingsButton>(buttonObject);
        target.menu = menu;
        target.item = item;
        target.action = action;
        UdonSharpEditorUtility.CopyProxyToUdon(target);
        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
        UnityEventTools.AddStringPersistentListener(button.onClick, new UnityAction<string>(udon.SendCustomEvent),
            "Press");

        TextMeshProUGUI label = NewText(buttonObject, "Label", font, ButtonFontSize, Vector2.zero,
            new Vector2(ButtonWidth, ButtonHeight));
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.black;
        label.text = text;
        menu.buttons[index] = buttonObject;
        menu.buttonImages[index] = image;
        menu.buttonItems[index] = item;
        menu.buttonActions[index] = action;
    }

    // Gives the menu to every empty ChessSettingsMenu field in the scene (SettingsTest's menu). Returns how many
    // behaviours changed.
    static int FillEmptyFields(ChessSettingsMenu menu) {
        int changed = 0;
        UdonSharpBehaviour[] behaviours = Object.FindObjectsOfType<UdonSharpBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++) {
            UdonSharpBehaviour behaviour = behaviours[i];
            if (behaviour.transform.IsChildOf(menu.transform)) continue;
            SerializedObject serialized = new SerializedObject(behaviour);
            SerializedProperty property = serialized.GetIterator();
            bool found = false;
            while (property.Next(true)) {
                if (property.depth == 0 && property.propertyType == SerializedPropertyType.ObjectReference
                    && property.objectReferenceValue == null && IsMenuField(behaviour, property.name)) {
                    property.objectReferenceValue = menu;
                    found = true;
                }
            }
            if (!found) continue;
            serialized.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
            changed++;
        }
        return changed;
    }

    // Whether a behaviour's field of that name holds a ChessSettingsMenu.
    static bool IsMenuField(UdonSharpBehaviour behaviour, string name) {
        System.Reflection.FieldInfo field = behaviour.GetType().GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        return field != null && field.FieldType == typeof(ChessSettingsMenu);
    }

    // As GameHistoryWallBuilder.AddTriggerCollider.
    static void AddTriggerCollider(GameObject canvasObject) {
        BoxCollider box = canvasObject.GetComponent<BoxCollider>();
        if (box == null) box = canvasObject.AddComponent<BoxCollider>();
        Vector2 size = canvasObject.GetComponent<RectTransform>().sizeDelta;
        box.center = Vector3.zero;
        box.size = new Vector3(size.x, size.y, 1);
        box.isTrigger = true;
    }

    // Right of the rightmost board, level with the boards' middle along z; beside the origin when there are none.
    static Vector3 NewMenuPosition() {
        ChessBoard[] boards = Object.FindObjectsOfType<ChessBoard>(true);
        if (boards.Length == 0) return new Vector3(MenuDistance, MenuHeight, 0);
        Vector3 sum = Vector3.zero;
        float maxX = float.MinValue;
        for (int i = 0; i < boards.Length; i++) {
            Vector3 position = boards[i].transform.position;
            sum += position;
            if (position.x > maxX) maxX = position.x;
        }
        Vector3 middle = sum / boards.Length;
        return new Vector3(maxX + MenuDistance, middle.y + MenuHeight, middle.z);
    }

    // TextMeshPro's default font (LiberationSans SDF, from TMP Essential Resources), by the GUID it always has
    // (step 12.17), or TMP's own default when it isn't there.
    static TMP_FontAsset DefaultFont() {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath("8f586378b4e144a9851e7b34d9b748ee"));
        return font != null ? font : TMP_Settings.defaultFontAsset;
    }

    // A text at a position from the parent's top left, in canvas pixels.
    static TextMeshProUGUI NewText(GameObject parent, string name, TMP_FontAsset font, int size, Vector2 topLeft,
        Vector2 extent) {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent.transform, false);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
        rect.sizeDelta = extent;
        TextMeshProUGUI text = child.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Left;
        text.color = Color.white;
        text.raycastTarget = false;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Truncate;
        return text;
    }

    // As GameHistoryWallBuilder.EnsureProgramAssets: the menu's and the buttons' program assets, created and compiled
    // when missing, after which the menu item has to be run again.
    static bool EnsureProgramAssets() {
        bool created = CreateProgramAssetIfMissing(MenuScriptPath, MenuProgramPath);
        created |= CreateProgramAssetIfMissing(ButtonScriptPath, ButtonProgramPath);
        if (!created) return true;
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        EditorUtility.DisplayDialog("Create Settings Menu", "Created the missing program assets next to "
            + MenuScriptPath + ". Once UdonSharp has finished compiling, run Tools > Chess Board > Create Settings "
            + "Menu again.", "OK");
        return false;
    }

    static bool CreateProgramAssetIfMissing(string scriptPath, string programPath) {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath) != null) return false;
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        if (script == null) {
            Debug.LogError("[ChessSettingsMenuBuilder] " + scriptPath + " not found.");
            return true;
        }
        UdonSharpProgramAsset program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        program.sourceCsScript = script;
        AssetDatabase.CreateAsset(program, programPath);
        return true;
    }
}
