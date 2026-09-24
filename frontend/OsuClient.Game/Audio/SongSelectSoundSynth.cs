using System;

namespace OsuClient.Game.Audio
{
    /// <summary>
    /// Song select's synthesized sounds, generated like
    /// <see cref="MenuSoundSynth"/> as 16-bit PCM WAV bytes: the record being
    /// turned, the cassette sticker being clicked into, and a difficulty
    /// cassette's deck key going down.
    ///
    /// <para>
    /// Everything here is physical and short. The screen is a turntable and a
    /// tape deck, so the sounds are what those make when handled — a scratch
    /// and a detent click, a plastic case, a sprung piano key — not interface
    /// blips, and each is over within a sixth of a second so a fast spin or a
    /// run of clicks stays a texture rather than a queue.
    /// </para>
    /// </summary>
    public static class SongSelectSoundSynth
    {
        private const int sample_rate = 44100;

        private const double target_peak = 0.8;

        /// <summary>
        /// One notch of the record turning: a short scratch whose pitch sweeps
        /// the way the record moves, over the click of the platter's detent,
        /// a few crackle pops, and a low thump for the platter's weight.
        /// </summary>
        /// <param name="forward">Sweeps up when true, down when false, so turning back sounds like turning back.</param>
        public static byte[] VinylTurn(bool forward)
        {
            const double duration = 0.12;

            int samples = (int)(sample_rate * duration);
            var data = new float[samples];

            // Fixed seeds, per direction: the same notch every time, so a spin
            // has a character rather than being fresh noise on each step.
            var random = new Random(forward ? 3131 : 3132);

            var scratch = new SynthKit.BandPass();

            for (int i = 0; i < samples; i++)
            {
                double t = i / (double)sample_rate;
                double u = t / duration;

                // The scratch: noise through a resonant band-pass sweeping
                // between 900 Hz and 2.6 kHz — the stylus dragged through the
                // groove. Four-millisecond attack, then a quick decay.
                double centre = forward ? 900 + 1700 * u : 2600 - 1700 * u;
                double noise = random.NextDouble() * 2 - 1;
                double attack = Math.Min(1, t / 0.004);
                double scratchPart = scratch.Process(noise, centre, 2.5) * attack * Math.Exp(-t / 0.035) * 1.6;

                // The detent: a two-millisecond bright click right at the start.
                double click = t < 0.002 ? (random.NextDouble() * 2 - 1) * Math.Exp(-t / 0.0006) * 0.7 : 0;

                // The platter's weight, felt rather than heard.
                double thump = Math.Sin(2 * Math.PI * 70 * t) * Math.Exp(-t / 0.025) * 0.3;

                data[i] = (float)(scratchPart + click + thump);
            }

            // Crackle: a few single pops scattered through the notch, the
            // surface noise that makes it a record rather than a filter sweep.
            for (int pop = 0; pop < 3; pop++)
            {
                int at = (int)(random.NextDouble() * samples * 0.85);
                float strength = (float)(0.25 + random.NextDouble() * 0.2) * (random.Next(2) == 0 ? 1 : -1);

                for (int k = 0; k < 6 && at + k < samples; k++)
                    data[at + k] += strength * (float)Math.Exp(-k / 1.5);
            }

            normalise(data, target_peak);

            return WavEncoder.Encode(data, sample_rate);
        }

        /// <summary>
        /// The search sticker clicked into: the two-part plastic clack of a
        /// cassette case shutting, over a small hollow body resonance.
        /// </summary>
        public static byte[] SearchClick()
        {
            const double duration = 0.12;
            const double second_click = 0.035;

            int samples = (int)(sample_rate * duration);
            var data = new float[samples];
            var random = new Random(5151);

            var tickFilter = new SynthKit.BandPass();

            for (int i = 0; i < samples; i++)
            {
                double t = i / (double)sample_rate;
                double noise = random.NextDouble() * 2 - 1;

                // Two ticks of hard plastic, the second softer — the lid
                // landing, then seating.
                double ticks = 0;

                if (t < 0.012)
                    ticks += Math.Exp(-t / 0.003) * 0.9;

                if (t >= second_click && t < second_click + 0.012)
                    ticks += Math.Exp(-(t - second_click) / 0.003) * 0.6;

                double plastic = tickFilter.Process(noise * ticks, 3200, 4) * 3;

                // The case itself ringing briefly once it has shut.
                double body = 0;

                if (t >= second_click)
                {
                    double s = t - second_click;
                    body = Math.Sin(2 * Math.PI * 420 * s) * Math.Exp(-s / 0.018) * 0.35
                           + Math.Sin(2 * Math.PI * 160 * s) * Math.Exp(-s / 0.03) * 0.25;
                }

                data[i] = (float)(plastic + body);
            }

            normalise(data, target_peak);

            return WavEncoder.Encode(data, sample_rate);
        }

