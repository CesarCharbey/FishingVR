using FishingVR.Water;
using UnityEngine;

namespace FishingVR.Boat
{
    /// <summary>
    /// Visual wake of the boat (no effect on the physics):
    /// - the hull pushes the water at the bow and the stern: the interactive ripples turn it into a V-shaped
    ///   wake while moving, and into rings when the boat rocks or is dropped;
    /// - the propeller leaves a foam trail, stronger with the engine speed;
    /// - spray at the bow when going fast, and a splash on impacts or when the propeller falls back in.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class BoatWake : MonoBehaviour
    {
        [Header("Hull ripples")]
        [Tooltip("Bow point at the waterline (boat space).")]
        [SerializeField] Vector3 bowPoint = new(0f, 0.2f, 1.45f);
        [SerializeField, Min(0.05f)] float bowRadius = 0.35f;

        [Tooltip("Stern point at the waterline (boat space).")]
        [SerializeField] Vector3 sternPoint = new(0f, 0.2f, -1.4f);
        [SerializeField, Min(0.05f)] float sternRadius = 0.55f;

        [Tooltip("Water pushed down per second, per m/s of the hull point's horizontal speed. Sets the wake height.")]
        [SerializeField, Min(0f)] float hullStrength = 0.04f;

        [Tooltip("Water pushed per second, per m/s of the hull point's vertical speed (rings when the boat rocks).")]
        [SerializeField, Min(0f)] float rockStrength = 0.05f;

        [Tooltip("Hull points are active within this distance (m) of the surface.")]
        [SerializeField, Min(0.05f)] float contactRange = 0.4f;

        [Header("Foam")]
        [Tooltip("Foam per second left by the propeller at full engine speed.")]
        [SerializeField, Min(0f)] float propellerFoam = 2.5f;
        [SerializeField, Min(0.05f)] float propellerFoamRadius = 0.3f;
        [Tooltip("Churned white water per second at full engine speed: the dense, short-lived foam right behind the propeller (drawn by the lake's Dense Core option).")]
        [SerializeField, Min(0f)] float propellerChurn = 12f;
        [Tooltip("Width of the churned white water behind the propeller (radius, m).")]
        [SerializeField, Min(0.05f)] float propellerChurnRadius = 0.5f;

        [Tooltip("Foam per second at each bow shoulder, per m/s above Bow Foam Speed. It seeds the arms of the V wake.")]
        [SerializeField, Min(0f)] float bowFoam = 0.8f;

        [Tooltip("Forward speed (m/s) above which the bow pushes up white water.")]
        [SerializeField, Min(0f)] float bowFoamSpeed = 1.5f;

        [Tooltip("Sideways distance (m) of the bow shoulders from the centre line, where the V wake starts.")]
        [SerializeField, Min(0f)] float bowShoulder = 0.4f;

        [Header("Spray")]
        [Tooltip("Material of the droplets (M_Water_Spray). Empty = no spray particles.")]
        [SerializeField] Material sprayMaterial;

        [Tooltip("Forward speed (m/s) above which the bow throws spray.")]
        [SerializeField, Min(0f)] float spraySpeed = 2.5f;

        [Tooltip("Droplets per second, per m/s above the spray speed, on each side.")]
        [SerializeField, Min(0f)] float sprayRate = 40f;

        [Tooltip("Droplets of a full-intensity impact splash.")]
        [SerializeField, Min(0)] int impactSplashCount = 60;

        Rigidbody body;
        BoatMotor motor;
        BoatImpacts impacts;
        ParticleSystem sprayLeft, sprayRight, splash;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            motor = GetComponent<BoatMotor>();
            impacts = GetComponent<BoatImpacts>();

            if (sprayMaterial != null)
            {
                sprayLeft = CreateSpray("BowSpray L (generated)", -1f);
                sprayRight = CreateSpray("BowSpray R (generated)", 1f);
                splash = CreateSplash();
            }
        }

        void OnEnable()
        {
            if (impacts != null)
                impacts.onImpact.AddListener(OnImpact);
            if (motor != null)
                motor.onPropellerSubmerged.AddListener(OnPropellerSubmerged);
        }

        void OnDisable()
        {
            if (impacts != null)
                impacts.onImpact.RemoveListener(OnImpact);
            if (motor != null)
                motor.onPropellerSubmerged.RemoveListener(OnPropellerSubmerged);
        }

