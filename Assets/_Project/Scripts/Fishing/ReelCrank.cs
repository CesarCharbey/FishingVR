using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Moulinet: grab the crank knob and turn it around the crank axis to reel the line in.
    /// The hand position is projected on the crank's rotation plane; the signed angle
    /// between frames drives the crank, the spool and the reeled length.
    /// </summary>
    public class ReelCrank : XRBaseInteractable
    {
        [Header("Reel")]
        [Tooltip("Pivot rotated by the hand. Rotates around its local axis below.")]
        [SerializeField] Transform crankPivot;
        [SerializeField] Vector3 crankLocalAxis = Vector3.right;
        [Tooltip("Optional spool spinning while reeling.")]
        [SerializeField] Transform spool;
        [Tooltip("Spool's own axis (the spool mesh is a cylinder around its local X, same as the crank).")]
        [SerializeField] Vector3 spoolLocalAxis = Vector3.right;
        [SerializeField] float spoolSpeedRatio = 5f;

        [Tooltip("Line retrieved per full crank turn (m). Gear ratio x spool circumference.")]
        [SerializeField] float metersPerTurn = 0.7f;
        [Tooltip("+1 or -1: which rotation direction reels in.")]
        [SerializeField] float reelInDirection = 1f;
        [Tooltip("Anti-reverse: the crank can't turn backward.")]
        [SerializeField] bool antiReverse = true;

        [Header("Feedback")]
        [SerializeField] float hapticEveryDegrees = 45f;
        [SerializeField] float hapticAmplitude = 0.1f;

        [Header("Events")]
        [Tooltip("Metres of line reeled in this frame (> 0).")]
        public UnityEvent<float> onLineReeled;

        Vector3 lastHandDir;
        bool hasLastDir;
        float hapticAccumulator;
        float crankSpeed; // turns per second (signed, reel-in positive)

        /// <summary>Current crank speed in turns per second (reel-in positive).</summary>
        public float CrankSpeed => crankSpeed;

        Vector3 WorldAxis => crankPivot.TransformDirection(crankLocalAxis).normalized;

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            hasLastDir = false;
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            hasLastDir = false;
            crankSpeed = 0f;
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic || crankPivot == null)
                return;

            if (!isSelected)
            {
                crankSpeed = Mathf.MoveTowards(crankSpeed, 0f, Time.deltaTime * 4f);
                return;
            }

            var hand = firstInteractorSelecting.GetAttachTransform(this).position;
            var axis = WorldAxis;
            var dir = Vector3.ProjectOnPlane(hand - crankPivot.position, axis);
            if (dir.sqrMagnitude < 0.0001f)
                return;
            dir.Normalize();

            if (hasLastDir)
                Turn(Vector3.SignedAngle(lastHandDir, dir, axis), firstInteractorSelecting);
            lastHandDir = dir;
            hasLastDir = true;
        }

        /// <summary>
        /// Apply a crank rotation in degrees (positive = crank axis direction).
        /// Public so it can be driven without a headset for testing.
        /// </summary>
        public void Turn(float degrees, IXRSelectInteractor hand = null)
        {
            var reelDegrees = degrees * Mathf.Sign(reelInDirection);
            if (antiReverse && reelDegrees < 0f)
                reelDegrees = 0f;

            var dt = Mathf.Max(Time.deltaTime, 0.0001f);
            crankSpeed = Mathf.Lerp(crankSpeed, reelDegrees / 360f / dt, 0.3f);
            if (reelDegrees <= 0f)
                return;

            var applied = reelDegrees * Mathf.Sign(reelInDirection);
            crankPivot.Rotate(crankLocalAxis, applied, Space.Self);
            if (spool != null)
                spool.Rotate(spoolLocalAxis, applied * spoolSpeedRatio, Space.Self);

            onLineReeled?.Invoke(reelDegrees / 360f * metersPerTurn);

            hapticAccumulator += reelDegrees;
            if (hapticAccumulator >= hapticEveryDegrees)
            {
                hapticAccumulator = 0f;
                if (hand is XRBaseInputInteractor input)
                    input.SendHapticImpulse(hapticAmplitude, 0.02f);
            }
        }
    }
}
