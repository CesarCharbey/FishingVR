using System;
using FishingVR.Fishing;
using UnityEngine;
using UnityEngine.Events;

namespace FishingVR.Fish
{
    public enum FightPhase
    {
        None,
        /// <summary>Just hooked: the fish pulls the line out to the fight distance (it never comes back further).</summary>
        Run,
        /// <summary>Swims left / right across the line: hold the rod opposite to its movement, barely reel.</summary>
        Struggle,
        /// <summary>Tired, near the surface: reel it in now (the line goes slack if you don't).</summary>
        Exhausted,
        /// <summary>Close to the angler: no more fight, reel it up, then grab it or net it.</summary>
        Landing,
        Caught,
        Broken,
        /// <summary>The hook came loose (line left slack too long).</summary>
        Unhooked,
    }

    public enum FightResult { Caught, LineBroken, Unhooked }

    /// <summary>
    /// Fight, Sea of Thieves style, adapted to VR:
    /// - Run: the fish pulls the line out to <see cref="RodStats.fightDistance"/> (if cast closer). After that
    ///   it can never take line back: the distance only decreases, by reeling.
    /// - Struggle: it swims left / right across the line, through the middle, switching direction randomly.
    ///   Holding the rod OPPOSITE to its movement lowers the tension and tires it; pointing at it or with it
    ///   raises the tension (strong haptics = warning) until the line snaps. Each direction switch allows a
    ///   small reel (<see cref="reelAllowanceTurns"/>); reeling more strains the line.
    /// - Exhausted: it stops fighting and floats up: reel it in freely. Each tired phase covers a share of the
    ///   distance so that it takes 2-3 cycles (species setting), then it recovers and struggles again.
    ///   Not reeling for <see cref="slackTimeout"/> s while it is tired: the hook comes loose.
    /// - Cranking too fast (> RodStats.maxCrankSpeed) strains the line at any time.
    /// - Landing: close to the angler the fish is reeled up to the rod tip, from floating flat at the surface to
    ///   hanging from the line. Lifting it costs its weight (heavy fish need the landing net). Grab it by hand
    ///   (light fish) or scoop it with the net.
    /// </summary>
    public class FishFight : MonoBehaviour
    {
        [SerializeField] FishingRod rod;

        [Header("Distances")]
        [Tooltip("Horizontal distance from the angler where the fish stops fighting and can be landed (m).")]
        [SerializeField] float landingDistance = 3f;

        [Header("Rules")]
        [Tooltip("Reel allowed after each direction switch while it struggles (crank turns).")]
        [SerializeField] float reelAllowanceTurns = 0.5f;
        [Tooltip("Tired fish + no reeling for this long = the hook comes loose (s).")]
        [SerializeField] float slackTimeout = 5f;
        [Tooltip("A tired phase ends after this long even if its distance share was not reeled (s).")]
        [SerializeField] float exhaustedMaxDuration = 12f;
        [Tooltip("Stamina recovered at the start of each new struggle (ratio of the max). 1 = every cycle is as long as the first.")]
        [Range(0.1f, 1.5f)] [SerializeField] float staminaRecoverRatio = 1f;
        [Tooltip("When it wakes up from a tired phase, the fish thrashes for this long (s): it does not tire, reeling " +
                 "slips (no line gained, no strain) and the line cannot break.")]
        [SerializeField] float wakeUpTime = 2.5f;
        [Tooltip("Max tension while it wakes up (stays under the warning / breaking zone).")]
        [Range(0f, 1f)] [SerializeField] float wakeUpMaxTension = 0.6f;

        [Header("Rod angle")]
        [Tooltip("Sideways component of the rod (sin of its angle to the line) giving a perfect counter.")]
        [SerializeField] float fullCounterSide = 0.35f;
        [Tooltip("Counter score above which the rod is 'well placed' (tension drops, the fish tires).")]
        [Range(0f, 1f)] [SerializeField] float goodCounter = 0.3f;
        [Tooltip("Raising the rod also counters the fish (Sea of Thieves): elevation (deg) where it starts to count...")]
        [SerializeField] float raisedRodStart = 30f;
        [Tooltip("...and where it is a perfect counter. Lets you hold the fish when it crosses the middle.")]
        [SerializeField] float raisedRodFull = 55f;
        [Tooltip("How fast a better rod position is taken into account (1/s).")]
        [SerializeField] float counterRiseRate = 12f;
        [Tooltip("How fast a worse rod position is taken into account (1/s): a short grace while you swing the rod over.")]
        [SerializeField] float counterFallRate = 3f;