        void FixedUpdate()
        {
            var water = WaterSurface.Current;
            if (water == null)
                return;

            HullPoint(water, bowPoint, bowRadius, out float bowSpeed);
            HullPoint(water, sternPoint, sternRadius, out _);

            float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
            float overSpray = Mathf.Max(0f, forwardSpeed - spraySpeed);

            // White water at the bow shoulders: where the arms of the V start.
            float overFoam = forwardSpeed - bowFoamSpeed;
            if (overFoam > 0f && bowSpeed >= 0f)
            {
                float foamRate = bowFoam * overFoam;
                float shoulderRadius = bowRadius * 0.6f;
                Vector3 shoulder = bowPoint + new Vector3(bowShoulder, 0f, -0.3f);
                WaterRipples.AddDisturbance(SurfacePoint(water, transform.TransformPoint(shoulder)), shoulderRadius, 0f, foamRate);
                shoulder.x = -bowShoulder;
                WaterRipples.AddDisturbance(SurfacePoint(water, transform.TransformPoint(shoulder)), shoulderRadius, 0f, foamRate);
            }

            // Propeller wash: foam trail, stronger with the engine speed.
            if (motor != null && motor.Propeller != null && motor.IsPropellerSubmerged && motor.EngineRpm > 0.05f)
            {
                Vector3 prop = SurfacePoint(water, motor.Propeller.position);
                WaterRipples.AddDisturbance(prop, propellerFoamRadius, -0.01f * motor.EngineRpm, propellerFoam * motor.EngineRpm);
                WaterRipples.AddDisturbance(prop, propellerChurnRadius, 0f, 0f, propellerChurn * motor.EngineRpm);
            }

            UpdateSpray(sprayLeft, overSpray);
            UpdateSpray(sprayRight, overSpray);
        }

        /// <summary>Pushes the water at a hull point; returns -1 as speed when the point is out of the water.</summary>
        void HullPoint(WaterSurface water, Vector3 localPoint, float radius, out float horizontalSpeed)
        {
            Vector3 point = transform.TransformPoint(localPoint);
            float depth = water.GetDepth(point);
            horizontalSpeed = -1f;
            if (depth < -contactRange || depth > contactRange)
                return;

            Vector3 velocity = body.GetPointVelocity(point);
            horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            float contact = 1f - Mathf.Abs(depth) / contactRange;
            float rate = (hullStrength * horizontalSpeed + rockStrength * Mathf.Abs(velocity.y)) * contact;
            if (rate > 0f)
                WaterRipples.AddDisturbance(SurfacePoint(water, point), radius, -rate);
        }

        static Vector3 SurfacePoint(WaterSurface water, Vector3 p) => new(p.x, water.GetHeight(p), p.z);

        void OnImpact(Vector3 point, float intensity)
        {
            var water = WaterSurface.Current;
            if (water == null || water.GetDepth(point) < -0.5f)
                return; // Hit above the water (dock deck): no splash.

            Vector3 surface = SurfacePoint(water, point);
            WaterRipples.AddImpulse(surface, 0.5f + intensity * 0.5f, -0.04f - 0.08f * intensity, 0.4f + 0.6f * intensity);
            EmitSplash(surface, Mathf.RoundToInt(impactSplashCount * Mathf.Lerp(0.3f, 1f, intensity)), 1.5f + 2.5f * intensity);
        }

        void OnPropellerSubmerged()
        {
            var water = WaterSurface.Current;
            if (water == null || motor.Propeller == null)
                return;
            Vector3 surface = SurfacePoint(water, motor.Propeller.position);
            WaterRipples.AddImpulse(surface, 0.4f, -0.05f, 0.6f);
            EmitSplash(surface, impactSplashCount / 2, 2f);
        }

        // ------------------------------------------------------------------------------------------------
        // Particles (built in code, so the prefab only needs the material)
        // ------------------------------------------------------------------------------------------------

        ParticleSystem CreateSpray(string objectName, float side)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = bowPoint + new Vector3(0.25f * side, 0.05f, -0.15f);
            // Thrown sideways, up and a bit forward.
            go.transform.localRotation = Quaternion.LookRotation(new Vector3(side * 0.9f, 0.55f, 0.35f));

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f);
            main.startColor = new Color(1f, 1f, 1f, 0.75f);
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.08f;

            SetupCommon(ps);
            ps.Play();
            return ps;
        }

        ParticleSystem CreateSplash()
        {
            var go = new GameObject("Splash (generated)");
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = 0f; // Set per burst.
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = new Color(1f, 1f, 1f, 0.8f);
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = false;

            SetupCommon(ps);
            return ps;
        }

        void SetupCommon(ParticleSystem ps)
        {
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = sprayMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void UpdateSpray(ParticleSystem ps, float overSpray)
        {
            if (ps == null)
                return;
            var emission = ps.emission;
            emission.rateOverTime = overSpray * sprayRate;
        }

        void EmitSplash(Vector3 position, int count, float speed)
        {
            if (splash == null || count <= 0)
                return;
            var emitParams = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector2 around = Random.insideUnitCircle;
                emitParams.position = position + new Vector3(around.x, 0f, around.y) * 0.25f;
                emitParams.velocity = new Vector3(around.x * 0.8f, Random.Range(0.6f, 1.2f), around.y * 0.8f) * speed;
                splash.Emit(emitParams, 1);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.TransformPoint(bowPoint), bowRadius);
            Gizmos.DrawWireSphere(transform.TransformPoint(sternPoint), sternRadius);
        }
    }
}
