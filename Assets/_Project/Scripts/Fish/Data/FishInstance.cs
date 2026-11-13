using System;
using UnityEngine;

namespace FishingVR.Fish
{
    /// <summary>A specific fish (rolled from a species) during a bite / fight.</summary>
    [Serializable]
    public class FishInstance
    {
        public FishSpecies species;
        public float lengthCm;
        public float weightKg;

        public FishInstance(FishSpecies species, float lengthCm, float weightKg)
        {
            this.species = species;
            this.lengthCm = lengthCm;
            this.weightKg = weightKg;
        }

        public FishBehaviourProfile Behaviour => species.behaviour;
        public float MaxStamina => Behaviour.baseStamina + Behaviour.staminaPerKg * weightKg;
        public float Value => weightKg * species.valuePerKg;

        public override string ToString() => $"{species.displayName} ({lengthCm:0} cm, {weightKg:0.00} kg)";
    }

    public enum CaptureMethod
    {
        Hand,
        LandingNet,
    }

    /// <summary>What the bestiary / economy will receive when a fish is caught.</summary>
    [Serializable]
    public struct FishCatchRecord
    {
        public FishSpecies species;
        public float lengthCm;
        public float weightKg;
        public float value;
        public CaptureMethod method;
        public DateTime time;

        public FishCatchRecord(FishInstance fish, CaptureMethod method)
        {
            species = fish.species;
            lengthCm = fish.lengthCm;
            weightKg = fish.weightKg;
            value = fish.Value;
            this.method = method;
            time = DateTime.Now;
        }
    }

    /// <summary>
    /// Global fishing events. The bestiary, economy, UI and audio can subscribe here
    /// without referencing the rod or the fish.
    /// </summary>
    public static class FishingEvents
    {
        public static event Action<FishCatchRecord> FishCaught;
        public static event Action<FishInstance> LineBroken;
        public static event Action<FishInstance> FishHooked;

        public static void RaiseFishCaught(FishCatchRecord record)
        {
            Debug.Log($"[Fish] Caught {record.species.displayName}: {record.lengthCm:0} cm, {record.weightKg:0.00} kg, {record.value:0} $ ({record.method})");
            FishCaught?.Invoke(record);
        }

        public static void RaiseLineBroken(FishInstance fish) => LineBroken?.Invoke(fish);
        public static void RaiseFishHooked(FishInstance fish) => FishHooked?.Invoke(fish);

        // Reset static listeners when entering play mode without domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            FishCaught = null;
            LineBroken = null;
            FishHooked = null;
        }
    }
}
