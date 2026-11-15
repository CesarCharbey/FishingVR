using FishingVR.Boat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishingVR.Dev
{
    /// <summary>
    /// Dev-only driving with the arrow keys, to tune the boat physics without a headset.
    /// Up/Down move the throttle lever (it stays where you leave it, like the VR tiller),
    /// Left/Right steer while held, ! cuts the throttle, R puts the boat back at its start pose.
    /// Arrow keys are used because WASD/ZQSD are taken by the XR Interaction Simulator.
    /// Remove or disable this component once the VR tiller drives the motor.
    /// </summary>
    [RequireComponent(typeof(BoatMotor))]
    public class BoatKeyboardInput : MonoBehaviour
    {
        [Tooltip("How fast the throttle lever moves while Up/Down is held (units per second).")]
        [SerializeField, Min(0.1f)] float throttleLeverSpeed = 0.6f;

        BoatMotor motor;
        float throttleLever;

        BoatHydrodynamics hydrodynamics;
        Rigidbody body;
        Vector3 startPosition;
        Quaternion startRotation;

        void Awake()
        {
            motor = GetComponent<BoatMotor>();
            hydrodynamics = GetComponent<BoatHydrodynamics>();
            body = GetComponent<Rigidbody>();
            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        /// <summary>Puts the boat back where it started, motionless, throttle cut.</summary>
        void ResetBoat()
        {
            throttleLever = 0f;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            // Teleport both the body and the transform so that interpolation does not smear the jump.
            body.position = startPosition;
            body.rotation = startRotation;
            transform.SetPositionAndRotation(startPosition, startRotation);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            float leverInput = (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);
            throttleLever = Mathf.Clamp(throttleLever + leverInput * throttleLeverSpeed * Time.deltaTime, -1f, 1f);
            if (keyboard.slashKey.wasPressedThisFrame)
                throttleLever = 0f;
            if (keyboard.rKey.wasPressedThisFrame)
                ResetBoat();

            float steer = (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
            motor.SetInput(steer, throttleLever);
        }

        void OnGUI()
        {
            float speedKmh = hydrodynamics != null ? hydrodynamics.ForwardSpeed * 3.6f : 0f;
            GUI.Label(new Rect(10, 10, 600, 22),
                $"Boat  throttle: {motor.Throttle:+0.00;-0.00}   motor angle: {motor.MotorAngle:0}°   speed: {speedKmh:0.0} km/h   (arrows, ! = cut, R = reset)");
        }
    }
}
