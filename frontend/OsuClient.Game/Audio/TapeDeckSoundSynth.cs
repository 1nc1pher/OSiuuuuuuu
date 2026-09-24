using System;

namespace OsuClient.Game.Audio
{
    /// <summary>
    /// The create flow's synthesized sounds: everything the tape deck and the
    /// generation screen make, built from the parts a real cassette deck is
    /// made of — plastic, springs, solenoids, a motor, a capstan, tape.
    ///
    /// <para>
    /// The same rules as <see cref="SongSelectSoundSynth"/>: 16-bit PCM WAV
    /// bytes, fixed seeds so each sound is the same every time, and short
    /// enough that a burst of them stays a texture. The one exception is the
    /// transport hum, which is built to loop without a seam.
    /// </para>
    /// </summary>
    public static class TapeDeckSoundSynth
    {
        private const int rate = SynthKit.SampleRate;
        private const double target_peak = 0.8;

        // ------------------------------------------------------------------
        // Handling the tape
        // ------------------------------------------------------------------

        /// <summary>
        /// A cassette going into the bay: plastic sliding in, the door
        /// clacking shut and thudding home, then the reels whirring up to
        /// speed — which is also what the reels on screen do.
        /// </summary>
        public static byte[] TapeInsert()
        {
            const double duration = 0.62;
            const double clack_at = 0.09;
            const double whirr_at = 0.2;

            return render(duration, 1101, (t, noise, f) =>
            {
                // The shell sliding along the bay's rails: a rising rasp.
                double slide = t < clack_at
                    ? f.Band(0).Process(noise, 1200, 1.5) * (t / clack_at) * 0.8
                    : 0;

                double clack = tick(f.Band(1), noise, t - clack_at, 3000, 0.003) * 1.4;
                double thunk = thud(f, noise, t - clack_at - 0.05, 110, 0.04);

                return slide + clack + thunk + whirr(t - whirr_at, 0.4, rising: true) * 0.35;
            });
        }

        /// <summary>
        /// Cover art going in: the paper J-card sliding into the case, then
        /// the lid snapping shut — two quick plastic ticks — and ringing
        /// briefly, as a case does.
        /// </summary>
        public static byte[] CaseClose()
        {
            const double snap_at = 0.12;

            return render(0.26, 2202, (t, noise, f) =>
            {
                double paper = t < snap_at
                    ? f.Band(0).Process(noise, 2500, 0.8) * Math.Sin(Math.PI * t / snap_at) * 0.6
                    : 0;

                double snap = tick(f.Band(1), noise, t - snap_at, 3600, 0.0025) * 1.3
                              + tick(f.Band(2), noise, t - snap_at - 0.02, 3600, 0.002);

                double body = ring(t - snap_at - 0.02, 520, 0.015) * 0.3;

                return paper + snap + body;
            });
        }

        /// <summary>Cover art coming out: the case clicking open, the card sliding free.</summary>
        public static byte[] CaseOpen()
        {
            return render(0.2, 3303, (t, noise, f) =>
            {
                double click = tick(f.Band(0), noise, t, 2800, 0.003) * 1.2;

                double paper = t is > 0.02 and < 0.16
                    ? f.Band(1).Process(noise, 2200, 0.8) * Math.Sin(Math.PI * (t - 0.02) / 0.14) * 0.5
                    : 0;

                return click + paper;
            });
        }

        /// <summary>A difficulty cassette pushed home in the rack: a solid plastic knock.</summary>
        public static byte[] SpineIn()
        {
            return render(0.1, 4404, (t, noise, f) =>
                tick(f.Band(0), noise, t, 1800, 0.004) * 1.3
                + ring(t, 180, 0.02) * 0.5);
        }

        /// <summary>A difficulty cassette pulled out of the rack: lighter, with a short slide.</summary>
        public static byte[] SpineOut()
        {
            return render(0.1, 5505, (t, noise, f) =>
                tick(f.Band(0), noise, t, 2600, 0.003)
                + (t < 0.04 ? f.Band(1).Process(noise, 1600, 1.2) * (1 - t / 0.04) * 0.4 : 0));
        }

        // ------------------------------------------------------------------
        // The transport
        // ------------------------------------------------------------------

