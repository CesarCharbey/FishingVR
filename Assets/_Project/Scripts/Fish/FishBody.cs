using System;
using FishingVR.Fishing;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FishingVR.Fish
{
    /// <summary>
    /// The visible, grabbable fish. Pivot = mouth (where the hook is), +Z = from the mouth toward the tail.
    ///
    /// Motion model:
    /// - The HEAD is driven: by steering (<see cref="SwimTowards"/>: approach, circling, flee) or set
    ///   directly by the fight (<see cref="SetHead"/>).
    /// - The HEADING (where the mouth points) turns toward the swim direction (or the facing given by the
    ///   fight) with a limited yaw rate and a limited pitch, like a real fish turning. It never flips.
    /// - The BODY (centre of mass, half a length behind the mouth) sits behind the head along the heading
    ///   while it is in the water, floats at the surface when the head is lifted out, and hangs under
    ///   gravity (pendulum) once it is out of the water. So the fish swims level, floats flat at the
    ///   surface, then tilts and hangs from the mouth as it is reeled up.
    /// - The back is kept up (no roll), plus a tail beat.
    ///
    /// Modes: Driven (hooked / approaching), Fleeing (swims away and fades out), InNet, Free (caught,
    /// normal physics object). Hand capture: near grab only, when <see cref="CanBeGrabbed"/>.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the rod tip / line have moved
    public class FishBody : MonoBehaviour, IXRSelectFilter
    {
        public enum Mode { Driven, Fleeing, InNet, Free }

        [SerializeField] float handReach = 0.3f;
        [Header("Swimming")]
        [Tooltip("Max turn rate of the heading (deg/s) for a 30 cm fish; bigger fish turn slower.")]
        [SerializeField] float yawRate = 320f;
        [SerializeField] float maxPitch = 30f;
        [Tooltip("How fast the body follows the heading under water (1/s).")]
        [SerializeField] float bodyFollow = 12f;
        [Header("Out of water")]
        [Tooltip("Damping of the hanging swing (1/s): the fish settles under the hook instead of swinging forever.")]
        [SerializeField] float airDrag = 2.5f;
        [Tooltip("Drag of the body in the water while the head is at the surface / lifted (1/s).")]
        [SerializeField] float waterDragAtSurface = 8f;
        [Header("Out of water struggle")]
        [SerializeField] float struggleKick = 0.25f;
        [SerializeField] Vector2 struggleInterval = new Vector2(0.4f, 1.6f);
        [Header("Under water visibility")]
        [Tooltip("Fully visible above this depth (m).")]
        [SerializeField] float clearDepth = 0.8f;
        [Tooltip("Invisible below clearDepth + fadeRange (m).")]
        [SerializeField] float fadeRange = 2.2f;

        Mode mode = Mode.Driven;
        XRGrabInteractable grab;
        Rigidbody body;
        CapsuleCollider capsule;
        float length;
        Vector3 head;
        Vector3 swimVelocity;
        Vector3 com, prevCom;
        Vector3 bodyOffset;             // com - head
        Vector3 upRef = Vector3.up;
        float yaw, pitch;               // current heading (degrees)
        Vector3 facingRequest;          // heading asked by the fight this frame (zero = follow the swim direction)
        float effortOverride;
        float wigglePhase;
        float nextKick;
        float releasedTimer = -1f;
        float fleeTimer;
        Vector3 fleeTarget;

        Renderer[] renderers;
        Material[][] originalMaterials;
        Material fadeMaterial;
        bool fadeApplied;
        float fleeAlpha = 1f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public FishInstance Fish { get; private set; }
        public Mode CurrentMode => mode;
        public float Length => length;
        public Vector3 Head => head;
        public Vector3 Velocity => swimVelocity;
        public bool IsGrabbed => grab != null && grab.isSelected;
        /// <summary>Set by the fight: the fish may be taken by hand right now.</summary>
        public bool CanBeGrabbed { get; set; }
        /// <summary>Set by the fight: the fish may be scooped by the landing net right now.</summary>
        public bool CanBeNetted { get; set; }

        /// <summary>0 = fully out of the water, 1 = fully under water (mouth to tail).</summary>
        public float SubmergedFraction
        {
            get
            {
                var tail = head + (com - head) * 2f;
                var water = FishingWater.HeightAt(head);
                var lo = Mathf.Min(head.y, tail.y);
                var hi = Mathf.Max(head.y, tail.y);
                if (hi <= water) return 1f;
                if (lo >= water) return 0f;
                return Mathf.Clamp01((water - lo) / Mathf.Max(0.001f, hi - lo));
            }
        }

        /// <summary>Raised when a hand grabs the fish.</summary>
        public event Action<FishBody> Grabbed;
        /// <summary>Raised when the landing net scoops a nettable fish.</summary>
        public event Action<FishBody, LandingNet> Netted;

        /// <summary>
        /// Spawn a fish with its mouth at <paramref name="headPosition"/>, facing <paramref name="facing"/>.
        /// <paramref name="underwaterMaterial"/> (optional) is a transparent version of the palette used to
        /// fade the fish with depth.
        /// </summary>
        public static FishBody Create(FishInstance fish, Vector3 headPosition, Vector3 facing, Material underwaterMaterial)
        {
            var species = fish.species;
            var root = new GameObject($"Fish_{species.displayName}");
            var fb = root.AddComponent<FishBody>();
            fb.Fish = fish;

            // Model: nose (+Z in its prefab) turned to -Z so the mouth is at the origin and the body extends along +Z.
            var model = Instantiate(species.modelPrefab, root.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
            var renderer = model.GetComponentInChildren<Renderer>();
            var nativeLength = renderer != null ? renderer.bounds.size.z : 0.5f;
            var scale = Mathf.Max(0.05f, fish.lengthCm / 100f / Mathf.Max(nativeLength, 0.01f));
            model.transform.localScale *= scale;
            var mouth = FindDeep(model.transform, "SOCKET_Mouth");
            if (mouth != null)
                model.transform.localPosition = -root.transform.InverseTransformPoint(mouth.position);
            fb.length = fish.lengthCm / 100f;

            // Physics + grab (kinematic while driven).
            var bounds = renderer != null ? renderer.bounds : new Bounds(Vector3.forward * fb.length * 0.5f, Vector3.one * 0.1f); // root is still at the origin
            fb.capsule = root.AddComponent<CapsuleCollider>();
            fb.capsule.direction = 2;
            fb.capsule.center = new Vector3(0f, bounds.center.y, fb.length * 0.5f);
            fb.capsule.height = fb.length;
            fb.capsule.radius = Mathf.Max(0.02f, Mathf.Min(bounds.size.x, bounds.size.y) * 0.5f);

            fb.body = root.AddComponent<Rigidbody>();
            fb.body.mass = Mathf.Max(0.1f, fish.weightKg);
            fb.body.isKinematic = true;
            fb.body.interpolation = RigidbodyInterpolation.None; // transform driven by script

            fb.grab = root.AddComponent<XRGrabInteractable>();
            fb.grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            fb.grab.useDynamicAttach = true;
            fb.grab.throwOnDetach = true;
            fb.grab.selectFilters.Add(fb);
            fb.grab.selectEntered.AddListener(fb.OnGrabbed);
            fb.grab.selectExited.AddListener(fb.OnReleased);

            // Visibility fade.
            fb.renderers = root.GetComponentsInChildren<Renderer>();
            fb.originalMaterials = new Material[fb.renderers.Length][];
            for (var i = 0; i < fb.renderers.Length; i++) fb.originalMaterials[i] = fb.renderers[i].sharedMaterials;
            if (underwaterMaterial != null) fb.fadeMaterial = new Material(underwaterMaterial);

            facing = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (facing.sqrMagnitude < 1e-4f) facing = Vector3.forward;
            facing.Normalize();
            fb.head = headPosition;
            fb.yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            fb.bodyOffset = -facing * fb.length * 0.5f;
            fb.com = fb.prevCom = headPosition + fb.bodyOffset;
            root.transform.SetPositionAndRotation(headPosition, Quaternion.LookRotation(-facing, Vector3.up));
            fb.UpdateVisibility();
            return fb;
        }

        // ---------- Head control ----------

        /// <summary>
        /// Place the head directly (fight / landing). <paramref name="facing"/> = where the mouth should point
        /// (the heading turns toward it at the fish's turn rate); zero = keep the current heading.
        /// </summary>
        public void SetHead(Vector3 position, Vector3 facing = default)
        {
            if (mode != Mode.Driven || IsGrabbed) return;
            var dt = Time.deltaTime;
            if (dt > 0f) swimVelocity = (position - head) / dt;
            head = position;
            facingRequest = facing.sqrMagnitude > 1e-4f ? facing : HeadingVector();
        }

        /// <summary>
        /// Steer the head toward <paramref name="target"/> like a fish: limited turn rate, smooth
        /// acceleration, pitch limited, stays under the surface. Call once per frame.
        /// </summary>
        public void SwimTowards(Vector3 target, float speed, float turnRateDeg = 120f, float arriveRadius = 0f, float acceleration = 2f)
        {
            if ((mode != Mode.Driven && mode != Mode.Fleeing) || IsGrabbed) return;
            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            var to = target - head;
            var dist = to.magnitude;
            var desiredSpeed = arriveRadius > 0f ? speed * Mathf.Clamp01(dist / arriveRadius) : speed;
            var current = swimVelocity.sqrMagnitude > 1e-4f ? swimVelocity.normalized : HeadingVector();
            var desired = dist > 1e-4f ? to / dist : current;
            // Avoid an undefined rotation axis on a U-turn: turn sideways.
            if (Vector3.Dot(current, desired) < -0.98f)
                desired = Quaternion.Euler(0f, 35f, 0f) * desired;

            var dir = Vector3.RotateTowards(current, desired, turnRateDeg * Mathf.Deg2Rad * dt, 0f);
            dir = LimitPitch(dir, 35f);
            var newSpeed = Mathf.MoveTowards(swimVelocity.magnitude, desiredSpeed, acceleration * dt);
            swimVelocity = dir * newSpeed;

            var next = head + swimVelocity * dt;
            next.y = Mathf.Min(next.y, FishingWater.HeightAt(next) - 0.08f);
            head = next;
            facingRequest = swimVelocity.sqrMagnitude > 0.01f ? swimVelocity : HeadingVector();
        }

        /// <summary>Current heading (mouth direction).</summary>
        public Vector3 HeadingVector() => Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;

        /// <summary>Tail beat intensity hint (0..1), e.g. 1 while struggling.</summary>
        public void SetEffort(float effort) => effortOverride = Mathf.Clamp01(effort);

        /// <summary>Swim away from <paramref name="awayFrom"/> and fade out (missed bite, line broken, unhooked).</summary>
        public void Flee(Vector3 awayFrom)
        {
            if (mode == Mode.Free || mode == Mode.InNet) return;
            CanBeGrabbed = false;
            CanBeNetted = false;
            mode = Mode.Fleeing;
            var away = Vector3.ProjectOnPlane(head - awayFrom, Vector3.up);
            if (away.sqrMagnitude < 1e-4f) away = -transform.forward;
            fleeTarget = head + away.normalized * 12f + Vector3.down * 3f;
            fleeTimer = 0f;
            if (head.y > FishingWater.HeightAt(head)) head.y = FishingWater.HeightAt(head) - 0.1f; // drops back in
        }

        // ---------- Capture ----------

        public void PutInNet(LandingNet net)
        {
            mode = Mode.InNet;
            CanBeNetted = false;
            CanBeGrabbed = true;
            body.isKinematic = true;
            transform.SetParent(net.Bag, false);
            transform.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            transform.localPosition = -Vector3.right * length * 0.5f;
            fleeAlpha = 1f;
            ApplyAlpha(1f);
        }

        /// <summary>Caught by hand (already in the hand) or taken out of the net: becomes a normal object.</summary>
        public void SetFree()
        {
            mode = Mode.Free;
            CanBeGrabbed = true;
            CanBeNetted = false;
            transform.SetParent(null, true);
            ApplyAlpha(1f);
            if (!IsGrabbed)
                MakeDynamic();
        }

        void MakeDynamic()
        {
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        // ---------- Unity ----------

        void LateUpdate()
        {
            var dt = Time.deltaTime;
            switch (mode)
            {
                case Mode.InNet when !IsGrabbed:
                    wigglePhase += dt * 8f;
                    transform.localRotation = Quaternion.AngleAxis(Mathf.Sin(wigglePhase) * 10f, Vector3.up) * Quaternion.LookRotation(Vector3.right, Vector3.up);
                    break;

                case Mode.Free when !IsGrabbed:
                    // Dropped back in the lake: it swims away.
                    if (transform.position.y < FishingWater.HeightAt(transform.position) - 0.2f)
                    {
                        if (releasedTimer < 0f) releasedTimer = 2f;
                        releasedTimer -= dt;
                        if (releasedTimer <= 0f) Destroy(gameObject);
                    }
                    break;

                case Mode.Fleeing:
                    SwimTowards(fleeTarget, 3f, 160f, 0f, 6f);
                    fleeTimer += dt;
                    fleeAlpha = Mathf.Clamp01(1f - (fleeTimer - 1f) / 1.5f);
                    SimulateBody(dt);
                    UpdateVisibility();
                    if (fleeTimer > 2.6f) Destroy(gameObject);
                    break;

                case Mode.Driven when !IsGrabbed:
                    SimulateBody(dt);
                    UpdateVisibility();
                    break;
            }
        }

        void SimulateBody(float dt)
        {
            if (dt <= 0f) return;
            var half = length * 0.5f;
            var water = FishingWater.HeightAt(com);
            var inWater = com.y < water;
            // Swimming while the head is under water; at the surface / lifted, the body is a physical
            // pendulum hanging from the mouth (buoyancy + water drag below the surface, gravity above).
            var swimming = head.y < FishingWater.HeightAt(head) - 0.03f;

            // Heading: turns toward the requested facing at a limited rate (shortest way, never a flip).
            if (facingRequest.sqrMagnitude > 1e-4f)
            {
                var flat = new Vector3(facingRequest.x, 0f, facingRequest.z);
                var rate = yawRate * Mathf.Clamp(0.3f / Mathf.Max(0.1f, length), 0.35f, 1.5f);
                if (flat.sqrMagnitude > 1e-4f)
                    yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, rate * dt);
                var targetPitch = Mathf.Clamp(Mathf.Atan2(facingRequest.y, Mathf.Max(0.001f, flat.magnitude)) * Mathf.Rad2Deg, -maxPitch, maxPitch);
                pitch = Mathf.MoveTowards(pitch, targetPitch, 90f * dt);
            }

            if (swimming)
            {
                // The body lies behind the head along the heading. Smoothed RELATIVE to the head, so a fast
                // head never leaves its body behind (that made small fish flip over on direction changes).
                bodyOffset = Vector3.Lerp(bodyOffset, -HeadingVector() * half, 1f - Mathf.Exp(-bodyFollow * dt));
                com = head + bodyOffset;
                prevCom = com;
            }
            else
            {
                // Pendulum from the mouth. Below the surface: floats (gravity cancelled + spring to the
                // surface) with strong water drag, so it lies flat; above: gravity, light air drag.
                var depth = water - com.y;
                var drag = depth > 0f ? waterDragAtSurface : airDrag;
                var velocity = (com - prevCom) * Mathf.Exp(-drag * dt);
                if (velocity.magnitude > 0.5f) velocity = velocity.normalized * 0.5f; // safety
                prevCom = com;
                var accel = depth > 0f
                    ? Vector3.up * (Mathf.Min(depth, 0.3f) * 60f) // buoyancy: rises to the surface
                    : Physics.gravity;
                if (depth <= 0f && Time.time >= nextKick)
                {
                    // Out of the water it flaps a little.
                    nextKick = Time.time + UnityEngine.Random.Range(struggleInterval.x, struggleInterval.y);
                    var kick = Vector3.ProjectOnPlane(UnityEngine.Random.onUnitSphere, Vector3.up).normalized * struggleKick;
                    prevCom -= kick * dt;
                }
                com += velocity + accel * dt * dt;
            }

            var dir = com - head;
            if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
            com = head + dir.normalized * half;
            bodyOffset = com - head;
            if (!swimming)
            {
                // Inelastic link: being pulled by the mouth gives no bounce along the body, only a swing
                // (otherwise a fish reeled up fast flies over the rod tip).
                var radial = dir.normalized;
                var v = com - prevCom;
                prevCom = com - (v - radial * Vector3.Dot(v, radial));
            }

            if (!swimming)
            {
                // Hanging: the heading follows the body so that falling back in the water is seamless.
                var flatBody = new Vector3(dir.x, 0f, dir.z);
                if (flatBody.sqrMagnitude > 1e-4f) yaw = Mathf.Atan2(-flatBody.x, -flatBody.z) * Mathf.Rad2Deg;
                pitch = 0f;
            }

            // Orientation: mouth -> body, back kept up (no roll) except when hanging vertically.
            var forward = (com - head).normalized;
            upRef = Vector3.ProjectOnPlane(upRef, forward);
            var levelUp = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (levelUp.sqrMagnitude > 0.02f)
                upRef = Vector3.RotateTowards(upRef.sqrMagnitude > 1e-4f ? upRef.normalized : levelUp.normalized, levelUp.normalized, 6f * dt, 0f);
            if (upRef.sqrMagnitude < 1e-4f) upRef = Vector3.ProjectOnPlane(Vector3.forward, forward);
            var baseRot = Quaternion.LookRotation(forward, upRef.normalized);

            // Tail beat: faster and wider with speed / effort, flapping when out of the water.
            var speedEffort = Mathf.Clamp01(swimVelocity.magnitude / 2.5f);
            var effort = Mathf.Max(effortOverride, speedEffort);
            float amp, freq;
            if (inWater) { amp = Mathf.Lerp(4f, 14f, effort); freq = Mathf.Lerp(4f, 16f, effort); }
            else { amp = 10f; freq = 9f; }
            wigglePhase += dt * freq;
            transform.SetPositionAndRotation(head, baseRot * Quaternion.AngleAxis(Mathf.Sin(wigglePhase) * amp, Vector3.up));
        }

        static Vector3 LimitPitch(Vector3 dir, float maxDeg)
        {
            var flat = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            var maxY = Mathf.Sin(maxDeg * Mathf.Deg2Rad);
            var y = Mathf.Clamp(dir.y, -maxY, maxY);
            return (flat.normalized * Mathf.Sqrt(1f - y * y) + Vector3.up * y).normalized;
        }

        // ---------- Visibility ----------

        void UpdateVisibility()
        {
            var depth = FishingWater.HeightAt(head) - head.y;
            var alpha = 1f - Mathf.Clamp01((depth - clearDepth) / Mathf.Max(0.01f, fadeRange));
            ApplyAlpha(alpha * fleeAlpha);
        }

        void ApplyAlpha(float alpha)
        {
            if (fadeMaterial == null || renderers == null) return;
            var useFade = alpha < 0.99f;
            if (useFade)
            {
                var c = fadeMaterial.GetColor(BaseColorId);
                c.a = alpha;
                fadeMaterial.SetColor(BaseColorId, c);
            }
            if (useFade == fadeApplied) return;
            fadeApplied = useFade;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (useFade)
                {
                    var mats = new Material[originalMaterials[i].Length];
                    for (var m = 0; m < mats.Length; m++) mats[m] = fadeMaterial;
                    renderers[i].sharedMaterials = mats;
                }
                else renderers[i].sharedMaterials = originalMaterials[i];
            }
        }

        void OnDestroy()
        {
            if (fadeMaterial != null) Destroy(fadeMaterial);
        }

        // ---------- Grab / net ----------

        void OnGrabbed(SelectEnterEventArgs args)
        {
            releasedTimer = -1f;
            Grabbed?.Invoke(this);
            if (mode == Mode.InNet)
                SetFree();
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (mode == Mode.Free && !grab.isSelected)
                MakeDynamic();
        }

        /// <summary>Called by <see cref="LandingNet"/> when the fish is inside the net.</summary>
        public void NotifyNetted(LandingNet net)
        {
            if (CanBeNetted && !IsGrabbed)
                Netted?.Invoke(this, net);
        }

        bool IXRSelectFilter.canProcess => isActiveAndEnabled;

        bool IXRSelectFilter.Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (!CanBeGrabbed) return false;
            var hand = interactor.GetAttachTransform(interactable).position;
            var closest = capsule.ClosestPoint(hand);
            return Vector3.Distance(closest, hand) <= handReach;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
