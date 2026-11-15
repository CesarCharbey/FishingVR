using UnityEngine;

namespace FishingVR.Boat
{
    /// <summary>
    /// Outboard motor: receives a steering and a throttle request from an input source
    /// (keyboard for debugging, VR tiller later), rotates the motor around the boat's vertical
    /// axis and pushes the boat at the propeller position, in the direction the motor faces.
    ///
    /// Turning is not scripted: because the thrust is applied off-center (at the stern) and at an
    /// angle, it naturally creates the torque that turns the boat, like a real outboard.
    /// The submerged lower unit also acts as a small rudder, so the boat still steers a little
    /// while coasting with the throttle cut.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoatBuoyancy))]
    [DisallowMultipleComponent]
    public class BoatMotor : MonoBehaviour
    {
        [Tooltip("Motor characteristics (thrust, steering...). Swap this asset to upgrade the motor.")]
        [SerializeField] BoatMotorSettings settings;

        [Tooltip("Part of the motor that rotates to steer (SM_Boat_Motor inside the model).")]
        [SerializeField] Transform motorPivot;

        [Tooltip("Propeller (SM_Boat_Motor_Propeller). Its position is where the thrust is applied.")]
        [SerializeField] Transform propeller;

        Rigidbody body;
        BoatBuoyancy buoyancy;

        // Rest pose of the motor and the axes used to rotate parts, captured at startup
        // in the parts' own local spaces (the imported model has Blender axes).
        Quaternion motorRestLocalRotation;
        Vector3 steerAxisInMotorParent;
        Vector3 spinAxisInPropeller;

        float requestedSteer;
        float requestedThrottle;

        /// <summary>Current (smoothed) throttle, from -1 (full reverse) to 1 (full forward).</summary>
        public float Throttle { get; private set; }

        /// <summary>Current motor angle in degrees. Negative = motor turned left = boat turns right.</summary>
        public float MotorAngle { get; private set; }

        /// <summary>True when the propeller is under water and can push the boat.</summary>
        public bool IsPropellerSubmerged { get; private set; }

        /// <summary>Sideways force (N) of the water on the motor's lower unit, along the motor's right axis.</summary>
        public float LowerUnitForce { get; private set; }

        public BoatMotorSettings Settings
        {
            get => settings;
            set => settings = value;
        }

        /// <summary>
        /// Sets the driver's request. Called every frame by the active input source.
        /// </summary>
        /// <param name="steer">-1 = turn left, +1 = turn right.</param>
        /// <param name="throttle">-1 = full reverse, 0 = idle, +1 = full forward.</param>
        public void SetInput(float steer, float throttle)
        {
            requestedSteer = Mathf.Clamp(steer, -1f, 1f);
            requestedThrottle = Mathf.Clamp(throttle, -1f, 1f);
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            buoyancy = GetComponent<BoatBuoyancy>();

            if (settings == null || motorPivot == null || propeller == null)
            {
                Debug.LogWarning($"[BoatMotor] '{name}': assign Settings, Motor Pivot and Propeller.", this);
                enabled = false;
                return;
            }

            // Steering axis = the boat's vertical axis, expressed in the motor's parent space.
            motorRestLocalRotation = motorPivot.localRotation;
            steerAxisInMotorParent = motorPivot.parent.InverseTransformDirection(transform.up);

            // Propeller spins around the boat's forward axis (motor at rest), in its own space.
            spinAxisInPropeller = propeller.InverseTransformDirection(transform.forward);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // Smooth the requests: a real motor neither spins up nor turns instantly.
            Throttle = Mathf.MoveTowards(Throttle, requestedThrottle, settings.throttleResponse * dt);
            // Turning right means swinging the motor (and the tiller) to the left: negative angle.
            float targetAngle = -requestedSteer * settings.maxSteerAngle;
            MotorAngle = Mathf.MoveTowards(MotorAngle, targetAngle, settings.steerResponse * dt);

            var water = buoyancy.ActiveWater;
            Vector3 thrustPoint = propeller.position;
            IsPropellerSubmerged = water != null && water.GetDepth(thrustPoint) > 0f;
            LowerUnitForce = 0f;
            if (!IsPropellerSubmerged)
                return;

            // Motor axes: the boat's axes rotated by the motor angle around the vertical.
            Quaternion motorRotation = Quaternion.AngleAxis(MotorAngle, transform.up);
            Vector3 motorForward = motorRotation * transform.forward;
            Vector3 motorSide = motorRotation * transform.right;

            // Thrust: the motor pushes in the direction it faces.
            if (!Mathf.Approximately(Throttle, 0f))
            {
                float thrust = Throttle > 0f
                    ? Throttle * settings.maxThrust
                    : Throttle * settings.maxThrust * settings.reverseThrustRatio;
                body.AddForceAtPosition(motorForward * thrust, thrustPoint, ForceMode.Force);
            }

            // Lower unit: a small plate aligned with the motor. Water crossing it sideways is resisted,
            // so a turned motor still steers the boat while it coasts (weaker than with thrust).
            float sideSpeed = Vector3.Dot(body.GetPointVelocity(thrustPoint), motorSide);
            LowerUnitForce = -settings.lowerUnitDrag * sideSpeed * Mathf.Abs(sideSpeed);
            body.AddForceAtPosition(motorSide * LowerUnitForce, thrustPoint, ForceMode.Force);
        }

        void Update()
        {
            // Visuals only (the physics uses MotorAngle and Throttle directly).
            motorPivot.localRotation = Quaternion.AngleAxis(MotorAngle, steerAxisInMotorParent) * motorRestLocalRotation;

            if (IsPropellerSubmerged || !Mathf.Approximately(Throttle, 0f))
                propeller.Rotate(spinAxisInPropeller, Throttle * settings.propellerMaxSpeed * Time.deltaTime, Space.Self);
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || propeller == null)
                return;

            Quaternion motorRotation = Quaternion.AngleAxis(MotorAngle, transform.up);
            Vector3 p = propeller.position;

            // Thrust (green, red if the propeller is out of the water).
            Gizmos.color = IsPropellerSubmerged ? Color.green : Color.red;
            Gizmos.DrawLine(p, p + motorRotation * transform.forward * Throttle * 1.5f);

            // Lower unit force (cyan), 1 m per 100 N.
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(p, p + motorRotation * transform.right * (LowerUnitForce / 100f));
        }
    }
}
