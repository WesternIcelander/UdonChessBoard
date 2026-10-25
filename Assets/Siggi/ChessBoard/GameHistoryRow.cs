using UdonSharp;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // One row of the history wall (step 10.2b, docs/archive.md): its UI Button calls Press, which picks the row's game
    // on the wall. A UI event carries no index, so each row has its own. Built by Tools > Chess Board > Create History
    // Wall. No synced variables.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GameHistoryRow : UdonSharpBehaviour {
        // The wall this row belongs to, and the row's index (the game's, oldest first).
        public GameHistoryWall wall;
        public int index;

        public void _Press() {
            if (Utilities.IsValid(wall)) wall._SelectRow(index);
        }
    }
}
