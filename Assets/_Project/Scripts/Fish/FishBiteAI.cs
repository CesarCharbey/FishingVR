using System.Collections.Generic;
using FishingVR.Fishing;
using UnityEngine;
using UnityEngine.Events;

namespace FishingVR.Fish
{
    public enum FishBiteState
    {
        /// <summary>No bait in the water.</summary>
        Idle,
        /// <summary>Bobber settled, a fish will show up after a short delay.</summary>
        Waiting,
        /// <summary>A fish emerges from the depths and swims to the hook.</summary>
        Approaching,
        /// <summary>It circles around the hook.</summary>
        Circling,
        /// <summary>Small touches on the bait (bobber dips): setting the hook now scares it away.</summary>
        Nibbling,
        /// <summary>Real bite (bobber pulled under): set the hook before the window ends.</summary>
        Biting,
        /// <summary>Fish on the line: <see cref="FishFight"/> runs the fight.</summary>
        Hooked,
        /// <summary>Fish left (or line broke), short cooldown before another one comes.</summary>
        Escaped,
        /// <summary>Fish landed, short cooldown before another one comes.</summary>
        Caught,
    }

    /// <summary>
    /// Before the fight: picks a species (by rarity weight), makes a visible fish rise from the depths,
    /// swim to the hook, circle around it, nibble (bobber dips) and bite (bobber pulled under).
    /// Hook set during the bite -> <see cref="FishFight"/>. Too early, too late or bait pulled out -> it flees.
    ///
    /// Fishing spots (when the scene has a <see cref="FishingSpotManager"/> or any <see cref="FishingSpot"/>):
    /// the wait before a fish comes depends on the spot influence at the hook (short in a spot, long outside),
    /// and so do the species (rare ones favoured in a spot, very rare outside) and the size (big fish more
    /// common in a spot, very rare outside). The spot loses stock on each catch and each escape.
    /// Without any spot in the scene (dev scenes), everything works as before: a fish comes after its
    /// approach delay, with the plain rarity weights and sizes.
    /// </summary>
    [RequireComponent(typeof(FishFight))]
    public class FishBiteAI : MonoBehaviour
    {
        [SerializeField] FishingRod rod;
        [SerializeField] List<FishSpecies> species = new List<FishSpecies>();
        [Tooltip("Transparent version of the palette, used to fade the fish with depth.")]
        [SerializeField] Material underwaterMaterial;
        [SerializeField] float escapeCooldown = 2f;
        [SerializeField] float nibbleDipDepth = 0.05f;
        [SerializeField] bool debugLogs = true;

        [Header("Stay in the water")]
        [Tooltip("No fish comes to a bait in water shallower than this (m).")]
        [SerializeField] float minBiteDepth = 0.4f;
        [Tooltip("Fish only swim (approach, circle) where the water is at least this deep (m).")]
        [SerializeField] float minSwimDepth = 0.35f;

        [Header("Fishing spots (only when the scene has some)")]
        [Tooltip("Seconds before a fish comes once the bait has settled, outside any fishing spot (random in this range).")]
        [MinMax] [SerializeField] Vector2 waitOutsideSpot = new Vector2(20f, 40f);
        [Tooltip("Seconds before a fish comes with the hook in the inner circle of a full spot (random in this range). " +
                 "In between (spot edge, weakened spot), the wait is blended.")]
        [MinMax] [SerializeField] Vector2 waitInSpot = new Vector2(5f, 10f);
        [Tooltip("Spawn weight multiplier per rarity OUTSIDE the spots: Common, Uncommon, Rare, Epic, Legendary.")]
        [SerializeField] float[] rarityOutsideSpot = { 1f, 0.6f, 0.25f, 0.08f, 0.02f };
        [Tooltip("Spawn weight multiplier per rarity IN a full spot: Common, Uncommon, Rare, Epic, Legendary.")]
        [SerializeField] float[] rarityInSpot = { 1f, 1.5f, 2.5f, 4f, 6f };
        [Tooltip("Size bias multiplier outside the spots (species' Size Rarity Bias times this): above 1, big fish get rarer.")]
        [SerializeField, Min(0.05f)] float sizeBiasOutsideSpot = 1.75f;
        [Tooltip("Size bias multiplier in a full spot: below 1, big fish get more common.")]
        [SerializeField, Min(0.05f)] float sizeBiasInSpot = 0.5f;

