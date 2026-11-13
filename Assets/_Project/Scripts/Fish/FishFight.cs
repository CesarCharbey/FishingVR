using System;
using FishingVR.Fishing;
using UnityEngine;
using UnityEngine.Events;

namespace FishingVR.Fish
{
    public enum FightPhase
    {
        None,
        /// <summary>Fish pulls hard and swims away: don't reel, hold the rod against it.</summary>
        Rush,
        /// <summary>Fish catches its breath: reel now.</summary>
        Rest,
        /// <summary>No stamina left: it comes up to the surface, reel it in and land it.</summary>
        Exhausted,
        /// <summary>Out of the water, hanging from the line: grab it by hand.</summary>
        Lifted,
        Caught,
        Broken,
    }

    public enum FightResult { Caught, LineBroken }

    /// <summary>
    /// Fight simulation once a fish is hooked: fish movement, stamina, line tension, drag,
    /// line break, and landing (by hand or with the landing net).
    ///
    /// Tension rules (GDD):
    /// - The fish pulls (weight x pullPerKg) while rushing, much less when resting or exhausted.
    /// - Not reeling: the drag lets line out above RodStats.DragTension, so the tension stays capped.
    /// - Reeling against a pulling fish adds tension (crank speed x reel tension) and bypasses the drag.
    /// - Holding the rod opposite to the fish's movement lowers tension and tires the fish faster.
    /// - Lifting the fish out of the water costs its weight (kg x 9.81 N): heavy fish need the net.
    /// - Tension above the line strength for longer than breakGraceTime = line breaks.
    /// </summary>
    public class FishFight : MonoBehaviour
    {
        [SerializeField] FishingRod rod;

        [Header("Landing")]
        [Tooltip("Max weight (kg) that can be grabbed by hand. Heavier fish need the landing net.")]
        [SerializeField] float handCatchMaxKg = 5f;
        [Tooltip("Horizontal distance from the rod tip under which a tired fish is within landing range (m).")]
        [SerializeField] float landingRange = 2.5f;
        [Tooltip("The net can scoop the fish once its stamina is below this ratio (and it is near the surface).")]
        [Range(0f, 1f)] [SerializeField] float nettableStaminaRatio = 0.35f;

        [Header("Rod angle (GDD: hold the rod opposite to the fish movement)")]
        [Tooltip("Tension reduction at a perfect counter angle.")]
        [Range(0f, 1f)] [SerializeField] float counterTensionReduction = 0.35f;
        [Tooltip("Extra stamina drain at a perfect counter angle.")]
        [SerializeField] float counterFatigueBonus = 1f;

        [Header("Feedback")]
        [SerializeField] float hapticInterval = 0.08f;

        [Header("Events")]
        public UnityEvent<float> onTensionChanged;
        public UnityEvent<FightPhase> onPhaseChanged;
        public UnityEvent onLineBroken;
        public UnityEvent<FishInstance> onFishCaught;

        FightPhase phase = FightPhase.None;
        FishInstance fish;
        FishBody body;
        Action<FightResult> onFinished;

        Vector3 position;     // fish mouth (hook)
        Vector3 velocity;
        Vector3 heading;
        float targetDepth;
        float stamina;
        float phaseTimer, headingTimer, hapticTimer, overTimer;
        float tension;        // smoothed, 0..1+ of line strength
        float counter;        // -1 (bad rod angle) .. 1 (perfect counter)

        public FightPhase Phase => phase;
        public FishInstance Fish => fish;
        public FishBody Body => body;
        public bool IsFighting => phase is FightPhase.Rush or FightPhase.Rest or FightPhase.Exhausted or FightPhase.Lifted;
        public float Tension => tension;
        public float StaminaRatio => fish != null ? Mathf.Clamp01(stamina / fish.MaxStamina) : 0f;
        public float Counter => counter;
        public Vector3 FishPosition => position;

        FishingLine Line => rod.Line;
        RodStats Stats => rod.Stats;
        float Water => WaterLevel.CurrentHeight;

