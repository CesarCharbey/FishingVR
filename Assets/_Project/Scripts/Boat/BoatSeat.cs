using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace FishingVR.Boat
{
    /// <summary>
    /// Seats the VR player in the boat. The XR Origin is parented to an anchor that follows the boat, so the
    /// player moves with it and the hands stay consistent with the tiller. While seated, every locomotion
    /// provider of the rig (move, turn, teleport, climb, gravity...) and its CharacterController are disabled.
    /// For comfort, the anchor only follows a fraction of the boat's roll and pitch (tiltFollow), so the
    /// horizon stays mostly level.
    /// </summary>
    // Runs before the XR Interaction Manager and the tracked pose drivers, so that everything reading the
    // hands this frame (e.g. the tiller) sees the anchor already aligned with the interpolated boat pose.
    [DefaultExecutionOrder(-300)]
    public class BoatSeat : MonoBehaviour
    {
        [Header("Seat")]
        [Tooltip("Seat socket of the boat model (SOCKET_Seat_Driver). Only its position is used.")]
        [SerializeField] Transform seat;

        [Tooltip("Sideways shift from the seat socket (metres, boat space; negative = port/left). " +
                 "Sitting beside the tiller puts the grip under the hand instead of in front of the belly.")]
        [SerializeField] float sideOffset = -0.25f;

        [Tooltip("Height of the player's real chair (metres). The XR Origin (the real floor) is placed this far " +
                 "below the seat, so a seated player's head ends up at a natural height above the virtual seat.")]
        [SerializeField, Min(0f)] float seatHeight = 0.45f;

        [Header("Comfort")]
        [Tooltip("How much of the boat's roll and pitch the player follows. 0 = horizon locked (most comfortable), " +
                 "1 = rigidly attached to the boat.")]
        [SerializeField, Range(0f, 1f)] float tiltFollow = 0.25f;

        [Header("Locomotion")]
        [Tooltip("Keep teleportation while seated: aim at the shore / dock to get off (see BoatBoarding).")]
        [SerializeField] bool keepTeleportWhileSeated = true;

        [Header("Dev")]
        [Tooltip("Seat the scene's XR Origin in this boat on Start (dev scenes). The game boards by teleporting onto the seat (BoatBoarding).")]
        [SerializeField] bool mountOnStart = true;

        Transform anchor;
        XROrigin seated;
        Transform previousParent;
        CharacterController characterController;
        bool characterControllerWasEnabled;
        readonly List<LocomotionProvider> disabledProviders = new();

        /// <summary>XR Origin currently seated in this boat, or null.</summary>
        public XROrigin Seated => seated;

        void Awake()
        {
            if (seat == null)
            {
                Debug.LogWarning($"[BoatSeat] '{name}': no seat assigned.", this);
                enabled = false;
                return;
            }

            anchor = new GameObject("PlayerAnchor (generated)").transform;
            anchor.SetParent(transform, false);
            Vector3 seatLocal = transform.InverseTransformPoint(seat.position);
            anchor.localPosition = seatLocal + new Vector3(sideOffset, -seatHeight, 0f);
            UpdateAnchorRotation();
        }

        IEnumerator Start()
        {
            if (!mountOnStart)
                yield break;

            // Wait a couple of frames so the headset (or the simulator) has reported a first pose to recenter on.
            yield return null;
            yield return null;

            var origin = FindFirstObjectByType<XROrigin>();
            if (origin == null)
                Debug.LogWarning("[BoatSeat] Mount On Start: no active XR Origin in the scene.", this);
            else
                Mount(origin);
        }

        void Update()
        {
            if (anchor != null)
                UpdateAnchorRotation();
        }

        void OnDisable()
        {
            if (seated != null)
                Dismount();
        }

        /// <summary>Seats the player: parent to the boat, lock locomotion, put the head above the seat.</summary>
        public void Mount(XROrigin origin)
        {
            if (origin == null || seated == origin)
                return;
            if (seated != null)
                Dismount();

            seated = origin;
            previousParent = origin.transform.parent;

            disabledProviders.Clear();
            foreach (var provider in origin.GetComponentsInChildren<LocomotionProvider>())
            {
                if (keepTeleportWhileSeated && provider is TeleportationProvider)
                    continue;
                if (provider.enabled)
                {
                    provider.enabled = false;
                    disabledProviders.Add(provider);
                }
            }

            characterController = origin.GetComponent<CharacterController>();
            if (characterController != null)
            {
                characterControllerWasEnabled = characterController.enabled;
                characterController.enabled = false; // Would collide with the hull and push the player around.
            }

            origin.transform.SetParent(anchor, false);
            origin.transform.localPosition = Vector3.zero;
            origin.transform.localRotation = Quaternion.identity;
            Recenter();
        }

        /// <summary>Releases the player where they are and restores their locomotion.</summary>
        public void Dismount()
        {
            if (seated == null)
                return;

            Transform originTransform = seated.transform;
            originTransform.SetParent(previousParent, true);
            // Stand back up straight.
            originTransform.rotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(originTransform.forward, Vector3.up), Vector3.up);

            if (characterController != null)
                characterController.enabled = characterControllerWasEnabled;
            foreach (var provider in disabledProviders)
            {
                if (provider != null)
                    provider.enabled = true;
            }

            disabledProviders.Clear();
            characterController = null;
            seated = null;
        }

        /// <summary>
        /// Puts the player's head right above the seat, facing the bow, wherever they stand in their real room.
        /// Height is left to the tracking (real head height above the real floor).
        /// </summary>
        public void Recenter()
        {
            if (seated == null || seated.Camera == null)
                return;

            Transform originTransform = seated.transform;
            Transform head = seated.Camera.transform;

            // Turn the origin around the head so the head looks toward the bow.
            Vector3 headForward = anchor.InverseTransformDirection(head.forward);
            float headYaw = Mathf.Atan2(headForward.x, headForward.z) * Mathf.Rad2Deg;
            originTransform.RotateAround(head.position, anchor.up, -headYaw);

            // Slide it horizontally so the head is above the anchor.
            Vector3 headLocal = anchor.InverseTransformPoint(head.position);
            originTransform.position -= anchor.TransformVector(new Vector3(headLocal.x, 0f, headLocal.z));
        }

        [ContextMenu("Debug: Recenter")]
        void DebugRecenter() => Recenter();

        [ContextMenu("Debug: Dismount")]
        void DebugDismount() => Dismount();

        /// <summary>Anchor = boat heading, plus only a fraction of the boat's roll and pitch.</summary>
        void UpdateAnchorRotation()
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f)
                return; // Boat pointing straight up or down: keep the last rotation.

            Quaternion level = Quaternion.LookRotation(flatForward, Vector3.up);
            anchor.rotation = Quaternion.Slerp(level, transform.rotation, tiltFollow);
        }

        void OnDrawGizmosSelected()
        {
            if (seat == null)
                return;

            Vector3 anchorPos = Application.isPlaying && anchor != null
                ? anchor.position
                : transform.TransformPoint(transform.InverseTransformPoint(seat.position) + new Vector3(sideOffset, -seatHeight, 0f));

            // Real floor (origin), seated head height (~1.2 m above the real floor) and facing direction.
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(anchorPos, new Vector3(0.4f, 0f, 0.4f));
            Vector3 head = anchorPos + transform.up * 1.2f;
            Gizmos.DrawLine(anchorPos, head);
            Gizmos.DrawWireSphere(head, 0.1f);
            Gizmos.DrawLine(head, head + transform.forward * 0.4f);
        }
    }
}
