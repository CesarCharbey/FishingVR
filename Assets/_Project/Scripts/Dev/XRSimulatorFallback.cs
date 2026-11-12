using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Management;

namespace FishingVR.Dev
{
    /// <summary>
    /// Dev-scene helper: spawns the XR Interaction Simulator in the Editor when no XR headset
    /// is running (no active OpenXR loader). With a headset connected, it does nothing.
    /// Simulator controls: see the on-screen UI (WASD + mouse, Tab to switch device).
    /// </summary>
    public class XRSimulatorFallback : MonoBehaviour
    {
        [SerializeField] GameObject simulatorPrefab;
        [Tooltip("Spawn the simulator even when a headset is detected.")]
        [SerializeField] bool forceSimulator;

        void Start()
        {
#if UNITY_EDITOR
            if (simulatorPrefab == null || XRInteractionSimulator.instance != null)
                return;

            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            var headsetActive = manager != null && manager.activeLoader != null;
            if (headsetActive && !forceSimulator)
                return;

            Debug.Log("[XRSimulatorFallback] No XR headset detected, starting XR Interaction Simulator.");
            Instantiate(simulatorPrefab);
#endif
        }
    }
}
