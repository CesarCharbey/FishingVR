using FishingVR.Water;
using UnityEngine;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Water queries for the fishing systems, backed by the scene's active <see cref="WaterSurface"/>
    /// (same water as the boat). Ready for waves: always ask the height at a position.
    /// </summary>
    public static class FishingWater
    {
        /// <summary>True when the scene has an active water surface.</summary>
        public static bool Exists => WaterSurface.Current != null;

        /// <summary>Water height at this position, or negative infinity when there is no water.</summary>
        public static float HeightAt(Vector3 position) =>
            WaterSurface.Current != null ? WaterSurface.Current.GetHeight(position) : float.NegativeInfinity;
    }
}