        [Header("Tension (0..1, snaps at 1)")]
        [Tooltip("Tension per second with the rod pointing with the fish (scaled by fish strength / rod).")]
        [SerializeField] float wrongTensionRate = 0.7f;
        [SerializeField] float relaxRate = 0.6f;
        [Tooltip("Tension per metre reeled beyond the allowance while it struggles.")]
        [SerializeField] float overReelTensionPerMeter = 1f;
        [Tooltip("Tension per second per crank turn/s above the rod's max crank speed.")]
        [SerializeField] float overspeedTensionRate = 0.5f;

        [Header("Stay in the water")]
        [Tooltip("The fish fights only where the water is at least this deep: it turns back at the shallows / shore (m).")]
        [SerializeField] float minFightDepth = 0.6f;
        [Tooltip("Its head keeps at least this height above the lake bed (m).")]
        [SerializeField] float bedClearance = 0.2f;
        [Tooltip("At hook time the fight line may turn up to this angle (deg) toward open water if the shore is in the way.")]
        [SerializeField] float maxAxisTurn = 75f;

        [Header("Landing")]
        [Tooltip("Max weight (kg) that can be grabbed by hand. Heavier fish need the landing net.")]
        [SerializeField] float handCatchMaxKg = 5f;

        [Header("Feedback")]
        [SerializeField] float hapticInterval = 0.08f;
        [SerializeField] float warningTension = 0.7f;

        [Header("Events")]
        public UnityEvent<float> onTensionChanged;
        public UnityEvent<FightPhase> onPhaseChanged;
        [Tooltip("True when the tension enters the danger zone (play a creak), false when it leaves it.")]
        public UnityEvent<bool> onTensionWarning;
        public UnityEvent onLineBroken;
        public UnityEvent<FishInstance> onFishCaught;

        FightPhase phase = FightPhase.None;
        FishInstance fish;
        FishBody body;
        Action<FightResult> onFinished;

        Vector3 anchor;          // angler position (horizontal), smoothed
        Vector3 axis, right;     // fight axis (angler -> fish) fixed at hook time
        float distance;          // horizontal distance angler -> fish along the axis
        float startDistance;
        float lateral, lateralVelocity;
        int direction = 1;
        float switchTimer, allowance;
        float stamina, staminaMax;
        int cyclesTotal, cyclesDone;
        float cycleBudget, budgetUsed;
        float exhaustedTimer, slackTimer;
        float wakeTimer;
        float depth;
        float landLength;
        Vector3 landDir;
        Vector3 head;
        float tension, overTimer, hapticTimer;
        float counter;
        float smoothedCounter;
        float reeledThisFrame;
        bool warning;

        /// <summary>Test hook: forces the rod counter score (-1..1). NaN = use the real rod.</summary>
        public float DebugCounterOverride { get; set; } = float.NaN;

        public FightPhase Phase => phase;
        public FishInstance Fish => fish;
        public FishBody Body => body;
        /// <summary>Just woke up from a tired phase: thrashing, can't be tired or reeled, the line is safe.</summary>
        public bool IsWakingUp => phase == FightPhase.Struggle && wakeTimer > 0f;
        public bool IsFighting => phase is FightPhase.Run or FightPhase.Struggle or FightPhase.Exhausted or FightPhase.Landing;
        public float Tension => tension;
        public float StaminaRatio => staminaMax > 0f ? Mathf.Clamp01(stamina / staminaMax) : 0f;
        public float Counter => counter;
        public float Distance => phase == FightPhase.Landing ? HorizontalDistance(anchor, head) : distance;
        public int CyclesDone => cyclesDone;
        public int CyclesTotal => cyclesTotal;
        public int Direction => direction;
        public Vector3 FishPosition => head;

        FishingLine Line => rod.Line;
        RodStats Stats => rod.Stats;
        FishBehaviourProfile B => fish.Behaviour;

