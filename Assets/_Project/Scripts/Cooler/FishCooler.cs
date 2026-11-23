using System.Collections;
using System.Collections.Generic;
using FishingVR.Bestiary;
using FishingVR.Fish;
using FishingVR.Fishing;
using FishingVR.UI;
using TMPro;
using UnityEngine;

namespace FishingVR.Cooler
{
    /// <summary>
    /// The cooler in the boat, where caught fish are put away (this is what fills the bestiary).
    /// - It stays at its spot in the boat (follows the boat), in front of the driver's seat.
    /// - Only a free fish goes in: held in the hand or lying in the landing net (never one still on the line).
    /// - Help: when such a fish comes close (in the hand or in the net), the lid opens and the rim lights up;
    ///   let the fish go anywhere near the cooler, or tilt the net above it, and the fish slides in along an arc.
    /// - Put away = recorded in the bestiary, laid in the cooler (the last few stay visible), with a message
    ///   (new species / record).
    /// </summary>
    public class FishCooler : MonoBehaviour
    {
        [Header("Parts")]
        [SerializeField] Transform lid;
        [Tooltip("Fish put away are laid here (local X = along the cooler, Y = up).")]
        [SerializeField] Transform contents;
        [Tooltip("Rim lit up while a fish can be put away (optional, all its renderers share one material).")]
        [SerializeField] GameObject highlight;
        [Tooltip("Message shown when a fish is put away (a text in a world-space canvas).")]
        [SerializeField] TMP_Text message;
        [Tooltip("What moves / turns with the message (its canvas).")]
        [SerializeField] Transform messageRoot;

        [Header("Place in the boat")]
        [Tooltip("Boat the cooler belongs to. Empty = the boat of the scene's BoatSeat.")]
        [SerializeField] Transform boat;
        [Tooltip("Spot on the boat floor (boat space), in front of the driver's seat.")]
        [SerializeField] Vector3 boatSpot = new Vector3(0.05f, 0.1f, -0.5f);
        [SerializeField] float boatSpotYaw;

        [Header("Help to put a fish away")]
        [Tooltip("A free fish this close to the cooler (horizontally, m)...")]
        [SerializeField] float helpRadius = 0.6f;
        [Tooltip("...and less than this above it (m) is guided in when let go / tipped out of the net.")]
        [SerializeField] float helpHeight = 1.3f;
        [Tooltip("The net only needs to be tilted this much (deg from upright) above the cooler to pour the fish in.")]
        [SerializeField] float netPourAngle = 55f;
        [SerializeField] float slideTime = 0.45f;
        [SerializeField] Color highlightColor = new Color(1f, 0.85f, 0.2f, 1f);

        [Header("Lid")]
        [SerializeField] float openAngle = 110f;
        [SerializeField] float lidSpeed = 4f;

        [Header("Contents")]
        [SerializeField] int visibleFish = 6;
        [SerializeField] float maxFishLength = 0.36f;
        [SerializeField] float messageTime = 3.5f;

        Camera cam;
        float lidAngle;
        bool lidOpening;
        float messageTimer;
        float highlightPulse;
        bool candidateNear;
        Material highlightMaterial;
        CanvasSnapshot messageSnapshot;
        readonly List<FishBody> stored = new List<FishBody>();
        readonly HashSet<FishBody> sliding = new HashSet<FishBody>();
        readonly Collider[] hits = new Collider[64];

        public int StoredCount { get; private set; }
        public bool IsOpen => lidAngle > openAngle * 0.6f;
        /// <summary>Keeps the lid open (tests).</summary>
        public bool KeepOpen { get; set; }
        /// <summary>A fish that can be put away is near (the rim is lit).</summary>
        public bool CandidateNear => candidateNear;

        void Start()
        {
            if (boat == null)
            {
                var seat = FindAnyObjectByType<FishingVR.Boat.BoatSeat>();
                if (seat != null) boat = seat.transform;
            }
            if (message != null)
            {
                messageSnapshot = MessageRoot.GetComponent<CanvasSnapshot>();
                if (messageSnapshot == null) messageSnapshot = MessageRoot.gameObject.AddComponent<CanvasSnapshot>();
                MessageRoot.gameObject.SetActive(false);
            }
            if (highlight != null)
            {
                // Own material instance (the GPU Resident Drawer ignores property blocks).
                var renderers = highlight.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    highlightMaterial = new Material(renderers[0].sharedMaterial);
                    foreach (var r in renderers) r.sharedMaterial = highlightMaterial;
                }
                highlight.SetActive(false);
            }
        }

