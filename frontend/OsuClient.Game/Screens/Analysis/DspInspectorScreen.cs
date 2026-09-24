using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Screens;
using OsuClient.Game.Audio;
using OsuClient.Game.Backend;
using OsuClient.Game.Graphics;
using OsuClient.Game.Graphics.Rack;
using OsuClient.Game.Screens.Generation;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Analysis
{
    /// <summary>
    /// The analyser: the DSP made explorable rather than watched
    /// (GENERATION_REDESIGN_PLAN.md part 6).
    ///
    /// <para>
    /// The reveal is a timed sequence — it shows what it chose to show, for as
    /// long as it chose. This is the same data with the timeline taken off:
    /// the spectrogram scrubs, every trace can be switched on or off, and the
    /// detector's two knobs are live.
    /// </para>
    ///
    /// <para>
    /// <b>Turning a knob re-runs the real algorithm.</b> The margin and delta
    /// controls feed <see cref="OnsetPeakPicker"/> over the exported flux and
    /// median, and the markers and the count redraw from its answer. That is a
    /// demonstration rather than a toy only because the port is known to
    /// reproduce the backend exactly — asserted two ways, in Python and in
    /// C#. If those tests ever fail, this screen starts lying.
    /// </para>
    ///
    /// <para>
    /// <b>What it can show depends on the map.</b> Every set generated before
    /// the trace existed has an <c>analysis.json</c> and no <c>dsp.json</c>:
    /// those open with the spectrogram, the recorded onsets and the beat grid,
    /// and say plainly that the rest needs regenerating. Missing data removes
    /// a panel; it never breaks one.
    /// </para>
    /// </summary>
    public partial class DspInspectorScreen : Screen
    {
        /// <summary>Quiet enough to scrub over.</summary>
        private const double preview_volume = 0.35;

        private readonly string beatmapFolder;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private AnalysisData? analysis;
        private DspDetail? detail;

        private readonly List<(ToggleSwitch Switch, Drawable Target)> traces =
            new List<(ToggleSwitch, Drawable)>();

        private Container stripArea = null!;
        private SpectrogramReveal? spectrogram;
        private Playhead playhead = null!;

        private HeaderChip playPauseChip = null!;

        private readonly TapeDeckSoundPlayer sounds = new TapeDeckSoundPlayer();

        /// <summary>
        /// Paused by the player, as opposed to stopped because the song ran
        /// out. A seek plays a song that ran out — scrubbing a finished song
        /// used to move the playhead in silence — but leaves a paused one
        /// paused: scrubbing to find a spot is what pausing is for.
        /// </summary>
        private bool userPaused;

        private bool scrubbing;
        private float lastScrubX;
        private double lastScrubTime;
        private PeakMarkers peaks = null!;
        private TraceGraph fluxTrace = null!;
        private TraceGraph thresholdTrace = null!;
        private ControlKnob marginKnob = null!;
        private ControlKnob deltaKnob = null!;
        private SegmentReadout onsetCount = null!;
        private RetroText comparison = null!;

        private Track? previewTrack;
        private bool repickQueued;

        public DspInspectorScreen(string beatmapFolder)
        {
            this.beatmapFolder = beatmapFolder;
        }

        /// <summary>The analysis being inspected, or null if the folder had none. Exposed for tests.</summary>
        public AnalysisData? Analysis => analysis;

        /// <summary>The trace, when the map has one. Exposed for tests.</summary>
        public DspDetail? Detail => detail;

        /// <summary>How many onsets the live picker currently finds. Exposed for tests.</summary>
        public int LiveOnsetCount { get; private set; }

        /// <summary>
        /// Sets both knobs and re-runs the picker immediately, exactly as
        /// turning them by hand would.
        ///
        /// Exposed for tests, and used by the preset buttons — a test that
        /// poked the knobs' bindables directly would be testing a path the
        /// screen itself does not take.
        /// </summary>
        public void SetSensitivity(double margin, double delta)
        {
            if (detail == null)
                return;

            marginKnob.Current.Value = (float)margin;
            deltaKnob.Current.Value = (float)delta;

            // Straight through rather than waiting for the coalesced update,
            // so a caller can read the count back on the next line.
            repickQueued = false;
            repick();
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            analysis = AnalysisData.LoadFromFolder(beatmapFolder);
            detail = DspDetail.LoadFromFolder(beatmapFolder);

            InternalChildren = new Drawable[]
            {
                sounds,
                new Box { RelativeSizeAxes = Axes.Both, Colour = RetroPalette.Void },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Horizontal = 34, Vertical = 26 },
                    Child = new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        RowDimensions = new[]
                        {
                            new Dimension(GridSizeMode.Absolute, 40),
                            new Dimension(GridSizeMode.Relative, 0.30f),
                            new Dimension(),
                            // Collapsed without a trace: the detector and the
                            // funnel have nothing to draw, and a reserved
                            // empty strip reads as something failing to load.
                            new Dimension(GridSizeMode.Absolute, detail == null ? 0 : 186),
                        },
                        Content = new[]
                        {
                            new Drawable[] { header() },
                            new Drawable[] { stripPanel() },
                            new Drawable[] { tracePanel() },
                            new Drawable[] { bottomPanels() },
                        },
                    },
                },
            };

            startPreviewAudio();
            repick();
        }

        private Drawable header() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new RetroText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Font = RetroFontFamily.Display,
                    TextSize = 14,
                    Colour = RetroPalette.Text,
                    Text = "ANALYSER",
                },
                new RetroText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 150,
                    Font = RetroFontFamily.Body,
                    TextSize = 11,
                    Colour = RetroPalette.TextDim,
                    Text = analysis == null
                        ? Path.GetFileName(beatmapFolder)
                        : $"{analysis.Track.Name} · {analysis.Track.Duration:0}s"
                          + $" · {analysis.BeatGrid.Bpm:0.0} BPM",
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(16, 0),
                    Children = new Drawable[]
                    {
                        new RetroText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Font = RetroFontFamily.Body,
                            TextSize = 10,
                            Colour = RetroPalette.TextDim.Opacity(0.8f),
                            Text = "DRAG THE PIN OR CLICK THE STRIP TO SEEK  ·  SPACE — PLAY / PAUSE  ·  ESC — BACK",
                        },
                        playPauseChip = new HeaderChip(FontAwesome.Solid.Pause, "PAUSE")
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Action = TogglePlayPause,
                        },
                        restartButton(),
                    },
                },
            },
        };

        /// <summary>
        /// Back to the top of the song, playing. In the header rather than on
        /// the strip, where a press would also land as a seek.
        /// </summary>
        private Drawable restartButton() => new HeaderChip(FontAwesome.Solid.UndoAlt, "RESTART")
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Action = Restart,
        };

        /// <summary>
        /// A small cyan transport key in the header: an icon and a word.
        /// Both can change, which is how PLAY and PAUSE are one key.
        /// </summary>
        private partial class HeaderChip : ClickableContainer
        {
            private readonly Box background;
            private readonly SpriteIcon icon;
            private readonly RetroText label;

            public HeaderChip(IconUsage iconUsage, string text)
            {
                Size = new Vector2(104, 26);
                Masking = true;
                CornerRadius = 4;
                BorderThickness = 1.5f;
                BorderColour = RetroPalette.Cyan.Opacity(0.6f);

                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = RetroPalette.PanelInset,
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(7, 0),
                        Children = new Drawable[]
                        {
                            icon = new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(11),
                                Icon = iconUsage,
                                Colour = RetroPalette.Cyan,
                            },
                            label = new RetroText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = RetroFontFamily.Body,
                                TextSize = 10,
                                Colour = RetroPalette.Cyan,
                                Text = text,
                            },
                        },
                    },
                };
            }

            /// <summary>Changes what the key shows, without rebuilding it.</summary>
            public void Show(IconUsage iconUsage, string text)
            {
                if (label.Text == text)
                    return;

                icon.Icon = iconUsage;
                label.Text = text;
            }

            /// <summary>What the key says. Exposed for tests.</summary>
            public string Label => label.Text;

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(RetroPalette.Cyan.Opacity(0.18f), 120, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(RetroPalette.PanelInset, 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }

        /// <summary>Plays the song from the beginning, whether it was still going, paused, or finished.</summary>
        public void Restart()
        {
            if (previewTrack == null)
                return;

            sounds.PlayRewind();

            userPaused = false;
            previewTrack.Seek(0);
            previewTrack.Start();
        }

        /// <summary>
        /// Pauses a playing song, or plays a paused or finished one — from
        /// the top, for a song that ran to its end.
        /// </summary>
        public void TogglePlayPause()
        {
            if (previewTrack == null)
                return;

            sounds.PlayKey();

            if (previewTrack.IsRunning)
            {
                userPaused = true;
                previewTrack.Stop();
                return;
            }

            userPaused = false;

            if (previewTrack.HasCompleted)
                previewTrack.Seek(0);

            previewTrack.Start();
        }

        /// <summary>What the play/pause key says. Exposed for tests.</summary>
        public string PlayPauseLabel => playPauseChip.Label;

        /// <summary>The playhead. Exposed for tests.</summary>
        public Playhead PlayheadPin => playhead;

        /// <summary>Whether the song is playing. Exposed for tests.</summary>
        public bool IsPlaying => previewTrack?.IsRunning == true;

        /// <summary>The spectrogram, with the recorded onsets and a playhead over it.</summary>
        private Drawable stripPanel()
        {
            var panel = new RackPanel
            {
                RelativeSizeAxes = Axes.Both,
                Margin = new MarginPadding { Bottom = 10 },
                Legend = "MEL SPECTROGRAM — DRAG TO SEEK",
            };

            stripArea = new Container { RelativeSizeAxes = Axes.Both };
            panel.Add(stripArea);

            if (analysis == null)
            {
                panel.Add(new RetroText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = RetroFontFamily.Body,
                    TextSize = 12,
                    Colour = RetroPalette.TextDim,
                    Text = "no analysis in this folder",
                });
            }

            if (analysis != null)
            {
                spectrogram = new SpectrogramReveal(AnalysisData.FindSpectrogram(beatmapFolder),
                                                    analysis.Track.Duration)
                {
                    RelativeSizeAxes = Axes.Both,
                };

                // Nothing is being revealed here — the whole track is on
                // screen from the moment it opens.
                spectrogram.RevealImmediately();

                stripArea.Add(spectrogram);
            }

            stripArea.Add(playhead = new Playhead());

            return panel;
        }

        /// <summary>The stacked traces, and the switches that turn them on and off.</summary>
        private Drawable tracePanel()
        {
            var panel = new RackPanel
            {
                RelativeSizeAxes = Axes.Both,
                Margin = new MarginPadding { Bottom = 10 },
                Legend = "SIGNALS",
            };

            if (detail == null)
            {
                panel.Add(new RetroText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = RetroFontFamily.Body,
                    TextSize = 12,
                    Colour = RetroPalette.TextDim,
                    // Said plainly rather than shown as an empty panel, which
                    // would read as broken.
                    Text = "no trace data for this map — regenerate it to record one",
                });

                return panel;
            }

            double rate = detail.Frames.FrameRate;
            double duration = detail.Frames.Duration;

            var lanes = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = 150 },
            };

            fluxTrace = lane(lanes, "flux", RetroPalette.Cyan, TraceGraph.TraceStyle.Fill, rate);

            thresholdTrace = new TraceGraph
            {
                RelativeSizeAxes = Axes.Both,
                Style = TraceGraph.TraceStyle.Line,
                TraceColour = RetroPalette.Amber,
                Thickness = 2f,
                ValueCurve = 0.4f,
            };

            thresholdTrace.SetCurve(detail.ThresholdFor(1.5, 0.05), rate);
            thresholdTrace.SetWindow(0, duration);
            lanes.Add(thresholdTrace);

            peaks = new PeakMarkers
            {
                MarkerColour = RetroPalette.Magenta,
                FrameCount = detail.Frames.Count,
                FrameRate = rate,
            };

            peaks.SetWindow(0, duration);
            lanes.Add(peaks);

            var envelopeTrace = lane(lanes, "envelope", RetroPalette.Mint,
                                     TraceGraph.TraceStyle.Line, rate);
            var rmsTrace = lane(lanes, "rms", RetroPalette.Violet,
                                TraceGraph.TraceStyle.Line, rate);
            // Built straight into their own group so one switch throws all
            // three. Adding them to `lanes` first and moving them afterwards
            // throws: a drawable may not be added to two containers, and the
            // removal does not take effect in time.
            var bandGroup = new Container { RelativeSizeAxes = Axes.Both };

            lane(bandGroup, "bandLow", RetroPalette.BandLow, TraceGraph.TraceStyle.Line, rate);
            lane(bandGroup, "bandMid", RetroPalette.BandMid, TraceGraph.TraceStyle.Line, rate);
            lane(bandGroup, "bandHigh", RetroPalette.BandHigh, TraceGraph.TraceStyle.Line, rate);

            lanes.Add(bandGroup);

            var switches = new FillFlowContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Margin = new MarginPadding { Left = 4, Top = 4 },
            };

            addSwitch(switches, "FLUX", RetroPalette.Cyan, fluxTrace, true);
            addSwitch(switches, "THRESHOLD", RetroPalette.Amber, thresholdTrace, true);
            addSwitch(switches, "ONSETS", RetroPalette.Magenta, peaks, true);
            addSwitch(switches, "ENVELOPE", RetroPalette.Mint, envelopeTrace, false);
            addSwitch(switches, "RMS", RetroPalette.Violet, rmsTrace, false);
            addSwitch(switches, "BANDS", RetroPalette.BandMid, bandGroup, false);

            panel.Add(lanes);
            panel.Add(switches);

            return panel;
        }

        private TraceGraph lane(Container parent, string curve, Color4 colour,
                                TraceGraph.TraceStyle style, double rate)
        {
            var graph = new TraceGraph
            {
                RelativeSizeAxes = Axes.Both,
                Style = style,
                TraceColour = colour,
                Thickness = style == TraceGraph.TraceStyle.Fill ? 2f : 1.5f,
                ValueCurve = 0.4f,
            };

            graph.SetCurve(detail!.Samples(curve), rate);
            graph.SetWindow(0, detail.Frames.Duration);

            parent.Add(graph);

            return graph;
        }

        private void addSwitch(FillFlowContainer parent, string label, Color4 accent,
                               Drawable target, bool on)
        {
            var toggle = new ToggleSwitch(label) { Accent = accent };

            toggle.Active.Value = on;
            toggle.Flicked += thrown => sounds.PlayFlick(thrown);
            target.Alpha = on ? 1 : 0;

            toggle.Active.BindValueChanged(active =>
                target.FadeTo(active.NewValue ? 1 : 0, 120, Easing.OutQuint));

            traces.Add((toggle, target));
            parent.Add(toggle);
        }

        /// <summary>The detector controls, the tempo readout and the tier funnel.</summary>
        private Drawable bottomPanels()
        {
            if (detail == null)
                return new Container();

            return new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                ColumnDimensions = new[]
                {
                    new Dimension(GridSizeMode.Absolute, 420),
                    new Dimension(),
                },
                Content = new[]
                {
                    new[] { detectorPanel(), funnelPanel() },
                },
            };
        }

        private Drawable detectorPanel()
        {
            marginKnob = new ControlKnob("MARGIN", 1.0f, 3.0f, 1.5f)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            };

            deltaKnob = new ControlKnob("DELTA", 0f, 0.15f, 0.05f)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Format = value => value.ToString("0.000"),
            };

            marginKnob.Current.BindValueChanged(_ => queueRepick());
            deltaKnob.Current.BindValueChanged(_ => queueRepick());

            // A click per detent, climbing as the knob turns up — only for a
            // hand on the knob, not for a preset moving it.
            marginKnob.Detented += position => sounds.PlayDetent(position);
            deltaKnob.Detented += position => sounds.PlayDetent(position);

            var presets = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(4, 0),
            };

            foreach (var (name, sensitivity) in detail!.Detection.Tiers)
                presets.Add(presetButton(name, sensitivity));

            return new RackPanel
            {
                RelativeSizeAxes = Axes.Both,
                Margin = new MarginPadding { Right = 10 },
                Legend = "DETECTOR — TURN A KNOB TO RE-RUN THE PICKER",
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(18, 0),
                        Margin = new MarginPadding { Left = 10 },
                        Children = new Drawable[] { marginKnob, deltaKnob },
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 7),
                        Margin = new MarginPadding { Right = 12 },
                        Children = new Drawable[]
                        {
                            onsetCount = new SegmentReadout(17)
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                Digits = 4,
                                DisplayColour = RetroPalette.Magenta,
                                Glowing = true,
                                Text = "0",
                            },
                            new RetroText
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                Font = RetroFontFamily.Body,
                                TextSize = 9,
                                Colour = RetroPalette.TextDim,
                                Text = "ONSETS FOUND",
                            },
                            comparison = new RetroText
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                Font = RetroFontFamily.Body,
                                TextSize = 9,
                                Colour = RetroPalette.TextDim,
                                Text = string.Empty,
                            },
                        },
                    },
                    new Container
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        AutoSizeAxes = Axes.Both,
                        Margin = new MarginPadding { Left = 10, Bottom = 4 },
                        Child = presets,
                    },
                },
            };
        }

        /// <summary>
        /// Snaps both knobs to a tier's own sensitivity.
        ///
        /// The moment the whole feature is for: pick a difficulty and the
        /// count lands on the number the backend recorded for it.
        /// </summary>
        private Drawable presetButton(string tier, DspSensitivity sensitivity)
        {
            var button = new ClickableContainer
            {
                Size = new Vector2(66, 22),
                Masking = true,
                CornerRadius = 3,
                BorderThickness = 1,
                BorderColour = RetroPalette.ChromeDark,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = RetroPalette.PanelInset,
                    },
                    new RetroText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = RetroFontFamily.Body,
                        TextSize = 9,
                        Colour = RetroPalette.TextDim,
                        Text = tier.ToUpperInvariant(),
                    },
                },
            };

            button.Action = () =>
            {
                sounds.PlayKey();
                SetSensitivity(sensitivity.Margin, sensitivity.Delta);
            };

            return button;
        }

        private Drawable funnelPanel()
        {
            if (detail?.Tiers is not { Count: > 0 })
                return new Container();

            return new TierFunnelStage(detail).With(stage => stage.RevealImmediately());
        }

        /// <summary>
        /// Marks the picker as needing to re-run.
        ///
        /// Coalesced to once per frame rather than run on every knob event: a
        /// drag fires many times a frame, and the pass itself is cheap but
        /// rebuilding the threshold path is not.
        /// </summary>
        private void queueRepick() => repickQueued = true;

        private void repick()
        {
            if (detail == null)
                return;

            double margin = marginKnob.Current.Value;
            double delta = deltaKnob.Current.Value;

            var frames = OnsetPeakPicker.Pick(detail, margin, delta);

            LiveOnsetCount = frames.Count;

            peaks.SetFrames(frames);
            thresholdTrace.SetCurve(detail.ThresholdFor(margin, delta), detail.Frames.FrameRate);
            onsetCount.Text = frames.Count.ToString();

            comparison.Text = matchedTier(margin, delta) is { } tier
                ? $"= {tier} AS THE BACKEND RECORDED IT"
                : "CUSTOM SENSITIVITY";
        }

        /// <summary>The tier whose knobs these are, if any — so the readout can say so.</summary>
        private string? matchedTier(double margin, double delta)
        {
            foreach (var (name, sensitivity) in detail!.Detection.Tiers)
            {
                if (Math.Abs(sensitivity.Margin - margin) < 1e-3
                    && Math.Abs(sensitivity.Delta - delta) < 1e-4)
                {
                    return name.ToUpperInvariant();
                }
            }

            return null;
        }

        protected override void Update()
        {
            base.Update();

            if (repickQueued)
            {
                repickQueued = false;
                repick();
            }

            if (previewTrack != null && analysis != null && analysis.Track.Duration > 0)
            {
                playhead.X = (float)(previewTrack.CurrentTime / 1000 / analysis.Track.Duration);
                playhead.SetTime(previewTrack.CurrentTime / 1000);
            }

            if (IsPlaying)
                playPauseChip.Show(FontAwesome.Solid.Pause, "PAUSE");
            else
                playPauseChip.Show(FontAwesome.Solid.Play, "PLAY");
        }

        // ------------------------------------------------------------------
        // Seeking
        // ------------------------------------------------------------------

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            return seekFrom(e.ScreenSpaceMouseDownPosition) || base.OnMouseDown(e);
        }

        protected override void OnDrag(DragEvent e)
        {
            if (!seekFrom(e.ScreenSpaceMousePosition))
                return;

            // A grain of tape per few pixels of travel, pitched by how fast
            // the pin is moving — slow drags grind, fast ones whizz.
            float x = e.ScreenSpaceMousePosition.X;
            double now = Time.Current;

            float moved = Math.Abs(x - lastScrubX);

            if (moved >= 3)
            {
                double pixelsPerSecond = moved / Math.Max(1, now - lastScrubTime) * 1000;

                sounds.PlayScrub(pixelsPerSecond / 3000);

                lastScrubX = x;
                lastScrubTime = now;
            }
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (!stripArea.ScreenSpaceDrawQuad.Contains(e.ScreenSpaceMouseDownPosition))
                return false;

            scrubbing = true;
            playhead.Grabbed = true;
            sounds.PlayPinGrab();

            lastScrubX = e.ScreenSpaceMousePosition.X;
            lastScrubTime = Time.Current;

            return true;
        }

        protected override void OnDragEnd(DragEndEvent e)
        {
            if (scrubbing)
            {
                scrubbing = false;
                playhead.Grabbed = false;
                sounds.PlayPinDrop();
            }

            base.OnDragEnd(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            // A click that did not become a drag: the playhead cued straight
            // to a spot. The seek itself already happened on mouse down.
            if (stripArea.ScreenSpaceDrawQuad.Contains(e.ScreenSpaceMouseDownPosition) && previewTrack != null)
            {
                sounds.PlayCue();
                return true;
            }

            return base.OnClick(e);
        }

        private bool seekFrom(Vector2 screenSpace)
        {
            if (previewTrack == null || analysis == null)
                return false;

            if (!stripArea.ScreenSpaceDrawQuad.Contains(screenSpace))
                return false;

            SeekToFraction(stripArea.ToLocalSpace(screenSpace).X / Math.Max(1, stripArea.DrawWidth));
            return true;
        }

        /// <summary>
        /// Plays from this far through the song, 0 to 1 — what a click or drag
        /// on the strip does. Public so tests drive the same path.
        /// </summary>
        public void SeekToFraction(double fraction)
        {
            if (previewTrack == null || analysis == null)
                return;

            previewTrack.Seek(Math.Clamp(fraction, 0, 1) * analysis.Track.Duration * 1000);

            // A track stops itself at the end, and a seek alone does not
            // restart it — so scrubbing a finished song moved the playhead
            // in silence. Seeking means "play from here" — unless the player
            // paused it, in which case they are looking for a spot.
            if (!previewTrack.IsRunning && !userPaused)
                previewTrack.Start();
        }

        /// <summary>Where playback is, in milliseconds. Exposed for tests.</summary>
        public double PlaybackTime => previewTrack?.CurrentTime ?? 0;

        /// <summary>
        /// Plays the map's own audio, so the strip can be scrubbed against
        /// what it is a picture of. Loaded by the route the generation screen
        /// already uses.
        /// </summary>
        private void startPreviewAudio()
        {
            if (!Directory.Exists(beatmapFolder))
                return;

            try
            {
                string? audioFile = Directory.EnumerateFiles(beatmapFolder)
                                             .FirstOrDefault(BackendRunner.IsSupportedAudioFile);

                if (audioFile == null)
                    return;

                var storage = host.GetStorage(beatmapFolder);
                var store = new StorageBackedResourceStore(storage);

                previewTrack = audio.GetTrackStore(store).Get(Path.GetFileName(audioFile));

                if (previewTrack == null)
                    return;

                previewTrack.Volume.Value = preview_volume;
                previewTrack.Start();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The panels are all readable without it; this costs the
                // screen its soundtrack, not its meaning.
            }
        }

        private void stopPreviewAudio()
        {
            previewTrack?.Stop();
            previewTrack = null;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == osuTK.Input.Key.Escape)
            {
                this.Exit();
                return true;
            }

            if (e.Key == osuTK.Input.Key.Space && !e.Repeat)
            {
                TogglePlayPause();
                return true;
            }

            return base.OnKeyDown(e);
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            this.FadeInFromZero(250, Easing.OutQuint);
        }

        // Audio lifecycle, copied from MenuTrack's contract: two songs at once
        // is the likeliest regression when a third screen with a preview track
        // joins a stack that already has two.
        public override void OnSuspending(ScreenTransitionEvent e)
        {
            stopPreviewAudio();
            base.OnSuspending(e);
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);
            startPreviewAudio();
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            stopPreviewAudio();
            this.FadeOut(200, Easing.OutQuint);

            return base.OnExiting(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            stopPreviewAudio();
            base.Dispose(isDisposing);
        }
    }
}
