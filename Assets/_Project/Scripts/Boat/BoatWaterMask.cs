using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishingVR.Boat
{
    /// <summary>
    /// Hides the water surface inside the boat. At startup, slices the hull mesh horizontally at
    /// <see cref="maskHeight"/>, takes the outline of that slice and builds a flat "lid" mesh
    /// rendered with the FishingVR/WaterMask shader (depth only, no color).
    ///
    /// Constraints:
    /// - The hull outline seen from above must be convex (true for a rowing boat).
    /// - The mask height must be above the waterline (with margin for rolling) and below the
    ///   lowest point of the gunwale (check the gizmo in the Scene view).
    /// - The hull mesh must be readable in builds (Read/Write enabled in the FBX import settings).
    /// </summary>
    [DisallowMultipleComponent]
    public class BoatWaterMask : MonoBehaviour
    {
        [Tooltip("MeshFilter of the hull (SM_Boat_Hull inside the model).")]
        [SerializeField] MeshFilter hull;

        [Tooltip("Material using the FishingVR/WaterMask shader.")]
        [SerializeField] Material maskMaterial;

        [Tooltip("Height of the lid in this object's local space (m). Above the waterline, below the gunwale.")]
        [SerializeField, Range(0f, 2f)] float maskHeight = 0.5f;

        [Tooltip("Shrinks the lid towards the center (m). Use a small value if water is hidden just outside the hull.")]
        [SerializeField, Range(0f, 0.1f)] float inset = 0f;

        GameObject maskObject;

        void Awake()
        {
            if (hull == null || hull.sharedMesh == null || maskMaterial == null)
            {
                Debug.LogWarning($"[BoatWaterMask] '{name}': assign the hull MeshFilter and the mask material.", this);
                return;
            }

            var outline = ComputeOutline();
            if (outline.Count < 3)
            {
                Debug.LogWarning($"[BoatWaterMask] '{name}': the hull does not cross the mask height ({maskHeight} m).", this);
                return;
            }

            BuildMaskObject(outline);
        }

        void OnDestroy()
        {
            if (maskObject != null)
                Destroy(maskObject.GetComponent<MeshFilter>().sharedMesh);
        }

        /// <summary>Outline (convex, counter-clockwise) of the hull slice, in local XZ coordinates.</summary>
        List<Vector2> ComputeOutline()
        {
            Mesh mesh = hull.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            // Hull vertices expressed in this object's local space.
            Matrix4x4 hullToLocal = transform.worldToLocalMatrix * hull.transform.localToWorldMatrix;
            var local = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                local[i] = hullToLocal.MultiplyPoint3x4(vertices[i]);

            // Intersect every triangle edge with the horizontal plane y = maskHeight.
            var points = new List<Vector2>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                AddEdgeCrossing(local[triangles[t]], local[triangles[t + 1]], points);
                AddEdgeCrossing(local[triangles[t + 1]], local[triangles[t + 2]], points);
                AddEdgeCrossing(local[triangles[t + 2]], local[triangles[t]], points);
            }

            // The slice contains the outer wall, the inner wall and possibly benches:
            // the convex hull keeps only the outermost contour.
            var outline = ConvexHull(points);
            if (inset > 0f && outline.Count >= 3)
                ApplyInset(outline, inset);
            return outline;
        }

        void AddEdgeCrossing(Vector3 a, Vector3 b, List<Vector2> points)
        {
            float da = a.y - maskHeight;
            float db = b.y - maskHeight;
            if (da * db > 0f || Mathf.Approximately(da, db))
                return; // Both ends on the same side of the plane (or edge lying in the plane).

            float t = da / (da - db);
            Vector3 p = Vector3.Lerp(a, b, t);
            points.Add(new Vector2(p.x, p.z));
        }

        /// <summary>Andrew's monotone chain. Returns the hull in counter-clockwise order.</summary>
        static List<Vector2> ConvexHull(List<Vector2> points)
        {
            points.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));
            var hull = new List<Vector2>();
            if (points.Count < 3)
                return hull;

            // Lower hull, then upper hull.
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int k = 0; k < points.Count; k++)
                {
                    Vector2 p = pass == 0 ? points[k] : points[points.Count - 1 - k];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1); // Last point is the first point of the next pass.
            }
            return hull;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        static void ApplyInset(List<Vector2> outline, float distance)
        {
            Vector2 center = Centroid(outline);
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 toCenter = center - outline[i];
                outline[i] += toCenter.normalized * Mathf.Min(distance, toCenter.magnitude);
            }
        }

        static Vector2 Centroid(List<Vector2> outline)
        {
            Vector2 sum = Vector2.zero;
            foreach (var p in outline)
                sum += p;
            return sum / outline.Count;
        }

        void BuildMaskObject(List<Vector2> outline)
        {
            // Triangle fan around the centroid (valid because the outline is convex).
            Vector2 center = Centroid(outline);
            var vertices = new Vector3[outline.Count + 1];
            vertices[0] = new Vector3(center.x, maskHeight, center.y);
            for (int i = 0; i < outline.Count; i++)
                vertices[i + 1] = new Vector3(outline[i].x, maskHeight, outline[i].y);

            var triangles = new int[outline.Count * 3];
            for (int i = 0; i < outline.Count; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = 1 + (i + 1) % outline.Count;
                triangles[i * 3 + 2] = 1 + i;
            }

            var mesh = new Mesh { name = "BoatWaterMask (generated)", vertices = vertices, triangles = triangles };
            mesh.RecalculateBounds();

            maskObject = new GameObject("WaterMask (generated)");
            maskObject.layer = gameObject.layer;
            maskObject.transform.SetParent(transform, false);
            maskObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            var meshRenderer = maskObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = maskMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // Preview of the lid outline in the Scene view (select the boat).
        void OnDrawGizmosSelected()
        {
            if (hull == null || hull.sharedMesh == null || Application.isPlaying)
                return;

            var outline = ComputeOutline();
            Gizmos.color = new Color(1f, 0.3f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i];
                Vector2 b = outline[(i + 1) % outline.Count];
                Gizmos.DrawLine(new Vector3(a.x, maskHeight, a.y), new Vector3(b.x, maskHeight, b.y));
            }
        }
    }
}
