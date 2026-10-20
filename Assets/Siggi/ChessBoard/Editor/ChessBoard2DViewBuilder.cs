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

// Builds the 2D board view (PLAN.md 12.13 to 12.15, ChessBoard2DView.cs, docs/board-2d.md) in the open scene: a
// world-space canvas with the selector (left, "Board N", right), the 8x8 board with a piece image on each square, the
// players and status texts under it, the step buttons (<<, <, >, >>) along the bottom, and the move list (12.15) to the
// right, the full height. The pieces wear the sprites
// in Sprites2D/ (KingW.png ... PawnB.png), whose import is set to Sprite here when it isn't. Its boards are every board
// in the scene by board number.
// - Create 2D Board View: the selected 2D view, or the scene's only one, is replaced where it stands, keeping its
//   boards, locked board and Synced; otherwise a new one stands behind the boards, facing them.
// - Create Locked 2D Board View: a view locked to the selected board (12.14), replacing one already locked to it where
//   it stands, or new behind that board.
// - Create Movable 2D Board View (12.16): the selected view (or the selected holder's) put in a holder with a grip and
//   a corner to move and resize it (ChessBoard2DViewHolder.cs), its grip and corner rebuilt where the view stands;
//   with nothing selected, a new view beside the others, in a new holder.
// Plain editor C#, not Udon.
public static class ChessBoard2DViewBuilder {
    const string ViewScriptPath = "Assets/Siggi/ChessBoard/ChessBoard2DView.cs";
    const string ViewProgramPath = "Assets/Siggi/ChessBoard/ChessBoard2DView.asset";
    const string CellScriptPath = "Assets/Siggi/ChessBoard/ChessMoveListCell.cs";
    const string CellProgramPath = "Assets/Siggi/ChessBoard/ChessMoveListCell.asset";
    const string HolderScriptPath = "Assets/Siggi/ChessBoard/ChessBoard2DViewHolder.cs";
    const string HolderProgramPath = "Assets/Siggi/ChessBoard/ChessBoard2DViewHolder.asset";
    const string SpritesFolder ="Assets/Siggi/ChessBoard/Sprites2D/";
    // The sprites' names by piece type, pawn to king (ChessRules.Pawn - 1 on), with W or B after.
    static readonly string[] SpriteNames = { "Pawn", "Knight", "Bishop", "Rook", "Queen", "King" };

    const float Margin = 40;
    const float SquareSize = 100;
    const float BoardSize = 8 * SquareSize;
    const float SelectorHeight = 90;
    const float SelectorButtonWidth = 120;
    const float Gap = 20;
    const float PlayersHeight = 90;
    const float StatusHeight = 200;
    const float StepHeight = 90;
    const float CanvasHeight = Margin + SelectorHeight + Gap + BoardSize + Gap + PlayersHeight + StatusHeight + Gap
        + StepHeight + Margin;
    // The move list (12.15), right of the board and the full height between the margins: a row per move number, its
    // number then White's and Black's cells, and a scrollbar on the right. The canvas grows to make room (decided in
    // 12.15), the board keeping its size.
    const float MoveRowHeight = 50;
    const float MoveNumberWidth = 80;
    const float MoveCellWidth = 170;
    const float MoveGap = 6;
    const float ScrollbarWidth = 24;
    const float MoveListWidth = MoveNumberWidth + 2 * (MoveCellWidth + MoveGap) + MoveGap + ScrollbarWidth;
    const float MoveListHeight = CanvasHeight - 2 * Margin;
    const float CanvasWidth = Margin + BoardSize + Gap + MoveListWidth + Margin;
    // The step buttons' labels, StepFirst to StepLast.
    static readonly string[] StepTexts = { "<<", "<", ">", ">>" };
    static readonly string[] StepEvents = {
        nameof(ChessBoard2DView.PressFirst), nameof(ChessBoard2DView.PressBack), nameof(ChessBoard2DView.PressForward),
        nameof(ChessBoard2DView.PressLast)
    };
    // The square's share the piece image fills.
    const float PieceInset = 4;
    // Where a new view goes: this far behind the boards (along +z), its centre this high above them.
    const float ViewDistance = 2.5f;
    const float ViewHeight = 1.4f;
    // Where a new locked view goes: this far behind its board.
    const float LockedViewDistance = 1.2f;

