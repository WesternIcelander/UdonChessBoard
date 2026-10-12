using System.Collections.Generic;
using SiggiChessBoard;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

// Builds the physical board (PLAN.md 9.1, ChessBoardView.cs) in the open scene for the selected ChessBoard, or the
// scene's only one: a table, the board's surface and squares, and the 32 piece objects, wired to a ChessBoardView. An
// existing view for the same board is replaced, keeping its place. The piece meshes and the materials are generated
// placeholders in Assets/Siggi/ChessBoard/Models/, made only when missing, so they can be replaced there (or other
// meshes assigned in the view's Meshes field). Menu: Tools > Chess Board > Create Board View. Plain editor C#, not Udon.
public static class ChessBoardViewBuilder {
    const string ViewScriptPath = "Assets/Siggi/ChessBoard/ChessBoardView.cs";
    const string ViewProgramPath = "Assets/Siggi/ChessBoard/ChessBoardView.asset";
    const string PieceScriptPath = "Assets/Siggi/ChessBoard/ChessPiece.cs";
    const string PieceProgramPath = "Assets/Siggi/ChessBoard/ChessPiece.asset";
    const string SquareScriptPath = "Assets/Siggi/ChessBoard/ChessSquare.cs";
    const string SquareProgramPath = "Assets/Siggi/ChessBoard/ChessSquare.asset";
    const string ModelsFolder = "Assets/Siggi/ChessBoard/Models";

    const float SquareSize = 0.05f;
    const float Border = 0.02f;
    const float BoardThickness = 0.02f;
    const float TableHeight = 0.8f;

    static readonly string[] TypeNames = { "", "Pawn", "Knight", "Bishop", "Rook", "Queen", "King" };
    // The back rank from a to h, by piece type, for the pieces' places in the editor.
    static readonly int[] BackRank = {
        ChessRules.Rook, ChessRules.Knight, ChessRules.Bishop, ChessRules.Queen, ChessRules.King, ChessRules.Bishop,
        ChessRules.Knight, ChessRules.Rook,
    };

