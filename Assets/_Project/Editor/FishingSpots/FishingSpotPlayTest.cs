using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FishingVR.Fish;
using FishingVR.Fishing;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Play mode check of the fishing spots with the real fishing (no headset), from the live CLI:
    ///   FishingVR.EditorTools.FishingSpotPlayTest.Run()   (plays the open scene, then stops Play mode)
    /// 1. The manager: number of spots, depth, spacing, distance to the dock.
    /// 2. A real cast into a spot near the rod: wait before the fish, then an escape (hook set too early)
    ///    and a catch, and their cost on the spot's stock.
    /// 3. Without any spot nor manager: the fish comes after its approach delay, as before the spots.
    /// Results: "[PlayTest] PASS/FAIL ..." lines in the Console, and a summary line.
    /// </summary>
    public static class FishingSpotPlayTest
    {
        const string Key = "FishingSpotPlayTest.Run";

        public static string Run()
        {
            SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
            return "Play test started";
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
                {
                    SessionState.EraseBool(Key);
                    new Session().Start();
                }
            };
        }

        class Session
        {
            int failures;
            readonly StringBuilder summary = new();
            FishBiteAI ai;
            FishingRod rod;
            FishingSpot testSpot;
            FishingSpotManager manager;
            Func<bool> waitUntil;
            float waitDeadline;
            Action next;
            float settledAt = -1f, pickedAt = -1f, waitingEndedAt = -1f, idleAt = -1f;
            FishBiteState lastState;

            void Check(bool ok, string what)
            {
                Debug.Log($"[PlayTest] {(ok ? "PASS" : "FAIL")} {what}");
                if (!ok) failures++;
            }

            void Info(string what) => Debug.Log($"[PlayTest] INFO {what}");

            public void Start()
            {
                EditorApplication.update += Update;
                After(2f, CheckManager);
            }

            // ---- tiny step machine (EditorApplication.update) ----

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
                    return;
                }
                if (ai != null && ai.State != lastState)
                {
                    Info($"{Time.time:0.0}s fish state {lastState} -> {ai.State}");
                    if (ai.State == FishBiteState.Waiting) pickedAt = Time.time;
                    if (ai.State == FishBiteState.Idle) idleAt = Time.time;
                    if (lastState == FishBiteState.Waiting) waitingEndedAt = Time.time;
                    lastState = ai.State;
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
                    Check(false, "step timed out");
                    Finish();
                    return;
                }
                try { action(); }
                catch (Exception e) { Debug.LogException(e); failures++; Finish(); }
            }

            // ---- 1. Manager ----

            void CheckManager()
            {
                manager = FishingSpotManager.Instance;
                Check(manager != null, "a FishingSpotManager runs in the scene");
                if (manager == null) { Finish(); return; }

                var spots = manager.Spots;
                int active = manager.ActiveCount;
                Check(active >= 3 && active <= 6, $"{active} spots at the start (wanted {manager.TargetCount}, 3 to 6)");
                var so = new SerializedObject(manager);
                float minDepth = so.FindProperty("minDepth").floatValue;
                float spacing = so.FindProperty("minSpacing").floatValue;
                float avoidRadius = so.FindProperty("avoidRadius").floatValue;
                var dock = GameObject.Find("SM_Dock_End");
                bool deep = true, apart = true, awayFromDock = true;
                for (int i = 0; i < spots.Count; i++)
                {
                    var s = spots[i];
                    float depth = FishingWater.DepthAt(s.Center);
                    Info($"{s.name}: {s.Center}, radius {s.Radius:0.0} m, depth {depth:0.0} m, {s.CurrentPhase}, intensity {s.Intensity:0.00}, {s.TimeLeft / 60f:0.0} min left");
                    deep &= depth >= minDepth;
                    for (int j = i + 1; j < spots.Count; j++)
                        apart &= Flat(s.Center - spots[j].Center) >= s.Radius + spots[j].Radius + spacing - 0.01f;
                    if (dock != null)
                        awayFromDock &= Flat(s.Center - dock.transform.position) >= avoidRadius + s.Radius - 0.01f;
                }
                Check(deep, $"every spot in water at least {minDepth} m deep");
                Check(apart, $"spots at least {spacing} m apart (edge to edge)");
                Check(awayFromDock, $"spots away from the dock ({avoidRadius} m)");

                SetUpCast();
            }

            // ---- 2. Real cast into a spot ----

            void SetUpCast()
            {
                ai = Object.FindAnyObjectByType<FishBiteAI>();
                rod = Object.FindAnyObjectByType<FishingRod>();
                Check(ai != null && rod != null && rod.Line != null, "a rod and its fish AI in the scene");
                if (ai == null || rod == null || rod.Line == null) { Finish(); return; }
                lastState = ai.State;
                ai.Fight.enabled = true;

                // A spot of our own a few metres in front of the rod tip, in deep water.
                Vector3 tip = rod.RodTip.position;
                if (!FishingSpotTools.FindDeepWater(tip, 9f, 16f, 1.5f, out var center))
                {
                    Check(false, "deep water within casting range of the rod");
                    Finish();
                    return;
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FishingSpotTools.PrefabPath);
                testSpot = Object.Instantiate(prefab, new Vector3(center.x, FishingWater.HeightAt(center), center.z), Quaternion.identity).GetComponent<FishingSpot>();
                testSpot.name = "FishingSpot (play test)";
                testSpot.Configure(4f, 30f, 0.2f, 0.1f);
                testSpot.BeginActive(0f);

                CastAt(testSpot.Center);
                Until(() => rod.Line.State == LineState.Deployed && rod.Line.Bobber.IsSettled, 20f, OnSettled);
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
                Info($"cast {d:0.0} m toward {target}");
            }

            void OnSettled()
            {
                settledAt = Time.time;
                pickedAt = -1f;
                float influence = FishingSpot.InfluenceAt(rod.Line.Hook.position, out var spot);
                Info($"bobber settled at {rod.Line.Bobber.transform.position}, hook influence {influence:0.00} ({(spot != null ? spot.name : "no spot")})");
                Check(spot == testSpot && influence > 0.6f, $"the hook is in the spot (influence {influence:0.00})");
                Until(() => pickedAt > 0f, 30f, OnFirstFishPicked);
            }

            void OnFirstFishPicked()
            {
                // Expected range at the influence where the hook landed (5-10 s in the inner circle, longer on the ring).
                var so = new SerializedObject(ai);
                Vector2 outside = so.FindProperty("waitOutsideSpot").vector2Value, inside = so.FindProperty("waitInSpot").vector2Value;
                float influence = FishingSpot.InfluenceAt(rod.Line.Hook.position);
                float min = FishBiteAI.SpotWait(influence, 0f, outside, inside) - 0.5f, max = FishBiteAI.SpotWait(influence, 1f, outside, inside) + 1f;
                float wait = pickedAt - settledAt;
                Check(wait >= min && wait <= max, $"first fish after {wait:0.0} s in the spot (expected {min + 0.5f:0.0} to {max - 1f:0.0} s at influence {influence:0.00})");
                Check(ai.CurrentSpot == testSpot, "the fish comes from the spot");
                Until(() => ai.State == FishBiteState.Circling, 40f, ScareFish);
            }

            void ScareFish()
            {
                float before = testSpot.Stock;
                ai.OnHookSetGesture(); // hook set while it circles: too early, it flees
                Check(ai.State == FishBiteState.Escaped, "setting the hook while it circles scares it off");
                Check(Mathf.Abs(before - testSpot.Stock - 0.1f) < 0.001f, $"an escape costs the spot 0.10 of stock ({before:0.00} -> {testSpot.Stock:0.00})");
                settledAt = Time.time; // the next wait starts once the cooldown is over and the bobber settles again
                Until(() => ai.State == FishBiteState.Idle && rod.Line.Bobber.IsSettled, 20f, () =>
                {
                    settledAt = Time.time;
                    pickedAt = -1f;
                    Until(() => ai.IsBiteActive, 60f, CatchFish);
                });
            }

            void CatchFish()
            {
                float wait = pickedAt - settledAt;
                Info($"second fish picked {wait:0.0} s after the bobber settled again");
                float before = testSpot.Stock;
                // Landing a fish needs a real fight in VR: report the end of the fight directly, as FishFight does.
                typeof(FishBiteAI).GetMethod("OnFightFinished", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(ai, new object[] { FightResult.Caught });
                Check(Mathf.Abs(before - testSpot.Stock - 0.2f) < 0.001f, $"a catch costs the spot 0.20 of stock ({before:0.00} -> {testSpot.Stock:0.00})");
                Check(ai.CurrentSpot == null, "the fish is no longer linked to the spot");
                if (ai.Body != null) ai.Body.Flee(rod.RodTip.position);

                // ---- 3. Without spots: the original behaviour ----
                if (manager != null) Object.Destroy(manager.gameObject); // its spots are its children
                Object.Destroy(testSpot.gameObject);
                After(0.5f, () =>
                {
                    Check(!FishBiteAI.SpotsInUse, "no spot and no manager left: spots not in use");
                    // Timestamps come from the state tracking: the bobber may settle on the very frame the next
                    // fish is picked, so never wait for "idle and settled" here.
                    float since = Time.time;
                    Until(() => idleAt >= since || ai.State == FishBiteState.Idle, 20f, () =>
                    {
                        float idleFrom = Mathf.Max(idleAt, since);
                        Until(() => pickedAt > idleFrom, 12f, () =>
                        {
                            Check(pickedAt - idleFrom < 2f, $"without spots a fish is picked as soon as the bobber settles ({pickedAt - idleFrom:0.0} s after idle), as before");
                            waitingEndedAt = -1f;
                            Until(() => waitingEndedAt > 0f, 10f, () =>
                            {
                                float delay = waitingEndedAt - pickedAt;
                                Check(delay >= 1.8f && delay <= 6.5f, $"then it comes after its approach delay ({delay:0.0} s, 2 to 6 s)");
                                Finish();
                            });
                        });
                    });
                });
            }

            void Finish()
            {
                waitUntil = null;
                Debug.Log(failures == 0 ? "[PlayTest] ALL PASSED" : $"[PlayTest] {failures} FAILURE(S)");
                EditorApplication.update -= Update;
                EditorApplication.ExitPlaymode();
            }

            static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        }
    }
}
