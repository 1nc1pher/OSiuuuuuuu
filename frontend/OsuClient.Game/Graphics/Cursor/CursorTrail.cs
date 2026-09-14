using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics.Cursor
{
    /// <summary>
    /// The comet tail behind <see cref="GameCursor"/>: a short trail of
    /// circles laid down along the path the cursor actually travelled,
    /// tapering in size and alpha toward the tail.
    ///
    /// Samples are placed at a fixed distance apart *along the movement
    /// segment*, not once per frame. A frame where the cursor jumps 300px
    /// would otherwise leave a single dot 300px from the last one, so the
    /// trail would visibly break up exactly when it's moving fast enough to
    /// matter; walking the segment keeps it continuous at any speed.
    ///
    /// Length is capped by sample count rather than by time, which bounds how
    /// far the tail can stretch no matter how fast the cursor moves — a
    /// time-based fade alone would let a quick flick smear a long streak
    /// across the screen and cover what's underneath.
    /// </summary>
    public partial class CursorTrail : CompositeDrawable
    {
        /// <summary>Distance along the path between successive samples.</summary>
        private const float sample_spacing = 2.5f;

        /// <summary>
        /// Samples kept behind the cursor. With the spacing above this caps
        /// the tail at ~45px — roughly 1.7x the cursor's diameter, short
        /// enough to stay out of the way of what's on screen.
        /// </summary>
        private const int max_samples = 18;

        /// <summary>How long a sample takes to fade once the cursor stops.</summary>
        private const double fade_duration = 220;

        /// <summary>
        /// A jump further than this is treated as a teleport (a screen change,
        /// a warp) rather than movement, and restarts the trail instead of
        /// drawing a streak across everything in between.
        /// </summary>
        private const float teleport_threshold = 250;

        private readonly Circle[] parts = new Circle[max_samples];
        private readonly List<Sample> samples = new List<Sample>(max_samples + 1);

        private readonly float headDiameter;
        private readonly float tailDiameter;

        private Vector2? lastPosition;

        /// <summary>Distance still to travel before the next sample is laid down.</summary>
        private float distanceToNextSample = sample_spacing;

        private readonly struct Sample
        {
            public readonly Vector2 Position;
            public readonly double Time;

            public Sample(Vector2 position, double time)
            {
                Position = position;
                Time = time;
            }
        }

        public CursorTrail(float cursorDiameter, Color4 headColour, Color4 tailColour)
        {
            RelativeSizeAxes = Axes.Both;

            headDiameter = cursorDiameter * 0.55f;
            tailDiameter = cursorDiameter * 0.12f;

            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = new Circle
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(headDiameter),
                    Alpha = 0,
                };
            }

            HeadColour = headColour;
            TailColour = tailColour;

            InternalChildren = parts;
        }

        private Color4 HeadColour { get; }

        private Color4 TailColour { get; }

        /// <summary>Number of trail samples currently alive. Exposed for tests.</summary>
        public int SampleCount => samples.Count;

        /// <summary>Path length the tail currently covers, in pixels. Exposed for tests.</summary>
        public float TrailLength => Math.Max(0, samples.Count - 1) * sample_spacing;

        /// <summary>Positions of the live samples, oldest first. Exposed for tests.</summary>
        public IEnumerable<Vector2> SamplePositions
        {
            get
            {
                foreach (var sample in samples)
                    yield return sample.Position;
            }
        }

        /// <summary>Feeds the cursor's latest position in this container's own space.</summary>
        public void MoveTo(Vector2 position)
        {
            double now = Time.Current;

            if (lastPosition is Vector2 last)
            {
                float distance = Vector2.Distance(last, position);

                if (distance > teleport_threshold)
                {
                    samples.Clear();
                    distanceToNextSample = sample_spacing;
                }
                else if (distance > 0)
                {
                    Vector2 direction = (position - last) / distance;
                    float travelled = 0;

                    // Carrying the leftover distance between frames is what
                    // lets slow movement build a trail at all: without it, a
                    // cursor creeping a pixel per frame would never cover a
                    // full spacing in one go and would never emit a sample.
                    while (travelled + distanceToNextSample <= distance)
                    {
                        travelled += distanceToNextSample;
                        samples.Add(new Sample(last + direction * travelled, now));
                        distanceToNextSample = sample_spacing;
                    }

                    distanceToNextSample -= distance - travelled;
                }
            }

            lastPosition = position;

            trim(now);
            layout(now);
        }

        private void trim(double now)
        {
            while (samples.Count > max_samples)
                samples.RemoveAt(0);

            while (samples.Count > 0 && now - samples[0].Time >= fade_duration)
                samples.RemoveAt(0);
        }

        private void layout(double now)
        {
            int count = samples.Count;

            for (int i = 0; i < parts.Length; i++)
            {
                // The newest sample is the head of the tail, so walk the list
                // backwards: part 0 sits just behind the cursor.
                int sampleIndex = count - 1 - i;

                if (sampleIndex < 0)
                {
                    parts[i].Alpha = 0;
                    continue;
                }

                var sample = samples[sampleIndex];

                float alongTail = i / (float)max_samples;
                float headness = 1 - alongTail;

                float age = (float)Math.Clamp((now - sample.Time) / fade_duration, 0, 1);

                parts[i].Position = sample.Position;
                parts[i].Size = new Vector2(float.Lerp(headDiameter, tailDiameter, alongTail));
                parts[i].Colour = lerpColour(TailColour, HeadColour, headness);
                parts[i].Alpha = headness * (1 - age);
            }
        }

        private static Color4 lerpColour(Color4 from, Color4 to, float amount) => new Color4(
            float.Lerp(from.R, to.R, amount),
            float.Lerp(from.G, to.G, amount),
            float.Lerp(from.B, to.B, amount),
            float.Lerp(from.A, to.A, amount));
    }
}
