using System.IO;
using FishingVR.Water;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation; // TeleportationAnchor (boat seat)

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Bakes the depth of the lake bed (terrain, rocks; not the docks, boats or anything that moves) into a
    /// texture used by the LakeWater shader for the shore foam, the water colour and the bottom caustics, so
    /// that objects under the surface (hull, posts, fish) are not mistaken for a beach.
    /// Re-run it after changing the terrain or moving rocks.
    /// </summary>
    public static class LakeDepthBaker
    {
        const string OutPath = "Assets/_Project/Art/Textures/Water/T_Lake_BedDepth.png";
        // Depth stored on 8 bits: value = (depth - MinDepth) / (MaxDepth - MinDepth). Must match LakeWater.shader.
        const float MinDepth = -1f;
        const float MaxDepth = 5f;
        const float TexelSize = 0.4f;   // m
        const int MaxResolution = 2048;
        const float Margin = 6f;        // m around the water
        const float DeepValue = 30f;    // depth written where nothing is found (open water)

        [MenuItem("FishingVR/Eau/Calculer la profondeur du lac")]
        public static void Bake()
        {
            var water = Object.FindAnyObjectByType<WaterSurface>();
            if (water == null) { Debug.LogError("[LakeDepth] No WaterSurface in the open scene."); return; }
            var renderer = water.GetComponent<Renderer>();
            var material = renderer != null ? renderer.sharedMaterial : null;
            var waterY = water.transform.position.y;
            var bounds = renderer != null ? renderer.bounds : new Bounds(water.transform.position, new Vector3(500f, 0f, 500f));

            // Coarse pass: where is there water at all?
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            const float coarse = 4f;
            for (var x = bounds.min.x; x <= bounds.max.x; x += coarse)
            for (var z = bounds.min.z; z <= bounds.max.z; z += coarse)
                if (Depth(x, z, waterY) > 0f)
                {
                    min = Vector2.Min(min, new Vector2(x, z));
                    max = Vector2.Max(max, new Vector2(x, z));
                }
            if (min.x > max.x) { Debug.LogError("[LakeDepth] No water found over the terrain."); return; }
            min -= Vector2.one * (coarse + Margin);
            max += Vector2.one * (coarse + Margin);
            var size = max - min;
            var w = Mathf.Clamp(Mathf.CeilToInt(size.x / TexelSize), 16, MaxResolution);
            var h = Mathf.Clamp(Mathf.CeilToInt(size.y / TexelSize), 16, MaxResolution);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            var px = new Color[w * h];
            try
            {
                for (var j = 0; j < h; j++)
                {
                    if (j % 32 == 0 && EditorUtility.DisplayCancelableProgressBar("Profondeur du lac", $"{j}/{h}", j / (float)h)) return;
                    for (var i = 0; i < w; i++)
                    {
                        var x = min.x + (i + 0.5f) / w * size.x;
                        var z = min.y + (j + 0.5f) / h * size.y;
                        var v = Mathf.InverseLerp(MinDepth, MaxDepth, Depth(x, z, waterY));
                        px[j * w + i] = new Color(v, v, v, 1f);
                    }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(OutPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(OutPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(OutPath);
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.textureType = TextureImporterType.SingleChannel;
            importer.npotScale = TextureImporterNPOTScale.None; // keep the exact size (metres per texel)
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            importer.SetTextureSettings(settings);
            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.format = TextureImporterFormat.R8;
            importer.SetPlatformTextureSettings(platform);
            importer.maxTextureSize = MaxResolution;
            importer.SaveAndReimport();

            if (material != null)
            {
                material.SetTexture("_BedDepthTex", AssetDatabase.LoadAssetAtPath<Texture2D>(OutPath));
                material.SetVector("_BedDepthArea", new Vector4(min.x, min.y, 1f / size.x, 1f / size.y));
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[LakeDepth] Baked {w}x{h} over {size.x:0}x{size.y:0} m from ({min.x:0},{min.y:0}) -> {OutPath}");
        }

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        // The terrain is a teleport area too: docks are recognised by name (SM_Dock_Platform, SM_Dock_End, posts...).
        static bool IsDock(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name.IndexOf("Dock", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.name.IndexOf("Ponton", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        /// <summary>Water depth at (x, z): positive under water, negative = ground above the water.</summary>
        static float Depth(float x, float z, float waterY)
        {
            var n = Physics.RaycastNonAlloc(new Vector3(x, waterY + 80f, z), Vector3.down, Hits, 200f, 1, QueryTriggerInteraction.Ignore);
            var ground = float.NegativeInfinity;
            for (var k = 0; k < n; k++)
            {
                var c = Hits[k].collider;
                if (c.attachedRigidbody != null) continue;                      // boats, items
                if (IsDock(c.transform)) continue;                              // docks and their posts
                if (c.GetComponentInParent<TeleportationAnchor>() != null) continue;
                ground = Mathf.Max(ground, Hits[k].point.y);
            }
            return float.IsNegativeInfinity(ground) ? DeepValue : Mathf.Clamp(waterY - ground, -5f, DeepValue);
        }
    }
}