        /// <summary>Start the fight with a freshly hooked fish whose mouth is at <paramref name="hookPosition"/>.</summary>
        public void Begin(FishInstance hookedFish, Vector3 hookPosition, Action<FightResult> finished)
        {
            fish = hookedFish;
            onFinished = finished;
            position = hookPosition;
            velocity = Vector3.zero;
            stamina = fish.MaxStamina;
            targetDepth = UnityEngine.Random.Range(fish.Behaviour.depthRange.x, fish.Behaviour.depthRange.y);
            tension = 0f;
            overTimer = 0f;
            PickHeading();

            body = FishBody.Create(fish, position);
            body.Grabbed += OnFishGrabbed;
            body.Netted += OnFishNetted;

            Line.BeginFight(position);
            FishingEvents.RaiseFishHooked(fish);
            SetPhase(FightPhase.Rush, RandomRange(fish.Behaviour.rushDuration));
        }

        void Update()
        {
            if (!IsFighting || rod == null)
                return;

            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            var b = fish.Behaviour;
            var tip = rod.RodTip.position;
            UpdateStamina(b, dt);

            // ---------- Fish movement (in water) ----------
            if (phase != FightPhase.Lifted)
            {
                headingTimer -= dt;
                if (phase == FightPhase.Rush && headingTimer <= 0f)
                    PickHeading();

                var speed = phase switch
                {
                    FightPhase.Rush => b.swimSpeed * (0.4f + 0.6f * StaminaRatio),
                    FightPhase.Rest => b.swimSpeed * 0.15f,
                    _ => 0.1f,
                };
                var depth = phase == FightPhase.Exhausted ? 0.05f : targetDepth;
                var desired = heading * speed;
                desired.y = (Water - depth - position.y) * 1.5f;
                velocity = Vector3.Lerp(velocity, desired, 1f - Mathf.Exp(-3f * dt));
                position += velocity * dt;
                position.y = Mathf.Min(position.y, Water - 0.02f);
            }

            // ---------- Line & tension ----------
            var pull = PullForce(b);
            var reelSpeed = rod.Reel != null ? Mathf.Max(0f, rod.Reel.CrankSpeed) : 0f;
            var reeling = reelSpeed > 0.05f;
            var effort = Mathf.Clamp01(pull / Mathf.Max(0.01f, fish.weightKg * b.pullPerKg));
            var reelTension = reelSpeed * Stats.reelTensionPerTurnPerSecond * effort;
            counter = ComputeCounter(tip);

            float tensionN;
            var lineLength = Line.LineLength;

            if (phase == FightPhase.Lifted)
            {
                // Hanging under the tip: the line carries the fish weight (+ its struggles).
                position = tip + Vector3.down * lineLength;
                tensionN = fish.weightKg * 9.81f * (1f + 0.25f * Mathf.PerlinNoise(Time.time * 3f, 0f)) + reelTension * 0.3f;
                // Hysteresis: back in the water only once half the body is under again.
                if (position.y < Water + body.Length * 0.5f)
                    SetPhase(FightPhase.Exhausted, 0f);
            }
            else
            {
                var toFish = position - tip;
                var dist = toFish.magnitude;
                if (dist > lineLength)
                {
                    if (!reeling && pull > Stats.DragTension && lineLength < Line.MaxLineLength)
                    {
                        // Drag: the reel gives line, tension capped at the drag setting.
                        Line.SetLineLength(dist);
                        tensionN = Stats.DragTension;
                    }
                    else
                    {
                        // Line holds: the fish is held / dragged toward the angler.
                        position = tip + toFish / dist * lineLength;
                        tensionN = pull + (reeling ? reelTension : 0f);
                        if (lineLength >= Line.MaxLineLength - 0.01f && pull > Stats.DragTension)
                            tensionN = pull * 1.3f; // no line left on the spool
                    }
                }
                else
                {
                    tensionN = 0.05f * pull; // slack line
                }
                tensionN *= 1f - counterTensionReduction * Mathf.Max(0f, counter);
                tensionN *= 1f + 0.25f * Mathf.Max(0f, -counter);

                // Reeled up out of the water (whole body above the surface), close to the angler.
                var hookUnderTip = tip.y - lineLength;
                if (phase == FightPhase.Exhausted && HorizontalDistance(tip, position) < 1f && hookUnderTip > Water + body.Length * 0.9f)
                    SetPhase(FightPhase.Lifted, 0f);
            }

            var target = tensionN / Stats.lineStrength;
            tension = Mathf.Lerp(tension, target, 1f - Mathf.Exp(-10f * dt));
            onTensionChanged?.Invoke(tension);

            // ---------- Break ----------
            overTimer = tension >= 1f ? overTimer + dt : 0f;
            if (overTimer >= Stats.breakGraceTime)
            {
                Break();
                return;
            }

            // ---------- Landing permissions ----------
            var near = HorizontalDistance(tip, position) < landingRange;
            var atSurface = position.y > Water - 0.4f;
            body.CanBeGrabbed = fish.weightKg <= handCatchMaxKg &&
                                (phase == FightPhase.Lifted || (phase == FightPhase.Exhausted && near && atSurface));
            body.CanBeNetted = atSurface || phase == FightPhase.Lifted
                ? phase == FightPhase.Exhausted || phase == FightPhase.Lifted || StaminaRatio <= nettableStaminaRatio
                : false;

            // ---------- Visuals & feedback ----------
            if (phase == FightPhase.Lifted)
                body.SetHanging(position);
            else
                body.SetSwimming(position, velocity, phase == FightPhase.Rush ? 1f : 0.2f);

            Line.UpdateFight(position, phase == FightPhase.Lifted, tension);
            rod.LineTension = tension;
            rod.LinePullPoint = position;

            hapticTimer -= dt;
            if (hapticTimer <= 0f)
            {
                hapticTimer = hapticInterval;
                var amp = Mathf.Clamp01(tension) * 0.5f + (tension > 0.85f ? 0.4f : 0f);
                if (amp > 0.03f)
                    rod.SendHaptic(Mathf.Clamp01(amp), hapticInterval * 0.8f);
            }
        }

