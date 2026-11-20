using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishingVR.Water
{
    /// <summary>
    /// Interactive ripples on the lake (purely visual, the physics stays flat).
    ///
    /// A small wave simulation runs on the GPU in a texture covering a square of the lake around the player
    /// (the area follows the camera). Anything touching the water adds disturbances through the static API
    /// (<see cref="AddImpulse"/>, <see cref="AddDisturbance"/>) or a <see cref="WaterRippleEmitter"/>; the wave
    /// equation then spreads them as rings, and a moving disturbance naturally draws a V-shaped wake.
    /// The LakeWater shader reads the texture (global _WaterRippleTex) for its normals and the wake foam.
    ///
    /// One instance is enough. If a scene with a <see cref="WaterSurface"/> has none, the prefab
    /// Resources/WaterRipples is spawned automatically: tune the ripples on that prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public class WaterRipples : MonoBehaviour
    {
        const int MaxSplats = 32;   // Must match MAX_SPLATS in WaterRippleSim.shader.
        const int MaxStepsPerFrame = 4;
        const string ResourcePath = "WaterRipples";

        [Tooltip("Hidden/FishingVR/WaterRippleSim.")]
        [SerializeField] Shader simulationShader;

        [Header("Area")]
        [Tooltip("The simulated area is centred on this transform. Empty = the main camera (the player's head).")]
        [SerializeField] Transform follow;

        [Tooltip("Side of the simulated square (m). Ripples outside it are not drawn.")]
        [SerializeField, Range(8f, 128f)] float areaSize = 32f;

        [Tooltip("Texels per side. 512 over 32 m = 6 cm per texel, enough for the bobber's rings.")]
        [SerializeField, Range(128, 1024)] int resolution = 512;

        [Header("Waves")]
        [Tooltip("Speed of the ripples (m/s). Faster than the boat's slow speeds, slower than its top speed, " +
                 "so the wake narrows into a V when the boat goes fast.")]
        [SerializeField, Range(0.2f, 3f)] float waveSpeed = 1.1f;

        [Tooltip("Time (s) for a ripple to lose half of its amplitude. Higher = rings travel further.")]
        [SerializeField, Range(0.1f, 6f)] float waveHalfLife = 1.4f;

        [Tooltip("Time (s) for the wake foam to fade by half.")]
        [SerializeField, Range(0.1f, 10f)] float foamHalfLife = 2.5f;

        [Tooltip("Time (s) for the churned white water right behind a propeller to fade by half (kept apart from the foam).")]
        [SerializeField, Range(0.1f, 5f)] float churnHalfLife = 1.2f;

        [Tooltip("How fast the churned white water spreads sideways (per second): the wash behind the propeller widens into a cone as it fades. 0 = a straight band.")]
        [SerializeField, Range(0f, 60f)] float churnSpread = 40f;

        [Tooltip("Simulation steps per second (fixed, independent of the frame rate).")]
        [SerializeField, Range(30, 120)] int stepsPerSecond = 60;

        [Tooltip("Width of the absorbing border, as a fraction of the area, so waves do not bounce off its edge.")]
        [SerializeField, Range(0.02f, 0.3f)] float edgeWidth = 0.12f;

        [Header("Foam on steep waves")]
        [Tooltip("Wave slope above which the crest breaks into foam (0.03 = 3 cm of rise per metre). " +
                 "Lower = foam along more of the boat's V wake; too low and every ripple foams.")]
        [SerializeField, Range(0.005f, 0.3f)] float steepFoamThreshold = 0.03f;

        [Tooltip("Foam added per second on a wave twice as steep as the threshold.")]
        [SerializeField, Range(0f, 20f)] float steepFoam = 4f;

        [Tooltip("How fast the foam spreads to the neighbouring water (per second). Softens the foam lines.")]
        [SerializeField, Range(0f, 10f)] float foamSpread = 2f;

        static WaterRipples instance;
        static bool quitting;

        static readonly int StateTexId = Shader.PropertyToID("_WaterRippleTex");
        static readonly int AreaId = Shader.PropertyToID("_WaterRippleArea");
        static readonly int ParamsId = Shader.PropertyToID("_WaterRippleParams");
        static readonly int ShiftId = Shader.PropertyToID("_Shift");
        static readonly int Courant2Id = Shader.PropertyToID("_Courant2");
        static readonly int DampingId = Shader.PropertyToID("_Damping");
        static readonly int FoamDecayId = Shader.PropertyToID("_FoamDecay");
        static readonly int ChurnDecayId = Shader.PropertyToID("_ChurnDecay");
        static readonly int ChurnSpreadId = Shader.PropertyToID("_ChurnSpread");
        static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
        static readonly int TexelWorldId = Shader.PropertyToID("_TexelWorld");
        static readonly int SteepThresholdId = Shader.PropertyToID("_SteepThreshold");
        static readonly int SteepFoamId = Shader.PropertyToID("_SteepFoam");
        static readonly int FoamSpreadId = Shader.PropertyToID("_FoamSpread");
        static readonly int SplatCountId = Shader.PropertyToID("_SplatCount");
        static readonly int SplatsId = Shader.PropertyToID("_Splats");
        static readonly int SplatFoamId = Shader.PropertyToID("_SplatFoam");

        struct Splat
        {
            public Vector2 position; // world XZ
            public float radius;     // m
            public float height;     // m
            public float foam;       // 0-1
            public float churn;      // 0-1, churned white water (propeller)
        }

        readonly List<Splat> pendingSplats = new();
        readonly Vector4[] splatData = new Vector4[MaxSplats];
        readonly Vector4[] splatFoamData = new Vector4[MaxSplats];

        Material material;
        RenderTexture current, next;
        Vector2Int contentCenter;   // Area centre of the texture's content, in texels (world XZ / texel size).
        Vector2Int pendingShift;    // Texels the area moved since the content was last shifted.
        bool hasContent;
        float stepAccumulator;
        bool warnedOverflow;
        Transform cameraFollow;

        /// <summary>The active ripple system, or null.</summary>
        public static WaterRipples Instance => instance;

        float TexelSize => areaSize / resolution;

        // ------------------------------------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// One-shot disturbance: a splash, a drop, a fish tail slap...
        /// </summary>
        /// <param name="worldPosition">Where (only X and Z are used).</param>
        /// <param name="radius">Size of the disturbed patch (m).</param>
        /// <param name="height">Water raised (positive) or pushed down (negative), in metres. A few cm is a lot.</param>
        /// <param name="foam">Foam left on the water (0-1).</param>
        /// <param name="churn">Churned white water (0-1): the dense, short-lived foam right behind a propeller.</param>
        public static void AddImpulse(Vector3 worldPosition, float radius, float height, float foam = 0f, float churn = 0f)
        {
            var ripples = GetOrCreate();
            if (ripples == null)
                return;
            ripples.pendingSplats.Add(new Splat
            {
                position = new Vector2(worldPosition.x, worldPosition.z),
                radius = radius,
                height = height,
                foam = foam,
                churn = churn,
            });
        }

        /// <summary>
        /// Continuous disturbance, to call every frame (or every physics step) while something moves in the water.
        /// Rates are per second; the call is scaled by the current delta time.
        /// </summary>
        public static void AddDisturbance(Vector3 worldPosition, float radius, float heightPerSecond, float foamPerSecond = 0f, float churnPerSecond = 0f)
        {
            float dt = Time.deltaTime; // = fixedDeltaTime inside FixedUpdate.
            if (dt <= 0f)
                return;
            AddImpulse(worldPosition, radius, heightPerSecond * dt, foamPerSecond * dt, churnPerSecond * dt);
        }

        // ------------------------------------------------------------------------------------------------
        // Lifetime
        // ------------------------------------------------------------------------------------------------

        static WaterRipples GetOrCreate()
        {
            if (instance != null || quitting || !Application.isPlaying)
                return instance;

            var prefab = Resources.Load<WaterRipples>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning($"[WaterRipples] No prefab at Resources/{ResourcePath}: interactive ripples disabled.");
                quitting = true; // Do not retry every call.
                return null;
            }
            var spawned = Instantiate(prefab);
            spawned.name = prefab.name;
            return spawned;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            quitting = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SpawnIfSceneHasWater();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SpawnIfSceneHasWater();

        static void SpawnIfSceneHasWater()
        {
            if (instance == null && FindAnyObjectByType<WaterSurface>() != null)
                GetOrCreate();
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("[WaterRipples] Several ripple systems in the scene: keeping the first one.", this);
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        void OnEnable()
        {
            if (instance != this)
                return;
            if (simulationShader == null)
                simulationShader = Shader.Find("Hidden/FishingVR/WaterRippleSim");
            if (simulationShader == null || !simulationShader.isSupported)
            {
                Debug.LogWarning("[WaterRipples] Simulation shader missing or unsupported: interactive ripples disabled.", this);
                enabled = false;
                return;
            }

            material = new Material(simulationShader) { hideFlags = HideFlags.HideAndDontSave };
            current = CreateTarget("WaterRipples A");
            next = CreateTarget("WaterRipples B");
            hasContent = false;
            stepAccumulator = 0f;
            pendingSplats.Clear();
        }

        void OnDisable()
        {
            if (instance == this)
                Shader.SetGlobalVector(AreaId, Vector4.zero); // Tells the water shader there is no simulation.
            ReleaseTarget(ref current);
            ReleaseTarget(ref next);
            if (material != null)
                Destroy(material);
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        void OnApplicationQuit() => quitting = true;

        void OnValidate()
        {
            resolution = Mathf.ClosestPowerOfTwo(resolution);
        }

        RenderTexture CreateTarget(string targetName)
        {
            var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = targetName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            rt.Create();
            Clear(rt);
            return rt;
        }

        static void ReleaseTarget(ref RenderTexture rt)
        {
            if (rt == null)
                return;
            rt.Release();
            Destroy(rt);
            rt = null;
        }

        static void Clear(RenderTexture rt)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        // ------------------------------------------------------------------------------------------------
        // Simulation
        // ------------------------------------------------------------------------------------------------

        void Update()
        {
            if (material == null)
                return;

            UpdateArea();

            float stepDt = 1f / stepsPerSecond;
            stepAccumulator = Mathf.Min(stepAccumulator + Time.deltaTime, stepDt * MaxStepsPerFrame);
            int steps = Mathf.FloorToInt(stepAccumulator / stepDt);
            stepAccumulator -= steps * stepDt;

            if (steps > 0)
            {
                float texel = TexelSize;
                float courant = waveSpeed * stepDt / texel;
                material.SetFloat(Courant2Id, Mathf.Min(courant * courant, 0.45f)); // Keeps the scheme stable.
                material.SetFloat(DampingId, Mathf.Pow(0.5f, stepDt / waveHalfLife));
                material.SetFloat(FoamDecayId, Mathf.Pow(0.5f, stepDt / foamHalfLife));
                material.SetFloat(ChurnDecayId, Mathf.Pow(0.5f, stepDt / churnHalfLife));
                material.SetFloat(ChurnSpreadId, Mathf.Clamp01(churnSpread * stepDt));
                material.SetFloat(EdgeWidthId, edgeWidth);
                material.SetFloat(TexelWorldId, texel);
                material.SetFloat(SteepThresholdId, steepFoamThreshold);
                material.SetFloat(SteepFoamId, steepFoam * stepDt);
                material.SetFloat(FoamSpreadId, Mathf.Clamp01(foamSpread * stepDt));

                for (int i = 0; i < steps; i++)
                {
                    // The area shift and this frame's splats are applied once, in the first step.
                    bool first = i == 0;
                    material.SetVector(ShiftId, first ? (Vector2)pendingShift / resolution : Vector2.zero);
                    if (first)
                    {
                        contentCenter += pendingShift;
                        pendingShift = Vector2Int.zero;
                    }
                    UploadSplats(first);

                    Graphics.Blit(current, next, material, 0);
                    (current, next) = (next, current);
                }
                pendingSplats.Clear();
            }

            PublishGlobals();
        }

        Transform FollowTarget()
        {
            if (follow != null)
                return follow;
            if (cameraFollow == null && Camera.main != null)
                cameraFollow = Camera.main.transform;
            return cameraFollow;
        }

        void UpdateArea()
        {
            Transform target = FollowTarget();
            if (target == null)
                return;

            float texel = TexelSize;
            var wanted = new Vector2Int(Mathf.RoundToInt(target.position.x / texel), Mathf.RoundToInt(target.position.z / texel));
            Vector2Int moved = wanted - (contentCenter + pendingShift);

            if (!hasContent || Mathf.Abs(moved.x + pendingShift.x) >= resolution / 2 || Mathf.Abs(moved.y + pendingShift.y) >= resolution / 2)
            {
                // First frame or teleport: start from calm water around the new position.
                Clear(current);
                Clear(next);
                contentCenter = wanted;
                pendingShift = Vector2Int.zero;
                hasContent = true;
                return;
            }
            pendingShift += moved;
        }

        void UploadSplats(bool withThisFrameSplats)
        {
            int count = 0;
            if (withThisFrameSplats && pendingSplats.Count > 0)
            {
                // Splats are placed relative to the content after this step's shift.
                Vector2 min = AreaMin(contentCenter);
                float invSize = 1f / areaSize;
                float minRadius = 1.5f / resolution; // Below ~1.5 texels a splat is just noise.

                foreach (var splat in pendingSplats)
                {
                    if (count == MaxSplats)
                    {
                        if (!warnedOverflow)
                        {
                            Debug.LogWarning($"[WaterRipples] More than {MaxSplats} disturbances in one frame: extra ones are ignored.", this);
                            warnedOverflow = true;
                        }
                        break;
                    }
                    Vector2 uv = (splat.position - min) * invSize;
                    float radiusUV = Mathf.Max(splat.radius * invSize, minRadius);
                    if (uv.x < -radiusUV || uv.y < -radiusUV || uv.x > 1f + radiusUV || uv.y > 1f + radiusUV)
                        continue; // Outside the simulated area.

                    splatData[count] = new Vector4(uv.x, uv.y, radiusUV, splat.height);
                    splatFoamData[count] = new Vector4(splat.foam, splat.churn, 0f, 0f);
                    count++;
                }
            }

            material.SetFloat(SplatCountId, count);
            material.SetVectorArray(SplatsId, splatData);
            material.SetVectorArray(SplatFoamId, splatFoamData);
        }

        Vector2 AreaMin(Vector2Int center)
        {
            float texel = TexelSize;
            return new Vector2(center.x * texel - areaSize * 0.5f, center.y * texel - areaSize * 0.5f);
        }

        void PublishGlobals()
        {
            Vector2 min = AreaMin(contentCenter);
            Shader.SetGlobalTexture(StateTexId, current);
            Shader.SetGlobalVector(AreaId, new Vector4(min.x, min.y, 1f / areaSize, hasContent ? 1f : 0f));
            Shader.SetGlobalVector(ParamsId, new Vector4(1f / resolution, TexelSize, 0f, 0f));
        }

        void OnDrawGizmosSelected()
        {
            Transform target = Application.isPlaying ? FollowTarget() : (follow != null ? follow : transform);
            if (target == null)
                return;
            Vector3 center = Application.isPlaying && hasContent
                ? new Vector3(contentCenter.x * TexelSize, target.position.y, contentCenter.y * TexelSize)
                : target.position;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(center, new Vector3(areaSize, 0.01f, areaSize));
        }
    }
}
