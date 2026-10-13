using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The instance's archive of played games (docs/archive.md): a circular log of the last games logged, in holders
    // of GameArchiveHolder.Capacity games each. Every board on every client hands each game it logs here (AddGame).
    // Only the archive's owner writes to the holders, so additions never race; every client also keeps its own last
    // games in a local-only holder (recent), and whoever owns the archive adds those missing from it, so a game
    // ending as the owner leaves isn't lost. Reading gives the games oldest first. Holds no board and touches no
    // world objects; the history wall (step 10.2) is a view of it.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class GameArchive : UdonSharpBehaviour {
        // The synced holders, in slot order: the archive holds holders.Length * GameArchiveHolder.Capacity games.
        [Tooltip("The synced holders of archived games (children of this object).")]
        public GameArchiveHolder[] holders = new GameArchiveHolder[0];
        // This client's last games logged, never synced (its localOnly set).
        [Tooltip("The local-only holder of this client's recent games.")]
        public GameArchiveHolder recent;

        // Counts every change to the archived games (an addition here or a holder update arriving), for views.
        [NonSerialized] public int changes;
        // For tests (the debug view's Hold archive): while set, this client archives nothing, even as the owner, and
        // the games it's handed wait in the recent games, as if the owner had left before archiving them.
        [NonSerialized] public bool held;

        // The holders' slots (holder * Capacity + slot) holding games, oldest first; rebuilt when orderDirty.
        int[] order = new int[0];
        int orderCount;
        bool orderDirty = true;
        // Scratch for copying a game from the recent holder.
        int[] scratchMoves = new int[256];
        int[] scratchClocks = new int[256];

        void Start() {
            for (int i = 0; i < holders.Length; i++) {
                if (!Utilities.IsValid(holders[i])) {
                    Debug.LogError("[GameArchive] " + gameObject.name + ": holder " + i + " is missing. Run Tools > "
                        + "Chess Board > Create Game Archive again.");
                }
            }
            if (!Utilities.IsValid(recent) || !recent.localOnly) {
                Debug.LogError("[GameArchive] " + gameObject.name + ": the recent holder is missing or not local "
                    + "only. Run Tools > Chess Board > Create Game Archive again.");
            }
        }

        public int Capacity() {
            return holders.Length * GameArchiveHolder.Capacity;
        }

        // ---- Adding ----

        // A game logged by a board on this client: its Game ID, ContinuedFrom ("" for none), players, mode
        // (ChessGame.Mode*), ending (ChessGame.Ending*) and winner (ChessRules colour, NoColor for none), starting
        // FEN, time control (seconds, base ChessArbiter.NoClock for no clock) and the first count of its moves
        // (packed) and clocks (milliseconds, -1 unknown). Kept in the recent games, then archived if this client owns
        // the archive (Flush). Returns false for a game with no ID or no moves, or one already in the recent games.
        public bool AddGame(string gameId, string continuedFrom, string white, string black, int mode, int ending,
            int winner, string startFen, int baseTime, int increment, int[] moves, int[] clocks, int count) {
            if (!StoreRecent(gameId, continuedFrom, white, black, mode, ending, winner, startFen, baseTime, increment,
                    moves, clocks, count)) {
                return false;
            }
            Flush();
            return true;
        }

        // AddGame without archiving: the game goes only into the recent games, replacing the oldest when they're
        // full. Public for tests, which use it as a client that doesn't own the archive.
        public bool StoreRecent(string gameId, string continuedFrom, string white, string black, int mode,
            int ending, int winner, string startFen, int baseTime, int increment, int[] moves, int[] clocks,
            int count) {
            if (!Utilities.IsValid(recent) || string.IsNullOrEmpty(gameId) || moves == null || clocks == null
                || count <= 0 || count > moves.Length || count > clocks.Length) {
                return false;
            }
            if (recent.SlotOf(gameId) >= 0) return false;
            int slot = OldestSlot(recent);
            recent.WriteSlot(slot, HighestSequence(recent) + 1, gameId, continuedFrom, white, black, mode, ending,
                winner, startFen, baseTime, increment, moves, clocks, count);
            return true;
        }

        // On the archive's owner, once every holder is ready (and unless held): archives each recent game, oldest
        // first, that no holder has. Returns how many were added.
        public int Flush() {
            if (held || !Utilities.IsValid(recent) || !Networking.IsOwner(gameObject) || !AllReady()) return 0;
            int added = 0;
            int last = 0;
            // The recent games by sequence, oldest first.
            while (true) {
                int next = -1;
                for (int i = 0; i < GameArchiveHolder.Capacity; i++) {
                    int sequence = recent.sequences[i];
                    if (sequence > last && (next < 0 || sequence < recent.sequences[next])) next = i;
                }
                if (next < 0) break;
                last = recent.sequences[next];
                if (!IsArchived(recent.gameIds[next]) && Archive(next)) added++;
            }
            return added;
        }

        // Whether every holder has the instance's games (GameArchiveHolder.ready).
        public bool AllReady() {
            for (int i = 0; i < holders.Length; i++) {
                if (!Utilities.IsValid(holders[i]) || !holders[i].ready) return false;
            }
            return true;
        }

        public bool IsArchived(string gameId) {
            for (int i = 0; i < holders.Length; i++) {
                if (Utilities.IsValid(holders[i]) && holders[i].SlotOf(gameId) >= 0) return true;
            }
            return false;
        }

        // Archives the recent game in recentSlot: the next sequence, in the slot after the newest one's (the oldest
        // game's once the archive is full), sending only that holder.
        bool Archive(int recentSlot) {
            int capacity = Capacity();
            if (capacity == 0) return false;
            int sequence = 0;
            for (int i = 0; i < holders.Length; i++) {
                int highest = HighestSequence(holders[i]);
                if (highest > sequence) sequence = highest;
            }
            sequence++;
            int slot = (sequence - 1) % capacity;
            GameArchiveHolder holder = holders[slot / GameArchiveHolder.Capacity];
            int holderSlot = slot % GameArchiveHolder.Capacity;

            int count = recent.MoveCount(recentSlot);
            if (scratchMoves.Length < count) {
                scratchMoves = new int[count];
                scratchClocks = new int[count];
            }
            recent.CopyMoves(recentSlot, scratchMoves);
            recent.CopyClocks(recentSlot, scratchClocks);
            holder.WriteSlot(holderSlot, sequence, recent.gameIds[recentSlot], recent.continuedFroms[recentSlot],
                recent.whites[recentSlot], recent.blacks[recentSlot],
                recent.Info(recentSlot, GameArchiveHolder.InfoMode),
                recent.Info(recentSlot, GameArchiveHolder.InfoEnding),
                recent.Info(recentSlot, GameArchiveHolder.InfoWinner), recent.startFens[recentSlot],
                recent.Info(recentSlot, GameArchiveHolder.InfoBaseTime),
                recent.Info(recentSlot, GameArchiveHolder.InfoIncrement), scratchMoves, scratchClocks, count);
            holder.Send();
            Debug.Log("[GameArchive] archived game " + sequence + ", " + recent.gameIds[recentSlot] + " ("
                + recent.whites[recentSlot] + " vs " + recent.blacks[recentSlot] + "), in " + holder.gameObject.name
                + " slot " + holderSlot);
            // Not OnHolderChanged: it flushes, and Udon methods mustn't recurse.
            orderDirty = true;
            changes++;
            return true;
        }

        static int HighestSequence(GameArchiveHolder holder) {
            int highest = 0;
            if (!Utilities.IsValid(holder)) return highest;
            for (int i = 0; i < GameArchiveHolder.Capacity; i++) {
                if (holder.sequences[i] > highest) highest = holder.sequences[i];
            }
            return highest;
        }

        // An empty slot of holder, or else the one with the oldest game.
        static int OldestSlot(GameArchiveHolder holder) {
            int oldest = 0;
            for (int i = 0; i < GameArchiveHolder.Capacity; i++) {
                if (holder.sequences[i] == 0) return i;
                if (holder.sequences[i] < holder.sequences[oldest]) oldest = i;
            }
            return oldest;
        }

        // A holder's games changed: an update arrived or this client wrote one. Waiting recent games may now go in.
        public void OnHolderChanged() {
            orderDirty = true;
            changes++;
            Flush();
        }

        // The previous owner left (nobody asks for the archive): this client's recent games missing from the archive go
        // in, perhaps a game that ended as the owner left. Logged, so the clients' logs show the handover.
        public override void OnOwnershipTransferred(VRCPlayerApi player) {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            int added = Flush();
            Debug.Log("[GameArchive] " + gameObject.name + " now owned here: " + added + " recent game(s) added"
                + (AllReady() ? "" : ", waiting for holders") + (held ? ", held" : ""));
        }

        // Nobody asks for the archive: VRChat hands it on when its owner leaves.
        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) {
            return false;
        }

        // Empties every holder and the recent games, for tests. Only the archive's owner; returns whether it did.
        public bool ClearArchive() {
            if (!Networking.IsOwner(gameObject)) return false;
            for (int i = 0; i < holders.Length; i++) {
                if (!Utilities.IsValid(holders[i])) continue;
                holders[i].Clear();
                holders[i].Send();
            }
            if (Utilities.IsValid(recent)) recent.Clear();
            orderDirty = true;
            changes++;
            return true;
        }

        // ---- Reading, oldest first ----

        public int GameCount() {
            UpdateOrder();
            return orderCount;
        }

        // The holder slot (holder * Capacity + slot) of the index-th game, oldest first.
        public int SlotAt(int index) {
            UpdateOrder();
            return order[index];
        }

        GameArchiveHolder HolderAt(int index) {
            return holders[SlotAt(index) / GameArchiveHolder.Capacity];
        }

        int HolderSlotAt(int index) {
            return SlotAt(index) % GameArchiveHolder.Capacity;
        }

        public int SequenceAt(int index) {
            return HolderAt(index).sequences[HolderSlotAt(index)];
        }

        public string GameIdAt(int index) {
            return Text(HolderAt(index).gameIds[HolderSlotAt(index)]);
        }

        public string ContinuedFromAt(int index) {
            return Text(HolderAt(index).continuedFroms[HolderSlotAt(index)]);
        }

        public string WhiteAt(int index) {
            return Text(HolderAt(index).whites[HolderSlotAt(index)]);
        }

        public string BlackAt(int index) {
            return Text(HolderAt(index).blacks[HolderSlotAt(index)]);
        }

        public string StartFenAt(int index) {
            return Text(HolderAt(index).startFens[HolderSlotAt(index)]);
        }

        public int ModeAt(int index) {
            return HolderAt(index).Info(HolderSlotAt(index), GameArchiveHolder.InfoMode);
        }

        public int EndingAt(int index) {
            return HolderAt(index).Info(HolderSlotAt(index), GameArchiveHolder.InfoEnding);
        }

        public int WinnerAt(int index) {
            return HolderAt(index).Info(HolderSlotAt(index), GameArchiveHolder.InfoWinner);
        }

        public int BaseTimeAt(int index) {
            return HolderAt(index).Info(HolderSlotAt(index), GameArchiveHolder.InfoBaseTime);
        }

        public int IncrementAt(int index) {
            return HolderAt(index).Info(HolderSlotAt(index), GameArchiveHolder.InfoIncrement);
        }

        public int MoveCountAt(int index) {
            return HolderAt(index).MoveCount(HolderSlotAt(index));
        }

        // Copies the index-th game's moves (or clocks) into destination, which needs room for MoveCountAt(index).
        public void CopyMovesAt(int index, int[] destination) {
            HolderAt(index).CopyMoves(HolderSlotAt(index), destination);
        }

        public void CopyClocksAt(int index, int[] destination) {
            HolderAt(index).CopyClocks(HolderSlotAt(index), destination);
        }

        static string Text(string value) {
            return value == null ? "" : value;
        }

        // Rebuilds order: every filled slot, sorted by sequence (an insertion sort; there are few).
        void UpdateOrder() {
            if (!orderDirty) return;
            orderDirty = false;
            int capacity = Capacity();
            if (order.Length != capacity) order = new int[capacity];
            orderCount = 0;
            for (int h = 0; h < holders.Length; h++) {
                GameArchiveHolder holder = holders[h];
                if (!Utilities.IsValid(holder)) continue;
                for (int s = 0; s < GameArchiveHolder.Capacity; s++) {
                    int sequence = holder.sequences[s];
                    if (sequence <= 0) continue;
                    int at = orderCount;
                    while (at > 0 && SequenceOfSlot(order[at - 1]) > sequence) {
                        order[at] = order[at - 1];
                        at--;
                    }
                    order[at] = h * GameArchiveHolder.Capacity + s;
                    orderCount++;
                }
            }
        }

        int SequenceOfSlot(int slot) {
            return holders[slot / GameArchiveHolder.Capacity].sequences[slot % GameArchiveHolder.Capacity];
        }
    }
}
