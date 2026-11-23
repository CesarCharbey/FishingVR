using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace FishingVR.EditorTools
{
    /// <summary>
    /// Generates the sounds we had no recording for (procedural synthesis, written as 16-bit mono WAV in
    /// Audio/Generated): wood creaks of the rod, line snap, water plips, fish slaps, cooler lid, rack knock,
    /// soft UI chimes. Re-running it rewrites them (same seeds = same sounds).
    /// </summary>
    public static class SfxSynth
    {
        const int Rate = 44100;
        const string Dir = "Assets/_Project/Audio/Generated";

        [MenuItem("FishingVR/Audio/Générer les sons synthétiques")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(Dir);
            // Rod creaks: recorded ones in Audio/Clips (Creak() kept as a fallback generator).
            Write("SFX_LineSnap", Snap(7));
            for (var i = 0; i < 4; i++) Write($"SFX_Plip_{i + 1:00}", Plip(30 + i));
            for (var i = 0; i < 3; i++) Write($"SFX_FishSlap_{i + 1:00}", Slap(50 + i));
            Write("SFX_CoolerOpen", Knock(60, new[] { 320f, 870f, 1650f }, 0.09f, 0.7f, 0.25f));
            Write("SFX_CoolerClose", Knock(61, new[] { 170f, 460f, 1100f }, 0.16f, 1f, 0.12f));
            Write("SFX_RackKnock", Knock(62, new[] { 520f, 1380f, 2900f }, 0.07f, 0.8f, 0.2f));
            Write("UI_Pop", Pop());
            Write("UI_NewSpecies", Marimba(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.11f, 1.6f));
            Write("UI_Record", Marimba(new[] { 783.99f, 1046.5f }, 0.13f, 1.2f));
            AssetDatabase.Refresh();
            Debug.Log("[SfxSynth] Generated sounds in " + Dir);
        }

        // ---------- Sounds ----------

        /// <summary>
        /// Wood creak: stick-slip friction. An irregular train of tiny impulses (the fibres catching and slipping)
        /// excites a few wooden resonances; the impulse rate glides (pitch of the creak) and it swells and fades.
        /// </summary>
        static float[] Creak(int seed, float duration)
        {
            var rng = new Random(seed);
            var n = (int)(duration * Rate);
            var excitation = new float[n];
            var rate0 = Lerp(55f, 120f, (float)rng.NextDouble()); // stick-slip rate = perceived pitch of the creak
            var rate1 = rate0 * Lerp(0.55f, 1.6f, (float)rng.NextDouble());
            var t = 0f;
            while (t < duration)
            {
                var k = t / duration;
                var rate = Lerp(rate0, rate1, k) * Lerp(0.9f, 1.1f, (float)rng.NextDouble());
                var i = (int)(t * Rate);
                if (i < n) excitation[i] += Lerp(0.6f, 1f, (float)rng.NextDouble()) * (rng.NextDouble() < 0.5 ? -1f : 1f);
                t += 1f / rate;
            }
            // A little scraping noise between the clicks.
            for (var i = 0; i < n; i++) excitation[i] += ((float)rng.NextDouble() * 2f - 1f) * 0.004f;

            var output = new float[n];
            var modes = new[] { Lerp(170f, 260f, (float)rng.NextDouble()), Lerp(430f, 640f, (float)rng.NextDouble()), Lerp(950f, 1400f, (float)rng.NextDouble()), Lerp(2100f, 2700f, (float)rng.NextDouble()) };
            var gains = new[] { 1f, 0.8f, 0.45f, 0.2f };
            for (var m = 0; m < modes.Length; m++)
            {
                var band = BandPass(BandPass(excitation, modes[m], Lerp(14f, 26f, (float)rng.NextDouble())), modes[m], 6f); // two stages: tonal wood, no hiss
                for (var i = 0; i < n; i++) output[i] += band[i] * gains[m];
            }
            // Swell in, hold, fade.
            for (var i = 0; i < n; i++)
            {
                var k = i / (float)n;
                output[i] *= Mathf.Clamp01(k / 0.15f) * Mathf.Clamp01((1f - k) / 0.35f);
            }
            return Normalize(output, 0.7f);
        }

        /// <summary>Line snapping: a sharp crack, a short twang that drops in pitch, a hiss of whipping line.</summary>
        static float[] Snap(int seed)
        {
            var rng = new Random(seed);
            var n = (int)(0.35f * Rate);
            var o = new float[n];
            var noise = new float[n];
            for (var i = 0; i < n; i++) noise[i] = (float)rng.NextDouble() * 2f - 1f;
            var crack = HighPass(noise, 3000f);
            var whip = BandPass(noise, 4200f, 1.2f);
            var phase = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                o[i] += crack[i] * Mathf.Exp(-t / 0.006f) * 1.4f;
                var f = Lerp(1100f, 520f, Mathf.Clamp01(t / 0.18f));
                phase += 2f * Mathf.PI * f / Rate;
                o[i] += Mathf.Sin(phase) * Mathf.Exp(-t / 0.07f) * 0.55f;
                o[i] += whip[i] * Mathf.Exp(-t / 0.04f) * 0.5f * Mathf.Clamp01(t / 0.01f);
            }
            return Normalize(o, 0.8f);
        }

        /// <summary>Water drop: a short sine sweeping up quickly (the bubble resonance), with a soft click.</summary>
        static float[] Plip(int seed)
        {
            var rng = new Random(seed);
            var n = (int)(0.16f * Rate);
            var o = new float[n];
            var f0 = Lerp(500f, 900f, (float)rng.NextDouble());
            var f1 = f0 * Lerp(2f, 3f, (float)rng.NextDouble());
            var phase = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                var f = Lerp(f0, f1, Mathf.Clamp01(t / 0.035f));
                phase += 2f * Mathf.PI * f / Rate;
                o[i] = Mathf.Sin(phase) * Mathf.Exp(-t / 0.03f) * Mathf.Clamp01(t / 0.002f);
            }
            return Normalize(o, 0.6f);
        }

        /// <summary>Wet fish slap: a low thump plus a burst of filtered noise (water).</summary>
        static float[] Slap(int seed)
        {
            var rng = new Random(seed);
            var n = (int)(0.3f * Rate);
            var noise = new float[n];
            for (var i = 0; i < n; i++) noise[i] = (float)rng.NextDouble() * 2f - 1f;
            var wet = BandPass(noise, Lerp(900f, 1400f, (float)rng.NextDouble()), 0.9f);
            var o = new float[n];
            var f = Lerp(95f, 140f, (float)rng.NextDouble());
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                o[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t / 0.045f) * 0.9f
                     + wet[i] * Mathf.Exp(-t / 0.035f) * 1.2f;
            }
            return Normalize(o, 0.7f);
        }

        /// <summary>Knock on a hollow object (plastic / wood): a click exciting a few resonances.</summary>
        static float[] Knock(int seed, float[] modes, float decay, float level, float click)
        {
            var rng = new Random(seed);
            var n = (int)((decay * 5f) * Rate);
            var o = new float[n];
            var gains = new[] { 1f, 0.55f, 0.25f };
            for (var m = 0; m < modes.Length; m++)
                for (var i = 0; i < n; i++)
                {
                    var t = i / (float)Rate;
                    o[i] += Mathf.Sin(2f * Mathf.PI * modes[m] * t) * Mathf.Exp(-t / (decay / (1f + m * 0.6f))) * gains[m];
                }
            for (var i = 0; i < Mathf.Min(n, Rate / 200); i++)
                o[i] += ((float)rng.NextDouble() * 2f - 1f) * click * (1f - i / (Rate / 200f));
            for (var i = 0; i < n; i++) o[i] *= Mathf.Clamp01(i / (Rate * 0.001f));
            return Normalize(o, 0.75f * level);
        }

        /// <summary>Soft UI pop: a short rounded blip sliding down.</summary>
        static float[] Pop()
        {
            var n = (int)(0.18f * Rate);
            var o = new float[n];
            var phase = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)Rate;
                var f = Lerp(880f, 620f, Mathf.Clamp01(t / 0.08f));
                phase += 2f * Mathf.PI * f / Rate;
                o[i] = (Mathf.Sin(phase) + 0.25f * Mathf.Sin(2f * phase)) * Mathf.Exp(-t / 0.045f) * Mathf.Clamp01(t / 0.004f);
            }
            return Normalize(o, 0.5f);
        }

        /// <summary>Gentle marimba arpeggio (fundamental + the bar's 4x partial, quick decay).</summary>
        static float[] Marimba(float[] notes, float spacing, float length)
        {
            var n = (int)(length * Rate);
            var o = new float[n];
            for (var k = 0; k < notes.Length; k++)
            {
                var start = (int)(k * spacing * Rate);
                var f = notes[k];
                for (var i = start; i < n; i++)
                {
                    var t = (i - start) / (float)Rate;
                    var env = Mathf.Exp(-t / 0.35f) * Mathf.Clamp01(t / 0.003f);
                    o[i] += (Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 4f * t) * Mathf.Exp(-t / 0.05f)
                             + 0.15f * Mathf.Sin(2f * Mathf.PI * f * 10f * t) * Mathf.Exp(-t / 0.015f)) * env * (k == notes.Length - 1 ? 1f : 0.8f);
                }
            }
            return Normalize(o, 0.5f);
        }

        // ---------- DSP helpers ----------

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>RBJ band-pass biquad (constant 0 dB peak gain).</summary>
        static float[] BandPass(float[] x, float freq, float q)
        {
            var w0 = 2f * Mathf.PI * freq / Rate;
            var alpha = Mathf.Sin(w0) / (2f * q);
            var a0 = 1f + alpha;
            return Biquad(x, alpha / a0, 0f, -alpha / a0, -2f * Mathf.Cos(w0) / a0, (1f - alpha) / a0);
        }

        static float[] HighPass(float[] x, float freq)
        {
            var w0 = 2f * Mathf.PI * freq / Rate;
            var alpha = Mathf.Sin(w0) / (2f * 0.707f);
            var c = Mathf.Cos(w0);
            var a0 = 1f + alpha;
            return Biquad(x, (1f + c) / 2f / a0, -(1f + c) / a0, (1f + c) / 2f / a0, -2f * c / a0, (1f - alpha) / a0);
        }

        static float[] Biquad(float[] x, float b0, float b1, float b2, float a1, float a2)
        {
            var y = new float[x.Length];
            float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (var i = 0; i < x.Length; i++)
            {
                var v = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x[i]; y2 = y1; y1 = v;
                y[i] = v;
            }
            return y;
        }

        static float[] Normalize(float[] x, float peak)
        {
            var max = 1e-6f;
            foreach (var v in x) max = Mathf.Max(max, Mathf.Abs(v));
            for (var i = 0; i < x.Length; i++) x[i] *= peak / max;
            // 3 ms fade out: no click at the end.
            var fade = Mathf.Min(x.Length, Rate * 3 / 1000);
            for (var i = 0; i < fade; i++) x[x.Length - 1 - i] *= i / (float)fade;
            return x;
        }

        static void Write(string name, float[] samples)
        {
            var path = $"{Dir}/{name}.wav";
            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            var dataBytes = samples.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);       // PCM
            w.Write((short)1);       // mono
            w.Write(Rate);
            w.Write(Rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            foreach (var s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
        }
    }
}
