using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace SiggiChessBoard {
    // The holder that lets players move and resize a 2D board view (docs/board-2d.md; PLAN.md 12.16). Its children are
    // the view (a ChessBoard2DView, which knows nothing of the holder), the grip (a bar under the view, with a
    // VRCPickup, and a VRCObjectSync when the view is synced) and the corner (a VRCPickup at the view's bottom right).
    // The view follows the grip, turning as it turns (a VR player's hand tilts it freely; after a desktop player picks
    // it up, it stands upright: the synced upright flag), its bottom edge staying where it was from the grip in the
    // scene and the view sized about it; the corner stays where it was from the view's bottom right corner. Holding
    // the corner sets the size (minSize to maxSize) from how far along the view's bottom edge it is. Built by Tools >
    // Chess Board > Create Movable 2D Board View (Editor/ChessBoard2DViewBuilder.cs).
    //
    // With the view's Synced on, the grip's VRCObjectSync carries the place to everyone (picking it up takes ownership,
    // as any pickup), and the size is a synced field here. VRCObjectSync forces continuous sync on behaviours on its own
    // object, so this manual-synced behaviour is on the grip's parent, the holder. With Synced off, moves and sizes stay
    // on the client that made them.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ChessBoard2DViewHolder : UdonSharpBehaviour {
        // While the corner is held, the size is sent at most this often (seconds), and once more on letting go.
        public const float SendInterval = 0.2f;
        // How fast another player's resize is followed (per second, exponential).
        const float FollowRate = 12;
        // The default smallest size, which the builder fits the grip to (so it doesn't reach the corner).
        public const float DefaultMinSize = 0.25f;

        [Tooltip("On: players can move the view (the bar under it) and resize it (the corner at its bottom right). "
            + "Off: the view stays where it's placed, and the bar and corner are hidden.")]
        public bool movable = true;
        public ChessBoard2DView view;
        // The grip and the corner, each with its pickup, and their visible parts (hidden when not movable).
        public Transform grip;
        public VRCPickup gripPickup;
        public Transform corner;
        public VRCPickup cornerPickup;
        public Renderer[] handleRenderers = new Renderer[0];
        // The grip's visible bar, turned upright under an upright view (found as the grip's first child when empty).
        public Transform gripModel;
        [Tooltip("The smallest and largest size, as a share of the size the view has in the scene. The builder fits "
            + "the bar to the smallest, so it doesn't reach the corner.")]
        public float minSize = DefaultMinSize;
        public float maxSize = 3f;
        [Tooltip("How near (m) a VR hand must be to grab the bar or the corner. Desktop players keep the pickups' own "
            + "reach.")]
        public float vrReach = 0.05f;

        [UdonSynced] float syncedSize = 1;
        [UdonSynced] bool syncedUpright;

        // The size drawn and the one it goes to (the same but while following another player's resize).
        float size = 1;
        float targetSize = 1;
        // The layout from the grip (in its space): the middle of the view's bottom edge, the view's place from it and
        // its bottom right corner from it at size 1, the corner's place from that, the view's turn and scale.
        Vector3 bottom;
        Vector3 viewFromBottom;
        Vector3 rightFromBottom;
        Vector3 cornerOut;
        Quaternion viewTurn = Quaternion.identity;
        Vector3 viewScale = Vector3.one;
        // Where the grip was placed, for PutBack.
        Vector3 startPosition;
        Quaternion startRotation = Quaternion.identity;
        bool started;
        // Whether the local player is in VR, whether the view stands upright (last picked up on desktop), the grip's
        // heading last placed (for when it points straight up or down), and the view's turn last placed.
        bool inVR;
        bool upright;
        Quaternion heading = Quaternion.identity;
        Quaternion turn = Quaternion.identity;
        bool gripHeld;
        bool cornerHeld;
        bool placed;
        bool sizeUnsent;
        float nextSend;

        void Start() {
            if (!Utilities.IsValid(view) || !Utilities.IsValid(grip) || !Utilities.IsValid(corner)
                || !Utilities.IsValid(gripPickup) || !Utilities.IsValid(cornerPickup)) {
                Debug.LogError("[ChessBoard2DViewHolder] " + gameObject.name + ": View, Grip, Grip Pickup, Corner or "
                    + "Corner Pickup is empty (Tools > Chess Board > Create Movable 2D Board View).");
                return;
            }
            Transform v = view.transform;
            Rect rect = view.GetComponent<RectTransform>().rect;
            Vector3 bottomWorld = v.TransformPoint(new Vector3(rect.center.x, rect.yMin, 0));
            Vector3 rightWorld = v.TransformPoint(new Vector3(rect.xMax, rect.yMin, 0));
            Quaternion back = Quaternion.Inverse(grip.rotation);
            bottom = back * (bottomWorld - grip.position);
            viewFromBottom = back * (v.position - bottomWorld);
            rightFromBottom = back * (rightWorld - bottomWorld);
            cornerOut = back * (corner.position - rightWorld);
            viewTurn = back * v.rotation;
            viewScale = v.localScale;
            startPosition = grip.position;
            startRotation = grip.rotation;
            if (minSize <= 0) minSize = 0.01f;
            if (maxSize < minSize) maxSize = minSize;
            VRCPlayerApi local = Networking.LocalPlayer;
            inVR = Utilities.IsValid(local) && local.IsUserInVR();
            heading = Upright(grip.rotation, Quaternion.identity);
            if (!Utilities.IsValid(gripModel) && grip.childCount > 0) gripModel = grip.GetChild(0);
            if (inVR) {
                gripPickup.proximity = vrReach;
                cornerPickup.proximity = vrReach;
            }
            started = true;
            if (view.synced) {
                size = Mathf.Clamp(syncedSize, minSize, maxSize);
                targetSize = size;
                upright = syncedUpright;
            }
            ApplyMovable();
            Follow();
        }

        public override void PostLateUpdate() {
            if (!started) return;
            bool gripHere = HeldHere(gripPickup);
            if (gripHeld && !gripHere) {
                Debug.Log("[ChessBoard2DViewHolder] " + gameObject.name + ": moved to " + grip.position
                    + (view.synced ? " here, sent" : " here"));
            }
            if (gripHere && !gripHeld) SetUpright(!inVR);
            gripHeld = gripHere;
            bool cornerHere = HeldHere(cornerPickup);
            if (cornerHere) {
                if (!cornerHeld && view.synced) Networking.SetOwner(Networking.LocalPlayer, gameObject);
                float wanted = SizeFor(Quaternion.Inverse(turn) * (corner.position - grip.position) - bottom
                    - cornerOut, rightFromBottom, minSize, maxSize);
                if (wanted != size) {
                    size = wanted;
                    targetSize = wanted;
                    sizeUnsent = view.synced;
                    placed = false;
                }
            } else if (cornerHeld) {
                Debug.Log("[ChessBoard2DViewHolder] " + gameObject.name + ": resized to " + Round(size)
                    + (view.synced ? " here, sent" : " here"));
                nextSend = 0;
                placed = false;
            }
            cornerHeld = cornerHere;
            if (sizeUnsent && Time.time >= nextSend) SendSize();

            if (size != targetSize) {
                size = Mathf.Lerp(size, targetSize, 1 - Mathf.Exp(-FollowRate * Time.deltaTime));
                if (Mathf.Abs(size - targetSize) < 0.001f) size = targetSize;
                placed = false;
            }
            if (!placed || grip.hasChanged) Follow();
        }

        public override void OnDeserialization() {
            if (!started || !view.synced) return;
            if (syncedUpright != upright) {
                upright = syncedUpright;
                placed = false;
            }
            if (cornerHeld) return;
            float received = Mathf.Clamp(syncedSize, minSize, maxSize);
            if (received == targetSize) return;
            targetSize = received;
            Debug.Log("[ChessBoard2DViewHolder] " + gameObject.name + ": resized to " + Round(received) + " by "
                + ChessUtil.OwnerName(gameObject, "?"));
        }

        // ---- Moving and resizing ----

        // Places the view (and the corner, unless it's held here) from the grip and the size. Done when either changes,
        // after the frame's moves (PostLateUpdate); public for tests, which move the grip themselves.
        public void Follow() {
            if (!started) return;
            heading = Upright(grip.rotation, heading);
            turn = upright ? heading : grip.rotation;
            if (Utilities.IsValid(gripModel)) gripModel.localRotation = Quaternion.Inverse(grip.rotation) * turn;
            Vector3 bottomWorld = grip.position + turn * bottom;
            view.transform.SetPositionAndRotation(bottomWorld + turn * (viewFromBottom * size), turn * viewTurn);
            view.transform.localScale = viewScale * size;
            if (!cornerHeld) {
                corner.SetPositionAndRotation(bottomWorld + turn * (rightFromBottom * size + cornerOut), turn);
            }
            grip.hasChanged = false;
            placed = true;
        }

        // Sets the size (clamped to minSize and maxSize), at once; synced, it's sent to everyone. For world scripts and
        // tests.
        public void SetSize(float value) {
            if (!started) return;
            size = Mathf.Clamp(value, minSize, maxSize);
            targetSize = size;
            placed = false;
            if (view.synced) {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
                SendSize();
            }
            Follow();
        }

        // Puts the view back where it was placed in the scene, at its size there; synced, for everyone. Not on a button:
        // for world scripts (a "put back" button of the world's own) and tests.
        public void PutBack() {
            if (!started) return;
            if (view.synced) Networking.SetOwner(Networking.LocalPlayer, grip.gameObject);
            if (HeldHere(gripPickup)) gripPickup.Drop();
            grip.SetPositionAndRotation(startPosition, startRotation);
            SetSize(1);
            Debug.Log("[ChessBoard2DViewHolder] " + gameObject.name + ": put back"
                + (view.synced ? " here, sent" : " here"));
        }

        // Allows or stops moving and resizing: the pickups' pickupable, and the bar and corner shown or hidden.
        public void SetMovable(bool value) {
            movable = value;
            ApplyMovable();
        }

        public float Size() {
            return size;
        }

        public float TargetSize() {
            return targetSize;
        }

        // Whether the view stands upright, turning only with the grip's heading (on), or turns and tilts with the grip
        // (off). Picking up the grip sets it: on for a desktop player, whose hand pose would tilt it, off for a VR player.
        // It stays after letting go, and synced, it's sent to everyone. The grip itself isn't turned: VRChat places a held
        // pickup, and VRCObjectSync sends that place, so its bar (gripModel) is turned instead. Public for tests.
        public void SetUpright(bool value) {
            if (!started) return;
            if (value != upright) placed = false;
            upright = value;
            if (view.synced && syncedUpright != value) {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
                SendSize();
            }
        }

        public bool IsUpright() {
            return upright;
        }

        // rotation's heading alone (turning about the vertical); fallback when it points straight up or down.
        public static Quaternion Upright(Quaternion rotation, Quaternion fallback) {
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0;
            if (forward.sqrMagnitude < 0.0001f) return fallback;
            return Quaternion.LookRotation(forward, Vector3.up);
        }

        // The size for the corner at local (from the view's bottom middle, less the corner's own offset, in the grip's
        // space) when the view's bottom right corner is at baseRight at size 1: how far along the bottom edge it is,
        // clamped to min and max.
        public static float SizeFor(Vector3 local, Vector3 baseRight, float min, float max) {
            float lengthSquared = baseRight.sqrMagnitude;
            if (lengthSquared <= 0) return Mathf.Clamp(1, min, max);
            return Mathf.Clamp(Vector3.Dot(local, baseRight) / lengthSquared, min, max);
        }

        void ApplyMovable() {
            if (!movable) {
                if (HeldHere(gripPickup)) gripPickup.Drop();
                if (HeldHere(cornerPickup)) cornerPickup.Drop();
            }
            if (Utilities.IsValid(gripPickup)) gripPickup.pickupable = movable;
            if (Utilities.IsValid(cornerPickup)) cornerPickup.pickupable = movable;
            for (int i = 0; handleRenderers != null && i < handleRenderers.Length; i++) {
                if (Utilities.IsValid(handleRenderers[i])) handleRenderers[i].enabled = movable;
            }
        }

        // Sends the size and the upright flag.
        void SendSize() {
            syncedSize = size;
            syncedUpright = upright;
            sizeUnsent = false;
            nextSend = Time.time + SendInterval;
            RequestSerialization();
        }

        static bool HeldHere(VRCPickup pickup) {
            if (!Utilities.IsValid(pickup) || !pickup.IsHeld) return false;
            VRCPlayerApi holder = pickup.currentPlayer;
            return Utilities.IsValid(holder) && holder.isLocal;
        }

        static string Round(float value) {
            return (Mathf.Round(value * 100) / 100).ToString();
        }
    }
}
