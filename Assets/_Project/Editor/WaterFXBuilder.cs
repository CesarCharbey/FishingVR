using System.IO;
using FishingVR.FX;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Builds the water particles: procedural textures (soft dot, ring, bubble), URP particle materials and
    /// the PF_WaterFX prefab (droplets, foam, ripples, bubbles + WaterFX). Re-running it rebuilds them
    /// (tweaks made by hand on the prefab are lost: change the values here instead, or stop re-running it).
    /// </summary>
    public static class WaterFXBuilder
    {
        const string TexDir = "Assets/_Project/Art/Textures/FX";
        const string MatDir = "Assets/_Project/Art/Materials/FX";
        const string PrefabDir = "Assets/_Project/Prefabs/FX";
        public const string PrefabPath = PrefabDir + "/PF_WaterFX.prefab";

        [MenuItem("FishingVR/FX/Créer les effets d'eau")]
        public static void Build()
        {
            Directory.CreateDirectory(TexDir);
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(PrefabDir);

            // Cartoon shapes: alpha = distance field (1 = core of the shape, 0 = outside), eroded over the
            // particle's life by the ToonWaterParticle shader (crisp white shapes that thin out and break up).
            var drop = MakeTextureXY("T_FX_Drop", 64, (x, y) => Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y) / 0.9f));
            var ring = MakeTextureXY("T_FX_FoamRing", 256, FoamRing);
            var blobs = MakeTextureXY("T_FX_FoamBlobs", 256, FoamBlobAtlas);
            var bubble = MakeTexture("T_FX_Bubble", 64, r =>
            {
                var rim = Mathf.Exp(-Mathf.Pow((r - 0.8f) / 0.1f, 2f));
                return Mathf.Clamp01(rim + 0.12f) * (r < 0.95f ? 1f : 0f);
            });

            // Droplets / foam / ripples after the water (queue 3000), bubbles before it (seen through it).
            var mDroplet = MakeToonMaterial("M_FX_Droplet", drop, 3010);
            var mFoam = MakeToonMaterial("M_FX_Foam", blobs, 3005);
            var mRipple = MakeToonMaterial("M_FX_Ripple", ring, 3002);
            var mBubble = MakeMaterial("M_FX_Bubble", bubble, 2990);

            var root = new GameObject("PF_WaterFX");
            var plane = new GameObject("WaterPlane").transform;
            plane.SetParent(root.transform, false);

            var ripples = MakeSystem(root.transform, "Ripples", mRipple, 600, ParticleSystemRenderMode.HorizontalBillboard);
            {
                var sol = ripples.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.15f, 0f, 3f), new Keyframe(0.35f, 0.65f), new Keyframe(1f, 1f, 0.4f, 0f)));
                FadeOut(ripples, 0.08f, 1f);
            }

            var droplets = MakeSystem(root.transform, "Droplets", mDroplet, 1500, ParticleSystemRenderMode.Stretch);
            {
                var main = droplets.main;
                main.gravityModifier = 1f;
                var r = droplets.GetComponent<ParticleSystemRenderer>();
                r.velocityScale = 0.035f;
                r.lengthScale = 1.2f;
                var col = droplets.collision;
                col.enabled = true;
                col.type = ParticleSystemCollisionType.Planes;
                col.SetPlane(0, plane);
                col.lifetimeLoss = 1f; // dies when it falls back in the water
                col.bounce = 0f;
                col.dampen = 1f;
                col.radiusScale = 0.1f;
                col.sendCollisionMessages = false;

                // A tiny ring where each drop falls back (some of them).
                var drops = MakeSystem(droplets.transform, "DropRipples", mRipple, 600, ParticleSystemRenderMode.HorizontalBillboard);
                var dm = drops.main;
                dm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
                dm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
                dm.startColor = new Color(0.92f, 0.97f, 1f, 0.35f);
                var dsol = drops.sizeOverLifetime;
                dsol.enabled = true;
                dsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f));
                FadeOut(drops, 0.05f, 1f);
                var sub = droplets.subEmitters;
                sub.enabled = true;
                sub.AddSubEmitter(drops, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing, 0.35f);
            }

            var foam = MakeSystem(root.transform, "Foam", mFoam, 300, ParticleSystemRenderMode.HorizontalBillboard);
            {
                var main = foam.main;
                main.gravityModifier = 0f;
                // 2x2 atlas of blob shapes, one picked at random.
                var sheet = foam.textureSheetAnimation;
                sheet.enabled = true;
                sheet.numTilesX = 2;
                sheet.numTilesY = 2;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.999f);
                sheet.cycleCount = 1;
                var sol = foam.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.4f));
                FadeOut(foam, 0.05f, 1f);
            }

            var bubbles = MakeSystem(root.transform, "Bubbles", mBubble, 400, ParticleSystemRenderMode.Billboard);
            {
                var noise = bubbles.noise;
                noise.enabled = true;
                noise.strength = 0.15f;
                noise.frequency = 1.5f;
                noise.scrollSpeed = 0.5f;
                var sol = bubbles.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.2f));
                FadeOut(bubbles, 0.1f, 0.85f);
            }

            var fx = root.AddComponent<WaterFX>();
            var so = new SerializedObject(fx);
            so.FindProperty("droplets").objectReferenceValue = droplets;
            so.FindProperty("foam").objectReferenceValue = foam;
            so.FindProperty("ripples").objectReferenceValue = ripples;
            so.FindProperty("bubbles").objectReferenceValue = bubbles;
            so.FindProperty("waterPlane").objectReferenceValue = plane;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[WaterFX] Built " + PrefabPath);
        }

        static ParticleSystem MakeSystem(Transform parent, string name, Material material, int maxParticles, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = maxParticles;
            main.startSpeed = 0f;
            main.startSize = 0.1f;
            main.startLifetime = 1f;
            main.gravityModifier = 0f;
            var emission = ps.emission;
            emission.enabled = false; // emitted from code only
            var shape = ps.shape;
            shape.enabled = false;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.alignment = ParticleSystemRenderSpace.View;
            r.minParticleSize = 0f;
            r.maxParticleSize = 5f;
            return ps;
        }

        /// <summary>Alpha: in fast (to <paramref name="peak"/>), out to 0 at the end.</summary>
        static void FadeOut(ParticleSystem ps, float peak, float startAlpha)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(startAlpha, peak), new GradientAlphaKey(startAlpha * 0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        // ---------- Cartoon shapes (distance fields) ----------

        /// <summary>Wobbly foam ring of uneven thickness, with weak spots where it breaks first.</summary>
        static float FoamRing(float x, float y)
        {
            var r = Mathf.Sqrt(x * x + y * y);
            var a = Mathf.Atan2(y, x);
            var radius = 0.76f + 0.035f * Mathf.Sin(3f * a + 0.7f) + 0.02f * Mathf.Sin(7f * a + 2.1f);
            var half = 0.1f * (0.6f + 0.25f * Mathf.Sin(2f * a + 1.3f) + 0.15f * Mathf.Sin(5f * a + 0.2f));
            var f = 1f - Mathf.Abs(r - radius) / half;
            f -= 0.75f * Mathf.Pow(Noise(x * 9f + 11f, y * 9f + 3f), 1.5f); // erodes unevenly: breaks into pieces
            return Mathf.Clamp01(f * 1.4f) * (r < 0.98f ? 1f : 0f);
        }

        /// <summary>2x2 atlas of splash / foam blobs (a few merged discs each, lumpy outline).</summary>
        static float FoamBlobAtlas(float x, float y)
        {
            // x, y in -1..1 over the whole texture: find the tile and local coordinates in -1..1.
            var u = (x + 1f) * 0.5f * 2f;
            var v = (y + 1f) * 0.5f * 2f;
            var tile = Mathf.Min(1, (int)u) + 2 * Mathf.Min(1, (int)v);
            var lx = (u - Mathf.Floor(Mathf.Min(u, 1.999f))) * 2f - 1f;
            var ly = (v - Mathf.Floor(Mathf.Min(v, 1.999f))) * 2f - 1f;
            var rng = new System.Random(17 + tile * 31);
            var count = 4 + tile % 4;
            var sum = 0f;
            for (var i = 0; i < count; i++)
            {
                var cx = (float)(rng.NextDouble() * 0.9 - 0.45);
                var cy = (float)(rng.NextDouble() * 0.9 - 0.45);
                var rad = (float)(0.1 + rng.NextDouble() * (i < 2 ? 0.25 : 0.12));
                var d = Mathf.Sqrt((lx - cx) * (lx - cx) + (ly - cy) * (ly - cy));
                sum += Mathf.Exp(-(d * d) / (rad * rad));
            }
            var f = Mathf.Clamp01((sum - 0.3f) * 1.2f) - 0.4f * Noise(lx * 7f + tile * 7f, ly * 7f);
            var border = Mathf.Max(Mathf.Abs(lx), Mathf.Abs(ly));
            return Mathf.Clamp01(f) * Mathf.Clamp01((0.98f - border) * 20f);
        }

        /// <summary>Smooth value noise, 0..1.</summary>
        static float Noise(float x, float y) =>
            Mathf.PerlinNoise(x, y) * 0.65f + Mathf.PerlinNoise(x * 2.3f + 5f, y * 2.3f + 9f) * 0.35f;

        static Texture2D MakeTextureXY(string name, int size, System.Func<float, float, float> alphaAt)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var a = Mathf.Clamp01(alphaAt((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            return SaveTexture(name, tex, px);
        }

        static Material MakeToonMaterial(string name, Texture2D tex, int queue)
        {
            var path = $"{MatDir}/{name}.mat";
            var shader = Shader.Find("FishingVR/ToonWaterParticle");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
                mat.shaderKeywords = new string[0];
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.renderQueue = queue;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D MakeTexture(string name, int size, System.Func<float, float> alphaAtRadius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f) / size * 2f - 1f;
                var dy = (y + 0.5f) / size * 2f - 1f;
                var a = Mathf.Clamp01(alphaAtRadius(Mathf.Sqrt(dx * dx + dy * dy)));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            return SaveTexture(name, tex, px);
        }

        static Texture2D SaveTexture(string name, Texture2D tex, Color32[] px)
        {
            tex.SetPixels32(px);
            tex.Apply();
            var path = $"{TexDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material MakeMaterial(string name, Texture2D tex, int queue)
        {
            var path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = shader;

            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            // Transparent, alpha blended, no depth write.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = queue;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
