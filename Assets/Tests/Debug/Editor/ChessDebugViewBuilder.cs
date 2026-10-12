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

// Builds the debug view (PLAN.md 8.1, ChessDebugView.cs) in the open scene for the selected ChessBoard, or the scene's
// only one: a world-space canvas with the 8x8 board, the status text and one button per public method of the view.
// An existing debug view for the same board is replaced, keeping its place, so it can be rebuilt when buttons change.
// Menu: Tools > Chess Board > Create Debug View. Plain editor C#, not Udon.
public static class ChessDebugViewBuilder {
    const string ViewScriptPath = "Assets/Tests/Debug/ChessDebugView.cs";
    const string ViewProgramPath = "Assets/Tests/Debug/ChessDebugView.asset";
    const string ImportPgnPath = "Assets/Tests/PGN/Siggi-vs-Nuka.pgn.txt";

    // Button label and the ChessDebugView method it calls, in rows of ButtonColumns.
    static readonly string[,] Buttons = {
        { "Join White", "JoinWhite" }, { "Join Black", "JoinBlack" }, { "Mode", "ToggleMode" },
        { "Clock", "CycleClock" }, { "Start", "StartGame" }, { "Reset", "ResetBoard" },
        { "Fool's mate move", "PlayFoolsMate" }, { "Knight shuffle move", "PlayKnightShuffle" },
        { "Random move", "PlayRandom" }, { "Resign", "Resign" }, { "Offer draw", "OfferDraw" },
        { "Accept draw", "AcceptDraw" }, { "Decline draw", "DeclineDraw" }, { "Claim draw", "ClaimDraw" },
        { "Enter replay", "EnterReplay" }, { "Leave replay", "LeaveReplay" }, { "Step back", "StepBack" },
        { "Step forward", "StepForward" }, { "Request takeback", "RequestTakeback" },
        { "Accept takeback", "AcceptTakeback" }, { "Decline takeback", "DeclineTakeback" },
        { "Continue from here", "ContinueGame" }, { "Arbiter: takebacks", "ToggleTakebacks" },
        { "Arbiter: draws", "ToggleDraws" }, { "Arbiter: clock forced", "ToggleClockForced" },
        { "Arbiter: clear overrides", "ClearOverrides" }, { "Re-snapshot", "Resnapshot" },
        { "Default takebacks", "ToggleDefaultTakebacks" }, { "Default reset time", "CycleDefaultResetTime" },
        { "Default auto-reset time", "CycleDefaultAutoResetTime" }, { "Take arbiter", "TakeArbiter" },
        { "Take board", "TakeBoard" }, { "Fidget", "Fidget" }, { "Forget notation", "ForgetNotation" },
        { "Import PGN", "ImportPgn" }, { "Import PGN (paused)", "ImportPaused" }, { "Resume import", "ResumeImport" },
        { "Input style", "CycleInputStyle" }, { "Take piece", "TakePiece" }, { "Check pieces", "CheckPieces" },
    };
    const int ButtonColumns = 7;

    const float CanvasWidth = 1600;
    const float CanvasHeight = 1040;
    const float Margin = 20;
    const float SquareSize = 60;
    const float ButtonHeight = 56;
    const float ButtonGap = 8;

    [MenuItem("Tools/Chess Board/Create Debug View")]
    static void Create() {
        ChessBoard board = FindBoard();
        if (board == null) return;
        if (!EnsureProgramAsset()) return;
        EnsureImporter(board);

        Font font = BuiltinFont();
        GameObject root = new GameObject("ChessDebugView (" + board.gameObject.name + ")");
        Undo.RegisterCreatedObjectUndo(root, "Create Debug View");
        GameObject old = ExistingView(board);
        root.layer = LayerMask.NameToLayer("Default");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
        rootRect.localScale = Vector3.one * 0.001f;
        rootRect.position = board.transform.position + new Vector3(0, 1.4f, 1.5f);
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<VRCUiShape>();

        Image background = root.AddComponent<Image>();
        background.color = new Color(0.12f, 0.12f, 0.14f, 0.95f);

        ChessDebugView view = UdonSharpUndo.AddComponent<ChessDebugView>(root);
        view.board = board;
        view.boardView = BoardView(board);
        view.importPgn = AssetDatabase.LoadAssetAtPath<TextAsset>(ImportPgnPath);
        if (view.importPgn == null) {
            Debug.LogWarning("[ChessDebugViewBuilder] " + ImportPgnPath + " not found; the import buttons need a PGN "
                + "in the view's Import Pgn field.");
        }
        view.squareImages = new Image[64];
        view.squareTexts = new Text[64];

        // The board, White at the bottom: a1 bottom left.
        for (int square = 0; square < 64; square++) {
            int file = square % 8;
            int rank = square / 8;
            GameObject cell = NewChild(root, SquareName(square),
                new Vector2(Margin + file * SquareSize, Margin + (7 - rank) * SquareSize),
                new Vector2(SquareSize, SquareSize));
            view.squareImages[square] = cell.AddComponent<Image>();
            Text piece = NewText(cell, "Piece", font, 40, TextAnchor.MiddleCenter);
            piece.fontStyle = FontStyle.Bold;
            Outline outline = piece.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.5f, 0.5f, 0.5f);
            view.squareTexts[square] = piece;
        }

