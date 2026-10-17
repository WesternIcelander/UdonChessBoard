using SiggiChessBoard;
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

// Builds the 2D board view (PLAN.md 12.13, ChessBoard2DView.cs, docs/board-2d.md) in the open scene: a world-space
// canvas with the selector (left, "Board N", right), the 8x8 board with a piece image on each square, and the players
// and status texts under it. The pieces wear the sprites in Sprites2D/ (KingW.png ... PawnB.png), whose import is set
// to Sprite here when it isn't. Its boards are every board in the scene by board number. The selected 2D view, or the
// scene's only one, is replaced where it stands, keeping its boards and Synced; otherwise a new one stands behind the
// boards, facing them. Menu: Tools > Chess Board > Create 2D Board View. Plain editor C#, not Udon.
public static class ChessBoard2DViewBuilder {
    const string ViewScriptPath = "Assets/Siggi/ChessBoard/ChessBoard2DView.cs";
    const string ViewProgramPath = "Assets/Siggi/ChessBoard/ChessBoard2DView.asset";
    const string SpritesFolder = "Assets/Siggi/ChessBoard/Sprites2D/";
    // The sprites' names by piece type, pawn to king (ChessRules.Pawn - 1 on), with W or B after.
    static readonly string[] SpriteNames = { "Pawn", "Knight", "Bishop", "Rook", "Queen", "King" };

    const float Margin = 40;
    const float SquareSize = 100;
    const float BoardSize = 8 * SquareSize;
    const float CanvasWidth = BoardSize + 2 * Margin;
    const float SelectorHeight = 90;
    const float SelectorButtonWidth = 120;
    const float Gap = 20;
    const float PlayersHeight = 90;
    const float StatusHeight = 200;
    const float CanvasHeight = Margin + SelectorHeight + Gap + BoardSize + Gap + PlayersHeight + StatusHeight + Margin;
    // The square's share the piece image fills.
    const float PieceInset = 4;
    // Where a new view goes: this far behind the boards (along +z), its centre this high above them.
    const float ViewDistance = 2.5f;
    const float ViewHeight = 1.4f;

