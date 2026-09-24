using System;

namespace OsuClient.Game.Audio
{
    /// <summary>
    /// The small set of building blocks the synthesized sound effects are
    /// made from: a resonant band-pass, a one-pole low-pass, and peak
    /// normalisation. Shared so each synth file says what its sounds are, not
    /// how a filter works.
    /// </summary>
    internal static class SynthKit
    {
        public const int SampleRate = 44100;

        /// <summary>
        /// A resonant band-pass (the RBJ cookbook biquad, constant 0 dB peak)
        /// whose centre can move every sample. Cascaded one-poles get away
        /// with a whoosh; a click or a stylus needs an actual resonance, or it
        /// is just hiss.
        /// </summary>
        public sealed class BandPass
        {
            private double x1, x2, y1, y2;

            public double Process(double input, double centre, double q)
            {
                double w = 2 * Math.PI * centre / SampleRate;
                double alpha = Math.Sin(w) / (2 * q);
                double cos = Math.Cos(w);

                double a0 = 1 + alpha;
                double b0 = alpha / a0;
                double b2 = -alpha / a0;
                double a1 = -2 * cos / a0;
                double a2 = (1 - alpha) / a0;

                double output = b0 * input + b2 * x2 - a1 * y1 - a2 * y2;

                x2 = x1;
                x1 = input;
                y2 = y1;
                y1 = output;

                return output;
            }
        }

        /// <summary>A one-pole low-pass: the cheap way to dull noise into a thud.</summary>
        public sealed class LowPass
        {
            private double state;

            public double Process(double input, double cutoff)
            {
                state += (1 - Math.Exp(-2 * Math.PI * cutoff / SampleRate)) * (input - state);
                return state;
            }
        }

        /// <summary>
        /// Scales the buffer so its loudest sample sits at
        /// <paramref name="target"/>. Every filter's level depends on its
        /// constants; a hand-tuned gain goes quietly wrong the next time one
        /// changes.
        /// </summary>
        public static void Normalise(float[] data, double target)
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
