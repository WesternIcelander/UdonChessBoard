using SiggiChessBoard;
using SiggiUSTools;
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

    // The other built-in authorizations, found on this object (the arbiter's own is checked through the arbiter).
    InstanceMasterAuth instanceMasterAuth;
    NoOneAuth noOneAuth;

    int passed;
    int failed;
    bool started;
    bool joined;
    bool ran;

    // The checks run a frame after both this Start and the local player joining: the arbiter reads its editor values
    // in its own Start, which may run after this one, and FirstJoinerAuth takes the first joiner's name on join.
    void Start() {
        instanceMasterAuth = GetComponent<InstanceMasterAuth>();
        noOneAuth = GetComponent<NoOneAuth>();
        started = true;
        SendCustomEventDelayedFrames(nameof(RunChecks), 1);
    }

    public override void OnPlayerJoined(VRCPlayerApi player) {
        if (!player.isLocal) return;
        joined = true;
        SendCustomEventDelayedFrames(nameof(RunChecks), 1);
    }

    public void RunChecks() {
        if (ran || !started || !joined) return;
        ran = true;
        TestSettingBits();
        TestFixNumbers();
        TestEditorDefaults();
        TestCombine();
        TestBoardOverrides();
        TestAuth();
        TestAuthFallback();
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

    // ---- 6.1b ----

    void TestCombine() {
        int t = ChessArbiter.SettingTakebacks;
        int a = ChessArbiter.SettingDrawAgreement;
        int r = ChessArbiter.SettingThreefoldRepetition;
        int m = ChessArbiter.SettingModeForced;
        int c = ChessArbiter.SettingClockForced;
        int all = ChessArbiter.OverrideSettingsMask;
        int numberBits = ChessArbiter.OverrideNumbersMask;
        CheckInt(numberBits & all, 0, "override bits: numbers above the settings");
        int numbers = 0;
        for (int i = 0; i < ChessArbiter.NumberCount; i++) numbers |= ChessArbiter.OverrideNumberBit(i);
        CheckInt(numbers, numberBits, "override bits: one per number, distinct");
        CheckInt(ChessArbiter.OverrideNumberBit(0), 1 << ChessArbiter.SettingCount, "override bits: first number");

        CheckInt(ChessArbiter.CombineSettings(t | r, 0, 0), t | r, "combine: no mask gives the defaults");
        CheckInt(ChessArbiter.CombineSettings(t | r, 0, all), t | r, "combine: unmasked values ignored");
        CheckInt(ChessArbiter.CombineSettings(t | r, all, a | c), a | c, "combine: full mask gives the values");
        CheckInt(ChessArbiter.CombineSettings(t | r, t | a, a), r | a, "combine: one off, one on");
        CheckInt(ChessArbiter.CombineSettings(t | r, t | r, t | r), t | r, "combine: overridden to the same");
        CheckInt(ChessArbiter.CombineSettings(t, m | c, m | a), t | m, "combine: only masked value bits count");
        CheckInt(ChessArbiter.CombineSettings(0, ChessArbiter.SettingsDrawRules, 0), 0, "combine: no draws, from none");
        CheckInt(ChessArbiter.CombineSettings(all, ChessArbiter.SettingsDrawRules, 0),
            t | m | c, "combine: no draws, from all");
        CheckInt(ChessArbiter.CombineSettings(t, numberBits, all), t, "combine: number bits don't touch settings");
        CheckInt(ChessArbiter.CombineSettings(t | numberBits | (1 << 30), ~0, a | (1 << 30)), a,
            "combine: no bits above the settings");
    }

    // Overrides on the arbiter's boards 0 to 2, against whatever its defaults are; cleared again at the end.
    void TestBoardOverrides() {
        if (!Utilities.IsValid(arbiter)) {
            Check(false, "overrides", "arbiter not assigned");
            return;
        }
        if (arbiter.BoardCount() < 3) {
            Check(false, "overrides", "the arbiter's boardCount is " + arbiter.BoardCount() + ", needs at least 3");
            return;
        }
        int defaults = arbiter.defaultSettings;
        int[] defaultNumbers = arbiter.defaultNumbers;
        string defaultText = ChessArbiter.NumbersToString(defaultNumbers);
        int t = ChessArbiter.SettingTakebacks;
        int a = ChessArbiter.SettingDrawAgreement;
        int r = ChessArbiter.SettingThreefoldRepetition;
        int m = ChessArbiter.SettingModeForced;
        int c = ChessArbiter.SettingClockForced;
        for (int b = 0; b < 3; b++) arbiter.ClearOverrides(b);

        // No overrides, and boards without overrides.
        CheckBoard(0, defaults, defaultText, "no overrides: board 0");
        CheckBoard(2, defaults, defaultText, "no overrides: board 2");
        CheckBoard(-1, defaults, defaultText, "no board: -1");
        CheckBoard(arbiter.BoardCount(), defaults, defaultText, "no board: board count");
        Check(!arbiter.SetSettingOverride(-1, t, true) && !arbiter.SetNumberOverride(arbiter.BoardCount(), 0, 5)
                && !arbiter.ClearOverrides(-1) && !arbiter.SetNumberOverride(0, ChessArbiter.NumberCount, 5),
            "no board: setters refuse", "");
        CheckBoard(-1, defaults, defaultText, "no board: still the defaults");

        // Board 1 overrides some bits on and some off: takebacks and agreement flipped, threefold overridden to its
        // default value, mode forced on and clock forced off.
        arbiter.SetSettingOverride(1, t, (defaults & t) == 0);
        arbiter.SetSettingOverride(1, a, (defaults & a) == 0);
        arbiter.SetSettingOverride(1, r, (defaults & r) != 0);
        arbiter.SetSettingOverride(1, m, true);
        arbiter.SetSettingOverride(1, c, false);
        int expected = ((defaults ^ t ^ a) | m) & ~c;
        CheckBoard(1, expected, defaultText, "bits: board 1 overrides some on, some off");
        CheckBoard(0, defaults, defaultText, "bits: board 0 unaffected");
        CheckBoard(2, defaults, defaultText, "bits: board 2 unaffected");
        Check(arbiter.IsSettingOverridden(1, t) && arbiter.IsSettingOverridden(1, r)
                && arbiter.IsSettingOverridden(1, c) && !arbiter.IsSettingOverridden(1, ChessArbiter.SettingFiftyMoveRule)
                && !arbiter.IsSettingOverridden(0, t),
            "bits: overridden flags", "");
        Check(arbiter.SettingOverrideValue(1, m) && !arbiter.SettingOverrideValue(1, c)
                && arbiter.SettingOverrideValue(1, t) == ((defaults & t) == 0),
            "bits: override values", "");
        CheckInt(arbiter.IsEffectiveOn(1, t) ? 1 : 0, (defaults & t) == 0 ? 1 : 0, "bits: IsEffectiveOn");
        arbiter.ClearSettingOverride(1, t);
        expected = (expected & ~t) | (defaults & t);
        CheckBoard(1, expected, defaultText, "bits: takebacks back to the default");
        Check(!arbiter.IsSettingOverridden(1, t), "bits: takebacks no longer overridden", "");
        arbiter.SetSettingOverride(1, ChessArbiter.SettingsDrawRules, false);
        expected &= ~ChessArbiter.SettingsDrawRules;
        CheckBoard(1, expected, defaultText, "bits: no draws on board 1");
        arbiter.SetSettingOverride(1, ChessArbiter.SettingsDrawRules, true);
        expected |= ChessArbiter.SettingsDrawRules;
        CheckBoard(1, expected, defaultText, "bits: all draws on board 1");
        CheckBoard(0, defaults, defaultText, "bits: board 0 still unaffected");

        // Board 2 overrides each numeric setting on its own, then all of them.
        int[] values = {
            defaultNumbers[0] == 7 ? 8 : 7, defaultNumbers[1] == 70 ? 80 : 70, 1 - defaultNumbers[2],
            defaultNumbers[3] == 7 ? 8 : 7, defaultNumbers[4] == 3 ? 4 : 3
        };
        for (int i = 0; i < ChessArbiter.NumberCount; i++) {
            arbiter.ClearOverrides(2);
            arbiter.SetNumberOverride(2, i, values[i]);
            int[] expectedNumbers = Copy(defaultNumbers);
            expectedNumbers[i] = values[i];
            ChessArbiter.FixNumbers(expectedNumbers);
            CheckBoard(2, defaults, ChessArbiter.NumbersToString(expectedNumbers), "numbers: board 2 overrides " + i);
            CheckInt(arbiter.EffectiveNumber(2, i), expectedNumbers[i], "numbers: board 2's number " + i);
            Check(arbiter.IsNumberOverridden(2, i) && arbiter.NumberOverrideValue(2, i) == values[i]
                    && CountNumberOverrides(2) == 1, "numbers: only number " + i + " overridden", "");
            CheckBoard(0, defaults, defaultText, "numbers: board 0 unaffected by " + i);
            CheckBoard(1, expected, defaultText, "numbers: board 1 unaffected by " + i);
        }
        for (int i = 0; i < ChessArbiter.NumberCount; i++) arbiter.SetNumberOverride(2, i, values[i]);
        CheckBoard(2, defaults, ChessArbiter.NumbersToString(values), "numbers: board 2 overrides all");
        arbiter.ClearNumberOverride(2, ChessArbiter.NumberForcedIncrement);
        int[] partly = Copy(values);
        partly[ChessArbiter.NumberForcedIncrement] = defaultNumbers[ChessArbiter.NumberForcedIncrement];
        CheckBoard(2, defaults, ChessArbiter.NumbersToString(partly), "numbers: increment back to the default");

        // Overridden and default times that conflict are corrected together.
        arbiter.ClearOverrides(2);
        arbiter.SetNumberOverride(2, ChessArbiter.NumberResetAvailableTime, 50);
        arbiter.SetNumberOverride(2, ChessArbiter.NumberAutoResetTime, 5);
        CheckInt(arbiter.EffectiveNumber(2, ChessArbiter.NumberResetAvailableTime), 5,
            "numbers: reset-available clamped to an overridden auto-reset");
        arbiter.SetNumberOverride(2, ChessArbiter.NumberAutoResetTime, ChessArbiter.Never);
        CheckInt(arbiter.EffectiveNumber(2, ChessArbiter.NumberResetAvailableTime), 50,
            "numbers: reset-available kept with auto-reset never");
        arbiter.SetNumberOverride(2, ChessArbiter.NumberForcedBaseTime, -9);
        CheckInt(arbiter.EffectiveNumber(2, ChessArbiter.NumberForcedBaseTime), ChessArbiter.NoClock,
            "numbers: invalid override corrected");
        CheckInt(arbiter.NumberOverrideValue(2, ChessArbiter.NumberForcedBaseTime), -9,
            "numbers: override kept as given");

        // Everything back to the defaults.
        for (int b = 0; b < 3; b++) arbiter.ClearOverrides(b);
        bool clear = true;
        for (int b = 0; b < 3; b++) {
            if (arbiter.overrideMask[b] != 0 || arbiter.EffectiveSettings(b) != defaults) clear = false;
        }
        Check(clear, "cleared: boards 0 to 2 have the defaults", "");
        CheckText(ChessArbiter.NumbersToString(arbiter.defaultNumbers), defaultText, "cleared: defaults unchanged");
    }

    void CheckBoard(int board, int expectedSettings, string expectedNumbers, string name) {
        int[] numbers = new int[ChessArbiter.NumberCount];
        arbiter.EffectiveNumbers(board, numbers);
        int settings = arbiter.EffectiveSettings(board);
        string numberText = ChessArbiter.NumbersToString(numbers);
        Check(settings == expectedSettings && numberText == expectedNumbers, name,
            "expected " + ChessArbiter.SettingsToString(expectedSettings) + "; " + expectedNumbers + ", got "
            + ChessArbiter.SettingsToString(settings) + "; " + numberText);
    }

    int CountNumberOverrides(int board) {
        int count = 0;
        for (int i = 0; i < ChessArbiter.NumberCount; i++) {
            if (arbiter.IsNumberOverridden(board, i)) count++;
        }
        return count;
    }

    static int[] Copy(int[] array) {
        int[] copy = new int[array.Length];
        for (int i = 0; i < array.Length; i++) copy[i] = array[i];
        return copy;
    }

    // ---- 6.2a ----

    // The built-in authorizations for the local player, who in Play Mode is the first joiner and the instance master,
    // and the arbiter's default wiring: its simpleAuth is the FirstJoinerAuth on its own object.
    void TestAuth() {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) {
            Check(false, "auth", "no local player");
            return;
        }
        Debug.Log("[SettingsTest] local player: " + local.displayName + (local.isMaster ? " (master)" : ""));

        if (!Utilities.IsValid(arbiter)) {
            Check(false, "auth: default wiring", "arbiter not assigned");
        } else {
            FirstJoinerAuth firstJoinerAuth = arbiter.GetComponent<FirstJoinerAuth>();
            Check(Utilities.IsValid(firstJoinerAuth), "auth: the arbiter has a FirstJoinerAuth", "none on its object");
            Check(Utilities.IsValid(arbiter.simpleAuth) && arbiter.simpleAuth == firstJoinerAuth,
                "auth: the arbiter's simpleAuth is its FirstJoinerAuth",
                Utilities.IsValid(arbiter.simpleAuth) ? "it is " + arbiter.simpleAuth.GetUdonTypeName() : "it is empty");
            if (Utilities.IsValid(firstJoinerAuth)) {
                CheckText(firstJoinerAuth.firstJoiner, local.displayName, "auth: first joiner is the local player");
                Check(firstJoinerAuth.IsAuthorized(local), "auth: FirstJoinerAuth authorizes the local player", "");
                Check(!firstJoinerAuth.IsAuthorized(null), "auth: FirstJoinerAuth refuses no player", "");
            }
        }

        if (!Utilities.IsValid(instanceMasterAuth)) {
            Check(false, "auth: InstanceMasterAuth", "none on the test object");
        } else {
            Check(local.isMaster, "auth: the local player is master", "");
            Check(instanceMasterAuth.IsAuthorized(local), "auth: InstanceMasterAuth authorizes the local player", "");
            Check(!instanceMasterAuth.IsAuthorized(null), "auth: InstanceMasterAuth refuses no player", "");
        }

        if (!Utilities.IsValid(noOneAuth)) {
            Check(false, "auth: NoOneAuth", "none on the test object");
        } else {
            Check(!noOneAuth.IsAuthorized(local), "auth: NoOneAuth refuses the local player", "");
            Check(!noOneAuth.IsAuthorized(null), "auth: NoOneAuth refuses no player", "");
        }
    }

    // ---- 6.2c ----

    // The arbiter's organizer answer and its fallback for an empty simpleAuth. With the field set (as it should be),
    // IsOrganizer gives simpleAuth's answer, and emptying the field for a moment makes it refuse everyone. With the
    // field emptied in the editor (6.2c's manual check; TestAuth's wiring check then fails), the arbiter must have
    // noticed it on Start and refuse everyone.
    void TestAuthFallback() {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(arbiter)) {
            Check(false, "auth fallback", "arbiter not assigned");
            return;
        }
        SimpleAuth auth = arbiter.simpleAuth;
        if (!Utilities.IsValid(auth)) {
            Debug.Log("[SettingsTest] the arbiter's simpleAuth is empty: checking the in-game fallback");
            Check(arbiter.simpleAuthMissing, "auth fallback: the arbiter noticed the empty field on Start", "");
            Check(!arbiter.IsOrganizer(local), "auth fallback: empty field, the local player isn't an organizer", "");
            Check(!arbiter.IsOrganizer(null), "auth fallback: empty field, no player isn't an organizer", "");
            return;
        }
        Check(!arbiter.simpleAuthMissing, "auth fallback: field set, the arbiter reports it present", "");
        Check(arbiter.IsOrganizer(local) == auth.IsAuthorized(local),
            "auth fallback: field set, IsOrganizer is simpleAuth's answer for the local player",
            "IsOrganizer " + arbiter.IsOrganizer(local) + ", simpleAuth " + auth.IsAuthorized(local));
        Check(arbiter.IsOrganizer(local), "auth fallback: field set, the local player is an organizer", "");
        Check(!arbiter.IsOrganizer(null), "auth fallback: field set, no player isn't an organizer", "");
        arbiter.simpleAuth = null;
        bool localWhileEmpty = arbiter.IsOrganizer(local);
        bool noneWhileEmpty = arbiter.IsOrganizer(null);
        arbiter.simpleAuth = auth;
        Check(!localWhileEmpty, "auth fallback: field emptied, the local player isn't an organizer", "");
        Check(!noneWhileEmpty, "auth fallback: field emptied, no player isn't an organizer", "");
        Check(arbiter.simpleAuth == auth && arbiter.IsOrganizer(local),
            "auth fallback: field restored, the local player is an organizer again", "");
    }
}