        /// <summary>
        /// RECORD going down: a heavier key than the others, the pinch roller
        /// closing on the capstan with a solid clunk, and the motor spinning
        /// the tape up to speed.
        /// </summary>
        public static byte[] RecordEngage()
        {
            return render(0.7, 6606, (t, noise, f) =>
                tick(f.Band(0), noise, t, 2000, 0.003) * 1.4
                + thud(f, noise, t - 0.035, 95, 0.045) * 1.1
                + tick(f.Band(1), noise, t - 0.07, 4200, 0.0015) * 0.5 // the record head settling
                + whirr(t - 0.08, 0.6, rising: true) * 0.5);
        }

        /// <summary>
        /// The transport running, for as long as the generator does: the
        /// motor's hum, the whine of its drive belt with a little wow in it,
        /// and tape hiss. Two seconds long and built to loop with no seam —
        /// every tone completes whole cycles in exactly that time, the wow
        /// included.
        /// </summary>
        public static byte[] TransportLoop()
        {
            const double duration = 2.0;

            return render(duration, 7707, (t, noise, f) =>
            {
                double motor = Math.Sin(2 * Math.PI * 50 * t) * 0.5
                               + Math.Sin(2 * Math.PI * 100 * t) * 0.25
                               + Math.Sin(2 * Math.PI * 150 * t) * 0.1;

                // The belt's whine, with wow: its speed breathing by 0.4%,
                // once per loop. As frequency modulation the phase is the
                // integral of the speed, and the cosine term is back where it
                // started after exactly one loop, so the join is seamless.
                const double wow_depth = 0.004;
                const double wow_rate = 0.5;

                double belt = Math.Sin(2 * Math.PI * 440 * t
                                       - 440 * wow_depth / wow_rate * Math.Cos(2 * Math.PI * wow_rate * t)) * 0.12;

                double hiss = f.Band(0).Process(noise, 6000, 0.5) * 0.18;

                return motor * 0.6 + belt + hiss;
            }, normaliseTo: 0.6);
        }

        /// <summary>
        /// The auto-stop at the end of a run: the key popping back up, and
        /// the motor winding down to a halt with a last thud.
        /// </summary>
        public static byte[] AutoStop()
        {
            return render(0.5, 8808, (t, noise, f) =>
                tick(f.Band(0), noise, t, 2200, 0.003) * 1.2
                + whirr(t - 0.02, 0.35, rising: false) * 0.45
                + thud(f, noise, t - 0.38, 85, 0.05) * 0.6);
        }

        /// <summary>
        /// Fast-forward, for skipping the reveal: the tape shuttling past the
        /// heads, a whine and a rush of hiss that both climb as it speeds up.
        /// </summary>
        public static byte[] FastForward()
        {
            const double duration = 0.45;

            double phase = 0;

            return render(duration, 9909, (t, noise, f) =>
            {
                double u = t / duration;
                double envelope = Math.Sin(Math.PI * Math.Pow(u, 0.7));

                double pitch = 600 + 2400 * u * u;
                phase += 2 * Math.PI * pitch / rate;

                double whine = Math.Sin(phase) * 0.25;
                double rush = f.Band(0).Process(noise, 1000 + 3000 * u, 1.2) * 0.9;

                return (whine + rush) * envelope;
            });
        }

        // ------------------------------------------------------------------
        // The machine at work
        // ------------------------------------------------------------------

        /// <summary>
        /// A relay pulling in: two contacts closing a few milliseconds apart,
        /// with a faint metallic ring. The sound of a stage starting.
        /// </summary>
        public static byte[] Relay()
        {
            return render(0.07, 1212, (t, noise, f) =>
                tick(f.Band(0), noise, t, 4000, 0.001)
                + tick(f.Band(1), noise, t - 0.006, 4000, 0.001) * 0.7
                + ring(t, 2400, 0.008) * 0.2);
        }

        /// <summary>
        /// One digit of a mechanical tape counter rolling over: a soft tick
        /// with a small woody ring. Used for counts — tiers, beats.
        /// </summary>
        public static byte[] CounterTick()
        {
            return render(0.05, 1313, (t, noise, f) =>
                tick(f.Band(0), noise, t, 1500, 0.0025)
                + ring(t, 900, 0.006) * 0.35);
        }

        /// <summary>
        /// A single onset found: the pop of a stylus meeting a transient in
        /// the groove. Pitched per band by the player, so the low end ticks
        /// low and the highs tick high.
        /// </summary>
        public static byte[] OnsetTick()
        {
            return render(0.03, 1414, (t, noise, f) =>
                tick(f.Band(0), noise, t, 2400, 0.0008) * 1.2
                + ring(t, 1800, 0.003) * 0.3);
        }

