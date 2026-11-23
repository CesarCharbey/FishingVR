using FishingVR.Fishing;
using UnityEngine;

namespace FishingVR.FX
{
    /// <summary>
    /// Water effects of the bobber: splash when it lands, rings when it floats (now and then) or is
    /// dragged, ripples + droplets on a nibble, splash + bubbles when a fish pulls it under.
    /// </summary>
    [RequireComponent(typeof(Bobber))]
    public class BobberWaterFX : MonoBehaviour
    {
        [Tooltip("Seconds between two rings while the bobber floats still.")]
        [MinMax] [SerializeField] Vector2 idleRippleInterval = new Vector2(2.5f, 5f);
        [Tooltip("Horizontal speed (m/s) above which the bobber leaves a wake when dragged.")]
        [SerializeField] float wakeSpeed = 0.25f;

        Bobber bobber;
        bool wasInWater;
        float idleTimer, wakeTimer;
        Vector3 lastPosition;

        void Awake() => bobber = GetComponent<Bobber>();

        void OnEnable()
        {
            bobber.Dipped += OnDipped;
            lastPosition = transform.position;
            wasInWater = false;
        }

        void OnDisable() => bobber.Dipped -= OnDipped;

        void LateUpdate()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            var pos = transform.position;
            var velocity = (pos - lastPosition) / dt;
            lastPosition = pos;

            // Carried under the rod tip: no effects (also when retracted / in a rack).
            if (bobber.Body.isKinematic)
            {
                wasInWater = false;
                return;
            }

            var inWater = bobber.IsInWater;
            if (inWater && !wasInWater)
            {
                // Lands from the cast: splash scaled by the fall speed.
                var impact = Mathf.Clamp01(-velocity.y / 7f);
                WaterFX.Splash(pos, 0.15f + 0.35f * impact);
                // Only a real fall (not the bobber bobbing back in after landing).
                if (-velocity.y > 1.2f) FishingVR.Audio.GameAudio.Play(FishingVR.Audio.SoundId.BobberLand, pos, 0.4f + 0.6f * impact);
                idleTimer = Random.Range(0.6f, 1.2f);
            }
            wasInWater = inWater;
            if (!inWater) return;

            var flat = new Vector2(velocity.x, velocity.z).magnitude;
            if (flat > wakeSpeed)
            {
                // Dragged by the line (reeling in): small V of rings behind it.
                wakeTimer -= dt;
                if (wakeTimer <= 0f)
                {
                    wakeTimer = Mathf.Lerp(0.2f, 0.08f, Mathf.InverseLerp(wakeSpeed, 2f, flat));
                    var strength = Mathf.InverseLerp(wakeSpeed, 1.5f, flat);
                    WaterFX.Ripple(pos, 0.12f + 0.1f * strength, 0.4f + 0.5f * strength);
                    if (flat > 1.2f) WaterFX.Spray(pos, new Vector3(velocity.x, 0f, velocity.z) * 0.3f + Vector3.up * 0.6f, 1, 0.8f, 0.7f);
                }
                idleTimer = Random.Range(idleRippleInterval.x, idleRippleInterval.y);
            }
            else
            {
                // Floats: a ring now and then (the bobber gently bobbing).
                idleTimer -= dt;
                if (idleTimer <= 0f)
                {
                    idleTimer = Random.Range(idleRippleInterval.x, idleRippleInterval.y);
                    WaterFX.Ripple(pos, 0.14f, 0.3f);
                }
            }
        }

        void OnDipped(float depth, float duration)
        {
            if (!bobber.IsInWater && !wasInWater) return;
            var pos = transform.position;
            var s = Mathf.Clamp01(depth / 0.25f);
            if (s < 0.4f)
            {
                // Nibble: a ring and a few drops.
                WaterFX.Ripple(pos, 0.18f + 0.2f * s, 0.8f);
                FishingVR.Audio.GameAudio.Play(FishingVR.Audio.SoundId.Nibble, pos);
                WaterFX.Spray(pos, Vector3.up * 0.8f, 2 + Mathf.RoundToInt(3f * s), 0.7f, 0.6f);
            }
            else
            {
                // Bite: pulled under.
                WaterFX.Splash(pos, 0.25f + 0.25f * s);
                FishingVR.Audio.GameAudio.Play(FishingVR.Audio.SoundId.Bite, pos);
                WaterFX.Ripple(pos, 0.5f, 1f);
                WaterFX.Bubbles(pos + Vector3.down * 0.12f, 6, 1f);
            }
            idleTimer = Random.Range(idleRippleInterval.x, idleRippleInterval.y);
        }
    }
}
