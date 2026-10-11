using UdonSharp;

namespace SiggiChessBoard {
    // Move generation (Java MoveFinder*) on a position's board and state arrays (see ChessPosition). Generates
    // pseudo-legal moves: each piece's moves by its movement rules, without checking whether the mover's own king is
    // left in check. Castling checks the rights, the rook and a clear path, but not whether the king is in or passes
    // through check. The legal move list (GenerateLegalMoves) filters these with the attack check.
    // Holds no state; everything comes in through parameters (see docs/architecture.md).
    // Abstract because it is never placed as a component, only used for its constants and static methods.
    public abstract class ChessMoves : UdonSharpBehaviour {
        // A move list big enough for any position: no reachable position has more than about 220 pseudo-legal moves.
        public const int MaxMoves = 320;

        // Step directions, with no static arrays: entry d of a table is ((Table >> (d * Bits)) & Mask) - Bias.
        // Knight jumps, 3 bits per entry, offsets -2..2 stored as 0..4.
        const int KnightFileSteps = (3 << 0) | (4 << 3) | (4 << 6) | (3 << 9) | (1 << 12) | (0 << 15) | (0 << 18) | (1 << 21);
        const int KnightRankSteps = (4 << 0) | (3 << 3) | (1 << 6) | (0 << 9) | (0 << 12) | (1 << 15) | (3 << 18) | (4 << 21);
        // King and slider steps, 2 bits per entry, offsets -1..1 stored as 0..2. Entries 0-3 are orthogonal (rook),
        // 4-7 diagonal (bishop): right, left, up, down, up-right, down-right, up-left, down-left.
        const int LineFileSteps = (2 << 0) | (0 << 2) | (1 << 4) | (1 << 6) | (2 << 8) | (2 << 10) | (0 << 12) | (0 << 14);
        const int LineRankSteps = (1 << 0) | (1 << 2) | (2 << 4) | (0 << 6) | (2 << 8) | (0 << 10) | (2 << 12) | (0 << 14);

        // All pseudo-legal moves for the side to move, written to moves from index 0. Returns the count.
        // moves must hold at least MaxMoves.
        public static int GenerateMoves(byte[] board, int[] state, int[] moves) {
            int side = state[ChessPosition.StateSideToMove];
            int count = 0;
            for (int sq = 0; sq < 64; sq++) {
                int piece = board[sq];
                if (piece == ChessRules.Empty || ((piece >> 3) & 1) != side) continue;
                count = GeneratePieceMoves(board, state, sq, moves, count);
            }
            return count;
        }

        // The pseudo-legal moves of the piece on from, appended to moves at index count. Returns the new count.
        // The piece should belong to the side to move: the moves carry the state's castling rights and en passant,
        // which MakeMove expects to be the side to move's. Nothing for an empty square.
        public static int GeneratePieceMoves(byte[] board, int[] state, int from, int[] moves, int count) {
            switch (board[from] & 7) {
                case ChessRules.Pawn: return GeneratePawnMoves(board, state, from, moves, count);
                case ChessRules.Knight: return GenerateKnightMoves(board, state, from, moves, count);
                case ChessRules.Bishop: return GenerateSliderMoves(board, state, from, 4, 8, moves, count);
                case ChessRules.Rook: return GenerateSliderMoves(board, state, from, 0, 4, moves, count);
                case ChessRules.Queen: return GenerateSliderMoves(board, state, from, 0, 8, moves, count);
                case ChessRules.King: return GenerateKingMoves(board, state, from, moves, count);
                default: return count;
            }
        }

        // The before-the-move fields every move made in this position carries (bits 22-29).
        static int PrevStateBits(int[] state) {
            int bits = state[ChessPosition.StateCastlingRights] << ChessRules.MoveCastlingRightsShift;
            int enPassantFile = state[ChessPosition.StateEnPassantFile];
            if (enPassantFile >= 0) {
                bits |= (1 << ChessRules.MoveEnPassantValidShift) | (enPassantFile << ChessRules.MoveEnPassantFileShift);
            }
            return bits;
        }

