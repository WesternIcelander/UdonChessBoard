using SiggiChessBoard;
using UnityEngine;
using VRC.SDKBase;

// What every test behaviour shares (step 12.19): its pass and fail counts, the checks logging
// "[<test>] PASS: <check>" or "[<test>] FAIL: <check>: <details>", and the summary line. A ChessBoardListener, so a
// test can listen to a board (BoardTest does); the others leave the listener methods as they are. Abstract, so it has
// no program asset of its own.
public abstract class ChessTest : ChessBoardListener {
    // Set once the checks are done or skipped (LogSummary sets it), for the tests that wait for this one.
    [System.NonSerialized] public bool finished;

    protected int passed;
    protected int failed;
    int waitedFrames;

    // The test's name in its log lines, e.g. "BoardTest".
    protected virtual string TestName() {
        return "ChessTest";
    }

    // Logs the summary line and sets finished.
    protected void LogSummary() {
        finished = true;
        Debug.Log("[" + TestName() + "] done: " + passed + " passed, " + failed + " failed"
            + (failed == 0 ? " ALL PASS" : " SOME FAIL"));
    }

    protected void Check(bool ok, string name, string details) {
        if (ok) {
            passed++;
            Debug.Log("[" + TestName() + "] PASS: " + name);
        } else {
            failed++;
            Debug.Log("[" + TestName() + "] FAIL: " + name + ": " + details);
        }
    }

    protected void CheckInt(int actual, int expected, string name) {
        Check(actual == expected, name, "got " + actual + ", expected " + expected);
    }

    protected void CheckString(string actual, string expected, string name) {
        Check(actual == expected, name, "got \"" + actual + "\", expected \"" + expected + "\"");
    }

    // Whether the local player has joined (joined) and each test given (none for null) has finished, for a test that
    // runs after them. While not, counts the frames waited, logging every 3000 that it's still waiting for waitingFor.
    protected bool ReadyAfter(bool joined, ChessTest first, ChessTest second, ChessTest third, string waitingFor) {
        if (joined && (!Utilities.IsValid(first) || first.finished) && (!Utilities.IsValid(second) || second.finished)
            && (!Utilities.IsValid(third) || third.finished)) {
            return true;
        }
        waitedFrames++;
        if (waitedFrames % 3000 == 0) Debug.Log("[" + TestName() + "] still waiting for " + waitingFor);
        return false;
    }

    // board's first count notation entries, separated by spaces.
    protected static string NotationText(ChessBoard board, int count) {
        string text = "";
        for (int i = 0; i < count; i++) text += (i > 0 ? " " : "") + board.notation[i];
        return text;
    }
}
