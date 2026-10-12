using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The logical board (docs/architecture.md): one board's game state, its lifecycle (docs/board-lifecycle.md) and
    // the rules applied to it. It never touches the world; ChessBoardView (step 9) owns the world objects and forwards
    // input here. Its state fields are the synced fields of docs/networking.md: each change made here takes ownership
    // and sends them, packed into the synced* fields (PackState), and a client receiving them unpacks them
    // (ReceiveState) and tells its listeners.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ChessBoard : UdonSharpBehaviour {
        // Lifecycle states (docs/board-lifecycle.md).
        public const int LifecycleReset = 0;
        public const int LifecycleLoaded = 1;
        public const int LifecycleLobby = 2;
        public const int LifecycleInGame = 3;
        public const int LifecycleEnded = 4;

        // What an input method returns (docs/architecture.md, Physical → logical).
        public const int InputAccepted = 0;
        public const int InputRejected = 1;
        // A drop matches a promotion: nothing is played until the view asks which piece and calls PromotionChosen.
        public const int InputPromotionNeeded = 2;

        // The sides a king can castle to through the castle drop space (docs/player-input.md): king side is the rook
        // right of the king, queen side the one left of it, as in Chess960.
        public const int CastleKingSide = 0;
        public const int CastleQueenSide = 1;

        // What the last change to the board was (lastChange), for listeners (docs/architecture.md): a view animates a
        // move and puts everything else straight where the board says.
        // Nothing yet since the board started.
        public const int ChangeNone = 0;
        // Nothing changed: a piece input (pickup, drop, promotion choice) was rejected, so the view puts it back.
        public const int ChangeRejected = 1;
        public const int ChangeReset = 2;
        public const int ChangeJoined = 3;
        // The mode or clock chosen in the lobby or on a loaded board, or a re-snapshot.
        public const int ChangeSettings = 4;
        public const int ChangeStarted = 5;
        // A move played, also one that ends the game (checkmate, stalemate, an automatic draw).
        public const int ChangeMove = 6;
        // The game ended by anything but a move: resignation, a draw agreed or claimed, flag fall.
        public const int ChangeEnded = 7;
        // A draw offer or takeback request made or declined.
        public const int ChangeRequest = 8;
        public const int ChangeEnteredReplay = 9;
        public const int ChangeLeftReplay = 10;
        public const int ChangeReplayStep = 11;
        // A free move on the replay board.
        public const int ChangeFreeMove = 12;
        // A takeback accepted: the game continues from an earlier ply.
        public const int ChangeTakeback = 13;
        public const int ChangeLoaded = 14;
        // A loaded game continued from here.
        public const int ChangeContinued = 15;

        // Room for this many plies in the history arrays at first; they grow as needed.
        public const int InitialHistoryCapacity = 128;

        // A loaded game's time control when its PGN has none (or one that can't be read): exported as "?".
        public const int UnknownClock = -2;

        // The arbiter whose settings this board snapshots, and this board's number for its per-board overrides
        // (docs/settings.md).
        [Tooltip("The ChessArbiter holding the rules settings this board uses.")]
        public ChessArbiter arbiter;
        [Tooltip("This board's number for per-board setting overrides: 0 to the arbiter's Board Count - 1. A number "
            + "outside that range always gets the instance defaults.")]
        public int boardNumber;
        // The PGN Event and Site tags of this board's games (docs/pgn.md, Export tags); empty is written "?".
        [Tooltip("The PGN Event tag of games played on this board. Empty is written \"?\".")]
        public string pgnEvent = "";
        [Tooltip("The PGN Site tag of games played on this board. Empty is written \"?\".")]
        public string pgnSite = "VRChat";
        // The behaviours told of every change (OnBoardChanged): set in the Inspector, or added with AddListener.
        [Tooltip("Behaviours told of every change to this board: its view, and anything else showing the game.")]
        public ChessBoardListener[] listeners = new ChessBoardListener[0];

        // ---- Board state (docs/networking.md, Synced fields) ----

        // The game's current position: pieces as in ChessRules and the state array of ChessPosition.
        [NonSerialized] public byte[] board = new byte[64];
        [NonSerialized] public int[] state = new int[ChessPosition.StateLength];
        // The FEN fullmove number of the current position.
        [NonSerialized] public int moveNumber;
        // The game's starting position: its FEN (for the PGN FEN tag, never parsed by receivers), halfmove clock and
        // move number.
        [NonSerialized] public string startFen = "";
        [NonSerialized] public int startHalfmoveClock;
        [NonSerialized] public int startMoveNumber;

        // The moves played (packed, docs/move-encoding.md), each one's SAN ("" if not known yet) and the mover's
        // remaining time in milliseconds after it (-1 if not known). The arrays have room for more; the first
        // moveCount entries are the game.
        [NonSerialized] public int[] moveHistory = new int[InitialHistoryCapacity];
        [NonSerialized] public string[] notation = new string[InitialHistoryCapacity];
        [NonSerialized] public int[] clockHistory = new int[InitialHistoryCapacity];
        [NonSerialized] public int moveCount;
        // Not synced: computed from the history (docs/move-encoding.md#undo). Entry i is the halfmove clock before
        // ply i; halfmoveClock is the current one.
        [NonSerialized] public int[] halfmoveClocks = new int[InitialHistoryCapacity];
        [NonSerialized] public int halfmoveClock;

        // Replay (docs/replay.md): whether the board is showing it, the ply it shows or started from, the position
        // shown, and whether that position has left the game history.
        [NonSerialized] public bool replayMode;
        [NonSerialized] public int replayIndex;
        [NonSerialized] public byte[] replayBoard = new byte[64];
        [NonSerialized] public bool replayCustom;

        // Pending requests: a takeback (the requesting colour and the ply to continue from) and a draw offer (the
        // offering colour), ChessRules.NoColor for none.
        [NonSerialized] public int takebackColor = ChessRules.NoColor;
        [NonSerialized] public int takebackPly;
        [NonSerialized] public int drawOfferColor = ChessRules.NoColor;

        // docs/game-ids.md; "" for none.
        [NonSerialized] public string gameId = "";
        [NonSerialized] public string continuedFrom = "";
        // A loaded game's White, Black and Result tags, shown in replay; "" for none.
        [NonSerialized] public string replaySourceWhite = "";
        [NonSerialized] public string replaySourceBlack = "";
        [NonSerialized] public string replaySourceResult = "";
        // A loaded game's own time control (seconds, base NoClock for none, UnknownClock when the PGN didn't say),
        // for its export; the clock for continuing it is baseTime and increment.
        [NonSerialized] public int replaySourceBaseTime = ChessArbiter.NoClock;
        [NonSerialized] public int replaySourceIncrement;

        // The seated players' display names, "" for an empty seat (docs/board-lifecycle.md, Seats).
        [NonSerialized] public string whitePlayer = "";
        [NonSerialized] public string blackPlayer = "";

        // Server time of the last piece touch (docs/inactivity.md, step 8.5).
        [NonSerialized] public double lastActivity;

        // How the game ended (a ChessGame.Ending* value, EndingNone while in progress) and who won (ChessRules.White
        // or Black, NoColor for a draw or no result).
        [NonSerialized] public int ending = ChessGame.EndingNone;
        [NonSerialized] public int winner = ChessRules.NoColor;

        [NonSerialized] public int lifecycle = LifecycleReset;
        // A ChessGame.Mode* value.
        [NonSerialized] public int mode = ChessGame.ModeStandard;

        // Time control in seconds, base ChessArbiter.NoClock for no clock (docs/clocks.md).
        [NonSerialized] public int baseTime = ChessArbiter.NoClock;
        [NonSerialized] public int increment;
        // Each side's remaining time in milliseconds, and the server time the current turn began.
        [NonSerialized] public int whiteTime;
        [NonSerialized] public int blackTime;
        [NonSerialized] public double turnStart;

        // The settings snapshot (docs/settings.md): ChessArbiter setting bits and its numeric settings.
        [NonSerialized] public int gameSettings;
        [NonSerialized] public int[] gameNumbers = new int[ChessArbiter.NumberCount];

        // What the last change was, a Change* value, for listeners, and whether it arrived from another client
        // (ReceiveState) rather than being made here: a view animates a received move, but a local drop has already
        // put the piece there. ChangeRejected is local: a rejection changes nothing to send, so the change sent is
        // lastSentChange, the last one that wasn't a rejection.
        [NonSerialized] public int lastChange = ChangeNone;
        [NonSerialized] public bool lastChangeReceived;
        [NonSerialized] public int lastSentChange = ChangeNone;

        // ---- Synced (docs/networking.md, Synced fields) ----
        // The state fields above, packed just before each serialization (PackState, from OnPreSerialization) and
        // unpacked when one arrives (ReceiveState, from OnDeserialization). Keeping them apart from the state lets a
        // receiver compare the old state with the new one (a game is logged on its transition), sends only the moves
        // of the game rather than the history arrays' spare room, and leaves the state untouched by an update that
        // isn't valid. The ints are laid out by the Sync* indices below.

        public const int SyncMoveNumber = 0;
        public const int SyncStartHalfmoveClock = 1;
        public const int SyncStartMoveNumber = 2;
        public const int SyncMoveCount = 3;
        public const int SyncReplayMode = 4;
        public const int SyncReplayIndex = 5;
        public const int SyncReplayCustom = 6;
        public const int SyncTakebackColor = 7;
        public const int SyncTakebackPly = 8;
        public const int SyncDrawOfferColor = 9;
        public const int SyncReplaySourceBaseTime = 10;
        public const int SyncReplaySourceIncrement = 11;
        public const int SyncEnding = 12;
        public const int SyncWinner = 13;
        public const int SyncLifecycle = 14;
        public const int SyncMode = 15;
        public const int SyncBaseTime = 16;
        public const int SyncIncrement = 17;
        public const int SyncWhiteTime = 18;
        public const int SyncBlackTime = 19;
        public const int SyncGameSettings = 20;
        public const int SyncChange = 21;
        // gameNumbers, then the state array.
        public const int SyncGameNumbers = 22;
        public const int SyncState = SyncGameNumbers + ChessArbiter.NumberCount;
        public const int SyncLength = SyncState + ChessPosition.StateLength;

        [UdonSynced] [NonSerialized] public byte[] syncedBoard = new byte[64];
        [UdonSynced] [NonSerialized] public byte[] syncedReplayBoard = new byte[64];
        [UdonSynced] [NonSerialized] public int[] syncedInts = new int[SyncLength];
        // The first moveCount entries of the history arrays. A synced string[] must never hold null: one null element
        // makes the whole serialization fail, so PackState writes "" for one.
        [UdonSynced] [NonSerialized] public int[] syncedMoves = new int[0];
        [UdonSynced] [NonSerialized] public int[] syncedClocks = new int[0];
        [UdonSynced] [NonSerialized] public string[] syncedNotation = new string[0];
        [UdonSynced] [NonSerialized] public double syncedTurnStart;
        [UdonSynced] [NonSerialized] public double syncedLastActivity;
        [UdonSynced] [NonSerialized] public string syncedStartFen = "";
        [UdonSynced] [NonSerialized] public string syncedGameId = "";
        [UdonSynced] [NonSerialized] public string syncedContinuedFrom = "";
        [UdonSynced] [NonSerialized] public string syncedReplaySourceWhite = "";
        [UdonSynced] [NonSerialized] public string syncedReplaySourceBlack = "";
        [UdonSynced] [NonSerialized] public string syncedReplaySourceResult = "";
        [UdonSynced] [NonSerialized] public string syncedWhitePlayer = "";
        [UdonSynced] [NonSerialized] public string syncedBlackPlayer = "";

        // ---- Local, not synced ----

        // How many games this client has logged, and the last one's PGN (docs/pgn.md, Logging), for tests.
        [NonSerialized] public int loggedGames;
        [NonSerialized] public string lastLoggedPgn = "";

        // Whether a serialization has arrived; Start mustn't clear it then.
        bool received;

        void Start() {
            if (!Utilities.IsValid(arbiter)) {
                Debug.LogError("[ChessBoard] " + gameObject.name + ": the Arbiter field is empty. Set it to "
                    + "the world's ChessArbiter. Until then the board plays with every setting off: no takebacks, no "
                    + "draws, nothing forced, never reset for inactivity.");
            }
            if (received) return;
            ClearState();
            // A late joiner gets the owner's synced fields even if the owner has never sent a change, so they must
            // hold the reset state from the start, not the empty arrays they're declared with.
            PackState();
            TellListeners(ChangeReset, false);
        }

        // ---- Listeners (docs/architecture.md, Logical → physical) ----
        // Every public method that changes the board tells the listeners once, after the change, with lastChange
        // saying what it was; a rejected piece input tells them too (ChangeRejected), so the view puts the piece back.
        // Methods that only answer a question, and refusals of anything else, tell nobody. ClearState and TakeSnapshot
        // are the exceptions: they're the steps of other changes, public for tests.

        // Adds a listener at run time (one set in the Inspector needn't be); adding one twice changes nothing.
        public void AddListener(ChessBoardListener listener) {
            if (!Utilities.IsValid(listener)) return;
            int count = listeners == null ? 0 : listeners.Length;
            for (int i = 0; i < count; i++) {
                if (listeners[i] == listener) return;
            }
            ChessBoardListener[] grown = new ChessBoardListener[count + 1];
            for (int i = 0; i < count; i++) grown[i] = listeners[i];
            grown[count] = listener;
            listeners = grown;
        }

        // Stops telling a listener of changes.
        public void RemoveListener(ChessBoardListener listener) {
            if (listeners == null) return;
            int count = 0;
            for (int i = 0; i < listeners.Length; i++) {
                if (listeners[i] != listener) count++;
            }
            if (count == listeners.Length) return;
            ChessBoardListener[] kept = new ChessBoardListener[count];
            int k = 0;
            for (int i = 0; i < listeners.Length; i++) {
                if (listeners[i] != listener) kept[k++] = listeners[i];
            }
            listeners = kept;
        }

        // A change made on this client: sends it (unless it's a rejection, which changes nothing) and tells every
        // listener.
        void NotifyChanged(int change) {
            if (change != ChangeRejected) {
                lastSentChange = change;
                SendState();
            }
            TellListeners(change, false);
        }

        // Records the change and tells every listener (empty Inspector entries are skipped).
        void TellListeners(int change, bool fromOtherClient) {
            lastChange = change;
            lastChangeReceived = fromOtherClient;
            if (listeners == null) return;
            for (int i = 0; i < listeners.Length; i++) {
                if (Utilities.IsValid(listeners[i])) listeners[i].OnBoardChanged();
            }
        }

        // Passes a piece input's result on, telling the listeners when it's a rejection.
        int NotifyIfRejected(int result) {
            if (result == InputRejected) NotifyChanged(ChangeRejected);
            return result;
        }

        // ---- Networking (docs/networking.md, docs/ownership.md) ----
        // Manual sync: the client making a change takes ownership of the board and asks for a serialization; the
        // state is packed when it's sent, so several changes in one frame go out as one update. Receivers unpack it
        // and tell their listeners, with no move generation. Who may take ownership is step 8.4.

        // Takes ownership of the board for the local player and asks VRChat to send the state.
        void SendState() {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(local, gameObject);
            RequestSerialization();
        }

        public override void OnPreSerialization() {
            PackState();
        }

        public override void OnDeserialization() {
            ReceiveState();
        }

        // Copies the state into the synced fields. Public for tests.
        public void PackState() {
            Array.Copy(board, syncedBoard, 64);
            Array.Copy(replayBoard, syncedReplayBoard, 64);
            int[] ints = new int[SyncLength];
            ints[SyncMoveNumber] = moveNumber;
            ints[SyncStartHalfmoveClock] = startHalfmoveClock;
            ints[SyncStartMoveNumber] = startMoveNumber;
            ints[SyncMoveCount] = moveCount;
            ints[SyncReplayMode] = replayMode ? 1 : 0;
            ints[SyncReplayIndex] = replayIndex;
            ints[SyncReplayCustom] = replayCustom ? 1 : 0;
            ints[SyncTakebackColor] = takebackColor;
            ints[SyncTakebackPly] = takebackPly;
            ints[SyncDrawOfferColor] = drawOfferColor;
            ints[SyncReplaySourceBaseTime] = replaySourceBaseTime;
            ints[SyncReplaySourceIncrement] = replaySourceIncrement;
            ints[SyncEnding] = ending;
            ints[SyncWinner] = winner;
            ints[SyncLifecycle] = lifecycle;
            ints[SyncMode] = mode;
            ints[SyncBaseTime] = baseTime;
            ints[SyncIncrement] = increment;
            ints[SyncWhiteTime] = whiteTime;
            ints[SyncBlackTime] = blackTime;
            ints[SyncGameSettings] = gameSettings;
            ints[SyncChange] = lastSentChange;
            Array.Copy(gameNumbers, 0, ints, SyncGameNumbers, ChessArbiter.NumberCount);
            Array.Copy(state, 0, ints, SyncState, ChessPosition.StateLength);
            syncedInts = ints;

            int[] moves = new int[moveCount];
            int[] clocks = new int[moveCount];
            string[] sans = new string[moveCount];
            Array.Copy(moveHistory, moves, moveCount);
            Array.Copy(clockHistory, clocks, moveCount);
            for (int i = 0; i < moveCount; i++) sans[i] = NotNull(notation[i]);
            syncedMoves = moves;
            syncedClocks = clocks;
            syncedNotation = sans;

            syncedTurnStart = turnStart;
            syncedLastActivity = lastActivity;
            syncedStartFen = NotNull(startFen);
            syncedGameId = NotNull(gameId);
            syncedContinuedFrom = NotNull(continuedFrom);
            syncedReplaySourceWhite = NotNull(replaySourceWhite);
            syncedReplaySourceBlack = NotNull(replaySourceBlack);
            syncedReplaySourceResult = NotNull(replaySourceResult);
            syncedWhitePlayer = NotNull(whitePlayer);
            syncedBlackPlayer = NotNull(blackPlayer);
        }

        static string NotNull(string text) {
            return text == null ? "" : text;
        }

        // Takes the state from the synced fields, as a client does when an update arrives, and tells the listeners of
        // the change it carries (lastChangeReceived). An update that isn't valid is ignored. A game this client saw in
        // progress is logged on its transition, once (docs/game-endings.md): when the update ends it, or when it's
        // gone (a reset, or a takeback continuing it as a new game) without this client having seen it end. A late
        // joiner's first update logs nothing. Public for tests.
        public void ReceiveState() {
            if (!IsSyncedStateValid()) return;
            received = true;
            int newLifecycle = syncedInts[SyncLifecycle];
            bool wasInGame = lifecycle == LifecycleInGame;
            bool sameGame = syncedGameId == gameId
                && (newLifecycle == LifecycleInGame || newLifecycle == LifecycleEnded);
            if (wasInGame && !sameGame) LogGoneGameAt(ServerTime());
            UnpackState();
            if (wasInGame && sameGame && lifecycle == LifecycleEnded) LogGame(ending, winner);
            lastSentChange = syncedInts[SyncChange];
            TellListeners(lastSentChange, true);
        }

        // Whether the synced fields hold a state PackState could have written: the arrays there and of the right
        // lengths, and the strings there. Anything else would leave the board half updated.
        bool IsSyncedStateValid() {
            if (syncedBoard == null || syncedBoard.Length != 64) return false;
            if (syncedReplayBoard == null || syncedReplayBoard.Length != 64) return false;
            if (syncedInts == null || syncedInts.Length != SyncLength) return false;
            int count = syncedInts[SyncMoveCount];
            if (count < 0) return false;
            if (syncedMoves == null || syncedMoves.Length != count) return false;
            if (syncedClocks == null || syncedClocks.Length != count) return false;
            if (syncedNotation == null || syncedNotation.Length != count) return false;
            return syncedStartFen != null && syncedGameId != null && syncedContinuedFrom != null
                && syncedReplaySourceWhite != null && syncedReplaySourceBlack != null
                && syncedReplaySourceResult != null && syncedWhitePlayer != null && syncedBlackPlayer != null;
        }

        // Logs the game in progress that an update has replaced, as the client replacing it did: on time if the side
        // to move's flag has fallen (as a reset does), otherwise as unfinished. A game with no moves isn't logged.
        void LogGoneGameAt(double now) {
            int flagged = FlaggedColorAt(now);
            if (flagged == ChessRules.NoColor) LogGame(ChessGame.EndingUnterminated, ChessRules.NoColor);
            else LogGame(ChessGame.EndingTimeForfeit, FlagFallWinner(flagged));
        }

        // Copies the synced fields into the state, and works out what isn't sent: the halfmove clocks, from the moves.
        // The history arrays keep their room (growing if needed), with the entries past the game emptied.
        void UnpackState() {
            int[] ints = syncedInts;
            Array.Copy(syncedBoard, board, 64);
            Array.Copy(syncedReplayBoard, replayBoard, 64);
            Array.Copy(ints, SyncState, state, 0, ChessPosition.StateLength);
            int[] numbers = new int[ChessArbiter.NumberCount];
            Array.Copy(ints, SyncGameNumbers, numbers, 0, ChessArbiter.NumberCount);
            gameNumbers = numbers;

            moveNumber = ints[SyncMoveNumber];
            startHalfmoveClock = ints[SyncStartHalfmoveClock];
            startMoveNumber = ints[SyncStartMoveNumber];
            int count = ints[SyncMoveCount];
            while (moveHistory.Length < count) GrowHistory();
            halfmoveClock = startHalfmoveClock;
            for (int i = 0; i < moveHistory.Length; i++) {
                if (i < count) {
                    int move = syncedMoves[i];
                    moveHistory[i] = move;
                    notation[i] = NotNull(syncedNotation[i]);
                    clockHistory[i] = syncedClocks[i];
                    halfmoveClocks[i] = halfmoveClock;
                    halfmoveClock = ChessDraws.HalfmoveClockAfter(halfmoveClock, move);
                } else {
                    moveHistory[i] = 0;
                    notation[i] = "";
                    clockHistory[i] = -1;
                    halfmoveClocks[i] = 0;
                }
            }
            moveCount = count;

            replayMode = ints[SyncReplayMode] != 0;
            replayIndex = ints[SyncReplayIndex];
            replayCustom = ints[SyncReplayCustom] != 0;
            takebackColor = ints[SyncTakebackColor];
            takebackPly = ints[SyncTakebackPly];
            drawOfferColor = ints[SyncDrawOfferColor];
            replaySourceBaseTime = ints[SyncReplaySourceBaseTime];
            replaySourceIncrement = ints[SyncReplaySourceIncrement];
            ending = ints[SyncEnding];
            winner = ints[SyncWinner];
            lifecycle = ints[SyncLifecycle];
            mode = ints[SyncMode];
            baseTime = ints[SyncBaseTime];
            increment = ints[SyncIncrement];
            whiteTime = ints[SyncWhiteTime];
            blackTime = ints[SyncBlackTime];
            gameSettings = ints[SyncGameSettings];

            turnStart = syncedTurnStart;
            lastActivity = syncedLastActivity;
            startFen = syncedStartFen;
            gameId = syncedGameId;
            continuedFrom = syncedContinuedFrom;
            replaySourceWhite = syncedReplaySourceWhite;
            replaySourceBlack = syncedReplaySourceBlack;
            replaySourceResult = syncedReplaySourceResult;
            whitePlayer = syncedWhitePlayer;
            blackPlayer = syncedBlackPlayer;
        }

        // ---- Getters for the view (docs/architecture.md) ----
        // Most of the state is read straight from the public fields: the lifecycle, replayMode and replayIndex, the
        // seats, ending and winner, the pending requests, the clocks (RemainingTime), and the Can… checks for the
        // buttons.

        // The piece shown on square (a ChessRules piece, Empty for none): the replay board's in replay, the game's
        // otherwise.
        public int PieceAt(int square) {
            if (square < 0 || square >= 64) return ChessRules.Empty;
            return replayMode ? replayBoard[square] : board[square];
        }

        // The move that led to the position shown (packed, for the last-move highlight and a move's animation), 0 for
        // none: the starting position, or a custom replay position.
        public int LastMove() {
            int ply = replayMode ? replayIndex : moveCount;
            if (replayMode && replayCustom) return 0;
            return ply > 0 && ply <= moveCount ? moveHistory[ply - 1] : 0;
        }

        // The colour to move in the game (ChessRules.White or Black), whatever replay shows.
        public int SideToMove() {
            return state[ChessPosition.StateSideToMove];
        }

        // Whether the side to move is in check in the game.
        public bool IsInCheck() {
            return ChessMoves.IsInCheck(board, state, state[ChessPosition.StateSideToMove]);
        }

        // The legal moves from square in the game (for the legal-target highlights) into moves, which
        // ChessMoves.MaxMoves entries always hold, and how many there are: none unless a game is in progress and not
        // in replay, with the side to move's flag not fallen. Promotions are four moves to one square.
        public int LegalMovesFrom(int square, int[] moves) {
            if (lifecycle != LifecycleInGame || replayMode || FlaggedColor() != ChessRules.NoColor) return 0;
            if (square < 0 || square >= 64 || moves == null) return 0;
            int legalCount = ChessMoves.GenerateLegalMoves(board, state, legalMoves, true);
            int count = 0;
            for (int i = 0; i < legalCount && count < moves.Length; i++) {
                if (((legalMoves[i] >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask) == square) {
                    moves[count++] = legalMoves[i];
                }
            }
            return count;
        }

        // The game's result as PGN writes it: "1-0", "0-1", "1/2-1/2", or "*" while it's undecided.
        public string ResultText() {
            return ChessPgn.ResultText(ending, winner);
        }

        // Puts every state field back to the reset state: the standard starting position, no game, empty seats.
        // The reset button (who may, logging an unfinished game) is ResetBoardAs.
        public void ClearState() {
            int clock;
            int number;
            ChessFen.ParseFen(ChessFen.StartFen, board, state, out clock, out number);
            SetStartPosition(clock, number);

            moveHistory = new int[InitialHistoryCapacity];
            notation = new string[InitialHistoryCapacity];
            clockHistory = new int[InitialHistoryCapacity];
            halfmoveClocks = new int[InitialHistoryCapacity];
            for (int i = 0; i < InitialHistoryCapacity; i++) {
                notation[i] = "";
                clockHistory[i] = -1;
            }
            moveCount = 0;

            replayMode = false;
            replayIndex = 0;
            replayCustom = false;

            takebackColor = ChessRules.NoColor;
            takebackPly = 0;
            drawOfferColor = ChessRules.NoColor;

            gameId = "";
            continuedFrom = "";
            replaySourceWhite = "";
            replaySourceBlack = "";
            replaySourceResult = "";
            replaySourceBaseTime = ChessArbiter.NoClock;
            replaySourceIncrement = 0;
            whitePlayer = "";
            blackPlayer = "";
            lastActivity = 0;

            ending = ChessGame.EndingNone;
            winner = ChessRules.NoColor;
            lifecycle = LifecycleReset;
            mode = ChessGame.ModeStandard;

            baseTime = ChessArbiter.NoClock;
            increment = 0;
            whiteTime = 0;
            blackTime = 0;
            turnStart = 0;

            gameSettings = 0;
            gameNumbers = new int[ChessArbiter.NumberCount];
        }

        // Makes the position in board and state the game's starting position, with these FEN counters, and shows it
        // on the replay board.
        void SetStartPosition(int clock, int number) {
            startFen = ChessFen.WriteFen(board, state, clock, number);
            startHalfmoveClock = clock;
            startMoveNumber = number;
            halfmoveClock = clock;
            moveNumber = number;
            Array.Copy(board, replayBoard, 64);
        }

        // The local player's display name, "" if there's none.
        string LocalName() {
            VRCPlayerApi player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player)) return "";
            return player.displayName;
        }

        // ---- Seats (docs/board-lifecycle.md) ----

        // The Join White and Join Black buttons, for the local player.
        public int JoinWhite() {
            return Join(ChessRules.White);
        }

        public int JoinBlack() {
            return Join(ChessRules.Black);
        }

        public int Join(int color) {
            return JoinAs(color, LocalName());
        }

        // Seats the player with this display name as color. Allowed on a reset board, which then enters the lobby, in
        // the lobby, and on a loaded board, which stays loaded; not once a game has started. A seat taken by someone
        // else can't be joined (there's no leaving a seat but a reset); names are compared exactly. Joining a seat
        // you already have changes nothing. Public for tests and the debug view; the buttons use JoinWhite/JoinBlack.
        public int JoinAs(int color, string name) {
            if (!CanJoinAs(color, name)) return IsSeated(color, name) && SeatsOpen() ? InputAccepted : InputRejected;

            if (color == ChessRules.White) whitePlayer = name;
            else blackPlayer = name;
            if (lifecycle == LifecycleReset) {
                lifecycle = LifecycleLobby;
                TakeSnapshot();
            }
            NotifyChanged(ChangeJoined);
            return InputAccepted;
        }

        public bool CanJoin(int color) {
            return CanJoinAs(color, LocalName());
        }

        // Whether the Join button of color applies to name: the seats are open (a reset board, the lobby or a loaded
        // board) and that seat is empty. A seat already held, by name or anyone else, has nothing to join.
        public bool CanJoinAs(int color, string name) {
            if (color != ChessRules.White && color != ChessRules.Black) return false;
            if (name == null || name.Length == 0 || !SeatsOpen()) return false;
            return (color == ChessRules.White ? whitePlayer : blackPlayer).Length == 0;
        }

        // Seats can be joined until a game starts: on a reset board, in the lobby and on a loaded board.
        bool SeatsOpen() {
            return lifecycle == LifecycleReset || lifecycle == LifecycleLobby || lifecycle == LifecycleLoaded;
        }

        // Whether this display name has the seat of color.
        public bool IsSeated(int color, string name) {
            if (name == null || name.Length == 0) return false;
            string seated = color == ChessRules.White ? whitePlayer : color == ChessRules.Black ? blackPlayer : "";
            // == on strings is ordinal: no trimming, case folding or normalization (docs/ownership.md).
            return seated == name;
        }

        // Whether this display name has either seat.
        public bool IsPlayer(string name) {
            return IsSeated(ChessRules.White, name) || IsSeated(ChessRules.Black, name);
        }

        // ---- Settings snapshot (docs/settings.md, Snapshot) ----
        // The board copies its effective settings from the arbiter when it leaves the reset state (the first join, or
        // loading a game in step 7.9a) and plays by that copy until it's reset, so changing the arbiter doesn't alter
        // a board already in use. An organizer can make it take a new one.

        // Copies the arbiter's effective settings for this board into gameSettings and gameNumbers, then applies the
        // forced lobby settings so the lobby shows them: the mode and clock in the lobby, the clock on a loaded board
        // (its mode is the loaded game's). A game already started keeps its mode and clock. With no arbiter, every
        // setting is off and the numbers are Never, Standard and NoClock.
        public void TakeSnapshot() {
            int[] numbers = new int[ChessArbiter.NumberCount];
            if (Utilities.IsValid(arbiter)) {
                gameSettings = arbiter.EffectiveSettings(boardNumber);
                arbiter.EffectiveNumbers(boardNumber, numbers);
            } else {
                gameSettings = 0;
                numbers[ChessArbiter.NumberResetAvailableTime] = ChessArbiter.Never;
                numbers[ChessArbiter.NumberAutoResetTime] = ChessArbiter.Never;
                numbers[ChessArbiter.NumberForcedMode] = ChessGame.ModeStandard;
                numbers[ChessArbiter.NumberForcedBaseTime] = ChessArbiter.NoClock;
                numbers[ChessArbiter.NumberForcedIncrement] = 0;
            }
            gameNumbers = numbers;
            if (lifecycle == LifecycleLobby) ApplyForcedSettings();
            else if (lifecycle == LifecycleLoaded) ApplyForcedClock();
        }

        // Whether a setting bit is on in the snapshot.
        public bool IsGameSettingOn(int setting) {
            return (gameSettings & setting) != 0;
        }

        // A numeric setting from the snapshot.
        public int GameNumber(int number) {
            return gameNumbers[number];
        }

        // Re-snapshot on request, for the local player.
        public int Resnapshot() {
            return ResnapshotAs(Networking.LocalPlayer);
        }

        // Re-snapshot on request (docs/settings.md): an organizer makes a board in use take a new snapshot, e.g. after
        // fixing a mistake in the settings. Refused for anyone else and on a reset board, which has no snapshot (it
        // takes one when it leaves the reset state). Like every change, it takes ownership of the board and sends it.
        public int ResnapshotAs(VRCPlayerApi who) {
            if (lifecycle == LifecycleReset) return InputRejected;
            if (!Utilities.IsValid(who) || !Utilities.IsValid(arbiter) || !arbiter.IsOrganizer(who)) {
                return InputRejected;
            }
            TakeSnapshot();
            NotifyChanged(ChangeSettings);
            return InputAccepted;
        }

        // ---- Lobby settings and Start (docs/board-lifecycle.md, Starting) ----
        // Any seated player can change the mode and the clock, unless the snapshot forces them. The local-player
        // methods are what the lobby buttons call; the *As methods take any name, for tests and the debug view.

        // The longest time control a player can choose, in seconds: 24 hours base, 1 hour increment.
        public const int MaxBaseTime = 86400;
        public const int MaxIncrement = 3600;

        // Whether the snapshot forces the mode or the clock.
        public bool IsModeForced() {
            return (gameSettings & ChessArbiter.SettingModeForced) != 0;
        }

        public bool IsClockForced() {
            return (gameSettings & ChessArbiter.SettingClockForced) != 0;
        }

        public int SetMode(int newMode) {
            return SetModeAs(LocalName(), newMode);
        }

        public bool CanSetMode() {
            return CanSetModeAs(LocalName());
        }

        // Whether name may choose the mode: a seated player in the lobby (a loaded game's mode is the game's own),
        // with the mode not forced.
        public bool CanSetModeAs(string name) {
            return lifecycle == LifecycleLobby && IsPlayer(name) && !IsModeForced();
        }

        // Chooses Standard or Chess960.
        public int SetModeAs(string name, int newMode) {
            if (!CanSetModeAs(name)) return InputRejected;
            if (newMode != ChessGame.ModeStandard && newMode != ChessGame.ModeChess960) return InputRejected;
            mode = newMode;
            NotifyChanged(ChangeSettings);
            return InputAccepted;
        }

        public int SetClock(int newBaseTime, int newIncrement) {
            return SetClockAs(LocalName(), newBaseTime, newIncrement);
        }

        public bool CanSetClock() {
            return CanSetClockAs(LocalName());
        }

        // Whether name may choose the clock: a seated player in the lobby or on a loaded board (which continues with
        // fresh clocks), with the clock not forced.
        public bool CanSetClockAs(string name) {
            return (lifecycle == LifecycleLobby || lifecycle == LifecycleLoaded) && IsPlayer(name) && !IsClockForced();
        }

        // Chooses the time control (seconds; base ChessArbiter.NoClock with increment 0 for no clock).
        public int SetClockAs(string name, int newBaseTime, int newIncrement) {
            if (!CanSetClockAs(name)) return InputRejected;
            if (!IsValidTimeControl(newBaseTime, newIncrement)) return InputRejected;
            baseTime = newBaseTime;
            increment = newIncrement;
            NotifyChanged(ChangeSettings);
            return InputAccepted;
        }

        // No clock (base NoClock, increment 0), or a base of 1 s to MaxBaseTime with an increment of 0 to
        // MaxIncrement.
        public static bool IsValidTimeControl(int baseSeconds, int incrementSeconds) {
            if (baseSeconds == ChessArbiter.NoClock) return incrementSeconds == 0;
            return baseSeconds >= 1 && baseSeconds <= MaxBaseTime && incrementSeconds >= 0
                && incrementSeconds <= MaxIncrement;
        }

        public int StartGame() {
            return StartGameAs(LocalName());
        }

        public bool CanStartGame() {
            return CanStartGameAs(LocalName());
        }

        // Whether the Start button applies to name: a seated player in the lobby with both seats filled (a loaded
        // board continues instead, see CanContinueAs).
        public bool CanStartGameAs(string name) {
            return lifecycle == LifecycleLobby && whitePlayer.Length != 0 && blackPlayer.Length != 0 && IsPlayer(name);
        }

        // The Start button: a seated player starts the game once both seats are filled. Forced settings are applied
        // first; a Chess960 game gets a random starting position (generated here, by the player pressing Start, and
        // synced like any other state). Both clocks get the base time and the white clock starts running.
        public int StartGameAs(string name) {
            if (!CanStartGameAs(name)) return InputRejected;

            ApplyForcedSettings();
            if (mode == ChessGame.ModeChess960) {
                ChessGame.Chess960Position(RandomChess960Index(), board, state);
                SetStartPosition(0, 1);
            } else {
                int clock;
                int number;
                ChessFen.ParseFen(ChessFen.StartFen, board, state, out clock, out number);
                SetStartPosition(clock, number);
            }
            int startTime = baseTime == ChessArbiter.NoClock ? 0 : baseTime * 1000;
            whiteTime = startTime;
            blackTime = startTime;
            turnStart = Networking.GetServerTimeInSeconds();
            gameId = NewGameIdAt(NetworkDateTime());
            continuedFrom = "";
            lifecycle = LifecycleInGame;
            NotifyChanged(ChangeStarted);
            return InputAccepted;
        }

        // Copies the forced mode and clock from the snapshot over the players' choices.
        void ApplyForcedSettings() {
            if (IsModeForced()) mode = gameNumbers[ChessArbiter.NumberForcedMode];
            ApplyForcedClock();
        }

        void ApplyForcedClock() {
            if (IsClockForced()) {
                baseTime = gameNumbers[ChessArbiter.NumberForcedBaseTime];
                increment = baseTime == ChessArbiter.NoClock ? 0 : gameNumbers[ChessArbiter.NumberForcedIncrement];
            }
        }

        // One of the 960 Chess960 starting positions at random (ChessGame.Chess960Position numbering).
        public int RandomChess960Index() {
            return UnityEngine.Random.Range(0, ChessGame.Chess960Count);
        }

        // ---- Game IDs (docs/game-ids.md) ----
        // A game gets its ID when it begins: on Start, and when it's continued from an earlier position (a takeback,
        // step 7.8b, or "Continue from here" on a loaded game, 7.9b), which also references the game it came from.

        // The UTC date and time now as the network has it (VRChat's network time, so every client agrees on it to
        // within a moment, whatever its own clock says).
        public DateTime NetworkDateTime() {
            return Networking.GetNetworkDateTime();
        }

        // A new Game ID for a game started at utc, with a random suffix.
        public string NewGameIdAt(DateTime utc) {
            return ChessGame.GameId(utc, UnityEngine.Random.Range(0, ChessGame.GameIdSuffixCount));
        }

        // The current game is continued from ply (half-moves from its starting position) as a new game started at
        // utc: continuedFrom becomes "<gameId>/<ply>" ("" when the current game has no ID, as with a PGN import
        // without a GameID tag) and the game gets a new ID. Truncating the history and the clocks is the caller's.
        public void ContinueGameIdAt(int ply, DateTime utc) {
            continuedFrom = ChessGame.ContinuedFromText(gameId, ply);
            gameId = NewGameIdAt(utc);
        }

        // ---- Moves and clocks (docs/clocks.md) ----
        // Only each side's remaining time when its turn began and the server time the current turn began are stored;
        // every client works out the running clock from them, so nothing is sent while a player thinks. The *At
        // methods take the server time to use, for tests; the others use the server time now.

        // Scratch for PlayMoveAt: the legal moves, and what MoveToSan needs for the check suffix.
        int[] legalMoves = new int[ChessMoves.MaxMoves];
        int[] sanScratch = new int[ChessMoves.MaxMoves];

        public double ServerTime() {
            return Networking.GetServerTimeInSeconds();
        }

        // Whether the game has a time control.
        public bool HasClock() {
            return baseTime != ChessArbiter.NoClock;
        }

        // Whether the side to move's clock is running: a game with a clock is in progress. Replay doesn't stop it.
        public bool IsClockRunning() {
            return lifecycle == LifecycleInGame && baseTime != ChessArbiter.NoClock;
        }

        public int RemainingTime(int color) {
            return RemainingTimeAt(color, ServerTime());
        }

        // A side's remaining time in milliseconds at server time now, never below 0. While the clock runs, the side
        // to move's is its time at the turn start less the time since; otherwise both are as stored (0 with no clock).
        public int RemainingTimeAt(int color, double now) {
            int stored = color == ChessRules.White ? whiteTime : color == ChessRules.Black ? blackTime : 0;
            if (!IsClockRunning() || color != state[ChessPosition.StateSideToMove]) return stored;
            double left = stored - ElapsedMs(now);
            return left <= 0 ? 0 : (int)left;
        }

        // Whole milliseconds since the turn began, never negative (a client's server time can be a little behind
        // the one the turn start was taken from).
        double ElapsedMs(double now) {
            double elapsed = Math.Floor((now - turnStart) * 1000.0);
            return elapsed < 0 ? 0 : elapsed;
        }

        public int FlaggedColor() {
            return FlaggedColorAt(ServerTime());
        }

        // The side whose time has run out at server time now (flag fall), ChessRules.NoColor if none. Only the side to
        // move's clock runs, and a move is refused once it has run out, so that's the only side that can flag. Every
        // client sees it; the owner ends the game on it (CheckFlagFallAt).
        public int FlaggedColorAt(double now) {
            if (!IsClockRunning()) return ChessRules.NoColor;
            int mover = state[ChessPosition.StateSideToMove];
            return RemainingTimeAt(mover, now) == 0 ? mover : ChessRules.NoColor;
        }

        public int PlayMove(int move) {
            return PlayMoveAt(move, ServerTime());
        }

        // Plays move, which must be one of the position's legal moves, as the next move of the game at server time
        // now, and records it: the move, its SAN, the halfmove clock, and the clocks. With a clock, the mover's time
        // is reduced by the time since their turn began, the increment is added, the result goes into the clock
        // history (-1 without a clock) and the turn start resets to now. Refused unless a game is in progress and the
        // board isn't in replay, if the mover's time has run out, and for a move that isn't legal. Who may move, and
        // matching a drop to a move, are the input API's (step 7.4a). A move that checkmates or stalemates ends the
        // game.
        public int PlayMoveAt(int move, double now) {
            if (lifecycle != LifecycleInGame || replayMode) return InputRejected;
            if (FlaggedColorAt(now) != ChessRules.NoColor) return InputRejected;
            int legalCount = ChessMoves.GenerateLegalMoves(board, state, legalMoves, true);
            bool legal = false;
            for (int i = 0; i < legalCount; i++) {
                if (legalMoves[i] == move) {
                    legal = true;
                    break;
                }
            }
            if (!legal) return InputRejected;

            if (moveCount == moveHistory.Length) GrowHistory();
            int mover = state[ChessPosition.StateSideToMove];
            notation[moveCount] = ChessNotation.MoveToSan(board, state, move, legalMoves, legalCount, sanScratch, true);
            ChessPosition.MakeMove(board, state, move);
            moveHistory[moveCount] = move;
            halfmoveClocks[moveCount] = halfmoveClock;
            halfmoveClock = ChessDraws.HalfmoveClockAfter(halfmoveClock, move);
            if (mover == ChessRules.Black) moveNumber++;

            if (baseTime == ChessArbiter.NoClock) {
                clockHistory[moveCount] = -1;
            } else {
                double left = (mover == ChessRules.White ? whiteTime : blackTime) - ElapsedMs(now)
                    + increment * 1000.0;
                int newTime = left >= int.MaxValue ? int.MaxValue : (int)left;
                if (mover == ChessRules.White) whiteTime = newTime;
                else blackTime = newTime;
                clockHistory[moveCount] = newTime;
            }
            turnStart = now;
            moveCount++;
            // A move by the side a draw was offered to declines it (FIDE 9.1.2.3); the offerer's own move keeps it.
            if (drawOfferColor != mover) drawOfferColor = ChessRules.NoColor;

            // Checkmate and stalemate end the game whatever the draw settings, and take precedence over the
            // automatic draws, which end it only while their rule is on.
            int status = ChessMoves.PositionStatus(board, state,
                ChessMoves.GenerateLegalMoves(board, state, legalMoves, true), true);
            if (status == ChessMoves.StatusCheckmate) EndGameAt(ChessGame.EndingCheckmate, mover, now);
            else if (status == ChessMoves.StatusStalemate) {
                EndGameAt(ChessGame.EndingStalemate, ChessRules.NoColor, now);
            } else {
                int automatic = AutomaticDraw();
                if (automatic != ChessGame.EndingNone) EndGameAt(automatic, ChessRules.NoColor, now);
            }
            NotifyChanged(ChangeMove);
            return InputAccepted;
        }

        // The draw the current position ends the game with by itself (docs/game-endings.md, Draw rules), checked
        // after each move that doesn't checkmate or stalemate: ChessGame.EndingFivefoldRepetition when the position has
        // occurred five times, EndingSeventyFiveMoveRule when seventy-five moves each have passed without a capture or
        // pawn move, EndingInsufficientMaterial when neither side can checkmate, each only while the snapshot has that
        // rule on; otherwise EndingNone (always under "No draws").
        public int AutomaticDraw() {
            return AutomaticDrawOf(board, state, moveCount, halfmoveClock);
        }

        // AutomaticDraw for the position in squares and positionState, reached by the first count moves of the
        // history, with this halfmove clock (an earlier position of the game, for CanContinueAs).
        int AutomaticDrawOf(byte[] squares, int[] positionState, int count, int clock) {
            if (IsGameSettingOn(ChessArbiter.SettingFivefoldRepetition)) {
                int repetitions = ChessDraws.CountRepetitions(squares, positionState, moveHistory, count, clock,
                    repetitionBoard, repetitionState, legalMoves, true);
                if (ChessDraws.IsFivefoldRepetition(repetitions)) return ChessGame.EndingFivefoldRepetition;
            }
            if (IsGameSettingOn(ChessArbiter.SettingSeventyFiveMoveRule) && ChessDraws.IsSeventyFiveMoveRule(clock)) {
                return ChessGame.EndingSeventyFiveMoveRule;
            }
            if (IsGameSettingOn(ChessArbiter.SettingDeadPosition) && ChessDraws.IsInsufficientMaterial(squares)) {
                return ChessGame.EndingInsufficientMaterial;
            }
            return ChessGame.EndingNone;
        }

        // ---- Game endings (docs/game-endings.md) ----

        // The owner watches for flag fall, so the game ends on time once; every other client sees the update.
        void Update() {
            if (lifecycle == LifecycleInGame && Networking.IsOwner(gameObject)) CheckFlagFall();
        }

        // Ends the game in progress at server time now: how it ended, who won (ChessRules.NoColor for a draw), and
        // the ended state. A running clock stops with the side to move's remaining time stored, so it keeps showing
        // what was left. A pending draw offer lapses; a takeback request stays, since an ended game can still be
        // continued. The game's PGN is logged on this transition, unless it has no moves.
        void EndGameAt(int newEnding, int newWinner, double now) {
            bool wasInGame = lifecycle == LifecycleInGame;
            if (IsClockRunning()) {
                int mover = state[ChessPosition.StateSideToMove];
                int left = RemainingTimeAt(mover, now);
                if (mover == ChessRules.White) whiteTime = left;
                else blackTime = left;
            }
            ending = newEnding;
            winner = newWinner;
            lifecycle = LifecycleEnded;
            drawOfferColor = ChessRules.NoColor;
            if (wasInGame) LogGame(ending, winner);
        }

        // ---- PGN (docs/pgn.md, Export and Logging) ----

        // The game's PGN as it stands: its tags (ChessPgn.WriteTags), a blank line and its move text.
        public string GamePgn() {
            return PgnWith(ending, winner);
        }

        // The game's PGN with this ending and winner in place of the board's own, so an unfinished game can be
        // logged as ChessGame.EndingUnterminated (a reset, 7.10, or a takeback, 7.8b) without ending it. The Date is
        // the start date in the Game ID; the first move's number and side come from the starting position. A loaded
        // game is written with its own players and time control, not the seats and the clock chosen for continuing.
        string PgnWith(int pgnEnding, int pgnWinner) {
            int firstSide = state[ChessPosition.StateSideToMove];
            if (moveCount % 2 == 1) firstSide = ChessRules.OtherColor(firstSide);
            bool loaded = lifecycle == LifecycleLoaded;
            int pgnBase = loaded ? replaySourceBaseTime : baseTime;
            // ChessPgn's convention: 0 for no clock, below 0 for unknown.
            int pgnBaseSeconds = pgnBase == ChessArbiter.NoClock ? 0 : pgnBase == UnknownClock ? -1 : pgnBase;
            string tags = ChessPgn.WriteTags(pgnEvent, pgnSite, ChessGame.DateFromGameId(gameId),
                loaded ? replaySourceWhite : whitePlayer, loaded ? replaySourceBlack : blackPlayer, pgnEnding,
                pgnWinner, mode, startFen, pgnBaseSeconds, loaded ? replaySourceIncrement : increment, gameId,
                continuedFrom);
            return tags + "\n" + ChessPgn.WriteMoveText(notation, clockHistory, moveCount, startMoveNumber, firstSide,
                pgnEnding, pgnWinner, ChessPgn.LineWidth);
        }

        // Writes the game's PGN, with this ending and winner, to the log as one entry: a "[ChessPGN]" line, then the
        // PGN, so a bot reading the output log finds every game (docs/game-endings.md: each client logs once, on the
        // transition). A game with no moves isn't logged.
        void LogGame(int logEnding, int logWinner) {
            if (moveCount == 0) return;
            lastLoggedPgn = PgnWith(logEnding, logWinner);
            loggedGames++;
            Debug.Log("[ChessPGN]\n" + lastLoggedPgn);
        }

        public int Resign(int color) {
            return ResignAs(color, LocalName());
        }

        // The resign button: name resigns for color (CanResignAs), and the other side wins.
        public int ResignAs(int color, string name) {
            if (!CanResignAs(color, name)) return InputRejected;
            EndGameAt(ChessGame.EndingResignation, ChessRules.OtherColor(color), ServerTime());
            NotifyChanged(ChangeEnded);
            return InputAccepted;
        }

        public bool CheckFlagFall() {
            return CheckFlagFallAt(ServerTime());
        }

        // Ends the game on time forfeit if the side to move's flag has fallen at server time now, and returns whether
        // it did. The side out of time loses, unless its opponent can't checkmate (ChessDraws.HasNoMatingMaterial) and
        // the snapshot's dead-position rule is on, which makes it a draw; under "No draws" it always loses.
        public bool CheckFlagFallAt(double now) {
            if (!EndOnFlagFallAt(now)) return false;
            NotifyChanged(ChangeEnded);
            return true;
        }

        // CheckFlagFallAt without telling the listeners, for a change that ends the game on time on its way (a reset).
        bool EndOnFlagFallAt(double now) {
            int flagged = FlaggedColorAt(now);
            if (flagged == ChessRules.NoColor) return false;
            EndGameAt(ChessGame.EndingTimeForfeit, FlagFallWinner(flagged), now);
            return true;
        }

        // Who wins when flagged's time runs out: the opponent, or nobody (a draw) when the opponent can't checkmate
        // and the dead-position rule is on.
        int FlagFallWinner(int flagged) {
            int opponent = ChessRules.OtherColor(flagged);
            bool draw = IsGameSettingOn(ChessArbiter.SettingDeadPosition)
                && ChessDraws.HasNoMatingMaterial(board, opponent);
            return draw ? ChessRules.NoColor : opponent;
        }

        // ---- Piece input (docs/player-input.md) ----
        // The view forwards pickups and drops as squares; the board decides. A drop is matched against the legal
        // moves by from and to square. One match is played; the four promotions of one pawn move answer
        // InputPromotionNeeded, and the view calls PromotionChosen with the same squares and the chosen piece. Nothing
        // is remembered in between, so there's nothing to sync or clear. A king castles by being dropped on its own
        // rook, on its castling destination when no other king move ends there, or on the castle drop space (a view
        // object, forwarded as KingDroppedOnCastleSpace). In replay, pickups and drops are free moves on the replay
        // board instead (ReplayPiecePickedUpAs, ReplayPieceDroppedAs). Activity timestamps come with 8.5. The *As
        // methods take any name, for tests and the debug view; in replay they don't count name as an organizer.

        public int PiecePickedUp(int square) {
            if (replayMode) return ReplayPiecePickedUpAs(LocalName(), LocalIsOrganizer(), square);
            return PiecePickedUpAs(LocalName(), square);
        }

        // Whether name may pick up the piece on square: a game is in progress and not in replay, the side to move
        // has time left, name holds that side's seat and the piece is that side's. Rejected, the view puts it back.
        // In replay, a free move's pickup (ReplayPiecePickedUpAs).
        public int PiecePickedUpAs(string name, int square) {
            if (replayMode) return ReplayPiecePickedUpAs(name, false, square);
            return NotifyIfRejected(MayPickUp(name, square) ? InputAccepted : InputRejected);
        }

        bool MayPickUp(string name, int square) {
            if (!MayMove(name)) return false;
            if (square < 0 || square >= 64) return false;
            int piece = board[square];
            return piece != ChessRules.Empty && ChessRules.PieceColorOf(piece) == state[ChessPosition.StateSideToMove];
        }

        public int PieceDropped(int from, int to) {
            if (replayMode) return ReplayPieceDroppedAs(LocalName(), LocalIsOrganizer(), from, to);
            return PieceDroppedAs(LocalName(), from, to);
        }

        // name dropped the piece from from on to. Plays the legal move it matches, answers InputPromotionNeeded for a
        // promotion (nothing played), and rejects anything else, including a drop back on its own square. A king
        // dropped on its own rook castles with it; dropped on its castling destination it castles unless a normal king
        // move ends there too (Chess960 castles moving the king one square), which is then played instead. In replay,
        // a free move (ReplayPieceDroppedAs).
        public int PieceDroppedAs(string name, int from, int to) {
            if (replayMode) return ReplayPieceDroppedAs(name, false, from, to);
            return NotifyIfRejected(DropAs(name, from, to, ChessRules.NoPieceType));
        }

        public int PromotionChosen(int from, int to, int pieceType) {
            return PromotionChosenAs(LocalName(), from, to, pieceType);
        }

        // The follow-up to InputPromotionNeeded: plays the promotion from-to to pieceType (ChessRules.Queen, Rook,
        // Bishop or Knight). Rejected for any other type and for a move that isn't a promotion, so a picker that's
        // cancelled passes ChessRules.NoPieceType and the rejection puts the pawn back.
        public int PromotionChosenAs(string name, int from, int to, int pieceType) {
            if (pieceType != ChessRules.Queen && pieceType != ChessRules.Rook && pieceType != ChessRules.Bishop
                && pieceType != ChessRules.Knight) {
                return NotifyIfRejected(InputRejected);
            }
            return NotifyIfRejected(DropAs(name, from, to, pieceType));
        }

        public int KingDroppedOnCastleSpace(int side) {
            return KingDroppedOnCastleSpaceAs(LocalName(), side);
        }

        // name dropped the king on the castle drop space of side (CastleKingSide or CastleQueenSide): plays the side to
        // move's castle on that side if it's legal. The view shows the space only when IsCastleDropSpaceNeeded, but the
        // castle is played either way.
        public int KingDroppedOnCastleSpaceAs(string name, int side) {
            if (!MayMove(name)) return NotifyIfRejected(InputRejected);
            int legalCount = ChessMoves.GenerateLegalMoves(board, state, legalMoves, true);
            int castle = FindCastle(legalCount, side);
            if (castle == 0) return NotifyIfRejected(InputRejected);
            return NotifyIfRejected(PlayMove(castle));
        }

        // Whether the view should offer the castle drop space for side while the side to move's king is held: a game
        // is in progress and not in replay, that castle is legal, and dropping the king on its destination wouldn't
        // castle (the king already stands there, or a normal king move ends there).
        public bool IsCastleDropSpaceNeeded(int side) {
            if (lifecycle != LifecycleInGame || replayMode) return false;
            int legalCount = ChessMoves.GenerateLegalMoves(board, state, legalMoves, true);
            int castle = FindCastle(legalCount, side);
            if (castle == 0) return false;
            int from = (castle >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask;
            int destination = CastleDestination(castle);
            return from == destination || FindNormalMove(legalCount, from, destination) != 0;
        }

        // Whether name may move now: a game in progress, not in replay, the side to move's flag not fallen, and name
        // seated as the side to move.
        bool MayMove(string name) {
            if (lifecycle != LifecycleInGame || replayMode) return false;
            if (FlaggedColor() != ChessRules.NoColor) return false;
            return IsSeated(state[ChessPosition.StateSideToMove], name);
        }

        // Matches from-to (and the promotion, NoPieceType for a plain drop) against the legal moves and plays a single
        // match. A plain drop matching promotions answers InputPromotionNeeded. A castle matches a plain drop on its
        // rook's square, or on the king's destination when no other move does.
        int DropAs(string name, int from, int to, int promotion) {
            if (!MayMove(name)) return InputRejected;
            if (from < 0 || from >= 64 || to < 0 || to >= 64 || from == to) return InputRejected;
            int legalCount = ChessMoves.GenerateLegalMoves(board, state, legalMoves, true);
            int found = 0;
            int matches = 0;
            int castle = 0;
            for (int i = 0; i < legalCount; i++) {
                int move = legalMoves[i];
                if (((move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask) != from) continue;
                if (((move >> ChessRules.MoveCastleShift) & 1) != 0) {
                    // No other move goes to a castle's rook square (the king's own rook stands there), but one can
                    // end on its destination.
                    if (promotion != ChessRules.NoPieceType) continue;
                    if (((move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask) == to) return PlayMove(move);
                    if (CastleDestination(move) == to) castle = move;
                    continue;
                }
                if (((move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask) != to) continue;
                int movePromotion = (move >> ChessRules.MovePromotionShift) & ChessRules.MoveTypeMask;
                if (promotion != ChessRules.NoPieceType && movePromotion != promotion) continue;
                found = move;
                matches++;
            }
            if (matches == 0) return castle != 0 ? PlayMove(castle) : InputRejected;
            if (matches > 1) return InputPromotionNeeded;
            return PlayMove(found);
        }

        // The legal castle on side among the first legalCount of legalMoves, 0 if there's none (a castle is never 0:
        // its castle bit is set).
        int FindCastle(int legalCount, int side) {
            for (int i = 0; i < legalCount; i++) {
                int move = legalMoves[i];
                if (((move >> ChessRules.MoveCastleShift) & 1) == 0) continue;
                int kingFile = ((move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask) & 7;
                int rookFile = ((move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask) & 7;
                if ((rookFile > kingFile ? CastleKingSide : CastleQueenSide) == side) return move;
            }
            return 0;
        }

        // The first move from-to among the first legalCount of legalMoves that isn't a castle, 0 if there's none.
        int FindNormalMove(int legalCount, int from, int to) {
            for (int i = 0; i < legalCount; i++) {
                int move = legalMoves[i];
                if (((move >> ChessRules.MoveCastleShift) & 1) != 0) continue;
                if (((move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask) == from
                    && ((move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask) == to) {
                    return move;
                }
            }
            return 0;
        }

        // The square a castle puts the king on: g or c on its back rank, by the side of its rook.
        static int CastleDestination(int move) {
            int from = (move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask;
            int rookFile = ((move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask) & 7;
            return (from & ~7) | (rookFile > (from & 7) ? ChessRules.FileG : ChessRules.FileC);
        }

        // ---- Buttons (docs/player-input.md, Buttons) ----
        // Whether each board button applies, for the view (which shows only those that do) and for the button's own
        // method, which refuses when its check doesn't. The methods themselves come with their steps: resign (with the
        // game endings), draws (below), replay 7.8a, takebacks 7.8b, loading 7.9a, continuing 7.9b, reset 7.10. The plain
        // checks are for the local player; the *As ones take any name, and an organizer flag where organizers count,
        // for tests and the debug view. Who may take ownership, and the idle-reset exception for everyone else, come
        // with steps 8.4 and 8.5.

        // Scratch for the repetition counts of ClaimableDraw and AutomaticDraw.
        byte[] repetitionBoard = new byte[64];
        int[] repetitionState = new int[ChessPosition.StateLength];

        // Whether the local player is an organizer of the board's arbiter (docs/organizers.md).
        bool LocalIsOrganizer() {
            VRCPlayerApi player = Networking.LocalPlayer;
            return Utilities.IsValid(player) && Utilities.IsValid(arbiter) && arbiter.IsOrganizer(player);
        }

        // Whether the game is being played: in progress and the side to move's flag not fallen. Replay doesn't pause
        // anything but moves, so the buttons below that aren't moves work in replay too.
        bool GameRunning() {
            return lifecycle == LifecycleInGame && FlaggedColor() == ChessRules.NoColor;
        }

        public bool CanResign(int color) {
            return CanResignAs(color, LocalName());
        }

        // Whether name may resign for color: the game is running and name has that seat (a player in both seats
        // chooses which side resigns).
        public bool CanResignAs(int color, string name) {
            return GameRunning() && IsSeated(color, name);
        }

        public bool CanOfferDraw(int color) {
            return CanOfferDrawAs(color, LocalName());
        }

        // Whether name may offer a draw for color: the snapshot allows draws by agreement, the game is running, name
        // has that seat and no offer is pending (one from the other side is accepted instead).
        public bool CanOfferDrawAs(int color, string name) {
            return IsGameSettingOn(ChessArbiter.SettingDrawAgreement) && GameRunning() && IsSeated(color, name)
                && drawOfferColor == ChessRules.NoColor;
        }

        public bool CanAcceptDraw(int color) {
            return CanAnswerDrawAs(color, LocalName());
        }

        public bool CanAcceptDrawAs(int color, string name) {
            return CanAnswerDrawAs(color, name);
        }

        public bool CanDeclineDraw(int color) {
            return CanAnswerDrawAs(color, LocalName());
        }

        public bool CanDeclineDrawAs(int color, string name) {
            return CanAnswerDrawAs(color, name);
        }

        // Whether name may accept or decline a draw offer for color: the other side's offer is pending, the snapshot
        // still allows draws by agreement (a re-snapshot can turn them off), the game is running and name has that
        // seat.
        bool CanAnswerDrawAs(int color, string name) {
            if (color != ChessRules.White && color != ChessRules.Black) return false;
            return IsGameSettingOn(ChessArbiter.SettingDrawAgreement) && GameRunning() && IsSeated(color, name)
                && drawOfferColor == ChessRules.OtherColor(color);
        }

        // The draw the side to move could claim now (docs/game-endings.md, Claims): ChessGame.EndingThreefoldRepetition
        // when the position has occurred three times and the snapshot allows that claim, otherwise
        // EndingFiftyMoveRule when fifty moves each have passed without a capture or pawn move and that claim is
        // allowed, otherwise EndingNone. Under "No draws" it's always EndingNone.
        public int ClaimableDraw() {
            if (IsGameSettingOn(ChessArbiter.SettingThreefoldRepetition)) {
                int count = ChessDraws.CountRepetitions(board, state, moveHistory, moveCount, halfmoveClock,
                    repetitionBoard, repetitionState, legalMoves, true);
                if (ChessDraws.IsThreefoldRepetition(count)) return ChessGame.EndingThreefoldRepetition;
            }
            if (IsGameSettingOn(ChessArbiter.SettingFiftyMoveRule) && ChessDraws.IsFiftyMoveRule(halfmoveClock)) {
                return ChessGame.EndingFiftyMoveRule;
            }
            return ChessGame.EndingNone;
        }

        public bool CanClaimDraw() {
            return CanClaimDrawAs(LocalName());
        }

        // Whether name may claim a draw: the game is running, name has the side to move's seat and ClaimableDraw has
        // one.
        public bool CanClaimDrawAs(string name) {
            if (!GameRunning() || !IsSeated(state[ChessPosition.StateSideToMove], name)) return false;
            return ClaimableDraw() != ChessGame.EndingNone;
        }

        // ---- Draws (docs/game-endings.md, Draw rules) ----
        // Offers and claims aren't moves, so like resigning they work in replay too. The automatic draws are checked
        // after each move (AutomaticDraw).

        public int OfferDraw(int color) {
            return OfferDrawAs(color, LocalName());
        }

        // The offer draw button: name offers a draw for color (CanOfferDrawAs). The offer stands until the other side
        // accepts or declines it, or declines it by making a move; the game ending lets it lapse.
        public int OfferDrawAs(int color, string name) {
            if (!CanOfferDrawAs(color, name)) return InputRejected;
            drawOfferColor = color;
            NotifyChanged(ChangeRequest);
            return InputAccepted;
        }

        public int AcceptDraw(int color) {
            return AcceptDrawAs(color, LocalName());
        }

        // The accept draw button: name accepts, for color, the other side's offer (CanAcceptDrawAs), and the game is
        // drawn by agreement.
        public int AcceptDrawAs(int color, string name) {
            if (!CanAcceptDrawAs(color, name)) return InputRejected;
            EndGameAt(ChessGame.EndingDrawAgreement, ChessRules.NoColor, ServerTime());
            NotifyChanged(ChangeEnded);
            return InputAccepted;
        }

        public int DeclineDraw(int color) {
            return DeclineDrawAs(color, LocalName());
        }

        // The decline draw button: name declines, for color, the other side's offer (CanDeclineDrawAs), and the game
        // goes on with no offer pending.
        public int DeclineDrawAs(int color, string name) {
            if (!CanDeclineDrawAs(color, name)) return InputRejected;
            drawOfferColor = ChessRules.NoColor;
            NotifyChanged(ChangeRequest);
            return InputAccepted;
        }

        public int ClaimDraw() {
            return ClaimDrawAs(LocalName());
        }

        // The claim draw button: name, seated as the side to move, claims the draw ClaimableDraw has
        // (CanClaimDrawAs), threefold repetition before the fifty-move rule, and the game is drawn.
        public int ClaimDrawAs(string name) {
            if (!CanClaimDrawAs(name)) return InputRejected;
            EndGameAt(ClaimableDraw(), ChessRules.NoColor, ServerTime());
            NotifyChanged(ChangeEnded);
            return InputAccepted;
        }

        // Whether name may work the replay controls: an organizer, a seated player, or anyone on a board nobody has
        // joined (a loaded game; docs/ownership.md limits only who isn't seated while someone is).
        bool MayControlReplay(string name, bool organizer) {
            if (organizer || IsPlayer(name)) return true;
            return name != null && name.Length != 0 && whitePlayer.Length == 0 && blackPlayer.Length == 0;
        }

        public bool CanEnterReplay() {
            return CanEnterReplayAs(LocalName(), LocalIsOrganizer());
        }

        // Whether name may enter replay: a game in progress or ended, not already in replay, and name may work the
        // replay controls. A loaded game is always in replay.
        public bool CanEnterReplayAs(string name, bool organizer) {
            if (lifecycle != LifecycleInGame && lifecycle != LifecycleEnded) return false;
            return !replayMode && MayControlReplay(name, organizer);
        }

        public bool CanLeaveReplay() {
            return CanLeaveReplayAs(LocalName(), LocalIsOrganizer());
        }

        // Whether name may leave replay: in replay of a game in progress or ended (a loaded board has no game to go
        // back to), and name may work the replay controls.
        public bool CanLeaveReplayAs(string name, bool organizer) {
            if (lifecycle != LifecycleInGame && lifecycle != LifecycleEnded) return false;
            return replayMode && MayControlReplay(name, organizer);
        }

        public bool CanStepTo(int ply) {
            return CanStepToAs(LocalName(), LocalIsOrganizer(), ply);
        }

        // Whether name may step the replay to ply (0 before the first move to moveCount after the last): in replay,
        // name may work the replay controls, and ply is in the game and not the position already shown (from a custom
        // position, stepping to its ply shows the game's position again).
        public bool CanStepToAs(string name, bool organizer, int ply) {
            if (!replayMode || !MayControlReplay(name, organizer)) return false;
            if (ply < 0 || ply > moveCount) return false;
            return ply != replayIndex || replayCustom;
        }

        // Whether a game can continue from the replay position: a position of the game history (not custom) with a
        // move after it, of a game in progress (the side to move's flag not fallen) or ended.
        bool CanContinueFromReplay() {
            if (!replayMode || replayCustom || replayIndex < 0 || replayIndex >= moveCount) return false;
            return GameRunning() || lifecycle == LifecycleEnded;
        }

        public bool CanRequestTakeback(int color) {
            return CanRequestTakebackAs(color, LocalName());
        }

        // Whether name may ask, for color, to continue from the replay position (docs/replay.md): the snapshot allows
        // takebacks, no request is pending, name has that seat, and the game can continue from there.
        public bool CanRequestTakebackAs(int color, string name) {
            return IsGameSettingOn(ChessArbiter.SettingTakebacks) && takebackColor == ChessRules.NoColor
                && IsSeated(color, name) && CanContinueFromReplay();
        }

        public bool CanAcceptTakeback(int color) {
            return CanAnswerTakebackAs(color, LocalName());
        }

        public bool CanAcceptTakebackAs(int color, string name) {
            return CanAnswerTakebackAs(color, name);
        }

        public bool CanDeclineTakeback(int color) {
            return CanAnswerTakebackAs(color, LocalName());
        }

        public bool CanDeclineTakebackAs(int color, string name) {
            return CanAnswerTakebackAs(color, name);
        }

        // Whether name may accept or decline a takeback request for color: the other side's request is pending for
        // the position still shown, the snapshot still allows takebacks, name has that seat, and the game can
        // continue from there.
        bool CanAnswerTakebackAs(int color, string name) {
            if (color != ChessRules.White && color != ChessRules.Black) return false;
            return IsGameSettingOn(ChessArbiter.SettingTakebacks) && takebackColor == ChessRules.OtherColor(color)
                && takebackPly == replayIndex && IsSeated(color, name) && CanContinueFromReplay();
        }

        public bool CanContinue() {
            return CanContinueAs(LocalName());
        }

        // Whether name may press "Continue from here" on a loaded game (docs/replay.md): both seats filled, name
        // seated, and the replay showing a position of the game's history (not custom) that isn't over already. Unlike
        // a takeback it can continue from the last position too, since nobody has played the loaded game on.
        public bool CanContinueAs(string name) {
            if (lifecycle != LifecycleLoaded || !replayMode || replayCustom) return false;
            if (replayIndex < 0 || replayIndex > moveCount) return false;
            if (whitePlayer.Length == 0 || blackPlayer.Length == 0 || !IsPlayer(name)) return false;
            return !IsOverAtPly(replayIndex);
        }

        // Scratch for IsOverAtPly: the game's position at the ply.
        byte[] plyBoard = new byte[64];
        int[] plyState = new int[ChessPosition.StateLength];

        // Whether a game continued from ply would be over at once: the position before move ply has no legal moves
        // (checkmate or stalemate, as at the end of a loaded game won by mate), or one of the automatic draws the
        // snapshot has on applies to it. Only a loaded game's positions can be: a played game ends on such a position.
        bool IsOverAtPly(int ply) {
            Array.Copy(board, plyBoard, 64);
            Array.Copy(state, plyState, ChessPosition.StateLength);
            for (int i = moveCount - 1; i >= ply; i--) ChessPosition.UnmakeMove(plyBoard, plyState, moveHistory[i]);
            if (ChessMoves.GenerateLegalMoves(plyBoard, plyState, legalMoves, true) == 0) return true;
            int clock = ply < moveCount ? halfmoveClocks[ply] : halfmoveClock;
            return AutomaticDrawOf(plyBoard, plyState, ply, clock) != ChessGame.EndingNone;
        }

        public bool CanReset() {
            return CanResetAs(LocalName(), LocalIsOrganizer());
        }

        // Whether name may reset the board (docs/board-lifecycle.md, Reset): a board that isn't reset already, by a
        // seated player or an organizer. Everyone else once the board has been idle long enough comes with step 8.5.
        public bool CanResetAs(string name, bool organizer) {
            return lifecycle != LifecycleReset && (organizer || IsPlayer(name));
        }

        public int ResetBoard() {
            return ResetBoardAs(LocalName(), LocalIsOrganizer());
        }

        // The reset button (CanResetAs; named ResetBoard since Reset is a Unity message): clears the board and both
        // seats. A game in progress is logged as unfinished (*) if it had moves, after a fallen flag has ended it on
        // time (as CheckFlagFall does), since that game is already over, not unfinished. An ended game was logged when
        // it ended and a loaded one wasn't played here, so neither is logged. Listeners hear of the reset only.
        public int ResetBoardAs(string name, bool organizer) {
            if (!CanResetAs(name, organizer)) return InputRejected;
            if (lifecycle == LifecycleInGame && !EndOnFlagFallAt(ServerTime())) {
                LogGame(ChessGame.EndingUnterminated, ChessRules.NoColor);
            }
            ClearState();
            NotifyChanged(ChangeReset);
            return InputAccepted;
        }

        // Whether a game can be loaded (a PGN import, or from the archive): only onto a reset board, by anyone.
        public bool CanLoadGame() {
            return lifecycle == LifecycleReset;
        }

        // ---- Replay (docs/replay.md) ----
        // Replay shows a position on its own board, replayBoard, and never touches the game state, so leaving it
        // returns to the game exactly as it was. Stepping shows a position of the game history (replayIndex, its ply)
        // by undoing the game's moves from the current position. Free moves are plain piece placement on the replay
        // board, with no legality, and make the position custom until the next step. The clock keeps running.

        // Scratch for stepping: the state of the position being undone to (the replay board has no state).
        int[] replayState = new int[ChessPosition.StateLength];

        public int EnterReplay() {
            return EnterReplayAs(LocalName(), LocalIsOrganizer());
        }

        // The enter replay button (CanEnterReplayAs): the replay shows the game's current position.
        public int EnterReplayAs(string name, bool organizer) {
            if (!CanEnterReplayAs(name, organizer)) return InputRejected;
            replayMode = true;
            ShowPly(moveCount);
            NotifyChanged(ChangeEnteredReplay);
            return InputAccepted;
        }

        public int LeaveReplay() {
            return LeaveReplayAs(LocalName(), LocalIsOrganizer());
        }

        // The leave replay button (CanLeaveReplayAs): back to the game, which replay never changed. The replay fields
        // are put back to the current position, as on entering.
        public int LeaveReplayAs(string name, bool organizer) {
            if (!CanLeaveReplayAs(name, organizer)) return InputRejected;
            ShowPly(moveCount);
            replayMode = false;
            NotifyChanged(ChangeLeftReplay);
            return InputAccepted;
        }

        public int StepTo(int ply) {
            return StepToAs(LocalName(), LocalIsOrganizer(), ply);
        }

        // The replay's step buttons (CanStepToAs): shows the position before ply of the game's moves (0 the starting
        // position, moveCount the current one), which clears the custom flag. The view steps by one with replayIndex
        // - 1 and + 1.
        public int StepToAs(string name, bool organizer, int ply) {
            if (!CanStepToAs(name, organizer, ply)) return InputRejected;
            ShowPly(ply);
            NotifyChanged(ChangeReplayStep);
            return InputAccepted;
        }

        // Puts the position before ply of the game's moves on the replay board, undoing the moves after it from the
        // current position, and makes ply the replay's game position (not custom). The replay position changes, so a
        // pending takeback request is cancelled.
        void ShowPly(int ply) {
            takebackColor = ChessRules.NoColor;
            takebackPly = 0;
            Array.Copy(board, replayBoard, 64);
            Array.Copy(state, replayState, ChessPosition.StateLength);
            for (int i = moveCount - 1; i >= ply; i--) {
                ChessPosition.UnmakeMove(replayBoard, replayState, moveHistory[i]);
            }
            replayIndex = ply;
            replayCustom = false;
        }

        // Whether name may pick up the piece on square of the replay board for a free move: in replay, name may work
        // the replay controls, and there's a piece there (either colour).
        public int ReplayPiecePickedUpAs(string name, bool organizer, int square) {
            return NotifyIfRejected(MayPickUpInReplay(name, organizer, square) ? InputAccepted : InputRejected);
        }

        bool MayPickUpInReplay(string name, bool organizer, int square) {
            if (!replayMode || !MayControlReplay(name, organizer)) return false;
            return square >= 0 && square < 64 && replayBoard[square] != ChessRules.Empty;
        }

        // A free move in replay: the piece on from goes to to, replacing whatever stood there (any piece, kings too),
        // with no legality, promotion or castling, and the position becomes custom, which cancels a pending takeback
        // request. Rejected unless name may pick the piece up, and for a drop back on its own square or off the board.
        public int ReplayPieceDroppedAs(string name, bool organizer, int from, int to) {
            if (!MayPickUpInReplay(name, organizer, from) || to < 0 || to >= 64 || to == from) {
                return NotifyIfRejected(InputRejected);
            }
            replayBoard[to] = replayBoard[from];
            replayBoard[from] = ChessRules.Empty;
            replayCustom = true;
            takebackColor = ChessRules.NoColor;
            takebackPly = 0;
            NotifyChanged(ChangeFreeMove);
            return InputAccepted;
        }

        // ---- Takebacks (docs/replay.md, Continuing from an earlier position) ----
        // A seated player asks to continue the game from the replay position; the other side accepts or declines. The
        // request is for the ply shown, and changing the replay position cancels it (ShowPly, ReplayPieceDroppedAs).

        public int RequestTakeback(int color) {
            return RequestTakebackAs(color, LocalName());
        }

        // The takeback button: name asks, for color, to continue from the position the replay shows
        // (CanRequestTakebackAs).
        public int RequestTakebackAs(int color, string name) {
            if (!CanRequestTakebackAs(color, name)) return InputRejected;
            takebackColor = color;
            takebackPly = replayIndex;
            NotifyChanged(ChangeRequest);
            return InputAccepted;
        }

        public int DeclineTakeback(int color) {
            return DeclineTakebackAs(color, LocalName());
        }

        // The decline takeback button: name declines, for color, the other side's request (CanDeclineTakebackAs).
        public int DeclineTakebackAs(int color, string name) {
            if (!CanDeclineTakebackAs(color, name)) return InputRejected;
            takebackColor = ChessRules.NoColor;
            takebackPly = 0;
            NotifyChanged(ChangeRequest);
            return InputAccepted;
        }

        public int AcceptTakeback(int color) {
            return AcceptTakebackAs(color, LocalName());
        }

        // The accept takeback button: name accepts, for color, the other side's request (CanAcceptTakebackAs), and the
        // game continues from the requested ply as a new game. A game still in progress is first logged as unfinished
        // (*) with its whole history; an ended one was logged when it ended. The continued game gets a new ID
        // referencing this one at that ply (ContinueGameIdAt), the history is cut there, the clocks are put back to
        // what they were at that ply with the turn starting now, and the game is in progress again, out of replay,
        // with no result and nothing pending.
        public int AcceptTakebackAs(int color, string name) {
            if (!CanAcceptTakebackAs(color, name)) return InputRejected;
            int ply = takebackPly;
            if (lifecycle == LifecycleInGame) LogGame(ChessGame.EndingUnterminated, ChessRules.NoColor);
            ContinueGameIdAt(ply, NetworkDateTime());
            TruncateHistory(ply);
            RestoreClocks(ServerTime());
            ending = ChessGame.EndingNone;
            winner = ChessRules.NoColor;
            lifecycle = LifecycleInGame;
            drawOfferColor = ChessRules.NoColor;
            ShowPly(moveCount);
            replayMode = false;
            NotifyChanged(ChangeTakeback);
            return InputAccepted;
        }

        // Cuts the game back to its first ply moves: undoes the moves after them on the game position (with the move
        // number and halfmove clock) and empties their history entries.
        void TruncateHistory(int ply) {
            if (ply >= moveCount) return;
            halfmoveClock = halfmoveClocks[ply];
            for (int i = moveCount - 1; i >= ply; i--) {
                ChessPosition.UnmakeMove(board, state, moveHistory[i]);
                if (state[ChessPosition.StateSideToMove] == ChessRules.Black) moveNumber--;
                moveHistory[i] = 0;
                notation[i] = "";
                clockHistory[i] = -1;
                halfmoveClocks[i] = 0;
            }
            moveCount = ply;
        }

        // Puts both clocks back to what they were after the last move each side made (the clock history), or the base
        // time for a side that hasn't moved, and starts the side to move's turn at server time now. With no clock
        // they stay 0. A history entry of -1 (none known) counts as the base time.
        void RestoreClocks(double now) {
            int start = baseTime == ChessArbiter.NoClock ? 0 : baseTime * 1000;
            int toMove = moveCount >= 2 ? clockHistory[moveCount - 2] : -1;
            int moved = moveCount >= 1 ? clockHistory[moveCount - 1] : -1;
            if (baseTime == ChessArbiter.NoClock || toMove < 0) toMove = start;
            if (baseTime == ChessArbiter.NoClock || moved < 0) moved = start;
            if (state[ChessPosition.StateSideToMove] == ChessRules.White) {
                whiteTime = toMove;
                blackTime = moved;
            } else {
                whiteTime = moved;
                blackTime = toMove;
            }
            turnStart = now;
        }

        // ---- Loading a game (docs/replay.md, Continuing a loaded game) ----
        // A finished PGN import (LoadImport) or a game from the archive (step 10) is put on a reset board, which
        // enters the loaded state showing it in replay. Players can then join and continue it (step 7.9b).

        // Loads a finished import from importer (ChessPgnImporter.result ImportDone): its starting position, moves,
        // SAN and clocks, the mode from Variant, the White, Black, Result and GameID tags, and the time control from
        // TimeControl ("-" no clock, a value it can't read unknown). Refused while it's still importing (its result
        // is ImportMore) or after a failed import, and as LoadGame refuses.
        public int LoadImport(ChessPgnImporter importer) {
            if (!Utilities.IsValid(importer) || importer.result != ChessPgn.ImportDone) return InputRejected;
            string[] tags = importer.tags;
            if (tags == null) return InputRejected;
            int baseSeconds;
            int incrementSeconds;
            int loadedBase = UnknownClock;
            int loadedIncrement = 0;
            if (ChessPgn.ParseTimeControl(tags[ChessPgn.TagTimeControl], out baseSeconds, out incrementSeconds)
                && baseSeconds != 0) {
                loadedBase = baseSeconds;
                loadedIncrement = incrementSeconds;
            } else if (baseSeconds == 0) {
                loadedBase = ChessArbiter.NoClock;
            }
            return LoadGame(importer.StartFen(), importer.moves, importer.sans, importer.clocks, importer.MoveCount(),
                ChessPgn.ModeFromVariant(tags[ChessPgn.TagVariant]), tags[ChessPgn.TagWhite], tags[ChessPgn.TagBlack],
                tags[ChessPgn.TagResult], tags[ChessPgn.TagGameId], loadedBase, loadedIncrement);
        }

        // Loads a game onto the board (CanLoadGame: only a reset board): the position fen ("" or null for the
        // standard start), the first count of moves (packed, the game's legal moves from fen, as an import or the
        // archive has them), their SAN in sans ("" where not known yet) and clocks (milliseconds, below 0 for
        // unknown), the mode (ChessGame.ModeStandard or ModeChess960), its White, Black and Result tags, its Game ID
        // ("" for none) and its time control (seconds, base ChessArbiter.NoClock for none or UnknownClock). The board
        // plays the moves through to rebuild the halfmove clocks and the move number, and enters the loaded state in
        // replay showing the last position, with the result: a checkmate or stalemate on the board, otherwise
        // ChessGame.EndingUnknown with the Result tag's winner, or none for "*" or no tag. The clock for continuing
        // starts as the game's own time control when a player could choose it, no clock otherwise. Then the settings
        // snapshot is taken (which applies a forced clock). Refused for anything invalid, which leaves the board
        // reset.
        public int LoadGame(string fen, int[] moves, string[] sans, int[] clocks, int count, int loadedMode,
            string white, string black, string result, string loadedGameId, int loadedBaseTime, int loadedIncrement) {
            if (!CanLoadGame()) return InputRejected;
            if (moves == null || sans == null || clocks == null || count < 0 || count > moves.Length
                || count > sans.Length || count > clocks.Length) {
                return InputRejected;
            }
            if (loadedMode != ChessGame.ModeStandard && loadedMode != ChessGame.ModeChess960) return InputRejected;
            if (loadedBaseTime < 1 && loadedBaseTime != ChessArbiter.NoClock && loadedBaseTime != UnknownClock) {
                return InputRejected;
            }
            if (loadedIncrement < 0) return InputRejected;
            int clock;
            int number;
            // ParseFen changes board and state only when it succeeds.
            if (!ChessFen.ParseFen(fen == null || fen.Length == 0 ? ChessFen.StartFen : fen, board, state, out clock,
                    out number)) {
                return InputRejected;
            }
            SetStartPosition(clock, number);

            while (moveHistory.Length < count) GrowHistory();
            for (int i = 0; i < count; i++) {
                int move = moves[i];
                int mover = state[ChessPosition.StateSideToMove];
                ChessPosition.MakeMove(board, state, move);
                moveHistory[i] = move;
                notation[i] = sans[i] == null ? "" : sans[i];
                clockHistory[i] = clocks[i] < 0 ? -1 : clocks[i];
                halfmoveClocks[i] = halfmoveClock;
                halfmoveClock = ChessDraws.HalfmoveClockAfter(halfmoveClock, move);
                if (mover == ChessRules.Black) moveNumber++;
            }
            moveCount = count;

            mode = loadedMode;
            replaySourceWhite = white == null ? "" : white;
            replaySourceBlack = black == null ? "" : black;
            replaySourceResult = result == null ? "" : result;
            replaySourceBaseTime = loadedBaseTime;
            replaySourceIncrement = loadedBaseTime < 1 ? 0 : loadedIncrement;
            gameId = loadedGameId == null ? "" : loadedGameId;
            continuedFrom = "";

            int status = ChessMoves.PositionStatus(board, state,
                ChessMoves.GenerateLegalMoves(board, state, legalMoves, true), true);
            int resultWinner;
            if (status == ChessMoves.StatusCheckmate) {
                ending = ChessGame.EndingCheckmate;
                winner = ChessRules.OtherColor(state[ChessPosition.StateSideToMove]);
            } else if (status == ChessMoves.StatusStalemate) {
                ending = ChessGame.EndingStalemate;
                winner = ChessRules.NoColor;
            } else if (ChessPgn.ParseResult(replaySourceResult, out resultWinner)) {
                ending = ChessGame.EndingUnknown;
                winner = resultWinner;
            } else {
                ending = ChessGame.EndingNone;
                winner = ChessRules.NoColor;
            }

            if (IsValidTimeControl(replaySourceBaseTime, replaySourceIncrement)) {
                baseTime = replaySourceBaseTime;
                increment = replaySourceIncrement;
            } else {
                baseTime = ChessArbiter.NoClock;
                increment = 0;
            }
            whiteTime = 0;
            blackTime = 0;
            turnStart = 0;

            lifecycle = LifecycleLoaded;
            replayMode = true;
            ShowPly(moveCount);
            TakeSnapshot();
            NotifyChanged(ChangeLoaded);
            return InputAccepted;
        }

        public int ContinueGame() {
            return ContinueGameAs(LocalName());
        }

        // The "Continue from here" button (CanContinueAs): the seated players play the loaded game on from the ply the
        // replay shows, as a new game. Nothing is logged (the loaded game wasn't played here). The clock is the one
        // chosen (a forced one was applied by the snapshot, and can't be changed); the mode stays the loaded game's.
        // The game gets a new ID
        // referencing the loaded one at that ply (none when it had no ID), the history is cut there, and the clock
        // history before it is emptied, since the loaded game's clocks aren't this game's: a takeback to a ply before
        // it gives both sides the base time, as they had when continuing. Both clocks get the base time with the turn
        // starting now, and the game is in progress, out of replay, with no result and the loaded game's White,
        // Black, Result and time control dropped.
        public int ContinueGameAs(string name) {
            if (!CanContinueAs(name)) return InputRejected;
            int ply = replayIndex;
            ContinueGameIdAt(ply, NetworkDateTime());
            TruncateHistory(ply);
            for (int i = 0; i < ply; i++) clockHistory[i] = -1;
            int startTime = baseTime == ChessArbiter.NoClock ? 0 : baseTime * 1000;
            whiteTime = startTime;
            blackTime = startTime;
            turnStart = ServerTime();
            ending = ChessGame.EndingNone;
            winner = ChessRules.NoColor;
            replaySourceWhite = "";
            replaySourceBlack = "";
            replaySourceResult = "";
            replaySourceBaseTime = ChessArbiter.NoClock;
            replaySourceIncrement = 0;
            drawOfferColor = ChessRules.NoColor;
            lifecycle = LifecycleInGame;
            ShowPly(moveCount);
            replayMode = false;
            NotifyChanged(ChangeContinued);
            return InputAccepted;
        }

        // Doubles the room in the history arrays, keeping the moves; new notation entries are "" (a synced string[]
        // must never hold null) and new clock entries -1.
        void GrowHistory() {
            int oldLength = moveHistory.Length;
            int newLength = oldLength * 2;
            int[] newMoves = new int[newLength];
            string[] newNotation = new string[newLength];
            int[] newClocks = new int[newLength];
            int[] newHalfmoveClocks = new int[newLength];
            Array.Copy(moveHistory, newMoves, oldLength);
            Array.Copy(notation, newNotation, oldLength);
            Array.Copy(clockHistory, newClocks, oldLength);
            Array.Copy(halfmoveClocks, newHalfmoveClocks, oldLength);
            for (int i = oldLength; i < newLength; i++) {
                newNotation[i] = "";
                newClocks[i] = -1;
            }
            moveHistory = newMoves;
            notation = newNotation;
            clockHistory = newClocks;
            halfmoveClocks = newHalfmoveClocks;
        }
    }
}