        [Header("Events")]
        public UnityEvent<FishInstance> onNibble;
        public UnityEvent<FishInstance> onBite;
        public UnityEvent<FishInstance> onHooked;
        public UnityEvent<FishInstance> onEscaped;

        FishFight fight;
        FishBiteState state = FishBiteState.Idle;
        FishInstance current;
        FishBody body;
        float timer;

        // Circling
        float orbitAngle, orbitSpeed, orbitRadius, orbitStartRadius, orbitDuration, orbitTime;
        // Nibbling
        int nibblesLeft;
        bool darting;
        bool biteStarted;

        public FishBiteState State => state;
        /// <summary>True while the fish holds the hook (bobber under): the moment to set the hook.</summary>
        public bool IsBiteActive => state == FishBiteState.Biting && biteStarted;
        public FishInstance Current => current;
        public FishBody Body => body;
        public FishFight Fight => fight;
        public IReadOnlyList<FishSpecies> Species => species;

        FishingLine Line => rod != null ? rod.Line : null;
        Vector3 HookPos => Line.Hook.position;
        /// <summary>Where the fish's mouth goes for the hook: the hook, kept off the bottom.</summary>
        Vector3 HookTarget
        {
            get
            {
                var h = HookPos;
                h.y = FishingWater.ClampUnderwater(h, h.y, 0.16f, 0.08f);
                return h;
            }
        }
        bool tooShallowLogged;
        float bobberOutTimer;

        // Fishing spots: wait before the next fish, and the spot the current fish comes from.
        float waitProgress;   // 0 to 1
        float waitRoll = -1f; // where in the wait ranges this bait sits (drawn when it settles)
        FishingSpot currentSpot;

        /// <summary>True when the scene uses fishing spots (manager or spots present).</summary>
        public static bool SpotsInUse => FishingSpotManager.Instance != null || FishingSpot.All.Count > 0;

        /// <summary>Spot the current fish comes from (null outside the spots).</summary>
        public FishingSpot CurrentSpot => currentSpot;

        /// <summary>Wait progress toward the next fish (0-1), with spots in use.</summary>
        public float WaitProgress => waitProgress;

        void Awake() => fight = GetComponent<FishFight>();

        void OnEnable()
        {
            if (rod != null) rod.onHookSetGesture.AddListener(OnHookSetGesture);
        }

        void OnDisable()
        {
            if (rod != null) rod.onHookSetGesture.RemoveListener(OnHookSetGesture);
        }

