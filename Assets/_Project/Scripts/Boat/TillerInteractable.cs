using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FishingVR.Boat
{
    /// <summary>
    /// VR tiller of the outboard motor. Put it on the tiller object (its pivot is the hinge), with a
    /// collider around the grip. While grabbed, the hand position, read in the boat's local space so
    /// that the boat's own motion does not interfere, drives two angles:
    /// - yaw around the motor's steering axis = motor angle (hand left = boat turns right),
    /// - pitch around the tiller hinge = throttle (lift = forward, lower = reverse).
    /// The mapping is relative to where the tiller was grabbed, so it never jumps.
    /// </summary>
    public class TillerInteractable : XRBaseInteractable
    {
        [Header("Motor")]
        [Tooltip("Motor driven by this tiller. If empty, searched in the parents.")]
        [SerializeField] BoatMotor motor;

        [Header("Throttle (tiller pitch)")]
        [Tooltip("Lifting the tiller by this angle (degrees) gives full forward throttle.")]
        [SerializeField, Range(5f, 60f)] float maxLiftAngle = 25f;

        [Tooltip("Lowering the tiller by this angle (degrees) gives full reverse.")]
        [SerializeField, Range(0f, 45f)] float maxDropAngle = 15f;

        [Tooltip("Small pitch range (degrees) around horizontal that means idle, so a relaxed hand does not move the boat.")]
        [SerializeField, Range(0f, 10f)] float idleDeadZone = 3f;

        [Header("Release")]
        [Tooltip("On release the tiller drops back to horizontal and the throttle goes to idle (dead-man). " +
                 "Uncheck to keep the throttle where it was left (cruise control).")]
        [SerializeField] bool releaseToIdle = true;

        [Tooltip("Speed (degrees per second) at which the released tiller returns to horizontal.")]
        [SerializeField, Min(1f)] float returnSpeed = 90f;

        [Header("Haptics")]
        [Tooltip("Controller vibration while holding the tiller at idle (engine running).")]
        [SerializeField, Range(0f, 1f)] float idleRumble = 0.02f;

        [Tooltip("Extra vibration at full throttle.")]
        [SerializeField, Range(0f, 1f)] float throttleRumble = 0.12f;

        const float HapticPeriod = 0.1f;

        Transform boat;
        Quaternion restLocalRotation;
        Vector3 pitchAxisInParent;

        float yaw;    // Motor angle in degrees (Unity convention: positive = clockwise seen from above).
        float pitch;  // Tiller pitch in degrees (positive = lifted).

        // Values captured when the tiller is grabbed (relative mapping).
        float grabHandYaw, grabHandPitch, grabYaw, grabPitch;
        float hapticTimer;

        /// <summary>Current tiller pitch in degrees (positive = lifted).</summary>
        public float Pitch => pitch;

        protected override void Awake()
        {
            base.Awake();
            if (motor == null)
                motor = GetComponentInParent<BoatMotor>();
            if (motor == null || motor.MotorPivot == null || motor.Settings == null)
            {
                Debug.LogWarning($"[TillerInteractable] '{name}': no configured BoatMotor found in the parents.", this);
                enabled = false;
                return;
            }

            boat = motor.transform;
            // The tiller pitches around the boat's right axis (motor at rest), expressed in the tiller's parent space.
            restLocalRotation = transform.localRotation;
            pitchAxisInParent = transform.parent.InverseTransformDirection(boat.right);
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            yaw = motor.MotorAngle; // The keyboard may have moved the motor in the meantime.
            Vector3 hand = HandInBoatSpace(args.interactorObject);
            grabHandYaw = HandYaw(hand);
            grabHandPitch = HandPitch(hand, yaw);
            grabYaw = yaw;
            grabPitch = pitch;
            hapticTimer = 0f;
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (releaseToIdle && motor != null)
                motor.SetInput(Steer, 0f, immediateSteer: true);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic || boat == null)
                return;

            if (isSelected)
            {
                var interactor = firstInteractorSelecting;
                Vector3 hand = HandInBoatSpace(interactor);
                float maxSteer = motor.Settings.maxSteerAngle;

                yaw = Mathf.Clamp(grabYaw + Mathf.DeltaAngle(grabHandYaw, HandYaw(hand)), -maxSteer, maxSteer);
                pitch = Mathf.Clamp(grabPitch + Mathf.DeltaAngle(grabHandPitch, HandPitch(hand, yaw)), -maxDropAngle, maxLiftAngle);

                motor.SetInput(Steer, PitchToThrottle(pitch), immediateSteer: true);
                UpdateHaptics(interactor);
            }
            else if (releaseToIdle)
            {
                pitch = Mathf.MoveTowards(pitch, 0f, returnSpeed * Time.deltaTime);
            }

            transform.localRotation = Quaternion.AngleAxis(-pitch, pitchAxisInParent) * restLocalRotation;
        }

        /// <summary>Steering request for the motor: the motor angle is the opposite of the boat's turn.</summary>
        float Steer => -yaw / motor.Settings.maxSteerAngle;

        float PitchToThrottle(float angle)
        {
            if (Mathf.Abs(angle) <= idleDeadZone)
                return 0f;
            return angle > 0f
                ? Mathf.InverseLerp(idleDeadZone, maxLiftAngle, angle)
                : -Mathf.InverseLerp(idleDeadZone, Mathf.Max(maxDropAngle, idleDeadZone + 0.01f), -angle);
        }

        Vector3 HandInBoatSpace(IXRInteractor interactor) =>
            boat.InverseTransformPoint(interactor.GetAttachTransform(this).position);

        /// <summary>Angle of the hand around the motor's steering axis, seen from above (degrees).</summary>
        float HandYaw(Vector3 handInBoat)
        {
            Vector3 v = handInBoat - boat.InverseTransformPoint(motor.MotorPivot.position);
            return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
        }

        /// <summary>Elevation of the hand seen from the tiller hinge, in the motor's turned frame (degrees).</summary>
        float HandPitch(Vector3 handInBoat, float motorYaw)
        {
            Vector3 v = handInBoat - boat.InverseTransformPoint(transform.position);
            v = Quaternion.AngleAxis(-motorYaw, Vector3.up) * v; // Undo the motor rotation.
            return Mathf.Atan2(v.y, v.z) * Mathf.Rad2Deg;
        }

        void UpdateHaptics(IXRSelectInteractor interactor)
        {
            hapticTimer -= Time.deltaTime;
            if (hapticTimer > 0f || interactor is not XRBaseInputInteractor input)
                return;

            hapticTimer = HapticPeriod;
            float amplitude = idleRumble + Mathf.Abs(motor.Throttle) * throttleRumble;
            if (amplitude > 0f)
                input.SendHapticImpulse(amplitude, HapticPeriod);
        }
    }
}
