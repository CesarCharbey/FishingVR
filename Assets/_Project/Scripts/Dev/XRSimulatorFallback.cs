using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace FishingVR.Dev
{
    /// <summary>
    /// Dev-scene helper: spawns the XR Interaction Simulator in the Editor when no XR headset is
    /// actually displaying. With the Meta Link runtime installed, OpenXR starts even with the headset
    /// unplugged (loader active, but no display running and no tracked device), so we wait a moment for
    /// a running display before deciding. With a headset connected, it does nothing.
    /// Simulator controls: see the on-screen UI (WASD + mouse, Tab to switch device).
    /// </summary>
    public class XRSimulatorFallback : MonoBehaviour
    {
        [SerializeField] GameObject simulatorPrefab;
        [Tooltip("Spawn the simulator even when a headset is detected.")]
        [SerializeField] bool forceSimulator;
        [Tooltip("Seconds to wait for a headset display before falling back to the simulator.")]
        [SerializeField] float headsetWaitTime = 1.5f;

#if UNITY_EDITOR
        IEnumerator Start()
        {
            if (simulatorPrefab == null || XRInteractionSimulator.instance != null)
                yield break;

            if (!forceSimulator)
            {
                var t = 0f;
                while (t < headsetWaitTime)
                {
                    if (HeadsetDisplaying()) yield break;
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            Debug.Log("[XRSimulatorFallback] No XR headset displaying, starting XR Interaction Simulator.");
            Instantiate(simulatorPrefab);
        }

        static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();

        static bool HeadsetDisplaying()
        {
            SubsystemManager.GetSubsystems(Displays);
            foreach (var d in Displays)
                if (d.running) return true;
            return false;
        }
#endif
    }
}
