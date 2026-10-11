using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;

namespace SiggiChessBoard {
    // Cancels a VRChat build or upload while any ChessArbiter in the open scenes has an empty SimpleAuth field
    // (docs/organizers.md). Choosing nobody is done with NoOneAuth, so an empty field is always a mistake.
    public class ChessArbiterBuildCheck : IVRCSDKBuildRequestedCallback {
        public int callbackOrder => 0;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType) {
            List<ChessArbiter> empty = Blocking(requestedBuildType);
            if (empty.Count == 0) return true;

            // The SDK's own popup only names this callback, so explain in a dialog too: the person building may not
            // think to look in the Console. Selecting the first arbiter shows them where to fix it.
            string paths = "";
            foreach (ChessArbiter arbiter in empty) {
                string path = PathOf(arbiter.transform);
                Debug.LogError("[ChessArbiter] " + path + ": the Simple Auth field is empty. Set it to an "
                    + "authorization behaviour (NoOneAuth if nobody should be an organizer). Build cancelled.", arbiter);
                paths += "\n  • " + path;
            }
            Selection.activeObject = empty[0].gameObject;
            EditorGUIUtility.PingObject(empty[0].gameObject);
            EditorUtility.DisplayDialog("Chess: build cancelled",
                "The \"Simple Auth\" field is empty on " + (empty.Count == 1 ? "this chess arbiter" : "these chess arbiters")
                + ":\n" + paths + "\n\n"
                + "This field decides who can change the chess settings in your world. To fix it, select the arbiter "
                + "(the first one is selected now), and in the Inspector drag a component into the \"Simple Auth\" "
                + "field under \"Organizers\":\n\n"
                + "  • FirstJoinerAuth (on the arbiter by default): the first player in the instance\n"
                + "  • InstanceMasterAuth: the instance master\n"
                + "  • NoOneAuth: nobody; the settings stay as you set them in the editor\n\n"
                + "Then build again.",
                "OK");
            return false;
        }

        // The arbiters that stop a build of this type: none unless it's a world build, then the ones with an empty
        // field. What OnBuildRequested decides on, without its dialog (ChessArbiterBuildCheckTest calls it).
        public static List<ChessArbiter> Blocking(VRCSDKRequestedBuildType requestedBuildType) {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene) return new List<ChessArbiter>();
            return FindEmpty();
        }

        // Every arbiter (inactive ones included) in the loaded scenes whose field is empty.
        public static List<ChessArbiter> FindEmpty() {
            List<ChessArbiter> empty = new List<ChessArbiter>();
            foreach (ChessArbiter arbiter in FindAll()) {
                if (arbiter.simpleAuth == null) empty.Add(arbiter);
            }
            return empty;
        }

        // Every arbiter (inactive ones included) in the loaded scenes.
        public static List<ChessArbiter> FindAll() {
            List<ChessArbiter> all = new List<ChessArbiter>();
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects()) {
                    all.AddRange(root.GetComponentsInChildren<ChessArbiter>(true));
                }
            }
            return all;
        }

        static string PathOf(Transform t) {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
