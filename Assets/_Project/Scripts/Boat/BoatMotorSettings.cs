using UnityEngine;

namespace FishingVR.Boat
{
    /// <summary>
    /// Characteristics of an outboard motor, stored as an asset so that several motors
    /// (shop upgrades in phase 4) can be swapped on the same boat without touching the prefab.
    /// Create one with: right-click in the Project window > Create > FishingVR > Boat > Motor Settings.
    /// </summary>
    [CreateAssetMenu(fileName = "MotorSettings_New", menuName = "FishingVR/Boat/Motor Settings")]
    public class BoatMotorSettings : ScriptableObject
    {
        [Header("Thrust")]
        [Tooltip("Forward push at full throttle (N). A small 5 hp outboard is roughly 500-700 N.")]
        [Min(0f)] public float maxThrust = 600f;

        [Tooltip("Reverse thrust as a fraction of the forward thrust (0 = no reverse).")]
        [Range(0f, 1f)] public float reverseThrustRatio = 0.4f;

        [Tooltip("How fast the actual throttle follows the requested one (throttle units per second).")]
        [Min(0.01f)] public float throttleResponse = 1.5f;

        [Header("Steering")]
        [Tooltip("Maximum rotation of the motor around its vertical axis (degrees).")]
        [Range(5f, 90f)] public float maxSteerAngle = 40f;

        [Tooltip("How fast the motor rotates towards the requested angle (degrees per second).")]
        [Min(1f)] public float steerResponse = 120f;

        [Header("Lower unit")]
        [Tooltip("Sideways resistance of the submerged part of the motor (N per (m/s)²). It acts like a small rudder: " +
                 "the boat still steers a little while coasting with the throttle cut. Bigger motor = bigger lower unit.")]
        [Min(0f)] public float lowerUnitDrag = 15f;

        [Header("Visuals")]
        [Tooltip("Propeller rotation speed at full throttle (degrees per second).")]
        public float propellerMaxSpeed = 2000f;
    }
}
