using UnityEngine;

namespace FishingVR.Boat
{
    /// <summary>
    /// Water resistance on the hull. The key to a "boat feel":
    /// - low resistance forward (the hull slides along its length),
    /// - high resistance sideways (the hull acts like a keel), so the boat goes where it points
    ///   instead of drifting like a bar of soap.
    ///
    /// The sideways resistance is applied at two points along the keel (bow and stern), each
    /// with its own velocity. When the boat spins, bow and stern sweep the water sideways, so the
    /// same resistance also opposes the rotation, like on a real hull.
    /// Forces scale with how much of the hull is in the water.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoatBuoyancy))]
    [DisallowMultipleComponent]
    public class BoatHydrodynamics : MonoBehaviour
    {
        [Header("Linear drag (N per (m/s)²)")]
        [Tooltip("Resistance along the boat's length. With the motor thrust, sets the top speed: v = sqrt(thrust / drag).")]
        [SerializeField, Min(0f)] float forwardDrag = 40f;

        [Tooltip("Total sideways resistance, shared between the bow and stern points. Keeps the boat on its course and resists turning.")]
        [SerializeField, Min(0f)] float lateralDrag = 600f;

        [Tooltip("Small linear term (N per m/s) so that the boat stops completely at low speed.")]
        [SerializeField, Min(0f)] float lowSpeedDrag = 30f;

        [Header("Keel (where the sideways resistance acts)")]
        [Tooltip("Distance (m) from the boat's origin to the bow and stern resistance points, along its length.")]
        [SerializeField, Min(0.1f)] float keelHalfLength = 1.3f;

        [Tooltip("Height (m) of the resistance points in the boat's local space (near the bottom of the hull).")]
        [SerializeField] float keelHeight = 0.1f;

        [Header("Angular damping (1/s)")]
        [Tooltip("Extra damping on turning (yaw), on top of the keel resistance. Keep it small: it stops slow residual rotations.")]
        [SerializeField, Min(0f)] float yawDamping = 0.5f;

        [Tooltip("Slows down rocking (roll and pitch). Higher = calmer boat, more comfortable in VR.")]
        [SerializeField, Min(0f)] float rockDamping = 1f;

        Rigidbody body;
        BoatBuoyancy buoyancy;
        readonly float[] lastKeelForces = new float[2];

        /// <summary>Signed forward speed of the boat (m/s).</summary>
        public float ForwardSpeed { get; private set; }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            buoyancy = GetComponent<BoatBuoyancy>();
        }

        void FixedUpdate()
        {
            Vector3 localVelocity = transform.InverseTransformDirection(body.linearVelocity);
            ForwardSpeed = localVelocity.z;

            float inWater = buoyancy.SubmergedRatio;
            if (inWater <= 0f)
                return;

            // Forward resistance: quadratic (dominant at speed) + linear (dominant near standstill).
            float forwardForce = -(forwardDrag * localVelocity.z * Mathf.Abs(localVelocity.z) + lowSpeedDrag * localVelocity.z);
            body.AddRelativeForce(new Vector3(0f, 0f, forwardForce * inWater), ForceMode.Force);

            // Sideways resistance at the bow (+1) and at the stern (-1). Each point uses its own
            // velocity, which includes the rotation (v + w x r): spinning is resisted naturally.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                Vector3 point = transform.TransformPoint(new Vector3(0f, keelHeight, side * keelHalfLength));
                float lateralSpeed = Vector3.Dot(body.GetPointVelocity(point), transform.right);
                float force = -(0.5f * lateralDrag * lateralSpeed * Mathf.Abs(lateralSpeed) + 0.5f * lowSpeedDrag * lateralSpeed);
                lastKeelForces[i] = force * inWater;
                body.AddForceAtPosition(transform.right * lastKeelForces[i], point, ForceMode.Force);
            }

            // Extra angular damping as an acceleration (independent of mass and shape): -rate * angularVelocity.
            Vector3 localAngular = transform.InverseTransformDirection(body.angularVelocity);
            Vector3 angularDamping = new Vector3(
                -rockDamping * localAngular.x, // pitch
                -yawDamping * localAngular.y,  // yaw
                -rockDamping * localAngular.z); // roll
            body.AddRelativeTorque(angularDamping * inWater, ForceMode.Acceleration);
        }

        void OnDrawGizmosSelected()
        {
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                Vector3 point = transform.TransformPoint(new Vector3(0f, keelHeight, side * keelHalfLength));
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(point, 0.08f);
                // Line = sideways force currently applied (1 m per 200 N).
                if (Application.isPlaying)
                    Gizmos.DrawLine(point, point + transform.right * (lastKeelForces[i] / 200f));
            }
        }
    }
}