        /// <summary>
        /// A hit object placed: a short, soft blip — the tone a cassette's
        /// data track makes, which is what a map written to tape would sound
        /// like. Pitched per object kind by the player.
        /// </summary>
        public static byte[] PlaceBlip()
        {
            return render(0.05, 1515, (t, _, _) =>
            {
                double attack = Math.Min(1, t / 0.002);
                double tone = Math.Sin(2 * Math.PI * 880 * t) + 0.3 * Math.Sin(2 * Math.PI * 1760 * t);

                return tone * attack * Math.Exp(-t / 0.012);
            });
        }

        /// <summary>
        /// The map is ready: the two-note beep of an old deck's timer — soft
        /// square-ish tones, rounded off so they sound like a speaker in a
        /// plastic case rather than a computer.
        /// </summary>
        public static byte[] ReadyBeep()
        {
            return render(0.3, 1616, (t, _, f) =>
            {
                double note(double start, double frequency)
                {
                    double s = t - start;

                    if (s < 0 || s > 0.1)
                        return 0;

                    double envelope = Math.Min(1, s / 0.004) * Math.Min(1, (0.1 - s) / 0.02);
                    double square = Math.Sign(Math.Sin(2 * Math.PI * frequency * s));

                    return square * envelope;
                }

                // Low-passed, or it is a PC speaker and not a cassette deck.
                return f.Low(0).Process(note(0, 1320) + note(0.14, 1760), 3000) * 0.9;
            });
        }

        /// <summary>
        /// Something refused: a short, soft double buzz — the dull complaint
        /// of a deck that won't take what it was given. Low and quiet, so it
        /// informs rather than scolds.
        /// </summary>
        public static byte[] Refuse()
        {
            return render(0.26, 1717, (t, _, f) =>
            {
                bool on = t < 0.09 || (t > 0.13 && t < 0.22);

                if (!on)
                    return f.Low(0).Process(0, 900);

                double buzz = Math.Sign(Math.Sin(2 * Math.PI * 110 * t)) * 0.6
                              + Math.Sign(Math.Sin(2 * Math.PI * 165 * t)) * 0.3;

                return f.Low(0).Process(buzz, 900);
            });
        }

        // ------------------------------------------------------------------
        // The analyser's controls
        // ------------------------------------------------------------------

        /// <summary>
        /// One grain of tape being scrubbed across the head by hand: a short
        /// rasp with a pitch in it, the way tape sounds dragged past a
        /// playback head. The player pitches it by how fast the drag is.
        /// </summary>
        public static byte[] Scrub()
        {
            const double duration = 0.07;

            return render(duration, 1818, (t, noise, f) =>
            {
                double u = t / duration;
                double envelope = Math.Sin(Math.PI * u);

                double rasp = f.Band(0).Process(noise, 1400 + 900 * u, 3) * 1.4;
                double tone = Math.Sin(2 * Math.PI * (380 * t + 300 * t * u)) * 0.35;

                return (rasp + tone) * envelope;
            });
        }

        /// <summary>Picking up the playhead's pin: a light latch lifting.</summary>
        public static byte[] PinGrab()
        {
            return render(0.06, 1919, (t, noise, f) =>
                tick(f.Band(0), noise, t, 3400, 0.0015)
                + ring(t, 2100, 0.01) * 0.3);
        }

        /// <summary>Setting the playhead's pin down: a soft click and a small thud.</summary>
        public static byte[] PinDrop()
        {
            return render(0.12, 2020, (t, noise, f) =>
                tick(f.Band(0), noise, t, 1700, 0.003)
                + thud(f, noise, t - 0.008, 140, 0.025) * 0.5);
        }

        /// <summary>
        /// One detent of a rotary knob: the small, dry click of a stepped
        /// potentiometer. Pitched by the player with the knob's position.
        /// </summary>
        public static byte[] KnobDetent()
        {
            return render(0.035, 2121, (t, noise, f) =>
                tick(f.Band(0), noise, t, 2900, 0.0009) * 1.2
                + ring(t, 1300, 0.004) * 0.25);
        }

        /// <summary>A toggle switch flicked: the lever snapping over its spring.</summary>
        public static byte[] SwitchFlick()
        {
            return render(0.07, 2222, (t, noise, f) =>
                tick(f.Band(0), noise, t, 2300, 0.002)
                + tick(f.Band(1), noise, t - 0.012, 3600, 0.0012) * 0.8
                + ring(t - 0.012, 1600, 0.008) * 0.2);
        }

