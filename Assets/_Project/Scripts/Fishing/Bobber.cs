using UnityEngine;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Floating bobber: simple buoyancy against <see cref="WaterLevel"/> plus helpers
    /// the fish uses to make it dip (nibble) or sink (bite).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Bobber : MonoBehaviour
    {
        [Header("Sockets")]
        [SerializeField] Transform lineTop;
        [SerializeField] Transform lineBottom;

        [Header("Buoyancy")]
        [Tooltip("Upward acceleration per metre of submersion.")]
        [SerializeField] float buoyancy = 60f;
        [SerializeField] float waterDrag = 4f;
        [SerializeField] float waterAngularDrag = 4f;
        [Tooltip("Height of the bobber's float line above its pivot when resting.")]
        [SerializeField] float floatOffset = 0.01f;

        Rigidbody body;
        float extraSink;      // metres pushed under by the fish
        float extraSinkTimer;

        public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();
        public Transform LineTop => lineTop != null ? lineTop : transform;
        public Transform LineBottom => lineBottom != null ? lineBottom : transform;

        public bool IsInWater => transform.position.y <= WaterLevel.CurrentHeight + floatOffset + 0.02f;

        /// <summary>True when the bobber floats and barely moves (ready for a fish).</summary>
        public bool IsSettled => IsInWater && Body.linearVelocity.sqrMagnitude < 0.04f;

        /// <summary>
        /// Kinematic = carried under the rod tip (transform driven, no interpolation),
        /// otherwise simulated.
        /// </summary>
        public void SetCarried(bool carried)
        {
            Body.isKinematic = carried;
            Body.interpolation = carried ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
        }

        void FixedUpdate()
        {
            if (Body.isKinematic)
                return;

            if (extraSinkTimer > 0f)
            {
                extraSinkTimer -= Time.fixedDeltaTime;
                if (extraSinkTimer <= 0f)
                    extraSink = 0f;
            }

            var surface = WaterLevel.CurrentHeight + floatOffset - extraSink;
            var depth = surface - transform.position.y;
            if (depth <= 0f)
                return;

            // Cancel gravity once under the float line, plus a spring: the bobber rests with its
            // pivot on the float line (top half visible) instead of sinking until buoyancy = g.
            body.AddForce(Vector3.up * (-Physics.gravity.y + buoyancy * Mathf.Min(depth, 0.5f)), ForceMode.Acceleration);
            body.AddForce(-body.linearVelocity * waterDrag, ForceMode.Acceleration);
            body.AddTorque(-body.angularVelocity * waterAngularDrag, ForceMode.Acceleration);

            // Keep the bobber upright in water.
            var tilt = Vector3.Cross(transform.up, Vector3.up);
            body.AddTorque(tilt * 30f, ForceMode.Acceleration);
        }

        /// <summary>Short dip, used for nibbles.</summary>
        public void Dip(float depth, float duration)
        {
            extraSink = depth;
            extraSinkTimer = duration;
        }

        /// <summary>Pull the bobber under water (bite) for a duration.</summary>
        public void Sink(float depth, float duration) => Dip(depth, duration);

        public void ClearSink()
        {
            extraSink = 0f;
            extraSinkTimer = 0f;
        }
    }
}
