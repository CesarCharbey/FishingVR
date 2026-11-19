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

            var dot = MakeTexture("T_FX_SoftDot", 64, r => Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f));
            var ring = MakeTexture("T_FX_Ring", 128, r =>
            {
                // Thin bright crest, soft trough inside.
                var crest = Mathf.Exp(-Mathf.Pow((r - 0.86f) / 0.05f, 2f));
                var inner = Mathf.Exp(-Mathf.Pow((r - 0.74f) / 0.08f, 2f)) * 0.25f;
                return Mathf.Clamp01(crest + inner) * (r < 1f ? 1f : 0f);
            });
            var bubble = MakeTexture("T_FX_Bubble", 64, r =>
            {
                var rim = Mathf.Exp(-Mathf.Pow((r - 0.8f) / 0.1f, 2f));
                return Mathf.Clamp01(rim + 0.12f) * (r < 0.95f ? 1f : 0f);
            });

            // Droplets / foam / ripples after the water (queue 3000), bubbles before it (seen through it).
            var mDroplet = MakeMaterial("M_FX_Droplet", dot, 3010);
            var mFoam = MakeMaterial("M_FX_Foam", dot, 3005);
            var mRipple = MakeMaterial("M_FX_Ripple", ring, 3002);
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

            var foam = MakeSystem(root.transform, "Foam", mFoam, 300, ParticleSystemRenderMode.Billboard);
            {
                var main = foam.main;
                main.gravityModifier = 0.15f;
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
