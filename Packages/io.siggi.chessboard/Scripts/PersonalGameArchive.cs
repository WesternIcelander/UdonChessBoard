using System;
using System.Text.RegularExpressions;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The local player's own games (step 13.10, docs/archive.md), kept in their VRChat PlayerData across visits and
    // instances: the personal kind of GameArchive, shown on the personal history wall. Local only, nothing synced.
    // It keeps only the games with the local player's display name as White or Black (the board identifies players by
    // name), capacity of them: a new game goes into an empty slot, or once full over the oldest; a game it holds
    // already (by ID) is replaced in its slot.
    //
    // Each slot is one PlayerData string (keyPrefix + "game" + slot) holding the game's PGN as the board logged it,
    // after two tags only the saved copy has: PlyCount (the number of plies) and Ending (ChessGame.EndingName), which
    // the wall's list needs without reading the moves. PGN rather than packed moves: a standard our own changes can't
    // break, exported as it is, and loaded through the importer. One more key (keyPrefix + "capacity") holds the
    // capacity. PlayerData keys can't be deleted, so an empty slot (a deleted game's too) is "". The order (index 0
    // the oldest) is by Game ID, which starts with the game's start time in UTC; there are no sequence numbers.
    //
    // Nothing is read until OnPlayerRestored for the local player; until then GameCount is 0 and a game handed in
    // waits in memory, saved once restored. Games handed in this session also keep their packed moves and clocks in
    // memory (HasMovesAt), so loading one is a copy; games read from PlayerData have only their PGN until imported.
    // Other players can read a player's PlayerData, so the history isn't secret.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PersonalGameArchive : GameArchive {
        public const string DefaultKeyPrefix = "siggi.chess.personal.";
        // capacity's default, the Personal Game Archive prefab's (and so the entries the Personal History Wall
        // prefab has).
        public const int DefaultCapacity = 20;

        [Tooltip("How many of the local player's games are kept (the oldest goes once full). Lowered later, the games "
            + "saved past it are no longer read; raised, they're read again.")]
        public int capacity = DefaultCapacity;
        [Tooltip("The start of every PlayerData key the archive writes, so a world's own keys don't clash.")]
        public string keyPrefix = DefaultKeyPrefix;

        // Whether the local player's PlayerData has been read (OnPlayerRestored).
        [NonSerialized] public bool restored;
        // How many slots this client has written to PlayerData, for tests.
        [NonSerialized] public int saves;

        // Per slot (capacity of them): the saved text ("" for an empty slot), what the readers give (from the saved
        // text's tags), and the packed moves and clocks of a game handed in this session (null otherwise).
        string[] saved;
        string[] gameIds;
        string[] continuedFroms;
        string[] whites;
        string[] blacks;
        string[] startFens;
        string[] events;
        string[] sites;
        int[] modes;
        int[] endings;
        int[] winners;
        int[] baseTimes;
        int[] increments;
        int[] plies;
        int[][] slotMoves;
        int[][] slotClocks;
        // Games handed in before the PlayerData was restored: their saved text, moves and clocks, put in their slots
        // once it is.
        string[] pendingTexts = new string[0];
        int[][] pendingMoves = new int[0][];
        int[][] pendingClocks = new int[0][];

        // The filled slots by Game ID, oldest first; rebuilt when orderDirty.
        int[] order = new int[0];
        int orderCount;
        bool orderDirty = true;
        Regex tokenRegex;
        string[] tags;

        // The slots, made on first use (capacity is read then).
        void Setup() {
            if (saved != null) return;
            int count = Mathf.Max(0, capacity);
            saved = new string[count];
            gameIds = new string[count];
            continuedFroms = new string[count];
            whites = new string[count];
            blacks = new string[count];
            startFens = new string[count];
            events = new string[count];
            sites = new string[count];
            modes = new int[count];
            endings = new int[count];
            winners = new int[count];
            baseTimes = new int[count];
            increments = new int[count];
            plies = new int[count];
            slotMoves = new int[count][];
            slotClocks = new int[count][];
            for (int i = 0; i < count; i++) ClearSlot(i);
            order = new int[count];
            tokenRegex = new Regex(ChessPgn.TokenPattern);
            tags = new string[ChessPgn.TagCount];
        }

        public string _SlotKey(int slot) {
            return keyPrefix + "game" + slot;
        }

        public string _CapacityKey() {
            return keyPrefix + "capacity";
        }

        // ---- Restoring ----

        // The local player's PlayerData has arrived.
        public override void OnPlayerRestored(VRCPlayerApi player) {
            if (Utilities.IsValid(player) && player.isLocal) _Restore();
        }

        // Reads each slot's saved game from the local player's PlayerData (its tags only), then saves the games handed
        // in meanwhile. Public for tests, which read their own keys again (keyPrefix) after _Forget.
        public void _Restore() {
            VRCPlayerApi player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player)) return;
            Setup();
            int read = 0;
            for (int i = 0; i < saved.Length; i++) {
                string text;
                if (!PlayerData.TryGetString(player, _SlotKey(i), out text) || text == null || text.Length == 0) {
                    continue;
                }
                if (ReadSlot(i, text, null, null)) {
                    read++;
                } else {
                    Debug.LogWarning("[PersonalGameArchive] " + gameObject.name + ": slot " + i + " doesn't read as a "
                        + "saved game (no GameID tag); counted as empty.");
                }
            }
            restored = true;
            Debug.Log("[PersonalGameArchive] " + gameObject.name + ": restored " + read + " game(s) of "
                + player.displayName + ", " + pendingTexts.Length + " waiting");
            for (int i = 0; i < pendingTexts.Length; i++) Put(pendingTexts[i], pendingMoves[i], pendingClocks[i]);
            pendingTexts = new string[0];
            pendingMoves = new int[0][];
            pendingClocks = new int[0][];
            orderDirty = true;
            _Changed();
        }

        // ---- Adding ----

        // A game logged on this client: kept when the local player sat as White or Black, with its moves, clocks and
        // logged PGN (saved after its PlyCount and Ending tags), whose own Event and Site tags it reads like a saved
        // game's (eventName and site are the same values). Refused (false) for a game with no ID or no moves, or
        // without the local player.
        public override bool _AddGame(string gameId, string continuedFrom, string white, string black, int mode,
            int ending, int winner, string startFen, int baseTime, int increment, int[] moves, int[] clocks, int count,
            string pgn, string eventName, string site) {
            if (string.IsNullOrEmpty(gameId) || moves == null || clocks == null || count <= 0 || count > moves.Length
                || count > clocks.Length || pgn == null) {
                return false;
            }
            string local = ChessUtil._LocalName("");
            if (local.Length == 0 || (white != local && black != local)) return false;
            Setup();
            if (saved.Length == 0) return false;
            int[] keptMoves = new int[count];
            int[] keptClocks = new int[count];
            Array.Copy(moves, keptMoves, count);
            Array.Copy(clocks, keptClocks, count);
            string text = _SavedText(pgn, count, ending);
            if (!restored) {
                AddPending(text, keptMoves, keptClocks);
                return true;
            }
            Put(text, keptMoves, keptClocks);
            orderDirty = true;
            _Changed();
            return true;
        }

        // A slot's saved text: the logged PGN after its PlyCount and Ending tags (docs/pgn.md).
        public static string _SavedText(string pgn, int plyCount, int ending) {
            return "[PlyCount \"" + plyCount + "\"]\n[Ending \"" + ChessGame.EndingName(ending) + "\"]\n" + pgn;
        }

        void AddPending(string text, int[] moves, int[] clocks) {
            int count = pendingTexts.Length;
            string[] texts = new string[count + 1];
            int[][] movesGrown = new int[count + 1][];
            int[][] clocksGrown = new int[count + 1][];
            for (int i = 0; i < count; i++) {
                texts[i] = pendingTexts[i];
                movesGrown[i] = pendingMoves[i];
                clocksGrown[i] = pendingClocks[i];
            }
            texts[count] = text;
            movesGrown[count] = moves;
            clocksGrown[count] = clocks;
            pendingTexts = texts;
            pendingMoves = movesGrown;
            pendingClocks = clocksGrown;
        }

        // Puts a saved text (with its moves and clocks) in its slot, the game's own if held, else an empty one, else
        // the oldest's, and saves it.
        void Put(string text, int[] moves, int[] clocks) {
            ChessPgn._ReadTags(tokenRegex, text, tags);
            int slot = SlotFor(tags[ChessPgn.TagGameId]);
            if (slot < 0 || !ReadSlot(slot, text, moves, clocks)) return;
            Save(slot);
        }

        int SlotFor(string gameId) {
            int empty = -1;
            int oldest = -1;
            for (int i = 0; i < saved.Length; i++) {
                if (saved[i].Length == 0) {
                    if (empty < 0) empty = i;
                    continue;
                }
                if (gameIds[i] == gameId) return i;
                if (oldest < 0 || string.CompareOrdinal(gameIds[i], gameIds[oldest]) < 0) oldest = i;
            }
            return empty >= 0 ? empty : oldest;
        }

        // Fills slot from a saved text's tags, with the moves and clocks when given (null when only the PGN is known).
        // A text without a GameID leaves the slot empty: false.
        bool ReadSlot(int slot, string text, int[] moves, int[] clocks) {
            ChessPgn._ReadTags(tokenRegex, text, tags);
            if (tags[ChessPgn.TagGameId].Length == 0) {
                ClearSlot(slot);
                return false;
            }
            saved[slot] = text;
            gameIds[slot] = tags[ChessPgn.TagGameId];
            continuedFroms[slot] = tags[ChessPgn.TagContinuedFrom];
            whites[slot] = tags[ChessPgn.TagWhite];
            blacks[slot] = tags[ChessPgn.TagBlack];
            startFens[slot] = tags[ChessPgn.TagFen].Length > 0 ? tags[ChessPgn.TagFen] : ChessFen.StartFen;
            events[slot] = ChessPgn._KnownTag(tags[ChessPgn.TagEvent]);
            sites[slot] = ChessPgn._KnownTag(tags[ChessPgn.TagSite]);
            modes[slot] = ChessPgn._ModeFromVariant(tags[ChessPgn.TagVariant]);
            int winner;
            bool decided = ChessPgn._ParseResult(tags[ChessPgn.TagResult], out winner);
            int ending = ChessGame.EndingFromName(tags[ChessPgn.TagEnding]);
            if (ending < 0) ending = decided ? ChessGame.EndingUnknown : ChessGame.EndingUnterminated;
            endings[slot] = ending;
            winners[slot] = winner;
            int baseSeconds;
            int incrementSeconds;
            if (ChessPgn._ParseTimeControl(tags[ChessPgn.TagTimeControl], out baseSeconds, out incrementSeconds)) {
                baseTimes[slot] = baseSeconds == 0 ? ChessArbiter.NoClock : baseSeconds;
                increments[slot] = incrementSeconds;
            } else {
                baseTimes[slot] = ChessBoard.UnknownClock;
                increments[slot] = 0;
            }
            int count = ChessFen._ParseCount(tags[ChessPgn.TagPlyCount]);
            plies[slot] = count < 0 ? 0 : count;
            bool kept = moves != null && clocks != null && moves.Length >= plies[slot] && clocks.Length >= plies[slot];
            slotMoves[slot] = kept ? moves : null;
            slotClocks[slot] = kept ? clocks : null;
            orderDirty = true;
            return true;
        }

        void ClearSlot(int slot) {
            saved[slot] = "";
            gameIds[slot] = "";
            continuedFroms[slot] = "";
            whites[slot] = "";
            blacks[slot] = "";
            startFens[slot] = "";
            events[slot] = "";
            sites[slot] = "";
            modes[slot] = ChessGame.ModeStandard;
            endings[slot] = ChessGame.EndingNone;
            winners[slot] = ChessRules.NoColor;
            baseTimes[slot] = ChessArbiter.NoClock;
            increments[slot] = 0;
            plies[slot] = 0;
            slotMoves[slot] = null;
            slotClocks[slot] = null;
            orderDirty = true;
        }

        // Writes slot to the local player's PlayerData, with the capacity, logs it, and asks for the storage used,
        // which OnPersistenceUsageUpdated logs once it's known (the client reports 0 until asked).
        void Save(int slot) {
            PlayerData.SetString(_SlotKey(slot), saved[slot]);
            PlayerData.SetInt(_CapacityKey(), capacity);
            saves++;
            Debug.Log("[PersonalGameArchive] " + gameObject.name + ": slot " + slot + " saved ("
                + (saved[slot].Length == 0 ? "empty" : gameIds[slot]) + ")");
            usageAsked = true;
            Networking.RequestStorageUsageUpdate();
        }

        // The storage used, as last reported after a save (-1 before any), for logs and the debug view.
        [NonSerialized] public int storageUsed = -1;
        bool usageAsked;

        // The storage used is known (asked for by Save): logged once per request.
        public override void OnPersistenceUsageUpdated() {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!usageAsked || !Utilities.IsValid(local)) return;
            usageAsked = false;
            storageUsed = Networking.GetPlayerDataStorageUsage(local);
            Debug.Log("[PersonalGameArchive] " + gameObject.name + ": PlayerData uses " + storageUsed + " of "
                + Networking.GetPlayerDataStorageLimit() + " bytes, " + GameCount() + " game(s) held");
        }

        // ---- Deleting ----

        public override bool CanDelete() {
            return true;
        }

        // Empties the game's slot (saved as "", since PlayerData keys can't be deleted). False for a game not held,
        // or before the PlayerData is restored.
        public override bool DeleteGame(string gameId) {
            if (!restored || string.IsNullOrEmpty(gameId)) return false;
            for (int i = 0; i < saved.Length; i++) {
                if (saved[i].Length == 0 || gameIds[i] != gameId) continue;
                ClearSlot(i);
                Save(i);
                _Changed();
                return true;
            }
            return false;
        }

        // ---- Reading, oldest first ----

        public override int Capacity() {
            return Mathf.Max(0, capacity);
        }

        // 0 until the PlayerData is restored.
        public override int GameCount() {
            if (!restored) return 0;
            UpdateOrder();
            return orderCount;
        }

        int SlotAt(int index) {
            UpdateOrder();
            return order[index];
        }

        public override string GameIdAt(int index) {
            return gameIds[SlotAt(index)];
        }

        public override string ContinuedFromAt(int index) {
            return continuedFroms[SlotAt(index)];
        }

        public override string WhiteAt(int index) {
            return whites[SlotAt(index)];
        }

        public override string BlackAt(int index) {
            return blacks[SlotAt(index)];
        }

        public override string StartFenAt(int index) {
            return startFens[SlotAt(index)];
        }

        public override string EventAt(int index) {
            return events[SlotAt(index)];
        }

        public override string SiteAt(int index) {
            return sites[SlotAt(index)];
        }

        public override int ModeAt(int index) {
            return modes[SlotAt(index)];
        }

        public override int EndingAt(int index) {
            return endings[SlotAt(index)];
        }

        public override int WinnerAt(int index) {
            return winners[SlotAt(index)];
        }

        public override int BaseTimeAt(int index) {
            return baseTimes[SlotAt(index)];
        }

        public override int IncrementAt(int index) {
            return increments[SlotAt(index)];
        }

        public override int MoveCountAt(int index) {
            return plies[SlotAt(index)];
        }

        public override bool HasMovesAt(int index) {
            if (index < 0 || index >= GameCount()) return false;
            return slotMoves[SlotAt(index)] != null;
        }

        public override void CopyMovesAt(int index, int[] destination) {
            int slot = SlotAt(index);
            if (slotMoves[slot] != null) Array.Copy(slotMoves[slot], destination, plies[slot]);
        }

        public override void CopyClocksAt(int index, int[] destination) {
            int slot = SlotAt(index);
            if (slotClocks[slot] != null) Array.Copy(slotClocks[slot], destination, plies[slot]);
        }

        // The PGN as the board logged it: the saved text without its first two lines, the PlyCount and Ending tags the
        // archive put there (no regex: they're always first, in this order).
        public override string PgnAt(int index) {
            return _LoggedPgn(saved[SlotAt(index)]);
        }

        // The saved text, tags and all: an import reads the Ending tag.
        public override string _LoadPgnAt(int index) {
            return saved[SlotAt(index)];
        }

        // A saved text with its PlyCount and Ending lines stripped (unchanged if it doesn't start with them).
        public static string _LoggedPgn(string text) {
            if (text == null || !text.StartsWith("[PlyCount \"")) return text == null ? "" : text;
            int first = text.IndexOf('\n');
            if (first < 0 || first + 1 >= text.Length || text.IndexOf("[Ending \"", first + 1) != first + 1) return text;
            int second = text.IndexOf('\n', first + 1);
            return second < 0 ? "" : text.Substring(second + 1);
        }

        // Rebuilds order: every filled slot, sorted by Game ID (an insertion sort; there are few).
        void UpdateOrder() {
            if (!orderDirty) return;
            orderDirty = false;
            Setup();
            orderCount = 0;
            for (int s = 0; s < saved.Length; s++) {
                if (saved[s].Length == 0) continue;
                int at = orderCount;
                while (at > 0 && string.CompareOrdinal(gameIds[order[at - 1]], gameIds[s]) > 0) {
                    order[at] = order[at - 1];
                    at--;
                }
                order[at] = s;
                orderCount++;
            }
        }

        // For tests: empties every slot and saves it so (""). Only once restored; returns whether it did.
        public bool _ClearSaved() {
            if (!restored) return false;
            for (int i = 0; i < saved.Length; i++) {
                ClearSlot(i);
                Save(i);
            }
            _Changed();
            return true;
        }

        // For tests: forgets every game in memory (nothing is written), as if the PlayerData hadn't arrived yet, and
        // makes the slots again for the capacity now.
        public void _Forget() {
            saved = null;
            Setup();
            restored = false;
            pendingTexts = new string[0];
            pendingMoves = new int[0][];
            pendingClocks = new int[0][];
            orderDirty = true;
            _Changed();
        }
    }
}
