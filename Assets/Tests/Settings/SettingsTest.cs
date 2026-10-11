using SiggiChessBoard;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Step 6 test behaviour (PLAN.md section 6): the arbiter's settings, with no world objects.
// Logs "[SettingsTest] PASS: <check>" or "[SettingsTest] FAIL: <check>: <details>" per check, then a summary line.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SettingsTest : UdonSharpBehaviour {
    // The arbiter under test; its checks fail if it isn't assigned. Set any values on it in the editor: the checks
    // compare what it reports with them, and its report is logged for comparing by eye.
    public ChessArbiter arbiter;

    int passed;
    int failed;

    void Start() {
        // The arbiter reads its editor values in its own Start, which may run after this one.
        SendCustomEventDelayedFrames(nameof(RunChecks), 1);
    }

    public void RunChecks() {
        TestSettingBits();
        TestFixNumbers();
        TestEditorDefaults();
        Debug.Log("[SettingsTest] done: " + passed + " passed, " + failed + " failed"
            + (failed == 0 ? " ALL PASS" : " SOME FAIL"));
    }

    void Check(bool ok, string name, string details) {
        if (ok) {
            passed++;
            Debug.Log("[SettingsTest] PASS: " + name);
        } else {
            failed++;
            Debug.Log("[SettingsTest] FAIL: " + name + ": " + details);
        }
    }

    void CheckInt(int actual, int expected, string name) {
        Check(actual == expected, name, "expected " + expected + ", got " + actual);
    }

    void CheckText(string actual, string expected, string name) {
        Check(actual == expected, name, "expected \"" + expected + "\", got \"" + actual + "\"");
    }

    // ---- 6.1a ----

    void TestSettingBits() {
        int[] bits = {
            ChessArbiter.SettingTakebacks, ChessArbiter.SettingDrawAgreement, ChessArbiter.SettingThreefoldRepetition,
            ChessArbiter.SettingFiftyMoveRule, ChessArbiter.SettingFivefoldRepetition,
            ChessArbiter.SettingSeventyFiveMoveRule, ChessArbiter.SettingDeadPosition, ChessArbiter.SettingModeForced,
            ChessArbiter.SettingClockForced
        };
        CheckInt(bits.Length, ChessArbiter.SettingCount, "bits: one per setting");
        int all = 0;
        bool single = true;
        for (int i = 0; i < bits.Length; i++) {
            if (bits[i] == 0 || (bits[i] & (bits[i] - 1)) != 0) single = false;
            all |= bits[i];
        }
        Check(single, "bits: each a single bit", "");
        CheckInt(all, (1 << ChessArbiter.SettingCount) - 1, "bits: distinct, the lowest SettingCount bits");
        CheckInt(ChessArbiter.SettingsDrawRules, all & ~(ChessArbiter.SettingTakebacks
                | ChessArbiter.SettingModeForced | ChessArbiter.SettingClockForced),
            "bits: draw rules are the six draw settings");
        CheckText(ChessArbiter.SettingsToString(0), "none", "bits text: none");
        CheckText(ChessArbiter.SettingsToString(all),
            "takebacks agreement threefold fifty-move fivefold seventy-five-move dead-position mode-forced clock-forced",
            "bits text: all");
        CheckText(ChessArbiter.SettingsToString(ChessArbiter.SettingTakebacks | ChessArbiter.SettingClockForced),
            "takebacks clock-forced", "bits text: two");
    }

    void TestFixNumbers() {
        CheckFix(120, 600, 0, 600, 0, false, 120, 600, 0, 600, 0, "fix: the editor's starting values");
        CheckFix(-1, -1, 1, -1, 0, false, -1, -1, 1, -1, 0, "fix: never, never, Chess960, no clock");
        CheckFix(0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0, "fix: all zero");
        CheckFix(600, 600, 0, 300, 2, false, 600, 600, 0, 300, 2, "fix: equal times");
        CheckFix(900, 600, 0, 300, 2, true, 600, 600, 0, 300, 2, "fix: reset-available above auto-reset");
        CheckFix(900, -1, 0, 300, 2, false, 900, -1, 0, 300, 2, "fix: auto-reset never, any reset-available");
        CheckFix(-1, 60, 0, 300, 2, false, -1, 60, 0, 300, 2, "fix: reset-available never, any auto-reset");
        CheckFix(-5, -2, 0, 300, 2, true, -1, -1, 0, 300, 2, "fix: negative times become never");
        CheckFix(120, 600, 2, 300, 2, true, 120, 600, 0, 300, 2, "fix: unknown mode");
        CheckFix(120, 600, -1, 300, 2, true, 120, 600, 0, 300, 2, "fix: negative mode");
        CheckFix(120, 600, 1, -7, -3, true, 120, 600, 1, -1, 0, "fix: negative clock");
        CheckFix(120, 600, 1, -1, 5, false, 120, 600, 1, -1, 5, "fix: no clock keeps the increment");
    }

    void CheckFix(int resetAvailable, int autoReset, int mode, int baseTime, int increment, bool expectChanged,
            int expectResetAvailable, int expectAutoReset, int expectMode, int expectBase, int expectIncrement,
            string name) {
        int[] numbers = { resetAvailable, autoReset, mode, baseTime, increment };
        int[] expected = { expectResetAvailable, expectAutoReset, expectMode, expectBase, expectIncrement };
        bool changed = ChessArbiter.FixNumbers(numbers);
        string actualText = ChessArbiter.NumbersToString(numbers);
        string expectedText = ChessArbiter.NumbersToString(expected);
        Check(changed == expectChanged && actualText == expectedText, name,
            "expected " + expectedText + " (changed " + expectChanged + "), got " + actualText + " (changed "
            + changed + ")");
    }

    // What the arbiter reports is what was set in the editor, each setting in its own place.
    void TestEditorDefaults() {
        if (!Utilities.IsValid(arbiter)) {
            Check(false, "editor defaults", "arbiter not assigned");
            return;
        }
        Debug.Log("[SettingsTest] arbiter reports: " + ChessArbiter.SettingsToString(arbiter.defaultSettings) + "; "
            + ChessArbiter.NumbersToString(arbiter.defaultNumbers));

        CheckSetting(ChessArbiter.SettingTakebacks, arbiter.takebacksAllowed, "takebacksAllowed");
        CheckSetting(ChessArbiter.SettingDrawAgreement, arbiter.drawAgreement, "drawAgreement");
        CheckSetting(ChessArbiter.SettingThreefoldRepetition, arbiter.threefoldRepetition, "threefoldRepetition");
        CheckSetting(ChessArbiter.SettingFiftyMoveRule, arbiter.fiftyMoveRule, "fiftyMoveRule");
        CheckSetting(ChessArbiter.SettingFivefoldRepetition, arbiter.fivefoldRepetition, "fivefoldRepetition");
        CheckSetting(ChessArbiter.SettingSeventyFiveMoveRule, arbiter.seventyFiveMoveRule, "seventyFiveMoveRule");
        CheckSetting(ChessArbiter.SettingDeadPosition, arbiter.deadPosition, "deadPosition");
        CheckSetting(ChessArbiter.SettingModeForced, arbiter.modeForced, "modeForced");
        CheckSetting(ChessArbiter.SettingClockForced, arbiter.clockForced, "clockForced");
        CheckInt(arbiter.defaultSettings & ~((1 << ChessArbiter.SettingCount) - 1), 0, "editor defaults: no other bits");

        // The numeric settings as entered, corrected the same way the arbiter corrects them.
        int[] expected = {
            arbiter.resetAvailableTime, arbiter.autoResetTime, arbiter.forcedMode, arbiter.forcedBaseTime,
            arbiter.forcedIncrement
        };
        ChessArbiter.FixNumbers(expected);
        CheckInt(arbiter.defaultNumbers.Length, ChessArbiter.NumberCount, "editor defaults: number count");
        CheckInt(arbiter.DefaultNumber(ChessArbiter.NumberResetAvailableTime),
            expected[ChessArbiter.NumberResetAvailableTime], "editor defaults: resetAvailableTime");
        CheckInt(arbiter.DefaultNumber(ChessArbiter.NumberAutoResetTime),
            expected[ChessArbiter.NumberAutoResetTime], "editor defaults: autoResetTime");
        CheckInt(arbiter.DefaultNumber(ChessArbiter.NumberForcedMode),
            expected[ChessArbiter.NumberForcedMode], "editor defaults: forcedMode");
        CheckInt(arbiter.DefaultNumber(ChessArbiter.NumberForcedBaseTime),
            expected[ChessArbiter.NumberForcedBaseTime], "editor defaults: forcedBaseTime");
        CheckInt(arbiter.DefaultNumber(ChessArbiter.NumberForcedIncrement),
            expected[ChessArbiter.NumberForcedIncrement], "editor defaults: forcedIncrement");
    }

    void CheckSetting(int setting, bool editorValue, string name) {
        Check(arbiter.IsDefaultOn(setting) == editorValue, "editor defaults: " + name,
            "editor " + editorValue + ", arbiter reports " + arbiter.IsDefaultOn(setting));
    }
}
