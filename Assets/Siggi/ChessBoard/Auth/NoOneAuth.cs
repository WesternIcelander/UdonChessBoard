using SiggiUSTools;
using UdonSharp;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // Organizer: nobody (docs/organizers.md). The editor defaults apply unchanged for the whole instance.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NoOneAuth : SimpleAuth {
        public override bool IsAuthorized(VRCPlayerApi who) {
            return false;
        }
    }
}
