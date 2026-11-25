using FishingVR.Boat;
using UnityEngine;

namespace FishingVR.Audio
{
    /// <summary>
    /// Boat sounds (added by <see cref="GameAudio"/> on each boat's motor): the engine, started by taking the
    /// tiller, following its speed, and stopping by itself a while after the tiller was let go
    /// (<see cref="GameAudio.engineStopDelay"/>); water rushing along the hull with the speed; knocks on impacts;
    /// a splash when the propeller falls back in the water.
    /// </summary>
    public class BoatAudio : MonoBehaviour
    {
        [SerializeField] Vector2 enginePitch = new Vector2(0.75f, 1.2f);
        [Tooltip("Speed (m/s) at which the water along the hull is loudest.")]
        [SerializeField] float hullFullSpeed = 4f;

        BoatMotor motor;
        TillerInteractable tiller;
        Rigidbody body;
        LoopPlayer engine, hull;
        float engineLevel;
        float engineOnUntil = -1f; // Time.time at which the idling engine stops

        /// <summary>The engine is running (heard, even at idle).</summary>
        public bool EngineOn => Time.time < engineOnUntil;

        void Start()
        {
            motor = GetComponentInParent<BoatMotor>();
            if (motor == null) motor = GetComponentInChildren<BoatMotor>();
            tiller = motor != null ? motor.GetComponentInChildren<TillerInteractable>(true) : null;
            body = GetComponentInParent<Rigidbody>();
            var audio = GameAudio.Instance;
            if (audio == null) { enabled = false; return; }

            engine = new LoopPlayer(gameObject, "EngineAudio", audio.motorLoop, 1f, 1.5f, 3f, 22f);
            engine.SetDistances(2f, 40f);
            hull = new LoopPlayer(gameObject, "HullWaterAudio", audio.hullWaterLoop, 1f, 2f, 2f, 3f);
            hull.SetDistances(1.5f, 25f);

            var impacts = GetComponentInParent<BoatImpacts>();
            if (impacts != null) impacts.onImpact.AddListener(OnImpact);
            if (motor != null) motor.onPropellerSubmerged.AddListener(OnPropellerSubmerged);
        }

        void OnDestroy()
        {
            var impacts = GetComponentInParent<BoatImpacts>();
            if (impacts != null) impacts.onImpact.RemoveListener(OnImpact);
            if (motor != null) motor.onPropellerSubmerged.RemoveListener(OnPropellerSubmerged);
        }

        void Update()
        {
            var audio = GameAudio.Instance;
            if (audio == null || motor == null) return;
            var dt = Time.deltaTime;
            var cat = audio.Category(SoundCategory.Boat);

            // Engine: starts with the tiller (or any throttle), louder with the engine speed; let go, it idles for
            // a moment then stops. Silent otherwise, even when sitting in the boat.
            var rpm = Mathf.Max(0f, motor.EngineRpm);
            if (rpm > 0.02f || (tiller != null && tiller.isSelected)) engineOnUntil = Time.time + audio.engineStopDelay;
            var target = EngineOn ? Mathf.Lerp(audio.engineIdle, audio.engineFull, Mathf.Clamp01(rpm)) : 0f;
            engineLevel = Mathf.MoveTowards(engineLevel, target, dt * (target > engineLevel ? 1.5f : 0.5f));
            if (engine.HasClip)
            {
                var at = motor.Propeller != null ? motor.Propeller.position : motor.transform.position;
                engine.Transform.position = at;
                engine.Update(dt, engineLevel * cat, Mathf.Lerp(enginePitch.x, enginePitch.y, Mathf.Clamp01(rpm / 1.2f)));
            }

            // Water along the hull.
            if (hull.HasClip && body != null)
            {
                var speed = new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude;
                var k = Mathf.Clamp01(speed / hullFullSpeed);
                hull.Transform.position = body.worldCenterOfMass;
                hull.Update(dt, audio.hullWater * k * k * cat, 0.9f + 0.25f * k);
            }
        }

        void OnImpact(Vector3 point, float intensity) => GameAudio.Play(SoundId.BoatImpact, point, 0.3f + 0.7f * Mathf.Clamp01(intensity), 1.1f - 0.3f * Mathf.Clamp01(intensity));

        void OnPropellerSubmerged()
        {
            if (motor.Propeller != null) GameAudio.Play(SoundId.PropellerSplash, motor.Propeller.position, 0.6f);
        }
    }
}
