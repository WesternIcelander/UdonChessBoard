using SiggiUSTools;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The player text of the chess system (PLAN.md 13.5; docs/architecture.md, Player text): every string a player
    // reads comes from a SimpleI18n language pack (Languages/en.json, and any a world adds) through one shared I18n,
    // the Chess Language object. A call into the I18n is a call into another behaviour, so each behaviour that shows
    // text loads every string once into a dictionary of its own (Load), again on OnLanguageChange, and looks them up
    // there (Get), filling in the ${name} parts locally. Holds no state; abstract because it is never placed as a
    // component, only used for its static methods.
    public abstract class ChessText : UdonSharpBehaviour {
        // Every key the chess system uses, as in Languages/en.json (ViewTest checks the two match).
        public static string[] Keys() {
            return new string[] {
                "color_white", "color_black", "mode_standard", "mode_chess960", "unknown_name", "board_number",
                "moves_one", "moves_many", "button_join_white", "button_join_black", "button_leave", "button_mode",
                "button_clock_none", "button_clock", "button_start", "button_continue", "button_reset",
                "button_resign", "button_offer_draw", "button_accept_draw", "button_decline_draw",
                "button_claim_draw", "button_claim_draw_threefold", "button_claim_draw_fifty_moves", "button_replay",
                "button_back_to_game", "button_take_back_to_here", "button_accept_takeback",
                "button_decline_takeback", "button_load_here", "button_pgn", "button_load_pgn", "button_hide_pgn",
                "clock_none", "clock_minutes", "clock_seconds", "pgn_copy_hint", "pgn_paste_hint", "pgn_import",
                "pgn_close", "import_paste_first", "import_running", "import_failed", "import_failed_load",
                "import_abandoned", "import_error_syntax", "import_error_illegal_move", "import_error_ambiguous_move",
                "import_error_fen", "import_error_too_long", "player_line", "player_open", "player_flag_fallen",
                "status_reset", "status_lobby", "status_loaded", "status_loaded_chess960", "status_time_up",
                "status_to_move", "status_to_move_in_check", "status_playing_on", "status_draw_offer",
                "status_takeback_to_here", "status_takeback", "status_ending", "status_unfinished_game",
                "status_draw_time_forfeit", "replay_custom", "replay_start", "replay_move", "ending_unfinished",
                "ending_white_wins", "ending_black_wins", "ending_draw", "ending_checkmate_white",
                "ending_checkmate_black", "ending_resignation_white", "ending_resignation_black",
                "ending_king_captured_white", "ending_king_captured_black", "ending_time_forfeit_white",
                "ending_time_forfeit_black", "ending_time_forfeit_draw", "ending_stalemate", "ending_draw_agreement",
                "ending_threefold_repetition", "ending_fivefold_repetition", "ending_fifty_move_rule",
                "ending_seventy_five_move_rule", "ending_insufficient_material", "hover_move", "hover_resize",
                "hover_select", "hover_put_back", "hover_place", "hover_capture", "hover_castle",
                "hover_promote_knight", "hover_promote_bishop", "hover_promote_rook", "hover_promote_queen",
                "wall_title", "wall_title_empty", "wall_row", "wall_row_chess960", "settings_title", "settings_scope",
                "settings_all_boards", "settings_item_takebacks", "settings_item_draw_rules",
                "settings_item_draw_agreement", "settings_item_threefold_repetition", "settings_item_fifty_move_rule",
                "settings_item_fivefold_repetition", "settings_item_seventy_five_move_rule",
                "settings_item_dead_position", "settings_item_mode_forced", "settings_item_forced_mode",
                "settings_item_clock_forced", "settings_item_forced_base_time", "settings_item_forced_increment",
                "settings_item_play_on_after_flag_fall", "settings_item_reset_available_time",
                "settings_item_auto_reset_time", "settings_item_resnapshot", "settings_on", "settings_off",
                "settings_no_draws", "settings_default", "settings_all_default", "settings_apply_now",
                "settings_value_off_no_draws", "settings_value_mixed", "settings_value_default", "settings_never",
                "duration_seconds", "duration_minutes", "duration_hours", "duration_minutes_seconds",
                "resnapshot_no_such_board", "resnapshot_none_in_use", "resnapshot_updated_one",
                "resnapshot_updated_many", "resnapshot_not_in_use", "resnapshot_refused",
                "settings_notice_simple_auth_missing", "settings_notice_organizers_only",
                "settings_notice_no_arbiter",
            };
        }

        // Every string of Keys in i18n's language now, by key. With no I18n (the field empty: the sanity check and
        // the builders fill it) each string is its key, and owner logs the error.
        public static DataDictionary Load(I18n i18n, Object owner) {
            DataDictionary texts = new DataDictionary();
            string[] keys = Keys();
            bool valid = Utilities.IsValid(i18n);
            if (!valid) Debug.LogError("[ChessText] " + owner.name + ": the I18n field is empty (add Chess Language).");
            for (int i = 0; i < keys.Length; i++) texts.SetValue(keys[i], valid ? i18n._(keys[i]) : keys[i]);
            return texts;
        }

        // The string for key in texts (from Load); the key itself when it's missing, as I18n shows a missing one.
        public static string Get(DataDictionary texts, string key) {
            DataToken value;
            if (texts != null && texts.TryGetValue(key, TokenType.String, out value)) return value.String;
            return key;
        }

        // Get with ${name} filled in with value, and the same for each further pair.
        public static string Get(DataDictionary texts, string key, string name, string value) {
            return GetWith(texts, key, new string[] { name, value });
        }

        public static string Get(DataDictionary texts, string key, string name1, string value1, string name2,
            string value2) {
            return GetWith(texts, key, new string[] { name1, value1, name2, value2 });
        }

        public static string Get(DataDictionary texts, string key, string name1, string value1, string name2,
            string value2, string name3, string value3) {
            return GetWith(texts, key, new string[] { name1, value1, name2, value2, name3, value3 });
        }

        // Get with each ${name} filled in from args, which alternate names and values.
        public static string GetWith(DataDictionary texts, string key, string[] args) {
            return Fill(Get(texts, key), args);
        }

        // template with each ${name} replaced by its value in args (alternating names and values), in one pass, so a
        // value holding ${...} (a player's name) stays as it is. A name not in args is left as it is.
        public static string Fill(string template, string[] args) {
            string result = "";
            int pos = 0;
            while (true) {
                int start = template.IndexOf("${", pos);
                if (start < 0) break;
                int end = template.IndexOf('}', start + 2);
                if (end < 0) break;
                string name = template.Substring(start + 2, end - start - 2);
                int found = -1;
                for (int i = 0; i + 1 < args.Length; i += 2) {
                    if (args[i] == name) {
                        found = i + 1;
                        break;
                    }
                }
                result += template.Substring(pos, start - pos)
                    + (found >= 0 ? args[found] : template.Substring(start, end - start + 1));
                pos = end + 1;
            }
            return result + template.Substring(pos);
        }

        // ---- Texts several behaviours share ----

        public static string ColorText(DataDictionary texts, int color) {
            return Get(texts, color == ChessRules.White ? "color_white" : "color_black");
        }

        public static string ModeText(DataDictionary texts, int mode) {
            return Get(texts, mode == ChessGame.ModeChess960 ? "mode_chess960" : "mode_standard");
        }

        // "Board 2" for board number 1.
        public static string BoardText(DataDictionary texts, int boardNumber) {
            return Get(texts, "board_number", "number", (boardNumber + 1).ToString());
        }

        // How a game ended (ChessGame.EndingKey): "White wins by checkmate", "Unfinished".
        public static string EndingText(DataDictionary texts, int ending, int winner) {
            return Get(texts, ChessGame.EndingKey(ending, winner));
        }

        // "1 move", "12 moves".
        public static string MovesText(DataDictionary texts, int moves) {
            return moves == 1 ? Get(texts, "moves_one") : Get(texts, "moves_many", "count", moves.ToString());
        }

        // A player's name, or "?" when there's none.
        public static string NameText(DataDictionary texts, string name) {
            return name != null && name.Length > 0 ? name : Get(texts, "unknown_name");
        }
    }
}