    [MenuItem("Tools/Chess Board/Create 2D Board View")]
    static void Create() {
        ChessBoard2DView old = ExistingView();
        Build(old, old != null ? old.lockedBoard : null, NewViewPosition());
    }

    // A view locked to the selected board (12.14).
    [MenuItem("Tools/Chess Board/Create Locked 2D Board View")]
    static void CreateLocked() {
        ChessBoard board = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<ChessBoard>()
            : null;
        if (board == null) {
            EditorUtility.DisplayDialog("Create Locked 2D Board View",
                "Select the board (its ChessBoard object) the view shows, then try again.", "OK");
            return;
        }
        ChessBoard2DView old = null;
        ChessBoard2DView[] views = Object.FindObjectsOfType<ChessBoard2DView>(true);
        for (int i = 0; i < views.Length && old == null; i++) {
            if (views[i].lockedBoard == board) old = views[i];
        }
        Vector3 position = board.transform.position + new Vector3(0, ViewHeight, LockedViewDistance);
        Build(old, board, position);
    }

    // One more view, whatever is selected: beside the new view's place, a view's width (and a gap) along +x per view
    // already in the scene (e.g. one synced and one not, 12.14's several-client run).
    [MenuItem("Tools/Chess Board/Create Another 2D Board View")]
    static void CreateAnother() {
        int count = Object.FindObjectsOfType<ChessBoard2DView>(true).Length;
        Build(null, null, NewViewPosition() + Vector3.right * (count * (CanvasWidth * 0.001f + 0.2f)));
    }

    // Builds a view locked to locked (none when null), replacing old where it stands (keeping its name, boards and
    // Synced), or new at position, named ChessBoard2DView, or ChessBoard2DView (N) beside N - 1 others.
    static ChessBoard2DView Build(ChessBoard2DView old, ChessBoard locked, Vector3 position) {
        if (!EnsureProgramAsset()) return null;
        Sprite[] whiteSprites = LoadPieceSprites(ChessRules.White);
        Sprite[] blackSprites = LoadPieceSprites(ChessRules.Black);
        int others = Object.FindObjectsOfType<ChessBoard2DView>(true).Length;
        string name = old != null ? old.gameObject.name
            : others == 0 ? "ChessBoard2DView" : "ChessBoard2DView (" + (others + 1) + ")";

        TMP_FontAsset font = DefaultFont();
        GameObject root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, "Create 2D Board View");
        root.layer = LayerMask.NameToLayer("Default");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
        rootRect.localScale = Vector3.one * 0.001f;
        // Facing the boards: seen from them, looking along +z.
        rootRect.position = position;
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
        view.lockedBoard = locked;
        view.synced = old != null && old.synced;
        view.whiteSprites = whiteSprites;
        view.blackSprites = blackSprites;
        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(view);

        // The selector: left, the board's name, right (hidden by the view when locked).
        view.leftButton = AddButton(root, udon, "Button Left", "<", nameof(ChessBoard2DView.PressLeft), font,
            new Vector2(Margin, Margin), new Vector2(SelectorButtonWidth, SelectorHeight));
        view.rightButton = AddButton(root, udon, "Button Right", ">", nameof(ChessBoard2DView.PressRight), font,
            new Vector2(Margin + BoardSize - SelectorButtonWidth, Margin),
            new Vector2(SelectorButtonWidth, SelectorHeight));
        view.boardLabel = NewText(root, "Board Label", font, 56, new Vector2(Margin + SelectorButtonWidth + Gap, Margin),
            new Vector2(BoardSize - 2 * (SelectorButtonWidth + Gap), SelectorHeight));
        view.boardLabel.alignment = TextAlignmentOptions.Center;
        view.boardLabel.fontStyle = FontStyles.Bold;

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
        view.statusLabel.alignment = TextAlignmentOptions.TopLeft;

