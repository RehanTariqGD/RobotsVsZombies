using System;
using UnityEngine;

namespace ZeroSynth.Audio
{
    public enum Sfx
    {
        Shot,
        Acid,
        Death,
        Hit,
        Build,
        Error,
        WaveStart,
        Victory,
        Defeat,
        UiClick,
    }

    /// <summary>
    /// Builds every sound in code.
    /// </summary>
    /// <remarks>
    /// Placeholder audio, and deliberately free: it needs no external service,
    /// no credits and no files in the repository, and it gives the settings
    /// sliders something real to control. It is simple synthesis (sine sweeps,
    /// filtered noise, exponential envelopes) so it will sound like a prototype.
    /// Replacing a sound later is one AudioClip assigned in place of one of
    /// these, with nothing else changing.
    ///
    /// Mono, 22.05 kHz: these are short effects and the music is a drone, so
    /// the half sample rate loses nothing audible and halves the memory.
    /// </remarks>
    public static class ProceduralAudio
    {
        const int Rate = 22050;
        const float Tau = Mathf.PI * 2f;

        public static AudioClip Create(Sfx id)
        {
            switch (id)
            {
                case Sfx.Shot: return Shot();
                case Sfx.Acid: return Acid();
                case Sfx.Death: return Death();
                case Sfx.Hit: return Hit();
                case Sfx.Build: return BuildSound();
                case Sfx.Error: return ErrorSound();
                case Sfx.UiClick: return UiClick();
                case Sfx.WaveStart: return WaveStart();
                case Sfx.Victory: return Phrase("sfx_victory", new[] { 262f, 330f, 392f, 523f }, 0.2f, 5f);
                case Sfx.Defeat: return Phrase("sfx_defeat", new[] { 330f, 277f, 233f, 196f }, 0.3f, 3.5f);
                default: return Shot();
            }
        }

        static AudioClip Render(string name, float seconds, Func<float, float, float> sample, bool loop = false)
        {
            int count = (int)(seconds * Rate);
            var data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                data[i] = Mathf.Clamp(sample(t, t / seconds), -0.95f, 0.95f);
            }

            // Fade the final few milliseconds so a one-shot never ends on a click.
            if (!loop)
            {
                int fade = Mathf.Min(count, Rate / 200);
                for (int i = 0; i < fade; i++)
                    data[count - 1 - i] *= i / (float)fade;
            }

            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        static AudioClip Shot()
        {
            var rng = new System.Random(11);
            float phase = 0f;
            return Render("sfx_shot", 0.14f, (t, p) =>
            {
                phase += (350f + 1100f * Mathf.Exp(-t * 40f)) / Rate;
                float tone = Mathf.Sin(Tau * phase) * Mathf.Exp(-t * 26f) * 0.7f;
                float click = Noise(rng) * Mathf.Exp(-t * 90f) * 0.35f;
                return tone + click;
            });
        }

        static AudioClip Acid()
        {
            var rng = new System.Random(23);
            float low = 0f, phase = 0f;
            return Render("sfx_acid", 0.22f, (t, p) =>
            {
                low += (Noise(rng) - low) * 0.18f;
                phase += (160f + 50f * Mathf.Sin(Tau * 30f * t)) / Rate;
                return (low * 0.9f + Mathf.Sin(Tau * phase) * 0.35f) * Mathf.Exp(-t * 9f) * 0.8f;
            });
        }

        static AudioClip Death()
        {
            var rng = new System.Random(37);
            float low = 0f, phase = 0f;
            return Render("sfx_death", 0.2f, (t, p) =>
            {
                low += (Noise(rng) - low) * 0.25f;
                phase += (40f + 90f * Mathf.Exp(-t * 9f)) / Rate;
                return low * Mathf.Exp(-t * 14f) * 0.9f + Mathf.Sin(Tau * phase) * Mathf.Exp(-t * 11f) * 0.8f;
            });
        }

