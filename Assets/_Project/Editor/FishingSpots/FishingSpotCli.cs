using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using FishingVR.Fish;
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
    /// Command-line checks for the fishing spots (Unity closed, batch mode):
    ///   Unity.exe -batchmode -projectPath . -executeMethod FishingVR.EditorTools.FishingSpotCli.SelfTest -quit
    ///   Unity.exe -batchmode -projectPath . -executeMethod FishingVR.EditorTools.FishingSpotCli.Capture -captureDir "C:\shots"
/// From the live CLI (Editor open): RunSelfTests() and CaptureLive(dir); FishingSpotPlayTest.Run() for the play test.
    /// SelfTest checks the life cycle and the depletion rules. Capture plays the Main scene, drops a spot in deep
    /// water off the dock and saves screenshots of it from several distances (no headset needed).
    /// </summary>
    public static class FishingSpotCli
    {
        const string MainScene = "Assets/_Project/Scenes/Main.unity";
        const string CaptureKey = "FishingSpotCli.CaptureDir";
        const string LiveKey = "FishingSpotCli.Live";

        // ------------------------------------------------------------------------------------------------
        // Self test (edit mode)
        // ------------------------------------------------------------------------------------------------

        /// <summary>Batch mode entry point: runs the tests and quits with 0 (all passed) or 1.</summary>
        public static void SelfTest()
        {
            int failures = RunSelfTests();
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        /// <summary>Runs the tests in the current Editor (also from the live CLI) and returns the failure count.</summary>
        public static int RunSelfTests()
        {
            int failures = 0;
            void Check(bool ok, string what)
            {
                Debug.Log($"[SpotTest] {(ok ? "PASS" : "FAIL")} {what}");
                if (!ok) failures++;
            }

            var created = new List<GameObject>();
            FishingSpot NewSpot(float arrive, float activeMin, float leave)
            {
                var go = new GameObject("SpotTest");
                created.Add(go);
                var spot = go.AddComponent<FishingSpot>();
                var so = new SerializedObject(spot);
                so.FindProperty("arriveDuration").floatValue = arrive;
                so.FindProperty("activeMinutes").floatValue = activeMin;
                so.FindProperty("leaveDuration").floatValue = leave;
                so.FindProperty("catchDepletion").floatValue = 0.2f;
                so.FindProperty("escapeDepletion").floatValue = 0.1f;
                so.FindProperty("emptyIntensity").floatValue = 0.3f;
                so.FindProperty("intensitySmoothing").floatValue = 10f;
                so.FindProperty("destroyWhenGone").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                return spot;
            }
            void Run(FishingSpot spot, float seconds)
            {
                for (float t = 0f; t < seconds; t += 0.05f)
                    spot.Tick(0.05f);
            }

            try
            {
                // 1. Arrival.
                var a = NewSpot(10f, 1f, 5f);
                Check(a.CurrentPhase == FishingSpot.Phase.Arriving && a.LifeIntensity == 0f, "starts Arriving at intensity 0");
                Run(a, 5f);
                Check(a.LifeIntensity > 0.2f && a.LifeIntensity < 0.8f, $"half way through arriving, intensity in between ({a.LifeIntensity:0.00})");
                Run(a, 5.5f);
                Check(a.CurrentPhase == FishingSpot.Phase.Active, "Active after the arrive duration");
                Run(a, 1f);
                Check(Mathf.Abs(a.LifeIntensity - 1f) < 0.01f, $"full intensity when active ({a.LifeIntensity:0.00})");

                // 2. Depletion: a catch costs twice an escape.
                float before = a.LifeIntensity;
                a.RegisterCatch();
                Run(a, 1f);
                float afterCatch = a.LifeIntensity;
                a.RegisterEscape();
                Run(a, 1f);
                float afterEscape = a.LifeIntensity;
                float catchDrop = before - afterCatch, escapeDrop = afterCatch - afterEscape;
                Check(Mathf.Abs(a.Stock - 0.7f) < 0.001f, $"stock after a catch and an escape ({a.Stock:0.00}, expected 0.70)");
                Check(Mathf.Abs(catchDrop - 2f * escapeDrop) < 0.01f, $"a catch lowers the intensity twice as much as an escape ({catchDrop:0.000} vs {escapeDrop:0.000})");
                Check(a.CurrentPhase == FishingSpot.Phase.Active, "still Active while fish are left");

                // 3. Empty school leaves early, then is gone.
                bool goneFired = false;
                a.onGone.AddListener(_ => goneFired = true);
                for (int i = 0; i < 4; i++) a.RegisterCatch();
                Check(a.Stock == 0f && a.CurrentPhase == FishingSpot.Phase.Leaving, "empty stock starts Leaving");
                float leavingStart = a.LifeIntensity;
                Run(a, 2.5f);
                Check(a.LifeIntensity < leavingStart, $"intensity fades while leaving ({leavingStart:0.00} -> {a.LifeIntensity:0.00})");
                Run(a, 3f);
                Check(a.CurrentPhase == FishingSpot.Phase.Gone && goneFired && a.LifeIntensity == 0f, "Gone after the leave duration, event fired, intensity 0");
                a.RegisterCatch();
                Check(a.Stock == 0f && a.CurrentPhase == FishingSpot.Phase.Gone, "catches after leaving are ignored");

                // 4. Time is up.
                var b = NewSpot(2f, 0.5f, 3f);
                Run(b, 2f + 30f + 0.2f);
                Check(b.CurrentPhase == FishingSpot.Phase.Leaving, "Leaving when the active time is over");
                Check(Mathf.Abs(b.TimeLeft) < 0.001f, "no time left once leaving");

                // 5. Area weight.
                b.transform.position = new Vector3(10f, 0f, 10f);
                Check(Mathf.Abs(b.AreaWeight(new Vector3(10f, 0f, 10f)) - 1f) < 0.001f, "area weight 1 at the centre");
                Check(b.AreaWeight(new Vector3(10f + b.Radius, 0f, 10f)) < 0.001f, "area weight 0 at the edge");
                Check(b.AreaWeight(new Vector3(10f + b.Radius * 0.75f, 0f, 10f)) is > 0f and < 1f, "area weight fades in between");

                // 6. Jump rate with the player's distance: full from afar, reduced when close.
                Check(Mathf.Approximately(FishingSpotJumps.DistanceRateFactor(30f, 8f, 30f, 0.2f), 1f), "full jump rate at the far distance");
                Check(Mathf.Approximately(FishingSpotJumps.DistanceRateFactor(80f, 8f, 30f, 0.2f), 1f), "full jump rate beyond the far distance");
                Check(Mathf.Approximately(FishingSpotJumps.DistanceRateFactor(8f, 8f, 30f, 0.2f), 0.2f), "reduced jump rate at the near distance");
                Check(Mathf.Approximately(FishingSpotJumps.DistanceRateFactor(2f, 8f, 30f, 0.2f), 0.2f), "reduced jump rate next to the spot");
                float mid = FishingSpotJumps.DistanceRateFactor(19f, 8f, 30f, 0.2f);
                Check(mid > 0.2f && mid < 1f, $"jump rate in between at mid distance ({mid:0.00})");

                // 7. Influence: 1 in the inner circle, 0 outside the big one, in between on the ring.
                var far = new Vector3(5000f, 0f, 5000f);
                var inf = NewSpot(1f, 1f, 1f);
                inf.transform.position = far;
                float r = inf.Radius, inner = inf.InnerRadius;
                Check(Mathf.Approximately(FishingSpot.InfluenceAt(far, out var hit), 1f) && hit == inf, "influence 1 at the centre, spot returned");
                Check(Mathf.Approximately(FishingSpot.InfluenceAt(far + Vector3.right * inner * 0.98f), 1f), $"influence 1 just inside the inner circle ({inner:0.00} m)");
                float ring = FishingSpot.InfluenceAt(far + Vector3.right * (inner + r) * 0.5f);
                Check(ring > 0f && ring < 1f, $"influence in between on the ring ({ring:0.00})");
                Check(FishingSpot.InfluenceAt(far + Vector3.forward * r * 1.01f, out var none) == 0f && none == null, "influence 0 outside the big circle, no spot");
                float prev = 2f; bool monotonic = true;
                for (float x = 0f; x <= r * 1.1f; x += r / 40f) { float v = FishingSpot.InfluenceAt(far + Vector3.right * x); if (v > prev + 1e-5f) monotonic = false; prev = v; }
                Check(monotonic, "influence only decreases from the centre outwards");

                // 8. Number of spots: 3 to 6, 4 the most likely, 6 rare, about 4 on average.
                var weights = new List<float> { 3f, 5f, 2.5f, 0.8f };
                var counts = new int[10];
                const int samples = 20000;
                for (int i = 0; i < samples; i++)
                    counts[FishingSpotManager.DrawCount(3, 6, weights, (i + 0.5f) / samples)]++;
                Check(counts[0] + counts[1] + counts[2] == 0 && counts[7] == 0, "never fewer than 3 or more than 6 spots");
                Check(counts[4] > counts[3] && counts[4] > counts[5] && counts[6] < counts[3] && counts[6] < counts[5], $"4 most likely, 6 rarest (3:{counts[3]} 4:{counts[4]} 5:{counts[5]} 6:{counts[6]})");
                float avg = FishingSpotManager.AverageCount(3, 6, weights);
                float sampled = (3f * counts[3] + 4f * counts[4] + 5f * counts[5] + 6f * counts[6]) / samples;
                Check(Mathf.Abs(avg - 4f) < 0.2f && Mathf.Abs(sampled - avg) < 0.01f, $"about 4 spots on average ({avg:0.00}, sampled {sampled:0.00})");

                // 9. Waits: 20-40 s outside, 5-10 s in a full spot, blended in between.
                var outside = new Vector2(20f, 40f); var inside = new Vector2(5f, 10f);
                Check(Mathf.Approximately(FishBiteAI.SpotWait(0f, 0f, outside, inside), 20f) && Mathf.Approximately(FishBiteAI.SpotWait(0f, 1f, outside, inside), 40f), "outside a spot: 20 to 40 s");
                Check(Mathf.Approximately(FishBiteAI.SpotWait(1f, 0f, outside, inside), 5f) && Mathf.Approximately(FishBiteAI.SpotWait(1f, 1f, outside, inside), 10f), "in a full spot: 5 to 10 s");
                float half = FishBiteAI.SpotWait(0.5f, 0.5f, outside, inside);
                Check(half > 7.5f && half < 30f, $"half influence: in between ({half:0.0} s)");

                // 10. Rarity: rare species very rare outside, favoured in a spot.
                var rOut = new[] { 1f, 0.6f, 0.25f, 0.08f, 0.02f }; var rIn = new[] { 1f, 1.5f, 2.5f, 4f, 6f };
                Check(Mathf.Approximately(FishBiteAI.SpotRarityMultiplier(FishRarity.Legendary, 0f, rOut, rIn), 0.02f), "legendary x0.02 outside the spots");
                Check(Mathf.Approximately(FishBiteAI.SpotRarityMultiplier(FishRarity.Legendary, 1f, rOut, rIn), 6f), "legendary x6 in a full spot");
                Check(Mathf.Approximately(FishBiteAI.SpotRarityMultiplier(FishRarity.Common, 0.3f, rOut, rIn), 1f), "commons unchanged");

                // 11. Size: big fish rarer outside, more common in a spot; weight still follows the length.
                var trout = AssetDatabase.FindAssets("t:FishSpecies").Select(g => AssetDatabase.LoadAssetAtPath<FishSpecies>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(s => s != null);
                if (trout != null)
                {
                    float MeanLength(float scale, out float meanWeight, out float bigShare)
                    {
                        float sum = 0f, wsum = 0f; int big = 0; const int n = 6000;
                        float bigThreshold = Mathf.Lerp(trout.lengthCmRange.x, trout.lengthCmRange.y, 0.8f);
                        for (int i = 0; i < n; i++) { var f = trout.Roll(scale); sum += f.lengthCm; wsum += f.weightKg; if (f.lengthCm >= bigThreshold) big++; }
                        meanWeight = wsum / n; bigShare = (float)big / n;
                        return sum / n;
                    }
                    float lOut = MeanLength(1.75f, out var wOut, out var bOut);
                    float lPlain = MeanLength(1f, out var wPlain, out var bPlain);
                    float lIn = MeanLength(0.5f, out var wIn, out var bIn);
                    Check(lOut < lPlain && lPlain < lIn, $"{trout.displayName}: mean length outside {lOut:0.0} < plain {lPlain:0.0} < in a spot {lIn:0.0} cm");
                    Check(wOut < wPlain && wPlain < wIn, $"{trout.displayName}: the weight follows ({wOut:0.00} < {wPlain:0.00} < {wIn:0.00} kg)");
                    Check(bOut < 0.1f && bIn > bPlain, $"{trout.displayName}: big fish (top 20% of sizes) {bOut:P0} outside, {bPlain:P0} plain, {bIn:P0} in a spot");
                }
                else
                    Check(false, "a fish species asset to test the sizes");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                failures++;
            }
            finally
            {
                foreach (var go in created)
                    Object.DestroyImmediate(go);
            }

            Debug.Log(failures == 0 ? "[SpotTest] ALL PASSED" : $"[SpotTest] {failures} FAILURE(S)");
            return failures;
        }

        // ------------------------------------------------------------------------------------------------
        // Screenshots (play mode)
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Live CLI entry point (Editor already open): plays the open scene, captures, then stops Play mode.
        /// Uses the spot already in the scene if there is one (same viewpoints as the batch captures).
        /// </summary>
        public static string CaptureLive(string dir)
        {
            Directory.CreateDirectory(dir);
            SessionState.SetString(CaptureKey, dir);
            SessionState.SetBool(LiveKey, true);
            EditorApplication.EnterPlaymode();
            return $"Capturing to {dir}";
        }

        public static void Capture()
        {
            SessionState.SetBool(LiveKey, false);
            string dir = ArgValue("-captureDir") ?? Path.Combine(Directory.GetCurrentDirectory(), "Temp", "SpotCaptures");
            Directory.CreateDirectory(dir);
            bool rebuild = Array.IndexOf(Environment.GetCommandLineArgs(), "-rebuildPrefab") >= 0;
            if (rebuild || AssetDatabase.LoadAssetAtPath<GameObject>(FishingSpotTools.PrefabPath) == null)
                FishingSpotTools.CreateOrUpdatePrefab();
            EditorSceneManager.OpenScene(MainScene);
            SessionState.SetString(CaptureKey, dir);
            Debug.Log($"[SpotCapture] Entering play mode, screenshots to {dir}");
            EditorApplication.EnterPlaymode();
        }

        static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }

        [InitializeOnLoadMethod]
        static void HookCapture()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && !string.IsNullOrEmpty(SessionState.GetString(CaptureKey, "")))
                    CaptureSession.Begin(SessionState.GetString(CaptureKey, ""));
            };
        }

        class CaptureSession
        {
            readonly string dir;
            readonly Queue<(float at, Action action)> steps = new();
            float startTime;
            Camera cam;
            GameObject spotObject;
            FishingSpot spot;
            Vector3 spotCenter, dockPoint;

            public static void Begin(string dir)
            {
                var session = new CaptureSession(dir);
                EditorApplication.update += session.Update;
            }

            CaptureSession(string dir)
            {
                this.dir = dir;
                startTime = Time.realtimeSinceStartup;
                Setup();
            }

            void Setup()
            {
                // Our own camera instead of the headset rig (it would stay at the spawn point).
                var main = Camera.main;
                var go = new GameObject("CaptureCamera");
                cam = go.AddComponent<Camera>();
                if (main != null)
                {
                    cam.CopyFrom(main);
                    cam.stereoTargetEye = StereoTargetEyeMask.None;
                }
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

                // The interactive ripples follow our camera.
                if (WaterRipples.Instance != null)
                    typeof(WaterRipples).GetField("follow", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(WaterRipples.Instance, cam.transform);

                // Spot in deep water, in view from the end of the dock: the scene's own spot if it has one.
                var dock = GameObject.Find("SM_Dock_End");
                dockPoint = dock != null ? dock.transform.position : new Vector3(102.76f, 0.18f, 165.94f);
                if (FishingSpot.All.Count > 0)
                {
                    spotObject = FishingSpot.All[0].gameObject;
                    spotCenter = spotObject.transform.position;
                    spotObject.SetActive(false);
                }
                else if (!FishingSpotTools.FindDeepWater(dockPoint, 18f, 45f, 2.5f, out spotCenter))
                    Debug.LogWarning("[SpotCapture] No deep water found off the dock, using a point in front of it.");
                spotCenter.y = FishingWater.Exists ? FishingWater.HeightAt(spotCenter) : 0f;
                Debug.Log($"[SpotCapture] Dock {dockPoint}, spot at {spotCenter} (depth {FishingWater.DepthAt(spotCenter):0.0} m)");

                Vector3 toSpot = Flat(spotCenter - dockPoint).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, toSpot);
                Vector3 dockEye = dockPoint + Vector3.up * 1.6f;
                Vector3 boatEye = spotCenter - toSpot * 8f + side * 2f + Vector3.up * 1.15f;
                // From the open lake, looking back at the spot (the dock side is the shore).
                Vector3 farEye = spotCenter + toSpot * 40f + side * 8f + Vector3.up * 1.6f;
                Vector3 jumpEye = spotCenter - toSpot * 9f - side * 3f + Vector3.up * 1.2f;

                // Warm-up renders: shaders compile asynchronously in the editor, the first frames are not final.
                Step(2f, () => Shot("00_warmup", dockEye, spotCenter, save: false));
                Step(2.5f, () => Shot("00_warmup", farEye, spotCenter, save: false));
                Step(5f, () => Shot("01_dock_no_spot", dockEye, spotCenter));
                Step(5.2f, SpawnSpot);
                Step(15f, () => Shot("02_dock_spot", dockEye, spotCenter));
                Step(15.3f, () => Shot("03_boat_close", boatEye, spotCenter));
                Step(15.6f, () => Shot("04_far", farEye, spotCenter));
                Step(15.9f, () => Shot("05_top", spotCenter + Vector3.up * 22f + toSpot * -4f, spotCenter));
                Step(16.6f, () => Shot("06_boat_close_b", boatEye, spotCenter));
                Step(17.3f, () => Shot("07_boat_close_c", boatEye, spotCenter));
                Step(18f, Jump);
                Step(18f + 0.25f, () => Shot("08_jump_rise", JumpEye(), JumpTarget(0.5f)));
                Step(18f + 0.5f, () => Shot("09_jump_top", JumpEye(), JumpTarget(0.5f)));
                Step(18f + 0.85f, () => Shot("10_jump_splash", JumpEye(), JumpTarget(0f)));
                Step(21f, Jump);
                Step(21f + 0.45f, () => Shot("11_jump_from_dock", dockEye, JumpTarget(0.4f)));
                // High view along the lake, away from the spot: there must be no rings out there.
                Step(21.8f, () => Shot("12_lake_overview", dockPoint + Vector3.up * 14f - toSpot * 6f, dockPoint + toSpot * 60f + side * 25f));
                Step(22.2f, Finish);
            }

            void SpawnSpot()
            {
                if (spotObject != null)
                {
                    spotObject.SetActive(true); // The scene's own spot, hidden for the "no spot" shot.
                }
                else
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FishingSpotTools.PrefabPath);
                    spotObject = Object.Instantiate(prefab, spotCenter, Quaternion.identity);
                }
                spot = spotObject.GetComponent<FishingSpot>();
                typeof(FishingSpot).GetMethod("DebugSkipToActive", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(spot, null);
                Debug.Log($"[SpotCapture] Spot spawned, intensity {spot.Intensity:0.00}, radius {spot.Radius}");
            }

            void Step(float at, Action action) => steps.Enqueue((at, action));

            FishingSpotJumps Jumps => spotObject != null ? spotObject.GetComponent<FishingSpotJumps>() : null;

            void Jump() => Jumps?.StartJump();

            Vector3 JumpTarget(float height) => (Jumps != null ? Jumps.LastJumpPoint : spotCenter) + Vector3.up * height;

            // 7 m from the jump, on the dock side, at a seated head height.
            Vector3 JumpEye()
            {
                Vector3 jump = JumpTarget(0f);
                Vector3 back = Flat(dockPoint - jump).normalized;
                return jump + back * 7f + Vector3.up * 1.15f;
            }

            void Update()
            {
                if (!EditorApplication.isPlaying)
                {
                    EditorApplication.update -= Update;
                    return;
                }
                float elapsed = Time.time;
                while (steps.Count > 0 && elapsed >= steps.Peek().at)
                {
                    var step = steps.Dequeue();
                    try { step.action(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }

            void Shot(string name, Vector3 eye, Vector3 target, bool save = true)
            {
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
                const int width = 1600, height = 900;
                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
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
                    if (!save)
                        return;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    string path = Path.Combine(dir, name + ".png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    Debug.Log($"[SpotCapture] Saved {path} (intensity {(spot != null ? spot.Intensity : 0f):0.00})");
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
                SessionState.EraseString(CaptureKey);
                SessionState.EraseBool(LiveKey);
                Object.DestroyImmediate(cam.gameObject);
                Debug.Log($"[SpotCapture] Done in {Time.realtimeSinceStartup - startTime:0} s");
                EditorApplication.update -= Update;
                if (live)
                    EditorApplication.ExitPlaymode(); // Back to the user's Editor as it was.
                else
                    EditorApplication.Exit(0);
            }

            static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
        }
    }
}
