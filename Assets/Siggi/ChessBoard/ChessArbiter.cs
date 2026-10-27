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

        // What changed, as the arbiter tells it (_OnArbiterChanged, step 13.10): the settings (a setter here, or an
        // update received), or the local player's pick of an archived game.
        public const int ChangeSettings = 1;
        public const int ChangeSelection = 2;

        // First in the Inspector (step 13.11), so a world creator sees it.
        [Header("Boards")]
        [Tooltip("How many boards can have their own overrides: board numbers 0 to this minus 1.")]
        public int boardCount = 4;

        // The PGN Event and Site tags of the games played here (docs/pgn.md, Export tags); empty is written "?". The
        // Inspector values are the instance's starting values; since step 14.13 they're synced with the settings and
        // changed in-world by organizers (SetPgnEvent, SetPgnSite, from the settings menu). Each game keeps the values
        // it ended with (ChessBoard.gameEvent). Read with _PgnEvent and _PgnSite; never null.
        [Header("PGN")]
        [Tooltip("The PGN Event tag of games played in this world, until an organizer changes it in the settings "
            + "menu. Empty is written \"?\".")]
        [UdonSynced] public string pgnEvent = "";
        [Tooltip("The PGN Site tag of games played in this world, until an organizer changes it in the settings "
            + "menu. Empty is written \"?\".")]
        [UdonSynced] public string pgnSite = "VRChat";
        // The longest Event or Site an organizer can set (SetPgnEvent, SetPgnSite; the menu's fields too), in UTF-16
        // units.
        public const int PgnTagMaxLength = 100;

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
        [Tooltip("Every chess piece set the world offers (docs/customizing.md); boards sync which one each side wears, "
            + "by its place here. The first is the default. Must not be empty or have empty entries.")]
        public ChessPieceSet[] pieceSets = new ChessPieceSet[0];
        // The local player's preferred set (step 13.6), an index into pieceSets, -1 for none: put on a board's sides
        // when they join it (ChessBoard.Join). Not synced; a world's own UI sets it (SetPreferredPieceSet or directly).
        [NonSerialized] public int preferredPieceSet = -1;
        // The sets this client shows for each colour on every board whatever the board has synced (step 13.6), -1 for
        // none. Not synced (SetPieceSetOverride).
        [NonSerialized] public int overrideWhitePieceSet = -1;
        [NonSerialized] public int overrideBlackPieceSet = -1;
        // The board views told when an override changes (SetPieceSetOverride); each adds itself on Start.
        [NonSerialized] public ChessBoardView[] pieceSetListeners = new ChessBoardView[0];

        [Header("Archives")]
        [Tooltip("Where the boards' logged games go (docs/archive.md): the scene's Game Archive and Personal Game "
            + "Archive, set after placing them. Every board hands each game it logs to each of them.")]
        public GameArchive[] archives = new GameArchive[0];
        // The game the local player picked on a history wall (step 13.10): its archive (null for none) and ID. Not
        // synced: each player's pick is their own. Every board's UI offers Load it here while there is one.
        [NonSerialized] public GameArchive selectedArchive;
        [NonSerialized] public string selectedGameId = "";
        // Who is told of every change (_OnArbiterChanged): the boards and settings menus add themselves on Start; the
        // archives are the list above.
        [NonSerialized] public ChessBoard[] boards = new ChessBoard[0];
        [NonSerialized] public ChessSettingsMenu[] menus = new ChessSettingsMenu[0];

        // ---- Synced settings ----

        // The instance defaults: setting bits and the numeric settings (NumberCount values).
        [UdonSynced] [NonSerialized] public int defaultSettings;
        [UdonSynced] [NonSerialized] public int[] defaultNumbers = new int[NumberCount];

        // Per-board overrides, indexed by board number (docs/settings.md, Per-board overrides). overrideMask says what
        // the board overrides (setting bits, plus _OverrideNumberBit per numeric setting); overrideSettings holds the
        // overriding setting bits and overrideNumbers the overriding numbers, NumberCount per board.
        [UdonSynced] [NonSerialized] public int[] overrideMask = new int[0];
        [UdonSynced] [NonSerialized] public int[] overrideSettings = new int[0];
        [UdonSynced] [NonSerialized] public int[] overrideNumbers = new int[0];

        // Whether simpleAuth was empty on Start (docs/organizers.md, In-game fallback): nobody is an organizer then,
        // and the settings menu (step 11) shows a message.
        [NonSerialized] public bool simpleAuthMissing;

        // Counts every change to the settings seen here: a setter below, or a serialization arriving. Each is also told
        // to the archives, boards and menus (ChangeSettings), which the settings menu (step 11.1a) redraws on.
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
            // The archives and their walls reach the arbiter through this, with no field of their own.
            for (int i = 0; i < archives.Length; i++) {
                if (Utilities.IsValid(archives[i])) archives[i]._SetArbiter(this);
            }
            // Synced strings must never be null.
            if (pgnEvent == null) pgnEvent = "";
            if (pgnSite == null) pgnSite = "";
            if (received) return;
            defaultSettings = _EditorSettings();
            defaultNumbers = _EditorNumbers();
            if (_FixNumbers(defaultNumbers)) {
                Debug.LogWarning("[ChessArbiter] " + gameObject.name + ": invalid numeric settings in the editor were "
                    + "corrected: " + _NumbersToString(defaultNumbers));
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
            SettingsChanged();
        }

        // ---- Telling (step 13.10) ----

        // Tells board of every change from now on (each board adds itself on Start; it tells its listeners).
        public void _AddBoard(ChessBoard board) {
            if (!Utilities.IsValid(board)) return;
            int count = boards.Length;
            for (int i = 0; i < count; i++) {
                if (boards[i] == board) return;
            }
            ChessBoard[] grown = new ChessBoard[count + 1];
            for (int i = 0; i < count; i++) grown[i] = boards[i];
            grown[count] = board;
            boards = grown;
        }

        // Tells menu of every change from now on (each settings menu adds itself on Start).
        public void _AddSettingsMenu(ChessSettingsMenu menu) {
            if (!Utilities.IsValid(menu)) return;
            int count = menus.Length;
            for (int i = 0; i < count; i++) {
                if (menus[i] == menu) return;
            }
            ChessSettingsMenu[] grown = new ChessSettingsMenu[count + 1];
            for (int i = 0; i < count; i++) grown[i] = menus[i];
            grown[count] = menu;
            menus = grown;
        }

        // Tells every archive, board and settings menu of a change (Change*).
        void TellChanged(int change) {
            for (int i = 0; i < archives.Length; i++) {
                if (Utilities.IsValid(archives[i])) archives[i]._OnArbiterChanged(change);
            }
            for (int i = 0; i < boards.Length; i++) {
                if (Utilities.IsValid(boards[i])) boards[i]._OnArbiterChanged(change);
            }
            for (int i = 0; i < menus.Length; i++) {
                if (Utilities.IsValid(menus[i])) menus[i]._OnArbiterChanged(change);
            }
        }

        // The settings changed: counted and told.
        void SettingsChanged() {
            changes++;
            TellChanged(ChangeSettings);
        }

        // ---- The pick (step 13.10, docs/archive.md) ----

        // A history wall's entry pressed: picks the game with this ID in archive, or clears the pick when it's the game
        // already picked. A pick in another archive replaces the earlier one.
        public void _SelectGame(GameArchive archive, string gameId) {
            if (!Utilities.IsValid(archive) || gameId == null || gameId.Length == 0) return;
            if (archive == selectedArchive && gameId == selectedGameId) {
                _ClearSelection();
                return;
            }
            selectedArchive = archive;
            selectedGameId = gameId;
            TellChanged(ChangeSelection);
        }

        // Clears the pick (loaded, or its game gone from its archive), telling everyone; nothing if there's none.
        public void _ClearSelection() {
            if (!Utilities.IsValid(selectedArchive) && selectedGameId.Length == 0) return;
            selectedArchive = null;
            selectedGameId = "";
            TellChanged(ChangeSelection);
        }

        // Whether the local player has picked a game.
        public bool HasSelection() {
            return Utilities.IsValid(selectedArchive) && selectedGameId.Length > 0;
        }

        // The PGN Event and Site tags now (pgnEvent, pgnSite), for the games in progress on the boards; never null.
        public string _PgnEvent() {
            return pgnEvent == null ? "" : pgnEvent;
        }

        public string _PgnSite() {
            return pgnSite == null ? "" : pgnSite;
        }

        // Sets the PGN Event tag of the games played from now on and of those in progress (step 14.13), cleaned up
        // (_CleanPgnTag). Like the other setters it only changes this client's copy: an organizer's change wraps it in
        // BeginChange and EndChange. Returns whether the value changed.
        public bool SetPgnEvent(string text) {
            string value = _CleanPgnTag(text);
            if (value == _PgnEvent()) return false;
            pgnEvent = value;
            SettingsChanged();
            return true;
        }

        // SetPgnEvent for the Site tag.
        public bool SetPgnSite(string text) {
            string value = _CleanPgnTag(text);
            if (value == _PgnSite()) return false;
            pgnSite = value;
            SettingsChanged();
            return true;
        }

        // An Event or Site as it's kept: trimmed, and cut to PgnTagMaxLength without splitting a surrogate pair. Null
        // gives "". Escaping is the PGN writer's (ChessPgn._EscapeTagValue).
        public static string _CleanPgnTag(string text) {
            if (text == null) return "";
            string value = text.Trim();
            if (value.Length <= PgnTagMaxLength) return value;
            int length = PgnTagMaxLength;
            char last = value[length - 1];
            if (last >= '\uD800' && last <= '\uDBFF') length--;
            return value.Substring(0, length).TrimEnd();
        }

        // ---- Piece sets (step 13.6, docs/customizing.md) ----

        // The piece set at index in pieceSets, null outside it (or for an empty entry).
        public ChessPieceSet PieceSet(int index) {
            if (pieceSets == null || index < 0 || index >= pieceSets.Length) return null;
            ChessPieceSet set = pieceSets[index];
            return Utilities.IsValid(set) ? set : null;
        }

        public int PieceSetCount() {
            return pieceSets == null ? 0 : pieceSets.Length;
        }

        // Whether index is a place in pieceSets.
        public bool IsPieceSetIndex(int index) {
            return index >= 0 && index < PieceSetCount();
        }

        // Makes index (in pieceSets, or -1 for none) the local player's preferred set, which their joins put on the
        // board (ChessBoard.Join). Refuses any other index (false).
        public bool SetPreferredPieceSet(int index) {
            if (index != -1 && !IsPieceSetIndex(index)) return false;
            preferredPieceSet = index;
            return true;
        }

        public int PreferredPieceSet() {
            return preferredPieceSet;
        }

        // Makes this client show the set at index (in pieceSets, or -1 for none) for a ChessRules colour on every
        // board, whatever the board has synced, and tells each piece set listener (the board views,
        // _OnPieceSetOverrideChanged) when it changes. Refuses another index or an unknown colour (false).
        public bool SetPieceSetOverride(int color, int index) {
            if (color != ChessRules.White && color != ChessRules.Black) return false;
            if (index != -1 && !IsPieceSetIndex(index)) return false;
            if (index == PieceSetOverride(color)) return true;
            if (color == ChessRules.White) overrideWhitePieceSet = index;
            else overrideBlackPieceSet = index;
            for (int i = 0; i < pieceSetListeners.Length; i++) {
                if (Utilities.IsValid(pieceSetListeners[i])) pieceSetListeners[i]._OnPieceSetOverrideChanged(color);
            }
            return true;
        }

        // The override for a ChessRules colour, -1 for none (and for an unknown colour).
        public int PieceSetOverride(int color) {
            if (color == ChessRules.White) return overrideWhitePieceSet;
            if (color == ChessRules.Black) return overrideBlackPieceSet;
            return -1;
        }

        // Tells view of every change to an override from now on (each board view adds itself on Start); adding one
        // twice changes nothing.
        public void _AddPieceSetListener(ChessBoardView view) {
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

        // Stops telling view of changes to the overrides.
        public void _RemovePieceSetListener(ChessBoardView view) {
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
        public int _PieceSetListenerCount() {
            return pieceSetListeners.Length;
        }

        // The setting bits from the editor fields.
        public int _EditorSettings() {
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

        // The numeric settings from the editor fields, as entered (not yet corrected by _FixNumbers).
        public int[] _EditorNumbers() {
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
            return _MayOwn(requestedOwner);
        }

        // Whether a player may own the arbiter: organizers only.
        public bool _MayOwn(VRCPlayerApi who) {
            return IsOrganizer(who);
        }

        // Turns the given setting bits (one or several) on or off in the instance defaults.
        public void SetDefaultSetting(int settings, bool on) {
            settings &= OverrideSettingsMask;
            if (on) defaultSettings |= settings;
            else defaultSettings &= ~settings;
            SettingsChanged();
        }

        // Sets a numeric setting's instance default, corrected by _FixNumbers (which can change another one: an
        // auto-reset time below the reset-available time clamps the latter). False for an unknown number.
        public bool SetDefaultNumber(int number, int value) {
            if (number < 0 || number >= NumberCount) return false;
            defaultNumbers[number] = value;
            _FixNumbers(defaultNumbers);
            SettingsChanged();
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
        public static int _OverrideNumberBit(int number) {
            return 1 << (SettingCount + number);
        }

        // Setting bits combined from defaults and overrides: the masked bits from values, the rest from defaults.
        public static int _CombineSettings(int defaults, int mask, int values) {
            return ((defaults & ~mask) | (values & mask)) & OverrideSettingsMask;
        }

        // Overrides the given setting bits (one or several) on a board, on or off.
        public bool SetSettingOverride(int board, int settings, bool on) {
            if (!HasBoard(board)) return false;
            settings &= OverrideSettingsMask;
            overrideMask[board] |= settings;
            if (on) overrideSettings[board] |= settings;
            else overrideSettings[board] &= ~settings;
            SettingsChanged();
            return true;
        }

        // Makes the given setting bits use the defaults again on a board.
        public bool ClearSettingOverride(int board, int settings) {
            if (!HasBoard(board)) return false;
            settings &= OverrideSettingsMask;
            overrideMask[board] &= ~settings;
            overrideSettings[board] &= ~settings;
            SettingsChanged();
            return true;
        }

        // Overrides a numeric setting on a board. The value is kept as given; EffectiveNumbers corrects the result.
        public bool SetNumberOverride(int board, int number, int value) {
            if (!HasBoard(board) || number < 0 || number >= NumberCount) return false;
            overrideMask[board] |= _OverrideNumberBit(number);
            overrideNumbers[board * NumberCount + number] = value;
            SettingsChanged();
            return true;
        }

        // Makes a numeric setting use the default again on a board.
        public bool ClearNumberOverride(int board, int number) {
            if (!HasBoard(board) || number < 0 || number >= NumberCount) return false;
            overrideMask[board] &= ~_OverrideNumberBit(number);
            overrideNumbers[board * NumberCount + number] = 0;
            SettingsChanged();
            return true;
        }

        // Makes everything use the defaults again on a board.
        public bool ClearOverrides(int board) {
            if (!HasBoard(board)) return false;
            overrideMask[board] = 0;
            overrideSettings[board] = 0;
            for (int i = 0; i < NumberCount; i++) overrideNumbers[board * NumberCount + i] = 0;
            SettingsChanged();
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
            return HasBoard(board) && (overrideMask[board] & _OverrideNumberBit(number)) != 0;
        }

        public int NumberOverrideValue(int board, int number) {
            return HasBoard(board) ? overrideNumbers[board * NumberCount + number] : 0;
        }

        // ---- Effective settings ----

        // A board's effective setting bits: its overrides, the instance defaults for the rest.
        public int EffectiveSettings(int board) {
            if (!HasBoard(board)) return defaultSettings;
            return _CombineSettings(defaultSettings, overrideMask[board], overrideSettings[board]);
        }

        public bool IsEffectiveOn(int board, int setting) {
            return (EffectiveSettings(board) & setting) != 0;
        }

        // Fills numbers (NumberCount long) with a board's effective numeric settings: its overrides, the instance
        // defaults for the rest, then corrected by _FixNumbers, since an overridden time can conflict with a default
        // one (e.g. an auto-reset time overridden below the default reset-available time clamps the latter).
        public void EffectiveNumbers(int board, int[] numbers) {
            bool has = HasBoard(board);
            int mask = has ? overrideMask[board] : 0;
            for (int i = 0; i < NumberCount; i++) {
                numbers[i] = (mask & _OverrideNumberBit(i)) != 0 ? overrideNumbers[board * NumberCount + i]
                    : defaultNumbers[i];
            }
            _FixNumbers(numbers);
        }

        public int EffectiveNumber(int board, int number) {
            int[] numbers = new int[NumberCount];
            EffectiveNumbers(board, numbers);
            return numbers[number];
        }

        // Makes numeric settings valid, returning whether anything changed: any negative time becomes Never (or
        // NoClock), the reset-available time is at most the auto-reset time when both are times (docs/inactivity.md),
        // an unknown forced mode becomes Standard, and a negative increment becomes 0.
        public static bool _FixNumbers(int[] numbers) {
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
        public static string _SettingsToString(int bits) {
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
        public static string _NumbersToString(int[] numbers) {
            return "resetAvailable=" + numbers[NumberResetAvailableTime]
                + " autoReset=" + numbers[NumberAutoResetTime]
                + " forcedMode=" + numbers[NumberForcedMode]
                + " forcedClock=" + numbers[NumberForcedBaseTime] + "+" + numbers[NumberForcedIncrement];
        }
    }
}
