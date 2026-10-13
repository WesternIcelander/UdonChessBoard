using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The settings menu (docs/settings.md, Settings menu): a view of the ChessArbiter, where an organizer sets each
    // setting for all boards (the instance defaults: on or off) or for one board (on, off, or the default), the
    // inactivity times (with Never) and the forced mode and time control. Only organizers see the controls; everyone
    // else sees a notice. Each change goes through the arbiter's BeginChange and EndChange, so it reaches every client,
    // and the menu redraws whenever the arbiter's changes count moves on (a change here, or one arriving). Holds no
    // settings and no synced fields: which board it shows is the local player's own. Built in the scene by
    // Tools > Chess Board > Create Settings Menu (Editor/ChessSettingsMenuBuilder.cs).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ChessSettingsMenu : UdonSharpBehaviour {
        // The menu's items, one row each. Setting items are on/off (one or several setting bits), number items step
        // through a list of choices.
        public const int ItemTakebacks = 0;
        // Every draw rule at once: on, off ("No draws") or the default.
        public const int ItemDrawRules = 1;
        public const int ItemDrawAgreement = 2;
        public const int ItemThreefoldRepetition = 3;
        public const int ItemFiftyMoveRule = 4;
        public const int ItemFivefoldRepetition = 5;
        public const int ItemSeventyFiveMoveRule = 6;
        public const int ItemDeadPosition = 7;
        public const int ItemModeForced = 8;
        public const int ItemForcedMode = 9;
        public const int ItemClockForced = 10;
        public const int ItemForcedBaseTime = 11;
        public const int ItemForcedIncrement = 12;
        public const int ItemResetAvailableTime = 13;
        public const int ItemAutoResetTime = 14;
        public const int ItemCount = 15;
        // The scope row: Previous and Next pick the boards the menu shows, Default clears the board's overrides.
        public const int ItemScope = -1;

        // A button's action. Setting items have On, Off and Default; number items Previous, Next and Default; Default
        // only for a single board, where it makes the board use the instance default again.
        public const int ActionOn = 0;
        public const int ActionOff = 1;
        public const int ActionDefault = 2;
        public const int ActionPrevious = 3;
        public const int ActionNext = 4;

        // The scope showing the instance defaults; 0 and up are board numbers.
        public const int ScopeAllBoards = -1;

        public ChessArbiter arbiter;
        public Text title;
        // Which boards the menu shows ("All boards", "Board 2").
        public Text scopeLabel;
        // Shown to everyone but organizers: why the controls are missing.
        public Text notice;
        // Everything an organizer works with, hidden from everyone else.
        public GameObject controls;
        // Each item's current value, by item.
        public Text[] values = new Text[0];
        // Every button: its object, image (tinted when it's the current choice), item and action, at the same index.
        public GameObject[] buttons = new GameObject[0];
        public Image[] buttonImages = new Image[0];
        public int[] buttonItems = new int[0];
        public int[] buttonActions = new int[0];
        public Color buttonColor = new Color(0.85f, 0.85f, 0.88f);
        public Color selectedButtonColor = new Color(1f, 0.8f, 0.3f);
        // The choices Previous and Next step through, in seconds. Never comes after the longest time.
        public int[] inactivityTimes = { 30, 60, 120, 300, 600, 900, 1800, 3600, ChessArbiter.Never };
        public int[] baseTimes = { ChessArbiter.NoClock, 60, 180, 300, 600, 900, 1800, 3600, 5400 };
        public int[] increments = { 0, 1, 2, 3, 5, 10, 15, 30 };

        // The boards shown: ScopeAllBoards or a board number. Local to this player.
        [NonSerialized] public int scope = ScopeAllBoards;
        // The arbiter's changes count when last drawn; -1 to draw on the first frame.
        [NonSerialized] public int drawnChanges = -1;
        // Whether the controls are shown to the local player (an organizer).
        [NonSerialized] public bool controlsShown;

        // How often being an organizer is checked again (FirstJoinerAuth's name arrives, the master changes).
        const float OrganizerCheckInterval = 1f;
        float nextOrganizerCheck;
        bool everChecked;

        void Update() {
            if (!everChecked || Time.time >= nextOrganizerCheck) {
                everChecked = true;
                nextOrganizerCheck = Time.time + OrganizerCheckInterval;
                UpdateVisibility();
            }
            RefreshIfChanged();
        }

        // Shows the controls only to an organizer, with the notice saying why when they're hidden.
        public void UpdateVisibility() {
            bool shown = Utilities.IsValid(arbiter) && arbiter.IsOrganizer(Networking.LocalPlayer);
            if (Utilities.IsValid(controls) && controls.activeSelf != shown) controls.SetActive(shown);
            controlsShown = shown;
            SetText(notice, NoticeText());
        }

        // Redraws if the arbiter's settings have changed since the last drawing. Returns whether it did.
        public bool RefreshIfChanged() {
            if (!Utilities.IsValid(arbiter) || arbiter.changes == drawnChanges) return false;
            Refresh();
            return true;
        }

        // Redraws the scope, every value and every button.
        public void Refresh() {
            if (!Utilities.IsValid(arbiter)) return;
            drawnChanges = arbiter.changes;
            FixScope();
            SetText(title, "Chess settings");
            SetText(scopeLabel, ScopeText());
            for (int item = 0; item < ItemCount && item < values.Length; item++) SetText(values[item], ValueText(item));
            for (int i = 0; i < buttons.Length; i++) {
                GameObject button = buttons[i];
                if (!Utilities.IsValid(button) || i >= buttonItems.Length || i >= buttonActions.Length) continue;
                int item = buttonItems[i];
                int action = buttonActions[i];
                bool shown = ButtonShown(item, action);
                if (button.activeSelf != shown) button.SetActive(shown);
                if (i < buttonImages.Length && Utilities.IsValid(buttonImages[i])) {
                    Color color = IsSelected(item, action) ? selectedButtonColor : buttonColor;
                    if (buttonImages[i].color != color) buttonImages[i].color = color;
                }
            }
            SetText(notice, NoticeText());
        }

        // ---- Pressing ----

        // A button pressed (its ChessSettingsButton): the scope row's Previous and Next pick the boards shown, locally;
        // everything else changes the arbiter's settings as an organizer, sent to every client. Returns whether
        // anything changed: false for a non-organizer, a button that doesn't apply, or a choice at the end of its list.
        public bool Press(int item, int action) {
            if (!Utilities.IsValid(arbiter)) return false;
            FixScope();
            if (item == ItemScope && (action == ActionPrevious || action == ActionNext)) {
                StepScope(action == ActionNext ? 1 : -1);
                return true;
            }
            if (!ButtonShown(item, action) || !WouldChange(item, action)) return false;
            if (!arbiter.BeginChange()) {
                // No longer an organizer: hide the controls now rather than at the next check.
                UpdateVisibility();
                return false;
            }
            bool done = Apply(item, action);
            arbiter.EndChange();
            Debug.Log("[ChessSettingsMenu] " + LocalName() + ": " + ScopeText() + ", " + ChangeText(item, action)
                + (done ? "" : " (not changed)"));
            Refresh();
            return done;
        }

        // A board the arbiter no longer has overrides for (its synced board count arrived smaller) shows All boards.
        void FixScope() {
            if (scope != ScopeAllBoards && !arbiter.HasBoard(scope)) scope = ScopeAllBoards;
        }

        // Moves the scope through All boards and each board, wrapping around.
        void StepScope(int direction) {
            int count = arbiter.BoardCount();
            int next = scope + direction;
            if (next >= count) next = ScopeAllBoards;
            if (next < ScopeAllBoards) next = count - 1;
            scope = next;
            Refresh();
        }

        // Makes the change on the arbiter's local copy (the caller sends it).
        bool Apply(int item, int action) {
            bool all = scope == ScopeAllBoards;
            if (item == ItemScope) return !all && arbiter.ClearOverrides(scope);
            int setting = ItemSetting(item);
            if (setting != 0) {
                if (action == ActionDefault) return arbiter.ClearSettingOverride(scope, setting);
                bool on = action == ActionOn;
                if (all) {
                    arbiter.SetDefaultSetting(setting, on);
                    return true;
                }
                return arbiter.SetSettingOverride(scope, setting, on);
            }
            int number = ItemNumber(item);
            if (action == ActionDefault) return arbiter.ClearNumberOverride(scope, number);
            int value = StepChoice(item, CurrentNumber(item), action == ActionNext ? 1 : -1);
            if (all) return arbiter.SetDefaultNumber(number, value);
            return arbiter.SetNumberOverride(scope, number, value);
        }

        // Whether pressing would change anything: not a choice already made, or a step past the end of the list.
        bool WouldChange(int item, int action) {
            if (item == ItemScope) return scope != ScopeAllBoards && arbiter.overrideMask[scope] != 0;
            if (IsSettingItem(item)) return !IsSelected(item, action);
            if (action == ActionDefault) return !IsSelected(item, action);
            int current = CurrentNumber(item);
            return StepChoice(item, current, action == ActionNext ? 1 : -1) != current;
        }

        // ---- Items ----

        // The setting bits an item sets, 0 for a number item.
        public static int ItemSetting(int item) {
            switch (item) {
                case ItemTakebacks: return ChessArbiter.SettingTakebacks;
                case ItemDrawRules: return ChessArbiter.SettingsDrawRules;
                case ItemDrawAgreement: return ChessArbiter.SettingDrawAgreement;
                case ItemThreefoldRepetition: return ChessArbiter.SettingThreefoldRepetition;
                case ItemFiftyMoveRule: return ChessArbiter.SettingFiftyMoveRule;
                case ItemFivefoldRepetition: return ChessArbiter.SettingFivefoldRepetition;
                case ItemSeventyFiveMoveRule: return ChessArbiter.SettingSeventyFiveMoveRule;
                case ItemDeadPosition: return ChessArbiter.SettingDeadPosition;
                case ItemModeForced: return ChessArbiter.SettingModeForced;
                case ItemClockForced: return ChessArbiter.SettingClockForced;
            }
            return 0;
        }

        // The ChessArbiter.Number* an item sets, -1 for a setting item.
        public static int ItemNumber(int item) {
            switch (item) {
                case ItemForcedMode: return ChessArbiter.NumberForcedMode;
                case ItemForcedBaseTime: return ChessArbiter.NumberForcedBaseTime;
                case ItemForcedIncrement: return ChessArbiter.NumberForcedIncrement;
                case ItemResetAvailableTime: return ChessArbiter.NumberResetAvailableTime;
                case ItemAutoResetTime: return ChessArbiter.NumberAutoResetTime;
            }
            return -1;
        }

        public static bool IsSettingItem(int item) {
            return ItemSetting(item) != 0;
        }

        // An item's row label.
        public static string ItemText(int item) {
            switch (item) {
                case ItemTakebacks: return "Takebacks";
                case ItemDrawRules: return "All draw rules";
                case ItemDrawAgreement: return "  Draw by agreement";
                case ItemThreefoldRepetition: return "  Threefold repetition (claim)";
                case ItemFiftyMoveRule: return "  Fifty-move rule (claim)";
                case ItemFivefoldRepetition: return "  Fivefold repetition";
                case ItemSeventyFiveMoveRule: return "  Seventy-five-move rule";
                case ItemDeadPosition: return "  Dead position";
                case ItemModeForced: return "Force the mode";
                case ItemForcedMode: return "  Forced mode";
                case ItemClockForced: return "Force the time control";
                case ItemForcedBaseTime: return "  Forced base time";
                case ItemForcedIncrement: return "  Forced increment";
                case ItemResetAvailableTime: return "Anyone can reset after idle";
                case ItemAutoResetTime: return "Board resets itself after idle";
            }
            return "";
        }

        // A button's label.
        public static string ActionText(int item, int action) {
            switch (action) {
                case ActionOn: return "On";
                case ActionOff: return item == ItemDrawRules ? "No draws" : "Off";
                case ActionDefault: return item == ItemScope ? "All default" : "Default";
                case ActionPrevious: return "<";
                case ActionNext: return ">";
            }
            return "";
        }

        // Whether a button is shown: Default only for a single board, and each item's own buttons (setting items On and
        // Off, number items and the scope Previous and Next).
        public bool ButtonShown(int item, int action) {
            if (action == ActionDefault) return scope != ScopeAllBoards;
            if (item == ItemScope) return action == ActionPrevious || action == ActionNext;
            if (IsSettingItem(item)) return action == ActionOn || action == ActionOff;
            return action == ActionPrevious || action == ActionNext;
        }

        // Whether a button is the current choice: On or Off when every one of the item's bits is that way (and, for a
        // board, overridden), Default when the board overrides none of it. A number item marks only Default.
        public bool IsSelected(int item, int action) {
            if (!Utilities.IsValid(arbiter) || item == ItemScope) return false;
            bool all = scope == ScopeAllBoards;
            int setting = ItemSetting(item);
            if (setting == 0) {
                return action == ActionDefault && !all && !arbiter.IsNumberOverridden(scope, ItemNumber(item));
            }
            int bits;
            if (all) {
                bits = arbiter.defaultSettings & setting;
            } else {
                int mask = arbiter.overrideMask[scope] & setting;
                if (mask == 0) return action == ActionDefault;
                if (mask != setting) return false;
                bits = arbiter.overrideSettings[scope] & setting;
            }
            if (action == ActionOn) return bits == setting;
            if (action == ActionOff) return bits == 0;
            return false;
        }

        // A number item's value as it applies: the instance default, or the board's effective value (corrected, so an
        // auto-reset time below the reset-available time shows the latter clamped).
        public int CurrentNumber(int item) {
            int number = ItemNumber(item);
            if (scope == ScopeAllBoards) return arbiter.DefaultNumber(number);
            return arbiter.EffectiveNumber(scope, number);
        }

        // The choice after (direction 1) or before (-1) a value in the item's list, or the value itself at the end.
        // A value not in the list steps to the nearest choice that way.
        public int StepChoice(int item, int value, int direction) {
            int[] choices = Choices(item);
            int rank = ChoiceRank(item, value);
            int found = value;
            int foundRank = 0;
            bool any = false;
            for (int i = 0; i < choices.Length; i++) {
                int choiceRank = ChoiceRank(item, choices[i]);
                bool beyond = direction > 0 ? choiceRank > rank : choiceRank < rank;
                if (!beyond) continue;
                bool nearer = !any || (direction > 0 ? choiceRank < foundRank : choiceRank > foundRank);
                if (nearer) {
                    found = choices[i];
                    foundRank = choiceRank;
                    any = true;
                }
            }
            return found;
        }

        int[] Choices(int item) {
            switch (item) {
                case ItemForcedMode: return new int[] { ChessGame.ModeStandard, ChessGame.ModeChess960 };
                case ItemForcedBaseTime: return baseTimes;
                case ItemForcedIncrement: return increments;
            }
            return inactivityTimes;
        }

        // Where a value stands among an item's choices: Never after every time.
        static int ChoiceRank(int item, int value) {
            bool inactivity = item == ItemResetAvailableTime || item == ItemAutoResetTime;
            return inactivity && value == ChessArbiter.Never ? int.MaxValue : value;
        }

        // ---- Texts ----

        // "All boards", or "Board 2" for board number 1.
        public string ScopeText() {
            return scope == ScopeAllBoards ? "All boards" : "Board " + (scope + 1);
        }

        // An item's value as shown: "On", "Off (No draws)", "Mixed", "2 min", "Never", "Chess960", "No clock"; for a
        // board, " (default)" after a value it takes from the instance defaults.
        public string ValueText(int item) {
            if (!Utilities.IsValid(arbiter)) return "";
            bool all = scope == ScopeAllBoards;
            int setting = ItemSetting(item);
            if (setting != 0) {
                int bits = (all ? arbiter.defaultSettings : arbiter.EffectiveSettings(scope)) & setting;
                string text = bits == setting ? "On" : bits == 0 ? (item == ItemDrawRules ? "Off (No draws)" : "Off")
                    : "Mixed";
                if (!all && (arbiter.overrideMask[scope] & setting) == 0) text += " (default)";
                return text;
            }
            string value = NumberText(item, CurrentNumber(item));
            if (!all && !arbiter.IsNumberOverridden(scope, ItemNumber(item))) value += " (default)";
            return value;
        }

        // A number item's value: "Standard", "No clock", "Never", "5 min", "0 s".
        public static string NumberText(int item, int value) {
            if (item == ItemForcedMode) return ChessBoardView.ModeText(value);
            if (item == ItemForcedBaseTime && value == ChessArbiter.NoClock) return "No clock";
            if ((item == ItemResetAvailableTime || item == ItemAutoResetTime) && value == ChessArbiter.Never) {
                return "Never";
            }
            return DurationText(value);
        }

        // Seconds as "45 s", "2 min", "1 h", or "1 min 30 s".
        public static string DurationText(int seconds) {
            if (seconds < 60) return seconds + " s";
            if (seconds % 3600 == 0) return (seconds / 3600) + " h";
            if (seconds % 60 == 0) return (seconds / 60) + " min";
            return (seconds / 60) + " min " + (seconds % 60) + " s";
        }

        // Why the controls are hidden; "" when they're shown.
        public string NoticeText() {
            if (!Utilities.IsValid(arbiter)) return "No arbiter assigned.";
            if (!controlsShown) return "Only organizers can change these settings.";
            return "";
        }

        // A change for the log: "Takebacks: Off", "Forced base time: >", "All default".
        string ChangeText(int item, int action) {
            if (item == ItemScope) return ActionText(item, action);
            string text = ItemText(item).Trim() + ": " + ActionText(item, action);
            if (!IsSettingItem(item) && action != ActionDefault) text += " (now " + ValueText(item) + ")";
            return text;
        }

        static string LocalName() {
            VRCPlayerApi local = Networking.LocalPlayer;
            return Utilities.IsValid(local) ? local.displayName : "?";
        }

        static void SetText(Text label, string text) {
            if (Utilities.IsValid(label) && label.text != text) label.text = text;
        }
    }
}
