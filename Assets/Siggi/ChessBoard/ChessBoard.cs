using System;
using UdonSharp;
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
            if (lifecycle == LifecycleReset) lifecycle = LifecycleLobby;
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
            if (IsClockForced()) {
                baseTime = gameNumbers[ChessArbiter.NumberForcedBaseTime];
                increment = baseTime == ChessArbiter.NoClock ? 0 : gameNumbers[ChessArbiter.NumberForcedIncrement];
            }
        }

        // One of the 960 Chess960 starting positions at random (ChessGame.Chess960Position numbering).
        public int RandomChess960Index() {
            return UnityEngine.Random.Range(0, ChessGame.Chess960Count);
        }
    }
}
