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

// Places the 2D board view (PLAN.md 12.13 to 12.16, ChessBoard2DView.cs, docs/board-2d.md) in the open scene, as an
// instance of one of its two prefabs (step 14.8, docs/prefabs.md; made when missing, EnsureViewPrefabs):
// - 2D Chess Board View (BuildView): the fixed view, a world-space canvas with the selector (left, "Board N", right),
//   the 8x8 board with a piece image on each square, the players and status texts under it, the step buttons (<<, <,
//   >, >>) along the bottom, and the move list (12.15) to the right, the full height. The pieces wear the sprites in
//   Sprites2D/ (KingW.png ... PawnB.png), whose import is set to Sprite here when it isn't.
// - Movable 2D Chess Board View (BuildMovable): a holder (ChessBoard2DViewHolder.cs, 12.16) with a nested instance of
//   the fixed prefab, so a change to that reaches both, and the grip and corner to move and resize it. The grip has no
//   VRCObjectSync in the prefab, as its view isn't synced; ChessBoard2DViewSyncCheck gives an instance's grip one (an
//   added component, an override) before every build when its view's Synced is on, so neither prefab needs a variant
//   per case (decided in 14.8).
// The instance's boards (every board in the scene by board number), locked board and Synced are set on it.
// - Create 2D Board View: the selected 2D view, or the scene's only one, is replaced where it stands (in a holder, by a
//   movable one), keeping its boards, locked board and Synced; otherwise a new one stands behind the boards, facing
//   them.
// - Create Locked 2D Board View: a view locked to the selected board (12.14), replacing one already locked to it where
//   it stands, or new behind that board.
// - Create Movable 2D Board View (12.16): the selected view (or the selected holder's) replaced by a movable one where
//   it stands; with nothing selected, a new movable view beside the others.
// - Update 2D View Prefabs: saves both prefabs again in place, and replaces the scene's views and holders built before
//   14.8 with instances of them.
// Whatever pointed at a replaced view or holder (Board2DTest's) is pointed at the new one. Plain editor C#, not Udon.
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
        Place("Create 2D Board View", old, old != null ? old.lockedBoard : null, NewViewPosition(),
            old != null && HolderOf(old) != null);
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
        Place("Create Locked 2D Board View", old, board, position, old != null && HolderOf(old) != null);
    }

    // One more view, whatever is selected: beside the new view's place, a view's width (and a gap) along +x per view
    // already in the scene (e.g. one synced and one not, 12.14's several-client run).
    [MenuItem("Tools/Chess Board/Create Another 2D Board View")]
    static void CreateAnother() {
        Place("Create Another 2D Board View", null, null, AnotherViewPosition(), false);
    }

    // A view players can move and resize (12.16): the selected view (or the view of the selected holder) replaced by
    // a movable one where it stands; otherwise a new movable view beside the others.
    [MenuItem("Tools/Chess Board/Create Movable 2D Board View")]
    static void CreateMovable() {
        ChessBoard2DView old = null;
        GameObject selected = Selection.activeGameObject;
        if (selected != null) {
            ChessBoard2DViewHolder holder = selected.GetComponent<ChessBoard2DViewHolder>();
            old = holder != null ? holder.view : selected.GetComponent<ChessBoard2DView>();
        }
        Place("Create Movable 2D Board View", old, old != null ? old.lockedBoard : null, AnotherViewPosition(), true);
    }

    // Places an instance of the view prefab (movable: the movable one) locked to locked (none when null), replacing old
    // where it stands (old's holder too, when it has one), keeping its name, boards and Synced (and the holder's name
    // and Movable), or new at position, named ChessBoard2DView, or ChessBoard2DView (N) beside N - 1 others (a movable
    // one's holder that name and Holder). Whatever pointed at old and its holder is pointed at the new ones.
    static ChessBoard2DView Place(string menuItem, ChessBoard2DView old, ChessBoard locked, Vector3 position,
            bool movable) {
        if (!ProgramAssetsReady(menuItem)) return null;
        string prefabNote = EnsureViewPrefabs();
        ChessBoard2DViewHolder oldHolder = old != null ? HolderOf(old) : null;
        int others = Object.FindObjectsOfType<ChessBoard2DView>(true).Length;
        string name = old != null ? old.gameObject.name
            : others == 0 ? "ChessBoard2DView" : "ChessBoard2DView (" + (others + 1) + ")";

        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(movable ? MovablePrefabPath : ViewPrefabPath));
        Undo.RegisterCreatedObjectUndo(root, menuItem);
        ChessBoard2DViewHolder holder = movable ? root.GetComponent<ChessBoard2DViewHolder>() : null;
        ChessBoard2DView view = movable ? holder.view : root.GetComponent<ChessBoard2DView>();
        view.gameObject.name = name;
        root.name = movable ? (oldHolder != null ? oldHolder.gameObject.name : name + " Holder") : name;
        view.boards = old != null ? old.boards : SceneBoards();
        view.lockedBoard = locked;
        view.synced = old != null && old.synced;
        RecordInstance(view);
        if (holder != null) {
            holder.movable = oldHolder == null || oldHolder.movable;
            RecordInstance(holder);
        }

        // The old view's (or its holder's) place: a holder's origin is its view's centre, as the fixed view's is.
        Transform rootTransform = root.transform;
        Transform oldRoot = oldHolder != null ? oldHolder.transform : old != null ? old.transform : null;
        // Facing the boards: seen from them, looking along +z.
        rootTransform.SetPositionAndRotation(position, Quaternion.identity);
        int repointed = 0;
        bool replaced = oldRoot != null;
        if (replaced) {
            rootTransform.SetParent(oldRoot.parent, false);
            rootTransform.SetSiblingIndex(oldRoot.GetSiblingIndex());
            rootTransform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
            // The canvas's own scale when it stands alone; a holder's scale when replacing one by one.
            if (!movable) {
                rootTransform.localScale = oldHolder != null ? old.transform.lossyScale : old.transform.localScale;
            } else if (oldHolder != null) {
                rootTransform.localScale = oldHolder.transform.localScale;
            }
            if (oldHolder != null && holder != null) repointed += ChessBoardViewBuilder.RepointReferences(oldHolder, holder);
            repointed += ChessBoardViewBuilder.RepointReferences(old, view);
            Undo.DestroyObjectImmediate(oldRoot.gameObject);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(root);
        PrefabUtility.RecordPrefabInstancePropertyModifications(rootTransform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(view.gameObject);
        if (holder != null) MatchObjectSync(holder);
        // The view's text comes from the shared Chess Language (PLAN.md 13.5), made when the scene has
        // none; the I18n fields are left empty, for the I18n sanity check to fill on build and before Play Mode.
        EnsureChessLanguage(root.scene);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[ChessBoard2DViewBuilder] " + (replaced ? "Replaced" : "Created") + " the "
            + (movable ? "movable " : "") + "2D board view " + name + " ("
            + (locked != null ? "locked to Board " + (locked.boardNumber + 1) : view.boards.Length + " board(s)")
            + ", " + (view.synced ? "synced" : "not synced")
            + (holder != null ? (view.synced ? ", with" : ", no") + " VRCObjectSync on its grip" : "")
            + (replaced ? ", " + repointed + " behaviour(s) pointed at it" : "") + prefabNote
            + "). Move it where you like and save the scene.");
        return view;
    }

    // ---- The 2D Chess Board View and Movable 2D Chess Board View prefabs (step 14.8, docs/prefabs.md) ----

    const string ViewPrefabName = "2D Chess Board View";
    const string ViewPrefabPath = PrefabsFolder + "/" + ViewPrefabName + ".prefab";
    const string MovablePrefabName = "Movable 2D Chess Board View";
    const string MovablePrefabPath = PrefabsFolder + "/" + MovablePrefabName + ".prefab";

    [MenuItem("Tools/Chess Board/Update 2D View Prefabs")]
    static void UpdateViewPrefabs() {
        if (!ProgramAssetsReady("Update 2D View Prefabs")) return;
        string note = EnsureViewPrefabs();
        GameObject viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPrefabPath);
        GameObject movablePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MovablePrefabPath);
        foreach (ChessBoard2DView view in Object.FindObjectsOfType<ChessBoard2DView>(true)) {
            ChessBoard2DViewHolder holder = HolderOf(view);
            GameObject root = holder != null ? holder.gameObject : view.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(root)) continue;
            string name = root.name;
            Place("Update 2D View Prefabs", view, view.lockedBoard, view.transform.position, holder != null);
            note += ", " + name + " replaced by an instance";
        }
        Debug.Log("[ChessBoard2DViewBuilder] 2D view prefabs: " + (note.Length > 0 ? note.Substring(2) : "unchanged")
            + (viewPrefab != null && movablePrefab != null ? "" : " (a prefab is missing)"));
    }

    // Makes the fixed view's prefab when it's missing (BuildView), then the movable one's (BuildMovable, nesting the
    // fixed one), and otherwise saves each again in place. Returns a note for the log ("" when both were there).
    static string EnsureViewPrefabs() {
        return EnsurePrefab(ViewPrefabPath, ViewPrefabName, BuildView)
            + EnsurePrefab(MovablePrefabPath, MovablePrefabName, BuildMovable);
    }

    // A view without boards (a prefab's contents), not locked or synced, at the origin facing +z (seen looking along
    // +z), its origin the canvas's centre.
    static GameObject BuildView() {
        Sprite[] whiteSprites = LoadPieceSprites(ChessRules.White);
        Sprite[] blackSprites = LoadPieceSprites(ChessRules.Black);
        TMP_FontAsset font = DefaultFont();
        GameObject root = new GameObject(ViewPrefabName);
        root.layer = LayerMask.NameToLayer("Default");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
        rootRect.localScale = Vector3.one * 0.001f;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<VRCUiShape>();
        AddTriggerCollider(root);
        Image background = root.AddComponent<Image>();
        background.color = new Color(0.1f, 0.1f, 0.12f, 1f);
        background.raycastTarget = false;

        ChessBoard2DView view = UdonSharpUndo.AddComponent<ChessBoard2DView>(root);
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
        return root;
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

    // A movable view (a prefab's contents, 12.16): the holder at the origin, a nested instance of the fixed view's
    // prefab as its child at its origin, and the grip and corner placed from where the view stands: the grip centred
    // under its bottom edge, the corner just outside its bottom right. Min Size and Max Size are the defaults, as the
    // grip's width is fitted to the smallest size. The view isn't synced, so the grip has no VRCObjectSync.
    static GameObject BuildMovable() {
        GameObject root = new GameObject(MovablePrefabName);
        root.layer = LayerMask.NameToLayer("Default");
        ChessBoard2DViewHolder holder = UdonSharpUndo.AddComponent<ChessBoard2DViewHolder>(root);
        GameObject viewObject = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(ViewPrefabPath));
        Transform viewTransform = viewObject.transform;
        viewTransform.SetParent(root.transform, false);
        viewTransform.localPosition = Vector3.zero;
        viewTransform.localRotation = Quaternion.identity;
        ChessBoard2DView view = viewObject.GetComponent<ChessBoard2DView>();
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
        holder.gripPickup = AddHandle(holder.transform, "Grip", gripSize,
            bottomCentre - heading * new Vector3(0, GripGap + gripSize.y * 0.5f, 0), heading, material,
            out gripRenderer);
        holder.grip = holder.gripPickup.transform;
        holder.gripModel = gripRenderer.transform;
        Renderer cornerRenderer;
        Vector3 cornerOut = new Vector3(CornerSize * 0.5f, -CornerSize * 0.5f, 0);
        holder.cornerPickup = AddHandle(holder.transform, "Corner", Vector3.one * CornerSize,
            bottomRight + heading * cornerOut, heading, material, out cornerRenderer);
        holder.corner = holder.cornerPickup.transform;
        holder.handleRenderers = new[] { gripRenderer, cornerRenderer };
        MatchObjectSync(holder);
        UdonSharpEditorUtility.CopyProxyToUdon(holder);
        return root;
    }

    // A handle to pick up: an object with a trigger box collider of size, a kinematic rigidbody without gravity (it
    // stays where it's let go) and a VRCPickup (its hover text left empty: the holder sets it from the language
    // pack), on the Pickup layer, with a cube of that size as its visible part.
    static VRCPickup AddHandle(Transform parent, string name, Vector3 size, Vector3 position,
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
        pickup.InteractionText = "";
        pickup.UseText = "";

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
    // a button with a ChessMoveListCell calling the view's _PressMoveCell. Dragging or the wheel over a cell scrolls
    // the list (the cells' parents carry the scroll rect).
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
                    nameof(ChessMoveListCell._Press));
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
            nameof(ChessBoard2DView._OnMoveListScrolled));

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

    // Beside the new view's place: a view's width (and a gap) along +x per view already in the scene.
    static Vector3 AnotherViewPosition() {
        int count = Object.FindObjectsOfType<ChessBoard2DView>(true).Length;
        return NewViewPosition() + Vector3.right * (count * (CanvasWidth * 0.001f + 0.2f));
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

    // The view's, the move list cells' and the holder's program assets (EnsureProgramAssets), for menuItem.
    static bool ProgramAssetsReady(string menuItem) {
        return EnsureProgramAssets(menuItem, ViewScriptPath, ViewProgramPath, CellScriptPath, CellProgramPath,
            HolderScriptPath, HolderProgramPath);
    }
}
