using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace SiggiChessBoard {
    // Before every VRChat build, gives each movable 2D view's grip a VRCObjectSync when its view is synced and takes it
    // away when not (PLAN.md 12.16, docs/board-2d.md), so ticking Synced in the Inspector needs no rebuild. Logs each
    // grip it changes. Plain editor C#, not Udon.
    public class ChessBoard2DViewSyncCheck : IVRCSDKBuildRequestedCallback {
        public int callbackOrder => 0;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType) {
            if (requestedBuildType == VRCSDKRequestedBuildType.Scene) MatchAll();
            return true;
        }

        // Matches every holder in the loaded scenes (inactive ones included); how many it changed.
        public static int MatchAll() {
            int changed = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects()) {
                    foreach (ChessBoard2DViewHolder holder in root.GetComponentsInChildren<ChessBoard2DViewHolder>(true)) {
                        if (!MatchObjectSync(holder)) continue;
                        changed++;
                        EditorSceneManager.MarkSceneDirty(scene);
                        Debug.Log("[ChessBoard2DViewSyncCheck] " + holder.gameObject.name + ": "
                            + (holder.view.synced ? "added a VRCObjectSync to" : "removed the VRCObjectSync from")
                            + " the grip, as its view's Synced is " + (holder.view.synced ? "on" : "off") + ".",
                            holder);
                    }
                }
            }
            return changed;
        }

        // Gives the holder's grip a VRCObjectSync when its view is synced and takes it away when not; whether it
        // changed anything. The Movable 2D Chess Board View prefab holds one set of components whatever the view's
        // Synced tick, so the grip has none in it (decided in 14.8).
        public static bool MatchObjectSync(ChessBoard2DViewHolder holder) {
            if (holder == null || holder.grip == null || holder.view == null) return false;
            VRCObjectSync sync = holder.grip.GetComponent<VRCObjectSync>();
            if (holder.view.synced && sync == null) {
                sync = Undo.AddComponent<VRCObjectSync>(holder.grip.gameObject);
                sync.AllowCollisionOwnershipTransfer = false;
                return true;
            }
            if (!holder.view.synced && sync != null) {
                Undo.DestroyObjectImmediate(sync);
                return true;
            }
            return false;
        }
    }
}
