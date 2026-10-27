using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // Up to Capacity archived games (docs/archive.md), one holder of the InstanceGameArchive's. Its games are synced,
    // written only by the archive's owner, who alone may own a holder. A localOnly holder (the archive's recent games)
    // never sends or takes anything. Udon can't sync arrays of arrays, so the games' moves and clocks are flattened:
    // back to back in slot order, each slot's length in its infos.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class GameArchiveHolder : UdonSharpBehaviour {
        public const int Capacity = 5;

        // Per slot, InfoLength ints in infos.
        public const int InfoMode = 0;
        public const int InfoEnding = 1;
        public const int InfoWinner = 2;
        public const int InfoBaseTime = 3;
        public const int InfoIncrement = 4;
        public const int InfoMoveCount = 5;
        public const int InfoLength = 6;

        // The archive this holder belongs to: only its owner may own the holder.
        [Tooltip("The InstanceGameArchive this holder belongs to.")]
        public InstanceGameArchive archive;
        // The recent games of this client: never sent, never taken from an update.
        [Tooltip("Keeps this client's recent games: never synced.")]
        public bool localOnly;

        // ---- Games ----
        // Each slot's place in the archive's order (from 1; 0 for an empty slot), its game's ID, ContinuedFrom, players
        // and starting FEN ("" or null for none), its numbers (InfoLength per slot), and the slots' moves and clocks
        // back to back. All zeros and nulls is every slot empty.
        [NonSerialized] public int[] sequences = new int[Capacity];
        [NonSerialized] public string[] gameIds = new string[Capacity];
        [NonSerialized] public string[] continuedFroms = new string[Capacity];
        [NonSerialized] public string[] whites = new string[Capacity];
        [NonSerialized] public string[] blacks = new string[Capacity];
        [NonSerialized] public string[] startFens = new string[Capacity];
        [NonSerialized] public int[] infos = new int[Capacity * InfoLength];
        [NonSerialized] public int[] moves = new int[0];
        [NonSerialized] public int[] clocks = new int[0];

        // ---- Synced ----
        // The games above, packed before each serialization and unpacked when one arrives, so a localOnly holder's
        // games are never replaced by an update. A synced string[] must never hold null: packing writes "" for one. A
        // localOnly holder sends these empty arrays, which receivers ignore.
        [UdonSynced] [NonSerialized] public int[] syncedSequences = new int[0];
        [UdonSynced] [NonSerialized] public string[] syncedGameIds = new string[0];
        [UdonSynced] [NonSerialized] public string[] syncedContinuedFroms = new string[0];
        [UdonSynced] [NonSerialized] public string[] syncedWhites = new string[0];
        [UdonSynced] [NonSerialized] public string[] syncedBlacks = new string[0];
        [UdonSynced] [NonSerialized] public string[] syncedStartFens = new string[0];
        [UdonSynced] [NonSerialized] public int[] syncedInfos = new int[0];
        [UdonSynced] [NonSerialized] public int[] syncedMoves = new int[0];
        [UdonSynced] [NonSerialized] public int[] syncedClocks = new int[0];

        // Whether this client holds the instance's games: an update has arrived, or the local player owned the holder
        // on Start as the master (the instance's first player). The archive's owner writes only once every holder is
        // ready.
        [NonSerialized] public bool ready;
        // How many times this client has sent the holder (Send), for tests.
        [NonSerialized] public int sends;

        void Start() {
            // Only the master: a late joiner can be reported as the owner of everything before the owners are known.
            VRCPlayerApi local = Networking.LocalPlayer;
            if (localOnly || (Utilities.IsValid(local) && local.isMaster && Networking.IsOwner(gameObject))) {
                ready = true;
            }
        }

        // The slot holding gameId, -1 for none.
        public int _SlotOf(string gameId) {
            if (string.IsNullOrEmpty(gameId)) return -1;
            for (int i = 0; i < Capacity; i++) {
                if (sequences[i] > 0 && gameIds[i] == gameId) return i;
            }
            return -1;
        }

        public int _Info(int slot, int info) {
            return infos[slot * InfoLength + info];
        }

        public int _MoveCount(int slot) {
            return infos[slot * InfoLength + InfoMoveCount];
        }

        // Where slot's moves and clocks start in moves and clocks.
        public int _MoveOffset(int slot) {
            int offset = 0;
            for (int i = 0; i < slot; i++) offset += _MoveCount(i);
            return offset;
        }

        // Copies slot's moves into destination (which must have room for _MoveCount(slot)).
        public void _CopyMoves(int slot, int[] destination) {
            Array.Copy(moves, _MoveOffset(slot), destination, 0, _MoveCount(slot));
        }

        public void _CopyClocks(int slot, int[] destination) {
            Array.Copy(clocks, _MoveOffset(slot), destination, 0, _MoveCount(slot));
        }

        // Puts a game in slot, replacing what was there: its sequence (its place in the archive's order), the first
        // count of gameMoves and gameClocks, and the rest as InstanceGameArchive._AddGame takes them. null strings are kept as
        // "". Nothing is sent (Send).
        public void _WriteSlot(int slot, int sequence, string gameId, string continuedFrom, string white, string black,
            int mode, int ending, int winner, string startFen, int baseTime, int increment, int[] gameMoves,
            int[] gameClocks, int count) {
            int offset = _MoveOffset(slot);
            int oldCount = _MoveCount(slot);
            int total = moves.Length - oldCount + count;
            int after = moves.Length - offset - oldCount;
            int[] newMoves = new int[total];
            int[] newClocks = new int[total];
            Array.Copy(moves, newMoves, offset);
            Array.Copy(clocks, newClocks, offset);
            Array.Copy(gameMoves, 0, newMoves, offset, count);
            Array.Copy(gameClocks, 0, newClocks, offset, count);
            Array.Copy(moves, offset + oldCount, newMoves, offset + count, after);
            Array.Copy(clocks, offset + oldCount, newClocks, offset + count, after);
            moves = newMoves;
            clocks = newClocks;

            sequences[slot] = sequence;
            gameIds[slot] = gameId == null ? "" : gameId;
            continuedFroms[slot] = continuedFrom == null ? "" : continuedFrom;
            whites[slot] = white == null ? "" : white;
            blacks[slot] = black == null ? "" : black;
            startFens[slot] = startFen == null ? "" : startFen;
            int info = slot * InfoLength;
            infos[info + InfoMode] = mode;
            infos[info + InfoEnding] = ending;
            infos[info + InfoWinner] = winner;
            infos[info + InfoBaseTime] = baseTime;
            infos[info + InfoIncrement] = increment;
            infos[info + InfoMoveCount] = count;
        }

        // Empties every slot. Nothing is sent.
        public void _Clear() {
            sequences = new int[Capacity];
            gameIds = new string[Capacity];
            continuedFroms = new string[Capacity];
            whites = new string[Capacity];
            blacks = new string[Capacity];
            startFens = new string[Capacity];
            infos = new int[Capacity * InfoLength];
            moves = new int[0];
            clocks = new int[0];
        }

        // Takes ownership (allowed only to the archive's owner) and sends the games; returns whether this client
        // owns the holder. A localOnly holder is never sent.
        public bool _Send() {
            if (localOnly) return false;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(local, gameObject);
            if (!Networking.IsOwner(gameObject)) {
                Debug.LogWarning("[GameArchiveHolder] " + gameObject.name + ": not sent, the holder isn't ours");
                return false;
            }
            sends++;
            RequestSerialization();
            return true;
        }

        // Asked on the requesting client and on the owner. A refusal is logged, so a client's log shows it.
        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) {
            bool allowed = _MayOwn(requestedOwner);
            if (!allowed) {
                Debug.Log("[GameArchiveHolder] " + gameObject.name + ": ownership refused for "
                    + (Utilities.IsValid(requestedOwner) ? requestedOwner.displayName : "nobody"));
            }
            return allowed;
        }

        // Whether a player may own this holder: the archive's owner, and nobody for a localOnly holder.
        public bool _MayOwn(VRCPlayerApi who) {
            if (localOnly || !Utilities.IsValid(who) || !Utilities.IsValid(archive)) return false;
            return Networking.IsOwner(who, archive.gameObject);
        }

        public override void OnPreSerialization() {
            _PackState();
        }

        public override void OnDeserialization() {
            _ReceiveState();
        }

        // Copies the games into the synced fields (nothing for a localOnly holder). Public for tests.
        public void _PackState() {
            if (localOnly) return;
            syncedSequences = sequences;
            syncedGameIds = PackStrings(gameIds);
            syncedContinuedFroms = PackStrings(continuedFroms);
            syncedWhites = PackStrings(whites);
            syncedBlacks = PackStrings(blacks);
            syncedStartFens = PackStrings(startFens);
            syncedInfos = infos;
            syncedMoves = moves;
            syncedClocks = clocks;
        }

        // Takes the games from the synced fields, unless the holder is localOnly or they aren't an update _PackState
        // could have written. Public for tests.
        public void _ReceiveState() {
            if (localOnly || !IsValidUpdate()) return;
            sequences = CopyInts(syncedSequences);
            gameIds = CopyStrings(syncedGameIds);
            continuedFroms = CopyStrings(syncedContinuedFroms);
            whites = CopyStrings(syncedWhites);
            blacks = CopyStrings(syncedBlacks);
            startFens = CopyStrings(syncedStartFens);
            infos = CopyInts(syncedInfos);
            moves = CopyInts(syncedMoves);
            clocks = CopyInts(syncedClocks);
            ready = true;
            if (Utilities.IsValid(archive)) archive._OnHolderChanged();
        }

        // Whether the synced fields are an update _PackState could have written.
        bool IsValidUpdate() {
            if (syncedSequences == null || syncedSequences.Length != Capacity) return false;
            if (!IsValidStrings(syncedGameIds) || !IsValidStrings(syncedContinuedFroms)
                || !IsValidStrings(syncedWhites) || !IsValidStrings(syncedBlacks)
                || !IsValidStrings(syncedStartFens)) {
                return false;
            }
            if (syncedInfos == null || syncedInfos.Length != Capacity * InfoLength) return false;
            if (syncedMoves == null || syncedClocks == null || syncedMoves.Length != syncedClocks.Length) return false;
            int total = 0;
            for (int i = 0; i < Capacity; i++) {
                int count = syncedInfos[i * InfoLength + InfoMoveCount];
                if (count < 0) return false;
                total += count;
            }
            return total == syncedMoves.Length;
        }

        static bool IsValidStrings(string[] values) {
            if (values == null || values.Length != Capacity) return false;
            for (int i = 0; i < Capacity; i++) {
                if (values[i] == null) return false;
            }
            return true;
        }

        // A copy of values with "" for each null.
        static string[] PackStrings(string[] values) {
            string[] packed = new string[values.Length];
            for (int i = 0; i < values.Length; i++) packed[i] = values[i] == null ? "" : values[i];
            return packed;
        }

        static int[] CopyInts(int[] values) {
            int[] copy = new int[values.Length];
            Array.Copy(values, copy, values.Length);
            return copy;
        }

        static string[] CopyStrings(string[] values) {
            string[] copy = new string[values.Length];
            Array.Copy(values, copy, values.Length);
            return copy;
        }
    }
}
