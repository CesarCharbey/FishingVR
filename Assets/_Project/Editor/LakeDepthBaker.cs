using System.Collections.Generic;
using System.IO;
using FishingVR.Water;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation; // TeleportationAnchor (boat seat)
using Object = UnityEngine.Object;

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

            if (material != null)
                BakeContacts(material, min, size, waterY);
        }

        // ------------------------------------------------------------------------------------------------
        // Contact foam: distance to the waterline of everything fixed that pokes out of the water
        // ------------------------------------------------------------------------------------------------

        const string ContactOutPath = "Assets/_Project/Art/Textures/Water/T_Lake_Contact.png";
        // Distance stored on 8 bits: value = distance / ContactRange. Must match CONTACT_RANGE in LakeWater.shader.
        const float ContactRange = 2f;          // m
        const float ContactTexelSize = 0.2f;    // m (finer than the bed: the foam band is ~25 cm wide)
        const int ContactMaxResolution = 4096;
        const string TerrainName = "Env_Terrain"; // the bank gets the shore lace, not a contact band

        /// <summary>
        /// Re-bakes only the contact foam (rocks, dock posts... moved), over the area of the last lake depth bake.
        /// </summary>
        [MenuItem("FishingVR/Eau/Calculer l'écume de contact (rochers, poteaux)")]
        public static void BakeContactsOnly()
        {
            var water = Object.FindAnyObjectByType<WaterSurface>();
            var material = water != null && water.GetComponent<Renderer>() != null ? water.GetComponent<Renderer>().sharedMaterial : null;
            if (material == null || !material.HasProperty("_BedDepthArea")) { Debug.LogError("[LakeContact] No lake water material in the open scene."); return; }
            var area = material.GetVector("_BedDepthArea");
            if (area.z <= 0f || area.w <= 0f) { Debug.LogError("[LakeContact] Bake the lake depth first (FishingVR > Eau > Calculer la profondeur du lac)."); return; }
            BakeContacts(material, new Vector2(area.x, area.y), new Vector2(1f / area.z, 1f / area.w), water.transform.position.y);
        }

        /// <summary>
        /// For every texel of the lake area: distance (m) to the nearest waterline of a fixed object crossing the
        /// water (rocks, dock posts, logs...), from the exact intersection of their render meshes with the water
        /// plane, divided by the band scale of that object (narrower band around small ones: posts, pebbles, reeds).
        /// The shader draws a solid jagged foam band where it is small, all around each object whatever the point of
        /// view. Leaves out the terrain (shore lace instead), anything that moves (boats, buoys, lily pads, items:
        /// WaterContactFoam) and the water itself. Re-run after changing Contact Foam Size on the lake material.
        /// </summary>
        static void BakeContacts(Material material, Vector2 min, Vector2 size, float waterY)
        {
            var w = Mathf.Clamp(Mathf.CeilToInt(size.x / ContactTexelSize), 16, ContactMaxResolution);
            var h = Mathf.Clamp(Mathf.CeilToInt(size.y / ContactTexelSize), 16, ContactMaxResolution);
            var dist2 = new float[w * h];
            for (var k = 0; k < dist2.Length; k++)
                dist2[k] = ContactRange * ContactRange;

            var segments = new List<Vector4>(); // (ax, az, bx, bz)
            var names = new List<string>();
            var objects = 0;
            // Band width from the size of the object: same settings as the shader uses for the moving objects.
            var fullWidthSize = material.HasProperty("_ContactSizeReference") ? material.GetFloat("_ContactSizeReference") : 0.6f;
            var smallest = material.HasProperty("_ContactSmallScale") ? material.GetFloat("_ContactSmallScale") : 0.35f;
            foreach (var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                var mesh = filter.sharedMesh;
                if (renderer == null || !renderer.enabled || mesh == null || !IsFixedContact(filter.transform))
                    continue;
                var bounds = renderer.bounds;
                if (bounds.min.y >= waterY || bounds.max.y <= waterY)
                    continue;

                segments.Clear();
                WaterContactFoam.Waterline(mesh, filter.transform.localToWorldMatrix, waterY, segments);
                if (segments.Count == 0)
                    continue;
                objects++;
                // Narrower band around small objects (posts, pebbles, reeds), like for the moving ones: stored as a
                // larger distance, so the shader's band width stays the same for all.
                var objectSize = WaterContactFoam.EffectiveRadius(segments);
                var scale = WaterContactFoam.BandScale(objectSize, fullWidthSize, smallest);
                var invScale2 = 1f / (scale * scale);
                names.Add($"{filter.name} ({objectSize:0.00} m, x{scale:0.00})");

                // Texels within reach of this object's waterline.
                var lo = new Vector2(float.MaxValue, float.MaxValue);
                var hi = new Vector2(float.MinValue, float.MinValue);
                foreach (var s in segments)
                {
                    lo = Vector2.Min(lo, Vector2.Min(new Vector2(s.x, s.y), new Vector2(s.z, s.w)));
                    hi = Vector2.Max(hi, Vector2.Max(new Vector2(s.x, s.y), new Vector2(s.z, s.w)));
                }
                var i0 = Mathf.Max(0, Mathf.FloorToInt((lo.x - ContactRange - min.x) / size.x * w));
                var i1 = Mathf.Min(w - 1, Mathf.CeilToInt((hi.x + ContactRange - min.x) / size.x * w));
                var j0 = Mathf.Max(0, Mathf.FloorToInt((lo.y - ContactRange - min.y) / size.y * h));
                var j1 = Mathf.Min(h - 1, Mathf.CeilToInt((hi.y + ContactRange - min.y) / size.y * h));
                for (var j = j0; j <= j1; j++)
                for (var i = i0; i <= i1; i++)
                {
                    var px = min.x + (i + 0.5f) / w * size.x;
                    var pz = min.y + (j + 0.5f) / h * size.y;
                    var best = dist2[j * w + i];
                    foreach (var s in segments)
                        best = Mathf.Min(best, SegmentDistance2(px, pz, s) * invScale2);
                    dist2[j * w + i] = best;
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            var px8 = new Color32[w * h];
            for (var k = 0; k < px8.Length; k++)
            {
                var v = (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.Sqrt(dist2[k]) / ContactRange) * 255f);
                px8[k] = new Color32(v, v, v, 255);
            }
            tex.SetPixels32(px8);
            tex.Apply();
            File.WriteAllBytes(ContactOutPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            ImportSingleChannel(ContactOutPath, ContactMaxResolution);

            material.SetTexture("_ContactTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ContactOutPath));
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LakeContact] Baked {w}x{h}, {objects} objects crossing the water -> {ContactOutPath}\n{string.Join(", ", names)}");
        }

        // Fixed objects only: not the terrain, the water, nor anything that moves (rigidbodies, floating plants and
        // buoys pushed by the boat, fishing spots).
        static bool IsFixedContact(Transform t)
        {
            if (t.GetComponent<WaterSurface>() != null || t.GetComponentInParent<Rigidbody>() != null)
                return false;
            if (t.GetComponentInParent<FishingVR.World.FloatingPlantField>() != null)
                return false;
            for (var p = t; p != null; p = p.parent)
                if (p.name == TerrainName)
                    return false;
            return true;
        }

        static float SegmentDistance2(float px, float pz, Vector4 s)
        {
            var dx = s.z - s.x;
            var dz = s.w - s.y;
            var l2 = dx * dx + dz * dz;
            var t = l2 > 1e-8f ? Mathf.Clamp01(((px - s.x) * dx + (pz - s.y) * dz) / l2) : 0f;
            var ex = s.x + dx * t - px;
            var ez = s.y + dz * t - pz;
            return ex * ex + ez * ez;
        }

        static void ImportSingleChannel(string path, int maxSize)
        {
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.textureType = TextureImporterType.SingleChannel;
            importer.npotScale = TextureImporterNPOTScale.None;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            importer.SetTextureSettings(settings);
            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.format = TextureImporterFormat.R8;
            platform.maxTextureSize = maxSize;
            importer.SetPlatformTextureSettings(platform);
            importer.maxTextureSize = maxSize;
            importer.SaveAndReimport();
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
