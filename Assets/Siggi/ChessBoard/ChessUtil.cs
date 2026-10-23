using SiggiUSTools;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // Small helpers shared by several behaviours (steps 12.18 and 12.19), so a change to one is made once. Holds no
    // state; everything comes in through parameters (see docs/architecture.md).
    // Abstract because it is never placed as a component, only used for its static methods.
    public abstract class ChessUtil : UdonSharpBehaviour {
        // Sets label's text (nothing for none) only when it has changed.
        public static void SetText(TextMeshProUGUI label, string text) {
            if (Utilities.IsValid(label) && label.text != text) label.text = text;
        }

        // value with a leading zero below 10 ("05").
        public static string TwoDigits(int value) {
            return value < 10 ? "0" + value : value.ToString();
        }

        // text with line added on a line of its own; text unchanged for an empty line.
        public static string AddLine(string text, string line) {
            if (line.Length == 0) return text;
            return text.Length == 0 ? line : text + "\n" + line;
        }

        // The local player's display name, fallback when there's none.
        public static string LocalName(string fallback) {
            VRCPlayerApi player = Networking.LocalPlayer;
            return Utilities.IsValid(player) ? player.displayName : fallback;
        }

        // The display name of target's owner, fallback when there's none.
        public static string OwnerName(GameObject target, string fallback) {
            VRCPlayerApi owner = Networking.GetOwner(target);
            return Utilities.IsValid(owner) ? owner.displayName : fallback;
        }

        // ---- Player text (PLAN.md 13.5) ----

        // The string for key in i18n's language now; the key itself with no I18n (the field empty), as I18n shows a
        // missing key. Each behaviour reads its strings with this once, into fields, and again on a language change.
        public static string Text(I18n i18n, string key) {
            return Utilities.IsValid(i18n) ? i18n._(key) : key;
        }

        // Logs that the I18n field of the behaviour named by who ("[ChessBoardUI] Board UI") is empty (the I18n sanity
        // check fills it on build and before Play Mode). Whether i18n is there.
        public static bool CheckI18n(I18n i18n, string who) {
            if (Utilities.IsValid(i18n)) return true;
            Debug.LogError(who + ": the I18n field is empty (add Chess Language beside the chess objects).");
            return false;
        }

        // ---- Sound effects (step 12.12), for ChessBoardView and ChessBoardUI ----

        // Whether the change board has just told is heard: not before soundsFrom (the first StartupSilence seconds,
        // the board's own start), nor a late joiner's first update (catching up), which is the first received change
        // when no change has been seen here since the silence. caughtUp is the behaviour's flag for that, handed back
        // as nowCaughtUp: set by the first change seen after the silence.
        public static bool SoundAllowed(ChessBoard board, float soundsFrom, bool caughtUp, out bool nowCaughtUp) {
            nowCaughtUp = caughtUp;
            if (Time.time < soundsFrom) return false;
            if (caughtUp) return true;
            nowCaughtUp = true;
            return !board.lastChangeReceived;
        }

        // Notes the sound of change, just told by board, for the behaviour's next Refresh (step 12.12): soundChange
        // becomes change when it makes a sound (makesSound, from the behaviour's own list), is heard (SoundAllowed,
        // with soundsFrom and caughtUp) and is alone since the last Refresh (soundChanges, which counts it, set back
        // by Refresh), ChangeNone otherwise. A touch from another client (ChangeActivity) doesn't count.
        public static void NoteSound(int change, bool makesSound, ChessBoard board, float soundsFrom, ref bool caughtUp,
            ref int soundChanges, ref int soundChange) {
            if (change == ChessBoard.ChangeActivity) return;
            soundChanges++;
            bool nowCaughtUp;
            bool heard = SoundAllowed(board, soundsFrom, caughtUp, out nowCaughtUp);
            caughtUp = nowCaughtUp;
            soundChange = soundChanges == 1 && heard && makesSound ? change : ChessBoard.ChangeNone;
        }

        // Plays clip from audioSource unless muted or audioSource is none. Whether there was a clip (false for none),
        // for the caller's lastSound and soundsPlayed, which follow it either way.
        public static bool PlaySound(AudioClip clip, AudioSource audioSource, bool muted) {
            if (!Utilities.IsValid(clip)) return false;
            if (!muted && Utilities.IsValid(audioSource)) audioSource.PlayOneShot(clip);
            return true;
        }
    }
}
