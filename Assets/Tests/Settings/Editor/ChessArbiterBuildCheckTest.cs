using System.Collections.Generic;
using SiggiChessBoard;
using SiggiUSTools;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

// Step 6.3's check of the empty-SimpleAuth build check (ChessArbiterBuildCheck), without building: run it from
// Tools > Chess Board > Test Build Check with the test scene open (it needs at least one ChessArbiter). It empties the
// arbiters' fields for a moment, in memory only, and puts them back. It runs the check without its error log, selection
// and dialog, which would otherwise claim that the scene's field is empty.
// Logs "[BuildCheckTest] PASS: <check>" or "[BuildCheckTest] FAIL: <check>: <details>" per check, then a summary line.
public static class ChessArbiterBuildCheckTest {
    static int passed;
    static int failed;

    [MenuItem("Tools/Chess Board/Test Build Check")]
    public static void Run() {
        passed = 0;
        failed = 0;
        List<ChessArbiter> arbiters = ChessArbiterBuildCheck.FindAll();
        Check(arbiters.Count > 0, "an arbiter in the open scenes", "none found");
        if (arbiters.Count == 0) {
            Summary();
            return;
        }
        SimpleAuth[] saved = new SimpleAuth[arbiters.Count];
        for (int i = 0; i < arbiters.Count; i++) saved[i] = arbiters[i].simpleAuth;
        for (int i = 0; i < arbiters.Count; i++) {
            Check(saved[i] != null, "field set on " + arbiters[i].name, "it's empty in the scene; the checks below "
                + "treat it as empty");
        }

        bool checkRan = false;
        bool worldAllowed = true;
        bool avatarAllowed = false;
        try {
            // With every field set, nothing blocks and the check lets the build through.
            for (int i = 0; i < arbiters.Count; i++) {
                if (arbiters[i].simpleAuth == null) arbiters[i].simpleAuth = AnyAuth(saved);
            }
            if (AnyAuth(saved) != null) {
                CheckBlocking(VRCSDKRequestedBuildType.Scene, arbiters, new bool[arbiters.Count],
                    "all set: a world build isn't blocked");
                Check(ChessArbiterBuildCheck.Run(VRCSDKRequestedBuildType.Scene, false), "all set: the check allows it",
                    "");
            }

            // Each field emptied on its own blocks a world build, naming only that arbiter; an avatar build isn't
            // blocked.
            for (int i = 0; i < arbiters.Count; i++) {
                for (int j = 0; j < arbiters.Count; j++) arbiters[j].simpleAuth = saved[j];
                arbiters[i].simpleAuth = null;
                bool[] expected = new bool[arbiters.Count];
                for (int j = 0; j < arbiters.Count; j++) expected[j] = j == i || saved[j] == null;
                CheckBlocking(VRCSDKRequestedBuildType.Scene, arbiters, expected,
                    arbiters[i].name + " emptied: a world build is blocked by it");
                CheckBlocking(VRCSDKRequestedBuildType.Avatar, arbiters, new bool[arbiters.Count],
                    arbiters[i].name + " emptied: an avatar build isn't blocked");
            }

            // Every field emptied: all of them are named.
            bool[] allEmpty = new bool[arbiters.Count];
            for (int i = 0; i < arbiters.Count; i++) {
                arbiters[i].simpleAuth = null;
                allEmpty[i] = true;
            }
            CheckBlocking(VRCSDKRequestedBuildType.Scene, arbiters, allEmpty, "all emptied: every arbiter blocks");

            // The check cancels a world build with the first one emptied, and allows an avatar build.
            for (int i = 0; i < arbiters.Count; i++) arbiters[i].simpleAuth = saved[i];
            arbiters[0].simpleAuth = null;
            worldAllowed = ChessArbiterBuildCheck.Run(VRCSDKRequestedBuildType.Scene, false);
            avatarAllowed = ChessArbiterBuildCheck.Run(VRCSDKRequestedBuildType.Avatar, false);
            checkRan = true;
        } finally {
            for (int i = 0; i < arbiters.Count; i++) arbiters[i].simpleAuth = saved[i];
        }
        Check(checkRan && !worldAllowed, "emptied: the check cancels a world build", "it allowed it");
        Check(checkRan && avatarAllowed, "emptied: the check allows an avatar build", "it cancelled it");
        bool restored = true;
        for (int i = 0; i < arbiters.Count; i++) {
            if (arbiters[i].simpleAuth != saved[i]) restored = false;
        }
        Check(restored, "fields restored", "");
        Summary();
    }

    static SimpleAuth AnyAuth(SimpleAuth[] auths) {
        foreach (SimpleAuth auth in auths) {
            if (auth != null) return auth;
        }
        return null;
    }

    static void CheckBlocking(VRCSDKRequestedBuildType type, List<ChessArbiter> arbiters, bool[] expected,
            string name) {
        List<ChessArbiter> blocking = ChessArbiterBuildCheck.Blocking(type);
        string problems = "";
        for (int i = 0; i < arbiters.Count; i++) {
            if (blocking.Contains(arbiters[i]) != expected[i]) {
                problems += " " + arbiters[i].name + (expected[i] ? " not blocking;" : " blocking;");
            }
        }
        foreach (ChessArbiter arbiter in blocking) {
            if (!arbiters.Contains(arbiter)) problems += " unknown " + arbiter.name + " blocking;";
        }
        Check(problems.Length == 0, name, problems);
    }

    static void Check(bool ok, string name, string details) {
        if (ok) {
            passed++;
            Debug.Log("[BuildCheckTest] PASS: " + name);
        } else {
            failed++;
            Debug.Log("[BuildCheckTest] FAIL: " + name + ": " + details);
        }
    }

    static void Summary() {
        Debug.Log("[BuildCheckTest] done: " + passed + " passed, " + failed + " failed"
            + (failed == 0 ? " ALL PASS" : " SOME FAIL"));
    }
}
