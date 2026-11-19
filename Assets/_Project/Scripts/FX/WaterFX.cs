using FishingVR.Fishing;
using UnityEngine;

namespace FishingVR.FX
{
    /// <summary>
    /// Water particles shared by the whole scene (bobber, fish, line): one particle system per effect,
    /// emitted on demand at any position (no pooling, no GameObject per splash).
    /// Put the PF_WaterFX prefab in the scene (FishingVR > FX > Créer les effets d'eau builds it).
    /// Strength arguments are roughly 0..1 (more for big fish).
    /// </summary>
    public class WaterFX : MonoBehaviour
    {
        [Tooltip("Stretched droplets thrown in the air, killed when they fall back in the water.")]
        [SerializeField] ParticleSystem droplets;
        [Tooltip("Soft white puffs on the surface (splash foam).")]
        [SerializeField] ParticleSystem foam;
        [Tooltip("Flat rings growing on the surface.")]
        [SerializeField] ParticleSystem ripples;
        [Tooltip("Small bubbles rising under the water.")]
        [SerializeField] ParticleSystem bubbles;
        [Tooltip("Collision plane of the droplets, kept on the water surface.")]
        [SerializeField] Transform waterPlane;

        [Header("Look")]
        [SerializeField] Color dropletColor = new Color(0.95f, 0.99f, 1f, 1f);
        [SerializeField] Color foamColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] Color rippleColor = new Color(0.97f, 1f, 1f, 0.95f);
        [SerializeField] Color bubbleColor = new Color(0.9f, 0.97f, 1f, 0.7f);
        [Tooltip("Height of the ripples above the water surface (avoids z-fighting).")]
        [SerializeField] float surfaceOffset = 0.012f;

        public static WaterFX Instance { get; private set; }
        public static bool Exists => Instance != null;

        void OnEnable()
        {
            Instance = this;
            foreach (var ps in new[] { droplets, foam, ripples, bubbles })
                if (ps != null && !ps.isPlaying) ps.Play();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            if (waterPlane == null) return;
            var cam = Camera.main;
            var at = cam != null ? cam.transform.position : transform.position;
            var water = FishingWater.HeightAt(at);
            if (!float.IsNegativeInfinity(water)) waterPlane.position = new Vector3(at.x, water, at.z);
        }

        // ---------- API ----------