        static AudioClip Hit()
        {
            var rng = new System.Random(41);
            return Render("sfx_hit", 0.28f, (t, p) =>
            {
                float metal = Mathf.Sin(Tau * 220f * t) + 0.6f * Mathf.Sin(Tau * 331f * t) + 0.4f * Mathf.Sin(Tau * 547f * t);
                return metal * Mathf.Exp(-t * 9f) * 0.3f + Noise(rng) * Mathf.Exp(-t * 70f) * 0.4f;
            });
        }

        static AudioClip BuildSound()
        {
            float phase = 0f;
            return Render("sfx_build", 0.24f, (t, p) =>
            {
                float local = (p % 0.5f) / 0.5f;
                phase += (p < 0.5f ? 330f : 495f) / Rate;
                return Mathf.Sin(Tau * phase) * (1f - local) * (1f - local) * 0.6f;
            });
        }

        static AudioClip ErrorSound()
        {
            return Render("sfx_error", 0.16f, (t, p) =>
                Mathf.Sign(Mathf.Sin(Tau * 120f * t)) * 0.3f * Mathf.Exp(-t * 10f));
        }

        /// <summary>A short dry tick, quiet enough to be pressed a dozen times in a row.</summary>
        static AudioClip UiClick()
        {
            var rng = new System.Random(53);
            return Render("sfx_ui_click", 0.07f, (t, p) =>
                Mathf.Sin(Tau * 1200f * t) * Mathf.Exp(-t * 70f) * 0.45f
                + Noise(rng) * Mathf.Exp(-t * 200f) * 0.15f);
        }

        static AudioClip WaveStart()
        {
            return Render("sfx_wave", 1.0f, (t, p) =>
            {
                float swell = Mathf.Sin(Mathf.PI * p);
                float tone = Mathf.Sin(Tau * 98f * t) * 0.5f
                           + Mathf.Sin(Tau * 147f * t) * 0.35f
                           + Mathf.Sin(Tau * 49f * t) * 0.4f;
                return tone * swell * swell * (1f + 0.3f * Mathf.Sin(Tau * 6f * t)) * 0.6f;
            });
        }

        /// <summary>A short run of notes, each with its own decay.</summary>
        static AudioClip Phrase(string name, float[] notes, float noteSeconds, float decay)
        {
            return Render(name, notes.Length * noteSeconds, (t, p) =>
            {
                int index = Mathf.Min((int)(t / noteSeconds), notes.Length - 1);
                float local = t - index * noteSeconds;
                float f = notes[index];
                float tone = Mathf.Sin(Tau * f * local) + 0.35f * Mathf.Sin(Tau * f * 2f * local);
                return tone * Mathf.Exp(-local * decay) * 0.4f;
            });
        }

        /// <summary>
        /// A sixteen-second loop: a low drone, a slow bass pulse and a soft kick.
        /// </summary>
        /// <remarks>
        /// Every sustained tone is an integer number of cycles over sixteen
        /// seconds, otherwise the loop point clicks. 90 BPM gives exactly 24
        /// beats.
        /// </remarks>
        public static AudioClip CreateMusic()
        {
            float[] bass = { 55f, 55f, 65.4f, 55f, 82.5f, 55f, 73.4f, 55f };

            return Render("music_loop", 16f, (t, p) =>
            {
                float beat = t * 1.5f;
                float inBeat = beat - Mathf.Floor(beat);

                float drone = Mathf.Sin(Tau * 55f * t) * 0.22f
                            + Mathf.Sin(Tau * 82.5f * t) * 0.12f
                            + Mathf.Sin(Tau * 110f * t) * 0.07f * (0.6f + 0.4f * Mathf.Sin(Tau * t / 16f));

                float eighth = beat * 2f;
                int step = (int)eighth % bass.Length;
                float local = (eighth - Mathf.Floor(eighth)) / 2f * (60f / 90f);
                float pulse = Mathf.Sin(Tau * bass[step] * local) * Mathf.Exp(-local * 7f) * 0.32f;

                float kick = Mathf.Sin(Tau * (48f * inBeat * 0.6667f + 70f * (1f - Mathf.Exp(-inBeat * 9f)) / 9f))
                           * Mathf.Exp(-inBeat * 10f) * 0.3f;

                return drone + pulse + kick;
            }, loop: true);
        }
    }
}
