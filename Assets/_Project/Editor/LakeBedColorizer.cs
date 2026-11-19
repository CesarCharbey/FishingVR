using System.Collections.Generic;
using System.IO;
using FishingVR.Water;
using UnityEditor;
using UnityEngine;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Paints the lake bed as wet sand that darkens with depth, without touching the shared palette:
    /// the terrain faces painted with the "lake bed" grey swatches are moved to a second material
    /// (M_Terrain_LakeBed, a vertical gradient texture) and their UVs follow their height under the water.
    /// Works on a copy of the terrain mesh (the collider keeps the original). Re-run after re-exporting the
    /// terrain from Blender.
    /// </summary>
    public static class LakeBedColorizer
    {
        const string TerrainName = "Env_Terrain";
        const string MeshPath = "Assets/_Project/Art/Models/Environment/Terrain_Lake_Colored.asset";
        const string TexPath = "Assets/_Project/Art/Textures/T_LakeBed_Gradient.png";
        const string MatPath = "Assets/_Project/Art/Materials/M_Terrain_LakeBed.mat";

        /// <summary>Palette cells (UV) of the grey lake bed painted in Blender.</summary>
        static readonly Vector2[] BedCells =
        {
            new Vector2(0.34375f, 0.71875f), // grey-brown: shore and most of the bed
            new Vector2(0.40625f, 0.71875f), // darker, deep
            new Vector2(0.65625f, 0.96875f), // darkest, deepest
        };

        // Gradient: top of the texture = just above the water (wet sand), bottom = deep.
        const float TopAboveWater = 0.5f;  // m
        const float BottomDepth = 7f;      // m

        static readonly (float t, Color c)[] Gradient =
        {
            (0.00f, new Color32(192, 154, 108, 255)), // wet sand at the water's edge
            (0.06f, new Color32(166, 131, 92, 255)),  // waterline
            (0.25f, new Color32(150, 121, 88, 255)),  // ~1.3 m
            (0.55f, new Color32(112, 94, 74, 255)),   // ~3.5 m
            (1.00f, new Color32(78, 70, 62, 255)),    // deep
        };

        [MenuItem("FishingVR/Map/Colorer le fond du lac (sable mouillé)")]
        public static void Colorize()
        {
            var go = GameObject.Find(TerrainName);
            if (go == null) { Debug.LogError($"[LakeBed] No '{TerrainName}' in the open scene."); return; }
            var filter = go.GetComponent<MeshFilter>();
            var renderer = go.GetComponent<MeshRenderer>();
            var source = SourceMesh(go, filter);
            var water = Object.FindAnyObjectByType<WaterSurface>();
            var waterY = water != null ? water.transform.position.y : 0f;

            var tex = MakeGradientTexture();
            var bedMat = MakeMaterial(renderer.sharedMaterials[0], tex);

            // Split the triangles: palette faces stay in submesh 0, lake bed faces go to submesh 1 with their
            // own vertices (UV = height in the gradient).
            var verts = new List<Vector3>(source.vertices);
            var normals = new List<Vector3>(source.normals);
            var uvs = new List<Vector2>(source.uv);
            var tris = source.triangles;
            var keep = new List<int>();
            var bed = new List<int>();
            var remap = new Dictionary<int, int>();
            var toWorld = go.transform.localToWorldMatrix;
            var top = waterY + TopAboveWater;
            var bottom = waterY - BottomDepth;

            for (var i = 0; i < tris.Length; i += 3)
            {
                var uvCenter = (source.uv[tris[i]] + source.uv[tris[i + 1]] + source.uv[tris[i + 2]]) / 3f;
                if (!IsBedCell(uvCenter))
                {
                    keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
                    continue;
                }
                for (var k = 0; k < 3; k++)
                {
                    var vi = tris[i + k];
                    if (!remap.TryGetValue(vi, out var ni))
                    {
                        ni = verts.Count;
                        verts.Add(source.vertices[vi]);
                        normals.Add(source.normals[vi]);
                        var y = toWorld.MultiplyPoint3x4(source.vertices[vi]).y;
                        uvs.Add(new Vector2(0.5f, 1f - Mathf.InverseLerp(top, bottom, y)));
                        remap[vi] = ni;
                    }
                    bed.Add(ni);
                }
            }

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            var created = mesh == null;
            if (created) mesh = new Mesh();
            mesh.Clear();
            mesh.name = "Terrain_Lake_Colored";
            mesh.indexFormat = verts.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(keep, 0);
            mesh.SetTriangles(bed, 1);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            if (created) AssetDatabase.CreateAsset(mesh, MeshPath);
            EditorUtility.SetDirty(mesh);

            Undo.RecordObject(filter, "Lake bed colours");
            Undo.RecordObject(renderer, "Lake bed colours");
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { renderer.sharedMaterials[0], bedMat };
            // The collider keeps the imported mesh (same shape).
            var collider = go.GetComponent<MeshCollider>();
            if (collider != null && collider.sharedMesh == mesh) collider.sharedMesh = source;

            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log($"[LakeBed] {bed.Count / 3} lake bed triangles recoloured ({remap.Count} vertices) -> {MeshPath}");
        }

        /// <summary>The imported mesh (re-running starts again from the FBX, not from the coloured copy).</summary>
        static Mesh SourceMesh(GameObject go, MeshFilter filter)
        {
            var collider = go.GetComponent<MeshCollider>();
            if (collider != null && collider.sharedMesh != null && AssetDatabase.GetAssetPath(collider.sharedMesh) != MeshPath)
                return collider.sharedMesh;
            return filter.sharedMesh;
        }

        static bool IsBedCell(Vector2 uv)
        {
            foreach (var c in BedCells)
                if (Mathf.Abs(uv.x - c.x) < 0.02f && Mathf.Abs(uv.y - c.y) < 0.02f)
                    return true;
            return false;
        }

        static Texture2D MakeGradientTexture()
        {
            const int h = 256;
            var tex = new Texture2D(4, h, TextureFormat.RGBA32, false);
            for (var y = 0; y < h; y++)
            {
                var t = 1f - y / (h - 1f); // row 0 = bottom of the texture = deep
                var c = Gradient[Gradient.Length - 1].c;
                for (var k = 0; k < Gradient.Length - 1; k++)
                    if (t <= Gradient[k + 1].t)
                    {
                        c = Color.Lerp(Gradient[k].c, Gradient[k + 1].c, Mathf.InverseLerp(Gradient[k].t, Gradient[k + 1].t, t));
                        break;
                    }
                for (var x = 0; x < 4; x++) tex.SetPixel(x, y, c);
            }
            tex.Apply();
            File.WriteAllBytes(TexPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexPath);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        }

        static Material MakeMaterial(Material palette, Texture2D tex)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                mat = new Material(palette) { name = "M_Terrain_LakeBed" };
                AssetDatabase.CreateAsset(mat, MatPath);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            mat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
