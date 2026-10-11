# Settings: `ChessArbiter`

An arbiter is the official who enforces the rules at a chess event, which matches what this object does.

- Holds the rules settings: whether takebacks are allowed, each draw rule (agreement, threefold, fifty-move, fivefold, seventy-five-move, dead position; "No draws" is all draw rules off), the inactivity times, and the forced lobby settings (an optional forced mode and an optional forced time control; see [Starting](board-lifecycle.md#starting)).
- Defaults come from the world's configuration in the editor. Only organizers can change settings in-world.

## Instance defaults

`ChessArbiter` in `Assets/Siggi/ChessBoard/ChessArbiter.cs`.

- **Setting bits** (`Setting*` constants, `SettingCount` = 9, the lowest bits): `SettingTakebacks`, the six draw rules (`SettingDrawAgreement`, `SettingThreefoldRepetition`, `SettingFiftyMoveRule`, `SettingFivefoldRepetition`, `SettingSeventyFiveMoveRule`, `SettingDeadPosition`; together `SettingsDrawRules`), `SettingModeForced`, `SettingClockForced`.
- **Numeric settings** are an `int[NumberCount]` (5) indexed by `NumberResetAvailableTime`, `NumberAutoResetTime` (seconds, `Never` = -1), `NumberForcedMode` (`ChessGame.Mode*`), `NumberForcedBaseTime` (seconds, `NoClock` = -1) and `NumberForcedIncrement` (seconds).
- **Editor values:** one Inspector field per setting (`takebacksAllowed`, `drawAgreement`, ..., `resetAvailableTime`, `autoResetTime`, `modeForced`, `forcedMode`, `clockForced`, `forcedBaseTime`, `forcedIncrement`). Starting values: takebacks on, every draw rule off ("No draws"), reset-available 120 s, auto-reset 600 s, nothing forced (forced values Standard, 600+0).
- On `Start`, every client copies the editor values into the synced `defaultSettings` (`int`) and `defaultNumbers` (`int[5]`), unless a serialization has already arrived (a late joiner whose `OnDeserialization` ran first keeps what was synced). All clients start from the same editor values, so nobody needs to serialize until settings change.
- **Invalid editor numbers are corrected** (`FixNumbers`), with a warning naming the object: a time below -1 becomes -1, a reset-available time above the auto-reset time (both set) becomes the auto-reset time ([Inactivity](inactivity.md)), an unknown forced mode becomes Standard, a negative increment becomes 0.
- Read with `IsDefaultOn(setting)` and `DefaultNumber(number)`; `SettingsToString` and `NumbersToString` give them as text for logs.

## Per-board overrides

- Each setting has an instance-wide default, and any board can override any setting, e.g. a tournament board with no takebacks next to casual boards.
- **Encoding:** each setting is one bit, so a full set of settings is one `int`. The arbiter holds:
  - the instance defaults (`int`),
  - per board: an override mask and override values (`int[]` each, indexed by board number).
  - A board's effective settings are `(defaults & ~mask) | (values & mask)`: overridden bits come from the board, the rest from the defaults.
  - On/off settings include "mode forced" and "clock forced".
  - **Numeric settings** aren't on/off, so they're stored as separate values: instance defaults plus per-board `int[]` overrides, with a bit in the override mask saying whether the board overrides each one. There are five:
    - reset-available time and auto-reset time (seconds, `-1` = never; see [Inactivity](inactivity.md)),
    - forced mode (used only while "mode forced" is on),
    - forced base time and forced increment (seconds, base `-1` = no clock; used only while "clock forced" is on).
- **Implementation** (`ChessArbiter`):
  - `boardCount` (Inspector, starting value 4) sets how many boards can have overrides, numbered 0 to `boardCount` − 1. On `Start` the synced arrays `overrideMask`, `overrideSettings` (`int[boardCount]` each) and `overrideNumbers` (`int[boardCount × NumberCount]`, flat since synced arrays can't nest) are created empty, unless a serialization has already arrived; `BoardCount()` is their synced length. A board number outside the range (`HasBoard`) has no overrides and always gets the defaults.
  - Mask bits: the setting bits themselves (`OverrideSettingsMask`), then `OverrideNumberBit(number)` = `1 << (SettingCount + number)` per numeric setting (`OverrideNumbersMask`).
  - `CombineSettings(defaults, mask, values)` is the formula above; `EffectiveSettings(board)` / `IsEffectiveOn(board, setting)` apply it. `EffectiveNumbers(board, numbers)` / `EffectiveNumber(board, number)` take each overridden number from the board and the rest from the defaults, then **correct the result with `FixNumbers`**, since an override can conflict with a default (an auto-reset time overridden below the default reset-available time clamps the latter). Override values are kept as given.
  - Setters for the settings menu: `SetSettingOverride(board, settings, on)` (one or several bits), `ClearSettingOverride`, `SetNumberOverride(board, number, value)`, `ClearNumberOverride`, `ClearOverrides(board)`; they return `false` for a board out of range. Readers: `IsSettingOverridden`, `SettingOverrideValue`, `IsNumberOverridden`, `NumberOverrideValue`. The setters only change the local copy; ownership and serializing come with networking (step 8.2).
- **Stored on the arbiter, not the board.** Overrides must survive a board reset, which clears all of the board's state, and changing them only needs ownership of the arbiter. Boards are given their board number in the editor.
- The settings menu (a view of the arbiter) lets an organizer pick "all boards" or a specific board, and set each setting to on, off, or "use default" for a board.
- **Snapshot:** a board copies its effective settings (the bits and all five numeric settings) into its own synced state when it leaves the reset state (someone joins a seat, or a game is loaded or imported), and uses that copy until it's reset. Changing settings doesn't alter the rules of a board already in use.
- **Re-snapshot on request:** if a mistake was made, an organizer can tell a board, or all boards, to take a new snapshot. The organizer takes ownership of the board (see [Ownership](ownership.md)), the board re-copies its effective settings, and serializes.
- **Implementation** (`ChessBoard`): the board's Inspector fields `arbiter` (the world's `ChessArbiter`) and `boardNumber` (its number for per-board overrides; a number the arbiter has no overrides for gets the defaults). The snapshot is `gameSettings` (bits) and `gameNumbers` (`int[5]`, the corrected effective numbers), both empty (0) on a reset board; read them with `IsGameSettingOn(setting)` and `GameNumber(number)`.
  - `TakeSnapshot()` copies `EffectiveSettings(boardNumber)` and `EffectiveNumbers(boardNumber, ...)`. `JoinAs` calls it when the first join takes the board from reset to the lobby (later joins don't); loading a game calls it too (step 7.9a). It then applies the forced lobby settings so the lobby shows them: the forced mode and clock in the lobby, only the forced clock on a loaded board (its mode is the loaded game's); a game already started keeps its mode and clock. Start applies them again, so the game always has the forced values. With the `arbiter` field empty (the board logs an error on `Start`), the snapshot is every bit off, both inactivity times `Never`, Standard, no clock.
  - `Resnapshot()` / `ResnapshotAs(who)` (a `VRCPlayerApi`): accepted when `arbiter.IsOrganizer(who)` and the board isn't reset (a reset board has no snapshot; it takes one on leaving the reset state); it calls `TakeSnapshot()`, so new forcing applies to the lobby and lifted forcing leaves the shown values as the players' choices. Taking ownership and serializing come with networking (step 8). The settings menu's "all boards" calls it on each board.
