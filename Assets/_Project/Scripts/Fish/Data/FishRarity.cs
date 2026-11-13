using UnityEngine;

namespace FishingVR.Fish
{
    public enum FishRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary,
    }

    public static class FishRarityExtensions
    {
        /// <summary>Default spawn weight when a species doesn't override it.</summary>
        public static float DefaultSpawnWeight(this FishRarity rarity) => rarity switch
        {
            FishRarity.Common => 10f,
            FishRarity.Uncommon => 5f,
            FishRarity.Rare => 2f,
            FishRarity.Epic => 0.7f,
            FishRarity.Legendary => 0.2f,
            _ => 1f,
        };

        public static string DisplayName(this FishRarity rarity) => rarity switch
        {
            FishRarity.Common => "Commun",
            FishRarity.Uncommon => "Peu commun",
            FishRarity.Rare => "Rare",
            FishRarity.Epic => "Épique",
            FishRarity.Legendary => "Légendaire",
            _ => rarity.ToString(),
        };

        public static Color DisplayColor(this FishRarity rarity) => rarity switch
        {
            FishRarity.Common => new Color(0.8f, 0.8f, 0.8f),
            FishRarity.Uncommon => new Color(0.35f, 0.8f, 0.35f),
            FishRarity.Rare => new Color(0.3f, 0.55f, 1f),
            FishRarity.Epic => new Color(0.7f, 0.35f, 0.95f),
            FishRarity.Legendary => new Color(1f, 0.7f, 0.15f),
            _ => Color.white,
        };
    }
}
