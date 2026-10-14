using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;

namespace SiggiChessBoard {
    // Cancels a VRChat build or upload while any ChessArbiter in the open scenes has an empty SimpleAuth field
    // (docs/organizers.md) or an empty Default Piece Set (step 12.3, docs/customizing.md). Choosing nobody is done with
    // NoOneAuth, and every board view needs a piece set to fall back on, so an empty field is always a mistake.
    public class ChessArbiterBuildCheck : IVRCSDKBuildRequestedCallback {
        public int callbackOrder => 0;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType) {
            return Run(requestedBuildType, true);
        }

        // Whether a build of this type may go ahead. With report, a cancelled build also logs an error per empty field,
        // selects the first arbiter and shows the dialog; ChessArbiterBuildCheckTest runs it without, since its empty
        // fields are only emptied for the test and the dialog would claim the scene's are.
        public static bool Run(VRCSDKRequestedBuildType requestedBuildType, bool report) {
            List<ChessArbiter> blocking = Blocking(requestedBuildType);
            if (blocking.Count == 0) return true;
            if (!report) return false;

            // The SDK's own popup only names this callback, so explain in a dialog too: the person building may not
            // think to look in the Console. Selecting the first arbiter shows them where to fix it.
            string authPaths = "";
            string setPaths = "";
            foreach (ChessArbiter arbiter in blocking) {
                string path = PathOf(arbiter.transform);
                if (arbiter.simpleAuth == null) {
                    Debug.LogError("[ChessArbiter] " + path + ": the Simple Auth field is empty. Set it to an "
                        + "authorization behaviour (NoOneAuth if nobody should be an organizer). Build cancelled.",
                        arbiter);
                    authPaths += "\n  • " + path;
                }
                if (arbiter.defaultPieceSet == null) {
                    Debug.LogError("[ChessArbiter] " + path + ": the Default Piece Set field is empty. Set it to a "
                        + "ChessPieceSet (Tools > Chess Board > Create Board View makes one). Build cancelled.", arbiter);
                    setPaths += "\n  • " + path;
                }
            }
            Selection.activeObject = blocking[0].gameObject;
            EditorGUIUtility.PingObject(blocking[0].gameObject);
            string message = "";
            if (authPaths.Length > 0) {
                message += "The \"Simple Auth\" field is empty on:\n" + authPaths + "\n\n"
                    + "This field decides who can change the chess settings in your world. To fix it, select the "
                    + "arbiter, and in the Inspector drag a component into the \"Simple Auth\" field under "
                    + "\"Organizers\":\n\n"
                    + "  • FirstJoinerAuth (on the arbiter by default): the first player in the instance\n"
                    + "  • InstanceMasterAuth: the instance master\n"
                    + "  • NoOneAuth: nobody; the settings stay as you set them in the editor\n\n";
            }
            if (setPaths.Length > 0) {
                message += "The \"Default Piece Set\" field is empty on:\n" + setPaths + "\n\n"
                    + "This field holds the chess piece models the boards use. To fix it, select the arbiter, and in "
                    + "the Inspector drag a Chess Piece Set into the \"Default Piece Set\" field under \"Pieces\" "
                    + "(Tools > Chess Board > Create Board View puts the default set on the arbiter).\n\n";
            }
            EditorUtility.DisplayDialog("Chess: build cancelled", message
                + "The first arbiter is selected now. Then build again.", "OK");
            return false;
        }

        // The arbiters that stop a build of this type: none unless it's a world build, then the ones with an empty
        // field. What Run decides on.
        public static List<ChessArbiter> Blocking(VRCSDKRequestedBuildType requestedBuildType) {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene) return new List<ChessArbiter>();
            List<ChessArbiter> blocking = new List<ChessArbiter>();
            foreach (ChessArbiter arbiter in FindAll()) {
                if (arbiter.simpleAuth == null || arbiter.defaultPieceSet == null) blocking.Add(arbiter);
            }
            return blocking;
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
