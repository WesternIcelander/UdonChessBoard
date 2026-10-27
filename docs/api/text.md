# Text

Every text a player reads on the chess objects comes from a language pack, through **Chess Language** (an `I18n` of `io.siggi.ustools`). This page lists every key of the package's pack, `Assets/Siggi/ChessBoard/Languages/en.json`: where it shows and the placeholders it has; then how a world's own UI shows the same texts, and how a translation pack uses the list. Read the [API index](README.md) first.

What goes into a PGN export (tags, results, SAN, `[%clk]` comments), log lines, tooltips and signs that read the same in every language (`<<` `<` `>` `>>`, clocks like `4:58`, move numbers) aren't in the pack: they stay as they are in every language ([architecture](../architecture.md#player-text-step-135)).

## A world's own UI with the chess texts

A world's own buttons and displays can say the same things the board's UI says, in the player's language, by reading the same keys from Chess Language. The package's behaviours do it this way, and a world's script does the same:

- **An `i18n` field of its own**, `public I18n i18n;` (namespace `SiggiUSTools`), on Chess Language. The `I18n` sanity check fills every `I18n` field of every behaviour in the scene on build and before Play Mode, as it does the package's, with the `I18n` closest to the behaviour ([prefabs](../prefabs.md#chess-language)): left empty, it's filled in for you when the script's object is beside Chess Language or beside one of the chess objects' parents. The sanity check also **changes a field set by hand** to the closest `I18n`, so a script nearer the world's own `I18n` (or one with a second `I18n` field for the world's own texts) gets an **I18n Lock** component on its object (`SiggiUSTools.I18nLock`, Add Component > Siggi USTools > I18n Lock), and its fields are then left as set.
- **`ChessUtil.Text(i18n, key)`** → `string`: the text of `key` in the player's language now, falling back to the first pack (`en.json`) for a key the language's pack lacks. A key no pack has comes back as the key itself, and `I18n` logs an error for it; with `i18n` empty every key comes back as itself, logging nothing. Each call is a call into another behaviour, so read the texts you show **once into fields of your own** (a `LoadTexts` method, as the package's behaviours have), not on every redraw.
- **`I18n.Format(template, args)`** → `string` (static, on `SiggiUSTools.I18n`): fills a text's `${name}` placeholders, `args` alternating names and values (`new string[] { "color", textWhite }`). It's compiled into the caller, so it costs no call into another behaviour. Placeholders are filled in one pass (a player's name holding `${…}` stays as it is); one without a value stays in the text and logs a warning. A value is plain text: fill a placeholder with another key's text where the key's entry below says so (`${color}` with `color_white`'s text, `${ending}` with an ending's).
- **Again on a language change:** register with `i18n.AddLanguageChangeListener(this)` (in `Start`), and give the behaviour a `public void OnLanguageChange()` (called by `SendCustomEvent`, so `public` and with that name) that reads the texts again and redraws. The language changes when the player's VRChat language changes, or when a world's main `I18n` calls `SetLanguageGlobally`.
- **Built from the board's codes:** the board keeps no text, only codes; the key of how a game ended is `ChessGame.EndingKey(board.ending, board.winner)` ([rules](rules.md)), and the rest come from the board's getters as below. The `ChessBoardUI` helpers that write the package's lines (`_StatusTextFor`, `_EndingText`, `_GameTexts`, the `Text…` index constants, …) are internal, as their underscore says: a world writes its own lines from the keys.

```csharp
using SiggiChessBoard;
using SiggiUSTools;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// A sign over a board saying whose turn it is, or how the game ended, in the player's language:
// "White to move", "Black to move, in check", "White wins by checkmate (1-0)".
public class ChessStatusSign : ChessBoardListener {
    public ChessBoard board;
    // Chess Language: left empty, the I18n sanity check fills it in.
    public I18n i18n;
    public TMPro.TextMeshPro label;

    string textWhite = "";
    string textBlack = "";
    string textToMove = "";
    string textToMoveInCheck = "";
    string textEnding = "";

    void Start() {
        if (Utilities.IsValid(i18n)) i18n.AddLanguageChangeListener(this);
        LoadTexts();
        board.AddListener(this);
        Draw();
    }

    // Each text read once here, not on every redraw.
    void LoadTexts() {
        textWhite = ChessUtil.Text(i18n, "color_white");
        textBlack = ChessUtil.Text(i18n, "color_black");
        textToMove = ChessUtil.Text(i18n, "status_to_move");
        textToMoveInCheck = ChessUtil.Text(i18n, "status_to_move_in_check");
        textEnding = ChessUtil.Text(i18n, "status_ending");
    }

    public void OnLanguageChange() {
        LoadTexts();
        Draw();
    }

    public override void OnBoardChanged() {
        Draw();
    }

    void Draw() {
        if (board.lifecycle == ChessBoard.LifecycleInGame) {
            string color = board.SideToMove() == ChessRules.White ? textWhite : textBlack;
            string line = board.IsInCheck() ? textToMoveInCheck : textToMove;
            label.text = I18n.Format(line, new string[] { "color", color });
        } else if (board.lifecycle == ChessBoard.LifecycleEnded) {
            // Read when the game ends, not every frame: one key of twenty, so not kept in a field.
            string ending = ChessUtil.Text(i18n, ChessGame.EndingKey(board.ending, board.winner));
            label.text = I18n.Format(textEnding, new string[] { "ending", ending, "result", board.ResultText() });
        } else {
            label.text = "";
        }
    }
}
```

## Translation packs

A pack is a JSON object: `languageCode` (`de`, `ja`, …), `languageName` (`Deutsch`), and the keys below with their texts in that language, keeping each `${name}` placeholder (it may move within the sentence, and a placeholder the English text doesn't have is never filled). A world adds its pack to its placed Chess Language's **Language Packs**, after `en.json`, as [customizing](../customizing.md#languages-step-135) says; a pack with code `en` changes some of the English texts. A key a pack leaves out shows in English, so a pack written for one release keeps working when a later one adds keys, showing only the new texts in English until the pack has them.

`languageCode` and `languageName` are the pack's own header, not texts the chess objects show; the 160 keys below are.

## The keys are API

From the first release (16.1) on, the keys are public API, kept by [compatibility](../compatibility.md) rule 10:

- **A key is never renamed or removed.** A world's pack holding the old key would fall back to English without a word: nothing logs a key that a pack has and nothing reads.
- **A text whose meaning changes gets a new key**, so a world's pack doesn't keep saying the old thing in its language. The old key stays in `en.json`.
- **A placeholder is never removed from or renamed in a key**: a pack's text still holding it would show `${name}`. A new value to show is a new key.
- New keys may come in any release, as may changes to the English wording that keep a key's meaning.

## Every key

Where it shows: **UI** (the **Chess Board UI**: its buttons, player panels, status panel and PGN box), **2D view** (the **2D Chess Board View**: its board label, players and status), **wall** (the **Game History Wall** and **Personal History Wall**), **menu** (the **Chess Settings Menu**), **view** (the **Chess Board View**'s hover texts on pieces, squares and promotion pieces), **holder** (the **Movable 2D Chess Board View**'s hover texts). A key whose text fills another's placeholder shows where that one does.

### Shared words

| Key | English | Placeholders | Shows |
|---|---|---|---|
| `color_white` | White | | `${color}`, `${flagged}` and `${other}` in the UI's and 2D view's lines |
| `color_black` | Black | | as `color_white` |
| `mode_standard` | Standard | | `${mode}` in `button_mode` and `status_lobby` (UI, 2D view); the menu's Forced mode value |
| `mode_chess960` | Chess960 | | as `mode_standard` |
| `unknown_name` | ? | | a missing name in `status_loaded` (UI, 2D view) and the wall's rows; a move without notation in `replay_move` |
| `board_number` | Board ${number} | `number` (from 1) | the 2D view's board label; the menu's scope (`settings_scope`'s value) |
| `moves_one` | 1 move | | `${moves}` in the wall's rows and `import_running` (UI) for one |
| `moves_many` | ${count} moves | `count` | as `moves_one`, for any other number |

### Time controls

| Key | English | Placeholders | Shows |
|---|---|---|---|
| `clock_none` | No clock | | `${clock}` in `button_clock` and `status_lobby` (UI, 2D view) for no clock; the menu's Forced base time value for no clock |
| `clock_minutes` | ${minutes}+${increment} | `minutes`, `increment` (seconds) | `${clock}` as above, for a base time in whole minutes (`5+3`) |
| `clock_seconds` | ${seconds}s+${increment} | `seconds`, `increment` | `${clock}` as above, otherwise (`45s+0`) |

### The UI's buttons

All on the **UI**.

| Key | English | Placeholders | Shows |
|---|---|---|---|
| `button_join_white` | Join White | | Join White |
| `button_join_black` | Join Black | | Join Black |
| `button_leave` | Leave | | each seat's Leave |
| `button_mode` | Mode: ${mode} | `mode` (`mode_standard`, `mode_chess960`) | the lobby's Mode button |
| `button_clock_none` | Clock | | the Clock button with no board |
| `button_clock` | Clock: ${clock} | `clock` (`clock_none`, `clock_minutes`, `clock_seconds`) | the lobby's Clock button |
| `button_start` | Start | | Start |
| `button_continue` | Continue from here | | Continue, in the replay of a loaded game (or of a game one player plays both sides of) |
| `button_reset` | Reset | | Reset |
| `button_resign` | Resign | | each player's Resign |
| `button_offer_draw` | Offer draw | | Offer draw |
| `button_accept_draw` | Accept draw | | Accept draw |
| `button_decline_draw` | Decline draw | | Decline draw |
| `button_claim_draw` | Claim draw | | Claim draw, for a claim not named below |
| `button_claim_draw_threefold` | Claim draw (threefold) | | Claim draw, by threefold repetition |
| `button_claim_draw_fifty_moves` | Claim draw (50 moves) | | Claim draw, by the fifty-move rule |
| `button_replay` | Replay | | Replay |
| `button_back_to_game` | Back to game | | leaving the replay |
| `button_take_back_to_here` | Take back to here | | a player's takeback request, in the replay |
| `button_accept_takeback` | Accept takeback | | Accept takeback |
| `button_decline_takeback` | Decline takeback | | Decline takeback |
| `button_load_here` | Load replay here | | loading the picked archived game onto the board |
| `button_pgn` | Export PGN | | showing the PGN box with the game's PGN |
| `button_load_pgn` | Load PGN | | showing the PGN box for pasting one |
| `button_hide_pgn` | Hide PGN | | either of those two while its box is shown |

### The PGN box and imports

| Key | English | Placeholders | Shows |
|---|---|---|---|
| `pgn_copy_hint` | PGN of this game. To copy it on desktop: … | | the UI's PGN box when exporting; the wall's PGN box |
| `pgn_paste_hint` | Paste a PGN here, then press Import. … | | the UI's PGN box when pasting |
| `pgn_import` | Import | | the UI's PGN box's Import button |
| `pgn_close` | Close | | the UI's PGN box's Close button |
| `import_paste_first` | Paste a PGN into the box first. | | the UI's PGN box, Import pressed with the box empty |
| `import_running` | Importing: ${moves} read | `moves` (`moves_one`, `moves_many`) | the UI's PGN box while an import runs |
| `import_failed` | Import failed at move ${move}: ${reason} | `move` (its label, `2.` or `2...`), `reason` (one of the `import_error_` keys) | the UI's PGN box, an import refused |
| `import_failed_load` | Import failed: the game couldn't be loaded | | the UI's PGN box, an import read but not loaded |
| `import_abandoned` | Import abandoned: the board changed before … | | the UI's PGN box, the board changed during the import |
| `import_error_syntax` | not a PGN | | `${reason}` in `import_failed` |
| `import_error_illegal_move` | illegal move | | as `import_error_syntax` |
| `import_error_ambiguous_move` | ambiguous move | | as `import_error_syntax` |
| `import_error_fen` | invalid FEN | | as `import_error_syntax` |
| `import_error_too_long` | game too long | | as `import_error_syntax` |

### Players and status lines

The UI's player panels and status panel, and the 2D view's players and status, write these the same way, except where said.

| Key | English | Placeholders | Shows |
|---|---|---|---|
| `player_line` | ${color}: ${name} | `color`, `name` (the player's display name, or `player_open`) | each seat's line (the side's time follows it, during and after a game with a clock) |
| `player_open` | (open) | | `${name}` in `player_line` for an empty seat |
| `player_flag_fallen` | GG | | in place of `0:00` after a seat's line, for the side out of time in a game played on untimed |
| `status_reset` | Join White or Black to play | | a reset board |
| `status_lobby` | Lobby: ${mode}, ${clock} | `mode`, `clock` | a board in the lobby |
| `status_loaded` | Loaded game: ${white} vs ${black} | `white`, `black` (names, `unknown_name` for none) | a loaded game |
| `status_loaded_chess960` | Loaded game: ${white} vs ${black} (Chess960) | `white`, `black` | a loaded Chess960 game |
| `status_time_up` | ${color}'s time is up | `color` | in a game, the side to move out of time, before the owner ends it |
| `status_to_move` | ${color} to move | `color` | in a game |
| `status_to_move_in_check` | ${color} to move, in check | `color` | in a game, the side to move in check |
| `status_playing_on` | Playing on untimed: ${color} ran out of time | `color` (the side out of time) | in a game played on untimed after a flag fall |
| `status_draw_offer` | ${color} offers a draw | `color` | a draw offer pending |
| `status_takeback_to_here` | ${color} asks to take back to here | `color` | a takeback request pending (**UI** only, which shows it with the replay) |
| `status_takeback` | ${color} asks for a takeback | `color` | a takeback request pending (**2D view** only, whose replay is its own) |
| `status_ending` | ${ending} (${result}) | `ending` (an ending's text, below), `result` (`1-0`, `0-1`, `1/2-1/2`) | an ended or loaded game |
| `status_unfinished_game` | Unfinished game (*) | | a loaded game without a result |
| `status_draw_time_forfeit` | Draw: ${flagged} ran out of time, ${other} can't checkmate (${result}) | `flagged`, `other` (colours), `result` | an ended game drawn on time, in place of `status_ending` |
| `replay_custom` | Replay: custom position | | the board's replay after free moves (**UI** only) |
| `replay_start` | Replay: start (${ply} of ${count}) | `ply`, `count` | the replay at the start (UI); the 2D view's own replay at the start |
| `replay_move` | Replay: ${move} ${san} (${ply} of ${count}) | `move` (`2.` or `2...`), `san` (the move, or `unknown_name`), `ply`, `count` | the replay after a move (UI); the 2D view's own replay, not on the latest position |

### Endings (built in code)

How a game ended. No code names these keys whole: `ChessGame.EndingKey(ending, winner)` builds them (`"ending_checkmate_" + "white"`), and its result is every key here. They show as `${ending}` in `status_ending` (UI, 2D view) and in the wall's rows.

| Key | English | Built for |
|---|---|---|
| `ending_unfinished` | Unfinished | `EndingNone` or `EndingUnterminated` (no result) |
| `ending_white_wins` | White wins | `EndingUnknown` (a result tag with no known reason), White winning |
| `ending_black_wins` | Black wins | the same, Black winning |
| `ending_draw` | Draw | `EndingUnknown` drawn; also checkmate, resignation or king captured with no winner (never made by the board) |
| `ending_checkmate_white` | White wins by checkmate | `EndingCheckmate`, White winning |
| `ending_checkmate_black` | Black wins by checkmate | the same, Black |
| `ending_resignation_white` | White wins by resignation | `EndingResignation`, White winning |
| `ending_resignation_black` | Black wins by resignation | the same, Black |
| `ending_king_captured_white` | White wins by capturing the king | `EndingKingCaptured`, White winning |
| `ending_king_captured_black` | Black wins by capturing the king | the same, Black |
| `ending_time_forfeit_white` | White wins on time | `EndingTimeForfeit`, White winning |
| `ending_time_forfeit_black` | Black wins on time | the same, Black |
| `ending_time_forfeit_draw` | Draw on time | `EndingTimeForfeit` drawn (the wall; the UI and 2D view write `status_draw_time_forfeit` instead) |
| `ending_stalemate` | Draw by stalemate | `EndingStalemate` |
| `ending_draw_agreement` | Draw by agreement | `EndingDrawAgreement` |
| `ending_threefold_repetition` | Draw by threefold repetition | `EndingThreefoldRepetition` |
| `ending_fivefold_repetition` | Draw by fivefold repetition | `EndingFivefoldRepetition` |
| `ending_fifty_move_rule` | Draw by the fifty-move rule | `EndingFiftyMoveRule` |
| `ending_seventy_five_move_rule` | Draw by the seventy-five-move rule | `EndingSeventyFiveMoveRule` |
| `ending_insufficient_material` | Draw: insufficient material | `EndingInsufficientMaterial` (also a dead position) |

### Hover texts

| Key | English | Shows |
|---|---|---|
| `hover_move` | Move | **view**: every piece's pickup; **holder**: the grip bar |
| `hover_resize` | Resize | **holder**: the corner |
| `hover_select` | Select | **view**: a piece the local player may click to move |
| `hover_put_back` | Put back | **view**: the clicked piece, to put it down again |
| `hover_place` | Place | **view**: a square the clicked piece may move to |
| `hover_capture` | Capture | **view**: a piece of the other side the clicked piece may capture (a square it may move to, en passant too, says `hover_place`) |
| `hover_castle` | Castle | **view**: a piece of its own side the clicked piece may move onto: the rook the clicked king castles with |
| `hover_promote_knight` | Promote to knight | **view**: the knight among the promotion pieces |
| `hover_promote_bishop` | Promote to bishop | **view**: the bishop among them |
| `hover_promote_rook` | Promote to rook | **view**: the rook among them |
| `hover_promote_queen` | Promote to queen | **view**: the queen among them |

The four `hover_promote_` keys are built in code too: `"hover_promote_"` and the promotion piece's type in lower case (`knight`, `bishop`, `rook`, `queen`).

### The history walls

All on the **wall**.

| Key | English | Placeholders | Shows |
|---|---|---|---|
| `wall_title` | Previous games | | the **Game History Wall**'s title (its **Title Key**) |
| `wall_title_personal` | My games | | the **Personal History Wall**'s title (its **Title Key**) |
| `wall_row` | #${number}  ${white} vs ${black}\n${ending} (${result}), ${moves} | `number` (the game's number in the instance), `white`, `black`, `ending`, `result`, `moves` | a row of the instance archive's games |
| `wall_row_chess960` | as `wall_row`, then `, Chess960` | `number`, `white`, `black`, `ending`, `result`, `moves` | such a row for a Chess960 game |
| `wall_row_personal` | ${date}  ${white} vs ${black}\n${ending} (${result}), ${moves} | `date` (when it started, in the player's time: `2026-10-26 21:30`), `white`, `black`, `ending`, `result`, `moves` | a row of the personal archive's games |
| `wall_row_personal_chess960` | as `wall_row_personal`, then `, Chess960` | `date`, `white`, `black`, `ending`, `result`, `moves` | such a row for a Chess960 game |
| `wall_export` | Export PGN | | a row's export button |
| `wall_export_hide` | Hide PGN | | the same while its PGN shows |
| `wall_delete` | Delete | | a row's delete button (personal wall) |
| `wall_delete_sure` | Sure? | | the delete button, pressed once |
| `wall_pgn_working` | Working… | | the PGN box while the PGN is being written |

Every row key's text may use any of the seven placeholders: `wall_row` could show the date too, and `wall_row_personal` has no number to show (`0`). A wall of the world's own can name another key in its **Title Key**: a world's pack then holds that key, in every language it has (English included, or the title shows the key).

### The settings menu

All on the **menu**.

| Key | English | Placeholders | Shows |
|---|---|---|---|
| `settings_title` | Chess settings | | the title |
| `settings_scope` | Settings for | | the scope row: All boards or one board (`board_number`) |
| `settings_all_boards` | All boards | | the scope row's value for the arbiter's own settings |
| `settings_item_takebacks` | Takebacks | | a setting's row |
| `settings_item_draw_rules` | All draw rules | | as above |
| `settings_item_draw_agreement` | ␣␣Draw by agreement | | as above (the two leading spaces indent it under All draw rules) |
| `settings_item_threefold_repetition` | ␣␣Threefold repetition (claim) | | as above |
| `settings_item_fifty_move_rule` | ␣␣Fifty-move rule (claim) | | as above |
| `settings_item_fivefold_repetition` | ␣␣Fivefold repetition | | as above |
| `settings_item_seventy_five_move_rule` | ␣␣Seventy-five-move rule | | as above |
| `settings_item_dead_position` | ␣␣Dead position | | as above |
| `settings_item_mode_forced` | Force the mode | | as above |
| `settings_item_forced_mode` | ␣␣Forced mode | | as above |
| `settings_item_clock_forced` | Force the time control | | as above |
| `settings_item_forced_base_time` | ␣␣Forced base time | | as above |
| `settings_item_forced_increment` | ␣␣Forced increment | | as above |
| `settings_item_play_on_after_flag_fall` | Play on untimed after a flag fall | | as above |
| `settings_item_reset_available_time` | Anyone can reset after idle | | as above |
| `settings_item_auto_reset_time` | Board resets itself after idle | | as above |
| `settings_item_resnapshot` | Apply to boards in use | | the row applying the settings to boards in use |
| `settings_item_pgn_event` | PGN Event | | the PGN Event row (All boards only) |
| `settings_item_pgn_site` | PGN Site | | the PGN Site row (All boards only) |
| `settings_pgn_empty` | None (written ?) | | the PGN rows' empty field |
| `settings_on` | On | | a row's On button; an on value |
| `settings_off` | Off | | a row's Off button; an off value |
| `settings_no_draws` | No draws | | All draw rules' Off button |
| `settings_default` | Default | | a row's Default button (a board's override cleared) |
| `settings_all_default` | All default | | the scope row's Default button (every override of the board cleared) |
| `settings_apply_now` | Apply now | | Apply to boards in use's button |
| `settings_value_off_no_draws` | Off (No draws) | | All draw rules' value when off |
| `settings_value_mixed` | Mixed | | All draw rules' value when the draw rules differ |
| `settings_value_default` | ${value} (default) | `value` (the value's text) | a board's value that comes from the arbiter's |
| `settings_never` | Never | | the two idle times' value when off |
| `duration_seconds` | ${seconds} s | `seconds` | a time under a minute (the idle times, forced base time and forced increment) |
| `duration_minutes` | ${minutes} min | `minutes` | a time in whole minutes |
| `duration_hours` | ${hours} h | `hours` | a time in whole hours |
| `duration_minutes_seconds` | ${minutes} min ${seconds} s | `minutes`, `seconds` | any other time |
| `resnapshot_no_such_board` | No such board | | Apply now's result, the scope's board gone |
| `resnapshot_none_in_use` | No board in use: each takes the settings when next used | | Apply now's result, no board in use |
| `resnapshot_updated_one` | 1 board updated | | Apply now's result, one board updated |
| `resnapshot_updated_many` | ${count} boards updated | `count` | Apply now's result, any other number |
| `resnapshot_not_in_use` | ${text}, ${count} not in use | `text` (the result so far), `count` | added to the result for boards not in use |
| `resnapshot_refused` | ${text}, ${count} refused | `text`, `count` | added to the result for boards that refused |
| `settings_notice_simple_auth_missing` | Settings can't be changed: the Chess Arbiter's Simple Auth field is empty, … | | the notice when the arbiter has no `SimpleAuth` |
| `settings_notice_organizers_only` | Only organizers can change these settings. | | the notice for a player who isn't an organizer |
| `settings_notice_no_arbiter` | No arbiter assigned. | | the notice when the menu has no arbiter |

(␣ is a space: keep them in a translation, where the row's label is indented.)

### Not shown by the package

| Key | English | Placeholders |
|---|---|---|
| `draw_offered` | ${name} offers a draw | `name` (a player's name) |
| `pgn_log_hint` | The PGN is also logged to the VRChat log file. | |

In the pack and kept as the others are, but none of the package's objects shows them yet: the UI says a draw offer by colour (`status_draw_offer`), and a world's own UI may use these. A translation pack translates them too.
