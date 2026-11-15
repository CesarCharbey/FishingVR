using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishingVR.World
{
    /// <summary>
    /// Draws <see cref="VegetationInstanceData"/> with GPU instancing (no GameObject per instance).
    /// Instances are grouped in square chunks; each camera only draws the chunks inside its
    /// frustum and within <see cref="drawDistance"/>. Works in edit mode and in VR (culling matrix
    /// covers both eyes).
    /// </summary>
    [ExecuteAlways]
    public class InstancedVegetation : MonoBehaviour
    {
        const int MaxPerDraw = 1023;

        [SerializeField] VegetationInstanceData data;
        [Tooltip("Instances further than this from the camera are not drawn (m).")]
        [SerializeField] float drawDistance = 70f;
        [SerializeField] float chunkSize = 25f;
        [SerializeField] bool castShadows;

        class Chunk
        {
            public Bounds bounds;
            public Matrix4x4[] matrices;
            public int count;
        }

        class Batch
        {
            public Mesh mesh;
            public int submesh;
            public Material material;
            public readonly List<Chunk> chunks = new List<Chunk>();
        }

        readonly List<Batch> batches = new List<Batch>();
        readonly Plane[] planes = new Plane[6];

        public VegetationInstanceData Data
        {
            get => data;
            set { data = value; Rebuild(); }
        }

        void OnEnable()
        {
            Rebuild();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        void OnValidate() => Rebuild();

        /// <summary>Rebuild the chunk lists from the data (call after painting).</summary>
        public void Rebuild()
        {
            batches.Clear();
            if (data == null) return;

            foreach (var layer in data.layers)
            {
                if (layer.prefab == null || layer.Count == 0) continue;

                // Each renderer of the prefab is a batch; its matrix in the prefab asset (root at the origin)
                // is applied after the instance transform, exactly like an instantiated prefab.
                foreach (var mf in layer.prefab.GetComponentsInChildren<MeshFilter>())
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    var local = mf.transform.localToWorldMatrix;
                    for (var sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        var mat = mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)];
                        if (mat == null) continue;
                        if (!mat.enableInstancing) mat.enableInstancing = true;
                        batches.Add(BuildBatch(layer, mf.sharedMesh, sub, mat, local));
                    }
                }
            }
        }

        Batch BuildBatch(VegetationInstanceData.Layer layer, Mesh mesh, int submesh, Material mat, Matrix4x4 local)
        {
            var batch = new Batch { mesh = mesh, submesh = submesh, material = mat };
            var byCell = new Dictionary<Vector2Int, List<Matrix4x4>>();
            for (var i = 0; i < layer.Count; i++)
            {
                var p = layer.Position(i);
                var cell = new Vector2Int(Mathf.FloorToInt(p.x / chunkSize), Mathf.FloorToInt(p.z / chunkSize));
                if (!byCell.TryGetValue(cell, out var list)) byCell[cell] = list = new List<Matrix4x4>();
                list.Add(layer.Matrix(i) * local);
            }

            var meshBounds = mesh.bounds;
            foreach (var kv in byCell)
            {
                var chunk = new Chunk { matrices = kv.Value.ToArray(), count = kv.Value.Count };
                var b = new Bounds(chunk.matrices[0].MultiplyPoint3x4(meshBounds.center), Vector3.zero);
                foreach (var m in chunk.matrices)
                    b.Encapsulate(new Bounds(m.MultiplyPoint3x4(meshBounds.center), meshBounds.size * m.lossyScale.x * 1.5f));
                chunk.bounds = b;
                batch.chunks.Add(chunk);
            }
            return batch;
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (batches.Count == 0 || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection)
                return;

            GeometryUtility.CalculateFrustumPlanes(cam.cullingMatrix, planes);
            var camPos = cam.transform.position;
            var maxSqr = drawDistance * drawDistance;

            foreach (var batch in batches)
            {
                var rp = new RenderParams(batch.material)
                {
                    camera = cam,
                    layer = gameObject.layer,
                    shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                };
                foreach (var chunk in batch.chunks)
                {
                    if (chunk.bounds.SqrDistance(camPos) > maxSqr) continue;
                    if (!GeometryUtility.TestPlanesAABB(planes, chunk.bounds)) continue;
                    rp.worldBounds = chunk.bounds;
                    for (var start = 0; start < chunk.count; start += MaxPerDraw)
                        Graphics.RenderMeshInstanced(rp, batch.mesh, batch.submesh, chunk.matrices, Mathf.Min(MaxPerDraw, chunk.count - start), start);
                }
            }
        }
    }
}
