using UnityEngine;

namespace FishingVR.Water
{
    /// <summary>
    /// Makes ripples where this object touches the water: rings when it enters the water or bobs up and down,
    /// a small wake when it moves along the surface. Works on anything (bobber, fish, hands, floating plants),
    /// with or without a Rigidbody: the speed is measured from the transform.
    /// </summary>
    public class WaterRippleEmitter : MonoBehaviour
    {
        [Tooltip("Size of the disturbed patch (m), about the object's radius at the waterline.")]
        [SerializeField, Min(0.02f)] float radius = 0.08f;

        [Tooltip("Depth (m) below the surface down to which the object still disturbs it. Deeper = no ripples.")]
        [SerializeField, Min(0.01f)] float contactDepth = 0.15f;

        [Header("Strength")]
        [Tooltip("Water pushed down per second, per m/s of horizontal speed (wake while moving along the surface).")]
        [SerializeField, Min(0f)] float moveStrength = 0.01f;

        [Tooltip("Water pushed per second, per m/s of vertical speed (rings while bobbing).")]
        [SerializeField, Min(0f)] float bobStrength = 0.02f;

        [Tooltip("Splash when entering the water: water pushed down (m) per m/s of falling speed.")]
        [SerializeField, Min(0f)] float splashStrength = 0.015f;

        [Tooltip("Foam left by a splash at 5 m/s (scaled with the speed).")]
        [SerializeField, Range(0f, 1f)] float splashFoam = 0.4f;

        [Tooltip("Speeds above this (m/s) are clamped; much bigger jumps are treated as teleports and ignored.")]
        [SerializeField, Min(0.1f)] float maxSpeed = 6f;

        Vector3 lastPosition;
        float lastDepth;
        bool hasLast;

        void OnEnable() => hasLast = false;

        void Update()
        {
            float dt = Time.deltaTime;
            var water = WaterSurface.Current;
            if (dt <= 0f || water == null)
                return;

            Vector3 position = transform.position;
            float depth = water.GetDepth(position); // > 0 under the surface.

            if (!hasLast)
            {
                Remember(position, depth);
                return;
            }

            Vector3 velocity = (position - lastPosition) / dt;
            if (velocity.magnitude > maxSpeed * 4f)
            {
                Remember(position, depth); // Teleported (or respawned): no splash.
                return;
            }
            velocity = Vector3.ClampMagnitude(velocity, maxSpeed);

            Vector3 onSurface = new Vector3(position.x, water.GetHeight(position), position.z);

            // Splash when crossing the surface downwards.
            if (lastDepth < 0f && depth >= 0f && velocity.y < 0f)
            {
                float fall = -velocity.y;
                WaterRipples.AddImpulse(onSurface, radius * 1.5f, -splashStrength * fall, splashFoam * Mathf.Clamp01(fall / 5f));
            }

            // Continuous disturbance while touching the surface.
            if (depth >= -0.02f && depth <= contactDepth)
            {
                float contact = 1f - Mathf.Clamp01(depth / contactDepth);
                float horizontal = new Vector2(velocity.x, velocity.z).magnitude;
                float rate = (moveStrength * horizontal + bobStrength * Mathf.Abs(velocity.y)) * contact;
                if (rate > 0f)
                    WaterRipples.AddDisturbance(onSurface, radius, -rate);
            }

            Remember(position, depth);
        }

        void Remember(Vector3 position, float depth)
        {
            lastPosition = position;
            lastDepth = depth;
            hasLast = true;
        }

        /// <summary>Extra splash from code (a fish slapping the water, a bite...).</summary>
        public void Splash(float height, float foam = 0.2f)
        {
            var water = WaterSurface.Current;
            if (water == null)
                return;
            Vector3 p = transform.position;
            WaterRipples.AddImpulse(new Vector3(p.x, water.GetHeight(p), p.z), radius * 1.5f, -Mathf.Abs(height), foam);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