        /// <summary>
        /// Rewind: the tape shuttling back, a whine and a rush that both
        /// fall — the reverse of <see cref="FastForward"/>.
        /// </summary>
        public static byte[] Rewind()
        {
            const double duration = 0.5;

            double phase = 0;

            return render(duration, 2323, (t, noise, f) =>
            {
                double u = t / duration;
                double envelope = Math.Sin(Math.PI * Math.Pow(u, 0.6));

                double pitch = 3000 - 2400 * u;
                phase += 2 * Math.PI * pitch / rate;

                return (Math.Sin(phase) * 0.25 + f.Band(0).Process(noise, 4000 - 3000 * u, 1.2) * 0.9) * envelope;
            });
        }

        // ------------------------------------------------------------------
        // Parts
        // ------------------------------------------------------------------

        /// <summary>Per-sound filter state, so each component keeps its own.</summary>
        private sealed class Filters
        {
            private readonly SynthKit.BandPass[] bands = { new SynthKit.BandPass(), new SynthKit.BandPass(), new SynthKit.BandPass() };
            private readonly SynthKit.LowPass[] lows = { new SynthKit.LowPass(), new SynthKit.LowPass() };

            public SynthKit.BandPass Band(int i) => bands[i];

            public SynthKit.LowPass Low(int i) => lows[i];
        }

        private static byte[] render(double duration, int seed, Func<double, double, Filters, double> sample, double normaliseTo = target_peak)
        {
            int count = (int)(rate * duration);
            var data = new float[count];
            var random = new Random(seed);
            var filters = new Filters();

            for (int i = 0; i < count; i++)
                data[i] = (float)sample(i / (double)rate, random.NextDouble() * 2 - 1, filters);

            SynthKit.Normalise(data, normaliseTo);

            return WavEncoder.Encode(data, rate);
        }

        /// <summary>
        /// A hard-plastic or metal tick at <paramref name="s"/> seconds from
        /// its start: noise through a resonance, decaying fast. Zero before it
        /// starts. The filter runs every sample regardless, so it is settled
        /// when the tick arrives.
        /// </summary>
        private static double tick(SynthKit.BandPass filter, double noise, double s, double centre, double decay)
        {
            double excitation = s >= 0 && s < decay * 8 ? noise * Math.Exp(-s / decay) : 0;
            return filter.Process(excitation, centre, 4) * 3;
        }

        /// <summary>A damped sine: a part ringing after being struck.</summary>
        private static double ring(double s, double frequency, double decay) =>
            s < 0 ? 0 : Math.Sin(2 * Math.PI * frequency * s) * Math.Exp(-s / decay);

        /// <summary>
        /// A mechanical thud: a low tone that drops a little as it settles,
        /// with a dull burst of noise for the mechanism.
        /// </summary>
        private static double thud(Filters f, double noise, double s, double frequency, double decay)
        {
            double dull = f.Low(1).Process(s >= 0 ? noise : 0, 700);

            if (s < 0)
                return 0;

            double pitch = frequency * (1 + 0.5 * Math.Exp(-s / 0.015));

            return Math.Sin(2 * Math.PI * pitch * s) * Math.Exp(-s / decay) * 0.8
                   + dull * Math.Exp(-s / (decay / 3)) * 1.2;
        }

        /// <summary>
        /// A small motor and its gearing spinning up (or down): a whine whose
        /// pitch and level follow the speed, over <paramref name="length"/>
        /// seconds from <paramref name="s"/> = 0.
        /// </summary>
        private static double whirr(double s, double length, bool rising)
        {
            if (s < 0 || s > length)
                return 0;

            double u = s / length;
            double speed = rising ? 1 - Math.Pow(1 - u, 2) : Math.Pow(1 - u, 1.5);

            // Integrated phase of a pitch that follows the speed: 120 Hz at a
            // standstill's edge up to 440 Hz at full speed.
            double phase = 2 * Math.PI * (120 * s + 320 * speed * s * 0.6);

            double envelope = rising ? Math.Min(1, u * 4) * (1 - Math.Pow(u, 6)) : speed;

            return (Math.Sin(phase) + 0.4 * Math.Sin(2 * phase)) * envelope * 0.5;
        }
    }
}
