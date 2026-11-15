using FishingVR.Fish;
using UnityEngine;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Épuisette: any nettable fish overlapping the catch volume (the bag) is scooped.
    /// The fight decides when a fish is nettable (tired and near the surface).
    /// </summary>
    public class LandingNet : MonoBehaviour
    {
        [SerializeField] BoxCollider catchVolume;
        [Tooltip("Where a netted fish lies.")]
        [SerializeField] Transform bag;

        readonly Collider[] hits = new Collider[16];

        public Transform Bag => bag != null ? bag : transform;

        void FixedUpdate()
        {
            if (catchVolume == null)
                return;

            var t = catchVolume.transform;
            var center = t.TransformPoint(catchVolume.center);
            var halfExtents = Vector3.Scale(catchVolume.size * 0.5f, t.lossyScale);
            var count = Physics.OverlapBoxNonAlloc(center, halfExtents, hits, t.rotation, ~0, QueryTriggerInteraction.Collide);
            for (var i = 0; i < count; i++)
            {
                var fish = hits[i].GetComponentInParent<FishBody>();
                if (fish != null && fish.CanBeNetted)
                    fish.NotifyNetted(this);
            }
        }
    }
}