        /// <summary>A ring on the surface above / at <paramref name="position"/>.</summary>
        public static void Ripple(Vector3 position, float size, float strength = 1f)
        {
            var fx = Instance;
            if (fx == null || fx.ripples == null || strength <= 0.01f) return;
            var water = FishingWater.HeightAt(position);
            if (float.IsNegativeInfinity(water)) return;
            var c = fx.rippleColor;
            c.a *= Mathf.Clamp01(strength);
            var p = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, water + fx.surfaceOffset, position.z),
                velocity = Vector3.zero,
                startSize = size * Random.Range(0.85f, 1.15f),
                startLifetime = Random.Range(1.1f, 1.7f) * Mathf.Lerp(0.8f, 1.3f, Mathf.Clamp01(size)),
                startColor = c,
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false,
            };
            fx.ripples.Emit(p, 1);
        }

        /// <summary>Droplets thrown from <paramref name="position"/> around <paramref name="velocity"/>.</summary>
        public static void Spray(Vector3 position, Vector3 velocity, int count, float spread = 0.6f, float size = 1f)
        {
            var fx = Instance;
            if (fx == null || fx.droplets == null || count <= 0) return;
            var water = FishingWater.HeightAt(position);
            if (!float.IsNegativeInfinity(water)) position.y = Mathf.Max(position.y, water + 0.02f);
            var speed = velocity.magnitude;
            for (var i = 0; i < count; i++)
            {
                var v = velocity + Random.insideUnitSphere * (spread * Mathf.Max(0.5f, speed));
                v.y = Mathf.Abs(v.y) + 0.3f;
                var p = new ParticleSystem.EmitParams
                {
                    position = position + Random.insideUnitSphere * 0.03f,
                    velocity = v,
                    startSize = Random.Range(0.02f, 0.045f) * size,
                    startLifetime = Random.Range(0.6f, 1.2f),
                    startColor = fx.dropletColor,
                    applyShapeToPosition = false,
                };
                fx.droplets.Emit(p, 1);
            }
        }

        /// <summary>Flat foam patch on the surface (cartoon blob that breaks up).</summary>
        public static void Foam(Vector3 position, float size, float strength = 1f)
        {
            var fx = Instance;
            if (fx == null || fx.foam == null || strength <= 0.01f) return;
            var water = FishingWater.HeightAt(position);
            if (float.IsNegativeInfinity(water)) return;
            var c = fx.foamColor;
            c.a *= Mathf.Clamp01(strength);
            var p = new ParticleSystem.EmitParams
            {
                position = new Vector3(position.x, water + 0.015f, position.z) + Vector3.ProjectOnPlane(Random.insideUnitSphere, Vector3.up) * size * 0.2f,
                velocity = Vector3.zero,
                startSize = size * Random.Range(0.8f, 1.2f),
                startLifetime = Random.Range(0.6f, 1.1f),
                startColor = c,
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false,
            };
            fx.foam.Emit(p, 1);
        }

        /// <summary>
        /// Splash: a crown of droplets, foam and rings. <paramref name="strength"/> 0.2 = bobber landing,
        /// 1 = fish breaking the surface, 2 = big fish.
        /// </summary>
        public static void Splash(Vector3 position, float strength)
        {
            if (!Exists || strength <= 0.01f) return;
            var s = Mathf.Clamp(strength, 0.05f, 3f);
            var water = FishingWater.HeightAt(position);
            if (float.IsNegativeInfinity(water)) return;
            var surface = new Vector3(position.x, water, position.z);

            var count = Mathf.RoundToInt(Mathf.Lerp(4f, 26f, Mathf.Clamp01(s / 1.5f)));
            for (var i = 0; i < count; i++)
            {
                var dir = Random.insideUnitCircle.normalized;
                var outward = new Vector3(dir.x, 0f, dir.y) * Random.Range(0.3f, 1.1f) * Mathf.Sqrt(s);
                var up = Vector3.up * Random.Range(1.2f, 2.8f) * Mathf.Sqrt(s);
                Spray(surface + new Vector3(dir.x, 0f, dir.y) * 0.05f * s, outward + up, 1, 0.15f, Mathf.Lerp(0.8f, 1.5f, Mathf.Clamp01(s - 0.5f)));
            }
            var puffs = Mathf.RoundToInt(Mathf.Lerp(1f, 5f, Mathf.Clamp01(s)));
            for (var i = 0; i < puffs; i++) Foam(surface, 0.15f + 0.3f * s, 0.7f + 0.3f * Mathf.Clamp01(s));
            Ripple(surface, 0.35f + 0.6f * s, 1f);
            if (s > 0.4f) Ripple(surface, 0.2f + 0.3f * s, 0.7f);
        }

        /// <summary>Bubbles rising from <paramref name="position"/> (under the water) to the surface.</summary>
        public static void Bubbles(Vector3 position, int count, float size = 1f)
        {
            var fx = Instance;
            if (fx == null || fx.bubbles == null || count <= 0) return;
            var water = FishingWater.HeightAt(position);
            if (float.IsNegativeInfinity(water)) return;
            var depth = water - position.y;
            if (depth < 0.05f) return;
            for (var i = 0; i < count; i++)
            {
                var rise = Random.Range(0.25f, 0.45f);
                var start = position + Random.insideUnitSphere * 0.04f * size;
                var p = new ParticleSystem.EmitParams
                {
                    position = start,
                    velocity = Vector3.up * rise + Random.insideUnitSphere * 0.05f,
                    startSize = Random.Range(0.008f, 0.022f) * size,
                    // Pops at the surface.
                    startLifetime = Mathf.Clamp((water - start.y) / rise, 0.1f, 3f),
                    startColor = fx.bubbleColor,
                    applyShapeToPosition = false,
                };
                fx.bubbles.Emit(p, 1);
            }
        }

        /// <summary>One drop falling from a wet fish / item.</summary>
        public static void Drip(Vector3 position)
        {
            var fx = Instance;
            if (fx == null || fx.droplets == null) return;
            var p = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = Vector3.down * Random.Range(0.1f, 0.4f),
                startSize = Random.Range(0.012f, 0.022f),
                startLifetime = 1.5f,
                startColor = fx.dropletColor,
                applyShapeToPosition = false,
            };
            fx.droplets.Emit(p, 1);
        }
    }
}
