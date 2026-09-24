using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
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
    /// Stages 3a and 3b: how the tempo was found, rather than what it was
    /// (GENERATION_REDESIGN_PLAN.md step 12).
    ///
    /// <para>
    /// This is the only part of the pipeline that performs a real search with
    /// a real ambiguity in it, and until now the BPM simply appeared. 3a draws
    /// the autocorrelation over a BPM axis with the log-normal prior over it,
    /// shades the band the search actually looked at, and then scores the
    /// three octave candidates — <c>phase strength × prior = score</c> — with
    /// the winner lit. 3b slides a pulse train across one beat period and
    /// locks it where the phase score peaks.
    /// </para>
    ///
    /// <para>
    /// On roughly one track in seven the octave stage changes the answer, and
    /// where it does the panel says so. Where it does not, the scores are
    /// usually close enough that showing them is still the honest picture:
    /// "this nearly went the other way" is a truer account of the algorithm
    /// than a single confident number.
    /// </para>
    /// </summary>
    public partial class TempoStage : CompositeDrawable
    {
        private readonly DspTempo tempo;
        private readonly DspDetail detail;

        private readonly BindableFloat searchProgress = new BindableFloat();
        private readonly BindableFloat phaseSlide = new BindableFloat();

        private TraceGraph acf = null!;
        private TraceGraph prior = null!;
        private TraceGraph weighted = null!;
        private Box peakMarker = null!;
        private Container searchBand = null!;
        private FillFlowContainer candidates = null!;
        private RackPanel phasePanel = null!;
        private TraceGraph envelope = null!;
        private Container pulseTrain = null!;
        private TraceGraph phaseScores = null!;
        private Box phaseLock = null!;
        private SegmentReadout bpmReadout = null!;
        private RetroText verdict = null!;

        private readonly List<Box> pulses = new List<Box>();

        public TempoStage(DspDetail detail)
        {
            this.detail = detail;
            tempo = detail.Tempo!;

            RelativeSizeAxes = Axes.Both;

            // Constructed here, not in LoadComplete: the reveal drives this
            // from its own sequence, which can fire first.
            build();
        }

        private void build()
        {
            InternalChild = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions = new[]
                {
                    new Dimension(),
                    new Dimension(GridSizeMode.Absolute, 86),
                    new Dimension(GridSizeMode.Absolute, 92),
                },
                Content = new[]
                {
                    new Drawable[] { searchPanel() },
                    // Their own row rather than floating over the plot: laid
                    // on top they cover the peaks they are scoring.
                    new Drawable[]
                    {
                        candidates = new FillFlowContainer
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(10, 0),
                            Margin = new MarginPadding { Bottom = 10 },
                            Alpha = 0,
                        },
                    },
                    new Drawable[] { phasePanel = phaseSection() },
                },
            };

            searchProgress.BindValueChanged(p =>
            {
                acf.Progress = p.NewValue;
                prior.Progress = p.NewValue;
                weighted.Progress = p.NewValue;
            }, true);

            phaseSlide.BindValueChanged(p => layOutPulses(p.NewValue), true);
        }

        private Drawable searchPanel()
        {
            // The autocorrelation is indexed by lag, so the trace is fed the
            // array at one "sample per second" and shown whole — the x axis is
            // lag, and the labels underneath turn it into tempo.
            acf = curve(tempo.Acf, RetroPalette.Cyan, TraceGraph.TraceStyle.Fill, 2f, 0.85f);
            prior = curve(tempo.Prior, RetroPalette.TextDim, TraceGraph.TraceStyle.Line, 1.5f, 0.45f);
            weighted = curve(tempo.Weighted, RetroPalette.Amber, TraceGraph.TraceStyle.Line, 2.5f, 1f);

            return new RackPanel
            {
                RelativeSizeAxes = Axes.Both,
                Margin = new MarginPadding { Bottom = 10 },
                Legend = "AUTOCORRELATION OF THE ONSET ENVELOPE",
                Children = new Drawable[]
                {
                    searchBand = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        RelativePositionAxes = Axes.X,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = RetroPalette.Cyan.Opacity(0.07f),
                        },
                    },
                    acf,
                    prior,
                    weighted,
                    peakMarker = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        RelativePositionAxes = Axes.X,
                        Width = 2,
                        Colour = RetroPalette.Magenta,
                        Alpha = 0,
                    },
                    bpmAxis(),
                    new FillFlowContainer
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 3),
                        Margin = new MarginPadding { Top = 4, Right = 6 },
                        Children = new Drawable[]
                        {
                            legendKey(RetroPalette.Cyan, "AUTOCORRELATION"),
                            legendKey(RetroPalette.TextDim, $"LOG-NORMAL PRIOR ({tempo.PriorBpm:0} BPM)"),
                            legendKey(RetroPalette.Amber, "PRODUCT — THE PEAK IS TAKEN FROM THIS"),
                        },
                    },
                },
            };
        }

        private TraceGraph curve(IReadOnlyList<double> values, Color4 colour,
                                 TraceGraph.TraceStyle style, float thickness, float alpha)
        {
            var graph = new TraceGraph
            {
                RelativeSizeAxes = Axes.Both,
                Style = style,
                TraceColour = colour,
                Thickness = thickness,
                Alpha = alpha,
                // One column per lag, not per pixel: there are only about a
                // hundred lags, and drawing them at pixel resolution would
                // interpolate a smooth curve out of a coarse measurement.
                ColumnWidth = 4,
            };

            var samples = new float[values.Count];

            for (int i = 0; i < samples.Length; i++)
                samples[i] = (float)Math.Max(0, values[i]);

            graph.SetCurve(samples, 1);
            graph.SetWindow(0, samples.Length);
            graph.FitValueRange();
            graph.Progress = 0;

            return graph;
        }

        /// <summary>Tick labels turning the lag axis into a tempo axis.</summary>
        private Drawable bpmAxis()
        {
            var container = new Container
            {
                RelativeSizeAxes = Axes.Both,
            };

            foreach (double bpm in new[] { 60.0, 90.0, 120.0, 160.0, 200.0 })
            {
                float x = fractionForBpm(bpm);

                if (x < 0 || x > 1)
                    continue;

                container.Add(new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    RelativePositionAxes = Axes.X,
                    X = x,
                    Width = 1,
                    Height = 1,
                    Colour = RetroPalette.TextDim.Opacity(0.16f),
                });

                container.Add(new RetroText
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativePositionAxes = Axes.X,
                    X = x,
                    Margin = new MarginPadding { Left = 3, Bottom = 2 },
                    Font = RetroFontFamily.Body,
                    TextSize = 8,
                    Colour = RetroPalette.TextDim.Opacity(0.7f),
                    Text = $"{bpm:0}",
                });
            }

            return container;
        }

        /// <summary>Where a tempo sits across the drawn lag range, 0..1.</summary>
        private float fractionForBpm(double bpm)
        {
            if (tempo.Bpms.Count < 2)
                return -1;

            // The axis runs fast-to-slow, because a shorter lag is a faster
            // tempo — the inversion that is easiest to get backwards.
            for (int i = 0; i < tempo.Bpms.Count - 1; i++)
            {
                double high = tempo.Bpms[i];
                double low = tempo.Bpms[i + 1];

                if (bpm <= high && bpm >= low)
                {
                    double within = (high - bpm) / Math.Max(1e-6, high - low);

                    return (float)((i + within) / (tempo.Bpms.Count - 1));
                }
            }

            return -1;
        }

        private RackPanel phaseSection()
        {
            envelope = new TraceGraph
            {
                RelativeSizeAxes = Axes.Both,
                Style = TraceGraph.TraceStyle.Fill,
                TraceColour = RetroPalette.Mint.Opacity(0.5f),
                Thickness = 1.5f,
                ValueCurve = 0.4f,
            };

            envelope.SetCurve(detail.Samples("envelope"), detail.Frames.FrameRate);
            envelope.ShowWholeCurve();

            phaseScores = new TraceGraph
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.24f,
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Style = TraceGraph.TraceStyle.Fill,
                TraceColour = RetroPalette.Violet.Opacity(0.8f),
                Thickness = 1.5f,
                ColumnWidth = 4,
            };

            var scores = tempo.PhaseScores.Select(v => (float)Math.Max(0, v)).ToArray();

            phaseScores.SetCurve(scores, 1);
            phaseScores.SetWindow(0, Math.Max(1, scores.Length));
            phaseScores.FitValueRange();

            return new RackPanel
            {
                RelativeSizeAxes = Axes.Both,
                Legend = "PHASE — A PULSE TRAIN SLID ACROSS ONE BEAT",
                Alpha = 0,
                Children = new Drawable[]
                {
                    envelope,
                    pulseTrain = new Container { RelativeSizeAxes = Axes.Both },
                    phaseLock = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        RelativePositionAxes = Axes.X,
                        Width = 2,
                        Colour = RetroPalette.Magenta,
                        Alpha = 0,
                    },
                    phaseScores,
                    new RetroText
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Margin = new MarginPadding { Top = 3, Right = 6 },
                        Font = RetroFontFamily.Body,
                        TextSize = 8,
                        Colour = RetroPalette.Violet,
                        Text = "PHASE SCORE PER OFFSET",
                    },
                },
            };
        }

        /// <summary>
        /// Lays the pulse train at a trial offset, in frames.
        ///
        /// Only the first stretch of the track is drawn: at a couple of
        /// hundred pulses across a panel the train stops reading as separate
        /// beats, and the point is to watch it line up, not to count it.
        /// </summary>
        private void layOutPulses(float offsetFrames)
        {
            double period = Math.Max(1, tempo.PeriodFrames);
            double visibleFrames = Math.Min(detail.Frames.Count, period * 28);

            int wanted = (int)Math.Floor((visibleFrames - offsetFrames) / period) + 1;
            wanted = Math.Clamp(wanted, 0, 64);

            while (pulses.Count < wanted)
            {
                var pulse = new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    RelativePositionAxes = Axes.X,
                    Width = 1.5f,
                    Colour = RetroPalette.Amber.Opacity(0.75f),
                };

                pulses.Add(pulse);
                pulseTrain.Add(pulse);
            }

            for (int i = 0; i < pulses.Count; i++)
            {
                if (i >= wanted)
                {
                    pulses[i].Alpha = 0;
                    continue;
                }

                double frame = offsetFrames + i * period;

                pulses[i].Alpha = 1;
                pulses[i].X = (float)(frame / Math.Max(1, detail.Frames.Count));
            }
        }

        private static Drawable legendKey(Color4 colour, string text) => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 0),
            Children = new Drawable[]
            {
                new Box
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

        /// <summary>One octave candidate, with the multiplication that scored it.</summary>
        private Drawable candidateCard(DspOctaveCandidate candidate)
        {
            Color4 accent = candidate.Chosen ? RetroPalette.Mint : RetroPalette.TextDim;

            var panel = new RackPanel
            {
                Size = new Vector2(196, 74),
                Legend = $"× {candidate.Factor:0.#}",
            };

            if (!candidate.InRange)
            {
                panel.Add(new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 3),
                    Children = new Drawable[]
                    {
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = RetroFontFamily.Display,
                            TextSize = 11,
                            Colour = RetroPalette.TextDim.Opacity(0.45f),
                            Text = $"{candidate.Bpm:0.0}",
                        },
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = RetroFontFamily.Body,
                            TextSize = 8,
                            Colour = RetroPalette.TextDim.Opacity(0.45f),
                            Text = "OUT OF SEARCH RANGE",
                        },
                    },
                });

                return panel;
            }

            panel.Add(new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 3),
                Children = new Drawable[]
                {
                    new RetroText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = RetroFontFamily.Display,
                        TextSize = 12,
                        Colour = accent,
                        Text = $"{candidate.Bpm:0.0}",
                    },
                    new RetroText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = RetroFontFamily.Body,
                        TextSize = 8,
                        Colour = RetroPalette.TextDim,
                        Text = $"{candidate.PhaseStrength:0.00} × {candidate.PriorWeight:0.00}",
                    },
                    new RetroText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = RetroFontFamily.Display,
                        TextSize = 10,
                        Colour = accent,
                        Text = $"= {candidate.Score:0.000}",
                    },
                },
            });

            return panel;
        }

        /// <summary>Stage 3a: the autocorrelation, the prior, and the octave scores.</summary>
        public void RevealSearch(double duration)
        {
            searchBand.X = fractionForLag(tempo.SearchLagMin);
            searchBand.Width = Math.Max(0.02f,
                fractionForLag(tempo.SearchLagMax) - fractionForLag(tempo.SearchLagMin));

            this.TransformBindableTo(searchProgress, 1f, duration * 0.45, Easing.OutSine);

            peakMarker.X = fractionForBpm(tempo.RawBpm);
            peakMarker.Delay(duration * 0.45).FadeTo(0.9f, 200, Easing.OutQuint);

            foreach (var candidate in tempo.OctaveCandidates)
                candidates.Add(candidateCard(candidate));

            candidates.Delay(duration * 0.55).FadeTo(1, duration * 0.3, Easing.OutQuint);
        }

        /// <summary>Stage 3b: the pulse train slides, then locks where the score peaks.</summary>
        public void RevealPhase(double duration)
        {
            phasePanel.FadeTo(1, duration * 0.2, Easing.OutQuint);

            // Slides across one whole beat and settles on the offset the
            // tracker chose, which is what the score curve beside it peaks at.
            phaseSlide.Value = 0;
            this.TransformBindableTo(phaseSlide, (float)tempo.PhaseOffsetFrames,
                                     duration * 0.7, Easing.OutQuint);

            phaseLock.X = (float)(tempo.PhaseOffsetFrames / Math.Max(1.0, detail.Frames.Count));
            phaseLock.Delay(duration * 0.7).FadeTo(0.85f, 200, Easing.OutQuint);
        }

        /// <summary>Everything at its finished state, for a skip.</summary>
        public void RevealImmediately()
        {
            FinishTransforms(true);

            searchProgress.Value = 1;
            peakMarker.Alpha = 0.9f;
            candidates.Alpha = 1;
            phasePanel.Alpha = 1;
            phaseSlide.Value = tempo.PhaseOffsetFrames;
            phaseLock.Alpha = 0.85f;
        }

        private float fractionForLag(int lag)
        {
            int span = Math.Max(1, tempo.LagMax - tempo.LagMin);

            return Math.Clamp((lag - tempo.LagMin) / (float)span, 0, 1);
        }
    }
}
