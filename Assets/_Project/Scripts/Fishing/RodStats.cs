using UnityEngine;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Rod + line + reel characteristics (upgradeable equipment).
    /// A weak rod can't land heavy fish: the tension rises faster and lifting them out of water breaks the line.
    /// </summary>
    [CreateAssetMenu(menuName = "FishingVR/Rod Stats", fileName = "RS_NewRod")]
    public class RodStats : ScriptableObject
    {
        public string displayName = "Canne de base";
        public int level = 1;

        [Tooltip("Line strength (N). Fish strength (kg x pullPerKg) is compared to it, and lifting a fish out of " +
                 "the water costs its weight (kg x 9.81 N).")]
        public float lineStrength = 45f;
        [Tooltip("A hooked fish always pulls the line out to this distance (m) before fighting.")]
        public float fightDistance = 18f;
        [Tooltip("Cranking faster than this (turns/s) strains the line.")]
        public float maxCrankSpeed = 2.2f;
        [Tooltip("Seconds the tension can stay at the limit before the line snaps.")]
        public float breakGraceTime = 0.15f;
    }
}