        /// <summary>Start the fight with the fish that just bit (its mouth is on the hook).</summary>
        public void Begin(FishInstance hookedFish, FishBody hookedBody, Action<FightResult> finished)
        {
            fish = hookedFish;
            body = hookedBody;
            onFinished = finished;
            head = body.Head;

            anchor = AnglerPosition();
            axis = Flat(head - anchor);
            if (axis.sqrMagnitude < 1e-4f) axis = Flat(rod.transform.forward);
            axis.Normalize();
            var hookDistance = HorizontalDistance(anchor, head);
            startDistance = Mathf.Max(hookDistance, Stats.fightDistance);
            ChooseFightAxis(hookDistance);
            right = Vector3.Cross(Vector3.up, axis);
            // The fish keeps its place: the fight line may have turned toward open water.
            distance = Vector3.Dot(Flat(head - anchor), axis);
            lateral = Vector3.Dot(Flat(head - anchor), right);
            lateralVelocity = 0f;
            depth = Mathf.Max(0.3f, FishingWater.HeightAt(head) - head.y);

            staminaMax = stamina = fish.MaxStamina;
            cyclesTotal = UnityEngine.Random.Range(B.fatigueCycles.x, B.fatigueCycles.y + 1);
            cyclesDone = 0;
            UpdateCycleBudget();
            tension = 0.2f;
            overTimer = 0f;
            warning = false;

            body.Grabbed += OnFishGrabbed;
            body.Netted += OnFishNetted;
            if (rod.Reel != null) rod.Reel.onLineReeled.AddListener(OnReeled);

            Line.BeginFight(head);
            FishingEvents.RaiseFishHooked(fish);
            SetPhase(distance < startDistance - 0.3f ? FightPhase.Run : FightPhase.Struggle);
            if (phase == FightPhase.Struggle) StartStruggle();
        }

        void OnReeled(float meters) => reeledThisFrame += meters;

