using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishingVR.Water
{
    /// <summary>
    /// Contact foam (the lake's white band hugging everything that pokes out of the water) for an object that
    /// moves: buoy, boat, bobber. Fixed objects (rocks, dock posts) are baked instead
    /// (FishingVR > Eau > Calculer l'écume de contact).
    ///
    /// The band starts at the object's real waterline, measured once in the editor (right-click on the component >
    /// Measure Waterline, or FishingVR > Eau > Ajouter l'écume de contact aux objets mobiles): a circle for round
    /// objects (buoys), the outline of the slice for the others (hull, pointed at the bow). Like for the rocks,
    /// the band is narrower around small objects (see Contact Foam Size on the lake material).
    /// It shows while the object sits at the surface and fades out when it leaves it (lifted out of the water,
    /// pulled under). The nearest objects to the camera are drawn (up to 16).
    /// </summary>
    [ExecuteAlways] // the band also shows in the Scene view
    [DisallowMultipleComponent]
    public class WaterContactFoam : MonoBehaviour
    {
        public enum Shape { Circle, Outline }

        [Tooltip("Circle: round object (buoy, bobber). Outline: the exact slice at the waterline (hull).")]
        [SerializeField] Shape shape = Shape.Circle;
        [Tooltip("Width of the band, relative to what the lake gives an object of this size. 1 = like a rock of the same size.")]
        [SerializeField, Range(0f, 2f)] float widthScale = 1f;
        [Tooltip("The band fades out when the object is this far out of the water or under it (m).")]
        [SerializeField, Min(0.01f)] float surfaceTolerance = 0.1f;

        [Header("Waterline (measured: Measure Waterline in the component menu)")]
        [Tooltip("Mesh sliced to measure the waterline (the hull of a boat, without the motor). Empty = every mesh below.")]
        [SerializeField] MeshFilter waterlineMesh;
        [Tooltip("Parts of the object up to this height above the water count as well (the belly of a ball buoy, a flared hull): the band starts at their edge seen from above, not hidden under them.")]
        [SerializeField, Min(0f)] float overhangHeight = 0.6f;
        [Tooltip("Centre of the waterline (m): X right, Z ahead of the object, Y = height of the water below its pivot.")]
        [SerializeField] Vector3 waterlineCentre;
        [Tooltip("Circle: radius of the waterline (m). 0 = not measured: half the width of the renderers.")]
        [SerializeField, Min(0f)] float waterlineRadius;
        [Tooltip("Outline: points of the waterline (m, X right, Y ahead of the object), convex, in order.")]
        [SerializeField] Vector2[] waterlineOutline = Array.Empty<Vector2>();
        [Tooltip("Size of the object at the waterline (m): sets the width of the band, like for the rocks.")]
        [SerializeField, Min(0f)] float waterlineSize;

        // Must match MAX_CONTACT_SHAPES / MAX_CONTACT_SEGMENTS in LakeWater.shader.
        public const int MaxShapes = 16;
        public const int MaxSegments = 64;
        public const int MaxOutlinePoints = 20;

        static readonly List<WaterContactFoam> all = new List<WaterContactFoam>();
        static readonly List<(float distance, WaterContactFoam foam)> candidates = new List<(float, WaterContactFoam)>();
        static readonly Vector4[] shapesA = new Vector4[MaxShapes];
        static readonly Vector4[] shapesB = new Vector4[MaxShapes];
        static readonly Vector4[] segments = new Vector4[MaxSegments];
        static readonly int ShapesAId = Shader.PropertyToID("_ContactShapesA");
        static readonly int ShapesBId = Shader.PropertyToID("_ContactShapesB");
        static readonly int SegmentsId = Shader.PropertyToID("_ContactSegments");
        static readonly int ShapeCountId = Shader.PropertyToID("_ContactShapeCount");
        static int publishedFrame = -1;

        Renderer[] renderers;
        float fallbackRadius; // half the width of the renderers, when the waterline was never measured

        /// <summary>How much of the band shows (0 out of the water or sunk, 1 at the surface).</summary>
        public float Strength { get; private set; }

        public static IReadOnlyList<WaterContactFoam> All => all;
        public Shape CurrentShape => shape;
        public float WaterlineSize => waterlineSize > 0f ? waterlineSize : fallbackRadius;
        public int OutlinePointCount => waterlineOutline != null ? waterlineOutline.Length : 0;
        public float WaterlineRadius => waterlineRadius;
        /// <summary>Flat direction the object faces (the frame its waterline is stored in).</summary>
        public Vector3 HeadingDirection => Heading;

        bool UsesOutline => shape == Shape.Outline && waterlineOutline != null && waterlineOutline.Length >= 3;

        void OnEnable()
        {
            renderers = GetComponentsInChildren<Renderer>();
            fallbackRadius = RendererRadius();
            all.Add(this);
        }

        void OnDisable()
        {
            all.Remove(this);
            if (all.Count == 0)
                Shader.SetGlobalFloat(ShapeCountId, 0f);
        }

        void LateUpdate()
        {
            if (publishedFrame == Time.frameCount)
                return;
            publishedFrame = Time.frameCount;
            var cam = Camera.main;
            Publish(cam != null ? cam.transform.position : transform.position);
        }

        // ---- Measure (editor) -------------------------------------------------------------------------------------

        /// <summary>
        /// Slices the object's meshes at the water's height (where it sits now) and keeps that waterline. Needs
        /// readable meshes (always the case in the editor).
        /// </summary>
        [ContextMenu("Measure Waterline")]
        public bool MeasureWaterline()
        {
            var water = WaterSurface.Current != null ? WaterSurface.Current : FindAnyObjectByType<WaterSurface>();
            float waterY = water != null ? water.GetHeight(transform.position) : 0f;

            var slice = new List<Vector4>();
            var points = new List<Vector2>(); // flat frame of the object (ToFrame)
            foreach (var filter in waterlineMesh != null ? new[] { waterlineMesh } : GetComponentsInChildren<MeshFilter>())
            {
                var meshRenderer = filter.GetComponent<MeshRenderer>();
                if (meshRenderer == null || !meshRenderer.enabled || filter.sharedMesh == null || filter.name.Contains("(generated)"))
                    continue;
                var toWorld = filter.transform.localToWorldMatrix;
                Waterline(filter.sharedMesh, toWorld, waterY, slice);
                // What overhangs the waterline, seen from above (it hides the start of the band).
                foreach (var v in filter.sharedMesh.vertices)
                {
                    var w = toWorld.MultiplyPoint3x4(v);
                    if (w.y > waterY && w.y < waterY + overhangHeight)
                        points.Add(ToFrame(w));
                }
            }
            if (slice.Count == 0)
            {
                Debug.LogWarning($"[WaterContactFoam] '{name}' does not cross the water (y = {waterY:0.00}): nothing measured.", this);
                return false;
            }

            // The waterline and the overhang: their convex outline seen from above, simplified.
            foreach (var s in slice)
            {
                points.Add(ToFrame(new Vector3(s.x, waterY, s.y)));
                points.Add(ToFrame(new Vector3(s.z, waterY, s.w)));
            }
            var hull = ConvexHull(points);
            Simplify(hull, MaxOutlinePoints);

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "Measure Waterline");
#endif
            Vector2 centre = Vector2.zero;
            foreach (var p in hull)
                centre += p;
            centre /= hull.Count;
            float radius = 0f;
            foreach (var p in hull)
                radius = Mathf.Max(radius, Vector2.Distance(p, centre));

            waterlineCentre = new Vector3(centre.x, waterY - transform.position.y, centre.y);
            waterlineRadius = radius;
            waterlineOutline = hull.ToArray();
            // Size from the outline (2 x area / perimeter): a hull is a thin shell, its slice has an outer and an
            // inner loop whose areas cancel out.
            double area = 0.0, perimeter = 0.0;
            for (int i = 0; i < hull.Count; i++)
            {
                var a = hull[i];
                var b = hull[(i + 1) % hull.Count];
                area += (double)a.x * b.y - (double)b.x * a.y;
                perimeter += Vector2.Distance(a, b);
            }
            waterlineSize = perimeter > 1e-6 ? (float)(Math.Abs(area) / perimeter) : radius;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            return true;
        }

        // The shape is kept in a flat frame that follows the object's heading (metres, X = right, Y = ahead): models
        // imported from Blender are turned on their side (local Z up), their local XZ plane is not horizontal.
        Vector3 Heading
        {
            get
            {
                foreach (var axis in new[] { transform.forward, transform.up, transform.right })
                {
                    var flat = new Vector3(axis.x, 0f, axis.z);
                    if (flat.sqrMagnitude > 0.04f)
                        return flat.normalized;
                }
                return Vector3.forward;
            }
        }

        Vector2 ToFrame(Vector3 world)
        {
            var heading = Heading;
            var right = new Vector3(heading.z, 0f, -heading.x);
            var d = world - transform.position;
            return new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, heading));
        }

        Vector3 FromFrame(Vector2 p, Vector3 heading)
        {
            var right = new Vector3(heading.z, 0f, -heading.x);
            return transform.position + right * p.x + heading * p.y;
        }

        // Half the width of the renderers seen from above: only used until the waterline is measured.
        float RendererRadius()
        {
            float r = 0f;
            foreach (var rend in GetComponentsInChildren<Renderer>())
            {
                if (rend is ParticleSystemRenderer || rend is TrailRenderer || rend is LineRenderer)
                    continue;
                var b = rend.bounds;
                r = Mathf.Max(r, Mathf.Max(b.extents.x, b.extents.z));
            }
            return r > 0f ? r : 0.1f;
        }

        // ---- Shared helpers (also used by the lake bake for the fixed objects) ----------------------------------

        /// <summary>
        /// Width of the band for an object of this size at the waterline (m), relative to the full width: thin posts,
        /// buoys and pebbles get a narrower band than big rocks. Same formula as in LakeWater.shader.
        /// </summary>
        public static float BandScale(float size, float fullWidthSize, float smallest)
        {
            return Mathf.Clamp(Mathf.Sqrt(Mathf.Max(size, 0f) / Mathf.Max(fullWidthSize, 0.01f)), smallest, 1f);
        }

        /// <summary>
        /// Intersection of the mesh with the plane y = planeY, as 2D segments (x, z), each turned so that the
        /// object is on the same side (the areas of all the loops then add up).
        /// </summary>
        public static void Waterline(Mesh mesh, Matrix4x4 toWorld, float planeY, List<Vector4> segmentsOut)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var world = new Vector3[vertices.Length];
            for (var k = 0; k < vertices.Length; k++)
                world[k] = toWorld.MultiplyPoint3x4(vertices[k]);

            Span<Vector2> points = stackalloc Vector2[3];
            for (var k = 0; k + 2 < triangles.Length; k += 3)
            {
                var a = world[triangles[k]];
                var b = world[triangles[k + 1]];
                var c = world[triangles[k + 2]];
                var n = 0;
                Cross(a, b, planeY, points, ref n);
                Cross(b, c, planeY, points, ref n);
                Cross(c, a, planeY, points, ref n);
                if (n < 2)
                    continue;
                // Outward normal of the face (Unity winding), seen from above: keep it on the right of the segment.
                var normal = Vector3.Cross(b - a, c - a);
                var d = points[1] - points[0];
                if (d.y * normal.x - d.x * normal.z < 0f)
                    segmentsOut.Add(new Vector4(points[1].x, points[1].y, points[0].x, points[0].y));
                else
                    segmentsOut.Add(new Vector4(points[0].x, points[0].y, points[1].x, points[1].y));
            }
        }

        static void Cross(Vector3 a, Vector3 b, float y, Span<Vector2> points, ref int n)
        {
            var da = a.y - y;
            var db = b.y - y;
            if ((da >= 0f) == (db >= 0f) || n >= 3)
                return;
            var p = Vector3.Lerp(a, b, da / (da - db));
            points[n++] = new Vector2(p.x, p.z);
        }

        /// <summary>
        /// Size of a waterline (m): 2 x area / perimeter. The radius for a round post or a rock, the half width for a
        /// long hull or a log; the same for one post or a dock with ten of them.
        /// </summary>
        public static float EffectiveRadius(List<Vector4> slice)
        {
            double area = 0.0, perimeter = 0.0;
            foreach (var s in slice)
            {
                area += (double)s.x * s.w - (double)s.z * s.y;
                perimeter += Math.Sqrt((s.z - s.x) * (s.z - s.x) + (s.w - s.y) * (s.w - s.y));
            }
            return perimeter > 1e-6 ? (float)(Math.Abs(area) / perimeter) : 0f; // |shoelace sum| / 2 x 2 / P
        }

        static List<Vector2> ConvexHull(List<Vector2> points)
        {
            points.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));
            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < points.Count; i++)
                {
                    var p = pass == 0 ? points[i] : points[points.Count - 1 - i];
                    while (hull.Count >= start + 2 && Turn(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        static float Turn(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        // Drops the points that change the outline least until there are at most maxPoints.
        static void Simplify(List<Vector2> outline, int maxPoints)
        {
            while (outline.Count > maxPoints)
            {
                int best = 0;
                float bestArea = float.MaxValue;
                for (int i = 0; i < outline.Count; i++)
                {
                    var prev = outline[(i + outline.Count - 1) % outline.Count];
                    var next = outline[(i + 1) % outline.Count];
                    float area = Mathf.Abs(Turn(prev, outline[i], next));
                    if (area < bestArea)
                    {
                        bestArea = area;
                        best = i;
                    }
                }
                outline.RemoveAt(best);
            }
        }

        // ---- Publish --------------------------------------------------------------------------------------------

        float SurfaceStrength(float waterY)
        {
            float bottom = float.MaxValue, top = float.MinValue;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;
                var b = r.bounds;
                bottom = Mathf.Min(bottom, b.min.y);
                top = Mathf.Max(top, b.max.y);
            }
            if (bottom > top)
                return 0f;
            float gap = Mathf.Max(0f, Mathf.Max(bottom - waterY, waterY - top));
            return 1f - Mathf.Clamp01(gap / surfaceTolerance);
        }

        bool Measured => waterlineRadius > 0f || UsesOutline;

        Vector3 WorldCentre => Measured ? FromFrame(new Vector2(waterlineCentre.x, waterlineCentre.z), Heading) : transform.position;

        /// <summary>
        /// Sends the shapes to the lake shader, nearest to the viewer first. Called once per frame by the components;
        /// edit mode tools (screenshots) call it themselves.
        /// </summary>
        public static void Publish(Vector3 viewer)
        {
            candidates.Clear();
            foreach (var foam in all)
            {
                if (foam == null || !foam.isActiveAndEnabled)
                    continue;
                Vector3 centre = foam.WorldCentre;
                float waterY = WaterSurface.Current != null ? WaterSurface.Current.GetHeight(centre) : 0f;
                foam.Strength = foam.SurfaceStrength(waterY);
                if (foam.Strength <= 0.001f || foam.widthScale <= 0f)
                    continue;
                candidates.Add(((new Vector2(centre.x, centre.z) - new Vector2(viewer.x, viewer.z)).sqrMagnitude, foam));
            }
            candidates.Sort((x, y) => x.distance.CompareTo(y.distance));

            int count = 0, segmentCount = 0;
            foreach (var (_, foam) in candidates)
            {
                if (count == MaxShapes)
                    break;
                Vector3 heading = foam.Heading;
                Vector3 centre = foam.WorldCentre;
                float radius = foam.waterlineRadius > 0f ? foam.waterlineRadius : foam.fallbackRadius;
                int first = segmentCount, edges = 0;
                if (foam.UsesOutline && segmentCount + foam.waterlineOutline.Length <= MaxSegments)
                {
                    // The outline in the world: the band follows the hull as the boat turns.
                    var outline = foam.waterlineOutline;
                    Vector3 previous = foam.FromFrame(outline[outline.Length - 1], heading);
                    radius = 0f;
                    for (int i = 0; i < outline.Length; i++)
                    {
                        Vector3 p = foam.FromFrame(outline[i], heading);
                        segments[segmentCount++] = new Vector4(previous.x, previous.z, p.x, p.z);
                        radius = Mathf.Max(radius, new Vector2(p.x - centre.x, p.z - centre.z).magnitude);
                        previous = p;
                    }
                    edges = outline.Length;
                }
                // xy = centre, z = radius of the circle / around the outline (m), w = band width scale x strength.
                shapesA[count] = new Vector4(centre.x, centre.z, radius, foam.widthScale * foam.Strength);
                // x = first edge, y = edge count (0: circle), z = size of the waterline (m: width of the band).
                shapesB[count] = new Vector4(first, edges, foam.WaterlineSize, 0f);
                count++;
            }
            for (int i = count; i < MaxShapes; i++)
            {
                shapesA[i] = Vector4.zero;
                shapesB[i] = Vector4.zero;
            }
            for (int i = segmentCount; i < MaxSegments; i++)
                segments[i] = Vector4.zero;

            Shader.SetGlobalVectorArray(ShapesAId, shapesA);
            Shader.SetGlobalVectorArray(ShapesBId, shapesB);
            Shader.SetGlobalVectorArray(SegmentsId, segments);
            Shader.SetGlobalFloat(ShapeCountId, count);
        }

        void OnDrawGizmosSelected()
        {
            if (!enabled)
                return;
            Gizmos.color = new Color(1f, 1f, 1f, 0.8f);
            // Drawn at the height of the waterline measured on the object.
            Vector3 up = Vector3.up * (Measured ? waterlineCentre.y : 0f);
            if (UsesOutline)
            {
                var heading = Heading;
                var outline = waterlineOutline;
                for (int i = 0; i < outline.Length; i++)
                    Gizmos.DrawLine(FromFrame(outline[i], heading) + up, FromFrame(outline[(i + 1) % outline.Length], heading) + up);
                return;
            }
            Vector3 centre = WorldCentre + up;
            float radius = waterlineRadius > 0f ? waterlineRadius : RendererRadius();
            const int steps = 24;
            for (int i = 0; i < steps; i++)
            {
                float a0 = i * Mathf.PI * 2f / steps, a1 = (i + 1) * Mathf.PI * 2f / steps;
                Gizmos.DrawLine(centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius, centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius);
            }
        }
    }
}
