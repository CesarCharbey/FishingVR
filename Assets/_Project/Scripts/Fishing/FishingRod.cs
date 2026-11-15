using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Root component of the CanneAPeche prefab.
    /// - One or two handed grip (XRGrabInteractable in Multiple select mode, first hand = main grip,
    ///   second hand = butt of the handle).
    /// - Tracks the rod tip velocity (used by the cast and hook-set gestures).
    /// - Cast gesture: hold the trigger (finger on the line) while holding the rod, swing forward
    ///   and release the trigger. Speed/direction of the tip at release drive the cast.
    /// - Hook-set gesture: sharp upward/backward jerk of the tip.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class FishingRod : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Transform rodTip;
        [SerializeField] FishingLine line;
        [SerializeField] ReelCrank reel;
        [Tooltip("Line strength, drag... (upgradeable equipment).")]
        [SerializeField] RodStats stats;

        [Header("Cast")]
        [Tooltip("Minimum tip speed (m/s) at trigger release to cast.")]
        [SerializeField] float minCastSpeed = 2.5f;
        [Tooltip("Tip speed is multiplied by this to get the bobber launch speed.")]
        [SerializeField] float castPowerMultiplier = 1.4f;
        [SerializeField] float maxLaunchSpeed = 18f;
        [Tooltip("0 = launch along the tip velocity, 1 = launch along the rod direction.")]
        [Range(0f, 1f)] [SerializeField] float rodDirectionInfluence = 0.35f;
        [Tooltip("Minimum forward component (dot with the rod's horizontal forward) of the swing.")]
        [Range(-1f, 1f)] [SerializeField] float minForwardDot = 0.2f;

        [Header("Hook set")]
        [Tooltip("Tip speed (m/s) upward/backward needed to set the hook.")]
        [SerializeField] float hookSetSpeed = 2.2f;
        [SerializeField] float hookSetCooldown = 0.5f;

        [Header("Velocity tracking")]
        [SerializeField] int velocitySamples = 6;

        [Header("Events")]
        public UnityEvent<Vector3> onCast;
        public UnityEvent onHookSetGesture;
        public UnityEvent onGrabbed;
        public UnityEvent onReleased;

        XRGrabInteractable grab;
        Vector3[] tipVelocities;
        int sampleIndex;
        Vector3 lastTipPos;
        bool lineHeld;
        float lastHookSetTime = -10f;

        public XRGrabInteractable Grab => grab;
        public Transform RodTip => rodTip;
        public FishingLine Line => line;
        public ReelCrank Reel => reel;
        public RodStats Stats => stats;

        /// <summary>Current line tension, 0..1 of the line strength (set by the fight, read by the bend / gauge).</summary>
        public float LineTension { get; set; }
        /// <summary>World point the line pulls toward (hook / fish).</summary>
        public Vector3 LinePullPoint { get; set; }
        public bool IsHeld => grab != null && grab.isSelected;
        public bool IsTwoHanded => grab != null && grab.interactorsSelecting.Count > 1;
        public IXRSelectInteractor MainHand => grab != null ? grab.firstInteractorSelecting : null;
        public bool IsLineHeld => lineHeld;

        /// <summary>Smoothed world velocity of the rod tip.</summary>
        public Vector3 TipVelocity
        {
            get
            {
                var sum = Vector3.zero;
                foreach (var v in tipVelocities) sum += v;
                return sum / tipVelocities.Length;
            }
        }

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            tipVelocities = new Vector3[Mathf.Max(1, velocitySamples)];
            if (rodTip != null) lastTipPos = rodTip.position;
        }

        void OnEnable()
        {
            grab.selectEntered.AddListener(OnSelectEntered);
            grab.selectExited.AddListener(OnSelectExited);
            grab.activated.AddListener(OnActivated);
            grab.deactivated.AddListener(OnDeactivated);
            if (reel != null && line != null)
                reel.onLineReeled.AddListener(line.Reel);
        }

        void OnDisable()
        {
            grab.selectEntered.RemoveListener(OnSelectEntered);
            grab.selectExited.RemoveListener(OnSelectExited);
            grab.activated.RemoveListener(OnActivated);
            grab.deactivated.RemoveListener(OnDeactivated);
            if (reel != null && line != null)
                reel.onLineReeled.RemoveListener(line.Reel);
        }

        void Update()
        {
            if (rodTip == null || Time.deltaTime <= 0f)
                return;

            var pos = rodTip.position;
            tipVelocities[sampleIndex] = (pos - lastTipPos) / Time.deltaTime;
            sampleIndex = (sampleIndex + 1) % tipVelocities.Length;
            lastTipPos = pos;

            if (IsHeld)
                DetectHookSet();
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (grab.interactorsSelecting.Count == 1)
            {
                onGrabbed?.Invoke();
                SendHaptic(0.2f, 0.05f);
            }
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (!grab.isSelected)
            {
                lineHeld = false;
                onReleased?.Invoke();
            }
        }

        void OnActivated(ActivateEventArgs args)
        {
            // Only the main hand holds the line with its index finger.
            if (args.interactorObject != (IXRActivateInteractor)MainHand)
                return;
            lineHeld = true;
        }

        void OnDeactivated(DeactivateEventArgs args)
        {
            if (!lineHeld || args.interactorObject != (IXRActivateInteractor)MainHand)
                return;
            lineHeld = false;
            TryCast(TipVelocity);
        }

        /// <summary>
        /// Cast with the given tip velocity, if fast enough and going forward.
        /// Public so it can be tested without a headset.
        /// </summary>
        public bool TryCast(Vector3 tipVelocity)
        {
            if (line == null || !line.CanCast)
                return false;

            var speed = tipVelocity.magnitude;
            var rodForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            var swingDir = tipVelocity / Mathf.Max(speed, 0.0001f);
            var forwardDot = Vector3.Dot(Vector3.ProjectOnPlane(swingDir, Vector3.up).normalized, rodForward);
            if (speed < minCastSpeed || forwardDot < minForwardDot)
                return false;

            var dir = Vector3.Slerp(swingDir, transform.forward, rodDirectionInfluence).normalized;
            var launch = dir * Mathf.Min(speed * castPowerMultiplier, maxLaunchSpeed);
            line.Cast(launch);
            onCast?.Invoke(launch);
            SendHaptic(0.4f, 0.08f);
            return true;
        }

        void DetectHookSet()
        {
            if (Time.time - lastHookSetTime < hookSetCooldown)
                return;

            var v = TipVelocity;
            // Upward, or backward toward the angler (opposite of the rod's horizontal forward).
            var back = -Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            var jerk = Mathf.Max(Vector3.Dot(v, Vector3.up), Vector3.Dot(v, back));
            if (jerk >= hookSetSpeed)
            {
                lastHookSetTime = Time.time;
                onHookSetGesture?.Invoke();
            }
        }

        /// <summary>Haptic pulse on the main hand (and the second hand if any).</summary>
        public void SendHaptic(float amplitude, float duration, bool bothHands = false)
        {
            if (grab == null)
                return;
            for (var i = 0; i < grab.interactorsSelecting.Count; i++)
            {
                if (i > 0 && !bothHands) break;
                if (grab.interactorsSelecting[i] is XRBaseInputInteractor input)
                    input.SendHapticImpulse(amplitude, duration);
            }
        }
    }
}
