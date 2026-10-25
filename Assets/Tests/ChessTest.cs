using SiggiChessBoard;
using SiggiUSTools;
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
    I18n textLanguage;

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

    // The text checks name the key whose text they expect and read it through the chess system's I18n (i18n, the
    // Chess Language its behaviours use) with Text and TextWith (PLAN.md 13.5a), so a test hands it over here first.
    // It's set to English, whatever the player's VRChat language, so the logs read the same on every machine.
    protected void UseEnglish(I18n i18n) {
        textLanguage = i18n;
        if (Utilities.IsValid(i18n)) i18n.SetLanguage("en");
    }

    // key's text in the Chess Language, as the code reads it (ChessUtil.Text); the key itself before UseEnglish. A
    // key missing from the pack logs an I18n error when read, which fails the run.
    protected string Text(string key) {
        return ChessUtil.Text(textLanguage, key);
    }

    // key's text with its ${name} parts filled from args (name, value pairs), as the code fills them (I18n.Format).
    protected string TextWith(string key, string[] args) {
        return I18n.Format(Text(key), args);
    }

    // board's first count notation entries, separated by spaces.
    protected static string NotationText(ChessBoard board, int count) {
        string text = "";
        for (int i = 0; i < count; i++) text += (i > 0 ? " " : "") + board.notation[i];
        return text;
    }
}
