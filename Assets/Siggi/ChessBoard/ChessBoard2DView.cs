using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // A flat 2D view of one board at a time (docs/board-2d.md; PLAN.md 12.13, 12.14): a UI canvas with the 8x8 board,
    // which board it is ("Board N"), and its players and status lines as the board's UI writes them
    // (ChessBoardUI.PlayersTextFor, GameStatusTextFor). Left and right step through the boards in use; nothing on it
    // acts on a board. It listens to the board it shows, switching listener as it steps. Built in the scene by
    // Tools > Chess Board > Create 2D Board View (Editor/ChessBoard2DViewBuilder.cs).
    //
    // It has a replay of its own (12.14): it shows a ply of the board's game (viewPly), building that position itself
    // from the board's history on a scratch position, whatever the board's own replay shows. On the latest position it
    // follows the game; the step buttons (<<, <, >, >>) move it. With lockedBoard set it shows only that board.
    //
    // To the right, a move list (12.15): White's and Black's moves in two columns, the move into the position shown in
    // yellow, a click on a move showing the position after it. Its rows are a pool filling the list's height, drawn
    // with the rows its scroll position puts there, so a game of any length needs no more.
    //
    // With synced on, the board and the ply shown are synced (a press takes ownership), so everyone sees the same,
    // late joiners included; with it off, each player chooses for themselves and nothing is sent.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ChessBoard2DView : ChessBoardListener {
        // The step buttons' indices in stepButtons and stepLabels, left to right.
        public const int StepFirst = 0;
        public const int StepBack = 1;
        public const int StepForward = 2;
        public const int StepLast = 3;
        // syncedPly for the latest position, followed as the game goes on.
        public const int LatestPly = -1;

        // The boards to choose from, in this order (as the settings menu's). It starts on the first.
        [Tooltip("The boards this view can show, in the order Left and Right step through them. Not used while Locked "
            + "Board is set.")]
        public ChessBoard[] boards = new ChessBoard[0];
        [Tooltip("Set to show only this board (e.g. a view beside its table): the selector is hidden. Empty: the view "
            + "steps through Boards.")]
        public ChessBoard lockedBoard;
        [Tooltip("On: everyone sees the board and position last chosen by anyone, late joiners included. Off: each "
            + "player chooses for themselves.")]
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
        // The selector's buttons, hidden on a locked view.
        public Button leftButton;
        public Button rightButton;
        // The step buttons and their labels, StepFirst to StepLast: <<, <, >, >>.
        public Button[] stepButtons = new Button[4];
        public Text[] stepLabels = new Text[4];
        public Color lightSquare = new Color(0.93f, 0.85f, 0.71f);
        public Color darkSquare = new Color(0.71f, 0.53f, 0.39f);
        public Color lastMoveTint = new Color(0.85f, 0.85f, 0.3f);
        public Color checkTint = new Color(0.9f, 0.3f, 0.3f);
        public Color stepLabelColor = Color.black;
        public Color stepLabelDisabledColor = new Color(0.45f, 0.45f, 0.45f);
        // The move list (12.15): the scroll rect, its viewport (the list's visible part), its content (sized to every
        // row, moved by scrolling) and the pooled rows, moved within a row's height as it scrolls. Each pooled row has
        // its move number and two cells, White's (2 * row) and Black's (2 * row + 1), each a button with its image and
        // label.
        public ScrollRect moveList;
        public RectTransform moveListViewport;
        public RectTransform moveListContent;
        public RectTransform moveListRows;
        public float moveRowHeight = 50;
        public Text[] moveNumbers = new Text[0];
        public Button[] moveCells = new Button[0];
        public Image[] moveCellImages = new Image[0];
        public Text[] moveCellLabels = new Text[0];
        public Color moveCellColor = new Color(0, 0, 0, 0);
        public Color currentMoveColor = new Color(1f, 0.85f, 0.2f);
        public Color moveTextColor = Color.white;
        public Color currentMoveTextColor = Color.black;

        // The index in boards of the board chosen, and the ply shown (LatestPly for the latest), when synced.
        [UdonSynced] int syncedIndex;
        [UdonSynced] int syncedPly = LatestPly;
        // The index in boards of the board shown (-1 for none, and on a locked view), and the board listened to.
        int shown = -1;
        ChessBoard listened;
        // The ply shown, whether it's the latest (followed as moves are made), the move that led to it when drawn (to
        // notice the history changing under it), and whether a change seen calls for the latest (a reset, a load).
        int viewPly;
        bool onLatest = true;
        int shownMove;
        bool toLatest;
        // A synced ply this client's game doesn't reach yet (the view's update can arrive before the board's, e.g. on
        // joining), shown once it does; LatestPly for none.
        int waitingPly = LatestPly;
        // The position shown: built from the board's game for viewPly.
        byte[] position = new byte[64];
        int[] positionState = new int[ChessPosition.StateLength];
        // Drawn on the frame after a change, as the other views are; the status lines also as the clocks run.
        bool dirty;
        float nextStatusUpdate;
        int[] checkers = new int[2];
        int[] kings = new int[2];
        // The move list's first row drawn in the pool, and the board and ply it last scrolled for (scrolled again only
        // when they change, so a player's own scrolling stays).
        int listFirstRow;
        ChessBoard listBoard;
        int listPly = -1;

        void Start() {
            if (!IsLocked() && (boards == null || boards.Length == 0)) {
                Debug.LogError("[ChessBoard2DView] " + gameObject.name + ": the Boards list is empty.");
                return;
            }
            int index = synced ? syncedIndex : 0;
            LogShown(index, "on start");
            Listen(index);
            if (synced) ApplyPly(syncedPly);
            Refresh();
        }

        void Update() {
            if (dirty) Refresh();
            else if (Time.time >= nextStatusUpdate) UpdateStatus();
        }

        public override void OnBoardChanged() {
            dirty = true;
            if (Utilities.IsValid(listened)) {
                int change = listened.lastChange;
                if (change == ChessBoard.ChangeReset || change == ChessBoard.ChangeLoaded) toLatest = true;
            }
        }

        // Only the move list's text changed: the list and the replay line may name moves they didn't know before.
        public override void OnNotationFilled() {
            DrawMoveList();
            UpdateStatus();
        }

        public override void OnDeserialization() {
            if (!synced) return;
            if (!IsLocked() && syncedIndex != shown) LogShown(syncedIndex, "chosen by " + OwnerName());
            Listen(syncedIndex);
            int before = viewPly;
            ApplyPly(syncedPly);
            if (viewPly != before) LogPly("chosen by " + OwnerName());
            Refresh();
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
        // board is in use, or on a locked view. The new board starts on its latest position. Synced, the choice is
        // sent to everyone.
        public void Step(int direction) {
            if (IsLocked()) return;
            int count = boards == null ? 0 : boards.Length;
            if (count == 0) return;
            bool[] inUse = new bool[count];
            for (int i = 0; i < count; i++) {
                inUse[i] = Utilities.IsValid(boards[i]) && boards[i].lifecycle != ChessBoard.LifecycleReset;
            }
            int target = StepTarget(inUse, shown, direction);
            if (target < 0 || target == shown) return;
            if (synced) {
                TakeView();
                syncedIndex = target;
                syncedPly = LatestPly;
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

        // Shows the board at index in boards (lockedBoard on a locked view, whatever index is), listening to it
        // instead of the last one. A board not shown before starts on its latest position. Public for tests.
        public void Show(int index) {
            Listen(index);
            Refresh();
        }

        void Listen(int index) {
            ChessBoard board;
            if (IsLocked()) {
                board = lockedBoard;
                index = -1;
            } else {
                if (boards == null || index < 0 || index >= boards.Length) return;
                board = boards[index];
            }
            if (listened != board) {
                if (Utilities.IsValid(listened)) listened.RemoveListener(this);
                listened = board;
                if (Utilities.IsValid(board)) board.AddListener(this);
                onLatest = true;
                waitingPly = LatestPly;
            }
            shown = index;
            UpdateSelector();
        }

        // Whether the view shows lockedBoard alone.
        public bool IsLocked() {
            return Utilities.IsValid(lockedBoard);
        }

        // Locks the view to board (null unlocks it, going back to the board chosen before, or the first), as the
        // Inspector's Locked Board does. For tests, and a world changing it at run time; nothing is sent.
        public void SetLockedBoard(ChessBoard board) {
            lockedBoard = board;
            Show(shown >= 0 ? shown : (synced ? syncedIndex : 0));
        }

        // Logs a switch to the board at index (lockedBoard on a locked view), and why (for comparing clients' logs).
        void LogShown(int index, string why) {
            ChessBoard board;
            if (IsLocked()) board = lockedBoard;
            else if (boards != null && index >= 0 && index < boards.Length) board = boards[index];
            else return;
            Debug.Log("[ChessBoard2DView] " + gameObject.name + ": showing "
                + (Utilities.IsValid(board) ? "Board " + (board.boardNumber + 1) : "nothing") + " (" + why + ")");
        }

        string OwnerName() {
            VRCPlayerApi owner = Networking.GetOwner(gameObject);
            return Utilities.IsValid(owner) ? owner.displayName : "nobody";
        }

        // Takes ownership of the view for the local player, to send a choice.
        void TakeView() {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (Utilities.IsValid(local) && !Networking.IsOwner(local, gameObject)) Networking.SetOwner(local, gameObject);
        }

        // The index in boards of the board shown, -1 for none and on a locked view.
        public int ShownIndex() {
            return shown;
        }

        // The board shown, null for none.
        public ChessBoard ShownBoard() {
            if (IsLocked()) return lockedBoard;
            return boards != null && shown >= 0 && shown < boards.Length ? boards[shown] : null;
        }

        // The selector shown and pressable, or hidden on a locked view.
        void UpdateSelector() {
            bool selectable = !IsLocked();
            if (Utilities.IsValid(leftButton)) {
                leftButton.interactable = selectable;
                leftButton.gameObject.SetActive(selectable);
            }
            if (Utilities.IsValid(rightButton)) {
                rightButton.interactable = selectable;
                rightButton.gameObject.SetActive(selectable);
            }
        }

        // ---- Its own replay (12.14) ----

        // The step buttons: the start, one move back, one move forward, the latest position.
        public void PressFirst() {
            ShowPly(0);
        }

        public void PressBack() {
            ShowPly(viewPly - 1);
        }

        public void PressForward() {
            ShowPly(viewPly + 1);
        }

        public void PressLast() {
            ChessBoard board = ShownBoard();
            if (Utilities.IsValid(board)) ShowPly(board.moveCount);
        }

        // The ply shown: 0 the start, the board's moveCount the latest.
        public int ViewPly() {
            return viewPly;
        }

        // Whether the view is on the latest position, following the game.
        public bool IsOnLatest() {
            return onLatest;
        }

        // Whether ShowPly(ply) would show something else: a ply of the game other than the one shown.
        public bool CanShowPly(int ply) {
            ChessBoard board = ShownBoard();
            return Utilities.IsValid(board) && ply >= 0 && ply <= board.moveCount && ply != viewPly;
        }

        // Shows the position before move ply of the shown board's game (0 the start, moveCount the latest, which
        // follows the game from then on), refused (false) unless CanShowPly. Synced, the ply is sent to everyone.
        public bool ShowPly(int ply) {
            if (!CanShowPly(ply)) return false;
            ChessBoard board = ShownBoard();
            int count = board.moveCount;
            waitingPly = LatestPly;
            viewPly = ply;
            onLatest = ply == count;
            shownMove = ply > 0 ? board.moveHistory[ply - 1] : 0;
            if (synced) {
                TakeView();
                syncedIndex = shown >= 0 ? shown : 0;
                syncedPly = onLatest ? LatestPly : ply;
                RequestSerialization();
            }
            LogPly(synced ? "chosen here, sent" : "chosen here");
            Refresh();
            return true;
        }

        // Takes a synced ply: LatestPly for the latest. A ply this client's game doesn't reach yet (the board's update
        // still on its way: the sender, off its latest, has more moves than the ply) shows the latest until the game
        // has moves beyond it (waitingPly, FollowGame).
        void ApplyPly(int ply) {
            ChessBoard board = ShownBoard();
            if (!Utilities.IsValid(board)) return;
            int count = board.moveCount;
            waitingPly = LatestPly;
            if (ply < 0 || ply >= count) {
                onLatest = true;
                viewPly = count;
                if (ply >= 0) waitingPly = ply;
            } else {
                onLatest = false;
                viewPly = ply;
            }
            shownMove = viewPly > 0 ? board.moveHistory[viewPly - 1] : 0;
        }

        // Takes ply as OnDeserialization takes a synced one, then redraws. For tests.
        public void ReceivePly(int ply) {
            ApplyPly(ply);
            Refresh();
        }

        // Logs the ply shown and why, as LogShown does the board.
        void LogPly(string why) {
            ChessBoard board = ShownBoard();
            Debug.Log("[ChessBoard2DView] " + gameObject.name + ": showing ply " + viewPly + " of "
                + (Utilities.IsValid(board) ? board.moveCount : 0) + (onLatest ? ", the latest" : "") + " (" + why + ")");
        }

        // Brings viewPly up to the board's game: the latest when on it (a new move moves it on), when the history no
        // longer reaches it or has changed under it (a takeback or continuation cutting below it), and after a reset or
        // a load; otherwise it stays. Going to the latest by itself, the owner of a synced view sends that.
        void FollowGame(ChessBoard board, int count) {
            if (toLatest) waitingPly = LatestPly;
            if (waitingPly != LatestPly && count > waitingPly) {
                onLatest = false;
                viewPly = waitingPly;
                shownMove = viewPly > 0 ? board.moveHistory[viewPly - 1] : 0;
                waitingPly = LatestPly;
                LogPly("received earlier, the game here now reaches it");
            }
            if (!onLatest) {
                bool cut = viewPly > count || (viewPly > 0 && board.moveHistory[viewPly - 1] != shownMove);
                if (toLatest || cut || viewPly == count) {
                    onLatest = true;
                    if (synced && syncedPly != LatestPly && Networking.IsOwner(gameObject)) {
                        syncedPly = LatestPly;
                        RequestSerialization();
                    }
                }
            }
            toLatest = false;
            if (onLatest) viewPly = count;
            shownMove = viewPly > 0 ? board.moveHistory[viewPly - 1] : 0;
        }

        // Puts the position before move ply of board's game in position and positionState: the game's position with
        // the moves from ply on undone, as ChessBoard.StepTo builds its replay board. Rebuilt from the latest each
        // time (decided in 12.14). Public static for tests.
        public static void BuildPosition(ChessBoard board, int ply, byte[] position, int[] positionState) {
            Array.Copy(board.board, position, 64);
            Array.Copy(board.state, positionState, ChessPosition.StateLength);
            int[] history = board.moveHistory;
            for (int i = board.moveCount - 1; i >= ply; i--) {
                ChessPosition.UnmakeMove(position, positionState, history[i]);
            }
        }

        // The piece on square in the position shown (Empty for none). Public for tests.
        public int ShownPieceAt(int square) {
            if (square < 0 || square >= 64) return ChessRules.Empty;
            return position[square];
        }

        // ---- The move list (12.15) ----

        // A move list cell pressed (its ChessMoveListCell): the position after its move, as a step button would.
        public void PressMoveCell(int cell) {
            int ply = MoveCellPly(cell);
            if (ply >= 0) ShowPly(ply + 1);
        }

        // The scroll rect moved (its onValueChanged): the rows it now shows.
        public void OnMoveListScrolled() {
            DrawMoveList();
        }

        // The move (index in the history) in the pool's cell at cell as drawn, -1 for none. For tests too.
        public int MoveCellPly(int cell) {
            ChessBoard board = ShownBoard();
            if (!Utilities.IsValid(board) || moveCells == null || cell < 0 || cell >= moveCells.Length) return -1;
            int ply = listFirstRow * 2 + cell - LeadCells(board);
            return ply >= 0 && ply < board.moveCount ? ply : -1;
        }

        // The pool's cell drawing move ply (index in the history), -1 when it's scrolled out of the pool. For tests.
        public int MoveCellOf(int ply) {
            ChessBoard board = ShownBoard();
            if (!Utilities.IsValid(board) || ply < 0 || ply >= board.moveCount || moveCells == null) return -1;
            int cell = ply + LeadCells(board) - listFirstRow * 2;
            return cell >= 0 && cell < moveCells.Length ? cell : -1;
        }

        // Whether move ply's row is wholly inside the list's visible part. For tests.
        public bool IsMoveInView(int ply) {
            ChessBoard board = ShownBoard();
            if (!Utilities.IsValid(board) || !Utilities.IsValid(moveListContent) || ply < 0 || ply >= board.moveCount) {
                return false;
            }
            float top = (ply + LeadCells(board)) / 2 * moveRowHeight;
            float offset = moveListContent.anchoredPosition.y;
            return top >= offset - 0.5f && top + moveRowHeight <= offset + ViewportHeight() + 0.5f;
        }

        // How far the list is scrolled down, in canvas pixels (0 at the top), and how far it can be. For tests.
        public float MoveListScroll() {
            return Utilities.IsValid(moveListContent) ? moveListContent.anchoredPosition.y : 0;
        }

        public float MoveListMaxScroll() {
            ChessBoard board = ShownBoard();
            return Mathf.Max(0, MoveRows(board) * moveRowHeight - ViewportHeight());
        }

        // Scrolls the list to offset (clamped to the rows there are) and draws it, as a player's scrolling would.
        // For tests.
        public void ScrollMoveList(float offset) {
            SetScroll(offset);
            DrawMoveList();
        }

        // The cells before the game's first move: 1 when it started with Black to move (White's cell left empty).
        static int LeadCells(ChessBoard board) {
            // The side that made the first move: the side to move now, or the other after an odd number of moves.
            int firstSide = board.SideToMove();
            if (board.moveCount % 2 == 1) firstSide = ChessRules.OtherColor(firstSide);
            return firstSide == ChessRules.Black ? 1 : 0;
        }

        // The list's rows for board's game: none before the first move.
        static int MoveRows(ChessBoard board) {
            if (!Utilities.IsValid(board) || board.moveCount == 0) return 0;
            return (board.moveCount + LeadCells(board) + 1) / 2;
        }

        float ViewportHeight() {
            return Utilities.IsValid(moveListViewport) ? moveListViewport.rect.height : 0;
        }

        void SetScroll(float offset) {
            if (!Utilities.IsValid(moveListContent)) return;
            offset = Mathf.Clamp(offset, 0, MoveListMaxScroll());
            Vector2 position = moveListContent.anchoredPosition;
            if (position.y != offset) moveListContent.anchoredPosition = new Vector2(position.x, offset);
        }

        // On Refresh: when the board or the ply shown has changed, scrolls just enough to bring the move into the
        // position shown into view (the top at the start); then draws the list.
        void UpdateMoveList(ChessBoard board, bool valid) {
            if (!Utilities.IsValid(moveListContent)) return;
            if (board != listBoard || viewPly != listPly) {
                listBoard = board;
                listPly = viewPly;
                float offset = moveListContent.anchoredPosition.y;
                if (!valid || viewPly == 0) {
                    offset = 0;
                } else {
                    float top = (viewPly - 1 + LeadCells(board)) / 2 * moveRowHeight;
                    float height = ViewportHeight();
                    if (top < offset) offset = top;
                    else if (top + moveRowHeight > offset + height) offset = top + moveRowHeight - height;
                }
                if (Utilities.IsValid(moveList)) moveList.StopMovement();
                SetContentHeight(board);
                SetScroll(offset);
            }
            DrawMoveList();
        }

        void SetContentHeight(ChessBoard board) {
            Vector2 size = moveListContent.sizeDelta;
            float height = MoveRows(board) * moveRowHeight;
            if (size.y != height) moveListContent.sizeDelta = new Vector2(size.x, height);
        }

        // Draws the pooled rows with the rows the scroll position puts in view: each move number, each cell's SAN
        // (empty until the board fills it in), the move into the position shown in yellow, and the cells without a
        // move not clickable.
        void DrawMoveList() {
            if (!Utilities.IsValid(moveListContent)) return;
            ChessBoard board = ShownBoard();
            bool valid = Utilities.IsValid(board);
            SetContentHeight(board);
            int count = valid ? board.moveCount : 0;
            int lead = valid ? LeadCells(board) : 0;
            int rows = MoveRows(board);
            float offset = Mathf.Clamp(moveListContent.anchoredPosition.y, 0, MoveListMaxScroll());
            int first = moveRowHeight > 0 ? Mathf.FloorToInt(offset / moveRowHeight) : 0;
            listFirstRow = first;
            if (Utilities.IsValid(moveListRows)) {
                Vector2 shift = new Vector2(moveListRows.anchoredPosition.x, offset - first * moveRowHeight);
                if (moveListRows.anchoredPosition != shift) moveListRows.anchoredPosition = shift;
            }
            int current = viewPly - 1;
            int poolRows = moveNumbers == null ? 0 : moveNumbers.Length;
            for (int r = 0; r < poolRows; r++) {
                int row = first + r;
                SetText(moveNumbers[r], valid && row < rows ?(board.startMoveNumber + row) + "." : "");
                for (int c = 0; c < 2; c++) {
                    int cell = 2 * r + c;
                    int ply = 2 * row + c - lead;
                    bool has = ply >= 0 && ply < count;
                    string san = has ? board.notation[ply] : "";
                    bool isCurrent = has && ply == current;
                    if (moveCellLabels != null && cell < moveCellLabels.Length) {
                        Text label = moveCellLabels[cell];
                        SetText(label, san == null ? "" : san);
                        if (Utilities.IsValid(label)) {
                            Color textColor = isCurrent ? currentMoveTextColor : moveTextColor;
                            if (label.color != textColor) label.color = textColor;
                        }
                    }
                    if (moveCellImages != null && cell < moveCellImages.Length
                        && Utilities.IsValid(moveCellImages[cell])) {
                        Color color = isCurrent ? currentMoveColor : moveCellColor;
                        if (moveCellImages[cell].color != color) moveCellImages[cell].color = color;
                    }
                    if (moveCells != null && cell < moveCells.Length && Utilities.IsValid(moveCells[cell])
                        && moveCells[cell].interactable != has) {
                        moveCells[cell].interactable = has;
                    }
                }
            }
        }

        static void SetText(Text text, string value) {
            if (Utilities.IsValid(text) && text.text != value) text.text = value;
        }

        // Whether the pool's cell at cell is drawn as the current move (yellow). For tests.
        public bool IsMoveCellCurrent(int cell) {
            return moveCellImages != null && cell >= 0 && cell < moveCellImages.Length
                && Utilities.IsValid(moveCellImages[cell]) && moveCellImages[cell].color == currentMoveColor;
        }

        // ---- Drawing ----

        // Brings the view up to the board shown now. Called on the frame after a change; public for tests.
        public void Refresh() {
            dirty = false;
            ChessBoard board = ShownBoard();
            bool valid = Utilities.IsValid(board);
            int count = 0;
            if (valid) {
                count = board.moveCount;
                FollowGame(board, count);
                BuildPosition(board, viewPly, position, positionState);
            } else {
                viewPly = 0;
                onLatest = true;
                for (int square = 0; square < 64; square++) position[square] = ChessRules.Empty;
            }
            if (Utilities.IsValid(boardLabel)) boardLabel.text = valid ? "Board " + (board.boardNumber + 1) : "";
            DrawSquares(valid && viewPly > 0 ? board.moveHistory[viewPly - 1] : 0);
            UpdateStepButtons(valid, count);
            UpdateMoveList(board, valid);
            UpdateStatus();
        }

        void DrawSquares(int last) {
            int lastFrom = last == 0 ? -1 : (last >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask;
            int lastTo = last == 0 ? -1 : (last >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask;
            int checks = PositionChecks();
            for (int square = 0; square < 64; square++) {
                int piece = position[square];
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

        // The checks in the position shown, as ChessBoard.Checks: each king attacked, in kings, with its attackers in
        // checkers, while they have room. Returns how many there are.
        int PositionChecks() {
            int limit = Mathf.Min(checkers.Length, kings.Length);
            int count = 0;
            for (int square = 0; square < 64 && count < limit; square++) {
                int piece = position[square];
                if (piece == ChessRules.Empty || (piece & 7) != ChessRules.King) continue;
                int start = count;
                count = Mathf.Min(ChessMoves.SquareAttackers(position, square, ((piece >> 3) & 1) ^ 1, checkers, count),
                    limit);
                for (int i = start; i < count; i++) kings[i] = square;
            }
            return count;
        }

        // Each step button pressable only where it steps somewhere: << and < off the start, > and >> off the latest.
        void UpdateStepButtons(bool valid, int count) {
            SetStepButton(StepFirst, valid && viewPly > 0);
            SetStepButton(StepBack, valid && viewPly > 0);
            SetStepButton(StepForward, valid && viewPly < count);
            SetStepButton(StepLast, valid && viewPly < count);
        }

        void SetStepButton(int index, bool enabled) {
            if (stepButtons != null && index < stepButtons.Length && Utilities.IsValid(stepButtons[index])) {
                stepButtons[index].interactable = enabled;
            }
            if (stepLabels != null && index < stepLabels.Length && Utilities.IsValid(stepLabels[index])) {
                stepLabels[index].color = enabled ? stepLabelColor : stepLabelDisabledColor;
            }
        }

        // Whether the step button at index (StepFirst to StepLast) is pressable. For tests.
        public bool IsStepEnabled(int index) {
            return stepButtons != null && index >= 0 && index < stepButtons.Length
                && Utilities.IsValid(stepButtons[index]) && stepButtons[index].interactable;
        }

        // The sprite for a piece (ChessRules encoding), null for an empty square or a sprite left out.
        public Sprite PieceSprite(int piece) {
            if (piece == ChessRules.Empty) return null;
            Sprite[] sprites = ChessRules.PieceColorOf(piece) == ChessRules.White ? whiteSprites : blackSprites;
            int index = ChessRules.PieceTypeOf(piece) - ChessRules.Pawn;
            if (sprites == null || index < 0 || index >= sprites.Length) return null;
            return sprites[index];
        }

        // The players and status lines at the server time now; soon again while a clock runs, once a second otherwise.
        // Public for tests.
        public void UpdateStatus() {
            ChessBoard board = ShownBoard();
            bool valid = Utilities.IsValid(board);
            nextStatusUpdate = Time.time + (valid && board.IsClockRunning() ? ChessBoardUI.StatusInterval : 1);
            double now = valid ? board.ServerTime() : 0;
            string players = valid ? ChessBoardUI.PlayersTextFor(board, now) : "";
            string status = valid ? StatusTextFor(board, now, viewPly) : "";
            if (Utilities.IsValid(playersLabel) && playersLabel.text != players) playersLabel.text = players;
            if (Utilities.IsValid(statusLabel) && statusLabel.text != status) statusLabel.text = status;
        }

        // The view's status lines for board at server time now, showing ply: the board's own (ChessBoardUI's
        // GameStatusTextFor, without the lines about the board's replay position, since the view's replay is its own),
        // a pending takeback request ("White asks for a takeback": "to here" would be the board's replay position), then
        // the position shown when it isn't the latest ("Replay: 2. Nf3 (3 of 4)", ChessBoardUI.PlyTextFor).
        public static string StatusTextFor(ChessBoard board, double now, int ply) {
            if (!Utilities.IsValid(board)) return "";
            string text = ChessBoardUI.GameStatusTextFor(board, now);
            if (board.takebackColor != ChessRules.NoColor) {
                text = AddLine(text, ChessBoardUI.ColorText(board.takebackColor) + " asks for a takeback");
            }
            if (ply >= 0 && ply < board.moveCount) text = AddLine(text, ChessBoardUI.PlyTextFor(board, ply));
            return text;
        }

        static string AddLine(string text, string line) {
            return text.Length == 0 ? line : text + "\n" + line;
        }
    }
}
