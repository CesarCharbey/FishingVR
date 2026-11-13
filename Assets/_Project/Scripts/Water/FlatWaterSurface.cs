using UnityEngine;

namespace FishingVR.Water
{
    /// <summary>
    /// Placeholder water: a perfectly flat surface at the height of this GameObject.
    /// Put it on the water plane; it will be replaced by a wave-aware surface in phase 3.
    /// </summary>
    public class FlatWaterSurface : WaterSurface
    {
        public override float GetHeight(Vector3 worldPosition) => transform.position.y;
    }
}
