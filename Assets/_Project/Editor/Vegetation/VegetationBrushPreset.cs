using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishingVR.EditorTools
{
    /// <summary>A vegetation brush: what to paint, how dense, and where it is allowed.</summary>
    [CreateAssetMenu(menuName = "FishingVR/Vegetation Brush Preset", fileName = "VB_NewBrush")]
    public class VegetationBrushPreset : ScriptableObject
    {
        public enum PlacementMode
        {
            /// <summary>One GameObject per instance (trees, rocks, props: editable one by one).</summary>
            GameObjects,
            /// <summary>GPU instanced, no GameObjects (grass, small plants: thousands of instances).</summary>
            Instanced,
        }

        [Serializable]
        public class Entry
        {
            public GameObject prefab;
            [Min(0f)] public float weight = 1f;
            public Vector2 scale = new Vector2(0.8f, 1.2f);
            [Tooltip("0 = always vertical, 1 = follows the ground slope.")]
            [Range(0f, 1f)] public float alignToNormal;
            [Tooltip("Pushed into the ground (m), avoids floating trunks on slopes.")]
            public float sink;
        }

        public PlacementMode mode = PlacementMode.GameObjects;
        public List<Entry> entries = new List<Entry>();

        [Header("Density")]
        [Tooltip("Instances per m² at strength 1.")]
        [Min(0f)] public float density = 0.01f;
        [Tooltip("Minimum distance between two instances of this brush (m).")]
        [Min(0f)] public float minSpacing = 3f;

        [Header("Where")]
        [Tooltip("Allowed height above the water level (m).")]
        public Vector2 heightAboveWater = new Vector2(0.5f, 1000f);
        [Range(0f, 90f)] public float maxSlope = 35f;
        [Tooltip("Place on the water surface (lily pads) where the water is at least Min Water Depth deep.")]
        public bool placeOnWaterSurface;
        public float minWaterDepth = 0.3f;
        public bool randomYaw = true;

        public enum ColliderMode
        {
            /// <summary>No collision (grass, bushes, shore plants).</summary>
            None,
            /// <summary>Upright capsule around the trunk: blocks the player, branches stay passable.</summary>
            Trunk,
            /// <summary>Exact mesh collider (rocks, props).</summary>
            Mesh,
        }

        [Header("Physics")]
        [Tooltip("GameObjects mode: collider added to each painted instance.")]
        public ColliderMode collider = ColliderMode.None;
        [Tooltip("GameObjects mode: mark instances static (faster). Turn off for things that move (lily pads pushed by boats).")]
        public bool markStatic = true;

        [Header("Organisation")]
        [Tooltip("GameObjects mode: parent group under --- MAP ---/Vegetation.")]
        public string groupName = "Vegetation";
        public Color brushColor = new Color(0.3f, 0.9f, 0.3f);

        public Entry PickEntry(System.Random rng)
        {
            var total = 0f;
            foreach (var e in entries) if (e.prefab != null) total += e.weight;
            var pick = (float)rng.NextDouble() * total;
            foreach (var e in entries)
            {
                if (e.prefab == null) continue;
                pick -= e.weight;
                if (pick <= 0f) return e;
            }
            return null;
        }

        public bool Contains(GameObject prefab)
        {
            foreach (var e in entries) if (e.prefab == prefab) return true;
            return false;
        }
    }
}
