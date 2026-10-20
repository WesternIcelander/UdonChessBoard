using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // Small helpers shared by several behaviours (step 12.18), so a change to one is made once. Holds no state;
    // everything comes in through parameters (see docs/architecture.md).
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

        // Plays clip from audioSource unless muted or audioSource is none. Whether there was a clip (false for none),
        // for the caller's lastSound and soundsPlayed, which follow it either way.
        public static bool PlaySound(AudioClip clip, AudioSource audioSource, bool muted) {
            if (!Utilities.IsValid(clip)) return false;
            if (!muted && Utilities.IsValid(audioSource)) audioSource.PlayOneShot(clip);
            return true;
        }
    }
}
