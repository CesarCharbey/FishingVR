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
    /// From the live CLI (Editor open): BoatWakeCapture.CaptureLive(dir).
    /// </summary>
    public static class BoatWakeCapture
    {
        const string MainScene = "Assets/_Project/Scenes/Main.unity";
        const string DirKey = "BoatWakeCapture.Dir";
        const string LiveKey = "BoatWakeCapture.Live";

        public static void Capture()
        {
            string dir = ArgValue("-captureDir") ?? Path.Combine(Directory.GetCurrentDirectory(), "Temp", "WakeCaptures");
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene(MainScene);
            SessionState.SetString(DirKey, dir);
            SessionState.SetBool(LiveKey, false);
            Debug.Log($"[WakeCapture] Entering play mode, pictures to {dir}");
            EditorApplication.EnterPlaymode();
        }

        public static string CaptureLive(string dir)
        {
            Directory.CreateDirectory(dir);
            SessionState.SetString(DirKey, dir);
            SessionState.SetBool(LiveKey, true);
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

                // The ripples follow the boat, as they follow the player sitting in it in the game.
                if (WaterRipples.Instance != null)
                    typeof(WaterRipples).GetField("follow", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(WaterRipples.Instance, motor.transform);

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

            // The same frame from several points of view, each without then with the contact foam.
            void Shots(string name, bool save = true)
            {
                var t = motor.transform;
                var fwd = Flat(t.forward).normalized;
                var right = new Vector3(fwd.z, 0f, -fwd.x);
                float waterY = FishingWater.Exists ? FishingWater.HeightAt(t.position) : 0f;
                var p = new Vector3(t.position.x, waterY, t.position.z);
                if (save)
                    Debug.Log($"[WakeCapture] {name}: speed {(hydro != null ? hydro.ForwardSpeed * 3.6f : 0f):0.0} km/h, throttle {motor.Throttle:0.00}, rpm {motor.EngineRpm:0.00}, motor angle {motor.MotorAngle:0} deg");

                Pair($"{name}_chase", p - fwd * 6f + right * 1.5f + Vector3.up * 3.5f, p - fwd * 1.5f, save);
                Pair($"{name}_top", p - fwd * 7f + Vector3.up * 16f, p - fwd * 6f, save, fwd);
                Pair($"{name}_seat", p + fwd * 0.6f + Vector3.up * 1.1f, p - fwd * 8f, save);
                Pair($"{name}_side", p + right * 9f - fwd * 3f + Vector3.up * 2.2f, p - fwd * 4f, save);
            }

            void Pair(string name, Vector3 eye, Vector3 target, bool save, Vector3? upHint = null)
            {
                var look = target - eye;
                var up = upHint.HasValue && Mathf.Abs(look.normalized.y) > 0.9f ? upHint.Value : Vector3.up;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look, up));

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

            void Render(string path)
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
                    if (path == null)
                        return;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply();
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
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
