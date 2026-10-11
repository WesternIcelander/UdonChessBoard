using SiggiUSTools;
using UdonSharp;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // Organizer: whoever is instance master right now (docs/organizers.md). Not recommended: when the master leaves,
    // VRChat picks the next one and nobody can choose who.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class InstanceMasterAuth : SimpleAuth {
        public override bool IsAuthorized(VRCPlayerApi who) {
            return Utilities.IsValid(who) && who.isMaster;
        }
    }
}
