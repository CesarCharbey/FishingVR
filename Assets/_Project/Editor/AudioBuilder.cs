using System.Collections.Generic;
using FishingVR.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Builds the game audio: import settings of the clips (long loops streamed, short sounds in memory),
    /// the PF_GameAudio prefab with its sound library (which clip for which event, volume, pitch, 3D) and puts
    /// it in the open scene. The mixing values live here: tweak them here and re-run (or on the prefab).
    /// </summary>
    public static class AudioBuilder
    {
        const string Clips = "Assets/_Project/Audio/Clips";
        const string Generated = "Assets/_Project/Audio/Generated";
        const string PrefabPath = "Assets/_Project/Prefabs/Audio/PF_GameAudio.prefab";

        /// <summary>Rebuilds the audio, keeping the mix set in the Mixage window (volumes, pitches).</summary>
        [MenuItem("FishingVR/Audio/Créer l'audio du jeu")]
        public static void Build() => Build(true);

        [MenuItem("FishingVR/Audio/Réinitialiser le mixage")]
        public static void ResetMix()
        {
            if (EditorUtility.DisplayDialog("Mixage audio", "Remettre tous les volumes aux valeurs par défaut ?", "Réinitialiser", "Annuler"))
                Build(false);
        }

        public static void Build(bool keepMix)
        {
            var old = keepMix ? AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)?.GetComponent<GameAudio>() : null;
            var oldMix = old != null ? new SerializedObject(old) : null;
            SfxSynth.GenerateAll();
            ImportSettings();

            System.IO.Directory.CreateDirectory("Assets/_Project/Prefabs/Audio");
            var root = new GameObject("PF_GameAudio");
            var audio = root.AddComponent<GameAudio>();
            var so = new SerializedObject(audio);
            so.FindProperty("lakeLoop").objectReferenceValue = Clip(Clips, "AMB_Lake");
            so.FindProperty("shoreWavesLoop").objectReferenceValue = Clip(Clips, "AMB_ShoreWaves");
            so.FindProperty("motorLoop").objectReferenceValue = Clip(Clips, "BOAT_Motor");
            so.FindProperty("reelLoop").objectReferenceValue = Clip(Clips, "SFX_Reel");
            so.FindProperty("hullWaterLoop").objectReferenceValue = Clip(Clips, "AMB_ShoreWaves");

            var list = so.FindProperty("sounds");
            var sounds = Library();
            list.arraySize = sounds.Count;
            for (var i = 0; i < sounds.Count; i++)
            {
                var s = sounds[i];
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("id").enumValueIndex = (int)s.id;
                e.FindPropertyRelative("category").enumValueIndex = (int)s.category;
                var clips = e.FindPropertyRelative("clips");
                clips.arraySize = s.clips.Length;
                for (var c = 0; c < s.clips.Length; c++) clips.GetArrayElementAtIndex(c).objectReferenceValue = s.clips[c];
                e.FindPropertyRelative("volume").floatValue = s.volume;
                e.FindPropertyRelative("volumeJitter").floatValue = s.volumeJitter;
                e.FindPropertyRelative("pitch").vector2Value = s.pitch;
                e.FindPropertyRelative("spatial").floatValue = s.spatial;
                e.FindPropertyRelative("minDistance").floatValue = s.minDistance;
                e.FindPropertyRelative("maxDistance").floatValue = s.maxDistance;
                e.FindPropertyRelative("startOffset").floatValue = s.startOffset;
                e.FindPropertyRelative("maxDuration").floatValue = s.maxDuration;
                e.FindPropertyRelative("cooldown").floatValue = s.cooldown;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            if (oldMix != null) KeepMix(oldMix, so);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AddToScene();
            Debug.Log($"[Audio] Built {PrefabPath} ({sounds.Count} sounds)");
        }

        /// <summary>Copies the mix of the previous prefab: every number / range, and the volume and pitch of each sound.</summary>
        static void KeepMix(SerializedObject from, SerializedObject to)
        {
            var it = from.GetIterator();
            it.NextVisible(true);
            while (it.NextVisible(false))
                if (it.propertyType == SerializedPropertyType.Float || it.propertyType == SerializedPropertyType.Vector2 || it.propertyType == SerializedPropertyType.Integer)
                    to.CopyFromSerializedProperty(it);
            var oldSounds = from.FindProperty("sounds");
            var newSounds = to.FindProperty("sounds");
            for (var i = 0; i < newSounds.arraySize; i++)
            {
                var n = newSounds.GetArrayElementAtIndex(i);
                var id = n.FindPropertyRelative("id").enumValueIndex;
                for (var j = 0; j < oldSounds.arraySize; j++)
                {
                    var o = oldSounds.GetArrayElementAtIndex(j);
                    if (o.FindPropertyRelative("id").enumValueIndex != id) continue;
                    n.FindPropertyRelative("volume").floatValue = o.FindPropertyRelative("volume").floatValue;
                    n.FindPropertyRelative("pitch").vector2Value = o.FindPropertyRelative("pitch").vector2Value;
                }
            }
            to.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Which clips for which event, and how loud: the default mix of the game.</summary>
        static List<Sound> Library()
        {
            AudioClip[] G(params string[] names) { var a = new AudioClip[names.Length]; for (var i = 0; i < names.Length; i++) a[i] = Clip(Generated, names[i]); return a; }
            AudioClip[] R(params string[] names) { var a = new AudioClip[names.Length]; for (var i = 0; i < names.Length; i++) a[i] = Clip(Clips, names[i]); return a; }
            Sound S(SoundId id, AudioClip[] clips, float volume, Vector2 pitch, float spatial = 1f, float min = 1f, float max = 25f,
                    SoundCategory cat = SoundCategory.Sfx, float start = 0f, float duration = 0f, float cooldown = 0f, float jitter = 0.1f) =>
                new Sound { id = id, clips = clips, volume = volume, pitch = pitch, spatial = spatial, minDistance = min, maxDistance = max,
                            category = cat, startOffset = start, maxDuration = duration, cooldown = cooldown, volumeJitter = jitter };

            return new List<Sound>
            {
                // Fishing
                S(SoundId.Cast, R("SFX_Cast_01", "SFX_Cast_02"), 0.7f, new Vector2(0.95f, 1.08f), 1f, 1f, 15f),
                S(SoundId.BobberLand, R("SFX_BobberLand"), 0.8f, new Vector2(0.9f, 1.1f), 1f, 2f, 30f, cooldown: 1.5f),
                // Nibbles: the bobber sound, softer and a bit higher (a small dip, not a landing).
                S(SoundId.Nibble, R("SFX_BobberLand"), 0.45f, new Vector2(1.1f, 1.3f), 1f, 2f, 25f, cooldown: 0.15f),
                // The splash recording is long and loud: its first second only.
                S(SoundId.Bite, R("SFX_Splash"), 0.45f, new Vector2(1.05f, 1.2f), 1f, 2f, 30f, start: 0.15f, duration: 0.7f),
                S(SoundId.HookSet, R("SFX_Splash"), 0.6f, new Vector2(0.9f, 1.05f), 1f, 2f, 30f, start: 0.4f, duration: 1.1f),
                S(SoundId.FishSplash, R("SFX_Splash"), 0.45f, new Vector2(0.95f, 1.25f), 1f, 2f, 30f, start: 0.5f, duration: 0.6f, cooldown: 0.2f),
                S(SoundId.NetScoop, R("SFX_Splash"), 0.5f, new Vector2(0.8f, 0.9f), 1f, 1f, 15f, start: 1.2f, duration: 0.8f),
                S(SoundId.LineRun, R("SFX_LineRun"), 0.6f, new Vector2(0.95f, 1.1f), 1f, 0.5f, 12f, cooldown: 0.5f),
                S(SoundId.LineStrain, R("SFX_LineStrain"), 0.55f, new Vector2(0.95f, 1.05f), 1f, 0.5f, 12f, start: 1.5f, cooldown: 2f),
                S(SoundId.LineSnap, G("SFX_LineSnap"), 0.9f, new Vector2(0.95f, 1.05f), 1f, 1f, 20f),
                S(SoundId.RodCreak, R("SFX_RodCreak_01", "SFX_RodCreak_02", "SFX_RodCreak_03", "SFX_RodCreak_04", "SFX_RodCreak_05", "SFX_RodCreak_06"),
                  0.7f, new Vector2(0.92f, 1.08f), 1f, 0.4f, 10f, jitter: 0.2f),
                S(SoundId.FishSlap, G("SFX_FishSlap_01", "SFX_FishSlap_02", "SFX_FishSlap_03"), 0.7f, new Vector2(0.9f, 1.1f), 1f, 0.5f, 12f, cooldown: 0.1f),
                S(SoundId.Drip, R("SFX_WaterDrop"), 0.3f, new Vector2(0.85f, 1.25f), 1f, 0.3f, 6f, cooldown: 0.12f, jitter: 0.3f),
                // Boat
                S(SoundId.BoatImpact, G("SFX_CoolerClose"), 0.9f, new Vector2(0.55f, 0.7f), 1f, 1.5f, 30f, SoundCategory.Boat, cooldown: 0.2f),
                S(SoundId.PropellerSplash, R("SFX_Splash"), 0.4f, new Vector2(0.8f, 0.95f), 1f, 2f, 25f, SoundCategory.Boat, start: 0.3f, duration: 0.6f, cooldown: 0.5f),
                S(SoundId.RackKnock, G("SFX_RackKnock"), 0.6f, new Vector2(0.9f, 1.1f), 1f, 0.5f, 10f),
                // Cooler / UI
                S(SoundId.CoolerOpen, G("SFX_CoolerOpen"), 0.45f, new Vector2(0.95f, 1.05f), 1f, 0.5f, 10f, cooldown: 0.3f),
                S(SoundId.CoolerClose, G("SFX_CoolerClose"), 0.5f, new Vector2(0.95f, 1.05f), 1f, 0.5f, 10f, cooldown: 0.3f),
                S(SoundId.FishInCooler, G("SFX_FishSlap_01", "SFX_FishSlap_02", "SFX_FishSlap_03"), 0.6f, new Vector2(0.85f, 1f), 1f, 0.5f, 10f),
                S(SoundId.NewSpecies, G("UI_NewSpecies"), 0.7f, new Vector2(1f, 1f), 0.3f, 1f, 10f, SoundCategory.Ui, jitter: 0f),
                S(SoundId.Record, G("UI_Record"), 0.7f, new Vector2(1f, 1f), 0.3f, 1f, 10f, SoundCategory.Ui, jitter: 0f),
                S(SoundId.WristOpen, G("UI_Pop"), 0.45f, new Vector2(0.95f, 1.05f), 0.5f, 0.3f, 5f, SoundCategory.Ui, cooldown: 0.3f),
                // Ambience
                S(SoundId.Bird, R("AMB_Bird_Short", "AMB_Birds"), 0.5f, new Vector2(0.9f, 1.1f), 1f, 8f, 80f, SoundCategory.Ambience, duration: 6f, jitter: 0.4f),
            };
        }

        /// <summary>Put PF_GameAudio in the open scene (once).</summary>
        [MenuItem("FishingVR/Audio/Ajouter l'audio à la scène")]
        public static void AddToScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var r in scene.GetRootGameObjects())
                if (r.GetComponentInChildren<GameAudio>(true) != null) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(go, "Add game audio");
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static void ImportSettings()
        {
            foreach (var dir in new[] { Clips, Generated })
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                var settings = importer.defaultSampleSettings;
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                var isLoop = name.StartsWith("AMB_") || name.StartsWith("BOAT_") || name == "SFX_Reel";
                // Long beds streamed from disk; short sounds decompressed in memory (no latency).
                settings.loadType = isLoop && clip != null && clip.length > 20f ? AudioClipLoadType.Streaming
                                  : clip != null && clip.length > 3f ? AudioClipLoadType.CompressedInMemory
                                  : AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                settings.preloadAudioData = !isLoop;
                importer.defaultSampleSettings = settings;
                // 3D sounds must be mono to be positioned; ambience beds stay stereo.
                importer.forceToMono = !name.StartsWith("AMB_Lake") && !name.StartsWith("AMB_Shore");
                importer.SaveAndReimport();
            }
        }

        static AudioClip Clip(string dir, string name)
        {
            foreach (var ext in new[] { ".wav", ".mp3" })
            {
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"{dir}/{name}{ext}");
                if (c != null) return c;
            }
            Debug.LogWarning($"[Audio] Missing clip {dir}/{name}");
            return null;
        }
    }
}
