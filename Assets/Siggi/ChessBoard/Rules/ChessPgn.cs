using System.Text;
using UdonSharp;

namespace SiggiChessBoard {
    // PGN export and import (Java PGN), see docs/pgn.md. Export is plain string building from the game's data, with
    // no replay: the moves' SAN is recorded as they are played.
    // Holds no state; everything comes in through parameters (see docs/architecture.md).
    // Abstract because it is never placed as a component, only used for its constants and static methods.
    public abstract class ChessPgn : UdonSharpBehaviour {
        // The tag section of a game's PGN, one "[Name "value"]\n" line per tag, in this order:
        // - the seven-tag roster: Event, Site, Date (PGN form yyyy.MM.dd), Round (always "-"), White, Black and the
        //   Result worked out from ending and winner. An empty or null Event, Site, White or Black is written "?",
        //   an empty Date "????.??.??", as the PGN standard writes unknown values;
        // - Variant "Chess960" for a Chess960 game (nothing for Standard);
        // - SetUp "1" and FEN when startFen is given and either isn't the standard start or the game is Chess960;
        // - TimeControl: "base" or "base+increment" in seconds, "-" for no clock (baseSeconds <= 0);
        // - GameID and ContinuedFrom ("<GameID>/<ply>"), each only when not empty;
        // - Termination (docs/game-endings.md), except for a game still in progress (EndingNone).
        // ending is a ChessGame.Ending* value, winner ChessRules.White, Black or NoColor (a draw). Every value is
        // escaped (EscapeTagValue). The blank line that ends the tag section is not included.
        public static string WriteTags(string eventName, string site, string date, string white, string black,
            int ending, int winner, int mode, string startFen, int baseSeconds, int incrementSeconds, string gameId,
            string continuedFrom) {
            StringBuilder sb = new StringBuilder();
            AppendTag(sb, "Event", OrUnknown(eventName, "?"));
            AppendTag(sb, "Site", OrUnknown(site, "?"));
            AppendTag(sb, "Date", OrUnknown(date, "????.??.??"));
            AppendTag(sb, "Round", "-");
            AppendTag(sb, "White", OrUnknown(white, "?"));
            AppendTag(sb, "Black", OrUnknown(black, "?"));
            AppendTag(sb, "Result", ResultText(ending, winner));
            bool chess960 = mode == ChessGame.ModeChess960;
            if (chess960) AppendTag(sb, "Variant", "Chess960");
            if (!string.IsNullOrEmpty(startFen) && (chess960 || startFen != ChessFen.StartFen)) {
                AppendTag(sb, "SetUp", "1");
                AppendTag(sb, "FEN", startFen);
            }
            AppendTag(sb, "TimeControl", TimeControlText(baseSeconds, incrementSeconds));
            if (!string.IsNullOrEmpty(gameId)) AppendTag(sb, "GameID", gameId);
            if (!string.IsNullOrEmpty(continuedFrom)) AppendTag(sb, "ContinuedFrom", continuedFrom);
            string termination = TerminationText(ending);
            if (termination != "") AppendTag(sb, "Termination", termination);
            return sb.ToString();
        }

        // The Result tag (and the move text's last token): "*" for a game in progress or unterminated, otherwise by
        // the winner: "1-0", "0-1", or "1/2-1/2" for NoColor.
        public static string ResultText(int ending, int winner) {
            if (ending == ChessGame.EndingNone || ending == ChessGame.EndingUnterminated) return "*";
            if (winner == ChessRules.White) return "1-0";
            if (winner == ChessRules.Black) return "0-1";
            return "1/2-1/2";
        }

        // The Termination tag's value for an ending (docs/game-endings.md), "" for EndingNone or an unknown value.
        public static string TerminationText(int ending) {
            switch (ending) {
                case ChessGame.EndingCheckmate: return "checkmate";
                case ChessGame.EndingResignation: return "resignation";
                case ChessGame.EndingTimeForfeit: return "time forfeit";
                case ChessGame.EndingStalemate: return "stalemate";
                case ChessGame.EndingDrawAgreement: return "draw agreement";
                case ChessGame.EndingThreefoldRepetition: return "threefold repetition";
                case ChessGame.EndingFivefoldRepetition: return "fivefold repetition";
                case ChessGame.EndingFiftyMoveRule: return "fifty-move rule";
                case ChessGame.EndingSeventyFiveMoveRule: return "seventy-five-move rule";
                case ChessGame.EndingInsufficientMaterial: return "insufficient material";
                case ChessGame.EndingUnterminated: return "unterminated";
                case ChessGame.EndingKingCaptured: return "king captured";
            }
            return "";
        }

        // The TimeControl tag's value (docs/clocks.md): "-" for no clock, "base" for no increment, else "base+inc".
        public static string TimeControlText(int baseSeconds, int incrementSeconds) {
            if (baseSeconds <= 0) return "-";
            if (incrementSeconds <= 0) return baseSeconds.ToString();
            return baseSeconds.ToString() + "+" + incrementSeconds.ToString();
        }

        // A tag value as the PGN standard requires: \ becomes \\ and " becomes \". Control characters (the Java's
        // Character.isISOControl: U+0000-U+001F and U+007F-U+009F) become a space, since a tag must stay on one line.
        // Everything else, surrogate pairs included, is kept as it is. Null gives "".
        public static string EscapeTagValue(string value) {
            if (value == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++) {
                char c = value[i];
                if (c == '\\' || c == '"') {
                    sb.Append('\\');
                    sb.Append(c);
                } else if (c < ' ' || (c >= '\u007f' && c <= '\u009f')) {
                    sb.Append(' ');
                } else {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        static void AppendTag(StringBuilder sb, string name, string value) {
            sb.Append('[');
            sb.Append(name);
            sb.Append(" \"");
            sb.Append(EscapeTagValue(value));
            sb.Append("\"]\n");
        }

        static string OrUnknown(string value, string unknown) {
            return string.IsNullOrEmpty(value) ? unknown : value;
        }
    }
}