        void Update()
        {
            var line = Line;
            if (line == null)
                return;

            var dt = Time.deltaTime;
            // The bobber bounces a little after a nibble: only a bait out of the water for a moment counts.
            bobberOutTimer = line.Bobber.IsInWater ? 0f : bobberOutTimer + dt;
            var baitInWater = line.State == LineState.Deployed && bobberOutTimer < 0.3f && !line.InFight;
            timer -= dt;

            switch (state)
            {
                case FishBiteState.Idle:
                    if (!baitInWater)
                    {
                        // Bait out of the water: the next cast starts a new wait.
                        waitProgress = 0f;
                        waitRoll = -1f;
                        break;
                    }
                    if (!line.Bobber.IsSettled || !DeepEnough(line))
                        break;

                    if (!SpotsInUse)
                    {
                        // No fishing spots in this scene: a fish comes after its approach delay, as always.
                        if (PickFish(0f, false))
                        {
                            timer = RandomRange(current.Behaviour.approachDelay);
                            SetState(FishBiteState.Waiting);
                        }
                        break;
                    }

                    {
                        // Fishing spots: the wait depends on the spot influence at the hook, re-evaluated every
                        // frame (casting into a spot, or a spot leaving, changes it at once).
                        if (waitRoll < 0f) waitRoll = Random.value;
                        var influence = FishingSpot.InfluenceAt(HookPos, out var spot);
                        waitProgress += dt / SpotWait(influence, waitRoll, waitOutsideSpot, waitInSpot);
                        if (waitProgress < 1f)
                            break;

                        waitProgress = 0f;
                        waitRoll = -1f;
                        if (PickFish(influence, true))
                        {
                            currentSpot = influence > 0.01f ? spot : null;
                            timer = 0.2f; // the wait already happened: the fish comes now
                            SetState(FishBiteState.Waiting);
                        }
                    }
                    break;

                case FishBiteState.Waiting:
                    if (!baitInWater) { SetState(FishBiteState.Idle); break; }
                    if (timer <= 0f) SpawnFish();
                    break;

                case FishBiteState.Approaching:
                {
                    if (!baitInWater) { Flee("bait pulled out"); break; }
                    var b = current.Behaviour;
                    var entry = SafePoint(OrbitPoint(orbitAngle, orbitRadius));
                    body.SwimTowards(entry, b.cruiseSpeed, 90f);
                    if (Horizontal(body.Head - HookPos) < orbitRadius * 1.4f || timer <= 0f)
                    {
                        orbitTime = 0f;
                        orbitAngle = Mathf.Atan2(body.Head.z - HookPos.z, body.Head.x - HookPos.x);
                        SetState(FishBiteState.Circling);
                    }
                    break;
                }

                case FishBiteState.Circling:
                {
                    if (!baitInWater) { Flee("bait pulled out"); break; }
                    var b = current.Behaviour;
                    orbitTime += dt;
                    orbitAngle += orbitSpeed * dt;
                    // Spirals in a bit while circling.
                    orbitRadius = Mathf.Lerp(orbitStartRadius, orbitStartRadius * 0.6f, orbitTime / orbitDuration);
                    var target = OrbitPoint(orbitAngle + Mathf.Sign(orbitSpeed) * 0.6f, orbitRadius); // aim a bit ahead
                    target.y += Mathf.Sin(orbitTime * 1.3f) * 0.08f;
                    target = SafePoint(target); // the circle is squashed against the shore
                    body.SwimTowards(target, b.cruiseSpeed * 0.8f, 150f);
                    if (orbitTime >= orbitDuration)
                    {
                        var n = b.nibbleCount;
                        nibblesLeft = Random.Range(n.x, n.y + 1);
                        darting = true;
                        timer = 2f;
                        if (nibblesLeft > 0) SetState(FishBiteState.Nibbling);
                        else StartBite();
                    }
                    break;
                }

                case FishBiteState.Nibbling:
                {
                    if (!baitInWater) { Flee("bait pulled out"); break; }
                    var b = current.Behaviour;
                    if (darting)
                    {
                        // Quick dart at the hook.
                        body.SwimTowards(HookTarget, b.cruiseSpeed * 2f, 260f, 0.15f, 6f);
                        if (Vector3.Distance(body.Head, HookTarget) < 0.1f || timer <= 0f)
                        {
                            nibblesLeft--;
                            line.Bobber.Dip(nibbleDipDepth, 0.15f);
                            rod.SendHaptic(0.15f, 0.05f);
                            onNibble?.Invoke(current);
                            darting = false;
                            timer = RandomRange(b.nibbleInterval);
                        }
                    }
                    else
                    {
                        // Backs off a little, keeps an eye on the bait.
                        var away = body.Head - HookPos;
                        away.y = 0f;
                        if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
                        body.SwimTowards(SafePoint(HookTarget + away.normalized * 0.55f), b.cruiseSpeed, 200f, 0.3f);
                        if (timer <= 0f)
                        {
                            if (nibblesLeft > 0) { darting = true; timer = 2f; }
                            else StartBite();
                        }
                    }
                    break;
                }

                case FishBiteState.Biting:
                    if (!baitInWater) { Flee("bait pulled out"); break; }
                    if (!biteStarted)
                    {
                        // Final dart at the hook.
                        body.SwimTowards(HookTarget, current.Behaviour.cruiseSpeed * 2.5f, 300f, 0.1f, 8f);
                        if (Vector3.Distance(body.Head, HookTarget) < 0.08f || timer <= 0f)
                        {
                            // Mouth on the hook: it pulls the bobber under.
                            biteStarted = true;
                            timer = current.Behaviour.biteWindow;
                            line.Bobber.Sink(current.Behaviour.biteSinkDepth, current.Behaviour.biteWindow);
                            rod.SendHaptic(0.7f, 0.25f, true);
                            onBite?.Invoke(current);
                        }
                    }
                    else
                    {
                        body.SetHead(HookPos);
                        body.SetEffort(0.6f);
                        if (timer <= 0f) Flee("too late");
                    }
                    break;

                case FishBiteState.Escaped:
                case FishBiteState.Caught:
                    if (timer <= 0f) SetState(FishBiteState.Idle);
                    break;
            }
        }

