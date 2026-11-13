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
    /// The visible, grabbable fish. Pivot = mouth, +Z = from the mouth toward the tail.
    /// Modes:
    /// - Swimming: placed by the fight simulation (nose along its velocity, tail wiggle).
    /// - Hanging: out of water at the end of the line, damped pendulum from the mouth.
    /// - InNet: lying in the landing net.
    /// - Free: caught, normal physics object that can be grabbed and dropped.
    /// Hand capture: grabbable only within <see cref="handReach"/> and when <see cref="CanBeGrabbed"/>.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the rod tip / line have moved
    public class FishBody : MonoBehaviour, IXRSelectFilter
    {
        public enum Mode { Swimming, Hanging, InNet, Free }

        [SerializeField] float handReach = 0.3f;
        [Range(0f, 1f)] [SerializeField] float airDamping = 0.02f;
        [SerializeField] float struggleStrength = 0.6f;
        [SerializeField] Vector2 struggleInterval = new Vector2(0.4f, 1.6f);

        Mode mode = Mode.Swimming;
        XRGrabInteractable grab;
        Rigidbody body;
        float length;
        Vector3 com, prevCom;
        Vector3 upRef = Vector3.up;
        float nextStruggle;
        float wigglePhase;
        float releasedTimer = -1f;

        public FishInstance Fish { get; private set; }
        public Mode CurrentMode => mode;
        public float Length => length;
        public bool IsGrabbed => grab != null && grab.isSelected;
        /// <summary>Set by the fight: the fish may be taken by hand right now.</summary>
        public bool CanBeGrabbed { get; set; }
        /// <summary>Set by the fight: the fish may be scooped by the landing net right now.</summary>
        public bool CanBeNetted { get; set; }

        /// <summary>Raised when a hand grabs the fish.</summary>
        public event Action<FishBody> Grabbed;
        /// <summary>Raised when the landing net scoops a nettable fish.</summary>
        public event Action<FishBody, LandingNet> Netted;

        public static FishBody Create(FishInstance fish, Vector3 mouthPosition)
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

            // Physics + grab (kinematic while driven by the fight).
            var bounds = renderer != null ? renderer.bounds : new Bounds(Vector3.forward * fb.length * 0.5f, Vector3.one * 0.1f); // root is still at the origin
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 2;
            col.center = new Vector3(0f, bounds.center.y, fb.length * 0.5f);
            col.height = fb.length;
            col.radius = Mathf.Max(0.02f, Mathf.Min(bounds.size.x, bounds.size.y) * 0.5f);

            fb.body = root.AddComponent<Rigidbody>();
            fb.body.mass = Mathf.Max(0.1f, fish.weightKg);
            fb.body.isKinematic = true;
            // No interpolation while the transform is driven by script (it would override our poses).
            fb.body.interpolation = RigidbodyInterpolation.None;

            fb.grab = root.AddComponent<XRGrabInteractable>();
            fb.grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            fb.grab.useDynamicAttach = true;
            fb.grab.throwOnDetach = true;
            fb.grab.selectFilters.Add(fb);
            fb.grab.selectEntered.AddListener(fb.OnGrabbed);
            fb.grab.selectExited.AddListener(fb.OnReleased);

            root.transform.SetPositionAndRotation(mouthPosition, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            fb.com = fb.prevCom = mouthPosition + Vector3.down * fb.length * 0.5f;
            return fb;
        }

        // ---------- Driven by the fight ----------

        /// <summary>Underwater / at the surface, mouth at <paramref name="mouth"/>, swimming along <paramref name="velocity"/>.</summary>
        public void SetSwimming(Vector3 mouth, Vector3 velocity, float effort)
        {
            if (mode == Mode.Free || mode == Mode.InNet || IsGrabbed) return;
            mode = Mode.Swimming;

            // Tail points opposite to the swim direction (or toward the line pull when idle).
            var tailDir = velocity.sqrMagnitude > 0.01f ? -velocity.normalized : transform.forward;
            tailDir = Vector3.Slerp(transform.forward, tailDir, 1f - Mathf.Exp(-6f * Time.deltaTime));
            wigglePhase += Time.deltaTime * Mathf.Lerp(4f, 14f, effort);
            var wiggle = Quaternion.AngleAxis(Mathf.Sin(wigglePhase) * Mathf.Lerp(5f, 25f, effort), Vector3.up);
            transform.SetPositionAndRotation(mouth, wiggle * Quaternion.LookRotation(tailDir, Vector3.up));
            com = prevCom = mouth + transform.forward * length * 0.5f;
        }

        /// <summary>Out of the water: hangs from the hook at <paramref name="anchor"/>.</summary>
        public void SetHanging(Vector3 anchor)
        {
            if (mode == Mode.Free || mode == Mode.InNet || IsGrabbed) return;
            if (mode != Mode.Hanging)
            {
                com = prevCom = anchor + transform.forward * length * 0.5f;
                mode = Mode.Hanging;
            }
            Pendulum(anchor);
        }

        public void PutInNet(LandingNet net)
        {
            mode = Mode.InNet;
            CanBeNetted = false;
            CanBeGrabbed = true;
            body.isKinematic = true;
            transform.SetParent(net.Bag, false);
            // Lying across the bag, centred.
            transform.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            transform.localPosition = -Vector3.right * length * 0.5f;
        }

        /// <summary>Caught by hand (already in the hand) or taken out of the net: becomes a normal object.</summary>
        public void SetFree()
        {
            mode = Mode.Free;
            CanBeGrabbed = true;
            CanBeNetted = false;
            transform.SetParent(null, true);
            if (!IsGrabbed)
                MakeDynamic();
        }

        void MakeDynamic()
        {
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>The line broke: the fish swims away and disappears.</summary>
        public void Escape()
        {
            CanBeGrabbed = false;
            CanBeNetted = false;
            mode = Mode.Free;
            MakeDynamic();
            Destroy(gameObject, 4f);
        }

        // ---------- Unity ----------

        void LateUpdate()
        {
            if (mode == Mode.InNet && !IsGrabbed)
            {
                // Small flops in the net.
                wigglePhase += Time.deltaTime * 8f;
                var baseRot = Quaternion.LookRotation(Vector3.right, Vector3.up);
                transform.localRotation = Quaternion.AngleAxis(Mathf.Sin(wigglePhase) * 10f, Vector3.up) * baseRot;
            }

            if (mode == Mode.Free && !IsGrabbed)
            {
                // Dropped back in the lake: it swims away.
                if (transform.position.y < WaterLevel.CurrentHeight - 0.2f)
                {
                    if (releasedTimer < 0f) releasedTimer = 2f;
                    releasedTimer -= Time.deltaTime;
                    if (releasedTimer <= 0f) Destroy(gameObject);
                }
            }
        }

        void Pendulum(Vector3 anchor)
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            var velocity = (com - prevCom) * (1f - airDamping);
            prevCom = com;
            com += velocity + Physics.gravity * dt * dt;

            if (Time.time >= nextStruggle)
            {
                nextStruggle = Time.time + UnityEngine.Random.Range(struggleInterval.x, struggleInterval.y);
                var kick = Vector3.ProjectOnPlane(UnityEngine.Random.onUnitSphere, Vector3.up).normalized * struggleStrength;
                prevCom -= kick * dt;
            }

            var dir = com - anchor;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.down;
            com = anchor + dir.normalized * length * 0.5f;

            var forward = (com - anchor).normalized;
            upRef = Vector3.ProjectOnPlane(upRef, forward);
            if (upRef.sqrMagnitude < 1e-4f) upRef = Vector3.ProjectOnPlane(Vector3.forward, forward);
            transform.SetPositionAndRotation(anchor, Quaternion.LookRotation(forward, upRef.normalized));
        }

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

        // ---------- IXRSelectFilter: near grab only, when allowed ----------

        bool IXRSelectFilter.canProcess => isActiveAndEnabled;

        bool IXRSelectFilter.Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (!CanBeGrabbed) return false;
            var hand = interactor.GetAttachTransform(interactable).position;
            var closest = GetComponent<Collider>().ClosestPoint(hand);
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
