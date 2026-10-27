# Settings

What a world's own scripts call on the `ChessArbiter` for the rules settings: reading the instance defaults, a board's overrides and its effective settings, reading the snapshot a board plays by, changing them as an organizer and sending the change, making boards in use take the new settings, and noticing a change. Also the PGN Event and Site tags, which an organizer sets the same way, and writing a `SimpleAuth` of the world's own to decide who the organizers are. The package's **Chess Settings Menu** (`ChessSettingsMenu`) is built on exactly this and is the reference for it. Read the [API index](README.md) first; the design behind the settings is in [settings](../settings.md) and [organizers](../organizers.md).

A script holds the arbiter in a public field set in the Inspector (`public ChessArbiter arbiter;`), the scene's **Chess Arbiter**; a board's own is its `arbiter` field.

## The settings

There are ten on/off settings, each one bit of an `int`, and five numeric settings, each an index into an `int[ChessArbiter.NumberCount]`. All constants are on `ChessArbiter`.

| Setting bit | Value | On means |
|---|---|---|
| `SettingTakebacks` | 1 | Players may request and accept takebacks. |
| `SettingDrawAgreement` | 2 | Players may offer and accept draws. |
| `SettingThreefoldRepetition` | 4 | A draw may be claimed on a threefold repetition. |
| `SettingFiftyMoveRule` | 8 | A draw may be claimed under the fifty-move rule. |
| `SettingFivefoldRepetition` | 16 | A fivefold repetition is drawn automatically. |
| `SettingSeventyFiveMoveRule` | 32 | The seventy-five-move rule draws automatically. |
| `SettingDeadPosition` | 64 | A dead position (insufficient material) is drawn automatically, and a flag fall is a draw when the opponent has no mating material. |
| `SettingModeForced` | 128 | The mode is `NumberForcedMode`'s; the players can't choose it. |
| `SettingClockForced` | 256 | The time control is `NumberForcedBaseTime` + `NumberForcedIncrement`; the players can't choose it. |
| `SettingPlayOnAfterFlagFall` | 512 | A game whose flag falls ends, is logged, and plays on untimed at once ([clocks](../clocks.md)). |

- `SettingsDrawRules` is the six draw rules together: all off is "No draws". `SettingCount` (10) is how many bits there are; `OverrideSettingsMask` is all ten. Any method taking `settings` takes one bit or several or'ed together (`SettingFiftyMoveRule | SettingThreefoldRepetition`); bits above the ten are ignored.
- The draw rules and their claims are in [game endings](../game-endings.md), the forced mode and clock in [board lifecycle](../board-lifecycle.md#starting) and [modes](../modes.md).

| Numeric setting | Index | Value |
|---|---|---|
| `NumberResetAvailableTime` | 0 | Seconds a board must be idle before anyone may reset it; `Never` (−1) for never (then only the seated players and organizers may). |
| `NumberAutoResetTime` | 1 | Seconds idle after which a board resets itself; `Never` (−1) for never. |
| `NumberForcedMode` | 2 | `ChessGame.ModeStandard` (0) or `ChessGame.ModeChess960` (1). Used only while `SettingModeForced` is on. |
| `NumberForcedBaseTime` | 3 | Seconds; `NoClock` (−1) for an untimed game. Used only while `SettingClockForced` is on. |
| `NumberForcedIncrement` | 4 | Seconds added per move (ignored with no clock). Used only while `SettingClockForced` is on. |

**Numbers are corrected** where they're combined (`SetDefaultNumber`, and every effective value): any time below −1 becomes `Never` / `NoClock`, a reset-available time above the auto-reset time (both set) becomes the auto-reset time ([inactivity](../inactivity.md)), an unknown forced mode becomes Standard, a negative increment 0. Nothing checks a forced time control's upper limits: keep it to what a player could choose, `ChessBoard.IsValidTimeControl(base, increment)` (`NoClock` with increment 0, or 1 s to `ChessBoard.MaxBaseTime` (24 h) with an increment of 0 to `ChessBoard.MaxIncrement` (1 h)). A number index outside 0 to `NumberCount` − 1 is refused by the setters (`false`); the getters don't check it.

## Three layers

- **Instance defaults:** what every board gets, unless it overrides a setting. They start as the arbiter's Inspector values (`takebacksAllowed`, `drawAgreement`, …, `resetAvailableTime`, `autoResetTime`, `modeForced`, `forcedMode`, `clockForced`, `forcedBaseTime`, `forcedIncrement`, `playOnAfterFlagFall`), read once on `Start` (changing those fields afterwards does nothing; a late joiner takes what's been synced instead).
- **Per-board overrides:** any board may override any setting, bit by bit and number by number, by its **board number**, the board's Inspector field `boardNumber`. The arbiter has overrides for boards 0 to `BoardCount()` − 1 (its Inspector `boardCount`, which the wiring script keeps at the highest board number plus one); a board number outside that has none and always gets the defaults (`HasBoard(n)` is false). Several boards may share a number, and so share their overrides.
- **The snapshot:** what a board actually plays by. A board copies its effective settings (its overrides, the defaults for the rest) when it leaves the reset state (the first join, a loaded or imported game), and keeps that copy until it's reset. Changing the arbiter doesn't alter a board in use until the board **re-snapshots** (below). A reset board has no snapshot.