        void Update()
        {
            if (!IsFighting || rod == null) return;
            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            var tip = rod.RodTip.position;
            anchor = Vector3.Lerp(anchor, AnglerPosition(), 1f - Mathf.Exp(-3f * dt));
            var reeled = reeledThisFrame;
            reeledThisFrame = 0f;
            var crank = rod.Reel != null ? Mathf.Max(0f, rod.Reel.CrankSpeed) : 0f;
            // How hard this fish is for this rod.
            var strength = Mathf.Clamp(0.6f + fish.weightKg * B.pullPerKg / Mathf.Max(1f, Stats.lineStrength), 0.6f, 2.5f);
            var tensionDelta = 0f;
            var targetDepth = depth;
            var effort = 0.2f;
            var facing = Vector3.zero; // where the mouth points (zero = keep)
            counter = 0f;

            switch (phase)
            {
                case FightPhase.Run:
                {
                    var nextDistance = Mathf.MoveTowards(distance, startDistance, B.runSpeed * dt);
                    var nextLateral = Mathf.MoveTowards(lateral, 0f, Mathf.Max(1f, B.runSpeed * 0.5f) * dt);
                    if (Blocked(nextDistance, nextLateral))
                    {
                        // Shallows ahead: the run ends here.
                        startDistance = distance;
                        UpdateCycleBudget();
                    }
                    else { distance = nextDistance; lateral = nextLateral; }
                    tension = Mathf.MoveTowards(tension, 0.25f, dt);
                    tensionDelta += reeled * overReelTensionPerMeter * strength; // reeling against the run
                    targetDepth = B.depthRange.y;
                    effort = 1f;
                    facing = axis; // runs away
                    if (distance >= startDistance - 0.01f) StartStruggle();
                    break;
                }

                case FightPhase.Struggle:
                {
                    effort = 1f;
                    var waking = wakeTimer > 0f;
                    if (waking) wakeTimer -= dt;
                    targetDepth = Mathf.Lerp(B.depthRange.x, B.depthRange.y, 0.5f + 0.5f * Mathf.Sin(Time.time * 0.7f));
                    var maxLateral = Mathf.Min(B.lateralRange, distance * 0.6f);
                    switchTimer -= dt;
                    if (switchTimer <= 0f || (direction > 0 && lateral >= maxLateral) || (direction < 0 && lateral <= -maxLateral))
                        SwitchDirection();
                    var speed = B.lateralSpeed * (0.6f + 0.4f * StaminaRatio);
                    lateralVelocity = Mathf.MoveTowards(lateralVelocity, direction * speed, B.lateralSpeed * 2f * dt);
                    var nextLateral = lateral + lateralVelocity * dt;
                    // Back inside the range smoothly (the fight line may have turned at hook time).
                    nextLateral = Mathf.MoveTowards(nextLateral, Mathf.Clamp(nextLateral, -maxLateral, maxLateral), B.lateralSpeed * dt);
                    if (Blocked(distance, nextLateral))
                    {
                        // Shore / shallows on this side: it turns back.
                        if (Mathf.Sign(nextLateral - lateral) == direction) SwitchDirection();
                        lateralVelocity = 0f;
                    }
                    else lateral = nextLateral;
                    // Swims sideways while pulling away: it turns through 'away', never toward the angler.
                    facing = right * lateralVelocity + axis * (B.lateralSpeed * 0.6f);

                    var rawCounter = ComputeCounter();
                    smoothedCounter = Mathf.MoveTowards(smoothedCounter, rawCounter,
                        (rawCounter > smoothedCounter ? counterRiseRate : counterFallRate) * dt);
                    counter = smoothedCounter;
                    if (counter >= goodCounter)
                    {
                        tension -= relaxRate * dt;
                        if (!waking) stamina -= dt * (0.5f + counter); // pulling against it tires it
                    }
                    else
                    {
                        tensionDelta += wrongTensionRate * strength * (goodCounter - counter) / (1f + goodCounter) * dt;
                    }

                    // Waking up: the line slips on the reel, no line gained and no strain.
                    if (reeled > 0f && !waking)
                    {
                        var allowed = Mathf.Min(reeled, allowance);
                        allowance -= allowed;
                        distance -= allowed;
                        var over = reeled - allowed;
                        if (over > 0f)
                        {
                            distance -= over * 0.5f;
                            tensionDelta += over * overReelTensionPerMeter * strength;
                        }
                    }

                    if (stamina <= 0f) StartExhausted();
                    break;
                }

                case FightPhase.Exhausted:
                    targetDepth = 0.25f;
                    lateralVelocity = Mathf.MoveTowards(lateralVelocity, 0f, B.lateralSpeed * 2f * dt);
                    lateral = Mathf.MoveTowards(lateral + lateralVelocity * dt, 0f, 0.4f * dt);
                    if (crank <= Stats.maxCrankSpeed) // cranking too hard keeps the line strained
                        tension = Mathf.MoveTowards(tension, 0f, relaxRate * 2f * dt);
                    exhaustedTimer += dt;
                    if (reeled > 0f)
                    {
                        distance -= reeled;
                        budgetUsed += reeled;
                        slackTimer = 0f;
                        facing = -axis; // towed head first toward the angler
                    }
                    else slackTimer += dt;

                    if (slackTimer >= slackTimeout) { Unhook(); return; }
                    if (distance <= landingDistance) StartLanding(tip);
                    else if (budgetUsed >= cycleBudget || exhaustedTimer >= exhaustedMaxDuration)
                    {
                        stamina = staminaMax * staminaRecoverRatio;
                        StartStruggle(true);
                    }
                    break;

                case FightPhase.Landing:
                    effort = 0.3f;
                    facing = Flat(tip - head);
                    if (reeled > 0f) landLength = Mathf.Max(0.25f, landLength - reeled);
                    var lifting = 1f - body.SubmergedFraction;
                    var targetTension = fish.weightKg * 9.81f * lifting / Mathf.Max(1f, Stats.lineStrength);
                    if (crank <= Stats.maxCrankSpeed || tension < targetTension)
                        tension = Mathf.MoveTowards(tension, targetTension, 3f * dt);
                    break;
            }

            // Cranking too fast strains the line whatever the phase (except while it wakes up: the line slips).
            if (crank > Stats.maxCrankSpeed && !IsWakingUp)
                tensionDelta += (crank - Stats.maxCrankSpeed) * overspeedTensionRate * strength * dt;

            tension = Mathf.Clamp(tension + tensionDelta, 0f, IsWakingUp ? wakeUpMaxTension : 1.3f);
            onTensionChanged?.Invoke(tension);

            overTimer = tension >= 1f ? overTimer + dt : 0f;
            if (overTimer >= Stats.breakGraceTime) { Break(); return; }

            // ---------- Fish position ----------
            if (phase == FightPhase.Landing)
                head = LandingHead(tip, dt);
            else
            {
                depth = Mathf.MoveTowards(depth, targetDepth, 0.8f * dt);
                // Small outward arc while it turns around (U-turn), visual only.
                var turn = phase == FightPhase.Struggle ? 1f - Mathf.Clamp01(Mathf.Abs(lateralVelocity) / Mathf.Max(0.1f, B.lateralSpeed * 0.6f)) : 0f;
                var p = anchor + axis * (distance + turn * 0.6f) + right * lateral;
                p.y = FishingWater.ClampUnderwater(p, FishingWater.HeightAt(p) - depth, bedClearance, 0.05f);
                head = p;
            }

            var lifted = body.SubmergedFraction < 0.5f;
            body.SetHead(head, facing);
            body.SetEffort(effort);
            UpdatePermissions(tip, lifted);

            Line.UpdateFight(head, lifted, tension);
            LineCutFX(tip, dt);
            Line.SetLineLength(Vector3.Distance(tip, head));
            rod.LineTension = tension;
            rod.LinePullPoint = head;
            Feedback(dt);
        }

