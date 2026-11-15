using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishingVR.World
{
    /// <summary>
    /// Instances painted in "Instanced" mode (grass, small plants), stored outside the scene so
    /// the scene file stays small. Per prefab: packed floats, 8 per instance
    /// (position xyz, rotation xyzw, uniform scale). Float arrays serialize compactly.
    /// </summary>
    [CreateAssetMenu(menuName = "FishingVR/Vegetation Instance Data", fileName = "VD_Scene")]
    public class VegetationInstanceData : ScriptableObject
    {
        public const int Stride = 8;

        [Serializable]
        public class Layer
        {
            public GameObject prefab;
            public float[] instances = Array.Empty<float>();

            public int Count => instances.Length / Stride;

            public Vector3 Position(int i) => new Vector3(instances[i * Stride], instances[i * Stride + 1], instances[i * Stride + 2]);

            public Matrix4x4 Matrix(int i)
            {
                var o = i * Stride;
                var rot = new Quaternion(instances[o + 3], instances[o + 4], instances[o + 5], instances[o + 6]);
                return Matrix4x4.TRS(Position(i), rot, Vector3.one * instances[o + 7]);
            }
        }

        public List<Layer> layers = new List<Layer>();

        public Layer GetOrCreateLayer(GameObject prefab)
        {
            foreach (var l in layers)
                if (l.prefab == prefab) return l;
            var layer = new Layer { prefab = prefab };
            layers.Add(layer);
            return layer;
        }

        public int TotalCount
        {
            get
            {
                var n = 0;
                foreach (var l in layers) n += l.Count;
                return n;
            }
        }
    }
}
