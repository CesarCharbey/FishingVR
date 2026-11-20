using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FishingVR.Water;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Screenshots of the water around everything that pokes out of the lake (rocks, dock posts, boat), to check
    /// the contact foam without a headset. Edit mode, same viewpoints every run (before / after comparisons):
    ///   Unity.exe -batchmode -projectPath . -executeMethod FishingVR.EditorTools.WaterFoamCapture.Capture -captureDir "C:\shots" -prefix before [-bakeContacts]
    /// From the live CLI (Editor open): WaterFoamCapture.CaptureTo(dir, prefix).
    /// </summary>
    public static class WaterFoamCapture
    {
        const string MainScene = "Assets/_Project/Scenes/Main.unity";
        const int Width = 1600, Height = 900;
        static bool showMoving = true;

        /// <summary>Batch mode entry point: opens Main, captures, quits.</summary>
        public static void Capture()
        {
            string dir = ArgValue("-captureDir") ?? Path.Combine(Directory.GetCurrentDirectory(), "Temp", "FoamCaptures");
            string prefix = ArgValue("-prefix") ?? "shot";
            int saved = 0;
            try
            {
                EditorSceneManager.OpenScene(ArgValue("-scene") ?? MainScene);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-bakeContacts") >= 0)
                    LakeDepthBaker.BakeContactsOnly();
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-setupMoving") >= 0)
                {
                    WaterContactFoamTools.Setup();
                    EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-compare") >= 0)
                    saved += CaptureTo(dir, "off", withContactFoam: false);
                saved += CaptureTo(dir, prefix);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.Exit(saved > 0 ? 0 : 1);
        }

        /// <summary>
        /// Captures the open scene into dir; returns the number of pictures saved. withContactFoam = false hides the
        /// contact foam (fixed and moving objects) for "before" pictures; the material is restored afterwards.
        /// </summary>
        public static int CaptureTo(string dir, string prefix, bool withContactFoam = true)
        {
            Directory.CreateDirectory(dir);
            var water = Object.FindAnyObjectByType<WaterSurface>();
            if (water == null)
            {
                Debug.LogError("[FoamCapture] No WaterSurface in the open scene.");
                return 0;
            }
            float waterY = water.transform.position.y;
            var waterRenderer = water.GetComponent<Renderer>();
            var waterShader = waterRenderer != null && waterRenderer.sharedMaterial != null ? waterRenderer.sharedMaterial.shader : null;
            if (waterShader != null && ShaderUtil.ShaderHasError(waterShader))
            {
                foreach (var message in ShaderUtil.GetShaderMessages(waterShader))
                    Debug.LogError($"[FoamCapture] Shader error line {message.line}: {message.message}");
                return 0;
            }

            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false; // final shaders on the first render
            var cam = CreateCamera();
            int saved = 0;
            var material = waterRenderer != null ? waterRenderer.sharedMaterial : null;
            float width = material != null ? material.GetFloat("_ContactFoamWidth") : 0f;
            float lace = material != null ? material.GetFloat("_ContactLaceWidth") : 0f;
            if (!withContactFoam && material != null)
            {
                material.SetFloat("_ContactFoamWidth", 0f);
                material.SetFloat("_ContactLaceWidth", 0f);
            }
            showMoving = withContactFoam;
            // The lid hiding the water inside the boats is only built in Play mode: build it for the pictures.
            var masks = new List<GameObject>();
            foreach (var mask in Object.FindObjectsByType<Boat.BoatWaterMask>(FindObjectsSortMode.None))
            {
                typeof(Boat.BoatWaterMask).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(mask, null);
                var generated = mask.transform.Find("WaterMask (generated)");
                if (generated != null)
                    masks.Add(generated.gameObject);
            }
            try
            {
                var targets = FindTargets(waterY, waterRenderer);
                foreach (var t in targets)
                    Debug.Log($"[FoamCapture] Target {t.name} at {t.contact} radius {t.radius:0.00}");

                // From the seat of each boat: what the player sees of the band along the hull.
                int boatIndex = 0;
                foreach (var foam in WaterContactFoam.All.Where(f => f != null && f.CurrentShape == WaterContactFoam.Shape.Outline))
                {
                    boatIndex++;
                    var heading = foam.HeadingDirection;
                    var right = new Vector3(heading.z, 0f, -heading.x);
                    var seat = new Vector3(foam.transform.position.x, waterY, foam.transform.position.z) - heading * 0.4f;
                    var eye = seat + Vector3.up * 1.1f;
                    // Leaning over the side: the water right against the hull.
                    var lean = seat + right * 0.45f + Vector3.up * 1.0f;
                    saved += Shot(cam, dir, $"{prefix}_inboard{boatIndex}_side", lean, seat + right * 1.0f + heading * 0.6f) ? 1 : 0;
                    saved += Shot(cam, dir, $"{prefix}_inboard{boatIndex}_bow", eye, seat + heading * 3.2f + right * 0.6f) ? 1 : 0;
                    // Close, from the water next to the boat (hull side, low).
                    var close = seat + right * 3.2f + heading * 1.2f + Vector3.up * 1.0f;
                    saved += Shot(cam, dir, $"{prefix}_boatside{boatIndex}", close, seat + right * 0.6f) ? 1 : 0;
                }

                // Overview of the water around the main camera (spawn): no stray foam on open water.
                var main = Camera.main;
                Vector3 spawn = main != null ? main.transform.position : (waterRenderer != null ? waterRenderer.bounds.center : Vector3.zero);
                Vector3 look = main != null ? Vector3.ProjectOnPlane(main.transform.forward, Vector3.up).normalized : Vector3.forward;
                if (look.sqrMagnitude < 0.5f)
                    look = Vector3.forward;
                var ground = new Vector3(spawn.x, waterY, spawn.z);
                saved += Shot(cam, dir, $"{prefix}_overview_high", ground - look * 6f + Vector3.up * 18f, ground + look * 18f) ? 1 : 0;
                saved += Shot(cam, dir, $"{prefix}_overview_seated", ground + Vector3.up * 1.2f, ground + look * 30f) ? 1 : 0;

                foreach (var t in targets)
                {
                    Vector3 away = OpenWaterDirection(t.contact, t.radius, waterY);
                    Vector3 side = Vector3.Cross(Vector3.up, away);
                    // High view, like the reference picture (about 45 degrees down).
                    Vector3 high = t.contact + away * (t.radius + 2.5f) + side * 0.8f + Vector3.up * (t.radius + 3f);
                    // Seated in the boat, a few metres away.
                    Vector3 seated = t.contact + away * (t.radius + 6f) - side * 1.5f + Vector3.up * 1.15f;
                    saved += Shot(cam, dir, $"{prefix}_{t.name}_high", high, t.contact) ? 1 : 0;
                    saved += Shot(cam, dir, $"{prefix}_{t.name}_seated", seated, t.contact + Vector3.up * 0.2f) ? 1 : 0;
                }
            }
            finally
            {
                Object.DestroyImmediate(cam.gameObject);
                foreach (var go in masks)
                    Object.DestroyImmediate(go);
                if (material != null)
                {
                    material.SetFloat("_ContactFoamWidth", width);
                    material.SetFloat("_ContactLaceWidth", lace);
                }
                showMoving = true;
                ShaderUtil.allowAsyncCompilation = async;
            }
            Debug.Log($"[FoamCapture] Done, {saved} pictures in {dir}");
            return saved;
        }

        struct Target
        {
            public string name;
            public Vector3 contact; // centre of the object at the water level
            public float radius;
        }

        // Rocks, dock posts and boats crossing the water plane (a few of each kind, spread over the lake).
        static List<Target> FindTargets(float waterY, Renderer waterRenderer)
        {
            var found = new List<(string kind, Renderer r)>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r == waterRenderer || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                var b = r.bounds;
                if (b.min.y > waterY - 0.05f || b.max.y < waterY + 0.05f)
                    continue;
                if (Mathf.Max(b.size.x, b.size.z) > 12f)
                    continue; // terrain chunks, big props
                if (!IsInWater(b.center, waterY))
                    continue;
                string kind = Kind(r.transform);
                if (kind != null)
                    found.Add((kind, r));
            }

            var targets = new List<Target>();
            foreach (var group in found.GroupBy(f => f.kind))
            {
                // A few per kind, the ones furthest apart from each other.
                var picked = new List<Renderer>();
                foreach (var f in group.OrderBy(f => f.r.name).ThenBy(f => f.r.bounds.center.x))
                {
                    if (picked.Count >= MaxPerKind(group.Key))
                        break;
                    if (picked.Any(p => Vector3.Distance(p.bounds.center, f.r.bounds.center) < 6f))
                        continue;
                    picked.Add(f.r);
                }
                for (int i = 0; i < picked.Count; i++)
                {
                    var b = picked[i].bounds;
                    targets.Add(new Target
                    {
                        name = $"{group.Key}{i + 1}",
                        contact = new Vector3(b.center.x, waterY, b.center.z),
                        radius = Mathf.Max(b.extents.x, b.extents.z),
                    });
                }
            }
            return targets;
        }

        static int MaxPerKind(string kind) => kind == "rock" ? 4 : kind == "boat" ? 1 : 2;

        static string Kind(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                string n = t.name;
                if (n.IndexOf("Rock", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Stone", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "rock";
                if (n.IndexOf("Dock", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Post", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "dock";
                if (n.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "boat";
                if (n.IndexOf("Cattail", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Reed", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "reeds";
                if (n.IndexOf("Buoy", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "buoy";
            }
            return null;
        }

        // Water around the point (not an object half buried in the bank).
        static bool IsInWater(Vector3 center, float waterY)
        {
            int wet = 0;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.5f;
                if (GroundY(p) < waterY - 0.2f)
                    wet++;
            }
            return wet >= 4;
        }

        // Direction (flat) towards the deepest water around the target: the camera stands over the lake.
        static Vector3 OpenWaterDirection(Vector3 contact, float radius, float waterY)
        {
            Vector3 best = Vector3.forward;
            float bestDepth = float.MinValue;
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float depth = 0f;
                for (float d = radius + 1f; d <= radius + 8f; d += 1f)
                    depth += Mathf.Min(waterY - GroundY(contact + dir * d), 3f);
                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    best = dir;
                }
            }
            return best;
        }

        static float GroundY(Vector3 p)
        {
            return Physics.Raycast(new Vector3(p.x, p.y + 60f, p.z), Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : float.NegativeInfinity;
        }

        static Camera CreateCamera()
        {
            var go = new GameObject("FoamCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null)
                cam.CopyFrom(main);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 800f;
            cam.enabled = false;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            return cam;
        }

        static bool Shot(Camera cam, string dir, string name, Vector3 eye, Vector3 target)
        {
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
            WaterContactFoam.Publish(eye); // moving objects: the components only publish on their own update
            if (!showMoving)
                Shader.SetGlobalFloat("_ContactShapeCount", 0f);
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                for (int i = 0; i < 2; i++) // the first render may still use placeholder shaders
                {
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, request))
                        RenderPipeline.SubmitRenderRequest(cam, request);
                    else
                    {
                        cam.targetTexture = rt;
                        cam.Render();
                        cam.targetTexture = null;
                    }
                }
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }
    }
}
