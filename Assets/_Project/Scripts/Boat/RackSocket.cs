using FishingVR.Fishing;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FishingVR.Boat
{
    /// <summary>
    /// Rack on the boat (rod or landing net): let go of the item near the rack and it snaps into place,
    /// held kinematically by the socket so it follows the boat with no physics.
    /// It only accepts its own item type, and the item's colliders stop colliding with the boat
    /// (otherwise the item, inside the hull's convex collider, would push the boat around).
    /// Because of that, an item dropped in the boat falls through it: once it has sunk in the lake (or fallen
    /// through the map) for <see cref="returnDelay"/> s, it comes back into its rack.
    /// </summary>
    public class RackSocket : XRSocketInteractor
    {
        public enum Item { FishingRod, LandingNet }

        [SerializeField] Item accepts = Item.FishingRod;

        [Header("Return to the rack")]
        [Tooltip("The item that lives in this rack. Empty = the Starting Selected Interactable.")]
        [SerializeField] XRGrabInteractable homeItem;
        [Tooltip("Seconds the dropped item stays lost (under water / under the map) before it comes back.")]
        [SerializeField] float returnDelay = 2f;
        [Tooltip("The item is lost when it is this far under the water surface (m).")]
        [SerializeField] float sunkDepth = 0.2f;
        [Tooltip("Without water: lost below this height (fell through the map).")]
        [SerializeField] float fallbackMinHeight = -50f;

        float lostTimer;

        public XRGrabInteractable HomeItem => homeItem;

        protected override void Start()
        {
            base.Start();
            if (homeItem == null) homeItem = startingSelectedInteractable as XRGrabInteractable;
        }

        bool Matches(IXRInteractable interactable)
        {
            // The item itself (not one of its parts: the reel crank is an interactable inside the rod).
            var t = interactable.transform;
            return accepts == Item.FishingRod
                ? t.GetComponent<FishingRod>() != null
                : t.GetComponent<LandingNet>() != null;
        }

        public override bool CanHover(IXRHoverInteractable interactable) => base.CanHover(interactable) && Matches(interactable);

        public override bool CanSelect(IXRSelectInteractable interactable) => base.CanSelect(interactable) && Matches(interactable);

        protected override void OnHoverEntered(HoverEnterEventArgs args)
        {
            base.OnHoverEntered(args);
            IgnoreBoatCollisions(args.interactableObject);
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            IgnoreBoatCollisions(args.interactableObject);
        }

        public override void ProcessInteractor(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractor(updatePhase);
            if (updatePhase == XRInteractionUpdateOrder.UpdatePhase.Dynamic)
                WatchHomeItem();
        }

        /// <summary>Brings the item back if it was dropped and is lost (sunk in the lake / under the map).</summary>
        void WatchHomeItem()
        {
            if (homeItem == null || homeItem.isSelected || !homeItem.isActiveAndEnabled)
            {
                lostTimer = 0f;
                return;
            }
            var p = homeItem.transform.position;
            var water = FishingWater.HeightAt(p);
            var lost = float.IsNegativeInfinity(water) ? p.y < fallbackMinHeight : p.y < water - sunkDepth;
            lostTimer = lost ? lostTimer + Time.deltaTime : 0f;
            if (lostTimer >= returnDelay) ReturnHome();
        }

        /// <summary>Puts the home item back in this rack now (also callable from code / tests).</summary>
        public void ReturnHome()
        {
            lostTimer = 0f;
            if (homeItem == null || homeItem.isSelected || hasSelection) return;

            var attach = attachTransform != null ? attachTransform : transform;
            var body = homeItem.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = attach.position;
                body.rotation = attach.rotation;
            }
            homeItem.transform.SetPositionAndRotation(attach.position, attach.rotation);

            // The line comes back under the tip (unless a fish is still on: the fight goes on from the rack).
            var rod = homeItem.GetComponent<FishingRod>();
            if (rod != null && rod.Line != null && !rod.Line.InFight) rod.Line.Retract();

            interactionManager.SelectEnter((IXRSelectInteractor)this, (IXRSelectInteractable)homeItem);
        }

        /// <summary>The item no longer collides with this boat (kept afterwards: it lives inside the hull).</summary>
        void IgnoreBoatCollisions(IXRInteractable interactable)
        {
            var boatBody = GetComponentInParent<Rigidbody>();
            if (boatBody == null) return;
            var boatColliders = boatBody.GetComponentsInChildren<Collider>(true);
            foreach (var itemCollider in interactable.transform.GetComponentsInChildren<Collider>(true))
            foreach (var boatCollider in boatColliders)
                if (boatCollider != null && itemCollider != null && !boatCollider.isTrigger)
                    Physics.IgnoreCollision(itemCollider, boatCollider, true);
        }
    }
}