        // ---------- Phases ----------

        /// <param name="wakeUp">Coming out of a tired phase: starts with a thrashing grace period (<see cref="wakeUpTime"/>).</param>
        /// <summary>
        /// The fish runs away from the angler along the fight line. If the shore / shallows are that way, the
        /// line turns (up to <see cref="maxAxisTurn"/>) toward the direction with the most open water, and the
        /// run is shortened to what is swimmable.
        /// </summary>
        void ChooseFightAxis(float hookDistance)
        {
            var original = axis;
            var bestScore = float.NegativeInfinity;
            var bestReach = startDistance;
            for (var angle = 0f; angle <= maxAxisTurn + 0.01f; angle += 15f)
            for (var side = -1; side <= 1; side += 2)
            {
                if (angle == 0f && side > 0) continue;
                var candidate = Quaternion.Euler(0f, angle * side, 0f) * original;
                // From where the fish would be on this line, out to the fight distance.
                var from = Mathf.Max(hookDistance * Mathf.Cos(angle * Mathf.Deg2Rad), landingDistance + 1f);
                var reach = from;
                if (from < startDistance)
                    reach = from + FishingWater.SwimmableDistance(anchor + candidate * from, candidate, startDistance - from, minFightDepth);
                // Prefer the original line (the cast direction): each degree costs 5 cm of run.
                var score = reach - angle * 0.05f;
                if (score > bestScore) { bestScore = score; bestReach = reach; axis = candidate; }
                if (angle == 0f && reach >= startDistance - 0.01f) return; // open water straight ahead
            }
            startDistance = Mathf.Max(hookDistance * Vector3.Dot(original, axis), Mathf.Min(startDistance, bestReach));
        }

        /// <summary>
        /// True if moving the fish to (distance, lateral) on the fight line would take it into water shallower
        /// than <see cref="minFightDepth"/> (unless it gets deeper than where it is: it can always swim out).
        /// </summary>
        bool Blocked(float atDistance, float atLateral)
        {
            var p = anchor + axis * atDistance + right * atLateral;
            var d = FishingWater.DepthAt(p);
            return d < minFightDepth && d < FishingWater.DepthAt(head) - 0.01f;
        }

        float lineFxTimer;

        /// <summary>The line slicing through the water while the fish runs / swims across: spray where it enters.</summary>
        void LineCutFX(Vector3 tip, float dt)
        {
            if (!FX.WaterFX.Exists || (phase != FightPhase.Run && phase != FightPhase.Struggle)) return;
            var water = FishingWater.HeightAt(head);
            if (tip.y <= water || head.y >= water) return;
            var speed = Mathf.Abs(lateralVelocity) + (phase == FightPhase.Run ? B.runSpeed : 0f);
            if (speed < 1f) return;
            lineFxTimer -= dt;
            if (lineFxTimer > 0f) return;
            lineFxTimer = Mathf.Lerp(0.2f, 0.07f, Mathf.InverseLerp(1f, 4f, speed));
            var entry = Vector3.Lerp(tip, head, (tip.y - water) / (tip.y - head.y));
            var side = phase == FightPhase.Run ? axis : right * Mathf.Sign(lateralVelocity);
            FX.WaterFX.Ripple(entry, 0.12f, 0.5f);
            FX.WaterFX.Spray(entry, side * speed * 0.3f + Vector3.up * 0.9f, Mathf.RoundToInt(Mathf.Lerp(1f, 3f, speed / 4f)), 0.4f, 0.6f);
        }

