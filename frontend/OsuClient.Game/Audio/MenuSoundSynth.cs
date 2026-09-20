using System;

namespace OsuClient.Game.Audio
{
    /// <summary>
    /// The main menu's own synthesized sounds, generated the same way
    /// <see cref="HitSoundSynth"/> generates the gameplay hitsounds — as
    /// 16-bit PCM WAV bytes at first use, with no sample file to ship or
    /// license.
    /// </summary>
    public static class MenuSoundSynth
    {
        private const int sample_rate = 44100;

        /// <summary>
        /// The whoosh the strip opens with: filtered noise under a swelling
        /// envelope, with its filter sweeping up and back down.
        ///
        /// A whoosh is noise, not a tone — what makes it read as movement
        /// rather than as static is the sweep. The cutoff rises through the
        /// first half and falls through the second, so the sound seems to
        /// travel past and away, and the band-pass keeps it airy instead of
        /// rumbling underneath the music that's already playing.
        ///
        /// The low-pass is two poles, not one. A single pole rolls off at
        /// only 6 dB an octave, which left so much energy above the cutoff
        /// that the result measured at a 7-9 kHz spectral centroid — audibly
        /// a hiss rather than a whoosh, whatever the cutoff was set to.
        /// Cascading two stages and dropping the peak cutoff to 1.8 kHz puts
        /// the centroid where it belongs, sweeping about 1.9 to 3.7 kHz and
        /// back.
        /// </summary>
        public static byte[] Whoosh()
        {
            const double duration = 0.62;
            const double low_cutoff = 220;
            const double peak_cutoff = 1800;

            // Rumble the band-pass removes, so the whoosh sits above the
            // music rather than fighting its bass.
            const double rumble_cutoff = 150;

            // Headroom below full scale after normalisation.
            const double target_peak = 0.85;

            int samples = (int)(sample_rate * duration);
            var data = new float[samples];

            // Fixed seed: the same whoosh every launch, so it's a sound the
            // app has rather than a different hiss each time.
            var random = new Random(4242);

            double stageOne = 0;
            double stageTwo = 0;
            double rumble = 0;

            for (int i = 0; i < samples; i++)
            {
                double t = i / (double)sample_rate;
                double u = t / duration;

                // A hump, not a decay: it swells in and fades out, which is
                // what makes it a whoosh rather than a hi-hat.
                double envelope = Math.Sin(Math.PI * Math.Pow(u, 0.75));

                double cutoff = low_cutoff + (peak_cutoff - low_cutoff) * Math.Sin(Math.PI * u);

                double noise = random.NextDouble() * 2 - 1;

                // Two one-pole low-pass stages in series, then a much slower
                // one subtracted from the result: the difference is a
                // band-pass, which is the cheap way to sweep a filter without
                // writing a biquad.
                double coefficient = 1 - Math.Exp(-2 * Math.PI * cutoff / sample_rate);

                stageOne += coefficient * (noise - stageOne);
                stageTwo += coefficient * (stageOne - stageTwo);

                rumble += (1 - Math.Exp(-2 * Math.PI * rumble_cutoff / sample_rate)) * (stageTwo - rumble);

                data[i] = (float)(envelope * (stageTwo - rumble));
            }

            normalise(data, target_peak);

            return WavEncoder.Encode(data, sample_rate);
        }

        /// <summary>
        /// Scales the whole buffer so its loudest sample sits at
        /// <paramref name="target"/>.
        ///
        /// The cascaded filter's output level depends on every constant
        /// above, so the alternative is a hand-tuned gain that quietly
        /// becomes wrong the next time one of them is touched.
        /// </summary>
        private static void normalise(float[] data, double target)
        {
            float peak = 0;

            foreach (float sample in data)
                peak = Math.Max(peak, Math.Abs(sample));

            if (peak <= 0)
                return;

            float scale = (float)(target / peak);

            for (int i = 0; i < data.Length; i++)
                data[i] *= scale;
        }
    }
}
