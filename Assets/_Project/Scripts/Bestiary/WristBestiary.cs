using Unity.XR.CoreUtils;
using UnityEngine;

namespace FishingVR.Bestiary
{
    /// <summary>
    /// Bestiary on the left wrist, only shown when asked for (it never blocks the view otherwise):
    /// turn the left palm toward your face, hand in front of you (like looking at the inside of your wrist),
    /// and hold it a moment; it appears above the hand, facing you, and hides as soon as the hand turns away.
    /// The panel itself is a <see cref="BestiaryPanel"/> child.
    /// </summary>
    public class WristBestiary : MonoBehaviour
    {
        [SerializeField] BestiaryPanel panel;
        [Tooltip("Left controller / hand. Empty = found under the XR Origin (name containing 'Left').")]
        [SerializeField] Transform wrist;
        [Tooltip("Axis of the wrist that comes out of the palm, in its local space (left hand: +X).")]
        [SerializeField] Vector3 palmAxis = Vector3.right;

        [Header("Gesture")]
        [Tooltip("The palm faces the eyes within this angle (deg).")]
        [SerializeField] float palmAngle = 50f;
        [Tooltip("The hand is in front of the eyes within this angle (deg)...")]
        [SerializeField] float viewAngle = 45f;
        [Tooltip("...and closer than this (m).")]
        [SerializeField] float maxDistance = 0.75f;
        [SerializeField] float showDelay = 0.2f;
        [SerializeField] float hideDelay = 0.3f;

        [Header("Placement")]
        [Tooltip("Panel position from the wrist: up (world) and toward the eyes (m).")]
        [SerializeField] float above = 0.17f;
        [SerializeField] float towardEyes = 0.05f;
        [SerializeField] float followSpeed = 14f;
        [SerializeField] float fadeSpeed = 8f;

        /// <summary>Always shown (tests, checking the text in the headset without the gesture).</summary>
        public bool ForceVisible { get => forceVisible; set => forceVisible = value; }
        [Tooltip("Always shown (tests: check the text in the headset without the gesture).")]
        [SerializeField] bool forceVisible;

        public bool IsShown => shown > 0.5f;

        Camera cam;
        float gestureTimer, lostTimer, shown;
        bool wanted;

        void Start()
        {
            if (panel == null) panel = GetComponentInChildren<BestiaryPanel>(true);
            transform.localScale = Vector3.one * 0.05f;
        }

        void LateUpdate()
        {
            if (cam == null || wrist == null) FindRig();
            if (cam == null || wrist == null || panel == null) return;
            var dt = Time.deltaTime;

            var head = cam.transform.position;
            var toHead = head - wrist.position;
            var distance = toHead.magnitude;
            var palm = wrist.TransformDirection(palmAxis);
            var gesture = distance < maxDistance
                          && Vector3.Angle(palm, toHead) < palmAngle
                          && Vector3.Angle(cam.transform.forward, -toHead) < viewAngle;

            if (gesture) { gestureTimer += dt; lostTimer = 0f; }
            else { lostTimer += dt; gestureTimer = 0f; }
            if (!wanted && gestureTimer >= showDelay)
            {
                wanted = true;
                FishingVR.Audio.GameAudio.Play(FishingVR.Audio.SoundId.WristOpen, wrist.position);
            }
            if (wanted && lostTimer >= hideDelay) wanted = false;
            var visible = wanted || forceVisible;

            shown = Mathf.MoveTowards(shown, visible ? 1f : 0f, fadeSpeed * dt);
            // Grows in / out (never to 0: the panel image is re-rendered at its current size).
            transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, Mathf.SmoothStep(0f, 1f, shown));
            if (panel.Snapshot != null) panel.Snapshot.Visible = shown > 0f;
            if (shown <= 0f) return;

            // Above the hand, a bit toward the eyes, facing them (upright).
            var target = wrist.position + Vector3.up * above + toHead.normalized * towardEyes;
            var look = target - head;
            look.y *= 0.6f; // keep it fairly upright
            var rot = Quaternion.LookRotation(look.sqrMagnitude > 1e-4f ? look.normalized : cam.transform.forward, Vector3.up);
            var k = 1f - Mathf.Exp(-followSpeed * dt);
            if (shown < 0.05f) transform.SetPositionAndRotation(target, rot);
            else transform.SetPositionAndRotation(Vector3.Lerp(transform.position, target, k), Quaternion.Slerp(transform.rotation, rot, k));
        }

        void FindRig()
        {
            var rig = FindAnyObjectByType<XROrigin>();
            if (rig == null) return;
            cam = rig.Camera != null ? rig.Camera : Camera.main;
            if (wrist != null) return;
            // The left controller: the first transform under the rig named "Left ... Controller" / "Left Hand".
            foreach (var tr in rig.GetComponentsInChildren<Transform>(true))
            {
                var n = tr.name;
                if (n.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n.IndexOf("Controller", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Hand", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    wrist = tr;
                    break;
                }
            }
            if (wrist == null) Debug.LogWarning("[WristBestiary] No left controller found under the XR Origin.", this);
        }
    }
}
