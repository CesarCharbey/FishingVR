using FishingVR.Fish;
using FishingVR.Fishing;
using UnityEngine;

namespace FishingVR.FX
{
    /// <summary>
    /// Water effects of a fish, from its movement (added to every fish by FishBody.Create):
    /// - fast under water: trail of bubbles, and a wake on the surface (rings, droplets) when shallow enough;
    /// - sharp turns near the surface: a swirl of foam (the fish "boils" the water);
    /// - breaking the surface (in or out): splash;
    /// - thrashing at the surface: splashes;
    /// - out of the water: drips for a few seconds.
    /// Strength grows with the fish's length.
    /// </summary>
    [RequireComponent(typeof(FishBody))]
    public class FishWaterFX : MonoBehaviour
    {
        [Tooltip("Below this depth (m) the fish leaves no wake on the surface.")]
        [SerializeField] float wakeMaxDepth = 1.8f;
        [Tooltip("Speed (m/s) from which it leaves a wake / bubbles.")]
        [SerializeField] float wakeSpeed = 0.9f;
        [Tooltip("Acceleration (m/s²) of a sharp turn that swirls the water.")]
        [SerializeField] float swirlAcceleration = 3f;
        [Tooltip("Seconds it keeps dripping once out of the water.")]
        [SerializeField] float dripTime = 4f;

        FishBody body;
        Vector3 lastHead, velocity, lastVelocity;
        bool initialized, wasUnder;
        float wakeTimer, bubbleTimer, thrashTimer, dripTimer, swirlCooldown, wetTimer;

        void Awake() => body = GetComponent<FishBody>();

        void LateUpdate()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f || !WaterFX.Exists) return;
            var head = body.Head;
            var water = FishingWater.HeightAt(head);
            if (float.IsNegativeInfinity(water)) return;

            var depth = water - head.y;
            if (!initialized)
            {
                initialized = true;
                lastHead = head;
                wasUnder = depth > 0.02f;
                return;
            }

            var raw = (head - lastHead) / dt;
            lastHead = head;
            if (raw.sqrMagnitude > 400f) raw = Vector3.zero; // teleport (net, grab), not a movement
            lastVelocity = velocity;
            velocity = Vector3.Lerp(velocity, raw, 1f - Mathf.Exp(-12f * dt));
            var acceleration = (velocity - lastVelocity) / dt;
            swirlCooldown -= dt;

            var size = Mathf.Clamp(body.Length / 0.35f, 0.75f, 3f);
            var mode = body.CurrentMode;
            if (mode == FishBody.Mode.InNet || body.IsGrabbed)
            {
                Drips(dt, size);
                return;
            }

            var under = depth > 0.02f;
            var surface = new Vector3(head.x, water, head.z);

            // Breaking the surface.
            if (under != wasUnder && Mathf.Abs(velocity.y) > 0.35f)
            {
                WaterFX.Splash(surface, Mathf.Clamp01(Mathf.Abs(velocity.y) / 3f) * 0.8f * size);
                FishingVR.Audio.GameAudio.Play(FishingVR.Audio.SoundId.FishSplash, surface, Mathf.Clamp01(Mathf.Abs(velocity.y) / 3f) * Mathf.Sqrt(size), 1.15f - 0.15f * Mathf.Clamp01(size - 1f));
            }
            wasUnder = under;

