using UnityEngine;

namespace FishingVR.UI
{
    /// <summary>
    /// Shows a world-space UI canvas in VR through an image: the canvas is rendered once into a texture by a
    /// hidden orthographic camera (not in stereo) each time <see cref="Capture"/> is called, then the canvas is
    /// hidden and the texture is displayed on a quad with the FishingVR/VRImage shader.
    /// Why: TextMeshPro and UI/Default only draw in the left eye in single-pass instanced VR on this project;
    /// a quad with our own URP shader (FishingVR/VRImage, built like the water) draws in both eyes. Call <see cref="Capture"/> after changing the texts.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class CanvasSnapshot : MonoBehaviour
    {
        /// <summary>Layer the canvas is drawn on for the capture only (never seen by the player's camera).</summary>
        public const int CaptureLayer = 31;

        [Tooltip("Texture resolution: pixels per canvas unit (1 unit = 1 mm on our canvases).")]
        [SerializeField] float pixelsPerUnit = 4f;
        [SerializeField] int maxSize = 2048;

        Canvas canvas;
        Camera captureCamera;
        RenderTexture texture;
        MeshRenderer quad;
        Material quadMaterial;

        /// <summary>Transparency of the displayed image (fades).</summary>
        public float Alpha
        {
            set { if (quadMaterial != null) quadMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, value)); }
        }

        /// <summary>Shows / hides the image (the canvas itself stays hidden).</summary>
        public bool Visible
        {
            set { if (quad != null) quad.enabled = value; }
        }

        void Awake() => canvas = GetComponent<Canvas>();

        void OnDestroy()
        {
            if (texture != null) texture.Release();
            if (quadMaterial != null) Destroy(quadMaterial);
        }

        /// <summary>Re-renders the canvas into the image (after the texts changed).</summary>
        public void Capture()
        {
            if (canvas == null) canvas = GetComponent<Canvas>();
            var rt = (RectTransform)transform;
            var size = rt.rect.size; // canvas units
            if (size.x <= 0f || size.y <= 0f) return;
            Setup(size);

            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (quad == null || t != quad.transform) t.gameObject.layer = CaptureLayer;

            canvas.enabled = true;
            Canvas.ForceUpdateCanvases();
            var tr = transform;
            var worldHeight = size.y * tr.lossyScale.y;
            captureCamera.orthographicSize = worldHeight * 0.5f;
            captureCamera.aspect = size.x / size.y;
            captureCamera.transform.SetPositionAndRotation(tr.TransformPoint(rt.rect.center) - tr.forward * 0.5f, tr.rotation);
            captureCamera.Render();
            canvas.enabled = false;
        }

        void Setup(Vector2 size)
        {
            var w = Mathf.Clamp(Mathf.RoundToInt(size.x * pixelsPerUnit), 16, maxSize);
            var h = Mathf.Clamp(Mathf.RoundToInt(size.y * pixelsPerUnit), 16, maxSize);
            if (texture == null || texture.width != w || texture.height != h)
            {
                if (texture != null) texture.Release();
                texture = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) { name = name + "_Snapshot", antiAliasing = 4, useMipMap = true, autoGenerateMips = true };
                texture.Create();
            }

            if (captureCamera == null)
            {
                var go = new GameObject("SnapshotCamera");
                go.hideFlags = HideFlags.HideAndDontSave;
                captureCamera = go.AddComponent<Camera>();
                captureCamera.enabled = false;
                captureCamera.orthographic = true;
                captureCamera.cullingMask = 1 << CaptureLayer;
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                captureCamera.nearClipPlane = 0.01f;
                captureCamera.farClipPlane = 2f;
                captureCamera.stereoTargetEye = StereoTargetEyeMask.None;
                captureCamera.allowHDR = false;
                captureCamera.allowMSAA = true;
            }
            captureCamera.targetTexture = texture;

            if (quad == null)
            {
                var go = new GameObject("SnapshotQuad", typeof(MeshFilter), typeof(MeshRenderer));
                go.layer = 0;
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                quad = go.GetComponent<MeshRenderer>();
                quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                quad.receiveShadows = false;
                // Own URP shader with stereo instancing support (UI/Default only draws in the left eye here).
                var shader = Shader.Find("FishingVR/VRImage");
                if (shader == null) { Debug.LogError("[CanvasSnapshot] Shader FishingVR/VRImage not found."); shader = Shader.Find("UI/Default"); }
                quadMaterial = new Material(shader) { name = "SnapshotQuad" };
                quad.sharedMaterial = quadMaterial;
            }
            quadMaterial.SetTexture("_BaseMap", texture);
            var rt = (RectTransform)transform;
            quad.transform.localPosition = rt.rect.center;
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
        }
    }
}
