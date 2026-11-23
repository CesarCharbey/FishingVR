using System.Collections.Generic;
using FishingVR.Audio;
using UnityEditor;
using UnityEngine;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// FishingVR > Audio > Mixage: every volume of the game on one screen.
    /// - Edits the PF_GameAudio prefab (saved, shared by every scene) and, in play mode, the running game too:
    ///   tweak while playing in the headset, the values are kept when play mode stops.
    /// - ▶ plays a sound (in play mode at the player's head, through the game's own volumes).
    /// </summary>
    public class AudioMixerWindow : EditorWindow
    {
        const string PrefabPath = "Assets/_Project/Prefabs/Audio/PF_GameAudio.prefab";

        static readonly Dictionary<SoundId, string> Names = new Dictionary<SoundId, string>
        {
            { SoundId.Cast, "Lancer (sifflement)" }, { SoundId.BobberLand, "Flotteur qui tombe" }, { SoundId.Nibble, "Touche (flotteur qui pique)" },
            { SoundId.Bite, "Morsure" }, { SoundId.HookSet, "Ferrage" }, { SoundId.LineRun, "Ligne qui file" },
            { SoundId.LineStrain, "Ligne en tension (zone rouge)" }, { SoundId.LineSnap, "Ligne qui casse" }, { SoundId.RodCreak, "Craquement de la canne" },
            { SoundId.FishSplash, "Poisson qui éclabousse" }, { SoundId.FishSlap, "Claque de poisson" }, { SoundId.Drip, "Gouttes du poisson" },
            { SoundId.NetScoop, "Épuisette" }, { SoundId.BoatImpact, "Choc du bateau" }, { SoundId.PropellerSplash, "Hélice qui retombe" },
            { SoundId.RackKnock, "Canne / épuisette reposée" }, { SoundId.CoolerOpen, "Glacière : ouverture" }, { SoundId.CoolerClose, "Glacière : fermeture" },
            { SoundId.FishInCooler, "Poisson dans la glacière" }, { SoundId.NewSpecies, "Jingle nouvelle espèce" }, { SoundId.Record, "Jingle record" },
            { SoundId.WristOpen, "Bestiaire (poignet)" }, { SoundId.Bird, "Oiseaux" },
        };

        static readonly (SoundCategory cat, string label)[] Groups =
        {
            (SoundCategory.Sfx, "Effets"), (SoundCategory.Boat, "Bateau"), (SoundCategory.Ui, "Interface"), (SoundCategory.Ambience, "Ambiance"),
        };

        GameAudio prefab;
        Vector2 scroll;
        AudioSource preview;

        [MenuItem("FishingVR/Audio/Mixage")]
        public static void Open() => GetWindow<AudioMixerWindow>("Mixage audio");

        void OnEnable() => prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)?.GetComponent<GameAudio>();

        void OnDisable()
        {
            if (preview != null) DestroyImmediate(preview.gameObject);
        }

        void OnGUI()
        {
            if (prefab == null) OnEnable();
            if (prefab == null)
            {
                EditorGUILayout.HelpBox("PF_GameAudio introuvable : lance FishingVR > Audio > Créer l'audio du jeu.", MessageType.Warning);
                return;
            }
            var live = Application.isPlaying ? GameAudio.Instance : null;
            EditorGUILayout.HelpBox(live != null
                ? "En jeu : les changements s'entendent tout de suite et sont gardés à l'arrêt."
                : "Réglages enregistrés sur PF_GameAudio. Lance le jeu pour régler en écoutant (même au casque).", MessageType.Info);

            var so = new SerializedObject(prefab);
            so.Update();
            scroll = EditorGUILayout.BeginScrollView(scroll);

            Header("Volumes généraux");
            Slider(so, "master", "Général");
            Slider(so, "sfxVolume", "Effets");
            Slider(so, "boatVolume", "Bateau");
            Slider(so, "ambienceVolume", "Ambiance");
            Slider(so, "uiVolume", "Interface");

            Header("Sons continus");
            LoopRow(so, "lakeLevel", "Fond du lac", "lakeLoop");
            LoopRow(so, "shoreWavesLevel", "Vagues de rive (près du bord)", "shoreWavesLoop");
            LoopRow(so, "engineIdle", "Moteur au ralenti (assis)", "motorLoop");
            LoopRow(so, "engineFull", "Moteur à plein gaz", "motorLoop");
            LoopRow(so, "hullWater", "Eau sur la coque (pleine vitesse)", "hullWaterLoop");
            LoopRow(so, "reel", "Moulinet", "reelLoop");
            var creak = so.FindProperty("rodCreak");
            var v = creak.vector2Value;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Craquements : tension faible / forte", GUILayout.Width(230));
            v.x = EditorGUILayout.Slider(v.x, 0f, 1f);
            v.y = EditorGUILayout.Slider(v.y, 0f, 1f);
            EditorGUILayout.EndHorizontal();
            creak.vector2Value = v;

            var sounds = so.FindProperty("sounds");
            foreach (var (cat, label) in Groups)
            {
                Header(label);
                for (var i = 0; i < sounds.arraySize; i++)
                {
                    var e = sounds.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("category").enumValueIndex != (int)cat) continue;
                    var id = (SoundId)e.FindPropertyRelative("id").enumValueIndex;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("▶", GUILayout.Width(26))) PlaySound(id, e);
                    EditorGUILayout.LabelField(Names.TryGetValue(id, out var n) ? n : id.ToString(), GUILayout.Width(200));
                    var vol = e.FindPropertyRelative("volume");
                    vol.floatValue = EditorGUILayout.Slider(vol.floatValue, 0f, 2f);
                    var pitch = e.FindPropertyRelative("pitch");
                    var p = pitch.vector2Value;
                    EditorGUILayout.LabelField("hauteur", GUILayout.Width(50));
                    p.x = EditorGUILayout.FloatField(p.x, GUILayout.Width(38));
                    p.y = EditorGUILayout.FloatField(p.y, GUILayout.Width(38));
                    pitch.vector2Value = p;
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();

            if (so.ApplyModifiedPropertiesWithoutUndo())
            {
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssetIfDirty(prefab);
                if (live != null) CopyTo(live);
            }
        }

        /// <summary>Same values on the running game (play mode).</summary>
        void CopyTo(GameAudio live)
        {
            var src = new SerializedObject(prefab);
            var dst = new SerializedObject(live);
            var it = src.GetIterator();
            it.NextVisible(true);
            while (it.NextVisible(false))
            {
                if (it.name == "m_Script") continue;
                dst.CopyFromSerializedProperty(it);
            }
            dst.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Header(string label)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        static void Slider(SerializedObject so, string field, string label)
        {
            var p = so.FindProperty(field);
            p.floatValue = EditorGUILayout.Slider(label, p.floatValue, 0f, 1f);
        }

        void LoopRow(SerializedObject so, string field, string label, string clipField)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("▶", GUILayout.Width(26)))
                PlayClip(so.FindProperty(clipField).objectReferenceValue as AudioClip, so.FindProperty(field).floatValue, 5f);
            EditorGUILayout.LabelField(label, GUILayout.Width(200));
            var p = so.FindProperty(field);
            p.floatValue = EditorGUILayout.Slider(p.floatValue, 0f, 1f);
            EditorGUILayout.EndHorizontal();
        }

        void PlaySound(SoundId id, SerializedProperty e)
        {
            if (Application.isPlaying && GameAudio.Instance != null)
            {
                var cam = Camera.main;
                GameAudio.Play(id, cam != null ? cam.transform.position + cam.transform.forward * 0.5f : Vector3.zero);
                return;
            }
            var clips = e.FindPropertyRelative("clips");
            if (clips.arraySize == 0) return;
            var clip = clips.GetArrayElementAtIndex(Random.Range(0, clips.arraySize)).objectReferenceValue as AudioClip;
            PlayClip(clip, e.FindPropertyRelative("volume").floatValue, 4f, e.FindPropertyRelative("startOffset").floatValue);
        }

        /// <summary>Preview outside play mode (2D, without the category volumes).</summary>
        void PlayClip(AudioClip clip, float volume, float seconds, float start = 0f)
        {
            if (clip == null) return;
            if (preview == null)
            {
                var go = new GameObject("AudioMixerPreview") { hideFlags = HideFlags.HideAndDontSave };
                preview = go.AddComponent<AudioSource>();
                preview.spatialBlend = 0f;
            }
            preview.Stop();
            preview.clip = clip;
            preview.volume = Mathf.Clamp01(volume);
            preview.time = Mathf.Clamp(start, 0f, clip.length - 0.05f);
            preview.Play();
            var stopAt = EditorApplication.timeSinceStartup + seconds;
            void Stop()
            {
                if (EditorApplication.timeSinceStartup < stopAt) return;
                EditorApplication.update -= Stop;
                if (preview != null && preview.clip == clip) preview.Stop();
            }
            EditorApplication.update += Stop;
        }
    }
}