    [MenuItem("Tools/Chess Board/Create Board View")]
    static void Create() {
        ChessBoard board = FindBoard();
        if (board == null) return;
        if (!EnsureProgramAssets()) return;

        Mesh[] meshes = new Mesh[7];
        for (int type = ChessRules.Pawn; type <= ChessRules.King; type++) meshes[type] = PieceMesh(type);
        Material whitePieces = MaterialAsset("White Pieces", new Color(0.92f, 0.89f, 0.82f), 0.55f);
        Material blackPieces = MaterialAsset("Black Pieces", new Color(0.13f, 0.12f, 0.12f), 0.6f);
        Material lightSquares = MaterialAsset("Light Squares", new Color(0.93f, 0.85f, 0.71f), 0.3f);
        Material darkSquares = MaterialAsset("Dark Squares", new Color(0.55f, 0.37f, 0.24f), 0.3f);
        Material frame = MaterialAsset("Board Frame", new Color(0.3f, 0.19f, 0.11f), 0.35f);
        Material castleSpace = MaterialAsset("Castle Space", new Color(0.35f, 0.62f, 0.4f), 0.3f);
        Mesh squares = SquaresMesh();

        GameObject root = new GameObject("ChessBoardView (" + board.gameObject.name + ")");
        Undo.RegisterCreatedObjectUndo(root, "Create Board View");
        GameObject old = ExistingView(board);
        root.transform.position = board.transform.position + new Vector3(0, TableHeight, 0);

        float boardWidth = 8 * SquareSize + 2 * Border;
        // Deep enough for the castle drop spaces, a square beyond each back rank.
        GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "Table";
        table.transform.SetParent(root.transform, false);
        table.transform.localPosition = new Vector3(0, -(TableHeight + BoardThickness) / 2, 0);
        table.transform.localScale = new Vector3(boardWidth * 1.6f, TableHeight - BoardThickness, boardWidth * 1.3f);
        table.GetComponent<MeshRenderer>().sharedMaterial = frame;

        GameObject boardBase = GameObject.CreatePrimitive(PrimitiveType.Cube);
        boardBase.name = "Board";
        boardBase.transform.SetParent(root.transform, false);
        boardBase.transform.localPosition = new Vector3(0, -BoardThickness / 2, 0);
        boardBase.transform.localScale = new Vector3(boardWidth, BoardThickness, boardWidth);
        boardBase.GetComponent<MeshRenderer>().sharedMaterial = frame;

        GameObject squaresObject = new GameObject("Squares");
        squaresObject.transform.SetParent(root.transform, false);
        squaresObject.AddComponent<MeshFilter>().sharedMesh = squares;
        squaresObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { lightSquares, darkSquares };

        ChessBoardView view = UdonSharpUndo.AddComponent<ChessBoardView>(root);
        view.board = board;
        view.squareSize = SquareSize;
        view.meshes = meshes;
        view.pieces = new Transform[ChessBoardView.PieceCount];
        view.pieceFilters = new MeshFilter[ChessBoardView.PieceCount];
        view.piecePickups = new VRCPickup[ChessBoardView.PieceCount];
        view.pieceColliders = new BoxCollider[ChessBoardView.PieceCount];
        view.pieceBehaviours = new ChessPiece[ChessBoardView.PieceCount];
        GameObject piecesParent = new GameObject("Pieces");
        piecesParent.transform.SetParent(root.transform, false);
        for (int obj = 0; obj < ChessBoardView.PieceCount; obj++) {
            bool white = obj < ChessBoardView.PiecesPerColor;
            int index = obj % ChessBoardView.PiecesPerColor;
            // Placed as at the start, in the order the view maps them on Start (a1 to h8): White back rank then pawns,
            // Black pawns then back rank.
            int file = index % 8;
            bool backRank = white == (index < 8);
            int type = backRank ? BackRank[file] : ChessRules.Pawn;
            int rank = white ? (backRank ? 0 : 1) : (backRank ? 7 : 6);
            GameObject piece = new GameObject((white ? "White " : "Black ") + (index + 1));
            piece.transform.SetParent(piecesParent.transform, false);
            piece.transform.localPosition = new Vector3((file - 3.5f) * SquareSize, 0, (rank - 3.5f) * SquareSize);
            piece.transform.localRotation = white ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
            MeshFilter filter = piece.AddComponent<MeshFilter>();
            filter.sharedMesh = meshes[type];
            piece.AddComponent<MeshRenderer>().sharedMaterial = white ? whitePieces : blackPieces;
            view.pieces[obj] = piece.transform;
            view.pieceFilters[obj] = filter;
            AddPickup(piece, view, obj, meshes[type]);
        }
        view.castleSpaces = new GameObject[2];
        view.castleSpaceTargets = new ChessSquare[2];
        for (int side = 0; side < 2; side++) view.castleSpaces[side] = CastleSpace(root, view, side, castleSpace);
        AddSquareTargets(root, view);
        UdonSharpEditorUtility.CopyProxyToUdon(view);
        if (old != null) {
            root.transform.SetParent(old.transform.parent, false);
            root.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
            root.transform.position = old.transform.position;
            root.transform.rotation = old.transform.rotation;
            root.transform.localScale = old.transform.localScale;
            Undo.DestroyObjectImmediate(old);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[ChessBoardViewBuilder] " + (old != null ? "Replaced " : "Created ") + root.name + " for "
            + board.gameObject.name + ". Move it where you like and save the scene.");
    }

