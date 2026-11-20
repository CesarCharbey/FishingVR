using System.Linq;
using FishingVR.Water;
using FishingVR.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Puts WaterContactFoam on the moving objects that sit at the surface and measures their real waterline: the
    /// boat (outline of the hull, measured on the boat of the open scene and saved in its prefab), the buoys of the
    /// open scene (circle) and the bobber (small, its size is enough). Fixed objects are baked instead
    /// (FishingVR > Eau > Calculer l'écume de contact).
    /// </summary>
    public static class WaterContactFoamTools
    {
        const string BoatPrefab = "Assets/_Project/Prefabs/Boat/PF_Boat.prefab";
        const string BobberPrefab = "Assets/_Project/Prefabs/Fishing/Flotteur.prefab";

        [MenuItem("FishingVR/Eau/Ajouter l'écume de contact aux objets mobiles")]
        public static void Setup()
        {
            SetupBoat();
            SetupBobber();
            SetupBuoysInOpenScene();
        }

        static void SetupBoat()
        {
            // The boat as it floats in the open scene: its waterline is measured there, then saved in the prefab.
            var boat = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .FirstOrDefault(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)
                                     && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == BoatPrefab);
            if (boat == null)
            {
                Debug.LogWarning("[ContactFoam] No boat in the open scene: open Main to measure its waterline.");
                return;
            }
            // The hull: the biggest mesh of the boat (not the motor, not the mask that hides the water inside it).
            var hull = boat.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.GetComponent<MeshRenderer>() != null && f.sharedMesh != null)
                .Where(f => f.GetComponent<MeshRenderer>().sharedMaterial == null || !f.GetComponent<MeshRenderer>().sharedMaterial.shader.name.Contains("WaterMask"))
                .OrderByDescending(f => f.GetComponent<MeshRenderer>().bounds.size.x * f.GetComponent<MeshRenderer>().bounds.size.z)
                .FirstOrDefault();

            // (GetComponent returns a fake null in the editor: no ?? on Unity objects.)
            var foam = boat.GetComponent<WaterContactFoam>();
            if (foam == null)
                foam = Undo.AddComponent<WaterContactFoam>(boat.gameObject);
            var so = new SerializedObject(foam);
            so.FindProperty("shape").enumValueIndex = (int)WaterContactFoam.Shape.Outline;
            so.FindProperty("widthScale").floatValue = 0.6f; // a long hull: a full band all along it reads heavy
            // The band starts at the real waterline of the hull, not at the gunwale (the hull flares out above the
            // water: measured from the gunwale, the band looked thick from the side).
            so.FindProperty("overhangHeight").floatValue = 0f;
            so.FindProperty("surfaceTolerance").floatValue = 0.3f;
            so.FindProperty("waterlineMesh").objectReferenceValue = hull;
            so.ApplyModifiedProperties();
            if (!foam.MeasureWaterline())
                return;
            PrefabUtility.ApplyObjectOverride(foam, BoatPrefab, InteractionMode.AutomatedAction);
            Debug.Log($"[ContactFoam] Boat: hull '{(hull != null ? hull.name : "?")}', waterline of {foam.OutlinePointCount} points, size {foam.WaterlineSize:0.00} m.");
        }

        static void SetupBobber()
        {
            var root = PrefabUtility.LoadPrefabContents(BobberPrefab);
            try
            {
                var foam = root.GetComponent<WaterContactFoam>();
                if (foam == null)
                    foam = root.AddComponent<WaterContactFoam>();
                var so = new SerializedObject(foam);
                so.FindProperty("shape").enumValueIndex = (int)WaterContactFoam.Shape.Circle;
                so.FindProperty("widthScale").floatValue = 1f; // small object: the lake already gives it a thin band
                so.FindProperty("surfaceTolerance").floatValue = 0.03f;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, BobberPrefab);
                Debug.Log("[ContactFoam] Bobber: thin band, fades when pulled under or out of the water.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void SetupBuoysInOpenScene()
        {
            int count = 0;
            foreach (var field in Object.FindObjectsByType<FloatingPlantField>(FindObjectsSortMode.None))
            {
                if (field.name.IndexOf("Buoy", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue; // lily pads: no foam around them
                foreach (Transform buoy in field.transform)
                {
                    var foam = buoy.GetComponent<WaterContactFoam>();
                    if (foam == null)
                        foam = Undo.AddComponent<WaterContactFoam>(buoy.gameObject);
                    var so = new SerializedObject(foam);
                    so.FindProperty("shape").enumValueIndex = (int)WaterContactFoam.Shape.Circle;
                    so.FindProperty("widthScale").floatValue = 1f;
                    so.ApplyModifiedProperties();
                    if (foam.MeasureWaterline())
                        count++;
                    Debug.Log($"[ContactFoam] Buoy '{buoy.name}': radius {foam.WaterlineRadius:0.00} m, size {foam.WaterlineSize:0.00} m.");
                }
            }
            if (count > 0)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[ContactFoam] Buoys: {count} measured in the open scene.");
        }
    }
}