        // ---------- Steps ----------

        void SpawnFish()
        {
            var b = current.Behaviour;
            var hook = HookPos;
            var water = FishingWater.HeightAt(hook);

            // Appears away from the angler (or on the sides), deep under the surface, above the lake bed,
            // in open water with a straight swim to the hook (never from the shore side).
            var tip = rod.RodTip.position;
            var away = Horizontal3(hook - tip).normalized;
            if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
            var dir = away;
            var distance = 0f;
            var bestDistance = -1f;
            for (var i = 0; i < 24 && distance <= 0f; i++)
            {
                // Away from the angler first, then any direction.
                var candidate = Quaternion.Euler(0f, i < 12 ? Random.Range(-110f, 110f) : Random.Range(-180f, 180f), 0f) * away;
                var free = FishingWater.SwimmableDistance(hook, candidate, b.spawnDistance.y, minSwimDepth + 0.1f);
                if (free >= b.spawnDistance.x) { dir = candidate; distance = Random.Range(b.spawnDistance.x, free); }
                else if (free > bestDistance) { bestDistance = free; dir = candidate; }
            }
            if (distance <= 0f) distance = Mathf.Max(1f, bestDistance); // enclosed water: as far as it can
            var pos = hook + dir * distance;
            pos.y = FishingWater.ClampUnderwater(pos, water - RandomRange(b.spawnDepth), 0.3f, 0.15f);

            body = FishBody.Create(current, pos, hook - pos, underwaterMaterial);

            orbitStartRadius = orbitRadius = RandomRange(b.circleRadius);
            orbitSpeed = b.cruiseSpeed / Mathf.Max(0.3f, orbitRadius) * (Random.value < 0.5f ? -1f : 1f);
            orbitDuration = RandomRange(b.circleLaps) * Mathf.PI * 2f / Mathf.Abs(orbitSpeed);
            orbitAngle = Mathf.Atan2(pos.z - hook.z, pos.x - hook.x);
            timer = 25f; // safety: reach the hook area in time
            SetState(FishBiteState.Approaching);
        }

        void StartBite()
        {
            biteStarted = false;
            timer = 1.5f; // max time for the final dart
            SetState(FishBiteState.Biting);
        }

        /// <summary>Called by the rod's hook-set gesture (public for testing).</summary>
        public void OnHookSetGesture()
        {
            switch (state)
            {
                case FishBiteState.Circling:
                case FishBiteState.Nibbling:
                    Flee("hook set too early");
                    break;
                case FishBiteState.Biting when biteStarted:
                    Hook();
                    break;
                case FishBiteState.Biting:
                    Flee("hook set too early");
                    break;
            }
        }

        void Hook()
        {
            Line.Bobber.ClearSink();
            rod.SendHaptic(1f, 0.3f, true);
            // The fish jerks when the hook goes in.
            FX.WaterFX.Splash(body.Head, 0.4f + 0.4f * Mathf.Clamp01(body.Length / 0.6f));
            FX.WaterFX.Bubbles(body.Head, 8, 1.2f);
            SetState(FishBiteState.Hooked);
            if (debugLogs) Debug.Log($"[Fish] Hooked {current}");
            onHooked?.Invoke(current);
            var hooked = body;
            body = null;
            fight.Begin(current, hooked, OnFightFinished);
        }

        void OnFightFinished(FightResult result)
        {
            timer = escapeCooldown;
            // Caught = the school loses a fish; line broken / unhooked = an escape (it costs less).
            ReportToSpot(result == FightResult.Caught);
            SetState(result == FightResult.Caught ? FishBiteState.Caught : FishBiteState.Escaped);
        }

        /// <summary>Tells the spot the current fish came from how it ended (catch or escape).</summary>
        void ReportToSpot(bool caught)
        {
            if (currentSpot != null)
            {
                if (caught) currentSpot.RegisterCatch();
                else currentSpot.RegisterEscape();
                if (debugLogs) Debug.Log($"[Fish] {currentSpot.name}: {(caught ? "catch" : "escape")}, stock {currentSpot.Stock:0.00}");
            }
            currentSpot = null;
        }

