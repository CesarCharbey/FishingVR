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
        /// <summary>Fish on the line (fight to be implemented).</summary>
        Hooked,
        /// <summary>Fish left, short cooldown before another one comes.</summary>
        Escaped,
        /// <summary>Fish landed, short cooldown before another one comes.</summary>
        Caught,
    }

    /// <summary>
    /// Preliminary fish behaviour: waiting phase, nibbles, bite and hook set.
    /// Listens to the rod's hook-set gesture. The fight is not implemented yet:
    /// a hooked fish hangs on the hook and is "caught" once the line is reeled in.
    /// </summary>
    public class FishBiteAI : MonoBehaviour
    {
        [SerializeField] FishingRod rod;
        [SerializeField] List<FishSpecies> species = new List<FishSpecies>();
        [SerializeField] float escapeCooldown = 2f;
        [SerializeField] float nibbleDipDepth = 0.04f;
        [SerializeField] bool debugLogs = true;

        [Header("Events")]
        public UnityEvent<FishSpecies> onNibble;
        public UnityEvent<FishSpecies> onBite;
        public UnityEvent<FishSpecies, float> onHooked;
        public UnityEvent<FishSpecies> onEscaped;
        public UnityEvent<FishSpecies, float> onCaught;

        FishBiteState state = FishBiteState.Idle;
        FishSpecies current;
        float currentWeight;
        float timer;
        int nibblesLeft;
        GameObject hookedModel;
        GameObject caughtModel;

        public FishBiteState State => state;
        public FishSpecies CurrentSpecies => current;

        FishingLine Line => rod != null ? rod.Line : null;

        void OnEnable()
        {
            if (rod == null) return;
            rod.onHookSetGesture.AddListener(OnHookSetGesture);
            rod.onCast.AddListener(OnRodCast);
        }

        void OnDisable()
        {
            if (rod == null) return;
            rod.onHookSetGesture.RemoveListener(OnHookSetGesture);
            rod.onCast.RemoveListener(OnRodCast);
        }

        // A caught fish stays on the hook until the next cast (no bucket/bestiary yet).
        void OnRodCast(Vector3 _)
        {
            if (caughtModel != null)
                Destroy(caughtModel);
            caughtModel = null;
        }

        void Update()
        {
            var line = Line;
            if (line == null)
                return;

            var baitInWater = line.State == LineState.Deployed && line.Bobber.IsInWater;
            timer -= Time.deltaTime;

            switch (state)
            {
                case FishBiteState.Idle:
                    if (baitInWater && line.Bobber.IsSettled && PickSpecies())
                    {
                        timer = Random.Range(current.approachDelay.x, current.approachDelay.y);
                        SetState(FishBiteState.Waiting);
                    }
                    break;

                case FishBiteState.Waiting:
                    if (!baitInWater) { SetState(FishBiteState.Idle); break; }
                    if (timer <= 0f)
                    {
                        nibblesLeft = Random.Range(current.nibbleCount.x, current.nibbleCount.y + 1);
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
                        timer = Random.Range(current.nibbleInterval.x, current.nibbleInterval.y);
                    }
                    else
                    {
                        line.Bobber.Sink(current.biteSinkDepth, current.biteWindow);
                        rod.SendHaptic(0.7f, 0.25f, true);
                        onBite?.Invoke(current);
                        timer = current.biteWindow;
                        SetState(FishBiteState.Biting);
                    }
                    break;

                case FishBiteState.Biting:
                    if (timer <= 0f) Escape("too late");
                    break;

                case FishBiteState.Hooked:
                    if (line.State == LineState.Retracted)
                        Catch();
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

        bool PickSpecies()
        {
            var total = 0f;
            foreach (var s in species) if (s != null) total += s.spawnWeight;
            if (total <= 0f) return false;

            var pick = Random.value * total;
            foreach (var s in species)
            {
                if (s == null) continue;
                pick -= s.spawnWeight;
                if (pick <= 0f) { current = s; break; }
            }
            currentWeight = Random.Range(current.weightKgRange.x, current.weightKgRange.y);
            return true;
        }

        void Hook()
        {
            Line.Bobber.ClearSink();
            rod.SendHaptic(1f, 0.3f, true);
            SpawnHookedModel();
            SetState(FishBiteState.Hooked);
            onHooked?.Invoke(current, currentWeight);
        }

        void Catch()
        {
            if (debugLogs) Debug.Log($"[Fish] Caught {current.displayName} ({currentWeight:0.00} kg)");
            onCaught?.Invoke(current, currentWeight);
            caughtModel = hookedModel;
            hookedModel = null;
            timer = escapeCooldown;
            SetState(FishBiteState.Caught);
        }

        void Escape(string reason)
        {
            if (debugLogs) Debug.Log($"[Fish] {current?.displayName} escaped: {reason}");
            Line.Bobber.ClearSink();
            onEscaped?.Invoke(current);
            timer = escapeCooldown;
            SetState(FishBiteState.Escaped);
        }

        void SpawnHookedModel()
        {
            var hook = Line.Hook;
            if (current.modelPrefab == null || hook == null)
                return;

            hookedModel = HangingFish.Create(current.modelPrefab, hook).gameObject;
        }

        void SetState(FishBiteState newState)
        {
            if (debugLogs && newState != state)
                Debug.Log($"[Fish] {state} -> {newState}" + (current != null ? $" ({current.displayName})" : ""));
            state = newState;
        }
    }
}
