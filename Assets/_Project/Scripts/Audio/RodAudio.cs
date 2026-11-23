using FishingVR.Fish;
using FishingVR.Fishing;
using UnityEngine;

namespace FishingVR.Audio
{
    /// <summary>
    /// Rod sounds (added by <see cref="GameAudio"/> on each rod):
    /// - the reel's ratchet while cranking, faster / higher with the crank speed;
    /// - the wooden rod creaking under the line tension: rare soft creaks at low tension, more and more
    ///   often, louder and deeper as the tension rises (the warning before the line snaps);
    /// - the line strain when the tension enters the danger zone, the snap when it breaks;
    /// - the swish of the cast, the line running out when the fish runs.
    /// </summary>
    public class RodAudio : MonoBehaviour
    {
        [Header("Reel")]
        [Tooltip("Crank speed (turns/s) for the full reel volume.")]
        [SerializeField] float reelFullSpeed = 1.2f;

        [Header("Creak (wood under tension)")]
        [Tooltip("No creak below this tension (0..1).")]
        [SerializeField] float creakStart = 0.15f;
        [Tooltip("Creaks per second at the lowest / highest tension.")]
        [SerializeField] Vector2 creakRate = new Vector2(0.25f, 4f);

        FishingRod rod;
        FishFight fight;
        LoopPlayer reel;
        float creakTimer;

        void Start()
        {
            rod = GetComponent<FishingRod>();
            var audio = GameAudio.Instance;
            if (rod == null || audio == null) { enabled = false; return; }
            reel = new LoopPlayer(gameObject, "ReelAudio", audio.reelLoop, 1f, 0.3f, 1f, 2f);
            reel.SetDistances(0.5f, 10f);

            rod.onCast.AddListener(OnCast);
            fight = FindAnyObjectByType<FishFight>();
            if (fight != null)
            {
                fight.onPhaseChanged.AddListener(OnPhase);
                fight.onTensionWarning.AddListener(OnWarning);
                fight.onLineBroken.AddListener(OnLineBroken);
            }
        }

        void OnDestroy()
        {
            if (rod != null) rod.onCast.RemoveListener(OnCast);
            if (fight != null)
            {
                fight.onPhaseChanged.RemoveListener(OnPhase);
                fight.onTensionWarning.RemoveListener(OnWarning);
                fight.onLineBroken.RemoveListener(OnLineBroken);
            }
        }

        void Update()
        {
            var audio = GameAudio.Instance;
            if (audio == null) return;
            var dt = Time.deltaTime;

            // Reel ratchet.
            if (reel.HasClip && rod.Reel != null)
            {
                var crank = Mathf.Max(0f, rod.Reel.CrankSpeed);
                var k = Mathf.Clamp01(crank / reelFullSpeed);
                reel.Transform.position = rod.Reel.transform.position;
                reel.Update(dt, audio.reel * Mathf.Sqrt(k) * audio.Category(SoundCategory.Sfx), 0.85f + 0.3f * Mathf.Clamp01(crank / (reelFullSpeed * 2f)));
            }

            // Creaks: more often, louder and deeper with the tension.
            var tension = Mathf.InverseLerp(creakStart, 1f, rod.LineTension);
            if (rod.IsHeld && tension > 0f)
            {
                creakTimer -= dt * Mathf.Lerp(creakRate.x, creakRate.y, Mathf.Pow(tension, 1.5f));
                if (creakTimer <= 0f)
                {
                    creakTimer = Random.Range(0.6f, 1.4f);
                    var at = Vector3.Lerp(rod.transform.position, rod.RodTip != null ? rod.RodTip.position : rod.transform.position, 0.35f);
                    GameAudio.Play(SoundId.RodCreak, at, Mathf.Lerp(audio.rodCreak.x, audio.rodCreak.y, tension * tension), 1.12f - 0.3f * tension);
                }
            }
            else creakTimer = Random.Range(0.2f, 1f);
        }

        Vector3 Tip => rod.RodTip != null ? rod.RodTip.position : rod.transform.position;

        void OnCast(Vector3 launch) => GameAudio.Play(SoundId.Cast, Tip, Mathf.Clamp01(0.4f + launch.magnitude / 15f), 0.9f + Mathf.Clamp01(launch.magnitude / 18f) * 0.25f);

        void OnPhase(FightPhase phase)
        {
            if (phase == FightPhase.Run && rod.Reel != null) GameAudio.Play(SoundId.LineRun, rod.Reel.transform.position);
        }

        void OnWarning(bool danger)
        {
            if (danger && rod.Reel != null) GameAudio.Play(SoundId.LineStrain, rod.Reel.transform.position);
        }

        void OnLineBroken() => GameAudio.Play(SoundId.LineSnap, Tip);
    }
}