        // Java MoveFinderPawn: one or two squares forward onto empty squares, diagonal captures of the other colour
        // (not of its own pieces, which the Java allowed) and en passant. A move to the last rank becomes four
        // promotions. A pawn on the first or last rank has no moves.
        public static int GeneratePawnMoves(byte[] board, int[] state, int from, int[] moves, int count) {
            int piece = board[from];
            int color = (piece >> 3) & 1;
            int rank = from >> 3;
            if (rank == ChessRules.Rank1 || rank == ChessRules.Rank8) return count;
            int file = from & 7;
            int step = color == ChessRules.White ? 8 : -8;
            int startRank = color == ChessRules.White ? ChessRules.Rank2 : ChessRules.Rank7;
            int lastRank = color == ChessRules.White ? ChessRules.Rank8 : ChessRules.Rank1;
            bool promotes = rank + (step >> 3) == lastRank;
            int bits = from | (ChessRules.Pawn << ChessRules.MovePieceTypeShift) | PrevStateBits(state);

            int to = from + step;
            if (board[to] == ChessRules.Empty) {
                count = AddPawnMove(moves, count, bits | (to << ChessRules.MoveToShift), promotes);
                int twoTo = to + step;
                if (rank == startRank && board[twoTo] == ChessRules.Empty) {
                    moves[count++] = bits | (twoTo << ChessRules.MoveToShift);
                }
            }

            // The en passant square is on rank 6 when White is to move, rank 3 when Black is. Only a pawn of the side
            // to move can be beside it.
            int enPassantFile = state[ChessPosition.StateEnPassantFile];
            int enPassantSquare = ChessRules.NoSquare;
            if (enPassantFile >= 0) {
                enPassantSquare = (state[ChessPosition.StateSideToMove] == ChessRules.White ? ChessRules.Rank6 : ChessRules.Rank3) << 3
                    | enPassantFile;
            }
            for (int side = -1; side <= 1; side += 2) {
                int captureFile = file + side;
                if (captureFile < 0 || captureFile > 7) continue;
                to = from + step + side;
                int target = board[to];
                if (target != ChessRules.Empty) {
                    if (((target >> 3) & 1) == color) continue;
                    count = AddPawnMove(moves, count,
                        bits | (to << ChessRules.MoveToShift) | ((target & 7) << ChessRules.MoveCapturedShift), promotes);
                } else if (to == enPassantSquare) {
                    moves[count++] = bits | (to << ChessRules.MoveToShift) | (ChessRules.Pawn << ChessRules.MoveCapturedShift);
                }
            }
            return count;
        }

        // Adds the move, or its four promotions (queen, rook, bishop, knight) when it reaches the last rank.
        static int AddPawnMove(int[] moves, int count, int move, bool promotes) {
            if (!promotes) {
                moves[count++] = move;
                return count;
            }
            moves[count++] = move | (ChessRules.Queen << ChessRules.MovePromotionShift);
            moves[count++] = move | (ChessRules.Rook << ChessRules.MovePromotionShift);
            moves[count++] = move | (ChessRules.Bishop << ChessRules.MovePromotionShift);
            moves[count++] = move | (ChessRules.Knight << ChessRules.MovePromotionShift);
            return count;
        }

        // Java MoveFinderKnight.
        public static int GenerateKnightMoves(byte[] board, int[] state, int from, int[] moves, int count) {
            int color = (board[from] >> 3) & 1;
            int file = from & 7;
            int rank = from >> 3;
            int bits = from | (ChessRules.Knight << ChessRules.MovePieceTypeShift) | PrevStateBits(state);
            for (int d = 0; d < 8; d++) {
                int toFile = file + ((KnightFileSteps >> (d * 3)) & 7) - 2;
                int toRank = rank + ((KnightRankSteps >> (d * 3)) & 7) - 2;
                if (toFile < 0 || toFile > 7 || toRank < 0 || toRank > 7) continue;
                int to = (toRank << 3) | toFile;
                int target = board[to];
                if (target == ChessRules.Empty) {
                    moves[count++] = bits | (to << ChessRules.MoveToShift);
                } else if (((target >> 3) & 1) != color) {
                    moves[count++] = bits | (to << ChessRules.MoveToShift) | ((target & 7) << ChessRules.MoveCapturedShift);
                }
            }
            return count;
        }