    // Makes a piece object something to carry (PLAN.md 9.2, docs/physical-pieces.md): a kinematic rigidbody (the
    // board, not physics, puts pieces down), a box collider fitted to its mesh (the view refits it when the mesh
    // changes), a VRCPickup that the view makes pickupable only for players who may touch the pieces, a VRCObjectSync
    // so others see it carried, and a ChessPiece asking the board about ownership. On the Pickup layer, so carried
    // pieces don't push players around.
    static void AddPickup(GameObject piece, ChessBoardView view, int obj, Mesh mesh) {
        int pickupLayer = LayerMask.NameToLayer("Pickup");
        if (pickupLayer >= 0) piece.layer = pickupLayer;
        Rigidbody body = piece.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        BoxCollider box = piece.AddComponent<BoxCollider>();
        box.center = mesh.bounds.center;
        box.size = mesh.bounds.size;
        VRCPickup pickup = piece.AddComponent<VRCPickup>();
        pickup.pickupable = false;
        pickup.DisallowTheft = true;
        pickup.InteractionText = "Move";
        piece.AddComponent<VRCObjectSync>();
        ChessPiece behaviour = UdonSharpUndo.AddComponent<ChessPiece>(piece);
        behaviour.view = view;
        behaviour.index = obj;
        UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
        view.piecePickups[obj] = pickup;
        view.pieceColliders[obj] = box;
        view.pieceBehaviours[obj] = behaviour;
    }

    // A castle drop space (docs/player-input.md): a pad the size of a square, as thick as the board and just above
    // its surface. A held king's drop on it is found by position; for desktop clicks (step 9.3c) the space has a
    // collider over the pad and a ChessSquare, which the view makes interactive only while a selected king needs it.
    // Built hidden; the view places and shows it while a held or selected king needs it.
    static GameObject CastleSpace(GameObject root, ChessBoardView view, int side, Material material) {
        GameObject space = new GameObject(side == ChessBoard.CastleKingSide
            ? "Castle Space (king side)" : "Castle Space (queen side)");
        space.transform.SetParent(root.transform, false);
        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = "Pad";
        Object.DestroyImmediate(pad.GetComponent<BoxCollider>());
        pad.transform.SetParent(space.transform, false);
        pad.transform.localPosition = new Vector3(0, -BoardThickness / 2 + 0.001f, 0);
        pad.transform.localScale = new Vector3(SquareSize * 0.92f, BoardThickness, SquareSize * 0.92f);
        pad.GetComponent<MeshRenderer>().sharedMaterial = material;
        BoxCollider box = space.AddComponent<BoxCollider>();
        box.center = pad.transform.localPosition;
        box.size = pad.transform.localScale;
        ChessSquare target = UdonSharpUndo.AddComponent<ChessSquare>(space);
        target.view = view;
        target.square = ChessRules.NoSquare;
        target.castleSide = side;
        UdonSharpEditorUtility.CopyProxyToUdon(target);
        view.castleSpaceTargets[side] = target;
        space.SetActive(false);
        return space;
    }

    // Desktop mode's square targets (step 9.3c): one object per square, a1 to h8, under "Square Targets", each a thin
    // box collider over its square with a ChessSquare, all inactive; the view activates the ones a click on would do
    // something. Interact doesn't say where a collider was hit, so each square needs its own.
    static void AddSquareTargets(GameObject root, ChessBoardView view) {
        GameObject parent = new GameObject("Square Targets");
        parent.transform.SetParent(root.transform, false);
        view.squareTargets = new ChessSquare[64];
        const string files = "abcdefgh";
        for (int square = 0; square < 64; square++) {
            int file = square % 8;
            int rank = square / 8;
            GameObject target = new GameObject(files[file] + (rank + 1).ToString());
            target.transform.SetParent(parent.transform, false);
            target.transform.localPosition = new Vector3((file - 3.5f) * SquareSize, 0, (rank - 3.5f) * SquareSize);
            BoxCollider box = target.AddComponent<BoxCollider>();
            box.center = new Vector3(0, 0.001f, 0);
            box.size = new Vector3(SquareSize, 0.002f, SquareSize);
            ChessSquare behaviour = UdonSharpUndo.AddComponent<ChessSquare>(target);
            behaviour.view = view;
            behaviour.square = square;
            behaviour.castleSide = ChessBoardView.NoCastleSide;
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
            view.squareTargets[square] = behaviour;
            target.SetActive(false);
        }
    }