        // The step buttons along the bottom, the board's width between them: <<, <, >, >>.
        float stepTop = textTop + PlayersHeight + StatusHeight + Gap;
        float stepWidth = (BoardSize - 3 * Gap) / 4;
        view.stepButtons = new Button[4];
        view.stepLabels = new TextMeshProUGUI[4];
        for (int i = 0; i < 4; i++) {
            Button button = AddButton(root, udon, "Button " + StepEvents[i].Substring("Press".Length), StepTexts[i],
                StepEvents[i], font, new Vector2(Margin + i * (stepWidth + Gap), stepTop),
                new Vector2(stepWidth, StepHeight));
            view.stepButtons[i] = button;
            view.stepLabels[i] = button.GetComponentInChildren<TextMeshProUGUI>();
        }

        BuildMoveList(root, view, udon, font);
        UdonSharpEditorUtility.CopyProxyToUdon(view);

        int repointed = 0;
        bool replaced = old != null;
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
        Debug.Log("[ChessBoard2DViewBuilder] " + (replaced ? "Replaced" : "Created") + " the 2D board view ("
            + (locked != null ? "locked to Board " + (locked.boardNumber + 1) : view.boards.Length + " board(s)")
            + ", " + (view.synced ? "synced" : "not synced")
            + (replaced ? ", " + repointed + " behaviour(s) pointed at it" : "")
            + "). Move it where you like and save the scene.");
        return view;
    }

    // ---- The movable view (12.16) ----

    // A view players can move and resize: the selected view (or the view of the selected holder) in a holder, its
    // grip and corner rebuilt where the view stands; otherwise a new view beside the others, in a new holder.
    [MenuItem("Tools/Chess Board/Create Movable 2D Board View")]
    static void CreateMovable() {
        if (!EnsureProgramAsset()) return;
        ChessBoard2DView view = null;
        ChessBoard2DViewHolder holder = null;
        GameObject selected = Selection.activeGameObject;
        if (selected != null) {
            holder = selected.GetComponent<ChessBoard2DViewHolder>();
            view = holder != null ? holder.view : selected.GetComponent<ChessBoard2DView>();
            if (holder == null && view != null) holder = HolderOf(view);
        }
        if (view == null) {
            int count = Object.FindObjectsOfType<ChessBoard2DView>(true).Length;
            view = Build(null, null, NewViewPosition() + Vector3.right * (count * (CanvasWidth * 0.001f + 0.2f)));
            if (view == null) return;
        }
        BuildHolder(view, holder);
    }

    // The holder whose view this is (its parent's), or null.
    public static ChessBoard2DViewHolder HolderOf(ChessBoard2DView view) {
        Transform parent = view.transform.parent;
        ChessBoard2DViewHolder holder = parent != null ? parent.GetComponent<ChessBoard2DViewHolder>() : null;
        return holder != null && holder.view == view ? holder : null;
    }

    // The grip bar's size (m; at most this wide, narrower to stay clear of the corner at the smallest size by
    // GripClearance), how far under the view it is, and the corner's size, outside the view's bottom right.
    static readonly Vector3 GripSize = new Vector3(0.5f, 0.05f, 0.05f);
    const float GripClearance = 0.02f;
    const float GripGap = 0.04f;
    const float CornerSize = 0.08f;

    // Puts view in holder (a new one, named after the view, when null), replacing the holder's grip and corner with
    // new ones placed from where the view stands: the grip centred under its bottom edge, the corner just outside its
    // bottom right. Keeps the holder's Movable; its Min Size and Max Size go back to the defaults, as the grip's width is
    // fitted to the smallest size. The grip has a VRCObjectSync when the view is synced.
    static void BuildHolder(ChessBoard2DView view, ChessBoard2DViewHolder holder) {
        Transform viewTransform = view.transform;
        bool created = holder == null;
        if (created) {
            GameObject root = new GameObject(view.gameObject.name + " Holder");
            Undo.RegisterCreatedObjectUndo(root, "Create Movable 2D Board View");
            root.transform.SetParent(viewTransform.parent, false);
            root.transform.SetSiblingIndex(viewTransform.GetSiblingIndex());
            root.transform.SetPositionAndRotation(viewTransform.position, viewTransform.rotation);
            holder = UdonSharpUndo.AddComponent<ChessBoard2DViewHolder>(root);
            Undo.SetTransformParent(viewTransform, root.transform, "Create Movable 2D Board View");
        } else {
            if (holder.grip != null) Undo.DestroyObjectImmediate(holder.grip.gameObject);
            if (holder.corner != null) Undo.DestroyObjectImmediate(holder.corner.gameObject);
            if (viewTransform.parent != holder.transform) {
                Undo.SetTransformParent(viewTransform, holder.transform, "Create Movable 2D Board View");
            }
        }
        Undo.RecordObject(holder, "Create Movable 2D Board View");
        holder.view = view;
        holder.minSize = ChessBoard2DViewHolder.DefaultMinSize;
        holder.maxSize = 3;

        // The view's size and corners in the world, from its rect.
        RectTransform rect = viewTransform.GetComponent<RectTransform>();
        Quaternion heading = viewTransform.rotation;
        Vector3 half = Vector3.Scale(new Vector3(rect.sizeDelta.x, rect.sizeDelta.y, 0) * 0.5f, viewTransform.lossyScale);
        Vector3 bottomCentre = viewTransform.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.yMin, 0));
        Vector3 bottomRight = viewTransform.TransformPoint(new Vector3(rect.rect.xMax, rect.rect.yMin, 0));
        Material material = ChessBoardViewBuilder.MaterialAsset("2D View Handle", new Color(0.55f, 0.56f, 0.6f), 0.4f);

        // The corner's inner edge is half the view's width times the size from the grip's middle.
        Vector3 gripSize = GripSize;
        gripSize.x = Mathf.Max(0.1f, Mathf.Min(GripSize.x, 2 * (half.x * holder.minSize - GripClearance)));
        Renderer gripRenderer;
        holder.gripPickup = AddHandle(holder.transform, "Grip", gripSize, "Move",
            bottomCentre - heading * new Vector3(0, GripGap + gripSize.y * 0.5f, 0), heading, material,
            out gripRenderer);
        holder.grip = holder.gripPickup.transform;
        holder.gripModel = gripRenderer.transform;
        Renderer cornerRenderer;
        Vector3 cornerOut = new Vector3(CornerSize * 0.5f, -CornerSize * 0.5f, 0);
        holder.cornerPickup = AddHandle(holder.transform, "Corner", Vector3.one * CornerSize, "Resize",
            bottomRight + heading * cornerOut, heading, material, out cornerRenderer);
        holder.corner = holder.cornerPickup.transform;
        holder.handleRenderers = new[] { gripRenderer, cornerRenderer };
        MatchObjectSync(holder);
        UdonSharpEditorUtility.CopyProxyToUdon(holder);

        EditorSceneManager.MarkSceneDirty(holder.gameObject.scene);
        Selection.activeGameObject = holder.gameObject;
        Debug.Log("[ChessBoard2DViewBuilder] " + (created ? "Put " : "Rebuilt the grip and corner of ") + view.gameObject.name
            + (created ? " in " + holder.gameObject.name : "") + " (" + (view.synced ? "synced, with" : "not synced, no")
            + " VRCObjectSync; " + (holder.movable ? "movable" : "not movable") + ", " + (half.x * 2).ToString("0.00")
            + " m wide, a " + gripSize.x.ToString("0.00") + " m bar). Save the scene.");
    }

    // A handle to pick up: an object with a trigger box collider of size, a kinematic rigidbody without gravity (it
    // stays where it's let go) and a VRCPickup, on the Pickup layer, with a cube of that size as its visible part.
    static VRCPickup AddHandle(Transform parent, string name, Vector3 size, string text, Vector3 position,
            Quaternion rotation, Material material, out Renderer renderer) {
        GameObject handle = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(handle, "Create Movable 2D Board View");
        int pickupLayer = LayerMask.NameToLayer("Pickup");
        if (pickupLayer >= 0) handle.layer = pickupLayer;
        handle.transform.SetParent(parent, false);
        handle.transform.SetPositionAndRotation(position, rotation);
        BoxCollider box = handle.AddComponent<BoxCollider>();
        box.size = size;
        box.isTrigger = true;
        Rigidbody body = handle.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        VRCPickup pickup = handle.AddComponent<VRCPickup>();
        pickup.InteractionText = text;
        pickup.UseText = text;

        GameObject visible = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visible.name = "Model";
        visible.layer = handle.layer;
        Object.DestroyImmediate(visible.GetComponent<Collider>());
        visible.transform.SetParent(handle.transform, false);
        visible.transform.localScale = size;
        renderer = visible.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return pickup;
    }

    // Gives the holder's grip a VRCObjectSync when its view is synced and takes it away when not; whether it changed
    // anything. For the builders and the build callback (ChessBoard2DViewSyncCheck), as a prefab holds one set of
    // components whatever the view's Synced tick.
    public static bool MatchObjectSync(ChessBoard2DViewHolder holder) {
        if (holder == null || holder.grip == null || holder.view == null) return false;
        VRCObjectSync sync = holder.grip.GetComponent<VRCObjectSync>();
        if (holder.view.synced && sync == null) {
            sync = Undo.AddComponent<VRCObjectSync>(holder.grip.gameObject);
            sync.AllowCollisionOwnershipTransfer = false;
            return true;
        }
        if (!holder.view.synced && sync != null) {
            Undo.DestroyObjectImmediate(sync);
            return true;
        }
        return false;
    }

    // The move list (12.15) right of the board: a scroll rect over the rows (its content an empty rect sized by the view
    // to every row, which scrolling moves), the pooled rows in its viewport (enough to fill it and one more, moved by
    // the view within a row's height as it scrolls), and its scrollbar, shown while the rows don't fit. Each cell is
    // a button with a ChessMoveListCell calling the view's PressMoveCell. Dragging or the wheel over a cell scrolls the
    // list (the cells' parents carry the scroll rect).
    static void BuildMoveList(GameObject root, ChessBoard2DView view, UdonBehaviour udon, TMP_FontAsset font) {
        GameObject list = NewChild(root, "Move List", new Vector2(Margin + BoardSize + Gap, Margin),
            new Vector2(MoveListWidth, MoveListHeight));
        Image background = list.AddComponent<Image>();
        background.color = new Color(0.16f, 0.16f, 0.19f, 1f);
        background.raycastTarget = false;
        ScrollRect scroll = list.AddComponent<ScrollRect>();

        float viewportWidth = MoveListWidth - ScrollbarWidth;
        GameObject viewport = NewChild(list, "Viewport", Vector2.zero, new Vector2(viewportWidth, MoveListHeight));
        Image viewportImage = viewport.AddComponent<Image>();
        // Clear but hit by the pointer, so a drag anywhere on the list scrolls it.
        viewportImage.color = new Color(0, 0, 0, 0);
        viewport.AddComponent<RectMask2D>();
        GameObject content = NewChild(viewport, "Content", Vector2.zero, new Vector2(viewportWidth, 0));
        GameObject rows = NewChild(viewport, "Rows", Vector2.zero, Vector2.zero);

        int poolRows = Mathf.CeilToInt(MoveListHeight / MoveRowHeight) + 1;
        rows.GetComponent<RectTransform>().sizeDelta = new Vector2(viewportWidth, poolRows * MoveRowHeight);
        view.moveNumbers = new TextMeshProUGUI[poolRows];
        view.moveCells = new Button[2 * poolRows];
        view.moveCellImages = new Image[2 * poolRows];
        view.moveCellLabels = new TextMeshProUGUI[2 * poolRows];
        for (int r = 0; r < poolRows; r++) {
            GameObject row = NewChild(rows, "Row " + (r + 1), new Vector2(0, r * MoveRowHeight),
                new Vector2(viewportWidth, MoveRowHeight));
            TextMeshProUGUI number = NewText(row, "Number", font, 30, Vector2.zero, new Vector2(MoveNumberWidth, MoveRowHeight));
            number.alignment = TextAlignmentOptions.Right;
            number.color = new Color(0.65f, 0.65f, 0.7f);
            number.text = "";
            view.moveNumbers[r] = number;
            for (int c = 0; c < 2; c++) {
                int cell = 2 * r + c;
                GameObject cellObject = NewChild(row, c == 0 ? "White" : "Black",
                    new Vector2(MoveNumberWidth + MoveGap + c * (MoveCellWidth + MoveGap), 2),
                    new Vector2(MoveCellWidth, MoveRowHeight - 4));
                Image image = cellObject.AddComponent<Image>();
                image.color = view.moveCellColor;
                Button button = cellObject.AddComponent<Button>();
                button.targetGraphic = image;
                ChessMoveListCell target = UdonSharpUndo.AddComponent<ChessMoveListCell>(cellObject);
                target.view = view;
                target.cell = cell;
                UdonSharpEditorUtility.CopyProxyToUdon(target);
                UnityEventTools.AddStringPersistentListener(button.onClick,
                    new UnityAction<string>(UdonSharpEditorUtility.GetBackingUdonBehaviour(target).SendCustomEvent),
                    nameof(ChessMoveListCell.Press));
                TextMeshProUGUI label = NewText(cellObject, "Label", font, 34, new Vector2(12, 0),
                    new Vector2(MoveCellWidth - 12, MoveRowHeight - 4));
                label.color = view.moveTextColor;
                label.text = "";
                view.moveCells[cell] = button;
                view.moveCellImages[cell] = image;
                view.moveCellLabels[cell] = label;
            }
        }

        // The scrollbar along the right edge.
        GameObject bar = NewChild(list, "Scrollbar", new Vector2(viewportWidth, 0),
            new Vector2(ScrollbarWidth, MoveListHeight));
        Image track = bar.AddComponent<Image>();
        track.color = new Color(0.22f, 0.22f, 0.26f, 1f);
        GameObject area = NewChild(bar, "Sliding Area", Vector2.zero, Vector2.zero);
        RectTransform areaRect = area.GetComponent<RectTransform>();
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(3, 3);
        areaRect.offsetMax = new Vector2(-3, -3);
        GameObject handle = NewChild(area, "Handle", Vector2.zero, Vector2.zero);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;
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
        scroll.scrollSensitivity = MoveRowHeight;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        UnityEventTools.AddStringPersistentListener(scroll.onValueChanged, new UnityAction<string>(udon.SendCustomEvent),
            nameof(ChessBoard2DView.OnMoveListScrolled));

        view.moveList = scroll;
        view.moveListViewport = viewport.GetComponent<RectTransform>();
        view.moveListContent = content.GetComponent<RectTransform>();
        view.moveListRows = rows.GetComponent<RectTransform>();
        view.moveRowHeight = MoveRowHeight;
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

    // A button calling eventName on the view, greyed while not interactable.
    static Button AddButton(GameObject parent, UdonBehaviour udon, string name, string label, string eventName,
        TMP_FontAsset font, Vector2 topLeft, Vector2 size) {
        GameObject buttonObject = NewChild(parent, name, topLeft, size);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.85f, 0.85f, 0.88f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.48f, 1f);
        button.colors = colors;
        UnityEventTools.AddStringPersistentListener(button.onClick, new UnityAction<string>(udon.SendCustomEvent),
            eventName);
        TextMeshProUGUI text = NewText(buttonObject, "Label", font, 56, Vector2.zero, size);
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.black;
        text.text = label;
        return button;
    }

    // As ChessSettingsMenuBuilder.EnsureProgramAssets: the view's and the move list cells' program assets, created and
    // compiled when missing, after which the menu item has to be run again.
    static bool EnsureProgramAsset() {
        bool created = CreateProgramAssetIfMissing(ViewScriptPath, ViewProgramPath, "ChessBoard2DViewBuilder");
        created |= CreateProgramAssetIfMissing(CellScriptPath, CellProgramPath, "ChessBoard2DViewBuilder");
        created |= CreateProgramAssetIfMissing(HolderScriptPath, HolderProgramPath, "ChessBoard2DViewBuilder");
        if (!created) return true;
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        EditorUtility.DisplayDialog("Create 2D Board View", "Created the missing program assets next to "
            + ViewScriptPath + ". Once UdonSharp has finished compiling, run the Tools > Chess Board menu item "
            + "again.", "OK");
        return false;
    }

    // A text at a position from the parent's top left, in canvas pixels.
    static TextMeshProUGUI NewText(GameObject parent, string name, TMP_FontAsset font, int size, Vector2 topLeft,
        Vector2 extent) {
        GameObject child = NewChild(parent, name, topLeft, extent);
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
}
