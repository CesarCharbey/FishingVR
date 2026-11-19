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

            var baitInWater = line.State == LineState.Deployed && line.Bobber.IsInWater && !line.InFight;
            var dt = Time.deltaTime;
            timer -= dt;

            switch (state)
            {
                case FishBiteState.Idle:
                    if (baitInWater && line.Bobber.IsSettled && PickFish())
                    {
                        timer = RandomRange(current.Behaviour.approachDelay);
                        SetState(FishBiteState.Waiting);
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
                    var entry = OrbitPoint(orbitAngle, orbitRadius);
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
                        body.SwimTowards(HookPos, b.cruiseSpeed * 2f, 260f, 0.15f, 6f);
                        if (Vector3.Distance(body.Head, HookPos) < 0.1f || timer <= 0f)
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
                        body.SwimTowards(HookPos + away.normalized * 0.55f, b.cruiseSpeed, 200f, 0.3f);
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
                        body.SwimTowards(HookPos, current.Behaviour.cruiseSpeed * 2.5f, 300f, 0.1f, 8f);
                        if (Vector3.Distance(body.Head, HookPos) < 0.08f || timer <= 0f)
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

            // Appears away from the angler (or on the sides), deep under the surface, above the lake bed.
            var tip = rod.RodTip.position;
            var away = Horizontal3(hook - tip).normalized;
            if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
            var dir = Quaternion.Euler(0f, Random.Range(-110f, 110f), 0f) * away;
            var pos = hook + dir * RandomRange(b.spawnDistance);
            pos.y = water - RandomRange(b.spawnDepth);
            if (Physics.Raycast(new Vector3(pos.x, water + 0.5f, pos.z), Vector3.down, out var bed, 60f, ~0, QueryTriggerInteraction.Ignore))
                pos.y = Mathf.Max(pos.y, Mathf.Min(bed.point.y + 0.4f, water - 0.3f));

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
            SetState(result == FightResult.Caught ? FishBiteState.Caught : FishBiteState.Escaped);
        }

        void Flee(string reason)
        {
            if (debugLogs) Debug.Log($"[Fish] {current?.species.displayName} left: {reason}");
            Line.Bobber.ClearSink();
            if (body != null) body.Flee(rod.RodTip.position);
            body = null;
            onEscaped?.Invoke(current);
            timer = escapeCooldown;
            SetState(FishBiteState.Escaped);
        }

        bool PickFish()
        {
            var total = 0f;
            foreach (var s in species) if (IsValid(s)) total += s.SpawnWeight;
            if (total <= 0f) return false;

            var pick = Random.value * total;
            foreach (var s in species)
            {
                if (!IsValid(s)) continue;
                pick -= s.SpawnWeight;
                if (pick <= 0f) { current = s.Roll(); return true; }
            }
            return false;
        }

        static bool IsValid(FishSpecies s) => s != null && s.behaviour != null && s.modelPrefab != null;

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
