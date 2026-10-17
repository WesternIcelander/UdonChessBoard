using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // A flat 2D view of one board at a time (docs/board-2d.md; PLAN.md 12.13): a UI canvas with the 8x8 board as it
    // shows (replay included), which board it is ("Board N"), and its players and status lines as the board's UI
    // writes them (ChessBoardUI.PlayersTextFor, StatusTextFor). Left and right step through the boards in use; nothing
    // else on it acts on a board. It listens to the board it shows, switching listener as it steps. Built in the scene
    // by Tools > Chess Board > Create 2D Board View (Editor/ChessBoard2DViewBuilder.cs).
    //
    // With synced on, the board shown is synced (a press takes ownership), so everyone sees the same board, late
    // joiners included; with it off, each player chooses for themselves and nothing is sent.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ChessBoard2DView : ChessBoardListener {
        // The boards to choose from, in this order (as the settings menu's). It starts on the first.
        [Tooltip("The boards this view can show, in the order Left and Right step through them.")]
        public ChessBoard[] boards = new ChessBoard[0];
        [Tooltip("On: everyone sees the board last chosen by anyone, late joiners included. Off: each player chooses for "
            + "themselves.")]
        public bool synced;
        // The squares, a1 = 0 to h8 = 63 (a1 bottom left): each square's background and the piece standing on it.
        public Image[] squareImages = new Image[64];
        public Image[] pieceImages = new Image[64];
        // Each colour's piece sprites by type, pawn, knight, bishop, rook, queen, king (ChessRules.Pawn - 1 on).
        [Tooltip("White's piece sprites: pawn, knight, bishop, rook, queen, king.")]
        public Sprite[] whiteSprites = new Sprite[6];
        [Tooltip("Black's piece sprites: pawn, knight, bishop, rook, queen, king.")]
        public Sprite[] blackSprites = new Sprite[6];
        // "Board 2", the board's players (both seats, with their clocks) and its status lines.
        public Text boardLabel;
        public Text playersLabel;
        public Text statusLabel;
        public Color lightSquare = new Color(0.93f, 0.85f, 0.71f);
        public Color darkSquare = new Color(0.71f, 0.53f, 0.39f);
        public Color lastMoveTint = new Color(0.85f, 0.85f, 0.3f);
        public Color checkTint = new Color(0.9f, 0.3f, 0.3f);

        // The index in boards of the board chosen, when synced.
        [UdonSynced] int syncedIndex;
        // The index in boards of the board shown (-1 for none), and the board listened to.
        int shown = -1;
        ChessBoard listened;
        // Drawn on the frame after a change, as the other views are; the status lines also as the clocks run.
        bool dirty;
        float nextStatusUpdate;
        int[] checkers = new int[2];
        int[] kings = new int[2];

        void Start() {
            if (boards == null || boards.Length == 0) {
                Debug.LogError("[ChessBoard2DView] " + gameObject.name + ": the Boards list is empty.");
                return;
            }
            int index = synced ? syncedIndex : 0;
            LogShown(index, "on start");
            Show(index);
        }

        void Update() {
            if (dirty) Refresh();
            else if (Time.time >= nextStatusUpdate) UpdateStatus();
        }

        public override void OnBoardChanged() {
            dirty = true;
        }

        // Only the move list's text changed: the replay line may name a move it didn't know before.
        public override void OnNotationFilled() {
            UpdateStatus();
        }

        public override void OnDeserialization() {
            if (synced) {
                if (syncedIndex != shown) LogShown(syncedIndex, "chosen by " + OwnerName());
                Show(syncedIndex);
            }
        }

        // ---- Choosing the board ----

        // The selector's buttons: the previous or next board in use, wrapping round.
        public void PressLeft() {
            Step(-1);
        }

        public void PressRight() {
            Step(1);
        }

        // Steps to the next board in use in direction (1 right, -1 left), skipping reset ones; nothing when no other
        // board is in use. Synced, the choice is sent to everyone.
        public void Step(int direction) {
            int count = boards == null ? 0 : boards.Length;
            if (count == 0) return;
            bool[] inUse = new bool[count];
            for (int i = 0; i < count; i++) {
                inUse[i] = Utilities.IsValid(boards[i]) && boards[i].lifecycle != ChessBoard.LifecycleReset;
            }
            int target = StepTarget(inUse, shown, direction);
            if (target < 0 || target == shown) return;
            if (synced) {
                VRCPlayerApi local = Networking.LocalPlayer;
                if (Utilities.IsValid(local) && !Networking.IsOwner(local, gameObject)) {
                    Networking.SetOwner(local, gameObject);
                }
                syncedIndex = target;
                RequestSerialization();
            }
            LogShown(target, synced ? "chosen here, sent" : "chosen here");
            Show(target);
        }

        // The board to step to from current in direction (1 or -1): the first one in use after it that way, wrapping
        // round, or -1 when no other board is in use (the board shown isn't left for itself, in use or not).
        public static int StepTarget(bool[] inUse, int current, int direction) {
            int count = inUse == null ? 0 : inUse.Length;
            if (count == 0) return -1;
            int step = direction < 0 ? -1 : 1;
            int start = current >= 0 && current < count ? current : (step > 0 ? -1 : count);
            for (int i = 1; i <= count; i++) {
                int index = ((start + step * i) % count + count) % count;
                if (index == current) return -1;
                if (inUse[index]) return index;
            }
            return -1;
        }

        // Shows the board at index in boards, listening to it instead of the last one. Public for tests.
        public void Show(int index) {
            if (boards == null || index < 0 || index >= boards.Length) return;
            ChessBoard board = boards[index];
            if (listened != board) {
                if (Utilities.IsValid(listened)) listened.RemoveListener(this);
                listened = board;
                if (Utilities.IsValid(board)) board.AddListener(this);
            }
            shown = index;
            Refresh();
        }

        // Logs a switch to the board at index, and why (for comparing clients' logs).
        void LogShown(int index, string why) {
            if (index < 0 || index >= boards.Length) return;
            ChessBoard board = boards[index];
            Debug.Log("[ChessBoard2DView] " + gameObject.name + ": showing "
                + (Utilities.IsValid(board) ? "Board " + (board.boardNumber + 1) : "nothing") + " (" + why + ")");
        }

        string OwnerName() {
            VRCPlayerApi owner = Networking.GetOwner(gameObject);
            return Utilities.IsValid(owner) ? owner.displayName : "nobody";
        }

        // The index in boards of the board shown, -1 for none.
        public int ShownIndex() {
            return shown;
        }

        // The board shown, null for none.
        public ChessBoard ShownBoard() {
            return shown >= 0 && shown < boards.Length ? boards[shown] : null;
        }

        // ---- Drawing ----

        // Brings the view up to the board shown now. Called on the frame after a change; public for tests.
        public void Refresh() {
            dirty = false;
            ChessBoard board = ShownBoard();
            if (Utilities.IsValid(boardLabel)) {
                boardLabel.text = Utilities.IsValid(board) ? "Board " + (board.boardNumber + 1) : "";
            }
            DrawSquares(board);
            UpdateStatus();
        }

        void DrawSquares(ChessBoard board) {
            bool valid = Utilities.IsValid(board);
            int last = valid ? board.LastMove() : 0;
            int lastFrom = last == 0 ? -1 : (last >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask;
            int lastTo = last == 0 ? -1 : (last >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask;
            int checks = valid ? board.Checks(checkers, kings) : 0;
            for (int square = 0; square < 64; square++) {
                int piece = valid ? board.PieceAt(square) : ChessRules.Empty;
                if (pieceImages != null && square < pieceImages.Length) {
                    Image image = pieceImages[square];
                    if (Utilities.IsValid(image)) {
                        Sprite sprite = PieceSprite(piece);
                        image.sprite = sprite;
                        image.enabled = sprite != null;
                    }
                }
                if (squareImages != null && square < squareImages.Length && Utilities.IsValid(squareImages[square])) {
                    bool light = (ChessRules.FileOf(square) + ChessRules.RankOf(square)) % 2 == 1;
                    Color color = light ? lightSquare : darkSquare;
                    if (square == lastFrom || square == lastTo) color = Color.Lerp(color, lastMoveTint, 0.6f);
                    for (int i = 0; i < checks; i++) {
                        if (kings[i] == square) color = checkTint;
                    }
                    squareImages[square].color = color;
                }
            }
        }

        // The sprite for a piece (ChessRules encoding), null for an empty square or a sprite left out.
        public Sprite PieceSprite(int piece) {
            if (piece == ChessRules.Empty) return null;
            Sprite[] sprites = ChessRules.PieceColorOf(piece) == ChessRules.White ? whiteSprites : blackSprites;
            int index = ChessRules.PieceTypeOf(piece) - ChessRules.Pawn;
            if (sprites == null || index < 0 || index >= sprites.Length) return null;
            return sprites[index];
        }

        // The players and status lines at the server time now, as the board's UI writes them; soon again while a clock
        // runs, once a second otherwise. Public for tests.
        public void UpdateStatus() {
            ChessBoard board = ShownBoard();
            bool valid = Utilities.IsValid(board);
            nextStatusUpdate = Time.time + (valid && board.IsClockRunning() ? ChessBoardUI.StatusInterval : 1);
            double now = valid ? board.ServerTime() : 0;
            string players = valid ? ChessBoardUI.PlayersTextFor(board, now) : "";
            string status = valid ? ChessBoardUI.StatusTextFor(board, now) : "";
            if (Utilities.IsValid(playersLabel) && playersLabel.text != players) playersLabel.text = players;
            if (Utilities.IsValid(statusLabel) && statusLabel.text != status) statusLabel.text = status;
        }
    }
}