        Transform MessageRoot => messageRoot != null ? messageRoot : message.transform;

        void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            var dt = Time.deltaTime;
            if (boat != null)
                transform.SetPositionAndRotation(boat.TransformPoint(boatSpot), boat.rotation * Quaternion.Euler(0f, boatSpotYaw, 0f));
            UpdateHighlight(dt);
            UpdateLid(dt);
            UpdateMessage(dt);
        }

        void FixedUpdate() => ScanFish();

        // ---------- Fish around the cooler ----------

        /// <summary>Is this point in the help zone (above / around the cooler)?</summary>
        bool InHelpZone(Vector3 p)
        {
            var local = transform.InverseTransformPoint(p);
            return new Vector2(local.x, local.z).magnitude < helpRadius && local.y > -0.1f && local.y < helpHeight;
        }

        void ScanFish()
        {
            candidateNear = false;
            var center = transform.position + Vector3.up * helpHeight * 0.5f;
            var half = new Vector3(helpRadius, helpHeight * 0.5f + 0.1f, helpRadius);
            var count = Physics.OverlapBoxNonAlloc(center, half, hits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var fish = hits[i].GetComponentInParent<FishBody>();
                if (fish == null || sliding.Contains(fish) || stored.Contains(fish) || !InHelpZone(fish.transform.position)) continue;
                switch (fish.CurrentMode)
                {
                    case FishBody.Mode.Free when fish.IsGrabbed:
                        candidateNear = true; // in the hand: let go to put it away
                        break;
                    case FishBody.Mode.Free:
                        StartCoroutine(SlideIn(fish)); // let go / fallen near the cooler
                        break;
                    case FishBody.Mode.InNet:
                        candidateNear = true;
                        var net = fish.GetComponentInParent<LandingNet>();
                        if (net != null && Vector3.Angle(net.transform.up, Vector3.up) > netPourAngle)
                        {
                            fish.SetFree(); // poured out of the tilted net
                            StartCoroutine(SlideIn(fish));
                        }
                        break;
                }
            }
        }

        /// <summary>The fish slides into the cooler along a small arc, then it is put away.</summary>
        IEnumerator SlideIn(FishBody fish)
        {
            sliding.Add(fish);
            var result = FishBestiary.Record(fish.Fish);
            StoredCount++;
            var slot = stored.Count;
            var startPos = fish.transform.position;
            var startRot = fish.transform.rotation;
            var startScale = fish.transform.localScale;
            // Put away at once (no physics, can't be grabbed), then animated from where it was.
            fish.StoreIn(contents != null ? contents : transform, SlotPosition(slot), SlotRotation(slot), maxFishLength);
            stored.Add(fish);
            var endLocalPos = fish.transform.localPosition;
            var endLocalRot = fish.transform.localRotation;
            var endScale = fish.transform.localScale;
            ShowMessage(result);
            if (result.newSpecies || result.weightRecord)
                Audio.GameAudio.Play(result.newSpecies ? Audio.SoundId.NewSpecies : Audio.SoundId.Record, transform.position + Vector3.up * 0.8f);

            for (var t = 0f; t < 1f && fish != null; t += Time.deltaTime / slideTime)
            {
                var k = Mathf.SmoothStep(0f, 1f, t);
                var parent = fish.transform.parent;
                var end = parent.TransformPoint(endLocalPos);
                var pos = Vector3.Lerp(startPos, end, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.18f);
                fish.transform.SetPositionAndRotation(pos, Quaternion.Slerp(startRot, parent.rotation * endLocalRot, k));
                fish.transform.localScale = Vector3.Lerp(startScale, endScale, k);
                yield return null;
            }
            if (fish != null) fish.transform.SetLocalPositionAndRotation(endLocalPos, endLocalRot);
            sliding.Remove(fish);
            Audio.GameAudio.Play(Audio.SoundId.FishInCooler, transform.position + Vector3.up * 0.15f);

            while (stored.Count > visibleFish)
            {
                if (stored[0] != null) Destroy(stored[0].gameObject);
                stored.RemoveAt(0);
                for (var k = 0; k < stored.Count; k++)
                    if (stored[k] != null && !sliding.Contains(stored[k]))
                        stored[k].transform.SetLocalPositionAndRotation(SlotPosition(k), SlotRotation(k));
            }
        }

        /// <summary>Put this fish away now (tests).</summary>
        public void Store(FishBody fish)
        {
            if (fish != null && !sliding.Contains(fish) && !stored.Contains(fish)) StartCoroutine(SlideIn(fish));
        }

        // Side by side, then a layer above; lying on their side, head at -X.
        Vector3 SlotPosition(int slot) => new Vector3(-maxFishLength * 0.5f, 0.07f + (slot / 2) * 0.05f, slot % 2 == 0 ? -0.06f : 0.06f);
        static Quaternion SlotRotation(int slot) => Quaternion.LookRotation(Vector3.right, slot % 2 == 0 ? Vector3.forward : Vector3.back);

        // ---------- Feedback ----------

        void UpdateHighlight(float dt)
        {
            if (highlight == null) return;
            if (highlight.activeSelf != candidateNear) highlight.SetActive(candidateNear);
            if (!candidateNear || highlightMaterial == null) return;
            highlightPulse += dt * 5f;
            // Pulses in brightness (opaque material).
            var c = Color.Lerp(highlightColor * 0.55f, highlightColor, 0.5f + 0.5f * Mathf.Sin(highlightPulse));
            c.a = 1f;
            highlightMaterial.color = c;
            if (highlightMaterial.HasProperty("_BaseColor")) highlightMaterial.SetColor("_BaseColor", c);
        }

        void UpdateLid(float dt)
        {
            var open = KeepOpen || candidateNear || sliding.Count > 0;
            if (open != lidOpening)
            {
                lidOpening = open;
                Audio.GameAudio.Play(open ? Audio.SoundId.CoolerOpen : Audio.SoundId.CoolerClose, transform.position + Vector3.up * 0.3f);
            }
            lidAngle = Mathf.MoveTowards(lidAngle, open ? openAngle : 0f, openAngle * lidSpeed * dt * (open ? 1f : 0.6f));
            if (lid != null) lid.localRotation = Quaternion.Euler(lidAngle, 0f, 0f);
        }

        void ShowMessage(BestiaryResult result)
        {
            if (message == null) return;
            var fish = result.fish;
            var rarity = fish.species.rarity;
            var color = "#" + ColorUtility.ToHtmlStringRGB(rarity.DisplayColor());
            var title = result.newSpecies ? "<b><color=#FFD84A>NOUVELLE ESPÈCE !</color></b>\n" : result.weightRecord ? "<b><color=#FFD84A>RECORD !</color></b>\n" : "";
            message.text = $"{title}{fish.species.displayName}\n{fish.weightKg:0.00} kg · {fish.lengthCm:0} cm · <color={color}>{rarity.DisplayName()}</color>";
            MessageRoot.gameObject.SetActive(true);
            PlaceMessage(0f);
            if (messageSnapshot != null) messageSnapshot.Capture();
            messageTimer = messageTime;
        }

        void PlaceMessage(float t)
        {
            var root = MessageRoot;
            root.position = transform.position + transform.forward * 0.2f + Vector3.up * (0.85f + 0.1f * t);
            if (cam == null) return;
            var away = root.position - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) root.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        void UpdateMessage(float dt)
        {
            if (message == null || !MessageRoot.gameObject.activeSelf) return;
            messageTimer -= dt;
            PlaceMessage(1f - messageTimer / messageTime);
            if (messageSnapshot != null) messageSnapshot.Alpha = Mathf.Clamp01(messageTimer / 0.6f);
            if (messageTimer <= 0f) MessageRoot.gameObject.SetActive(false);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * helpHeight * 0.5f, helpRadius);
        }
    }
}
