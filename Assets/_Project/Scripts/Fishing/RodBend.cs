using UnityEngine;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Visual bend of the rod blank toward the line, proportional to the line tension.
    /// Each segment adds its own angle, the tip bending the most.
    /// Runs before FishingLine draws the line from the (bent) tip.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(FishingRod))]
    public class RodBend : MonoBehaviour
    {
        [Tooltip("Blank segments from the handle to the tip.")]
        [SerializeField] Transform[] segments;
        [Tooltip("Max bend (degrees) of each segment at full tension.")]
        [SerializeField] float[] maxBendDegrees = { 4f, 8f, 12f, 16f };
        [SerializeField] float smoothing = 10f;

        FishingRod rod;
        Quaternion[] restRotations;
        float tension;

        void Awake()
        {
            rod = GetComponent<FishingRod>();
            restRotations = new Quaternion[segments.Length];
            for (var i = 0; i < segments.Length; i++)
                restRotations[i] = segments[i].localRotation;
        }

        void LateUpdate()
        {
            tension = Mathf.Lerp(tension, Mathf.Clamp01(rod.LineTension), 1f - Mathf.Exp(-smoothing * Time.deltaTime));

            var rodForward = transform.forward;
            var toLine = rod.LinePullPoint - rod.RodTip.position;
            var axis = Vector3.Cross(rodForward, toLine);
            var bendable = tension > 0.001f && axis.sqrMagnitude > 1e-6f;
            axis = bendable ? axis.normalized : Vector3.zero;
            // Pulling straight along the rod doesn't bend it.
            var sideFactor = bendable ? Mathf.Clamp01(Vector3.Angle(rodForward, toLine) / 60f) : 0f;

            for (var i = 0; i < segments.Length; i++)
            {
                segments[i].localRotation = restRotations[i];
                if (!bendable) continue;
                var max = i < maxBendDegrees.Length ? maxBendDegrees[i] : maxBendDegrees[^1];
                segments[i].rotation = Quaternion.AngleAxis(max * tension * sideFactor, axis) * segments[i].rotation;
            }
        }
    }
}
