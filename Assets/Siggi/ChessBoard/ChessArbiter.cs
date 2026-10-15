using System;
using SiggiUSTools;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The instance's rules settings (docs/settings.md): which rules apply to the boards, as one int of setting bits
    // plus five numeric settings. The instance defaults come from the fields set in the editor; organizers change
    // them in-world through the settings menu (a view of this behaviour, step 11), taking ownership of this object to
    // sync them (only organizers may own it; see BeginChange). Boards read their effective
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
        // A game whose flag falls plays on untimed at once, after ending and being logged (step 12.9, docs/clocks.md).
        public const int SettingPlayOnAfterFlagFall = 1 << 9;
        public const int SettingCount = 10;
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

        // The override mask's bits: the setting bits themselves, then one bit per numeric setting above them.
        public const int OverrideSettingsMask = (1 << SettingCount) - 1;
        public const int OverrideNumbersMask = ((1 << NumberCount) - 1) << SettingCount;

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
        [Tooltip("When a side's time runs out, the game ends and is logged, then plays on at once as an untimed game.")]
        public bool playOnAfterFlagFall = true;

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

        [Header("Organizers")]
        [Tooltip("Who may change settings in-world (docs/organizers.md). FirstJoinerAuth on this object by default; "
            + "NoOneAuth for nobody. Must not be empty.")]
        public SimpleAuth simpleAuth;

        [Header("Pieces")]
        [Tooltip("The chess piece set White wears on every board view without its own (docs/customizing.md). Must not "
            + "be empty.")]
        public ChessPieceSet defaultWhitePieceSet;
        [Tooltip("The chess piece set Black wears on every board view without its own (docs/customizing.md). Must not "
            + "be empty.")]
        public ChessPieceSet defaultBlackPieceSet;
        // The board views told when a default set changes (SetDefaultChessPieces); each adds itself on Start.
        [NonSerialized] public ChessBoardView[] pieceSetListeners = new ChessBoardView[0];

        [Header("Boards")]
        [Tooltip("How many boards can have their own overrides: board numbers 0 to this minus 1.")]
        public int boardCount = 4;

        // ---- Synced settings ----

        // The instance defaults: setting bits and the numeric settings (NumberCount values).
        [UdonSynced] [NonSerialized] public int defaultSettings;
        [UdonSynced] [NonSerialized] public int[] defaultNumbers = new int[NumberCount];

        // Per-board overrides, indexed by board number (docs/settings.md, Per-board overrides). overrideMask says what
        // the board overrides (setting bits, plus OverrideNumberBit per numeric setting); overrideSettings holds the
        // overriding setting bits and overrideNumbers the overriding numbers, NumberCount per board.
        [UdonSynced] [NonSerialized] public int[] overrideMask = new int[0];
        [UdonSynced] [NonSerialized] public int[] overrideSettings = new int[0];
        [UdonSynced] [NonSerialized] public int[] overrideNumbers = new int[0];

        // Whether simpleAuth was empty on Start (docs/organizers.md, In-game fallback): nobody is an organizer then,
        // and the settings menu (step 11) shows a message.
        [NonSerialized] public bool simpleAuthMissing;

        // Counts every change to the settings seen here: a setter below, or a serialization arriving. The settings menu
        // (step 11.1a) redraws when it moves on.
        [NonSerialized] public int changes;

        // Whether a serialization has arrived; the editor values then mustn't overwrite it.
        bool received;

        void Start() {
            // The build check (ChessArbiterBuildCheck) stops uploads with an empty field; this catches Play Mode.
            if (!Utilities.IsValid(simpleAuth)) {
                simpleAuthMissing = true;
                Debug.LogError("[ChessArbiter] " + gameObject.name + ": the Simple Auth field is empty. Set it to an "
                    + "authorization behaviour (NoOneAuth if nobody should be an organizer). Until then nobody is "
                    + "an organizer.");
            }
            if (received) return;
            defaultSettings = EditorSettings();
            defaultNumbers = EditorNumbers();
            if (FixNumbers(defaultNumbers)) {
                Debug.LogWarning("[ChessArbiter] " + gameObject.name + ": invalid numeric settings in the editor were "
                    + "corrected: " + NumbersToString(defaultNumbers));
            }
            int boards = boardCount;
            if (boards < 0) {
                Debug.LogWarning("[ChessArbiter] " + gameObject.name + ": boardCount " + boards + " is negative, "
                    + "using 0");
                boards = 0;
            }
            overrideMask = new int[boards];
            overrideSettings = new int[boards];
            overrideNumbers = new int[boards * NumberCount];
        }

        public override void OnDeserialization() {
            received = true;
            changes++;
        }

        // Makes set the default chess piece set for a ChessRules colour (steps 12.3 and 12.10), on this client only,
        // and tells each piece set listener (the board views, OnDefaultPieceSetChanged), so a view without a set of its
        // own for that side wears it at once. The arbiter only holds the set. Refuses null and an unknown colour
        // (false): a view needs a set to fall back on. Setting the same set again tells nobody.
        public bool SetDefaultChessPieces(int color, ChessPieceSet set) {
            if (!Utilities.IsValid(set) || (color != ChessRules.White && color != ChessRules.Black)) return false;
            if (set == DefaultPieceSet(color)) return true;
            if (color == ChessRules.White) defaultWhitePieceSet = set;
            else defaultBlackPieceSet = set;
            for (int i = 0; i < pieceSetListeners.Length; i++) {
                if (Utilities.IsValid(pieceSetListeners[i])) pieceSetListeners[i].OnDefaultPieceSetChanged(color);
            }
            return true;
        }

        // The default chess piece set for a ChessRules colour (null when empty or for an unknown colour).
        public ChessPieceSet DefaultPieceSet(int color) {
            if (color == ChessRules.White) return defaultWhitePieceSet;
            if (color == ChessRules.Black) return defaultBlackPieceSet;
            return null;
        }

        // Tells view of every change to a default piece set from now on (each board view adds itself on Start); adding
        // one twice changes nothing.
        public void AddPieceSetListener(ChessBoardView view) {
            if (!Utilities.IsValid(view)) return;
            int count = pieceSetListeners.Length;
            for (int i = 0; i < count; i++) {
                if (pieceSetListeners[i] == view) return;
            }
            ChessBoardView[] grown = new ChessBoardView[count + 1];
            for (int i = 0; i < count; i++) grown[i] = pieceSetListeners[i];
            grown[count] = view;
            pieceSetListeners = grown;
        }

        // Stops telling view of changes to the default piece sets.
        public void RemovePieceSetListener(ChessBoardView view) {
            int count = pieceSetListeners.Length;
            int index = -1;
            for (int i = 0; i < count && index < 0; i++) {
                if (pieceSetListeners[i] == view) index = i;
            }
            if (index < 0) return;
            ChessBoardView[] shrunk = new ChessBoardView[count - 1];
            for (int i = 0, j = 0; i < count; i++) {
                if (i != index) shrunk[j++] = pieceSetListeners[i];
            }
            pieceSetListeners = shrunk;
        }

        // How many piece set listeners there are, for tests.
        public int PieceSetListenerCount() {
            return pieceSetListeners.Length;
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
            if (playOnAfterFlagFall) bits |= SettingPlayOnAfterFlagFall;
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

        // ---- Organizers ----

        // Whether a player is an organizer (docs/organizers.md): simpleAuth's answer, or nobody if it's empty, as
        // with NoOneAuth. Everything that asks who is an organizer asks here, not simpleAuth directly.
        public bool IsOrganizer(VRCPlayerApi who) {
            if (!Utilities.IsValid(simpleAuth)) return false;
            return simpleAuth.IsAuthorized(who);
        }

        // ---- Changing settings (docs/organizers.md) ----
        // The setters below only change this client's copy. An organizer's change goes through BeginChange (taking
        // ownership), the setters, then EndChange (serializing), so every client gets it:
        //     if (!arbiter.BeginChange()) return;   // not an organizer
        //     arbiter.SetSettingOverride(board, ChessArbiter.SettingTakebacks, false);
        //     arbiter.EndChange();

        // Makes the local player the owner, if they're an organizer, and returns whether they now own the arbiter.
        // OnOwnershipRequest refuses anyone else on every client, so a non-organizer can't take it.
        public bool BeginChange() {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!IsOrganizer(local)) return false;
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(local, gameObject);
            return Networking.IsOwner(gameObject);
        }

        // Sends the settings to every client (late joiners get the last ones sent). Only the owner can.
        public bool EndChange() {
            if (!Networking.IsOwner(gameObject)) return false;
            RequestSerialization();
            return true;
        }

        // Only organizers may own the arbiter, so only they can change the synced settings. Checked on the requesting
        // client and on the owner. VRChat doesn't ask when the owner leaves: the next owner can be anyone, who then
        // owns the settings but can't change them (BeginChange refuses), and an organizer can still take it.
        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) {
            return MayOwn(requestedOwner);
        }

        // Whether a player may own the arbiter: organizers only.
        public bool MayOwn(VRCPlayerApi who) {
            return IsOrganizer(who);
        }

        // Turns the given setting bits (one or several) on or off in the instance defaults.
        public void SetDefaultSetting(int settings, bool on) {
            settings &= OverrideSettingsMask;
            if (on) defaultSettings |= settings;
            else defaultSettings &= ~settings;
            changes++;
        }

        // Sets a numeric setting's instance default, corrected by FixNumbers (which can change another one: an
        // auto-reset time below the reset-available time clamps the latter). False for an unknown number.
        public bool SetDefaultNumber(int number, int value) {
            if (number < 0 || number >= NumberCount) return false;
            defaultNumbers[number] = value;
            FixNumbers(defaultNumbers);
            changes++;
            return true;
        }

        // ---- Per-board overrides ----
        // The setters only change this client's copy (see Changing settings). They return false for a board number
        // without overrides (out of range), which then always has the defaults.

        // How many boards can have overrides (as synced, so a late joiner agrees with the owner).
        public int BoardCount() {
            return overrideMask.Length;
        }

        public bool HasBoard(int board) {
            return board >= 0 && board < overrideMask.Length;
        }

        // The override mask bit for a numeric setting.
        public static int OverrideNumberBit(int number) {
            return 1 << (SettingCount + number);
        }

        // Setting bits combined from defaults and overrides: the masked bits from values, the rest from defaults.
        public static int CombineSettings(int defaults, int mask, int values) {
            return ((defaults & ~mask) | (values & mask)) & OverrideSettingsMask;
        }

        // Overrides the given setting bits (one or several) on a board, on or off.
        public bool SetSettingOverride(int board, int settings, bool on) {
            if (!HasBoard(board)) return false;
            settings &= OverrideSettingsMask;
            overrideMask[board] |= settings;
            if (on) overrideSettings[board] |= settings;
            else overrideSettings[board] &= ~settings;
            changes++;
            return true;
        }

        // Makes the given setting bits use the defaults again on a board.
        public bool ClearSettingOverride(int board, int settings) {
            if (!HasBoard(board)) return false;
            settings &= OverrideSettingsMask;
            overrideMask[board] &= ~settings;
            overrideSettings[board] &= ~settings;
            changes++;
            return true;
        }

        // Overrides a numeric setting on a board. The value is kept as given; EffectiveNumbers corrects the result.
        public bool SetNumberOverride(int board, int number, int value) {
            if (!HasBoard(board) || number < 0 || number >= NumberCount) return false;
            overrideMask[board] |= OverrideNumberBit(number);
            overrideNumbers[board * NumberCount + number] = value;
            changes++;
            return true;
        }

        // Makes a numeric setting use the default again on a board.
        public bool ClearNumberOverride(int board, int number) {
            if (!HasBoard(board) || number < 0 || number >= NumberCount) return false;
            overrideMask[board] &= ~OverrideNumberBit(number);
            overrideNumbers[board * NumberCount + number] = 0;
            changes++;
            return true;
        }

        // Makes everything use the defaults again on a board.
        public bool ClearOverrides(int board) {
            if (!HasBoard(board)) return false;
            overrideMask[board] = 0;
            overrideSettings[board] = 0;
            for (int i = 0; i < NumberCount; i++) overrideNumbers[board * NumberCount + i] = 0;
            changes++;
            return true;
        }

        // Whether a board overrides a setting bit, and the value it overrides it with (meaningful only if overridden).
        public bool IsSettingOverridden(int board, int setting) {
            return HasBoard(board) && (overrideMask[board] & setting) != 0;
        }

        public bool SettingOverrideValue(int board, int setting) {
            return HasBoard(board) && (overrideSettings[board] & setting) != 0;
        }

        // Whether a board overrides a numeric setting, and the value it overrides it with (0 if not overridden).
        public bool IsNumberOverridden(int board, int number) {
            return HasBoard(board) && (overrideMask[board] & OverrideNumberBit(number)) != 0;
        }

        public int NumberOverrideValue(int board, int number) {
            return HasBoard(board) ? overrideNumbers[board * NumberCount + number] : 0;
        }

        // ---- Effective settings ----

        // A board's effective setting bits: its overrides, the instance defaults for the rest.
        public int EffectiveSettings(int board) {
            if (!HasBoard(board)) return defaultSettings;
            return CombineSettings(defaultSettings, overrideMask[board], overrideSettings[board]);
        }

        public bool IsEffectiveOn(int board, int setting) {
            return (EffectiveSettings(board) & setting) != 0;
        }

        // Fills numbers (NumberCount long) with a board's effective numeric settings: its overrides, the instance
        // defaults for the rest, then corrected by FixNumbers, since an overridden time can conflict with a default
        // one (e.g. an auto-reset time overridden below the default reset-available time clamps the latter).
        public void EffectiveNumbers(int board, int[] numbers) {
            bool has = HasBoard(board);
            int mask = has ? overrideMask[board] : 0;
            for (int i = 0; i < NumberCount; i++) {
                numbers[i] = (mask & OverrideNumberBit(i)) != 0 ? overrideNumbers[board * NumberCount + i]
                    : defaultNumbers[i];
            }
            FixNumbers(numbers);
        }

        public int EffectiveNumber(int board, int number) {
            int[] numbers = new int[NumberCount];
            EffectiveNumbers(board, numbers);
            return numbers[number];
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
            if ((bits & SettingPlayOnAfterFlagFall) != 0) text += " play-on";
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