        // Java MoveFinderBishop/Rook/Queen: along the directions firstDirection up to (not including) lastDirection
        // of the line tables (0-4 rook, 4-8 bishop, 0-8 queen), up to and including the first piece if it can be
        // captured.
        public static int GenerateSliderMoves(byte[] board, int[] state, int from, int firstDirection, int lastDirection,
            int[] moves, int count) {
            int piece = board[from];
            int color = (piece >> 3) & 1;
            int file = from & 7;
            int rank = from >> 3;
            int bits = from | ((piece & 7) << ChessRules.MovePieceTypeShift) | PrevStateBits(state);
            for (int d = firstDirection; d < lastDirection; d++) {
                int fileStep = ((LineFileSteps >> (d * 2)) & 3) - 1;
                int rankStep = ((LineRankSteps >> (d * 2)) & 3) - 1;
                int toFile = file + fileStep;
                int toRank = rank + rankStep;
                while (toFile >= 0 && toFile <= 7 && toRank >= 0 && toRank <= 7) {
                    int to = (toRank << 3) | toFile;
                    int target = board[to];
                    if (target == ChessRules.Empty) {
                        moves[count++] = bits | (to << ChessRules.MoveToShift);
                    } else {
                        if (((target >> 3) & 1) != color) {
                            moves[count++] = bits | (to << ChessRules.MoveToShift)
                                | ((target & 7) << ChessRules.MoveCapturedShift);
                        }
                        break;
                    }
                    toFile += fileStep;
                    toRank += rankStep;
                }
            }
            return count;
        }

        // Java MoveFinderKing: one step in each direction, then castling.
        public static int GenerateKingMoves(byte[] board, int[] state, int from, int[] moves, int count) {
            int color = (board[from] >> 3) & 1;
            int file = from & 7;
            int rank = from >> 3;
            int prevBits = PrevStateBits(state);
            int bits = from | (ChessRules.King << ChessRules.MovePieceTypeShift) | prevBits;
            for (int d = 0; d < 8; d++) {
                int toFile = file + ((LineFileSteps >> (d * 2)) & 3) - 1;
                int toRank = rank + ((LineRankSteps >> (d * 2)) & 3) - 1;
                if (toFile < 0 || toFile > 7 || toRank < 0 || toRank > 7) continue;
                int to = (toRank << 3) | toFile;
                int target = board[to];
                if (target == ChessRules.Empty) {
                    moves[count++] = bits | (to << ChessRules.MoveToShift);
                } else if (((target >> 3) & 1) != color) {
                    moves[count++] = bits | (to << ChessRules.MoveToShift) | ((target & 7) << ChessRules.MoveCapturedShift);
                }
            }

            // Castling: the white rights are the low two bits, the black ones the next two.
            int rights = (state[ChessPosition.StateCastlingRights] >> (color * 2))
                & (ChessRules.CastleWhiteKingSide | ChessRules.CastleWhiteQueenSide);
            if (rights == 0) return count;
            int backRank = color == ChessRules.White ? 0 : 56;
            if (from != (backRank | state[ChessPosition.StateKingFile])) return count;
            int castleBits = bits | (1 << ChessRules.MoveCastleShift);
            if ((rights & ChessRules.CastleWhiteKingSide) != 0
                && CanCastle(board, color, from, backRank | state[ChessPosition.StateRightRookFile],
                    ChessPosition.KingSideKingFile, ChessPosition.KingSideRookFile)) {
                moves[count++] = castleBits | ((backRank | state[ChessPosition.StateRightRookFile]) << ChessRules.MoveToShift);
            }
            if ((rights & ChessRules.CastleWhiteQueenSide) != 0
                && CanCastle(board, color, from, backRank | state[ChessPosition.StateLeftRookFile],
                    ChessPosition.QueenSideKingFile, ChessPosition.QueenSideRookFile)) {
                moves[count++] = castleBits | ((backRank | state[ChessPosition.StateLeftRookFile]) << ChessRules.MoveToShift);
            }
            return count;
        }

