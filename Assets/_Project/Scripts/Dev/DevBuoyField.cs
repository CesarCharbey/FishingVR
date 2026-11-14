using FishingVR.Water;
using UnityEngine;

namespace FishingVR.Dev
{
    /// <summary>
    /// Dev-only visual references on the water: spawns a grid of buoys at startup so that the
    /// boat's speed and turns can be judged (a buoy every <see cref="spacing"/> meters, a taller
    /// marker every <see cref="markerEvery"/> buoys). Nothing is saved in the scene: the buoys
    /// exist only in Play mode. The grid is previewed with gizmos in the Scene view.
    /// </summary>
    public class DevBuoyField : MonoBehaviour
    {
        [Tooltip("Small buoy placed at every grid point (e.g. SM_Buoy_Ball).")]
        [SerializeField] GameObject buoyPrefab;

        [Tooltip("Taller buoy placed every 'Marker Every' points (e.g. SM_Buoy_Marker).")]
        [SerializeField] GameObject markerPrefab;

        [Tooltip("Distance between two buoys (m).")]
        [SerializeField, Min(2f)] float spacing = 10f;

        [Tooltip("Number of buoys from the center to the edge, in each direction.")]
        [SerializeField, Range(1, 20)] int halfCount = 6;

        [Tooltip("A marker replaces the buoy every N grid points (e.g. 5 x 10 m = every 50 m).")]
        [SerializeField, Min(1)] int markerEvery = 5;

        [Tooltip("Points closer than this to the center are left empty, so the boat does not start on a buoy (m).")]
        [SerializeField, Min(0f)] float clearRadius = 6f;

        void Start()
        {
            if (buoyPrefab == null)
            {
                Debug.LogWarning("[DevBuoyField] Assign at least the buoy prefab.", this);
                return;
            }

            float waterHeight = WaterSurface.Current != null ? WaterSurface.Current.GetHeight(transform.position) : transform.position.y;

            for (int x = -halfCount; x <= halfCount; x++)
            for (int z = -halfCount; z <= halfCount; z++)
            {
                Vector3 position = GridPoint(x, z);
                if (Vector3.Distance(position, transform.position) < clearRadius)
                    continue;

                bool isMarker = markerPrefab != null && x % markerEvery == 0 && z % markerEvery == 0;
                GameObject prefab = isMarker ? markerPrefab : buoyPrefab;
                position.y = waterHeight;

                // Keep the model's own rotation: imported Blender models carry an axis-conversion rotation.
                var buoy = Instantiate(prefab, position, prefab.transform.rotation, transform);
                buoy.name = $"{prefab.name} ({x},{z})";
            }
        }

        Vector3 GridPoint(int x, int z) => transform.position + new Vector3(x * spacing, 0f, z * spacing);

        void OnDrawGizmosSelected()
        {
            for (int x = -halfCount; x <= halfCount; x++)
            for (int z = -halfCount; z <= halfCount; z++)
            {
                bool isMarker = x % markerEvery == 0 && z % markerEvery == 0;
                Gizmos.color = isMarker ? Color.red : new Color(1f, 0.6f, 0f);
                Gizmos.DrawSphere(GridPoint(x, z), isMarker ? 0.6f : 0.3f);
            }
        }
    }
}
