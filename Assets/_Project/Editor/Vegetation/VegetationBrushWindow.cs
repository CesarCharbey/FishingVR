using System.Collections.Generic;
using FishingVR.Fishing;
using FishingVR.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Vegetation brush for mesh terrains (Window: FishingVR > Pinceau de végétation).
    /// Left click = paint, Shift + left click = erase (only what the current brush paints),
    /// Ctrl + scroll = brush radius. Respects height above water, slope and spacing.
    /// GameObjects brushes create static prefab instances under --- MAP ---/Vegetation/&lt;group&gt;;
    /// Instanced brushes add GPU-instanced data (VegetationInstanceData + InstancedVegetation).
    /// </summary>
    public class VegetationBrushWindow : EditorWindow
    {
        const string RootName = "--- MAP ---";
        const string VegetationName = "Vegetation";
        const string InstancedName = "Instanced";
        const string DataFolder = "Assets/_Project/Data/Vegetation";

        VegetationBrushPreset[] presets = new VegetationBrushPreset[0];
        VegetationBrushPreset preset;
        Editor presetEditor;
        bool active = true;
        float radius = 8f;
        float strength = 1f;
        Vector2 scroll;

        // Stroke state
        bool stroking;
        Vector3 lastStamp;
        SpatialHash hash;
        System.Random rng = new System.Random();
        Vector3 cursor;
        Vector3 cursorNormal = Vector3.up;
        bool cursorValid;

        [MenuItem("FishingVR/Pinceau de végétation")]
        static void Open() => GetWindow<VegetationBrushWindow>("Végétation");

        void OnEnable()
        {
            RefreshPresets();
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += RebuildAllInstanced;
            radius = EditorPrefs.GetFloat("FishingVR.VegBrush.Radius", 8f);
            strength = EditorPrefs.GetFloat("FishingVR.VegBrush.Strength", 1f);
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= RebuildAllInstanced;
            EditorPrefs.SetFloat("FishingVR.VegBrush.Radius", radius);
            EditorPrefs.SetFloat("FishingVR.VegBrush.Strength", strength);
            if (presetEditor != null) DestroyImmediate(presetEditor);
        }

        void OnFocus() => RefreshPresets();

        void RefreshPresets()
        {
            var guids = AssetDatabase.FindAssets("t:VegetationBrushPreset");
            var list = new List<VegetationBrushPreset>();
            foreach (var g in guids)
            {
                var p = AssetDatabase.LoadAssetAtPath<VegetationBrushPreset>(AssetDatabase.GUIDToAssetPath(g));
                if (p != null) list.Add(p);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            presets = list.ToArray();
            if (preset == null && presets.Length > 0) Select(presets[0]);
        }

        void Select(VegetationBrushPreset p)
        {
            preset = p;
            if (presetEditor != null) DestroyImmediate(presetEditor);
            presetEditor = p != null ? Editor.CreateEditor(p) : null;
        }

        // ---------- Window UI ----------

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Clic gauche : peindre    Shift + clic : effacer (seulement ce pinceau)\n" +
                "Ctrl + molette : taille du pinceau    Ctrl+Z : annuler un trait", MessageType.Info);

            active = EditorGUILayout.Toggle("Pinceau actif", active);
            radius = EditorGUILayout.Slider("Rayon (m)", radius, 0.5f, 60f);
            strength = EditorGUILayout.Slider("Force", strength, 0.05f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pinceaux", EditorStyles.boldLabel);
            var perRow = Mathf.Max(1, (int)(position.width / 150f));
            for (var i = 0; i < presets.Length; i += perRow)
            {
                EditorGUILayout.BeginHorizontal();
                for (var j = i; j < Mathf.Min(i + perRow, presets.Length); j++)
                {
                    var p = presets[j];
                    var old = GUI.backgroundColor;
                    if (p == preset) GUI.backgroundColor = p.brushColor;
                    var label = p.name.Replace("VB_", "") + (p.mode == VegetationBrushPreset.PlacementMode.Instanced ? " ⚡" : "");
                    if (GUILayout.Button(label, GUILayout.Height(28))) Select(p);
                    GUI.backgroundColor = old;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.LabelField("⚡ = instancié (GPU, pour l'herbe)", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            var holder = FindInstancedHolder(false);
            var instancedCount = holder != null && holder.Data != null ? holder.Data.TotalCount : 0;
            EditorGUILayout.LabelField($"Instances GPU dans la scène : {instancedCount}");

            if (presetEditor != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Réglages du pinceau sélectionné", EditorStyles.boldLabel);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                presetEditor.OnInspectorGUI();
                EditorGUILayout.EndScrollView();
            }
        }

        // ---------- Scene interaction ----------

        void OnSceneGUI(SceneView view)
        {
            if (!active || preset == null) return;
            var e = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            UpdateCursor(e);
            var erasing = e.shift;
            if (cursorValid)
            {
                Handles.color = erasing ? new Color(1f, 0.3f, 0.25f) : preset.brushColor;
                Handles.DrawWireDisc(cursor, cursorNormal, radius, 2f);
                Handles.color = new Color(Handles.color.r, Handles.color.g, Handles.color.b, 0.08f);
                Handles.DrawSolidDisc(cursor, cursorNormal, radius);
                Handles.Label(cursor + Vector3.up * 0.5f, $"{preset.name.Replace("VB_", "")}  r={radius:0.#}m" + (erasing ? "  (effacer)" : ""));
            }

            switch (e.type)
            {
                case EventType.ScrollWheel when e.control:
                    radius = Mathf.Clamp(radius * (e.delta.y > 0 ? 0.9f : 1.1f), 0.5f, 60f);
                    Repaint();
                    e.Use();
                    break;
                case EventType.MouseDown when e.button == 0 && !e.alt && cursorValid:
                    BeginStroke();
                    Stamp(cursor, erasing);
                    e.Use();
                    break;
                case EventType.MouseDrag when e.button == 0 && stroking:
                    if (cursorValid && Vector3.Distance(cursor, lastStamp) > radius * 0.3f)
                        Stamp(cursor, erasing);
                    e.Use();
                    break;
                case EventType.MouseUp when e.button == 0 && stroking:
                    EndStroke();
                    e.Use();
                    break;
                case EventType.MouseMove:
                    view.Repaint();
                    break;
            }
        }

        void UpdateCursor(Event e)
        {
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            cursorValid = false;
            if (preset.placeOnWaterSurface)
            {
                var plane = new Plane(Vector3.up, new Vector3(0f, WaterHeight, 0f));
                if (plane.Raycast(ray, out var d)) { cursor = ray.GetPoint(d); cursorNormal = Vector3.up; cursorValid = true; }
                return;
            }
            if (Physics.Raycast(ray, out var hit, 5000f, ~0, QueryTriggerInteraction.Ignore))
            {
                cursor = hit.point; cursorNormal = hit.normal; cursorValid = true;
            }
        }

        static float WaterHeight
        {
            get
            {
                // Edit mode: WaterSurface.Current is only set in play mode, so look the surface up.
                var w = Object.FindFirstObjectByType<FishingVR.Water.WaterSurface>();
                return w != null ? w.GetHeight(Vector3.zero) : 0f;
            }
        }

        // ---------- Stroke ----------

        void BeginStroke()
        {
            stroking = true;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Pinceau de végétation");
            Physics.SyncTransforms();
            hash = new SpatialHash(Mathf.Max(0.5f, preset.minSpacing));
            if (preset.mode == VegetationBrushPreset.PlacementMode.Instanced)
            {
                var data = FindInstancedHolder(true).Data;
                Undo.RecordObject(data, "Pinceau de végétation");
                foreach (var layer in data.layers)
                    if (preset.Contains(layer.prefab))
                        for (var i = 0; i < layer.Count; i++) hash.Add(layer.Position(i));
            }
            else
            {
                foreach (var t in ExistingInstances(GetGroup(false))) hash.Add(t.position);
            }
        }

        void EndStroke()
        {
            stroking = false;
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            if (preset.mode == VegetationBrushPreset.PlacementMode.Instanced)
            {
                var holder = FindInstancedHolder(false);
                if (holder != null && holder.Data != null)
                {
                    EditorUtility.SetDirty(holder.Data);
                    AssetDatabase.SaveAssetIfDirty(holder.Data);
                }
            }
        }

        void Stamp(Vector3 center, bool erase)
        {
            lastStamp = center;
            if (erase) { Erase(center); return; }

            var area = Mathf.PI * radius * radius;
            var target = preset.density * area * strength;
            var existing = hash.CountInRadius(center, radius);
            var missing = target - existing;
            if (missing <= 0f) return;
            var toAdd = Mathf.FloorToInt(missing) + (rng.NextDouble() < missing % 1f ? 1 : 0);
            var attempts = toAdd * 6 + 4;

            var water = WaterHeight;
            var instanced = preset.mode == VegetationBrushPreset.PlacementMode.Instanced;
            var holder = instanced ? FindInstancedHolder(true) : null;
            var group = instanced ? null : GetGroup(true);
            var pending = instanced ? new Dictionary<GameObject, List<float>>() : null;

            while (toAdd > 0 && attempts-- > 0)
            {
                var a = (float)rng.NextDouble() * Mathf.PI * 2f;
                var r = Mathf.Sqrt((float)rng.NextDouble()) * radius;
                var x = center.x + Mathf.Cos(a) * r;
                var z = center.z + Mathf.Sin(a) * r;
                if (!SampleSurface(x, z, center.y, water, out var pos, out var normal)) continue;
                if (preset.minSpacing > 0f && hash.AnyWithin(pos, preset.minSpacing)) continue;

                var entry = preset.PickEntry(rng);
                if (entry == null) break;
                var scale = Mathf.Lerp(entry.scale.x, entry.scale.y, (float)rng.NextDouble());
                var yaw = preset.randomYaw ? (float)rng.NextDouble() * 360f : 0f;
                var rot = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal), entry.alignToNormal) * Quaternion.Euler(0f, yaw, 0f);
                pos -= Vector3.up * entry.sink * scale;

                if (instanced)
                {
                    if (!pending.TryGetValue(entry.prefab, out var list)) pending[entry.prefab] = list = new List<float>();
                    list.Add(pos.x); list.Add(pos.y); list.Add(pos.z);
                    list.Add(rot.x); list.Add(rot.y); list.Add(rot.z); list.Add(rot.w);
                    list.Add(scale);
                }
                else
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(entry.prefab, group);
                    go.transform.SetPositionAndRotation(pos, rot * entry.prefab.transform.rotation);
                    go.transform.localScale = entry.prefab.transform.localScale * scale;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                    Undo.RegisterCreatedObjectUndo(go, "Pinceau de végétation");
                }
                hash.Add(pos);
                toAdd--;
            }

            if (instanced && pending.Count > 0)
            {
                var data = holder.Data;
                foreach (var kv in pending)
                {
                    var layer = data.GetOrCreateLayer(kv.Key);
                    var merged = new float[layer.instances.Length + kv.Value.Count];
                    layer.instances.CopyTo(merged, 0);
                    kv.Value.CopyTo(merged, layer.instances.Length);
                    layer.instances = merged;
                }
                holder.Rebuild();
                SceneView.RepaintAll();
            }
        }

        bool SampleSurface(float x, float z, float refY, float water, out Vector3 pos, out Vector3 normal)
        {
            pos = default; normal = Vector3.up;
            var top = Mathf.Max(refY, water) + 300f;
            if (!Physics.Raycast(new Vector3(x, top, z), Vector3.down, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
                return false;

            if (preset.placeOnWaterSurface)
            {
                if (hit.point.y > water - preset.minWaterDepth) return false; // too shallow, or something above the water
                pos = new Vector3(x, water, z);
                return true;
            }

            var height = hit.point.y - water;
            if (height < preset.heightAboveWater.x || height > preset.heightAboveWater.y) return false;
            if (Vector3.Angle(hit.normal, Vector3.up) > preset.maxSlope) return false;
            pos = hit.point; normal = hit.normal;
            return true;
        }

        void Erase(Vector3 center)
        {
            var r2 = radius * radius;
            if (preset.mode == VegetationBrushPreset.PlacementMode.Instanced)
            {
                var holder = FindInstancedHolder(false);
                if (holder == null || holder.Data == null) return;
                var changed = false;
                foreach (var layer in holder.Data.layers)
                {
                    if (!preset.Contains(layer.prefab)) continue;
                    var kept = new List<float>(layer.instances.Length);
                    for (var i = 0; i < layer.Count; i++)
                    {
                        var p = layer.Position(i);
                        if (HorizontalSqr(p, center) <= r2) { changed = true; continue; }
                        for (var k = 0; k < VegetationInstanceData.Stride; k++) kept.Add(layer.instances[i * VegetationInstanceData.Stride + k]);
                    }
                    layer.instances = kept.ToArray();
                }
                if (changed) { RebuildHashFromData(holder.Data); holder.Rebuild(); SceneView.RepaintAll(); }
            }
            else
            {
                foreach (var t in ExistingInstances(GetGroup(false)))
                    if (HorizontalSqr(t.position, center) <= r2)
                        Undo.DestroyObjectImmediate(t.gameObject);
                hash = new SpatialHash(Mathf.Max(0.5f, preset.minSpacing));
                foreach (var t in ExistingInstances(GetGroup(false))) hash.Add(t.position);
            }
        }

        void RebuildHashFromData(VegetationInstanceData data)
        {
            hash = new SpatialHash(Mathf.Max(0.5f, preset.minSpacing));
            foreach (var layer in data.layers)
                if (preset.Contains(layer.prefab))
                    for (var i = 0; i < layer.Count; i++) hash.Add(layer.Position(i));
        }

        static float HorizontalSqr(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x; var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>Children of the group painted by the current brush.</summary>
        List<Transform> ExistingInstances(Transform group)
        {
            var list = new List<Transform>();
            if (group == null) return list;
            foreach (Transform child in group)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (source != null && preset.Contains(source)) list.Add(child);
            }
            return list;
        }

        // ---------- Scene objects ----------

        static Transform GetOrCreate(Transform parent, string name, bool create)
        {
            var t = parent != null ? parent.Find(name) : GameObject.Find(name)?.transform;
            if (t != null || !create) return t;
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else SceneManagerMove(go);
            Undo.RegisterCreatedObjectUndo(go, "Pinceau de végétation");
            return go.transform;
        }

        static void SceneManagerMove(GameObject go) =>
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, EditorSceneManager.GetActiveScene());

        Transform GetGroup(bool create)
        {
            var root = GetOrCreate(null, RootName, create);
            var veg = GetOrCreate(root, VegetationName, create);
            return GetOrCreate(veg, string.IsNullOrEmpty(preset.groupName) ? "Divers" : preset.groupName, create);
        }

        static InstancedVegetation FindInstancedHolder(bool create)
        {
            var existing = Object.FindFirstObjectByType<InstancedVegetation>();
            if (existing != null || !create)
            {
                if (existing != null && existing.Data == null && create) existing.Data = CreateData();
                return existing;
            }
            var root = GetOrCreate(null, RootName, true);
            var veg = GetOrCreate(root, VegetationName, true);
            var holderT = GetOrCreate(veg, InstancedName, true);
            var holder = Undo.AddComponent<InstancedVegetation>(holderT.gameObject);
            holder.Data = CreateData();
            EditorSceneManager.MarkSceneDirty(holder.gameObject.scene);
            return holder;
        }

        static VegetationInstanceData CreateData()
        {
            System.IO.Directory.CreateDirectory(DataFolder);
            var scene = EditorSceneManager.GetActiveScene().name;
            var path = $"{DataFolder}/VD_{scene}.asset";
            var data = AssetDatabase.LoadAssetAtPath<VegetationInstanceData>(path);
            if (data == null)
            {
                data = CreateInstance<VegetationInstanceData>();
                AssetDatabase.CreateAsset(data, path);
                AssetDatabase.SaveAssets();
            }
            return data;
        }

        static void RebuildAllInstanced()
        {
            foreach (var v in Object.FindObjectsByType<InstancedVegetation>(FindObjectsSortMode.None)) v.Rebuild();
            SceneView.RepaintAll();
        }

        // ---------- Spatial hash ----------

        class SpatialHash
        {
            readonly float cell;
            readonly Dictionary<Vector2Int, List<Vector3>> cells = new Dictionary<Vector2Int, List<Vector3>>();

            public SpatialHash(float cellSize) => cell = cellSize;

            Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.z / cell));

            public void Add(Vector3 p)
            {
                var k = Key(p);
                if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<Vector3>();
                list.Add(p);
            }

            public bool AnyWithin(Vector3 p, float dist)
            {
                var d2 = dist * dist;
                var k = Key(p);
                var span = Mathf.CeilToInt(dist / cell);
                for (var x = -span; x <= span; x++)
                for (var y = -span; y <= span; y++)
                    if (cells.TryGetValue(new Vector2Int(k.x + x, k.y + y), out var list))
                        foreach (var q in list)
                            if (HorizontalSqr(p, q) < d2) return true;
                return false;
            }

            public int CountInRadius(Vector3 p, float r)
            {
                var r2 = r * r;
                var k = Key(p);
                var span = Mathf.CeilToInt(r / cell);
                var n = 0;
                for (var x = -span; x <= span; x++)
                for (var y = -span; y <= span; y++)
                    if (cells.TryGetValue(new Vector2Int(k.x + x, k.y + y), out var list))
                        foreach (var q in list)
                            if (HorizontalSqr(p, q) <= r2) n++;
                return n;
            }
        }
    }
}
