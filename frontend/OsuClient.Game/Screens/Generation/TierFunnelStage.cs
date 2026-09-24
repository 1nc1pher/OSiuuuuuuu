using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Backend;
using OsuClient.Game.Graphics;
using OsuClient.Game.Graphics.Rack;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// Step 5 of the pipeline, on screen for the first time: what difficulty
    /// scaling actually does (GENERATION_REDESIGN_PLAN.md step 13).
    ///
    /// <para>
    /// Five maps come out of one song and nothing anywhere showed what was
    /// turned to make them different. Each lane here is one tier, narrowing
    /// through the four counts the backend recorded — detected onsets, what
    /// survived quantisation onto the grid, what survived that tier's minimum
    /// spacing, and the objects finally placed — with the preset knobs that
    /// caused each narrowing printed beside it.
    /// </para>
    ///
    /// <para>
    /// The tiers narrow by very different amounts, and that <i>is</i> the
    /// content: Easy sheds most of its notes at the spacing step because it
    /// thins at two beats, while Expert barely thins at all and folds no
    /// streams. The lanes are drawn against a shared scale so those
    /// differences are comparable by eye rather than only by reading numbers.
    /// </para>
    /// </summary>
    public partial class TierFunnelStage : CompositeDrawable
    {
        /// <summary>
        /// How big everything is drawn. <see cref="Compact"/> is the
        /// analyser's bottom-right panel: fixed-height lanes that scroll.
        /// <see cref="Full"/> is the reveal, where this is the whole chassis:
        /// lanes share the height, the four columns share the width, and the
        /// text is sized like every other stage's, to be read at a distance.
        /// </summary>
        public enum Size
        {
            Compact,
            Full,
        }

        /// <summary>Every size the layout uses, for one <see cref="Size"/>.</summary>
        private sealed record Metrics(
            float LaneHeight, float LaneSpacing, float LabelWidth,
            float NameSize, float KnobSize, float CountSize, float StepLabelSize,
            float StepWidth, float StepSpacing, float CountMargin);

        private static readonly Metrics compact = new Metrics(
            LaneHeight: 54, LaneSpacing: 6, LabelWidth: 186,
            NameSize: 11, KnobSize: 8, CountSize: 11, StepLabelSize: 7,
            StepWidth: 132, StepSpacing: 6, CountMargin: 7);

        /// <summary>
        /// For the reveal. LaneHeight here is the most a lane may grow to —
        /// lanes otherwise share whatever height there is — so two tiers do
        /// not become two slabs. The four columns share the width, so
        /// StepWidth is unused.
        /// </summary>
        private static readonly Metrics full = new Metrics(
            LaneHeight: 190, LaneSpacing: 14, LabelWidth: 300,
            NameSize: 20, KnobSize: 11, CountSize: 22, StepLabelSize: 10,
            StepWidth: 0, StepSpacing: 12, CountMargin: 14);

        private readonly bool fullSize;
        private readonly Metrics metrics;

        private readonly IReadOnlyList<KeyValuePair<string, DspTier>> tiers;
        private readonly int widest;

        private readonly List<(Container Bar, float Target)> bars =
            new List<(Container, float)>();

        /// <summary>The lanes, in <see cref="Size.Full"/>: sized each frame to what they want of the room there is.</summary>
        private Container? laneBlock;

        public TierFunnelStage(DspDetail detail, Size size = Size.Compact)
        {
            fullSize = size == Size.Full;
            metrics = fullSize ? full : compact;

            // Ordered easiest-first by star target, so the ramp runs the way
            // it does everywhere else in the client.
            tiers = (detail.Tiers ?? new Dictionary<string, DspTier>())
                    .OrderBy(pair => pair.Value.Preset.Stars)
                    .ToList();

            widest = Math.Max(1, tiers.Select(t => t.Value.Funnel.Detected).DefaultIfEmpty(1).Max());

            RelativeSizeAxes = Axes.Both;

            InternalChild = new RackPanel
            {
                RelativeSizeAxes = Axes.Both,
                Legend = "DIFFICULTY SCALING — DETECTED · SNAPPED · SPACED · OBJECTS",
                Child = fullSize ? fullLanes() : compactLanes(),
            };
        }

        /// <summary>A scrolling stack of fixed-height lanes, for the analyser's short panel.</summary>
        private Drawable compactLanes()
        {
            var lanes = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, metrics.LaneSpacing),
            };

            for (int i = 0; i < tiers.Count; i++)
            {
                var laneDrawable = lane(tiers[i].Key, tiers[i].Value, i, tiers.Count);

                laneDrawable.RelativeSizeAxes = Axes.X;
                laneDrawable.Height = metrics.LaneHeight;

                lanes.Add(laneDrawable);
            }

            // Scrolls when the panel is shorter than the lanes — the
            // analyser's bottom row fits two tiers of five. Where everything
            // fits it is inert.
            return new RackScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Horizontal = 8, Top = 4, Bottom = 4 },
                Child = lanes,
            };
        }

        /// <summary>
        /// Lanes sharing the panel's whole height, up to a lane's maximum,
        /// and centred in it when they stop short of it.
        /// </summary>
        private Drawable fullLanes()
        {
            var rows = new Drawable[tiers.Count][];

            for (int i = 0; i < tiers.Count; i++)
            {
                var laneDrawable = lane(tiers[i].Key, tiers[i].Value, i, tiers.Count);

                laneDrawable.RelativeSizeAxes = Axes.Both;

                rows[i] = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Vertical = metrics.LaneSpacing / 2 },
                        Child = laneDrawable,
                    },
                };
            }

            return new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Horizontal = 16, Vertical = 12 },
                Child = laneBlock = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    Child = new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        RowDimensions = Enumerable.Range(0, tiers.Count).Select(_ => new Dimension()).ToArray(),
                        Content = rows,
                    },
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            // As tall as the lanes want, up to all the room there is.
            if (laneBlock?.Parent is Container area)
                laneBlock.Height = Math.Min(area.ChildSize.Y, tiers.Count * metrics.LaneHeight);
        }

        private Container lane(string name, DspTier tier, int rank, int rankCount)
        {
            Color4 accent = RetroPalette.ForDifficultyRank(rank, rankCount);
            var funnel = tier.Funnel;

            (string Label, int Count)[] steps =
            {
                ("DETECTED", funnel.Detected),
                ("SNAPPED", funnel.Snapped),
                ("SPACED", funnel.AfterSpacing),
                ("OBJECTS", funnel.Objects),
            };

            return new Container
            {
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Width = metrics.LabelWidth - 8,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, fullSize ? 8 : 3),
                        Children = new Drawable[]
                        {
                            new RetroText
                            {
                                Font = RetroFontFamily.Display,
                                TextSize = metrics.NameSize,
                                Colour = accent,
                                Text = name.ToUpperInvariant(),
                            },
                            new RetroText
                            {
                                Font = RetroFontFamily.Body,
                                TextSize = metrics.KnobSize,
                                Colour = RetroPalette.TextDim,
                                // The knobs that caused this lane's shape.
                                Text = $"margin {tier.Preset.Margin:0.0} · δ {tier.Preset.Delta:0.00}"
                                       + $" · 1/{tier.Preset.SnapDivision} · ≥{tier.Preset.MinSpacingBeats:0.##} beats",
                            },
                        },
                    },
                    fullSize ? sharedColumns(steps, accent) : fixedColumns(steps, accent),
                },
            };
        }

        /// <summary>Four columns sharing the lane's width — the reveal's, rather than four boxes clustered at the left of a wide panel.</summary>
        private Drawable sharedColumns((string Label, int Count)[] steps, Color4 accent) => new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            Padding = new MarginPadding { Left = metrics.LabelWidth },
            ColumnDimensions = steps.Select(_ => new Dimension()).ToArray(),
            Content = new[]
            {
                steps.Select(s => (Drawable)new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Right = metrics.StepSpacing },
                    Child = step(s.Label, s.Count, accent).With(c => c.RelativeSizeAxes = Axes.Both),
                }).ToArray(),
            },
        };

        /// <summary>Four fixed-width columns, the analyser's.</summary>
        private Drawable fixedColumns((string Label, int Count)[] steps, Color4 accent)
        {
            var flow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(metrics.StepSpacing, 0),
                Padding = new MarginPadding { Left = metrics.LabelWidth },
            };

            foreach (var (label, count) in steps)
            {
                flow.Add(step(label, count, accent).With(c =>
                {
                    c.RelativeSizeAxes = Axes.Y;
                    c.Width = metrics.StepWidth;
                }));
            }

            return flow;
        }

        /// <summary>One column of the funnel: a bar scaled against every tier's widest count, and the count itself.</summary>
        private Container step(string label, int count, Color4 accent)
        {
            var bar = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Height = 0,
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Masking = true,
                CornerRadius = 2,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = accent.Opacity(0.75f),
                },
            };

            bars.Add((bar, count / (float)widest));

            return new Container
            {
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = RetroPalette.PanelInset.Opacity(0.6f),
                    },
                    bar,
                    new RetroText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = metrics.CountMargin },
                        Font = RetroFontFamily.Display,
                        TextSize = metrics.CountSize,
                        Colour = RetroPalette.Text,
                        Text = count.ToString(),
                    },
                    // Top-right, not bottom: the bar grows up from the
                    // baseline and swallowed the label on a full lane.
                    new RetroText
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Margin = new MarginPadding { Right = metrics.CountMargin * 0.7f, Top = metrics.CountMargin * 0.5f },
                        Font = RetroFontFamily.Body,
                        TextSize = metrics.StepLabelSize,
                        Colour = RetroPalette.TextDim.Opacity(0.8f),
                        Text = label,
                    },
                },
            };
        }

        /// <summary>Grows every bar to its share, lane by lane.</summary>
        public void Reveal(double duration)
        {
            for (int i = 0; i < bars.Count; i++)
            {
                var (bar, target) = bars[i];

                // Staggered so the funnel fills lane by lane and the
                // narrowing is something you watch rather than arrive at.
                bar.Delay(i * duration / Math.Max(1, bars.Count) * 0.5)
                   .ResizeHeightTo(target, duration * 0.4, Easing.OutQuint);
            }
        }

        /// <summary>Everything at its finished state, for a skip.</summary>
        public void RevealImmediately()
        {
            foreach (var (bar, target) in bars)
            {
                bar.FinishTransforms();
                bar.Height = target;
            }
        }
    }
}
