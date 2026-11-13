using UnityEngine;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Flat water surface used by the fishing systems. The surface height is this transform's Y.
    /// Put one in each scene with water (e.g. on the water plane).
    /// </summary>
    public class WaterLevel : MonoBehaviour
    {
        public static WaterLevel Instance { get; private set; }

        public float Height => transform.position.y;

        /// <summary>Water height, or negative infinity when the scene has no water.</summary>
        public static float CurrentHeight => Instance != null ? Instance.Height : float.NegativeInfinity;

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
