using System;
using SiggiUSTools;
using UdonSharp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The settings menu (docs/settings.md, Settings menu): a view of the ChessArbiter, where an organizer sets each
    // setting for all boards (the instance defaults: on or off) or for one board (on, off, or the default), the
    // inactivity times (with Never) and the forced mode and time control, and makes the boards in use take the new
    // settings (a re-snapshot, step 11.1b). Only organizers see the controls; everyone else sees a notice, which also
    // says when the arbiter's Simple Auth field is empty. Each change goes through the arbiter's BeginChange and
    // EndChange, so it reaches every client, and the menu redraws whenever the arbiter's changes count moves on (a
    // change here, or one arriving). Holds no settings and no synced fields: which board it shows is the local
    // player's own. Built in the scene by Tools > Chess Board > Create Settings Menu
    // (Editor/ChessSettingsMenuBuilder.cs).
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
        // Playing on untimed after a flag fall (step 12.9).
        public const int ItemPlayOnAfterFlagFall = 13;
        public const int ItemResetAvailableTime = 14;
        public const int ItemAutoResetTime = 15;
        public const int ItemCount = 16;
        // The scope row: Previous and Next pick the boards the menu shows, Default clears the board's overrides.
        public const int ItemScope = -1;
        // The re-snapshot row (step 11.1b): On makes the boards shown that are in use take the settings now.
        public const int ItemResnapshot = -2;

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
        [Tooltip("The chess system's shared I18n (the Chess Language object), for the menu's text. Filled in by the "
            + "builders and the I18n sanity check.")]
        public I18n i18n;
        public TextMeshProUGUI title;
        // The labels that change only with the language (PLAN.md 13.5): each item's row label (by item), the scope
        // row's ("Settings for") and the re-snapshot row's, and each button's label (the same index as buttons).
        public TextMeshProUGUI[] itemLabels = new TextMeshProUGUI[0];
        public TextMeshProUGUI scopeTitle;
        public TextMeshProUGUI resnapshotTitle;
        public TextMeshProUGUI[] buttonLabels = new TextMeshProUGUI[0];
        // Which boards the menu shows ("All boards", "Board 2").
        public TextMeshProUGUI scopeLabel;
        // Shown to everyone but organizers: why the controls are missing (or, for everyone, that the arbiter's Simple
        // Auth field is empty).
        public TextMeshProUGUI notice;
        // Everything an organizer works with, hidden from everyone else.
        public GameObject controls;
        // Each item's current value, by item.
        public TextMeshProUGUI[] values = new TextMeshProUGUI[0];
        // The last re-snapshot's outcome, beside its button.
        public TextMeshProUGUI resnapshotLabel;
        // Every board on this arbiter, for re-snapshots: all of them for All boards, those with the board number shown
        // otherwise.
        public ChessBoard[] boards = new ChessBoard[0];
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
        // The last re-snapshot's outcome ("" before any, and again once the scope or the settings change), and its
        // counts (updated, not in use, refused), to write it again in another language.
        [NonSerialized] public string resnapshotText = "";
        int[] resnapshotCounts = new int[3];
        // Every string the menu shows, from i18n (ChessText.Load), loaded on first use and again on a language change.
        DataDictionary texts;

        // How often being an organizer is checked again (FirstJoinerAuth's name arrives, the master changes).
        const float OrganizerCheckInterval = 1f;
        float nextOrganizerCheck;
        bool everChecked;

        void Start() {
            if (Utilities.IsValid(i18n)) i18n.AddLanguageChangeListener(this);
        }

        // Every string the menu shows, in the language now. Public for tests.
        public DataDictionary Texts() {
            if (texts == null) texts = ChessText.Load(i18n, this);
            return texts;
        }

        // The I18n's language changed (its AddLanguageChangeListener): every text is written again.
        public void OnLanguageChange() {
            texts = ChessText.Load(i18n, this);
            if (resnapshotText.Length > 0) {
                resnapshotText = ResnapshotText(resnapshotCounts[0], resnapshotCounts[1], resnapshotCounts[2]);
            }
            Refresh();
            UpdateVisibility();
        }

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
            ChessUtil.SetText(notice, NoticeText());
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
            SetFixedTexts();
            ChessUtil.SetText(scopeLabel, ScopeText());
            for (int item = 0; item < ItemCount && item < values.Length; item++) {
                ChessUtil.SetText(values[item], ValueText(item));
            }
            ChessUtil.SetText(resnapshotLabel, resnapshotText);
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
            ChessUtil.SetText(notice, NoticeText());
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
            if (item == ItemResnapshot) return action == ActionOn && ResnapshotBoards();
            if (!ButtonShown(item, action) || !WouldChange(item, action)) return false;
            if (!arbiter.BeginChange()) {
                // No longer an organizer: hide the controls now rather than at the next check.
                UpdateVisibility();
                return false;
            }
            bool done = Apply(item, action);
            arbiter.EndChange();
            resnapshotText = "";
            Debug.Log("[ChessSettingsMenu] " + ChessUtil.LocalName("?") + ": " + ScopeLogText()
                + ", " + ChangeText(item, action)
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
            resnapshotText = "";
            Refresh();
        }

        // ---- Re-snapshot (step 11.1b) ----

        // Makes the boards shown (every board for All boards, otherwise those with the board number shown) take a new
        // snapshot of the settings as the local organizer (ChessBoard.ResnapshotAs, which takes ownership of each and
        // sends it). A reset board has no snapshot and takes the settings when it's next used, so it's only counted.
        // Returns whether any board took one; the outcome is shown beside the button and logged.
        public bool ResnapshotBoards() {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!arbiter.IsOrganizer(local)) {
                UpdateVisibility();
                return false;
            }
            int updated = 0;
            int unused = 0;
            int refused = 0;
            for (int i = 0; i < boards.Length; i++) {
                ChessBoard board = boards[i];
                if (!Utilities.IsValid(board)) continue;
                if (scope != ScopeAllBoards && board.boardNumber != scope) continue;
                if (board.lifecycle == ChessBoard.LifecycleReset) {
                    unused++;
                } else if (board.ResnapshotAs(local) == ChessBoard.InputAccepted) {
                    updated++;
                } else {
                    refused++;
                }
            }
            resnapshotCounts[0] = updated;
            resnapshotCounts[1] = unused;
            resnapshotCounts[2] = refused;
            resnapshotText = ResnapshotText(updated, unused, refused);
            ChessUtil.SetText(resnapshotLabel, resnapshotText);
            Debug.Log("[ChessSettingsMenu] " + ChessUtil.LocalName("?") + ": " + ScopeLogText() + ", re-snapshot: "
                + updated + " updated, " + unused + " not in use, " + refused + " refused");
            return updated > 0;
        }

        // "2 boards updated, 1 not in use", "No board in use: each takes the settings when next used", "No such board".
        public string ResnapshotText(int updated, int unused, int refused) {
            DataDictionary t = Texts();
            if (updated + unused + refused == 0) return ChessText.Get(t, "resnapshot_no_such_board");
            if (updated == 0 && refused == 0) return ChessText.Get(t, "resnapshot_none_in_use");
            string text = updated == 1 ? ChessText.Get(t, "resnapshot_updated_one")
                : ChessText.Get(t, "resnapshot_updated_many", "count", updated.ToString());
            if (unused > 0) text = ChessText.Get(t, "resnapshot_not_in_use", "text", text, "count", unused.ToString());
            if (refused > 0) text = ChessText.Get(t, "resnapshot_refused", "text", text, "count", refused.ToString());
            return text;
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
                case ItemPlayOnAfterFlagFall: return ChessArbiter.SettingPlayOnAfterFlagFall;
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

        // An item's row label's key in the language pack.
        public static string ItemKey(int item) {
            switch (item) {
                case ItemTakebacks: return "settings_item_takebacks";
                case ItemDrawRules: return "settings_item_draw_rules";
                case ItemDrawAgreement: return "settings_item_draw_agreement";
                case ItemThreefoldRepetition: return "settings_item_threefold_repetition";
                case ItemFiftyMoveRule: return "settings_item_fifty_move_rule";
                case ItemFivefoldRepetition: return "settings_item_fivefold_repetition";
                case ItemSeventyFiveMoveRule: return "settings_item_seventy_five_move_rule";
                case ItemDeadPosition: return "settings_item_dead_position";
                case ItemModeForced: return "settings_item_mode_forced";
                case ItemForcedMode: return "settings_item_forced_mode";
                case ItemClockForced: return "settings_item_clock_forced";
                case ItemForcedBaseTime: return "settings_item_forced_base_time";
                case ItemForcedIncrement: return "settings_item_forced_increment";
                case ItemPlayOnAfterFlagFall: return "settings_item_play_on_after_flag_fall";
                case ItemResetAvailableTime: return "settings_item_reset_available_time";
                case ItemAutoResetTime: return "settings_item_auto_reset_time";
                case ItemResnapshot: return "settings_item_resnapshot";
                case ItemScope: return "settings_scope";
            }
            return "";
        }

        // A button's label's key in the language pack; "" for Previous and Next, whose labels are the signs < and >.
        public static string ActionKey(int item, int action) {
            switch (action) {
                case ActionOn: return item == ItemResnapshot ? "settings_apply_now" : "settings_on";
                case ActionOff: return item == ItemDrawRules ? "settings_no_draws" : "settings_off";
                case ActionDefault: return item == ItemScope ? "settings_all_default" : "settings_default";
            }
            return "";
        }

        // An item's row label ("Takebacks").
        public string ItemText(int item) {
            string key = ItemKey(item);
            return key.Length > 0 ? ChessText.Get(Texts(), key) : "";
        }

        // A button's label ("On", "No draws", "<").
        public string ActionText(int item, int action) {
            if (action == ActionPrevious) return "<";
            if (action == ActionNext) return ">";
            string key = ActionKey(item, action);
            return key.Length > 0 ? ChessText.Get(Texts(), key) : "";
        }

        // Whether a button is shown: Default only for a single board, and each item's own buttons (setting items On and
        // Off, number items and the scope Previous and Next).
        public bool ButtonShown(int item, int action) {
            if (item == ItemResnapshot) return action == ActionOn;
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

        // The labels that change only with the language: the title, the rows' labels and the buttons'.
        void SetFixedTexts() {
            DataDictionary t = Texts();
            ChessUtil.SetText(title, ChessText.Get(t, "settings_title"));
            ChessUtil.SetText(scopeTitle, ItemText(ItemScope));
            ChessUtil.SetText(resnapshotTitle, ItemText(ItemResnapshot));
            for (int item = 0; item < ItemCount && item < itemLabels.Length; item++) {
                ChessUtil.SetText(itemLabels[item], ItemText(item));
            }
            for (int i = 0; i < buttonLabels.Length && i < buttonItems.Length && i < buttonActions.Length; i++) {
                ChessUtil.SetText(buttonLabels[i], ActionText(buttonItems[i], buttonActions[i]));
            }
        }

        // "All boards", or "Board 2" for board number 1.
        public string ScopeText() {
            DataDictionary t = Texts();
            return scope == ScopeAllBoards ? ChessText.Get(t, "settings_all_boards") : ChessText.BoardText(t, scope);
        }

        // The scope for the log, in English whatever the language.
        string ScopeLogText() {
            return scope == ScopeAllBoards ? "all boards" : "board " + (scope + 1);
        }

        // An item's value as shown: "On", "Off (No draws)", "Mixed", "2 min", "Never", "Chess960", "No clock"; for a
        // board, " (default)" after a value it takes from the instance defaults.
        public string ValueText(int item) {
            if (!Utilities.IsValid(arbiter)) return "";
            DataDictionary t = Texts();
            bool all = scope == ScopeAllBoards;
            int setting = ItemSetting(item);
            string value;
            bool fromDefault;
            if (setting != 0) {
                int bits = (all ? arbiter.defaultSettings : arbiter.EffectiveSettings(scope)) & setting;
                string key = bits == setting ? "settings_on"
                    : bits == 0 ? (item == ItemDrawRules ? "settings_value_off_no_draws" : "settings_off")
                    : "settings_value_mixed";
                value = ChessText.Get(t, key);
                fromDefault = !all && (arbiter.overrideMask[scope] & setting) == 0;
            } else {
                value = NumberText(item, CurrentNumber(item));
                fromDefault = !all && !arbiter.IsNumberOverridden(scope, ItemNumber(item));
            }
            return fromDefault ? ChessText.Get(t, "settings_value_default", "value", value) : value;
        }

        // A number item's value: "Standard", "No clock", "Never", "5 min", "0 s".
        public string NumberText(int item, int value) {
            DataDictionary t = Texts();
            if (item == ItemForcedMode) return ChessText.ModeText(t, value);
            if (item == ItemForcedBaseTime && value == ChessArbiter.NoClock) return ChessText.Get(t, "clock_none");
            if ((item == ItemResetAvailableTime || item == ItemAutoResetTime) && value == ChessArbiter.Never) {
                return ChessText.Get(t, "settings_never");
            }
            return DurationText(value);
        }

        // Seconds as "45 s", "2 min", "1 h", or "1 min 30 s".
        public string DurationText(int seconds) {
            DataDictionary t = Texts();
            if (seconds < 60) return ChessText.Get(t, "duration_seconds", "seconds", seconds.ToString());
            if (seconds % 3600 == 0) return ChessText.Get(t, "duration_hours", "hours", (seconds / 3600).ToString());
            string minutes = (seconds / 60).ToString();
            if (seconds % 60 == 0) return ChessText.Get(t, "duration_minutes", "minutes", minutes);
            return ChessText.Get(t, "duration_minutes_seconds", "minutes", minutes, "seconds", (seconds % 60).ToString());
        }

        // The notice while the arbiter's Simple Auth field is empty (docs/organizers.md, In-game fallback): nobody is
        // an organizer then, so nobody sees the controls.
        public string SimpleAuthMissingText() {
            return ChessText.Get(Texts(), "settings_notice_simple_auth_missing");
        }

        // Why the controls are hidden (the empty Simple Auth field first); "" when they're shown.
        public string NoticeText() {
            if (!Utilities.IsValid(arbiter)) return ChessText.Get(Texts(), "settings_notice_no_arbiter");
            if (arbiter.simpleAuthMissing) return SimpleAuthMissingText();
            if (!controlsShown) return ChessText.Get(Texts(), "settings_notice_organizers_only");
            return "";
        }

        // A change for the log, by the language pack's keys: "settings_item_takebacks: settings_off",
        // "settings_item_forced_base_time: > (now 300)", "settings_all_default".
        string ChangeText(int item, int action) {
            string actionText = ActionKey(item, action);
            if (actionText.Length == 0) actionText = action == ActionNext ? ">" : "<";
            if (item == ItemScope) return actionText;
            string text = ItemKey(item) + ": " + actionText;
            if (!IsSettingItem(item) && action != ActionDefault) text += " (now " + CurrentNumber(item) + ")";
            return text;
        }
    }
}