        float statusLeft = Margin * 2 + 8 * SquareSize;
        GameObject status = NewChild(root, "Status", new Vector2(statusLeft, Margin),
            new Vector2(CanvasWidth - statusLeft - Margin, 8 * SquareSize + 100));
        view.statusText = NewText(status, "Text", font, 20, TextAnchor.UpperLeft);
        view.statusText.supportRichText = true;
        view.statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        view.statusText.verticalOverflow = VerticalWrapMode.Overflow;

        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(view);
        float buttonsTop = Margin * 2 + 8 * SquareSize + 100;
        float buttonWidth = (CanvasWidth - 2 * Margin - (ButtonColumns - 1) * ButtonGap) / ButtonColumns;
        for (int i = 0; i < Buttons.GetLength(0); i++) {
            int column = i % ButtonColumns;
            int row = i / ButtonColumns;
            GameObject buttonObject = NewChild(root, "Button " + Buttons[i, 1],
                new Vector2(Margin + column * (buttonWidth + ButtonGap), buttonsTop + row * (ButtonHeight + ButtonGap)),
                new Vector2(buttonWidth, ButtonHeight));
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.85f, 0.85f, 0.88f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            UnityEventTools.AddStringPersistentListener(button.onClick,
                new UnityAction<string>(udon.SendCustomEvent), Buttons[i, 1]);
            Text label = NewText(buttonObject, "Label", font, 20, TextAnchor.MiddleCenter);
            label.color = Color.black;
            label.text = Buttons[i, 0];
        }

        UdonSharpEditorUtility.CopyProxyToUdon(view);
        if (old != null) {
            root.transform.SetParent(old.transform.parent, false);
            root.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
            rootRect.position = old.transform.position;
            rootRect.rotation = old.transform.rotation;
            rootRect.localScale = old.transform.localScale;
            Undo.DestroyObjectImmediate(old);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[ChessDebugViewBuilder] " + (old != null ? "Replaced " : "Created ") + root.name + " for "
            + board.gameObject.name + ". Move it where you like and save the scene.");
    }

    // The import buttons (8.7) need the board to have an importer: if it has none, one is added as a child of the
    // board. The proxy holds the board's fields: only the importer is set on it (as the Inspector would), then copied to
    // the Udon side. Copying the Udon side to the proxy first wiped the board's Arbiter field (8.7).
    static void EnsureImporter(ChessBoard board) {
        if (board.importer != null) return;
        GameObject child = new GameObject("ChessPgnImporter");
        Undo.RegisterCreatedObjectUndo(child, "Create Debug View");
        child.transform.SetParent(board.transform, false);
        ChessPgnImporter importer = UdonSharpUndo.AddComponent<ChessPgnImporter>(child);
        SerializedObject boardFields = new SerializedObject(board);
        boardFields.FindProperty("importer").objectReferenceValue = importer;
        boardFields.ApplyModifiedProperties();
        UdonSharpEditorUtility.CopyProxyToUdon(board);
        PrefabUtility.RecordPrefabInstancePropertyModifications(UdonSharpEditorUtility.GetBackingUdonBehaviour(board));
        Debug.Log("[ChessDebugViewBuilder] Added a ChessPgnImporter to " + board.gameObject.name + " for its imports.");
    }

    // The scene's ChessBoardView for a board, for the pieces' buttons (9.2), or null with a warning.
    static ChessBoardView BoardView(ChessBoard board) {
        ChessBoardView[] views = Object.FindObjectsOfType<ChessBoardView>(true);
        for (int i = 0; i < views.Length; i++) {
            if (views[i].board == board) return views[i];
        }
        Debug.LogWarning("[ChessDebugViewBuilder] No ChessBoardView for " + board.gameObject.name + "; the Input style "
            + "and Take piece buttons need one in the debug view's Board View field.");
        return null;
    }

    // The scene's debug view for a board (built earlier), or null.
    static GameObject ExistingView(ChessBoard board) {
        ChessDebugView[] views = Object.FindObjectsOfType<ChessDebugView>(true);
        for (int i = 0; i < views.Length; i++) {
            if (views[i].board == board) return views[i].gameObject;
        }
        return null;
    }

    // The selected ChessBoard, or the scene's only one.
    static ChessBoard FindBoard() {
        if (Selection.activeGameObject != null) {
            ChessBoard selected = Selection.activeGameObject.GetComponent<ChessBoard>();
            if (selected != null) return selected;
        }
        ChessBoard[] boards = Object.FindObjectsOfType<ChessBoard>(true);
        if (boards.Length == 1) return boards[0];
        EditorUtility.DisplayDialog("Create Debug View", boards.Length == 0
            ? "There's no ChessBoard in the open scene."
            : "There are several ChessBoards in the scene. Select the one the debug view is for, then try again.", "OK");
        return null;
    }

    // UdonSharp needs a compiled program asset next to the script before the behaviour can be added. The repository
    // has one; if it's missing it's created and compiled, and the menu item has to be run again afterwards.
    static bool EnsureProgramAsset() {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ViewProgramPath) != null) return true;
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(ViewScriptPath);
        if (script == null) {
            Debug.LogError("[ChessDebugViewBuilder] " + ViewScriptPath + " not found.");
            return false;
        }
        UdonSharpProgramAsset program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        program.sourceCsScript = script;
        AssetDatabase.CreateAsset(program, ViewProgramPath);
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        EditorUtility.DisplayDialog("Create Debug View", "Created the debug view's program asset (" + ViewProgramPath
            + "). Once UdonSharp has finished compiling, run Tools > Chess Board > Create Debug View again.", "OK");
        return false;
    }

    static Font BuiltinFont() {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
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

    // A text filling its parent.
    static Text NewText(GameObject parent, string name, Font font, int size, TextAnchor alignment) {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(4, 2);
        rect.offsetMax = new Vector2(-4, -2);
        Text text = child.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    static string SquareName(int square) {
        return "Square " + (char)('a' + square % 8) + (square / 8 + 1);
    }
}
