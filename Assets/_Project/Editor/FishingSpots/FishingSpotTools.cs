using System.Collections.Generic;
using System.Linq;
using FishingVR.Fish;
using FishingVR.Fishing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Editor menu for the fishing spots: build the spot prefab and drop a spot in the open scene.
    /// </summary>
    public static class FishingSpotTools
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Fishing/PF_FishingSpot.prefab";

        [MenuItem("FishingVR/Fishing Spots/Create Or Update Spot Prefab")]
        public static GameObject CreateOrUpdatePrefab()
        {
            var root = new GameObject("PF_FishingSpot");
            try
            {
                root.AddComponent<FishingSpot>();
                root.AddComponent<FishingSpotBubbles>();
                var jumps = root.AddComponent<FishingSpotJumps>();

                // Every species of the lake can jump.
                var species = AssetDatabase.FindAssets("t:FishSpecies")
                    .Select(guid => AssetDatabase.LoadAssetAtPath<FishSpecies>(AssetDatabase.GUIDToAssetPath(guid)))
                    .Where(s => s != null && s.modelPrefab != null)
                    .OrderBy(s => s.name)
                    .ToList();
                var so = new SerializedObject(jumps);
                var list = so.FindProperty("species");
                list.arraySize = species.Count;
                for (int i = 0; i < species.Count; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = species[i];
                so.ApplyModifiedPropertiesWithoutUndo();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[FishingSpotTools] Saved {PrefabPath} with {species.Count} jumping species.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Adds a FishingSpotManager to the open scene (or updates the existing one): area fitted to the lake bed
        /// baked in the lake material (_BedDepthArea), the dock as a place to avoid, and the hand-placed test
        /// spots removed. Undoable; the scene is marked dirty, not saved.
        /// </summary>
        [MenuItem("FishingVR/Fishing Spots/Add Spot Manager To Open Scene")]
        public static string SetupManagerInOpenScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                prefab = CreateOrUpdatePrefab();

            var manager = Object.FindAnyObjectByType<FishingSpotManager>();
            if (manager == null)
            {
                var go = new GameObject("FishingSpotManager");
                Undo.RegisterCreatedObjectUndo(go, "Add Fishing Spot Manager");
                manager = go.AddComponent<FishingSpotManager>();
            }

            var so = new SerializedObject(manager);
            so.FindProperty("spotPrefab").objectReferenceValue = prefab.GetComponent<FishingSpot>();

            // Area: the lake bed bake of the water material (min X, min Z, 1/size X, 1/size Z), if any.
            string area = "default area";
            var water = Object.FindAnyObjectByType<FishingVR.Water.WaterSurface>();
            var material = water != null ? water.GetComponent<Renderer>()?.sharedMaterial : null;
            if (material != null && material.HasProperty("_BedDepthArea"))
            {
                Vector4 bed = material.GetVector("_BedDepthArea");
                if (bed.z > 0f && bed.w > 0f)
                {
                    var size = new Vector2(1f / bed.z, 1f / bed.w);
                    manager.transform.position = new Vector3(bed.x + size.x * 0.5f, water.transform.position.y, bed.y + size.y * 0.5f);
                    so.FindProperty("areaSize").vector2Value = size;
                    area = $"lake bed area {size.x:0} x {size.y:0} m";
                }
            }

            // Keep the spots away from the dock.
            var dock = GameObject.Find("SM_Dock_End");
            var avoid = so.FindProperty("avoid");
            if (dock != null)
            {
                bool listed = false;
                for (int i = 0; i < avoid.arraySize; i++)
                    listed |= avoid.GetArrayElementAtIndex(i).objectReferenceValue == dock.transform;
                if (!listed)
                {
                    avoid.arraySize++;
                    avoid.GetArrayElementAtIndex(avoid.arraySize - 1).objectReferenceValue = dock.transform;
                }
            }
            so.ApplyModifiedProperties();

            // The manager places the spots now: remove the hand-placed ones.
            int removed = 0;
            foreach (var spot in Object.FindObjectsByType<FishingSpot>(FindObjectsInactive.Include))
            {
                if (spot.transform.parent != null && spot.transform.parent.GetComponent<FishingSpotManager>() != null)
                    continue;
                Undo.DestroyObjectImmediate(spot.gameObject);
                removed++;
            }

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeGameObject = manager.gameObject;
            return $"Manager at {manager.transform.position} ({area}), dock avoided: {dock != null}, hand-placed spots removed: {removed}";
        }

        [MenuItem("FishingVR/Fishing Spots/Add Spot At Scene View Centre")]
        static void AddSpotAtSceneView()
        {
            var view = SceneView.lastActiveSceneView;
            Vector3 p = view != null ? view.pivot : Vector3.zero;
            var spot = PlaceSpot(new Vector3(p.x, 0f, p.z));
            Selection.activeGameObject = spot;
        }

        /// <summary>Instantiates the spot prefab at a point of the water (Y snapped to the water level).</summary>
        public static GameObject PlaceSpot(Vector3 position)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                prefab = CreateOrUpdatePrefab();

            var spot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            float water = FishingWater.Exists ? FishingWater.HeightAt(position) : 0f;
            if (float.IsNegativeInfinity(water)) water = 0f;
            spot.transform.position = new Vector3(position.x, water, position.z);
            spot.name = "FishingSpot";
            Undo.RegisterCreatedObjectUndo(spot, "Add Fishing Spot");
            EditorSceneManager.MarkSceneDirty(spot.scene);
            return spot;
        }

        /// <summary>
        /// Finds a point of the lake at least <paramref name="minDepth"/> deep, between the two distances from
        /// <paramref name="from"/>. Returns false if none is found.
        /// </summary>
        public static bool FindDeepWater(Vector3 from, float minDistance, float maxDistance, float minDepth, out Vector3 point)
        {
            var candidates = new List<(float depth, Vector3 p)>();
            for (float d = minDistance; d <= maxDistance; d += 2f)
            for (int a = 0; a < 36; a++)
            {
                float angle = a * 10f * Mathf.Deg2Rad;
                var p = from + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * d;
                float depth = FishingWater.DepthAt(p);
                if (depth >= minDepth)
                    candidates.Add((depth, p));
            }
            if (candidates.Count == 0)
            {
                point = from;
                return false;
            }
            // Closest qualifying point (first in the scan order), so it stays in view from the start point.
            point = candidates[0].p;
            return true;
        }
    }
}
