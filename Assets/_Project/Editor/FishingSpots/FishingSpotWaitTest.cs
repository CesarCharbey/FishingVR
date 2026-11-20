using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FishingVR.Fish;
using FishingVR.Fishing;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Play mode measure of how long a fish takes to come, in a fishing spot and outside, with real casts (no
    /// headset), from the live CLI:
    ///   FishingVR.EditorTools.FishingSpotWaitTest.Run(3, 4f)   (trials per zone, time scale; then stops Play mode)
    /// Casts alternately into a spot of its own and into open water away from every spot. From the moment the
    /// bobber settles: wait before a fish is picked (the part the spots change), then its approach, circling and
    /// nibbles, and the total until the bite. The bait is pulled out at the bite (the spot loses no stock).
    /// Results: "[WaitTest]" lines in the Console, and the averages per zone.
    /// </summary>
    public static class FishingSpotWaitTest
    {
        const string Key = "FishingSpotWaitTest.Run";
        const string TrialsKey = "FishingSpotWaitTest.Trials";
        const string ScaleKey = "FishingSpotWaitTest.Scale";

        public static string Run(int trialsPerZone = 3, float timeScale = 4f)
        {
            SessionState.SetBool(Key, true);
            SessionState.SetInt(TrialsKey, Mathf.Max(1, trialsPerZone));
            SessionState.SetFloat(ScaleKey, Mathf.Clamp(timeScale, 1f, 8f));
            EditorApplication.EnterPlaymode();
            return "Wait test started";
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                {
                    SessionState.EraseBool(Key);
                    new Session(SessionState.GetInt(TrialsKey, 3), SessionState.GetFloat(ScaleKey, 4f)).Start();
                }
            };
        }

        class Trial
        {
            public bool inSpot;
            public float influence;
            public float settled, picked = -1f, circling = -1f, nibbling = -1f, bite = -1f;
            public float Wait => picked - settled;
            public float Approach => circling - picked;
            public float Circling => (nibbling > 0f ? nibbling : bite) - circling;
            public float Nibbling => nibbling > 0f ? bite - nibbling : 0f;
            public float Total => bite - settled;
        }

        class Session
        {
            readonly int trialsPerZone;
            readonly float timeScale;
            readonly List<Trial> trials = new();
            FishBiteAI ai;
            FishingRod rod;
            FishingSpot spot;
            Vector3 spotTarget, openTarget;
            Trial trial;
            FishBiteState lastState;
            Func<bool> waitUntil;
            float waitDeadline;
            Action next;
            int failures;

            public Session(int trialsPerZone, float timeScale)
            {
                this.trialsPerZone = trialsPerZone;
                this.timeScale = timeScale;
            }

            public void Start()
            {
                EditorApplication.update += Update;
                After(2f, Setup);
            }

            void After(float seconds, Action action)
            {
                float at = Time.time + seconds;
                Until(() => Time.time >= at, 1e6f, action);
            }

            void Until(Func<bool> condition, float timeout, Action action)
            {
                waitUntil = condition;
                waitDeadline = Time.time + timeout;
                next = action;
            }

            void Update()
            {
                if (!EditorApplication.isPlaying)
                {
                    EditorApplication.update -= Update;
                    Time.timeScale = 1f;
                    return;
                }
                if (ai != null && trial != null)
                {
                    if (ai.State != lastState)
                    {
                        if (ai.State == FishBiteState.Waiting && trial.picked < 0f) trial.picked = Time.time;
                        if (ai.State == FishBiteState.Circling && trial.circling < 0f) trial.circling = Time.time;
                        if (ai.State == FishBiteState.Nibbling && trial.nibbling < 0f) trial.nibbling = Time.time;
                        lastState = ai.State;
                    }
                    if (ai.IsBiteActive && trial.bite < 0f) trial.bite = Time.time;
                }
                if (waitUntil == null)
                    return;
                bool timedOut = Time.time > waitDeadline;
                if (!timedOut && !waitUntil())
                    return;
                var action = next;
                waitUntil = null;
                next = null;
                if (timedOut)
                {
                    Debug.LogError("[WaitTest] FAIL step timed out");
                    failures++;
                    Finish();
                    return;
                }
                try { action(); }
                catch (Exception e) { Debug.LogException(e); failures++; Finish(); }
            }

            void Setup()
            {
                ai = Object.FindAnyObjectByType<FishBiteAI>();
                rod = Object.FindAnyObjectByType<FishingRod>();
                if (ai == null || rod == null || rod.Line == null)
                {
                    Debug.LogError("[WaitTest] FAIL no rod or fish AI in the scene");
                    failures++;
                    Finish();
                    return;
                }
                var so = new SerializedObject(ai);
                Debug.Log($"[WaitTest] waits: outside {so.FindProperty("waitOutsideSpot").vector2Value} s, in a spot {so.FindProperty("waitInSpot").vector2Value} s; spots in use: {FishBiteAI.SpotsInUse}");

                // A full spot of our own within casting range (it loses no stock), and open water away from every spot.
                Vector3 tip = rod.RodTip.position;
                if (!FishingSpotTools.FindDeepWater(tip, 10f, 16f, 2f, out var center))
                {
                    Debug.LogError("[WaitTest] FAIL no deep water within casting range");
                    failures++;
                    Finish();
                    return;
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FishingSpotTools.PrefabPath);
                spot = Object.Instantiate(prefab, new Vector3(center.x, FishingWater.HeightAt(center), center.z), Quaternion.identity).GetComponent<FishingSpot>();
                spot.name = "FishingSpot (wait test)";
                spot.Configure(6f, 60f, 0f, 0f);
                spot.BeginActive(0f);
                spotTarget = spot.Center;

                bool found = false;
                for (float r = 11f; r <= 18f && !found; r += 1.5f)
                for (int a = 0; a < 24 && !found; a++)
                {
                    var dir = Quaternion.Euler(0f, a * 15f, 0f) * Vector3.forward;
                    var p = new Vector3(tip.x, 0f, tip.z) + dir * r;
                    p.y = FishingWater.HeightAt(p);
                    if (FishingWater.DepthAt(p) < 2f || Flat(p - spotTarget) < spot.Radius + 8f)
                        continue;
                    bool clear = true;
                    for (int k = 0; k < 8 && clear; k++)
                        clear &= FishingSpot.InfluenceAt(p + Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * 3f) <= 0.001f;
                    if (clear && FishingSpot.InfluenceAt(p) <= 0.001f)
                    {
                        openTarget = p;
                        found = true;
                    }
                }
                if (!found)
                {
                    Debug.LogError("[WaitTest] FAIL no open water away from the spots within casting range");
                    failures++;
                    Finish();
                    return;
                }
                Debug.Log($"[WaitTest] spot at {spotTarget} (radius {spot.Radius} m), open water at {openTarget}; time x{timeScale}");
                Time.timeScale = timeScale;
                // The spot fades in when it starts: wait until it is at full strength.
                Until(() => spot.Intensity > 0.97f, 60f, NextTrial);
            }

            void NextTrial()
            {
                int done = trials.Count;
                if (done >= trialsPerZone * 2)
                {
                    Report();
                    Finish();
                    return;
                }
                bool inSpot = done % 2 == 0;
                rod.Line.Retract();
                lastState = ai.State;
                // The fish AI resets its wait while the bait is out of the water; then cast.
                After(0.6f, () =>
                {
                    trial = null;
                    CastAt(inSpot ? spotTarget : openTarget);
                    Until(() => rod.Line.State == LineState.Deployed && rod.Line.Bobber.IsSettled, 20f, () =>
                    {
                        trial = new Trial { inSpot = inSpot, settled = Time.time, influence = FishingSpot.InfluenceAt(rod.Line.Hook.position) };
                        lastState = ai.State;
                        Until(() => trial.bite > 0f, 150f, () =>
                        {
                            trials.Add(trial);
                            Debug.Log($"[WaitTest] {(trial.inSpot ? "in the spot " : "open water  ")} #{trials.Count}: influence {trial.influence:0.00}, " +
                                      $"wait {trial.Wait:0.0} s, then approach {trial.Approach:0.0} s, circling {trial.Circling:0.0} s, nibbles {trial.Nibbling:0.0} s, " +
                                      $"bite {trial.Total:0.0} s after the bobber settled");
                            trial = null;
                            rod.Line.Retract(); // bait out at the bite: it flees, the spot keeps its stock
                            Until(() => ai.State == FishBiteState.Idle, 20f, NextTrial);
                        });
                    });
                });
            }

            void Report()
            {
                var sb = new StringBuilder("[WaitTest] averages:");
                foreach (bool inSpot in new[] { true, false })
                {
                    var zone = trials.Where(t => t.inSpot == inSpot).ToList();
                    if (zone.Count == 0)
                        continue;
                    sb.Append($"\n  {(inSpot ? "in the spot" : "open water ")}: influence {zone.Average(t => t.influence):0.00}, wait {zone.Average(t => t.Wait):0.0} s, " +
                              $"approach {zone.Average(t => t.Approach):0.0} s, circling {zone.Average(t => t.Circling):0.0} s, nibbles {zone.Average(t => t.Nibbling):0.0} s, " +
                              $"total {zone.Average(t => t.Total):0.0} s (min {zone.Min(t => t.Total):0.0}, max {zone.Max(t => t.Total):0.0})");
                }
                Debug.Log(sb.ToString());
            }

            void CastAt(Vector3 target)
            {
                // Ballistic throw from the rod tip, 35 degrees up (the bobber has no air drag).
                Vector3 tip = rod.RodTip.position;
                Vector3 flat = new(target.x - tip.x, 0f, target.z - tip.z);
                float d = flat.magnitude, dy = target.y - tip.y, g = Mathf.Abs(Physics.gravity.y);
                float angle = 35f * Mathf.Deg2Rad;
                float v = Mathf.Sqrt(g * d * d / (2f * Mathf.Cos(angle) * Mathf.Cos(angle) * Mathf.Max(0.1f, d * Mathf.Tan(angle) - dy)));
                rod.Line.Cast(flat.normalized * v * Mathf.Cos(angle) + Vector3.up * v * Mathf.Sin(angle));
            }

            void Finish()
            {
                waitUntil = null;
                Time.timeScale = 1f;
                if (spot != null)
                    Object.Destroy(spot.gameObject);
                Debug.Log(failures == 0 ? $"[WaitTest] DONE, {trials.Count} trials" : $"[WaitTest] {failures} FAILURE(S)");
                EditorApplication.update -= Update;
                EditorApplication.ExitPlaymode();
            }

            static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        }
    }
}
