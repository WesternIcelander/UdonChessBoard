
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace SiggiUSTools {
    public abstract class SimpleAuth : UdonSharpBehaviour {
        public abstract bool IsAuthorized(VRCPlayerApi who);
    }
}
