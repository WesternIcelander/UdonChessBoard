using System.Text;
using System.Text.RegularExpressions;
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

        // ---- Import (docs/pgn.md, Import syntax) ----

        // One token of a PGN, after any whitespace, matched at a given position (\G). Adapted from the Java's
        // TOKEN_PATTERN and TAG_PATTERN: atomic groups in place of possessive quantifiers, and every token the port
        // accepts is listed, since a position no alternative matches is a failure rather than skipped text. Groups:
        // - tag: a tag pair, with name and its still escaped value (one line, up to the closing unescaped quote);
        // - result: a result token, which ends the move text;
        // - number: a move number, "12." or "12...";
        // - move: a move's SAN with any +/#, without its annotation (!, ?, !!, ??, !?, ?!), and only when followed
        //   by whitespace, a comment, a variation, a NAG or the end, so "hello" and "e4x" aren't read as moves;
        // - comment: the text of a {} comment, which may span lines (an unclosed { matches nothing);
        // - nag: a NAG ($1) or an annotation standing alone;
        // - open, close: a variation's ( and );
        // - end: the end of the text.
        // A ; comment (to the end of the line) and a line starting with % match with no named group.
        public const string TokenPattern = @"\G[\s﻿]*(?:"
            + @"(?<tag>\[[ \t]*(?<name>\w+)[ \t]*""(?<value>(?>[^""\\\r\n]|\\[^\r\n])*)""[ \t]*\])"
            + @"|(?<result>1-0|0-1|1/2-1/2|\*)"
            + @"|(?<number>[0-9]+\.(?:\.\.)?)"
            + @"|(?<move>(?:[PNBRQK]?[a-h]?[1-8]?x?[a-h][1-8](?:=[NBRQ])?|O-O(?:-O)?|0-0(?:-0)?)[+#]?)"
            + @"(?:[!?]{1,2})?(?=[\s{}();$]|\z)"
            + @"|\{(?<comment>(?>[^}]*))\}"
            + @"|;(?>[^\n]*)"
            + @"|(?m:^%(?>[^\n]*))"
            + @"|(?<nag>\$[0-9]+|[!?]{1,2})"
            + @"|(?<open>\()"
            + @"|(?<close>\))"
            + @"|(?<end>\z))";

        // A [%clk H:MM:SS] command in a comment, seconds with an optional fraction (docs/clocks.md).
        public const string ClockPattern =
            @"\[%clk\s+(?<h>[0-9]{1,9}):(?<m>[0-9]{1,2}):(?<s>[0-9]{1,2})(?:\.(?<f>[0-9]+))?\s*\]";

        // The tags import reads, as indices into the tags array ReadNext fills. All other tags are ignored.
        public const int TagFen = 0;
        public const int TagVariant = 1;
        public const int TagWhite = 2;
        public const int TagBlack = 3;
        public const int TagResult = 4;
        public const int TagGameId = 5;
        public const int TagTimeControl = 6;
        public const int TagCount = 7;

        // The reading state ReadNext keeps between calls (an int[CursorLength]), so an import can stop after any move
        // and go on in a later frame: where in the text it is, how deep in variations, how many main-line moves it
        // has read, and whether the move text has started (1) or it is still in the tag section (0).
        public const int CursorPosition = 0;
        public const int CursorDepth = 1;
        public const int CursorMoves = 2;
        public const int CursorInMoveText = 3;
        public const int CursorLength = 4;

        // What ReadNext found.
        public const int ReadMove = 0;
        public const int ReadClock = 1;
        public const int ReadEnd = 2;
        public const int ReadError = 3;

        // The name of a tag import reads (TagFen...), as written in a PGN.
        public static string TagName(int tag) {
            switch (tag) {
                case TagFen: return "FEN";
                case TagVariant: return "Variant";
                case TagWhite: return "White";
                case TagBlack: return "Black";
                case TagResult: return "Result";
                case TagGameId: return "GameID";
                case TagTimeControl: return "TimeControl";
            }
            return "";
        }

        // Which tag import reads a tag name is (case-sensitive, as in the PGN standard), -1 for any other.
        public static int TagIndex(string name) {
            for (int tag = 0; tag < TagCount; tag++) {
                if (TagName(tag) == name) return tag;
            }
            return -1;
        }

        // Prepares cursor (int[CursorLength]) and tags (string[TagCount]) for reading a PGN from its start. Tags the
        // PGN doesn't have stay "".
        public static void StartReading(int[] cursor, string[] tags) {
            for (int i = 0; i < CursorLength; i++) cursor[i] = 0;
            for (int i = 0; i < TagCount; i++) tags[i] = "";
        }

        // Reads a PGN (a single game) up to the next thing an import needs, using tokenRegex (TokenPattern) and
        // clockRegex (ClockPattern), and the state in cursor (see StartReading). Returns:
        // - ReadMove: the next main-line move, its SAN in san, as written (with any +/#) except that castling is
        //   always O-O/O-O-O and annotations are left out. cursor[CursorMoves] counts it.
        // - ReadClock: a [%clk] in a comment after a main-line move, in milliseconds in clock; it is the clock of
        //   the move read last (a later one after the same move replaces it). Clocks before the first move and
        //   inside variations are ignored, as is a comment's other text.
        // - ReadEnd: the result token, the end of the text, or (with no result token) a tag after the move text has
        //   started, i.e. the next game's tags. Whatever follows is not read.
        // - ReadError: text that is no PGN token, an unclosed { or (, a ) with no (, or the end of the move text
        //   inside a variation. cursor[CursorPosition] is where the bad token starts (before its whitespace), and
        //   cursor[CursorMoves] the number of main-line moves read before it, i.e. the failing ply.
        // Tag pairs (FEN, Variant, White, Black, Result, GameID, TimeControl) are stored in tags as they are read,
        // unescaped; all come before the first move, so a FEN tag is known before the first ReadMove. A tag read
        // twice keeps its last value. Move numbers, NAGs, annotations, ; comments and % lines are skipped, and so
        // are variations with everything in them. Move numbers aren't checked against the moves.
        public static int ReadNext(Regex tokenRegex, Regex clockRegex, string pgn, int[] cursor, string[] tags,
            out string san, out int clock) {
            san = "";
            clock = -1;
            while (true) {
                Match match = tokenRegex.Match(pgn, cursor[CursorPosition]);
                if (!match.Success) return ReadError;
                if (match.Groups["end"].Success) {
                    return cursor[CursorDepth] > 0 ? ReadError : ReadEnd;
                }
                if (match.Groups["tag"].Success) {
                    if (cursor[CursorInMoveText] != 0) {
                        return cursor[CursorDepth] > 0 ? ReadError : ReadEnd;
                    }
                    int tag = TagIndex(match.Groups["name"].Value);
                    if (tag >= 0) tags[tag] = UnescapeTagValue(match.Groups["value"].Value);
                    cursor[CursorPosition] = match.Index + match.Length;
                    continue;
                }
                if (match.Groups["comment"].Success) {
                    cursor[CursorPosition] = match.Index + match.Length;
                    if (cursor[CursorDepth] == 0 && cursor[CursorMoves] > 0) {
                        clock = ClockFromComment(clockRegex, match.Groups["comment"].Value);
                        if (clock >= 0) return ReadClock;
                    }
                    continue;
                }
                if (match.Groups["result"].Success) {
                    if (cursor[CursorDepth] > 0) return ReadError;
                    cursor[CursorPosition] = match.Index + match.Length;
                    cursor[CursorInMoveText] = 1;
                    return ReadEnd;
                }
                if (match.Groups["close"].Success) {
                    if (cursor[CursorDepth] == 0) return ReadError;
                    cursor[CursorDepth]--;
                } else if (match.Groups["open"].Success) {
                    cursor[CursorDepth]++;
                }
                cursor[CursorPosition] = match.Index + match.Length;
                if (match.Groups["move"].Success) {
                    cursor[CursorInMoveText] = 1;
                    if (cursor[CursorDepth] == 0) {
                        cursor[CursorMoves]++;
                        san = match.Groups["move"].Value.Replace("0", "O");
                        return ReadMove;
                    }
                } else if (match.Groups["number"].Success || match.Groups["nag"].Success
                    || match.Groups["open"].Success || match.Groups["close"].Success) {
                    cursor[CursorInMoveText] = 1;
                }
            }
        }

        // The first [%clk] in a comment's text, using clockRegex (ClockPattern), in milliseconds. Digits after the
        // thousandths are dropped. -1 when there is none, or it doesn't fit in an int (over 596:31:23.647).
        public static int ClockFromComment(Regex clockRegex, string comment) {
            Match match = clockRegex.Match(comment);
            if (!match.Success) return -1;
            long seconds = (long)ChessFen.ParseCount(match.Groups["h"].Value) * 3600
                + ChessFen.ParseCount(match.Groups["m"].Value) * 60 + ChessFen.ParseCount(match.Groups["s"].Value);
            string fraction = match.Groups["f"].Value;
            int milliseconds = 0;
            for (int i = 0; i < 3; i++) {
                milliseconds *= 10;
                if (i < fraction.Length) milliseconds += fraction[i] - '0';
            }
            long total = seconds * 1000 + milliseconds;
            if (total > int.MaxValue) return -1;
            return (int)total;
        }

        // Undoes EscapeTagValue's escapes: a backslash and the character after it become that character.
        public static string UnescapeTagValue(string value) {
            if (value.IndexOf('\\') < 0) return value;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++) {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length) {
                    i++;
                    c = value[i];
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // Reads a TimeControl tag's value (docs/clocks.md): "-" is no clock (0, 0), "base" or "base+increment" in
        // seconds with a base above 0. Anything else (including an empty value) is unknown: returns false with
        // both -1.
        public static bool ParseTimeControl(string text, out int baseSeconds, out int incrementSeconds) {
            baseSeconds = -1;
            incrementSeconds = -1;
            if (text == "-") {
                baseSeconds = 0;
                incrementSeconds = 0;
                return true;
            }
            int plus = text.IndexOf('+');
            int parsedBase = ChessFen.ParseCount(plus < 0 ? text : text.Substring(0, plus));
            int parsedIncrement = plus < 0 ? 0 : ChessFen.ParseCount(text.Substring(plus + 1));
            if (parsedBase <= 0 || parsedIncrement < 0) return false;
            baseSeconds = parsedBase;
            incrementSeconds = parsedIncrement;
            return true;
        }

        // The mode a Variant tag gives (docs/modes.md): ChessGame.ModeChess960 for "Chess960", otherwise Standard.
        public static int ModeFromVariant(string variant) {
            return variant == "Chess960" ? ChessGame.ModeChess960 : ChessGame.ModeStandard;
        }
    }
}
