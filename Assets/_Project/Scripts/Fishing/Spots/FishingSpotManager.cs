using System.Collections.Generic;
using UnityEngine;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Keeps the lake populated with fishing spots.
    ///
    /// How many: the wanted number of spots is drawn now and then between Min Spots and Max Spots, with a
    /// relative chance per count (by default 4 is the most likely, 3 and 5 less, 6 rare). When fewer schools
    /// are there, a new spot appears after a short delay; when there are too many, the extra ones simply end
    /// their life.
    /// Where: a random point of the area, in water deep enough all over the spot, away from the other spots,
    /// from the places to avoid (dock...) and from the player.
    /// What: each spot gets its own random radius, active time and stock of fish (cost of a catch, an escape
    /// costing a set share of it), so some schools are small and quickly emptied, others big and generous.
    /// </summary>
    [DisallowMultipleComponent]
    public class FishingSpotManager : MonoBehaviour
    {
        [Tooltip("Spot prefab (PF_FishingSpot).")]
        [SerializeField] FishingSpot spotPrefab;

        [Header("How many spots")]
        [SerializeField, Min(0)] int minSpots = 3;
        [SerializeField, Min(0)] int maxSpots = 6;

        [Tooltip("Relative chance of each count, from Min Spots to Max Spots (here 3, 4, 5, 6). " +
                 "Missing entries count as 1. The default gives about 4 on average.")]
        [SerializeField] List<float> countWeights = new() { 3f, 5f, 2.5f, 0.8f };

        [Tooltip("Seconds between two draws of the wanted number of spots (random in this range).")]
        [MinMax] [SerializeField] Vector2 recountInterval = new(180f, 360f);

        [Tooltip("Seconds before a missing spot appears (random in this range).")]
        [MinMax] [SerializeField] Vector2 spawnDelay = new(15f, 45f);

        [Tooltip("When the game starts, the spots are already there, at random points of their life " +
                 "(otherwise they all arrive at the start, then all leave together).")]
        [SerializeField] bool startWithSpots = true;

        [Header("Where")]
        [Tooltip("Area where spots can appear (X and Z sizes, centred on this object). Only deep water is used.")]
        [SerializeField] Vector2 areaSize = new(260f, 660f);

        [Tooltip("Minimum water depth (m) under the whole spot.")]
        [SerializeField, Min(0f)] float minDepth = 2f;

        [Tooltip("Minimum gap (m) between the edges of two spots.")]
        [SerializeField, Min(0f)] float minSpacing = 12f;

        [Tooltip("Places spots must stay away from (dock, beach...).")]
        [SerializeField] List<Transform> avoid = new();

        [Tooltip("Distance (m) to keep from the places to avoid.")]
        [SerializeField, Min(0f)] float avoidRadius = 15f;

        [Tooltip("A new spot never appears closer than this to the player (m): no school popping at your feet.")]
        [SerializeField, Min(0f)] float minPlayerDistance = 20f;

        [Tooltip("Random points tried to place a spot before giving up until the next attempt.")]
        [SerializeField, Min(1)] int placementAttempts = 80;

        [Header("Each spot (random in these ranges)")]
        [Tooltip("Radius of the spot (m).")]
        [MinMax] [SerializeField] Vector2 radius = new(3f, 6f);

        [Tooltip("Minutes the school stays at full strength.")]
        [MinMax] [SerializeField] Vector2 activeMinutes = new(10f, 16f);

        [Tooltip("Stock removed by a catch: 0.15 = about 7 catches, 0.3 = 3 or 4. Sets how many fish the school holds.")]
        [MinMax] [SerializeField] Vector2 catchDepletion = new(0.15f, 0.3f);

        [Tooltip("Cost of an escape as a share of the cost of a catch (0.5 = a catch costs twice an escape).")]
        [SerializeField, Range(0f, 1f)] float escapeToCatchRatio = 0.5f;

        [Header("Debug")]
        [SerializeField] bool logSpawns = true;

        readonly List<FishingSpot> owned = new();
        int targetCount;
        float nextRecount;
        float nextSpawn;
        int spawnedTotal;
        Transform player;

        /// <summary>The manager of the scene, or null (fishing then works without spots).</summary>
        public static FishingSpotManager Instance { get; private set; }

        /// <summary>Number of spots currently wanted.</summary>
        public int TargetCount => targetCount;

        /// <summary>Spots whose school is still there (arriving or active).</summary>
        public int ActiveCount
        {
            get
            {
                int n = 0;
                foreach (var s in owned)
                    if (s != null && s.IsHere)
                        n++;
                return n;
            }
        }

        /// <summary>Spots created by this manager and still in the scene (leaving ones included).</summary>
        public IReadOnlyList<FishingSpot> Spots => owned;

        void OnEnable()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("[FishingSpotManager] Several managers in the scene: only one should drive the spots.", this);
            Instance = this;
        }

        void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }

        void Start()
        {
            if (spotPrefab == null)
            {
                Debug.LogWarning("[FishingSpotManager] No spot prefab: no fishing spots.", this);
                enabled = false;
                return;
            }

            Recount();
            if (startWithSpots)
            {
                // Spread over their active time: they will not all leave at the same moment.
                for (int i = 0; i < targetCount; i++)
                {
                    var spot = TrySpawn(ignorePlayer: false);
                    if (spot != null)
                        spot.BeginActive(Random.Range(0f, 0.8f));
                }
            }
            nextSpawn = Time.time + RandomRange(spawnDelay);
        }

        void Update()
        {
            owned.RemoveAll(s => s == null);

            if (Time.time >= nextRecount)
                Recount();

            if (ActiveCount < targetCount && Time.time >= nextSpawn)
            {
                TrySpawn(ignorePlayer: false);
                nextSpawn = Time.time + RandomRange(spawnDelay);
            }
        }

        void Recount()
        {
            int previous = targetCount;
            targetCount = DrawCount(minSpots, maxSpots, countWeights);
            nextRecount = Time.time + RandomRange(recountInterval);
            if (logSpawns && previous != targetCount)
                Debug.Log($"[FishingSpots] Wanted number of spots: {targetCount} (there are {ActiveCount}).", this);
        }

        /// <summary>
        /// Draws a number of spots between min and max, each count with its relative weight (weights[0] for
        /// min). Public and static for the tests.
        /// </summary>
        public static int DrawCount(int min, int max, IReadOnlyList<float> weights, float random01 = -1f)
        {
            if (max < min)
                (min, max) = (max, min);
            float total = 0f;
            for (int n = min; n <= max; n++)
                total += Weight(n - min);
            if (total <= 0f)
                return min;

            float pick = (random01 < 0f ? Random.value : random01) * total;
            for (int n = min; n <= max; n++)
            {
                pick -= Weight(n - min);
                if (pick <= 0f)
                    return n;
            }
            return max;

            float Weight(int i) => weights != null && i < weights.Count ? Mathf.Max(0f, weights[i]) : 1f;
        }

        /// <summary>Average number of spots given by these weights (shown in the Inspector tooltip / tests).</summary>
        public static float AverageCount(int min, int max, IReadOnlyList<float> weights)
        {
            float total = 0f, sum = 0f;
            for (int n = min; n <= max; n++)
            {
                float w = weights != null && n - min < weights.Count ? Mathf.Max(0f, weights[n - min]) : 1f;
                total += w;
                sum += w * n;
            }
            return total > 0f ? sum / total : min;
        }

        /// <summary>Places a new spot, or returns null if no valid place was found this time.</summary>
        public FishingSpot TrySpawn(bool ignorePlayer)
        {
            float spotRadius = RandomRange(radius);
            Vector3 playerPos = PlayerPosition(out bool hasPlayer);

            for (int attempt = 0; attempt < placementAttempts; attempt++)
            {
                Vector3 p = transform.position + new Vector3(
                    Random.Range(-0.5f, 0.5f) * areaSize.x, 0f, Random.Range(-0.5f, 0.5f) * areaSize.y);

                if (!IsDeepEverywhere(p, spotRadius) || !IsFarFromSpots(p, spotRadius) || !IsFarFromAvoided(p, spotRadius))
                    continue;
                if (!ignorePlayer && hasPlayer && Horizontal(p - playerPos) < minPlayerDistance + spotRadius)
                    continue;

                p.y = FishingWater.HeightAt(p);
                var spot = Instantiate(spotPrefab, p, Quaternion.identity, transform);
                spot.name = $"FishingSpot {++spawnedTotal:00}";
                float catchCost = RandomRange(catchDepletion);
                spot.Configure(spotRadius, RandomRange(activeMinutes), catchCost, catchCost * escapeToCatchRatio);
                owned.Add(spot);
                if (logSpawns)
                    Debug.Log($"[FishingSpots] {spot.name} at {p} (radius {spotRadius:0.0} m, depth {FishingWater.DepthAt(p):0.0} m, " +
                              $"about {Mathf.CeilToInt(1f / Mathf.Max(catchCost, 0.01f))} catches). {ActiveCount}/{targetCount} spots.", this);
                return spot;
            }

            if (logSpawns)
                Debug.Log($"[FishingSpots] No place found for a new spot this time ({placementAttempts} tries).", this);
            return null;
        }

        /// <summary>Deep enough at the centre and around the whole spot (never half on a beach), real lake bed.</summary>
        bool IsDeepEverywhere(Vector3 center, float spotRadius)
        {
            if (!FishingWater.Exists)
                return false;
            if (!DeepAt(center))
                return false;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                if (!DeepAt(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spotRadius))
                    return false;
            }
            return true;

            bool DeepAt(Vector3 p)
            {
                float depth = FishingWater.DepthAt(p);
                // Infinite = no lake bed under it (outside the map): not a place for a school.
                return depth >= minDepth && !float.IsInfinity(depth);
            }
        }

        bool IsFarFromSpots(Vector3 p, float spotRadius)
        {
            foreach (var other in FishingSpot.All)
            {
                if (other == null || !other.isActiveAndEnabled)
                    continue;
                if (Horizontal(p - other.Center) < spotRadius + other.Radius + minSpacing)
                    return false;
            }
            return true;
        }

        bool IsFarFromAvoided(Vector3 p, float spotRadius)
        {
            foreach (var t in avoid)
                if (t != null && Horizontal(p - t.position) < avoidRadius + spotRadius)
                    return false;
            return true;
        }

        Vector3 PlayerPosition(out bool found)
        {
            if (player == null && Camera.main != null)
                player = Camera.main.transform;
            found = player != null;
            return found ? player.position : Vector3.zero;
        }

        static float Horizontal(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        static float RandomRange(Vector2 r) => Random.Range(Mathf.Min(r.x, r.y), Mathf.Max(r.x, r.y));

        [ContextMenu("Debug: Spawn A Spot Now")]
        void DebugSpawn() => TrySpawn(ignorePlayer: true);

        void OnValidate()
        {
            if (maxSpots < minSpots)
                maxSpots = minSpots;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(transform.position, new Vector3(areaSize.x, 0.1f, areaSize.y));
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            foreach (var t in avoid)
                if (t != null)
                    Gizmos.DrawWireSphere(t.position, avoidRadius);
        }
    }
}