        // Whether a piece of byColor attacks square (docs/legality-checking.md): looks outward from the square for
        // pawns, knights and the king of that colour, and along each line for the first piece, a rook or queen on
        // the orthogonals and a bishop or queen on the diagonals. What stands on the square itself doesn't matter.
        // En passant isn't an attack on a square, so it isn't counted; it never attacks a king.
        public static bool IsSquareAttacked(byte[] board, int square, int byColor) {
            int file = square & 7;
            int rank = square >> 3;
            int colorBits = byColor << 3;

            // A pawn attacks diagonally forward, so it stands one rank behind the square from its own side.
            int pawnRank = byColor == ChessRules.White ? rank - 1 : rank + 1;
            if (pawnRank >= 0 && pawnRank <= 7) {
                int pawn = ChessRules.Pawn | colorBits;
                if (file > 0 && board[(pawnRank << 3) | (file - 1)] == pawn) return true;
                if (file < 7 && board[(pawnRank << 3) | (file + 1)] == pawn) return true;
            }

            int knight = ChessRules.Knight | colorBits;
            int king = ChessRules.King | colorBits;
            for (int d = 0; d < 8; d++) {
                int toFile = file + ((KnightFileSteps >> (d * 3)) & 7) - 2;
                int toRank = rank + ((KnightRankSteps >> (d * 3)) & 7) - 2;
                if (toFile >= 0 && toFile <= 7 && toRank >= 0 && toRank <= 7
                    && board[(toRank << 3) | toFile] == knight) return true;
                toFile = file + ((LineFileSteps >> (d * 2)) & 3) - 1;
                toRank = rank + ((LineRankSteps >> (d * 2)) & 3) - 1;
                if (toFile >= 0 && toFile <= 7 && toRank >= 0 && toRank <= 7
                    && board[(toRank << 3) | toFile] == king) return true;
            }

            // Directions 0-3 are orthogonal, 4-7 diagonal (see the line tables).
            int queen = ChessRules.Queen | colorBits;
            int rook = ChessRules.Rook | colorBits;
            int bishop = ChessRules.Bishop | colorBits;
            for (int d = 0; d < 8; d++) {
                int slider = d < 4 ? rook : bishop;
                int fileStep = ((LineFileSteps >> (d * 2)) & 3) - 1;
                int rankStep = ((LineRankSteps >> (d * 2)) & 3) - 1;
                int toFile = file + fileStep;
                int toRank = rank + rankStep;
                while (toFile >= 0 && toFile <= 7 && toRank >= 0 && toRank <= 7) {
                    int target = board[(toRank << 3) | toFile];
                    if (target != ChessRules.Empty) {
                        if (target == slider || target == queen) return true;
                        break;
                    }
                    toFile += fileStep;
                    toRank += rankStep;
                }
            }
            return false;
        }

        // Java Board.getAllLegalMoves: all legal moves for the side to move, written to moves from index 0. Returns
        // the count. With prohibitSelfCheck (Java Board.prohibitSelfCheck, normally true) a move is kept only if it
        // doesn't leave the mover's king in check, and castling only if the king isn't in check and doesn't pass
        // through or land on an attacked square. With it false this is GenerateMoves (for Fog of War, where the king
        // can be captured). moves must hold at least MaxMoves. The position is left as it was.
        public static int GenerateLegalMoves(byte[] board, int[] state, int[] moves, bool prohibitSelfCheck) {
            int count = GenerateMoves(board, state, moves);
            if (!prohibitSelfCheck) return count;
            return FilterLegalMoves(board, state, moves, 0, count);
        }

