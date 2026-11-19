using FishingVR.Boat;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace FishingVR.Dev
{
    /// <summary>
    /// Dev tool: logs every boat event in the Console, to check them before any sound or effect exists.
    /// Put it on the boat (or anywhere, with the components assigned). Subscribes in code with AddListener,
    /// which is what an audio or VFX script will do too.
    /// </summary>
    public class DevBoatEventLogger : MonoBehaviour
    {
        [SerializeField] BoatMotor motor;
        [SerializeField] BoatImpacts impacts;
        [SerializeField] BoatSeat seat;
        [SerializeField] TillerInteractable tiller;

        void Reset() => FindComponents();

        void Awake()
        {
            if (motor == null || impacts == null || seat == null || tiller == null)
                FindComponents();
        }

        void FindComponents()
        {
            if (motor == null) motor = GetComponentInParent<BoatMotor>();
            if (impacts == null) impacts = GetComponentInParent<BoatImpacts>();
            if (seat == null) seat = GetComponentInParent<BoatSeat>();
            if (tiller == null) tiller = GetComponentInChildren<TillerInteractable>();
        }

        void OnEnable()
        {
            if (motor != null)
            {
                motor.onPropellerSurfaced.AddListener(OnPropellerSurfaced);
                motor.onPropellerSubmerged.AddListener(OnPropellerSubmerged);
            }
            if (impacts != null)
                impacts.onImpact.AddListener(OnImpact);
            if (seat != null)
            {
                seat.onMounted.AddListener(OnMounted);
                seat.onDismounted.AddListener(OnDismounted);
            }
            if (tiller != null)
            {
                // Grab and release events come from XRI itself (every interactable has them).
                tiller.selectEntered.AddListener(OnTillerGrabbed);
                tiller.selectExited.AddListener(OnTillerReleased);
            }
        }

        // Always unsubscribe what was subscribed, or a destroyed listener would still be called.
        void OnDisable()
        {
            if (motor != null)
            {
                motor.onPropellerSurfaced.RemoveListener(OnPropellerSurfaced);
                motor.onPropellerSubmerged.RemoveListener(OnPropellerSubmerged);
            }
            if (impacts != null)
                impacts.onImpact.RemoveListener(OnImpact);
            if (seat != null)
            {
                seat.onMounted.RemoveListener(OnMounted);
                seat.onDismounted.RemoveListener(OnDismounted);
            }
            if (tiller != null)
            {
                tiller.selectEntered.RemoveListener(OnTillerGrabbed);
                tiller.selectExited.RemoveListener(OnTillerReleased);
            }
        }

        void OnPropellerSurfaced() => Debug.Log("[Boat] Propeller out of the water", this);
        void OnPropellerSubmerged() => Debug.Log("[Boat] Propeller back in the water", this);
        void OnImpact(Vector3 point, float intensity) => Debug.Log($"[Boat] Impact at {point}, intensity {intensity:0.00}", this);
        void OnMounted(XROrigin origin) => Debug.Log($"[Boat] '{origin.name}' seated at the helm", this);
        void OnDismounted(XROrigin origin) => Debug.Log($"[Boat] '{origin.name}' left the boat", this);
        void OnTillerGrabbed(SelectEnterEventArgs args) => Debug.Log("[Boat] Tiller grabbed", this);
        void OnTillerReleased(SelectExitEventArgs args) => Debug.Log("[Boat] Tiller released", this);
    }
}
