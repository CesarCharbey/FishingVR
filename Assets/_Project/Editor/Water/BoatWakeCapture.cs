using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishingVR.Boat;
using FishingVR.Fishing;
using FishingVR.Water;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// In-game check of the boat's wake, no headset: plays Main, puts the boat in open water, drives it at full
    /// throttle in a straight line then in a full turn, and saves pictures of the foam behind it (chase, top, from
    /// the seat, side). Every picture is rendered twice on the same frame, without then with the contact foam, to
    /// check that the wake foam itself is untouched.
    ///   Unity.exe -batchmode -projectPath . -executeMethod FishingVR.EditorTools.BoatWakeCapture.Capture -captureDir "C:\shots"
    /// From the live CLI (Editor open): BoatWakeCapture.CaptureLive(dir) or CaptureLive(dir, variants: true).
    /// </summary>
    public static class BoatWakeCapture
    {
        const string MainScene = "Assets/_Project/Scenes/Main.unity";
        const string DirKey = "BoatWakeCapture.Dir";
        const string LiveKey = "BoatWakeCapture.Live";
        const string VariantsKey = "BoatWakeCapture.Variants";

        /// <summary>
        /// Batch entry point. Option -variants renders every view as it is now, then without the propeller wash's
        /// dense core, then without its bubble cloud tint (same frame), instead of without/with the contact foam,
        /// and measures how much the wake shimmers when the head moves.
        /// </summary>
        public static void Capture()
        {
            string dir = ArgValue("-captureDir") ?? Path.Combine(Directory.GetCurrentDirectory(), "Temp", "WakeCaptures");
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene(MainScene);
            SessionState.SetString(DirKey, dir);
            SessionState.SetBool(LiveKey, false);
            SessionState.SetBool(VariantsKey, Array.IndexOf(Environment.GetCommandLineArgs(), "-variants") >= 0);
            Debug.Log($"[WakeCapture] Entering play mode, pictures to {dir}");
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Live CLI entry point: plays the open scene (Main), captures, then stops Play mode.</summary>
        public static string CaptureLive(string dir, bool variants = false)
        {
            Directory.CreateDirectory(dir);
            SessionState.SetString(DirKey, dir);
            SessionState.SetBool(LiveKey, true);
            SessionState.SetBool(VariantsKey, variants);
            EditorApplication.EnterPlaymode();
            return $"Capturing to {dir}";
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && !string.IsNullOrEmpty(SessionState.GetString(DirKey, "")))
                    new Session(SessionState.GetString(DirKey, ""));
            };
        }

        static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }

        class Session
        {
            const int Width = 1600, Height = 900;
            const float StraightSeconds = 8f, TurnSeconds = 4f;

            readonly string dir;
            readonly Queue<(float at, Action action)> steps = new Queue<(float, Action)>();
            Camera cam;
            BoatMotor motor;
            Rigidbody body;
            BoatHydrodynamics hydro;
            Material waterMaterial;
            float steer, throttle;
            bool driving;

            public Session(string dir)
            {
                this.dir = dir;
                try
                {
                    Setup();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Finish();
                    return;
                }
                EditorApplication.update += Update;
            }

            void Setup()
            {
                ShaderUtil.allowAsyncCompilation = false;
                var main = Camera.main;
                var go = new GameObject("WakeCaptureCamera");
                cam = go.AddComponent<Camera>();
                if (main != null)
                    cam.CopyFrom(main);
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.fieldOfView = 60f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 800f;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                var origin = Object.FindAnyObjectByType<XROrigin>();
                if (origin != null)
                    origin.gameObject.SetActive(false);
                go.tag = "MainCamera";

                motor = Object.FindAnyObjectByType<BoatMotor>();
                if (motor == null)
                    throw new InvalidOperationException("[WakeCapture] No boat (BoatMotor) in the scene.");
                body = motor.GetComponent<Rigidbody>();
                hydro = motor.GetComponent<BoatHydrodynamics>();
                var water = Object.FindAnyObjectByType<WaterSurface>();
                waterMaterial = water != null && water.GetComponent<Renderer>() != null ? water.GetComponent<Renderer>().sharedMaterial : null;
                // A shader that does not compile still renders (its last good version): stop instead.
                if (waterMaterial != null && ShaderUtil.ShaderHasError(waterMaterial.shader))
                {
                    foreach (var message in ShaderUtil.GetShaderMessages(waterMaterial.shader))
                        Debug.LogError($"[WakeCapture] Shader error line {message.line}: {message.message}");
                    throw new InvalidOperationException("[WakeCapture] The water shader does not compile.");
                }

                // The ripples follow the boat, as they follow the player sitting in it in the game.
                var ripples = WaterRipples.Instance;
                if (ripples != null)
                {
                    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                    typeof(WaterRipples).GetField("follow", flags)?.SetValue(ripples, motor.transform);
                    Debug.Log("[WakeCapture] The ripple simulation follows the boat.");
                }
                else
                    Debug.LogWarning("[WakeCapture] No ripple simulation yet: it follows the capture camera.");

                float start = 1f;
                steps.Enqueue((start, PlaceInOpenWater));
                steps.Enqueue((start + 0.5f, () => { throttle = 1f; steer = 0f; driving = true; Debug.Log("[WakeCapture] Full throttle, straight."); }));
                steps.Enqueue((start + 1.5f, () => Shots("00_warmup", save: false)));
                float straight = start + 0.5f + StraightSeconds;
                steps.Enqueue((straight, () => Shots("straight")));
                steps.Enqueue((straight + 0.1f, () => { steer = 1f; Debug.Log("[WakeCapture] Full throttle, full turn."); }));
                steps.Enqueue((straight + 0.1f + TurnSeconds * 0.5f, () => Shots("turn_a")));
                steps.Enqueue((straight + 0.1f + TurnSeconds, () => Shots("turn_b")));
                steps.Enqueue((straight + 0.3f + TurnSeconds, Finish));
            }

            // A long straight run of deep water with room on both sides for the turn.
            void PlaceInOpenWater()
            {
                const float run = 90f, side = 22f, minDepth = 2.5f;
                for (float x = -90f; x <= 150f; x += 15f)
                for (float z = -190f; z <= 450f; z += 15f)
                {
                    var from = new Vector3(x, 0f, z);
                    if (FishingWater.DepthAt(from) < minDepth)
                        continue;
                    for (int h = 0; h < 8; h++)
                    {
                        float a = h * Mathf.PI / 4f;
                        var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                        var right = new Vector3(dir.z, 0f, -dir.x);
                        if (FishingWater.SwimmableDistance(from, dir, run, minDepth, 3f) < run
                            || FishingWater.SwimmableDistance(from + right * side, dir, run, minDepth, 3f) < run
                            || FishingWater.SwimmableDistance(from - right * side, dir, run, minDepth, 3f) < run
                            || FishingWater.SwimmableDistance(from + dir * run * 0.6f, right, side * 1.5f, minDepth, 3f) < side * 1.5f)
                            continue;
                        Teleport(from, dir);
                        Debug.Log($"[WakeCapture] Boat placed at {from}, heading {dir}");
                        return;
                    }
                }
                Debug.LogWarning("[WakeCapture] No open water run found: the boat stays where it is.");
            }

            void Teleport(Vector3 position, Vector3 heading)
            {
                position.y = motor.transform.position.y;
                var rotation = Quaternion.LookRotation(heading, Vector3.up);
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = position;
                body.rotation = rotation;
                motor.transform.SetPositionAndRotation(position, rotation);
            }

            void Update()
            {
                if (!EditorApplication.isPlaying)
                {
                    EditorApplication.update -= Update;
                    return;
                }
                if (driving)
                    motor.SetInput(steer, throttle);
                while (steps.Count > 0 && Time.time >= steps.Peek().at)
                {
                    var step = steps.Dequeue();
                    try { step.action(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }

            // -variants mode: the wake as it is, then without each part of the propeller wash (property set to 0).
            static readonly (string suffix, string property, float value)[] Variants =
            {
                ("current", null, 0f),
                ("no_core", "_WakeCore", 0f),
                ("no_tint", "_WakeBubbleTint", 0f),
            };

            bool VariantsMode => SessionState.GetBool(VariantsKey, false);

            // The same frame from several points of view: without then with the contact foam, or once per wake option.
            void Shots(string name, bool save = true)
            {
                var t = motor.transform;
                var fwd = Flat(t.forward).normalized;
                var right = new Vector3(fwd.z, 0f, -fwd.x);
                float waterY = FishingWater.Exists ? FishingWater.HeightAt(t.position) : 0f;
                var p = new Vector3(t.position.x, waterY, t.position.z);
                if (save)
                {
                    Debug.Log($"[WakeCapture] {name}: speed {(hydro != null ? hydro.ForwardSpeed * 3.6f : 0f):0.0} km/h, throttle {motor.Throttle:0.00}, rpm {motor.EngineRpm:0.00}, motor angle {motor.MotorAngle:0} deg");
                    LogFoamBehind(name, fwd);
                }

                View($"{name}_chase", p - fwd * 6f + right * 1.5f + Vector3.up * 3.5f, p - fwd * 1.5f, save);
                View($"{name}_top", p - fwd * 7f + Vector3.up * 16f, p - fwd * 6f, save, fwd);
                View($"{name}_seat", p + fwd * 0.6f + Vector3.up * 1.1f, p - fwd * 8f, save, flicker: true);
                View($"{name}_side", p + right * 9f - fwd * 3f + Vector3.up * 2.2f, p - fwd * 4f, save);
                // Right behind the propeller, and the whole wake down to where it ends.
                View($"{name}_close", p - fwd * 1.2f + right * 1.4f + Vector3.up * 1.6f, p - fwd * 3.2f, save);
                View($"{name}_wide", p - fwd * 4f + right * 10f + Vector3.up * 14f, p - fwd * 16f, save, flicker: true);
            }

            void View(string name, Vector3 eye, Vector3 target, bool save, Vector3? upHint = null, bool flicker = false)
            {
                var look = target - eye;
                var up = upHint.HasValue && Mathf.Abs(look.normalized.y) > 0.9f ? upHint.Value : Vector3.up;
                var rotation = Quaternion.LookRotation(look, up);
                cam.transform.SetPositionAndRotation(eye, rotation);
                if (!VariantsMode || waterMaterial == null)
                {
                    Pair(name, eye, save);
                    return;
                }
                (float[] std, float[] lum) currentShimmer = default;
                foreach (var (suffix, property, value) in Variants)
                {
                    float old = property != null ? waterMaterial.GetFloat(property) : 0f;
                    try
                    {
                        if (property != null)
                            waterMaterial.SetFloat(property, value);
                        Render(save ? Path.Combine(dir, $"{name}_{suffix}.png") : null);
                        // Shimmer of the wake as it is now, measured on the far views.
                        if (save && flicker && property == null)
                            currentShimmer = Flicker($"{name}_{suffix}", eye, rotation);
                    }
                    finally
                    {
                        if (property != null)
                            waterMaterial.SetFloat(property, old);
                        cam.transform.SetPositionAndRotation(eye, rotation);
                    }
                }
                if (currentShimmer.std != null)
                    WakeShimmer(name, eye, rotation, currentShimmer);
            }

            // Shimmer of the wake alone: on the pixels where the wake foam shows (compared with the same frame without
            // any wake foam), the glints of the sun and the light lines are left out.
            void WakeShimmer(string name, Vector3 eye, Quaternion rotation, (float[] std, float[] lum) withWake)
            {
                float strength = waterMaterial.GetFloat("_WakeFoamStrength"), crest = waterMaterial.GetFloat("_CrestFoam");
                (float[] std, float[] lum) none;
                try
                {
                    waterMaterial.SetFloat("_WakeFoamStrength", 0f);
                    waterMaterial.SetFloat("_CrestFoam", 0f);
                    none = Flicker(null, eye, rotation);
                }
                finally
                {
                    waterMaterial.SetFloat("_WakeFoamStrength", strength);
                    waterMaterial.SetFloat("_CrestFoam", crest);
                    cam.transform.SetPositionAndRotation(eye, rotation);
                }
                double sumWake = 0.0, sumNone = 0.0;
                int count = 0;
                for (int i = 0; i < none.lum.Length; i++)
                {
                    if (Mathf.Abs(withWake.lum[i] - none.lum[i]) < 15f)
                        continue;
                    sumWake += withWake.std[i];
                    sumNone += none.std[i];
                    count++;
                }
                if (count > 0)
                    Debug.Log($"[WakeCapture] Wake shimmer {name} over {count} wake pixels: without wake foam {sumNone / count:0.00}, with the wake {sumWake / count:0.00}");
            }

            // Shimmer: the same frame from 8 head positions a centimetre apart; per pixel spread of the brightness.
            // Saves a map (red = shimmer) when a name is given; returns the spread and the brightness of the first frame.
            (float[] std, float[] lum) Flicker(string name, Vector3 eye, Quaternion rotation)
            {
                const int frames = 8;
                var random = new System.Random(7);
                var sum = new float[Width * Height];
                var sumSq = new float[Width * Height];
                Color32[] first = null;
                for (int f = 0; f < frames; f++)
                {
                    var offset = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 0.02f;
                    var turn = Quaternion.Euler(((float)random.NextDouble() - 0.5f) * 0.08f, ((float)random.NextDouble() - 0.5f) * 0.08f, 0f);
                    cam.transform.SetPositionAndRotation(eye + offset, rotation * turn);
                    var pixels = Render(null, readBack: true);
                    first ??= pixels;
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        float l = 0.299f * pixels[i].r + 0.587f * pixels[i].g + 0.114f * pixels[i].b;
                        sum[i] += l;
                        sumSq[i] += l * l;
                    }
                }
                var stds = new float[Width * Height];
                var lums = new float[Width * Height];
                var heat = new Color32[Width * Height];
                for (int i = 0; i < heat.Length; i++)
                {
                    float mean = sum[i] / frames;
                    stds[i] = Mathf.Sqrt(Mathf.Max(0f, sumSq[i] / frames - mean * mean));
                    lums[i] = 0.299f * first[i].r + 0.587f * first[i].g + 0.114f * first[i].b;
                    byte grey = (byte)(0.3f * lums[i]);
                    byte red = (byte)Mathf.Clamp(stds[i] * 6f, 0f, 255f);
                    heat[i] = new Color32((byte)Mathf.Max(grey, red), grey, grey, 255);
                }
                if (name != null)
                {
                    var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                    tex.SetPixels32(heat);
                    tex.Apply();
                    File.WriteAllBytes(Path.Combine(dir, $"{name}_flicker.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                return (stds, lums);
            }

            // Foam of the ripple simulation on the line behind the propeller (to set the threshold of option 1).
            void LogFoamBehind(string name, Vector3 fwd)
            {
                var rt = Shader.GetGlobalTexture("_WaterRippleTex") as RenderTexture;
                var area = Shader.GetGlobalVector("_WaterRippleArea");
                if (rt == null || area.w < 0.5f)
                    return;
                var previous = RenderTexture.active;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false, true);
                try
                {
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                }
                var from = motor.Propeller != null ? motor.Propeller.position : motor.transform.position - fwd * 1.8f;
                var line = new System.Text.StringBuilder($"[WakeCapture] {name} behind the propeller (foam/churn):");
                foreach (float d in new[] { 0.25f, 0.5f, 1f, 1.5f, 2f, 3f, 4f, 6f, 8f, 10f, 12f, 14f, 16f, 20f, 24f, 28f })
                {
                    var q = from - fwd * d;
                    float u = (q.x - area.x) * area.z, v = (q.z - area.y) * area.z;
                    if (u is < 0f or > 1f || v is < 0f or > 1f)
                        line.Append($" {d}m=out");
                    else
                    {
                        var c = tex.GetPixelBilinear(u, v);
                        line.Append($" {d}m={c.b:0.00}/{c.a:0.00}");
                    }
                }
                // Across the wake (churn): does the wash open into a cone, with soft sides?
                var right = new Vector3(fwd.z, 0f, -fwd.x);
                foreach (float d in new[] { 1f, 3f, 5f })
                {
                    line.Append($"\n    churn across at {d} m:");
                    foreach (float x in new[] { -2f, -1.5f, -1f, -0.5f, 0f, 0.5f, 1f, 1.5f, 2f })
                    {
                        var q = from - fwd * d + right * x;
                        float u = (q.x - area.x) * area.z, v = (q.z - area.y) * area.z;
                        line.Append(u is < 0f or > 1f || v is < 0f or > 1f ? " out" : $" {tex.GetPixelBilinear(u, v).a:0.00}");
                    }
                }
                Object.DestroyImmediate(tex);
                Debug.Log(line.ToString());
            }

            void Pair(string name, Vector3 eye, bool save)
            {
                float width = 0f, lace = 0f;
                if (waterMaterial != null)
                {
                    width = waterMaterial.GetFloat("_ContactFoamWidth");
                    lace = waterMaterial.GetFloat("_ContactLaceWidth");
                }
                try
                {
                    // Without the contact foam: the water as it was before it.
                    if (waterMaterial != null)
                    {
                        waterMaterial.SetFloat("_ContactFoamWidth", 0f);
                        waterMaterial.SetFloat("_ContactLaceWidth", 0f);
                    }
                    Shader.SetGlobalFloat("_ContactShapeCount", 0f);
                    Render(save ? Path.Combine(dir, name + "_off.png") : null);
                }
                finally
                {
                    if (waterMaterial != null)
                    {
                        waterMaterial.SetFloat("_ContactFoamWidth", width);
                        waterMaterial.SetFloat("_ContactLaceWidth", lace);
                    }
                    WaterContactFoam.Publish(eye);
                }
                Render(save ? Path.Combine(dir, name + "_on.png") : null);
            }

            Color32[] Render(string path, bool readBack = false)
            {
                var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                try
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
                    if (path == null && !readBack)
                        return null;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply();
                    if (path != null)
                        File.WriteAllBytes(path, tex.EncodeToPNG());
                    var pixels = readBack ? tex.GetPixels32() : null;
                    Object.DestroyImmediate(tex);
                    return pixels;
                }
                finally
                {
                    RenderTexture.active = previous;
                    rt.Release();
                    Object.DestroyImmediate(rt);
                }
            }

            void Finish()
            {
                bool live = SessionState.GetBool(LiveKey, false);
                SessionState.EraseString(DirKey);
                SessionState.EraseBool(LiveKey);
                SessionState.EraseBool(VariantsKey);
                driving = false;
                if (cam != null)
                    Object.DestroyImmediate(cam.gameObject);
                Debug.Log("[WakeCapture] Done");
                EditorApplication.update -= Update;
                if (live)
                    EditorApplication.ExitPlaymode();
                else
                    EditorApplication.Exit(0);
            }

            static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        }
    }
}
