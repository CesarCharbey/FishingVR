using UnityEngine;

namespace FishingVR.Fish
{
    /// <summary>
    /// How a fish approaches the bait, bites and fights. Shared between species
    /// (e.g. "Combatif", "Colosse").
    /// </summary>
    [CreateAssetMenu(menuName = "FishingVR/Fish/Behaviour Profile", fileName = "FB_NewBehaviour")]
    public class FishBehaviourProfile : ScriptableObject
    {
        [Header("Approach (visible under water)")]
        [Tooltip("Seconds before a fish shows up once the bobber is settled.")]
        [MinMax] public Vector2 approachDelay = new Vector2(2f, 6f);
        [Tooltip("Horizontal distance from the hook where the fish appears (m).")]
        [MinMax] public Vector2 spawnDistance = new Vector2(6f, 9f);
        [Tooltip("Depth under the surface where the fish appears (m), clamped above the lake bed.")]
        [MinMax] public Vector2 spawnDepth = new Vector2(2.5f, 3.5f);
        [Tooltip("Cruise speed while approaching / circling (m/s).")]
        public float cruiseSpeed = 0.8f;
        [Tooltip("Radius of the circles around the hook (m).")]
        [MinMax] public Vector2 circleRadius = new Vector2(0.7f, 1.2f);
        [Tooltip("Laps around the hook before the first nibble.")]
        [MinMax] public Vector2 circleLaps = new Vector2(1f, 2f);

        [Header("Bite")]
        [Tooltip("Number of small nibbles before the real bite.")]
        [MinMax] public Vector2Int nibbleCount = new Vector2Int(1, 3);
        [Tooltip("Seconds between nibbles.")]
        [MinMax] public Vector2 nibbleInterval = new Vector2(0.8f, 1.6f);
        [Tooltip("Seconds the player has to set the hook once the fish bites.")]
        public float biteWindow = 1.2f;
        [Tooltip("How deep the fish drags the bobber during the bite (m).")]
        public float biteSinkDepth = 0.25f;

        [Header("Fight - strength")]
        [Tooltip("Pull per kg (N/kg). Compared to the rod's line strength: the higher, the faster the tension rises.")]
        public float pullPerKg = 8f;
        [Tooltip("Speed of the first run, when the fish pulls the line out to the fight distance (m/s).")]
        public float runSpeed = 5f;

        [Header("Fight - left / right struggle")]
        [Tooltip("Sideways swim speed while struggling (m/s).")]
        public float lateralSpeed = 2.5f;
        [Tooltip("How far it goes sideways from the middle (m).")]
        public float lateralRange = 6f;
        [Tooltip("Seconds between random direction switches (it also turns at the edge of its range).")]
        [MinMax] public Vector2 directionChangeInterval = new Vector2(1.2f, 3f);
        [Tooltip("Depth while struggling (m).")]
        [MinMax] public Vector2 depthRange = new Vector2(0.8f, 1.5f);

        [Header("Fight - fatigue")]
        [Tooltip("Seconds of well countered struggle (rod opposite to its movement) to tire a 0 kg fish.")]
        public float struggleStamina = 4f;
        [Tooltip("Extra seconds per kg.")]
        public float staminaPerKg = 0.5f;
        [Tooltip("How many fatigue cycles it takes to bring the fish in (each tired phase covers a share of the distance).")]
        [MinMax] public Vector2Int fatigueCycles = new Vector2Int(2, 3);
    }
}