        // The legal moves of the piece on from (see GenerateLegalMoves), appended to moves at index count. Returns
        // the new count.
        public static int GenerateLegalPieceMoves(byte[] board, int[] state, int from, int[] moves, int count,
            bool prohibitSelfCheck) {
            int end = GeneratePieceMoves(board, state, from, moves, count);
            if (!prohibitSelfCheck) return end;
            return FilterLegalMoves(board, state, moves, count, end);
        }

        // Drops the moves in moves[start..count) that leave the side to move's king in check, or castle out of,
        // through or into check, keeping the others in order (docs/legality-checking.md: play the move, check the
        // king's square, undo). Returns the new count. The position is left as it was.
        public static int FilterLegalMoves(byte[] board, int[] state, int[] moves, int start, int count) {
            int mover = state[ChessPosition.StateSideToMove];
            int opponent = mover ^ 1;
            int kept = start;
            for (int i = start; i < count; i++) {
                int move = moves[i];
                if (((move >> ChessRules.MoveCastleShift) & 1) != 0 && !IsCastlePathSafe(board, move, opponent)) continue;
                ChessPosition.MakeMove(board, state, move);
                // With no king (only possible if it was captured with prohibitSelfCheck off) there is no check.
                int king = state[ChessPosition.StateWhiteKingSquare + mover];
                bool legal = king == ChessRules.NoSquare || !IsSquareAttacked(board, king, opponent);
                ChessPosition.UnmakeMove(board, state, move);
                if (legal) moves[kept++] = move;
            }
            return kept;
        }

        // Whether color's king is attacked. False if that king isn't on the board.
        public static bool IsInCheck(byte[] board, int[] state, int color) {
            int king = state[ChessPosition.StateWhiteKingSquare + color];
            return king != ChessRules.NoSquare && IsSquareAttacked(board, king, color ^ 1);
        }

        // Java MoveFinderKing.findCastleMove's check conditions: no square from the king's square to its destination,
        // both included, is attacked by opponent. As in the Java, each square is checked with the king lifted off its
        // starting square and the rook still on its own. The position after castling is checked like any other move.
        static bool IsCastlePathSafe(byte[] board, int move, int opponent) {
            int from = (move >> ChessRules.MoveFromShift) & ChessRules.MoveSquareMask;
            int to = (move >> ChessRules.MoveToShift) & ChessRules.MoveSquareMask;
            int kingTo = (from & 56) | ((to & 7) > (from & 7) ? ChessPosition.KingSideKingFile : ChessPosition.QueenSideKingFile);
            int step = kingTo >= from ? 1 : -1;
            byte king = board[from];
            board[from] = ChessRules.Empty;
            bool safe = true;
            for (int sq = from; sq != kingTo + step; sq += step) {
                if (IsSquareAttacked(board, sq, opponent)) {
                    safe = false;
                    break;
                }
            }
            board[from] = king;
            return safe;
        }

        // Whether the king on kingSquare may castle with the rook on rookSquare as far as the pieces go: the rook must
        // be there (the Java didn't check), and every square the king or rook passes through or lands on must be
        // empty apart from the king and this rook themselves (the Java let any king or rook stand in the way). Doesn't
        // look at check. The two paths always overlap or touch, so together they cover every square from the
        // leftmost to the rightmost of the four files.
        static bool CanCastle(byte[] board, int color, int kingSquare, int rookSquare, int kingToFile, int rookToFile) {
            if (board[rookSquare] != (byte)(ChessRules.Rook | (color << 3))) return false;
            int kingFile = kingSquare & 7;
            int rookFile = rookSquare & 7;
            int low = kingFile < rookFile ? kingFile : rookFile;
            if (kingToFile < low) low = kingToFile;
            if (rookToFile < low) low = rookToFile;
            int high = kingFile > rookFile ? kingFile : rookFile;
            if (kingToFile > high) high = kingToFile;
            if (rookToFile > high) high = rookToFile;
            int backRank = kingSquare & 56;
            for (int f = low; f <= high; f++) {
                int sq = backRank | f;
                if (sq != kingSquare && sq != rookSquare && board[sq] != ChessRules.Empty) return false;
            }
            return true;
        }
    }
}
