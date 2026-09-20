using System;
using System.Collections.Generic;
using OsuClient.Game.Beatmaps.HitObjects;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>What a gap between hit objects offers the player while it lasts.</summary>
    public enum BreakKind
    {
        /// <summary>Long enough to skip past outright.</summary>
        Skip,

        /// <summary>Noticeable, but too short to bother skipping — worth a glance at how the play is going instead.</summary>
        Performance,
    }

    /// <summary>One gap in the map with nothing to hit, from where the previous object stops being interactive to where the next one's approach begins.</summary>
    public readonly struct BreakPeriod
    {
        public double Start { get; }
        public double End { get; }
        public BreakKind Kind { get; }

        public BreakPeriod(double start, double end, BreakKind kind)
        {
            Start = start;
            End = end;
            Kind = kind;
        }

        public bool Contains(double time) => time >= Start && time < End;

        /// <summary>How much of the break is still ahead, 1 at the start easing down to 0 at the end.</summary>
        public double RemainingFraction(double time)
        {
            double span = End - Start;

            return span > 0 ? Math.Clamp((End - time) / span, 0, 1) : 0;
        }
    }

    /// <summary>
    /// Finds the gaps in a map with nothing to hit — no formal break markers
    /// exist in this project's <c>.osu</c> files (the decoder skips
    /// <c>[Events]</c> outright, and the backend never writes any), so a break
    /// is derived purely from the spacing the objects themselves leave behind.
    ///
    /// Pure logic, no osu.Framework dependency, so it's tested the same direct
    /// way as the rest of this project's timing maths: set up a map, read the
    /// breaks back.
    /// </summary>
    public static class BreakPeriods
    {
        /// <summary>A gap at least this long offers a skip rather than just a performance readout.</summary>
        public const double SkipThreshold = 5000;

        /// <summary>A gap shorter than this isn't worth announcing at all — it reads as normal spacing between objects, not a break.</summary>
        public const double PerformanceThreshold = 1500;

        /// <summary>
        /// Every break in the map, in order. <paramref name="hitObjects"/> must
        /// already be sorted by <see cref="HitObjectData.StartTime"/>.
        /// </summary>
        public static List<BreakPeriod> Compute(IReadOnlyList<HitObjectData> hitObjects,
                                                 double approachRate, double overallDifficulty)
        {
            var breaks = new List<BreakPeriod>();

            if (hitObjects.Count == 0)
                return breaks;

            double preempt = JudgementProcessor.Preempt(approachRate);
            double mehWindow = JudgementProcessor.MehWindow(overallDifficulty);

            // The track itself starts at 0; anything before the first object's
            // approach is exactly the same shape of gap as one between two
            // later objects, so it's folded into the same loop rather than
            // handled as a special case.
            double previousInteractiveEnd = 0;

            foreach (var hitObject in hitObjects)
            {
                double approachStart = hitObject.StartTime - preempt;
                double gap = approachStart - previousInteractiveEnd;

                if (gap >= PerformanceThreshold)
                {
                    var kind = gap >= SkipThreshold ? BreakKind.Skip : BreakKind.Performance;
                    breaks.Add(new BreakPeriod(previousInteractiveEnd, approachStart, kind));
                }

                // A plain circle stays hittable (and so un-skippable-through)
                // until its miss window closes at StartTime + mehWindow, which
                // for a zero-duration circle is later than its own EndTime —
                // using EndTime alone would open a "break" while the object in
                // front of it could still be pressed.
                previousInteractiveEnd = Math.Max(hitObject.EndTime, hitObject.StartTime + mehWindow);
            }

            return breaks;
        }
    }
}