The PGN tags are the one exception: a game in progress writes the arbiter's Event and Site as they are now, with no re-snapshot ([below](#pgn-event-and-site)).

## Reading

On the arbiter, from the local copy, so always the latest this client has received:

| Call | Gives |
|---|---|
| `IsDefaultOn(setting)` | Whether a bit is on in the instance defaults. |
| `DefaultNumber(number)` | A numeric setting's instance default. |
| `BoardCount()` | How many board numbers have overrides (as synced, so every client agrees); `HasBoard(n)` whether `n` is one. |
| `IsSettingOverridden(n, setting)` | Whether board number `n` overrides that bit; `SettingOverrideValue(n, setting)` the value it overrides it with (meaningful only while overridden). |
| `IsNumberOverridden(n, number)` | Whether board number `n` overrides a number; `NumberOverrideValue(n, number)` its value, as set (not corrected; 0 when not overridden). |
| `EffectiveSettings(n)` | Board number `n`'s setting bits: its overrides, the defaults for the rest (the defaults for a number without overrides). `IsEffectiveOn(n, setting)` for one bit. |
| `EffectiveNumber(n, number)` | Board number `n`'s numeric setting, its override or the default, corrected. `EffectiveNumbers(n, numbers)` fills an `int[NumberCount]` with all five at once. |
| `IsOrganizer(player)` | Whether a `VRCPlayerApi` is an organizer (below). |

These are what a board would take if it took a snapshot now. What a board **plays by** is on the board:

| Call on `ChessBoard` | Gives |
|---|---|
| `IsGameSettingOn(setting)` | Whether a bit is on in the board's snapshot. |
| `GameNumber(number)` | A numeric setting from the snapshot (corrected). |
| `IsModeForced()`, `IsClockForced()` | `IsGameSettingOn(SettingModeForced)` / `(SettingClockForced)`. |

A reset board's snapshot is empty: every bit off and every number 0. To show what a reset board will play by, read `arbiter.EffectiveSettings(board.boardNumber)` and `EffectiveNumber`. A board whose `arbiter` field is empty plays with every bit off, both inactivity times `Never`, Standard and no clock.

The board's `mode`, `baseTime` and `increment` (readable state, [UI](ui.md#readable-state)) are the mode and time control the lobby shows or the game has, which forcing writes into: read those for what's chosen, the snapshot for whether it's forced.

Readable fields of the arbiter (read, never write): `changes` and `simpleAuthMissing` (below), and the Inspector's `pgnEvent`, `pgnSite`, `simpleAuth`, `boardCount` and `pieceSets`. The synced arrays behind the getters (`defaultSettings`, `defaultNumbers`, `overrideMask`, `overrideSettings`, `overrideNumbers`) aren't API: use the getters.

## Changing settings

The setters change **only this client's copy**. An organizer's change goes in three steps, so it reaches everyone:

```csharp
if (!arbiter.BeginChange()) return;   // not an organizer: nothing changed
arbiter.SetSettingOverride(2, ChessArbiter.SettingTakebacks, false);
arbiter.SetNumberOverride(2, ChessArbiter.NumberAutoResetTime, ChessArbiter.Never);
arbiter.EndChange();                  // sends it
```

- **`BeginChange()`** returns `false` unless the local player is an organizer; for an organizer it takes ownership of the arbiter if they don't hold it (`Networking.SetOwner`) and returns whether they now own it. Only organizers may own the arbiter: its `OnOwnershipRequest` refuses anyone else, on the asking client and on the owner.
- **The setters**, any number of them, between the two.
- **`EndChange()`** asks VRChat to send the settings (manual sync, `RequestSerialization`) and returns `true`; `false` if this client doesn't own the arbiter. Other clients receive the whole set of settings a moment later; a player joining later gets the last ones sent.

Call the setters and `EndChange` **only when `BeginChange` returned `true`**. A setter without it changes the local copy alone, which the next update from the owner overwrites; and when the organizer who owned the arbiter leaves, VRChat hands it to someone at random, whose `EndChange` would still send (only `BeginChange` checks for an organizer).

The setters (board number `n`, `setting` one bit or several, `number` a `Number…` index):

