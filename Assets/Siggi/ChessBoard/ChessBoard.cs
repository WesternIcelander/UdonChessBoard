using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The logical board (docs/architecture.md): one board's game state, its lifecycle (docs/board-lifecycle.md) and
    // the rules applied to it. It never touches the world; ChessBoardView (step 9) owns the world objects and forwards
    // input here. Its state fields are the synced fields of docs/networking.md, kept as plain fields until they are
    // synced in step 8.3.
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

        // Room for this many plies in the history arrays at first; they grow as needed.
        public const int InitialHistoryCapacity = 128;

        // The arbiter whose settings this board snapshots, and this board's number for its per-board overrides
        // (docs/settings.md).
        [Tooltip("The ChessArbiter holding the rules settings this board uses.")]
        public ChessArbiter arbiter;
        [Tooltip("This board's number for per-board setting overrides: 0 to the arbiter's Board Count - 1. A number "
            + "outside that range always gets the instance defaults.")]
        public int boardNumber;

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

        void Start() {
            if (!Utilities.IsValid(arbiter)) {
                Debug.LogError("[ChessBoard] " + gameObject.name + ": the Arbiter field is empty. Set it to "
                    + "the world's ChessArbiter. Until then the board plays with every setting off: no takebacks, no "
                    + "draws, nothing forced, never reset for inactivity.");
            }
            ClearState();
        }

        // Puts every state field back to the reset state: the standard starting position, no game, empty seats.
        // The reset itself (who may, logging an unfinished game) comes in step 7.10.
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
            if (color != ChessRules.White && color != ChessRules.Black) return InputRejected;
            if (name == null || name.Length == 0) return InputRejected;
            if (lifecycle != LifecycleReset && lifecycle != LifecycleLobby && lifecycle != LifecycleLoaded) {
                return InputRejected;
            }
            string seated = color == ChessRules.White ? whitePlayer : blackPlayer;
            // == on strings is ordinal: no trimming, case folding or normalization (docs/ownership.md).
            if (seated.Length != 0) return seated == name ? InputAccepted : InputRejected;

            if (color == ChessRules.White) whitePlayer = name;
            else blackPlayer = name;
            if (lifecycle == LifecycleReset) {
                lifecycle = LifecycleLobby;
                TakeSnapshot();
            }
            return InputAccepted;
        }

        // Whether this display name has the seat of color.
        public bool IsSeated(int color, string name) {
            if (name == null || name.Length == 0) return false;
            string seated = color == ChessRules.White ? whitePlayer : color == ChessRules.Black ? blackPlayer : "";
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
        // takes one when it leaves the reset state). Taking ownership of the board and serializing come with step 8.
        public int ResnapshotAs(VRCPlayerApi who) {
            if (lifecycle == LifecycleReset) return InputRejected;
            if (!Utilities.IsValid(who) || !Utilities.IsValid(arbiter) || !arbiter.IsOrganizer(who)) {
                return InputRejected;
            }
            TakeSnapshot();
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

        // Chooses Standard or Chess960. Only in the lobby: a loaded game's mode is the game's own.
        public int SetModeAs(string name, int newMode) {
            if (lifecycle != LifecycleLobby) return InputRejected;
            if (!IsPlayer(name) || IsModeForced()) return InputRejected;
            if (newMode != ChessGame.ModeStandard && newMode != ChessGame.ModeChess960) return InputRejected;
            mode = newMode;
            return InputAccepted;
        }

        public int SetClock(int newBaseTime, int newIncrement) {
            return SetClockAs(LocalName(), newBaseTime, newIncrement);
        }

        // Chooses the time control (seconds; base ChessArbiter.NoClock with increment 0 for no clock). In the lobby
        // and on a loaded board, which continues with fresh clocks.
        public int SetClockAs(string name, int newBaseTime, int newIncrement) {
            if (lifecycle != LifecycleLobby && lifecycle != LifecycleLoaded) return InputRejected;
            if (!IsPlayer(name) || IsClockForced()) return InputRejected;
            if (!IsValidTimeControl(newBaseTime, newIncrement)) return InputRejected;
            baseTime = newBaseTime;
            increment = newIncrement;
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

        // The Start button: a seated player starts the game once both seats are filled. Forced settings are applied
        // first; a Chess960 game gets a random starting position (generated here, by the player pressing Start, and
        // synced like any other state). Both clocks get the base time and the white clock starts running.
        public int StartGameAs(string name) {
            if (lifecycle != LifecycleLobby) return InputRejected;
            if (whitePlayer.Length == 0 || blackPlayer.Length == 0) return InputRejected;
            if (!IsPlayer(name)) return InputRejected;

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
            lifecycle = LifecycleInGame;
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
        // client sees it; ending the game (time forfeit) is step 7.5a.
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
        // matching a drop to a move, are the input API's (step 7.4a); game endings are step 7.5.
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
