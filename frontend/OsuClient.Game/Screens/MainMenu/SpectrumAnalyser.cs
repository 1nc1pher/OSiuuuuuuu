using System;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// Turns osu.Framework's raw FFT bins into something worth drawing
    /// (MENU_REDESIGN_PLAN.md step 6a).
    ///
    /// <c>Track.CurrentAmplitudes.FrequencyAmplitudes</c> hands out 256 bins
    /// spread linearly across 0–20 kHz, which is the wrong shape for a
    /// visualiser three times over: music lives in the bottom eighth of that
    /// range, so a linear ring is empty for most of its circumference; the
    /// values swing over orders of magnitude, so a linear height is either all
    /// floor or all ceiling; and consecutive frames differ enough that
    /// untreated bars strobe rather than move.
    ///
    /// This fixes all three — log-spaced grouping, a perceptual curve, and a
    /// decay floor so bars fall smoothly — and does it with no framework
    /// dependency, so the behaviour is pinned by ordinary unit tests rather
    /// than by squinting at a running window.
    /// </summary>
    public class SpectrumAnalyser
    {
        /// <summary>
        /// Lowest bin used. Bin 0 is DC plus whatever rumble is below hearing;
        /// including it puts a permanent spike at the start of the ring.
        /// </summary>
        public const int MinBin = 1;

        /// <summary>
        /// Highest bin used. At ~78 Hz per bin this is about 10 kHz — above
        /// that, music has almost nothing in it, and the ring would just carry
        /// a dead arc.
        /// </summary>
        public const int MaxBin = 128;

        /// <summary>
        /// Raw amplitudes are small — a loud mix rarely pushes a single bin
        /// far past a tenth. Lifted here before the curve so the ring uses its
        /// full height instead of trembling along the bottom.
        ///
        /// Tuned against real captures rather than derived: at 9 the ring sat
        /// pinned at full height through an ordinary chorus, which is a
        /// circle, not a spectrum.
        /// </summary>
        private const float gain = 5f;

        /// <summary>
        /// Below 1, so quiet detail is stretched and loud detail compressed —
        /// the same reason level meters are drawn in dB rather than linearly.
        /// </summary>
        private const float curve = 0.65f;

        /// <summary>
        /// Per-band tilt: how hard high frequencies are lifted relative to
        /// low ones, as an exponent on the band's centre frequency.
        ///
        /// Music is not flat — it falls off steeply with frequency, so an
        /// untilted ring is a wall of bass with a dead arc of treble beside
        /// it, all its motion crowded into one quarter of the circle. This
        /// is the standard fix: bands are weighted by frequency so a typical
        /// mix drives the whole ring to a similar height, and what's left to
        /// see is the music moving rather than the shape of the spectrum.
        /// </summary>
        private const float tilt = 0.55f;

        /// <summary>
        /// Band whose gain the tilt leaves alone — roughly 1.2 kHz. Bands
        /// below it are pulled down, bands above it pushed up.
        ///
        /// Tilt and pivot were both softened after a first pass at 0.75/24
        /// over-corrected: the bass end came out quieter than everything
        /// else, which just moved the ring's bald patch from the treble to
        /// the top of the circle instead of removing it.
        /// </summary>
        private const float tilt_pivot_bin = 16f;

        /// <summary>
        /// Fraction of a bar's height that survives one second of silence.
        /// Small, so a bar drops quickly, but not instantly — an undecayed
        /// ring flickers at the frame rate.
        /// </summary>
        private const float decay_per_second = 0.02f;

        /// <summary>
        /// Fraction of the columns counted as the low end for
        /// <see cref="LowEnergy"/> — the bottom eighth of the log-spaced
        /// range, which is roughly kick and bass.
        /// </summary>
        private const float low_end_fraction = 0.12f;

        private readonly float[] values;
        private readonly int[] groupStart;
        private readonly int[] groupEnd;

        /// <summary>Per-band tilt weighting, precomputed — see <see cref="tilt"/>.</summary>
        private readonly float[] groupGain;

        /// <summary>Smoothed column heights, 0–1, one per column.</summary>
        public ReadOnlySpan<float> Values => values;

        /// <summary>
        /// How much is going on in the low end right now, 0–1 — the mean of
        /// the bottom <see cref="low_end_fraction"/> of the columns.
        ///
        /// Exists so the menu's beat pulse can be *modulated* by how loud the
        /// song currently is without growing a second envelope follower: this
        /// is already smoothed and decayed, which a raw
        /// <c>CurrentAmplitudes.Average</c> is not.
        /// </summary>
        public float LowEnergy { get; private set; }

        public int Columns => values.Length;

        public SpectrumAnalyser(int columns = 128)
        {
            if (columns < 1)
                throw new ArgumentOutOfRangeException(nameof(columns), "a spectrum needs at least one column");

            values = new float[columns];
            groupStart = new int[columns];
            groupEnd = new int[columns];
            groupGain = new float[columns];

            buildGroups();
        }

        /// <summary>
        /// Which bins each column covers, spaced logarithmically.
        ///
        /// Precomputed because the mapping never changes, and because the
        /// clamping below — every group is forced to hold at least one bin —
        /// is easier to reason about once than per frame. Without it the first
        /// columns, whose ideal width is a fraction of a bin, would be empty.
        /// </summary>
        private void buildGroups()
        {
            double ratio = (double)MaxBin / MinBin;

            for (int i = 0; i < values.Length; i++)
            {
                int start = (int)Math.Round(MinBin * Math.Pow(ratio, i / (double)values.Length));
                int end = (int)Math.Round(MinBin * Math.Pow(ratio, (i + 1) / (double)values.Length));

                start = Math.Clamp(start, MinBin, MaxBin);
                end = Math.Clamp(end, start + 1, MaxBin + 1);

                groupStart[i] = start;
                groupEnd[i] = end;

                // Weighted by the band's geometric centre, which is its real
                // midpoint on a log axis — the arithmetic mean would bias
                // every band towards its top edge.
                float centre = MathF.Sqrt(start * (float)end);
                groupGain[i] = MathF.Pow(centre / tilt_pivot_bin, tilt);
            }
        }

        /// <summary>
        /// Folds one frame of FFT data into the column heights.
        ///
        /// <paramref name="bins"/> is read once, immediately, and never
        /// retained — the array a track hands out is mutated in place on the
        /// audio thread.
        /// </summary>
        /// <param name="bins">Raw frequency amplitudes. Shorter or empty input is tolerated.</param>
        /// <param name="elapsedMs">Time since the last update, for the decay.</param>
        public void Update(ReadOnlySpan<float> bins, double elapsedMs)
        {
            float falloff = decayFactor(elapsedMs);

            for (int i = 0; i < values.Length; i++)
            {
                float peak = peakOf(bins, groupStart[i], groupEnd[i]);
                float target = shape(peak * groupGain[i]);

                // A bar jumps to a new peak at once and only ever falls
                // slowly: rising with the same smoothing would blunt exactly
                // the transients that make the ring look like it's listening.
                values[i] = MathF.Max(target, values[i] * falloff);
            }

            smooth();
            updateLowEnergy();
        }

        /// <summary>
        /// Drops every column to zero — used when playback stops, so the ring
        /// doesn't freeze mid-shape.
        /// </summary>
        public void Reset()
        {
            Array.Clear(values);
            LowEnergy = 0;
        }

        private void updateLowEnergy()
        {
            int count = Math.Max(1, (int)MathF.Round(values.Length * low_end_fraction));

            float total = 0;

            for (int i = 0; i < count; i++)
                total += values[i];

            LowEnergy = total / count;
        }

        private static float decayFactor(double elapsedMs)
        {
            if (!(elapsedMs > 0))
                return 1;

            // Framerate-independent: the same fraction is lost per second of
            // wall clock however often this is called.
            return MathF.Pow(decay_per_second, (float)(elapsedMs / 1000));
        }

        private static float peakOf(ReadOnlySpan<float> bins, int start, int end)
        {
            float peak = 0;

            for (int bin = start; bin < end && bin < bins.Length; bin++)
            {
                float value = bins[bin];

                // Guards the whole pipeline against a bad frame: a NaN here
                // would otherwise propagate into a column height and stay
                // there for good, since NaN survives both Max and the decay.
                if (float.IsFinite(value) && value > peak)
                    peak = value;
            }

            return peak;
        }

        private static float shape(float raw) =>
            MathF.Pow(Math.Clamp(raw * gain, 0, 1), curve);

        /// <summary>
        /// One pass of 0.25/0.5/0.25 blur along the ring, so neighbouring
        /// columns lean on each other. Grouped bins are independent, which
        /// looks like a comb; this makes the ring read as one curve.
        /// </summary>
        private void smooth()
        {
            if (values.Length < 3)
                return;

            float previous = values[0];

            for (int i = 1; i < values.Length - 1; i++)
            {
                float current = values[i];

                values[i] = previous * 0.25f + current * 0.5f + values[i + 1] * 0.25f;

                // The unsmoothed value, kept so the next column blurs against
                // the original rather than against an already-blurred one.
                previous = current;
            }
        }
    }
}
