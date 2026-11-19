using UnityEngine;
using UnityEngine.Events;

namespace FishingVR.Boat
{
    /// <summary>
    /// Detects the boat hitting something solid (dock, shore, rocks...) and raises an event with the
    /// contact point and a 0-1 intensity, for sounds, splashes, controller rumble or camera shake.
    /// Put it on the boat root (the Rigidbody receives the collisions of all the hull's colliders).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class BoatImpacts : MonoBehaviour
    {
        [Tooltip("Impact speed (m/s, along the contact normal) below which nothing is raised: rubbing, resting against the dock...")]
        [SerializeField, Min(0f)] float minImpactSpeed = 0.5f;

        [Tooltip("Impact speed (m/s) that gives an intensity of 1.")]
        [SerializeField, Min(0.1f)] float fullImpactSpeed = 4f;

        [Tooltip("Minimum time (s) between two impact events, so one crash does not fire a burst of events.")]
        [SerializeField, Min(0f)] float cooldown = 0.25f;

        [Header("Events")]
        [Tooltip("Contact point (world) and intensity (0 = light bump, 1 = violent crash).")]
        public UnityEvent<Vector3, float> onImpact = new();

        float lastImpactTime = float.NegativeInfinity;

        /// <summary>Intensity (0-1) of the last impact.</summary>
        public float LastImpactIntensity { get; private set; }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0 || Time.time - lastImpactTime < cooldown)
                return;

            // Only the speed toward the obstacle counts: sliding along a dock is not a crash.
            ContactPoint contact = collision.GetContact(0);
            float impactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (impactSpeed < minImpactSpeed)
                return;

            lastImpactTime = Time.time;
            LastImpactIntensity = Mathf.InverseLerp(minImpactSpeed, Mathf.Max(fullImpactSpeed, minImpactSpeed + 0.01f), impactSpeed);
            onImpact?.Invoke(contact.point, LastImpactIntensity);
        }
    }
}
