using System.Collections.Generic;
using SiggiChessBoard;
using SiggiUSTools;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

// Step 6.3's check of the build check (ChessArbiterBuildCheck), without building: run it from Tools > Chess Board >
// Test Build Check with the test scene open (it needs at least one ChessArbiter). It empties the arbiters' Simple Auth
// fields (6.3) and Default Piece Set fields (12.3) for a moment, in memory only, and puts them back. It runs the check
// without its error log, selection and dialog, which would otherwise claim that the scene's fields are empty.
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
        SimpleAuth[] savedAuth = new SimpleAuth[arbiters.Count];
        ChessPieceSet[] savedSet = new ChessPieceSet[arbiters.Count];
        for (int i = 0; i < arbiters.Count; i++) {
            savedAuth[i] = arbiters[i].simpleAuth;
            savedSet[i] = arbiters[i].defaultPieceSet;
            Check(savedAuth[i] != null, "Simple Auth set on " + arbiters[i].name, "it's empty in the scene; the "
                + "checks below treat it as empty");
            Check(savedSet[i] != null, "Default Piece Set set on " + arbiters[i].name, "it's empty in the scene; the "
                + "checks below treat it as empty");
        }
        SimpleAuth anyAuth = null;
        ChessPieceSet anySet = null;
        for (int i = 0; i < arbiters.Count; i++) {
            if (anyAuth == null) anyAuth = savedAuth[i];
            if (anySet == null) anySet = savedSet[i];
        }

        bool checkRan = false;
        bool worldAllowed = true;
        bool avatarAllowed = false;
        bool setWorldAllowed = true;
        try {
            // With every field set, nothing blocks and the check lets the build through.
            if (anyAuth != null && anySet != null) {
                for (int i = 0; i < arbiters.Count; i++) {
                    if (arbiters[i].simpleAuth == null) arbiters[i].simpleAuth = anyAuth;
                    if (arbiters[i].defaultPieceSet == null) arbiters[i].defaultPieceSet = anySet;
                }
                CheckBlocking(VRCSDKRequestedBuildType.Scene, arbiters, new bool[arbiters.Count],
                    "all set: a world build isn't blocked");
                Check(ChessArbiterBuildCheck.Run(VRCSDKRequestedBuildType.Scene, false), "all set: the check allows it",
                    "");
            }

            // Each field emptied on its own blocks a world build, naming only that arbiter; an avatar build isn't
            // blocked.
            for (int field = 0; field < 2; field++) {
                string fieldName = field == 0 ? "Simple Auth" : "Default Piece Set";
                for (int i = 0; i < arbiters.Count; i++) {
                    Restore(arbiters, savedAuth, savedSet);
                    if (field == 0) arbiters[i].simpleAuth = null;
                    else arbiters[i].defaultPieceSet = null;
                    bool[] expected = new bool[arbiters.Count];
                    for (int j = 0; j < arbiters.Count; j++) {
                        expected[j] = j == i || savedAuth[j] == null || savedSet[j] == null;
                    }
                    CheckBlocking(VRCSDKRequestedBuildType.Scene, arbiters, expected,
                        arbiters[i].name + " " + fieldName + " emptied: a world build is blocked by it");
                    CheckBlocking(VRCSDKRequestedBuildType.Avatar, arbiters, new bool[arbiters.Count],
                        arbiters[i].name + " " + fieldName + " emptied: an avatar build isn't blocked");
                }
            }

            // Every field emptied: all of them are named.
            bool[] allEmpty = new bool[arbiters.Count];
            for (int i = 0; i < arbiters.Count; i++) {
                arbiters[i].simpleAuth = null;
                arbiters[i].defaultPieceSet = null;
                allEmpty[i] = true;
            }
            CheckBlocking(VRCSDKRequestedBuildType.Scene, arbiters, allEmpty, "all emptied: every arbiter blocks");

            // The check cancels a world build with the first one's field emptied, and allows an avatar build.
            Restore(arbiters, savedAuth, savedSet);
            arbiters[0].simpleAuth = null;
            worldAllowed = ChessArbiterBuildCheck.Run(VRCSDKRequestedBuildType.Scene, false);
            avatarAllowed = ChessArbiterBuildCheck.Run(VRCSDKRequestedBuildType.Avatar, false);
            Restore(arbiters, savedAuth, savedSet);
            arbiters[0].defaultPieceSet = null;
            setWorldAllowed = ChessArbiterBuildCheck.Run(VRCSDKRequestedBuildType.Scene, false);
            checkRan = true;
        } finally {
            Restore(arbiters, savedAuth, savedSet);
        }
        Check(checkRan && !worldAllowed, "Simple Auth emptied: the check cancels a world build", "it allowed it");
        Check(checkRan && avatarAllowed, "Simple Auth emptied: the check allows an avatar build", "it cancelled it");
        Check(checkRan && !setWorldAllowed, "Default Piece Set emptied: the check cancels a world build",
            "it allowed it");
        bool restored = true;
        for (int i = 0; i < arbiters.Count; i++) {
            if (arbiters[i].simpleAuth != savedAuth[i] || arbiters[i].defaultPieceSet != savedSet[i]) restored = false;
        }
        Check(restored, "fields restored", "");
        Summary();
    }

    static void Restore(List<ChessArbiter> arbiters, SimpleAuth[] auths, ChessPieceSet[] sets) {
        for (int i = 0; i < arbiters.Count; i++) {
            arbiters[i].simpleAuth = auths[i];
            arbiters[i].defaultPieceSet = sets[i];
        }
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
