using System.Collections.Generic;
using FishingVR.FX;
using FishingVR.Water;
using UnityEngine;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Bubbles of a fishing spot: little clusters rise from under the surface and pop when they reach it,
    /// leaving a small ring (and a dot of foam) on the water. More of them the stronger the spot.
    /// Uses the shared water particles (WaterFX) and the interactive ripples (WaterRipples).
    /// </summary>
    [RequireComponent(typeof(FishingSpot))]
    public class FishingSpotBubbles : MonoBehaviour
    {
        [Tooltip("Bubble clusters per minute at full intensity, for the whole spot.")]
        [SerializeField, Min(0f)] float clustersPerMinute = 45f;

        [Tooltip("Bubbles in a cluster.")]
        [MinMax] [SerializeField] Vector2Int bubblesPerCluster = new(4, 9);

        [Tooltip("Depth (m) the clusters start from (kept above the lake bed).")]
        [MinMax] [SerializeField] Vector2 startDepth = new(0.6f, 1.4f);

        [Tooltip("Rising speed of the bubbles (m/s).")]
        [SerializeField, Min(0.05f)] float riseSpeed = 0.35f;

        [Tooltip("Size of the bubbles (WaterFX scale, 1 = the fish's bubbles).")]
        [SerializeField, Min(0.1f)] float bubbleSize = 2f;

        [Header("Pop at the surface")]
        [Tooltip("Size of the ring left on the water (m).")]
        [MinMax] [SerializeField] Vector2 ringSize = new(0.15f, 0.35f);

        [Tooltip("Water pushed by a pop in the interactive ripples (m). Small: a dimple, not a splash.")]
        [SerializeField, Min(0f)] float popRipple = 0.006f;

        FishingSpot spot;
        float accumulator;
        readonly List<(float time, Vector3 position, float size)> pops = new();

        void Awake() => spot = GetComponent<FishingSpot>();

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            // Random emission: on average clustersPerMinute * intensity per minute, at irregular moments.
            accumulator += clustersPerMinute / 60f * spot.Intensity * dt * Random.Range(0f, 2f);
            while (accumulator >= 1f)
            {
                accumulator -= 1f;
                EmitCluster();
            }

            for (int i = pops.Count - 1; i >= 0; i--)
            {
                if (Time.time < pops[i].time)
                    continue;
                Pop(pops[i].position, pops[i].size);
                pops.RemoveAt(i);
            }
        }

        void EmitCluster()
        {
            Vector3 surface = spot.RandomPointInside();
            float water = FishingWater.HeightAt(surface);
            if (float.IsNegativeInfinity(water))
                return;
            surface.y = water;

            // Start under the surface, never in the lake bed.
            float depth = Random.Range(startDepth.x, startDepth.y);
            float available = FishingWater.DepthAt(surface) - 0.1f;
            if (available < 0.2f)
                return; // Too shallow here.
            depth = Mathf.Min(depth, available);

            Vector3 start = surface + Vector3.down * depth;
            WaterFX.Bubbles(start, Random.Range(bubblesPerCluster.x, bubblesPerCluster.y + 1), bubbleSize);
            pops.Add((Time.time + depth / riseSpeed, surface, Random.Range(ringSize.x, ringSize.y)));
        }

        void Pop(Vector3 surface, float size)
        {
            WaterFX.Ripple(surface, size, 0.8f);
            if (Random.value < 0.5f)
                WaterFX.Foam(surface, size * 0.35f, 0.6f);
            WaterRipples.AddImpulse(surface, size * 0.5f, -popRipple);
        }
    }
}