        void UpdateStamina(FishBehaviourProfile b, float dt)
        {
            phaseTimer -= dt;
            switch (phase)
            {
                case FightPhase.Rush:
                    var drain = 1f + counterFatigueBonus * Mathf.Max(0f, counter) - 0.4f * Mathf.Max(0f, -counter);
                    // Pulling against the drag / a taut line tires it more.
                    drain *= 0.6f + 0.8f * Mathf.Clamp01(tension);
                    stamina -= dt * drain;
                    if (stamina <= 0f) { stamina = 0f; SetPhase(FightPhase.Exhausted, 0f); }
                    else if (phaseTimer <= 0f) SetPhase(FightPhase.Rest, RandomRange(b.restDuration));
                    break;
                case FightPhase.Rest:
                    stamina = Mathf.Min(fish.MaxStamina, stamina + b.restRecovery * dt);
                    if (phaseTimer <= 0f) { PickHeading(); SetPhase(FightPhase.Rush, RandomRange(b.rushDuration)); }
                    break;
                case FightPhase.Exhausted:
                    stamina = Mathf.Min(fish.MaxStamina, stamina + b.exhaustedRecovery * dt);
                    if (StaminaRatio >= b.wakeUpRatio) { PickHeading(); SetPhase(FightPhase.Rush, RandomRange(b.rushDuration)); }
                    break;
                case FightPhase.Lifted:
                    stamina = Mathf.Min(fish.MaxStamina, stamina + b.exhaustedRecovery * 0.5f * dt);
                    break;
            }
        }

