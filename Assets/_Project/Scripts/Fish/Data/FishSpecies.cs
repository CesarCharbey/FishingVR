using UnityEngine;

namespace FishingVR.Fish
{
    /// <summary>One fish species: identity, size, rarity, value and behaviour.</summary>
    [CreateAssetMenu(menuName = "FishingVR/Fish/Species", fileName = "FS_NewFish")]
    public class FishSpecies : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used for saves / bestiary (don't change once shipped).")]
        public string id = "fish";
        public string displayName = "Fish";
        [TextArea] public string description;
        [Tooltip("Bestiary icon (optional for now).")]
        public Sprite icon;
        public FishRarity rarity = FishRarity.Common;
        [Tooltip("Relative spawn chance. 0 = use the rarity default.")]
        [Min(0f)] public float spawnWeightOverride;

        [Header("Visual")]
        [Tooltip("Model facing +Z with a SOCKET_Mouth child.")]
        public GameObject modelPrefab;

        [Header("Size")]
        [MinMax] public Vector2 lengthCmRange = new Vector2(20f, 40f);
        [MinMax] public Vector2 weightKgRange = new Vector2(0.2f, 1f);
        [Tooltip("1 = uniform sizes, higher = big fish are rarer.")]
        [Range(1f, 4f)] public float sizeRarityBias = 2f;

        [Header("Economy")]
        [Tooltip("Sale value per kg.")]
        public float valuePerKg = 10f;

        [Header("Behaviour")]
        public FishBehaviourProfile behaviour;

        public float SpawnWeight => spawnWeightOverride > 0f ? spawnWeightOverride : rarity.DefaultSpawnWeight();

        /// <summary>Roll a fish of this species (length and weight are correlated).</summary>
        public FishInstance Roll() => Roll(1f);

        /// <summary>
        /// Rolls a fish with the size bias scaled: &gt; 1 makes big fish rarer (outside the fishing spots),
        /// &lt; 1 makes them more common (in a spot). The weight still follows the size, as in <see cref="Roll()"/>.
        /// </summary>
        public FishInstance Roll(float sizeBiasScale)
        {
            var bias = Mathf.Clamp(sizeRarityBias * Mathf.Max(0.05f, sizeBiasScale), 0.3f, 8f);
            var t = Mathf.Pow(Random.value, bias);
            var length = Mathf.Lerp(lengthCmRange.x, lengthCmRange.y, t);
            var weightT = Mathf.Clamp01(t + Random.Range(-0.08f, 0.08f));
            var weight = Mathf.Lerp(weightKgRange.x, weightKgRange.y, weightT);
            return new FishInstance(this, length, weight);
        }
    }
}
