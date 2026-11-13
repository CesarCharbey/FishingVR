using UnityEngine;

namespace FishingVR.Fishing
{
    /// <summary>
    /// Small tension bar on top of the rod handle (VR-friendly, visible when looking at the rod).
    /// Green -> yellow -> red, blinks near the break point. Hidden when there is no fight.
    /// </summary>
    public class LineTensionGauge : MonoBehaviour
    {
        [SerializeField] FishingRod rod;
        [SerializeField] GameObject root;
        [Tooltip("Fill bar, scaled along its local Z from its back end (pivot at the back).")]
        [SerializeField] Transform fill;
        [SerializeField] Renderer fillRenderer;
        [SerializeField] Gradient colors;
        [SerializeField] float dangerThreshold = 0.85f;

        Material fillMaterial;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Reset()
        {
            colors = new Gradient();
            colors.SetKeys(
                new[] { new GradientColorKey(new Color(0.2f, 0.85f, 0.3f), 0f), new GradientColorKey(new Color(1f, 0.85f, 0.1f), 0.6f), new GradientColorKey(new Color(1f, 0.15f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        }

        void LateUpdate()
        {
            if (rod == null || fill == null)
                return;

            var line = rod.Line;
            var show = line != null && line.InFight;
            if (root != null && root.activeSelf != show)
                root.SetActive(show);
            if (!show)
                return;

            var t = Mathf.Clamp01(rod.LineTension);
            var s = fill.localScale;
            s.z = Mathf.Max(0.001f, t);
            fill.localScale = s;

            var color = colors != null ? colors.Evaluate(t) : Color.Lerp(Color.green, Color.red, t);
            if (t >= dangerThreshold && Mathf.Repeat(Time.time * 8f, 1f) > 0.5f)
                color = Color.white;
            // Per-renderer material instance (property blocks are ignored by the GPU resident drawer).
            if (fillMaterial == null && fillRenderer != null) fillMaterial = fillRenderer.material;
            if (fillMaterial != null) fillMaterial.SetColor(BaseColorId, color);
        }

        void OnDestroy()
        {
            if (fillMaterial != null) Destroy(fillMaterial);
        }
    }
}
