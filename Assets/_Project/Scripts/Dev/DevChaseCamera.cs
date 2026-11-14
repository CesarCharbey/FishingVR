using UnityEngine;
using UnityEngine.InputSystem;

namespace FishingVR.Dev
{
    /// <summary>
    /// Dev-only third-person camera that follows a target (the boat) to test it without a headset.
    /// While this GameObject is active, the objects listed in <see cref="disableWhileActive"/>
    /// (XR Origin, XR simulator fallback...) are deactivated so they do not fight for the
    /// keyboard and the Game view. Deactivate this GameObject to go back to normal VR testing.
    ///
    /// Controls (Game view focused): right mouse drag = orbit, mouse wheel = zoom.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class DevChaseCamera : MonoBehaviour
    {
        [Tooltip("Object to follow (the boat).")]
        [SerializeField] Transform target;

        [Tooltip("Deactivated while this camera is active (XR Origin, XR Simulator Fallback...).")]
        [SerializeField] GameObject[] disableWhileActive;

        [Header("Framing")]
        [SerializeField, Min(1f)] float distance = 7f;
        [SerializeField, Range(-10f, 80f)] float pitch = 20f;
        [Tooltip("Point looked at, relative to the target (m).")]
        [SerializeField] Vector3 lookOffset = new Vector3(0f, 0.8f, 0f);
        [Tooltip("Time (s) for the camera to catch up with the boat's heading. Higher = lazier camera.")]
        [SerializeField, Min(0f)] float followSmoothTime = 0.4f;

        [Header("Mouse")]
        [SerializeField] float orbitSensitivity = 0.2f;
        [Tooltip("Zoom change per wheel notch (fraction of the distance).")]
        [SerializeField, Range(0.01f, 0.5f)] float zoomStep = 0.1f;
        [SerializeField] Vector2 distanceLimits = new Vector2(2f, 30f);

        float orbitYaw;       // Extra rotation around the boat chosen with the mouse.
        float followedYaw;    // Smoothed heading of the boat.
        float yawVelocity;

        void OnEnable()
        {
            SetOthersActive(false);
            if (target != null)
                followedYaw = target.eulerAngles.y;
        }

        // When Play mode stops, Unity calls OnApplicationQuit before OnDisable: the scene is being
        // torn down, so we must not reactivate objects at that moment.
        bool quitting;
        void OnApplicationQuit() => quitting = true;

        void OnDisable()
        {
            if (!quitting)
                SetOthersActive(true);
        }

        void SetOthersActive(bool active)
        {
            if (disableWhileActive == null)
                return;
            foreach (var go in disableWhileActive)
                if (go != null)
                    go.SetActive(active);
        }

        void LateUpdate()
        {
            if (target == null)
                return;

            ReadMouse();

            // Follow only the boat's heading (yaw): ignoring its pitch and roll keeps the view stable.
            followedYaw = Mathf.SmoothDampAngle(followedYaw, target.eulerAngles.y, ref yawVelocity, followSmoothTime);

            Quaternion orientation = Quaternion.Euler(pitch, followedYaw + orbitYaw, 0f);
            Vector3 lookAt = target.position + lookOffset;
            transform.SetPositionAndRotation(lookAt - orientation * Vector3.forward * distance, orientation);
        }

        void ReadMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                orbitYaw += delta.x * orbitSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * orbitSensitivity, -10f, 80f);
            }

            // Only the wheel direction is used: raw scroll values differ between platforms.
            float scroll = mouse.scroll.ReadValue().y;
            if (!Mathf.Approximately(scroll, 0f))
                distance = Mathf.Clamp(distance * (1f - Mathf.Sign(scroll) * zoomStep), distanceLimits.x, distanceLimits.y);
        }
    }
}
