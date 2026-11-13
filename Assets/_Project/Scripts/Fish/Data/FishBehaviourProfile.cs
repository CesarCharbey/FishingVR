using UnityEngine;

namespace FishingVR.Fish
{
    /// <summary>
    /// How a fish bites and fights. Shared between species (e.g. "Combatif", "Colosse").
    /// Forces are in newtons and scale with the fish weight.
    /// </summary>
    [CreateAssetMenu(menuName = "FishingVR/Fish/Behaviour Profile", fileName = "FB_NewBehaviour")]
    public class FishBehaviourProfile : ScriptableObject
    {
        [Header("Bite")]
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

        [Header("Fight - strength")]
        [Tooltip("Pull force per kg of fish while rushing (N/kg).")]
        public float pullPerKg = 8f;
        [Tooltip("Pull multiplier while resting.")]
        [Range(0f, 1f)] public float restPull = 0.3f;
        [Tooltip("Pull multiplier when exhausted.")]
        [Range(0f, 1f)] public float exhaustedPull = 0.08f;
        [Tooltip("Random variation of the pull (shakes of the head).")]
        [Range(0f, 1f)] public float pullJitter = 0.2f;

        [Header("Fight - stamina")]
        [Tooltip("Seconds of full rush for a 0 kg fish.")]
        public float baseStamina = 5f;
        [Tooltip("Extra seconds of rush per kg.")]
        public float staminaPerKg = 1.5f;
        [Tooltip("Stamina recovered per second while resting.")]
        public float restRecovery = 0.5f;
        [Tooltip("Stamina recovered per second while exhausted (left alone, it comes back).")]
        public float exhaustedRecovery = 0.15f;
        [Tooltip("Exhausted fish starts fighting again above this stamina ratio.")]
        [Range(0f, 1f)] public float wakeUpRatio = 0.3f;

        [Header("Fight - movement")]
        [Tooltip("Swim speed while rushing (m/s).")]
        public float swimSpeed = 2.5f;
        public Vector2 rushDuration = new Vector2(1.5f, 3.5f);
        public Vector2 restDuration = new Vector2(1f, 2.5f);
        [Tooltip("Seconds between direction changes while rushing.")]
        public Vector2 headingChangeInterval = new Vector2(0.6f, 1.8f);
        [Tooltip("Depth below the surface while fighting (m).")]
        public Vector2 depthRange = new Vector2(0.8f, 2f);
    }
}
