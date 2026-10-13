using UdonSharp;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // One button of the settings menu (step 11.1a, docs/settings.md): its UI Button calls Press, which passes the
    // button's item and action to the menu. A UI event carries no arguments, so each button has its own. Built by
    // Tools > Chess Board > Create Settings Menu. No synced variables.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ChessSettingsButton : UdonSharpBehaviour {
        public ChessSettingsMenu menu;
        // A ChessSettingsMenu.Item* (ItemScope for the scope row) and Action*.
        public int item;
        public int action;

        public void Press() {
            if (Utilities.IsValid(menu)) menu.Press(item, action);
        }
    }
}
