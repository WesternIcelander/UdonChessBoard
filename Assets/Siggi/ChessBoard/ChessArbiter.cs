using System;
using UdonSharp;
using UnityEngine;

namespace SiggiChessBoard {
    // The instance's rules settings (docs/settings.md): which rules apply to the boards, as one int of setting bits
    // plus five numeric settings. The instance defaults come from the fields set in the editor; organizers change
    // them in-world through the settings menu (a view of this behaviour, step 11). Boards read their effective
    // settings from here when they take a snapshot (step 7). Holds no game and touches no world objects.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ChessArbiter : UdonSharpBehaviour {
        // Setting bits. "No draws" is every draw rule off.
        public const int SettingTakebacks = 1 << 0;
        public const int SettingDrawAgreement = 1 << 1;
        public const int SettingThreefoldRepetition = 1 << 2;
        public const int SettingFiftyMoveRule = 1 << 3;
        public const int SettingFivefoldRepetition = 1 << 4;
        public const int SettingSeventyFiveMoveRule = 1 << 5;
        public const int SettingDeadPosition = 1 << 6;
        // The lobby settings forced on the players (docs/board-lifecycle.md, Starting): the mode and the time
        // control are then the forced mode and forced base time and increment, and the players can't change them.
        public const int SettingModeForced = 1 << 7;
        public const int SettingClockForced = 1 << 8;
        public const int SettingCount = 9;
        public const int SettingsDrawRules = SettingDrawAgreement | SettingThreefoldRepetition | SettingFiftyMoveRule
            | SettingFivefoldRepetition | SettingSeventyFiveMoveRule | SettingDeadPosition;

        // Numeric settings, indices into a numbers array.
        // Inactivity times in seconds (docs/inactivity.md), Never for never.
        public const int NumberResetAvailableTime = 0;
        public const int NumberAutoResetTime = 1;
        // A ChessGame.Mode* value, used only while SettingModeForced is on.
        public const int NumberForcedMode = 2;
        // In seconds, base NoClock for no clock; used only while SettingClockForced is on.
        public const int NumberForcedBaseTime = 3;
        public const int NumberForcedIncrement = 4;
        public const int NumberCount = 5;

        public const int Never = -1;
        public const int NoClock = -1;

        // ---- Instance defaults, set in the editor ----
        // Read once on Start into the synced defaults below; changing them afterwards does nothing.

        [Header("Rules")]
        public bool takebacksAllowed = true;
        [Tooltip("All draw rules off is \"No draws\".")]
        public bool drawAgreement;
        public bool threefoldRepetition;
        public bool fiftyMoveRule;
        public bool fivefoldRepetition;
        public bool seventyFiveMoveRule;
        public bool deadPosition;

        [Header("Inactivity (seconds, -1 = never)")]
        [Tooltip("Idle time after which anyone can reset a board. Clamped to the auto-reset time when both are set.")]
        public int resetAvailableTime = 120;
        [Tooltip("Idle time after which a board resets itself.")]
        public int autoResetTime = 600;

        [Header("Forced lobby settings")]
        public bool modeForced;
        [Tooltip("0 = Standard, 1 = Chess960.")]
        public int forcedMode = ChessGame.ModeStandard;
        public bool clockForced;
        [Tooltip("Seconds, -1 = no clock.")]
        public int forcedBaseTime = 600;
        [Tooltip("Seconds added per move.")]
        public int forcedIncrement;

        // ---- Synced settings ----

        // The instance defaults: setting bits and the numeric settings (NumberCount values).
        [UdonSynced] [NonSerialized] public int defaultSettings;
        [UdonSynced] [NonSerialized] public int[] defaultNumbers = new int[NumberCount];

        // Whether a serialization has arrived; the editor values then mustn't overwrite it.
        bool received;

        void Start() {
            if (received) return;
            defaultSettings = EditorSettings();
            defaultNumbers = EditorNumbers();
            if (FixNumbers(defaultNumbers)) {
                Debug.LogWarning("[ChessArbiter] " + gameObject.name + ": invalid numeric settings in the editor were "
                    + "corrected: " + NumbersToString(defaultNumbers));
            }
        }

        public override void OnDeserialization() {
            received = true;
        }

