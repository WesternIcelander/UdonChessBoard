using System;
using UdonSharp;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // An archive of played games (docs/archive.md), the base of both kinds: the instance's (InstanceGameArchive,
    // synced, every game played in the instance) and the local player's own (PersonalGameArchive, in their PlayerData).
    // Udon has no interfaces, so this is an abstract behaviour with virtual methods, as ChessBoardListener is: the
    // arbiter, the boards and the history walls hold either kind through a field of this type. Reading gives the games
    // oldest first. It also holds what both kinds share: the walls shown on it, told of every change, and the arbiter
    // (set by the arbiter on its Start, _SetArbiter), whose pick it clears when the picked game leaves it.
    public abstract class GameArchive : UdonSharpBehaviour {
        // Counts every change to the games held, for views and tests.
        [NonSerialized] public int changes;
        // The history walls showing this archive (each adds itself on Start), told of every change (_OnArchiveChanged)
        // and of every change to the arbiter's pick (_OnSelectionChanged).
        [NonSerialized] public GameHistoryWall[] walls = new GameHistoryWall[0];
        // The arbiter listing this archive in its archives, set by it on its Start; null until then or without one.
        [NonSerialized] public ChessArbiter arbiter;

        // ---- Reading, oldest first (index 0 the oldest game) ----

        // How many games the archive can hold.
        public virtual int Capacity() {
            return 0;
        }

        public virtual int GameCount() {
            return 0;
        }

        // The index (oldest first) of the game with this ID, -1 when the archive doesn't hold it.
        public virtual int IndexOf(string gameId) {
            if (gameId == null || gameId.Length == 0) return -1;
            int count = GameCount();
            for (int i = 0; i < count; i++) {
                if (GameIdAt(i) == gameId) return i;
            }
            return -1;
        }

        public virtual string GameIdAt(int index) {
            return "";
        }

        public virtual string ContinuedFromAt(int index) {
            return "";
        }

        public virtual string WhiteAt(int index) {
            return "";
        }

        public virtual string BlackAt(int index) {
            return "";
        }

        public virtual string StartFenAt(int index) {
            return "";
        }

        // The PGN Event and Site tags the game was played with (step 14.13), "" for none (written "?").
        public virtual string EventAt(int index) {
            return "";
        }

        public virtual string SiteAt(int index) {
            return "";
        }

        // ChessGame.Mode*.
        public virtual int ModeAt(int index) {
            return ChessGame.ModeStandard;
        }

        // ChessGame.Ending*, and the winner as a ChessRules colour (NoColor for none).
        public virtual int EndingAt(int index) {
            return ChessGame.EndingNone;
        }

        public virtual int WinnerAt(int index) {
            return ChessRules.NoColor;
        }

        // The time control in seconds: base ChessArbiter.NoClock for no clock (ChessBoard.UnknownClock unknown).
        public virtual int BaseTimeAt(int index) {
            return ChessArbiter.NoClock;
        }

        public virtual int IncrementAt(int index) {
            return 0;
        }

        // The number of plies.
        public virtual int MoveCountAt(int index) {
            return 0;
        }

        // The order the game was archived in, from 1; 0 on an archive without sequence numbers (the personal one),
        // whose wall shows the game's date instead.
        public virtual int _SequenceAt(int index) {
            return 0;
        }

        // Whether CopyMovesAt and CopyClocksAt can give the game's moves and clocks (always on the instance's archive;
        // on the personal one only for games handed in this session).
        public virtual bool HasMovesAt(int index) {
            return false;
        }

        // Copies the index-th game's moves (packed) or clocks (milliseconds, -1 unknown) into destination, which needs
        // room for MoveCountAt(index). Only when HasMovesAt(index).
        public virtual void CopyMovesAt(int index, int[] destination) {
        }

        public virtual void CopyClocksAt(int index, int[] destination) {
        }

        // The game's PGN as the board logged it, when the archive keeps it; "" otherwise (the instance's archive).
        public virtual string PgnAt(int index) {
            return "";
        }

        // The text a board imports to load the game when HasMovesAt is false: PgnAt, unless the archive saves more
        // with it (the personal archive's PlyCount and Ending tags, which the import reads).
        public virtual string _LoadPgnAt(int index) {
            return PgnAt(index);
        }

        // ---- Changing ----

        // A game logged by a board on this client (ChessBoard.LogGame hands every game to every archive on the
        // arbiter): its Game ID, ContinuedFrom ("" for none), players, mode (ChessGame.Mode*), ending
        // (ChessGame.Ending*) and winner (ChessRules colour, NoColor for none), starting FEN, time control (seconds,
        // base ChessArbiter.NoClock for no clock), the first count of its moves (packed) and clocks (milliseconds, -1
        // unknown), the PGN the board logged, and the PGN Event and Site tags it was played with (step 14.13, "" for
        // none). Each kind keeps what it wants; returns whether it kept the game.
        public virtual bool _AddGame(string gameId, string continuedFrom, string white, string black, int mode,
            int ending, int winner, string startFen, int baseTime, int increment, int[] moves, int[] clocks, int count,
            string pgn, string eventName, string site) {
            return false;
        }

        // Whether the local player may delete games from this archive (only from their own).
        public virtual bool CanDelete() {
            return false;
        }

        // Deletes the game with this ID; returns whether it did.
        public virtual bool DeleteGame(string gameId) {
            return false;
        }

        // ---- Telling ----

        // The arbiter introduces itself on its Start.
        public void _SetArbiter(ChessArbiter value) {
            arbiter = value;
        }

        // Tells wall of every change from now on (each history wall adds itself on Start); adding one twice changes
        // nothing.
        public void _AddWall(GameHistoryWall wall) {
            if (!Utilities.IsValid(wall)) return;
            int count = walls.Length;
            for (int i = 0; i < count; i++) {
                if (walls[i] == wall) return;
            }
            GameHistoryWall[] grown = new GameHistoryWall[count + 1];
            for (int i = 0; i < count; i++) grown[i] = walls[i];
            grown[count] = wall;
            walls = grown;
        }

        // The games held changed: counted, the arbiter's pick cleared when it was a game of this archive that's gone
        // (deleted, or overwritten as the oldest), and every wall told. Each kind calls it on every change.
        public void _Changed() {
            changes++;
            if (Utilities.IsValid(arbiter) && arbiter.selectedArchive == this && IndexOf(arbiter.selectedGameId) < 0) {
                arbiter._ClearSelection();
            }
            for (int i = 0; i < walls.Length; i++) {
                if (Utilities.IsValid(walls[i])) walls[i]._OnArchiveChanged();
            }
        }

        // The arbiter changed (ChessArbiter.Change*): on a change to the pick, every wall redraws which entry is
        // picked.
        public void _OnArbiterChanged(int change) {
            if (change != ChessArbiter.ChangeSelection) return;
            for (int i = 0; i < walls.Length; i++) {
                if (Utilities.IsValid(walls[i])) walls[i]._OnSelectionChanged();
            }
        }
    }
}
