using FishingVR.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Collisions for the map (menu FishingVR > Map). Imported Blender props have no colliders, so the
    /// player walked through the dock. Rules for --- MAP ---:
    /// - Environment props (dock, cabin...): mesh colliders; docks also get a teleport area.
    /// - Vegetation/Forest: a trunk capsule per tree (branches stay passable).
    /// - Vegetation/Rocks: mesh colliders.
    /// - Vegetation/LilyPads: not static + FloatingPlantField (pushed aside by the boats).
    /// - Grass, bushes, shore plants, mountains, water: no collision.
    /// Safe to run again: existing colliders are kept.
    /// </summary>
    public static class MapColliderTools
    {
        [MenuItem("FishingVR/Map/Ajouter les collisions de la map")]
        public static void SetupActiveScene()
        {
            var report = Setup();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[Map] " + report);
        }

        public static string Setup()
        {
            var map = GameObject.Find("--- MAP ---");
            if (map == null) return "no --- MAP --- in the scene";
            int props = 0, docks = 0, trunks = 0, rocks = 0;

            var env = map.transform.Find("Environment");
            if (env != null)
            {
                foreach (Transform child in env)
                {
                    var n = child.name;
                    if (n.StartsWith("Env_") || n.StartsWith("Water")) continue;
                    props += AddMeshColliders(child.gameObject);
                    if (n.Contains("Dock") && child.GetComponent<TeleportationArea>() == null)
                    {
                        var area = Undo.AddComponent<TeleportationArea>(child.gameObject);
                        UseTeleportLayer(area);
                        docks++;
                    }
                }
            }

            var veg = map.transform.Find("Vegetation");
            if (veg != null)
            {
                var forest = veg.Find("Forest");
                if (forest != null) foreach (Transform tree in forest) trunks += AddTrunkCollider(tree.gameObject) ? 1 : 0;
                var rockGroup = veg.Find("Rocks");
                if (rockGroup != null) foreach (Transform rock in rockGroup) rocks += AddMeshColliders(rock.gameObject) > 0 ? 1 : 0;
                var lily = veg.Find("LilyPads");
                if (lily != null) SetupFloatingPlants(lily.gameObject);
            }
            return $"colliders: {props} prop meshes, {docks} dock teleport areas, {trunks} tree trunks, {rocks} rocks";
        }

        /// <summary>
        /// Teleport destinations must be on the "Teleport" interaction layer: the teleport ray only targets that
        /// layer, and the hands (Default) would otherwise grab the ground / dock instead of the items.
        /// </summary>
        public static void UseTeleportLayer(BaseTeleportationInteractable interactable)
        {
            var mask = UnityEngine.XR.Interaction.Toolkit.InteractionLayerMask.GetMask("Teleport");
            if (mask != 0) interactable.interactionLayers = mask;
        }

        public static void SetupFloatingPlants(GameObject group)
        {
            foreach (Transform t in group.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            if (group.GetComponent<FloatingPlantField>() == null)
                Undo.AddComponent<FloatingPlantField>(group);
        }

        /// <summary>Mesh collider on every mesh of the object that has none. Returns how many were added.</summary>
        public static int AddMeshColliders(GameObject go)
        {
            var added = 0;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                var col = Undo.AddComponent<MeshCollider>(mf.gameObject);
                col.sharedMesh = mf.sharedMesh;
                added++;
            }
            return added;
        }

        /// <summary>Upright capsule around the base of the trunk. False if the tree already has a collider.</summary>
        public static bool AddTrunkCollider(GameObject tree)
        {
            if (tree.GetComponentInChildren<Collider>(true) != null) return false;
            var renderer = tree.GetComponentInChildren<Renderer>();
            if (renderer == null) return false;
            var b = renderer.bounds;
            var height = Mathf.Min(b.size.y * 0.5f, 4f);
            var radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.06f, 0.12f, 0.45f);

            var child = new GameObject("TrunkCollider");
            Undo.RegisterCreatedObjectUndo(child, "Trunk collider");
            child.transform.SetParent(tree.transform, false);
            child.transform.SetPositionAndRotation(new Vector3(b.center.x, b.min.y + height * 0.5f, b.center.z), Quaternion.identity);
            GameObjectUtility.SetStaticEditorFlags(child, GameObjectUtility.GetStaticEditorFlags(tree));
            var scale = Mathf.Max(0.0001f, Mathf.Abs(child.transform.lossyScale.x));
            var capsule = child.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = radius / scale;
            capsule.height = Mathf.Max(height, radius * 2f) / scale;
            return true;
        }
    }
}
