using UdonSharp;
using UnityEngine;

// Throwaway check for PLAN.md item 1.3: test behaviours can read the PGN test set through TextAsset fields.
// Not part of the port. Set pgn to Assets/Tests/PGN/Siggi-vs-Nuka.pgn.txt; delete once 1.3 is ticked.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class TextAssetCheck : UdonSharpBehaviour {
    public TextAsset pgn;

    const string expectedFirstLine = "[Site \"VRChess\"]";

    void Start() {
        if (pgn == null) {
            Debug.Log("[TextAssetCheck] FAIL: no TextAsset assigned");
            return;
        }
        string text = pgn.text;
        int end = text.IndexOf('\n');
        string firstLine = end < 0 ? text : text.Substring(0, end);
        firstLine = firstLine.TrimEnd('\r');
        Debug.Log("[TextAssetCheck] " + pgn.name + " first line: " + firstLine);
        Debug.Log("[TextAssetCheck] " + (firstLine == expectedFirstLine ? "PASS" : "FAIL") + ": first line matches");
    }
}
