using UnityEngine;

namespace FishingVR.Fish
{
    /// <summary>Data for one fish species (preliminary, will grow with the bestiary).</summary>
    [CreateAssetMenu(menuName = "FishingVR/Fish Species", fileName = "FS_NewFish")]
    public class FishSpecies : ScriptableObject
    {
        public string displayName = "Fish";
        public GameObject modelPrefab;

        [Tooltip("Relative chance to be picked (higher = more common).")]
        [Min(0f)] public float spawnWeight = 1f;
        public Vector2 weightKgRange = new Vector2(0.5f, 2f);

        [Header("Bite behaviour")]
        [Tooltip("Seconds before the fish comes to the bait once the bobber is settled.")]
        public Vector2 approachDelay = new Vector2(3f, 8f);
        [Tooltip("Number of small nibbles before the real bite.")]
        public Vector2Int nibbleCount = new Vector2Int(1, 4);
        [Tooltip("Seconds between nibbles.")]
        public Vector2 nibbleInterval = new Vector2(0.6f, 1.5f);
        [Tooltip("Seconds the player has to set the hook once the fish bites.")]
        public float biteWindow = 1.2f;
        [Tooltip("How deep the fish drags the bobber during the bite (m).")]
        public float biteSinkDepth = 0.25f;
    }
}