        /// <summary>
        /// A difficulty cassette's deck key going down: the "ka-chunk" of a
        /// tape deck's piano key — a sharp latch, a brief ring from its
        /// spring, then the low mechanical thunk of the transport engaging.
        /// </summary>
        public static byte[] DeckKey()
        {
            const double duration = 0.16;
            const double chunk_at = 0.028;

            int samples = (int)(sample_rate * duration);
            var data = new float[samples];
            var random = new Random(7171);

            var latchFilter = new SynthKit.BandPass();
            double thunkNoise = 0;
            double thunkPhase = 0;

            for (int i = 0; i < samples; i++)
            {
                double t = i / (double)sample_rate;
                double noise = random.NextDouble() * 2 - 1;

                // "Ka": the latch snapping over.
                double latch = latchFilter.Process(noise, 2200, 3) * Math.Exp(-t / 0.0025) * 2.4;

                // The key's spring, ringing for a moment — two slightly
                // inharmonic partials, as metal does.
                double spring = Math.Sin(2 * Math.PI * 1350 * t) * Math.Exp(-t / 0.012) * 0.25
                                + Math.Sin(2 * Math.PI * 1720 * t) * Math.Exp(-t / 0.009) * 0.12;

                // "Chunk": the transport engaging a beat later, a pitch that
                // drops as it settles, with a little dull noise for the
                // mechanism.
                double chunk = 0;

                if (t >= chunk_at)
                {
                    double s = t - chunk_at;
                    double frequency = 90 + 60 * Math.Exp(-s / 0.02);

                    thunkPhase += 2 * Math.PI * frequency / sample_rate;
                    thunkNoise += (1 - Math.Exp(-2 * Math.PI * 600.0 / sample_rate)) * (noise - thunkNoise);

                    chunk = Math.Sin(thunkPhase) * Math.Exp(-s / 0.035) * 0.7
                            + thunkNoise * Math.Exp(-s / 0.01) * 0.9;
                }

                data[i] = (float)(latch + spring + chunk);
            }

            normalise(data, target_peak);

            return WavEncoder.Encode(data, sample_rate);
        }

        /// <summary>
        /// The needle going down on the record — for opening the analyser,
        /// which is putting the song under the stylus to look at closely: the
        /// arm's soft thump, the stylus landing in the groove, and a second
        /// of surface crackle as it settles in.
        /// </summary>
        public static byte[] NeedleDrop()
        {
            const double duration = 0.55;
            const double land_at = 0.04;

            int samples = (int)(sample_rate * duration);
            var data = new float[samples];
            var random = new Random(8181);

            var landing = new SynthKit.BandPass();
            var body = new SynthKit.LowPass();

            for (int i = 0; i < samples; i++)
            {
                double t = i / (double)sample_rate;
                double noise = random.NextDouble() * 2 - 1;

                // The arm settling onto its rest: a low, dull thump.
                double thump = Math.Sin(2 * Math.PI * 60 * t) * Math.Exp(-t / 0.05) * 0.7
                               + body.Process(t < 0.03 ? noise : 0, 400) * 1.5;

                // The stylus meeting the groove: a bright little strike.
                double s = t - land_at;
                double strike = s >= 0 ? landing.Process(noise * Math.Exp(-s / 0.002), 2600, 3) * 2.5 : 0;

                // The groove's hiss once it is in, fading.
                double hiss = s >= 0 ? noise * 0.05 * Math.Exp(-s / 0.25) : 0;

                data[i] = (float)(thump + strike + hiss);
            }

            // Surface crackle as it settles: a few pops, thinning out.
            for (int pop = 0; pop < 9; pop++)
            {
                int at = (int)(sample_rate * (land_at + 0.02 + random.NextDouble() * random.NextDouble() * 0.45));
                float strength = (float)(0.15 + random.NextDouble() * 0.25) * (random.Next(2) == 0 ? 1 : -1);

                for (int k = 0; k < 6 && at + k < samples; k++)
                    data[at + k] += strength * (float)Math.Exp(-k / 1.5);
            }

            normalise(data, target_peak);

            return WavEncoder.Encode(data, sample_rate);
        }

        private static void normalise(float[] data, double target) => SynthKit.Normalise(data, target);
    }
}
