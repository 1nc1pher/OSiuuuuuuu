using System;
using System.Collections.Generic;

namespace OsuClient.Game.Backend
{
    /// <summary>
    /// The backend's onset peak-picker, ported to C# so the client can re-run
    /// detection over the exported flux curve at sensitivities the backend
    /// never used (GENERATION_REDESIGN_PLAN.md step 5).
    ///
    /// This exists for one feature: two knobs, <c>margin</c> and
    /// <c>delta</c>, that redraw the threshold and recount the onsets live.
    /// That is only worth showing if the numbers it produces are the numbers
    /// the backend would have produced — otherwise it is a demonstration of
    /// nothing, told convincingly. So the port is line-for-line from
    /// <c>src/onset/detector.py</c>, quirks included, and a test asserts it
    /// reproduces a real generated map's onset list exactly.
    ///
    /// Three details are load-bearing and easy to "tidy" into wrongness:
    ///
    /// <list type="bullet">
    /// <item>The local-maximum test is strict on the left and loose on the
    /// right (<c>flux[i] &gt; flux[i-1] &amp;&amp; flux[i] &gt;= flux[i+1]</c>).
    /// Making both strict drops peaks on a flat plateau; making both loose
    /// picks every sample of one.</item>
    /// <item>Within the minimum spacing the picker keeps the *stronger* of
    /// two candidates, replacing the one it already kept rather than skipping
    /// the new one.</item>
    /// <item>Seconds convert to frames by <b>ceiling</b>, not truncation.
    /// Truncating would round the spacing requirement down and quietly
    /// enforce a shorter gap than asked for — the Python has a comment saying
    /// exactly this, and the mistake is invisible without one.</item>
    /// </list>
    /// </summary>
    public static class OnsetPeakPicker
    {
        /// <summary>
        /// The adaptive threshold at every frame:
        /// <c>delta + margin × median[n]</c>, the standard form from Bello et
        /// al. (2005).
        ///
        /// The moving median itself is computed by the backend and shipped in
        /// <c>dsp.json</c>, because it is the only part of the threshold that
        /// doesn't depend on the difficulty preset. That is what makes the
        /// knobs possible: one curve in the file, every threshold derivable
        /// from it.
        ///
        /// <c>delta</c> matters more than it looks. In near-silent passages
        /// the local median collapses toward zero and a purely multiplicative
        /// threshold collapses with it, so noise starts registering as onsets;
        /// the additive floor is what keeps silence silent.
        /// </summary>
        public static float[] Threshold(IReadOnlyList<float> median, double margin, double delta)
        {
            var threshold = new float[median.Count];

            for (int i = 0; i < median.Count; i++)
                threshold[i] = (float)(delta + median[i] * margin);

            return threshold;
        }

        /// <summary>
        /// Converts a minimum spacing in seconds to whole frames, rounding
        /// up and never below one.
        /// </summary>
        public static int SpacingInFrames(double minSpacingSeconds, double frameRate)
        {
            if (frameRate <= 0)
                return 1;

            return Math.Max(1, (int)Math.Ceiling(minSpacingSeconds * frameRate));
        }

        /// <summary>
        /// Frame indices of the picked onsets: local maxima of
        /// <paramref name="flux"/> that clear <paramref name="threshold"/>,
        /// no two closer than <paramref name="minSpacingFrames"/>.
        /// </summary>
        public static IReadOnlyList<int> Pick(IReadOnlyList<float> flux,
                                              IReadOnlyList<float> threshold,
                                              int minSpacingFrames)
        {
            var kept = new List<int>();

            if (flux == null || threshold == null || flux.Count < 3)
                return kept;

            // A threshold shorter than the flux would silently stop picking
            // partway through the song; treat the pair as unusable instead.
            if (threshold.Count < flux.Count)
                return kept;

            minSpacingFrames = Math.Max(1, minSpacingFrames);

            for (int i = 1; i < flux.Count - 1; i++)
            {
                if (flux[i] <= threshold[i])
                    continue;

                // Strict on the left, loose on the right. See the class
                // remarks — this asymmetry is deliberate.
                if (!(flux[i] > flux[i - 1] && flux[i] >= flux[i + 1]))
                    continue;

                if (kept.Count == 0)
                {
                    kept.Add(i);
                    continue;
                }

                int last = kept[^1];

                if (i - last >= minSpacingFrames)
                    kept.Add(i);
                else if (flux[i] > flux[last])
                    kept[^1] = i;     // too close, but stronger: it takes the slot
            }

            return kept;
        }

        /// <summary>
        /// The whole pass at one sensitivity, straight off a loaded
        /// <see cref="DspDetail"/>: derive the threshold from the exported
        /// median, then pick over the exported flux.
        /// </summary>
        public static IReadOnlyList<int> Pick(DspDetail detail, double margin, double delta)
        {
            if (detail == null)
                return Array.Empty<int>();

            float[] flux = detail.Samples("flux");
            float[] threshold = detail.ThresholdFor(margin, delta);

            int spacing = SpacingInFrames(detail.Detection.MinSpacingSec,
                                          detail.Frames.FrameRate);

            return Pick(flux, threshold, spacing);
        }

        /// <summary>The same, at a named difficulty's own sensitivity.</summary>
        public static IReadOnlyList<int> PickForTier(DspDetail detail, string tier)
        {
            if (detail == null || !detail.Detection.Tiers.TryGetValue(tier, out var sensitivity))
                return Array.Empty<int>();

            return Pick(detail, sensitivity.Margin, sensitivity.Delta);
        }

        /// <summary>Picked frames as times in seconds, on the document's own axis.</summary>
        public static IReadOnlyList<double> PickTimes(DspDetail detail, double margin, double delta)
        {
            var frames = Pick(detail, margin, delta);
            var times = new double[frames.Count];

            for (int i = 0; i < frames.Count; i++)
                times[i] = detail.Frames.TimeAt(frames[i]);

            return times;
        }
    }
}
