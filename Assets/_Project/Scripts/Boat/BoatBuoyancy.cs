using FishingVR.Water;
using UnityEngine;

namespace FishingVR.Boat
{
    /// <summary>
    /// Simple point-based buoyancy: each float point acts as a damped spring pushing the hull up
    /// when it is below the water surface. Forces are applied at the point positions, so the
    /// boat naturally rights itself (roll / pitch) when tilted.
    ///
    /// Tuning is expressed in intuitive terms: <see cref="restDepth"/> is how deep the float points
    /// sit at rest, and the spring stiffness is derived from it so that buoyancy balances gravity.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class BoatBuoyancy : MonoBehaviour
    {
        [Header("Water")]
        [Tooltip("Optional. If empty, the active water of the scene (WaterSurface.Current) is used.")]
        [SerializeField] WaterSurface waterOverride;

        [Header("Float points")]
        [Tooltip("Parent object whose children are the float points (placed on the underside of the hull).")]
        [SerializeField] Transform floatPointsRoot;

        [Header("Buoyancy")]
        [Tooltip("Depth (m) of the float points below the surface when the boat is at rest. Smaller = the boat floats higher.")]
        [SerializeField, Range(0.02f, 0.5f)] float restDepth = 0.15f;
        [Tooltip("Depth (m) beyond which a point stops pushing harder (the hull is fully under water there).")]
        [SerializeField, Range(0.1f, 2f)] float maxDepth = 0.6f;
        [Tooltip("Vertical damping. 1 = no bounce (critical), < 1 = bouncy, > 1 = sluggish.")]
        [SerializeField, Range(0f, 2f)] float dampingRatio = 0.6f;

        [Header("Debug")]
        [SerializeField] bool drawGizmos = true;

        Rigidbody body;
        Transform[] floatPoints = System.Array.Empty<Transform>();
        float[] lastDepths = System.Array.Empty<float>();

        /// <summary>Fraction (0..1) of float points currently under water.</summary>
        public float SubmergedRatio { get; private set; }

        /// <summary>The water surface used by this boat (override, or the scene's active one).</summary>
        public WaterSurface ActiveWater => waterOverride != null ? waterOverride : WaterSurface.Current;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            CollectFloatPoints();
        }

        void CollectFloatPoints()
        {
            if (floatPointsRoot == null || floatPointsRoot.childCount == 0)
            {
                Debug.LogWarning($"[BoatBuoyancy] No float points on '{name}': assign 'Float Points Root'.", this);
                return;
            }

            floatPoints = new Transform[floatPointsRoot.childCount];
            for (int i = 0; i < floatPoints.Length; i++)
                floatPoints[i] = floatPointsRoot.GetChild(i);
            lastDepths = new float[floatPoints.Length];
        }

        void FixedUpdate()
        {
            var water = ActiveWater;
            int count = floatPoints.Length;
            if (water == null || count == 0)
            {
                SubmergedRatio = 0f;
                return;
            }

            // Each point carries an equal share of the boat's weight.
            // Spring: at rest, count * stiffness * restDepth = mass * g.
            float massPerPoint = body.mass / count;
            float stiffness = massPerPoint * Physics.gravity.magnitude / restDepth;          // N/m
            float damping = dampingRatio * 2f * Mathf.Sqrt(stiffness * massPerPoint);       // N.s/m

            int submerged = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 point = floatPoints[i].position;
                float depth = water.GetDepth(point);
                lastDepths[i] = depth;
                if (depth <= 0f)
                    continue;

                submerged++;
                float springForce = stiffness * Mathf.Min(depth, maxDepth);
                // Fade the damping in as the point enters the water, to avoid a sudden kick.
                float verticalSpeed = body.GetPointVelocity(point).y;
                float dampingForce = -damping * verticalSpeed * Mathf.Clamp01(depth / restDepth);
                // Water can push the hull up, never pull it down.
                float force = Mathf.Max(0f, springForce + dampingForce);
                body.AddForceAtPosition(Vector3.up * force, point, ForceMode.Force);
            }

            SubmergedRatio = (float)submerged / count;
        }

        // --- Debug helpers: right-click the component header (or its â‹® menu) in Play mode ---

        [ContextMenu("Debug: Kick Roll")]
        void DebugKickRoll() => Kick(Vector3.forward);

        [ContextMenu("Debug: Kick Pitch")]
        void DebugKickPitch() => Kick(Vector3.right);

        [ContextMenu("Debug: Drop From 2m")]
        void DebugDrop()
        {
            if (!Application.isPlaying) return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position += Vector3.up * 2f;
        }

        void Kick(Vector3 localAxis)
        {
            if (!Application.isPlaying) return;
            body.AddRelativeTorque(localAxis * 1.5f, ForceMode.VelocityChange);
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos || floatPointsRoot == null)
                return;

            bool playing = Application.isPlaying && lastDepths.Length == floatPoints.Length;
            for (int i = 0; i < floatPointsRoot.childCount; i++)
            {
                Vector3 p = floatPointsRoot.GetChild(i).position;
                if (playing && i < lastDepths.Length)
                {
                    float d = lastDepths[i];
                    Gizmos.color = d > 0f ? new Color(0.1f, 0.4f, 1f) : new Color(1f, 0.6f, 0.1f);
                    // Line length = depth under water: shows how hard each point is pushing.
                    if (d > 0f)
                        Gizmos.DrawLine(p, p + Vector3.up * Mathf.Min(d, maxDepth));
                }
                else
                {
                    Gizmos.color = Color.cyan;
                }
                Gizmos.DrawSphere(p, 0.04f);
            }

            if (Application.isPlaying && body != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(body.worldCenterOfMass, 0.08f);
            }
        }
    }
}