        float PullForce(FishBehaviourProfile b)
        {
            var mult = phase switch
            {
                FightPhase.Rush => 0.5f + 0.5f * StaminaRatio,
                FightPhase.Rest => b.restPull,
                _ => b.exhaustedPull,
            };
            var jitter = 1f + b.pullJitter * (Mathf.PerlinNoise(Time.time * 4f, 7.3f) * 2f - 1f);
            return fish.weightKg * b.pullPerKg * mult * jitter;
        }

        /// <summary>
        /// +1 when the rod points opposite to the fish's sideways movement (or up/back when it swims away),
        /// -1 when the rod follows the fish.
        /// </summary>
        float ComputeCounter(Vector3 tip)
        {
            var lineDir = Vector3.ProjectOnPlane(position - tip, Vector3.up);
            if (lineDir.sqrMagnitude < 0.01f) return 0f;
            lineDir.Normalize();
            var right = Vector3.Cross(Vector3.up, lineDir);

            var rodFwd = rod.transform.forward;
            var rodSide = Vector3.Dot(Vector3.ProjectOnPlane(rodFwd, Vector3.up), right);
            var fishSide = Vector3.Dot(velocity, right);
            var lateral = Mathf.Clamp01(Mathf.Abs(fishSide) / Mathf.Max(0.5f, fish.Behaviour.swimSpeed * 0.5f));
            var sideScore = Mathf.Clamp(-rodSide * 2f * Mathf.Sign(fishSide), -1f, 1f);
            var pitchScore = Mathf.Clamp((rodFwd.y - 0.3f) / 0.4f, -1f, 1f); // rod up = cushions a fish swimming away
            return Mathf.Clamp(lateral * sideScore + (1f - lateral) * pitchScore, -1f, 1f);
        }

        void PickHeading()
        {
            headingTimer = RandomRange(fish.Behaviour.headingChangeInterval);
            var tip = rod.RodTip.position;
            var away = Vector3.ProjectOnPlane(position - tip, Vector3.up).normalized;
            // Mostly away / sideways from the angler.
            for (var i = 0; i < 8; i++)
            {
                var h = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
                if (Vector3.Dot(h, away) > -0.2f) { heading = h; return; }
            }
            heading = away;
        }

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
            Finish(CaptureMethod.LandingNet);
        }

        void Finish(CaptureMethod method)
        {
            SetPhase(FightPhase.Caught, 0f);
            Line.EndFight();
            ResetRod();
            rod.SendHaptic(0.5f, 0.15f, true);
            FishingEvents.RaiseFishCaught(new FishCatchRecord(fish, method));
            onFishCaught?.Invoke(fish);
            Detach();
            onFinished?.Invoke(FightResult.Caught);
        }

        void Break()
        {
            Debug.Log($"[Fish] Line broke! {fish} (tension {tension:0.00})");
            SetPhase(FightPhase.Broken, 0f);
            Line.EndFight();
            ResetRod();
            rod.SendHaptic(1f, 0.4f, true);
            body.Escape();
            FishingEvents.RaiseLineBroken(fish);
            onLineBroken?.Invoke();
            Detach();
            onFinished?.Invoke(FightResult.LineBroken);
        }

        void ResetRod()
        {
            tension = 0f;
            rod.LineTension = 0f;
            onTensionChanged?.Invoke(0f);
        }

        void Detach()
        {
            if (body != null)
            {
                body.Grabbed -= OnFishGrabbed;
                body.Netted -= OnFishNetted;
                if (body.CurrentMode != FishBody.Mode.Free && body.CurrentMode != FishBody.Mode.InNet)
                    body.SetFree();
            }
            body = null;
        }

        void SetPhase(FightPhase newPhase, float duration)
        {
            phaseTimer = duration;
            if (phase == newPhase) return;
            phase = newPhase;
            onPhaseChanged?.Invoke(phase);
        }

        static float RandomRange(Vector2 range) => UnityEngine.Random.Range(range.x, range.y);

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>For tests / debug: drain the fish's stamina.</summary>
        public void DebugExhaust()
        {
            if (!IsFighting) return;
            stamina = 0f;
            SetPhase(FightPhase.Exhausted, 0f);
        }
    }
}