    [MenuItem("Tools/Chess Board/Create 2D Board View")]
    static void Create() {
        if (!EnsureProgramAsset()) return;
        Sprite[] whiteSprites = LoadPieceSprites(ChessRules.White);
        Sprite[] blackSprites = LoadPieceSprites(ChessRules.Black);
        ChessBoard2DView old = ExistingView();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        GameObject root = new GameObject("ChessBoard2DView");
        Undo.RegisterCreatedObjectUndo(root, "Create 2D Board View");
        root.layer = LayerMask.NameToLayer("Default");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
        rootRect.localScale = Vector3.one * 0.001f;
        // Facing the boards: seen from them, looking along +z.
        rootRect.position = NewViewPosition();
        rootRect.rotation = Quaternion.identity;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<VRCUiShape>();
        AddTriggerCollider(root);
        Image background = root.AddComponent<Image>();
        background.color = new Color(0.1f, 0.1f, 0.12f, 1f);
        background.raycastTarget = false;

        ChessBoard2DView view = UdonSharpUndo.AddComponent<ChessBoard2DView>(root);
        view.boards = old != null ? old.boards : SceneBoards();
        view.synced = old != null && old.synced;
        view.whiteSprites = whiteSprites;
        view.blackSprites = blackSprites;
        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(view);

        // The selector: left, the board's name, right.
        AddButton(root, udon, "Button Left", "<", nameof(ChessBoard2DView.PressLeft), font,
            new Vector2(Margin, Margin), new Vector2(SelectorButtonWidth, SelectorHeight));
        AddButton(root, udon, "Button Right", ">", nameof(ChessBoard2DView.PressRight), font,
            new Vector2(CanvasWidth - Margin - SelectorButtonWidth, Margin),
            new Vector2(SelectorButtonWidth, SelectorHeight));
        view.boardLabel = NewText(root, "Board Label", font, 56, new Vector2(Margin + SelectorButtonWidth + Gap, Margin),
            new Vector2(BoardSize - 2 * (SelectorButtonWidth + Gap), SelectorHeight));
        view.boardLabel.alignment = TextAnchor.MiddleCenter;
        view.boardLabel.fontStyle = FontStyle.Bold;

        // The board, White at the bottom: a1 bottom left.
        float boardTop = Margin + SelectorHeight + Gap;
        GameObject squares = NewChild(root, "Squares", new Vector2(Margin, boardTop), new Vector2(BoardSize, BoardSize));
        view.squareImages = new Image[64];
        view.pieceImages = new Image[64];
        for (int square = 0; square < 64; square++) {
            int file = square % 8;
            int rank = square / 8;
            GameObject cell = NewChild(squares, "Square " + (char)('a' + file) + (rank + 1),
                new Vector2(file * SquareSize, (7 - rank) * SquareSize), new Vector2(SquareSize, SquareSize));
            Image squareImage = cell.AddComponent<Image>();
            squareImage.raycastTarget = false;
            view.squareImages[square] = squareImage;
            view.pieceImages[square] = NewPieceImage(cell, PieceInset);
        }

        float textTop = boardTop + BoardSize + Gap;
        view.playersLabel = NewText(root, "Players", font, 34, new Vector2(Margin, textTop),
            new Vector2(BoardSize, PlayersHeight));
        view.statusLabel = NewText(root, "Status", font, 30, new Vector2(Margin, textTop + PlayersHeight),
            new Vector2(BoardSize, StatusHeight));
        view.statusLabel.alignment = TextAnchor.UpperLeft;
        UdonSharpEditorUtility.CopyProxyToUdon(view);

        int repointed = 0;
        if (old != null) {
            root.transform.SetParent(old.transform.parent, false);
            root.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
            rootRect.position = old.transform.position;
            rootRect.rotation = old.transform.rotation;
            rootRect.localScale = old.transform.localScale;
            repointed = ChessBoardViewBuilder.RepointReferences(old, view);
            Undo.DestroyObjectImmediate(old.gameObject);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[ChessBoard2DViewBuilder] " + (old != null ? "Replaced" : "Created") + " the 2D board view ("
            + view.boards.Length + " board(s), " + (view.synced ? "synced" : "not synced")
            + (old != null ? ", " + repointed + " behaviour(s) pointed at it" : "")
            + "). Move it where you like and save the scene.");
    }

    // Each colour's piece sprites, pawn to king, from Sprites2D/ (null where one is missing, with a warning). A texture
    // not imported as a sprite is set to Sprite and reimported. Public for the debug view's builder.
    public static Sprite[] LoadPieceSprites(int color) {
        Sprite[] sprites = new Sprite[SpriteNames.Length];
        string suffix = color == ChessRules.White ? "W" : "B";
        for (int i = 0; i < SpriteNames.Length; i++) {
            string path = SpritesFolder + SpriteNames[i] + suffix + ".png";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) {
                Debug.LogWarning("[ChessBoard2DViewBuilder] " + path + " not found; that piece shows nothing.");
                continue;
            }
            if (importer.textureType != TextureImporterType.Sprite
                || importer.spriteImportMode != SpriteImportMode.Single) {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        return sprites;
    }

    // A piece image filling its square but for inset on each side, keeping the sprite's shape, hidden until drawn.
    // Public for the debug view's builder.
    public static Image NewPieceImage(GameObject square, float inset) {
        GameObject child = new GameObject("Piece", typeof(RectTransform));
        child.layer = square.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(square.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        Image image = child.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.enabled = false;
        return image;
    }

    // The selected 2D view, or the scene's only one; null when there's none (or several and none selected).
    static ChessBoard2DView ExistingView() {
        if (Selection.activeGameObject != null) {
            ChessBoard2DView selected = Selection.activeGameObject.GetComponent<ChessBoard2DView>();
            if (selected != null) return selected;
        }
        ChessBoard2DView[] views = Object.FindObjectsOfType<ChessBoard2DView>(true);
        return views.Length == 1 ? views[0] : null;
    }

    // Every board in the scene, by board number.
    static ChessBoard[] SceneBoards() {
        System.Collections.Generic.List<ChessBoard> list =
            new System.Collections.Generic.List<ChessBoard>(Object.FindObjectsOfType<ChessBoard>(true));
        list.Sort((a, b) => a.boardNumber.CompareTo(b.boardNumber));
        return list.ToArray();
    }

    // Behind the boards (beyond the farthest along +z), level with their middle along x; ahead of the origin when
    // there are none.
    static Vector3 NewViewPosition() {
        ChessBoard[] boards = Object.FindObjectsOfType<ChessBoard>(true);
        if (boards.Length == 0) return new Vector3(0, ViewHeight, ViewDistance);
        Vector3 sum = Vector3.zero;
        float maxZ = float.MinValue;
        for (int i = 0; i < boards.Length; i++) {
            Vector3 position = boards[i].transform.position;
            sum += position;
            if (position.z > maxZ) maxZ = position.z;
        }
        Vector3 middle = sum / boards.Length;
        return new Vector3(middle.x, middle.y + ViewHeight, maxZ + ViewDistance);
    }

    static void AddButton(GameObject parent, UdonBehaviour udon, string name, string label, string eventName, Font font,
        Vector2 topLeft, Vector2 size) {
        GameObject buttonObject = NewChild(parent, name, topLeft, size);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.85f, 0.85f, 0.88f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        UnityEventTools.AddStringPersistentListener(button.onClick, new UnityAction<string>(udon.SendCustomEvent),
            eventName);
        Text text = NewText(buttonObject, "Label", font, 56, Vector2.zero, size);
        text.alignment = TextAnchor.MiddleCenter;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.black;
        text.text = label;
    }

    // As GameHistoryWallBuilder.AddTriggerCollider: a trigger box over the whole canvas, so players walk through it
    // and the pointer still hits it.
    static void AddTriggerCollider(GameObject canvasObject) {
        BoxCollider box = canvasObject.GetComponent<BoxCollider>();
        if (box == null) box = canvasObject.AddComponent<BoxCollider>();
        Vector2 size = canvasObject.GetComponent<RectTransform>().sizeDelta;
        box.center = Vector3.zero;
        box.size = new Vector3(size.x, size.y, 1);
        box.isTrigger = true;
    }

    // As the other builders: the view's program asset, created and compiled when missing, after which the menu item
    // has to be run again.
    static bool EnsureProgramAsset() {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ViewProgramPath) != null) return true;
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(ViewScriptPath);
        if (script == null) {
            Debug.LogError("[ChessBoard2DViewBuilder] " + ViewScriptPath + " not found.");
            return false;
        }
        UdonSharpProgramAsset program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        program.sourceCsScript = script;
        AssetDatabase.CreateAsset(program, ViewProgramPath);
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        EditorUtility.DisplayDialog("Create 2D Board View", "Created the view's program asset (" + ViewProgramPath
            + "). Once UdonSharp has finished compiling, run Tools > Chess Board > Create 2D Board View again.", "OK");
        return false;
    }

    // A child rect at a position from the parent's top left, in canvas pixels.
    static GameObject NewChild(GameObject parent, string name, Vector2 topLeft, Vector2 size) {
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

    // A text at a position from the parent's top left, in canvas pixels.
    static Text NewText(GameObject parent, string name, Font font, int size, Vector2 topLeft, Vector2 extent) {
        GameObject child = NewChild(parent, name, topLeft, extent);
        Text text = child.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }
}
