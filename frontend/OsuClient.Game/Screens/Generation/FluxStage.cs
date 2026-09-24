using System;
using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using OsuClient.Game.Backend;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// Stages 2a and 2b of the reveal: the signal onset detection actually ran
    /// over, and the threshold it was tested against
    /// (GENERATION_REDESIGN_PLAN.md step 11).
    ///
    /// <para>
    /// The original stage 2 showed onset markers appearing over a
    /// spectrogram — the algorithm's <i>result</i>, with no sign of how it was
    /// reached. This is the method: spectral flux, its local median, and
    /// <c>δ + margin × median</c> drawn over the top, so the markers land
    /// exactly where the curve crosses the line. They have to: an onset
    /// <b>is</b> a threshold crossing, which is why the sparks need no
    /// re-timing to agree with this picture.
    /// </para>
    ///
    /// <para>
    /// Everything here shares one <see cref="TraceGraph.ValueCurve"/>. A
    /// perceptual axis is unavoidable — flux has a median near 0.0001 against
    /// a maximum of 1, so drawn linearly the threshold sits at 3% of the panel
    /// and is invisible — and because the exponent is monotonic, applying the
    /// same one to the signal and its threshold leaves every crossing exactly
    /// where it was. Two different exponents here would draw crossings the
    /// detector never made.
    /// </para>
    /// </summary>
    public partial class FluxStage : CompositeDrawable
    {
        /// <summary>
        /// The perceptual exponent. Measured rather than chosen: at 1.0 the
        /// threshold sits at 3% of the panel height, at 0.4 it sits near a
        /// third and the peaks visibly cross it.
        /// </summary>
        private const float value_curve = 0.4f;

        /// <summary>
        /// Height of the band trio, as a fraction of the panel.
        ///
        /// The trio gets its own lanes under the global curve rather than
        /// sitting behind it. Drawn behind, a filled flux trace hides them
        /// completely — which is what the first render showed, and no amount
        /// of opacity fixes it while the fill reaches the same baseline.
        /// </summary>
        private const float band_fraction = 0.46f;

        private readonly DspDetail detail;
        private readonly string tier;

        private TraceGraph flux = null!;
        private TraceGraph median = null!;
        private TraceGraph threshold = null!;
        private Container bandArea = null!;
        private FillFlowContainer legend = null!;

        private readonly List<TraceGraph> bands = new List<TraceGraph>();

        /// <summary>
        /// Drives the draw-in.
        ///
        /// A <see cref="BindableFloat"/> animated with
        /// <c>TransformBindableTo</c>, not a plain property animated with
        /// <c>TransformTo(nameof(...))</c>: that overload resolves the member
        /// by name at runtime and, when it fails to, fails <b>silently</b>.
        /// It did exactly that to this project's menu strip once already.
        /// </summary>
        private readonly BindableFloat fluxProgress = new BindableFloat();

        public FluxStage(DspDetail detail, string tier)
        {
            this.detail = detail;
            this.tier = tier;

            RelativeSizeAxes = Axes.Both;

            // Built here rather than in LoadComplete: the reveal drives this
            // stage from its own sequence, which can fire before this
            // drawable has finished loading — and RevealFlux on a half-built
            // stage throws on the first null child. Same rule
            // SpectrogramReveal follows for its overlays.
            build();
        }

        /// <summary>The sensitivity the threshold was drawn at, for the heading to quote.</summary>
        public DspSensitivity Sensitivity =>
            detail.Detection.Tiers.TryGetValue(tier, out var found)
                ? found
                : new DspSensitivity { Margin = 0, Delta = 0 };

        private void build()
        {
            double rate = detail.Frames.FrameRate;

            InternalChildren = new Drawable[]
            {
                // The global curve and its threshold, in the upper lane.
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Height = 1 - band_fraction,
                    Children = new Drawable[]
                    {
                        flux = trace("flux", TraceGraph.TraceStyle.Fill, RetroPalette.Cyan, 2f, rate),
                        median = trace("median", TraceGraph.TraceStyle.Line,
                                       RetroPalette.TextDim.Opacity(0.6f), 1.5f, rate),
                        threshold = thresholdTrace(rate),
                    },
                },
                bandArea = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Height = band_fraction,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Alpha = 0,
                },
                legend = new FillFlowContainer
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 3),
                    Margin = new MarginPadding { Top = 4, Right = 6 },
                    Alpha = 0,
                },
            };

            buildBands(rate);
            buildLegend();

            fluxProgress.BindValueChanged(progress =>
            {
                flux.Progress = progress.NewValue;

                foreach (var band in bands)
                    band.Progress = progress.NewValue;
            }, true);

            median.Alpha = 0;
            threshold.Alpha = 0;
        }

        private TraceGraph trace(string curve, TraceGraph.TraceStyle style,
                                 Color4 colour, float thickness, double rate)
        {
            var graph = new TraceGraph
            {
                RelativeSizeAxes = Axes.Both,
                Style = style,
                TraceColour = colour,
                Thickness = thickness,
                ValueCurve = value_curve,
            };

            graph.SetCurve(detail.Samples(curve), rate);
            graph.ShowWholeCurve();

            return graph;
        }

        private TraceGraph thresholdTrace(double rate)
        {
            var graph = new TraceGraph
            {
                RelativeSizeAxes = Axes.Both,
                Style = TraceGraph.TraceStyle.Line,
                TraceColour = RetroPalette.Amber,
                Thickness = 2.5f,
                ValueCurve = value_curve,
            };

            graph.SetCurve(detail.ThresholdFor(Sensitivity.Margin, Sensitivity.Delta), rate);
            graph.ShowWholeCurve();

            return graph;
        }

        private void buildBands(double rate)
        {
            (string Curve, Color4 Colour)[] split =
            {
                ("bandLow", RetroPalette.BandLow),
                ("bandMid", RetroPalette.BandMid),
                ("bandHigh", RetroPalette.BandHigh),
            };

            for (int i = 0; i < split.Length; i++)
            {
                var band = new TraceGraph
                {
                    RelativeSizeAxes = Axes.Both,
                    Height = 1f / split.Length,
                    RelativePositionAxes = Axes.Y,
                    Y = i / (float)split.Length,
                    Style = TraceGraph.TraceStyle.Fill,
                    TraceColour = split[i].Colour.Opacity(0.8f),
                    Thickness = 1.5f,
                    ValueCurve = value_curve,
                };

                band.SetCurve(detail.Samples(split[i].Curve), rate);
                band.ShowWholeCurve();

                bands.Add(band);

                bandArea.Add(band);
                bandArea.Add(new RetroText
                {
                    RelativePositionAxes = Axes.Y,
                    Y = i / (float)split.Length,
                    Margin = new MarginPadding { Left = 5, Top = 2 },
                    Font = RetroFontFamily.Body,
                    TextSize = 8,
                    Colour = split[i].Colour.Opacity(0.85f),
                    Text = split[i].Curve.Replace("band", string.Empty).ToUpperInvariant(),
                });
            }
        }

        private void buildLegend()
        {
            legend.Add(key(RetroPalette.Cyan, "SPECTRAL FLUX"));
            legend.Add(key(RetroPalette.Amber,
                $"THRESHOLD  δ {Sensitivity.Delta:0.00} + {Sensitivity.Margin:0.0} × MEDIAN"));
            legend.Add(key(RetroPalette.TextDim.Opacity(0.6f), "LOCAL MEDIAN"));
        }

        private static Drawable key(Color4 colour, string text) => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 0),
            Children = new Drawable[]
            {
                new osu.Framework.Graphics.Shapes.Box
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Size = new Vector2(14, 2.5f),
                    Colour = colour,
                },
                new RetroText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Font = RetroFontFamily.Body,
                    TextSize = 9,
                    Colour = RetroPalette.TextDim,
                    Text = text,
                },
            },
        };

        /// <summary>
        /// Stage 2a: the flux curve draws in left to right, then the three
        /// band curves fade in beneath it.
        /// </summary>
        public void RevealFlux(double duration)
        {
            this.TransformBindableTo(fluxProgress, 1f, duration, Easing.OutSine);

            bandArea.Delay(duration * 0.55).FadeTo(1, duration * 0.4, Easing.OutQuint);
        }

        /// <summary>
        /// Stage 2b: the local median rises out of the flux and the derived
        /// threshold draws over it.
        /// </summary>
        public void RevealThreshold(double duration)
        {
            median.FadeTo(1, duration * 0.35, Easing.OutQuint);
            threshold.Delay(duration * 0.2).FadeTo(1, duration * 0.35, Easing.OutQuint);
            legend.Delay(duration * 0.3).FadeTo(1, duration * 0.4, Easing.OutQuint);
        }

        /// <summary>Everything at its finished state, for a skip.</summary>
        public void RevealImmediately()
        {
            FinishTransforms(true);

            fluxProgress.Value = 1;
            bandArea.Alpha = 1;
            median.Alpha = 1;
            threshold.Alpha = 1;
            legend.Alpha = 1;
        }

        /// <summary>How far the flux trace has drawn in, 0..1. Exposed for tests.</summary>
        public float FluxProgress => fluxProgress.Value;
    }
}
