using System.Text;
using UdonSharp;

namespace SiggiChessBoard {
    // PGN export and import (Java PGN), see docs/pgn.md. Export is plain string building from the game's data, with
    // no replay: the moves' SAN is recorded as they are played.
    // Holds no state; everything comes in through parameters (see docs/architecture.md).
    // Abstract because it is never placed as a component, only used for its constants and static methods.
    public abstract class ChessPgn : UdonSharpBehaviour {
        // The move text's line width, as in the Java's PGN.write (LineWriter(writer, 80)).
        public const int LineWidth = 80;

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

        // The move text of a game's PGN (docs/pgn.md, Export move text): moveCount moves from sans (their recorded
        // SAN), each followed by "{ [%clk H:MM:SS] }" when clocks (null for none) holds a known time for it, then the
        // result worked out from ending and winner. Moves are numbered from the starting position's fullmove number
        // and side to move (ChessRules.White or Black): "12. e4" for White, "12... e5" for Black's first move and for
        // a Black move after a clock comment, otherwise Black's move alone.
        // Lines are wrapped to at most lineWidth characters, breaking only at the space between two units: a
        // numbered or unnumbered move, a clock comment, the result. Units are never split (a unit longer than
        // lineWidth gets a line of its own). Every line, the last included, ends with "\n".
        public static string WriteMoveText(string[] sans, int[] clocks, int moveCount, int firstMoveNumber,
            int firstSideToMove, int ending, int winner, int lineWidth) {
            StringBuilder sb = new StringBuilder();
            int lineStart = 0;
            int moveNumber = firstMoveNumber;
            int side = firstSideToMove;
            bool numberBlack = true;
            for (int i = 0; i < moveCount; i++) {
                string unit;
                if (side == ChessRules.White) {
                    unit = moveNumber.ToString() + ". " + sans[i];
                } else if (numberBlack) {
                    unit = moveNumber.ToString() + "... " + sans[i];
                } else {
                    unit = sans[i];
                }
                lineStart = AppendUnit(sb, lineStart, unit, lineWidth);
                int clock = clocks == null ? -1 : clocks[i];
                numberBlack = clock >= 0;
                if (numberBlack) {
                    lineStart = AppendUnit(sb, lineStart, "{ [%clk " + ClockText(clock) + "] }", lineWidth);
                }
                if (side == ChessRules.Black) moveNumber++;
                side = side == ChessRules.White ? ChessRules.Black : ChessRules.White;
            }
            AppendUnit(sb, lineStart, ResultText(ending, winner), lineWidth);
            sb.Append('\n');
            return sb.ToString();
        }

        // A [%clk] value (docs/clocks.md): milliseconds as H:MM:SS, with tenths (H:MM:SS.t) when they aren't zero.
        // Hundredths and below are dropped, as a clock display drops them. Hours are not limited to one digit.
        // milliseconds must not be negative (-1, unknown, gets no [%clk] at all).
        public static string ClockText(int milliseconds) {
            int tenths = milliseconds / 100;
            int seconds = tenths / 10;
            int minutes = seconds / 60;
            string text = (minutes / 60).ToString() + ":" + TwoDigits(minutes % 60) + ":"
                + TwoDigits(seconds % 60);
            if (tenths % 10 != 0) text += "." + (tenths % 10).ToString();
            return text;
        }

        static string TwoDigits(int value) {
            return value < 10 ? "0" + value.ToString() : value.ToString();
        }

        // Appends a unit of move text, after a space or, if that would make the line longer than lineWidth, on a
        // new line. lineStart is where the current line starts in sb; returns where it starts after the unit.
        static int AppendUnit(StringBuilder sb, int lineStart, string unit, int lineWidth) {
            int lineLength = sb.Length - lineStart;
            if (lineLength > 0) {
                if (lineLength + 1 + unit.Length > lineWidth) {
                    sb.Append('\n');
                    lineStart = sb.Length;
                } else {
                    sb.Append(' ');
                }
            }
            sb.Append(unit);
            return lineStart;
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
