using FishingVR.Fishing;
using UnityEngine;

namespace FishingVR.Fish
{
    /// <summary>
    /// A hooked fish hanging from the hook by its mouth: a damped pendulum (verlet) so it
    /// hangs head up, swings when the rod moves, and wriggles a bit.
    /// The fish lives in world space; its pivot is the mouth, glued to the anchor (hook).
    /// </summary>
    [DefaultExecutionOrder(100)] // after FishingLine has placed the hook
    public class HangingFish : MonoBehaviour
    {
        [SerializeField] Transform anchor;
        [Tooltip("Distance from the mouth to the centre of mass (m).")]
        [SerializeField] float pendulumLength = 0.25f;
        [Range(0f, 1f)] [SerializeField] float airDamping = 0.02f;
        [Range(0f, 1f)] [SerializeField] float waterDamping = 0.15f;
        [Tooltip("Random kicks applied to the body (m/s).")]
        [SerializeField] float struggleStrength = 0.6f;
        [SerializeField] Vector2 struggleInterval = new Vector2(0.4f, 1.6f);

        Vector3 body, prevBody;
        Vector3 upRef = Vector3.forward;
        float nextStruggle;

        /// <summary>
        /// Wrap a fish model so it hangs from <paramref name="hookAnchor"/> by its SOCKET_Mouth.
        /// Fish models face +Z (nose) in their prefab space.
        /// </summary>
        public static HangingFish Create(GameObject modelPrefab, Transform hookAnchor)
        {
            var root = new GameObject(modelPrefab.name + " (Hooked)");
            var fish = root.AddComponent<HangingFish>();
            fish.anchor = hookAnchor;

            // Pivot space: origin = mouth, +Z = from the mouth toward the tail.
            var model = Instantiate(modelPrefab, root.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
            var mouth = FindDeep(model.transform, "SOCKET_Mouth");
            if (mouth != null)
                model.transform.localPosition = -root.transform.InverseTransformPoint(mouth.position);

            var renderer = model.GetComponentInChildren<Renderer>();
            if (renderer != null)
                fish.pendulumLength = Mathf.Max(0.05f, renderer.bounds.size.z * 0.5f);

            // Start hanging straight down.
            root.transform.SetPositionAndRotation(hookAnchor.position, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            fish.body = fish.prevBody = hookAnchor.position + Vector3.down * fish.pendulumLength;
            return fish;
        }

        void LateUpdate()
        {
            if (anchor == null)
            {
                Destroy(gameObject);
                return;
            }

            var dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            var pivot = anchor.position;
            var inWater = body.y < WaterLevel.CurrentHeight;
            var damping = inWater ? waterDamping : airDamping;

            // Verlet integration of the centre of mass, then length constraint.
            var velocity = (body - prevBody) * (1f - damping);
            prevBody = body;
            body += velocity + Physics.gravity * (inWater ? 0.15f : 1f) * dt * dt;

            if (Time.time >= nextStruggle)
            {
                nextStruggle = Time.time + Random.Range(struggleInterval.x, struggleInterval.y);
                var kick = Vector3.ProjectOnPlane(Random.onUnitSphere, Vector3.up).normalized * struggleStrength;
                prevBody -= kick * dt;
            }

            var dir = body - pivot;
            if (dir.sqrMagnitude < 0.000001f)
                dir = Vector3.down;
            body = pivot + dir.normalized * pendulumLength;

            // Keep a stable "up" (back of the fish) to avoid flips.
            var forward = (body - pivot).normalized;
            upRef = Vector3.ProjectOnPlane(upRef, forward);
            if (upRef.sqrMagnitude < 0.0001f)
                upRef = Vector3.ProjectOnPlane(Vector3.forward, forward);
            upRef.Normalize();

            transform.SetPositionAndRotation(pivot, Quaternion.LookRotation(forward, upRef));
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
