using System.Collections.Generic;
using FishingVR.Water;
using UnityEngine;

namespace FishingVR.World
{
    /// <summary>
    /// Floating plants (lily pads) pushed aside by the boats instead of showing through the hull.
    /// Put it on the parent of the plants. Each plant slides out of any boat collider it touches
    /// (convex hull), then drifts slowly back to where it was painted once the boat has gone.
    /// </summary>
    public class FloatingPlantField : MonoBehaviour
    {
        [Tooltip("Colliders that push the plants (boat hulls). Empty = every convex MeshCollider / BoxCollider on a Rigidbody in the scene.")]
        [SerializeField] List<Collider> pushers = new List<Collider>();
        [Tooltip("Radius of a plant (m): kept this far from the hull.")]
        [SerializeField] float plantRadius = 0.35f;
        [Tooltip("Speed at which a pushed plant drifts back home (m/s).")]
        [SerializeField] float returnSpeed = 0.15f;
        [Tooltip("Seconds a plant waits after being pushed before drifting back.")]
        [SerializeField] float returnDelay = 3f;

        Transform[] plants;
        Vector3[] home;
        float[] lastPushed;

        void Start()
        {
            plants = new Transform[transform.childCount];
            home = new Vector3[plants.Length];
            lastPushed = new float[plants.Length];
            for (var i = 0; i < plants.Length; i++)
            {
                plants[i] = transform.GetChild(i);
                home[i] = plants[i].position;
                lastPushed[i] = -100f;
            }

            if (pushers.Count == 0)
            {
                foreach (var body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
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
            if (plants == null) return;
            var now = Time.time;
            for (var i = 0; i < plants.Length; i++)
            {
                var plant = plants[i];
                if (plant == null) continue;
                var pos = plant.position;
                var pushed = false;

                foreach (var hull in pushers)
                {
                    if (hull == null || !hull.enabled) continue;
                    var b = hull.bounds;
                    b.Expand(plantRadius * 2f);
                    if (!b.Contains(new Vector3(pos.x, b.center.y, pos.z))) continue;

                    // Closest point of the hull at the plant's height (ClosestPoint returns the point itself when inside).
                    var probe = new Vector3(pos.x, Mathf.Clamp(pos.y, hull.bounds.min.y, hull.bounds.max.y), pos.z);
                    var closest = hull.ClosestPoint(probe);
                    var away = new Vector3(probe.x - closest.x, 0f, probe.z - closest.z);
                    float dist;
                    if (away.sqrMagnitude < 1e-6f)
                    {
                        // Inside the hull: push out sideways from the boat's centre line.
                        var t = hull.transform;
                        var local = t.InverseTransformPoint(pos);
                        away = t.right * (local.x >= 0f ? 1f : -1f);
                        away.y = 0f;
                        dist = 0f;
                        pos += away.normalized * Mathf.Max(0.05f, hull.bounds.extents.magnitude * 0.05f);
                        pushed = true;
                        continue;
                    }
                    dist = away.magnitude;
                    if (dist < plantRadius)
                    {
                        pos += away / dist * (plantRadius - dist);
                        pushed = true;
                    }
                }

                if (pushed) lastPushed[i] = now;
                else if (now - lastPushed[i] > returnDelay)
                    pos = Vector3.MoveTowards(pos, home[i], returnSpeed * Time.deltaTime);

                // Stay on the water surface.
                if (WaterSurface.Current != null) pos.y = WaterSurface.Current.GetHeight(pos);
                plant.position = pos;
            }
        }
    }
}
