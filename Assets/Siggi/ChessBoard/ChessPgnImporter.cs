using System;
using System.Text.RegularExpressions;
using UdonSharp;
using UnityEngine;

namespace SiggiChessBoard {
    // PGN import as a time-budgeted state machine (docs/pgn.md, Import): each frame it runs ChessPgn.ImportNext until
    // its budget runs out, then goes on in the next frame, until the PGN is read or fails. Everything it works on is
    // local: the working board and the history it builds are only read by whoever started the import, once it's done.
    // Holds no game and touches no world objects; the board (step 7) starts it and takes the result.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ChessPgnImporter : UdonSharpBehaviour {
        // The starting budget per frame in milliseconds (docs/pgn.md). At least one step runs each frame, whatever
        // the budget.
        public const float DefaultBudgetMs = 10f;

        // Whether an import is under way (started and neither finished nor abandoned).
        [NonSerialized] public bool importing;
        // The last ChessPgn.ImportNext result: ImportMore while importing, then ImportDone or a failure.
        [NonSerialized] public int result = ChessPgn.ImportDone;

        // The import's state and result (see ChessPgn.ImportNext). The history arrays are as long as the capacity the
        // import was started with; the moves played are cursor[ChessPgn.CursorMoves].
        [NonSerialized] public int[] cursor;
        [NonSerialized] public string[] tags;
        [NonSerialized] public byte[] board;
        [NonSerialized] public int[] state;
        [NonSerialized] public int[] moves;
        [NonSerialized] public string[] sans;
        [NonSerialized] public int[] clocks;

        // Frames the import has run in (calls to ImportFor), the longest of them and all of them together, in
        // milliseconds of import work.
        [NonSerialized] public int frames;
        [NonSerialized] public double longestFrameMs;
        [NonSerialized] public double totalMs;

        string pgn;
        float budgetMs;
        UdonSharpBehaviour doneTarget;
        string doneEvent;
        Regex tokenRegex;
        Regex clockRegex;
        int[] legalMoves;
        int[] scratch;
        System.Diagnostics.Stopwatch watch;

        // Starts importing text into a history with room for capacity moves, running for about budgetMs each frame
        // from the next Update on. When it finishes (done or failed, not when abandoned), doneEvent is sent to
        // doneTarget (if not null). Starting again abandons any import under way.
        public void StartImport(string text, int capacity, float budget, UdonSharpBehaviour target, string eventName) {
            if (tokenRegex == null) {
                tokenRegex = new Regex(ChessPgn.TokenPattern);
                clockRegex = new Regex(ChessPgn.ClockPattern);
                legalMoves = new int[ChessMoves.MaxMoves];
                scratch = new int[ChessMoves.MaxMoves];
                cursor = new int[ChessPgn.CursorLength];
                tags = new string[ChessPgn.TagCount];
                board = new byte[64];
                state = new int[ChessPosition.StateLength];
                watch = new System.Diagnostics.Stopwatch();
            }
            pgn = text == null ? "" : text;
            budgetMs = budget;
            doneTarget = target;
            doneEvent = eventName;
            if (moves == null || moves.Length != capacity) {
                moves = new int[capacity];
                sans = new string[capacity];
                clocks = new int[capacity];
            }
            for (int i = 0; i < capacity; i++) {
                moves[i] = 0;
                sans[i] = "";
                clocks[i] = -1;
            }
            ChessPgn.StartReading(cursor, tags);
            frames = 0;
            longestFrameMs = 0;
            totalMs = 0;
            result = ChessPgn.ImportMore;
            importing = true;
        }

        // Stops the import under way without finishing it (e.g. the board changed, docs/pgn.md). No event is sent.
        public void Abandon() {
            importing = false;
        }

        // Runs the import for about budget milliseconds (at least one step), counting it as one frame. Returns the
        // result so far: ImportMore while there is more to do. The import is finished when it returns anything else,
        // but only Update sends the done event, so a caller importing in one go (a huge budget) gets no event.
        public int ImportFor(float budget) {
            if (result != ChessPgn.ImportMore) return result;
            watch.Reset();
            watch.Start();
            result = Step();
            while (result == ChessPgn.ImportMore && watch.Elapsed.TotalMilliseconds < budget) result = Step();
            watch.Stop();
            double ms = watch.Elapsed.TotalMilliseconds;
            frames++;
            totalMs += ms;
            if (ms > longestFrameMs) longestFrameMs = ms;
            if (result != ChessPgn.ImportMore) importing = false;
            return result;
        }

        // One ChessPgn.ImportNext. Imports are Standard or Chess960 (docs/modes.md), so self-check is prohibited.
        int Step() {
            return ChessPgn.ImportNext(tokenRegex, clockRegex, pgn, cursor, tags, board, state, moves, sans, clocks,
                legalMoves, scratch, true);
        }

        void Update() {
            if (!importing) return;
            if (ImportFor(budgetMs) == ChessPgn.ImportMore) return;
            if (doneTarget != null) doneTarget.SendCustomEvent(doneEvent);
        }

        // The number of moves played (the failing ply after a failure).
        public int MoveCount() {
            return cursor == null ? 0 : cursor[ChessPgn.CursorMoves];
        }

        // The move a failed import failed at, e.g. "2..." (ChessPgn.ImportFailedAt).
        public string FailedAt() {
            return ChessPgn.ImportFailedAt(cursor);
        }

        // The starting position: the FEN tag, or the standard start without one.
        public string StartFen() {
            return tags[ChessPgn.TagFen] == "" ? ChessFen.StartFen : tags[ChessPgn.TagFen];
        }
    }
}