| Setter | Does | Returns |
|---|---|---|
| `SetDefaultSetting(setting, on)` | Turns the bits on or off in the instance defaults. | nothing |
| `SetDefaultNumber(number, value)` | Sets a number's instance default, then corrects the defaults (so lowering the auto-reset time below the reset-available time lowers that too, and raising it again doesn't put it back). | `false` for an unknown number |
| `SetSettingOverride(n, setting, on)` | Board `n` overrides the bits, on or off. | `false` for a board number without overrides |
| `ClearSettingOverride(n, setting)` | Board `n` uses the defaults for the bits again. | as above |
| `SetNumberOverride(n, number, value)` | Board `n` overrides a number. Kept as given; only the effective value is corrected. | `false` for a board number without overrides or an unknown number |
| `ClearNumberOverride(n, number)` | Board `n` uses the default for the number again. | as above |
| `ClearOverrides(n)` | Board `n` uses the defaults for everything. | `false` for a board number without overrides |

- **"All boards" is the defaults,** not an override on each board: a board that overrides a setting keeps its override when the default changes. To make every board the same, set the default and clear the boards' overrides of it.
- **Forcing the mode or the time control** is the bit and the values: `SettingModeForced` with `NumberForcedMode`, `SettingClockForced` with `NumberForcedBaseTime` and `NumberForcedIncrement`. The values are kept while the bit is off, so they can be set first. A snapshot puts the forced mode and clock into a board's lobby, and only the clock on a loaded board (its mode is the game's); Start applies them again, so a game always has the forced values. Lifting forcing leaves the values shown as the players' own choice.
- **"Never":** `ChessArbiter.Never` (−1) for either inactivity time, `ChessArbiter.NoClock` (−1) for no forced clock.
- **Every setter call counts as a change** and tells everyone ([below](#noticing-a-change)), even one that sets what was already there; the settings menu checks first that a press would change something. `EndChange` sends whatever the copy holds, changed or not.

### Making boards in use take the settings

A board in use keeps its snapshot until it's reset. To apply a change now, make it **re-snapshot**:

- **`board.Resnapshot()`** as the local player: accepted (`InputAccepted`) for an organizer on a board that isn't reset, which copies its effective settings again (the forced mode and clock into the lobby as above; a game already started keeps its mode and clock) and, like every board change, takes ownership of the board and sends it ([index](README.md#calls-and-when-they-take-effect)). Told everywhere as `OnBoardChanged` with `lastChange` `ChessBoard.ChangeSettings`. `InputRejected` for anyone else, and for a reset board, which takes the settings anyway when it's next used.
- **All boards:** call it on each. The arbiter's list of boards isn't API; a world's script holds its own `ChessBoard[]` set in the Inspector, as the settings menu's `boards` does. The menu re-snapshots the boards with the board number it shows, or every board for "All boards", counts reset boards as not in use, and keeps the changes and the re-snapshot separate presses, so an organizer can change several settings before applying them.

Call it after `EndChange`, on the same client: the board reads this client's copy of the arbiter, which already holds the change.

### PGN Event and Site

The Event and Site tags of the games played here ([PGN](../pgn.md#export-tags)), instance-wide only (no per-board override), changed like the settings, between `BeginChange` and `EndChange`:

- **`SetPgnEvent(text)`**, **`SetPgnSite(text)`**: clean the text up (trimmed, cut to `ChessArbiter.PgnTagMaxLength`, 100 UTF-16 units, without splitting a surrogate pair; `null` is `""`, which a PGN writes as `?`), and return whether the value changed. A change counts and is told as a settings change.
- **Read** the arbiter's `pgnEvent` and `pgnSite` (never `null` once it has started).
- Games in progress follow the new values at once, without a re-snapshot; an ended game keeps those it ended with, and a loaded game those it came with.

## Who may: organizers

An **organizer** is a player the arbiter's `SimpleAuth` authorizes: `arbiter.IsOrganizer(player)`. Organizers change the settings and the PGN tags, make boards re-snapshot, reset any board, work the replay controls on any board, and may own any board ([organizers](../organizers.md), [ownership](../ownership.md)). Being one changes no setting by itself.

- **Ask the arbiter, not the `SimpleAuth`:** `IsOrganizer` is `false` for everyone while the arbiter's **Simple Auth** field is empty, which also sets the arbiter's readable `simpleAuthMissing` (the settings menu then says why nobody can change settings). A build or upload with the field empty is stopped in the editor (`ChessArbiterBuildCheck`, with a dialog saying how to fill it); `simpleAuthMissing` covers Play Mode.
- **Check again on a timer.** Whether the local player is an organizer can change with nothing on the arbiter changing: `FirstJoinerAuth`'s name arrives by sync, the instance master changes. The settings menu checks `arbiter.IsOrganizer(Networking.LocalPlayer)` once a second to show or hide its controls, and hides them at once when `BeginChange` refuses.
- **Built in** (namespace `SiggiUSTools`, in the `io.siggi.ustools` package): `FirstJoinerAuth` (the first player in the instance, by display name, so a rejoin keeps it; the Chess Arbiter prefab's default, on the arbiter's object), `InstanceMasterAuth` (whoever is instance master now) and `NoOneAuth` (nobody: the Inspector's settings apply unchanged).

### A `SimpleAuth` of the world's own

Derive from `SiggiUSTools.SimpleAuth`, override `IsAuthorized(VRCPlayerApi who)`, put it on any object, and drag it into the arbiter's **Simple Auth** field. The arbiter calls only this one-argument form.

- **Give every client the same answer** for a player: the arbiter's ownership check runs on the asking client and on the owner, and each client shows or hides its own menu by it. Decide from what every client knows alike (the player's display name, `isMaster`, a synced field of your own), never from something local such as who pressed a button here.
- **Refuse an invalid player** (`Utilities.IsValid(who)`): a caller can pass one, such as a player who has just left.
- **Keep it cheap.** It's called often: by the boards' checks (who may reset, step a replay, own the board), each UI's buttons once a second, every ownership request.

```csharp
using SiggiUSTools;
using UdonSharp;
using VRC.SDKBase;

// Organizers by display name, listed in the Inspector.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class NameListAuth : SimpleAuth {
    public string[] names = new string[0];

    public override bool IsAuthorized(VRCPlayerApi who) {
        if (!Utilities.IsValid(who)) return false;
        string name = who.displayName;
        for (int i = 0; i < names.Length; i++) {
            if (names[i] == name) return true;
        }
        return false;
    }
}
```

## Noticing a change

The arbiter has no listeners of a world's own. Two ways to notice a settings change:

- **Through a board:** a `ChessBoardListener` added to a board ([UI](ui.md#being-told-of-changes)) gets `OnArbiterChanged(board, ChessArbiter.ChangeSettings)` for every settings change: each setter call on this client, and each update arriving from the owner. A listener on several boards is told once per board. `ChessArbiter.ChangeSelection` is the pick of an archived game ([history](history.md)), not settings.
- **Polling `changes`:** the arbiter's readable `changes` counts the same events (every setter call here, every update arriving). Keep the value last drawn and redraw when it differs, as the settings menu does (`_RefreshIfChanged`, also its first drawing):

```csharp
int drawnChanges = -1;

void Update() {
    if (arbiter.changes == drawnChanges) return;
    drawnChanges = arbiter.changes;
    Redraw();   // read the getters above
}
```

The count is only for telling that something changed: one organizer's change of three settings counts three on their client and one on everyone else's.

**A board's snapshot** changes on that board, so it's told by `OnBoardChanged` like any board change: `ChessBoard.ChangeSettings` for a re-snapshot (and for the players' mode or clock choice), `ChangeJoined` for the first join that takes one, `ChangeLoaded` for a load, `ChangeReset` for the reset that clears it. A display of what a board plays by redraws on every `OnBoardChanged`; one showing what it would take (the arbiter's effective settings) redraws on the arbiter's changes too.

## Example

A tournament switch beside one table: an organizer's press makes that board forced 5 + 3 with no takebacks and every draw rule, and applies it to a game already in its lobby.

```csharp
using SiggiChessBoard;
using UdonSharp;
using UnityEngine;

public class TournamentSwitch : UdonSharpBehaviour {
    public ChessArbiter arbiter;
    public ChessBoard board;

    public void PressTournament() {
        int n = board.boardNumber;
        if (!arbiter.HasBoard(n)) return;   // a board number without overrides
        if (!arbiter.BeginChange()) return; // not an organizer
        arbiter.SetSettingOverride(n, ChessArbiter.SettingTakebacks, false);
        arbiter.SetSettingOverride(n, ChessArbiter.SettingsDrawRules, true);
        arbiter.SetSettingOverride(n, ChessArbiter.SettingClockForced, true);
        arbiter.SetNumberOverride(n, ChessArbiter.NumberForcedBaseTime, 300);
        arbiter.SetNumberOverride(n, ChessArbiter.NumberForcedIncrement, 3);
        arbiter.EndChange();
        // A board in use takes the new settings now; a reset board takes them when it's next used.
        board.Resnapshot();
    }

    public void PressCasual() {
        if (!arbiter.BeginChange()) return;
        arbiter.ClearOverrides(board.boardNumber);
        arbiter.EndChange();
        board.Resnapshot();
    }
}
```
