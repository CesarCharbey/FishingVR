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
        /// <summary>Bobber settled, a fish is coming.</summary>
        Waiting,
        /// <summary>Small touches on the bait: setting the hook now scares the fish.</summary>
        Nibbling,
        /// <summary>Real bite: set the hook before the window ends.</summary>
        Biting,
        /// <summary>Fish on the line: <see cref="FishFight"/> runs the fight.</summary>
        Hooked,
        /// <summary>Fish left (or line broke), short cooldown before another one comes.</summary>
        Escaped,
        /// <summary>Fish landed, short cooldown before another one comes.</summary>
        Caught,
    }

    /// <summary>
    /// Bite phase: picks a species (by rarity weight), waiting, nibbles, bite and hook set.
    /// Once hooked, hands over to <see cref="FishFight"/>.
    /// </summary>
    [RequireComponent(typeof(FishFight))]
    public class FishBiteAI : MonoBehaviour
    {
        [SerializeField] FishingRod rod;
        [SerializeField] List<FishSpecies> species = new List<FishSpecies>();
        [SerializeField] float escapeCooldown = 2f;
        [SerializeField] float nibbleDipDepth = 0.04f;
        [SerializeField] bool debugLogs = true;

        [Header("Events")]
        public UnityEvent<FishInstance> onNibble;
        public UnityEvent<FishInstance> onBite;
        public UnityEvent<FishInstance> onHooked;
        public UnityEvent<FishInstance> onEscaped;

        FishFight fight;
        FishBiteState state = FishBiteState.Idle;
        FishInstance current;
        float timer;
        int nibblesLeft;

        public FishBiteState State => state;
        public FishInstance Current => current;
        public FishFight Fight => fight;
        public IReadOnlyList<FishSpecies> Species => species;

        FishingLine Line => rod != null ? rod.Line : null;

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
            timer -= Time.deltaTime;

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
                    if (timer <= 0f)
                    {
                        var n = current.Behaviour.nibbleCount;
                        nibblesLeft = Random.Range(n.x, n.y + 1);
                        timer = 0f;
                        SetState(FishBiteState.Nibbling);
                    }
                    break;

                case FishBiteState.Nibbling:
                    if (!baitInWater) { Escape("bait pulled out"); break; }
                    if (timer > 0f) break;
                    if (nibblesLeft > 0)
                    {
                        nibblesLeft--;
                        line.Bobber.Dip(nibbleDipDepth, 0.15f);
                        rod.SendHaptic(0.15f, 0.05f);
                        onNibble?.Invoke(current);
                        timer = RandomRange(current.Behaviour.nibbleInterval);
                    }
                    else
                    {
                        line.Bobber.Sink(current.Behaviour.biteSinkDepth, current.Behaviour.biteWindow);
                        rod.SendHaptic(0.7f, 0.25f, true);
                        onBite?.Invoke(current);
                        timer = current.Behaviour.biteWindow;
                        SetState(FishBiteState.Biting);
                    }
                    break;

                case FishBiteState.Biting:
                    if (!baitInWater) { Escape("bait pulled out"); break; }
                    if (timer <= 0f) Escape("too late");
                    break;

                case FishBiteState.Escaped:
                case FishBiteState.Caught:
                    if (timer <= 0f) SetState(FishBiteState.Idle);
                    break;
            }
        }

        /// <summary>Called by the rod's hook-set gesture (public for testing).</summary>
        public void OnHookSetGesture()
        {
            switch (state)
            {
                case FishBiteState.Nibbling:
                    Escape("hook set too early");
                    break;
                case FishBiteState.Biting:
                    Hook();
                    break;
            }
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

        void Hook()
        {
            Line.Bobber.ClearSink();
            rod.SendHaptic(1f, 0.3f, true);
            SetState(FishBiteState.Hooked);
            if (debugLogs) Debug.Log($"[Fish] Hooked {current}");
            onHooked?.Invoke(current);
            fight.Begin(current, Line.Hook.position, OnFightFinished);
        }

        void OnFightFinished(FightResult result)
        {
            timer = escapeCooldown;
            SetState(result == FightResult.Caught ? FishBiteState.Caught : FishBiteState.Escaped);
        }

        void Escape(string reason)
        {
            if (debugLogs) Debug.Log($"[Fish] {current?.species.displayName} escaped: {reason}");
            Line.Bobber.ClearSink();
            onEscaped?.Invoke(current);
            timer = escapeCooldown;
            SetState(FishBiteState.Escaped);
        }

        void SetState(FishBiteState newState)
        {
            if (debugLogs && newState != state)
                Debug.Log($"[Fish] {state} -> {newState}" + (current != null ? $" ({current.species.displayName})" : ""));
            state = newState;
        }

        static float RandomRange(Vector2 r) => Random.Range(r.x, r.y);
    }
}
