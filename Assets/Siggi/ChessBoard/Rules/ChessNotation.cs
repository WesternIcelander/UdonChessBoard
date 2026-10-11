using UdonSharp;

namespace SiggiChessBoard {
    // Move notation (SAN) for packed moves (Java RegularMove.notation / CastleMove.notation), worked out directly from
    // the position's legal move list instead of trying candidate spellings with the Java's regex parser
    // (docs/legality-checking.md). Castling is written O-O / O-O-O, never with zeros (docs/pgn.md).
    // Holds no state; everything comes in through parameters (see docs/architecture.md).
    // Abstract because it is never placed as a component, only used for its constants and static methods.
    public abstract class ChessNotation : UdonSharpBehaviour {
        // The SAN of move, which must be one of legalMoves[0..legalCount), the legal moves of the position on board
        // and state (ChessMoves.GenerateLegalMoves with the same prohibitSelfCheck), which the caller usually has
        // already. With prohibitSelfCheck, "+" is added when the move gives check and "#" when it also leaves the
        // opponent no legal move, worked out in scratch (an int[ChessMoves.MaxMoves], only used for a check). With
        // it off (Fog of War) there is no suffix (docs/modes.md). The position is left as it was.
        public static string MoveToSan(byte[] board, int[] state, int move, int[] legalMoves, int legalCount,
            int[] scratch, bool prohibitSelfCheck) {
            string san;
            if (((move >> ChessRules.MoveCastleShift) & 1) != 0) {
                // The rook right of the king is king side, whatever the files (Chess960 too).
                int kingFile = (move >> ChessRules.MoveFromShift) & 7;
                int rookFile = (move >> ChessRules.MoveToShift) & 7;
                san = rookFile > kingFile ? "O-O" : "O-O-O";
            } else {
                int to = (move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask;
                int type = (move >> ChessRules.MovePieceTypeShift) & ChessRules.MoveTypeMask;
                int promotion = (move >> ChessRules.MovePromotionShift) & ChessRules.MoveTypeMask;
                bool capture = ((move >> ChessRules.MoveCapturedShift) & ChessRules.MoveTypeMask) != ChessRules.NoPieceType;
                if (type == ChessRules.Pawn) {
                    // A pawn capture always names the from-file, which is always enough: two pawns that can capture
                    // on the same square stand on different files.
                    san = capture ? FileLetter((move >> ChessRules.MoveFromShift) & 7) + "x" : "";
                } else {
                    san = ChessRules.PieceTypeLetter(type) + Disambiguation(move, legalMoves, legalCount);
                    if (capture) san += "x";
                }
                san += ChessRules.SquareName(to);
                if (promotion != ChessRules.NoPieceType) san += "=" + ChessRules.PieceTypeLetter(promotion);
            }
            if (!prohibitSelfCheck) return san;

            ChessPosition.MakeMove(board, state, move);
            int opponent = state[ChessPosition.StateSideToMove];
            if (ChessMoves.IsInCheck(board, state, opponent)) {
                san += ChessMoves.GenerateLegalMoves(board, state, scratch, true) == 0 ? "#" : "+";
            }
            ChessPosition.UnmakeMove(board, state, move);
            return san;
        }

        // What a piece move (not a pawn, not castling) needs between the piece letter and the destination so that no
        // other legal move of the same piece type to the same square reads the same: nothing if there is none, else
        // the from-file if that's unique, else the from-rank if that's unique, else both (docs/legality-checking.md).
        static string Disambiguation(int move, int[] legalMoves, int legalCount) {
            int from = (move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask;
            // Castling never matches: its to is the mover's own rook, which no other move lands on.
            int sameMask = (ChessRules.MoveSquareMask << ChessRules.MoveToShift)
                | (ChessRules.MoveTypeMask << ChessRules.MovePieceTypeShift)
                | (1 << ChessRules.MoveCastleShift);
            int same = move & sameMask;
            bool ambiguous = false;
            bool sameFile = false;
            bool sameRank = false;
            for (int i = 0; i < legalCount; i++) {
                int other = legalMoves[i];
                if ((other & sameMask) != same) continue;
                int otherFrom = (other >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask;
                if (otherFrom == from) continue;
                ambiguous = true;
                if ((otherFrom & 7) == (from & 7)) sameFile = true;
                if ((otherFrom >> 3) == (from >> 3)) sameRank = true;
            }
            if (!ambiguous) return "";
            if (!sameFile) return FileLetter(from & 7);
            if (!sameRank) return RankDigit(from >> 3);
            return ChessRules.SquareName(from);
        }

        // What MoveFromSan returns when no legal move matches, or more than one does. Packed moves are never negative.
        public const int SanNoMove = -1;
        public const int SanAmbiguous = -2;

        // The legal move (of legalMoves[0..legalCount)) that san names (Java Board.parseMove with MovePredicates, for
        // PGN import): SanNoMove if none does or san isn't SAN, SanAmbiguous if more than one does. Any "+"/"#" at the
        // end is ignored, and castling may be written with zeros. Otherwise san is [piece][from file][from rank][x]
        // square[=promotion], as the Java reads it, with these rules:
        // - no piece letter is a pawn ("P" is allowed); the from file and rank, when given, must match, so an
        //   over-disambiguated move ("Ng1f3") is accepted and a move missing its disambiguation is ambiguous;
        // - a pawn's "x" must match whether it captures (en passant included); a piece's "x" needs a capture, but a
        //   piece capture without "x" is accepted, as in the Java;
        // - the promotion must be given exactly when the move promotes ("e8" alone matches no move; the Java reads it
        //   as all four promotions, which fails as well);
        // - O-O is the castling with the rook right of the king, O-O-O left (Chess960 too); "Kg1" never matches a
        //   castling.
        public static int MoveFromSan(string san, int[] legalMoves, int legalCount) {
            int end = san.Length;
            while (end > 0 && (san[end - 1] == '+' || san[end - 1] == '#')) end--;
            string text = san.Substring(0, end);
            bool castle = false;
            bool kingSide = false;
            if (text == "O-O" || text == "0-0") {
                castle = true;
                kingSide = true;
            } else if (text == "O-O-O" || text == "0-0-0") {
                castle = true;
            }
            int type = ChessRules.Pawn;
            int fromFile = -1;
            int fromRank = -1;
            int to = ChessRules.NoSquare;
            int promotion = ChessRules.NoPieceType;
            bool capture = false;
            if (!castle) {
                int start = 0;
                if (end > 0) {
                    int letterType = ChessRules.PieceTypeFromLetter(text[0]);
                    if (letterType != ChessRules.NoPieceType) {
                        type = letterType;
                        start = 1;
                    }
                }
                if (end - start >= 2 && text[end - 2] == '=') {
                    promotion = ChessRules.PieceTypeFromLetter(text[end - 1]);
                    if (promotion == ChessRules.NoPieceType || promotion == ChessRules.Pawn
                        || promotion == ChessRules.King) {
                        return SanNoMove;
                    }
                    end -= 2;
                }
                if (end - start < 2) return SanNoMove;
                to = ChessRules.ParseSquare(text.Substring(end - 2, 2));
                if (to == ChessRules.NoSquare) return SanNoMove;
                end -= 2;
                if (end > start && text[end - 1] == 'x') {
                    capture = true;
                    end--;
                }
                if (end > start && text[end - 1] >= '1' && text[end - 1] <= '8') {
                    fromRank = text[end - 1] - '1';
                    end--;
                }
                if (end > start && text[end - 1] >= 'a' && text[end - 1] <= 'h') {
                    fromFile = text[end - 1] - 'a';
                    end--;
                }
                if (end != start) return SanNoMove;
            }

            int found = SanNoMove;
            for (int i = 0; i < legalCount; i++) {
                int move = legalMoves[i];
                bool isCastle = ((move >> ChessRules.MoveCastleShift) & 1) != 0;
                if (castle) {
                    if (!isCastle) continue;
                    int kingFile = (move >> ChessRules.MoveFromShift) & 7;
                    int rookFile = (move >> ChessRules.MoveToShift) & 7;
                    if ((rookFile > kingFile) != kingSide) continue;
                } else {
                    if (isCastle) continue;
                    if (((move >> ChessRules.MovePieceTypeShift) & ChessRules.MoveTypeMask) != type) continue;
                    if (((move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask) != to) continue;
                    int from = (move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask;
                    if (fromFile >= 0 && (from & 7) != fromFile) continue;
                    if (fromRank >= 0 && (from >> 3) != fromRank) continue;
                    bool takes = ((move >> ChessRules.MoveCapturedShift) & ChessRules.MoveTypeMask)
                        != ChessRules.NoPieceType;
                    if (type == ChessRules.Pawn ? takes != capture : capture && !takes) continue;
                    if (((move >> ChessRules.MovePromotionShift) & ChessRules.MoveTypeMask) != promotion) continue;
                }
                if (found != SanNoMove) return SanAmbiguous;
                found = move;
            }
            return found;
        }

        static string FileLetter(int file) {
            return "" + (char)('a' + file);
        }

        static string RankDigit(int rank) {
            return "" + (char)('1' + rank);
        }
    }
}
