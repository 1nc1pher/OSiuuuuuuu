using System;
using System.IO;

namespace OsuClient.Game.Audio
{
    /// <summary>
    /// Procedurally generates the two hitsounds gameplay plays, as short
    /// 16-bit PCM WAV byte arrays. There's no bundled sample here — nothing
    /// to license, nothing to keep track of — just synthesis at first use.
    /// </summary>
    public static class HitSoundSynth
    {
        private const int sample_rate = 44100;

        /// <summary>
        /// The main hit sound: a kick-drum thump — a fast downward pitch
        /// sweep under an exponential decay envelope, the standard way to
        /// synthesize a kick, plus a short noise burst for the attack click.
        /// </summary>
        public static byte[] Kick()
        {
            const double duration = 0.15;
            const double startFrequency = 150;
            const double endFrequency = 45;
            const double decay = 18;

            int samples = (int)(sample_rate * duration);
            var data = new float[samples];
            var random = new Random(1);

            double phase = 0;

            for (int i = 0; i < samples; i++)
            {
                double t = i / (double)sample_rate;
                double envelope = Math.Exp(-decay * t);

                // Sweeping the instantaneous frequency down (rather than
                // just modulating a fixed tone) is what gives a synthesized
                // kick its characteristic pitched "thump" instead of a beep.
                double frequency = endFrequency + (startFrequency - endFrequency) * Math.Exp(-30 * t);
                phase += frequency / sample_rate;

                double tone = Math.Sin(2 * Math.PI * phase);
                double click = t < 0.004 ? (random.NextDouble() * 2 - 1) * (1 - t / 0.004) : 0;

                data[i] = (float)(envelope * (tone * 0.85 + click * 0.5));
            }

            return WavEncoder.Encode(data, sample_rate);
        }

        /// <summary>
        /// The slider-tick sound: a short, quiet, high-pitched click —
        /// distinct from the main hit so ticks read as lighter feedback.
        /// </summary>
        public static byte[] Tick()
        {
            const double duration = 0.05;
            const double frequency = 1400;
            const double decay = 60;

            int samples = (int)(sample_rate * duration);
            var data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                double t = i / (double)sample_rate;
                double envelope = Math.Exp(-decay * t);

                data[i] = (float)(envelope * Math.Sin(2 * Math.PI * frequency * t) * 0.35);
            }

            return WavEncoder.Encode(data, sample_rate);
        }
    }

    /// <summary>Writes mono float samples as a 16-bit PCM WAV.</summary>
    internal static class WavEncoder
    {
        public static byte[] Encode(float[] samples, int sampleRate)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            int byteRate = sampleRate * 2;
            int dataSize = samples.Length * 2;

            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + dataSize);
            writer.Write("WAVE"u8.ToArray());

            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)2); // block align
            writer.Write((short)16); // bits per sample

            writer.Write("data"u8.ToArray());
            writer.Write(dataSize);

            foreach (float sample in samples)
                writer.Write((short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue));

            return stream.ToArray();
        }
    }
}
