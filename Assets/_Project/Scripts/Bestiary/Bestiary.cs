using System;
using System.Collections.Generic;
using System.IO;
using FishingVR.Fish;
using UnityEngine;

namespace FishingVR.Bestiary
{
    /// <summary>What the bestiary knows about one species.</summary>
    [Serializable]
    public class BestiaryEntry
    {
        public string speciesId;
        public int caught;
        public float heaviestKg;
        public float longestCm;
        /// <summary>Total sale value of the fish of this species put in the cooler (for the future economy).</summary>
        public float totalValue;
        public string firstCaught;  // ISO date
        public string lastCaught;   // ISO date
    }

    /// <summary>Result of putting one fish in the cooler.</summary>
    public readonly struct BestiaryResult
    {
        public readonly FishInstance fish;
        public readonly BestiaryEntry entry;
        public readonly bool newSpecies;
        public readonly bool weightRecord;
        public readonly float value;

        public BestiaryResult(FishInstance fish, BestiaryEntry entry, bool newSpecies, bool weightRecord, float value)
        {
            this.fish = fish;
            this.entry = entry;
            this.newSpecies = newSpecies;
            this.weightRecord = weightRecord;
            this.value = value;
        }
    }

    /// <summary>
    /// The player's bestiary: per species, how many were caught, the heaviest / longest one and the total
    /// value, fed by the cooler (a fish counts once it is put in the cooler). Saved as JSON in the persistent
    /// data folder (bestiary.json).
    /// </summary>
    public static class FishBestiary
    {
        [Serializable]
        class SaveData
        {
            public int version = 1;
            public float totalValue;
            public List<BestiaryEntry> entries = new List<BestiaryEntry>();
        }

        static SaveData data;

        /// <summary>A fish was added (cooler). UI, sounds, economy.</summary>
        public static event Action<BestiaryResult> FishRecorded;
        /// <summary>Anything changed (record, reset).</summary>
        public static event Action Changed;

        public static string SavePath => Path.Combine(Application.persistentDataPath, "bestiary.json");

        /// <summary>Sum of the value of every fish put in the cooler.</summary>
        public static float TotalValue { get { Load(); return data.totalValue; } }

        public static int DiscoveredCount { get { Load(); return data.entries.Count; } }

        /// <summary>Entry of a species, or null if never caught.</summary>
        public static BestiaryEntry Get(FishSpecies species)
        {
            Load();
            if (species == null) return null;
            foreach (var e in data.entries)
                if (e.speciesId == species.id) return e;
            return null;
        }

        /// <summary>Add a fish (put in the cooler) and save.</summary>
        public static BestiaryResult Record(FishInstance fish)
        {
            Load();
            var entry = Get(fish.species);
            var isNew = entry == null;
            var now = DateTime.Now.ToString("s");
            if (isNew)
            {
                entry = new BestiaryEntry { speciesId = fish.species.id, firstCaught = now };
                data.entries.Add(entry);
            }
            var record = !isNew && fish.weightKg > entry.heaviestKg;
            var value = fish.Value;
            entry.caught++;
            entry.heaviestKg = Mathf.Max(entry.heaviestKg, fish.weightKg);
            entry.longestCm = Mathf.Max(entry.longestCm, fish.lengthCm);
            entry.totalValue += value;
            entry.lastCaught = now;
            data.totalValue += value;
            Save();

            var result = new BestiaryResult(fish, entry, isNew, record, value);
            Debug.Log($"[Bestiary] {fish}{(isNew ? " NEW SPECIES" : "")}{(record ? " RECORD" : "")} -> {entry.caught} caught, best {entry.heaviestKg:0.00} kg");
            FishRecorded?.Invoke(result);
            Changed?.Invoke();
            return result;
        }

        /// <summary>Forget everything (tests / new game).</summary>
        public static void ResetAll()
        {
            data = new SaveData();
            Save();
            Changed?.Invoke();
        }

        static void Load()
        {
            if (data != null) return;
            data = new SaveData();
            try
            {
                if (File.Exists(SavePath))
                    data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)) ?? new SaveData();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Bestiary] Could not read {SavePath}: {e.Message}");
                data = new SaveData();
            }
        }

        static void Save()
        {
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(data, true)); }
            catch (Exception e) { Debug.LogWarning($"[Bestiary] Could not save {SavePath}: {e.Message}"); }
        }

        // Fresh state when entering play mode without domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            data = null;
            FishRecorded = null;
            Changed = null;
        }
    }
}
