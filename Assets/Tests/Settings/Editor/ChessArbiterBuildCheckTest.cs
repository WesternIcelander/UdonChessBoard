using System.Collections.Generic;
using SiggiChessBoard;
using SiggiUSTools;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

// Step 6.3's check of the build check (ChessArbiterBuildCheck), without building: run it from Tools > Chess Board >
// Test Build Check with the test scene open (it needs at least one ChessArbiter). It empties the arbiters' Simple Auth
// fields (6.3) and Default White and Black Piece Set fields (12.3, 12.10) for a moment, in memory only, and puts them
// back. It runs the check without its error log, selection and dialog, which would otherwise claim that the scene's
// fields are empty.
// Logs "[BuildCheckTest] PASS: <check>" or "[BuildCheckTest] FAIL: <check>: <details>" per check, then a summary line.
public static class ChessArbiterBuildCheckTest {
    static int passed;
    static int failed;

    // The fields emptied, in turn.
    static readonly string[] FieldNames = { "Simple Auth", "Default White Piece Set", "Default Black Piece Set" };

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
        ChessPieceSet[] savedWhite = new ChessPieceSet[arbiters.Count];
        ChessPieceSet[] savedBlack = new ChessPieceSet[arbiters.Count];
        for (int i = 0; i < arbiters.Count; i++) {
            savedAuth[i] = arbiters[i].simpleAuth;
            savedWhite[i] = arbiters[i].defaultWhitePieceSet;
            savedBlack[i] = arbiters[i].defaultBlackPieceSet;
            Check(savedAuth[i] != null, "Simple Auth set on " + arbiters[i].name, "it's empty in the scene; the "
                + "checks below treat it as empty");
            Check(savedWhite[i] != null, "Default White Piece Set set on " + arbiters[i].name, "it's empty in the "
                + "scene; the checks below treat it as empty");
            Check(savedBlack[i] != null, "Default Black Piece Set set on " + arbiters[i].name, "it's empty in the "
                + "scene; the checks below treat it as empty");
        }
        SimpleAuth anyAuth = null;
        ChessPieceSet anySet = null;
        for (int i = 0; i < arbiters.Count; i++) {
            if (anyAuth == null) anyAuth = savedAuth[i];
            if (anySet == null) anySet = savedWhite[i] != null ? savedWhite[i] : savedBlack[i];
        }

        bool checkRan = false;
        bool worldAllowed = true;
        bool avatarAllowed = false;
        bool whiteWorldAllowed = true;
        bool blackWorldAllowed = true;
        try {
            // With every field set, nothing blocks and the check lets the build through.
            if (anyAuth != null && anySet != null) {
                for (int i = 0; i < arbiters.Count; i++) {
                    if (arbiters[i].simpleAuth == null) arbiters[i].simpleAuth = anyAuth;
                    if (arbiters[i].defaultWhitePieceSet == null) arbiters[i].defaultWhitePieceSet = anySet;
                    if (arbiters[i].defaultBlackPieceSet == null) arbiters[i].defaultBlackPieceSet = anySet;
                }
                CheckBlocking(VRCSDKRequestedBuildType.Scene, arbiters, new bool[arbiters.Count],
                    "all set: a world build isn't blocked");
                Check(ChessArbiterBuildCheck.Run(VRCSDKRequestedBuildType.Scene, false), "all set: the check allows it",
                    "");
            }

            // Each field emptied on its own blocks a world build, naming only that arbiter; an avatar build isn't
            // blocked.
            for (int field = 0; field < FieldNames.Length; field++) {
                for (int i = 0; i < arbiters.Count; i++) {
                    Restore(arbiters, savedAuth, savedWhite, savedBlack);
                    Empty(arbiters[i], field);
                    bool[] expected = new bool[arbiters.Count];
                    for (int j = 0; j < arbiters.Count; j++) {
                        expected[j] = j == i || savedAuth[j] == null || savedWhite[j] == null || savedBlack[j] == null;
                    }
                    CheckBlocking(VRCSDKRequestedBuildType.Scene, arbiters, expected,
                        arbiters[i].name + " " + FieldNames[field] + " emptied: a world build is blocked by it");
                    CheckBlocking(VRCSDKRequestedBuildType.Avatar, arbiters, new bool[arbiters.Count],
                        arbiters[i].name + " " + FieldNames[field] + " emptied: an avatar build isn't blocked");
                }
            }

            // Every field emptied: all of them are named.
            bool[] allEmpty = new bool[arbiters.Count];
            for (int i = 0; i < arbiters.Count; i++) {
                for (int field = 0; field < FieldNames.Length; field++) Empty(arbiters[i], field);
                allEmpty[i] = true;
            }
            CheckBlocking(VRCSDKRequestedBuildType.Scene, arbiters, allEmpty, "all emptied: every arbiter blocks");

            // The check cancels a world build with the first one's field emptied, and allows an avatar build.
            Restore(arbiters, savedAuth, savedWhite, savedBlack);
            Empty(arbiters[0], 0);
            worldAllowed = ChessArbiterBuildCheck.Run(VRCSDKRequestedBuildType.Scene, false);
            avatarAllowed = ChessArbiterBuildCheck.Run(VRCSDKRequestedBuildType.Avatar, false);
            Restore(arbiters, savedAuth, savedWhite, savedBlack);
            Empty(arbiters[0], 1);
            whiteWorldAllowed = ChessArbiterBuildCheck.Run(VRCSDKRequestedBuildType.Scene, false);
            Restore(arbiters, savedAuth, savedWhite, savedBlack);
            Empty(arbiters[0], 2);
            blackWorldAllowed = ChessArbiterBuildCheck.Run(VRCSDKRequestedBuildType.Scene, false);
            checkRan = true;
        } finally {
            Restore(arbiters, savedAuth, savedWhite, savedBlack);
        }
        Check(checkRan && !worldAllowed, "Simple Auth emptied: the check cancels a world build", "it allowed it");
        Check(checkRan && avatarAllowed, "Simple Auth emptied: the check allows an avatar build", "it cancelled it");
        Check(checkRan && !whiteWorldAllowed, "Default White Piece Set emptied: the check cancels a world build",
            "it allowed it");
        Check(checkRan && !blackWorldAllowed, "Default Black Piece Set emptied: the check cancels a world build",
            "it allowed it");
        bool restored = true;
        for (int i = 0; i < arbiters.Count; i++) {
            if (arbiters[i].simpleAuth != savedAuth[i] || arbiters[i].defaultWhitePieceSet != savedWhite[i]
                    || arbiters[i].defaultBlackPieceSet != savedBlack[i]) {
                restored = false;
            }
        }
        Check(restored, "fields restored", "");
        Summary();
    }

    // Empties one of the fields (FieldNames) on arbiter.
    static void Empty(ChessArbiter arbiter, int field) {
        if (field == 0) arbiter.simpleAuth = null;
        else if (field == 1) arbiter.defaultWhitePieceSet = null;
        else arbiter.defaultBlackPieceSet = null;
    }

    static void Restore(List<ChessArbiter> arbiters, SimpleAuth[] auths, ChessPieceSet[] whites,
            ChessPieceSet[] blacks) {
        for (int i = 0; i < arbiters.Count; i++) {
            arbiters[i].simpleAuth = auths[i];
            arbiters[i].defaultWhitePieceSet = whites[i];
            arbiters[i].defaultBlackPieceSet = blacks[i];
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
