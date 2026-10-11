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
        // A drop matches a promotion: nothing is played until the view asks which piece and calls PromotionChosen.
        public const int InputPromotionNeeded = 2;

        // The sides a king can castle to through the castle drop space (docs/player-input.md): king side is the rook
        // right of the king, queen side the one left of it, as in Chess960.
        public const int CastleKingSide = 0;
        public const int CastleQueenSide = 1;

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
            if (!CanJoinAs(color, name)) return IsSeated(color, name) && SeatsOpen() ? InputAccepted : InputRejected;

            if (color == ChessRules.White) whitePlayer = name;
            else blackPlayer = name;
            if (lifecycle == LifecycleReset) {
                lifecycle = LifecycleLobby;
                TakeSnapshot();
            }
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
            return InputAccepted;
        }

        // The draw the current position ends the game with by itself (docs/game-endings.md, Draw rules), checked
        // after each move that doesn't checkmate or stalemate: ChessGame.EndingFivefoldRepetition when the position has
        // occurred five times, EndingSeventyFiveMoveRule when seventy-five moves each have passed without a capture or
        // pawn move, EndingInsufficientMaterial when neither side can checkmate, each only while the snapshot has that
        // rule on; otherwise EndingNone (always under "No draws").
        public int AutomaticDraw() {
            if (IsGameSettingOn(ChessArbiter.SettingFivefoldRepetition)) {
                int count = ChessDraws.CountRepetitions(board, state, moveHistory, moveCount, halfmoveClock,
                    repetitionBoard, repetitionState, legalMoves, true);
                if (ChessDraws.IsFivefoldRepetition(count)) return ChessGame.EndingFivefoldRepetition;
            }
            if (IsGameSettingOn(ChessArbiter.SettingSeventyFiveMoveRule)
                && ChessDraws.IsSeventyFiveMoveRule(halfmoveClock)) {
                return ChessGame.EndingSeventyFiveMoveRule;
            }
            if (IsGameSettingOn(ChessArbiter.SettingDeadPosition) && ChessDraws.IsInsufficientMaterial(board)) {
                return ChessGame.EndingInsufficientMaterial;
            }
            return ChessGame.EndingNone;
        }

        // ---- Game endings (docs/game-endings.md) ----

        // The owner watches for flag fall. Ownership comes with step 8; until then every client is the owner.
        void Update() {
            if (lifecycle == LifecycleInGame) CheckFlagFall();
        }

        // Ends the game in progress at server time now: how it ended, who won (ChessRules.NoColor for a draw), and
        // the ended state. A running clock stops with the side to move's remaining time stored, so it keeps showing
        // what was left. A pending draw offer lapses; a takeback request stays, since an ended game can still be
        // continued. Logging the PGN on this transition is step 7.7.
        void EndGameAt(int newEnding, int newWinner, double now) {
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
        }

        public int Resign(int color) {
            return ResignAs(color, LocalName());
        }

        // The resign button: name resigns for color (CanResignAs), and the other side wins.
        public int ResignAs(int color, string name) {
            if (!CanResignAs(color, name)) return InputRejected;
            EndGameAt(ChessGame.EndingResignation, ChessRules.OtherColor(color), ServerTime());
            return InputAccepted;
        }

        public bool CheckFlagFall() {
            return CheckFlagFallAt(ServerTime());
        }

        // Ends the game on time forfeit if the side to move's flag has fallen at server time now, and returns whether
        // it did. The side out of time loses, unless its opponent can't checkmate (ChessDraws.HasNoMatingMaterial) and
        // the snapshot's dead-position rule is on, which makes it a draw; under "No draws" it always loses.
        public bool CheckFlagFallAt(double now) {
            int flagged = FlaggedColorAt(now);
            if (flagged == ChessRules.NoColor) return false;
            int opponent = ChessRules.OtherColor(flagged);
            bool draw = IsGameSettingOn(ChessArbiter.SettingDeadPosition)
                && ChessDraws.HasNoMatingMaterial(board, opponent);
            EndGameAt(ChessGame.EndingTimeForfeit, draw ? ChessRules.NoColor : opponent, now);
            return true;
        }

        // ---- Piece input (docs/player-input.md) ----
        // The view forwards pickups and drops as squares; the board decides. A drop is matched against the legal
        // moves by from and to square. One match is played; the four promotions of one pawn move answer
        // InputPromotionNeeded, and the view calls PromotionChosen with the same squares and the chosen piece. Nothing
        // is remembered in between, so there's nothing to sync or clear. A king castles by being dropped on its own
        // rook, on its castling destination when no other king move ends there, or on the castle drop space (a view
        // object, forwarded as KingDroppedOnCastleSpace). Free moves in replay are step 7.8a, activity timestamps
        // 8.5. The *As methods take any name, for tests and the debug view.

        public int PiecePickedUp(int square) {
            return PiecePickedUpAs(LocalName(), square);
        }

        // Whether name may pick up the piece on square: a game is in progress and not in replay, the side to move
        // has time left, name holds that side's seat and the piece is that side's. Rejected, the view puts it back.
        public int PiecePickedUpAs(string name, int square) {
            if (!MayMove(name)) return InputRejected;
            if (square < 0 || square >= 64) return InputRejected;
            int piece = board[square];
            if (piece == ChessRules.Empty
                || ChessRules.PieceColorOf(piece) != state[ChessPosition.StateSideToMove]) {
                return InputRejected;
            }
            return InputAccepted;
        }

        public int PieceDropped(int from, int to) {
            return PieceDroppedAs(LocalName(), from, to);
        }

        // name dropped the piece from from on to. Plays the legal move it matches, answers InputPromotionNeeded for a
        // promotion (nothing played), and rejects anything else, including a drop back on its own square. A king
        // dropped on its own rook castles with it; dropped on its castling destination it castles unless a normal king
        // move ends there too (Chess960 castles moving the king one square), which is then played instead.
        public int PieceDroppedAs(string name, int from, int to) {
            return DropAs(name, from, to, ChessRules.NoPieceType);
        }

        public int PromotionChosen(int from, int to, int pieceType) {
            return PromotionChosenAs(LocalName(), from, to, pieceType);
        }

        // The follow-up to InputPromotionNeeded: plays the promotion from-to to pieceType (ChessRules.Queen, Rook,
        // Bishop or Knight). Rejected for any other type and for a move that isn't a promotion.
        public int PromotionChosenAs(string name, int from, int to, int pieceType) {
            if (pieceType != ChessRules.Queen && pieceType != ChessRules.Rook && pieceType != ChessRules.Bishop
                && pieceType != ChessRules.Knight) {
                return InputRejected;
            }
            return DropAs(name, from, to, pieceType);
        }

        public int KingDroppedOnCastleSpace(int side) {
            return KingDroppedOnCastleSpaceAs(LocalName(), side);
        }

        // name dropped the king on the castle drop space of side (CastleKingSide or CastleQueenSide): plays the side to
        // move's castle on that side if it's legal. The view shows the space only when IsCastleDropSpaceNeeded, but the
        // castle is played either way.
        public int KingDroppedOnCastleSpaceAs(string name, int side) {
            if (!MayMove(name)) return InputRejected;
            int legalCount = ChessMoves.GenerateLegalMoves(board, state, legalMoves, true);
            int castle = FindCastle(legalCount, side);
            if (castle == 0) return InputRejected;
            return PlayMove(castle);
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
        // for tests and the debug view. Taking ownership, and the idle-reset exception for everyone else, come with step 8.

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
        // seated, and the replay showing a position of the game's history (not custom). Unlike a takeback it can
        // continue from the last position too, since nobody has played the loaded game on.
        public bool CanContinueAs(string name) {
            if (lifecycle != LifecycleLoaded || !replayMode || replayCustom) return false;
            if (replayIndex < 0 || replayIndex > moveCount) return false;
            return whitePlayer.Length != 0 && blackPlayer.Length != 0 && IsPlayer(name);
        }

        public bool CanReset() {
            return CanResetAs(LocalName(), LocalIsOrganizer());
        }

        // Whether name may reset the board (docs/board-lifecycle.md, Reset): a board that isn't reset already, by a
        // seated player or an organizer. Everyone else once the board has been idle long enough comes with step 8.5.
        public bool CanResetAs(string name, bool organizer) {
            return lifecycle != LifecycleReset && (organizer || IsPlayer(name));
        }

        // Whether a game can be loaded (a PGN import, or from the archive): only onto a reset board, by anyone.
        public bool CanLoadGame() {
            return lifecycle == LifecycleReset;
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
