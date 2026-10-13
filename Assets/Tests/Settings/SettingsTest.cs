using SiggiChessBoard;
using SiggiUSTools;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Step 6 test behaviour (PLAN.md section 6, 8.2's single-client checks, and 11.1a's settings menu): the arbiter's
// settings, and the menu changing them.
// Logs "[SettingsTest] PASS: <check>" or "[SettingsTest] FAIL: <check>: <details>" per check, then a summary line.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SettingsTest : UdonSharpBehaviour {
    // The arbiter under test; its checks fail if it isn't assigned. Set any values on it in the editor: the checks
    // compare what it reports with them, and its report is logged for comparing by eye.
    public ChessArbiter arbiter;
    // The scene's settings menu on that arbiter (step 11.1a); set by Tools > Chess Board > Create Settings Menu.
    public ChessSettingsMenu menu;

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
        TestCombinations();
        TestChanging();
        TestMenu();
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

    // ---- 8.2 ----

    // Changing settings as an organizer, as far as one client can check it: BeginChange makes the local player (an
    // organizer) the owner, EndChange sends, and the ownership check allows organizers only. The default setters
    // change the defaults (numbers corrected). Swapping NoOneAuth in for a moment makes the local player a
    // non-organizer. Syncing to other clients needs several clients (PLAN 8.2's Verify, with the debug view).
    void TestChanging() {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(arbiter)) {
            Check(false, "changing", "arbiter not assigned");
            return;
        }
        SimpleAuth auth = arbiter.simpleAuth;
        if (!Utilities.IsValid(auth) || !arbiter.IsOrganizer(local)) {
            Check(false, "changing", "the local player isn't an organizer");
            return;
        }
        Check(arbiter.BeginChange() && Networking.IsOwner(arbiter.gameObject),
            "changing: an organizer's BeginChange makes them the owner", "");
        Check(arbiter.EndChange(), "changing: the owner's EndChange sends", "");
        Check(arbiter.MayOwn(local), "changing: ownership allowed to an organizer", "");
        Check(!arbiter.MayOwn(null), "changing: ownership refused to no player", "");
        if (Utilities.IsValid(noOneAuth)) {
            arbiter.simpleAuth = noOneAuth;
            bool begun = arbiter.BeginChange();
            bool allowed = arbiter.MayOwn(local);
            arbiter.simpleAuth = auth;
            Check(!begun, "changing: a non-organizer's BeginChange is refused", "");
            Check(!allowed, "changing: ownership refused to a non-organizer", "");
        } else {
            Check(false, "changing: non-organizer", "no NoOneAuth on the test object");
        }

        int savedSettings = arbiter.defaultSettings;
        int[] savedNumbers = Copy(arbiter.defaultNumbers);
        int t = ChessArbiter.SettingTakebacks;
        bool takebacks = arbiter.IsDefaultOn(t);
        arbiter.SetDefaultSetting(t, !takebacks);
        Check(arbiter.IsDefaultOn(t) != takebacks
                && (arbiter.defaultSettings & ~t) == (savedSettings & ~t),
            "changing: default takebacks flipped, the rest kept", ChessArbiter.SettingsToString(arbiter.defaultSettings));
        arbiter.SetDefaultSetting(ChessArbiter.SettingsDrawRules, true);
        CheckInt(arbiter.defaultSettings & ChessArbiter.SettingsDrawRules, ChessArbiter.SettingsDrawRules,
            "changing: default draw rules all on");
        arbiter.SetDefaultSetting(ChessArbiter.SettingsDrawRules | (1 << ChessArbiter.SettingCount), false);
        CheckInt(arbiter.defaultSettings & ChessArbiter.SettingsDrawRules, 0, "changing: default draw rules all off");
        arbiter.SetDefaultSetting(1 << ChessArbiter.SettingCount, true);
        CheckInt(arbiter.defaultSettings & ~ChessArbiter.OverrideSettingsMask, 0,
            "changing: no default bits outside the settings");
        Check(arbiter.SetDefaultNumber(ChessArbiter.NumberAutoResetTime, 600)
                && arbiter.SetDefaultNumber(ChessArbiter.NumberResetAvailableTime, 30),
            "changing: default times set", "");
        Check(arbiter.DefaultNumber(ChessArbiter.NumberResetAvailableTime) == 30
                && arbiter.DefaultNumber(ChessArbiter.NumberAutoResetTime) == 600,
            "changing: default times", ChessArbiter.NumbersToString(arbiter.defaultNumbers));
        arbiter.SetDefaultNumber(ChessArbiter.NumberAutoResetTime, 10);
        CheckInt(arbiter.DefaultNumber(ChessArbiter.NumberResetAvailableTime), 10,
            "changing: default auto-reset below reset-available clamps it");
        arbiter.SetDefaultNumber(ChessArbiter.NumberForcedMode, 7);
        CheckInt(arbiter.DefaultNumber(ChessArbiter.NumberForcedMode), ChessGame.ModeStandard,
            "changing: unknown default mode corrected");
        Check(!arbiter.SetDefaultNumber(ChessArbiter.NumberCount, 5) && !arbiter.SetDefaultNumber(-1, 5),
            "changing: unknown number refused", "");
        arbiter.defaultSettings = savedSettings;
        arbiter.defaultNumbers = savedNumbers;
        arbiter.EndChange();
    }

    // ---- 11.1a ----

    // The settings menu, as one client sees it: its wiring, the controls shown to an organizer only (NoOneAuth swapped
    // in for a moment), and every kind of control for all boards and for one board, checked on the arbiter, in the
    // value texts and in which buttons are shown and marked. The defaults and the overrides of every board are put
    // back at the end, and sent. Other clients receiving the changes needs several clients (PLAN 11.1a's Verify).
    void TestMenu() {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(menu)) {
            Check(false, "menu: assigned", "run Tools > Chess Board > Create Settings Menu, which sets SettingsTest's "
                + "Menu");
            return;
        }
        if (!Utilities.IsValid(arbiter)) {
            Check(false, "menu", "arbiter not assigned");
            return;
        }
        Check(menu.arbiter == arbiter, "menu: on the arbiter", "");
        CheckInt(menu.values.Length, ChessSettingsMenu.ItemCount, "menu: a value text per item");
        string missing = "";
        for (int item = ChessSettingsMenu.ItemScope; item < ChessSettingsMenu.ItemCount; item++) {
            bool setting = ChessSettingsMenu.IsSettingItem(item);
            int first = setting ? ChessSettingsMenu.ActionOn : ChessSettingsMenu.ActionPrevious;
            int second = setting ? ChessSettingsMenu.ActionOff : ChessSettingsMenu.ActionNext;
            if (MenuButton(item, first) < 0) missing += " " + item + "/" + first;
            if (MenuButton(item, second) < 0) missing += " " + item + "/" + second;
            if (MenuButton(item, ChessSettingsMenu.ActionDefault) < 0) {
                missing += " " + item + "/" + ChessSettingsMenu.ActionDefault;
            }
        }
        CheckText(missing, "", "menu: every item has its buttons, each with its image");
        Check(Utilities.IsValid(menu.controls) && Utilities.IsValid(menu.notice) && Utilities.IsValid(menu.scopeLabel),
            "menu: controls, notice and scope label assigned", "");
        SimpleAuth auth = arbiter.simpleAuth;
        if (!Utilities.IsValid(auth) || !arbiter.IsOrganizer(local)) {
            Check(false, "menu", "the local player isn't an organizer");
            return;
        }

        int savedSettings = arbiter.defaultSettings;
        int[] savedNumbers = Copy(arbiter.defaultNumbers);
        int[] savedMask = Copy(arbiter.overrideMask);
        int[] savedOverrideSettings = Copy(arbiter.overrideSettings);
        int[] savedOverrideNumbers = Copy(arbiter.overrideNumbers);
        int savedScope = menu.scope;
        int boards = arbiter.BoardCount();
        if (boards < 2) {
            Check(false, "menu", "the arbiter needs a Board Count of 2 or more for the menu checks, has " + boards);
            return;
        }

        // Known defaults, no overrides.
        arbiter.defaultSettings = ChessArbiter.SettingTakebacks;
        arbiter.defaultNumbers[ChessArbiter.NumberResetAvailableTime] = 120;
        arbiter.defaultNumbers[ChessArbiter.NumberAutoResetTime] = 600;
        arbiter.defaultNumbers[ChessArbiter.NumberForcedMode] = ChessGame.ModeStandard;
        arbiter.defaultNumbers[ChessArbiter.NumberForcedBaseTime] = 600;
        arbiter.defaultNumbers[ChessArbiter.NumberForcedIncrement] = 0;
        for (int b = 0; b < boards; b++) arbiter.ClearOverrides(b);
        menu.scope = ChessSettingsMenu.ScopeAllBoards;

        // Visibility.
        menu.UpdateVisibility();
        Check(menu.controlsShown && menu.controls.activeSelf, "menu: controls shown to an organizer", "");
        CheckText(menu.notice.text, "", "menu: no notice for an organizer");
        if (Utilities.IsValid(noOneAuth)) {
            arbiter.simpleAuth = noOneAuth;
            menu.UpdateVisibility();
            bool hidden = !menu.controlsShown && !menu.controls.activeSelf;
            string notice = menu.notice.text;
            bool pressed = menu.Press(ChessSettingsMenu.ItemTakebacks, ChessSettingsMenu.ActionOff);
            bool takebacks = arbiter.IsDefaultOn(ChessArbiter.SettingTakebacks);
            arbiter.simpleAuth = auth;
            menu.UpdateVisibility();
            Check(hidden, "menu: controls hidden from a non-organizer", "");
            CheckText(notice, "Only organizers can change these settings.", "menu: a non-organizer's notice");
            Check(!pressed && takebacks, "menu: a non-organizer's press changes nothing", "");
            Check(menu.controlsShown && menu.controls.activeSelf && menu.notice.text == "",
                "menu: controls shown again to the organizer", "");
        } else {
            Check(false, "menu: non-organizer", "no NoOneAuth on the test object");
        }

        // Redrawing.
        Check(menu.RefreshIfChanged(), "menu: redrawn after the settings changed", "");
        Check(!menu.RefreshIfChanged(), "menu: not redrawn again without a change", "");
        int changesBefore = arbiter.changes;
        arbiter.SetDefaultSetting(ChessArbiter.SettingDeadPosition, false);
        Check(arbiter.changes != changesBefore && menu.RefreshIfChanged(), "menu: a setter counts as a change", "");

        // All boards: on and off.
        int t = ChessSettingsMenu.ItemTakebacks;
        CheckText(menu.scopeLabel.text, "All boards", "menu: all boards: scope shown");
        CheckText(menu.values[t].text, "On", "menu: all boards: takebacks shown on");
        Check(menu.IsSelected(t, ChessSettingsMenu.ActionOn) && !menu.IsSelected(t, ChessSettingsMenu.ActionOff),
            "menu: all boards: On marked", "");
        Check(ButtonColorIs(t, ChessSettingsMenu.ActionOn, menu.selectedButtonColor)
                && ButtonColorIs(t, ChessSettingsMenu.ActionOff, menu.buttonColor),
            "menu: all boards: On's button tinted, Off's not", "");
        Check(!ButtonActive(t, ChessSettingsMenu.ActionDefault) && ButtonActive(t, ChessSettingsMenu.ActionOn)
                && !ButtonActive(ChessSettingsMenu.ItemScope, ChessSettingsMenu.ActionDefault),
            "menu: all boards: no Default buttons", "");
        Check(!menu.Press(t, ChessSettingsMenu.ActionOn), "menu: all boards: pressing the current choice changes nothing",
            "");
        Check(!menu.Press(t, ChessSettingsMenu.ActionDefault), "menu: all boards: Default refused", "");
        Check(menu.Press(t, ChessSettingsMenu.ActionOff) && !arbiter.IsDefaultOn(ChessArbiter.SettingTakebacks),
            "menu: all boards: takebacks off", ChessArbiter.SettingsToString(arbiter.defaultSettings));
        Check(Networking.IsOwner(arbiter.gameObject), "menu: the organizer owns the arbiter after a change", "");
        CheckText(menu.values[t].text, "Off", "menu: all boards: takebacks shown off");
        Check(menu.IsSelected(t, ChessSettingsMenu.ActionOff) && ButtonColorIs(t, ChessSettingsMenu.ActionOff,
            menu.selectedButtonColor), "menu: all boards: Off marked", "");
        menu.Press(t, ChessSettingsMenu.ActionOn);

        // All boards: the draw rules together and apart.
        int d = ChessSettingsMenu.ItemDrawRules;
        CheckText(menu.values[d].text, "Off (No draws)", "menu: all boards: No draws shown");
        Check(menu.Press(d, ChessSettingsMenu.ActionOn), "menu: all boards: all draw rules on", "");
        CheckInt(arbiter.defaultSettings & ChessArbiter.SettingsDrawRules, ChessArbiter.SettingsDrawRules,
            "menu: all boards: every draw rule on");
        CheckText(menu.values[d].text, "On", "menu: all boards: all draw rules shown on");
        menu.Press(ChessSettingsMenu.ItemThreefoldRepetition, ChessSettingsMenu.ActionOff);
        CheckInt(arbiter.defaultSettings & ChessArbiter.SettingsDrawRules,
            ChessArbiter.SettingsDrawRules & ~ChessArbiter.SettingThreefoldRepetition,
            "menu: all boards: threefold alone off");
        CheckText(menu.values[d].text, "Mixed", "menu: all boards: draw rules shown mixed");
        CheckText(menu.values[ChessSettingsMenu.ItemThreefoldRepetition].text, "Off",
            "menu: all boards: threefold shown off");
        Check(!menu.IsSelected(d, ChessSettingsMenu.ActionOn) && !menu.IsSelected(d, ChessSettingsMenu.ActionOff),
            "menu: all boards: nothing marked for mixed draw rules", "");
        Check(menu.Press(d, ChessSettingsMenu.ActionOff), "menu: all boards: No draws pressed", "");
        CheckInt(arbiter.defaultSettings & ChessArbiter.SettingsDrawRules, 0, "menu: all boards: every draw rule off");
        Check(arbiter.IsDefaultOn(ChessArbiter.SettingTakebacks), "menu: all boards: the draw rules leave takebacks",
            "");
        Check(menu.Press(ChessSettingsMenu.ItemModeForced, ChessSettingsMenu.ActionOn)
                && arbiter.IsDefaultOn(ChessArbiter.SettingModeForced)
                && menu.Press(ChessSettingsMenu.ItemClockForced, ChessSettingsMenu.ActionOn)
                && arbiter.IsDefaultOn(ChessArbiter.SettingClockForced),
            "menu: all boards: mode and clock forced", ChessArbiter.SettingsToString(arbiter.defaultSettings));

        // All boards: times, with Never last.
        int r = ChessSettingsMenu.ItemResetAvailableTime;
        int a = ChessSettingsMenu.ItemAutoResetTime;
        CheckText(menu.values[r].text, "2 min", "menu: all boards: reset-available time shown");
        menu.Press(r, ChessSettingsMenu.ActionNext);
        CheckDefault(ChessArbiter.NumberResetAvailableTime, 300, "5 min", r, "menu: all boards: reset-available >");
        menu.Press(r, ChessSettingsMenu.ActionPrevious);
        CheckDefault(ChessArbiter.NumberResetAvailableTime, 120, "2 min", r, "menu: all boards: reset-available <");
        menu.Press(a, ChessSettingsMenu.ActionNext);
        CheckDefault(ChessArbiter.NumberAutoResetTime, 900, "15 min", a, "menu: all boards: auto-reset >");
        arbiter.SetDefaultNumber(ChessArbiter.NumberAutoResetTime, 3600);
        menu.Refresh();
        CheckText(menu.values[a].text, "1 h", "menu: all boards: an hour shown");
        menu.Press(a, ChessSettingsMenu.ActionNext);
        CheckDefault(ChessArbiter.NumberAutoResetTime, ChessArbiter.Never, "Never", a,
            "menu: all boards: auto-reset > after the longest time is Never");
        Check(!menu.Press(a, ChessSettingsMenu.ActionNext), "menu: all boards: nothing after Never", "");
        menu.Press(a, ChessSettingsMenu.ActionPrevious);
        CheckDefault(ChessArbiter.NumberAutoResetTime, 3600, "1 h", a, "menu: all boards: auto-reset < from Never");
        arbiter.SetDefaultNumber(ChessArbiter.NumberAutoResetTime, 120);
        menu.Press(a, ChessSettingsMenu.ActionPrevious);
        CheckDefault(ChessArbiter.NumberAutoResetTime, 60, "1 min", a, "menu: all boards: auto-reset < to a minute");
        CheckDefault(ChessArbiter.NumberResetAvailableTime, 60, "1 min", r,
            "menu: all boards: auto-reset below reset-available clamps it");
        arbiter.SetDefaultNumber(ChessArbiter.NumberAutoResetTime, ChessArbiter.Never);
        arbiter.SetDefaultNumber(ChessArbiter.NumberResetAvailableTime, 90);
        menu.Refresh();
        CheckText(menu.values[r].text, "1 min 30 s", "menu: all boards: a time off the list shown");
        menu.Press(r, ChessSettingsMenu.ActionNext);
        CheckDefault(ChessArbiter.NumberResetAvailableTime, 120, "2 min", r, "menu: all boards: > from off the list");
        arbiter.SetDefaultNumber(ChessArbiter.NumberResetAvailableTime, 90);
        menu.Press(r, ChessSettingsMenu.ActionPrevious);
        CheckDefault(ChessArbiter.NumberResetAvailableTime, 60, "1 min", r, "menu: all boards: < from off the list");
        menu.Press(r, ChessSettingsMenu.ActionPrevious);
        CheckDefault(ChessArbiter.NumberResetAvailableTime, 30, "30 s", r, "menu: all boards: < to the shortest time");
        Check(!menu.Press(r, ChessSettingsMenu.ActionPrevious), "menu: all boards: nothing before the shortest time",
            "");

        // All boards: the forced mode and time control.
        int m = ChessSettingsMenu.ItemForcedMode;
        int bt = ChessSettingsMenu.ItemForcedBaseTime;
        int inc = ChessSettingsMenu.ItemForcedIncrement;
        CheckText(menu.values[m].text, "Standard", "menu: all boards: forced mode shown");
        Check(!menu.Press(m, ChessSettingsMenu.ActionPrevious), "menu: all boards: nothing before Standard", "");
        menu.Press(m, ChessSettingsMenu.ActionNext);
        CheckDefault(ChessArbiter.NumberForcedMode, ChessGame.ModeChess960, "Chess960", m,
            "menu: all boards: forced mode >");
        Check(!menu.Press(m, ChessSettingsMenu.ActionNext), "menu: all boards: nothing after Chess960", "");
        CheckText(menu.values[bt].text, "10 min", "menu: all boards: forced base time shown");
        menu.Press(bt, ChessSettingsMenu.ActionPrevious);
        CheckDefault(ChessArbiter.NumberForcedBaseTime, 300, "5 min", bt, "menu: all boards: forced base time <");
        arbiter.SetDefaultNumber(ChessArbiter.NumberForcedBaseTime, 60);
        menu.Press(bt, ChessSettingsMenu.ActionPrevious);
        CheckDefault(ChessArbiter.NumberForcedBaseTime, ChessArbiter.NoClock, "No clock", bt,
            "menu: all boards: forced base time < to no clock");
        Check(!menu.Press(bt, ChessSettingsMenu.ActionPrevious), "menu: all boards: nothing before no clock", "");
        menu.Press(bt, ChessSettingsMenu.ActionNext);
        CheckDefault(ChessArbiter.NumberForcedBaseTime, 60, "1 min", bt, "menu: all boards: forced base time >");
        CheckText(menu.values[inc].text, "0 s", "menu: all boards: forced increment shown");
        menu.Press(inc, ChessSettingsMenu.ActionNext);
        CheckDefault(ChessArbiter.NumberForcedIncrement, 1, "1 s", inc, "menu: all boards: forced increment >");

        // One board.
        int defaultsNow = arbiter.defaultSettings;
        int[] numbersNow = Copy(arbiter.defaultNumbers);
        Check(menu.Press(ChessSettingsMenu.ItemScope, ChessSettingsMenu.ActionNext) && menu.scope == 0,
            "menu: scope > from all boards is board 1", "scope " + menu.scope);
        CheckText(menu.scopeLabel.text, "Board 1", "menu: board 1: scope shown");
        Check(ButtonActive(t, ChessSettingsMenu.ActionDefault)
                && ButtonActive(ChessSettingsMenu.ItemScope, ChessSettingsMenu.ActionDefault),
            "menu: board 1: Default buttons shown", "");
        CheckText(menu.values[t].text, "On (default)", "menu: board 1: takebacks from the default");
        Check(menu.IsSelected(t, ChessSettingsMenu.ActionDefault) && !menu.IsSelected(t, ChessSettingsMenu.ActionOn),
            "menu: board 1: Default marked", "");
        Check(menu.Press(t, ChessSettingsMenu.ActionOff), "menu: board 1: takebacks off pressed", "");
        Check(arbiter.IsSettingOverridden(0, ChessArbiter.SettingTakebacks)
                && !arbiter.SettingOverrideValue(0, ChessArbiter.SettingTakebacks)
                && !arbiter.IsEffectiveOn(0, ChessArbiter.SettingTakebacks),
            "menu: board 1: takebacks overridden off", "");
        Check(arbiter.defaultSettings == defaultsNow && arbiter.overrideMask[1] == 0,
            "menu: board 1: the defaults and board 2 untouched", "");
        CheckText(menu.values[t].text, "Off", "menu: board 1: takebacks shown off");
        Check(menu.IsSelected(t, ChessSettingsMenu.ActionOff) && !menu.IsSelected(t, ChessSettingsMenu.ActionDefault),
            "menu: board 1: Off marked", "");
        Check(menu.Press(t, ChessSettingsMenu.ActionDefault)
                && !arbiter.IsSettingOverridden(0, ChessArbiter.SettingTakebacks),
            "menu: board 1: takebacks back to the default", "");
        CheckText(menu.values[t].text, "On (default)", "menu: board 1: takebacks shown from the default again");
        Check(!menu.Press(t, ChessSettingsMenu.ActionDefault), "menu: board 1: Default again changes nothing", "");

        Check(menu.Press(ChessSettingsMenu.ItemDrawAgreement, ChessSettingsMenu.ActionOn)
                && arbiter.IsEffectiveOn(0, ChessArbiter.SettingDrawAgreement),
            "menu: board 1: draw by agreement on", "");
        CheckText(menu.values[d].text, "Mixed", "menu: board 1: draw rules mixed, partly overridden");
        Check(!menu.IsSelected(d, ChessSettingsMenu.ActionDefault), "menu: board 1: partly overridden, Default not marked",
            "");
        Check(menu.Press(d, ChessSettingsMenu.ActionDefault)
                && (arbiter.overrideMask[0] & ChessArbiter.SettingsDrawRules) == 0,
            "menu: board 1: draw rules all back to the default", "");
        CheckText(menu.values[d].text, "Off (No draws) (default)", "menu: board 1: No draws from the default");

        CheckText(menu.values[r].text, "30 s (default)", "menu: board 1: reset-available time from the default");
        Check(menu.IsSelected(r, ChessSettingsMenu.ActionDefault), "menu: board 1: a time from the default marked", "");
        menu.Press(r, ChessSettingsMenu.ActionNext);
        Check(arbiter.IsNumberOverridden(0, ChessArbiter.NumberResetAvailableTime)
                && arbiter.NumberOverrideValue(0, ChessArbiter.NumberResetAvailableTime) == 60
                && arbiter.DefaultNumber(ChessArbiter.NumberResetAvailableTime) == 30,
            "menu: board 1: reset-available time overridden",
            "override " + arbiter.NumberOverrideValue(0, ChessArbiter.NumberResetAvailableTime));
        CheckText(menu.values[r].text, "1 min", "menu: board 1: overridden time shown");
        Check(!menu.IsSelected(r, ChessSettingsMenu.ActionDefault), "menu: board 1: overridden time, Default unmarked",
            "");
        menu.Press(a, ChessSettingsMenu.ActionPrevious);
        CheckInt(arbiter.EffectiveNumber(0, ChessArbiter.NumberAutoResetTime), 3600,
            "menu: board 1: auto-reset < from Never");
        menu.Press(bt, ChessSettingsMenu.ActionNext);
        CheckInt(arbiter.EffectiveNumber(0, ChessArbiter.NumberForcedBaseTime), 180,
            "menu: board 1: forced base time > from the default");
        Check(arbiter.defaultNumbers[ChessArbiter.NumberAutoResetTime] == numbersNow[ChessArbiter.NumberAutoResetTime]
                && arbiter.defaultNumbers[ChessArbiter.NumberForcedBaseTime]
                    == numbersNow[ChessArbiter.NumberForcedBaseTime]
                && !arbiter.IsNumberOverridden(1, ChessArbiter.NumberAutoResetTime),
            "menu: board 1: the default times and board 2 untouched", "");
        Check(menu.Press(r, ChessSettingsMenu.ActionDefault)
                && !arbiter.IsNumberOverridden(0, ChessArbiter.NumberResetAvailableTime),
            "menu: board 1: reset-available time back to the default", "");
        CheckText(menu.values[r].text, "30 s (default)", "menu: board 1: time shown from the default again");
        Check(menu.Press(ChessSettingsMenu.ItemScope, ChessSettingsMenu.ActionDefault) && arbiter.overrideMask[0] == 0,
            "menu: board 1: All default clears its overrides", "mask " + arbiter.overrideMask[0]);
        Check(!menu.Press(ChessSettingsMenu.ItemScope, ChessSettingsMenu.ActionDefault),
            "menu: board 1: All default with nothing to clear changes nothing", "");

        // Scope.
        menu.Press(ChessSettingsMenu.ItemScope, ChessSettingsMenu.ActionNext);
        CheckText(menu.scopeLabel.text, "Board 2", "menu: scope > to board 2");
        menu.Press(t, ChessSettingsMenu.ActionOff);
        Check(arbiter.IsSettingOverridden(1, ChessArbiter.SettingTakebacks) && arbiter.overrideMask[0] == 0,
            "menu: board 2: its own override", "");
        menu.Press(ChessSettingsMenu.ItemScope, ChessSettingsMenu.ActionPrevious);
        menu.Press(ChessSettingsMenu.ItemScope, ChessSettingsMenu.ActionPrevious);
        Check(menu.scope == ChessSettingsMenu.ScopeAllBoards && menu.scopeLabel.text == "All boards",
            "menu: scope < back to all boards", "scope " + menu.scope);
        menu.Press(ChessSettingsMenu.ItemScope, ChessSettingsMenu.ActionPrevious);
        CheckText(menu.scopeLabel.text, "Board " + boards, "menu: scope < from all boards wraps to the last board");
        menu.Press(ChessSettingsMenu.ItemScope, ChessSettingsMenu.ActionNext);
        CheckInt(menu.scope, ChessSettingsMenu.ScopeAllBoards, "menu: scope > from the last board wraps to all boards");
        menu.scope = boards;
        menu.Refresh();
        CheckInt(menu.scope, ChessSettingsMenu.ScopeAllBoards, "menu: a board without overrides shows all boards");

        // Texts.
        CheckText(ChessSettingsMenu.DurationText(45) + "|" + ChessSettingsMenu.DurationText(5400) + "|"
                + ChessSettingsMenu.DurationText(7200) + "|" + ChessSettingsMenu.DurationText(0),
            "45 s|90 min|2 h|0 s", "menu: durations");

        arbiter.defaultSettings = savedSettings;
        arbiter.defaultNumbers = savedNumbers;
        arbiter.overrideMask = savedMask;
        arbiter.overrideSettings = savedOverrideSettings;
        arbiter.overrideNumbers = savedOverrideNumbers;
        arbiter.changes++;
        arbiter.EndChange();
        menu.scope = savedScope;
        menu.Refresh();
    }

    // A default number after a menu press, and its value text.
    void CheckDefault(int number, int expected, string expectedText, int item, string name) {
        CheckInt(arbiter.DefaultNumber(number), expected, name);
        CheckText(menu.values[item].text, expectedText, name + ": shown");
    }

    // The index of the menu's button for an item and action with its object and image, -1 for none.
    int MenuButton(int item, int action) {
        for (int i = 0; i < menu.buttons.Length && i < menu.buttonItems.Length && i < menu.buttonActions.Length; i++) {
            if (menu.buttonItems[i] != item || menu.buttonActions[i] != action) continue;
            if (!Utilities.IsValid(menu.buttons[i]) || i >= menu.buttonImages.Length
                || !Utilities.IsValid(menu.buttonImages[i])) {
                return -1;
            }
            return i;
        }
        return -1;
    }

    bool ButtonActive(int item, int action) {
        int i = MenuButton(item, action);
        return i >= 0 && menu.buttons[i].activeSelf;
    }

    bool ButtonColorIs(int item, int action, Color color) {
        int i = MenuButton(item, action);
        return i >= 0 && menu.buttonImages[i].color == color;
    }

    // ---- 6.3 ----

    // Effective settings for combinations of instance defaults and overrides. The test swaps in each set of defaults
    // below for a moment (the arbiter's own ones last), and for each gives board 1 every override pattern, board 0 the
    // next pattern and board 2 none. The expected values are worked out bit by bit and number by number here, not with
    // CombineSettings, then corrected with FixNumbers (checked on its own in 6.1a). Defaults and boards 0 to 2 are put
    // back at the end.
    void TestCombinations() {
        if (!Utilities.IsValid(arbiter)) {
            Check(false, "combinations", "arbiter not assigned");
            return;
        }
        if (arbiter.BoardCount() < 3) {
            Check(false, "combinations", "the arbiter's boardCount is " + arbiter.BoardCount() + ", needs at least 3");
            return;
        }
        int savedSettings = arbiter.defaultSettings;
        int[] savedNumbers = arbiter.defaultNumbers;
        int n = ChessArbiter.NumberCount;
        int t = ChessArbiter.SettingTakebacks;
        int r = ChessArbiter.SettingThreefoldRepetition;
        int m = ChessArbiter.SettingModeForced;
        int c = ChessArbiter.SettingClockForced;
        int all = ChessArbiter.OverrideSettingsMask;

        // Defaults: the editor's starting values, everything on with every number at its "off" value, a mix (valid,
        // as defaults always are after correction), and the arbiter's own.
        int[] defaultSets = { t, all, t | r | c, savedSettings };
        int[] defaultNumberSets = {
            120, 600, ChessGame.ModeStandard, 600, 0,
            ChessArbiter.Never, ChessArbiter.Never, ChessGame.ModeChess960, ChessArbiter.NoClock, 0,
            30, 60, ChessGame.ModeChess960, 180, 2,
            savedNumbers[0], savedNumbers[1], savedNumbers[2], savedNumbers[3], savedNumbers[4]
        };

        // Override patterns: setting mask and values (value bits outside the mask must not count), then which numbers
        // are overridden (bit per number) and with what. The numbers include ones that conflict with some defaults
        // (auto-reset 10 below a default reset-available time, reset-available 900 above one) and invalid ones.
        int[] maskSets = { 0, all, all, ChessArbiter.SettingsDrawRules, t | m | c, 0x155 };
        int[] valueSets = { all, 0, all, 0, m | r, 0x0AA | c };
        int[] numberMaskSets = { 0, 0x1F, 1 << 1, (1 << 0) | (1 << 3), 0x1F, (1 << 2) | (1 << 4) };
        int[] numberValueSets = {
            5, 5, 5, 5, 5,
            900, 300, ChessGame.ModeChess960, 5, 0,
            0, 10, 0, 0, 0,
            ChessArbiter.Never, 0, 0, 0, 0,
            -4, -1, 7, -9, -2,
            0, 0, ChessGame.ModeStandard, 0, 30
        };
        int patterns = maskSets.Length;

        for (int d = 0; d < defaultSets.Length; d++) {
            arbiter.defaultSettings = defaultSets[d];
            int[] defaults = new int[n];
            for (int i = 0; i < n; i++) defaults[i] = defaultNumberSets[d * n + i];
            arbiter.defaultNumbers = defaults;
            for (int p = 0; p < patterns; p++) {
                int q = (p + 1) % patterns;
                for (int b = 0; b < 3; b++) arbiter.ClearOverrides(b);
                ApplyPattern(1, maskSets[p], valueSets[p], numberMaskSets[p], numberValueSets, p);
                ApplyPattern(0, maskSets[q], valueSets[q], numberMaskSets[q], numberValueSets, q);
                string problems = BoardProblems(1, defaultSets[d], defaults, maskSets[p], valueSets[p],
                        numberMaskSets[p], numberValueSets, p)
                    + BoardProblems(0, defaultSets[d], defaults, maskSets[q], valueSets[q], numberMaskSets[q],
                        numberValueSets, q)
                    + BoardProblems(2, defaultSets[d], defaults, 0, 0, 0, numberValueSets, 0);
                Check(problems.Length == 0, "combinations: defaults " + d + ", board 1 pattern " + p
                    + ", board 0 pattern " + q, problems);
            }
        }

        // Back to the arbiter's own defaults, nothing overridden.
        for (int b = 0; b < 3; b++) arbiter.ClearOverrides(b);
        arbiter.defaultSettings = savedSettings;
        arbiter.defaultNumbers = savedNumbers;
        Check(arbiter.defaultSettings == savedSettings && arbiter.defaultNumbers == savedNumbers
                && arbiter.EffectiveSettings(1) == savedSettings && arbiter.overrideMask[0] == 0
                && arbiter.overrideMask[1] == 0,
            "combinations: defaults restored, boards cleared", "");
    }

    // Gives a board one override pattern through the arbiter's setters.
    void ApplyPattern(int board, int mask, int values, int numberMask, int[] numberValueSets, int pattern) {
        arbiter.SetSettingOverride(board, mask & values, true);
        arbiter.SetSettingOverride(board, mask & ~values, false);
        for (int i = 0; i < ChessArbiter.NumberCount; i++) {
            if ((numberMask & (1 << i)) != 0) {
                arbiter.SetNumberOverride(board, i, numberValueSets[pattern * ChessArbiter.NumberCount + i]);
            }
        }
    }

    // What's wrong with a board's effective settings for the given defaults and override pattern, or "" if nothing.
    string BoardProblems(int board, int defaults, int[] defaultNumbers, int mask, int values, int numberMask,
            int[] numberValueSets, int pattern) {
        int expected = 0;
        for (int i = 0; i < ChessArbiter.SettingCount; i++) {
            int bit = 1 << i;
            int from = (mask & bit) != 0 ? values : defaults;
            if ((from & bit) != 0) expected |= bit;
        }
        int[] expectedNumbers = new int[ChessArbiter.NumberCount];
        for (int i = 0; i < ChessArbiter.NumberCount; i++) {
            expectedNumbers[i] = (numberMask & (1 << i)) != 0
                ? numberValueSets[pattern * ChessArbiter.NumberCount + i] : defaultNumbers[i];
        }
        ChessArbiter.FixNumbers(expectedNumbers);
        int[] numbers = new int[ChessArbiter.NumberCount];
        arbiter.EffectiveNumbers(board, numbers);
        int settings = arbiter.EffectiveSettings(board);
        string expectedText = ChessArbiter.NumbersToString(expectedNumbers);
        string numberText = ChessArbiter.NumbersToString(numbers);
        string problems = "";
        if (settings != expected) {
            problems += " board " + board + " settings: expected " + ChessArbiter.SettingsToString(expected)
                + ", got " + ChessArbiter.SettingsToString(settings) + ";";
        }
        if (numberText != expectedText) {
            problems += " board " + board + " numbers: expected " + expectedText + ", got " + numberText + ";";
        }
        for (int i = 0; i < ChessArbiter.SettingCount; i++) {
            int bit = 1 << i;
            if (arbiter.IsEffectiveOn(board, bit) != ((expected & bit) != 0)) {
                problems += " board " + board + " IsEffectiveOn " + ChessArbiter.SettingsToString(bit) + ";";
            }
        }
        return problems;
    }
}
