using System;
using OsuClient.Game.Beatmaps;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// Shared pure maths behind every beat-synced ambient effect (the side
    /// flash, the background's heartbeat zoom): where "now" sits within the
    /// current beat, and how strong an effect tied to that beat should be.
    ///
    /// No osu.Framework dependency, so it's tested the same direct way as the
    /// rest of this project's timing maths — set a time, read the result.
    /// </summary>
    public static class BeatPulse
    {
        /// <summary>
        /// How far <paramref name="time"/> is through the current beat: 0
        /// exactly on a beat, approaching 1 right before the next one. NaN if
        /// the beatmap carries no timing point to measure against.
        /// </summary>
        public static double PhaseAt(Beatmap beatmap, double time)
        {
            var timingPoint = beatmap.PrimaryTimingPoint;

            if (timingPoint == null)
                return double.NaN;

            double beatLength = beatmap.BeatLengthAt(time);
            double sincePoint = time - timingPoint.Time;

            // C#'s % can return a negative remainder for a negative dividend
            // (time before the timing point, during the lead-in); folding it
            // back into [0, beatLength) keeps the phase meaningful there too.
            return (sincePoint % beatLength + beatLength) % beatLength / beatLength;
        }

        /// <summary>
        /// A smooth pulse from a beat phase: 1 exactly on the beat, easing to
        /// 0 by the midpoint between beats and back up again for the next one.
        /// Raised to <paramref name="sharpness"/> to narrow a plain cosine
        /// (which spends half of every beat above half strength) into
        /// something felt as a moment "at" the beat rather than a slow
        /// breathing wave — the higher the sharpness, the briefer the pulse.
        /// </summary>
        public static double IntensityAt(double phase, double sharpness)
        {
            if (double.IsNaN(phase))
                return 0;

            double raised = 0.5 + 0.5 * Math.Cos(2 * Math.PI * phase);
            return Math.Pow(raised, sharpness);
        }

        /// <summary>
        /// 0 outside the beatmap's playable span, 1 across the middle of it,
        /// and a short linear crossfade at each end, so a beat effect doesn't
        /// appear or vanish abruptly right at the first or last hit object.
        /// </summary>
        public static double PlayableEnvelope(Beatmap beatmap, double time, double fadeDuration = 500)
        {
            double start = beatmap.FirstHitObjectTime;
            double end = beatmap.LastHitObjectTime;

            if (end <= start)
                return 0;

            double fadeIn = Math.Clamp((time - start) / fadeDuration + 1, 0, 1);
            double fadeOut = Math.Clamp((end - time) / fadeDuration + 1, 0, 1);

            return Math.Min(fadeIn, fadeOut);
        }
    }
}