    // The scene's board view for a board (built earlier), or null.
    static GameObject ExistingView(ChessBoard board) {
        ChessBoardView[] views = Object.FindObjectsOfType<ChessBoardView>(true);
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
        EditorUtility.DisplayDialog("Create Board View", boards.Length == 0
            ? "There's no ChessBoard in the open scene."
            : "There are several ChessBoards in the scene. Select the one the view is for, then try again.", "OK");
        return null;
    }

    // UdonSharp needs a compiled program asset next to a script before its behaviour can be added. The repository
    // has them (the view's, the pieces' and the squares'); any that's missing is created and compiled, and the menu item has to be
    // run again afterwards.
    static bool EnsureProgramAssets() {
        bool created = CreateProgramAssetIfMissing(ViewScriptPath, ViewProgramPath);
        created |= CreateProgramAssetIfMissing(PieceScriptPath, PieceProgramPath);
        created |= CreateProgramAssetIfMissing(SquareScriptPath, SquareProgramPath);
        if (!created) return true;
        AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
        EditorUtility.DisplayDialog("Create Board View", "Created the missing program assets next to "
            + ViewScriptPath + ", " + PieceScriptPath + " or " + SquareScriptPath + ". Once UdonSharp has finished compiling, run Tools > Chess Board > "
            + "Create Board View again.", "OK");
        return false;
    }

