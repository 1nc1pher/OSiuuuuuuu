using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Backend;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// Stage 3 of the DSP reveal (FRONTEND_PLAN.md Phase 7): the tracked beat
    /// grid snapping across the spectrogram, with the BPM counting up to the
    /// value the tracker settled on.
    ///
    /// The counter climbing rather than simply appearing is the point of the
    /// stage. Step 3 is an estimate — an autocorrelation over the onset
    /// envelope, octave-corrected, then phase-fitted — and a number that
    /// arrives already correct reads like a lookup. A number that races and
    /// settles reads like the search it actually was, and the confidence
    /// underneath it says how sure the result is.
    /// </summary>
    public partial class BeatGridReveal : CompositeDrawable
    {
        /// <summary>
        /// Beat lines past this many are dropped.
        ///
        /// A six-minute track at 175 BPM has over a thousand beats; across a
        /// screen-width panel that is a line every pixel, which is a flat wash
        /// rather than a grid. Beyond this count the lines are drawn every
        /// other beat, then every fourth, so the visible spacing stays a
        /// spacing.
        ///
        /// Tuned against a real generated map: 260 lines still came out as a
        /// fine comb covering the spectrogram, where this leaves roughly a
        /// line every 20 pixels — near enough a bar's worth of beats, which
        /// reads as a grid laid over the song rather than as hatching.
        /// </summary>
        public const int MaxBeatLines = 80;

        private readonly AnalysisBeatGrid grid;
        private readonly SpectrogramReveal spectrogram;
        private readonly IReadOnlyList<double> beatTimes;
        private readonly List<Box> lines = new List<Box>();

        private Container lineContainer = null!;
        private FillFlowContainer readout = null!;
        private RetroText bpmText = null!;
        private RetroText confidenceText = null!;

        private double sweepDuration;
        private double? sweepStartTime;
        private int placed;
        private double shownBpm;

        public BeatGridReveal(AnalysisBeatGrid grid, SpectrogramReveal spectrogram)
        {
            this.grid = grid;
            this.spectrogram = spectrogram;

            beatTimes = Thin(grid.BeatTimes, MaxBeatLines);

            RelativeSizeAxes = Axes.Both;
        }

        /// <summary>Beat lines currently drawn. Exposed for tests.</summary>
        public int LineCount => placed;

        /// <summary>How many lines this will draw in total. Exposed for tests.</summary>
        public int TotalLines => beatTimes.Count;

        /// <summary>The BPM currently on the readout. Exposed for tests.</summary>
        public double DisplayedBpm => shownBpm;

        /// <summary>
        /// Keeps every Nth beat so at most <paramref name="limit"/> lines are
        /// drawn, where N is a power of two — halving keeps the grid reading
        /// as a beat grid (every beat, every other beat, every bar) instead of
        /// an arbitrary subset at uneven spacing.
        /// </summary>
        public static IReadOnlyList<double> Thin(IReadOnlyList<double> beatTimes, int limit)
        {
            if (beatTimes.Count <= limit || limit <= 0)
                return beatTimes;

            int step = 1;

            while (beatTimes.Count / (step * 2) > limit)
                step *= 2;

            // One more doubling: the loop above stops at the last step that is
            // still too many.
            step *= 2;

            return beatTimes.Where((_, index) => index % step == 0).ToList();
        }

        [osu.Framework.Allocation.BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                lineContainer = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                },
                readout = new FillFlowContainer
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Margin = new MarginPadding(14),
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        bpmText = new RetroText
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Font = RetroFontFamily.Display,
                            TextSize = 26,
                            Colour = new Color4(1f, 0.92f, 0.55f, 1f),
                            Text = "0 BPM",
                        },
                        confidenceText = new RetroText
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Font = RetroFontFamily.Body,
                            TextSize = 13,
                            Colour = new Color4(0.7f, 0.72f, 0.82f, 1f),
                            Text = string.Empty,
                        },
                    },
                },
            };
        }

        /// <summary>Snaps the grid in and runs the BPM counter over <paramref name="duration"/> milliseconds.</summary>
        public void Sweep(double duration)
        {
            sweepDuration = Math.Max(1, duration);
            sweepStartTime = Clock.CurrentTime;

            readout.FadeIn(280, Easing.OutQuint);
        }

        /// <summary>Draws the whole grid and the final numbers at once, for a skip.</summary>
        public void SweepImmediately()
        {
            sweepStartTime = null;

            while (placed < beatTimes.Count)
                addLine(beatTimes[placed++], animate: false);

            readout.Alpha = 1;
            setBpm(grid.Bpm);
        }

        protected override void Update()
        {
            base.Update();

            if (sweepStartTime != null)
            {
                double elapsed = Clock.CurrentTime - sweepStartTime.Value;
                double progress = Math.Clamp(elapsed / sweepDuration, 0, 1);

                int target = (int)Math.Round(progress * beatTimes.Count);

                while (placed < target)
                    addLine(beatTimes[placed++], animate: true);

                // Overshoot slightly and ease back: the counter passing the
                // answer and settling onto it looks like a search converging,
                // which is what octave correction actually does.
                double eased = 1 - Math.Pow(1 - progress, 3);
                double overshoot = Math.Sin(progress * Math.PI) * grid.Bpm * 0.12;

                setBpm(grid.Bpm * eased + overshoot * (1 - progress));

                if (progress >= 1)
                {
                    setBpm(grid.Bpm);
                    sweepStartTime = null;
                }
            }

            for (int i = 0; i < lines.Count; i++)
                lines[i].X = spectrogram.TimeToX(beatTimes[i]);
        }

        private void setBpm(double bpm)
        {
            shownBpm = bpm;

            bpmText.Text = $"{bpm:0} BPM";

            // Confidence only means something next to a settled number, so it
            // waits for the counter rather than flickering through the climb.
            confidenceText.Text = sweepStartTime == null
                ? $"confidence {grid.Confidence:0.00}   offset {grid.Offset * 1000:0} ms"
                : string.Empty;
        }

        private void addLine(double time, bool animate)
        {
            var line = new Box
            {
                RelativeSizeAxes = Axes.Y,
                Width = 1,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopCentre,
                Colour = new Color4(0.95f, 0.85f, 0.45f, 1f),
                Alpha = animate ? 0 : 0.35f,
                EdgeSmoothness = new Vector2(1),
                X = spectrogram.TimeToX(time),
            };

            lines.Add(line);
            lineContainer.Add(line);

            if (animate)
                line.FadeTo(0.9f, 60).Then().FadeTo(0.35f, 420, Easing.OutQuint);
        }
    }
}