        void Flee(string reason)
        {
            if (debugLogs) Debug.Log($"[Fish] {current?.species.displayName} left: {reason}");
            Line.Bobber.ClearSink();
            if (body != null) body.Flee(rod.RodTip.position);
            body = null;
            ReportToSpot(false);
            onEscaped?.Invoke(current);
            timer = escapeCooldown;
            SetState(FishBiteState.Escaped);
        }

        /// <summary>
        /// Picks the species by rarity weight and rolls its size. With spots in use, both depend on the spot
        /// influence at the hook (0 = outside any spot, 1 = inner circle of a full spot).
        /// </summary>
        bool PickFish(float influence, bool spotsInUse)
        {
            var total = 0f;
            foreach (var s in species) if (IsValid(s)) total += Weight(s);
            if (total <= 0f) return false;

            var sizeBias = spotsInUse ? Mathf.Lerp(sizeBiasOutsideSpot, sizeBiasInSpot, influence) : 1f;
            var pick = Random.value * total;
            foreach (var s in species)
            {
                if (!IsValid(s)) continue;
                pick -= Weight(s);
                if (pick <= 0f)
                {
                    current = s.Roll(sizeBias);
                    if (debugLogs && spotsInUse)
                        Debug.Log($"[Fish] {current} picked with spot influence {influence:0.00} (size bias x{sizeBias:0.00})");
                    return true;
                }
            }
            return false;

            float Weight(FishSpecies s) =>
                s.SpawnWeight * (spotsInUse ? SpotRarityMultiplier(s.rarity, influence, rarityOutsideSpot, rarityInSpot) : 1f);
        }

        /// <summary>
        /// Seconds before a fish comes: the outside range blended toward the in-spot range by the influence.
        /// roll01 places the bait in both ranges (same roll, so the blend stays monotonic). Public for tests.
        /// </summary>
        public static float SpotWait(float influence, float roll01, Vector2 outside, Vector2 inside)
        {
            var o = Mathf.Lerp(outside.x, outside.y, roll01);
            var i = Mathf.Lerp(inside.x, inside.y, roll01);
            return Mathf.Max(0.1f, Mathf.Lerp(o, i, Mathf.Clamp01(influence)));
        }

        /// <summary>Spawn weight multiplier of a rarity at this spot influence (outside -> in spot). Public for tests.</summary>
        public static float SpotRarityMultiplier(FishRarity rarity, float influence, float[] outside, float[] inside)
        {
            var index = (int)rarity;
            var o = outside != null && index < outside.Length ? outside[index] : 1f;
            var i = inside != null && index < inside.Length ? inside[index] : 1f;
            return Mathf.Max(0f, Mathf.Lerp(o, i, Mathf.Clamp01(influence)));
        }

        static bool IsValid(FishSpecies s) => s != null && s.behaviour != null && s.modelPrefab != null;

        /// <summary>The point, brought back toward the hook if it is in the shallows, kept off the bottom.</summary>
        Vector3 SafePoint(Vector3 point)
        {
            var p = FishingWater.PullTowardsWater(point, HookTarget, minSwimDepth);
            p.y = FishingWater.ClampUnderwater(p, p.y, 0.16f, 0.08f);
            return p;
        }

        /// <summary>No fish in a few centimetres of water (bobber on the shore).</summary>
        bool DeepEnough(FishingLine line)
        {
            var deep = FishingWater.IsSwimmable(line.Bobber.transform.position, minBiteDepth);
            if (!deep && debugLogs && !tooShallowLogged)
                Debug.Log($"[Fish] Bait in {FishingWater.DepthAt(line.Bobber.transform.position):0.00} m of water: too shallow, no fish will come.");
            tooShallowLogged = !deep;
            return deep;
        }

        Vector3 OrbitPoint(float angle, float radius)
        {
            var hook = HookPos;
            return new Vector3(hook.x + Mathf.Cos(angle) * radius, hook.y, hook.z + Mathf.Sin(angle) * radius);
        }

        void SetState(FishBiteState newState)
        {
            if (debugLogs && newState != state)
                Debug.Log($"[Fish] {state} -> {newState}" + (current != null ? $" ({current.species.displayName})" : ""));
            state = newState;
        }

        static float Horizontal(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        static Vector3 Horizontal3(Vector3 v) => new Vector3(v.x, 0f, v.z);
        static float RandomRange(Vector2 r) => Random.Range(r.x, r.y);
    }
}