        // The setting bits from the editor fields.
        public int EditorSettings() {
            int bits = 0;
            if (takebacksAllowed) bits |= SettingTakebacks;
            if (drawAgreement) bits |= SettingDrawAgreement;
            if (threefoldRepetition) bits |= SettingThreefoldRepetition;
            if (fiftyMoveRule) bits |= SettingFiftyMoveRule;
            if (fivefoldRepetition) bits |= SettingFivefoldRepetition;
            if (seventyFiveMoveRule) bits |= SettingSeventyFiveMoveRule;
            if (deadPosition) bits |= SettingDeadPosition;
            if (modeForced) bits |= SettingModeForced;
            if (clockForced) bits |= SettingClockForced;
            return bits;
        }

        // The numeric settings from the editor fields, as entered (not yet corrected by FixNumbers).
        public int[] EditorNumbers() {
            int[] numbers = new int[NumberCount];
            numbers[NumberResetAvailableTime] = resetAvailableTime;
            numbers[NumberAutoResetTime] = autoResetTime;
            numbers[NumberForcedMode] = forcedMode;
            numbers[NumberForcedBaseTime] = forcedBaseTime;
            numbers[NumberForcedIncrement] = forcedIncrement;
            return numbers;
        }

        // Whether a setting bit is on in the instance defaults.
        public bool IsDefaultOn(int setting) {
            return (defaultSettings & setting) != 0;
        }

        // A numeric setting's instance default.
        public int DefaultNumber(int number) {
            return defaultNumbers[number];
        }

        // Makes numeric settings valid, returning whether anything changed: any negative time becomes Never (or
        // NoClock), the reset-available time is at most the auto-reset time when both are times (docs/inactivity.md),
        // an unknown forced mode becomes Standard, and a negative increment becomes 0.
        public static bool FixNumbers(int[] numbers) {
            bool changed = false;
            if (numbers[NumberResetAvailableTime] < Never) {
                numbers[NumberResetAvailableTime] = Never;
                changed = true;
            }
            if (numbers[NumberAutoResetTime] < Never) {
                numbers[NumberAutoResetTime] = Never;
                changed = true;
            }
            if (numbers[NumberResetAvailableTime] != Never && numbers[NumberAutoResetTime] != Never
                    && numbers[NumberResetAvailableTime] > numbers[NumberAutoResetTime]) {
                numbers[NumberResetAvailableTime] = numbers[NumberAutoResetTime];
                changed = true;
            }
            if (numbers[NumberForcedMode] != ChessGame.ModeStandard && numbers[NumberForcedMode] != ChessGame.ModeChess960) {
                numbers[NumberForcedMode] = ChessGame.ModeStandard;
                changed = true;
            }
            if (numbers[NumberForcedBaseTime] < NoClock) {
                numbers[NumberForcedBaseTime] = NoClock;
                changed = true;
            }
            if (numbers[NumberForcedIncrement] < 0) {
                numbers[NumberForcedIncrement] = 0;
                changed = true;
            }
            return changed;
        }

        // Setting bits as text, e.g. "takebacks threefold", or "none".
        public static string SettingsToString(int bits) {
            string text = "";
            if ((bits & SettingTakebacks) != 0) text += " takebacks";
            if ((bits & SettingDrawAgreement) != 0) text += " agreement";
            if ((bits & SettingThreefoldRepetition) != 0) text += " threefold";
            if ((bits & SettingFiftyMoveRule) != 0) text += " fifty-move";
            if ((bits & SettingFivefoldRepetition) != 0) text += " fivefold";
            if ((bits & SettingSeventyFiveMoveRule) != 0) text += " seventy-five-move";
            if ((bits & SettingDeadPosition) != 0) text += " dead-position";
            if ((bits & SettingModeForced) != 0) text += " mode-forced";
            if ((bits & SettingClockForced) != 0) text += " clock-forced";
            return text.Length == 0 ? "none" : text.Substring(1);
        }

        // Numeric settings as text, e.g. "resetAvailable=120 autoReset=600 forcedMode=0 forcedClock=600+0".
        public static string NumbersToString(int[] numbers) {
            return "resetAvailable=" + numbers[NumberResetAvailableTime]
                + " autoReset=" + numbers[NumberAutoResetTime]
                + " forcedMode=" + numbers[NumberForcedMode]
                + " forcedClock=" + numbers[NumberForcedBaseTime] + "+" + numbers[NumberForcedIncrement];
        }
    }
}
