using UdonSharp;
using UnityEngine;

// Throwaway benchmark for PLAN.md item 1.5: rough Udon costs of move generation before the design relies on them.
// Not part of the port. The generator is a stand-in in the planned style (byte[64] board, packed int moves,
// static methods taking arrays, legality by make -> attack check -> unmake). It has no en passant, and castling is
// standard-only, which is enough for the two positions measured. Delete once the numbers are recorded in PLAN.md.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UdonSpeedCheck : UdonSharpBehaviour {
    const int PAWN = 1, KNIGHT = 2, BISHOP = 3, ROOK = 4, QUEEN = 5, KING = 6;
    const int BLACK = 8;

    [Tooltip("Measured time per phase, in seconds.")]
    public float secondsPerPhase = 3f;
    [Tooltip("Work done per frame, in milliseconds. Keeps each Update well under Udon's time limit.")]
    public float msPerFrame = 20f;

    byte[] startBoard;
    byte[] kiwiBoard;
    int[] pseudo;
    int[] legal;
    int[] kdf, kdr, sdf, sdr;

    int phase;
    int phaseIterations;
    double phaseMs;
    int phaseMoveCount;
    System.Diagnostics.Stopwatch sw;

    void Start() {
        startBoard = new byte[64];
        kiwiBoard = new byte[64];
        pseudo = new int[256];
        legal = new int[256];
        kdf = new int[] { 1, 2, 2, 1, -1, -2, -2, -1 };
        kdr = new int[] { 2, 1, -1, -2, -2, -1, 1, 2 };
        // 0-3 orthogonal, 4-7 diagonal
        sdf = new int[] { 1, -1, 0, 0, 1, 1, -1, -1 };
        sdr = new int[] { 0, 0, 1, -1, 1, -1, 1, -1 };

        LoadBoard(startBoard, "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR");
        LoadBoard(kiwiBoard, "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R");

        // Sanity: expected legal counts are 20 (start) and 48 (Kiwipete).
        int startPseudo = GenerateMoves(startBoard, 0, 15, pseudo, kdf, kdr, sdf, sdr);
        int startLegal = GenerateLegal(startBoard, 0, 15, 4, pseudo, legal, kdf, kdr, sdf, sdr);
        int kiwiPseudo = GenerateMoves(kiwiBoard, 0, 15, pseudo, kdf, kdr, sdf, sdr);
        int kiwiLegal = GenerateLegal(kiwiBoard, 0, 15, 4, pseudo, legal, kdf, kdr, sdf, sdr);
        Debug.Log("[UdonSpeedCheck] sanity: start pseudo " + startPseudo + " legal " + startLegal + " (expect 20)"
            + ", Kiwipete pseudo " + kiwiPseudo + " legal " + kiwiLegal + " (expect 48)"
            + ((startLegal == 20 && kiwiLegal == 48) ? " PASS" : " FAIL"));
        Debug.Log("[UdonSpeedCheck] running " + secondsPerPhase + " s per phase, " + msPerFrame + " ms per frame...");

        sw = new System.Diagnostics.Stopwatch();
        phase = 0;
    }

    void Update() {
        if (phase >= 4) return;
        byte[] b = phase == 0 || phase == 2 ? startBoard : kiwiBoard;
        bool doLegal = phase >= 2;

        sw.Reset();
        sw.Start();
        int iterations = 0;
        int count = 0;
        double elapsed;
        do {
            // Batches of 4 keep the stopwatch reads out of the measurement.
            for (int i = 0; i < 4; i++) {
                count = doLegal
                    ? GenerateLegal(b, 0, 15, 4, pseudo, legal, kdf, kdr, sdf, sdr)
                    : GenerateMoves(b, 0, 15, pseudo, kdf, kdr, sdf, sdr);
            }
            iterations += 4;
            elapsed = sw.Elapsed.TotalMilliseconds;
        } while (elapsed < msPerFrame);
        sw.Stop();

        phaseIterations += iterations;
        phaseMs += elapsed;
        phaseMoveCount = count;

        if (phaseMs >= secondsPerPhase * 1000.0) {
            string name = (phase == 0 || phase == 2 ? "start" : "Kiwipete") + (doLegal ? " legal" : " pseudo-legal");
            double seconds = phaseMs / 1000.0;
            double positionsPerSecond = phaseIterations / seconds;
            Debug.Log("[UdonSpeedCheck] " + name + ": " + phaseMoveCount + " moves/position, "
                + positionsPerSecond.ToString("F0") + " positions/s, "
                + (positionsPerSecond * phaseMoveCount).ToString("F0") + " moves/s, "
                + (phaseMs / phaseIterations).ToString("F3") + " ms/position"
                + " (" + phaseIterations + " positions in " + seconds.ToString("F2") + " s)");
            phase++;
            phaseIterations = 0;
            phaseMs = 0;
            if (phase >= 4) Debug.Log("[UdonSpeedCheck] done");
        }
    }

    // Board: a1 = 0 ... h8 = 63. Piece byte: type (1-6), plus 8 for black, 0 = empty.
    void LoadBoard(byte[] b, string fen) {
        int rank = 7;
        int file = 0;
        for (int i = 0; i < fen.Length; i++) {
            char c = fen[i];
            if (c == '/') {
                rank--;
                file = 0;
            } else if (c >= '1' && c <= '8') {
                file += c - '0';
            } else {
                int color = char.IsUpper(c) ? 0 : BLACK;
                char lower = char.ToLower(c);
                int type = lower == 'p' ? PAWN : lower == 'n' ? KNIGHT : lower == 'b' ? BISHOP
                    : lower == 'r' ? ROOK : lower == 'q' ? QUEEN : KING;
                b[rank * 8 + file] = (byte)(color | type);
                file++;
            }
        }
    }

    // Packed move: from | to << 6 | type << 12 | promotion << 15 | captured << 18 | castle << 21.
    // side: 0 = white, 8 = black. castling: 1 = white O-O, 2 = white O-O-O, 4 = black O-O, 8 = black O-O-O.
    public static int GenerateMoves(byte[] b, int side, int castling, int[] outMoves,
        int[] kdf, int[] kdr, int[] sdf, int[] sdr) {
        int n = 0;
        int dir = side == 0 ? 8 : -8;
        int startRank = side == 0 ? 1 : 6;
        int promoRank = side == 0 ? 7 : 0;
        for (int sq = 0; sq < 64; sq++) {
            int piece = b[sq];
            if (piece == 0 || (piece & BLACK) != side) continue;
            int type = piece & 7;
            int f = sq & 7;
            int r = sq >> 3;
            int typeBits = type << 12;
            if (type == PAWN) {
                int to = sq + dir;
                if (b[to] == 0) {
                    if ((to >> 3) == promoRank) {
                        for (int p = KNIGHT; p <= QUEEN; p++) outMoves[n++] = sq | (to << 6) | typeBits | (p << 15);
                    } else {
                        outMoves[n++] = sq | (to << 6) | typeBits;
                        if (r == startRank && b[to + dir] == 0) outMoves[n++] = sq | ((to + dir) << 6) | typeBits;
                    }
                }
                for (int df = -1; df <= 1; df += 2) {
                    int cf = f + df;
                    if (cf < 0 || cf > 7) continue;
                    int cto = sq + dir + df;
                    int target = b[cto];
                    if (target == 0 || (target & BLACK) == side) continue;
                    int capBits = (target & 7) << 18;
                    if ((cto >> 3) == promoRank) {
                        for (int p = KNIGHT; p <= QUEEN; p++) outMoves[n++] = sq | (cto << 6) | typeBits | (p << 15) | capBits;
                    } else {
                        outMoves[n++] = sq | (cto << 6) | typeBits | capBits;
                    }
                }
            } else if (type == KNIGHT || type == KING) {
                int[] df8 = type == KNIGHT ? kdf : sdf;
                int[] dr8 = type == KNIGHT ? kdr : sdr;
                for (int d = 0; d < 8; d++) {
                    int nf = f + df8[d];
                    int nr = r + dr8[d];
                    if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
                    int to = nr * 8 + nf;
                    int target = b[to];
                    if (target == 0) outMoves[n++] = sq | (to << 6) | typeBits;
                    else if ((target & BLACK) != side) outMoves[n++] = sq | (to << 6) | typeBits | ((target & 7) << 18);
                }
                if (type == KING) {
                    int rank = side == 0 ? 0 : 56;
                    int kBit = side == 0 ? 1 : 4;
                    int qBit = side == 0 ? 2 : 8;
                    int rook = side | ROOK;
                    if (sq == rank + 4) {
                        if ((castling & kBit) != 0 && b[rank + 7] == rook && b[rank + 5] == 0 && b[rank + 6] == 0)
                            outMoves[n++] = sq | ((rank + 7) << 6) | typeBits | (1 << 21);
                        if ((castling & qBit) != 0 && b[rank] == rook && b[rank + 1] == 0 && b[rank + 2] == 0 && b[rank + 3] == 0)
                            outMoves[n++] = sq | (rank << 6) | typeBits | (1 << 21);
                    }
                }
            } else {
                int dStart = type == BISHOP ? 4 : 0;
                int dEnd = type == ROOK ? 4 : 8;
                for (int d = dStart; d < dEnd; d++) {
                    int df = sdf[d];
                    int dr = sdr[d];
                    int nf = f + df;
                    int nr = r + dr;
                    while (nf >= 0 && nf <= 7 && nr >= 0 && nr <= 7) {
                        int to = nr * 8 + nf;
                        int target = b[to];
                        if (target == 0) {
                            outMoves[n++] = sq | (to << 6) | typeBits;
                        } else {
                            if ((target & BLACK) != side) outMoves[n++] = sq | (to << 6) | typeBits | ((target & 7) << 18);
                            break;
                        }
                        nf += df;
                        nr += dr;
                    }
                }
            }
        }
        return n;
    }

    public static int GenerateLegal(byte[] b, int side, int castling, int kingSq, int[] pseudo, int[] outMoves,
        int[] kdf, int[] kdr, int[] sdf, int[] sdr) {
        int n = GenerateMoves(b, side, castling, pseudo, kdf, kdr, sdf, sdr);
        int opp = side ^ BLACK;
        int count = 0;
        for (int i = 0; i < n; i++) {
            int move = pseudo[i];
            int from = move & 63;
            int to = (move >> 6) & 63;
            if (((move >> 21) & 1) != 0) {
                // Castle: the king's square, the square it passes and its destination must not be attacked.
                int rank = from & 56;
                bool kingSide = (to & 7) > (from & 7);
                if (!IsAttacked(b, from, opp, kdf, kdr, sdf, sdr)
                    && !IsAttacked(b, rank + (kingSide ? 5 : 3), opp, kdf, kdr, sdf, sdr)
                    && !IsAttacked(b, rank + (kingSide ? 6 : 2), opp, kdf, kdr, sdf, sdr))
                    outMoves[count++] = move;
                continue;
            }
            int type = (move >> 12) & 7;
            int promo = (move >> 15) & 7;
            byte moving = b[from];
            byte captured = b[to];
            b[to] = promo != 0 ? (byte)(side | promo) : moving;
            b[from] = 0;
            bool inCheck = IsAttacked(b, type == KING ? to : kingSq, opp, kdf, kdr, sdf, sdr);
            b[from] = moving;
            b[to] = captured;
            if (!inCheck) outMoves[count++] = move;
        }
        return count;
    }

    public static bool IsAttacked(byte[] b, int sq, int bySide, int[] kdf, int[] kdr, int[] sdf, int[] sdr) {
        int f = sq & 7;
        int r = sq >> 3;
        // Pawns attack from one rank behind them, from the attacked square's point of view.
        int pr = bySide == 0 ? r - 1 : r + 1;
        if (pr >= 0 && pr <= 7) {
            int pawn = bySide | PAWN;
            if (f > 0 && b[pr * 8 + f - 1] == pawn) return true;
            if (f < 7 && b[pr * 8 + f + 1] == pawn) return true;
        }
        int knight = bySide | KNIGHT;
        int king = bySide | KING;
        for (int d = 0; d < 8; d++) {
            int nf = f + kdf[d];
            int nr = r + kdr[d];
            if (nf >= 0 && nf <= 7 && nr >= 0 && nr <= 7 && b[nr * 8 + nf] == knight) return true;
            nf = f + sdf[d];
            nr = r + sdr[d];
            if (nf >= 0 && nf <= 7 && nr >= 0 && nr <= 7 && b[nr * 8 + nf] == king) return true;
        }
        int queen = bySide | QUEEN;
        for (int d = 0; d < 8; d++) {
            int slider = d < 4 ? (bySide | ROOK) : (bySide | BISHOP);
            int df = sdf[d];
            int dr = sdr[d];
            int nf = f + df;
            int nr = r + dr;
            while (nf >= 0 && nf <= 7 && nr >= 0 && nr <= 7) {
                int target = b[nr * 8 + nf];
                if (target != 0) {
                    if (target == slider || target == queen) return true;
                    break;
                }
                nf += df;
                nr += dr;
            }
        }
        return false;
    }
}
