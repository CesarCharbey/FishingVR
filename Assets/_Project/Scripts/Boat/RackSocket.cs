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
    /// </summary>
    public class RackSocket : XRSocketInteractor
    {
        public enum Item { FishingRod, LandingNet }

        [SerializeField] Item accepts = Item.FishingRod;

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
