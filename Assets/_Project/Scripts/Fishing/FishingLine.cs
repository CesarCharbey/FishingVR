using UnityEngine;
using UnityEngine.Events;

namespace FishingVR.Fishing
{
    public enum LineState
    {
        /// <summary>Bobber hangs under the rod tip, ready to cast.</summary>
        Retracted,
        /// <summary>Bobber is flying, the line pays out freely.</summary>
        Flying,
        /// <summary>Bobber landed (water or ground), line length is fixed and can be reeled in.</summary>
        Deployed,
    }

    /// <summary>
    /// Line between the rod tip and the bobber, plus the hook hanging under the bobber.
    /// The line is a max-length constraint (no stretch): reeling shortens it, and when it is
    /// short enough the bobber comes back under the rod tip.
    /// </summary>
    public class FishingLine : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Transform rodTip;
        [SerializeField] Bobber bobber;
        [SerializeField] Transform hook;
        [SerializeField] LineRenderer mainLine;
        [SerializeField] LineRenderer leaderLine;

        [Header("Lengths (m)")]
        [Tooltip("Line length under the tip when retracted.")]
        [SerializeField] float hangLength = 0.35f;
        [SerializeField] float maxLineLength = 40f;
        [Tooltip("When reeled under this length, the bobber returns under the tip.")]
        [SerializeField] float retractLength = 1.2f;
        [Tooltip("Hook depth under the bobber when floating.")]
        [SerializeField] float hookDepth = 0.6f;

        [Header("Rendering")]
        [SerializeField] int segments = 20;
        [SerializeField] float sagPerSlackMeter = 0.4f;
        [SerializeField] float maxSag = 2f;

        [Header("Events")]
        public UnityEvent<LineState> onStateChanged;
        public UnityEvent onBobberLanded;

        [Header("Fight")]
        [Tooltip("Shortest line while a fish is on (m).")]
        [SerializeField] float minFightLength = 0.4f;
        [SerializeField] Color slackColor = new Color(0.85f, 0.85f, 0.8f);
        [SerializeField] Color tautColor = new Color(1f, 0.15f, 0.1f);

        LineState state = LineState.Retracted;
        float lineLength;
        Collider[] ownerColliders;

        // Fight mode: the fish drives the hook; the bobber slides on the line toward the fish.
        bool inFight;
        bool fishLifted;
        Vector3 fightHookPosition;
        float fightTension;
        Material tintMaterial;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public LineState State => state;
        public float LineLength => lineLength;
        public float MaxLineLength => maxLineLength;
        public bool InFight => inFight;
        public Bobber Bobber => bobber;
        public Transform Hook => hook;
        public bool CanCast => state == LineState.Retracted;
        public float Distance => Vector3.Distance(rodTip.position, bobber.LineTop.position);
        /// <summary>0 = slack, 1 = taut.</summary>
        public float Tension => lineLength <= 0f ? 0f : Mathf.Clamp01(Distance / lineLength);

        void Awake()
        {
            // The bobber lives in world space so the rod's rigidbody doesn't drag it around.
            if (bobber != null)
                bobber.transform.SetParent(null, true);

            ownerColliders = GetComponentsInParent<Rigidbody>().Length > 0
                ? GetComponentInParent<Rigidbody>().GetComponentsInChildren<Collider>(true)
                : new Collider[0];
            if (bobber != null)
                foreach (var own in ownerColliders)
                foreach (var bc in bobber.GetComponentsInChildren<Collider>(true))
                    Physics.IgnoreCollision(own, bc);

            if (mainLine != null) mainLine.positionCount = segments + 1;
            if (leaderLine != null) leaderLine.positionCount = 2;
            Retract();
        }

        void OnDestroy()
        {
            if (bobber != null)
                Destroy(bobber.gameObject);
            if (tintMaterial != null)
                Destroy(tintMaterial);
        }

        /// <summary>Launch the bobber from the rod tip.</summary>
        public void Cast(Vector3 launchVelocity)
        {
            if (!CanCast)
                return;
            var body = bobber.Body;
            bobber.SetCarried(false);
            body.position = rodTip.position;
            body.linearVelocity = launchVelocity;
            body.angularVelocity = Vector3.zero;
            lineLength = hangLength;
            SetState(LineState.Flying);
        }

        /// <summary>Reel in some line (metres).</summary>
        public void Reel(float meters)
        {
            if (state == LineState.Retracted || meters <= 0f)
                return;
            if (inFight)
            {
                // The fight decides when the fish is landed; the line never auto-retracts.
                lineLength = Mathf.Max(minFightLength, lineLength - meters);
                return;
            }
            lineLength = Mathf.Max(0f, lineLength - meters);
            if (state == LineState.Flying)
                SetState(LineState.Deployed); // reeling during the flight stops the line
            if (lineLength <= retractLength)
                Retract();
        }

        // ---------- Fight mode (driven by FishFight) ----------

        /// <summary>A fish is hooked: from now on the fight drives the hook and the line length.</summary>
        public void BeginFight(Vector3 hookPosition)
        {
            inFight = true;
            fishLifted = false;
            fightHookPosition = hookPosition;
            fightTension = 0f;
            bobber.ClearSink();
            bobber.SetCarried(true);
            if (state != LineState.Deployed)
                SetState(LineState.Deployed);
        }

        /// <summary>Per-frame fight state: hook (fish mouth) position, fish out of water, tension 0..1+.</summary>
        public void UpdateFight(Vector3 hookPosition, bool lifted, float tension)
        {
            fightHookPosition = hookPosition;
            fishLifted = lifted;
            fightTension = tension;
        }

        /// <summary>The drag lets line out (or the fight adjusts it).</summary>
        public void SetLineLength(float length) =>
            lineLength = Mathf.Clamp(length, minFightLength, maxLineLength);

