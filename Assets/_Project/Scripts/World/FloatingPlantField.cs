using System.Collections.Generic;
using FishingVR.Water;
using UnityEngine;

namespace FishingVR.World
{
    /// <summary>
    /// Floating objects (lily pads, buoys) pushed aside by the boats instead of showing through the hull.
    /// Put it on the parent of the objects. Each one slides out of any boat collider it touches (convex hull),
    /// keeps the speed it was given and drifts on for a moment (inertia), then drifts slowly back to where it
    /// was placed once the boat has gone. Each object keeps its height relative to the water surface.
    /// </summary>
    public class FloatingPlantField : MonoBehaviour
    {
        [Tooltip("Colliders that push the objects (boat hulls). Empty = every convex MeshCollider / BoxCollider on a Rigidbody in the scene.")]
        [SerializeField] List<Collider> pushers = new List<Collider>();
        [Tooltip("Radius of an object (m): kept this far from the hull.")]
        [SerializeField] float plantRadius = 0.35f;
        [Tooltip("Use each object's own size (renderer bounds) as its radius instead of Plant Radius.")]
        [SerializeField] bool radiusFromBounds;
        [Tooltip("Speed at which a pushed object drifts back home (m/s).")]
        [SerializeField] float returnSpeed = 0.15f;
        [Tooltip("Seconds an object waits after being pushed before drifting back.")]
        [SerializeField] float returnDelay = 3f;

        [Header("Inertia")]
        [Tooltip("How fast the drift slows down once the boat stops pushing (1/s). 3 = almost stopped after 1 s.")]
        [SerializeField] float driftDamping = 3f;
        [Tooltip("Max drift speed given by a push (m/s).")]
        [SerializeField] float maxDriftSpeed = 1.5f;
        [Tooltip("Share of the push speed kept as drift (0..1).")]
        [Range(0f, 1f)] [SerializeField] float driftTransfer = 0.8f;
        [Tooltip("Tilt (degrees per m/s of drift) toward the drift direction: buoys lean when pushed. 0 = never tilt.")]
        [SerializeField] float tiltPerSpeed;
        [SerializeField] float maxTilt = 25f;

        class Item
        {
            public Transform transform;
            public Vector3 home;
            public Quaternion homeRotation;
            public float heightAboveWater;
            public float radius;
            public Vector3 velocity;
            public float lastPushed = -100f;
            public Vector3 tilt; // smoothed tilt axis * angle
        }

        readonly List<Item> items = new List<Item>();

        void Start()
        {
            for (var i = 0; i < transform.childCount; i++)
            {
                var t = transform.GetChild(i);
                var water = WaterSurface.Current != null ? WaterSurface.Current.GetHeight(t.position) : t.position.y;
                var radius = plantRadius;
                if (radiusFromBounds)
                {
                    var rends = t.GetComponentsInChildren<Renderer>();
                    if (rends.Length > 0)
                    {
                        var b = rends[0].bounds;
                        foreach (var r in rends) b.Encapsulate(r.bounds);
                        radius = Mathf.Max(0.05f, Mathf.Max(b.extents.x, b.extents.z));
                    }
                }
                items.Add(new Item
                {
                    transform = t,
                    home = t.position,
                    homeRotation = t.rotation,
                    heightAboveWater = t.position.y - water,
                    radius = radius,
                });
            }

            if (pushers.Count == 0)
            {
                foreach (var body in FindObjectsByType<Rigidbody>())
                foreach (var c in body.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger) continue;
                    if (c is BoxCollider || (c is MeshCollider mc && mc.convex))
                        pushers.Add(c);
                }
            }
        }

        void LateUpdate()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            var now = Time.time;
            var damping = Mathf.Exp(-driftDamping * dt);

            foreach (var item in items)
            {
                var t = item.transform;
                if (t == null) continue;
                var pos = t.position;
                var push = Vector3.zero;

                foreach (var hull in pushers)
                {
                    if (hull == null || !hull.enabled) continue;
                    var b = hull.bounds;
                    b.Expand(item.radius * 2f);
                    if (!b.Contains(new Vector3(pos.x, b.center.y, pos.z))) continue;

                    // Closest point of the hull at the object's height (ClosestPoint returns the point itself when inside).
                    var probe = new Vector3(pos.x, Mathf.Clamp(pos.y, hull.bounds.min.y, hull.bounds.max.y), pos.z);
                    var closest = hull.ClosestPoint(probe);
                    var away = new Vector3(probe.x - closest.x, 0f, probe.z - closest.z);
                    if (away.sqrMagnitude < 1e-6f)
                    {
                        // Inside the hull: push out sideways from the boat's centre line.
                        var ht = hull.transform;
                        var local = ht.InverseTransformPoint(pos);
                        away = ht.right * (local.x >= 0f ? 1f : -1f);
                        away.y = 0f;
                        push += away.normalized * Mathf.Max(0.05f, hull.bounds.extents.magnitude * 0.05f);
                        continue;
                    }
                    var dist = away.magnitude;
                    if (dist < item.radius)
                        push += away / dist * (item.radius - dist);
                }

                if (push.sqrMagnitude > 1e-8f)
                {
                    pos += push;
                    // Keeps the speed of the push: it will drift on once the hull has passed.
                    // A weaker push (the stern grazing it as the hull goes by) does not kill the momentum.
                    var pushVelocity = Vector3.ClampMagnitude(push / dt * driftTransfer, maxDriftSpeed);
                    if (pushVelocity.sqrMagnitude >= item.velocity.sqrMagnitude)
                        item.velocity = Vector3.Lerp(item.velocity, pushVelocity, 0.5f);
                    else
                        item.velocity = item.velocity * damping + pushVelocity * (1f - damping);
                    item.lastPushed = now;
                }
                else
                {
                    // Drift (inertia), slowing down.
                    pos += item.velocity * dt;
                    item.velocity *= damping;
                    if (item.velocity.sqrMagnitude < 1e-4f) item.velocity = Vector3.zero;
                    if (now - item.lastPushed > returnDelay)
                        pos = Vector3.MoveTowards(pos, new Vector3(item.home.x, pos.y, item.home.z), returnSpeed * dt);
                }

                // Stays on the water surface, at its own height.
                if (WaterSurface.Current != null) pos.y = WaterSurface.Current.GetHeight(pos) + item.heightAboveWater;

                if (tiltPerSpeed > 0f)
                {
                    // Leans toward its drift direction, rights itself as it slows down.
                    var v = item.velocity;
                    var target = Vector3.Cross(Vector3.up, new Vector3(v.x, 0f, v.z)).normalized
                                 * Mathf.Min(maxTilt, new Vector2(v.x, v.z).magnitude * tiltPerSpeed);
                    item.tilt = Vector3.Lerp(item.tilt, target, 1f - Mathf.Exp(-6f * dt));
                    var angle = item.tilt.magnitude;
                    var rot = angle > 0.01f ? Quaternion.AngleAxis(angle, item.tilt / angle) * item.homeRotation : item.homeRotation;
                    t.SetPositionAndRotation(pos, rot);
                }
                else t.position = pos;
            }
        }
    }
}
