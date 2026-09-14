using System;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Audio
{
    /// <summary>
    /// Synthesises the game's sound at load time.
    ///
    /// A horror game with no audio is not a horror game, and this repository ships no
    /// binary assets — so the sound is generated. That constraint turns out to suit
    /// the material: everything the player hears is machinery, water and rock, which
    /// is exactly the class of sound that additive synthesis and filtered noise do
    /// well. The generator is genuinely built from a firing fundamental and its
    /// harmonics; the drip is a real pitch-falling resonance.
    ///
    /// Loops are made seamless by crossfading the tail back over the head, so noise
    /// beds do not click once a second. Everything is seeded, so the same sound comes
    /// out on every machine, and the CC0 packs the Asset Fetcher pulls can replace any
    /// of it by name without touching code.
    /// </summary>
    public static class ProceduralAudio
    {
        public const int SampleRate = 44100;

        /// <summary>Fraction of a looping clip used for the seam crossfade.</summary>
        private const float CrossfadeFraction = 0.12f;

        // =====================================================================
        // Loops
        // =====================================================================

        /// <summary>
        /// The Lister: a firing fundamental with harmonics, mechanical clatter locked
        /// to the firing rate, and the alternator's whine sitting on top.
        /// </summary>
        public static AudioClip GeneratorHum(float seconds = 2f, int seed = 11)
        {
            const float firing = 25f;            // 1500 rpm, four stroke
            const float alternator = 100f;
            var rng = new RandomSource(seed);
            float lowpass = 0f;

            return CreateLoop("SFX_GeneratorHum", seconds, firing, (t, i) =>
            {
                float value = 0f;

                // Harmonic stack. Diesels are rich down low and thin above the fourth.
                value += Mathf.Sin(Tau * firing * t) * 0.42f;
                value += Mathf.Sin(Tau * firing * 2f * t) * 0.26f;
                value += Mathf.Sin(Tau * firing * 3f * t + 0.7f) * 0.15f;
                value += Mathf.Sin(Tau * firing * 5f * t + 1.9f) * 0.06f;

                // Alternator whine, slightly detuned so it beats against itself.
                value += Mathf.Sin(Tau * alternator * t) * 0.05f;
                value += Mathf.Sin(Tau * (alternator * 1.004f) * t) * 0.04f;

                // Valve clatter: noise gated to the top of each firing cycle.
                float phase = Mathf.Repeat(t * firing, 1f);
                float gate = phase < 0.1f ? 1f - phase / 0.1f : 0f;
                float noise = rng.Range(-1f, 1f);
                lowpass += (noise - lowpass) * 0.35f;
                value += lowpass * gate * 0.22f;

                return value * 0.55f;
            }, MachineryLevel);
        }

        /// <summary>Axial fan: broadband rush plus a blade-pass tone.</summary>
        public static AudioClip FanLoop(float seconds = 2f, int seed = 23)
        {
            const float bladePass = 61f;
            var rng = new RandomSource(seed);
            float lowA = 0f, lowB = 0f;

            return CreateLoop("SFX_FanLoop", seconds, bladePass, (t, i) =>
            {
                // Two cascaded one-poles give a steeper rolloff than one, which is the
                // difference between "air moving" and "hiss".
                float noise = rng.Range(-1f, 1f);
                lowA += (noise - lowA) * 0.28f;
                lowB += (lowA - lowB) * 0.28f;

                float value = lowB * 0.75f;
                value += Mathf.Sin(Tau * bladePass * t) * 0.12f;
                value += Mathf.Sin(Tau * bladePass * 2f * t) * 0.05f;

                return value * 0.7f;
            }, MachineryLevel * 0.8f);
        }

        /// <summary>Column pump: low rumble, water rush, and the impeller's tone.</summary>
        public static AudioClip PumpLoop(float seconds = 2f, int seed = 37)
        {
            const float impeller = 29f;
            var rng = new RandomSource(seed);
            float low = 0f;

            return CreateLoop("SFX_PumpLoop", seconds, impeller, (t, i) =>
            {
                float noise = rng.Range(-1f, 1f);
                low += (noise - low) * 0.14f;

                float value = low * 0.55f;
                value += Mathf.Sin(Tau * impeller * t) * 0.3f;
                value += Mathf.Sin(Tau * impeller * 2f * t + 0.5f) * 0.14f;

                // Water moving through the column: brighter noise, amplitude modulated.
                float wash = rng.Range(-1f, 1f) * 0.18f;
                value += wash * (0.6f + 0.4f * Mathf.Sin(Tau * impeller * 0.5f * t));

                return value * 0.6f;
            }, MachineryLevel);
        }

        /// <summary>Cavitation: the pump eating air. Irregular, hard, and wrong.</summary>
        public static AudioClip CavitationLoop(float seconds = 1.5f, int seed = 41)
        {
            var rng = new RandomSource(seed);

            return CreateLoop("SFX_Cavitation", seconds, 20f, (t, i) =>
            {
                float crackle = rng.Chance(0.06f) ? rng.Range(-1f, 1f) : 0f;
                float rumble = Mathf.Sin(Tau * 42f * t) * 0.2f;
                return (crackle * 0.7f + rumble) * 0.65f;
            }, MachineryLevel);
        }

        /// <summary>Monitor snow.</summary>
        public static AudioClip StaticHiss(float seconds = 1.5f, int seed = 53)
        {
            var rng = new RandomSource(seed);
            float low = 0f;

            return CreateLoop("SFX_Static", seconds, 10f, (t, i) =>
            {
                float noise = rng.Range(-1f, 1f);
                low += (noise - low) * 0.55f;
                return low * 0.4f;
            }, HissLevel);
        }

        /// <summary>Cave room tone: a very low moving air bed with distant drips baked in.</summary>
        public static AudioClip CaveAmbience(float seconds = 6f, int seed = 67)
        {
            var rng = new RandomSource(seed);
            float low = 0f;

            return CreateLoop("SFX_CaveAmbience", seconds, 1f, (t, i) =>
            {
                float noise = rng.Range(-1f, 1f);
                low += (noise - low) * 0.02f;      // very heavy filtering: almost sub-bass

                float value = low * 1.4f;
                value += Mathf.Sin(Tau * 41f * t) * 0.05f;
                value += Mathf.Sin(Tau * 57f * t + 1.2f) * 0.03f;

                return value * 0.5f;
            }, BedLevel);
        }

        // =====================================================================
        // One-shots
        // =====================================================================

        /// <summary>A drip landing in standing water: falling resonance, fast decay.</summary>
        public static AudioClip Drip(int seed = 71)
        {
            var rng = new RandomSource(seed);
            float startPitch = rng.Range(760f, 1250f);
            float phase = 0f;

            return Create("SFX_Drip", 0.35f, (t, i) =>
            {
                float progress = t / 0.35f;

                // The pitch falls as the cavity the drop makes closes up.
                float pitch = Mathf.Lerp(startPitch, startPitch * 0.35f, progress * progress);
                phase += Tau * pitch / SampleRate;

                float envelope = Mathf.Exp(-14f * t);
                float transient = t < 0.004f ? rng.Range(-1f, 1f) * 0.5f : 0f;

                return (Mathf.Sin(phase) * envelope + transient) * 0.6f;
            });
        }

        /// <summary>Something metal being struck: noise transient plus ringing partials.</summary>
        public static AudioClip MetalImpact(int seed = 83, float brightness = 1f)
        {
            var rng = new RandomSource(seed);
            float f0 = rng.Range(180f, 320f) * brightness;

            return Create("SFX_MetalImpact", 0.9f, (t, i) =>
            {
                float strike = t < 0.012f ? rng.Range(-1f, 1f) * Mathf.Exp(-180f * t) : 0f;

                // Inharmonic partials — a struck plate, not a tuned bell.
                float ring =
                    Mathf.Sin(Tau * f0 * t) * Mathf.Exp(-4.5f * t) * 0.5f +
                    Mathf.Sin(Tau * f0 * 2.41f * t) * Mathf.Exp(-6.5f * t) * 0.3f +
                    Mathf.Sin(Tau * f0 * 3.87f * t) * Mathf.Exp(-9f * t) * 0.18f;

                return (strike + ring) * 0.7f;
            });
        }

        /// <summary>A blast door cycling: motor ramp, travel, and the seat.</summary>
        public static AudioClip DoorCycle(int seed = 97)
        {
            var rng = new RandomSource(seed);
            float phase = 0f;
            float low = 0f;

            return Create("SFX_DoorCycle", 1.2f, (t, i) =>
            {
                float progress = Mathf.Clamp01(t / 1.1f);

                // Motor loads up, runs, then drops off the back.
                float pitch = Mathf.Lerp(52f, 96f, Mathf.Sin(progress * Mathf.PI));
                phase += Tau * pitch / SampleRate;

                // A sawtooth reads as geared machinery far better than a sine does.
                float saw = Mathf.Repeat(phase / Tau, 1f) * 2f - 1f;

                float noise = rng.Range(-1f, 1f);
                low += (noise - low) * 0.2f;

                float envelope = Mathf.Min(1f, t / 0.06f) * (1f - Mathf.SmoothStep(0.85f, 1f, progress));
                float body = (saw * 0.35f + low * 0.4f) * envelope;

                // The clunk of it seating home.
                float seat = 0f;
                if (t > 1.02f)
                {
                    float u = t - 1.02f;
                    seat = (rng.Range(-1f, 1f) * Mathf.Exp(-90f * u) +
                            Mathf.Sin(Tau * 140f * u) * Mathf.Exp(-22f * u)) * 0.8f;
                }

                return (body + seat) * 0.65f;
            });
        }

        /// <summary>A breaker handle thrown.</summary>
        public static AudioClip BreakerClack(int seed = 101)
        {
            var rng = new RandomSource(seed);

            return Create("SFX_BreakerClack", 0.4f, (t, i) =>
            {
                // Two transients: the mechanism releasing, then the contacts.
                float first = Mathf.Exp(-140f * t);
                float second = t > 0.055f ? Mathf.Exp(-110f * (t - 0.055f)) : 0f;

                float noise = rng.Range(-1f, 1f);
                float ring = Mathf.Sin(Tau * 410f * t) * Mathf.Exp(-26f * t) * 0.35f;

                return (noise * (first + second) * 0.55f + ring) * 0.8f;
            });
        }

        /// <summary>A heavy footfall on rock, with grit.</summary>
        public static AudioClip Footstep(int seed = 113)
        {
            var rng = new RandomSource(seed);
            float weight = new RandomSource(seed).Range(0.8f, 1.2f);

            return Create("SFX_Footstep", 0.4f, (t, i) =>
            {
                float thud = Mathf.Sin(Tau * 74f * weight * t) * Mathf.Exp(-26f * t) * 0.8f;
                float grit = rng.Range(-1f, 1f) * Mathf.Exp(-45f * t) * 0.3f;
                return (thud + grit) * 0.7f;
            });
        }

        /// <summary>
        /// The jumpscare.
        ///
        /// Detuned sawtooths an augmented fourth apart, a falling noise sweep, and hard
        /// amplitude modulation. Short — it lands and gets out, because a long one
        /// stops being a shock and becomes a noise the player waits out.
        /// </summary>
        public static AudioClip Jumpscare(int seed = 127)
        {
            var rng = new RandomSource(seed);
            float p1 = 0f, p2 = 0f, p3 = 0f;
            float low = 0f;

            return Create("SFX_Jumpscare", 1.1f, (t, i) =>
            {
                float progress = t / 1.1f;

                // Falling cluster: root, tritone, and a detuned root above.
                float f = Mathf.Lerp(320f, 90f, progress * progress);
                p1 += Tau * f / SampleRate;
                p2 += Tau * (f * 1.414f) / SampleRate;
                p3 += Tau * (f * 2.02f) / SampleRate;

                float Saw(float p) => Mathf.Repeat(p / Tau, 1f) * 2f - 1f;
                float cluster = (Saw(p1) * 0.4f + Saw(p2) * 0.3f + Saw(p3) * 0.22f);

                // Noise sweep, opening then closing.
                float noise = rng.Range(-1f, 1f);
                float cutoff = Mathf.Lerp(0.6f, 0.08f, progress);
                low += (noise - low) * cutoff;

                // Ring modulation: metallic, and genuinely unpleasant.
                float ring = Mathf.Sin(Tau * Mathf.Lerp(70f, 23f, progress) * t);

                float envelope = Mathf.Min(1f, t / 0.004f) * (1f - Mathf.SmoothStep(0.7f, 1f, progress));

                return (cluster * ring * 0.6f + low * 0.5f) * envelope * 0.85f;
            }, StingLevel);
        }

        /// <summary>The 6 AM bell. The only kind sound in the game.</summary>
        public static AudioClip DawnChime(int seed = 131)
        {
            return Create("SFX_DawnChime", 2.4f, (t, i) =>
            {
                // A major triad with a slow attack — it should feel like relief.
                float envelope = (1f - Mathf.Exp(-6f * t)) * Mathf.Exp(-1.4f * t);

                float value =
                    Mathf.Sin(Tau * 523.25f * t) * 0.45f +   // C5
                    Mathf.Sin(Tau * 659.25f * t) * 0.32f +   // E5
                    Mathf.Sin(Tau * 783.99f * t) * 0.24f +   // G5
                    Mathf.Sin(Tau * 1046.5f * t) * 0.12f;    // C6

                return value * envelope * 0.5f;
            });
        }

        /// <summary>
        /// The survey office acknowledging a filed reading.
        ///
        /// A teleprinter, not a chime. This is the only positive feedback in the game
        /// and it would be very easy to make it feel like a mobile-game coin sound;
        /// three dry mechanical clacks and a bell keep it in the same world as the rest
        /// of the building.
        /// </summary>
        public static AudioClip SurveyFiled(int seed = 137)
        {
            var rng = new System.Random(seed);
            var offsets = new[] { 0f, 0.09f, 0.17f };
            var jitter = new float[offsets.Length];
            for (int i = 0; i < jitter.Length; i++) jitter[i] = (float)rng.NextDouble() * 0.012f;

            return Create("SFX_SurveyFiled", 1.1f, (t, i) =>
            {
                float value = 0f;

                // Three key strikes.
                for (int k = 0; k < offsets.Length; k++)
                {
                    float local = t - offsets[k] - jitter[k];
                    if (local < 0f || local > 0.09f) continue;

                    float envelope = Mathf.Exp(-90f * local);
                    value += (Mathf.Sin(Tau * (1650f + k * 180f) * local) * 0.5f +
                              Mathf.Sin(Tau * 3900f * local) * 0.3f) * envelope;
                }

                // Carriage bell at the end of the line.
                float bell = t - 0.34f;
                if (bell > 0f)
                {
                    float envelope = Mathf.Exp(-5.5f * bell);
                    value += (Mathf.Sin(Tau * 2093f * bell) * 0.34f +
                              Mathf.Sin(Tau * 3136f * bell) * 0.16f) * envelope;
                }

                return value * 0.7f;
            });
        }

        /// <summary>
        /// The few seconds of warning before a night event lands.
        ///
        /// A rising two-tone, deliberately unmusical — the interval is a tritone, which
        /// is the one thing the ear refuses to hear as resolution. It should be
        /// impossible to mistake for anything else on the panel.
        /// </summary>
        public static AudioClip EventWarning(int seed = 139)
        {
            return Create("SFX_EventWarning", 1.4f, (t, i) =>
            {
                float value = 0f;

                for (int k = 0; k < 2; k++)
                {
                    float local = t - k * 0.42f;
                    if (local < 0f || local > 0.36f) continue;

                    float envelope = Mathf.Min(1f, local * 40f) * Mathf.Exp(-6f * local);
                    float frequency = k == 0 ? 392f : 554.37f;   // G4 to C#5

                    value += (Mathf.Sin(Tau * frequency * local) * 0.5f +
                              Mathf.Sin(Tau * frequency * 2f * local) * 0.18f) * envelope;
                }

                return value * 0.8f;
            });
        }

        /// <summary>
        /// A tremor: infrasonic, long, with the rock creaking over the top of it.
        ///
        /// Most of the energy is below 40 Hz, where it is felt on a subwoofer and
        /// merely implied on laptop speakers — which is correct. The creak is what
        /// carries it on a small system.
        /// </summary>
        public static AudioClip Tremor(int seed = 149)
        {
            var rng = new System.Random(seed);
            float phase = (float)rng.NextDouble() * Tau;

            return Create("SFX_Tremor", 3.6f, (t, i) =>
            {
                float envelope = Mathf.Min(1f, t * 2.2f) * Mathf.Exp(-0.8f * t);

                // The body of it: two very low tones beating against each other.
                float body = Mathf.Sin(Tau * 27f * t + phase) * 0.6f +
                             Mathf.Sin(Tau * 33f * t) * 0.4f;

                // Rock complaining. Filtered noise, amplitude-modulated so it groans
                // rather than hisses.
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float groan = noise * (0.10f + 0.08f * Mathf.Sin(Tau * 3.1f * t));

                return (body + groan) * envelope * 0.8f;
            }, StingLevel * 0.75f);
        }

        // =====================================================================
        // Machinery
        // =====================================================================

        private const float Tau = Mathf.PI * 2f;

        // Target RMS per clip role. These are the mix, expressed once, in the place
        // the waveforms are made — rather than as a pile of volume multipliers
        // scattered through AudioDirector that each have to be re-tuned by ear.
        private const float BedLevel = 0.030f;        // room tone: felt, not heard
        private const float HissLevel = 0.035f;       // monitor snow
        private const float MachineryLevel = 0.075f;  // generator, fan, pump
        private const float OneShotLevel = 0.130f;    // drips, impacts, footfalls
        private const float StingLevel = 0.240f;      // the jumpscare

        private static AudioClip Create(string clipName, float seconds, Func<float, int, float> sample,
            float targetRms = OneShotLevel)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                data[i] = Mathf.Clamp(sample(t, i), -1f, 1f);
            }

            return Finalise(clipName, data, targetRms);
        }

        /// <summary>
        /// Builds a loop whose length is rounded to a whole number of cycles of
        /// <paramref name="fundamental"/>, then crossfades the tail over the head so
        /// the noise components do not click at the seam.
        /// </summary>
        private static AudioClip CreateLoop(string clipName, float seconds, float fundamental,
            Func<float, int, float> sample, float targetRms = MachineryLevel)
        {
            // Snap to whole cycles so the tonal content is continuous across the loop.
            float cycles = Mathf.Max(1f, Mathf.Round(seconds * fundamental));
            float exactSeconds = cycles / fundamental;

            int count = Mathf.Max(1024, Mathf.RoundToInt(exactSeconds * SampleRate));
            var data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                data[i] = Mathf.Clamp(sample(t, i), -1f, 1f);
            }

            int fade = Mathf.Clamp(Mathf.RoundToInt(count * CrossfadeFraction), 1, count / 2);
            for (int i = 0; i < fade; i++)
            {
                // Equal-power crossfade keeps the perceived level flat through the seam.
                float x = i / (float)fade;
                float headGain = Mathf.Sqrt(x);
                float tailGain = Mathf.Sqrt(1f - x);

                int tailIndex = count - fade + i;
                data[i] = data[i] * headGain + data[tailIndex] * tailGain;
            }

            // The tail has been folded into the head, so drop it.
            var trimmed = new float[count - fade];
            Array.Copy(data, trimmed, trimmed.Length);

            return Finalise(clipName, trimmed, targetRms);
        }

        /// <summary>
        /// Normalises to a target RMS rather than a target peak.
        ///
        /// Peak normalisation is the obvious choice and it is wrong here. These
        /// waveforms have wildly different crest factors: a drip is a single
        /// transient with a high peak and almost no energy, while the cave bed is
        /// heavily filtered noise with a low peak and constant energy. Normalising
        /// both to the same peak makes the bed enormously louder than the drip —
        /// which is exactly the wall of static this produced before.
        ///
        /// RMS tracks perceived loudness, so every clip arrives at the mixer at the
        /// level it was meant to have. The peak ceiling then catches anything that
        /// would clip on the way.
        /// </summary>
        private static AudioClip Finalise(string clipName, float[] data,
            float targetRms, float maxPeak = 0.9f)
        {
            double sumOfSquares = 0.0;
            float peak = 0f;

            for (int i = 0; i < data.Length; i++)
            {
                sumOfSquares += (double)data[i] * data[i];
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }

            float rms = data.Length > 0 ? Mathf.Sqrt((float)(sumOfSquares / data.Length)) : 0f;

            if (rms > 1e-5f && peak > 1e-5f)
            {
                float gain = targetRms / rms;

                // Never push the loudest sample past the ceiling.
                gain = Mathf.Min(gain, maxPeak / peak);

                for (int i = 0; i < data.Length; i++) data[i] *= gain;
            }

            var clip = AudioClip.Create(clipName, data.Length, 1, SampleRate, stream: false);
            clip.SetData(data, 0);

            GLog.Verbose(LogChannel.Audio,
                $"Synthesised {clipName}: {data.Length} samples, rms {rms:0.0000} -> {targetRms:0.0000}.");
            return clip;
        }
    }
}
