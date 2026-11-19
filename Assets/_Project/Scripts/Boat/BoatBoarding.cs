using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace FishingVR.Boat
{
    /// <summary>
    /// Getting on and off the boat by teleporting (no button to learn):
    /// - Teleport onto the seat (a teleport anchor on the driver seat) = sit down at the helm (BoatSeat.Mount):
    ///   drive with the tiller, take the rod from its rack and fish from there.
    /// - While seated, teleport anywhere else (dock, shore) = get off (BoatSeat.Dismount), standing where you landed.
    /// Teleportation stays enabled while seated (BoatSeat.keepTeleportWhileSeated).
    /// </summary>
    [RequireComponent(typeof(BoatSeat))]
    public class BoatBoarding : MonoBehaviour
    {
        [Tooltip("Teleport anchor placed on the seat. Created on the seat socket if empty.")]
        [SerializeField] TeleportationAnchor seatAnchor;

        BoatSeat seat;
        XROrigin origin;
        TeleportationProvider teleport;
        bool boardRequested;

        public BoatSeat Seat => seat;
        public TeleportationAnchor SeatAnchor => seatAnchor;
        public bool IsAboard => seat != null && seat.Seated != null;

        void Awake()
        {
            seat = GetComponent<BoatSeat>();
            // The seat anchor has its own (kinematic) body so it does not change the boat's mass properties:
            // it must never touch the boat's colliders, or it would pin the boat in place.
            if (seatAnchor != null)
            {
                var boatBody = GetComponent<Rigidbody>();
                foreach (var a in seatAnchor.GetComponentsInChildren<Collider>(true))
                foreach (var b in GetComponentsInChildren<Collider>(true))
                    if (b.attachedRigidbody == boatBody) Physics.IgnoreCollision(a, b, true);
            }
        }

        IEnumerator Start()
        {
            if (seatAnchor != null)
                seatAnchor.teleporting.AddListener(OnSeatTeleporting);

            // The rig may be created / enabled a bit later (simulator, headset).
            while (origin == null)
            {
                origin = FindAnyObjectByType<XROrigin>();
                if (origin == null) yield return null;
            }
            teleport = origin.GetComponentInChildren<TeleportationProvider>(true);
            if (teleport != null)
                teleport.locomotionEnded += OnTeleportEnded;
            else
                Debug.LogWarning("[BoatBoarding] No TeleportationProvider on the XR Origin: boarding by teleport is unavailable.", this);
        }

        void OnDestroy()
        {
            if (seatAnchor != null) seatAnchor.teleporting.RemoveListener(OnSeatTeleporting);
            if (teleport != null) teleport.locomotionEnded -= OnTeleportEnded;
        }

        void OnSeatTeleporting(TeleportingEventArgs args) => boardRequested = true;

        void OnTeleportEnded(LocomotionProvider provider)
        {
            var board = boardRequested;
            boardRequested = false;
            if (board || IsAboard)
                StartCoroutine(AfterTeleport(board));
        }

        // The body transformer finishes moving the rig after locomotionEnded: seat / release one frame later,
        // otherwise the rest of the teleport is applied on top of the seated pose.
        IEnumerator AfterTeleport(bool board)
        {
            yield return null;
            if (board) Board();
            else Disembark();
        }

        /// <summary>Sit at the helm (also callable from code / tests).</summary>
        public void Board()
        {
            if (origin == null) origin = FindAnyObjectByType<XROrigin>();
            if (origin == null) return;
            // Re-seat cleanly even if already aboard (the teleport moved the rig).
            if (seat.Seated == origin) seat.Dismount();
            seat.Mount(origin);
        }

        /// <summary>Stand up and leave the boat where the rig currently is (after a teleport to the shore).</summary>
        public void Disembark()
        {
            if (IsAboard) seat.Dismount();
        }
    }
}
