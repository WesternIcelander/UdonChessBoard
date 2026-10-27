using UdonSharp;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // One entry of the history wall (step 10.2b, docs/archive.md): its UI Button, covering both of the entry's lines,
    // calls Press, which picks the entry's game on the wall; on the personal wall its delete button (a child of the
    // entry, step 13.10) calls _PressDelete. A UI event carries no index, so each entry has its own. Built by Tools >
    // Chess Board > Create History Wall (and Create Personal History Wall); the wall makes more by instantiating its
    // last entry when its archive holds more games. No synced variables.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GameHistoryRow : UdonSharpBehaviour {
        // The wall this entry belongs to, and the entry's index (0 at the top, the newest game).
        public GameHistoryWall wall;
        public int index;

        public void _Press() {
            if (Utilities.IsValid(wall)) wall._SelectRow(index);
        }

        public void _PressDelete() {
            if (Utilities.IsValid(wall)) wall._PressDelete(index);
        }
    }
}
