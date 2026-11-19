using System.Collections.Generic;
using FishingVR.Cooler;
using FishingVR.Fish;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Builds the cooler prefab (simple low-poly model made of boxes, blue body and white lid), with its
    /// FishCooler, the bestiary panel inside the lid and the floating message. Re-running it rebuilds the
    /// prefab (hand edits on it are lost: change the values here).
    /// </summary>
    public static class CoolerBuilder
    {
        const string PrefabPath = "Assets/_Project/Prefabs/Fishing/PF_Cooler.prefab";
        const string MatDir = "Assets/_Project/Art/Materials";

        // Outer size (m): length (x), height (y), depth (z); wall thickness.
        const float L = 0.46f, H = 0.3f, D = 0.3f, T = 0.025f;
        const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [MenuItem("FishingVR/Glacière/Créer la glacière")]
        public static void Build()
        {
            var blue = Mat("M_Cooler_Blue", new Color(0.16f, 0.45f, 0.75f));
            var white = Mat("M_Cooler_White", new Color(0.95f, 0.95f, 0.93f));
            var grey = Mat("M_Cooler_Grey", new Color(0.33f, 0.36f, 0.4f));
            var liner = Mat("M_Cooler_Liner", new Color(0.82f, 0.88f, 0.92f));

            var root = new GameObject("PF_Cooler");
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            // Hollow box: bottom + 4 walls (with colliders: a fish dropped beside it does not go through).
            Box(body, "Bottom", new Vector3(0f, T * 0.5f, 0f), new Vector3(L, T, D), blue, true);
            Box(body, "Liner", new Vector3(0f, T + 0.002f, 0f), new Vector3(L - 2f * T, 0.004f, D - 2f * T), liner, false);
            Box(body, "WallFront", new Vector3(0f, H * 0.5f, -(D - T) * 0.5f), new Vector3(L, H, T), blue, true);
            Box(body, "WallBack", new Vector3(0f, H * 0.5f, (D - T) * 0.5f), new Vector3(L, H, T), blue, true);
            Box(body, "WallLeft", new Vector3(-(L - T) * 0.5f, H * 0.5f, 0f), new Vector3(T, H, D - 2f * T), blue, true);
            Box(body, "WallRight", new Vector3((L - T) * 0.5f, H * 0.5f, 0f), new Vector3(T, H, D - 2f * T), blue, true);
            // White rim and base band.
            // No two faces in the same plane (they flicker): the rim stands 4 mm above the walls, the base band
            // stops below the bottom's top, the side rims are shorter than the front / back ones.
            const float rim = 0.03f, rimUp = 0.004f;
            var rimY = H - rim * 0.5f + rimUp;
            Box(body, "RimFront", new Vector3(0f, rimY, -(D - T) * 0.5f), new Vector3(L + 0.008f, rim, T + 0.008f), white, false);
            Box(body, "RimBack", new Vector3(0f, rimY, (D - T) * 0.5f), new Vector3(L + 0.008f, rim, T + 0.008f), white, false);
            Box(body, "RimLeft", new Vector3(-(L - T) * 0.5f, rimY - 0.001f, 0f), new Vector3(T + 0.006f, rim, D - 2f * T - 0.004f), white, false);
            Box(body, "RimRight", new Vector3((L - T) * 0.5f, rimY - 0.001f, 0f), new Vector3(T + 0.006f, rim, D - 2f * T - 0.004f), white, false);
            Box(body, "Base", new Vector3(0f, 0.012f, 0f), new Vector3(L + 0.008f, 0.024f, D + 0.008f), white, false);
            // Side handles.
            Box(body, "HandleLeft", new Vector3(-L * 0.5f - 0.012f, H - 0.06f, 0f), new Vector3(0.024f, 0.022f, 0.12f), grey, false);
            Box(body, "HandleRight", new Vector3(L * 0.5f + 0.012f, H - 0.06f, 0f), new Vector3(0.024f, 0.022f, 0.12f), grey, false);

            // Lid, hinged on the back edge (opens away from the player).
            var lid = new GameObject("LidHinge").transform;
            lid.SetParent(root.transform, false);
            lid.localPosition = new Vector3(0f, H + rimUp, D * 0.5f);
            Box(lid, "Lid", new Vector3(0f, 0.02f, -D * 0.5f), new Vector3(L + 0.012f, 0.04f, D + 0.012f), white, false);
            Box(lid, "LidTop", new Vector3(0f, 0.043f, -D * 0.5f), new Vector3(L - 0.05f, 0.008f, D - 0.05f), blue, false);
            Box(lid, "LidHandle", new Vector3(0f, 0.058f, -D + 0.04f), new Vector3(0.16f, 0.022f, 0.03f), grey, false);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

            // Glowing frame over the rim, shown while a fish can be put away.
            var glow = new GameObject("Highlight").transform;
            glow.SetParent(root.transform, false);
            var glowMat = GlowMaterial();
            const float g = 0.022f, gy = H + rimUp + 0.012f;
            Box(glow, "GlowFront", new Vector3(0f, gy, -D * 0.5f - 0.004f), new Vector3(L + 0.03f, g, g), glowMat, false);
            Box(glow, "GlowBack", new Vector3(0f, gy, D * 0.5f + 0.004f), new Vector3(L + 0.03f, g, g), glowMat, false);
            Box(glow, "GlowLeft", new Vector3(-L * 0.5f - 0.004f, gy, 0f), new Vector3(g, g, D - 0.004f), glowMat, false);
            Box(glow, "GlowRight", new Vector3(L * 0.5f + 0.004f, gy, 0f), new Vector3(g, g, D - 0.004f), glowMat, false);

            var contents = new GameObject("Contents").transform;
            contents.SetParent(root.transform, false);
            contents.localPosition = new Vector3(0f, T, 0f);

            // Message: world-space canvas (1 unit = 1 mm) with one text.
            var messageGo = new GameObject("Message", typeof(RectTransform));
            messageGo.transform.SetParent(root.transform, false);
            var messageCanvas = messageGo.AddComponent<Canvas>();
            messageCanvas.renderMode = RenderMode.WorldSpace;
            var mrt = (RectTransform)messageGo.transform;
            mrt.sizeDelta = new Vector2(620f, 230f);
            mrt.localScale = Vector3.one * 0.001f;
            // Dark background: readable over the sky, the water, the boat.
            var background = messageGo.AddComponent<UnityEngine.UI.Image>();
            background.color = new Color(0.06f, 0.09f, 0.13f, 0.72f);
            background.raycastTarget = false;
            var textGo = new GameObject("Text", typeof(RectTransform));
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(mrt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var message = textGo.AddComponent<TextMeshProUGUI>();
            message.font = font;
            message.fontSize = 42f; // ~4 cm lines
            message.alignment = TextAlignmentOptions.Center;
            message.color = Color.white;
            message.textWrappingMode = TextWrappingModes.NoWrap;
            message.raycastTarget = false;

            var cooler = root.AddComponent<FishCooler>();
            var cso = new SerializedObject(cooler);
            cso.FindProperty("lid").objectReferenceValue = lid;
            cso.FindProperty("contents").objectReferenceValue = contents;
            cso.FindProperty("message").objectReferenceValue = message;
            cso.FindProperty("messageRoot").objectReferenceValue = messageGo.transform;
            cso.FindProperty("highlight").objectReferenceValue = glow.gameObject;
            cso.ApplyModifiedPropertiesWithoutUndo();

            // Moving colliders: kinematic body. Items layer: no collision with the boat hull (it rides inside).
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            var items = LayerMask.NameToLayer("Items");
            if (items >= 0)
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = items;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Cooler] Built {PrefabPath}");
        }

        /// <summary>Put the cooler in the open scene (under "--- MAP ---/Fishing Gear" when it exists).</summary>
        [MenuItem("FishingVR/Glacière/Ajouter la glacière à la scène")]
        public static void AddToScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Build(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var r in scene.GetRootGameObjects())
                if (r.GetComponentInChildren<FishCooler>(true) != null) { Debug.Log($"[Cooler] Already in {scene.name}."); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            foreach (var r in scene.GetRootGameObjects())
            {
                var gear = r.name == "--- MAP ---" ? r.transform.Find("Fishing Gear") : null;
                if (gear != null) { go.transform.SetParent(gear, false); break; }
            }
            Undo.RegisterCreatedObjectUndo(go, "Add cooler");
            EditorSceneManager.MarkSceneDirty(scene);
        }

        [MenuItem("FishingVR/Glacière/Réinitialiser le bestiaire")]
        public static void ResetBestiary()
        {
            var message = "Effacer toutes les prises enregistrées ?" + System.Environment.NewLine + FishingVR.Bestiary.FishBestiary.SavePath;
            if (!EditorUtility.DisplayDialog("Bestiaire", message, "Effacer", "Annuler")) return;
            FishingVR.Bestiary.FishBestiary.ResetAll();
            Debug.Log("[Bestiary] Reset.");
        }

        /// <summary>The bestiary on the left wrist (shown with the palm turned toward the face).</summary>
        [MenuItem("FishingVR/Bestiaire/Ajouter le bestiaire au poignet")]
        public static void AddWristBestiary()
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var r in scene.GetRootGameObjects())
                if (r.GetComponentInChildren<FishingVR.Bestiary.WristBestiary>(true) != null) { Debug.Log($"[Bestiary] Already in {scene.name}."); return; }
            var root = new GameObject("Wrist Bestiary");
            SceneManager.MoveGameObjectToScene(root, scene);
            foreach (var r in scene.GetRootGameObjects())
            {
                var gear = r.name == "--- MAP ---" ? r.transform.Find("Fishing Gear") : null;
                if (gear != null) { root.transform.SetParent(gear, false); break; }
            }
            root.AddComponent<FishingVR.Bestiary.WristBestiary>();
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            var comp = panel.AddComponent<FishingVR.Bestiary.BestiaryPanel>();
            var so = new SerializedObject(comp);
            so.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var catalog = so.FindProperty("catalog");
            var species = AllSpecies();
            catalog.arraySize = species.Count;
            for (var i = 0; i < species.Count; i++) catalog.GetArrayElementAtIndex(i).objectReferenceValue = species[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            var wso = new SerializedObject(root.GetComponent<FishingVR.Bestiary.WristBestiary>());
            wso.FindProperty("panel").objectReferenceValue = comp;
            wso.ApplyModifiedPropertiesWithoutUndo();
            Undo.RegisterCreatedObjectUndo(root, "Add wrist bestiary");
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static List<FishSpecies> AllSpecies()
        {
            var list = new List<FishSpecies>();
            foreach (var guid in AssetDatabase.FindAssets("t:FishSpecies"))
            {
                var s = AssetDatabase.LoadAssetAtPath<FishSpecies>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null && s.modelPrefab != null) list.Add(s);
            }
            return list;
        }

        static void Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        /// <summary>Unlit, opaque, bright: the frame that lights up around the rim (a transparent one would be
        /// hidden by the boat's water mask inside the hull).</summary>
        static Material GlowMaterial()
        {
            var path = $"{MatDir}/M_Cooler_Highlight.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.2f, 1f));
            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_SrcBlend", 1f);
            mat.SetFloat("_DstBlend", 0f);
            mat.SetFloat("_ZWrite", 1f);
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = -1;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Mat(string name, Color color)
        {
            var path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
