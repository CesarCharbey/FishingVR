using UnityEngine;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Rod + line + reel characteristics (upgradeable equipment).
    /// A weak rod can't land heavy fish: the line breaks above <see cref="lineStrength"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "FishingVR/Rod Stats", fileName = "RS_NewRod")]
    public class RodStats : ScriptableObject
    {
        public string displayName = "Canne de base";
        public int level = 1;

        [Tooltip("Tension (N) above which the line breaks. Also the max weight it can lift out of water (N = kg x 9.81).")]
        public float lineStrength = 45f;
        [Tooltip("Fraction of the line strength where the reel drag lets line out (when not reeling).")]
        [Range(0.1f, 1f)] public float dragRatio = 0.65f;
        [Tooltip("Extra tension (N) per crank turn/second when reeling against a pulling fish.")]
        public float reelTensionPerTurnPerSecond = 20f;
        [Tooltip("Seconds the tension can stay above the limit before the line snaps.")]
        public float breakGraceTime = 0.25f;

        public float DragTension => lineStrength * dragRatio;
    }
}
