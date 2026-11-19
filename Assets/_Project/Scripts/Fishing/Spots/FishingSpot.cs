using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace FishingVR.Fishing
{
    /// <summary>
    /// A fishing spot: a static round area of the lake where a school of fish gathers. Casting the bobber and
    /// the hook inside it will greatly improve the chances of a bite (gameplay link to come).
    ///
    /// Its life is driven by one value, <see cref="Intensity"/> (0-1), shared by the visuals (lively water patch,
    /// bubbles, jumping fish) and, later, the bite chances:
    /// - Arriving: 0 to 1, the school gathers;
    /// - Active: the school stays (long enough to sail there);
    /// - Leaving: back to 0, then the spot is gone (destroyed, a new one appears elsewhere).
    /// The school also gets depleted: each catch and each escape (fish scared off, line broken) removes some of
    /// its stock, a catch about twice as much as an escape. An empty school leaves early.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class FishingSpot : MonoBehaviour
    {
        public enum Phase { Arriving, Active, Leaving, Gone }

        const int MaxRenderedSpots = 8; // Must match MAX_FISHING_SPOTS in LakeWater.shader.

        [Header("Area")]
        [Tooltip("Radius of the spot on the water (m), the big circle. The centre is this object's position. " +
                 "No effect on the bite chances outside it.")]
        [SerializeField, Min(0.5f)] float radius = 4f;

        [Tooltip("Inner circle, as a fraction of the radius: full effect on the bite chances inside it, " +
                 "fading to nothing at the big circle.")]
        [SerializeField, Range(0.1f, 0.95f)] float innerRadiusRatio = 0.55f;

        [Header("Lifetime")]
        [Tooltip("Seconds for the school to gather (intensity 0 to 1).")]
        [SerializeField, Min(0f)] float arriveDuration = 30f;

        [Tooltip("Minutes the school stays at full strength (if not depleted before). Long enough to sail there.")]
        [SerializeField, Min(0f)] float activeMinutes = 12f;

        [Tooltip("Seconds for the school to leave (intensity back to 0).")]
        [SerializeField, Min(0.1f)] float leaveDuration = 45f;

        [Tooltip("Destroy this object when the school has left.")]
        [SerializeField] bool destroyWhenGone = true;

        [Header("Stock of fish")]
        [Tooltip("Stock removed by a fish caught (the stock starts at 1). 0.2 = about 5 catches.")]
        [SerializeField, Range(0f, 1f)] float catchDepletion = 0.2f;

        [Tooltip("Stock removed by a fish that escaped (scared off by the bobber, line broken). About half a catch.")]
        [SerializeField, Range(0f, 1f)] float escapeDepletion = 0.1f;

        [Tooltip("Intensity of a nearly empty school: the visuals stay readable until the last fish.")]
        [SerializeField, Range(0f, 1f)] float emptyIntensity = 0.3f;

        [Tooltip("How fast the intensity follows a change (catch, escape), per second, so the visuals never pop.")]
        [SerializeField, Min(0.01f)] float intensitySmoothing = 0.15f;

        [Header("Editor preview")]
        [Tooltip("Intensity shown in the editor (outside Play mode), to tune the look.")]
        [SerializeField, Range(0f, 1f)] float editorPreviewIntensity = 1f;

        [Header("Events")]
        [Tooltip("The school has gathered (end of Arriving).")]
        public UnityEvent<FishingSpot> onActive = new();

        [Tooltip("The school starts leaving (time is up or the stock is empty).")]
        public UnityEvent<FishingSpot> onLeaving = new();

        [Tooltip("The school is gone (intensity 0). The object is destroyed right after if Destroy When Gone is set.")]
        public UnityEvent<FishingSpot> onGone = new();

        static readonly List<FishingSpot> spots = new();
        static readonly Vector4[] shaderSpots = new Vector4[MaxRenderedSpots];
        static readonly int SpotsId = Shader.PropertyToID("_FishingSpots");
        static readonly int SpotCountId = Shader.PropertyToID("_FishingSpotCount");
        static int publishedFrame = -1;
        static bool warnedOverflow;

        Phase phase = Phase.Arriving;
        float phaseTime;
        float stock = 1f;
        float intensity;        // Smoothed value used by everything.
        float leaveFrom = 1f;   // Envelope when Leaving started (it can start before the end of Arriving).

        /// <summary>All the spots currently in the scenes.</summary>
        public static IReadOnlyList<FishingSpot> All => spots;

        /// <summary>0 = no fish (or editor preview off), 1 = full school. Drives visuals and bite chances.</summary>
        public float Intensity => Application.isPlaying ? intensity : editorPreviewIntensity;

        /// <summary>Intensity from the simulated life only (Play mode value, also advanced by <see cref="Tick"/>).</summary>
        public float LifeIntensity => intensity;

        /// <summary>Remaining fish, 1 = untouched, 0 = empty.</summary>
        public float Stock => stock;

        public Phase CurrentPhase => phase;
        public float Radius => radius;

        /// <summary>Radius of the inner circle (m): full influence inside it.</summary>
        public float InnerRadius => radius * innerRadiusRatio;

        /// <summary>True while the school is still there (arriving or active), false once it leaves.</summary>
        public bool IsHere => phase == Phase.Arriving || phase == Phase.Active;
        public Vector3 Center => transform.position;

        /// <summary>Seconds left before the school starts leaving (0 once Leaving).</summary>
        public float TimeLeft => phase switch
        {
            Phase.Arriving => arriveDuration - phaseTime + activeMinutes * 60f,
            Phase.Active => activeMinutes * 60f - phaseTime,
            _ => 0f,
        };

        /// <summary>Sets the spot up from code (used by the spot manager). Call right after creating it.</summary>
        public void Configure(float spotRadius, float activeTimeMinutes, float catchCost, float escapeCost)
        {
            radius = Mathf.Max(0.5f, spotRadius);
            activeMinutes = Mathf.Max(0f, activeTimeMinutes);
            catchDepletion = Mathf.Clamp01(catchCost);
            escapeDepletion = Mathf.Clamp01(escapeCost);
        }

        /// <summary>
        /// For spots that exist when the game starts: skips the arrival and starts at a point of the active
        /// time (0 = beginning, 0.8 = most of it already elapsed), so the first spots do not all leave together.
        /// </summary>
        public void BeginActive(float elapsedFraction)
        {
            if (phase == Phase.Leaving || phase == Phase.Gone)
                return;
            SetPhase(Phase.Active);
            phaseTime = Mathf.Clamp01(elapsedFraction) * activeMinutes * 60f;
            intensity = Mathf.Lerp(emptyIntensity, 1f, stock);
        }

        /// <summary>
        /// Influence of the fishing spots at this point (the hook): 1 inside the inner circle of a spot, 0 outside
        /// its big circle, in between on the ring, times the spot intensity. The strongest spot wins.
        /// </summary>
        /// <param name="spot">The spot giving that influence, or null (influence 0).</param>
        public static float InfluenceAt(Vector3 position, out FishingSpot spot)
        {
            spot = null;
            float best = 0f;
            foreach (var s in spots)
            {
                if (s == null || !s.isActiveAndEnabled)
                    continue;
                float influence = s.AreaWeight(position) * s.Intensity;
                if (influence > best)
                {
                    best = influence;
                    spot = s;
                }
            }
            return best;
        }

        /// <summary>Same as <see cref="InfluenceAt(Vector3, out FishingSpot)"/>, without the spot.</summary>
        public static float InfluenceAt(Vector3 position) => InfluenceAt(position, out _);

        /// <summary>A fish was caught in this spot.</summary>
        public void RegisterCatch() => Deplete(catchDepletion);

        /// <summary>A fish of this spot escaped (scared off, line broken, released).</summary>
        public void RegisterEscape() => Deplete(escapeDepletion);

        void OnEnable()
        {
            if (!spots.Contains(this))
                spots.Add(this);
            publishedFrame = -1;
        }

        void OnDisable()
        {
            spots.Remove(this);
            PublishGlobals(force: true);
        }

        void Update()
        {
            if (Application.isPlaying)
                Tick(Time.deltaTime);
        }

        void LateUpdate() => PublishGlobals(force: false);

        /// <summary>Advances the life of the spot. Public so it can be driven by tests.</summary>
        public void Tick(float dt)
        {
            if (phase == Phase.Gone || dt <= 0f)
                return;

            phaseTime += dt;
            switch (phase)
            {
                case Phase.Arriving:
                    if (phaseTime >= arriveDuration)
                        SetPhase(Phase.Active);
                    break;
                case Phase.Active:
                    if (phaseTime >= activeMinutes * 60f)
                        StartLeaving();
                    break;
                case Phase.Leaving:
                    if (phaseTime >= leaveDuration)
                        SetPhase(Phase.Gone);
                    break;
            }

            float target = Envelope() * Mathf.Lerp(emptyIntensity, 1f, stock);
            intensity = Mathf.MoveTowards(intensity, target, intensitySmoothing * dt);

            if (phase == Phase.Gone)
            {
                intensity = 0f;
                onGone?.Invoke(this);
                if (destroyWhenGone && Application.isPlaying)
                    Destroy(gameObject);
            }
        }

        /// <summary>Envelope of the lifetime only (0-1), before the stock is applied.</summary>
        float Envelope()
        {
            switch (phase)
            {
                case Phase.Arriving:
                    return arriveDuration <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, phaseTime / arriveDuration);
                case Phase.Active:
                    return 1f;
                case Phase.Leaving:
                    return leaveFrom * (1f - Mathf.SmoothStep(0f, 1f, phaseTime / leaveDuration));
                default:
                    return 0f;
            }
        }

        void Deplete(float amount)
        {
            if (phase == Phase.Leaving || phase == Phase.Gone)
                return;
            stock = Mathf.Max(0f, stock - amount);
            if (stock <= 0f)
                StartLeaving();
        }

        void StartLeaving()
        {
            if (phase == Phase.Leaving || phase == Phase.Gone)
                return;
            leaveFrom = Envelope();
            SetPhase(Phase.Leaving);
        }

        void SetPhase(Phase next)
        {
            phase = next;
            phaseTime = 0f;
            if (next == Phase.Active)
                onActive?.Invoke(this);
            else if (next == Phase.Leaving)
                onLeaving?.Invoke(this);
        }

        /// <summary>
        /// Area weight at a point (no intensity applied): 1 inside the inner circle, 0 outside the big circle,
        /// a smooth fade on the ring between them.
        /// </summary>
        public float AreaWeight(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            float d = new Vector2(offset.x, offset.z).magnitude / radius;
            // smoothstep(inner, 1, d). (Mathf.SmoothStep interpolates between two values, it is not this.)
            float t = Mathf.Clamp01((d - innerRadiusRatio) / Mathf.Max(1f - innerRadiusRatio, 1e-3f));
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>Random point on the water inside the spot (world XZ, Y = this object's height).</summary>
        public Vector3 RandomPointInside(float fractionOfRadius = 0.85f)
        {
            Vector2 p = Random.insideUnitCircle * radius * fractionOfRadius;
            return transform.position + new Vector3(p.x, 0f, p.y);
        }

        /// <summary>Sends the spots to the lake shader (centre XZ, radius, intensity), once per frame.</summary>
        static void PublishGlobals(bool force)
        {
            if (!force && publishedFrame == Time.frameCount)
                return;
            publishedFrame = Time.frameCount;

            int count = 0;
            foreach (var spot in spots)
            {
                if (spot == null || !spot.isActiveAndEnabled)
                    continue;
                if (count == MaxRenderedSpots)
                {
                    if (!warnedOverflow)
                    {
                        Debug.LogWarning($"[FishingSpot] More than {MaxRenderedSpots} spots: the extra ones are not drawn on the water.");
                        warnedOverflow = true;
                    }
                    break;
                }
                Vector3 c = spot.transform.position;
                shaderSpots[count++] = new Vector4(c.x, c.z, spot.radius, spot.Intensity);
            }
            for (int i = count; i < MaxRenderedSpots; i++)
                shaderSpots[i] = Vector4.zero;

            Shader.SetGlobalVectorArray(SpotsId, shaderSpots);
            Shader.SetGlobalFloat(SpotCountId, count);
        }

        void OnValidate() => publishedFrame = -1;

        // ---- Debug (right-click on the component in Play mode) ----------------------------------------------

        [ContextMenu("Debug: Skip To Active")]
        void DebugSkipToActive()
        {
            if (phase == Phase.Arriving)
            {
                SetPhase(Phase.Active);
                intensity = Mathf.Lerp(emptyIntensity, 1f, stock);
            }
        }

        [ContextMenu("Debug: Register Catch")]
        void DebugCatch() => RegisterCatch();

        [ContextMenu("Debug: Register Escape")]
        void DebugEscape() => RegisterEscape();

        [ContextMenu("Debug: Start Leaving")]
        void DebugLeave() => StartLeaving();

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.35f + 0.5f * Intensity);
            DrawCircle(transform.position, radius);
            DrawCircle(transform.position, radius * innerRadiusRatio);
        }

        static void DrawCircle(Vector3 center, float r)
        {
            const int segments = 48;
            Vector3 previous = center + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 p = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Gizmos.DrawLine(previous, p);
                previous = p;
            }
        }
    }
}
