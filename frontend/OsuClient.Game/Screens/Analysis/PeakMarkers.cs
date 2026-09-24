using System;
using System.Collections.Generic;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Analysis
{
    /// <summary>
    /// Vertical markers at a set of frame positions, over a time window.
    ///
    /// Drawn for the onsets the analyser's live peak-picker finds, so they
    /// move as the detector's knobs turn. Markers are pooled and repositioned
    /// rather than rebuilt: turning a knob can change the set a few times a
    /// second, and allocating a thousand drawables per turn is the obvious way
    /// to make a live control feel dead.
    ///
    /// Past <see cref="MaxMarkers"/> the set is thinned evenly. A real track
    /// detects far more onsets than a panel has pixels, and a marker every
    /// pixel is a solid block that says nothing about where the onsets are —
    /// the same lesson <c>OnsetSparkLayer</c> recorded at 420 sparks.
    /// </summary>
    public partial class PeakMarkers : CompositeDrawable
    {
        /// <summary>Most markers drawn at once, whatever the count.</summary>
        public const int MaxMarkers = 400;

        private readonly List<Box> pool = new List<Box>();

        private IReadOnlyList<int> frames = Array.Empty<int>();
        private int frameCount = 1;
        private double windowStart;
        private double windowEnd = 1;
        private double frameRate = 1;

        public PeakMarkers()
        {
            RelativeSizeAxes = Axes.Both;
        }

        /// <summary>The marker colour.</summary>
        public Color4 MarkerColour { get; init; } = RetroPalette.Magenta;

        /// <summary>How many frames the curve holds, for clamping.</summary>
        public int FrameCount
        {
            set => frameCount = Math.Max(1, value);
        }

        /// <summary>Samples per second, to place a frame in time.</summary>
        public double FrameRate
        {
            set => frameRate = value > 0 ? value : 1;
        }

        /// <summary>The visible span of the track, in seconds.</summary>
        public void SetWindow(double startSeconds, double endSeconds)
        {
            windowStart = startSeconds;
            windowEnd = Math.Max(startSeconds + 1e-6, endSeconds);

            layOut();
        }

        /// <summary>The frames to mark.</summary>
        public void SetFrames(IReadOnlyList<int> picked)
        {
            frames = picked;

            layOut();
        }

        /// <summary>How many markers are actually drawn. Exposed for tests.</summary>
        public int DrawnCount { get; private set; }

        private void layOut()
        {
            int step = Math.Max(1, (int)Math.Ceiling(frames.Count / (double)MaxMarkers));
            int wanted = 0;

            for (int i = 0; i < frames.Count; i += step)
            {
                double seconds = frames[i] / frameRate;
                float x = (float)((seconds - windowStart) / (windowEnd - windowStart));

                if (x < 0 || x > 1)
                    continue;

                if (wanted >= pool.Count)
                {
                    var marker = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        RelativePositionAxes = Axes.X,
                        Width = 1.5f,
                        // Faint on purpose: at full strength a few
                        // hundred full-height lines bury the curve they
                        // are supposed to be marking points on.
                        Colour = MarkerColour.Opacity(0.42f),
                    };

                    pool.Add(marker);
                    AddInternal(marker);
                }

                pool[wanted].X = x;
                pool[wanted].Alpha = 1;
                wanted++;
            }

            for (int i = wanted; i < pool.Count; i++)
                pool[i].Alpha = 0;

            DrawnCount = wanted;
        }
    }
}