        void UpdateCycleBudget() =>
            cycleBudget = Mathf.Max(1f, (startDistance - landingDistance) / Mathf.Max(1, cyclesTotal));

        void StartStruggle(bool wakeUp = false)
        {
            wakeTimer = wakeUp ? wakeUpTime : 0f;
            if (wakeUp) rod.SendHaptic(0.6f, 0.25f); // the fish jerks awake
            smoothedCounter = 0.5f; // neutral-good start, the player gets a moment to react
            direction = UnityEngine.Random.value < 0.5f ? -1 : 1;
            switchTimer = RandomRange(B.directionChangeInterval);
            allowance = reelAllowanceTurns * (rod.Reel != null ? rod.Reel.MetersPerTurn : 0.7f);
            SetPhase(FightPhase.Struggle);
        }

        void SwitchDirection()
        {
            direction = -direction;
            switchTimer = RandomRange(B.directionChangeInterval);
            allowance = reelAllowanceTurns * (rod.Reel != null ? rod.Reel.MetersPerTurn : 0.7f);
            rod.SendHaptic(0.3f, 0.06f); // the tip jerks to the other side
        }

        void StartExhausted()
        {
            stamina = 0f;
            cyclesDone++;
            budgetUsed = 0f;
            exhaustedTimer = 0f;
            slackTimer = 0f;
            rod.SendHaptic(0.2f, 0.05f);
            SetPhase(FightPhase.Exhausted);
        }

        void StartLanding(Vector3 tip)
        {
            landLength = Vector3.Distance(tip, head);
            landDir = Flat(head - tip);
            if (landDir.sqrMagnitude < 1e-4f) landDir = axis;
            landDir.Normalize();
            SetPhase(FightPhase.Landing);
        }

        /// <summary>
        /// The line (fixed length, shortened by reeling) goes from the rod tip to the fish: while it is longer
        /// than the tip height above the water the fish floats at the surface, then it is lifted under the tip.
        /// </summary>
        Vector3 LandingHead(Vector3 tip, float dt)
        {
            var water = FishingWater.HeightAt(head);
            var height = tip.y - water;
            var horizontal = Mathf.Sqrt(Mathf.Max(0f, landLength * landLength - height * height));
            Vector3 target;
            if (horizontal > 0.02f)
            {
                target = Flat(tip) + landDir * horizontal;
                target.y = water - 0.05f;
                // Dragged up a beach: on the ground, not through it.
                var bed = FishingWater.BedHeight(target);
                if (!float.IsNegativeInfinity(bed)) target.y = Mathf.Max(target.y, bed + 0.04f);
            }
            else target = tip + Vector3.down * landLength;
            return Vector3.Lerp(head, target, 1f - Mathf.Exp(-10f * dt));
        }

        void UpdatePermissions(Vector3 tip, bool lifted)
        {
            var nearSurface = FishingWater.HeightAt(head) - head.y < 0.5f;
            var near = HorizontalDistance(anchor, head) < landingDistance + 2f;
            var tired = phase is FightPhase.Exhausted or FightPhase.Landing;
            body.CanBeNetted = tired && near && (nearSurface || lifted);
            body.CanBeGrabbed = fish.weightKg <= handCatchMaxKg &&
                                (phase == FightPhase.Landing && (lifted || HorizontalDistance(tip, head) < 1.2f));
        }

        /// <summary>
        /// Rod placement score, -1..1. Sideways: +1 when the rod points opposite to the fish's sideways
        /// movement, -1 when it follows it, 0 when it points straight at the fish. Raised: a rod held high
        /// is a good counter whatever the side (Sea of Thieves), which covers the fish crossing the middle.
        /// The best of the two counts.
        /// </summary>
        float ComputeCounter()
        {
            if (!float.IsNaN(DebugCounterOverride)) return Mathf.Clamp(DebugCounterOverride, -1f, 1f);
            var fwd = rod.transform.forward;
            var elevation = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            var raised = Mathf.InverseLerp(raisedRodStart, raisedRodFull, elevation);

            var lineDir = Flat(head - anchor);
            if (lineDir.sqrMagnitude < 1e-4f) return raised;
            var r = Vector3.Cross(Vector3.up, lineDir.normalized);
            var flat = Flat(fwd);
            // Sideways reading fades out as the rod gets vertical (its horizontal direction becomes noise).
            var rodSide = flat.sqrMagnitude > 1e-4f ? Vector3.Dot(flat.normalized, r) * Mathf.Clamp01(flat.magnitude / 0.5f) : 0f;
            var move = Mathf.Abs(lateralVelocity) > 0.2f ? Mathf.Sign(lateralVelocity) : direction;
            var sideways = Mathf.Clamp(-rodSide / fullCounterSide * move, -1f, 1f);
            // A raised rod never makes it worse; a low rod following the fish still counts as wrong (-1).
            return raised > 0f ? Mathf.Max(sideways, raised) : sideways;
        }