            var submerged = body.SubmergedFraction;
            if (under && submerged > 0.95f)
            {
                wetTimer = dripTime;
                UnderwaterFX(dt, head, surface, depth, size, acceleration);
            }
            else if (submerged > 0.05f)
            {
                // At the surface (tired, reeled in, hanging half out): it thrashes.
                wetTimer = dripTime;
                thrashTimer -= dt * (0.6f + 1.4f * body.Effort);
                if (thrashTimer <= 0f)
                {
                    thrashTimer = Random.Range(0.25f, 0.7f);
                    var tail = head + transform.forward * body.Length;
                    var at = Vector3.Lerp(head, tail, Random.Range(0.3f, 1f));
                    WaterFX.Splash(at, (0.15f + 0.35f * body.Effort) * size);
                    FishingVR.Audio.GameAudio.Play(FishingVR.Audio.SoundId.FishSplash, at, (0.25f + 0.45f * body.Effort) * Mathf.Sqrt(size), 1.2f - 0.15f * Mathf.Clamp01(size - 1f));
                }
                // Dragged at the surface: a wake of rings.
                var flat = new Vector2(velocity.x, velocity.z).magnitude;
                if (flat > 0.4f) SurfaceWake(dt, surface, flat, 1f, size);
            }
            else Drips(dt, size);
        }

        void UnderwaterFX(float dt, Vector3 head, Vector3 surface, float depth, float size, Vector3 acceleration)
        {
            var speed = velocity.magnitude;
            var flat = new Vector2(velocity.x, velocity.z).magnitude;

            // Bubbles from the gills / tail when it swims fast.
            if (speed > wakeSpeed)
            {
                bubbleTimer -= dt * speed;
                if (bubbleTimer <= 0f)
                {
                    bubbleTimer = 0.35f;
                    WaterFX.Bubbles(head + transform.forward * body.Length * 0.6f, Random.Range(1, 3), Mathf.Sqrt(size));
                }
            }

            // Wake on the surface, fading with depth.
            var deep = Mathf.Clamp01(depth / (wakeMaxDepth * Mathf.Sqrt(size)));
            var shallow = 1f - deep * deep;
            if (shallow > 0f && flat > wakeSpeed) SurfaceWake(dt, surface, flat, shallow, size);

            // Sharp turn close to the surface: swirl.
            var flatAccel = new Vector2(acceleration.x, acceleration.z).magnitude;
            if (shallow > 0.2f && flatAccel > swirlAcceleration && swirlCooldown <= 0f)
            {
                swirlCooldown = 0.5f;
                var s = shallow * Mathf.Clamp01(flatAccel / (swirlAcceleration * 3f));
                WaterFX.Ripple(surface, (0.3f + 0.4f * s) * size, 0.6f + 0.4f * s);
                WaterFX.Foam(surface, 0.15f * size, s);
                if (depth < 1f)
                    WaterFX.Spray(surface, new Vector3(velocity.x, 0f, velocity.z) * 0.3f + Vector3.up * 1.2f, Mathf.RoundToInt(3f * s * size) + 1, 0.6f);
            }
        }

        /// <summary>Rings (and droplets when fast and shallow) above a fish moving near the surface.</summary>
        void SurfaceWake(float dt, Vector3 surface, float flatSpeed, float shallow, float size)
        {
            wakeTimer -= dt;
            if (wakeTimer > 0f) return;
            var fast = Mathf.InverseLerp(wakeSpeed, 4f, flatSpeed);
            wakeTimer = Mathf.Lerp(0.16f, 0.06f, fast);
            var strength = Mathf.Min(1f, shallow * (0.6f + 0.6f * fast));
            WaterFX.Ripple(surface, (0.25f + 0.3f * fast) * size, strength);
            if (shallow > 0.45f && fast > 0.2f)
            {
                // Droplets thrown sideways, like a bow wave: the faster, the more.
                var dir = new Vector3(velocity.x, 0f, velocity.z).normalized;
                var side = Vector3.Cross(Vector3.up, dir) * (Random.value < 0.5f ? -1f : 1f);
                var count = Mathf.RoundToInt(Mathf.Lerp(0f, 4f, fast * shallow) * Mathf.Sqrt(size));
                WaterFX.Spray(surface, dir * flatSpeed * 0.25f + side * 0.8f + Vector3.up * (0.8f + fast), count, 0.4f);
                if (fast > 0.5f) WaterFX.Foam(surface - dir * 0.1f, 0.1f * size, 0.5f * strength);
            }
        }

        void Drips(float dt, float size)
        {
            wetTimer -= dt;
            if (wetTimer <= 0f) return;
            dripTimer -= dt;
            if (dripTimer > 0f) return;
            // Less and less as it dries.
            dripTimer = Mathf.Lerp(0.6f, 0.06f, wetTimer / dripTime) / Mathf.Sqrt(size);
            var head = body.Head;
            var tail = head + transform.forward * body.Length;
            var drip = Vector3.Lerp(head, tail, Random.Range(0.2f, 1f)) + Vector3.down * 0.02f;
            WaterFX.Drip(drip);
            if (Random.value < 0.3f) FishingVR.Audio.GameAudio.Play(FishingVR.Audio.SoundId.Drip, drip, 0.5f);
        }
    }
}