    // Creates the program asset for a script if it's missing; whether it did (also logged when the script is missing,
    // which stops the build the same way).
    static bool CreateProgramAssetIfMissing(string scriptPath, string programPath) {
        if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath) != null) return false;
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        if (script == null) {
            Debug.LogError("[ChessBoardViewBuilder] " + scriptPath + " not found.");
            return true;
        }
        UdonSharpProgramAsset program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
        program.sourceCsScript = script;
        AssetDatabase.CreateAsset(program, programPath);
        return true;
    }

    // ---- Generated assets ----

    static void EnsureModelsFolder() {
        if (!AssetDatabase.IsValidFolder(ModelsFolder)) AssetDatabase.CreateFolder("Assets/Siggi/ChessBoard", "Models");
    }

    // The material asset of this name, made with the Standard shader if missing.
    static Material MaterialAsset(string name, Color color, float smoothness) {
        string path = ModelsFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        EnsureModelsFolder();
        material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.SetFloat("_Glossiness", smoothness);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // The piece type's mesh asset, generated if missing: turned profiles (radius, height in metres, bottom to top)
    // with a few boxes for the rook's battlements, the knight's head and the king's cross. Origin at the piece's
    // base centre; the knight faces +z.
    static Mesh PieceMesh(int type) {
        string path = ModelsFolder + "/" + TypeNames[type] + ".asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        EnsureModelsFolder();
        MeshBuilder b = new MeshBuilder();
        switch (type) {
            case ChessRules.Pawn:
                b.Lathe(new[] {
                    new Vector2(0.016f, 0), new Vector2(0.016f, 0.004f), new Vector2(0.012f, 0.007f),
                    new Vector2(0.007f, 0.012f), new Vector2(0.005f, 0.02f), new Vector2(0.008f, 0.022f),
                    new Vector2(0.005f, 0.024f),
                });
                b.Sphere(new Vector3(0, 0.03f, 0), 0.0075f);
                break;
            case ChessRules.Rook:
                b.Lathe(new[] {
                    new Vector2(0.017f, 0), new Vector2(0.017f, 0.005f), new Vector2(0.013f, 0.008f),
                    new Vector2(0.01f, 0.012f), new Vector2(0.01f, 0.03f), new Vector2(0.013f, 0.032f),
                    new Vector2(0.013f, 0.038f),
                });
                for (int i = 0; i < 4; i++) {
                    float angle = i * 90f + 45f;
                    Quaternion turn = Quaternion.Euler(0, angle, 0);
                    b.Box(turn * new Vector3(0, 0, 0.0105f) + new Vector3(0, 0.041f, 0),
                        new Vector3(0.008f, 0.006f, 0.005f), turn);
                }
                break;
            case ChessRules.Knight:
                b.Lathe(new[] {
                    new Vector2(0.017f, 0), new Vector2(0.017f, 0.005f), new Vector2(0.013f, 0.008f),
                    new Vector2(0.011f, 0.011f),
                });
                b.Box(new Vector3(0, 0.022f, -0.002f), new Vector3(0.011f, 0.024f, 0.014f), Quaternion.Euler(-15, 0, 0));
                b.Box(new Vector3(0, 0.034f, 0.006f), new Vector3(0.01f, 0.01f, 0.022f), Quaternion.Euler(20, 0, 0));
                b.Box(new Vector3(0, 0.041f, -0.003f), new Vector3(0.008f, 0.006f, 0.004f), Quaternion.identity);
                break;
            case ChessRules.Bishop:
                b.Lathe(new[] {
                    new Vector2(0.016f, 0), new Vector2(0.016f, 0.004f), new Vector2(0.012f, 0.007f),
                    new Vector2(0.007f, 0.014f), new Vector2(0.006f, 0.028f), new Vector2(0.009f, 0.03f),
                    new Vector2(0.006f, 0.032f), new Vector2(0.009f, 0.038f), new Vector2(0.007f, 0.044f),
                    new Vector2(0.001f, 0.049f),
                });
                b.Sphere(new Vector3(0, 0.051f, 0), 0.0025f);
                break;
            case ChessRules.Queen:
                b.Lathe(new[] {
                    new Vector2(0.018f, 0), new Vector2(0.018f, 0.005f), new Vector2(0.013f, 0.008f),
                    new Vector2(0.008f, 0.016f), new Vector2(0.006f, 0.036f), new Vector2(0.01f, 0.038f),
                    new Vector2(0.007f, 0.04f), new Vector2(0.011f, 0.052f), new Vector2(0.004f, 0.054f),
                });
                b.Sphere(new Vector3(0, 0.058f, 0), 0.004f);
                break;
            case ChessRules.King:
                b.Lathe(new[] {
                    new Vector2(0.018f, 0), new Vector2(0.018f, 0.005f), new Vector2(0.013f, 0.008f),
                    new Vector2(0.008f, 0.016f), new Vector2(0.006f, 0.04f), new Vector2(0.01f, 0.042f),
                    new Vector2(0.007f, 0.044f), new Vector2(0.01f, 0.055f), new Vector2(0.003f, 0.057f),
                });
                b.Box(new Vector3(0, 0.063f, 0), new Vector3(0.003f, 0.012f, 0.003f), Quaternion.identity);
                b.Box(new Vector3(0, 0.065f, 0), new Vector3(0.009f, 0.003f, 0.003f), Quaternion.identity);
                break;
        }
        mesh = b.ToMesh(TypeNames[type]);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // The 64 squares as one mesh just above the surface, a1 dark at the view's -x -z corner: submesh 0 the light
    // squares, 1 the dark ones.
    static Mesh SquaresMesh() {
        string path = ModelsFolder + "/Squares.asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        EnsureModelsFolder();
        List<Vector3> vertices = new List<Vector3>();
        List<int> light = new List<int>();
        List<int> dark = new List<int>();
        const float y = 0.0005f;
        for (int square = 0; square < 64; square++) {
            int file = square % 8;
            int rank = square / 8;
            float x0 = (file - 4) * SquareSize;
            float z0 = (rank - 4) * SquareSize;
            int first = vertices.Count;
            vertices.Add(new Vector3(x0, y, z0));
            vertices.Add(new Vector3(x0, y, z0 + SquareSize));
            vertices.Add(new Vector3(x0 + SquareSize, y, z0 + SquareSize));
            vertices.Add(new Vector3(x0 + SquareSize, y, z0));
            List<int> triangles = (file + rank) % 2 == 0 ? dark : light;
            triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }
        mesh = new Mesh { name = "Squares" };
        mesh.SetVertices(vertices);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(light, 0);
        mesh.SetTriangles(dark, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // Collects the triangles of a generated piece. Each lathe band and each box face has its own vertices, so the
    // edges between them stay sharp.
    class MeshBuilder {
        const int Segments = 32;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<int> triangles = new List<int>();

        // A surface turned around the y axis from a profile of (radius, height), bottom to top, closed at both ends.
        public void Lathe(Vector2[] profile) {
            Disc(profile[0].y, profile[0].x, false);
            for (int i = 0; i + 1 < profile.Length; i++) Band(profile[i], profile[i + 1]);
            Vector2 top = profile[profile.Length - 1];
            if (top.x > 0) Disc(top.y, top.x, true);
        }

        public void Sphere(Vector3 centre, float radius) {
            const int rings = 8;
            for (int i = 0; i < rings; i++) {
                float a0 = Mathf.PI * i / rings - Mathf.PI / 2;
                float a1 = Mathf.PI * (i + 1) / rings - Mathf.PI / 2;
                Vector2 p0 = new Vector2(Mathf.Cos(a0) * radius, centre.y + Mathf.Sin(a0) * radius);
                Vector2 p1 = new Vector2(Mathf.Cos(a1) * radius, centre.y + Mathf.Sin(a1) * radius);
                Band(p0, p1);
            }
        }

        public void Box(Vector3 centre, Vector3 size, Quaternion rotation) {
            Vector3 h = size / 2;
            Vector3[] c = new Vector3[8];
            for (int i = 0; i < 8; i++) {
                Vector3 corner = new Vector3((i & 1) != 0 ? h.x : -h.x, (i & 2) != 0 ? h.y : -h.y,
                    (i & 4) != 0 ? h.z : -h.z);
                c[i] = centre + rotation * corner;
            }
            Quad(c[0], c[2], c[3], c[1]); // -z
            Quad(c[5], c[7], c[6], c[4]); // +z
            Quad(c[4], c[6], c[2], c[0]); // -x
            Quad(c[1], c[3], c[7], c[5]); // +x
            Quad(c[2], c[6], c[7], c[3]); // +y
            Quad(c[4], c[0], c[1], c[5]); // -y
        }

        void Band(Vector2 low, Vector2 high) {
            for (int s = 0; s < Segments; s++) {
                Quaternion a = Quaternion.Euler(0, 360f * s / Segments, 0);
                Quaternion b = Quaternion.Euler(0, 360f * (s + 1) / Segments, 0);
                Quad(a * new Vector3(low.x, 0, 0) + Vector3.up * low.y, b * new Vector3(low.x, 0, 0) + Vector3.up * low.y,
                    b * new Vector3(high.x, 0, 0) + Vector3.up * high.y, a * new Vector3(high.x, 0, 0) + Vector3.up * high.y);
            }
        }

        void Disc(float y, float radius, bool up) {
            int centre = vertices.Count;
            vertices.Add(new Vector3(0, y, 0));
            for (int s = 0; s < Segments; s++) {
                vertices.Add(Quaternion.Euler(0, 360f * s / Segments, 0) * new Vector3(radius, 0, 0) + Vector3.up * y);
            }
            for (int s = 0; s < Segments; s++) {
                int a = centre + 1 + s;
                int b = centre + 1 + (s + 1) % Segments;
                if (up) triangles.AddRange(new[] { centre, a, b });
                else triangles.AddRange(new[] { centre, b, a });
            }
        }

        // Two triangles, a b c d clockwise seen from the front (Unity's winding).
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) {
            int first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }

        public Mesh ToMesh(string name) {
            Mesh mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