        /// <summary>Rod elevation in degrees (for debug / UI).</summary>
        public float RodElevation => Mathf.Asin(Mathf.Clamp(rod.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;

        void Feedback(float dt)
        {
            var danger = tension >= warningTension;
            if (danger != warning)
            {
                warning = danger;
                onTensionWarning?.Invoke(danger);
            }
            hapticTimer -= dt;
            if (hapticTimer > 0f) return;
            hapticTimer = hapticInterval;
            var amp = tension * 0.35f + (danger ? (tension - warningTension) * 2f : 0f);
            if (amp > 0.03f) rod.SendHaptic(Mathf.Clamp01(amp), hapticInterval * 0.8f, danger);
        }

        // ---------- Endings ----------

        void OnFishGrabbed(FishBody b)
        {
            if (!IsFighting || !b.CanBeGrabbed) return;
            Finish(CaptureMethod.Hand);
            b.SetFree();
        }

        void OnFishNetted(FishBody b, LandingNet net)
        {
            if (!IsFighting) return;
            b.PutInNet(net);
            Audio.GameAudio.Play(Audio.SoundId.NetScoop, net.Bag.position);
            Finish(CaptureMethod.LandingNet);
        }

        void Finish(CaptureMethod method)
        {
            SetPhase(FightPhase.Caught);
            EndLine();
            rod.SendHaptic(0.5f, 0.15f, true);
            FishingEvents.RaiseFishCaught(new FishCatchRecord(fish, method));
            onFishCaught?.Invoke(fish);
            Detach();
            onFinished?.Invoke(FightResult.Caught);
        }

        void Break()
        {
            Debug.Log($"[Fish] Line snapped! {fish} ({phase}, tension {tension:0.00})");
            SetPhase(FightPhase.Broken);
            EndLine();
            rod.SendHaptic(1f, 0.4f, true);
            body.Flee(anchor);
            FishingEvents.RaiseLineBroken(fish);
            onLineBroken?.Invoke();
            Detach();
            onFinished?.Invoke(FightResult.LineBroken);
        }

        void Unhook()
        {
            Debug.Log($"[Fish] Slack line, the hook came loose: {fish}");
            SetPhase(FightPhase.Unhooked);
            EndLine();
            rod.SendHaptic(0.4f, 0.1f);
            body.Flee(anchor);
            Detach();
            onFinished?.Invoke(FightResult.Unhooked);
        }

        void EndLine()
        {
            Line.EndFight();
            tension = 0f;
            rod.LineTension = 0f;
            onTensionChanged?.Invoke(0f);
            if (warning) { warning = false; onTensionWarning?.Invoke(false); }
        }

        void Detach()
        {
            if (rod.Reel != null) rod.Reel.onLineReeled.RemoveListener(OnReeled);
            if (body != null)
            {
                body.Grabbed -= OnFishGrabbed;
                body.Netted -= OnFishNetted;
            }
            body = null;
            reeledThisFrame = 0f;
        }

        void SetPhase(FightPhase newPhase)
        {
            if (phase == newPhase) return;
            phase = newPhase;
            onPhaseChanged?.Invoke(phase);
        }

        // ---------- Helpers ----------

        Vector3 AnglerPosition()
        {
            var cam = Camera.main;
            return Flat(cam != null ? cam.transform.position : rod.transform.position);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        static float RandomRange(Vector2 range) => UnityEngine.Random.Range(range.x, range.y);
        static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>Test hook: drain the fish's stamina now.</summary>
        public void DebugExhaust()
        {
            if (phase == FightPhase.Struggle || phase == FightPhase.Run) StartExhausted();
        }
    }
}
