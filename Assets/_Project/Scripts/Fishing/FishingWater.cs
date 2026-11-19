using System.Collections.Generic;
using FishingVR.Water;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Water queries for the fishing systems, backed by the scene's active <see cref="WaterSurface"/>
    /// (same water as the boat). Ready for waves: always ask the height at a position.
    /// Also knows the ground under the water (lake bed, shores, rocks) so that the fish stay in the water:
    /// <see cref="BedHeight"/>, <see cref="DepthAt"/>, <see cref="IsSwimmable"/>.
    /// </summary>
    public static class FishingWater
    {
        /// <summary>Layers that make the lake bed / shores (terrain, rocks, docks).</summary>
        public static LayerMask GroundMask = 1 << 0; // Default

        /// <summary>True when the scene has an active water surface.</summary>
        public static bool Exists => WaterSurface.Current != null;

        /// <summary>Water height at this position, or negative infinity when there is no water.</summary>
        public static float HeightAt(Vector3 position) =>
            WaterSurface.Current != null ? WaterSurface.Current.GetHeight(position) : float.NegativeInfinity;

        // ---------- Ground under the water ----------

        const float CellSize = 0.25f;
        const int MaxCachedCells = 60000;
        static readonly Dictionary<Vector2Int, float> BedCache = new Dictionary<Vector2Int, float>();
        static bool hooked;

        /// <summary>
        /// Height of the ground under this position: the lake bed under the water, or the land itself on the
        /// shore (above the water). Negative infinity when there is nothing (open water, bottomless).
        /// Static colliders only (the result is cached on a 25 cm grid).
        /// </summary>
        public static float BedHeight(Vector3 position)
        {
            if (!hooked)
            {
                hooked = true;
                SceneManager.sceneLoaded += (_, _) => BedCache.Clear();
                SceneManager.sceneUnloaded += _ => BedCache.Clear();
            }
            var cell = new Vector2Int(Mathf.FloorToInt(position.x / CellSize), Mathf.FloorToInt(position.z / CellSize));
            if (BedCache.TryGetValue(cell, out var cached)) return cached;
            if (BedCache.Count > MaxCachedCells) BedCache.Clear();

            var x = (cell.x + 0.5f) * CellSize;
            var z = (cell.y + 0.5f) * CellSize;
            var water = HeightAt(position);
            if (float.IsNegativeInfinity(water)) water = position.y;
            float bed;
            // From just under the surface, down: the lake bed (docks / boats above the water are not seen).
            if (Physics.Raycast(new Vector3(x, water - 0.02f, z), Vector3.down, out var hit, 200f, GroundMask, QueryTriggerInteraction.Ignore))
                bed = hit.point.y;
            // Nothing below: either the ground is above the water here (shore: we started inside it), or open water.
            else if (Physics.Raycast(new Vector3(x, water + 60f, z), Vector3.down, out hit, 60.5f, GroundMask, QueryTriggerInteraction.Ignore))
                bed = hit.point.y;
            else
                bed = float.NegativeInfinity;
            BedCache[cell] = bed;
            return bed;
        }

        /// <summary>Water depth here (surface to lake bed): 0 on land, +infinity in bottomless water.</summary>
        public static float DepthAt(Vector3 position)
        {
            var water = HeightAt(position);
            if (float.IsNegativeInfinity(water)) return 0f;
            return Mathf.Max(0f, water - BedHeight(position));
        }

        /// <summary>Is the water at least <paramref name="minDepth"/> deep here?</summary>
        public static bool IsSwimmable(Vector3 position, float minDepth) => DepthAt(position) >= minDepth;

        /// <summary>
        /// Shallowest water depth along the straight path from <paramref name="from"/> to <paramref name="to"/>
        /// (sampled every <paramref name="step"/> m).
        /// </summary>
        public static float MinDepthAlong(Vector3 from, Vector3 to, float step = 0.5f)
        {
            var flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
            var length = flat.magnitude;
            var n = Mathf.Max(1, Mathf.CeilToInt(length / step));
            var min = float.PositiveInfinity;
            for (var i = 0; i <= n; i++)
                min = Mathf.Min(min, DepthAt(from + flat * (i / (float)n)));
            return min;
        }

        /// <summary>
        /// Distance (m) one can swim from <paramref name="from"/> in <paramref name="direction"/> before the
        /// water gets shallower than <paramref name="minDepth"/>, up to <paramref name="maxDistance"/>.
        /// </summary>
        public static float SwimmableDistance(Vector3 from, Vector3 direction, float maxDistance, float minDepth, float step = 0.5f)
        {
            var dir = new Vector3(direction.x, 0f, direction.z);
            if (dir.sqrMagnitude < 1e-6f) return 0f;
            dir.Normalize();
            for (var d = step; d <= maxDistance + 1e-3f; d += step)
                if (DepthAt(from + dir * d) < minDepth) return d - step;
            return maxDistance;
        }

        /// <summary>
        /// Keeps a height under water and above the bed: <paramref name="y"/> clamped between
        /// bed + <paramref name="bedClearance"/> and surface - <paramref name="surfaceMargin"/>
        /// (the bed wins in very shallow water).
        /// </summary>
        public static float ClampUnderwater(Vector3 position, float y, float bedClearance, float surfaceMargin)
        {
            var water = HeightAt(position);
            if (!float.IsNegativeInfinity(water)) y = Mathf.Min(y, water - surfaceMargin);
            var bed = BedHeight(position);
            if (!float.IsNegativeInfinity(bed)) y = Mathf.Max(y, bed + bedClearance);
            return y;
        }

        /// <summary>
        /// Brings <paramref name="point"/> back toward <paramref name="safe"/> (horizontally) until the water
        /// there is at least <paramref name="minDepth"/> deep. Returns <paramref name="safe"/> if no point is.
        /// </summary>
        public static Vector3 PullTowardsWater(Vector3 point, Vector3 safe, float minDepth, int steps = 8)
        {
            if (IsSwimmable(point, minDepth)) return point;
            for (var i = 1; i <= steps; i++)
            {
                var p = Vector3.Lerp(point, safe, i / (float)steps);
                p.y = point.y;
                if (IsSwimmable(p, minDepth)) return p;
            }
            return new Vector3(safe.x, point.y, safe.z);
        }
    }
}
