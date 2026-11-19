using System.Collections.Generic;
using FishingVR.Fish;
using FishingVR.FX;
using FishingVR.Water;
using UnityEngine;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Fish jumping out of a fishing spot from time to time: the long-distance cue that a school is there.
    /// A model of one of the species leaps along an arc (purely visual, no physics, no collider) with a splash
    /// when it leaves and when it falls back in. The stronger the spot, the more often; and less often when the
    /// player is close (the jumps are a cue to find the spot from afar, not a show once there).
    /// Species are picked by their rarity weight and sized like the caught fish (FishSpecies.Roll), as FishBody does.
    /// </summary>
    [RequireComponent(typeof(FishingSpot))]
    public class FishingSpotJumps : MonoBehaviour
    {
        [Tooltip("Species whose models jump here, picked by their rarity weight (SpawnWeight). Empty = no jumps.")]
        [SerializeField] List<FishSpecies> species = new();

        [Tooltip("Seconds between two jumps at full intensity, seen from afar (random in this range). Longer when the spot weakens or the player is close.")]
        [MinMax] [SerializeField] Vector2 interval = new(5f, 14f);

        [Header("Distance to the player")]
        [Tooltip("From this distance (m) and beyond, the jumps happen at the full rate.")]
        [SerializeField, Min(0f)] float farDistance = 30f;

        [Tooltip("At this distance (m) or closer, the rate is multiplied by Near Rate Multiplier.")]
        [SerializeField, Min(0f)] float nearDistance = 8f;

        [Tooltip("Jump rate multiplier when the player is close (0 = no jumps at all next to the spot).")]
        [SerializeField, Range(0f, 1f)] float nearRateMultiplier = 0.2f;

        [Header("Jump")]

        [Tooltip("Below this intensity the fish stop jumping.")]
        [SerializeField, Range(0f, 1f)] float minIntensity = 0.15f;

        [Tooltip("Height of the jump above the water (m).")]
        [MinMax] [SerializeField] Vector2 height = new(0.4f, 1f);

        [Tooltip("Horizontal length of the jump (m).")]
        [MinMax] [SerializeField] Vector2 length = new(0.8f, 1.8f);

        [Tooltip("How deep the fish starts and ends under the surface (m), so it is seen breaking the surface.")]
        [SerializeField, Min(0f)] float underwaterStart = 0.25f;

        [Tooltip("Size multiplier on the species' rolled length (1 = the size of the caught fish).")]
        [SerializeField, Min(0.1f)] float sizeMultiplier = 1f;

        [Tooltip("Splash strength (WaterFX scale) for a 30 cm fish; scaled with the fish length.")]
        [SerializeField, Min(0f)] float splashStrength = 0.9f;

        FishingSpot spot;
        float progress;      // Seconds of "full rate" accumulated since the last jump.
        float nextJumpAt;    // Progress needed for the next jump.
        Transform player;

        class Jump
        {
            public GameObject model;
            public Vector3 start, end;
            public float apex;      // height of the apex above the surface (m)
            public float duration;  // s
            public float time;
            public float strength;
            public bool exited, entered;
            public float surface;
        }

        readonly List<Jump> jumps = new();

        /// <summary>Where the last jump happened (middle of its arc, on the water).</summary>
        public Vector3 LastJumpPoint { get; private set; }

        void Awake() => spot = GetComponent<FishingSpot>();

        void OnEnable() => ScheduleNext();

        /// <summary>
        /// Jump rate multiplier for a player at this horizontal distance of the spot: 1 from far away, down to
        /// nearRate when close, smooth in between. Public and static for the tests.
        /// </summary>
        public static float DistanceRateFactor(float distance, float near, float far, float nearRate)
        {
            if (far <= near)
                return distance >= far ? 1f : nearRate;
            float t = Mathf.Clamp01((distance - near) / (far - near));
            return Mathf.Lerp(nearRate, 1f, t * t * (3f - 2f * t));
        }

        float PlayerDistance()
        {
            if (player == null && Camera.main != null)
                player = Camera.main.transform;
            if (player == null)
                return float.PositiveInfinity;
            Vector3 offset = player.position - transform.position;
            return new Vector2(offset.x, offset.z).magnitude;
        }

        void OnDisable()
        {
            foreach (var jump in jumps)
                if (jump.model != null)
                    Destroy(jump.model);
            jumps.Clear();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            // The rate is evaluated every frame: walking or sailing closer slows the jumps down at once.
            if (spot.Intensity >= minIntensity)
            {
                float rate = spot.Intensity * DistanceRateFactor(PlayerDistance(), nearDistance, farDistance, nearRateMultiplier);
                progress += dt * rate;
                if (progress >= nextJumpAt)
                {
                    StartJump();
                    ScheduleNext();
                }
            }

            for (int i = jumps.Count - 1; i >= 0; i--)
            {
                if (!UpdateJump(jumps[i], dt))
                {
                    if (jumps[i].model != null)
                        Destroy(jumps[i].model);
                    jumps.RemoveAt(i);
                }
            }
        }

        void ScheduleNext()
        {
            // Time to wait at full rate; a weaker spot or a closer player makes it pass slower.
            progress = 0f;
            nextJumpAt = Random.Range(interval.x, interval.y);
        }

        /// <summary>Makes a fish jump now (also callable from code or tests).</summary>
        [ContextMenu("Debug: Jump Now")]
        public void StartJump()
        {
            var picked = PickSpecies();
            if (picked == null)
                return;

            Vector3 middle = spot.RandomPointInside(0.7f);
            float surface = FishingWater.HeightAt(middle);
            if (float.IsNegativeInfinity(surface) || FishingWater.DepthAt(middle) < underwaterStart + 0.2f)
                return;
            LastJumpPoint = new Vector3(middle.x, surface, middle.z);

            var fish = picked.Roll();
            float fishLength = fish.lengthCm / 100f * sizeMultiplier; // the species' size distribution (Roll)
            float jumpLength = Random.Range(length.x, length.y) * Mathf.Lerp(0.8f, 1.3f, Mathf.InverseLerp(0.2f, 0.8f, fishLength));
            Vector2 dir2 = Random.insideUnitCircle.normalized;
            Vector3 dir = new(dir2.x, 0f, dir2.y);

            var jump = new Jump
            {
                model = CreateModel(picked, fishLength),
                start = middle - dir * jumpLength * 0.5f + Vector3.up * (surface - middle.y - underwaterStart),
                end = middle + dir * jumpLength * 0.5f + Vector3.up * (surface - middle.y - underwaterStart),
                apex = Random.Range(height.x, height.y),
                surface = surface,
                strength = splashStrength * Mathf.Clamp(fishLength / 0.3f, 0.5f, 2.5f),
            };
            // Ballistic time for the vertical part: up to the apex and down again.
            float rise = jump.apex + underwaterStart;
            jump.duration = 2f * Mathf.Sqrt(2f * rise / Mathf.Abs(Physics.gravity.y));

            if (jump.model == null)
                return;
            jumps.Add(jump);
            UpdateJump(jump, 0f);
        }

        /// <summary>Random species weighted by rarity, like FishBiteAI picks the fish that comes to the bait.</summary>
        FishSpecies PickSpecies()
        {
            float total = 0f;
            foreach (var s in species)
                if (s != null && s.modelPrefab != null)
                    total += s.SpawnWeight;
            if (total <= 0f)
                return null;

            float pick = Random.value * total;
            foreach (var s in species)
            {
                if (s == null || s.modelPrefab == null)
                    continue;
                pick -= s.SpawnWeight;
                if (pick <= 0f)
                    return s;
            }
            return null;
        }

        /// <summary>
        /// The model under a pivot, as FishBody does: the prefab keeps its own rotation (the imported models are
        /// not upright without it) and the pivot is what we turn. Pivot +Z = the fish's nose.
        /// </summary>
        GameObject CreateModel(FishSpecies picked, float fishLength)
        {
            var pivot = new GameObject($"Jumping {picked.displayName}");
            pivot.transform.SetParent(transform, false);
            pivot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // measure at the origin, like FishBody

            var model = Instantiate(picked.modelPrefab, pivot.transform, false);
            foreach (var c in model.GetComponentsInChildren<Collider>())
                Destroy(c); // Purely visual: never hits the boat or the player.
            foreach (var rb in model.GetComponentsInChildren<Rigidbody>())
                Destroy(rb);

            // Same sizing as FishBody: the native length is the bounds along Z with the pivot at the origin.
            var renderer = model.GetComponentInChildren<Renderer>();
            float nativeLength = renderer != null ? renderer.bounds.size.z : 0.5f;
            model.transform.localScale *= Mathf.Max(0.05f, fishLength / Mathf.Max(nativeLength, 0.01f));

            // Centre the body on the pivot, so it rotates around its middle during the arc.
            if (renderer != null)
                model.transform.localPosition -= pivot.transform.InverseTransformPoint(renderer.bounds.center);
            return pivot;
        }

        /// <summary>Moves a jumping fish along its arc. Returns false when the jump is over.</summary>
        bool UpdateJump(Jump jump, float dt)
        {
            jump.time += dt;
            float u = Mathf.Clamp01(jump.time / jump.duration);
            float rise = jump.apex + underwaterStart;

            // Parabola from start to end, peaking at the apex.
            Vector3 position = Vector3.Lerp(jump.start, jump.end, u) + Vector3.up * (4f * rise * u * (1f - u));
            // Direction of motion: forward along the jump, up then down (the fish noses in).
            float horizontal = Vector3.Distance(jump.start, jump.end) / jump.duration;
            float vertical = 4f * rise * (1f - 2f * u) / jump.duration;
            Vector3 velocity = (jump.end - jump.start).normalized * horizontal + Vector3.up * vertical;

            if (jump.model != null)
            {
                jump.model.transform.position = position;
                if (velocity.sqrMagnitude > 1e-4f)
                    jump.model.transform.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up)
                                                  * Quaternion.Euler(0f, Mathf.Sin(jump.time * 25f) * 12f, 0f); // tail wiggle
            }

            Vector3 surfacePoint = new(position.x, jump.surface, position.z);
            if (!jump.exited && position.y > jump.surface)
            {
                jump.exited = true;
                Splash(surfacePoint, jump.strength * 0.7f);
            }
            else if (jump.exited && !jump.entered && position.y < jump.surface && u > 0.5f)
            {
                jump.entered = true;
                Splash(surfacePoint, jump.strength);
            }
            return u < 1f;
        }

        static void Splash(Vector3 surface, float strength)
        {
            WaterFX.Splash(surface, strength);
            WaterRipples.AddImpulse(surface, 0.25f + 0.15f * strength, -0.03f * strength, 0.4f);
        }
    }
}
