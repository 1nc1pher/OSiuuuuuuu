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
    /// Step 4 of the pipeline: quantising onsets onto the beat grid, and
    /// deciding what each gap becomes (GENERATION_REDESIGN_PLAN.md step 13).
    ///
    /// <para>
    /// Two things that were invisible. Every onset is moved to the nearest
    /// subdivision, and how far it had to move is recorded — a histogram of
    /// those distances says whether the track was already on the grid or was
    /// dragged onto it. Then each gap is measured for sustained energy, and
    /// that is what decides between a slider, a spinner and a plain circle.
    /// </para>
    ///
    /// <para>
    /// The decision cards deliberately include <b>near misses</b>: gaps long
    /// enough for a slider that stayed circles because the sound had already
    /// decayed. A panel that only showed the rule succeeding would not be
    /// showing a test at all.
    /// </para>
    /// </summary>
    public partial class SnapStage : CompositeDrawable
    {
        /// <summary>How many decisions to show. More than this and none of them can be read.</summary>
        private const int max_decisions = 4;

        private readonly DspTier tier;

        private readonly List<(Drawable Bar, float Target)> histogram =
            new List<(Drawable, float)>();

        private Drawable decisions = null!;

        public SnapStage(DspTier tier)
        {
            this.tier = tier;

            RelativeSizeAxes = Axes.Both;

            build();
        }

        private void build()
        {
            InternalChild = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                ColumnDimensions = new[]
                {
                    new Dimension(GridSizeMode.Relative, 0.42f),
                    new Dimension(),
                },
                Content = new[]
                {
                    new[] { snapPanel(), sustainPanel() },
                },
            };
        }

        /// <summary>
        /// Rows sharing a panel's whole height, each centred in its share —
        /// so a handful of rows spread over the chassis the way every other
        /// stage's plots do, rather than stacking at its top edge.
        /// </summary>
        private static Drawable distributed(IReadOnlyList<Drawable> rows, MarginPadding padding) => new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            Padding = padding,
            RowDimensions = rows.Select(_ => new Dimension()).ToArray(),
            Content = rows.Select(r => new[] { r }).ToArray(),
        };

        private Drawable snapPanel()
        {
            int total = Math.Max(1, tier.SnapHistogram.Values.Sum());

            var rows = new[] { "1/1", "1/2", "1/4" }.Select(division =>
            {
                tier.SnapHistogram.TryGetValue(division, out int count);
                return histogramRow(division, count, count / (float)total);
            }).ToList();

            var lanes = distributed(rows, new MarginPadding { Left = 16, Right = 24, Top = 12, Bottom = 40 });

            double worst = tier.SnapDeltasMs.Count == 0
                ? 0
                : tier.SnapDeltasMs.Max(Math.Abs);

            double mean = tier.SnapDeltasMs.Count == 0
                ? 0
                : tier.SnapDeltasMs.Average(Math.Abs);

            return new RackPanel
            {
                RelativeSizeAxes = Axes.Both,
                Margin = new MarginPadding { Right = 10 },
                Legend = $"SNAP — 1/{tier.Preset.SnapDivision} GRID",
                Children = new Drawable[]
                {
                    lanes,
                    new RetroText
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Margin = new MarginPadding { Left = 16, Bottom = 12 },
                        Font = RetroFontFamily.Body,
                        TextSize = 13,
                        Colour = RetroPalette.TextDim,
                        Text = $"moved {mean:0} ms on average, {worst:0} ms at worst"
                               + $" · {tier.Merged} lost a slot to a stronger onset",
                    },
                },
            };
        }

        private Drawable histogramRow(string division, int count, float share)
        {
            var fill = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0,
                Colour = RetroPalette.Cyan.Opacity(0.7f),
            };

            histogram.Add((fill, share));

            // Centred in its share of the panel, at a fixed, readable height:
            // stretched to the whole share, three bars became three slabs.
            return new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.X,
                Height = 48,
                Children = new Drawable[]
                {
                    new RetroText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = RetroFontFamily.Display,
                        TextSize = 22,
                        Colour = RetroPalette.Text,
                        Text = division,
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = 96, Right = 90 },
                        Child = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 3,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = RetroPalette.PanelInset.Opacity(0.6f),
                                },
                                fill,
                            },
                        },
                    },
                    new RetroText
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Font = RetroFontFamily.Display,
                        TextSize = 20,
                        Colour = RetroPalette.TextDim,
                        Text = count.ToString(),
                    },
                },
            };
        }

        private Drawable sustainPanel()
        {
            decisions = distributed(pickDecisions().Select(decisionRow).ToList(),
                new MarginPadding { Left = 16, Right = 24, Vertical = 12 });

            decisions.Alpha = 0;

            return new RackPanel
            {
                RelativeSizeAxes = Axes.Both,
                Legend = $"SUSTAIN — A GAP IS HELD IF ENERGY STAYS UP FOR {tier.Preset.SustainThreshold:0%} OF IT",
                Child = decisions,
            };
        }

        /// <summary>
        /// A spread of decisions rather than the first few: one of each
        /// outcome where possible, so the panel shows the rule both
        /// succeeding and failing.
        /// </summary>
        private IEnumerable<DspSustainDecision> pickDecisions()
        {
            var chosen = new List<DspSustainDecision>();

            foreach (string outcome in new[] { "spinner", "heldSlider", "circle", "streamSlider" })
            {
                var match = tier.Sustain.FirstOrDefault(d => d.Became == outcome);

                if (match != null)
                    chosen.Add(match);
            }

            foreach (var extra in tier.Sustain)
            {
                if (chosen.Count >= max_decisions)
                    break;

                if (!chosen.Contains(extra))
                    chosen.Add(extra);
            }

            return chosen.Take(max_decisions).OrderBy(d => d.Time);
        }

        private Drawable decisionRow(DspSustainDecision decision)
        {
            bool held = decision.Became != "circle";
            Color4 accent = held ? RetroPalette.Mint : RetroPalette.Amber;

            return new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.X,
                Height = 48,
                Children = new Drawable[]
                {
                    new RetroText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = RetroFontFamily.Body,
                        TextSize = 15,
                        Colour = RetroPalette.TextDim,
                        Text = $"{decision.Time:0.0}s · {decision.GapBeats:0.##} beats",
                    },
                    // The gauge: how much of the gap stayed loud, against the
                    // threshold it had to clear.
                    new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.46f,
                        X = 220,
                        Height = 30,
                        Masking = true,
                        CornerRadius = 3,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = RetroPalette.PanelInset.Opacity(0.7f),
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = (float)Math.Clamp(decision.Sustain, 0, 1),
                                Colour = accent.Opacity(0.7f),
                            },
                            // The bar to beat.
                            new Box
                            {
                                RelativeSizeAxes = Axes.Y,
                                RelativePositionAxes = Axes.X,
                                X = (float)tier.Preset.SustainThreshold,
                                Width = 3,
                                Colour = RetroPalette.Text,
                            },
                        },
                    },
                    new RetroText
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Font = RetroFontFamily.Display,
                        TextSize = 16,
                        Colour = accent,
                        Text = $"{decision.Sustain:0.00} → {label(decision.Became)}",
                    },
                },
            };
        }

        private static string label(string became) => became switch
        {
            "heldSlider" => "SLIDER",
            "streamSlider" => "STREAM",
            "spinner" => "SPINNER",
            _ => "CIRCLE",
        };

        /// <summary>Fills the histogram, then brings the decisions in under it.</summary>
        public void Reveal(double duration)
        {
            foreach (var (bar, target) in histogram)
                bar.ResizeWidthTo(target, duration * 0.35, Easing.OutQuint);

            decisions.Delay(duration * 0.3).FadeTo(1, duration * 0.35, Easing.OutQuint);
        }

        /// <summary>Everything at its finished state, for a skip.</summary>
        public void RevealImmediately()
        {
            foreach (var (bar, target) in histogram)
            {
                bar.FinishTransforms();
                bar.Width = target;
            }

            decisions.FinishTransforms();
            decisions.Alpha = 1;
        }
    }
}
