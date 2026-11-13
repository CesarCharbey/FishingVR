using UnityEngine;

namespace FishingVR.Water
{
    /// <summary>
    /// Base class for any water surface the gameplay can query (buoyancy, bobber, fish...).
    /// Concrete implementations: <see cref="FlatWaterSurface"/> for now, a wave-based one later.
    /// The active surface registers itself in <see cref="Current"/> so that prefabs
    /// (which cannot reference scene objects) can find it at runtime.
    /// </summary>
    public abstract class WaterSurface : MonoBehaviour
    {
        /// <summary>The water surface currently active in the scene (null if none).</summary>
        public static WaterSurface Current { get; private set; }

        /// <summary>World-space height (Y) of the water surface at the given position.</summary>
        public abstract float GetHeight(Vector3 worldPosition);

        /// <summary>Signed depth of a point below the surface: positive when submerged.</summary>
        public float GetDepth(Vector3 worldPosition) => GetHeight(worldPosition) - worldPosition.y;

        protected virtual void OnEnable()
        {
            if (Current != null && Current != this)
                Debug.LogWarning($"[WaterSurface] Several water surfaces are active, '{name}' replaces '{Current.name}'.", this);
            Current = this;
        }

        protected virtual void OnDisable()
        {
            if (Current == this)
                Current = null;
        }
    }
}
