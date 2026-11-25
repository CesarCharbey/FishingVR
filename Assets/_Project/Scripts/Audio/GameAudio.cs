using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FishingVR.Audio
{
    public enum SoundId
    {
        None,
        // Fishing
        Cast, BobberLand, Nibble, Bite, HookSet, LineRun, LineStrain, LineSnap, RodCreak, FishSplash, FishSlap, Drip, NetScoop,
        // Boat
        BoatImpact, PropellerSplash, RackKnock,
        // Cooler / UI
        CoolerOpen, CoolerClose, FishInCooler, NewSpecies, Record, WristOpen,
        // Ambience one-shots
        Bird,
    }

    public enum SoundCategory { Sfx, Ambience, Boat, Ui }

    /// <summary>One sound: a few variations, picked at random, with a random pitch / volume.</summary>
    [Serializable]
    public class Sound
    {
        public SoundId id;
        public SoundCategory category = SoundCategory.Sfx;
        public AudioClip[] clips;
        [Range(0f, 2f)] public float volume = 1f;
        [Tooltip("Random volume variation (x 1 - this .. 1).")]
        [Range(0f, 1f)] public float volumeJitter = 0.1f;
        public Vector2 pitch = new Vector2(0.95f, 1.05f);
        [Tooltip("0 = 2D, 1 = fully 3D (positioned).")]
        [Range(0f, 1f)] public float spatial = 1f;
        public float minDistance = 1f;
        public float maxDistance = 30f;
        [Tooltip("Start this far into the clip (s), e.g. to skip silence.")]
        public float startOffset;
        [Tooltip("> 0: stop after this many seconds (with a short fade). Cuts long recordings.")]
        public float maxDuration;
        [Tooltip("Minimum time (s) between two plays (avoids machine-gun repeats).")]
        public float cooldown;
    }

    /// <summary>
    /// The game's audio: a library of sounds played by id (static <see cref="Play"/>), a pool of audio
    /// sources, a volume per category, and the ambience (lake bed, shore waves near the shore, distant birds).
    /// It also adds the boat and rod audio components to the scene's boat and rods.
    /// One per scene (PF_GameAudio, built by FishingVR > Audio > Créer l'audio du jeu).
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        [Header("Volumes")]
        [Range(0f, 1f)] public float master = 1f;
        [Range(0f, 1f)] public float sfxVolume = 1f;
        [Range(0f, 1f)] public float ambienceVolume = 0.55f;
        [Range(0f, 1f)] public float boatVolume = 0.5f;
        [Range(0f, 1f)] public float uiVolume = 0.6f;

        [Header("Library")]
        [SerializeField] List<Sound> sounds = new List<Sound>();

        [Header("Loops (used by the ambience, BoatAudio, RodAudio)")]
        public AudioClip lakeLoop;
        public AudioClip shoreWavesLoop;
        public AudioClip motorLoop;
        public AudioClip reelLoop;
        public AudioClip hullWaterLoop;

        [Header("Ambience")]
        [Range(0f, 1f)] [SerializeField] float lakeLevel = 0.8f;
        [Range(0f, 1f)] [SerializeField] float shoreWavesLevel = 0.7f;
        [Tooltip("Shore waves are heard when the water under the player is shallower than this (m).")]
        [SerializeField] float shoreDepth = 2.5f;
        [Tooltip("Seconds between two distant bird calls.")]
        [SerializeField] Vector2 birdInterval = new Vector2(45f, 100f);
        [SerializeField] Vector2 birdDistance = new Vector2(18f, 40f);

        [Header("Continuous sounds (levels before the category volume)")]
        [Tooltip("Boat engine at idle (tiller held, or just let go).")]
        [Range(0f, 1f)] public float engineIdle = 0.12f;
        [Tooltip("Boat engine at full throttle.")]
        [Range(0f, 1f)] public float engineFull = 0.6f;
        [Tooltip("Seconds the engine keeps idling once the tiller is let go, before it stops by itself.")]
        [Min(0f)] public float engineStopDelay = 10f;
        [Tooltip("Water rushing along the hull at full speed.")]
        [Range(0f, 1f)] public float hullWater = 0.45f;
        [Tooltip("Reel ratchet while cranking.")]
        [Range(0f, 1f)] public float reel = 0.6f;
        [Tooltip("Rod creak volume at the lowest / highest tension.")]
        public Vector2 rodCreak = new Vector2(0.18f, 1f);

        [Header("Pool")]
        [SerializeField] int voices = 24;

        public static GameAudio Instance { get; private set; }

        readonly Dictionary<SoundId, float> lastPlayed = new Dictionary<SoundId, float>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        int next;
        LoopPlayer lake, shore;
        float birdTimer;
        Transform listener;

        void Awake()
        {
            Instance = this;
            for (var i = 0; i < voices; i++)
            {
                var go = new GameObject($"Voice{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.dopplerLevel = 0f;
                pool.Add(src);
            }
            lake = new LoopPlayer(gameObject, "Lake", lakeLoop, 0f, 3f, 0.2f, 0.2f); // recording without fades
            shore = new LoopPlayer(gameObject, "ShoreWaves", shoreWavesLoop, 0f, 3f, 2f, 3f);
            birdTimer = UnityEngine.Random.Range(3f, 8f);
        }

        void Start()
        {
            // Audio of the boats and rods of the scene (no prefab edit needed).
            foreach (var motor in FindObjectsByType<FishingVR.Boat.BoatMotor>())
                if (motor.GetComponentInParent<BoatAudio>() == null) motor.gameObject.AddComponent<BoatAudio>();
            foreach (var rod in FindObjectsByType<FishingVR.Fishing.FishingRod>())
                if (rod.GetComponent<RodAudio>() == null) rod.gameObject.AddComponent<RodAudio>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (listener == null)
            {
                var cam = Camera.main;
                listener = cam != null ? cam.transform : null;
            }
            var dt = Time.deltaTime;
            lake.Update(dt, lakeLevel * Category(SoundCategory.Ambience));
            // Shore waves: louder in shallow water (near the shore, on the dock).
            var shoreAmount = 0f;
            if (listener != null && FishingVR.Fishing.FishingWater.Exists)
                shoreAmount = 1f - Mathf.Clamp01(FishingVR.Fishing.FishingWater.DepthAt(listener.position) / shoreDepth);
            shore.Update(dt, shoreWavesLevel * shoreAmount * Category(SoundCategory.Ambience));

            birdTimer -= dt;
            if (birdTimer <= 0f && listener != null)
            {
                birdTimer = UnityEngine.Random.Range(birdInterval.x, birdInterval.y);
                var dir = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
                Play(SoundId.Bird, listener.position + dir * UnityEngine.Random.Range(birdDistance.x, birdDistance.y) + Vector3.up * 6f);
            }
        }

        /// <summary>The sound of this id (looked up each time: the mixing window edits the list live).</summary>
        public Sound Find(SoundId id)
        {
            foreach (var s in sounds) if (s != null && s.id == id) return s;
            return null;
        }

        /// <summary>Volume of a category (with the master volume).</summary>
        public float Category(SoundCategory c) => master * c switch
        {
            SoundCategory.Ambience => ambienceVolume,
            SoundCategory.Boat => boatVolume,
            SoundCategory.Ui => uiVolume,
            _ => sfxVolume,
        };

        /// <summary>Plays a sound at a world position (volume / pitch multiplied by the given scales).</summary>
        public static AudioSource Play(SoundId id, Vector3 position, float volumeScale = 1f, float pitchScale = 1f)
        {
            var audio = Instance;
            var s = audio != null ? audio.Find(id) : null;
            if (s == null || s.clips == null || s.clips.Length == 0) return null;
            var now = Time.time;
            if (s.cooldown > 0f && audio.lastPlayed.TryGetValue(id, out var last) && now - last < s.cooldown) return null;
            audio.lastPlayed[id] = now;

            var clip = s.clips[UnityEngine.Random.Range(0, s.clips.Length)];
            if (clip == null) return null;
            var src = audio.NextVoice();
            src.transform.position = position;
            src.clip = clip;
            src.volume = Mathf.Clamp01(s.volume * volumeScale * UnityEngine.Random.Range(1f - s.volumeJitter, 1f) * audio.Category(s.category));
            src.pitch = UnityEngine.Random.Range(s.pitch.x, s.pitch.y) * pitchScale;
            src.spatialBlend = s.spatial;
            src.minDistance = s.minDistance;
            src.maxDistance = s.maxDistance;
            src.loop = false;
            src.time = Mathf.Clamp(s.startOffset, 0f, Mathf.Max(0f, clip.length - 0.05f));
            src.Play();
            if (s.maxDuration > 0f) audio.StartCoroutine(audio.StopAfter(src, clip, s.maxDuration));
            return src;
        }

        AudioSource NextVoice()
        {
            // The first free voice, else the oldest one.
            for (var i = 0; i < pool.Count; i++)
            {
                var src = pool[(next + i) % pool.Count];
                if (!src.isPlaying) { next = (next + i + 1) % pool.Count; return src; }
            }
            var steal = pool[next];
            next = (next + 1) % pool.Count;
            steal.Stop();
            return steal;
        }

        IEnumerator StopAfter(AudioSource src, AudioClip clip, float duration)
        {
            const float fade = 0.25f;
            yield return new WaitForSeconds(Mathf.Max(0f, duration - fade));
            if (src.clip != clip || !src.isPlaying) yield break;
            var v0 = src.volume;
            for (var t = 0f; t < fade; t += Time.deltaTime)
            {
                if (src.clip != clip) yield break;
                src.volume = v0 * (1f - t / fade);
                yield return null;
            }
            if (src.clip == clip) src.Stop();
        }
    }

    /// <summary>
    /// Seamless loop of a clip whose ends do not match (recordings): two sources crossfade each time the clip
    /// wraps around. Volume and pitch are driven every frame; the sources can be 3D (positioned by their object).
    /// </summary>
    public class LoopPlayer
    {
        readonly AudioSource a, b;
        readonly AudioClip clip;
        readonly float start, end, crossfade;
        AudioSource current;
        float volume;
        float pitch = 1f;

        public Transform Transform { get; }
        public bool HasClip => clip != null;

        /// <param name="endTrim">Seconds cut at the end of the clip (fade-out of the recording).</param>
        public LoopPlayer(GameObject parent, string name, AudioClip clip, float spatial, float crossfade, float startOffset = 2f, float endTrim = 1f)
        {
            this.clip = clip;
            this.crossfade = crossfade;
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            Transform = go.transform;
            a = Make(go, spatial);
            b = Make(go, spatial);
            // Skip the fade-in of the recordings.
            start = clip != null ? Mathf.Min(startOffset, clip.length * 0.25f) : 0f;
            end = clip != null ? Mathf.Max(start + 1f, clip.length - Mathf.Min(endTrim, clip.length * 0.4f)) : 0f;
        }

        static AudioSource Make(GameObject go, float spatial)
        {
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = spatial;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 2f;
            s.maxDistance = 40f;
            s.dopplerLevel = 0f;
            return s;
        }

        public void SetDistances(float min, float max)
        {
            a.minDistance = b.minDistance = min;
            a.maxDistance = b.maxDistance = max;
        }

        /// <summary>Call every frame with the wanted volume (0 = silent: the sources stop) and pitch.</summary>
        public void Update(float dt, float targetVolume, float targetPitch = 1f)
        {
            if (clip == null) return;
            volume = targetVolume;
            pitch = targetPitch;
            if (volume <= 0.001f)
            {
                if (a.isPlaying) a.Stop();
                if (b.isPlaying) b.Stop();
                current = null;
                return;
            }
            if (current == null)
            {
                current = a;
                StartAt(a, UnityEngine.Random.Range(start, Mathf.Max(start, start + (end - start) * 0.6f)));
            }
            var other = current == a ? b : a;
            var remaining = (end - current.time) / Mathf.Max(0.05f, pitch);
            var fade = Mathf.Min(crossfade, (end - start) * 0.2f);
            if (remaining <= fade && !other.isPlaying) StartAt(other, start);

            // Equal power crossfade.
            if (other.isPlaying)
            {
                var k = Mathf.Clamp01(1f - remaining / fade);
                current.volume = volume * Mathf.Cos(k * Mathf.PI * 0.5f);
                other.volume = volume * Mathf.Sin(k * Mathf.PI * 0.5f);
                if (!current.isPlaying || k >= 1f || current.time >= end) { current.Stop(); current = other; current.volume = volume; }
            }
            else current.volume = volume;
            a.pitch = b.pitch = pitch;
        }

        void StartAt(AudioSource s, float time)
        {
            s.clip = clip;
            s.time = Mathf.Clamp(time, 0f, clip.length - 0.1f);
            s.pitch = pitch;
            s.volume = 0f;
            s.Play();
        }
    }
}