        /// <summary>Fight over (caught or line broken): the hook comes back under the tip.</summary>
        public void EndFight()
        {
            inFight = false;
            fightTension = 0f;
            Retract();
        }

        /// <summary>Bring the bobber back under the rod tip.</summary>
        public void Retract()
        {
            lineLength = hangLength;
            if (bobber != null)
            {
                bobber.ClearSink();
                bobber.SetCarried(true);
                bobber.transform.SetPositionAndRotation(HangPosition(), Quaternion.identity);
            }
            SetState(LineState.Retracted);
        }

        Vector3 HangPosition() => rodTip.position + Vector3.down * hangLength;

        void FixedUpdate()
        {
            if (bobber == null || rodTip == null || inFight)
                return;

            switch (state)
            {
                case LineState.Flying:
                    lineLength = Mathf.Min(Mathf.Max(lineLength, Distance), maxLineLength);
                    if (lineLength >= maxLineLength)
                        ApplyConstraint();
                    if (bobber.IsInWater || IsGrounded())
                    {
                        lineLength = Mathf.Min(Distance + 0.2f, maxLineLength);
                        SetState(LineState.Deployed);
                        onBobberLanded?.Invoke();
                    }
                    break;
                case LineState.Deployed:
                    ApplyConstraint();
                    break;
            }
        }

        bool IsGrounded()
        {
            var body = bobber.Body;
            return body.linearVelocity.sqrMagnitude < 0.01f &&
                   Physics.Raycast(bobber.transform.position + Vector3.up * 0.05f, Vector3.down, 0.15f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Keep the bobber within the line length (inextensible line).</summary>
        void ApplyConstraint()
        {
            var body = bobber.Body;
            var toBobber = body.position - rodTip.position;
            var dist = toBobber.magnitude;
            if (dist <= lineLength || dist < 0.0001f)
                return;

            var dir = toBobber / dist;
            body.position = rodTip.position + dir * lineLength;
            var outward = Vector3.Dot(body.linearVelocity, dir);
            if (outward > 0f)
                body.linearVelocity -= dir * outward;
        }

        void LateUpdate()
        {
            if (bobber == null || rodTip == null)
                return;

            if (state == LineState.Retracted)
            {
                // Simple lagged hang under the tip.
                var t = bobber.transform;
                t.position = Vector3.Lerp(t.position, HangPosition(), 1f - Mathf.Exp(-12f * Time.deltaTime));
                t.rotation = Quaternion.identity;
            }

            if (inFight)
                UpdateFightPose();
            else
                UpdateHook();
            DrawLines();
            ApplyTint(inFight ? fightTension : 0f);
        }

        void UpdateFightPose()
        {
            // Hook = fish mouth. Bobber slides on the line, hookDepth above the hook toward the tip,
            // held at the surface while the fish is under water.
            var toTip = rodTip.position - fightHookPosition;
            var bob = fightHookPosition + toTip.normalized * Mathf.Min(hookDepth, toTip.magnitude * 0.5f);
            var water = WaterLevel.CurrentHeight;
            if (!fishLifted && bob.y < water)
                bob.y = water - Mathf.Clamp01(fightTension) * 0.1f; // pulled a bit under when taut
            var t = bobber.transform;
            t.position = Vector3.Lerp(t.position, bob, 1f - Mathf.Exp(-15f * Time.deltaTime));
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.FromToRotation(Vector3.up, toTip.normalized), 0.2f);

            if (hook != null)
            {
                hook.position = fightHookPosition;
                hook.rotation = Quaternion.identity;
            }
        }

        void ApplyTint(float tension)
        {
            if (mainLine == null) return;
            // Shared material instance for both lines (property blocks are ignored by the GPU resident drawer).
            if (tintMaterial == null)
            {
                tintMaterial = new Material(mainLine.sharedMaterial);
                mainLine.sharedMaterial = tintMaterial;
                if (leaderLine != null) leaderLine.sharedMaterial = tintMaterial;
            }
            tintMaterial.SetColor(BaseColorId, Color.Lerp(slackColor, tautColor, Mathf.InverseLerp(0.4f, 1f, tension)));
        }

        void UpdateHook()
        {
            if (hook == null)
                return;
            var depth = state == LineState.Deployed && bobber.IsInWater ? hookDepth : 0.12f;
            hook.position = bobber.LineBottom.position + Vector3.down * depth;
            hook.rotation = Quaternion.identity;
        }

        void DrawLines()
        {
            if (mainLine != null)
            {
                var a = rodTip.position;
                var b = bobber.LineTop.position;
                var slack = Mathf.Max(0f, lineLength - Vector3.Distance(a, b));
                var sag = state == LineState.Retracted ? 0f
                    : inFight ? Mathf.Lerp(Mathf.Min(0.4f, Vector3.Distance(a, b) * 0.05f), 0f, Mathf.Clamp01(fightTension * 1.5f))
                    : Mathf.Min(maxSag, 0.02f + slack * sagPerSlackMeter);
                var mid = (a + b) * 0.5f + Vector3.down * sag;
                for (var i = 0; i <= segments; i++)
                {
                    var t = i / (float)segments;
                    // Quadratic bezier a -> mid -> b
                    var p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * mid + t * t * b;
                    mainLine.SetPosition(i, p);
                }
            }

            if (leaderLine != null && hook != null)
            {
                leaderLine.SetPosition(0, bobber.LineBottom.position);
                leaderLine.SetPosition(1, hook.position);
            }
        }

        void SetState(LineState newState)
        {
            if (state == newState)
                return;
            state = newState;
            onStateChanged?.Invoke(state);
        }
    }
}
