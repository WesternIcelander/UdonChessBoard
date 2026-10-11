using System;
using SiggiUSTools;
using UdonSharp;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // Organizer: the first player in the instance (docs/organizers.md). Rights don't pass on when they leave; they
    // come back if the same display name rejoins. The default on the arbiter prefab.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class FirstJoinerAuth : SimpleAuth {
        // The first joiner's display name, "" until they've arrived. Synced so late joiners know who it was.
        [UdonSynced] [NonSerialized] public string firstJoiner = "";

        // The first player owns every object when they arrive, so only they find themselves the owner with the
        // name still empty. A late joiner isn't the owner (and gets the name by serialization).
        public override void OnPlayerJoined(VRCPlayerApi player) {
            if (!player.isLocal || !Networking.IsOwner(gameObject) || firstJoiner.Length > 0) return;
            firstJoiner = player.displayName;
            RequestSerialization();
        }

        public override bool IsAuthorized(VRCPlayerApi who) {
            return Utilities.IsValid(who) && firstJoiner.Length > 0 && who.displayName == firstJoiner;
        }
    }
}
