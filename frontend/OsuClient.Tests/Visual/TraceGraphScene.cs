using System;
using System.IO;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Backend;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The <c>trace-graph</c> screenshot scene: real spectral flux out of a
    /// real <c>dsp.json</c>, with the adaptive threshold derived on top of it.
    ///
    /// A green reduction test says a spike survives an array transform. It
    /// says nothing about whether the path renders, whether it is the right
    /// way up, or whether the threshold visibly rides the curve's local level
    /// — which is the one thing that would reveal the threshold being computed
    /// against the wrong curve, or scaled against the wrong axis. This project
    /// has twice shipped a component that passed every test and drew nothing
    /// at all, so the frame gets looked at.
    ///
    /// Falls back to a synthetic curve when there is no fixture, so the scene
    /// always renders something rather than an empty panel that could mean
    /// either "broken" or "no data".
    /// </summary>
    public partial class TraceGraphScene : CompositeDrawable
    {
        private const float row_height = 116;

        public TraceGraphScene()
        {
            RelativeSizeAxes = Axes.Both;

            var detail = loadDetail();

            float[] flux = detail?.Samples("flux") ?? syntheticFlux();
            float[] median = detail?.Samples("median") ?? syntheticThreshold(flux);
            double frameRate = detail?.Frames.FrameRate ?? 43.066;

            // A tier the file actually carries, rather than a hardcoded name:
            // the committed fixture holds two of the five presets, and a
            // scene that assumes a particular one crashes on a fixture that
            // was regenerated with a different pair.
            string tier = detail == null ? string.Empty : firstTier(detail);

            float[] threshold = tier.Length > 0
                ? detail!.ThresholdFor(tier)
                : syntheticThreshold(flux);

            string caption = tier.Length == 0
                ? "synthetic curve — no dsp.json fixture found"
                : $"flux + adaptive threshold ({tier}: δ {detail!.Detection.Tiers[tier].Delta:0.00} · "
                  + $"margin {detail.Detection.Tiers[tier].Margin:0.0}) — {flux.Length} frames";

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = RetroPalette.Void },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Width = 0.9f,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 18),
                    Children = new Drawable[]
                    {
                        label(caption, RetroPalette.TextDim),
                        label("LINEAR AXIS — flux in cyan, its threshold in amber, both at 3% of the height",
                              RetroPalette.TextDim),
                        panel(flux, threshold, frameRate, 1f),
                        label("CURVED AXIS (0.4) — the same two curves, the same crossings, now readable",
                              RetroPalette.TextDim),
                        panel(flux, threshold, frameRate, 0.4f),
                        label("LINE STYLE on the median — a smooth curve reads as a line, not a band",
                              RetroPalette.TextDim),
                        linePanel(median, frameRate),
                        label("PROGRESS 0.45 — the reveal is a wipe over a finished path",
                              RetroPalette.TextDim),
                        revealPanel(flux, frameRate),
                    },
                },
            };
        }

        /// <summary>The loosest tier the document carries — the one that detects the most.</summary>
        private static string firstTier(DspDetail detail)
        {
            string best = string.Empty;
            double loosest = double.MaxValue;

            foreach (var (name, sensitivity) in detail.Detection.Tiers)
            {
                if (sensitivity.Margin < loosest)
                {
                    loosest = sensitivity.Margin;
                    best = name;
                }
            }

            return best;
        }

        private static Drawable label(string text, Color4 colour) => new RetroText
        {
            Font = RetroFontFamily.Body,
            TextSize = 13,
            Colour = colour,
            Text = text,
        };

        /// <summary>A bezelled well with a trace in it, the shape every graph in the analyser will have.</summary>
        private static Container well(params Drawable[] content)
        {
            var children = new Drawable[content.Length + 1];
            children[0] = new Box { RelativeSizeAxes = Axes.Both, Colour = RetroPalette.PanelInset };
            Array.Copy(content, 0, children, 1, content.Length);

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = row_height,
                Masking = true,
                CornerRadius = 4,
                BorderThickness = 1.5f,
                BorderColour = RetroPalette.ChromeDark,
                Children = children,
            };
        }

        private static Container panel(float[] flux, float[] threshold, double frameRate, float curve)
        {
            var fluxTrace = new TraceGraph
            {
                RelativeSizeAxes = Axes.Both,
                Style = TraceGraph.TraceStyle.Fill,
                TraceColour = RetroPalette.Cyan,
                Thickness = 2,
                ValueCurve = curve,
            };

            fluxTrace.SetCurve(flux, frameRate);
            fluxTrace.ShowWholeCurve();

            var thresholdTrace = new TraceGraph
            {
                RelativeSizeAxes = Axes.Both,
                Style = TraceGraph.TraceStyle.Line,
                TraceColour = RetroPalette.Amber,
                Thickness = 2.5f,
                // The same exponent as the curve underneath it. Anything else
                // and the drawn crossings stop being the real ones.
                ValueCurve = curve,
            };

            thresholdTrace.SetCurve(threshold, frameRate);
            thresholdTrace.ShowWholeCurve();

            return well(fluxTrace, thresholdTrace);
        }

        private static Container linePanel(float[] curve, double frameRate)
        {
            var trace = new TraceGraph
            {
                RelativeSizeAxes = Axes.Both,
                Style = TraceGraph.TraceStyle.Line,
                TraceColour = RetroPalette.Mint,
                Thickness = 1.5f,
                ValueCurve = 0.4f,
            };

            trace.SetCurve(curve, frameRate);
            trace.ShowWholeCurve();

            // Shown on its own, so it gets the whole panel. The median peaks
            // around 0.005 on this track — against flux's 0..1 axis it would
            // be a flat line along the bottom.
            trace.FitValueRange();

            return well(trace);
        }

        private static Container revealPanel(float[] flux, double frameRate)
        {
            var trace = new TraceGraph
            {
                RelativeSizeAxes = Axes.Both,
                Style = TraceGraph.TraceStyle.Fill,
                TraceColour = RetroPalette.Magenta,
                Thickness = 2,
                Progress = 0.45f,
                ValueCurve = 0.4f,
            };

            trace.SetCurve(flux, frameRate);
            trace.ShowWholeCurve();

            return well(trace);
        }

        private static DspDetail? loadDetail()
        {
            string? root = BeatmapLibrary.FindRepositoryRoot(AppContext.BaseDirectory);

            if (root == null)
                return null;

            return DspDetail.LoadFromFolder(Path.Combine(root, "frontend", "OsuClient.Tests", "Fixtures"));
        }

        /// <summary>Spiky, uneven, and loud in places — enough to tell a working trace from a flat one.</summary>
        private static float[] syntheticFlux()
        {
            var values = new float[4000];
            var random = new Random(20260920);

            for (int i = 0; i < values.Length; i++)
                values[i] = (float)(random.NextDouble() * 0.05);

            for (int i = 20; i < values.Length; i += 37)
                values[i] = 0.4f + (float)(random.NextDouble() * 0.6);

            return values;
        }

        private static float[] syntheticThreshold(float[] flux)
        {
            var threshold = new float[flux.Length];

            for (int i = 0; i < threshold.Length; i++)
                threshold[i] = 0.18f + 0.06f * MathF.Sin(i / 300f);

            return threshold;
        }
    }
}
